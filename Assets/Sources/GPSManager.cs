using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class GPSManager : MonoBehaviour
{
    [Header("参照設定")]
    [SerializeField] private MapViewController _mapViewController;

    [Header("GPS精度設定")]
    [SerializeField] private float _desiredAccuracyInMeters = 5.0f; // 精度(m)
    [SerializeField] private float _updateDistanceInMeters = 1.0f;  // 更新感度(m)

    private void Start()
    {
        if (_mapViewController == null)
        {
            _mapViewController = FindFirstObjectByType<MapViewController>();
        }

        StartCoroutine(InitAndStartGPSCoroutine());
    }

    private IEnumerator InitAndStartGPSCoroutine()
    {
#if UNITY_ANDROID
        // 1. Android 実行時パーミッション要求 (画面上に許可ダイアログを表示)
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Debug.Log("[GPSManager] 位置情報(FineLocation)のパーミッションを要求します...");
            Permission.RequestUserPermission(Permission.FineLocation);

            // ダイアログでユーザーが許可/拒否を選択するまで待機
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        Debug.Log("[GPSManager LOG] Android 位置情報パーミッション確認完了");
#endif

        // 2. 端末の位置情報機能(GPS)自体がオンになっているかチェック
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("[GPSManager ERROR] 端末設定の「位置情報サービス」がオフになっています。設定画面からオンにしてください。");
            yield break;
        }

        // 3. GPS サービスの起動処理
        Debug.Log("[GPSManager LOG] 位置情報サービスを開始します...");
        Input.location.Start(_desiredAccuracyInMeters, _updateDistanceInMeters);

        // 初期化完了の待機 (最大20秒)
        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1.0f);
            maxWait--;
        }

        if (maxWait < 1 || Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("[GPSManager ERROR] GPS の初期化に失敗しました。(Status: " + Input.location.status + ")");
            yield break;
        }

        Debug.Log("[GPSManager LOG] GPS 取得成功。カメラ追随を開始します。");

        // 4. 定期的に現在地を取得して MapViewController へ座標を渡すループ
        while (true)
        {
            if (Input.location.status == LocationServiceStatus.Running)
            {
                LocationInfo data = Input.location.lastData;
                if (_mapViewController != null)
                {
                    // MapViewController の座標更新メソッドを実行
                    _mapViewController.UpdateMapPosition(data.latitude, data.longitude);
                }
            }
            yield return new WaitForSeconds(1.0f);
        }
    }
}