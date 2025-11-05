using System;

namespace NINA.Plugin.MaximumHorizon.Utils
{
    /// <summary>
    /// Utility class for converting between astronomical coordinate systems
    /// </summary>
    public static class CoordinateConverter
    {
        /// <summary>
        /// Convert Right Ascension and Declination to Altitude and Azimuth
        /// </summary>
        /// <param name="raHours">Right Ascension in hours (0-24)</param>
        /// <param name="decDegrees">Declination in degrees (-90 to +90)</param>
        /// <param name="latitude">Observer latitude in degrees (-90 to +90, North is positive)</param>
        /// <param name="longitude">Observer longitude in degrees (-180 to +180, East is positive)</param>
        /// <param name="utcTime">Universal Time for the calculation</param>
        /// <returns>Tuple containing (altitude in degrees, azimuth in degrees)</returns>
        public static (double altitude, double azimuth) ConvertRaDecToAltAz(
            double raHours,
            double decDegrees,
            double latitude,
            double longitude,
            DateTime utcTime)
        {
            // Convert RA from hours to degrees
            double raDegrees = raHours * 15.0;

            // Calculate Local Sidereal Time (LST)
            double lst = CalculateLocalSiderealTime(longitude, utcTime);

            // Calculate Hour Angle (HA) in degrees
            double hourAngle = NormalizeAngle(lst - raDegrees);

            // Convert to radians for calculations
            double haRad = DegreesToRadians(hourAngle);
            double decRad = DegreesToRadians(decDegrees);
            double latRad = DegreesToRadians(latitude);

            // Calculate altitude using spherical trigonometry
            double sinAlt = Math.Sin(decRad) * Math.Sin(latRad) +
                           Math.Cos(decRad) * Math.Cos(latRad) * Math.Cos(haRad);
            double altitude = RadiansToDegrees(Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinAlt))));

            // Calculate azimuth
            double cosAz = (Math.Sin(decRad) - Math.Sin(latRad) * sinAlt) /
                          (Math.Cos(latRad) * Math.Cos(DegreesToRadians(altitude)));

            // Clamp cos(azimuth) to valid range [-1, 1] to avoid numerical errors
            cosAz = Math.Max(-1.0, Math.Min(1.0, cosAz));

            double azimuth = RadiansToDegrees(Math.Acos(cosAz));

            // Determine azimuth quadrant based on hour angle
            if (Math.Sin(haRad) > 0)
            {
                // Object is west of meridian
                azimuth = 360.0 - azimuth;
            }

            // Normalize azimuth to 0-360 degrees
            azimuth = NormalizeAngle(azimuth);

            return (altitude, azimuth);
        }

        /// <summary>
        /// Calculate Local Sidereal Time (LST) in degrees
        /// </summary>
        private static double CalculateLocalSiderealTime(double longitude, DateTime utcTime)
        {
            // Calculate Julian Day
            double jd = CalculateJulianDay(utcTime);

            // Calculate days since J2000.0
            double d = jd - 2451545.0;

            // Calculate Greenwich Mean Sidereal Time (GMST) in hours
            double gmstHours = 18.697374558 + 24.06570982441908 * d;
            gmstHours = gmstHours % 24.0;
            if (gmstHours < 0) gmstHours += 24.0;

            // Convert to degrees
            double gmstDegrees = gmstHours * 15.0;

            // Add longitude to get Local Sidereal Time
            double lst = gmstDegrees + longitude;

            // Normalize to 0-360 degrees
            return NormalizeAngle(lst);
        }

        /// <summary>
        /// Calculate Julian Day from UTC DateTime
        /// </summary>
        private static double CalculateJulianDay(DateTime utcTime)
        {
            int year = utcTime.Year;
            int month = utcTime.Month;
            int day = utcTime.Day;
            double hour = utcTime.Hour + utcTime.Minute / 60.0 + utcTime.Second / 3600.0 + utcTime.Millisecond / 3600000.0;

            if (month <= 2)
            {
                year -= 1;
                month += 12;
            }

            int a = year / 100;
            int b = 2 - a + a / 4;

            double jd = Math.Floor(365.25 * (year + 4716)) +
                       Math.Floor(30.6001 * (month + 1)) +
                       day + b - 1524.5 +
                       hour / 24.0;

            return jd;
        }

        /// <summary>
        /// Normalize angle to 0-360 degrees
        /// </summary>
        private static double NormalizeAngle(double degrees)
        {
            degrees = degrees % 360.0;
            if (degrees < 0) degrees += 360.0;
            return degrees;
        }

        /// <summary>
        /// Convert degrees to radians
        /// </summary>
        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        /// <summary>
        /// Convert radians to degrees
        /// </summary>
        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }
    }
}

