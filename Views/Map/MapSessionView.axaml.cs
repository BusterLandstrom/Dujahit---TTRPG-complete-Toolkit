using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Dujahit.ViewModels;

namespace Dujahit.Views.Map;

public partial class MapSessionView : UserControl
{
    public MapSessionView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is MapSessionViewModel vm)
                Dispatcher.UIThread.InvokeAsync(vm.LoadCombatStateAsync);
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is MapSessionViewModel vm)
                vm.Detach();
        };
    }
}
