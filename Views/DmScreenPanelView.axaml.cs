using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Dujahit.Models.DmScreen;
using Dujahit.ViewModels;
using Markdown.Avalonia;
using System;
using System.Threading.Tasks;
using System.ComponentModel;

namespace Dujahit.Views
{
    public partial class DmScreenPanelView : UserControl
    {
        private DmScreenPanel? _panel;
        private MarkdownScrollViewer? _viewer;

        public DmScreenPanelView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            if (this.FindControl<TextBox>("ContentBox") is { } box) MarkdownListEditing.Attach(box);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_panel != null) _panel.PropertyChanged -= OnPanelPropertyChanged;
            _panel = DataContext as DmScreenPanel;
            if (_panel != null) _panel.PropertyChanged += OnPanelPropertyChanged;
            _ = Render();
        }

        private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DmScreenPanel.Content) || e.PropertyName == nameof(DmScreenPanel.IsEditing))
                _ = Render();
        }

        private async Task Render()
        {
            _viewer ??= this.FindControl<MarkdownScrollViewer>("MdViewer");
            if (_viewer == null || _panel == null) return;
            _viewer.Markdown = await MarkdownEditorViewModel.PreRenderAsync(_panel.Content ?? "");
        }

        private void OnOpen(object? sender, RoutedEventArgs e)
        {
            if (_panel == null) return;
            if (this.GetVisualRoot() is not Window owner) return;
            _ = new DmScreenPanelDialog(_panel.Title, _panel.Content).ShowDialog(owner);
        }
    }
}
