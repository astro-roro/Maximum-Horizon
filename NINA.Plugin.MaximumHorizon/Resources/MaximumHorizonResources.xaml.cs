using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.MaximumHorizon.Resources
{
    [Export(typeof(ResourceDictionary))]
    public partial class MaximumHorizonResources : ResourceDictionary
    {
        public MaximumHorizonResources()
        {
            Source = new Uri("pack://application:,,,/NINA.Plugin.MaximumHorizon;component/Resources/MaximumHorizonResources.xaml");
        }
    }
}

