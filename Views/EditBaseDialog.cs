using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dujahit.Views
{
    public class EditBaseDialog : DialogWindow
    {
        public class Result
        {
            public string Name { get; set; } = "";
            public int Level { get; set; } = 1;
            public Dictionary<string, int> Abilities { get; set; } = new();
        }

        private readonly TaskCompletionSource<Result?> _tcs = new();
        public Task<Result?> GetResultAsync() => _tcs.Task;

        private readonly TextBox _nameBox;
        private readonly NumericUpDown _levelBox;
        private readonly Dictionary<string, NumericUpDown> _abilityBoxes = new();

        public EditBaseDialog(string name, int level, IReadOnlyDictionary<string, int> abilities)
        {
            Title = "Edit Base Settings";
            Width = 380;
            Height = 520;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _nameBox = new TextBox { Text = name, Watermark = "Name" };
            var rules = App.PM?.Rules;
            _levelBox = new NumericUpDown { Value = level, Minimum = 1, Maximum = rules?.MaxLevel ?? 20, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };

            var shorts = rules != null && rules.Abilities.Count > 0
                ? rules.Abilities.Select(a => a.Short).ToArray()
                : new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" };

            var abilityPanel = new StackPanel { Spacing = 8 };
            foreach (var abbrev in shorts)
            {
                var box = new NumericUpDown
                {
                    Value = abilities.TryGetValue(abbrev, out var v) ? v : 10,
                    Minimum = 1,
                    Maximum = rules?.AbilityScoreHardCap ?? 30,
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                _abilityBoxes[abbrev] = box;
                var label = new TextBlock { Text = abbrev, VerticalAlignment = VerticalAlignment.Center };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*") };
                Grid.SetColumn(label, 0);
                Grid.SetColumn(box, 1);
                row.Children.Add(label);
                row.Children.Add(box);
                abilityPanel.Children.Add(row);
            }

            var save = new Button { Content = "Apply", IsDefault = true, Classes = { "primary" } };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Classes = { "ghost" } };
            save.Click += (_, _) => Finish(Collect());
            cancel.Click += (_, _) => Finish(null);
            Closed += (_, _) => _tcs.TrySetResult(null);

            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children =
                    {
                        Label("Name"), _nameBox,
                        Label("Level"), _levelBox,
                        Label("Ability Scores"), abilityPanel,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Margin = new Thickness(0, 16, 0, 0),
                            Children = { cancel, save }
                        }
                    }
                }
            };
        }


        private Result Collect()
        {
            var r = new Result
            {
                Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "Unnamed" : _nameBox.Text!.Trim(),
                Level = (int)(_levelBox.Value ?? 1)
            };
            foreach (var kv in _abilityBoxes)
                r.Abilities[kv.Key] = (int)(kv.Value.Value ?? 10);
            return r;
        }

        private void Finish(Result? result)
        {
            _tcs.TrySetResult(result);
            Close();
        }
    }
}
