using Avalonia.Controls;
using Avalonia.Input;
using Dujahit.ViewModels;

namespace Dujahit.Views.Map
{
    public class PlayerDisplayWindow : Window
    {
        private readonly MapCanvasView _canvas;

        public PlayerDisplayWindow(MapCanvasViewModel canvas)
        {
            Title = "Player view";
            Width = 1280;
            Height = 720;
            MinWidth = 320;
            MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _canvas = new MapCanvasView { DataContext = canvas };
            _canvas.UsePlayerEyes();
            Content = _canvas;

            KeyDown += OnKeyDown;
            DoubleTapped += (_, _) => ToggleFullScreen();
            Closed += (_, _) => _canvas.ReleaseViewModel();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11) ToggleFullScreen();
            else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen) WindowState = WindowState.Normal;
        }

        private void ToggleFullScreen() =>
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }
}
