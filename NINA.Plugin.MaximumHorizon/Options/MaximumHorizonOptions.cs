using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.MaximumHorizon.Models;
using NINA.Plugin.MaximumHorizon.Services;

namespace NINA.Plugin.MaximumHorizon.Options
{
    public class MaximumHorizonOptions : BaseINPC
    {
        internal readonly IMaximumHorizonService _horizonService; // Changed to internal for access

        public MaximumHorizonOptions(IMaximumHorizonService horizonService)
        {
            _horizonService = horizonService ?? throw new ArgumentNullException(nameof(horizonService));
            _horizonService.ProfilesChanged += async (_, __) => await LoadProfilesAsync();
            Task.Run(async () => await LoadProfilesAsync());
        }

        private List<string> _availableProfiles = new();
        public List<string> AvailableProfiles
        {
            get => _availableProfiles;
            private set
            {
                _availableProfiles = value;
                RaisePropertyChanged();
            }
        }

        public string SelectedProfile
        {
            get => _horizonService.SelectedProfileName;
            set
            {
                _horizonService.SelectedProfileName = value;
                NINA.Core.Utility.Logger.Debug($"MaximumHorizonOptions: SelectedProfile set to '{value}' (service now '{_horizonService.SelectedProfileName}')");
                Services.MaximumHorizonServiceAccessor.SetGlobalSelectedProfile(_horizonService.SelectedProfileName);
                RaisePropertyChanged();
                Task.Run(async () => await LoadSelectedProfileAsync());
            }
        }

        public double MarginBuffer
        {
            get => _horizonService.GlobalMarginBuffer;
            set
            {
                var clamped = Math.Max(0, Math.Min(10, value));
                if (Math.Abs(_horizonService.GlobalMarginBuffer - clamped) < 0.0001) return;
                _horizonService.GlobalMarginBuffer = clamped;
                RaisePropertyChanged();
            }
        }

        private HorizonProfile? _currentProfile;
        public HorizonProfile? CurrentProfile
        {
            get => _currentProfile;
            private set
            {
                _currentProfile = value;
                RaisePropertyChanged();
            }
        }

        private async Task LoadProfilesAsync()
        {
            try
            {
                var profiles = await _horizonService.GetAvailableProfilesAsync();
                AvailableProfiles = profiles.ToList();
                RaisePropertyChanged(nameof(AvailableProfiles));

                if (AvailableProfiles.Count == 1 && string.IsNullOrWhiteSpace(SelectedProfile))
                {
                    SelectedProfile = AvailableProfiles[0];
                }
                else if (!string.IsNullOrWhiteSpace(SelectedProfile) && !AvailableProfiles.Contains(SelectedProfile))
                {
                    SelectedProfile = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading profiles: {ex.Message}", ex);
            }
        }

        private async Task LoadSelectedProfileAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedProfile))
            {
                CurrentProfile = null;
                return;
            }

            try
            {
                CurrentProfile = await _horizonService.GetProfileAsync(SelectedProfile);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading profile {SelectedProfile}: {ex.Message}", ex);
                CurrentProfile = null;
            }
        }

        public async Task SaveCurrentProfileAsync()
        {
            if (CurrentProfile == null || string.IsNullOrWhiteSpace(CurrentProfile.Name))
            {
                return;
            }

            try
            {
                await _horizonService.SaveProfileAsync(CurrentProfile);
                await LoadProfilesAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving profile: {ex.Message}", ex);
                throw;
            }
        }

        public async Task DeleteProfileAsync(string profileName)
        {
            try
            {
                await _horizonService.DeleteProfileAsync(profileName);
                await LoadProfilesAsync();
                if (SelectedProfile == profileName)
                {
                    SelectedProfile = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting profile {profileName}: {ex.Message}", ex);
                throw;
            }
        }

        public async Task<HorizonProfile> CreateNewProfileAsync(string profileName)
        {
            var profile = new HorizonProfile(profileName);
            await _horizonService.SaveProfileAsync(profile);
            await LoadProfilesAsync();
            SelectedProfile = profileName;
            return profile;
        }

        public async Task<HorizonProfile> DuplicateProfileAsync(string sourceProfileName, string newProfileName)
        {
            var sourceProfile = await _horizonService.GetProfileAsync(sourceProfileName);
            if (sourceProfile == null)
            {
                throw new ArgumentException($"Source profile '{sourceProfileName}' not found");
            }

            var newProfile = new HorizonProfile(newProfileName)
            {
                Points = sourceProfile.Points.Select(p => new HorizonPoint(p.Azimuth, p.MaxAltitude)).ToList(),
                Description = $"Copy of {sourceProfileName}"
            };

            await _horizonService.SaveProfileAsync(newProfile);
            await LoadProfilesAsync();
            SelectedProfile = newProfileName;
            return newProfile;
        }
    }
}

