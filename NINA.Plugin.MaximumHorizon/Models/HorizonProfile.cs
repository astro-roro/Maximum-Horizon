using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.MaximumHorizon.Models
{
    /// <summary>
    /// Represents a complete horizon profile with a name and altitude data for all azimuth angles
    /// </summary>
    public class HorizonProfile
    {
        /// <summary>
        /// Name of the horizon profile (e.g., "Home Setup", "Remote Site")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// List of horizon points defining the maximum altitude at each azimuth
        /// </summary>
        public List<HorizonPoint> Points { get; set; } = new List<HorizonPoint>();

        /// <summary>
        /// Optional description of the profile
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Date/time when the profile was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Date/time when the profile was last modified
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        public HorizonProfile()
        {
        }

        public HorizonProfile(string name)
        {
            Name = name;
            // Start with empty points list - user will add points manually
            Points = new List<HorizonPoint>();
        }

        /// <summary>
        /// Initialize profile with default values (90 degrees for all azimuths - no restrictions)
        /// This method is kept for backward compatibility but not used in normal profile creation
        /// </summary>
        private void InitializeDefaultProfile()
        {
            Points = new List<HorizonPoint>();
            for (int i = 0; i < 360; i++)
            {
                Points.Add(new HorizonPoint(i, 90.0));
            }
        }

        /// <summary>
        /// Get the maximum altitude for a given azimuth, with interpolation if needed
        /// </summary>
        public double GetMaxAltitude(int azimuth)
        {
            // Normalize azimuth to 0-359
            azimuth = ((azimuth % 360) + 360) % 360;

            // Find exact match
            var exactPoint = Points.FirstOrDefault(p => p.Azimuth == azimuth);
            if (exactPoint != null)
            {
                return exactPoint.MaxAltitude;
            }

            // If no exact match, interpolate between neighboring points
            var sortedPoints = Points.OrderBy(p => p.Azimuth).ToList();
            
            if (sortedPoints.Count == 0)
            {
                return 90.0; // Default: no restriction
            }

            // Find the two points to interpolate between
            HorizonPoint? lowerPoint = null;
            HorizonPoint? upperPoint = null;

            for (int i = 0; i < sortedPoints.Count; i++)
            {
                if (sortedPoints[i].Azimuth <= azimuth)
                {
                    lowerPoint = sortedPoints[i];
                }
                else
                {
                    upperPoint = sortedPoints[i];
                    break;
                }
            }

            // Handle edge cases
            if (lowerPoint == null)
            {
                // Azimuth is before all points, interpolate between last and first (wrapping around)
                // Default to 90 if no points exist
                if (sortedPoints.Count == 0)
                {
                    return 90.0;
                }
                lowerPoint = sortedPoints[sortedPoints.Count - 1];
                upperPoint = sortedPoints[0];
                int lowerAzimuth = lowerPoint.Azimuth - 360; // Wrap around backwards
                int upperAzimuth = upperPoint.Azimuth;
                int normalizedAzimuth = azimuth;

                double ratio = (normalizedAzimuth - lowerAzimuth) / (double)(upperAzimuth - lowerAzimuth);
                return lowerPoint.MaxAltitude + (upperPoint.MaxAltitude - lowerPoint.MaxAltitude) * ratio;
            }

            if (upperPoint == null)
            {
                // Azimuth is after all points, interpolate between last and first (wrapping around)
                upperPoint = sortedPoints[0];
                int lowerAzimuth = lowerPoint.Azimuth;
                int upperAzimuth = upperPoint.Azimuth + 360; // Wrap around
                int normalizedAzimuth = azimuth;

                double ratio = (normalizedAzimuth - lowerAzimuth) / (double)(upperAzimuth - lowerAzimuth);
                return lowerPoint.MaxAltitude + (upperPoint.MaxAltitude - lowerPoint.MaxAltitude) * ratio;
            }

            // Linear interpolation
            double t = (azimuth - lowerPoint.Azimuth) / (double)(upperPoint.Azimuth - lowerPoint.Azimuth);
            return lowerPoint.MaxAltitude + (upperPoint.MaxAltitude - lowerPoint.MaxAltitude) * t;
        }

        /// <summary>
        /// Check if a target at the given altitude and azimuth is visible
        /// </summary>
        public bool IsTargetVisible(double altitude, int azimuth)
        {
            double maxAltitude = GetMaxAltitude(azimuth);
            return altitude <= maxAltitude;
        }

        /// <summary>
        /// Set or update the maximum altitude for a specific azimuth
        /// </summary>
        public void SetMaxAltitude(int azimuth, double maxAltitude)
        {
            // Normalize azimuth
            azimuth = ((azimuth % 360) + 360) % 360;
            
            // Clamp maxAltitude to valid range
            maxAltitude = Math.Max(0, Math.Min(90, maxAltitude));

            var existingPoint = Points.FirstOrDefault(p => p.Azimuth == azimuth);
            if (existingPoint != null)
            {
                existingPoint.MaxAltitude = maxAltitude;
            }
            else
            {
                Points.Add(new HorizonPoint(azimuth, maxAltitude));
            }

            ModifiedAt = DateTime.Now;
        }

        /// <summary>
        /// Ensure the profile has data for all 360 degrees (fill gaps with interpolation)
        /// </summary>
        public void NormalizeProfile()
        {
            var normalizedPoints = new List<HorizonPoint>();
            for (int i = 0; i < 360; i++)
            {
                normalizedPoints.Add(new HorizonPoint(i, GetMaxAltitude(i)));
            }
            Points = normalizedPoints;
            ModifiedAt = DateTime.Now;
        }
    }
}

