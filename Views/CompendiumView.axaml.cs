using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Dujahit.ViewModels;

namespace Dujahit.Views
{
    public partial class CompendiumView : UserControl
    {
        public CompendiumView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnEntryTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not Control c || c.DataContext is not CompendiumEntry entry) return;
            if (this.GetVisualRoot() is not Window owner) return;

            Window dialog = entry.Category switch
            {
                "Spells" => new SpellViewDialog(entry.Name, entry.DataJson),
                "Items" => new ItemViewDialog(entry.Name, entry.ItemType, entry.DataJson),
                _ => new FeatureDetailDialog(entry.Name, string.IsNullOrWhiteSpace(entry.Detail) ? entry.Subtitle : entry.Subtitle + "\n" + entry.Detail, 0)
            };
            _ = dialog.ShowDialog(owner);
        }
    }
}
