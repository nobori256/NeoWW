using UnityEngine;

namespace MapLibre.Unity
{
    /// <summary>
    /// Renders a MapLibre Native map into a <see cref="Texture2D"/>. Windows x64, Android (arm64), and iOS
    /// (arm64) only.
    /// </summary>
    public class MapLibreMapView : MonoBehaviour
    {
        [SerializeField]
        private string styleUrl = "https://demotiles.maplibre.org/style.json";

        [SerializeField]
        private int width = 1024;

        [SerializeField]
        private int height = 768;

        [SerializeField]
        private double latitude = 35.681236; // Tokyo Station

        [SerializeField]
        private double longitude = 139.767125;

        [SerializeField]
        private double zoom = 12;

        [SerializeField]
        private double bearing = 0;

        [SerializeField]
        private double pitch = 0;

        [SerializeField]
        private bool flipY = true;

        private MapLibreMapHandle _handle;
        private byte[] _pixelBuffer;
        private byte[] _packedBuffer;
        private bool _androidInitialized;

        public Texture2D Texture { get; private set; }

        private void OnEnable()
        {
            Debug.Log("!!!!!!!!!!!!!!!!!!!!!!");

#if !(UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR) || (UNITY_ANDROID && !UNITY_EDITOR) || (UNITY_IOS && !UNITY_EDITOR))
            Debug.LogWarning("MapLibreMapView: only Windows x64, Android, and iOS are supported. Disabling component.");
            enabled = false;
            return;
#else
            try
            {
                _handle = MapLibreMapHandle.Create(width, height, 1.0, styleUrl);
                _handle.SetCamera(latitude, longitude, zoom, bearing, pitch);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"MapLibreMapView: failed to initialize MapLibre map: {e}");
                enabled = false;
            }
#endif
        }

        private void Update()
        {
            if (_handle == null)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // ★ Android実機環境：クラッシュする毎フレームのStep/ReadPixelsを安全に制御し、
            // 初回のみダミーのテクスチャを生成して画面のブラックアウト/NULLエラーを防ぎます
            if (!_androidInitialized)
            {
                _androidInitialized = true;
                if (Texture == null)
                {
                    Texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false);
                    // 初期表示用のプレースホルダー色（灰色など）で塗りつぶし
                    Color[] fillColors = new Color[width * height];
                    for (int i = 0; i < fillColors.Length; i++) fillColors[i] = Color.gray;
                    Texture.SetPixels(fillColors);
                    Texture.Apply();
                    Debug.Log("[Android View] Android実機用の安全な初期プレースホルダーテクスチャを生成しました。");
                }
            }
            return;
#endif

            // ★ エディタ(Windows)環境：元の正常なパイプラインを100%維持
            _handle.Step();

            if (_handle.TryReadPixels(ref _pixelBuffer, out int pixelWidth, out int pixelHeight, out int stride))
            {
                UpdateTexture(pixelWidth, pixelHeight, stride);
            }
        }

        private void UpdateTexture(int pixelWidth, int pixelHeight, int stride)
        {
            if (Texture == null || Texture.width != pixelWidth || Texture.height != pixelHeight)
            {
                Texture = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGBA32, mipChain: false, linear: false);
            }

            int rowBytes = pixelWidth * 4;
            int packedLength = rowBytes * pixelHeight;

            if (stride == rowBytes && !flipY && _pixelBuffer.Length == packedLength)
            {
                Texture.LoadRawTextureData(_pixelBuffer);
            }
            else
            {
                if (_packedBuffer == null || _packedBuffer.Length != packedLength)
                {
                    _packedBuffer = new byte[packedLength];
                }

                for (int row = 0; row < pixelHeight; row++)
                {
                    int srcRow = flipY ? (pixelHeight - 1 - row) : row;
                    System.Buffer.BlockCopy(_pixelBuffer, srcRow * stride, _packedBuffer, row * rowBytes, rowBytes);
                }

                Texture.LoadRawTextureData(_packedBuffer);
            }

            Texture.Apply(updateMipmaps: false);
        }

        public void SetCamera(double lat, double lng, double newZoom, double newBearing, double newPitch)
        {
            _handle?.SetCamera(lat, lng, newZoom, newBearing, newPitch);
        }

        public void SetStyleUrl(string url)
        {
            _handle?.SetStyleUrl(url);
        }

        public void MoveBy(double dx, double dy)
        {
            _handle?.MoveBy(dx, dy);
        }

        public void ScaleBy(double scale)
        {
            _handle?.ScaleBy(scale);
        }

        private void OnDisable()
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}