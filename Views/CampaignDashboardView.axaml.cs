using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Dujahit.Views
{
    public partial class CampaignDashboardView : UserControl
    {
        public CampaignDashboardView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
