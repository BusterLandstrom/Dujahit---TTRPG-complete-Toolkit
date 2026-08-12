using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Dujahit.Models;
using Dujahit.ViewModels;
using System;

namespace Dujahit.Views.Map;

public partial class MapSessionView : UserControl
{
    private PlayerDisplayWindow? _playerDisplay;

    public MapSessionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

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

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MapSessionViewModel vm) return;
        vm.PlayerDisplayRequested += OpenPlayerDisplay;
        vm.CloseRequested += ClosePlayerDisplay;
    }

    private void OpenPlayerDisplay()
    {
        try
        {
            if (DataContext is not MapSessionViewModel vm) return;
            if (_playerDisplay != null)
            {
                _playerDisplay.Activate();
                return;
            }
            _playerDisplay = new PlayerDisplayWindow(vm.Canvas);
            _playerDisplay.Closed += (_, _) => _playerDisplay = null;
            _playerDisplay.Show();
        }
        catch (Exception ex) { ErrorLog.Log("Unhandled in OpenPlayerDisplay", ex); }
    }

    private void ClosePlayerDisplay() => _playerDisplay?.Close();
}
