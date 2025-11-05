using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.MaximumHorizon.Models;
using NINA.Plugin.MaximumHorizon.Utils;

namespace NINA.Plugin.MaximumHorizon.Services
{
    [Export(typeof(IMaximumHorizonService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class MaximumHorizonService : IMaximumHorizonService
    {
        private readonly string _profilesDirectory;
        private readonly Dictionary<string, HorizonProfile> _profileCache = new(StringComparer.OrdinalIgnoreCase);
        private string _selectedProfileName = string.Empty;
        private double _globalMarginBuffer = 0.0;

        public event EventHandler? ProfilesChanged;
        public event EventHandler? SettingsChanged;

        [ImportingConstructor]
        public MaximumHorizonService()
        {
            _profilesDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA",
                "Plugins",
                "MaximumHorizon",
                "Profiles"
            );

            // Ensure profiles directory exists
            Directory.CreateDirectory(_profilesDirectory);

            // Preload cache synchronously to support fast, non-blocking lookups on UI thread
            try
            {
                var files = Directory.GetFiles(_profilesDirectory, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var profile = System.Text.Json.JsonSerializer.Deserialize<HorizonProfile>(json);
                        if (profile?.Name != null)
                        {
                            _profileCache[profile.Name] = profile;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to preload profile from {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to initialize profile cache: {ex.Message}");
            }
        }

        public async Task<IEnumerable<string>> GetAvailableProfilesAsync()
        {
            try
            {
                if (!Directory.Exists(_profilesDirectory))
                {
                    return Enumerable.Empty<string>();
                }

                var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");
                var profileNames = new List<string>();

                foreach (var file in profileFiles)
                {
                    try
                    {
                        var profile = await LoadProfileFromFileAsync(file);
                        if (profile != null)
                        {
                            profileNames.Add(profile.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to load profile from {file}: {ex.Message}");
                    }
                }

                return profileNames.OrderBy(n => n);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error getting available profiles: {ex.Message}", ex);
                return Enumerable.Empty<string>();
            }
        }

        public IEnumerable<string> GetAvailableProfiles()
        {
            if (_profileCache.Count > 0)
            {
                return _profileCache.Keys.OrderBy(n => n).ToList();
            }

            try
            {
                var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");
                var names = new List<string>();
                foreach (var file in profileFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var profile = System.Text.Json.JsonSerializer.Deserialize<HorizonProfile>(json);
                        if (profile?.Name != null)
                        {
                            _profileCache[profile.Name] = profile;
                            names.Add(profile.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to read profile {file}: {ex.Message}");
                    }
                }
                return names.OrderBy(n => n).ToList();
            }
            catch (Exception ex)
            {
                Logger.Warning($"GetAvailableProfiles sync fallback failed: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public async Task<HorizonProfile?> GetProfileAsync(string profileName)
        {
            try
            {
                var filePath = GetProfileFilePath(profileName);
                if (!File.Exists(filePath))
                {
                    return null;
                }

                return await LoadProfileFromFileAsync(filePath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading profile {profileName}: {ex.Message}", ex);
                return null;
            }
        }

        public async Task SaveProfileAsync(HorizonProfile profile)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profile.Name))
                {
                    throw new ArgumentException("Profile name cannot be empty", nameof(profile));
                }

                profile.ModifiedAt = DateTime.Now;
                if (profile.CreatedAt == default)
                {
                    profile.CreatedAt = DateTime.Now;
                }

                var filePath = GetProfileFilePath(profile.Name);
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(profile, jsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                // Update cache
                _profileCache[profile.Name] = profile;

                Logger.Info($"Saved horizon profile: {profile.Name}");
                OnProfilesChanged();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving profile {profile.Name}: {ex.Message}", ex);
                throw;
            }
        }

        public async Task DeleteProfileAsync(string profileName)
        {
            try
            {
                var filePath = GetProfileFilePath(profileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Logger.Info($"Deleted horizon profile: {profileName}");
                    // Remove from cache
                    _profileCache.Remove(profileName);
                    OnProfilesChanged();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting profile {profileName}: {ex.Message}", ex);
                throw;
            }
        }

        public async Task<bool> IsTargetVisibleAsync(double altitude, int azimuth, string profileName)
        {
            var profile = await GetProfileAsync(profileName);
            if (profile == null)
            {
                Logger.Warning($"Profile {profileName} not found, assuming target is visible");
                return true; // Default to visible if profile not found
            }

            return profile.IsTargetVisible(altitude, azimuth);
        }

        public async Task<double> GetMaximumAltitudeAsync(int azimuth, string profileName)
        {
            var profile = await GetProfileAsync(profileName);
            if (profile == null)
            {
                Logger.Warning($"Profile {profileName} not found, returning default max altitude (90 degrees)");
                return 90.0; // Default: no restriction
            }

            return profile.GetMaxAltitude(azimuth);
        }

        public double GetMaximumAltitude(int azimuth, string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return 90.0;
            }

            if (_profileCache.TryGetValue(profileName, out var profile) && profile != null)
            {
                return profile.GetMaxAltitude(azimuth);
            }

            // Fallback: attempt to load synchronously once
            try
            {
                var filePath = GetProfileFilePath(profileName);
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<HorizonProfile>(json);
                    if (loaded != null)
                    {
                        _profileCache[profileName] = loaded;
                        return loaded.GetMaxAltitude(azimuth);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Synchronous profile load failed for {profileName}: {ex.Message}");
            }

            return 90.0;
        }

        public string SelectedProfileName
        {
            get => _selectedProfileName;
            set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_selectedProfileName, newValue, StringComparison.Ordinal))
                {
                    _selectedProfileName = newValue;
                    Logger.Debug($"MaximumHorizonService: SelectedProfileName set to '{_selectedProfileName}'");
                    OnSettingsChanged();
                }
            }
        }

        public double GlobalMarginBuffer
        {
            get => _globalMarginBuffer;
            set
            {
                var clamped = Math.Max(0, Math.Min(10, value));
                if (Math.Abs(_globalMarginBuffer - clamped) > 0.0001)
                {
                    _globalMarginBuffer = clamped;
                    OnSettingsChanged();
                }
            }
        }

        public HorizonProfile? TryGetCachedProfile(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return null;
            return _profileCache.TryGetValue(profileName, out var p) ? p : null;
        }

        public async Task<bool> IsTargetVisibleAtTimeAsync(
            double raHours, 
            double decDegrees, 
            double latitude, 
            double longitude, 
            DateTime utcTime, 
            string? profileName = null)
        {
            // Use provided profile or fall back to selected profile
            var effectiveProfile = profileName ?? _selectedProfileName;
            
            // Calculate Alt/Az from RA/Dec
            var (altitude, azimuth) = CoordinateConverter.ConvertRaDecToAltAz(
                raHours, 
                decDegrees, 
                latitude, 
                longitude, 
                utcTime);

            // Normalize azimuth to 0-359
            int normalizedAzimuth = (int)Math.Round(azimuth) % 360;
            if (normalizedAzimuth < 0) normalizedAzimuth += 360;

            // Check visibility using the standard method
            return await IsTargetVisibleAsync(altitude, normalizedAzimuth, effectiveProfile);
        }

        public bool IsTargetVisibleAtTime(
            double raHours,
            double decDegrees,
            double latitude,
            double longitude,
            DateTime utcTime,
            string? profileName = null)
        {
            // Use provided profile or fall back to selected profile
            var effectiveProfile = profileName ?? _selectedProfileName;

            // Calculate Alt/Az from RA/Dec
            var (altitude, azimuth) = CoordinateConverter.ConvertRaDecToAltAz(
                raHours,
                decDegrees,
                latitude,
                longitude,
                utcTime);

            // Normalize azimuth to 0-359
            int normalizedAzimuth = (int)Math.Round(azimuth) % 360;
            if (normalizedAzimuth < 0) normalizedAzimuth += 360;

            // Get maximum altitude for this azimuth (synchronous, cache-backed)
            double maxAltitude = GetMaximumAltitude(normalizedAzimuth, effectiveProfile);
            
            // Apply margin buffer
            double effectiveMaxAltitude = maxAltitude - _globalMarginBuffer;

            // Target is visible if altitude is below (or equal to) the maximum allowed
            return altitude <= effectiveMaxAltitude;
        }

        private string GetProfileFilePath(string profileName)
        {
            // Sanitize filename
            var safeFileName = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_profilesDirectory, $"{safeFileName}.json");
        }

        private async Task<HorizonProfile?> LoadProfileFromFileAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var profile = JsonSerializer.Deserialize<HorizonProfile>(json);
                return profile;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deserializing profile from {filePath}: {ex.Message}", ex);
                return null;
            }
        }

        private void OnProfilesChanged()
        {
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSettingsChanged()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

