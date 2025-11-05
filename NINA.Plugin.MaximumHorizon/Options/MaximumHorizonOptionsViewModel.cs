using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WpfApplication = System.Windows.Application;
using System.Windows.Input;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Logger = NINA.Core.Utility.Logger;
using NINA.Plugin.MaximumHorizon.Models;
using NINA.Plugin.MaximumHorizon.Services;
using NINA.Plugin.MaximumHorizon.Utils;

namespace NINA.Plugin.MaximumHorizon.Options
{
    public class MaximumHorizonOptionsViewModel : NINA.Core.Utility.BaseINPC
    {
        private readonly IMaximumHorizonService _horizonService;
        private readonly CsvImporter _csvImporter;
        private readonly ImageHorizonExtractor _imageExtractor;
        private readonly MaximumHorizonOptions _options;

        public MaximumHorizonOptionsViewModel()
        {
            // Use shared service so SelectedProfileName is consistent with conditions
            _horizonService = MaximumHorizonServiceAccessor.GetShared();
            _csvImporter = new CsvImporter();
            _imageExtractor = new ImageHorizonExtractor();
            _options = new MaximumHorizonOptions(_horizonService);
            Initialize();
        }

        public MaximumHorizonOptionsViewModel(MaximumHorizonOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _horizonService = options._horizonService;
            _csvImporter = new CsvImporter();
            _imageExtractor = new ImageHorizonExtractor();
            Initialize();
        }

        private void Initialize()
        {

            AvailableProfiles = new ObservableCollection<string>();
            HorizonPoints = new ObservableCollection<HorizonPoint>();

            CreateNewProfileCommand = new AsyncRelayCommand(CreateNewProfileAsync);
            DeleteProfileCommand = new AsyncRelayCommand<string?>(DeleteProfileAsync, CanDeleteProfile);
            DuplicateProfileCommand = new AsyncRelayCommand<string?>(DuplicateProfileAsync, CanDuplicateProfile);
            SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, CanSaveProfile);
            ImportCsvCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ImportCsv);
            ImportImageCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ImportImage);
            LoadProfileCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string?>(LoadProfile);
            AddRowCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(AddRow);
            DeleteRowCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<Models.HorizonPoint>(DeleteRow);

            _options.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(_options.AvailableProfiles))
                {
                    RefreshAvailableProfiles();
                }
                if (e.PropertyName == nameof(_options.SelectedProfile))
                {
                    SelectedProfile = _options.SelectedProfile;
                }
                if (e.PropertyName == nameof(_options.CurrentProfile))
                {
                    RefreshHorizonPoints();
                }
            };

            WpfApplication.Current?.Dispatcher?.InvokeAsync(async () => await RefreshAvailableProfilesAsync());
            _horizonService.ProfilesChanged += async (sender, e) => await RefreshAvailableProfilesAsync();
        }

        private static void RunOnUIThread(Action action)
        {
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher == null)
            {
                action();
                return;
            }
            if (dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
        }

        private ObservableCollection<string> _availableProfiles = new();
        public ObservableCollection<string> AvailableProfiles
        {
            get => _availableProfiles;
            private set
            {
                _availableProfiles = value;
                RaisePropertyChanged();
            }
        }

        private string _selectedProfile = string.Empty;
        public string SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (string.Equals(_selectedProfile, value, StringComparison.Ordinal))
                {
                    return;
                }
                _selectedProfile = value;
                RaisePropertyChanged();
                if (!string.Equals(_options.SelectedProfile, value, StringComparison.Ordinal))
                {
                    _options.SelectedProfile = value;
                }
                RefreshHorizonPoints();
            }
        }

        public double MarginBuffer
        {
            get => _options.MarginBuffer;
            set
            {
                if (Math.Abs(_options.MarginBuffer - value) < 0.0001) return;
                _options.MarginBuffer = value;
                RaisePropertyChanged();
            }
        }

        private ObservableCollection<HorizonPoint> _horizonPoints = new();
        public ObservableCollection<HorizonPoint> HorizonPoints
        {
            get => _horizonPoints;
            private set
            {
                _horizonPoints = value;
                RaisePropertyChanged();
            }
        }

        private string _newProfileName = string.Empty;
        public string NewProfileName
        {
            get => _newProfileName;
            set
            {
                _newProfileName = value;
                RaisePropertyChanged();
            }
        }

        private string _csvFilePath = string.Empty;
        public string CsvFilePath
        {
            get => _csvFilePath;
            set
            {
                _csvFilePath = value;
                RaisePropertyChanged();
            }
        }

        private string _imageFilePath = string.Empty;
        public string ImageFilePath
        {
            get => _imageFilePath;
            set
            {
                _imageFilePath = value;
                RaisePropertyChanged();
            }
        }

        private int _imageThreshold = 128;
        public int ImageThreshold
        {
            get => _imageThreshold;
            set
            {
                _imageThreshold = value;
                RaisePropertyChanged();
            }
        }

        public ICommand CreateNewProfileCommand { get; private set; }
        public ICommand DeleteProfileCommand { get; private set; }
        public ICommand DuplicateProfileCommand { get; private set; }
        public ICommand SaveProfileCommand { get; private set; }
        public ICommand ImportCsvCommand { get; private set; }
        public ICommand ImportImageCommand { get; private set; }
        public ICommand LoadProfileCommand { get; private set; }
        public ICommand AddRowCommand { get; private set; }
        public ICommand DeleteRowCommand { get; private set; }

        private async Task RefreshAvailableProfilesAsync()
        {
            var profiles = await _horizonService.GetAvailableProfilesAsync();
            RunOnUIThread(() =>
            {
                AvailableProfiles = new ObservableCollection<string>(profiles);
                RaisePropertyChanged(nameof(AvailableProfiles));
                EnsureProfileSelection();
            });
        }

        private void RefreshAvailableProfiles()
        {
            RunOnUIThread(() =>
            {
                AvailableProfiles = new ObservableCollection<string>(_options.AvailableProfiles);
                RaisePropertyChanged(nameof(AvailableProfiles));
                EnsureProfileSelection();
            });
        }

        private void EnsureProfileSelection()
        {
            if (AvailableProfiles.Count == 1 && string.IsNullOrWhiteSpace(SelectedProfile))
            {
                SelectedProfile = AvailableProfiles[0];
            }
            else if (!string.IsNullOrWhiteSpace(SelectedProfile) && !AvailableProfiles.Contains(SelectedProfile))
            {
                SelectedProfile = string.Empty;
            }
        }

        private void RefreshHorizonPoints()
        {
            RunOnUIThread(() =>
            {
                if (_options.CurrentProfile != null)
                {
                    HorizonPoints = new ObservableCollection<HorizonPoint>(
                        _options.CurrentProfile.Points.OrderBy(p => p.Azimuth));
                }
                else
                {
                    HorizonPoints = new ObservableCollection<HorizonPoint>();
                }
                RaisePropertyChanged(nameof(HorizonPoints));
            });
        }

        private async Task CreateNewProfileAsync()
        {
            if (string.IsNullOrWhiteSpace(NewProfileName))
            {
                return;
            }

            try
            {
                var profile = await _options.CreateNewProfileAsync(NewProfileName);
                SelectedProfile = profile.Name;
                NewProfileName = string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error creating profile '{NewProfileName}': {ex.Message}", ex);
            }
        }

        private bool CanDeleteProfile(string? profileName)
        {
            return !string.IsNullOrWhiteSpace(profileName);
        }

        private async Task DeleteProfileAsync(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            try
            {
                await _options.DeleteProfileAsync(profileName);
                if (SelectedProfile == profileName)
                {
                    SelectedProfile = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error deleting profile '{profileName}': {ex.Message}", ex);
            }
        }

        private bool CanDuplicateProfile(string? profileName)
        {
            return !string.IsNullOrWhiteSpace(profileName);
        }

        private async Task DuplicateProfileAsync(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            try
            {
                var newName = $"{profileName} Copy";
                await _options.DuplicateProfileAsync(profileName, newName);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error duplicating profile '{profileName}': {ex.Message}", ex);
            }
        }

        private bool CanSaveProfile()
        {
            return _options.CurrentProfile != null;
        }

        private async Task SaveProfileAsync()
        {
            try
            {
                if (_options.CurrentProfile != null)
                {
                    // Update profile from UI
                    _options.CurrentProfile.Points = HorizonPoints.ToList();
                    await _options.SaveCurrentProfileAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving profile: {ex.Message}", ex);
            }
        }

        private void ImportCsv()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Select CSV File to Import"
            };

            if (dialog.ShowDialog() == true)
            {
                CsvFilePath = dialog.FileName;
                Task.Run(async () => await ImportCsvAsync());
            }
        }

        private async Task ImportCsvAsync()
        {
            try
            {
                var points = _csvImporter.ImportFromCsv(CsvFilePath);
                if (_options.CurrentProfile != null)
                {
                    _options.CurrentProfile.Points = points;
                    RefreshHorizonPoints();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error importing CSV '{CsvFilePath}': {ex.Message}", ex);
            }
        }

        private void ImportImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Select Image File to Import"
            };

            if (dialog.ShowDialog() == true)
            {
                ImageFilePath = dialog.FileName;
                Task.Run(async () => await ImportImageAsync());
            }
        }

        private async Task ImportImageAsync()
        {
            try
            {
                var points = _imageExtractor.ExtractFromImage(ImageFilePath, ImageThreshold);
                if (_options.CurrentProfile != null)
                {
                    _options.CurrentProfile.Points = points;
                    RefreshHorizonPoints();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error importing image '{ImageFilePath}': {ex.Message}", ex);
            }
        }

        private void LoadProfile(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            SelectedProfile = profileName;
        }

        private void AddRow()
        {
            RunOnUIThread(() =>
            {
                // Find the next available azimuth (or start at 0 if empty)
                int nextAzimuth = 0;
                if (HorizonPoints.Any())
                {
                    var maxAzimuth = HorizonPoints.Max(p => p.Azimuth);
                    nextAzimuth = (int)(maxAzimuth + 10) % 360; // Add 10 degrees, wrap around
                }

                var newPoint = new HorizonPoint { Azimuth = nextAzimuth, MaxAltitude = 50.0 };
                HorizonPoints.Add(newPoint);
                RaisePropertyChanged(nameof(HorizonPoints));
            });
        }

        private void DeleteRow(Models.HorizonPoint? point)
        {
            if (point == null) return;
            RunOnUIThread(() =>
            {
                HorizonPoints.Remove(point);
                RaisePropertyChanged(nameof(HorizonPoints));
                // Trigger visualization update
                RaisePropertyChanged(nameof(HorizonPoints));
            });
        }
    }
}

