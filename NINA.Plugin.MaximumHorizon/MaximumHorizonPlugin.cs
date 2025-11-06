using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.MaximumHorizon.Options;
using NINA.Plugin.MaximumHorizon.Services;
using NINA.Plugin.MaximumHorizon.Resources;
using System.Windows;

namespace NINA.Plugin.MaximumHorizon
{
    [Export(typeof(PluginBase))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class MaximumHorizonPlugin : PluginBase
    {
        private readonly IMaximumHorizonService _horizonService;
        private MaximumHorizonOptions? _options;

        [ImportingConstructor]
        public MaximumHorizonPlugin([Import(AllowDefault = true)] IMaximumHorizonService? horizonService = null)
        {
            // Ensure the whole plugin (Options + Conditions) share the exact same service instance
            _horizonService = horizonService ?? MaximumHorizonServiceAccessor.GetShared();
        }

        public MaximumHorizonOptions Options => _options ??= new MaximumHorizonOptions(_horizonService);

        public object? Settings => Options;

        public override Task Initialize()
        {
            Logger.Info("Maximum Horizon Plugin initialized");
            try
            {
                // Ensure our resources (including the Options template) are merged into the app
                var dict = new MaximumHorizonResources();
                if (global::System.Windows.Application.Current != null)
                {
                    global::System.Windows.Application.Current.Resources.MergedDictionaries.Add(dict);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"MaximumHorizonPlugin: Failed to merge resources: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public override Task Teardown()
        {
            Logger.Info("Maximum Horizon Plugin teardown");
            return Task.CompletedTask;
        }
    }
}

