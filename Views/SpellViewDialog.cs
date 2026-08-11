using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls.Primitives;

namespace Dujahit.Views
{
    public class SpellViewDialog : DialogWindow
    {
        public SpellViewDialog(string name, string dataJson)
        {
            Width = 480;
            Height = 520;

            var panel = new StackPanel { Spacing = 10 };
            JsonElement root = default;
            var haveRoot = false;
            try { using var doc = JsonDocument.Parse(dataJson); root = doc.RootElement.Clone(); haveRoot = root.ValueKind == JsonValueKind.Object; }
            catch (JsonException) { }

            var level = Int(root, haveRoot, "Level");
            var school = Str(root, haveRoot, "School");
            var levelLine = App.PM?.Rules?.SpellLevelName(level) ?? (level == 0 ? "Cantrip" : $"Level {level}");
            if (!string.IsNullOrWhiteSpace(school)) levelLine += "  " + school;

            var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            tags.Children.Add(Badge(levelLine, false));
            if (Bool(root, haveRoot, "Concentration")) tags.Children.Add(Badge("Concentration", true));
            if (Bool(root, haveRoot, "Ritual")) tags.Children.Add(Badge("Ritual", true));
            panel.Children.Add(tags);

            AddRow(panel, "Casting time", Str(root, haveRoot, "CastingTime"));
            AddRow(panel, "Range", Str(root, haveRoot, "Range"));
            AddRow(panel, "Duration", Str(root, haveRoot, "Duration"));
            AddRow(panel, "Components", StrOrList(root, haveRoot, "Components"));

            var desc = Str(root, haveRoot, "Description");
            if (!string.IsNullOrWhiteSpace(desc))
            {
                panel.Children.Add(new TextBlock { Text = "Description", Classes = { "sectionLabel" }, Margin = new Thickness(0, 8, 0, 0) });
                panel.Children.Add(new TextBlock { Text = desc, Classes = { "body" }, TextWrapping = TextWrapping.Wrap });
            }

            var close = GhostButton("Close");
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 16, 0, 0);
            close.Click += (_, _) => Close();

            var scroll = new ScrollViewer
            {
                Content = panel,
                Height = 400,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var body = new StackPanel { Spacing = 0 };
            body.Children.Add(scroll);
            body.Children.Add(close);

            Mount(string.IsNullOrWhiteSpace(name) ? "Spell" : name, body);
        }

        private static void AddRow(StackPanel panel, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
            grid.Children.Add(new TextBlock { Text = label, Classes = { "fieldLabel" }, VerticalAlignment = VerticalAlignment.Top });
            var val = new TextBlock { Text = value, Classes = { "body" }, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);
            panel.Children.Add(grid);
        }

        private Border Badge(string text, bool accent) => new()
        {
            Background = (accent ? Brush("AccentColor") : Brush("Divider")) ?? Brushes.Gray,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (accent ? Brush("Background") : Brush("WidgetForeground")) ?? Brushes.White
            }
        };


        private static string Str(JsonElement r, bool have, string prop) =>
            have && r.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static string StrOrList(JsonElement r, bool have, string prop)
        {
            if (!have || !r.TryGetProperty(prop, out var v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind != JsonValueKind.Array) return "";
            var parts = new List<string>();
            foreach (var e in v.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                    parts.Add(e.GetString()!);
            return string.Join(", ", parts);
        }
        private static int Int(JsonElement r, bool have, string prop) =>
            have && r.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
        private static bool Bool(JsonElement r, bool have, string prop) =>
            have && r.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
    }
}
