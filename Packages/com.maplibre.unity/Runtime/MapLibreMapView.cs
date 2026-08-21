using UnityEngine;

namespace MapLibre.Unity
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class MapLibreMapView : MonoBehaviour
    {
        [SerializeField] private int _width = 1024;
        [SerializeField] private int _height = 768;
        [SerializeField] private double _scaleFactor = 1.0;
        [SerializeField] private string _styleUrl = "https://demotiles.maplibre.org/style.json";

        private MapLibreMapHandle _map;
        private Texture2D _texture;

        public Texture Texture => _texture;

        private void OnEnable()
        {
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                var rect = rectTransform.rect;
                if (rect.width > 0) _width = Mathf.RoundToInt(rect.width);
                if (rect.height > 0) _height = Mathf.RoundToInt(rect.height);
            }

            if (_width <= 0) _width = 1024;
            if (_height <= 0) _height = 768;

            try
            {
                _map = MapLibreMapHandle.Create(_width, _height, _scaleFactor, _styleUrl);
                _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false, false);
                _texture.filterMode = FilterMode.Bilinear;
                _texture.wrapMode = TextureWrapMode.Clamp;
                
                // 初回は強制的に全領域を黒でクリアしておく（後でマップが上書きされる）
                Color32[] fill = new Color32[_width * _height];
                _texture.SetPixels32(fill);
                _texture.Apply();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"MapLibreMapView: failed to initialize MapLibre map: {ex}");
                enabled = false;
            }
        }

        private void OnDisable()
        {
            if (_map != null)
            {
                _map.Dispose();
                _map = null;
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }

        private void Update()
        {
            if (_map == null || _texture == null)
            {
                return;
            }

            // メインループを進める
            _map.Step();

            // C++ 側からテクスチャのピクセル配列をコピーして Unity の Texture2D に適用
            byte[] pixelBuffer = null;
            if (_map.TryReadPixels(ref pixelBuffer, out int outWidth, out int outHeight, out int outStride))
            {
                if (outWidth == _texture.width && outHeight == _texture.height)
                {
                    _texture.LoadRawTextureData(pixelBuffer);
                    _texture.Apply(false);
                }
            }
            else
            {
                // ピクセルが読めなかった場合でも、再描画リクエストを送って次に備える
                _map.RequestRepaint();
            }
        }

        // --- 外部からカメラやスタイルを操作するためのラッパーメソッド ---

        public void SetStyleUrl(string styleUrl)
        {
            _styleUrl = styleUrl;
            if (_map != null)
            {
                _map.SetStyleUrl(styleUrl);
            }
        }

        public void SetCamera(double latitude, double longitude, double zoom, double bearing, double pitch)
        {
            if (_map != null)
            {
                _map.SetCamera(latitude, longitude, zoom, bearing, pitch);
            }
        }

        public void MoveBy(double deltaX, double deltaY)
        {
            if (_map != null)
            {
                _map.MoveBy(deltaX, deltaY);
            }
        }

        public void ScaleBy(double scale, double? anchorX = null, double? anchorY = null)
        {
            if (_map != null)
            {
                _map.ScaleBy(scale, anchorX, anchorY);
            }
        }
    }
}