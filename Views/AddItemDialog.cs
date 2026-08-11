using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dujahit.Views
{
    public class AddItemDialog : DialogWindow
    {
        public class ItemRow
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string ItemType { get; set; } = "";
            public string DataJson { get; set; } = "{}";
            public override string ToString() => string.IsNullOrEmpty(ItemType) ? Name : $"{Name}  ({ItemType})";
        }

        private readonly TaskCompletionSource<ItemRow?> _tcs = new();
        public Task<ItemRow?> GetResultAsync() => _tcs.Task;

        private readonly List<ItemRow> _all;
        private readonly Dictionary<ItemRow, (List<string> Weapon, string? Armor)> _props = new();
        private readonly HashSet<string>? _heldProf;
        private readonly Dictionary<string, string> _armorMap;
        private readonly Dictionary<ItemRow, bool> _usable = new();
        private readonly CheckBox _profOnly;
        private readonly TextBox _searchBox;
        private readonly ComboBox _typeCombo;
        private readonly ComboBox _propCombo;
        private readonly ListBox _list;
        private readonly TextBlock _detailName;
        private readonly TextBlock _detailMeta;
        private readonly TextBlock _detailBody;

        public AddItemDialog(IReadOnlyList<ItemRow> items, HashSet<string>? heldProfIds = null, IReadOnlyDictionary<string, string>? armorTypeToProf = null)
        {
            _all = items.ToList();
            _heldProf = heldProfIds;
            _armorMap = armorTypeToProf != null ? new Dictionary<string, string>(armorTypeToProf) : new Dictionary<string, string>();
            foreach (var i in _all)
            {
                _props[i] = ParseProps(i.DataJson);
                _usable[i] = _heldProf == null || ProficiencyResolver.ItemUsable(i.DataJson, _heldProf, _armorMap);
            }

            Title = "Add Item";
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _searchBox = new TextBox { Watermark = "Search items...", Margin = new Thickness(0, 0, 8, 0) };
            _searchBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Filter(); };

            var types = new List<string> { "All types" };
            types.AddRange(_all.Select(i => i.ItemType).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t));
            _typeCombo = new ComboBox { MinWidth = 140, ItemsSource = types, SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 0) };
            _typeCombo.SelectionChanged += (_, _) => { RebuildPropOptions(); Filter(); };

            _propCombo = new ComboBox { MinWidth = 150, IsVisible = false };
            _propCombo.SelectionChanged += (_, _) => Filter();

            _profOnly = new CheckBox { Content = "Only what I can use", IsVisible = _heldProf != null, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _profOnly.IsCheckedChanged += (_, _) => Filter();

            _list = new ListBox { ItemsSource = _all, Margin = new Thickness(0, 0, 12, 0) };
            _list.SelectionChanged += (_, _) => ShowDetail(_list.SelectedItem as ItemRow);

            _detailName = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap };
            _detailMeta = new TextBlock { Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
            _detailBody = new TextBlock { Opacity = 0.75, FontSize = 12, TextWrapping = TextWrapping.Wrap, Text = "Select an item to see its details." };

            var detail = new Border
            {
                MaxWidth = 260,
                BorderBrush = Brush("Divider") ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = new StackPanel { Spacing = 6, Children = { _detailName, _detailMeta, _detailBody } }
            };

            var add = new Button { Content = "Add", IsDefault = true, Classes = { "primary" } };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Classes = { "ghost" } };
            add.Click += (_, _) => Finish(_list.SelectedItem as ItemRow);
            cancel.Click += (_, _) => Finish(null);
            Closed += (_, _) => _tcs.TrySetResult(null);

            var topRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
            topRow.Children.Add(_searchBox);
            Grid.SetColumn(_typeCombo, 1);
            topRow.Children.Add(_typeCombo);
            Grid.SetColumn(_propCombo, 2);
            topRow.Children.Add(_propCombo);
            Grid.SetColumn(_profOnly, 3);
            topRow.Children.Add(_profOnly);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MaxWidth = 720, Height = 460 };
            body.Children.Add(_list);
            Grid.SetColumn(detail, 1);
            body.Children.Add(detail);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, add }, Margin = new Thickness(0, 16, 0, 0) };
            var header = new TextBlock { Text = "Pick an item from the catalog", FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 10) };
            topRow.Margin = new Thickness(0, 0, 0, 10);

            var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
            Grid.SetRow(header, 0); root.Children.Add(header);
            Grid.SetRow(topRow, 1); root.Children.Add(topRow);
            Grid.SetRow(body, 2); root.Children.Add(body);
            Grid.SetRow(buttons, 3); root.Children.Add(buttons);

            Content = root;

            RebuildPropOptions();
        }


        // The property dropdown is filled from whatever the type-filtered items carry, so it works for any game
        private void RebuildPropOptions()
        {
            var type = _typeCombo.SelectedIndex > 0 ? _typeCombo.SelectedItem as string : null;
            var scope = string.IsNullOrEmpty(type) ? _all : _all.Where(i => string.Equals(i.ItemType, type, StringComparison.OrdinalIgnoreCase)).ToList();

            var weaponProps = scope.SelectMany(i => _props[i].Weapon).Distinct().OrderBy(x => x).ToList();
            var armorTypes = scope.Select(i => _props[i].Armor).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).Distinct().OrderBy(x => x).ToList();

            List<string> opts;
            if (weaponProps.Count > 0) { opts = new List<string> { "Any property" }; opts.AddRange(weaponProps); }
            else if (armorTypes.Count > 0) { opts = new List<string> { "Any armor type" }; opts.AddRange(armorTypes); }
            else { _propCombo.IsVisible = false; _propCombo.ItemsSource = null; return; }

            _propCombo.ItemsSource = opts;
            _propCombo.SelectedIndex = 0;
            _propCombo.IsVisible = true;
        }

        private void ShowDetail(ItemRow? row)
        {
            if (row == null)
            {
                _detailName.Text = "";
                _detailMeta.Text = "";
                _detailBody.Text = "Select an item to see its details.";
                return;
            }
            var d = ItemDisplay.FromJson(row.Name, row.ItemType, row.DataJson);
            _detailName.Text = row.Name;
            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(d.TypeLine)) meta.Add(d.TypeLine);
            if (d.HasMagic) meta.Add(d.MagicTag);
            if (d.HasStats) meta.Add(d.StatLine);
            if (d.HasProperties) meta.Add(d.PropertiesLine);
            _detailMeta.Text = string.Join("\n", meta);
            _detailBody.Text = d.Description;
        }

        private void Filter()
        {
            var q = _searchBox.Text?.Trim();
            var type = _typeCombo.SelectedIndex > 0 ? _typeCombo.SelectedItem as string : null;
            var prop = _propCombo.IsVisible && _propCombo.SelectedIndex > 0 ? _propCombo.SelectedItem as string : null;

            IEnumerable<ItemRow> res = _all;
            if (_profOnly.IsChecked == true) res = res.Where(i => _usable[i]);
            if (!string.IsNullOrEmpty(type)) res = res.Where(i => string.Equals(i.ItemType, type, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(prop)) res = res.Where(i => _props[i].Weapon.Contains(prop) || string.Equals(_props[i].Armor, prop, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(q)) res = res.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || i.ItemType.Contains(q, StringComparison.OrdinalIgnoreCase));
            _list.ItemsSource = res.ToList();
        }

        private static (List<string>, string?) ParseProps(string dataJson)
        {
            var weapon = new List<string>();
            string? armor = null;
            if (string.IsNullOrWhiteSpace(dataJson)) return (weapon, armor);
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return (weapon, armor);
                if (root.TryGetProperty("WeaponCategory", out var wc) && wc.ValueKind == JsonValueKind.Array)
                    foreach (var c in wc.EnumerateArray())
                    {
                        var s = (c.GetString() ?? "").Replace("wp-", "");
                        if (s.Length > 0) weapon.Add(s);
                    }
                if (root.TryGetProperty("IsRanged", out var ir) && ir.ValueKind == JsonValueKind.True) weapon.Add("ranged");
                else if (weapon.Count > 0) weapon.Add("melee");
                if (root.TryGetProperty("ArmorType", out var at) && at.ValueKind == JsonValueKind.String)
                {
                    var a = (at.GetString() ?? "").Replace("atype-", "");
                    if (a.Length > 0) armor = a;
                }
            }
            catch (JsonException) { }
            return (weapon, armor);
        }

        private void Finish(ItemRow? result)
        {
            _tcs.TrySetResult(result);
            Close();
        }
    }
}
