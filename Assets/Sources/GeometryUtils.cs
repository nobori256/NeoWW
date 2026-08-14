using UnityEngine;

namespace GeoLookup
{
	public static class GeometryUtils
	{
		//======================================================================
		// ContainsBBox
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. 指定した点(lon, lat)がBBox範囲内に含まれるか評価する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    lon: 評価対象の経度
		//    lat: 評価対象の緯度
		//    minLon, maxLon, minLat, maxLat: BBox範囲
		//
		// [戻り値]
		//    bool: 範囲内であればtrue
		//======================================================================
		public static bool ContainsBBox(float lon, float lat, float minLon, float maxLon, float minLat, float maxLat)
		{
			return lon >= minLon && lon <= maxLon && lat >= minLat && lat <= maxLat;
		}

		//======================================================================
		// IsPointInPolygon
		// 26/08/12 14:21 ※JST
		//
		// [機能詳細]
		// 1. Ray-Casting (Ray-Crossing) アルゴリズムを用い、点が多角形内にあるか判定する。
		// 2. X軸方向(水平方向)へ伸ばしたレイが多角形の各辺と交差する回数の奇偶性を評価する。
		//
		//----------------------------------------------------------------------
		// [引数]
		//    point: 評価する点(x: 経度, y: 緯度)
		//    polygon: 多角形を構成する頂点配列
		//
		// [戻り値]
		//    bool: 内部にある場合true
		//======================================================================
		public static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
		{
			if (polygon == null || polygon.Length < 3)
			{
				return false;
			}

			bool inside = false;
			int j = polygon.Length - 1;

			for (int i = 0; i < polygon.Length; i++)
			{
				// 点のY座標が辺のY範囲内にあり、かつ水平レイが交差するか計算
				if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
					(point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
				{
					inside = !inside;
				}
				j = i;
			}

			return inside;
		}
	}
}