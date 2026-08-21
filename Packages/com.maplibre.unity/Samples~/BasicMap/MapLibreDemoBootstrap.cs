using MapLibre.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace MapLibreDemo
{
    /// <summary>
    /// Minimal demo: attach this to an empty GameObject and it builds a full-screen Canvas + RawImage showing the
    /// MapLibre map, with mouse-drag panning and scroll-wheel zooming.
    /// </summary>
    public class MapLibreDemoBootstrap : MonoBehaviour
    {
        private MapLibreMapView _mapView;
        private RawImage _rawImage;
        private Vector3 _lastMousePosition;
        private bool _dragging;

        private void Awake()
        {
            GameObject canvasGo = new GameObject("MapLibreCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject imageGo = new GameObject("MapRawImage");
            imageGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);

            _rawImage = imageGo.AddComponent<RawImage>();
            RectTransform rect = _rawImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _mapView = gameObject.AddComponent<MapLibreMapView>();
        }

        private void Update()
        {
            if (_mapView == null)
            {
                return;
            }

            // Keep the RawImage's texture in sync as soon as the map view produces one (or replaces it, e.g. on
            // resize). Checking every frame keeps this simple without needing a callback/event on MapLibreMapView.
            if (_rawImage.texture != _mapView.Texture && _mapView.Texture != null)
            {
                _rawImage.texture = _mapView.Texture;
            }

            HandleMouseDrag();
            HandleMouseWheel();
        }

        private void HandleMouseDrag()
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

                if (delta.sqrMagnitude > 0f)
                {
                    // Screen-space pan: MapLibre's mln_map_move_by expects a delta in the same screen space as
                    // the map's own pixel buffer. Unity's mouse delta.y is flipped relative to screen pixel rows,
                    // so it is negated here to match expected drag direction (drag down -> map moves down).
                    _mapView.MoveBy(delta.x, -delta.y);
                }
            }
        }

        private void HandleMouseWheel()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0f)
            {
                // Positive scroll (wheel up) zooms in (scale > 1), negative zooms out (scale < 1).
                double scale = 1.0 + scroll * 2.0;
                _mapView.ScaleBy(scale);
            }
        }
    }
}
