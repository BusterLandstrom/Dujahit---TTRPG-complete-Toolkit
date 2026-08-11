using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Dujahit.ViewModels;

namespace Dujahit.Views;

public partial class CharacterDashboardView : UserControl
{
    public CharacterDashboardView()
    {
        InitializeComponent();
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is CharacterDashboardViewModel vm)
                await vm.LoadAsync();
        };
    }
}