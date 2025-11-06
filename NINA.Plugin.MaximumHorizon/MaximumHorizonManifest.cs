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
            Author = "Rohan Hinton";
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
                LongDescription = "Maximum Horizon lets you build horizon profiles manually, import them from CSV, or extract from images so your sequences stop when a target exceeds your custom altitude limit.",
                FeaturedImageURL = "https://raw.githubusercontent.com/yourusername/NINA.Plugin.MaximumHorizon/main/images/featured.png",
                ScreenshotURL = "https://raw.githubusercontent.com/yourusername/NINA.Plugin.MaximumHorizon/main/images/screenshot1.png",
                AltScreenshotURL = "https://raw.githubusercontent.com/yourusername/NINA.Plugin.MaximumHorizon/main/images/screenshot2.png"
            };

            Installer = new PluginInstallerDetails
            {
                URL = "https://github.com/astro-roro/Maximum-Horizon",
                Type = InstallerType.ARCHIVE,
                Checksum = "YOUR_CHECKSUM_HERE",
                ChecksumType = InstallerChecksum.SHA256
            };
        }
    }
}


