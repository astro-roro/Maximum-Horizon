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
            
            // Also try to load profile immediately if one is selected in the service
            // This helps with first load scenario
            var initialProfile = _horizonService.SelectedProfileName;
            if (!string.IsNullOrWhiteSpace(initialProfile))
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(100); // Small delay to ensure initialization is complete
                    if (string.IsNullOrWhiteSpace(SelectedProfile))
                    {
                        SelectedProfile = initialProfile;
                    }
                });
            }
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
                    // Only update if different to avoid circular updates
                    if (!string.Equals(SelectedProfile, _options.SelectedProfile, StringComparison.Ordinal))
                {
                    SelectedProfile = _options.SelectedProfile;
                    }
                }
                if (e.PropertyName == nameof(_options.CurrentProfile))
                {
                    RefreshHorizonPoints();
                    // Also trigger SelectedProfile change to ensure UI updates
                    RaisePropertyChanged(nameof(SelectedProfile));
                }
            };

            // Initialize profile loading synchronously where possible
            // Use InvokeAsync to ensure we're on the UI thread, but await it properly
            var initTask = WpfApplication.Current?.Dispatcher?.InvokeAsync(async () => 
            {
                await RefreshAvailableProfilesAsync();
                // Ensure selected profile is synced with options when view initializes
                // Check both the service and options for the selected profile
                var serviceSelectedProfile = _options.SelectedProfile;
                if (string.IsNullOrWhiteSpace(serviceSelectedProfile))
                {
                    // Try to get from service directly
                    serviceSelectedProfile = _horizonService.SelectedProfileName;
                }
                
                if (!string.IsNullOrWhiteSpace(serviceSelectedProfile))
                {
                    SelectedProfile = serviceSelectedProfile;
                    // Ensure the profile is loaded if CurrentProfile is null
                    if (_options.CurrentProfile == null || 
                        !string.Equals(_options.CurrentProfile.Name, serviceSelectedProfile, StringComparison.Ordinal))
                    {
                        await _options.LoadSelectedProfileAsync();
                        RefreshHorizonPoints();
                        // Trigger a property change to ensure UI updates
                        RaisePropertyChanged(nameof(HorizonPoints));
                    }
                }
            });
            
            // Store the initialization task so we can await it later if needed
            _initializationTask = initTask?.Task;
            
            // When initialization completes, ensure we trigger any necessary updates
            if (_initializationTask != null)
            {
                _initializationTask.ContinueWith(_ =>
                {
                    WpfApplication.Current?.Dispatcher?.Invoke(() =>
                    {
                        // Trigger property changes to ensure UI updates
                        RaisePropertyChanged(nameof(SelectedProfile));
                        RaisePropertyChanged(nameof(HorizonPoints));
                    });
                }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);
            }
            
            _horizonService.ProfilesChanged += async (sender, e) => await RefreshAvailableProfilesAsync();
        }

        private System.Threading.Tasks.Task? _initializationTask;

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
                // Refresh will happen automatically when CurrentProfile property changes
                // via the PropertyChanged subscription in Initialize()
                // But ensure we refresh if CurrentProfile is already set for this profile
                if (_options.CurrentProfile != null && 
                    string.Equals(_options.CurrentProfile.Name, value, StringComparison.Ordinal))
                {
                RefreshHorizonPoints();
                }
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
            // Use dispatcher to ensure we're on UI thread
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => RefreshHorizonPoints());
                return;
            }

            // We're on UI thread now
            if (_options.CurrentProfile != null && _options.CurrentProfile.Points != null && _options.CurrentProfile.Points.Count > 0)
                {
                var newPoints = new ObservableCollection<HorizonPoint>(
                        _options.CurrentProfile.Points.OrderBy(p => p.Azimuth));
                HorizonPoints = newPoints;
                }
                else
                {
                    HorizonPoints = new ObservableCollection<HorizonPoint>();
                }
                RaisePropertyChanged(nameof(HorizonPoints));
            
            // Explicitly trigger a second property change to ensure UI updates
            // Sometimes WPF needs a nudge to update bindings
            System.Threading.Tasks.Task.Delay(10).ContinueWith(_ =>
            {
                dispatcher?.Invoke(() => RaisePropertyChanged(nameof(HorizonPoints)));
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

        private async void LoadProfile(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            // Update the selected profile
            if (!string.Equals(SelectedProfile, profileName, StringComparison.Ordinal))
            {
            SelectedProfile = profileName;
            }
            
            // Ensure the profile is loaded asynchronously
            // This method is called from the view when it loads, so we need to ensure CurrentProfile is set
            try
            {
                if (_options.CurrentProfile == null || 
                    !string.Equals(_options.CurrentProfile?.Name, profileName, StringComparison.Ordinal))
                {
                    await _options.LoadSelectedProfileAsync();
                    // Refresh the horizon points after loading
                    RefreshHorizonPoints();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading profile '{profileName}': {ex.Message}", ex);
            }
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

        /// <summary>
        /// Ensures the currently selected profile is loaded when the view becomes visible.
        /// This is called when navigating back to the options view.
        /// </summary>
        public async System.Threading.Tasks.Task EnsureProfileLoadedAsync()
        {
            // Wait for initialization to complete if it's still running
            if (_initializationTask != null && !_initializationTask.IsCompleted)
            {
                try
                {
                    await _initializationTask;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error waiting for initialization: {ex.Message}", ex);
                }
            }

            // First, sync SelectedProfile from the service/options to ensure we have the right value
            // Check both the service and options
            var serviceSelectedProfile = _options.SelectedProfile;
            if (string.IsNullOrWhiteSpace(serviceSelectedProfile))
            {
                // Try to get from service directly
                serviceSelectedProfile = _horizonService.SelectedProfileName;
            }
            
            if (!string.IsNullOrWhiteSpace(serviceSelectedProfile))
            {
                if (string.IsNullOrWhiteSpace(SelectedProfile) || 
                    !string.Equals(SelectedProfile, serviceSelectedProfile, StringComparison.Ordinal))
                {
                    SelectedProfile = serviceSelectedProfile;
                }
            }

            if (string.IsNullOrWhiteSpace(SelectedProfile))
            {
                // No profile selected, clear points
                RefreshHorizonPoints();
                return;
            }

            try
            {
                // Check if we need to load the profile
                if (_options.CurrentProfile == null || 
                    !string.Equals(_options.CurrentProfile?.Name, SelectedProfile, StringComparison.Ordinal))
                {
                    // Ensure SelectedProfile is synced with options first
                    if (!string.Equals(_options.SelectedProfile, SelectedProfile, StringComparison.Ordinal))
                    {
                        _options.SelectedProfile = SelectedProfile;
                    }
                    // Load the profile and wait for it to complete
                    await _options.LoadSelectedProfileAsync();
                    // Wait a moment for property change events to propagate
                    await System.Threading.Tasks.Task.Delay(50);
                }
                
                // Always refresh the horizon points to ensure they're up to date
                RefreshHorizonPoints();
                
                // Wait a bit more to ensure the UI has updated
                await System.Threading.Tasks.Task.Delay(50);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error ensuring profile '{SelectedProfile}' is loaded: {ex.Message}", ex);
            }
        }
    }
}

