using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using MapLibre.Unity;

//======================================================================
// MapViewController (全内部状態ダンプ・診断機能組み込み版)
// ======================================================================
public class MapViewController : MonoBehaviour
{
	[SerializeField] private MapLibreMapView _mapView;
	[SerializeField] private RawImage _targetRawImage;

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

	// カメラ現在状態
	private double _currentLat;
	private double _currentLon;
	private double _currentZoom;
	private double _currentBearing;
	private double _currentPitch;

	public double CurrentZoom => _currentZoom;

	// GPSターゲット座標
	private double _gpsLat;
	private double _gpsLon;

	private bool _isCameraInitialized = false;
	private bool _isDragging = false;
	private bool _isUserManualMode = false;
	private float _manualModeTimer = 0.0f;

	private Vector2 _lastMousePos;
	private float _lastPinchDistance;
	private float _lastPinchAngle;

	// 画面サイズ監視用
	private Vector2 _lastScreenSize;

	private bool _hasWarnedNullTarget = false;

private void Awake()
{
    if (_mapView == null) _mapView = FindFirstObjectByType<MapLibreMapView>();
    if (_targetRawImage == null) _targetRawImage = GetComponentInChildren<RawImage>();

    _currentLat = _gpsLat = _defaultLat;
    _currentLon = _gpsLon = _defaultLon;
    _currentZoom = _defaultZoomLevel;
    _currentBearing = _bearing;
    _currentPitch = _pitch;

    // スクリプトからの上書き処理を停止
    // if (_useLocal3DStyle && _mapView != null) { ... }
}
private void Start()
{
    // ★ 追加: すべてのコンポーネントのAwake完了後にローカルスタイルを確実に上書き適用する
    if (_useLocal3DStyle && _mapView != null)
    {
        string localUrl = GetLocalStyleUrl(_localStyleFileName);
        Debug.Log($"[MapViewController LOG] Startにてローカルスタイルを強制上書き適用: {localUrl}");

    }

    ForceSyncNativeResolution();

    InspectMapLibreInternalState();
}

	/// <summary>
	/// MapLibreMapView が保持する全プロパティ・フィールド・メソッドを Console に出力する
	/// </summary>
	private void InspectMapLibreInternalState()
	{
		if (_mapView == null)
		{
			Debug.LogError("[INSPECTOR ERROR] _mapView が Null です。");
			return;
		}

		Debug.Log("==========================================");
		Debug.Log("[MAPLIBRE INTERNAL DUMP] オブジェクト内部情報の出力開始");
		
		var type = _mapView.GetType();
		var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

		// 1. 全プロパティの現在値を出力
		Debug.Log("--- [PROPERTIES] ---");
		foreach (var prop in type.GetProperties(flags))
		{
			try
			{
				object val = prop.CanRead ? prop.GetValue(_mapView, null) : "<WriteOnly>";
				Debug.Log($"[Prop] {prop.Name} ({prop.PropertyType.Name}) = {val}");
			}
			catch (System.Exception ex)
			{
				Debug.Log($"[Prop Error] {prop.Name} : {ex.Message}");
			}
		}

		// 2. 全プライベート/パブリック変数の現在値を出力
		Debug.Log("--- [FIELDS] ---");
		foreach (var field in type.GetFields(flags))
		{
			try
			{
				object val = field.GetValue(_mapView);
				Debug.Log($"[Field] {field.Name} ({field.FieldType.Name}) = {val}");
			}
			catch (System.Exception ex)
			{
				Debug.Log($"[Field Error] {field.Name} : {ex.Message}");
			}
		}

		// 3. ログ・エラー・イベント関連メソッドの探索
		Debug.Log("--- [METHODS (Log/Error/Tile/Status Candidate)] ---");
		foreach (var method in type.GetMethods(flags))
		{
			string name = method.Name.ToLower();
			if (name.Contains("log") || name.Contains("error") || name.Contains("tile") || 
				name.Contains("status") || name.Contains("event") || name.Contains("callback"))
			{
				var paramStrs = System.Array.ConvertAll(method.GetParameters(), p => $"{p.ParameterType.Name} {p.Name}");
				Debug.Log($"[Method] {method.ReturnType.Name} {method.Name}({string.Join(", ", paramStrs)})");
			}
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
				Debug.Log("[MapViewController LOG] カメラ初期位置を適用します。");
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
//			Debug.Log($"[MapViewController LOG] ホイールズーム実行: Scroll={scroll}, NewZoom={_currentZoom}");
			ApplyCamera();
		}

		HandleMultiTouch();

		if (_isUserManualMode && _autoRecenterDelay > 0.0f && !_isDragging)
		{
			_manualModeTimer += Time.deltaTime;
			if (_manualModeTimer >= _autoRecenterDelay)
			{
				OnClickRecenter();
			}
		}
	}

	private void ForceSyncNativeResolution()
	{
		if (_targetRawImage == null || _mapView == null) return;

		RectTransform rt = _targetRawImage.rectTransform;
		Vector2 currentSize = new Vector2(rt.rect.width, rt.rect.height);

		if (currentSize.x <= 10 || currentSize.y <= 10) return;
		if (currentSize == _lastScreenSize) return;

		_lastScreenSize = currentSize;

		int targetW = Mathf.RoundToInt(currentSize.x);
		int targetH = Mathf.RoundToInt(currentSize.y);

		var type = _mapView.GetType();
		var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

		SetFieldValue(type, _mapView, "width", targetW, flags);
		SetFieldValue(type, _mapView, "height", targetH, flags);
		SetFieldValue(type, _mapView, "_width", targetW, flags);
		SetFieldValue(type, _mapView, "_height", targetH, flags);
		SetFieldValue(type, _mapView, "Width", targetW, flags);
		SetFieldValue(type, _mapView, "Height", targetH, flags);

		string[] methodNames = new[] { "Resize", "SetSize", "UpdateSize", "OnRectTransformDimensionsChange" };
		foreach (var mName in methodNames)
		{
			var method = type.GetMethod(mName, flags);
			if (method != null)
			{
				var parameters = method.GetParameters();
				if (parameters.Length == 2)
				{
					method.Invoke(_mapView, new object[] { targetW, targetH });
					break;
				}
				else if (parameters.Length == 0)
				{
					method.Invoke(_mapView, null);
					break;
				}
			}
		}

		ApplyCamera();
		Debug.Log($"[MapViewController LOG] C++描画解像度をUIに同期修正: Width={targetW}, Height={targetH}");
	}

	private void SetFieldValue(System.Type type, object instance, string fieldName, object val, BindingFlags flags)
	{
		var field = type.GetField(fieldName, flags);
		if (field != null)
		{
			field.SetValue(instance, val);
			return;
		}
		var prop = type.GetProperty(fieldName, flags);
		if (prop != null && prop.CanWrite)
		{
			prop.SetValue(instance, val, null);
		}
	}

	private void HandleDirectDrag()
	{
		if (Input.touchCount > 1)
		{
			_isDragging = false;
			return;
		}

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
//				Debug.Log($"[MapViewController DRAG LOG] ドラッグ判定成功（枠内クリック）: MousePos={mousePos}");
			}
			else
			{
//				Debug.LogWarning($"[MapViewController DRAG LOG] ドラッグ失敗: クリック位置 {mousePos} が MapRawImage の枠外です。");
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

//				Debug.Log($"[MapViewController DRAG LOG] 地図ドラッグ移動中: Delta={delta}, NewLat={_currentLat}, NewLon={_currentLon}");

				ApplyCamera();
			}
		}

		if (Input.GetMouseButtonUp(0) && _isDragging)
		{
			_isDragging = false;
//			Debug.Log("[MapViewController DRAG LOG] ドラッグ終了 (マウス離された)");
		}
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
//				Debug.Log($"[MapViewController LOG] 右クリックPitch変更: NewPitch={_currentPitch}");
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

	public void UpdateMapPosition(double latitude, double longitude)
	{
		_gpsLat = latitude;
		_gpsLon = longitude;

		if (!_isUserManualMode)
		{
			_currentLat = latitude;
			_currentLon = longitude;
			ApplyCamera();
		}
	}

	public void ApplyCamera()
	{
		if (_mapView == null) return;
//		Debug.Log($"[MapViewController LOG] SetCamera実行 -> Lat:{_currentLat}, Lon:{_currentLon}, Zoom:{_currentZoom}, Bearing:{_currentBearing}, Pitch:{_currentPitch}");
		_mapView.SetCamera(_currentLat, _currentLon, _currentZoom, _currentBearing, _currentPitch);
	}

	private void SetMapStyleUrl(MapLibreMapView mapView, string url)
	{
		if (mapView == null) return;
		var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
		var type = mapView.GetType();

		var prop = type.GetProperty("styleUrl", flags) ?? type.GetProperty("StyleUrl", flags) ?? type.GetProperty("_styleUrl", flags);
		if (prop != null && prop.CanWrite)
		{
			prop.SetValue(mapView, url, null);
			return;
		}

		var method = type.GetMethod("SetStyleUrl", flags) ?? type.GetMethod("LoadStyle", flags);
		if (method != null)
		{
			method.Invoke(mapView, new object[] { url });
			return;
		}

		var field = type.GetField("styleUrl", flags) ?? type.GetField("_styleUrl", flags) ?? type.GetField("StyleUrl", flags);
		if (field != null) field.SetValue(mapView, url);
	}

	private string GetLocalStyleUrl(string fileName)
	{
		string filePath = Path.Combine(Application.streamingAssetsPath, fileName);
		filePath = filePath.Replace("\\", "/");

		if (!filePath.StartsWith("file:///"))
		{
			filePath = "file:///" + filePath.TrimStart('/');
		}
		return filePath;
	}

	private void OnValidate()
	{
		if (Application.isPlaying && _isCameraInitialized) ApplyCamera();
	}


}
