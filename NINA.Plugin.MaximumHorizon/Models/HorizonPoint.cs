using System;

namespace NINA.Plugin.MaximumHorizon.Models
{
    /// <summary>
    /// Represents a single point in a horizon profile with azimuth and maximum altitude
    /// </summary>
    public class HorizonPoint
    {
        /// <summary>
        /// Azimuth angle in degrees (0-359)
        /// </summary>
        public int Azimuth { get; set; }

        /// <summary>
        /// Maximum visible altitude in degrees (0-90)
        /// </summary>
        public double MaxAltitude { get; set; }

        public HorizonPoint()
        {
        }

        public HorizonPoint(int azimuth, double maxAltitude)
        {
            Azimuth = azimuth;
            MaxAltitude = maxAltitude;
        }

        public override bool Equals(object? obj)
        {
            if (obj is HorizonPoint other)
            {
                return Azimuth == other.Azimuth && Math.Abs(MaxAltitude - other.MaxAltitude) < 0.001;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Azimuth, MaxAltitude);
        }
    }
}

