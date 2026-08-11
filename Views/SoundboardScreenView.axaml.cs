using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Dujahit.Views
{
    public partial class SoundboardScreenView : UserControl
    {
        public SoundboardScreenView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
