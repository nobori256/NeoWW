using MapLibre.Unity;
using MapLibre.Unity.Plateau;
using MapLibre.Unity.Terrain;
using UnityEngine;
using UnityEngine.UI;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demo bootstrap for the "3D Terrain + PLATEAU" sample: sets up a directional light, an orbit-controllable
    /// main camera, a <see cref="GsiTerrainLayer"/> plus <see cref="PlateauTilesetLayer"/> centered on Tokyo
    /// Station, a small 2D MapLibre mini-map in the bottom-left corner, and a data-attribution label in the
    /// bottom-right corner. Attach this to an empty GameObject in an otherwise-empty scene.
    /// </summary>
    public class PlateauTerrain3DBootstrap : MonoBehaviour
    {
        private const double OriginLatitude = 35.681236; // Tokyo Station
        private const double OriginLongitude = 139.767125;

        private const int MiniMapWidth = 320;
        private const int MiniMapHeight = 240;

        private const float InitialOrbitDistance = 536f;

        private Transform _orbitPivot;
        private Camera _orbitCamera;

        // Yaw/pitch/distance chosen so the initial camera position works out to approximately (-250, 220, -420)
        // relative to the orbit pivot (origin + Y50), looking back at the pivot.
        private float _orbitYawDegrees = -149.2f;
        private float _orbitPitchDegrees = 24.2f;
        private float _orbitDistance = InitialOrbitDistance;
        private Vector3 _lastMousePosition;
        private bool _dragging;

        public GsiTerrainLayer TerrainLayer { get; private set; }

        public PlateauTilesetLayer BuildingsLayer { get; private set; }

        private void Awake()
        {
            SetupLighting();
            SetupOrbitCamera();
            SetupTerrainAndBuildings();
            SetupMiniMap();
            SetupAttributionLabel();
        }

        private void SetupLighting()
        {
            var lightGo = new GameObject("Directional Light");
            lightGo.transform.SetParent(transform, worldPositionStays: false);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
        }

        private void SetupOrbitCamera()
        {
            var pivotGo = new GameObject("OrbitPivot");
            pivotGo.transform.SetParent(transform, worldPositionStays: false);
            pivotGo.transform.position = new Vector3(0f, 50f, 0f);
            _orbitPivot = pivotGo.transform;

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(transform, worldPositionStays: false);
            cameraGo.tag = "MainCamera";

            _orbitCamera = cameraGo.AddComponent<Camera>();
            _orbitCamera.nearClipPlane = 1f;
            _orbitCamera.farClipPlane = 10000f;

            cameraGo.AddComponent<AudioListener>();

            UpdateOrbitCameraTransform();
        }

        private void SetupTerrainAndBuildings()
        {
            var terrainGo = new GameObject("GsiTerrainLayer");
            terrainGo.transform.SetParent(transform, worldPositionStays: false);
            TerrainLayer = terrainGo.AddComponent<GsiTerrainLayer>();
            TerrainLayer.OriginLatitude = OriginLatitude;
            TerrainLayer.OriginLongitude = OriginLongitude;

            var buildingsGo = new GameObject("PlateauTilesetLayer");
            buildingsGo.transform.SetParent(transform, worldPositionStays: false);
            BuildingsLayer = buildingsGo.AddComponent<PlateauTilesetLayer>();
            BuildingsLayer.OriginLatitude = OriginLatitude;
            BuildingsLayer.OriginLongitude = OriginLongitude;
        }

        private void SetupMiniMap()
        {
            try
            {
                var canvasGo = new GameObject("MiniMapCanvas");
                canvasGo.transform.SetParent(transform, worldPositionStays: false);

                Canvas canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGo.AddComponent<CanvasScaler>();
                canvasGo.AddComponent<GraphicRaycaster>();

                var imageGo = new GameObject("MiniMapImage");
                imageGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);

                RawImage rawImage = imageGo.AddComponent<RawImage>();
                RectTransform rect = rawImage.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.sizeDelta = new Vector2(MiniMapWidth, MiniMapHeight);
                rect.anchoredPosition = Vector2.zero;

                var mapView = imageGo.AddComponent<MapLibreMapView>();
                imageGo.AddComponent<MiniMapTextureBinder>().Bind(mapView, rawImage);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"PlateauTerrain3DBootstrap: mini-map initialization failed, continuing without it: {e}");
            }
        }

        private void SetupAttributionLabel()
        {
            var canvasGo = new GameObject("AttributionCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var textGo = new GameObject("AttributionText");
            textGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);

            RectTransform rect = textGo.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(420, 60);
            rect.anchoredPosition = new Vector2(-8, -8);

            Text text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.LowerRight;
            text.color = Color.white;
            text.text = "地図・標高タイル: 国土地理院 / 建物: Project PLATEAU (国土交通省)";
        }

        private void Update()
        {
            HandleOrbitInput();
        }

        private void HandleOrbitInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _dragging = true;
                _lastMousePosition = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                _dragging = false;
            }

            if (_dragging && Input.GetMouseButton(0))
            {
                Vector3 delta = Input.mousePosition - _lastMousePosition;
                _lastMousePosition = Input.mousePosition;

                _orbitYawDegrees += delta.x * 0.3f;
                _orbitPitchDegrees = Mathf.Clamp(_orbitPitchDegrees - delta.y * 0.3f, 5f, 85f);
                UpdateOrbitCameraTransform();
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0f)
            {
                _orbitDistance = Mathf.Clamp(_orbitDistance - scroll * 200f, 50f, 3000f);
                UpdateOrbitCameraTransform();
            }
        }

        private void UpdateOrbitCameraTransform()
        {
            if (_orbitCamera == null || _orbitPivot == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(_orbitPitchDegrees, _orbitYawDegrees, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_orbitDistance);
            _orbitCamera.transform.position = _orbitPivot.position + offset;
            _orbitCamera.transform.LookAt(_orbitPivot.position);
        }

        /// <summary>
        /// Keeps a RawImage's texture in sync with a MapLibreMapView's output texture, which is only created once
        /// the underlying native map has produced its first frame.
        /// </summary>
        private class MiniMapTextureBinder : MonoBehaviour
        {
            private MapLibreMapView _mapView;
            private RawImage _rawImage;

            public void Bind(MapLibreMapView mapView, RawImage rawImage)
            {
                _mapView = mapView;
                _rawImage = rawImage;
            }

            private void Update()
            {
                if (_mapView == null || _rawImage == null)
                {
                    return;
                }

                if (_rawImage.texture != _mapView.Texture && _mapView.Texture != null)
                {
                    _rawImage.texture = _mapView.Texture;
                }
            }
        }

    }
}
