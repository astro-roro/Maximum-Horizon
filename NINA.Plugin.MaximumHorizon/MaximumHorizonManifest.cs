using System.ComponentModel.Composition;
using NINA.Plugin.Interfaces;
using NINA.Plugin.ManifestDefinition;

namespace NINA.Plugin.MaximumHorizon
{
    [Export(typeof(IPluginManifest))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public sealed class MaximumHorizonManifest : PluginManifest
    {
        public MaximumHorizonManifest()
        {
            Identifier = "NINA.Plugin.MaximumHorizon";
            Name = "Maximum Horizon";
            Version = new PluginVersion("1.0.2.0");
            Author = "Astro With RoRo";
            Homepage = "https://github.com/astro-roro/Maximum-Horizon";
            Repository = "https://github.com/astro-roro/Maximum-Horizon";
            License = "MIT";
            LicenseURL = "https://opensource.org/licenses/MIT";
            ChangelogURL = "https://github.com/astro-roro/Maximum-Horizon";
            Tags = new[] { "Sequencer", "Condition", "Horizon" };
            MinimumApplicationVersion = new PluginVersion("3.0.0.0");

            Descriptions = new PluginDescription
            {
                ShortDescription = "Defines maximum altitude constraints to avoid imaging targets blocked by overhead obstructions.",
                LongDescription = "Maximum Horizon lets you build horizon profiles manually, import them from CSV, or extract from images so your sequences stop when a target exceeds your custom altitude limit."
            };

            Installer = new PluginInstallerDetails
            {
                URL = "https://github.com/astro-roro/Maximum-Horizon/releases/download/v1.0.2.0/NINA.Plugin.MaximumHorizon.dll",
                Type = InstallerType.ARCHIVE,
                Checksum = "0c3dd3d69a0dda73267324bb63857174e38dfa0b20ebd6c2d434b2f5e7482c46",
                ChecksumType = InstallerChecksum.SHA256
            };
        }
    }
}
