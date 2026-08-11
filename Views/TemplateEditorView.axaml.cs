using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Dujahit.Views
{
    public partial class TemplateEditorView : UserControl
    {
        public TemplateEditorView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
