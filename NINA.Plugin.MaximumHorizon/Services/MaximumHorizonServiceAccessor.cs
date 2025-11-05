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
                NINA.Core.Utility.Logger.Debug($"MaximumHorizonServiceAccessor: Shared service resolved. SelectedProfileName='{_shared.SelectedProfileName}'");
                return _shared;
            }
        }

        public static void SetGlobalSelectedProfile(string profileName)
        {
            _globalSelectedProfileName = profileName ?? string.Empty;
        }

        public static string GetGlobalSelectedProfile()
        {
            return _globalSelectedProfileName;
        }
    }
}


