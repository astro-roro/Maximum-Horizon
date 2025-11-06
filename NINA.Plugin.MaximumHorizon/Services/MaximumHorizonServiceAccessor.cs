using System;

namespace NINA.Plugin.MaximumHorizon.Services
{
    internal static class MaximumHorizonServiceAccessor
    {
        private static IMaximumHorizonService? _shared;
        private static readonly object _lock = new object();
        private static string _globalSelectedProfileName = string.Empty;

        public static IMaximumHorizonService GetShared()
        {
            if (_shared != null) return _shared;
            lock (_lock)
            {
                if (_shared != null) return _shared;
                _shared = new MaximumHorizonService();
                // Sync global selected profile into the service instance if it's set (but don't override if service already loaded one from disk)
                if (!string.IsNullOrWhiteSpace(_globalSelectedProfileName) && string.IsNullOrWhiteSpace(_shared.SelectedProfileName))
                {
                    _shared.SelectedProfileName = _globalSelectedProfileName;
                }
                else if (!string.IsNullOrWhiteSpace(_shared.SelectedProfileName))
                {
                    // Service loaded a profile from disk, sync it to global
                    _globalSelectedProfileName = _shared.SelectedProfileName;
                }
                return _shared;
            }
        }

        public static void SetGlobalSelectedProfile(string profileName)
        {
            var newValue = profileName ?? string.Empty;
            if (_globalSelectedProfileName != newValue)
            {
                _globalSelectedProfileName = newValue;
                // Also sync to shared service instance if it exists
                if (_shared != null && _shared.SelectedProfileName != newValue)
                {
                    _shared.SelectedProfileName = newValue;
                }
            }
        }

        public static string GetGlobalSelectedProfile()
        {
            return _globalSelectedProfileName;
        }
    }
}


