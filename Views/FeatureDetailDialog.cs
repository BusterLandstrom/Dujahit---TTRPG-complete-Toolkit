using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;

namespace Dujahit.Views
{
    public class FeatureDetailDialog : DialogWindow
    {
        public FeatureDetailDialog(string name, string description, int level)
        {
            Width = 460;
            Height = 380;

            var close = GhostButton("Close");
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 16, 0, 0);
            close.Click += (_, _) => Close();

            var body = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = level > 0 ? $"Gained at level {level}" : "", Classes = { "fieldLabel" }, IsVisible = level > 0 },
                    new ScrollViewer
                    {
                        Height = 240,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(description) ? "No description available." : description,
                            Classes = { "body" },
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    close
                }
            };

            Mount(string.IsNullOrWhiteSpace(name) ? "Feature" : name, body);
        }
    }
}
