using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Plugin.MaximumHorizon.Utils;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using NINA.Profile.Interfaces;
using Newtonsoft.Json;
using System.Timers;
using System.ComponentModel;
using System.Windows;

namespace NINA.Plugin.MaximumHorizon.Conditions
{
    [Export(typeof(ISequenceCondition))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    [ExportMetadata("Name", "Loop until Maximum Altitude")]
    [ExportMetadata("Description", "Breaks loop when target altitude exceeds a fixed maximum altitude")]
    [ExportMetadata("Category", "Maximum Horizon")]
    [ExportMetadata("Group", "Maximum Horizon")]
    [ExportMetadata("Type", "Condition")]
    [ExportMetadata("Icon", "Mountain")]
    [JsonObject(MemberSerialization.OptIn)]
    public class SimpleMaxAltitudeCondition : SequenceCondition, IValidatable
    {
        private IDeepSkyObjectContainer? _targetContainer;
        private IProfileService? _profileService;
        private readonly System.Timers.Timer _updateTimer = new System.Timers.Timer(2000) { AutoReset = true };

        public SimpleMaxAltitudeCondition()
        {
            MaxAltitude = 50.0;
            Name = "Loop until Maximum Altitude";
            Description = "Breaks loop when target altitude exceeds a fixed maximum altitude";
            Category = "Maximum Horizon";
            _updateTimer.Elapsed += (_, __) => SafeUpdateState();
            _updateTimer.Enabled = true;
        }

        [Import(AllowDefault = true)]
        public IDeepSkyObjectContainer TargetContainer
        {
            set { _targetContainer = value; SafeUpdateState(); }
        }

        [Import(AllowDefault = true)]
        public IProfileService ProfileService
        {
            set { _profileService = value; }
        }

        private double _maxAltitude = 50.0;
        [JsonProperty]
        public double MaxAltitude
        {
            get => _maxAltitude;
            set
            {
                _maxAltitude = Math.Max(0, Math.Min(90, value));
                RaisePropertyChanged();
                SafeUpdateState();
            }
        }

        private double _currentAltitude = 0.0;
        public double CurrentAltitude
        {
            get => _currentAltitude;
            private set
            {
                _currentAltitude = value;
                RaisePropertyChanged();
            }
        }

        private bool _isTargetVisible = true;
        public bool IsTargetVisible
        {
            get => _isTargetVisible;
            private set
            {
                _isTargetVisible = value;
                RaisePropertyChanged();
            }
        }

        public override bool AllowMultiplePerSet => false;

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem)
        {
            try
            {
                double altitude;
                if (!TryGetTargetAltitude(out altitude))
                {
                    Logger.Warning("No target available for Simple Max Altitude Condition");
                    return true; // Continue loop if can't evaluate
                }

                CurrentAltitude = altitude;
                var isVisible = CurrentAltitude <= MaxAltitude;
                IsTargetVisible = isVisible;

                if (!isVisible)
                {
                    Logger.Info($"Target blocked: Altitude {CurrentAltitude:F2}° exceeds maximum {MaxAltitude:F2}°");
                }

                // Return true when visible (continue), false when blocked (break)
                return isVisible;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error checking simple max altitude condition: {ex.Message}", ex);
                return true;
            }
        }

        private bool TryGetTargetAltitude(out double altitude)
        {
            altitude = 0.0;
            try
            {
                // Try parent context first
                if (TryGetParentRaDec(out var raHours, out var decDegrees))
                {
                    var (lat, lon) = GetObserverLocation();
                    var utc = DateTime.UtcNow;
                    var (alt, az) = CoordinateConverter.ConvertRaDecToAltAz(raHours, decDegrees, lat, lon, utc);
                    altitude = alt;
                    return true;
                }

                // Try container target
                var target = _targetContainer?.Target;
                if (target != null)
                {
                    var (alt, az) = CalculateTargetAltAz(target);
                    altitude = alt;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private (double altitude, double azimuth) CalculateTargetAltAz(InputTarget target)
        {
            try
            {
                if (target == null) return (0.0, 0.0);

                var inputCoordinates = target.InputCoordinates;
                var coordinates = inputCoordinates?.Coordinates;
                if (coordinates == null) return (0.0, 0.0);

                var raHours = coordinates.RA > 24.0 ? coordinates.RA / 15.0 : coordinates.RA;
                var decDegrees = coordinates.Dec;

                var (latitude, longitude) = GetObserverLocation();
                var utcNow = DateTime.UtcNow;

                var (altitude, azimuth) = CoordinateConverter.ConvertRaDecToAltAz(
                    raHours,
                    decDegrees,
                    latitude,
                    longitude,
                    utcNow
                );

                return (altitude, azimuth);
            }
            catch
            {
                return (0.0, 0.0);
            }
        }

        private bool TryGetParentRaDec(out double raHours, out double decDegrees)
        {
            raHours = 0; decDegrees = 0;
            try
            {
                var parentPi = typeof(SequenceCondition).GetProperty("Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var parent = parentPi?.GetValue(this);
                if (parent == null) return false;

                var targetPi = parent.GetType().GetProperty("Target");
                var targetObj = targetPi?.GetValue(parent);
                if (targetObj == null) return false;

                var icProp = targetObj.GetType().GetProperty("InputCoordinates");
                var icVal = icProp?.GetValue(targetObj);
                var cProp = icVal?.GetType().GetProperty("Coordinates");
                var cVal = cProp?.GetValue(icVal);
                if (cVal == null) return false;

                var raProp = cVal.GetType().GetProperty("RA") ?? cVal.GetType().GetProperty("RightAscension");
                var decProp = cVal.GetType().GetProperty("Dec") ?? cVal.GetType().GetProperty("Declination");
                if (raProp == null || decProp == null) return false;

                var raVal = Convert.ToDouble(raProp.GetValue(cVal));
                var decVal = Convert.ToDouble(decProp.GetValue(cVal));
                raHours = raVal > 24.0 ? raVal / 15.0 : raVal;
                decDegrees = decVal;
                return true;
            }
            catch { }
            return false;
        }

        private (double latitude, double longitude) GetObserverLocation()
        {
            try
            {
                if (_profileService?.ActiveProfile?.AstrometrySettings != null)
                {
                    var astro = _profileService.ActiveProfile.AstrometrySettings;
                    return (astro.Latitude, astro.Longitude);
                }
            }
            catch { }
            Logger.Warning("Could not determine observer location, using default (0, 0)");
            return (0.0, 0.0);
        }

        private void SafeUpdateState()
        {
            try
            {
                if (!TryGetTargetAltitude(out var altitude))
                {
                    return;
                }

                var isVisible = altitude <= MaxAltitude;

                void apply()
                {
                    CurrentAltitude = altitude;
                    IsTargetVisible = isVisible;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(apply);
                }
                else
                {
                    apply();
                }
            }
            catch { }
        }

        public override object Clone()
        {
            return new SimpleMaxAltitudeCondition
            {
                MaxAltitude = MaxAltitude
            };
        }

        public IList<string> Issues { get; private set; } = new List<string>();

        public bool Validate()
        {
            var issues = new List<string>();
            if (MaxAltitude < 0 || MaxAltitude > 90)
            {
                issues.Add("Maximum altitude must be between 0 and 90 degrees");
            }
            Issues = issues;
            RaisePropertyChanged(nameof(Issues));
            return !issues.Any();
        }
    }
}

