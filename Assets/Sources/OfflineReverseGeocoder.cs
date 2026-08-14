using System.Collections.Generic;
using UnityEngine;

namespace GeoLookup
{
	public class OfflineReverseGeocoder
	{
		private List<CityFeature> _cityList;

		//======================================================================
		// OfflineReverseGeocoder (コンストラクタ)
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. 与えられたGeoJSONテキストをパースし、内部リストを初期化する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    geoJsonText: GeoJSONファイル文字列
		//
		// [戻り値]
		//    なし
		//======================================================================
		public OfflineReverseGeocoder(string geoJsonText)
		{
			_cityList = GeoJsonParser.ParseGeoJson(geoJsonText);
			Debug.Log($"[OfflineReverseGeocoder] 初期化完了。初期化済みデータ数: {_cityList.Count}");
		}

		//======================================================================
		// GetCity
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. 指定した緯度・経度から所属する自治体を検索して返す。
		// 2. 自治体単位のBBoxで一次絞り込みを行う。
		// 3. 多角形単位のBBoxおよびRay-Castingで精密判定を実施する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    latitude: 緯度
		//    longitude: 経度
		//
		// [戻り値]
		//    CityFeature: 該当する自治体情報 (非該当時はnull)
		//======================================================================
		public CityFeature GetCity(double latitude, double longitude)
		{
			float lon = (float)longitude;
			float lat = (float)latitude;
			Vector2 point = new Vector2(lon, lat);

			foreach (CityFeature city in _cityList)
			{
				// 1. 自治体全体BBoxによる事前判定（一次絞り込み）
				if (!GeometryUtils.ContainsBBox(lon, lat, city.MinLon, city.MaxLon, city.MinLat, city.MaxLat))
				{
					continue;
				}

				// 2. 各ポリゴンの詳細判定（二次絞り込み）
				if (EvaluateCityPolygons(point, city))
				{
					return city; // 一致した自治体を決定
				}
			}

			return null;
		}

		//======================================================================
		// EvaluateCityPolygons
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. 対象自治体の各ポリゴン(離島・飛地含む)に対して個別判定を行う。
		// 2. 外形線内部かつ穴(ドーナツ状の空白領域)外部であるか検証する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    point: 評価対象点(x:経度, y:緯度)
		//    city: 評価対象自治体
		//
		// [戻り値]
		//    bool: 内包されている場合true
		//======================================================================
		private bool EvaluateCityPolygons(Vector2 point, CityFeature city)
		{
			foreach (PolygonData poly in city.Polygons)
			{
				// ポリゴン単位BBox判定
				if (!GeometryUtils.ContainsBBox(point.x, point.y, poly.MinLon, poly.MaxLon, poly.MinLat, poly.MaxLat))
				{
					continue;
				}

				// 外形線(Rings[0])の判定へ移動
				if (GeometryUtils.IsPointInPolygon(point, poly.Rings[0]))
				{
					// 穴判定(Rings[1..N])のサブ検証
					if (!IsPointInHoles(point, poly))
					{
						return true;
					}
				}
			}

			return false;
		}

		//======================================================================
		// IsPointInHoles
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. 多角形の内側にある「穴領域(湖など)」の中に点が含まれるか評価する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    point: 評価対象点
		//    poly: 評価対象ポリゴン
		//
		// [戻り値]
		//    bool: 穴の中に落ちている場合true
		//======================================================================
		private bool IsPointInHoles(Vector2 point, PolygonData poly)
		{
			for (int h = 1; h < poly.Rings.Count; h++)
			{
				if (GeometryUtils.IsPointInPolygon(point, poly.Rings[h]))
				{
					return true;
				}
			}
			return false;
		}
	}
}