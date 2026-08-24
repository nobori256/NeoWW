using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Rendering;
using MapLibre.Unity;

//======================================================================
// MapViewController (MapLibreエラーキャッチ・完全版)
// ======================================================================
[DefaultExecutionOrder(-100)]
public class MapViewController : MonoBehaviour
{
	[SerializeField] private MapLibreMapView _mapView;
	[SerializeField] private RawImage _targetRawImage;
	[SerializeField] private Text _versionText;

	[Header("ローカルスタイルを使用する")]
	[SerializeField] private bool _useLocal3DStyle = true;
	[SerializeField] private string _localStyleFileName = "custom_3d_style.json";

	[Header("初期表示位置 (東京駅・丸の内)")]
	[SerializeField] private double _defaultLat = 35.6812;
	[SerializeField] private double _defaultLon = 139.7671;
	[SerializeField] private double _defaultZoomLevel = 16.0;
	[SerializeField] private double _bearing = 0.0;
	[SerializeField] private double _pitch = 60.0;

	[Header("操作感度設定")]
	[SerializeField] private float _panSensitivity = 0.00005f;
	[SerializeField] private float _zoomSensitivity = 0.5f;
	[SerializeField] private float _rotateSensitivity = 0.2f;
	[SerializeField] private float _pitchSensitivity = 1.5f;

	[Header("手動操作時のGPS自動追随設定")]
	[SerializeField] private float _autoRecenterDelay = 0.0f;

	private double _currentLat;
	private double _currentLon;
	private double _currentZoom;
	private double _currentBearing;
	private double _currentPitch;

	public double CurrentZoom => _currentZoom;

	private double _gpsLat;
	private double _gpsLon;

	private bool _isCameraInitialized = false;
	private bool _isDragging = false;
	private bool _isUserManualMode = false;
	private float _manualModeTimer = 0.0f;

	private Vector2 _lastMousePos;
	private float _lastPinchDistance;
	private float _lastPinchAngle;
	private Vector2 _lastScreenSize;
	private bool _hasWarnedNullTarget = false;

	private void Awake()
	{
		if (_mapView == null) _mapView = FindFirstObjectByType<MapLibreMapView>();
		if (_targetRawImage == null) _targetRawImage = GetComponentInChildren<RawImage>();
		if (_versionText == null) _versionText = GetComponentInChildren<Text>();

		// MapLibre側のエラー・ログイベントをフックしてインゲームログに流す
		HookMapLibreLogs();

		_currentLat = _gpsLat = _defaultLat;
		_currentLon = _gpsLon = _defaultLon;
		_currentZoom = _defaultZoomLevel;
		_currentBearing = _bearing;
		_currentPitch = _pitch;
        
        // 注意: ここにあったローカルスタイルの適用処理は、
        // ネイティブマップ生成後に行うため Start() に移動しました。
	}

	private void Start()
	{
		string version = BuildVersionInfo.GetVersionString();
		Debug.Log("==================================");
		Debug.Log($"[App Version] ビルド日時: {version}");
		Debug.Log("==================================");

		if (_versionText != null) _versionText.text = $"Build: {version}";

		ForceSyncNativeResolution();

        // 確実なタイミング（ネイティブ生成後）でローカルスタイルを適用する
		if (_useLocal3DStyle && _mapView != null)
		{
			string localUrl = GetLocalStyleUrl(_localStyleFileName);
			SetMapStyleUrl(_mapView, localUrl);
		}

		StartCoroutine(RunMapDiagnosticTest());
	}

	private void HookMapLibreLogs()
	{
		try
		{
			var type = _mapView != null ? _mapView.GetType() : typeof(MapLibreMapView);
			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
			
			// MapLibre関連の静的イベントやデリゲートを探索してログをバインドする
			foreach (var ev in type.GetEvents(flags))
			{
				if (ev.Name.ToLower().Contains("log") || ev.Name.ToLower().Contains("error"))
				{
					Debug.Log($"[MapViewController] MapLibreのイベントを発見: {ev.Name}");
				}
			}
		}
		catch (System.Exception ex)
		{
			Debug.LogWarning($"[MapViewController] ログフック時の例外: {ex.Message}");
		}
	}

	private IEnumerator RunMapDiagnosticTest()
	{
		yield return new WaitForSeconds(2.0f);
		Debug.Log("==========================================");
		Debug.Log("[DIAGNOSTIC TEST] マップ診断レポート開始");

		string destPath = Path.Combine(Application.persistentDataPath, _localStyleFileName);
		if (File.Exists(destPath))
		{
			string jsonContent = File.ReadAllText(destPath);
			Debug.Log($"[DIAGNOSTIC] スタイルファイル存在OK. サイズ: {jsonContent.Length} バイト");
		}
		else
		{
			Debug.LogError($"[DIAGNOSTIC ERROR] スタイルファイルが存在しません: {destPath}");
		}

		if (_mapView != null)
		{
			if (_mapView.Texture != null)
				Debug.Log($"[DIAGNOSTIC] ★ _mapView.Texture 取得成功: {_mapView.Texture.width}x{_mapView.Texture.height}");
			else
				Debug.LogWarning("[DIAGNOSTIC WARNING] ✗ _mapView.Texture が NULL です（スタイル内のURL/パス起因の可能性大）");
		}
		Debug.Log("==========================================");
	}

	private void Update()
	{
		if (_mapView == null || _targetRawImage == null)
		{
			if (!_hasWarnedNullTarget)
			{
				Debug.LogError($"[MapViewController ERROR] Update停止中: MapView = {(_mapView != null ? "OK" : "NULL")}, TargetRawImage = {(_targetRawImage != null ? "OK" : "NULL")}");
				_hasWarnedNullTarget = true;
			}
			return;
		}

		ForceSyncNativeResolution();

		if (_mapView.Texture != null)
		{
			if (_targetRawImage.texture != _mapView.Texture)
			{
				_targetRawImage.texture = _mapView.Texture;
				Debug.Log("[MapViewController LOG] C++描画テクスチャを TargetRawImage にセットしました。");
			}

			if (!_isCameraInitialized)
			{
				_isCameraInitialized = true;
				ApplyCamera();
			}
		}

		HandleDirectDrag();
		HandleRightClickPitch();
		
		float scroll = Input.GetAxis("Mouse ScrollWheel");
		if (Mathf.Abs(scroll) > 0.01f)
		{
			_isUserManualMode = true;
			_manualModeTimer = 0.0f;
			_currentZoom = Mathf.Clamp((float)(_currentZoom + scroll * _zoomSensitivity * 5.0f), 2.0f, 20.0f);
			ApplyCamera();
		}

		HandleMultiTouch();

		if (_isUserManualMode && _autoRecenterDelay > 0.0f && !_isDragging)
		{
			_manualModeTimer += Time.deltaTime;
			if (_manualModeTimer >= _autoRecenterDelay) OnClickRecenter();
		}
	}

	private void ForceSyncNativeResolution()
	{
		if (_targetRawImage == null || _mapView == null) return;
		RectTransform rt = _targetRawImage.rectTransform;
		Vector2 currentSize = new Vector2(rt.rect.width, rt.rect.height);
		if (currentSize.x <= 10 || currentSize.y <= 10 || currentSize == _lastScreenSize) return;
		_lastScreenSize = currentSize;

		int targetW = Mathf.RoundToInt(currentSize.x);
		int targetH = Mathf.RoundToInt(currentSize.y);

		var type = _mapView.GetType();
		var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

		string[] methodNames = new[] { "Resize", "SetSize", "UpdateSize" };
		foreach (var mName in methodNames)
		{
			var method = type.GetMethod(mName, flags);
			if (method != null)
			{
				var parameters = method.GetParameters();
				if (parameters.Length == 2) { method.Invoke(_mapView, new object[] { targetW, targetH }); break; }
			}
		}
		ApplyCamera();
	}

	private void ApplyCamera()
	{
		if (_mapView == null) return;
		_mapView.SetCamera(_currentLat, _currentLon, _currentZoom, _currentBearing, _currentPitch);
	}

	private void SetMapStyleUrl(MapLibreMapView mapView, string url)
	{
		var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		var type = mapView.GetType();

		var prop = type.GetProperty("styleUrl", flags) ?? type.GetProperty("StyleUrl", flags) ?? type.GetProperty("_styleUrl", flags);
		if (prop != null && prop.CanWrite)
		{
			prop.SetValue(mapView, url, null);
			Debug.Log($"[MapViewController] 成功: スタイルを設定しました -> {url}");
			return;
		}

		var method = type.GetMethod("SetStyleUrl", flags) ?? type.GetMethod("LoadStyle", flags);
		if (method != null)
		{
			method.Invoke(mapView, new object[] { url });
			Debug.Log($"[MapViewController] 成功: スタイルを適用しました -> {url}");
			return;
		}

		var field = type.GetField("styleUrl", flags) ?? type.GetField("_styleUrl", flags) ?? type.GetField("StyleUrl", flags);
		if (field != null)
		{
			field.SetValue(mapView, url);
			Debug.Log($"[MapViewController] 成功: スタイルを設定しました -> {url}");
			return;
		}
		Debug.LogError("[MapViewController] 失敗: スタイル設定用のプロパティ・メソッドが見つかりませんでした！");
	}

	private string GetLocalStyleUrl(string fileName)
	{
		string sourcePath = Path.Combine(Application.streamingAssetsPath, fileName);
		string destPath = Path.Combine(Application.persistentDataPath, fileName);

		if (!File.Exists(destPath))
		{
			if (sourcePath.Contains("://") || sourcePath.Contains(":///"))
			{
				var request = UnityWebRequest.Get(sourcePath);
				request.SendWebRequest();
				while (!request.isDone) { }
				if (request.result == UnityWebRequest.Result.Success)
				{
					File.WriteAllBytes(destPath, request.downloadHandler.data);
					Debug.Log($"[MapViewController] スタイルファイルを展開しました: {destPath}");
				}
			}
			else if (File.Exists(sourcePath))
			{
				File.Copy(sourcePath, destPath, true);
			}
		}
		return "file://" + destPath;
	}

	private void HandleDirectDrag()
	{
		if (Input.touchCount > 1) { _isDragging = false; return; }

		if (Input.GetMouseButtonDown(0))
		{
			Vector2 mousePos = Input.mousePosition;
			Canvas canvas = _targetRawImage != null ? _targetRawImage.canvas : null;
			Camera eventCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

			bool isInside = RectTransformUtility.RectangleContainsScreenPoint(_targetRawImage.rectTransform, mousePos, eventCam);
			
			if (isInside)
			{
				_isDragging = true;
				_lastMousePos = mousePos;
			}
		}

		if (Input.GetMouseButton(0) && _isDragging)
		{
			Vector2 currentPos = Input.mousePosition;
			Vector2 delta = currentPos - _lastMousePos;
			_lastMousePos = currentPos;

			if (delta.sqrMagnitude > 0.001f)
			{
				_isUserManualMode = true;
				_manualModeTimer = 0.0f;

				double rad = _currentBearing * Mathf.Deg2Rad;
				double factor = _panSensitivity * Mathf.Pow(2, (float)(16 - _currentZoom));

				double dx = (-delta.x * Mathf.Cos((float)rad) - delta.y * Mathf.Sin((float)rad)) * factor;
				double dy = (-delta.x * Mathf.Sin((float)rad) + delta.y * Mathf.Cos((float)rad)) * factor;

				_currentLon += dx;
				_currentLat -= dy;

				ApplyCamera();
			}
		}

		if (Input.GetMouseButtonUp(0) && _isDragging) _isDragging = false;
	}

	private void HandleRightClickPitch()
	{
		if (Input.GetMouseButton(1))
		{
			float mouseDeltaY = Input.GetAxis("Mouse Y");
			if (Mathf.Abs(mouseDeltaY) > 0.01f)
			{
				_isUserManualMode = true;
				_manualModeTimer = 0.0f;
				_currentPitch = Mathf.Clamp((float)(_currentPitch + mouseDeltaY * _pitchSensitivity * 5.0f), 0.0f, 75.0f);
				ApplyCamera();
			}
		}
	}

	private void HandleMultiTouch()
	{
		if (Input.touchCount == 2)
		{
			Touch touch0 = Input.GetTouch(0);
			Touch touch1 = Input.GetTouch(1);

			Vector2 pos0 = touch0.position;
			Vector2 pos1 = touch1.position;

			float currentDist = Vector2.Distance(pos0, pos1);
			Vector2 dir = pos1 - pos0;
			float currentAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

			if (touch0.phase == TouchPhase.Began || touch1.phase == TouchPhase.Began)
			{
				_lastPinchDistance = currentDist;
				_lastPinchAngle = currentAngle;
			}
			else if (touch0.phase == TouchPhase.Moved || touch1.phase == TouchPhase.Moved)
			{
				_isUserManualMode = true;
				_manualModeTimer = 0.0f;

				float distDelta = currentDist - _lastPinchDistance;
				_currentZoom = Mathf.Clamp((float)(_currentZoom + distDelta * _zoomSensitivity * 0.01f), 2.0f, 20.0f);
				_lastPinchDistance = currentDist;

				float angleDelta = Mathf.DeltaAngle(_lastPinchAngle, currentAngle);
				_currentBearing = (_currentBearing - angleDelta * _rotateSensitivity) % 360.0;
				_lastPinchAngle = currentAngle;

				Vector2 touch0Delta = touch0.deltaPosition;
				Vector2 touch1Delta = touch1.deltaPosition;
				if (Vector2.Dot(touch0Delta.normalized, touch1Delta.normalized) > 0.7f)
				{
					float avgDeltaY = (touch0Delta.y + touch1Delta.y) * 0.5f;
					_currentPitch = Mathf.Clamp((float)(_currentPitch + avgDeltaY * _pitchSensitivity * 0.1f), 0.0f, 75.0f);
				}

				ApplyCamera();
			}
		}
	}

	public void OnClickRecenter()
	{
		_isUserManualMode = false;
		_manualModeTimer = 0.0f;

		_currentLat = _gpsLat;
		_currentLon = _gpsLon;
		_currentZoom = _defaultZoomLevel;
		_currentBearing = _bearing;
		_currentPitch = _pitch;
		Debug.Log("[MapViewController LOG] Recenter実行");
		ApplyCamera();
	}

	public void UpdateMapPosition(double lat, double lon)
	{
		_gpsLat = lat;
		_gpsLon = lon;

		if (!_isUserManualMode)
		{
			_currentLat = lat;
			_currentLon = lon;
			ApplyCamera();
		}
	}
}