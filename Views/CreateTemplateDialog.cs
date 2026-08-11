using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Dujahit.Models.Database;
using Dujahit.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Avalonia.Automation;

namespace Dujahit.Views
{
    public class CreateTemplateDialog : DialogWindow
    {
        private readonly TaskCompletionSource<ItemDraft?> _tcs = new();
        public Task<ItemDraft?> GetResultAsync() => _tcs.Task;

        private readonly TemplateItemCatalogs _cat;

        private readonly TextBox _nameBox;
        private readonly TextBox _descBox;
        private readonly ComboBox _typeBox;

        private readonly StackPanel _weaponPanel;
        private readonly StackPanel _armorPanel;
        private readonly StackPanel _consumablePanel;

        private readonly ComboBox _masteryBox;
        private readonly List<CheckBox> _propertyChecks = new();

        private readonly StackPanel _damageRowsPanel;
        private readonly List<DamageRow> _damageRows = new();

        private readonly ComboBox _armorTypeBox;
        private readonly NumericUpDown _baseAcBox;
        private readonly NumericUpDown _magicBox;

        private readonly StackPanel _effectRowsPanel;
        private readonly List<EffectRow> _effectRows = new();

        private readonly NumericUpDown _hitBonusBox;
        private readonly NumericUpDown _damageBonusBox;
        private readonly CheckBox _isRangedBox;
        private readonly NumericUpDown _rangeNormalBox;
        private readonly NumericUpDown _rangeMaxBox;
        private readonly TextBox _ammoBox;
        private readonly CheckBox _allowsDexBox;
        private readonly NumericUpDown _maxDexBox;
        private readonly NumericUpDown _maxChargesBox;
        private readonly ComboBox _rechargeBox;
        private readonly CheckBox _attunementBox;
        private readonly CheckBox _magicItemBox;
        private readonly TextBox _useTextBox;

        public CreateTemplateDialog(TemplateItemCatalogs catalogs)
        {
            _cat = catalogs;

            Title = "Create Custom Item";
            Width = 680;
            Height = 700;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _nameBox = new TextBox { Watermark = "Item name" };
            _descBox = new TextBox { Watermark = "Description", AcceptsReturn = true, Height = 60 };

            _typeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            _typeBox.ItemsSource = new[] { "Weapon", "Armor", "Consumable", "Generic" };
            _typeBox.SelectedIndex = 0;
            _typeBox.SelectionChanged += (_, _) => UpdateVisible();

            _masteryBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = _cat.Masteries };

            var propsPanel = new WrapPanel();
            foreach (var p in _cat.WeaponProperties)
            {
                var cb = new CheckBox { Content = p.Name, Tag = p.Id, Margin = new Thickness(0, 0, 10, 4) };
                _propertyChecks.Add(cb);
                propsPanel.Children.Add(cb);
            }

            _damageRowsPanel = new StackPanel { Spacing = 6 };
            var addDamage = new Button { Content = "+ Add damage type", Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            addDamage.Click += (_, _) => AddDamageRow();

            _hitBonusBox = Number(-5, 10, 0);
            _damageBonusBox = Number(-5, 20, 0);
            _isRangedBox = new CheckBox { Content = "Ranged weapon" };
            _rangeNormalBox = Number(0, 2000, 0);
            _rangeMaxBox = Number(0, 6000, 0);
            _ammoBox = new TextBox { Watermark = "Ammunition item id, blank for none" };

            var bonusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bonusRow.Children.Add(Stacked("To hit", _hitBonusBox));
            bonusRow.Children.Add(Stacked("Damage", _damageBonusBox));

            var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            rangeRow.Children.Add(Stacked("Normal", _rangeNormalBox));
            rangeRow.Children.Add(Stacked("Long", _rangeMaxBox));

            _weaponPanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Label("Damage (type, count, die, flat)"),
                    _damageRowsPanel,
                    addDamage,
                    Label("Flat bonuses"), bonusRow,
                    Label("Mastery"), _masteryBox,
                    _isRangedBox,
                    Label("Range in feet"), rangeRow,
                    Label("Ammunition"), _ammoBox,
                    Label("Properties"), propsPanel
                }
            };
            AddDamageRow();

            _armorTypeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = _cat.ArmorTypes };
            if (_cat.ArmorTypes.Count > 0) _armorTypeBox.SelectedIndex = 0;
            _baseAcBox = new NumericUpDown { Value = 11, Minimum = 0, Maximum = 30, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            _magicBox = new NumericUpDown { Value = 0, Minimum = -5, Maximum = 10, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };

            _allowsDexBox = new CheckBox { Content = "Adds the dex modifier", IsChecked = true };
            _maxDexBox = Number(0, 10, 0);

            _armorPanel = new StackPanel
            {
                Spacing = 8,
                IsVisible = false,
                Children =
                {
                    Label("Armor type"), _armorTypeBox,
                    Label("Base AC (hard baseline, e.g. plate 18)"), _baseAcBox,
                    Label("AC modifier (flat bonus, e.g. +1 shield)"), _magicBox,
                    _allowsDexBox,
                    Label("Dex cap (0 for no cap)"), _maxDexBox
                }
            };

            _effectRowsPanel = new StackPanel { Spacing = 6 };
            var addEffect = new Button { Content = "+ Add effect", Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            addEffect.Click += (_, _) => AddEffectRow();

            _consumablePanel = new StackPanel
            {
                Spacing = 8,
                IsVisible = false,
                Children =
                {
                    Label("Effects (heal/damage dice or a status)"),
                    _effectRowsPanel,
                    addEffect
                }
            };
            AddEffectRow();

            _maxChargesBox = Number(0, 100, 0);
            _rechargeBox = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "none", "short", "long" }, SelectedIndex = 0 };
            _attunementBox = new CheckBox { Content = "Needs attunement" };
            _magicItemBox = new CheckBox { Content = "Magic item" };
            _useTextBox = new TextBox { Watermark = "What using it says" };

            var chargeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            chargeRow.Children.Add(Stacked("Max charges", _maxChargesBox));
            chargeRow.Children.Add(Stacked("Refills on", _rechargeBox));

            var sharedPanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Label("Charges"), chargeRow,
                    _attunementBox,
                    _magicItemBox,
                    Label("Use text"), _useTextBox
                }
            };

            var create = new Button { Content = "Create", IsDefault = true, Classes = { "primary" } };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Classes = { "ghost" } };
            create.Click += (_, _) => Finish(BuildDraft());
            cancel.Click += (_, _) => Finish(null);
            Closed += (_, _) => _tcs.TrySetResult(null);

            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 10,
                    Children =
                    {
                        Label("Name"), _nameBox,
                        Label("Description"), _descBox,
                        Label("Item type"), _typeBox,
                        _weaponPanel,
                        _armorPanel,
                        _consumablePanel,
                        sharedPanel,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Margin = new Thickness(0, 16, 0, 0),
                            Children = { cancel, create }
                        }
                    }
                }
            };

            UpdateVisible();
        }


        private static NumericUpDown Number(int min, int max, int value) =>
            new() { Minimum = min, Maximum = max, Value = value, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };

        private static StackPanel Stacked(string caption, Control control) =>
            new() { Spacing = 4, Children = { Label(caption), control } };

        private static IDataTemplate OptionTemplate() =>
            new FuncDataTemplate<CatalogOption>((opt, _) =>
                new TextBlock { Text = opt?.Name ?? "" }, true);

        private string SelectedType => _typeBox.SelectedItem as string ?? "Generic";

        private void UpdateVisible()
        {
            _weaponPanel.IsVisible = SelectedType == "Weapon";
            _armorPanel.IsVisible = SelectedType == "Armor";
            _consumablePanel.IsVisible = SelectedType == "Consumable";
        }

        private void AddDamageRow()
        {
            var row = new DamageRow(_cat, OptionTemplate());
            row.RemoveRequested += () =>
            {
                if (_damageRows.Count <= 1) return;
                _damageRows.Remove(row);
                _damageRowsPanel.Children.Remove(row.Root);
            };
            _damageRows.Add(row);
            _damageRowsPanel.Children.Add(row.Root);
        }

        private void AddEffectRow()
        {
            var row = new EffectRow(_cat, OptionTemplate());
            row.RemoveRequested += () =>
            {
                if (_effectRows.Count <= 1) return;
                _effectRows.Remove(row);
                _effectRowsPanel.Children.Remove(row.Root);
            };
            _effectRows.Add(row);
            _effectRowsPanel.Children.Add(row.Root);
        }

        private ItemDraft BuildDraft()
        {
            var type = SelectedType;
            var data = new Dictionary<string, object?>
            {
                ["$type"] = type,
                ["Description"] = _descBox.Text ?? ""
            };

            if (type == "Weapon")
            {
                data["DamageValues"] = _damageRows.Select(r => r.ToData()).ToList();
                data["WeaponCategory"] = _propertyChecks.Where(c => c.IsChecked == true).Select(c => c.Tag as string).ToList();
                var mastery = (_masteryBox.SelectedItem as CatalogOption)?.Id;
                if (!string.IsNullOrEmpty(mastery)) data["Mastery"] = mastery;
                data["HitBonus"] = (int)(_hitBonusBox.Value ?? 0);
                data["DamageBonus"] = (int)(_damageBonusBox.Value ?? 0);
                data["IsRanged"] = _isRangedBox.IsChecked == true;
                data["RangeNormal"] = (int)(_rangeNormalBox.Value ?? 0);
                data["RangeMax"] = (int)(_rangeMaxBox.Value ?? 0);
                var ammo = (_ammoBox.Text ?? "").Trim();
                if (ammo.Length > 0) data["AmmoItemId"] = ammo;
            }
            else if (type == "Armor")
            {
                data["ArmorType"] = (_armorTypeBox.SelectedItem as CatalogOption)?.Id ?? "";
                data["BaseAC"] = (int)(_baseAcBox.Value ?? 11);
                data["AcBonus"] = (int)(_magicBox.Value ?? 0);
                data["AllowsDexBonus"] = _allowsDexBox.IsChecked == true;
                data["MaxDexBonus"] = (int)(_maxDexBox.Value ?? 0);
            }
            else if (type == "Consumable")
            {
                data["IsConsumable"] = true;
                var effects = _effectRows.Select(r => r.ToData()).ToList();
                data["Effects"] = effects;

                var healRow = effects.FirstOrDefault(e => (e.TryGetValue("Kind", out var k) ? k as string : "") == "Heal");
                if (healRow != null)
                {
                    var dice = healRow.TryGetValue("DiceId", out var hd) ? hd as string ?? "" : "";
                    var count = healRow.TryGetValue("Count", out var hc) && hc is int ci ? ci : 0;
                    var flat = healRow.TryGetValue("Flat", out var hf) && hf is int fi ? fi : 0;
                    if ((count > 0 && dice.Length > 0) || flat > 0)
                        data["Healing"] = new Dictionary<string, object?> { ["DiceId"] = dice, ["DiceCount"] = count, ["Flat"] = flat };
                }
            }

            var maxCharges = (int)(_maxChargesBox.Value ?? 0);
            if (maxCharges > 0)
            {
                data["MaxCharges"] = maxCharges;
                var recharge = _rechargeBox.SelectedItem as string ?? "none";
                data["RechargeOn"] = recharge == "none" ? "" : recharge;
            }

            data["Attunement"] = _attunementBox.IsChecked == true;
            data["IsMagic"] = _magicItemBox.IsChecked == true;

            var useText = (_useTextBox.Text ?? "").Trim();
            if (useText.Length > 0) data["UseText"] = useText;

            return new ItemDraft
            {
                Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "Unnamed Item" : _nameBox.Text!.Trim(),
                ItemType = type,
                Data = data
            };
        }

        private void Finish(ItemDraft? draft)
        {
            _tcs.TrySetResult(draft);
            Close();
        }
    }

    public class DamageRow
    {
        public Control Root { get; }
        public event Action? RemoveRequested;

        private readonly ComboBox _type;
        private readonly NumericUpDown _count;
        private readonly ComboBox _die;
        private readonly NumericUpDown _flat;

        public DamageRow(TemplateItemCatalogs cat, IDataTemplate optionTemplate)
        {
            _type = new ComboBox { Width = 170, ItemsSource = cat.DamageTypes };
            if (cat.DamageTypes.Count > 0) _type.SelectedIndex = 0;
            _count = new NumericUpDown { Value = 1, Minimum = 1, Maximum = 99, Width = 160 };
            _die = new ComboBox { Width = 75, ItemsSource = cat.Dice };
            if (cat.Dice.Count > 0) _die.SelectedIndex = 0;
            _flat = new NumericUpDown { Value = 0, Minimum = -20, Maximum = 99, Width = 160 };

            var remove = new Button { Content = "x", Width = 30 };
            AutomationProperties.SetName(remove, "Remove");
            remove.Click += (_, _) => RemoveRequested?.Invoke();

            Root = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _type, _count, _die, _flat, remove }
            };
        }

        public Dictionary<string, object?> ToData() => new()
        {
            ["TypeId"] = (_type.SelectedItem as CatalogOption)?.Id ?? "",
            ["Count"] = (int)(_count.Value ?? 1),
            ["DiceId"] = (_die.SelectedItem as CatalogOption)?.Id ?? "",
            ["Flat"] = (int)(_flat.Value ?? 0)
        };

        public void SetValues(string typeId, int count, string diceId, int flat)
        {
            _count.Value = count;
            _flat.Value = flat;
            for (int i = 0; i < _type.ItemCount; i++)
                if (_type.Items[i] is CatalogOption o && o.Id == typeId) { _type.SelectedIndex = i; break; }
            for (int i = 0; i < _die.ItemCount; i++)
                if (_die.Items[i] is CatalogOption o && o.Id == diceId) { _die.SelectedIndex = i; break; }
        }
    }

    public class EffectRow
    {
        public Control Root { get; }
        public event Action? RemoveRequested;

        private readonly ComboBox _kind;
        private readonly NumericUpDown _count;
        private readonly ComboBox _die;
        private readonly NumericUpDown _flat;
        private readonly ComboBox _status;

        public EffectRow(TemplateItemCatalogs cat, IDataTemplate optionTemplate)
        {
            _kind = new ComboBox { Width = 120, ItemsSource = new[] { "Heal", "Damage", "Status Effect" }, SelectedIndex = 0 };
            _count = new NumericUpDown { Value = 1, Minimum = 1, Maximum = 99, Width = 60 };
            _die = new ComboBox { Width = 80, ItemsSource = cat.Dice };
            if (cat.Dice.Count > 0) _die.SelectedIndex = 0;
            _flat = new NumericUpDown { Value = 0, Minimum = -20, Maximum = 999, Width = 60 };
            _status = new ComboBox { Width = 130, ItemsSource = cat.Conditions };
            if (cat.Conditions.Count > 0) _status.SelectedIndex = 0;

            _kind.SelectionChanged += (_, _) => Sync();

            var remove = new Button { Content = "x", Width = 30 };
            AutomationProperties.SetName(remove, "Remove");
            remove.Click += (_, _) => RemoveRequested?.Invoke();

            Root = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _kind, _count, _die, _flat, _status, remove }
            };
            Sync();
        }

        private void Sync()
        {
            var isStatus = (_kind.SelectedItem as string) == "Status Effect";
            _count.IsEnabled = !isStatus;
            _die.IsEnabled = !isStatus;
            _flat.IsEnabled = !isStatus;
            _status.IsEnabled = isStatus;
        }

        public Dictionary<string, object?> ToData()
        {
            var kind = _kind.SelectedItem as string ?? "Heal";
            var d = new Dictionary<string, object?> { ["Kind"] = kind };
            if (kind == "Status Effect")
            {
                d["StatusId"] = (_status.SelectedItem as CatalogOption)?.Id ?? "";
            }
            else
            {
                d["Count"] = (int)(_count.Value ?? 1);
                d["DiceId"] = (_die.SelectedItem as CatalogOption)?.Id ?? "";
                d["Flat"] = (int)(_flat.Value ?? 0);
            }
            return d;
        }
    }
}
