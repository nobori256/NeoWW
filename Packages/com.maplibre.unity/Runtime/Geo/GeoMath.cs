using UnityEngine;

namespace MapLibre.Unity.Geo
{
    /// <summary>
    /// Coordinate-system-agnostic geodesy helpers shared by the 3D terrain and PLATEAU 3D Tiles layers: WGS84
    /// geodetic <-> ECEF conversion, an ENU (East-North-Up) local tangent-plane basis around a chosen origin, and
    /// Web Mercator tile/pixel math. All internal math is done in double precision; only the final Unity-facing
    /// result (<see cref="EcefToUnity"/>) is converted to <see cref="Vector3"/> (float).
    /// </summary>
    public static class GeoMath
    {
        // WGS84 ellipsoid parameters.
        private const double EarthSemiMajorAxis = 6378137.0;
        private const double EarthFlattening = 1.0 / 298.257223563;

        /// <summary>
        /// An ECEF (Earth-Centered, Earth-Fixed) coordinate in meters, stored as three doubles (no dependency on
        /// System.Numerics or Unity's single-precision Vector3, since ECEF magnitudes are on the order of 6.4e6 m
        /// and need double precision to keep local-scale (meter to sub-meter) accuracy after subtraction).
        /// </summary>
        public readonly struct EcefCoordinate
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;

            public EcefCoordinate(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static EcefCoordinate operator -(EcefCoordinate a, EcefCoordinate b)
            {
                return new EcefCoordinate(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            }
        }

        /// <summary>
        /// The East/North/Up unit vectors (expressed in ECEF) that define the local tangent-plane basis at a given
        /// geodetic origin. Used both to build the ENU frame for a point and to re-project arbitrary ECEF direction
        /// vectors (e.g. a rotated glTF tile's local axes) into ENU components.
        /// </summary>
        public readonly struct EnuFrame
        {
            public readonly EcefCoordinate East;
            public readonly EcefCoordinate North;
            public readonly EcefCoordinate Up;

            public EnuFrame(EcefCoordinate east, EcefCoordinate north, EcefCoordinate up)
            {
                East = east;
                North = north;
                Up = up;
            }
        }

        /// <summary>
        /// Converts WGS84 geodetic coordinates (degrees, degrees, meters above the ellipsoid) to ECEF meters.
        /// </summary>
        public static EcefCoordinate Wgs84ToEcef(double latitudeDegrees, double longitudeDegrees, double heightMeters)
        {
            double lat = latitudeDegrees * Mathd.Deg2Rad;
            double lon = longitudeDegrees * Mathd.Deg2Rad;
            double sinLat = System.Math.Sin(lat);
            double cosLat = System.Math.Cos(lat);
            double sinLon = System.Math.Sin(lon);
            double cosLon = System.Math.Cos(lon);

            double e2 = EarthFlattening * (2.0 - EarthFlattening);
            double primeVerticalRadius = EarthSemiMajorAxis / System.Math.Sqrt(1.0 - e2 * sinLat * sinLat);

            double x = (primeVerticalRadius + heightMeters) * cosLat * cosLon;
            double y = (primeVerticalRadius + heightMeters) * cosLat * sinLon;
            double z = (primeVerticalRadius * (1.0 - e2) + heightMeters) * sinLat;

            return new EcefCoordinate(x, y, z);
        }

        /// <summary>
        /// Builds the East/North/Up unit-vector basis (in ECEF) for the local tangent plane at the given geodetic
        /// origin. This basis is reused both for translating points into ENU and for re-projecting rotated axes
        /// (e.g. PLATEAU tile orientation) into ENU components.
        /// </summary>
        public static EnuFrame EnuBasis(double originLatitudeDegrees, double originLongitudeDegrees)
        {
            double lat = originLatitudeDegrees * Mathd.Deg2Rad;
            double lon = originLongitudeDegrees * Mathd.Deg2Rad;
            double sinLat = System.Math.Sin(lat);
            double cosLat = System.Math.Cos(lat);
            double sinLon = System.Math.Sin(lon);
            double cosLon = System.Math.Cos(lon);

            var east = new EcefCoordinate(-sinLon, cosLon, 0.0);
            var north = new EcefCoordinate(-sinLat * cosLon, -sinLat * sinLon, cosLat);
            var up = new EcefCoordinate(cosLat * cosLon, cosLat * sinLon, sinLat);

            return new EnuFrame(east, north, up);
        }

        /// <summary>
        /// Projects an ECEF coordinate (or ECEF direction vector, when used with an offset from the origin) onto
        /// the given ENU basis and returns it as a Unity <see cref="Vector3"/> with X=East, Y=Up, Z=North.
        /// </summary>
        public static Vector3 EcefToUnity(EcefCoordinate ecef, EcefCoordinate originEcef, EnuFrame basis)
        {
            EcefCoordinate offset = ecef - originEcef;
            return EcefOffsetToUnity(offset, basis);
        }

        /// <summary>
        /// Like <see cref="EcefToUnity"/>, but takes an already-computed ECEF offset (origin already subtracted, or
        /// a pure direction vector with no translation component) rather than an absolute ECEF coordinate.
        /// </summary>
        public static Vector3 EcefOffsetToUnity(EcefCoordinate ecefOffset, EnuFrame basis)
        {
            double east = Dot(ecefOffset, basis.East);
            double north = Dot(ecefOffset, basis.North);
            double up = Dot(ecefOffset, basis.Up);

            return new Vector3((float)east, (float)up, (float)north);
        }

        private static double Dot(EcefCoordinate a, EcefCoordinate b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// <summary>
        /// Web Mercator (EPSG:3857 / XYZ tile scheme) longitude-to-tile-X conversion, double precision so that
        /// sub-tile (pixel-level) positions can be derived without float rounding artifacts.
        /// </summary>
        public static double LonToTileX(double longitudeDegrees, int zoom)
        {
            double tilesPerAxis = System.Math.Pow(2.0, zoom);
            return (longitudeDegrees + 180.0) / 360.0 * tilesPerAxis;
        }

        /// <summary>
        /// Web Mercator (EPSG:3857 / XYZ tile scheme) latitude-to-tile-Y conversion, double precision.
        /// </summary>
        public static double LatToTileY(double latitudeDegrees, int zoom)
        {
            double lat = latitudeDegrees * Mathd.Deg2Rad;
            double tilesPerAxis = System.Math.Pow(2.0, zoom);
            return (1.0 - System.Math.Log(System.Math.Tan(lat) + 1.0 / System.Math.Cos(lat)) / System.Math.PI) / 2.0 * tilesPerAxis;
        }

        /// <summary>
        /// Inverse of <see cref="LonToTileX"/>: converts a (possibly fractional) tile-X coordinate back to
        /// longitude in degrees.
        /// </summary>
        public static double TileToLon(double tileX, int zoom)
        {
            double tilesPerAxis = System.Math.Pow(2.0, zoom);
            return tileX / tilesPerAxis * 360.0 - 180.0;
        }

        /// <summary>
        /// Inverse of <see cref="LatToTileY"/>: converts a (possibly fractional) tile-Y coordinate back to
        /// latitude in degrees.
        /// </summary>
        public static double TileToLat(double tileY, int zoom)
        {
            double tilesPerAxis = System.Math.Pow(2.0, zoom);
            double n = System.Math.PI - 2.0 * System.Math.PI * tileY / tilesPerAxis;
            return Mathd.Rad2Deg * System.Math.Atan(0.5 * (System.Math.Exp(n) - System.Math.Exp(-n)));
        }

        /// <summary>
        /// Minimal double-precision degree/radian constants (UnityEngine.Mathf only offers float precision, and
        /// this class needs double precision throughout for meter-level accuracy at ECEF magnitudes).
        /// </summary>
        private static class Mathd
        {
            public const double Deg2Rad = System.Math.PI / 180.0;
            public const double Rad2Deg = 180.0 / System.Math.PI;
        }
    }
}
