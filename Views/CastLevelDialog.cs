using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Dujahit.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;

namespace Dujahit.Views
{
    public class CastLevelDialog : DialogWindow
    {
        private readonly TaskCompletionSource<int?> _tcs = new();
        public Task<int?> GetResultAsync() => _tcs.Task;

        public CastLevelDialog(string spellName, IReadOnlyList<CastLevelOption> options)
        {
            Width = 360;
            SizeToContent = SizeToContent.Height;

            var list = new StackPanel { Spacing = 8 };
            foreach (var o in options)
            {
                var lvl = o.Level;
                var btn = new Button
                {
                    Content = $"{o.Label}  ({o.Remaining} left)",
                    Classes = { "primary" },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                btn.Click += (_, _) => { _tcs.TrySetResult(lvl); Close(); };
                list.Children.Add(btn);
            }

            // A high-level caster upcasting a low spell can throw a lot of slots at this, cap it and let it scroll past a full screen.
            var scroll = new ScrollViewer
            {
                Content = list,
                MaxHeight = 460,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "Choose the slot level to cast at", Classes = { "muted" }, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(scroll);

            var cancel = GhostButton("Cancel");
            cancel.HorizontalAlignment = HorizontalAlignment.Stretch;
            cancel.HorizontalContentAlignment = HorizontalAlignment.Center;
            cancel.Margin = new Thickness(0, 4, 0, 0);
            cancel.Click += (_, _) => { _tcs.TrySetResult(null); Close(); };
            panel.Children.Add(cancel);

            Closed += (_, _) => _tcs.TrySetResult(null);
            Mount($"Cast {spellName}", panel);
        }
    }
}
