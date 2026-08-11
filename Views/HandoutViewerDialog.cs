using Avalonia.Controls;
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
        private readonly Image _image = new() { Stretch = Stretch.Uniform, MaxWidth = 620, MaxHeight = 560 };
        private readonly TextBlock _counter = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(6, 0) };
        private readonly Button _prev = new() { Content = "Prev", Classes = { "ghost" } };
        private readonly Button _next = new() { Content = "Next", Classes = { "ghost" } };

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

            var body = new StackPanel { Spacing = 12, Width = 640, Children = { _image, nav } };

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
        }
    }
}
