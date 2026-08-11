using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dujahit.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Dujahit.Views
{
    public class LevelUpDialog : DialogWindow
    {
        private readonly TaskCompletionSource<Dictionary<string, List<string>>?> _tcs = new();
        public Task<Dictionary<string, List<string>>?> GetResultAsync() => _tcs.Task;

        private readonly List<(LevelChoice Choice, Func<List<string>> Collect)> _sections = new();
        private readonly List<LevelChoice> _answered = new();
        public IReadOnlyList<LevelChoice> AnsweredChoices => _answered;

        private static (string Abbrev, string Display)[] AbilityDefs()
        {
            var defs = App.PM?.Rules?.Abilities;
            if (defs != null && defs.Count > 0)
                return defs.Select(d => (d.Short, d.Name)).ToArray();
            return new[]
            {
                ("STR", "Strength"), ("DEX", "Dexterity"), ("CON", "Constitution"),
                ("INT", "Intelligence"), ("WIS", "Wisdom"), ("CHA", "Charisma")
            };
        }

        public LevelUpDialog(int newLevel, IReadOnlyList<LevelChoice> choices)
        {
            Title = "Level Up";
            Width = 520;
            Height = 600;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var outer = new StackPanel { Margin = new Thickness(24), Spacing = 16 };

            outer.Children.Add(new TextBlock
            {
                Text = "You reached level " + newLevel,
                FontSize = 22,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("AccentColor") ?? Brushes.Goldenrod
            });

            if (choices.Count == 0)
            {
                outer.Children.Add(new TextBlock
                {
                    Text = "No new choices at this level, just new power. Carry on.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8
                });
            }

            foreach (var choice in choices)
            {
                var card = new StackPanel { Spacing = 6 };
                card.Children.Add(new TextBlock { Text = choice.Label, FontSize = 16, FontWeight = FontWeight.SemiBold });
                if (!string.IsNullOrWhiteSpace(choice.Description))
                    card.Children.Add(new TextBlock { Text = choice.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 });

                if (string.Equals(choice.Kind, "abilityOrFeat", StringComparison.OrdinalIgnoreCase))
                {
                    _sections.Add((choice, BuildAbilityOrFeat(choice, card)));
                }
                else
                {
                    var pickOne = choice.ChooseCount <= 1;
                    var groupName = "grp_" + choice.Id;
                    var toggles = new List<ToggleButtonLike>();
                    var storeKey = string.IsNullOrEmpty(choice.StoreAs) ? choice.Id : choice.StoreAs;
                    var lvlRules = App.PM?.Rules ?? new GameRules();
                    // These three get matched by name on the sheet, so they store the display name and not the id
                    var storeByName = lvlRules.IsSkillStore(storeKey) || lvlRules.IsExpertiseStore(storeKey) || lvlRules.IsSubclassStore(storeKey);
                    var checkedBoxes = new List<CheckBox>();

                    foreach (var opt in choice.Options)
                    {
                        var token = storeByName ? opt.Name : opt.Id;
                        if (pickOne)
                        {
                            var rb = new RadioButton { Content = opt.Name, GroupName = groupName, Margin = new Thickness(0, 2) };
                            AttachOptionTip(rb, opt);
                            toggles.Add(new ToggleButtonLike(token, () => rb.IsChecked == true));
                            card.Children.Add(rb);
                        }
                        else
                        {
                            var cb = new CheckBox { Content = opt.Name, Margin = new Thickness(0, 2) };
                            AttachOptionTip(cb, opt);
                            cb.IsCheckedChanged += (_, _) =>
                            {
                                if (cb.IsChecked == true)
                                {
                                    if (checkedBoxes.Count >= choice.ChooseCount) { cb.IsChecked = false; return; }
                                    if (!checkedBoxes.Contains(cb)) checkedBoxes.Add(cb);
                                }
                                else checkedBoxes.Remove(cb);
                            };
                            toggles.Add(new ToggleButtonLike(token, () => cb.IsChecked == true));
                            card.Children.Add(cb);
                        }
                    }

                    if (!pickOne)
                        card.Children.Add(new TextBlock { Text = "Pick " + choice.ChooseCount, Opacity = 0.6, FontSize = 11 });

                    _sections.Add((choice, () => toggles.Where(t => t.IsSelected()).Select(t => t.Id).ToList()));
                }

                outer.Children.Add(new Border
                {
                    Background = Brush("Widget") ?? new SolidColorBrush(Color.Parse("#1A1A2A")),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14),
                    Child = card
                });
            }

            var confirm = new Button { Content = "Confirm", Width = 110, IsDefault = true, Classes = { "primary" } };
            var skip = new Button { Content = "Skip for now", Width = 110, IsCancel = true, Classes = { "ghost" } };
            confirm.Click += (_, _) => Finish(Collect());
            skip.Click += (_, _) => Finish(null);
            Closed += (_, _) => _tcs.TrySetResult(null);

            outer.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { skip, confirm }
            });

            Content = new ScrollViewer { Content = outer };
        }

        private Func<List<string>> BuildAbilityOrFeat(LevelChoice choice, StackPanel card)
        {
            var modeCombo = new ComboBox
            {
                ItemsSource = new[] { "Ability Score Improvement", "Feat" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4)
            };
            card.Children.Add(modeCombo);

            var abilityDefs = AbilityDefs();
            var abilityNames = abilityDefs.Select(a => a.Display).ToList();
            var bumpA = new ComboBox { ItemsSource = abilityNames, PlaceholderText = "Increase...", HorizontalAlignment = HorizontalAlignment.Stretch };
            var bumpB = new ComboBox { ItemsSource = abilityNames, PlaceholderText = "Increase...", HorizontalAlignment = HorizontalAlignment.Stretch };

            var asiPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Raise two scores by +1, or one by +2 (pick it in both). Caps at 20.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 },
                    bumpA,
                    bumpB
                }
            };

            var featToggles = new List<ToggleButtonLike>();
            var featList = new StackPanel { Spacing = 2 };
            var groupName = "featgrp_" + choice.Id;
            foreach (var opt in choice.Options)
            {
                var rb = new RadioButton { Content = opt.Name, GroupName = groupName, Margin = new Thickness(0, 2) };
                AttachOptionTip(rb, opt);
                featToggles.Add(new ToggleButtonLike(opt.Id, () => rb.IsChecked == true));
                featList.Children.Add(rb);
            }

            var featPanel = new StackPanel
            {
                Spacing = 6,
                IsVisible = false,
                Children =
                {
                    new TextBlock
                    {
                        Text = choice.Options.Count == 0 ? "No feats in this template yet." : "Pick one feat.",
                        Opacity = 0.7,
                        FontSize = 12
                    },
                    new ScrollViewer { MaxHeight = 240, Content = featList }
                }
            };

            card.Children.Add(asiPanel);
            card.Children.Add(featPanel);

            modeCombo.SelectionChanged += (_, _) =>
            {
                var feat = modeCombo.SelectedIndex == 1;
                featPanel.IsVisible = feat;
                asiPanel.IsVisible = !feat;
            };

            return () =>
            {
                var picks = new List<string>();
                if (modeCombo.SelectedIndex == 1)
                {
                    foreach (var t in featToggles)
                        if (t.IsSelected()) picks.Add(t.Id);
                }
                else
                {
                    var asi = App.PM?.Rules?.AsiTokenPrefix ?? "asi:";
                    if (bumpA.SelectedIndex >= 0) picks.Add(asi + AbilityDefs()[bumpA.SelectedIndex].Abbrev);
                    if (bumpB.SelectedIndex >= 0) picks.Add(asi + AbilityDefs()[bumpB.SelectedIndex].Abbrev);
                }
                return picks;
            };
        }

        public static bool IsFullyAnswered(LevelChoice choice, int pickCount) =>
            pickCount >= Math.Min(choice.ChooseCount, Math.Max(1, choice.Options.Count));

        private Dictionary<string, List<string>> Collect()
        {
            var picked = new Dictionary<string, List<string>>();
            _answered.Clear();
            foreach (var (choice, collect) in _sections)
            {
                var chosen = collect();
                if (chosen.Count == 0) continue;
                if (IsFullyAnswered(choice, chosen.Count)) _answered.Add(choice);
                var key = string.IsNullOrEmpty(choice.StoreAs) ? choice.Id : choice.StoreAs;
                if (!picked.ContainsKey(key)) picked[key] = new List<string>();
                picked[key].AddRange(chosen);
            }
            return picked;
        }

        private static void AttachOptionTip(Control target, LevelChoiceOption opt)
        {
            var hasDesc = !string.IsNullOrWhiteSpace(opt.Description);
            var hasMech = !string.IsNullOrWhiteSpace(opt.Mechanics);
            if (!hasDesc && !hasMech) return;

            var panel = new StackPanel { Spacing = 6, MaxWidth = 360 };
            panel.Children.Add(new TextBlock { Text = opt.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (hasDesc)
                panel.Children.Add(new TextBlock { Text = opt.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
            if (hasMech)
                panel.Children.Add(new TextBlock { Text = opt.Mechanics, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });

            ToolTip.SetTip(target, panel);
            ToolTip.SetShowDelay(target, 300);
        }

        private void Finish(Dictionary<string, List<string>>? result)
        {
            _tcs.TrySetResult(result);
            Close();
        }

        private sealed class ToggleButtonLike
        {
            public string Id { get; }
            private readonly Func<bool> _isSelected;
            public ToggleButtonLike(string id, Func<bool> isSelected) { Id = id; _isSelected = isSelected; }
            public bool IsSelected() => _isSelected();
        }
    }
}
