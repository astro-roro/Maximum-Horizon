using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NINA.Plugin.MaximumHorizon.Models;

namespace NINA.Plugin.MaximumHorizon.Services
{
    /// <summary>
    /// Interface for the Maximum Horizon Service that manages horizon profiles and provides visibility checking
    /// </summary>
    public interface IMaximumHorizonService
    {
        /// <summary>
        /// Get all available horizon profile names
        /// </summary>
        Task<IEnumerable<string>> GetAvailableProfilesAsync();

        /// <summary>
        /// Synchronous list of available profiles from in-memory cache
        /// </summary>
        IEnumerable<string> GetAvailableProfiles();

        /// <summary>
        /// Get a horizon profile by name
        /// </summary>
        Task<HorizonProfile?> GetProfileAsync(string profileName);

        /// <summary>
        /// Save or update a horizon profile
        /// </summary>
        Task SaveProfileAsync(HorizonProfile profile);

        /// <summary>
        /// Delete a horizon profile
        /// </summary>
        Task DeleteProfileAsync(string profileName);

        /// <summary>
        /// Check if a target at the given altitude and azimuth is visible for the specified profile
        /// </summary>
        Task<bool> IsTargetVisibleAsync(double altitude, int azimuth, string profileName);

        /// <summary>
        /// Get the maximum altitude for a given azimuth in the specified profile
        /// </summary>
        Task<double> GetMaximumAltitudeAsync(int azimuth, string profileName);

        /// <summary>
        /// Synchronous lookup of maximum altitude using in-memory cache to avoid UI thread deadlocks
        /// </summary>
        double GetMaximumAltitude(int azimuth, string profileName);

        /// <summary>
        /// Event raised when profiles are added, updated, or deleted
        /// </summary>
        event EventHandler ProfilesChanged;

        /// <summary>
        /// Event raised when global settings change (e.g., SelectedProfileName or GlobalMarginBuffer)
        /// </summary>
        event EventHandler SettingsChanged;

        /// <summary>
        /// Globally selected profile name (set via options UI)
        /// </summary>
        string SelectedProfileName { get; set; }

        /// <summary>
        /// Global margin buffer in degrees (set via options UI)
        /// </summary>
        double GlobalMarginBuffer { get; set; }

        /// <summary>
        /// Try to get a profile from in-memory cache without I/O; returns null if not present
        /// </summary>
        NINA.Plugin.MaximumHorizon.Models.HorizonProfile? TryGetCachedProfile(string profileName);

        /// <summary>
        /// Check if a target at given RA/Dec coordinates is visible at a specific time
        /// This method is designed for integration with Target Scheduler and other planning systems.
        /// It calculates Alt/Az from RA/Dec and checks against the horizon profile.
        /// </summary>
        /// <param name="raHours">Right Ascension in hours (0-24)</param>
        /// <param name="decDegrees">Declination in degrees (-90 to +90)</param>
        /// <param name="latitude">Observer latitude in degrees</param>
        /// <param name="longitude">Observer longitude in degrees</param>
        /// <param name="utcTime">Time to check visibility for (UTC)</param>
        /// <param name="profileName">Name of the horizon profile to use. If null/empty, uses SelectedProfileName</param>
        /// <returns>True if target is visible (below maximum horizon), false if blocked</returns>
        Task<bool> IsTargetVisibleAtTimeAsync(
            double raHours, 
            double decDegrees, 
            double latitude, 
            double longitude, 
            DateTime utcTime, 
            string? profileName = null);

        /// <summary>
        /// Synchronous version of IsTargetVisibleAtTimeAsync for use in planning/scoring engines
        /// </summary>
        bool IsTargetVisibleAtTime(
            double raHours,
            double decDegrees,
            double latitude,
            double longitude,
            DateTime utcTime,
            string? profileName = null);
    }
}

