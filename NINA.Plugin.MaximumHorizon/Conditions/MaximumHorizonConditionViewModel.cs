using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.MaximumHorizon.Services;

namespace NINA.Plugin.MaximumHorizon.Conditions
{
    public class MaximumHorizonConditionViewModel : BaseINPC
    {
        private readonly IMaximumHorizonService _horizonService;
        private MaximumHorizonCondition _condition;

        public MaximumHorizonConditionViewModel(MaximumHorizonCondition condition)
        {
            _condition = condition;
            _horizonService = new MaximumHorizonService();
            
            Task.Run(async () => await LoadAvailableProfilesAsync());
            
            _condition.PropertyChanged += (sender, e) => RaisePropertyChanged(e.PropertyName);
            _horizonService.ProfilesChanged += async (sender, e) => await LoadAvailableProfilesAsync();
        }

        private List<string> _availableProfiles = new List<string>();
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
            get => _condition.SelectedProfile;
            set
            {
                _condition.SelectedProfile = value;
                RaisePropertyChanged();
            }
        }

        public double MarginBuffer
        {
            get => _condition.MarginBuffer;
            set
            {
                _condition.MarginBuffer = value;
                RaisePropertyChanged();
            }
        }

        public double CurrentAltitude => _condition.CurrentAltitude;

        public double MaximumAltitude => _condition.MaximumAltitude;

        public int CurrentAzimuth => _condition.CurrentAzimuth;

        public bool IsTargetVisible => _condition.IsTargetVisible;

        private async Task LoadAvailableProfilesAsync()
        {
            try
            {
                var profiles = await _horizonService.GetAvailableProfilesAsync();
                AvailableProfiles = profiles.ToList();
                RaisePropertyChanged(nameof(AvailableProfiles));
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading horizon profiles: {ex.Message}", ex);
                AvailableProfiles = new List<string>();
            }
        }
    }
}

