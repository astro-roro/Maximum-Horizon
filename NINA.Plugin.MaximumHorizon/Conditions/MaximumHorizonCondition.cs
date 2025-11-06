using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Plugin.MaximumHorizon.Models;
using NINA.Plugin.MaximumHorizon.Services;
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
    [ExportMetadata("Name", "Loop until Maximum Horizon")]
    [ExportMetadata("Description", "Blocks when target altitude exceeds maximum altitude profile")]
    [ExportMetadata("Category", "Maximum Horizon")]
    [ExportMetadata("Group", "Maximum Horizon")]
    [ExportMetadata("Type", "Condition")]
    // Do not export as ISequenceItem to avoid contract mismatch with host types
    [ExportMetadata("Icon", "Mountain")] // optional icon hint
    [JsonObject(MemberSerialization.OptIn)]
    public class MaximumHorizonCondition : SequenceCondition, IValidatable
    {
        private const bool VerboseLogging = false;
        private IMaximumHorizonService _horizonService;
        private string _effectiveProfileName = string.Empty;
        private IDeepSkyObjectContainer? _targetContainer;
        private INotifyPropertyChanged? _containerNotify;
        private INotifyPropertyChanged? _targetNotify;
        private IProfileService? _profileService;
        private readonly System.Timers.Timer _updateTimer = new System.Timers.Timer(2000) { AutoReset = true };
        private string _lastKnownServiceProfile = string.Empty; // Track service profile to detect changes

        // Prefer MEF-injected shared service; fall back is set later if MEF fails
        [ImportingConstructor]
        public MaximumHorizonCondition([Import(AllowDefault = true)] IMaximumHorizonService? horizonService = null)
        {
            // Always use the shared service instance to ensure consistency
            _horizonService = horizonService ?? MaximumHorizonServiceAccessor.GetShared();
            
            // If we got a different instance from MEF, sync it to shared and use shared
            if (horizonService != null && horizonService != _horizonService)
            {
                var shared = MaximumHorizonServiceAccessor.GetShared();
                if (!string.IsNullOrWhiteSpace(horizonService.SelectedProfileName) &&
                    !string.Equals(shared.SelectedProfileName, horizonService.SelectedProfileName, StringComparison.Ordinal))
                {
                    shared.SelectedProfileName = horizonService.SelectedProfileName;
                }
                _horizonService = shared;
            }
            
            // Subscribe to events - use the shared service instance
            _horizonService.ProfilesChanged += async (_, __) => await LoadAvailableProfilesAsync();
            _horizonService.SettingsChanged += OnSettingsChanged;
            
            SelectedProfile = string.Empty;
            MarginBuffer = 0.0;
            Name = "Maximum Horizon Check";
            Description = "Blocks sequence execution when target exceeds maximum altitude constraints";
            Category = "Maximum Horizon";
            
            // Initialize tracking of service profile
            var currentServiceProfile = _horizonService.SelectedProfileName ?? string.Empty;
            _lastKnownServiceProfile = currentServiceProfile;
            
            // If condition was deserialized with a SelectedProfile that matches the current service value,
            // treat it as "following" the service and clear it so it will continue following changes
            if (!string.IsNullOrWhiteSpace(SelectedProfile) && 
                string.Equals(SelectedProfile, currentServiceProfile, StringComparison.Ordinal))
            {
                SelectedProfile = string.Empty;
            }
            
            // Initialize EffectiveProfileName from service to ensure UI shows correct state
            var initialEffectiveProfile = !string.IsNullOrWhiteSpace(SelectedProfile)
                ? SelectedProfile
                : (!string.IsNullOrWhiteSpace(currentServiceProfile)
                    ? currentServiceProfile
                    : Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile());
            EffectiveProfileName = initialEffectiveProfile;
            
            _ = LoadAvailableProfilesAsync();
            _updateTimer.Elapsed += (_, __) => SafeUpdateState();
            _updateTimer.Enabled = true;
        }

        // Optional property injections; MEF will set these when available
        [Import(AllowDefault = true)]
        public IMaximumHorizonService HorizonService
        {
            set
            {
                if (value != null)
                {
                    // Always use the shared singleton service; synchronize any injected state into it
                    var shared = MaximumHorizonServiceAccessor.GetShared();
                    if (!string.IsNullOrWhiteSpace(value.SelectedProfileName) &&
                        !string.Equals(shared.SelectedProfileName, value.SelectedProfileName, StringComparison.Ordinal))
                    {
                        shared.SelectedProfileName = value.SelectedProfileName;
                    }
                    _horizonService = shared;
                    _horizonService.ProfilesChanged += async (_, __) => await LoadAvailableProfilesAsync();
                    _horizonService.SettingsChanged += OnSettingsChanged;
                    
                    // Initialize tracking of service profile
                    _lastKnownServiceProfile = _horizonService.SelectedProfileName ?? string.Empty;
                    
                    // Sync EffectiveProfileName with service when service is set
                    var effectiveProfile = !string.IsNullOrWhiteSpace(SelectedProfile)
                        ? SelectedProfile
                        : (!string.IsNullOrWhiteSpace(_horizonService.SelectedProfileName)
                            ? _horizonService.SelectedProfileName
                            : Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile());
                    EffectiveProfileName = effectiveProfile;
                    
                    _ = LoadAvailableProfilesAsync();
                }
            }
        }

        [Import(AllowDefault = true)]
        public IDeepSkyObjectContainer TargetContainer
        {
            set
            {
                // Unsubscribe old
                if (_containerNotify != null)
                {
                    _containerNotify.PropertyChanged -= OnContainerPropertyChanged;
                    _containerNotify = null;
                }
                if (_targetNotify != null)
                {
                    _targetNotify.PropertyChanged -= OnTargetPropertyChanged;
                    _targetNotify = null;
                }

                _targetContainer = value;
                Logger.Info($"MaximumHorizonCondition: TargetContainer set to '{_targetContainer?.GetType().FullName ?? "(null)"}'");

                // Subscribe new
                if (_targetContainer is INotifyPropertyChanged npc)
                {
                    _containerNotify = npc;
                    _containerNotify.PropertyChanged += OnContainerPropertyChanged;
                }
                var tgt = _targetContainer?.Target as INotifyPropertyChanged;
                if (tgt != null)
                {
                    _targetNotify = tgt;
                    _targetNotify.PropertyChanged += OnTargetPropertyChanged;
                }

                SafeUpdateState();
            }
        }

        [Import(AllowDefault = true)]
        public IProfileService ProfileService
        {
            set { _profileService = value; }
        }

        // No direct dependency on plugin class; use shared service for global settings

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

        private async Task LoadAvailableProfilesAsync()
        {
            try
            {
                var profiles = await _horizonService.GetAvailableProfilesAsync();
                AvailableProfiles = profiles.ToList();
                RaisePropertyChanged(nameof(AvailableProfiles));

                // If condition has a SelectedProfile that no longer exists, clear it and use service's selection
                if (!string.IsNullOrWhiteSpace(SelectedProfile) && !AvailableProfiles.Contains(SelectedProfile))
                {
                    SelectedProfile = string.Empty;
                }

                // Get the current service selected profile (this is the source of truth)
                var serviceSelectedProfile = _horizonService?.SelectedProfileName ?? string.Empty;
                var globalSelectedProfile = Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile();
                var resolvedServiceProfile = !string.IsNullOrWhiteSpace(serviceSelectedProfile) 
                    ? serviceSelectedProfile 
                    : globalSelectedProfile;
                
                // Only auto-select first profile if there's exactly one, nothing is selected in condition,
                // AND the service also has nothing selected (don't override service selection)
                if (AvailableProfiles.Count == 1 && 
                    string.IsNullOrWhiteSpace(SelectedProfile) && 
                    string.IsNullOrWhiteSpace(resolvedServiceProfile))
                {
                    SelectedProfile = AvailableProfiles[0];
                }
                // If service has a selection but condition doesn't, ensure condition follows service
                else if (string.IsNullOrWhiteSpace(SelectedProfile) && !string.IsNullOrWhiteSpace(resolvedServiceProfile))
                {
                    // Condition should follow service - don't set SelectedProfile, let it use service value
                }
                
                // Sync EffectiveProfileName after profiles are loaded
                var effectiveProfile = !string.IsNullOrWhiteSpace(SelectedProfile)
                    ? SelectedProfile
                    : resolvedServiceProfile;
                EffectiveProfileName = effectiveProfile;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not load available horizon profiles: {ex.Message}");
            }
        }

        /// <summary>
        /// Get observer location (latitude, longitude) from NINA
        /// </summary>
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
            catch (Exception ex)
            {
                Logger.Warning($"Could not get observer location from profile service: {ex.Message}");
            }

            // Default fallback - log warning and return 0,0
            Logger.Warning("Could not determine observer location, using default (0, 0). Altitude/Azimuth calculations may be incorrect.");
            return (0.0, 0.0);
        }

        /// <summary>
        /// Calculate altitude and azimuth from target coordinates
        /// </summary>
        private (double altitude, double azimuth) CalculateTargetAltAz(InputTarget target)
        {
            try
            {
                if (target == null)
                {
                    return (0.0, 0.0);
                }

                // Try to retrieve coordinates from the parent instruction context (like NINA's AltitudeCondition)
                try
                {
                    if (TryGetParentRaDec(out var raParentHours, out var decParentDeg))
                    {
                        var (latP, lonP) = GetObserverLocation();
                        var utcP = DateTime.UtcNow;
                        var (altP, azP) = CoordinateConverter.ConvertRaDecToAltAz(raParentHours, decParentDeg, latP, lonP, utcP);
                        var aznP = azP % 360.0; if (aznP < 0) aznP += 360.0;
                        return (altP, aznP);
                    }
                }
                catch { /* ignore and continue */ }

                // Try to leverage container helper methods similar to NINA's built-in AltitudeCondition
                try
                {
                    var c = _targetContainer;
                    var cType = c?.GetType();
                    if (cType != null)
                    {
                        double? altFromMethod = null;
                        double? azFromMethod = null;

                        double? TryInvokeAltitude()
                        {
                            foreach (var name in new[] { "GetTargetAltitude", "GetCurrentAltitude", "GetAltitude", "CalculateAltitude", "ComputeAltitude" })
                            {
                                var mi = cType.GetMethod(name, Type.EmptyTypes);
                                if (mi != null && mi.ReturnType == typeof(double))
                                {
                                    try { return (double)mi.Invoke(c, null); } catch { }
                                }
                            }
                            return null;
                        }

                        double? TryInvokeAzimuth()
                        {
                            foreach (var name in new[] { "GetTargetAzimuth", "GetCurrentAzimuth", "GetAzimuth", "CalculateAzimuth", "ComputeAzimuth" })
                            {
                                var mi = cType.GetMethod(name, Type.EmptyTypes);
                                if (mi != null && mi.ReturnType == typeof(double))
                                {
                                    try { return (double)mi.Invoke(c, null); } catch { }
                                }
                            }
                            return null;
                        }

                        altFromMethod = TryInvokeAltitude();
                        azFromMethod = TryInvokeAzimuth();
                        if (altFromMethod.HasValue && azFromMethod.HasValue)
                        {
                            var azn = azFromMethod.Value % 360.0; if (azn < 0) azn += 360.0;
                            return (altFromMethod.Value, azn);
                        }
                    }
                }
                catch { /* ignore and continue */ }

                // Prefer direct Alt/Az from the parent deep sky object (if exposed by host)
                try
                {
                    var tType = target.GetType();
                    var altProp = tType.GetProperty("Altitude") ?? tType.GetProperty("Alt") ?? tType.GetProperty("CurrentAltitude");
                    var azProp = tType.GetProperty("Azimuth") ?? tType.GetProperty("Az") ?? tType.GetProperty("CurrentAzimuth");
                    if (altProp != null && azProp != null)
                    {
                        var altVal = Convert.ToDouble(altProp.GetValue(target));
                        var azVal = Convert.ToDouble(azProp.GetValue(target));
                        var normAz = azVal % 360.0; if (normAz < 0) normAz += 360.0;
                        return (altVal, normAz);
                    }
                }
                catch { /* ignore and fall back to RA/Dec */ }

                // Try container-level Alt/Az properties
                try
                {
                    var c = _targetContainer;
                    var cType = c?.GetType();
                    if (cType != null)
                    {
                        var altProp2 = cType.GetProperty("Altitude") ?? cType.GetProperty("Alt") ?? cType.GetProperty("CurrentAltitude");
                        var azProp2 = cType.GetProperty("Azimuth") ?? cType.GetProperty("Az") ?? cType.GetProperty("CurrentAzimuth");
                        if (altProp2 != null && azProp2 != null)
                        {
                            var altVal2 = Convert.ToDouble(altProp2.GetValue(c));
                            var azVal2 = Convert.ToDouble(azProp2.GetValue(c));
                            var normAz2 = azVal2 % 360.0; if (normAz2 < 0) normAz2 += 360.0;
                            return (altVal2, normAz2);
                        }
                    }
                }
                catch { /* ignore */ }

                // Helper local funcs to read HMS/DMS components by common names
                static bool TryGetDouble(object source, string[] names, out double value)
                {
                    foreach (var n in names)
                    {
                        var pi = source.GetType().GetProperty(n);
                        if (pi != null)
                        {
                            var v = pi.GetValue(source);
                            if (v == null) continue;
                            try
                            {
                                value = Convert.ToDouble(v);
                                return true;
                            }
                            catch { }
                        }
                    }
                    value = 0;
                    return false;
                }

                static bool TryGetString(object source, string[] names, out string text)
                {
                    foreach (var n in names)
                    {
                        var pi = source.GetType().GetProperty(n);
                        if (pi != null)
                        {
                            var v = pi.GetValue(source);
                            if (v == null) continue;
                            text = v.ToString() ?? string.Empty;
                            return true;
                        }
                    }
                    text = string.Empty;
                    return false;
                }

                bool TryGetRaHoursFromHms(object source, out double raHours)
                {
                    // RA h m s components
                    double h = 0, m = 0, s = 0;
                    var found = false;
                    found |= TryGetDouble(source, new[] { "RAHours", "RA_Hours", "RA_H", "RAh", "RAhour", "RA_Hour" }, out h);
                    found |= TryGetDouble(source, new[] { "RAMinutes", "RA_Minutes", "RA_M", "RAm", "RAminute", "RA_Minute" }, out m);
                    found |= TryGetDouble(source, new[] { "RASeconds", "RA_Seconds", "RA_S", "RAs", "RAsecond", "RA_Second" }, out s);
                    if (!found)
                    {
                        // Sometimes provided as a single string "hh:mm:ss"
                        if (TryGetString(source, new[] { "RAHMS", "RAString", "RAHms", "RA_HMS" }, out var ras) && !string.IsNullOrWhiteSpace(ras))
                        {
                            var parts = ras.Split(':');
                            if (parts.Length >= 1) double.TryParse(parts[0], out h);
                            if (parts.Length >= 2) double.TryParse(parts[1], out m);
                            if (parts.Length >= 3) double.TryParse(parts[2], out s);
                            raHours = Math.Abs(h) + (m / 60.0) + (s / 3600.0);
                            return true;
                        }
                        raHours = 0;
                        return false;
                    }
                    raHours = Math.Abs(h) + (m / 60.0) + (s / 3600.0);
                    return true;
                }

                bool TryGetDecDegFromDms(object source, out double decDegrees)
                {
                    double d = 0, m = 0, s = 0;
                    var any = false;
                    any |= TryGetDouble(source, new[] { "DecDegrees", "Dec_Degrees", "Dec_D", "Decd", "DeclinationDegrees", "DecDegree" }, out d);
                    any |= TryGetDouble(source, new[] { "DecMinutes", "Dec_Minutes", "Dec_M", "Decm", "DeclinationMinutes", "DecMinute" }, out m);
                    any |= TryGetDouble(source, new[] { "DecSeconds", "Dec_Seconds", "Dec_S", "Decs", "DeclinationSeconds", "DecSecond" }, out s);
                    int sign = 1;
                    // Read explicit sign if available
                    if (TryGetString(source, new[] { "DecSign", "DeclinationSign" }, out var sgn) && !string.IsNullOrWhiteSpace(sgn))
                    {
                        if (sgn.Trim().StartsWith("-")) sign = -1;
                    }
                    var signBoolPi = source.GetType().GetProperty("DecNegative") ?? source.GetType().GetProperty("DeclinationNegative");
                    if (signBoolPi != null)
                    {
                        try { if (Convert.ToBoolean(signBoolPi.GetValue(source))) sign = -1; } catch { }
                    }
                    if (!any)
                    {
                        if (TryGetString(source, new[] { "DecDMS", "DecString", "DecDms", "Dec_DMS" }, out var decs) && !string.IsNullOrWhiteSpace(decs))
                        {
                            var parts = decs.Split(':');
                            if (parts.Length >= 1) double.TryParse(parts[0], out d);
                            if (parts.Length >= 2) double.TryParse(parts[1], out m);
                            if (parts.Length >= 3) double.TryParse(parts[2], out s);
                            decDegrees = sign * (Math.Abs(d) + (m / 60.0) + (s / 3600.0));
                            return true;
                        }
                        decDegrees = 0;
                        return false;
                    }
                    decDegrees = sign * (Math.Abs(d) + (m / 60.0) + (s / 3600.0));
                    return true;
                }

                // Try RA h m s / Dec d m s from target then container
                if (TryGetRaHoursFromHms(target, out var raHh) && TryGetDecDegFromDms(target, out var decDd))
                {
                    var (lat1, lon1) = GetObserverLocation();
                    var utc1 = DateTime.UtcNow;
                    var (alt1, az1) = CoordinateConverter.ConvertRaDecToAltAz(raHh, decDd, lat1, lon1, utc1);
                    var azn1 = az1 % 360.0; if (azn1 < 0) azn1 += 360.0;
                    return (alt1, azn1);
                }
                if (_targetContainer != null && TryGetRaHoursFromHms(_targetContainer, out var raHh2) && TryGetDecDegFromDms(_targetContainer, out var decDd2))
                {
                    var (lat2, lon2) = GetObserverLocation();
                    var utc2 = DateTime.UtcNow;
                    var (alt2, az2) = CoordinateConverter.ConvertRaDecToAltAz(raHh2, decDd2, lat2, lon2, utc2);
                    var azn2 = az2 % 360.0; if (azn2 < 0) azn2 += 360.0;
                    return (alt2, azn2);
                }

                var inputCoordinates = target.InputCoordinates;
                var coordinates = inputCoordinates?.Coordinates;
                // Fallbacks for different API shapes
                if (coordinates == null)
                {
                    try
                    {
                        // Try direct Coordinates property if available
                        var directCoordsProp = target.GetType().GetProperty("Coordinates") ?? target.GetType().GetProperty("CurrentCoordinates") ?? target.GetType().GetProperty("SkyCoordinates");
                        coordinates = directCoordsProp?.GetValue(target) as Coordinates;
                    }
                    catch { /* ignore */ }
                }
                
                // Try generic RA/Dec on target or container if Coordinates unavailable
                if (coordinates == null)
                {
                    try
                    {
                        double? raGen = null, decGen = null;
                        var tType = target.GetType();
                        var raPT = tType.GetProperty("RA") ?? tType.GetProperty("RightAscension") ?? tType.GetProperty("RightAscensionHours") ?? tType.GetProperty("RAHours");
                        var decPT = tType.GetProperty("Dec") ?? tType.GetProperty("Declination") ?? tType.GetProperty("DecDegrees") ?? tType.GetProperty("DeclinationDegrees");
                        if (raPT != null) raGen = Convert.ToDouble(raPT.GetValue(target));
                        if (decPT != null) decGen = Convert.ToDouble(decPT.GetValue(target));
                        if ((!raGen.HasValue || !decGen.HasValue) && _targetContainer != null)
                        {
                            var cType = _targetContainer.GetType();
                            var raPC = cType.GetProperty("RA") ?? cType.GetProperty("RightAscension") ?? cType.GetProperty("RightAscensionHours") ?? cType.GetProperty("RAHours");
                            var decPC = cType.GetProperty("Dec") ?? cType.GetProperty("Declination") ?? cType.GetProperty("DecDegrees") ?? cType.GetProperty("DeclinationDegrees");
                            if (raPC != null) raGen ??= Convert.ToDouble(raPC.GetValue(_targetContainer));
                            if (decPC != null) decGen ??= Convert.ToDouble(decPC.GetValue(_targetContainer));
                        }
                        if (raGen.HasValue && decGen.HasValue)
                        {
                            var raHoursGen = raGen.Value > 24.0 ? raGen.Value / 15.0 : raGen.Value;
                            var (latG, lonG) = GetObserverLocation();
                            var utcNowG = DateTime.UtcNow;
                            var (altG, azG) = CoordinateConverter.ConvertRaDecToAltAz(raHoursGen, decGen.Value, latG, lonG, utcNowG);
                            var azNormG = azG % 360.0; if (azNormG < 0) azNormG += 360.0;
                            return (altG, azNormG);
                        }
                    }
                    catch { /* ignore */ }
                }
                if (coordinates == null)
                {
                    Logger.Warning("Target coordinates are null");
                    return (0.0, 0.0);
                }

                // Get RA in hours and Dec in degrees (try common property names)
                double raHours;
                double decDegrees;
                try
                {
                    // RA can be in hours or degrees depending on host; detect
                    var raCandidate = coordinates.RA;
                    raHours = raCandidate > 24.0 ? raCandidate / 15.0 : raCandidate;
                    decDegrees = coordinates.Dec; // degrees
                }
                catch
                {
                    // Reflection fallback if API differs
                    var raProp = coordinates.GetType().GetProperty("RA") ?? coordinates.GetType().GetProperty("RightAscension");
                    var decProp = coordinates.GetType().GetProperty("Dec") ?? coordinates.GetType().GetProperty("Declination");
                    var raVal = raProp != null ? Convert.ToDouble(raProp.GetValue(coordinates)) : 0.0;
                    raHours = raVal > 24.0 ? raVal / 15.0 : raVal;
                    decDegrees = decProp != null ? Convert.ToDouble(decProp.GetValue(coordinates)) : 0.0;
                }

                // Get observer location
                var (latitude, longitude) = GetObserverLocation();

                // Get current UTC time
                DateTime utcNow = DateTime.UtcNow;

                // Calculate Alt/Az
                var (altitude, azimuth) = CoordinateConverter.ConvertRaDecToAltAz(
                    raHours,
                    decDegrees,
                    latitude,
                    longitude,
                    utcNow
                );

                // Normalize azimuth to [0, 360)
                var az = azimuth % 360.0;
                if (az < 0) az += 360.0;
                return (altitude, az);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error calculating target Alt/Az: {ex.Message}", ex);
                return (0.0, 0.0);
            }
        }

        private bool TryGetParentRaDec(out double raHours, out double decDegrees)
        {
            raHours = 0; decDegrees = 0;
            try
            {
                // Access this.Parent (SequenceItem.Parent) via reflection to avoid tight coupling
                var parentPi = typeof(SequenceCondition).GetProperty("Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var parent = parentPi?.GetValue(this);
                if (parent == null)
                {
                    return false;
                }

                // Try method RetrieveContextCoordinates() if present
                var m = parent.GetType().GetMethod("RetrieveContextCoordinates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                object? context = null;
                try { context = m?.Invoke(parent, new object[] { parent }); } catch { }

                // If method signature differs, try parameterless
                if (context == null)
                {
                    try { context = m?.Invoke(parent, Array.Empty<object>()); } catch { }
                }

                // The example shows .Coordinates.Coordinates; walk that chain flexibly
                object? coordsHolder = context ?? parent;
                for (int i = 0; i < 3 && coordsHolder != null; i++)
                {
                    var pi = coordsHolder.GetType().GetProperty("Coordinates");
                    if (pi == null) break;
                    coordsHolder = pi.GetValue(coordsHolder);
                }

                // Now try to read RA/Dec from coordsHolder
                if (coordsHolder != null)
                {
                    var raProp = coordsHolder.GetType().GetProperty("RA") ?? coordsHolder.GetType().GetProperty("RightAscension");
                    var decProp = coordsHolder.GetType().GetProperty("Dec") ?? coordsHolder.GetType().GetProperty("Declination");
                    if (raProp != null && decProp != null)
                    {
                        var raVal = Convert.ToDouble(raProp.GetValue(coordsHolder));
                        var decVal = Convert.ToDouble(decProp.GetValue(coordsHolder));
                        raHours = raVal > 24.0 ? raVal / 15.0 : raVal;
                        decDegrees = decVal;
                        return true;
                    }
                }

                // Fallback: parent appears to be a DeepSkyObjectContainer; try its Target directly
                try
                {
                    var targetPi = parent.GetType().GetProperty("Target");
                    var targetObj = targetPi?.GetValue(parent);
                    if (targetObj != null)
                    {
                        // Special-case NINA.Astrometry.InputTarget: InputCoordinates -> Coordinates
                        try
                        {
                            var icProp = targetObj.GetType().GetProperty("InputCoordinates");
                            var icVal = icProp?.GetValue(targetObj);
                            var cProp = icVal?.GetType().GetProperty("Coordinates");
                            var cVal = cProp?.GetValue(icVal);
                            if (cVal != null)
                            {
                                var raPi = cVal.GetType().GetProperty("RA") ?? cVal.GetType().GetProperty("RightAscension");
                                var decPi = cVal.GetType().GetProperty("Dec") ?? cVal.GetType().GetProperty("Declination");
                                if (raPi != null && decPi != null)
                                {
                                    var raVal = Convert.ToDouble(raPi.GetValue(cVal));
                                    var decVal = Convert.ToDouble(decPi.GetValue(cVal));
                                    raHours = raVal > 24.0 ? raVal / 15.0 : raVal;
                                    decDegrees = decVal;
                                    return true;
                                }
                            }
                        }
                        catch { }
                        // Try nested Coordinates on target
                        object? tCoords = targetObj;
                        for (int i = 0; i < 3 && tCoords != null; i++)
                        {
                            var cpi = tCoords.GetType().GetProperty("Coordinates");
                            if (cpi == null) break;
                            tCoords = cpi.GetValue(tCoords);
                        }
                        if (tCoords != null)
                        {
                            var tra = tCoords.GetType().GetProperty("RA") ?? tCoords.GetType().GetProperty("RightAscension");
                            var tdec = tCoords.GetType().GetProperty("Dec") ?? tCoords.GetType().GetProperty("Declination");
                            if (tra != null && tdec != null)
                            {
                                var raVal = Convert.ToDouble(tra.GetValue(tCoords));
                                var decVal = Convert.ToDouble(tdec.GetValue(tCoords));
                                raHours = raVal > 24.0 ? raVal / 15.0 : raVal;
                                decDegrees = decVal;
                                return true;
                            }
                        }

                        // Try direct RA/Dec on target
                        var raPT = targetObj.GetType().GetProperty("RA") ?? targetObj.GetType().GetProperty("RightAscension") ?? targetObj.GetType().GetProperty("RightAscensionHours") ?? targetObj.GetType().GetProperty("RAHours");
                        var decPT = targetObj.GetType().GetProperty("Dec") ?? targetObj.GetType().GetProperty("Declination") ?? targetObj.GetType().GetProperty("DecDegrees") ?? targetObj.GetType().GetProperty("DeclinationDegrees");
                        if (raPT != null && decPT != null)
                        {
                            var raVal = Convert.ToDouble(raPT.GetValue(targetObj));
                            var decVal = Convert.ToDouble(decPT.GetValue(targetObj));
                            raHours = raVal > 24.0 ? raVal / 15.0 : raVal;
                            decDegrees = decVal;
                            return true;
                        }
                    }
                }
                catch { }
            }
            catch { }
            return false;
        }

        private string _selectedProfile = string.Empty;
        private bool _hasExplicitOverride = false; // Track if user has explicitly set an override
        [JsonProperty]
        public string SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                var newValue = value ?? string.Empty;
                
                // If this is the first time SelectedProfile is being set (likely from deserialization)
                // and the service has a selection, clear it so condition follows the service
                // This ensures conditions always follow the options menu by default
                if (!_hasExplicitOverride && !string.IsNullOrWhiteSpace(newValue) && _horizonService != null)
                {
                    var currentServiceProfile = _horizonService.SelectedProfileName ?? string.Empty;
                    var globalProfile = Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile();
                    var resolvedServiceProfile = !string.IsNullOrWhiteSpace(currentServiceProfile) ? currentServiceProfile : globalProfile;
                    
                    // If service has a selection, always follow it (don't use deserialized value)
                    if (!string.IsNullOrWhiteSpace(resolvedServiceProfile))
                    {
                        newValue = string.Empty;
                    }
                    // Only if service has no selection and we're setting a value, keep it
                    // This allows the condition to work even if no service selection exists
                }
                // If we've already had an explicit override, allow the new value (user is changing it)
                else if (_hasExplicitOverride)
                {
                    // User is explicitly setting/changing the override
                    _hasExplicitOverride = true;
                }
                
                // If setting a non-empty value that's different from service, mark as explicit override
                if (!string.IsNullOrWhiteSpace(newValue))
                {
                    var currentServiceProfile = _horizonService?.SelectedProfileName ?? string.Empty;
                    var globalProfile = Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile();
                    var resolvedServiceProfile = !string.IsNullOrWhiteSpace(currentServiceProfile) ? currentServiceProfile : globalProfile;
                    
                    if (!string.IsNullOrWhiteSpace(resolvedServiceProfile) && !string.Equals(newValue, resolvedServiceProfile, StringComparison.Ordinal))
                    {
                        _hasExplicitOverride = true;
                    }
                }
                else if (string.IsNullOrWhiteSpace(newValue))
                {
                    // Clearing the override - condition will follow service again
                    _hasExplicitOverride = false;
                }
                
                _selectedProfile = newValue;
                RaisePropertyChanged();
                
                // Update EffectiveProfileName when SelectedProfile changes
                var effectiveProfile = !string.IsNullOrWhiteSpace(_selectedProfile)
                    ? _selectedProfile
                    : (!string.IsNullOrWhiteSpace(_horizonService?.SelectedProfileName)
                        ? _horizonService.SelectedProfileName
                        : Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile());
                EffectiveProfileName = effectiveProfile;
                
                SafeUpdateState();
            }
        }

        private double _marginBuffer = 0.0;
        [JsonProperty]
        public double MarginBuffer
        {
            get => _marginBuffer;
            set
            {
                _marginBuffer = Math.Max(0, Math.Min(10, value)); // Clamp between 0 and 10 degrees
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

        private double _maximumAltitude = 90.0;
        public double MaximumAltitude
        {
            get => _maximumAltitude;
            private set
            {
                _maximumAltitude = value;
                RaisePropertyChanged();
            }
        }

        private int _currentAzimuth = 0;
        public int CurrentAzimuth
        {
            get => _currentAzimuth;
            private set
            {
                _currentAzimuth = value;
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

        public string EffectiveProfileName
        {
            get => _effectiveProfileName;
            private set
            {
                _effectiveProfileName = value ?? string.Empty;
                RaisePropertyChanged();
            }
        }

        public override bool AllowMultiplePerSet => false;

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem)
        {
            try
            {
                // Always get fresh service reference to ensure we have latest SelectedProfileName
                var service = _horizonService ?? MaximumHorizonServiceAccessor.GetShared();
                if (service == null)
                {
                    // Last resort: do nothing without service context
                    Logger.Warning("MaximumHorizonCondition: Horizon service not available; skipping check");
                    return true;
                }
                if (service != _horizonService)
                {
                    _horizonService = service;
                }
                
                // Get effective profile - DON'T fall back to first profile if user has selected one
                // Always read fresh from service to ensure we have the latest value
                var serviceSelectedProfile = _horizonService.SelectedProfileName;
                var globalSelectedProfile = Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile();
                var effectiveProfile = !string.IsNullOrWhiteSpace(SelectedProfile)
                    ? SelectedProfile
                    : (!string.IsNullOrWhiteSpace(serviceSelectedProfile)
                        ? serviceSelectedProfile
                        : globalSelectedProfile);
                
                // Update EffectiveProfileName to ensure UI shows correct profile
                if (!string.Equals(EffectiveProfileName, effectiveProfile, StringComparison.Ordinal))
                {
                    EffectiveProfileName = effectiveProfile;
                }
                
                if (string.IsNullOrWhiteSpace(effectiveProfile))
                {
                    var profiles = _horizonService.GetAvailableProfiles().ToList();
                    Logger.Warning($"MaximumHorizonCondition: Check() No profile selected. Available profiles: [{string.Join(", ", profiles)}]. Condition will allow execution.");
                    // Don't fall back to first profile - let the user explicitly select one
                    // But still update EffectiveProfileName to empty so UI shows no profile
                    if (!string.IsNullOrWhiteSpace(EffectiveProfileName))
                    {
                        EffectiveProfileName = string.Empty;
                    }
                    return true;
                }

                // Get current target or resolve from parent context
                var target = _targetContainer?.Target;
                double altitude;
                double azimuth;
                if (target == null)
                {
                    Logger.Warning("No target available for Maximum Horizon Condition");
                    // Try parent context so loop evaluation can still work
                    if (TryGetParentRaDec(out var raH, out var decD))
                    {
                        var (lat, lon) = GetObserverLocation();
                        var utc = DateTime.UtcNow;
                        var altaz = CoordinateConverter.ConvertRaDecToAltAz(raH, decD, lat, lon, utc);
                        altitude = altaz.altitude;
                        azimuth = altaz.azimuth;
                    }
                    else
                    {
                        // Cannot evaluate; continue loop
                        return true;
                    }
                }
                else
                {
                    // Calculate altitude and azimuth from target
                    var tup = CalculateTargetAltAz(target);
                    altitude = tup.altitude;
                    azimuth = tup.azimuth;
                }

                CurrentAltitude = altitude;
                CurrentAzimuth = (int)Math.Round(azimuth) % 360;
                if (CurrentAzimuth < 0) CurrentAzimuth += 360;

                // Get maximum altitude for current azimuth (use synchronous cache-backed call to avoid UI thread deadlocks)
                var maxAltitude = _horizonService.GetMaximumAltitude(CurrentAzimuth, effectiveProfile);
                var effectiveMargin = _horizonService.GlobalMarginBuffer;
                MaximumAltitude = maxAltitude - effectiveMargin; // Apply global margin buffer

                // Check if target is visible
                var isVisible = CurrentAltitude <= MaximumAltitude;
                IsTargetVisible = isVisible;

                if (!isVisible)
                {
                    Logger.Info($"Target blocked by maximum horizon: Altitude {CurrentAltitude:F2}° exceeds maximum {MaximumAltitude + effectiveMargin:F2}° (with {effectiveMargin:F2}° margin) at azimuth {CurrentAzimuth}°");
                    Logger.Info("MaximumHorizonCondition: Blocked → requesting loop to break (returning false)");
                }

                // Some loop containers expect true to continue and false to break
                // Return true when visible (continue), false when blocked (break)
                return isVisible;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error checking maximum horizon condition: {ex.Message}", ex);
                return true; // Allow sequence to continue on error
            }
        }

        private void OnContainerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // React to target/coordinate changes
            if (e.PropertyName == null ||
                e.PropertyName.Contains("Target", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("RA", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Dec", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Coordinates", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Altitude", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Azimuth", StringComparison.OrdinalIgnoreCase))
            {
                if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: Container PropertyChanged '{e.PropertyName}' detected → updating state");
                SafeUpdateState();
            }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // Force refresh service reference to ensure we have the latest
            var latestService = MaximumHorizonServiceAccessor.GetShared();
            var newServiceProfile = latestService?.SelectedProfileName ?? string.Empty;
            
            if (latestService != null && latestService != _horizonService)
            {
                // Unsubscribe from old service
                if (_horizonService != null)
                {
                    _horizonService.SettingsChanged -= OnSettingsChanged;
                }
                _horizonService = latestService;
                // Re-subscribe to new service
                _horizonService.SettingsChanged += OnSettingsChanged;
            }
            
            // If the condition's SelectedProfile matches the old service value, it was likely just "following" the service
            // Clear it so it will now follow the new service value
            if (!string.IsNullOrWhiteSpace(SelectedProfile) && 
                string.Equals(SelectedProfile, _lastKnownServiceProfile, StringComparison.Ordinal) &&
                !string.Equals(SelectedProfile, newServiceProfile, StringComparison.Ordinal))
            {
                SelectedProfile = string.Empty;
            }
            
            // Update the last known service profile
            _lastKnownServiceProfile = newServiceProfile;
            
            // Update EffectiveProfileName to match service selection (when condition doesn't have local override)
            var effectiveProfile = !string.IsNullOrWhiteSpace(SelectedProfile)
                ? SelectedProfile
                : (!string.IsNullOrWhiteSpace(newServiceProfile)
                    ? newServiceProfile
                    : Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile());
            
            // Update EffectiveProfileName on UI thread to ensure property change is propagated
            void updateEffectiveProfile()
            {
                if (!string.Equals(EffectiveProfileName, effectiveProfile, StringComparison.Ordinal))
                {
                    EffectiveProfileName = effectiveProfile;
                }
            }
            
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(updateEffectiveProfile);
            }
            else
            {
                updateEffectiveProfile();
            }
            
            SafeUpdateState();
            // Also trigger validation to update the condition's validation state and UI
            Validate();
        }

        private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null ||
                e.PropertyName.Contains("RA", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Dec", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Coordinates", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Altitude", StringComparison.OrdinalIgnoreCase) ||
                e.PropertyName.Contains("Azimuth", StringComparison.OrdinalIgnoreCase))
            {
                if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: Target PropertyChanged '{e.PropertyName}' detected → updating state");
                SafeUpdateState();
            }
        }

        private void SafeUpdateState()
        {
            try
            {
                if (VerboseLogging) Logger.Debug("MaximumHorizonCondition: SafeUpdateState invoked");
                if (_horizonService == null)
                {
                    _horizonService = MaximumHorizonServiceAccessor.GetShared();
                    if (_horizonService == null) return;
                }
                var target = _targetContainer?.Target;
                double altitude; double azimuth;
                if (target == null)
                {
                    // Try parent context when container target is not available
                    if (TryGetParentRaDec(out var raH, out var decD))
                    {
                        var (lat, lon) = GetObserverLocation();
                        var utc = DateTime.UtcNow;
                        var (altP, azP) = CoordinateConverter.ConvertRaDecToAltAz(raH, decD, lat, lon, utc);
                        altitude = altP; azimuth = azP;
                        if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: SafeUpdateState using ParentContext Alt={altitude:F2}, Az={azimuth:F1}");
                    }
                    else
                    {
                        if (VerboseLogging) Logger.Debug("MaximumHorizonCondition: No target/container and no parent context; skipping update");
                        return;
                    }
                }
                else
                {
                    var tup = CalculateTargetAltAz(target);
                    altitude = tup.altitude; azimuth = tup.azimuth;
                }
                var roundedAz = (int)Math.Round(azimuth) % 360;
                if (roundedAz < 0) roundedAz += 360;

                var effectiveProfile = !string.IsNullOrWhiteSpace(SelectedProfile)
                    ? SelectedProfile
                    : (!string.IsNullOrWhiteSpace(_horizonService.SelectedProfileName)
                        ? _horizonService.SelectedProfileName
                        : Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile());
                
                // Update EffectiveProfileName on UI thread to ensure property change is propagated
                void updateEffectiveProfile()
                {
                    if (!string.Equals(EffectiveProfileName, effectiveProfile, StringComparison.Ordinal))
                    {
                        EffectiveProfileName = effectiveProfile;
                    }
                }
                
                var dispatcherForProfile = System.Windows.Application.Current?.Dispatcher;
                if (dispatcherForProfile != null && !dispatcherForProfile.CheckAccess())
                {
                    dispatcherForProfile.Invoke(updateEffectiveProfile);
                }
                else
                {
                    updateEffectiveProfile();
                }
                
                var effectiveMargin = _horizonService.GlobalMarginBuffer;
                var profileName = string.IsNullOrWhiteSpace(effectiveProfile) ? string.Empty : effectiveProfile;
                if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: SafeUpdateState using profileName='{profileName}', margin={effectiveMargin:F2}, az={roundedAz}");
                var maxAltitude = _horizonService.GetMaximumAltitude(roundedAz, profileName);
                if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: SafeUpdateState service.GetMaximumAltitude -> {maxAltitude:F2}");
                // If service returned default (90) but we have cached profile, compute directly
                if (Math.Abs(maxAltitude - 90.0) < 0.0001)
                {
                    var cached = _horizonService.TryGetCachedProfile(profileName);
                    if (cached != null)
                    {
                        var computed = cached.GetMaxAltitude(roundedAz);
                        if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: Overriding default max from cache profile '{profileName}' at az {roundedAz}: {computed:F2}");
                        maxAltitude = computed;
                    }
                    else
                    {
                        if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: No cached profile for '{profileName}', using default 90");
                    }
                }
                var finalMax = maxAltitude - effectiveMargin;

                void apply()
                {
                    if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: UI apply Alt={altitude:F2}, Az={roundedAz}, Max={finalMax:F2}");
                    CurrentAltitude = altitude;
                    CurrentAzimuth = roundedAz;
                    MaximumAltitude = finalMax;
                    IsTargetVisible = CurrentAltitude <= MaximumAltitude;
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
            catch
            {
                // best-effort UI refresh
            }
        }

        public override object Clone()
        {
            var clone = new MaximumHorizonCondition
            {
                SelectedProfile = SelectedProfile,
                MarginBuffer = MarginBuffer
            };
            // Let MEF inject services later; copy known services if present
            clone.HorizonService = _horizonService;
            if (_targetContainer != null) clone.TargetContainer = _targetContainer;
            if (_profileService != null) clone.ProfileService = _profileService;
            return clone;
        }

        public IList<string> Issues { get; private set; } = new List<string>();

        public bool Validate()
        {
            var issues = new List<string>();

            // Ensure service is available
            if (_horizonService == null)
            {
                _horizonService = MaximumHorizonServiceAccessor.GetShared();
            }

            // Resolve effective profile (prefer condition-level override, else global from options)
            var effectiveProfile = !string.IsNullOrWhiteSpace(SelectedProfile)
                ? SelectedProfile
                : (!string.IsNullOrWhiteSpace(_horizonService.SelectedProfileName)
                    ? _horizonService.SelectedProfileName
                    : Services.MaximumHorizonServiceAccessor.GetGlobalSelectedProfile());
            
            // Update EffectiveProfileName to ensure UI shows correct profile
            if (!string.Equals(EffectiveProfileName, effectiveProfile, StringComparison.Ordinal))
            {
                EffectiveProfileName = effectiveProfile;
            }
            
            if (VerboseLogging) Logger.Debug($"MaximumHorizonCondition: Validate() SelectedProfile(local)='{SelectedProfile}', Service.SelectedProfileName='{_horizonService.SelectedProfileName}', effectiveProfile='{effectiveProfile}'");

            if (string.IsNullOrWhiteSpace(effectiveProfile))
            {
                issues.Add("No horizon profile selected");
            }
            else
            {
                // Check existence
                var profiles = _horizonService.GetAvailableProfiles().ToList();
                if (!profiles.Contains(effectiveProfile))
                {
                    issues.Add($"Selected profile '{effectiveProfile}' does not exist");
                }
            }

            if (MarginBuffer < 0 || MarginBuffer > 10)
            {
                issues.Add("Margin buffer must be between 0 and 10 degrees");
            }

            Issues = issues;
            RaisePropertyChanged(nameof(Issues));
            return !issues.Any();
        }
    }
}

