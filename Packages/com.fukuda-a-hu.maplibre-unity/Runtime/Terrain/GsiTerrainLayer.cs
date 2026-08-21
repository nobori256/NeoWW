using System.Collections.Generic;
using System.Threading.Tasks;
using MapLibre.Unity.Geo;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Terrain
{
    /// <summary>
    /// Streams a small grid of GSI (Geospatial Information Authority of Japan) elevation ("dem_png") and aerial
    /// photo tiles around a geodetic origin, and builds one textured mesh GameObject per tile in an ENU local
    /// coordinate frame (X=East, Y=Up, Z=North) centered on that origin. Intended as a simple, non-LOD 3D ground
    /// plane to pair with <see cref="MapLibre.Unity.Plateau.PlateauTilesetLayer"/> building tiles.
    /// </summary>
    public class GsiTerrainLayer : MonoBehaviour
    {
        [SerializeField]
        private double originLatitude = 35.681236; // Tokyo Station

        [SerializeField]
        private double originLongitude = 139.767125;

        [SerializeField]
        private int zoom = 14;

        [SerializeField]
        private int radiusTiles = 1;

        [Tooltip("Added to GSI elevation (orthometric height) to approximate ellipsoidal height, so terrain lines up with PLATEAU building heights (which are ellipsoidal). Default is an approximate geoid height for the Tokyo area.")]
        [SerializeField]
        private float geoidHeightMeters = 36.7f;

        [Tooltip("Number of quads per tile edge; the generated mesh has (meshSegments+1)^2 vertices.")]
        [SerializeField]
        private int meshSegments = 64;

        [SerializeField]
        private string demUrlTemplate = "https://cyberjapandata.gsi.go.jp/xyz/dem_png/{z}/{x}/{y}.png";

        [SerializeField]
        private string textureUrlTemplate = "https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg";

        private const int DemPixelSize = 256;
        private const double InvalidDemValue = 8388608.0; // 2^23, GSI's documented "no data" sentinel.

        private int _loadedTileCount;
        private float _sampleMinY = float.PositiveInfinity;
        private float _sampleMaxY = float.NegativeInfinity;
        private int _expectedTileCount;

        /// <summary>Geodetic origin latitude (degrees) that the generated terrain's local ENU frame is centered on. Must be set before <see cref="Start"/> runs.</summary>
        public double OriginLatitude
        {
            get => originLatitude;
            set => originLatitude = value;
        }

        /// <summary>Geodetic origin longitude (degrees) that the generated terrain's local ENU frame is centered on. Must be set before <see cref="Start"/> runs.</summary>
        public double OriginLongitude
        {
            get => originLongitude;
            set => originLongitude = value;
        }

        /// <summary>Number of tiles that have finished loading (successfully or otherwise-skipped-after-error).</summary>
        public int LoadedTileCount => _loadedTileCount;

        /// <summary>True once every requested tile has finished loading (or failed and been skipped).</summary>
        public bool IsReady => _expectedTileCount > 0 && _loadedTileCount >= _expectedTileCount;

        /// <summary>Minimum Y (Unity units, meters) among all generated terrain vertices so far. Verification helper.</summary>
        public float SampleMinY => _sampleMinY;

        /// <summary>Maximum Y (Unity units, meters) among all generated terrain vertices so far. Verification helper.</summary>
        public float SampleMaxY => _sampleMaxY;

        private async void Start()
        {
            var basis = GeoMath.EnuBasis(originLatitude, originLongitude);
            GeoMath.EcefCoordinate originEcef = GeoMath.Wgs84ToEcef(originLatitude, originLongitude, 0.0);

            double centerTileX = GeoMath.LonToTileX(originLongitude, zoom);
            double centerTileY = GeoMath.LatToTileY(originLatitude, zoom);
            int centerTileXInt = (int)System.Math.Floor(centerTileX);
            int centerTileYInt = (int)System.Math.Floor(centerTileY);

            var tileCoords = new List<Vector2Int>();
            for (int dy = -radiusTiles; dy <= radiusTiles; dy++)
            {
                for (int dx = -radiusTiles; dx <= radiusTiles; dx++)
                {
                    tileCoords.Add(new Vector2Int(centerTileXInt + dx, centerTileYInt + dy));
                }
            }

            _expectedTileCount = tileCoords.Count;

            foreach (Vector2Int tile in tileCoords)
            {
                await LoadTileAsync(tile.x, tile.y, originEcef, basis);
                if (this == null)
                {
                    return;
                }
            }
        }

        private async Task LoadTileAsync(int tileX, int tileY, GeoMath.EcefCoordinate originEcef, GeoMath.EnuFrame basis)
        {
            byte[] demBytes = await DownloadBytesAsync(BuildUrl(demUrlTemplate, tileX, tileY));
            if (this == null)
            {
                return;
            }

            if (demBytes == null)
            {
                Debug.LogWarning($"GsiTerrainLayer: skipping tile ({tileX},{tileY}) - failed to download elevation data.");
                _loadedTileCount++;
                return;
            }

            byte[] photoBytes = await DownloadBytesAsync(BuildUrl(textureUrlTemplate, tileX, tileY));
            if (this == null)
            {
                return;
            }

            float[,] elevations;
            try
            {
                elevations = DecodeElevations(demBytes);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"GsiTerrainLayer: skipping tile ({tileX},{tileY}) - failed to decode elevation PNG: {e.Message}");
                _loadedTileCount++;
                return;
            }

            BuildTileMesh(tileX, tileY, elevations, photoBytes, originEcef, basis);
            _loadedTileCount++;
        }

        private string BuildUrl(string template, int x, int y)
        {
            return template
                .Replace("{z}", zoom.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{x}", x.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{y}", y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private void BuildTileMesh(int tileX, int tileY, float[,] elevations, byte[] photoBytes, GeoMath.EcefCoordinate originEcef, GeoMath.EnuFrame basis)
        {
            int verticesPerEdge = meshSegments + 1;
            var vertices = new Vector3[verticesPerEdge * verticesPerEdge];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[meshSegments * meshSegments * 6];

            for (int row = 0; row < verticesPerEdge; row++)
            {
                double v = (double)row / meshSegments;
                double tileYCoord = tileY + v;
                double lat = GeoMath.TileToLat(tileYCoord, zoom);

                for (int col = 0; col < verticesPerEdge; col++)
                {
                    double u = (double)col / meshSegments;
                    double tileXCoord = tileX + u;
                    double lon = GeoMath.TileToLon(tileXCoord, zoom);

                    float elevation = SampleElevation(elevations, u, v);
                    double heightMeters = elevation + geoidHeightMeters;

                    GeoMath.EcefCoordinate ecef = GeoMath.Wgs84ToEcef(lat, lon, heightMeters);
                    Vector3 unityPos = GeoMath.EcefToUnity(ecef, originEcef, basis);

                    int index = row * verticesPerEdge + col;
                    vertices[index] = unityPos;
                    uvs[index] = new Vector2((float)u, 1f - (float)v);

                    if (unityPos.y < _sampleMinY)
                    {
                        _sampleMinY = unityPos.y;
                    }

                    if (unityPos.y > _sampleMaxY)
                    {
                        _sampleMaxY = unityPos.y;
                    }
                }
            }

            int triIndex = 0;
            for (int row = 0; row < meshSegments; row++)
            {
                for (int col = 0; col < meshSegments; col++)
                {
                    int topLeft = row * verticesPerEdge + col;
                    int topRight = topLeft + 1;
                    int bottomLeft = (row + 1) * verticesPerEdge + col;
                    int bottomRight = bottomLeft + 1;

                    triangles[triIndex++] = topLeft;
                    triangles[triIndex++] = bottomLeft;
                    triangles[triIndex++] = topRight;

                    triangles[triIndex++] = topRight;
                    triangles[triIndex++] = bottomLeft;
                    triangles[triIndex++] = bottomRight;
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = vertices.Length > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var tileGo = new GameObject($"GsiTile_{tileX}_{tileY}");
            tileGo.transform.SetParent(transform, worldPositionStays: false);

            var meshFilter = tileGo.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = tileGo.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = BuildTerrainMaterial(photoBytes);
        }

        private static Material BuildTerrainMaterial(byte[] photoBytes)
        {
            var material = new Material(Shader.Find("Standard"));
            material.SetFloat("_Glossiness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);

            if (photoBytes != null)
            {
                var texture = new Texture2D(DemPixelSize, DemPixelSize, TextureFormat.RGB24, mipChain: true, linear: false);
                if (texture.LoadImage(photoBytes))
                {
                    material.mainTexture = texture;
                }
                else
                {
                    Object.Destroy(texture);
                }
            }

            return material;
        }

        private static float SampleElevation(float[,] elevations, double u, double v)
        {
            // elevations is a (DemPixelSize x DemPixelSize) grid; sample with bilinear interpolation so the mesh
            // (meshSegments+1 vertices per edge) isn't limited to the DEM's native 256x256 resolution.
            double px = u * (DemPixelSize - 1);
            double py = v * (DemPixelSize - 1);

            int x0 = Mathf.Clamp((int)px, 0, DemPixelSize - 1);
            int y0 = Mathf.Clamp((int)py, 0, DemPixelSize - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, DemPixelSize - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, DemPixelSize - 1);

            double fx = px - x0;
            double fy = py - y0;

            float v00 = elevations[y0, x0];
            float v10 = elevations[y0, x1];
            float v01 = elevations[y1, x0];
            float v11 = elevations[y1, x1];

            float top = Mathf.Lerp(v00, v10, (float)fx);
            float bottom = Mathf.Lerp(v01, v11, (float)fx);
            return Mathf.Lerp(top, bottom, (float)fy);
        }

        private static float[,] DecodeElevations(byte[] pngBytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
            try
            {
                if (!texture.LoadImage(pngBytes))
                {
                    throw new System.Exception("Texture2D.LoadImage failed to decode PNG bytes.");
                }

                Color32[] pixels = texture.GetPixels32();
                int width = texture.width;
                int height = texture.height;
                var elevations = new float[height, width];

                // GSI dem_png tiles are top-down (row 0 = northernmost), but Texture2D.GetPixels32 returns
                // bottom-up (row 0 = southernmost) - flip vertically while decoding.
                for (int y = 0; y < height; y++)
                {
                    int srcRow = height - 1 - y;
                    for (int x = 0; x < width; x++)
                    {
                        Color32 c = pixels[srcRow * width + x];
                        elevations[y, x] = DecodeDemPixel(c.r, c.g, c.b);
                    }
                }

                return elevations;
            }
            finally
            {
                Object.Destroy(texture);
            }
        }

        private static float DecodeDemPixel(byte r, byte g, byte b)
        {
            if (r == 128 && g == 0 && b == 0)
            {
                // Documented GSI "no data" sentinel.
                return 0f;
            }

            double x = (r << 16) | (g << 8) | b;

            if (x == InvalidDemValue)
            {
                return 0f;
            }

            double h = x < InvalidDemValue ? x * 0.01 : (x - 16777216.0) * 0.01; // 16777216 = 2^24

            return (float)h;
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
                Debug.LogWarning($"GsiTerrainLayer: request to {url} failed: {request.error}");
                return null;
            }

            return request.downloadHandler.data;
        }
    }
}
