using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GeoLookup
{
	//======================================================================
	// LocationStayManager
	//======================================================================
	public class LocationStayManager : MonoBehaviour
	{
		[Serializable]
		public class CityStayConfirmedEvent : UnityEvent<CityFeature> { }

		[Header("データ設定")]
		[SerializeField] private TextAsset _geoJsonFile;
		[SerializeField] private float _maxHorizontalAccuracyMeters = 30.0f; // 許容GPS誤差(m)

		[Header("GPS回数制御設定")]
		[SerializeField] private int _requiredSampleCount = 10; // 判定実行に必要なGPS取得回数
		private int _currentSampleCount = 0;                     // 現在のGPS取得カウント

		[Header("UI連携設定")]
		[SerializeField] private Text _displayText; // 画面表示用テキストコンポーネント

		[Header("Map Settings")]
		[SerializeField] private MapViewController _mapViewController; // ベクターマップコントローラー

		[Header("イベント")]
		public CityStayConfirmedEvent OnCityStayConfirmed = new CityStayConfirmedEvent();

		private OfflineReverseGeocoder _geocoder;
		private CityFeature _currentConfirmedCity = null; // 現在確定している自治体
		private CityFeature _candidateCity = null;        // 候補自治体

		// GPS制御・タイマー用変数
		private double _lastGpsTimestamp = 0.0;           // 前回取得したGPSログのタイムスタンプ
		private float _sampleTimer = 0.0f;                // 1秒周期判定用タイマー

		private void Start()
		{
			InitializeGeocoder();
			StartCoroutine(StartGpsServiceCoroutine());
			UpdateUI("GPS初期化中...", "未確定", 0, _requiredSampleCount, 0.0, 0.0, 0.0f, -1.0f, -1.0f, 0.0, "Initializing");
		}

		private void InitializeGeocoder()
		{
			if (_geoJsonFile == null)
			{
				Debug.LogError("[LocationStayManager] GeoJSONファイルがInspectorにアタッチされていません。");
				return;
			}

			try
			{
				_geocoder = new OfflineReverseGeocoder(_geoJsonFile.text);
				Debug.Log($"[LocationStayManager] GeoJSONデータを正常に読み込みました。(ファイルサイズ: {_geoJsonFile.text.Length} bytes)");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[LocationStayManager] ジオコーダーの初期化に失敗しました: {ex.Message}");
			}
		}

		public void StartGpsService()
		{
			StartCoroutine(StartGpsServiceCoroutine());
		}

		private void Update()
		{
			double currentLat = 0.0;
			double currentLon = 0.0;
			float currentAlt = 0.0f;
			float currentHAcc = -1.0f;
			float currentVAcc = -1.0f;
			double currentTimestamp = 0.0;
			string gpsStatusStr = Input.location.status.ToString();

			if (Input.location.status == LocationServiceStatus.Running)
			{
				LocationInfo lastData = Input.location.lastData;
				currentLat = lastData.latitude;
				currentLon = lastData.longitude;
				currentAlt = lastData.altitude;
				currentHAcc = lastData.horizontalAccuracy;
				currentVAcc = lastData.verticalAccuracy;
				currentTimestamp = lastData.timestamp;

				_sampleTimer += Time.deltaTime;
				if (_sampleTimer >= 1.0f)
				{
					_sampleTimer = 0.0f;
					OnLocationUpdated(currentLat, currentLon, currentHAcc);
				}
			}
#if UNITY_EDITOR
			else
			{
				currentLat = 35.685175;  // 千代田区
				currentLon = 139.7528;
				currentHAcc = 5.0f;
				currentTimestamp = Time.time;
				gpsStatusStr = "Running (Editor Default)";

				_sampleTimer += Time.deltaTime;
				if (_sampleTimer >= 1.0f)
				{
					_sampleTimer = 0.0f;
					OnLocationUpdated(currentLat, currentLon, currentHAcc);
				}
			}
#endif

			if (_mapViewController != null && (currentLat != 0.0 || currentLon != 0.0))
			{
				_mapViewController.UpdateMapPosition(currentLat, currentLon);
			}

			string currentCityText = _currentConfirmedCity != null ? 
				$"{_currentConfirmedCity.StateName} {_currentConfirmedCity.CityName}" : "未確定";

			string candidateCityText = _candidateCity != null ? 
				$"{_candidateCity.StateName} {_candidateCity.CityName}" : "検出なし";

			UpdateUI(currentCityText, candidateCityText, _currentSampleCount, _requiredSampleCount, currentLat, currentLon, currentAlt, currentHAcc, currentVAcc, currentTimestamp, gpsStatusStr);
		}

		public void SetRequiredSampleCount(int count)
		{
			if (count <= 0)
			{
				Debug.LogWarning("[LocationStayManager] 設定回数は1以上を指定してください。");
				return;
			}

			_requiredSampleCount = count;
			_currentSampleCount = 0;

			Debug.Log($"[LocationStayManager] GPS判定の要求回数を変更しました: {count}回");
		}

		private void OnLocationUpdated(double latitude, double longitude, float accuracy)
		{
			if (!ValidateGpsAccuracy(accuracy)) return;
			if (_geocoder == null) return;

			CityFeature detectedCity = _geocoder.GetCity(latitude, longitude);

			if (detectedCity == null)
			{
				_candidateCity = null;
				_currentSampleCount = 0;
				return;
			}

			if (_candidateCity != null && _candidateCity.CityCode == detectedCity.CityCode)
			{
				_currentSampleCount++;
				if (_currentSampleCount >= _requiredSampleCount && _currentConfirmedCity?.CityCode != detectedCity.CityCode)
				{
					_currentConfirmedCity = detectedCity;
					OnCityStayConfirmed?.Invoke(_currentConfirmedCity);
				}
			}
			else
			{
				_candidateCity = detectedCity;
				_currentSampleCount = 1;
			}
		}

		private bool ValidateGpsAccuracy(float accuracy)
		{
			return true;
		}

		private void UpdateUI(string confirmedCityStr, string candidateCityStr, int currentCount, int requiredCount, double latitude, double longitude, float altitude, float hAccuracy, float vAccuracy, double timestamp, string gpsStatus)
		{
			if (_displayText == null) return;

			_displayText.fontSize = 28;

			// MapViewController から実数値を取得して UI に反映
			string zoomStr = _mapViewController != null ? _mapViewController.CurrentZoom.ToString("F2") : "N/A";

			string displayBuffer = $"[GPS Status]: {gpsStatus}\n" +
			                      $"[確定地]: {confirmedCityStr}\n" +
			                      $"[直近検出]: {candidateCityStr}\n" +
			                      $"[カウント]: {currentCount}/{requiredCount}\n" +
			                      $"-------------------\n" +
			                      $"Zoom: {zoomStr}\n" +
			                      $"Lat: {latitude:F6}\n" +
			                      $"Lon: {longitude:F6}\n" +
			                      $"Alt: {altitude:F1}m\n" +
			                      $"H.Acc: {hAccuracy:F1}m / V.Acc: {vAccuracy:F1}m\n" +
			                      $"Time: {timestamp}";

			_displayText.text = displayBuffer;
		}

		private IEnumerator StartGpsServiceCoroutine()
		{
#if UNITY_ANDROID
			if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
			{
				UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
				yield return new WaitForSeconds(1.0f);
			}
#endif

			if (!Input.location.isEnabledByUser)
			{
				UpdateUI("GPS無効", "未確定", 0, _requiredSampleCount, 0.0, 0.0, 0.0f, -1.0f, -1.0f, 0.0, "Disabled");
				yield break;
			}

			Input.location.Start(1.0f, 0.0f);

			int maxWaitSeconds = 20;
			while (Input.location.status == LocationServiceStatus.Initializing && maxWaitSeconds > 0)
			{
				UpdateUI("GPS初期化中...", "未確定", 0, _requiredSampleCount, 0.0, 0.0, 0.0f, -1.0f, -1.0f, 0.0, $"Initializing ({maxWaitSeconds})");
				yield return new WaitForSeconds(1.0f);
				maxWaitSeconds--;
			}

			if (maxWaitSeconds < 1)
			{
				UpdateUI("GPSタイムアウト", "未確定", 0, _requiredSampleCount, 0.0, 0.0, 0.0f, -1.0f, -1.0f, 0.0, "Timed Out");
				yield break;
			}

			if (Input.location.status == LocationServiceStatus.Failed)
			{
				UpdateUI("GPS取得失敗", "未確定", 0, _requiredSampleCount, 0.0, 0.0, 0.0f, -1.0f, -1.0f, 0.0, "Failed");
				yield break;
			}
		}
	}
}