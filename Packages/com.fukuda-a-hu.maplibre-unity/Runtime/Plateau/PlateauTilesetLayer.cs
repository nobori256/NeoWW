using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using MapLibre.Unity.Geo;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Plateau
{
    /// <summary>
    /// Streams PLATEAU (Project PLATEAU, MLIT Japan) 3D city model buildings, published as 3D Tiles 1.0 tilesets of
    /// b3dm tiles, around a geodetic origin. Walks the tileset tree to find leaf tiles (tiles with content and no
    /// children) within a radius of the origin, downloads and sanitizes their b3dm/glb payloads (see
    /// <see cref="B3dm"/>), and instantiates each one under a tile-root GameObject positioned/oriented from the
    /// tile's CESIUM_RTC center.
    /// </summary>
    public class PlateauTilesetLayer : MonoBehaviour
    {
        [SerializeField]
        private string tilesetUrl = "https://assets.cms.plateau.reearth.io/assets/0e/e5948a-e95c-4e31-be85-1f8c066ed996/13101_chiyoda-ku_pref_2023_citygml_1_op_bldg_3dtiles_13101_chiyoda-ku_lod1/tileset.json";

        [SerializeField]
        private double originLatitude = 35.681236; // Tokyo Station

        [SerializeField]
        private double originLongitude = 139.767125;

        [SerializeField]
        private double loadRadiusMeters = 1200;

        [SerializeField]
        private int maxTiles = 6;

        [Tooltip("If set, overrides the material glTFast would otherwise generate for each tile's meshes.")]
        [SerializeField]
        private Material overrideMaterial;

        private const double EarthMeanRadiusMeters = 6371000.0;

        private readonly List<GltfImport> _liveImports = new List<GltfImport>();
        private readonly List<Renderer> _instantiatedRenderers = new List<Renderer>();
        private readonly List<MeshFilter> _instantiatedMeshFilters = new List<MeshFilter>();

        private int _loadedTileCount;
        private bool _isReady;

        /// <summary>Geodetic origin latitude (degrees) that loaded tiles are positioned relative to. Must be set before <see cref="Start"/> runs.</summary>
        public double OriginLatitude
        {
            get => originLatitude;
            set => originLatitude = value;
        }

        /// <summary>Geodetic origin longitude (degrees) that loaded tiles are positioned relative to. Must be set before <see cref="Start"/> runs.</summary>
        public double OriginLongitude
        {
            get => originLongitude;
            set => originLongitude = value;
        }

        /// <summary>Number of leaf tiles that finished loading (successfully instantiated).</summary>
        public int LoadedTileCount => _loadedTileCount;

        /// <summary>True once tile selection and all download/instantiate attempts have completed (success or failure).</summary>
        public bool IsReady => _isReady;

        /// <summary>
        /// Combined world-space (axis-aligned) bounds of every Renderer instantiated so far. Verification helper.
        /// Note: since each PLATEAU tile typically contains many buildings spread across a wide area (up to ~1km),
        /// and the tile root is rotated to align its glTF Y-up axis with true "up" at its geodetic location, this
        /// AABB's Y size reflects the tile's rotated horizontal footprint as much as actual building height - see
        /// <see cref="MaxLocalBuildingHeightMeters"/> for a rotation-aware height estimate.
        /// </summary>
        public Bounds CombinedBounds
        {
            get
            {
                if (_instantiatedRenderers.Count == 0)
                {
                    return new Bounds(Vector3.zero, Vector3.zero);
                }

                Bounds combined = _instantiatedRenderers[0].bounds;
                for (int i = 1; i < _instantiatedRenderers.Count; i++)
                {
                    combined.Encapsulate(_instantiatedRenderers[i].bounds);
                }

                return combined;
            }
        }

        /// <summary>
        /// Estimates the tallest single building's height (in meters) among all loaded tiles, correcting for the
        /// fact that a tile's combined AABB conflates horizontal footprint with vertical extent (see
        /// <see cref="CombinedBounds"/>). Each tile's mesh vertices are transformed to world space, bucketed into a
        /// horizontal grid (in the world XZ plane), and the largest per-bucket world-Y range is taken as the
        /// tallest building's approximate height - since a single ~20m grid cell realistically contains at most one
        /// building footprint, this isolates vertical extent from the tile's horizontal spread. Verification helper.
        /// </summary>
        public float MaxLocalBuildingHeightMeters
        {
            get
            {
                const float gridCellSizeMeters = 20f;
                var maxHeightPerCell = new Dictionary<(long, long), (float min, float max)>();

                foreach (MeshFilter meshFilter in _instantiatedMeshFilters)
                {
                    if (meshFilter == null || meshFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
                    Vector3[] vertices = meshFilter.sharedMesh.vertices;
                    foreach (Vector3 localVertex in vertices)
                    {
                        Vector3 worldVertex = localToWorld.MultiplyPoint3x4(localVertex);
                        var cell = (
                            (long)Mathf.Floor(worldVertex.x / gridCellSizeMeters),
                            (long)Mathf.Floor(worldVertex.z / gridCellSizeMeters));

                        if (maxHeightPerCell.TryGetValue(cell, out (float min, float max) range))
                        {
                            maxHeightPerCell[cell] = (Mathf.Min(range.min, worldVertex.y), Mathf.Max(range.max, worldVertex.y));
                        }
                        else
                        {
                            maxHeightPerCell[cell] = (worldVertex.y, worldVertex.y);
                        }
                    }
                }

                float maxHeight = 0f;
                foreach ((float min, float max) range in maxHeightPerCell.Values)
                {
                    maxHeight = Mathf.Max(maxHeight, range.max - range.min);
                }

                return maxHeight;
            }
        }

        private async void Start()
        {
            try
            {
                await LoadTilesetAsync();
            }
            finally
            {
                if (this != null)
                {
                    _isReady = true;
                }
            }
        }

        private async Task LoadTilesetAsync()
        {
            byte[] tilesetBytes = await DownloadBytesAsync(tilesetUrl);
            if (this == null)
            {
                return;
            }

            if (tilesetBytes == null)
            {
                Debug.LogWarning("PlateauTilesetLayer: failed to download tileset.json - no buildings will be loaded.");
                return;
            }

            JObject tilesetJson;
            try
            {
                tilesetJson = JObject.Parse(System.Text.Encoding.UTF8.GetString(tilesetBytes));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"PlateauTilesetLayer: failed to parse tileset.json: {e.Message}");
                return;
            }

            string baseUrl = GetDirectoryUrl(tilesetUrl);

            var leaves = new List<LeafTile>();
            if (tilesetJson["root"] is JObject root)
            {
                CollectLeafTiles(root, leaves);
            }

            var basis = GeoMath.EnuBasis(originLatitude, originLongitude);
            GeoMath.EcefCoordinate originEcef = GeoMath.Wgs84ToEcef(originLatitude, originLongitude, 0.0);

            var candidates = new List<(LeafTile tile, double distance)>();
            foreach (LeafTile leaf in leaves)
            {
                if (!TryGetRegionCenter(leaf.BoundingVolume, out double centerLatDeg, out double centerLonDeg))
                {
                    continue;
                }

                double distance = HorizontalDistanceMeters(originLatitude, originLongitude, centerLatDeg, centerLonDeg);
                if (distance <= loadRadiusMeters)
                {
                    candidates.Add((leaf, distance));
                }
            }

            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            int tileCount = Mathf.Min(maxTiles, candidates.Count);
            for (int i = 0; i < tileCount; i++)
            {
                await LoadLeafTileAsync(candidates[i].tile, baseUrl, originEcef, basis);
                if (this == null)
                {
                    return;
                }
            }
        }

        private readonly struct LeafTile
        {
            public readonly string ContentUri;
            public readonly JArray BoundingVolume;

            public LeafTile(string contentUri, JArray boundingVolume)
            {
                ContentUri = contentUri;
                BoundingVolume = boundingVolume;
            }
        }

        /// <summary>
        /// Recursively walks the tileset tree, collecting only "leaf" tiles - tiles that have a content.uri and no
        /// (or empty) children - since 3D Tiles 1.0 REPLACE refinement means only leaf content needs to be loaded
        /// for a complete, non-overlapping representation of the requested area at native detail.
        /// </summary>
        private static void CollectLeafTiles(JObject tile, List<LeafTile> leaves)
        {
            JArray children = tile["children"] as JArray;
            bool hasChildren = children != null && children.Count > 0;

            if (!hasChildren)
            {
                if (tile["content"] is JObject content && content["uri"] != null)
                {
                    JArray boundingVolumeRegion = (content["boundingVolume"] as JObject)?["region"] as JArray
                        ?? (tile["boundingVolume"] as JObject)?["region"] as JArray;

                    leaves.Add(new LeafTile((string)content["uri"], boundingVolumeRegion));
                }

                return;
            }

            foreach (JToken child in children)
            {
                if (child is JObject childObject)
                {
                    CollectLeafTiles(childObject, leaves);
                }
            }
        }

        private static bool TryGetRegionCenter(JArray region, out double centerLatDeg, out double centerLonDeg)
        {
            centerLatDeg = 0;
            centerLonDeg = 0;

            if (region == null || region.Count < 4)
            {
                return false;
            }

            double west = region[0].Value<double>();
            double south = region[1].Value<double>();
            double east = region[2].Value<double>();
            double north = region[3].Value<double>();

            centerLonDeg = (west + east) * 0.5 * (180.0 / Math.PI);
            centerLatDeg = (south + north) * 0.5 * (180.0 / Math.PI);
            return true;
        }

        /// <summary>
        /// Equirectangular approximation of horizontal great-circle distance, adequate for a small (sub-few-km)
        /// selection radius around the origin latitude.
        /// </summary>
        private static double HorizontalDistanceMeters(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
        {
            double lat1 = lat1Deg * Math.PI / 180.0;
            double lat2 = lat2Deg * Math.PI / 180.0;
            double dLat = lat2 - lat1;
            double dLon = (lon2Deg - lon1Deg) * Math.PI / 180.0;

            double meanLat = (lat1 + lat2) * 0.5;
            double x = dLon * Math.Cos(meanLat);
            double y = dLat;

            return Math.Sqrt(x * x + y * y) * EarthMeanRadiusMeters;
        }

        private async Task LoadLeafTileAsync(LeafTile leaf, string baseUrl, GeoMath.EcefCoordinate originEcef, GeoMath.EnuFrame basis)
        {
            string tileUrl = CombineUrl(baseUrl, leaf.ContentUri);

            byte[] b3dmBytes = await DownloadBytesAsync(tileUrl);
            if (this == null)
            {
                return;
            }

            if (b3dmBytes == null)
            {
                Debug.LogWarning($"PlateauTilesetLayer: failed to download tile content at {tileUrl} - skipping.");
                return;
            }

            if (!B3dm.TryParse(b3dmBytes, out byte[] sanitizedGlb, out double[] rtcCenterEcef, out string parseError))
            {
                Debug.LogWarning($"PlateauTilesetLayer: failed to parse b3dm at {tileUrl}: {parseError} - skipping.");
                return;
            }

            var gltfImport = new GltfImport();
            bool loaded;
            try
            {
                loaded = await gltfImport.LoadGltfBinary(sanitizedGlb, new Uri(tileUrl));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"PlateauTilesetLayer: exception loading glb for {tileUrl}: {e.Message} - skipping.");
                gltfImport.Dispose();
                return;
            }

            if (this == null)
            {
                gltfImport.Dispose();
                return;
            }

            if (!loaded)
            {
                Debug.LogWarning($"PlateauTilesetLayer: glTFast failed to load glb for {tileUrl} - skipping.");
                gltfImport.Dispose();
                return;
            }

            var tileRoot = new GameObject($"PlateauTile_{System.IO.Path.GetFileNameWithoutExtension(leaf.ContentUri)}");
            tileRoot.transform.SetParent(transform, worldPositionStays: false);

            if (rtcCenterEcef != null)
            {
                ApplyRtcTransform(tileRoot.transform, rtcCenterEcef, originEcef, basis);
            }

            bool instantiated;
            try
            {
                instantiated = await gltfImport.InstantiateMainSceneAsync(tileRoot.transform);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"PlateauTilesetLayer: exception instantiating scene for {tileUrl}: {e.Message} - skipping.");
                UnityEngine.Object.Destroy(tileRoot);
                gltfImport.Dispose();
                return;
            }

            if (this == null)
            {
                gltfImport.Dispose();
                return;
            }

            if (!instantiated)
            {
                Debug.LogWarning($"PlateauTilesetLayer: glTFast failed to instantiate scene for {tileUrl} - skipping.");
                UnityEngine.Object.Destroy(tileRoot);
                gltfImport.Dispose();
                return;
            }

            if (overrideMaterial != null)
            {
                foreach (Renderer renderer in tileRoot.GetComponentsInChildren<Renderer>())
                {
                    renderer.sharedMaterial = overrideMaterial;
                }
            }

            _instantiatedRenderers.AddRange(tileRoot.GetComponentsInChildren<Renderer>());
            _instantiatedMeshFilters.AddRange(tileRoot.GetComponentsInChildren<MeshFilter>());

            // Keep the GltfImport alive for the lifetime of the instantiated scene - its meshes/textures are owned
            // by glTFast and would be destroyed if the GltfImport were disposed now. Disposed together in OnDestroy.
            _liveImports.Add(gltfImport);
            _loadedTileCount++;
        }

        /// <summary>
        /// Computes the tile-root transform from the glb's CESIUM_RTC center. The math translates the tile's
        /// Unity-local basis vectors into ENU, following the chain: glTFast's glTF (right-handed, Y-up) to Unity
        /// (left-handed) conversion negates X, then 3D Tiles' glb-local (Y-up) to ECEF (Z-up) convention swaps Y
        /// and Z (with a sign flip). Concretely, for a Unity-local direction (ux,uy,uz), the corresponding ECEF
        /// direction is (-ux, -uz, uy); projecting the three Unity basis vectors' ECEF images onto the local ENU
        /// frame yields a 3x3 rotation matrix from Unity-local to ENU, which (via ENU's own East/Up/North axes
        /// mapping to Unity's X/Y/Z) becomes this transform's rotation.
        /// </summary>
        private static void ApplyRtcTransform(Transform tileRoot, double[] rtcCenterEcef, GeoMath.EcefCoordinate originEcef, GeoMath.EnuFrame basis)
        {
            var centerEcef = new GeoMath.EcefCoordinate(rtcCenterEcef[0], rtcCenterEcef[1], rtcCenterEcef[2]);
            tileRoot.position = GeoMath.EcefToUnity(centerEcef, originEcef, basis);

            Vector3 unityXImage = UnityDirectionToEnu(new Vector3(1, 0, 0), basis);
            Vector3 unityYImage = UnityDirectionToEnu(new Vector3(0, 1, 0), basis);
            Vector3 unityZImage = UnityDirectionToEnu(new Vector3(0, 0, 1), basis);

            var rotationMatrix = new Matrix4x4(
                new Vector4(unityXImage.x, unityXImage.y, unityXImage.z, 0),
                new Vector4(unityYImage.x, unityYImage.y, unityYImage.z, 0),
                new Vector4(unityZImage.x, unityZImage.y, unityZImage.z, 0),
                new Vector4(0, 0, 0, 1));

            tileRoot.rotation = rotationMatrix.rotation;
        }

        /// <summary>
        /// Maps a Unity-local direction vector (glTFast/glb tile space) to its ENU-frame components, expressed as
        /// a Unity Vector3 (X=East, Y=Up, Z=North) so it can be dropped straight into a Unity rotation matrix
        /// column. See the two-step conversion described on <see cref="ApplyRtcTransform"/>.
        /// </summary>
        private static Vector3 UnityDirectionToEnu(Vector3 unityDir, GeoMath.EnuFrame basis)
        {
            // Step 1: glTFast's glTF->Unity conversion negates X (right-handed Y-up -> left-handed Y-up).
            // Undo it to get back to the glb's own right-handed, Y-up local axes.
            double gx = -unityDir.x;
            double gy = unityDir.y;
            double gz = unityDir.z;

            // Step 2: 3D Tiles glb-local (Y-up) -> ECEF-relative (Z-up): (gx,gy,gz) -> (gx,-gz,gy).
            var ecefOffset = new GeoMath.EcefCoordinate(gx, -gz, gy);

            return GeoMath.EcefOffsetToUnity(ecefOffset, basis);
        }

        private static string GetDirectoryUrl(string url)
        {
            int lastSlash = url.LastIndexOf('/');
            return lastSlash >= 0 ? url.Substring(0, lastSlash + 1) : url;
        }

        private static string CombineUrl(string baseUrl, string relativeUri)
        {
            if (Uri.TryCreate(relativeUri, UriKind.Absolute, out _))
            {
                return relativeUri;
            }

            return baseUrl + relativeUri;
        }

        private static async Task<byte[]> DownloadBytesAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            var tcs = new TaskCompletionSource<bool>();

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            operation.completed += _ => tcs.TrySetResult(true);

            await tcs.Task;

#if UNITY_2020_2_OR_NEWER
            bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif
            if (failed)
            {
                Debug.LogWarning($"PlateauTilesetLayer: request to {url} failed: {request.error}");
                return null;
            }

            return request.downloadHandler.data;
        }

        private void OnDestroy()
        {
            foreach (GltfImport import in _liveImports)
            {
                import.Dispose();
            }

            _liveImports.Clear();
        }
    }
}
