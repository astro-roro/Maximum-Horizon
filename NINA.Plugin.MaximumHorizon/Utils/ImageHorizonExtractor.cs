using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using NINA.Core.Utility;
using NINA.Plugin.MaximumHorizon.Models;

namespace NINA.Plugin.MaximumHorizon.Utils
{
    public class ImageHorizonExtractor
    {
        /// <summary>
        /// Extract horizon profile from an image file
        /// White pixels represent visible sky, black pixels represent obstructions
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="threshold">Threshold for white/black detection (0-255, default 128)</param>
        /// <param name="imageWidth">Expected image width in pixels (default 360, 1 pixel per degree)</param>
        /// <returns>List of horizon points extracted from the image</returns>
        public List<HorizonPoint> ExtractFromImage(string imagePath, int threshold = 128, int? imageWidth = null)
        {
            var points = new List<HorizonPoint>();

            try
            {
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException($"Image file not found: {imagePath}");
                }

                using (var bitmap = new Bitmap(imagePath))
                {
                    int width = bitmap.Width;
                    int height = bitmap.Height;

                    // If imageWidth is specified, we'll scale/interpolate
                    int targetWidth = imageWidth ?? width;
                    double scaleFactor = (double)targetWidth / width;

                    Logger.Info($"Processing image: {width}x{height} pixels, target width: {targetWidth}");

                    // Process each column (azimuth)
                    for (int targetAzimuth = 0; targetAzimuth < targetWidth; targetAzimuth++)
                    {
                        // Map target azimuth to image column
                        int imageColumn = (int)(targetAzimuth / scaleFactor);
                        if (imageColumn >= width) imageColumn = width - 1;

                        // Find the highest white pixel in this column (maximum visible altitude)
                        double maxVisibleAltitude = 0;
                        bool foundWhitePixel = false;

                        for (int y = height - 1; y >= 0; y--)
                        {
                            var pixel = bitmap.GetPixel(imageColumn, y);
                            var brightness = GetBrightness(pixel);

                            if (brightness >= threshold)
                            {
                                // White/light pixel found - this is visible sky
                                // Calculate altitude: bottom of image = 0°, top = 90°
                                double altitude = 90.0 * (1.0 - (double)y / height);
                                maxVisibleAltitude = Math.Max(maxVisibleAltitude, altitude);
                                foundWhitePixel = true;
                            }
                        }

                        // If no white pixels found in column, assume fully obstructed (0 altitude)
                        // Otherwise use the maximum altitude found
                        double maxAltitude = foundWhitePixel ? maxVisibleAltitude : 0.0;

                        // Normalize azimuth to 0-359
                        int azimuth = targetAzimuth % 360;

                        points.Add(new HorizonPoint(azimuth, maxAltitude));
                    }
                }

                // Remove duplicates and sort
                points = points
                    .GroupBy(p => p.Azimuth)
                    .Select(g => g.First())
                    .OrderBy(p => p.Azimuth)
                    .ToList();

                Logger.Info($"Successfully extracted {points.Count} horizon points from image: {imagePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error extracting horizon from image {imagePath}: {ex.Message}", ex);
                throw;
            }

            return points;
        }

        /// <summary>
        /// Calculate brightness of a pixel (0-255)
        /// Uses standard luminance formula: 0.299*R + 0.587*G + 0.114*B
        /// </summary>
        private int GetBrightness(Color color)
        {
            return (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
        }

        /// <summary>
        /// Validate that the image file can be processed
        /// </summary>
        public bool ValidateImageFormat(string imagePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                if (!File.Exists(imagePath))
                {
                    errorMessage = "Image file does not exist";
                    return false;
                }

                var extension = Path.GetExtension(imagePath).ToLower();
                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp")
                {
                    errorMessage = "Unsupported image format. Supported formats: PNG, JPG, JPEG, BMP";
                    return false;
                }

                // Try to load the image
                using (var bitmap = new Bitmap(imagePath))
                {
                    if (bitmap.Width < 10 || bitmap.Height < 10)
                    {
                        errorMessage = "Image is too small. Minimum size: 10x10 pixels";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error validating image file: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Get image dimensions for preview
        /// </summary>
        public (int width, int height) GetImageDimensions(string imagePath)
        {
            try
            {
                using (var bitmap = new Bitmap(imagePath))
                {
                    return (bitmap.Width, bitmap.Height);
                }
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}

