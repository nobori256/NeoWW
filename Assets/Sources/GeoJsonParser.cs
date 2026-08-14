using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GeoLookup
{
	public static class GeoJsonParser
	{
		//======================================================================
		// ParseGeoJson
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. GeoJSON文字列を受け取り、JObjectとして解析する。
		// 2. 各Feature要素を順次ループ処理し、自治体リストを構築して返す。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    jsonText: GeoJSON形式の文字列
		//
		// [戻り値]
		//    List<CityFeature>: 解析・生成された自治体リスト
		//======================================================================
		public static List<CityFeature> ParseGeoJson(string jsonText)
		{
			Debug.Log("[GeoJsonParser] GeoJSONのパース処理を開始します。");

			if (string.IsNullOrEmpty(jsonText))
			{
				Debug.LogError("[GeoJsonParser] 入力されたGeoJSON文字列が空です。");
				return new List<CityFeature>();
			}

			List<CityFeature> cityList = new List<CityFeature>();

			try
			{
				JObject root = JObject.Parse(jsonText);
				JArray features = (JArray)root["features"];

				if (features == null)
				{
					Debug.LogError("[GeoJsonParser] 'features' ノードが見つかりません。");
					return cityList;
				}

				foreach (JObject feature in features)
				{
					// サブ関数へ飛ばして各Featureを解析する
					CityFeature city = ParseFeature(feature);
					if (city != null)
					{
						cityList.Add(city);
					}
				}

				Debug.Log($"[GeoJsonParser] パース完了。総自治体数: {cityList.Count}");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[GeoJsonParser] パース中に例外が発生しました: {ex.Message}");
			}

			return cityList;
		}

		//======================================================================
		// ParseFeature
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. JObject形式のFeature単体から属性情報(N03_001等)を抽出する。
		// 2. Geometryタイプ(Polygon/MultiPolygon)に応じてポリゴン抽出処理へ分配する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    feature: Feature単位のJObject
		//
		// [戻り値]
		//    CityFeature: 生成された自治体オブジェクト
		//======================================================================
		private static CityFeature ParseFeature(JObject feature)
		{
			CityFeature city = new CityFeature();
			JToken properties = feature["properties"];

			if (properties != null)
			{
				city.StateName = properties["N03_001"]?.ToString();
				city.CityName = properties["N03_004"]?.ToString();
				city.CityCode = properties["N03_007"]?.ToString();
			}

			JToken geometry = feature["geometry"];
			if (geometry == null)
			{
				return null;
			}

			string type = geometry["type"]?.ToString();
			JArray coordinates = (JArray)geometry["coordinates"];

			if (coordinates == null)
			{
				return null;
			}

			// 形状タイプに応じたサブ関数呼び出し
			if (type == "Polygon")
			{
				ParsePolygonRings(coordinates, city);
			}
			else if (type == "MultiPolygon")
			{
				foreach (JArray polyCoords in coordinates)
				{
					ParsePolygonRings(polyCoords, city);
				}
			}

			return city;
		}

		//======================================================================
		// ParsePolygonRings
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. ポリゴンを構成する各リング(外形・穴)座標配列を抽出する。
		// 2. 座標値を読み込み、同時並行でBBoxの最小・最大値を更新する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    ringsArray: リング座標群のJArray
		//    city: 登録先のCityFeatureインスタンス
		//
		// [戻り値]
		//    void
		//======================================================================
		private static void ParsePolygonRings(JArray ringsArray, CityFeature city)
		{
			PolygonData polyData = new PolygonData();

			foreach (JArray ringCoords in ringsArray)
			{
				Vector2[] points = new Vector2[ringCoords.Count];

				for (int i = 0; i < ringCoords.Count; i++)
				{
					float lon = (float)ringCoords[i][0];
					float lat = (float)ringCoords[i][1];

					points[i] = new Vector2(lon, lat);

					// 範囲の更新（サブ処理）
					polyData.UpdateBBox(lon, lat);
					city.UpdateBBox(lon, lat);
				}

				polyData.Rings.Add(points);
			}

			city.Polygons.Add(polyData);
		}
	}
}