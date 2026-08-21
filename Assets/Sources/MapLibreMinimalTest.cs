using System.Collections;
using UnityEngine;
using MapLibre.Unity;

public class MapLibreMinimalTest : MonoBehaviour
{
    [SerializeField] private MapLibreMapView _mapView;

//======================================================================
	// Start
	// 26/08/14 17:05 ※JST
	//
	// [機能詳細]
	// 1. UIの確定を待機し、RectTransform の異常サイズを検知・補正する。
	// 2. リフレクションを用いて MapLibreMapView 内部のスケール値や解像度キャッシュを検査する。
	// 3. 不正値（0やNaN）が検出された場合、ネイティブクラッシュを防ぐため安全な値へ強制上書きする。
	// 4. パラメータが担保された状態でコンポーネントを有効化し、テクスチャ生成を監視する。
	//
	//----------------------------------------------------------------------
	// [引数]
	//    なし
	//
	// [戻り値]
	//    {IEnumerator}
	//======================================================================
	private IEnumerator Start()
	{
		Debug.Log("=== [MINIMAL TEST START (V3: Reflection Guard)] ===");

		if (_mapView == null)
		{
			_mapView = GetComponent<MapLibreMapView>();
		}
		if (_mapView == null)
		{
			Debug.LogError("[MINIMAL TEST] MapLibreMapView が見つかりません。");
			yield break;
		}

		yield return null;

		// 1. RectTransform のサイズ補正
		RectTransform rt = _mapView.GetComponent<RectTransform>();
		if (rt != null)
		{
			float w = rt.rect.width;
			float h = rt.rect.height;
			Debug.Log($"[MINIMAL TEST] 現在の UI サイズ: {w} x {h}");

			if (w <= 0 || h <= 0 || w > 8192 || h > 8192)
			{
				Debug.LogWarning($"[MINIMAL TEST] UIサイズが異常なため、512x512 を強制適用します。");
				rt.sizeDelta = new Vector2(512, 512);
				UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
				yield return null;
			}
		}

		// 2. リフレクションによる内部パラメータの強制検査と上書き
		var type = _mapView.GetType();
		var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

		string[] scaleNames = { "scaleFactor", "pixelRatio", "_scaleFactor", "_pixelRatio", "Scale", "PixelRatio" };
		foreach (var sName in scaleNames)
		{
			var field = type.GetField(sName, flags);
			if (field != null)
			{
				double val = System.Convert.ToDouble(field.GetValue(_mapView));
				Debug.Log($"[MINIMAL TEST] 検出された {sName}: {val}");
				if (val <= 0.0 || double.IsNaN(val))
				{
					Debug.LogWarning($"[MINIMAL TEST] {sName} の値が不正なため、強制的に 2.0 を設定します。");
					if (field.FieldType == typeof(float)) field.SetValue(_mapView, 2.0f);
					else if (field.FieldType == typeof(double)) field.SetValue(_mapView, 2.0);
				}
			}
		}

		string[] sizeNames = { "width", "_width", "height", "_height" };
		foreach (var sizeName in sizeNames)
		{
			var field = type.GetField(sizeName, flags);
			if (field != null)
			{
				int val = System.Convert.ToInt32(field.GetValue(_mapView));
				if (val <= 0)
				{
					Debug.LogWarning($"[MINIMAL TEST] 内部変数 {sizeName} が {val} のため、512 に強制設定します。");
					field.SetValue(_mapView, 512);
				}
			}
		}

		// 3. 安全状態での起動
		_mapView.enabled = true;
		Debug.Log($"[MINIMAL TEST] 強制有効化実行後 -> MapView Enabled: {_mapView.enabled}, GameObject Active: {_mapView.gameObject.activeInHierarchy}");

		// 4. 結果監視
		for (int i = 1; i <= 10; i++)
		{
			yield return new WaitForSeconds(0.5f);
			Texture tex = _mapView.Texture;
			if (tex != null)
			{
				Debug.Log($"<color=#00FF00>[MINIMAL TEST SUCCESS] {i * 0.5f}秒後にテクスチャが生成されました！ Size: {tex.width}x{tex.height}</color>");
				yield break;
			}
			Debug.Log($"[MINIMAL TEST] 経過 {i * 0.5f}秒: Texture は Null です");
		}
		Debug.LogError("<color=#FF0000>[MINIMAL TEST FAILED] 5秒経過しても Texture が生成されませんでした。</color>");
	}
}