using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Dujahit.Views
{
    public class HandoutViewerDialog : DialogWindow
    {
        private readonly List<string> _pages;
        private int _index;
        private readonly Image _image = new() { Stretch = Stretch.Uniform };
        private readonly Border _viewport = new() { ClipToBounds = true, Height = 560, Background = Brushes.Transparent };   // Picked 560 by eye, not worked out.
        private readonly TextBlock _counter = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(6, 0) };
        private readonly Button _prev = new() { Content = "Prev", Classes = { "ghost" } };
        private readonly Button _next = new() { Content = "Next", Classes = { "ghost" } };
        private readonly Button _fit = new() { Content = "Fit", Classes = { "ghost" } };

        private const double MinZoom = 1.0;
        private const double MaxZoom = 16.0;
        private const double ZoomStep = 1.1;

        private double _zoom = 1.0;
        private double _panX;
        private double _panY;
        private bool _panning;
        private Point _panStart;

        public HandoutViewerDialog(string title, List<string> pages, Func<string, Task>? reveal)
        {
            _pages = pages ?? new List<string>();

            _prev.Click += (_, _) => Show(_index - 1);
            _next.Click += (_, _) => Show(_index + 1);

            var nav = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _prev, _counter, _next }
            };
            nav.IsVisible = _pages.Count > 1;

            _viewport.Child = _image;
            _image.RenderTransformOrigin = RelativePoint.TopLeft;
            _viewport.PointerWheelChanged += OnWheel;
            _viewport.PointerPressed += OnPressed;
            _viewport.PointerMoved += OnMoved;
            _viewport.PointerReleased += OnReleased;
            _fit.Click += (_, _) => ResetView();

            var hint = new TextBlock
            {
                Text = "Scroll to zoom, drag to pan.",
                Classes = { "muted" },
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var view = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _fit, hint }
            };

            var body = new StackPanel { Spacing = 12, Width = 640, Children = { _viewport, view, nav } };

            if (reveal != null)
            {
                var revealBtn = new Button { Content = "Reveal this page to players", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Center };
                revealBtn.Click += async (_, _) =>
                {
                    if (_index >= 0 && _index < _pages.Count) await reveal(_pages[_index]);
                };
                body.Children.Add(revealBtn);
            }

            Mount(string.IsNullOrWhiteSpace(title) ? "Handout" : title, body);
            Show(0);
        }

        private void Show(int i)
        {
            if (_pages.Count == 0) return;
            _index = Math.Clamp(i, 0, _pages.Count - 1);
            try
            {
                if (File.Exists(_pages[_index])) _image.Source = new Bitmap(_pages[_index]);
            }
            catch { }
            _counter.Text = (_index + 1) + " / " + _pages.Count;
            _prev.IsEnabled = _index > 0;
            _next.IsEnabled = _index < _pages.Count - 1;
            ResetView();
        }

        private void ResetView()
        {
            _zoom = 1.0;
            _panX = 0;
            _panY = 0;
            ApplyView();
        }

        private void ApplyView()
        {
            // Clamp it or the page wanders off and only Fit brings it back.
            var spillX = _viewport.Bounds.Width * (_zoom - 1.0);
            var spillY = _viewport.Bounds.Height * (_zoom - 1.0);
            _panX = spillX > 0 ? Math.Clamp(_panX, -spillX, 0) : 0;
            _panY = spillY > 0 ? Math.Clamp(_panY, -spillY, 0) : 0;
            _image.RenderTransform = new MatrixTransform(new Matrix(_zoom, 0, 0, _zoom, _panX, _panY));
        }

        private void OnWheel(object? sender, PointerWheelEventArgs e)
        {
            var target = Math.Clamp(_zoom * (e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep), MinZoom, MaxZoom);
            if (Math.Abs(target - _zoom) < 0.0001) return;

            var p = e.GetPosition(_viewport);
            var beforeX = (p.X - _panX) / _zoom;
            var beforeY = (p.Y - _panY) / _zoom;
            _zoom = target;
            _panX = p.X - beforeX * _zoom;
            _panY = p.Y - beforeY * _zoom;
            ApplyView();
            e.Handled = true;
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(_viewport).Properties;
            if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed) return;
            _panning = true;
            _panStart = e.GetPosition(_viewport);
            e.Pointer.Capture(_viewport);
        }

        private void OnMoved(object? sender, PointerEventArgs e)
        {
            if (!_panning) return;
            var here = e.GetPosition(_viewport);
            _panX += here.X - _panStart.X;
            _panY += here.Y - _panStart.Y;
            _panStart = here;
            ApplyView();
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            _panning = false;
            e.Pointer.Capture(null);
        }
    }
}
