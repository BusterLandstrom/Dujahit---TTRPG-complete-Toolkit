using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using System;
using Dujahit.Models.Application;
using Avalonia.Automation;

namespace Dujahit.Views
{
    public class DialogWindow : Window
    {
        private bool _wrapped;
        private LayoutTransformControl? _scaleHost;
        private Control? _surface;

        protected double ContentWidthCap { get; set; } = 760;

        protected DialogWindow()
        {
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            MinWidth = 320;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (!_wrapped && Content is Control body)
            {
                Content = null;
                Mount(Title ?? "", body);
            }

            FitToScreen();
            Dispatcher.UIThread.Post(FitToScreen, DispatcherPriority.Loaded);  // Second pass once layout is real
        }

        private void FitToScreen()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen != null && screen.Scaling > 0)
            {
                var roomW = screen.WorkingArea.Width / screen.Scaling - 32;
                var roomH = screen.WorkingArea.Height / screen.Scaling - 32;
                var scale = UiScaleService.Scale;

                // Without a cap a paragraph measures at infinite width and never wraps, so pin it to a readable column or the screen, whichever is smaller
                // Measure is handed the cap so DesiredSize can never beat it, this Max does nothing. Raise the cap per dialog instead.
                var needed = ContentWidthCap;
                if (_surface != null)
                {
                    _surface.InvalidateMeasure();
                    _surface.Measure(new Size(ContentWidthCap, double.PositiveInfinity));
                    if (_surface.DesiredSize.Width > 0) needed = Math.Max(needed, _surface.DesiredSize.Width);
                }

                if (needed * scale > roomW) scale = Math.Max(roomW / needed, UiScaleService.Min);
                if (_scaleHost != null) _scaleHost.LayoutTransform = new ScaleTransform(scale, scale);

                MaxWidth = Math.Min(roomW, Math.Ceiling(needed * scale) + 2);
                MaxHeight = roomH;

            }
        }

        protected IBrush? Brush(string key) => this.TryFindResource(key, out var v) && v is IBrush b ? b : null;

        protected static TextBlock Label(string text) => new() { Text = text, Classes = { "fieldLabel" } };

        protected static Button PrimaryButton(string text) => new() { Content = text, Classes = { "primary" }, IsDefault = true };

        protected static Button GhostButton(string text) => new() { Content = text, Classes = { "ghost" }, IsCancel = true };

        protected static Control ButtonRow(Button cancel, Button confirm) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancel, confirm }
        };

        public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string confirmLabel = "Delete")
        {
            if (owner == null) return false;
            var tcs = new TaskCompletionSource<bool>();
            var dlg = new DialogWindow();
            var cancel = GhostButton("Cancel");
            var confirm = new Button { Content = confirmLabel, Classes = { "danger" }, IsDefault = true };
            cancel.Click += (_, _) => { tcs.TrySetResult(false); dlg.Close(); };
            confirm.Click += (_, _) => { tcs.TrySetResult(true); dlg.Close(); };
            var body = new StackPanel
            {
                Spacing = 14,
                MaxWidth = 380,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ButtonRow(cancel, confirm)
                }
            };
            dlg.Mount(title, body);
            dlg.Closed += (_, _) => tcs.TrySetResult(false);
            await dlg.ShowDialog(owner);
            return await tcs.Task;
        }

        protected void Mount(Control body) => Mount(Title ?? "", body);

        protected void Mount(string title, Control body)
        {
            _wrapped = true;
            Title = title;

            var scale = UiScaleService.Scale;

            var bar = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(18, 10, 8, 10)
            };
            bar.Children.Add(new TextBlock
            {
                Text = title,
                Classes = { "header" },
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var closeBtn = new Button
            {
                Classes = { "close" },
                Content = "×",
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(closeBtn, "Close");
            closeBtn.Click += (_, _) => Close();
            Grid.SetColumn(closeBtn, 1);
            bar.Children.Add(closeBtn);

            var barHost = new Border { Child = bar, Background = Brushes.Transparent };
            barHost.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(barHost).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            };

            var layout = new DockPanel();
            DockPanel.SetDock(barHost, Dock.Top);
            layout.Children.Add(barHost);

            var divider = new Border { Height = 1, Background = Brush("Divider") ?? Brushes.Gray };
            DockPanel.SetDock(divider, Dock.Top);
            layout.Children.Add(divider);

            layout.Children.Add(new Border { Child = body, Padding = new Thickness(20, 16, 20, 20) });

            var outer = new Border
            {
                Background = Brush("Background") ?? Brushes.Black,
                BorderBrush = Brush("Divider") ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = layout
            };

            _surface = outer;
            _scaleHost = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = outer
            };

            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = body is ScrollViewer ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
                Content = _scaleHost
            };
        }
    }
}
