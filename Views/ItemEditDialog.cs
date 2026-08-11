using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Dujahit.Models.Database;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dujahit.Views
{
    public class ItemEditDialog : DialogWindow
    {
        private readonly TaskCompletionSource<string?> _tcs = new();
        public Task<string?> GetResultAsync() => _tcs.Task;

        private readonly TemplateItemCatalogs _cat;
        private readonly string _type;

        private readonly TextBox _descBox;
        private readonly StackPanel _damageRowsPanel;
        private readonly List<DamageRow> _damageRows = new();
        private readonly ComboBox? _masteryBox;
        private readonly NumericUpDown? _baseAcBox;
        private readonly NumericUpDown? _acModBox;

        private readonly CheckBox? _isRangedBox;
        private readonly NumericUpDown? _rangeNormalBox;
        private readonly NumericUpDown? _rangeMaxBox;
        private readonly NumericUpDown? _hitBonusBox;
        private readonly NumericUpDown? _damageBonusBox;
        private readonly TextBox? _ammoBox;
        private readonly List<(CatalogOption Option, CheckBox Box)> _propertyBoxes = new();
        private readonly ComboBox? _armorTypeBox;
        private readonly CheckBox? _allowsDexBox;
        private readonly NumericUpDown? _maxDexBox;
        private readonly NumericUpDown _maxChargesBox;
        private readonly ComboBox _rechargeBox;
        private readonly CheckBox _attunementBox;
        private readonly CheckBox _magicBox;
        private readonly CheckBox _consumableBox;
        private readonly NumericUpDown _healCountBox;
        private readonly ComboBox _healDieBox;
        private readonly NumericUpDown _healFlatBox;
        private readonly TextBox _useTextBox;

        public ItemEditDialog(string name, string itemType, string dataJson, TemplateItemCatalogs catalogs)
        {
            _cat = catalogs;
            _type = itemType;

            Title = "Edit " + (string.IsNullOrWhiteSpace(name) ? "Item" : name);
            Width = 560;
            Height = 600;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            JsonElement root = default;
            var haveRoot = false;
            try { root = JsonDocument.Parse(dataJson).RootElement.Clone(); haveRoot = true; }
            catch (JsonException) { }

            _descBox = new TextBox { Watermark = "Description", AcceptsReturn = true, Height = 60 };
            if (haveRoot && root.TryGetProperty("Description", out var desc))
                _descBox.Text = desc.GetString() ?? "";

            var outer = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
            outer.Children.Add(Label("Description"));
            outer.Children.Add(_descBox);

            _damageRowsPanel = new StackPanel { Spacing = 6 };

            if (_type == "Weapon")
            {
                if (haveRoot && root.TryGetProperty("DamageValues", out var dv) && dv.ValueKind == JsonValueKind.Array)
                    foreach (var e in dv.EnumerateArray())
                    {
                        var typeId = e.TryGetProperty("TypeId", out var ti) ? ti.GetString() ?? "" : "";
                        var count = e.TryGetProperty("Count", out var c) && c.TryGetInt32(out var cv) ? cv : 1;
                        var diceId = e.TryGetProperty("DiceId", out var di) ? di.GetString() ?? "" : "";
                        var flat = e.TryGetProperty("Flat", out var f) && f.TryGetInt32(out var fv) ? fv : 0;
                        AddDamageRow(typeId, count, diceId, flat);
                    }
                if (_damageRows.Count == 0) AddDamageRow(null, 1, null, 0);

                var addDamage = new Button { Content = "+ Add damage type", Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
                addDamage.Click += (_, _) => AddDamageRow(null, 1, null, 0);

                _masteryBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = _cat.Masteries };
                var curMastery = haveRoot && root.TryGetProperty("Mastery", out var ma) ? ma.GetString() ?? "" : "";
                SelectById(_masteryBox, curMastery);

                _hitBonusBox = Number(-5, 10, ReadInt(root, haveRoot, "HitBonus", 0));
                _damageBonusBox = Number(-5, 20, ReadInt(root, haveRoot, "DamageBonus", 0));

                _isRangedBox = new CheckBox { Content = "Ranged weapon", IsChecked = ReadBool(root, haveRoot, "IsRanged", false) };
                _rangeNormalBox = Number(0, 2000, ReadInt(root, haveRoot, "RangeNormal", 0));
                _rangeMaxBox = Number(0, 6000, ReadInt(root, haveRoot, "RangeMax", 0));
                _ammoBox = new TextBox { Watermark = "Ammunition item id, blank for none", Text = ReadStr(root, haveRoot, "AmmoItemId", "") };

                var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                rangeRow.Children.Add(Stacked("Normal", _rangeNormalBox));
                rangeRow.Children.Add(Stacked("Long", _rangeMaxBox));

                var bonusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                bonusRow.Children.Add(Stacked("To hit", _hitBonusBox));
                bonusRow.Children.Add(Stacked("Damage", _damageBonusBox));

                var props = new WrapPanel();
                var current = new HashSet<string>();
                if (haveRoot && root.TryGetProperty("WeaponCategory", out var wc) && wc.ValueKind == JsonValueKind.Array)
                    foreach (var e in wc.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) current.Add(e.GetString() ?? "");
                foreach (var opt in _cat.WeaponProperties)
                {
                    var box = new CheckBox { Content = opt.Name, IsChecked = current.Contains(opt.Id), Width = 160 };
                    _propertyBoxes.Add((opt, box));
                    props.Children.Add(box);
                }

                outer.Children.Add(Label("Damage (type, count, die, flat)"));
                outer.Children.Add(_damageRowsPanel);
                outer.Children.Add(addDamage);
                outer.Children.Add(Label("Flat bonuses"));
                outer.Children.Add(bonusRow);
                outer.Children.Add(Label("Mastery"));
                outer.Children.Add(_masteryBox);
                outer.Children.Add(_isRangedBox);
                outer.Children.Add(Label("Range in feet"));
                outer.Children.Add(rangeRow);
                outer.Children.Add(Label("Ammunition"));
                outer.Children.Add(_ammoBox);
                outer.Children.Add(Label("Properties"));
                outer.Children.Add(props);
            }
            else if (_type == "Armor")
            {
                _baseAcBox = new NumericUpDown { Minimum = 0, Maximum = 30, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
                _baseAcBox.Value = haveRoot && root.TryGetProperty("BaseAC", out var ba) && ba.TryGetInt32(out var bav) ? bav : 11;
                _acModBox = new NumericUpDown { Minimum = -5, Maximum = 10, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
                _acModBox.Value = haveRoot && root.TryGetProperty("AcBonus", out var ab) && ab.TryGetInt32(out var abv) ? abv : 0;

                _armorTypeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = _cat.ArmorTypes };
                SelectById(_armorTypeBox, ReadStr(root, haveRoot, "ArmorType", ""));
                _allowsDexBox = new CheckBox { Content = "Adds the dex modifier", IsChecked = ReadBool(root, haveRoot, "AllowsDexBonus", true) };
                _maxDexBox = Number(0, 10, ReadInt(root, haveRoot, "MaxDexBonus", 0));

                outer.Children.Add(Label("Base AC (hard baseline)"));
                outer.Children.Add(_baseAcBox);
                outer.Children.Add(Label("AC modifier (flat bonus)"));
                outer.Children.Add(_acModBox);
                outer.Children.Add(Label("Armor type"));
                outer.Children.Add(_armorTypeBox);
                outer.Children.Add(_allowsDexBox);
                outer.Children.Add(Label("Dex cap (0 for no cap)"));
                outer.Children.Add(_maxDexBox);
            }

            _maxChargesBox = Number(0, 100, ReadInt(root, haveRoot, "MaxCharges", 0));
            _rechargeBox = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new List<string> { "none", "short", "long" } };
            _rechargeBox.SelectedItem = ReadStr(root, haveRoot, "RechargeOn", "none") switch
            {
                "short" => "short",
                "long" => "long",
                _ => "none"
            };
            _attunementBox = new CheckBox { Content = "Needs attunement", IsChecked = ReadBool(root, haveRoot, "Attunement", false) };
            _magicBox = new CheckBox { Content = "Magic item", IsChecked = ReadBool(root, haveRoot, "IsMagic", false) };
            _consumableBox = new CheckBox { Content = "Consumable", IsChecked = ReadBool(root, haveRoot, "IsConsumable", false) };
            _useTextBox = new TextBox { Watermark = "What using it says", Text = ReadStr(root, haveRoot, "UseText", "") };

            var heal = haveRoot && root.TryGetProperty("Healing", out var hv) && hv.ValueKind == JsonValueKind.Object ? hv : default;
            _healCountBox = Number(0, 20, heal.ValueKind == JsonValueKind.Object ? ReadInt(heal, true, "DiceCount", 0) : 0);
            _healDieBox = new ComboBox { Width = 90, ItemsSource = _cat.Dice };
            SelectById(_healDieBox, heal.ValueKind == JsonValueKind.Object ? ReadStr(heal, true, "DiceId", "") : "");
            _healFlatBox = Number(0, 99, heal.ValueKind == JsonValueKind.Object ? ReadInt(heal, true, "Flat", 0) : 0);

            var chargeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            chargeRow.Children.Add(Stacked("Max charges", _maxChargesBox));
            chargeRow.Children.Add(Stacked("Refills on", _rechargeBox));

            var healRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            healRow.Children.Add(Stacked("Dice", _healCountBox));
            healRow.Children.Add(Stacked("Die", _healDieBox));
            healRow.Children.Add(Stacked("Flat", _healFlatBox));

            outer.Children.Add(Label("Charges"));
            outer.Children.Add(chargeRow);
            outer.Children.Add(_attunementBox);
            outer.Children.Add(_magicBox);
            outer.Children.Add(_consumableBox);
            outer.Children.Add(Label("Healing"));
            outer.Children.Add(healRow);
            outer.Children.Add(Label("Use text"));
            outer.Children.Add(_useTextBox);

            var save = new Button { Content = "Save", IsDefault = true, Classes = { "primary" } };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Classes = { "ghost" } };
            save.Click += (_, _) => Finish(BuildJson(haveRoot ? root : default, haveRoot));
            cancel.Click += (_, _) => Finish(null);
            Closed += (_, _) => _tcs.TrySetResult(null);

            outer.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0),
                Children = { cancel, save }
            });

            Content = new ScrollViewer { Content = outer };
        }

        private void AddDamageRow(string? typeId, int count, string? diceId, int flat)
        {
            var row = new DamageRow(_cat, null!);
            if (!string.IsNullOrEmpty(typeId) || !string.IsNullOrEmpty(diceId))
                row.SetValues(typeId ?? "", count, diceId ?? "", flat);
            row.RemoveRequested += () =>
            {
                if (_damageRows.Count <= 1) return;
                _damageRows.Remove(row);
                _damageRowsPanel.Children.Remove(row.Root);
            };
            _damageRows.Add(row);
            _damageRowsPanel.Children.Add(row.Root);
        }

        private string BuildJson(JsonElement root, bool haveRoot)
        {
            var map = new Dictionary<string, object?>();
            if (haveRoot && root.ValueKind == JsonValueKind.Object)
                foreach (var p in root.EnumerateObject())
                    map[p.Name] = JsonToObject(p.Value);

            map["$type"] = _type;
            map["Description"] = _descBox.Text ?? "";

            if (_type == "Weapon")
            {
                map["DamageValues"] = _damageRows.Select(r => r.ToData()).ToList();
                var mastery = (_masteryBox?.SelectedItem as CatalogOption)?.Id;
                if (!string.IsNullOrEmpty(mastery)) map["Mastery"] = mastery;

                map["HitBonus"] = (int)(_hitBonusBox?.Value ?? 0);
                map["DamageBonus"] = (int)(_damageBonusBox?.Value ?? 0);
                map["IsRanged"] = _isRangedBox?.IsChecked == true;
                map["RangeNormal"] = (int)(_rangeNormalBox?.Value ?? 0);
                map["RangeMax"] = (int)(_rangeMaxBox?.Value ?? 0);
                map["WeaponCategory"] = _propertyBoxes.Where(p => p.Box.IsChecked == true).Select(p => p.Option.Id).ToList();

                var ammo = (_ammoBox?.Text ?? "").Trim();
                if (ammo.Length > 0) map["AmmoItemId"] = ammo;
                else map.Remove("AmmoItemId");
            }
            else if (_type == "Armor")
            {
                map["BaseAC"] = (int)(_baseAcBox?.Value ?? 11);
                map["AcBonus"] = (int)(_acModBox?.Value ?? 0);
                map["AllowsDexBonus"] = _allowsDexBox?.IsChecked == true;
                map["MaxDexBonus"] = (int)(_maxDexBox?.Value ?? 0);
                var armorType = (_armorTypeBox?.SelectedItem as CatalogOption)?.Id;
                if (!string.IsNullOrEmpty(armorType)) map["ArmorType"] = armorType;
            }

            var maxCharges = (int)_maxChargesBox.Value.GetValueOrDefault();
            var recharge = _rechargeBox.SelectedItem as string ?? "none";
            if (maxCharges > 0)
            {
                map["MaxCharges"] = maxCharges;
                map["RechargeOn"] = recharge == "none" ? "" : recharge;
            }
            else
            {
                map.Remove("MaxCharges");
                map.Remove("RechargeOn");
            }

            map["Attunement"] = _attunementBox.IsChecked == true;
            map["IsMagic"] = _magicBox.IsChecked == true;
            map["IsConsumable"] = _consumableBox.IsChecked == true;

            var healDie = (_healDieBox.SelectedItem as CatalogOption)?.Id ?? "";
            var healCount = (int)_healCountBox.Value.GetValueOrDefault();
            var healFlat = (int)_healFlatBox.Value.GetValueOrDefault();
            if ((healCount > 0 && healDie.Length > 0) || healFlat > 0)
                map["Healing"] = new Dictionary<string, object?> { ["DiceId"] = healDie, ["DiceCount"] = healCount, ["Flat"] = healFlat };
            else map.Remove("Healing");
            var useText = (_useTextBox.Text ?? "").Trim();
            if (useText.Length > 0) map["UseText"] = useText; else map.Remove("UseText");

            return JsonSerializer.Serialize(map);
        }

        private static object? JsonToObject(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt32(out var i) ? i : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => e.EnumerateArray().Select(JsonToObject).ToList(),
            JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => JsonToObject(p.Value)),
            _ => null
        };

        private static NumericUpDown Number(int min, int max, int value) =>
            new() { Minimum = min, Maximum = max, Value = value, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };

        private static StackPanel Stacked(string caption, Control control) =>
            new() { Spacing = 4, Children = { Label(caption), control } };

        private static int ReadInt(JsonElement root, bool haveRoot, string name, int fallback) =>
            haveRoot && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : fallback;

        private static bool ReadBool(JsonElement root, bool haveRoot, string name, bool fallback) =>
            haveRoot && root.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;

        private static string ReadStr(JsonElement root, bool haveRoot, string name, string fallback) =>
            haveRoot && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

        private static void SelectById(ComboBox box, string id)
        {
            for (int i = 0; i < box.ItemCount; i++)
                if (box.Items[i] is CatalogOption o && o.Id == id) { box.SelectedIndex = i; return; }
        }

        private void Finish(string? json)
        {
            _tcs.TrySetResult(json);
            Close();
        }
    }
}
