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
            Identifier = "B7957C29-EE62-45E8-A918-6FEE3C1F566D";
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
                Type = InstallerType.DLL,
                Checksum = "45be4b425e9f62b502b6409796654361a40846676b805a41cca990ffdd39befa",
                ChecksumType = InstallerChecksum.SHA256
            };
        }
    }
}
