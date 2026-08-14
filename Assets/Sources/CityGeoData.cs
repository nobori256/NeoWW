using System;
using System.Collections.Generic;
using UnityEngine;

namespace GeoLookup
{
	/// <summary>
	/// 多角形（ポリゴン）データ構造
	/// </summary>
	public class PolygonData
	{
		public float MinLon = float.MaxValue;
		public float MaxLon = float.MinValue;
		public float MinLat = float.MaxValue;
		public float MaxLat = float.MinValue;

		// Rings[0]: 外形線 (Outer) / Rings[1..N]: 内側の穴 (Inner holes)
		public List<Vector2[]> Rings = new List<Vector2[]>();

		//======================================================================
		// UpdateBBox
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. 点(経度・緯度)を受け取り、ポリゴンのBBox範囲を更新する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    lon: 経度(x)
		//    lat: 緯度(y)
		//
		// [戻り値]
		//    void
		//======================================================================
		public void UpdateBBox(float lon, float lat)
		{
			if (lon < MinLon) MinLon = lon;
			if (lon > MaxLon) MaxLon = lon;
			if (lat < MinLat) MinLat = lat;
			if (lat > MaxLat) MaxLat = lat;
		}
	}

	/// <summary>
	/// 自治体（Feature）データ構造
	/// </summary>
	public class CityFeature
	{
		public string StateName; // N03_001 (都道府県)
		public string CityName;  // N03_004 (市区町村)
		public string CityCode;  // N03_007 (自治体コード)

		public float MinLon = float.MaxValue;
		public float MaxLon = float.MinValue;
		public float MinLat = float.MaxValue;
		public float MaxLat = float.MinValue;

		public List<PolygonData> Polygons = new List<PolygonData>();

		//======================================================================
		// UpdateBBox
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. 点(経度・緯度)を受け取り、自治体全体のBBox範囲を更新する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    lon: 経度(x)
		//    lat: 緯度(y)
		//
		// [戻り値]
		//    void
		//======================================================================
		public void UpdateBBox(float lon, float lat)
		{
			if (lon < MinLon) MinLon = lon;
			if (lon > MaxLon) MaxLon = lon;
			if (lat < MinLat) MinLat = lat;
			if (lat > MaxLat) MaxLat = lat;
		}
	}
}