using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NINA.Core.Utility;
using NINA.Plugin.MaximumHorizon.Models;

namespace NINA.Plugin.MaximumHorizon.Utils
{
    public class CsvImporter
    {
        /// <summary>
        /// Import horizon profile data from a CSV file
        /// </summary>
        /// <param name="filePath">Path to the CSV file</param>
        /// <returns>List of horizon points parsed from the CSV</returns>
        public List<HorizonPoint> ImportFromCsv(string filePath)
        {
            var points = new List<HorizonPoint>();

            try
            {
                var lines = File.ReadAllLines(filePath);
                if (lines.Length == 0)
                {
                    throw new InvalidDataException("CSV file is empty");
                }

                // Detect header row
                int startIndex = 0;
                var headerLine = lines[0].ToLower();
                if (headerLine.Contains("azimuth") || headerLine.Contains("angle") || headerLine.Contains("degree") ||
                    headerLine.Contains("altitude") || headerLine.Contains("height"))
                {
                    startIndex = 1; // Skip header row
                }

                // Detect column positions
                var firstDataLine = lines[startIndex].Split(',');
                int azimuthColumn = -1;
                int altitudeColumn = -1;

                // Try to detect from header if available
                if (startIndex > 0)
                {
                    var headerParts = lines[0].ToLower().Split(',');
                    for (int i = 0; i < headerParts.Length; i++)
                    {
                        var header = headerParts[i].Trim();
                        if ((header.Contains("azimuth") || header.Contains("angle") || header.Contains("degree")) && azimuthColumn == -1)
                        {
                            azimuthColumn = i;
                        }
                        if ((header.Contains("altitude") || header.Contains("height") || header.Contains("max")) && altitudeColumn == -1)
                        {
                            altitudeColumn = i;
                        }
                    }
                }

                // If not found in header, assume first two columns
                if (azimuthColumn == -1) azimuthColumn = 0;
                if (altitudeColumn == -1) altitudeColumn = 1;

                // Parse data rows
                for (int i = startIndex; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var parts = line.Split(',');
                    if (parts.Length < 2)
                    {
                        Logger.Warning($"Skipping invalid CSV line {i + 1}: {line}");
                        continue;
                    }

                    if (azimuthColumn >= parts.Length || altitudeColumn >= parts.Length)
                    {
                        Logger.Warning($"Skipping CSV line {i + 1}: insufficient columns");
                        continue;
                    }

                    try
                    {
                        var azimuthStr = parts[azimuthColumn].Trim();
                        var altitudeStr = parts[altitudeColumn].Trim();

                        if (double.TryParse(azimuthStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double azimuth) &&
                            double.TryParse(altitudeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double altitude))
                        {
                            // Normalize azimuth to 0-359
                            int azimuthInt = ((int)Math.Round(azimuth) % 360 + 360) % 360;
                            
                            // Clamp altitude to 0-90
                            altitude = Math.Max(0, Math.Min(90, altitude));

                            points.Add(new HorizonPoint(azimuthInt, altitude));
                        }
                        else
                        {
                            Logger.Warning($"Skipping CSV line {i + 1}: could not parse numbers");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error parsing CSV line {i + 1}: {ex.Message}");
                    }
                }

                // Remove duplicates (keep last occurrence)
                points = points
                    .GroupBy(p => p.Azimuth)
                    .Select(g => g.Last())
                    .OrderBy(p => p.Azimuth)
                    .ToList();

                Logger.Info($"Successfully imported {points.Count} points from CSV file: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error importing CSV file {filePath}: {ex.Message}", ex);
                throw;
            }

            return points;
        }

        /// <summary>
        /// Validate CSV file format before import
        /// </summary>
        public bool ValidateCsvFormat(string filePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                if (!File.Exists(filePath))
                {
                    errorMessage = "File does not exist";
                    return false;
                }

                var lines = File.ReadAllLines(filePath);
                if (lines.Length == 0)
                {
                    errorMessage = "CSV file is empty";
                    return false;
                }

                // Check if we can parse at least one data row
                int startIndex = 0;
                var firstLine = lines[0].ToLower();
                if (firstLine.Contains("azimuth") || firstLine.Contains("angle") || firstLine.Contains("degree"))
                {
                    startIndex = 1;
                }

                if (lines.Length <= startIndex)
                {
                    errorMessage = "CSV file contains only header row";
                    return false;
                }

                // Try to parse first data line
                var firstDataLine = lines[startIndex].Split(',');
                if (firstDataLine.Length < 2)
                {
                    errorMessage = "CSV file must have at least 2 columns";
                    return false;
                }

                if (double.TryParse(firstDataLine[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double az) &&
                    double.TryParse(firstDataLine[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double alt))
                {
                    return true;
                }

                errorMessage = "Could not parse numeric values from CSV file";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error validating CSV file: {ex.Message}";
                return false;
            }
        }
    }
}

