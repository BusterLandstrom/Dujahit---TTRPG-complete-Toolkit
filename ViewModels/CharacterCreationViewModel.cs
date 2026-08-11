using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.ComponentModel;
using Dujahit.Models.UI;
using Avalonia.Media;

namespace Dujahit.ViewModels
{
    public enum AbilityMethod { PointBuy, Manual, Roll }

    public class CharacterCreationViewModel : ViewModelBase
    {
        private readonly CampaignViewModel? _parent;
        private readonly List<SubraceOption> _allSubraces = new();

        private readonly List<ItemOption> _allEquipment = new();

        // Same deal for classes and races, the bound collections are just whatever survives their search boxes.
        private readonly List<ClassOption> _allClasses = new();
        private readonly List<RaceOption> _allRaces = new();

        private readonly CharacterRuntime? _editing;
        public bool IsEditMode => _editing != null;

        private bool _isNpc;
        public bool IsNpc { get => _isNpc; set => this.RaiseAndSetIfChanged(ref _isNpc, value); }

        public string SaveButtonText => IsEditMode ? "Save Changes" : (IsNpc ? "Create NPC" : "Create Character");

        private static readonly string[] TokenColorPalette =
        {
            "#FFD700", "#4FC3F7", "#E57373", "#81C784", "#BA68C8", "#FFB74D",
            "#4DD0E1", "#F06292", "#AED581", "#9575CD", "#FF8A65", "#A1887F"
        };

        public ObservableCollection<ColorSwatchOption> ColorSwatches { get; } = new();

        private string _selectedColorHex = "";
        public string SelectedColorHex
        {
            get => _selectedColorHex;
            set => this.RaiseAndSetIfChanged(ref _selectedColorHex, value);
        }

        public ReactiveCommand<ColorSwatchOption, Unit> SelectColorCommand { get; }

        public ObservableCollection<XpModeOption> XpModes { get; } =
            new() { new XpModeOption("Milestone"), new XpModeOption("XP") };

        public ObservableCollection<int> LevelChoices { get; } =
            new(Enumerable.Range(1, App.PM?.Rules?.MaxLevel ?? 20));

        public ObservableCollection<ClassOption> Classes { get; } = new();
        public ObservableCollection<RaceOption> Races { get; } = new();
        public ObservableCollection<SubraceOption> Subraces { get; } = new();
        public ObservableCollection<ItemOption> Equipment { get; } = new();
        public ObservableCollection<AbilityEntry> Abilities { get; } = new();

        public ObservableCollection<MulticlassRow> MulticlassRows { get; } = new();
        public bool ShowMulticlassEditor => (App.PM?.Rules?.MulticlassingOn ?? false) && _allClasses.Count > 0;

        private readonly List<LanguageOption> _allLanguages = new();
        public ObservableCollection<string> RaceLanguageNames { get; } = new();
        public ObservableCollection<LanguagePickRow> LanguagePicks { get; } = new();
        public bool HasLanguages => _allLanguages.Count > 0;
        public bool HasRaceLanguages => RaceLanguageNames.Count > 0;
        public bool HasLanguagePicks => LanguagePicks.Count > 0;

        public ObservableCollection<ChoiceGroupViewModel> ChoiceGroups { get; } = new();

        private bool _choicesSatisfied = true;
        public bool ChoicesSatisfied
        {
            get => _choicesSatisfied;
            private set => this.RaiseAndSetIfChanged(ref _choicesSatisfied, value);
        }

        public bool HasChoices => ChoiceGroups.Count > 0;

        private string _name = "";
        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        private string _backstory = "";
        public string Backstory
        {
            get => _backstory;
            set => this.RaiseAndSetIfChanged(ref _backstory, value);
        }

        public ObservableCollection<BackstoryOption> BackstoryOptions { get; } = new();
        private BackstoryOption? _selectedBackstoryOption;
        public BackstoryOption? SelectedBackstoryOption { get => _selectedBackstoryOption; set => this.RaiseAndSetIfChanged(ref _selectedBackstoryOption, value); }
        public bool HasBackstoryOptions => BackstoryOptions.Count > 0;

        public ObservableCollection<BackgroundOption> Backgrounds { get; } = new();
        private BackgroundOption? _selectedBackground;
        public BackgroundOption? SelectedBackground { get => _selectedBackground; set => this.RaiseAndSetIfChanged(ref _selectedBackground, value); }
        public bool HasBackgrounds => Backgrounds.Count > 0;

        public ObservableCollection<string> BackgroundAbilities { get; } = new();
        public bool HasBackgroundAbilities => BackgroundAbilities.Count > 0;

        private bool _bgSpread;
        public bool BgSpread { get => _bgSpread; set { this.RaiseAndSetIfChanged(ref _bgSpread, value); this.RaisePropertyChanged(nameof(BgNotSpread)); } }
        public bool BgNotSpread => !_bgSpread;

        private string? _bgPlusTwo;
        public string? BgPlusTwo { get => _bgPlusTwo; set => this.RaiseAndSetIfChanged(ref _bgPlusTwo, value); }
        private string? _bgPlusOne;
        public string? BgPlusOne { get => _bgPlusOne; set => this.RaiseAndSetIfChanged(ref _bgPlusOne, value); }

        private string _originFeatLabel = "";
        public string OriginFeatLabel { get => _originFeatLabel; private set => this.RaiseAndSetIfChanged(ref _originFeatLabel, value); }
        public bool HasOriginFeat => _originFeatLabel.Length > 0;

        public ObservableCollection<SpellPrepEntry> Spellbook { get; } = new();
        public ObservableCollection<SpellLevelGroup> SpellGroups { get; } = new();

        public event Action<SpellPrepEntry>? OpenSpellViewRequested;
        private void OnSpellView(SpellPrepEntry entry) => OpenSpellViewRequested?.Invoke(entry);

        private bool _isSpellcaster;
        public bool IsSpellcaster
        {
            get => _isSpellcaster;
            private set
            {
                if (_isSpellcaster == value) return;
                this.RaiseAndSetIfChanged(ref _isSpellcaster, value);
                this.RaisePropertyChanged(nameof(StepCount));
                this.RaisePropertyChanged(nameof(IsLastStep));
                if (Step > StepCount - 1) Step = StepCount - 1;
            }
        }

        private int _cantripLimit;
        public int CantripLimit { get => _cantripLimit; private set { this.RaiseAndSetIfChanged(ref _cantripLimit, value); this.RaisePropertyChanged(nameof(SpellPrepLabel)); } }
        private int _preparedLimit;
        public int PreparedLimit { get => _preparedLimit; private set { this.RaiseAndSetIfChanged(ref _preparedLimit, value); this.RaisePropertyChanged(nameof(SpellPrepLabel)); } }
        private int _cantripCount;
        public int CantripCount { get => _cantripCount; private set { this.RaiseAndSetIfChanged(ref _cantripCount, value); this.RaisePropertyChanged(nameof(SpellPrepLabel)); } }
        private int _preparedSpellCount;
        public int PreparedSpellCount { get => _preparedSpellCount; private set { this.RaiseAndSetIfChanged(ref _preparedSpellCount, value); this.RaisePropertyChanged(nameof(SpellPrepLabel)); } }

        public string SpellPrepLabel => $"Cantrips {CantripCount}/{CantripLimit}    Prepared {PreparedSpellCount}/{PreparedLimit}";

        private bool _suppressSpellToggle;

        private string _equipmentSearch = "";
        public string EquipmentSearch
        {
            get => _equipmentSearch;
            set { this.RaiseAndSetIfChanged(ref _equipmentSearch, value); FilterEquipment(); }
        }

        private string _classSearch = "";
        public string ClassSearch
        {
            get => _classSearch;
            set { this.RaiseAndSetIfChanged(ref _classSearch, value); FilterClasses(); }
        }

        private string _raceSearch = "";
        public string RaceSearch
        {
            get => _raceSearch;
            set { this.RaiseAndSetIfChanged(ref _raceSearch, value); FilterRaces(); }
        }

        public ObservableCollection<string> EquipmentTypes { get; } = new();

        private string _equipmentTypeFilter = "All types";
        public string EquipmentTypeFilter
        {
            get => _equipmentTypeFilter;
            set { this.RaiseAndSetIfChanged(ref _equipmentTypeFilter, value); RebuildEquipmentProperties(); FilterEquipment(); }
        }

        public ObservableCollection<string> EquipmentProperties { get; } = new();

        private bool _hasEquipmentProperties;
        public bool HasEquipmentProperties { get => _hasEquipmentProperties; private set => this.RaiseAndSetIfChanged(ref _hasEquipmentProperties, value); }

        private string _equipmentPropertyFilter = "";
        public string EquipmentPropertyFilter
        {
            get => _equipmentPropertyFilter;
            set { this.RaiseAndSetIfChanged(ref _equipmentPropertyFilter, value); FilterEquipment(); }
        }

        private bool _proficientOnly;
        public bool ProficientOnly
        {
            get => _proficientOnly;
            set { this.RaiseAndSetIfChanged(ref _proficientOnly, value); FilterEquipment(); }
        }
        public bool CanFilterByProficiency => SelectedClass != null;

        private HashSet<string>? _heldProfIds;
        private Dictionary<string, string> _armorProfMap = new();

        private async Task ResolveEquipmentProfAsync()
        {
            if (App.PM == null || SelectedClass == null) { _heldProfIds = null; return; }
            try
            {
                var granted = await App.PM.ResolveProficienciesAsync(SelectedClass.Id, SelectedRace?.Id);
                _armorProfMap = await App.PM.GetArmorProfMapAsync();
                _heldProfIds = granted.AllIds;
                if (ProficientOnly) FilterEquipment();
            }
            catch (Exception ex) { ErrorLog.Log($"[CharacterCreation] proficiency resolve failed", ex); }
        }

        private readonly Dictionary<ItemOption, (List<string> Weapon, string? Armor)> _itemProps = new();

        private (List<string> Weapon, string? Armor) PropsOf(ItemOption o)
        {
            if (_itemProps.TryGetValue(o, out var cached)) return cached;
            var weapon = new List<string>();
            string? armor = null;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(o.DataJson) ? "{}" : o.DataJson);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("WeaponCategory", out var wc) && wc.ValueKind == JsonValueKind.Array)
                        foreach (var c in wc.EnumerateArray()) { var s = (c.GetString() ?? "").Replace("wp-", ""); if (s.Length > 0) weapon.Add(s); }
                    if (root.TryGetProperty("IsRanged", out var ir) && ir.ValueKind == JsonValueKind.True) weapon.Add("ranged");
                    else if (weapon.Count > 0) weapon.Add("melee");
                    if (root.TryGetProperty("ArmorType", out var at) && at.ValueKind == JsonValueKind.String) { var a = (at.GetString() ?? "").Replace("atype-", ""); if (a.Length > 0) armor = a; }
                }
            }
            catch (JsonException) { }
            var val = (weapon, armor);
            _itemProps[o] = val;
            return val;
        }

        private void RebuildEquipmentProperties()
        {
            EquipmentProperties.Clear();
            _equipmentPropertyFilter = "";
            this.RaisePropertyChanged(nameof(EquipmentPropertyFilter));
            if (_equipmentTypeFilter == "All types" || string.IsNullOrEmpty(_equipmentTypeFilter)) { HasEquipmentProperties = false; return; }

            var scope = _allEquipment.Where(e => string.Equals(e.ItemType, _equipmentTypeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            var weaponProps = scope.SelectMany(e => PropsOf(e).Weapon).Distinct().OrderBy(x => x).ToList();
            var armorTypes = scope.Select(e => PropsOf(e).Armor).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).Distinct().OrderBy(x => x).ToList();

            var opts = weaponProps.Count > 0 ? weaponProps : armorTypes;
            if (opts.Count == 0) { HasEquipmentProperties = false; return; }

            EquipmentProperties.Add(weaponProps.Count > 0 ? "Any property" : "Any type");
            foreach (var p in opts) EquipmentProperties.Add(p);
            _equipmentPropertyFilter = EquipmentProperties[0];
            this.RaisePropertyChanged(nameof(EquipmentPropertyFilter));
            HasEquipmentProperties = true;
        }

        private XpModeOption? _selectedXpMode;
        public XpModeOption? SelectedXpMode
        {
            get => _selectedXpMode;
            set => this.RaiseAndSetIfChanged(ref _selectedXpMode, value);
        }

        private int _level = 1;
        public int Level
        {
            get => _level;
            set { this.RaiseAndSetIfChanged(ref _level, value); RecomputeHp(); _ = RefreshChoicesAsync(); }
        }

        private ClassOption? _selectedClass;
        public ClassOption? SelectedClass
        {
            get => _selectedClass;
            set { this.RaiseAndSetIfChanged(ref _selectedClass, value); this.RaisePropertyChanged(nameof(CanFilterByProficiency)); RecomputeHp(); _ = RefreshChoicesAsync(); }
        }

        private RaceOption? _selectedRace;
        public RaceOption? SelectedRace
        {
            get => _selectedRace;
            set { this.RaiseAndSetIfChanged(ref _selectedRace, value); RefilterSubraces(); RebuildLanguageOptions(); RecomputeHp(); }
        }

        private SubraceOption? _selectedSubrace;
        public SubraceOption? SelectedSubrace
        {
            get => _selectedSubrace;
            set { this.RaiseAndSetIfChanged(ref _selectedSubrace, value); RecomputeHp(); }
        }

        private int _maxHp;
        public int MaxHp
        {
            get => _maxHp;
            private set => this.RaiseAndSetIfChanged(ref _maxHp, value);
        }

        private int _currentHp;
        public int CurrentHp
        {
            get => _currentHp;
            private set => this.RaiseAndSetIfChanged(ref _currentHp, value);
        }

        private AbilityMethod _method = AbilityMethod.PointBuy;

        public bool IsPointBuy
        {
            get => _method == AbilityMethod.PointBuy;
            set { if (value) SetMethod(AbilityMethod.PointBuy); }
        }

        public bool IsManual
        {
            get => _method == AbilityMethod.Manual;
            set { if (value) SetMethod(AbilityMethod.Manual); }
        }

        public bool IsRolling
        {
            get => _method == AbilityMethod.Roll;
            set { if (value) SetMethod(AbilityMethod.Roll); }
        }

        private int _pointsRemaining = App.PM?.Rules?.PointBuyBudget ?? new GameRules().PointBuyBudget;
        public int PointsRemaining
        {
            get => _pointsRemaining;
            private set
            {
                this.RaiseAndSetIfChanged(ref _pointsRemaining, value);
                this.RaisePropertyChanged(nameof(PointsRemainingLabel));
            }
        }

        public string PointsRemainingLabel => $"{PointsRemaining} points left";

        public ReactiveCommand<Unit, Unit> FillStandardArrayCommand { get; }
        public ReactiveCommand<string, Unit> UseAbilityMethodCommand { get; }
        public ReactiveCommand<Unit, Unit> RollAllAbilitiesCommand { get; }
        public ReactiveCommand<Unit, Unit> AddMulticlassRowCommand { get; }
        public ReactiveCommand<MulticlassRow, Unit> RemoveMulticlassRowCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyBackstoryCommand { get; }
        public ReactiveCommand<Unit, Unit> NextStepCommand { get; }
        public ReactiveCommand<Unit, Unit> BackStepCommand { get; }

        public int StepCount => _isSpellcaster ? 8 : 7;
        private int _step;
        public int Step
        {
            get => _step;
            set
            {
                this.RaiseAndSetIfChanged(ref _step, Math.Clamp(value, 0, StepCount - 1));
                this.RaisePropertyChanged(nameof(IsFirstStep));
                this.RaisePropertyChanged(nameof(IsLastStep));
                if (SelectedClass != null) _ = LoadSpellChoicesAsync();
            }
        }
        public bool IsFirstStep => _step <= 0;
        public bool IsLastStep => _step >= StepCount - 1;

        private string _blocker = "";
        public string Blocker
        {
            get => _blocker;
            private set { this.RaiseAndSetIfChanged(ref _blocker, value); this.RaisePropertyChanged(nameof(HasBlocker)); }
        }
        public bool HasBlocker => _blocker.Length > 0;

        private void RecomputeBlocker()
        {
            string b = "";
            if (string.IsNullOrWhiteSpace(Name)) b = "Give your character a name.";
            else if (HasRaces && SelectedRace == null) b = "Pick a species.";
            else if (HasSubraces && SelectedSubrace == null) b = "Pick your species lineage or ancestry.";
            else if (HasClasses && SelectedClass == null) b = "Pick a class.";
            else if (IsPointBuy && PointsRemaining < 0) b = "You have overspent your ability points.";
            else if (!ChoicesSatisfied && !IsEditMode) b = "Some required choices are still open, check the Choices tab.";
            Blocker = b;
        }

        public CharacterCreationViewModel() : this(null) { }

        public CharacterCreationViewModel(CampaignViewModel? parent) : this(parent, null) { }

        public static CharacterCreationViewModel ForNpc(CampaignViewModel? parent)
        {
            var vm = new CharacterCreationViewModel(parent, null);
            vm.IsNpc = true;
            return vm;
        }

        public CharacterCreationViewModel(CampaignViewModel? parent, CharacterRuntime? editing)
        {
            _parent = parent;
            _editing = editing;
            SelectedXpMode = XpModes[0];

            var adefs = App.PM?.Rules?.Abilities;
            if (adefs != null && adefs.Count > 0)
                foreach (var d in adefs)
                    Abilities.Add(new AbilityEntry(d.Name, d.Short, OnAbilityChanged, d.Id, CanIncreaseAbility, RollAbility));
            else
                foreach (var (name, abbrev, id) in new[]
                {
                    ("Strength", "STR", "ability-str"), ("Dexterity", "DEX", "ability-dex"), ("Constitution", "CON", "ability-con"),
                    ("Intelligence", "INT", "ability-int"), ("Wisdom", "WIS", "ability-wis"), ("Charisma", "CHA", "ability-cha")
                })
                    Abilities.Add(new AbilityEntry(App.PM?.Rules?.AbilityName(abbrev, name) ?? name, abbrev, OnAbilityChanged, id, CanIncreaseAbility, RollAbility));

            SetMethod(AbilityMethod.PointBuy);

            FillStandardArrayCommand = ReactiveCommand.Create(FillStandardArray);
            UseAbilityMethodCommand = ReactiveCommand.Create<string>(which =>
                SetMethod(which switch
                {
                    "manual" => AbilityMethod.Manual,
                    "roll" => AbilityMethod.Roll,
                    _ => AbilityMethod.PointBuy
                }));
            RollAllAbilitiesCommand = ReactiveCommand.Create(() => { foreach (var a in Abilities) RollAbility(a); });
            SelectColorCommand = ReactiveCommand.Create<ColorSwatchOption>(PickColor);
            AddMulticlassRowCommand = ReactiveCommand.Create(() => { MulticlassRows.Add(new MulticlassRow()); });
            RemoveMulticlassRowCommand = ReactiveCommand.Create<MulticlassRow>(r => MulticlassRows.Remove(r));

            ApplyBackstoryCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedBackstoryOption != null) Backstory = SelectedBackstoryOption.Description;
            });

            var canCreate = this.WhenAnyValue(
                x => x.Name, x => x.SelectedClass, x => x.SelectedRace, x => x.SelectedSubrace, x => x.PointsRemaining, x => x.ChoicesSatisfied,
                (n, c, r, sr, pts, choicesOk) =>
                    !string.IsNullOrWhiteSpace(n) && (c != null || !HasClasses) && (r != null || !HasRaces) && (!HasSubraces || sr != null) && (!IsPointBuy || pts >= 0) && (choicesOk || IsEditMode));

            CreateCommand = ReactiveCommand.CreateFromTask(CreateAsync, canCreate);
            NextStepCommand = ReactiveCommand.Create(() => { Step += 1; });
            BackStepCommand = ReactiveCommand.Create(() => { Step -= 1; });

            this.WhenAnyValue(x => x.Name, x => x.SelectedClass, x => x.SelectedRace, x => x.SelectedSubrace, x => x.PointsRemaining, x => x.ChoicesSatisfied)
                .Subscribe(_ => RecomputeBlocker());
            this.WhenAnyValue(x => x.SelectedBackground).Subscribe(_ => OnBackgroundChanged());
            this.WhenAnyValue(x => x.BgSpread, x => x.BgPlusTwo, x => x.BgPlusOne).Subscribe(_ => RecomputeHp());
            CancelCommand = ReactiveCommand.Create(() => _parent?.CancelCharacterCreation());
            CreateCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[CharacterCreation] create failed", ex));
            FillStandardArrayCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[CharacterCreation] fill failed", ex));
            CancelCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[CharacterCreation] cancel failed", ex));
            ApplyBackstoryCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[CharacterCreation] backstory apply failed", ex));
            _ = LoadAsync();
            _ = LoadBackstoryOptionsAsync();
            _ = LoadBackgroundOptionsAsync();
            _ = LoadLanguageOptionsAsync();
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;

            await using var conn = await App.PM.DbManager.OpenAsync();

            await using (var cmd = conn.CreateCommand())
            {
                var editionFilter = App.PM.GetRulesVersionFilter();
                cmd.CommandText = "SELECT Id, Name, Description, HitDiceId, PrimaryAbility, Version FROM Classes ORDER BY Name COLLATE NOCASE";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var ver = r.IsDBNull(5) ? "2014" : r.GetString(5);
                    if (!VersionVisible(ver, editionFilter)) continue;
                    _allClasses.Add(new ClassOption(
                        r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2),
                        r.GetString(3), r.GetString(4)));
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                var editionFilter = App.PM.GetRulesVersionFilter();
                cmd.CommandText = $"SELECT Id, Name, Description, Size, Speed, Version, {CatalogResolver.ResolvedJsonSql("Races", "Races")} FROM Races ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var ver = r.IsDBNull(5) ? "2014" : r.GetString(5);
                    if (!VersionVisible(ver, editionFilter)) continue;
                    var opt = new RaceOption(
                        r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2),
                        r.GetString(3), r.GetInt32(4), ver);
                    var dataJson = r.IsDBNull(6) ? null : r.GetString(6);
                    opt.BonusValues = ParseBonusValues(dataJson);
                    opt.LanguageIds = ParseLanguageIds(dataJson);
                    _allRaces.Add(opt);
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                var editionFilter = App.PM.GetRulesVersionFilter();
                cmd.CommandText = $"SELECT Id, Name, ParentRaceId, Description, Version, {CatalogResolver.ResolvedJsonSql("Subraces", "Subraces")} FROM Subraces ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var ver = r.IsDBNull(4) ? "2014" : r.GetString(4);
                    if (!VersionVisible(ver, editionFilter)) continue;
                    var opt = new SubraceOption(
                        r.GetString(0), r.GetString(1), r.GetString(2),
                        r.IsDBNull(3) ? "" : r.GetString(3));
                    opt.BonusValues = ParseBonusValues(r.IsDBNull(5) ? null : r.GetString(5));
                    _allSubraces.Add(opt);
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT i.Id, i.Name, i.ItemType, i.DataJson
                    FROM Items i
                    INNER JOIN CampaignItems ci ON ci.ItemId = i.Id
                    WHERE ci.CampaignId = $cid AND ci.IsEnabled = 1
                    ORDER BY i.Name COLLATE NOCASE
                    """;
                cmd.Parameters.AddWithValue("$cid", App.PM.GetCampaignId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    _allEquipment.Add(new ItemOption(r.GetString(0), r.GetString(1), r.GetString(2),
                        r.IsDBNull(3) ? "" : r.GetString(3)));
            }

            if (_allEquipment.Count == 0)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Id, Name, ItemType, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    _allEquipment.Add(new ItemOption(r.GetString(0), r.GetString(1), r.GetString(2),
                        r.IsDBNull(3) ? "" : r.GetString(3)));
            }

            FilterClasses();
            FilterRaces();
            FilterEquipment();
            this.RaisePropertyChanged(nameof(ShowMulticlassEditor));

            if (_editing != null)
                PrefillFromEditing(_editing);

            await BuildColorSwatchesAsync();
        }

        private async Task LoadBackstoryOptionsAsync()
        {
            if (App.PM == null) return;
            try
            {
                var options = await App.PM.LoadBackstoriesAsync();
                BackstoryOptions.Clear();
                foreach (var o in options) BackstoryOptions.Add(o);
                this.RaisePropertyChanged(nameof(HasBackstoryOptions));
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[CharacterCreation] backstory load failed", ex);
            }
        }

        private readonly Dictionary<string, string> _featNames = new();

        private async Task LoadBackgroundOptionsAsync()
        {
            if (App.PM == null) return;
            try
            {
                foreach (var kv in await App.PM.LoadFeatNamesAsync()) _featNames[kv.Key] = kv.Value;

                var options = await App.PM.LoadBackgroundsAsync();
                Backgrounds.Clear();
                foreach (var o in options) Backgrounds.Add(o);
                this.RaisePropertyChanged(nameof(HasBackgrounds));
                if (_editing != null && !string.IsNullOrEmpty(_editing.BackgroundId))
                {
                    SelectedBackground = Backgrounds.FirstOrDefault(b => b.Id == _editing.BackgroundId);
                    if (_editing.AbilityBumps != null && HasBackgroundAbilities)
                    {
                        BgSpread = _editing.BgSpread;
                        if (!string.IsNullOrEmpty(_editing.BgPlusTwo)) BgPlusTwo = _editing.BgPlusTwo;
                        if (!string.IsNullOrEmpty(_editing.BgPlusOne)) BgPlusOne = _editing.BgPlusOne;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[CharacterCreation] background load failed", ex);
            }
        }

        private async Task LoadLanguageOptionsAsync()
        {
            if (App.PM == null) return;
            try
            {
                var options = await App.PM.LoadLanguagesAsync();
                _allLanguages.Clear();
                foreach (var o in options) _allLanguages.Add(o);
                RebuildLanguageOptions();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[CharacterCreation] language load failed", ex);
            }
        }

        // Races carry language ids, backgrounds just write it in prose, so the free pick count comes off a regex. Not great.
        public static int ParseLanguagePickCount(string? backgroundDescription)
        {
            if (string.IsNullOrEmpty(backgroundDescription)) return 0;
            var m = Regex.Match(backgroundDescription, @"Languages?:\s*(one|two|three|\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            return m.Groups[1].Value.ToLowerInvariant() switch
            {
                "one" => 1,
                "two" => 2,
                "three" => 3,
                var w => int.TryParse(w, out var n) ? n : 0
            };
        }

        private void RebuildLanguageOptions()
        {
            var raceIds = SelectedRace?.LanguageIds ?? new List<string>();
            RaceLanguageNames.Clear();
            if (_allLanguages.Count > 0)
                foreach (var id in raceIds)
                    RaceLanguageNames.Add(_allLanguages.FirstOrDefault(l => l.Id == id)?.Name ?? id);

            var kept = LanguagePicks.Where(p => p.Selected != null).Select(p => p.Selected!.Id).ToList();
            if (kept.Count == 0 && _editing != null)
                kept = _editing.Languages.Where(id => !raceIds.Contains(id)).ToList();

            var options = _allLanguages.Where(l => !raceIds.Contains(l.Id)).ToList();
            var n = ParseLanguagePickCount(SelectedBackground?.Description);
            LanguagePicks.Clear();
            for (int i = 0; i < n && options.Count > 0; i++)
            {
                var row = new LanguagePickRow(options);
                if (i < kept.Count) row.Selected = options.FirstOrDefault(o => o.Id == kept[i]);
                LanguagePicks.Add(row);
            }

            this.RaisePropertyChanged(nameof(HasLanguages));
            this.RaisePropertyChanged(nameof(HasRaceLanguages));
            this.RaisePropertyChanged(nameof(HasLanguagePicks));
        }

        private List<string> LanguageIdsForSave()
        {
            var ids = new List<string>();
            if (_allLanguages.Count == 0) return ids;
            foreach (var id in SelectedRace?.LanguageIds ?? new List<string>())
                if (!ids.Contains(id)) ids.Add(id);
            foreach (var p in LanguagePicks)
                if (p.Selected != null && !ids.Contains(p.Selected.Id)) ids.Add(p.Selected.Id);
            return ids;
        }

        private void OnBackgroundChanged()
        {
            BackgroundAbilities.Clear();
            var bg = SelectedBackground;
            if (bg != null) foreach (var a in bg.AbilityIds) BackgroundAbilities.Add(a);
            this.RaisePropertyChanged(nameof(HasBackgroundAbilities));
            BgSpread = false;
            BgPlusTwo = BackgroundAbilities.Count > 0 ? BackgroundAbilities[0] : null;
            BgPlusOne = BackgroundAbilities.Count > 1 ? BackgroundAbilities[1] : null;

            var feat = bg?.FeatIds.FirstOrDefault();
            OriginFeatLabel = !string.IsNullOrEmpty(feat) && _featNames.TryGetValue(feat!, out var fn) ? fn : "";
            this.RaisePropertyChanged(nameof(HasOriginFeat));
            RebuildLanguageOptions();
            RebuildExpertiseOptions();
            RecomputeChoicesSatisfied();
            RecomputeHp();
        }

        private Dictionary<string, int> BackgroundBumps()
        {
            var d = new Dictionary<string, int>();
            if (!HasBackgroundAbilities) return d;
            if (BgSpread) { foreach (var a in BackgroundAbilities) d[a] = 1; }
            else
            {
                if (!string.IsNullOrEmpty(BgPlusTwo)) d[BgPlusTwo!] = 2;
                if (!string.IsNullOrEmpty(BgPlusOne) && BgPlusOne != BgPlusTwo) d[BgPlusOne!] = 1;
            }
            return d;
        }

        private static Dictionary<string, int> ParseBonusValues(string? dataJson)
        {
            var d = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(dataJson)) return d;
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.TryGetProperty("BonusValues", out var bv) && bv.ValueKind == JsonValueKind.Object)
                    foreach (var p in bv.EnumerateObject())
                        if (p.Value.TryGetInt32(out var n)) d[p.Name] = n;
            }
            catch (JsonException) { }
            return d;
        }

        private static List<string> ParseLanguageIds(string? dataJson)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(dataJson)) return list;
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.TryGetProperty("LanguageIds", out var li) && li.ValueKind == JsonValueKind.Array)
                    foreach (var x in li.EnumerateArray())
                        if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                            list.Add(x.GetString()!);
            }
            catch (JsonException) { }
            return list;
        }

        private void PrefillFromEditing(CharacterRuntime rt)
        {
            Name = rt.Name;
            Backstory = rt.Backstory ?? "";
            SelectedClass = Classes.FirstOrDefault(c => c.Id == rt.ClassId);
            SelectedRace = Races.FirstOrDefault(r => r.Id == rt.RaceId);
            if (rt.SubraceId != null)
                SelectedSubrace = Subraces.FirstOrDefault(s => s.Id == rt.SubraceId);
            Level = rt.Level;

            MulticlassRows.Clear();
            if (rt.ClassLevels.Count > 1)
                foreach (var c in rt.ClassLevels)
                    MulticlassRows.Add(new MulticlassRow
                    {
                        SelectedClass = _allClasses.FirstOrDefault(x => string.Equals(x.Id, c.ClassId, StringComparison.OrdinalIgnoreCase)),
                        Level = c.Level
                    });

            SetMethod(AbilityMethod.Manual);
            foreach (var e in Abilities)
            {
                if (string.IsNullOrEmpty(e.Id)) continue;
                var score = rt.AbilityScores.Get(e.Id);
                if (rt.AbilityBumps != null)
                {
                    rt.AbilityBumps.TryGetValue(e.Id, out var creation);
                    rt.LevelUpBumps.TryGetValue(e.Id, out var lvl);
                    score -= creation + lvl;
                }
                e.Score = score;
            }

            foreach (var e in _allEquipment)
                e.IsSelected = rt.InventoryInstanceIds.Contains(e.Id);

            this.RaisePropertyChanged(nameof(IsEditMode));
            this.RaisePropertyChanged(nameof(SaveButtonText));
        }

        private void RefilterSubraces()
        {
            Subraces.Clear();
            SelectedSubrace = null;
            if (SelectedRace == null) return;
            foreach (var s in _allSubraces.Where(s => s.ParentRaceId == SelectedRace.Id))
                Subraces.Add(s);
            this.RaisePropertyChanged(nameof(HasSubraces));
            RecomputeBlocker();
        }

        public bool HasSubraces => Subraces.Count > 0;

        // Full lists, not the filtered ones, or searching down to nothing convinces the blocker there are no classes.
        public bool HasClasses => _allClasses.Count > 0;
        public bool HasRaces => _allRaces.Count > 0;

        private void FilterClasses()
        {
            var q = _classSearch?.Trim();
            var cur = SelectedClass;
            Classes.Clear();
            IEnumerable<ClassOption> source = _allClasses;
            if (!string.IsNullOrEmpty(q))
                source = source.Where(c =>
                    c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || c.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
            foreach (var c in source) Classes.Add(c);
            if (cur != null && !Classes.Contains(cur)) Classes.Add(cur);
            if (cur != null && Classes.Contains(cur)) SelectedClass = cur;
        }

        private void FilterRaces()
        {
            var q = _raceSearch?.Trim();
            var curRace = SelectedRace;
            var curSub = SelectedSubrace;
            Races.Clear();
            IEnumerable<RaceOption> source = _allRaces;
            if (!string.IsNullOrEmpty(q))
                source = source.Where(r =>
                    r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
            foreach (var r in source) Races.Add(r);
            if (curRace != null && !Races.Contains(curRace)) Races.Add(curRace);
            if (curRace != null && Races.Contains(curRace))
            {
                // The listbox nulls the selection when the items vanish, and putting it back refilters subraces, so the subrace pick has to be restored by hand.
                SelectedRace = curRace;
                if (curSub != null && Subraces.Contains(curSub)) SelectedSubrace = curSub;
            }
        }

        private void FilterEquipment()
        {
            if (EquipmentTypes.Count == 0 && _allEquipment.Count > 0)
            {
                EquipmentTypes.Add("All types");
                foreach (var t in _allEquipment.Select(e => e.ItemType).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t))
                    EquipmentTypes.Add(t);
            }

            var q = _equipmentSearch?.Trim();
            Equipment.Clear();

            IEnumerable<ItemOption> source = _allEquipment;
            if (ProficientOnly && _heldProfIds != null)
                source = source.Where(e => ProficiencyResolver.ItemUsable(e.DataJson, _heldProfIds, _armorProfMap));
            if (_equipmentTypeFilter != "All types" && !string.IsNullOrEmpty(_equipmentTypeFilter))
                source = source.Where(e => string.Equals(e.ItemType, _equipmentTypeFilter, StringComparison.OrdinalIgnoreCase));
            var prop = HasEquipmentProperties && !string.IsNullOrEmpty(_equipmentPropertyFilter) && !_equipmentPropertyFilter.StartsWith("Any", StringComparison.OrdinalIgnoreCase) ? _equipmentPropertyFilter : null;
            if (prop != null)
                source = source.Where(e => PropsOf(e).Weapon.Contains(prop) || string.Equals(PropsOf(e).Armor, prop, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(q))
                source = source.Where(e =>
                    e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || e.ItemType.Contains(q, StringComparison.OrdinalIgnoreCase));

            foreach (var e in source) Equipment.Add(e);
        }

        private void SetMethod(AbilityMethod method)
        {
            _method = method;
            var pointBuy = method == AbilityMethod.PointBuy;

            this.RaisePropertyChanged(nameof(IsPointBuy));
            this.RaisePropertyChanged(nameof(IsManual));
            this.RaisePropertyChanged(nameof(IsRolling));

            var rules = App.PM?.Rules ?? new GameRules();
            foreach (var a in Abilities)
            {
                a.Min = pointBuy ? rules.PointBuyMinScore : rules.ManualMinScore;
                a.Max = pointBuy ? rules.PointBuyMaxScore : rules.ManualMaxScore;
                if (a.Score < a.Min) a.Score = a.Min;
                if (a.Score > a.Max) a.Score = a.Max;
            }

            if (pointBuy)
            {
                int spent = Abilities.Sum(a => rules.PointBuyCosts.TryGetValue(a.Score, out var c) ? c : 0);
                if (spent > rules.PointBuyBudget)
                    foreach (var a in Abilities) a.Score = rules.PointBuyMinScore;
            }

            OnAbilityChanged();
        }

        private void FillStandardArray()
        {
            SetMethod(AbilityMethod.Manual);
            var array = (App.PM?.Rules ?? new GameRules()).StandardArray;
            for (int i = 0; i < Abilities.Count && i < array.Count; i++)
                Abilities[i].Score = array[i];
            OnAbilityChanged();
        }

        private void OnAbilityChanged()
        {
            if (IsPointBuy)
            {
                var rules = App.PM?.Rules ?? new GameRules();
                int spent = Abilities.Sum(a => rules.PointBuyCosts.TryGetValue(a.Score, out var c) ? c : 0);
                PointsRemaining = rules.PointBuyBudget - spent;
            }
            RecomputeHp();
        }

        // The per score cap is not enough on its own, six maxed scores all pass it while the total goes over, so I check the cost fits in what is left too
        private bool CanIncreaseAbility(AbilityEntry e)
        {
            if (!IsPointBuy) return true;
            var rules = App.PM?.Rules ?? new GameRules();
            var current = rules.PointBuyCosts.TryGetValue(e.Score, out var c) ? c : 0;
            var next = rules.PointBuyCosts.TryGetValue(e.Score + 1, out var nx) ? nx : current;
            return next - current <= PointsRemaining;
        }

        private void RollAbility(AbilityEntry e)
        {
            if (!IsRolling) return;
            var rules = App.PM?.Rules ?? new GameRules();
            if (!DiceManager.TryRoll(rules.AbilityRollDice, out var res) || res == null) return;
            e.Score = Math.Clamp(res.Total, rules.ManualMinScore, rules.ManualMaxScore);
        }

        private int AbilityScoreOf(string abbrev) => Abilities.FirstOrDefault(a => a.Abbrev == abbrev)?.Score ?? 10;

        private AbilityScores AbilityScoresFromEntries(IReadOnlyDictionary<string, int>? bumps = null, int cap = int.MaxValue)
        {
            var s = new AbilityScores();
            foreach (var a in Abilities)
            {
                var score = a.Score + (bumps != null && bumps.TryGetValue(a.Abbrev, out var v) ? v : 0);
                if (cap < int.MaxValue) score = Math.Min(cap, score);
                if (!string.IsNullOrEmpty(a.Id)) s.Set(a.Id, score);
            }
            return s;
        }

        private void ClearSpells()
        {
            Spellbook.Clear();
            SpellGroups.Clear();
            IsSpellcaster = false;
            CantripLimit = 0;
            PreparedLimit = 0;
            RecountPreparedSpells();
        }

        private void RebuildSpellGroups()
        {
            SpellGroups.Clear();
            foreach (var g in Spellbook.GroupBy(s => s.Level).OrderBy(g => g.Key))
            {
                var group = new SpellLevelGroup(App.PM?.Rules?.SpellLevelName(g.Key) ?? (g.Key == 0 ? "Cantrips" : "Level " + g.Key));
                foreach (var s in g) group.Spells.Add(s);
                SpellGroups.Add(group);
            }
        }

        private void RecountPreparedSpells()
        {
            CantripCount = Spellbook.Count(s => s.Level == 0 && s.IsPrepared);
            PreparedSpellCount = Spellbook.Count(s => s.Level > 0 && s.IsPrepared);
        }

        private void OnSpellPrepToggled(SpellPrepEntry entry)
        {
            if (_suppressSpellToggle) return;
            if (entry.IsPrepared)
            {
                if (entry.Level == 0 && CantripLimit > 0 && Spellbook.Count(s => s.Level == 0 && s.IsPrepared) > CantripLimit)
                {
                    _suppressSpellToggle = true; entry.IsPrepared = false; _suppressSpellToggle = false; return;
                }
                if (entry.Level > 0 && PreparedLimit > 0 && Spellbook.Count(s => s.Level > 0 && s.IsPrepared) > PreparedLimit)
                {
                    _suppressSpellToggle = true; entry.IsPrepared = false; _suppressSpellToggle = false; return;
                }
            }
            RecountPreparedSpells();
        }

        private int _spellLoadToken;

        private async Task LoadSpellChoicesAsync()
        {
            var token = ++_spellLoadToken;
            if (App.PM == null || SelectedClass == null) { ClearSpells(); return; }
            try
            {
                var preview = new CharacterRuntime
                {
                    ClassId = SelectedClass.Id,
                    Level = Level,
                    AbilityScores = AbilityScoresFromEntries()
                };

                var sc = await App.PM.ResolveSpellcastingAsync(preview);
                if (token != _spellLoadToken) return;
                if (sc == null) { ClearSpells(); return; }

                var limits = await App.PM.ResolveSpellPrepLimitsAsync(preview);
                var spells = await App.PM.LoadCastableSpellsAsync(preview);
                if (token != _spellLoadToken) return;

                var prepared = new HashSet<string>(Spellbook.Where(s => s.IsPrepared).Select(s => s.SpellId));
                if (prepared.Count == 0 && _editing?.PreparedSpellIds != null)
                    foreach (var id in _editing.PreparedSpellIds) prepared.Add(id);

                _suppressSpellToggle = true;
                Spellbook.Clear();
                foreach (var s in spells)
                    Spellbook.Add(new SpellPrepEntry(s, prepared.Contains(s.Id), OnSpellPrepToggled, OnSpellView));
                RebuildSpellGroups();
                _suppressSpellToggle = false;

                CantripLimit = limits.Cantrips;
                PreparedLimit = limits.Prepared;
                _suppressSpellToggle = true;
                var cantripsKept = 0;
                var preparedKept = 0;
                foreach (var s in Spellbook)
                {
                    if (!s.IsPrepared) continue;
                    if (s.Level == 0)
                    {
                        cantripsKept++;
                        if (CantripLimit > 0 && cantripsKept > CantripLimit) s.IsPrepared = false;
                    }
                    else
                    {
                        preparedKept++;
                        if (PreparedLimit > 0 && preparedKept > PreparedLimit) s.IsPrepared = false;
                    }
                }
                _suppressSpellToggle = false;
                IsSpellcaster = Spellbook.Count > 0;
                RecountPreparedSpells();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[CharacterCreation] spell choices load failed", ex);
            }
        }

        private int _choicesLoadToken;

        private async Task RefreshChoicesAsync()
        {
            var token = ++_choicesLoadToken;
            _ = LoadSpellChoicesAsync();
            _ = ResolveEquipmentProfAsync();

            foreach (var g in ChoiceGroups)
                foreach (var o in g.Options)
                    o.PropertyChanged -= OnChoiceOptionChanged;
            ChoiceGroups.Clear();

            var cls = SelectedClass;
            if (App.PM == null || cls == null)
            {
                this.RaisePropertyChanged(nameof(HasChoices));
                RecomputeChoicesSatisfied();
                return;
            }

            var resolved = await App.PM.ReadBuilderChoicesAsync(cls.Id, Level);
            if (token != _choicesLoadToken) return;
            foreach (var grp in CharacterChoiceEngine.BuildGroups(resolved))
            {
                foreach (var o in grp.Options)
                    o.PropertyChanged += OnChoiceOptionChanged;
                ChoiceGroups.Add(grp);
            }
            RebuildExpertiseOptions();

            if (IsEditMode && _editing?.AbilityBumps != null && _editing.CreationAsiPicks.Count > 0)
            {
                var remaining = new List<string>(_editing.CreationAsiPicks);
                foreach (var g in CharacterChoiceEngine.OfStore(ChoiceGroups, (App.PM?.Rules ?? new GameRules()).FeatStoreKey))
                    foreach (var pick in remaining.ToList())
                    {
                        var opt = g.Options.FirstOrDefault(x => x.Id == pick && !x.IsSelected);
                        if (opt != null && g.SelectedCount < g.ChooseCount)
                        {
                            opt.IsSelected = true;
                            remaining.Remove(pick);
                            break;
                        }
                    }
            }

            this.RaisePropertyChanged(nameof(HasChoices));
            RecomputeChoicesSatisfied();
        }

        private void OnChoiceOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChoiceOptionViewModel.IsSelected))
            {
                var rules = App.PM?.Rules ?? new GameRules();
                if (sender is ChoiceOptionViewModel opt
                    && ChoiceGroups.Any(g => rules.IsSkillStore(g.StoreAs) && g.Options.Contains(opt)))
                    RebuildExpertiseOptions();
                RecomputeChoicesSatisfied();
            }
        }

        private void RebuildExpertiseOptions()
        {
            var rules = App.PM?.Rules ?? new GameRules();
            var pool = CharacterChoiceEngine.OfStore(ChoiceGroups, rules.SkillStoreKey)
                .SelectMany(g => g.Selected.Select(o => o.Name))
                .ToList();
            if (SelectedBackground != null && rules.Skills != null)
                foreach (var sid in SelectedBackground.SkillIds)
                {
                    var nm = rules.Skills.FirstOrDefault(s => s.Id == sid)?.Name;
                    if (!string.IsNullOrEmpty(nm) && !pool.Contains(nm!)) pool.Add(nm!);
                }

            foreach (var g in CharacterChoiceEngine.OfStore(ChoiceGroups, rules.ExpertiseStoreKey))
            {
                foreach (var o in g.Options)
                    o.PropertyChanged -= OnChoiceOptionChanged;
                g.ResetOptions(pool.Distinct().Select(n => new ChoiceOption(n, n, "")));
                foreach (var o in g.Options)
                    o.PropertyChanged += OnChoiceOptionChanged;
            }
        }

        private void RecomputeChoicesSatisfied()
        {
            ChoicesSatisfied = ChoiceGroups.Count == 0 || CharacterChoiceEngine.AllSatisfied(ChoiceGroups);
        }

        private void RecomputeHp()
        {
            int die = ParseHitDie(SelectedClass?.HitDiceId);
            if (die <= 0) { MaxHp = 0; CurrentHp = 0; return; }

            var rules = App.PM?.Rules ?? new GameRules();
            var hpAbility = rules.HitPointAbility;
            var hpEntry = Abilities.FirstOrDefault(a => string.Equals(a.Abbrev, hpAbility, StringComparison.OrdinalIgnoreCase));
            int hpScore = hpEntry?.Score ?? 10;
            var bump = BackgroundBumps().FirstOrDefault(kv => string.Equals(kv.Key, hpAbility, StringComparison.OrdinalIgnoreCase));
            var extra = bump.Key != null ? bump.Value : 0;
            if (hpEntry != null && SelectedRace != null)
                extra += SelectedRace.BonusValues.FirstOrDefault(kv => string.Equals(kv.Key, hpEntry.Id, StringComparison.OrdinalIgnoreCase)).Value;
            if (hpEntry != null && SelectedSubrace != null)
                extra += SelectedSubrace.BonusValues.FirstOrDefault(kv => string.Equals(kv.Key, hpEntry.Id, StringComparison.OrdinalIgnoreCase)).Value;
            if (extra != 0) hpScore = Math.Min(rules.AbilityScoreCap, hpScore + extra);
            int hpMod = Mod(hpScore);

            int perLevel = rules.HitDieHeal(die, rules.HpUsesAverage);
            int hp = (rules.HpFirstLevelMax ? die : perLevel) + hpMod;
            for (int lvl = 2; lvl <= Level; lvl++) hp += perLevel + hpMod;

            MaxHp = Math.Max(1, hp);
            CurrentHp = MaxHp;
        }

        private static bool VersionVisible(string? entryVersion, string? campaignFilter) =>
            (App.PM?.Rules ?? new GameRules()).VisibleInEdition(entryVersion, campaignFilter);

        private static int Mod(int score) => App.PM?.AbilityMod(score) ?? (int)Math.Floor((score - 10) / 2.0);

        private static int ParseHitDie(string? hitDiceId)
        {
            if (string.IsNullOrEmpty(hitDiceId)) return 0;
            var digits = new string(hitDiceId.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var d) ? d : 0;
        }

        private async Task CreateAsync()
        {
            if (App.PM == null) return;

            var bumps = BackgroundBumps();
            // 2014 races keep their score bumps in BonusValues, 2024 races put them on the background so those carry none.
            if (SelectedRace != null)
                foreach (var kv in SelectedRace.BonusValues)
                {
                    var ab = Abilities.FirstOrDefault(a => string.Equals(a.Id, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (ab == null) continue;
                    bumps.TryGetValue(ab.Abbrev, out var cur);
                    bumps[ab.Abbrev] = cur + kv.Value;
                }
            if (SelectedSubrace != null)
                foreach (var kv in SelectedSubrace.BonusValues)
                {
                    var ab = Abilities.FirstOrDefault(a => string.Equals(a.Id, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (ab == null) continue;
                    bumps.TryGetValue(ab.Abbrev, out var cur);
                    bumps[ab.Abbrev] = cur + kv.Value;
                }
            var rules = App.PM.Rules ?? new GameRules();
            var featPicks = CharacterChoiceEngine.OfStore(ChoiceGroups, rules.FeatStoreKey)
                .SelectMany(g => g.Selected.Select(o => o.Id))
                .ToList();
            var asiPicks = featPicks.Where(p => rules.AsiAbilityFromToken(p) != null).ToList();
            foreach (var pick in asiPicks)
            {
                var abbrev = rules.AsiAbilityFromToken(pick);
                var ab = Abilities.FirstOrDefault(a => string.Equals(a.Abbrev, abbrev, StringComparison.OrdinalIgnoreCase));
                if (ab == null) continue;
                bumps.TryGetValue(ab.Abbrev, out var cur);
                bumps[ab.Abbrev] = cur + rules.AbilityScoreIncrementPerAsi;
            }
            var abilityScores = AbilityScoresFromEntries(bumps, rules.AbilityScoreCap);
            var abilityJson = JsonSerializer.Serialize(abilityScores);

            var idBumps = new Dictionary<string, int>();
            foreach (var a in Abilities)
                if (!string.IsNullOrEmpty(a.Id))
                {
                    var delta = abilityScores.Get(a.Id) - a.Score;
                    if (delta != 0) idBumps[a.Id] = delta;
                }

            var invIds = _allEquipment.Where(e => e.IsSelected).Select(e => e.Id).ToList();
            var invJson = JsonSerializer.Serialize(invIds);

            var chosenSkills = CharacterChoiceEngine.OfStore(ChoiceGroups, rules.SkillStoreKey)
                .SelectMany(g => g.Selected.Select(o => o.Name))
                .Distinct()
                .ToList();

            if (SelectedBackground != null && App.PM.Rules?.Skills != null)
                foreach (var sid in SelectedBackground.SkillIds)
                {
                    var nm = App.PM.Rules.Skills.FirstOrDefault(s => s.Id == sid)?.Name;
                    if (!string.IsNullOrEmpty(nm) && !chosenSkills.Contains(nm)) chosenSkills.Add(nm);
                }

            var chosenFeatures = CharacterChoiceEngine.OfStore(ChoiceGroups, rules.SubclassStoreKey)
                .SelectMany(g => g.Selected.Select(o => rules.SubclassFeatureLine(o.Name)))
                .ToList();

            if (SelectedBackground != null)
                foreach (var fid in SelectedBackground.FeatIds)
                    if (!string.IsNullOrEmpty(fid)) chosenFeatures.Add(rules.FeatFeatureLine(fid));

            foreach (var pick in featPicks)
            {
                if (rules.AsiAbilityFromToken(pick) != null) continue;
                var line = rules.FeatFeatureLine(pick);
                if (!chosenFeatures.Contains(line)) chosenFeatures.Add(line);
            }

            var chosenExpertise = CharacterChoiceEngine.OfStore(ChoiceGroups, rules.ExpertiseStoreKey)
                .SelectMany(g => g.Selected.Select(o => o.Name))
                .Distinct()
                .ToList();

            var granted = await App.PM.ResolveProficienciesAsync(SelectedClass?.Id, SelectedRace?.Id);
            var profList = granted.Armor
                .Concat(granted.Weapon)
                .Concat(granted.Other)
                .Distinct()
                .ToList();

            var answeredChoiceKeys = ChoiceGroups
                .Where(g => g.SelectedCount > 0)
                .Select(g => rules.AnsweredChoiceKey(g.Id, g.Level, g.StoreAs))
                .Distinct()
                .ToList();

            var stateJson = JsonSerializer.Serialize(new
            {
                UsesMilestone = (SelectedXpMode?.Name ?? "Milestone") == "Milestone",
                ProficientSkills = chosenSkills,
                ExpertiseSkills = chosenExpertise,
                ProficientSaves = granted.SaveIds,
                Proficiencies = profList,
                Features = chosenFeatures,
                AbilityBumps = idBumps,
                LevelUpBumps = new Dictionary<string, int>(),
                CreationAsiPicks = asiPicks,
                AnsweredLevelChoices = answeredChoiceKeys,
                LevelChoicesRecorded = true,
                BgSpread = BgSpread,
                BgPlusTwo = BgPlusTwo ?? "",
                BgPlusOne = BgPlusOne ?? "",
                Backstory = Backstory ?? "",
                BackgroundId = SelectedBackground?.Id ?? "",
                PreparedSpellIds = Spellbook.Where(s => s.IsPrepared).Select(s => s.SpellId).ToList(),
                Languages = LanguageIdsForSave(),
                ColorHex = SelectedColorHex
            });

            if (IsEditMode)
            {
                await ApplyEditAsync(_editing!, chosenSkills, chosenExpertise, granted, profList, chosenFeatures, invIds, bumps, asiPicks);
                if (_parent != null)
                    await _parent.FinishCharacterEditAsync(_editing!);
                return;
            }

            string? assignTo = (!IsNpc && _parent?.CurrentRole == UserRole.Player) ? App.PM.GetUID() : null;

            var newId = await App.PM.CreateCharacterAsync(
                string.IsNullOrWhiteSpace(Name) ? "New Character" : Name.Trim(),
                SelectedRace?.Id, SelectedSubrace?.Id, SelectedClass?.Id,
                Level, CurrentHp, MaxHp, abilityJson, invJson, stateJson, assignTo,
                IsNpc ? "npc" : "pc");

            // Second write instead of adding another parameter to CreateCharacterAsync, feels backwards but it works. Sets ClassId and Level to the split.
            var mcLevels = MulticlassLevelsFromRows();
            if (mcLevels.Count >= 2)
            {
                await App.PM.SaveClassLevelsAsync(newId, mcLevels);
                await App.PM.BroadcastCharacterAsync(newId);
            }

            if (_parent != null)
            {
                if (IsNpc) await _parent.FinishNpcCreationAsync();
                else await _parent.FinishCharacterCreationAsync(newId, assignTo != null);
            }
        }

        private List<ClassLevel> MulticlassLevelsFromRows()
        {
            var result = new List<ClassLevel>();
            foreach (var r in MulticlassRows)
            {
                if (r.SelectedClass == null || r.Level < 1) continue;
                if (result.Any(x => string.Equals(x.ClassId, r.SelectedClass.Id, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new ClassLevel(r.SelectedClass.Id, r.Level));
            }
            return result;
        }

        private void PickColor(ColorSwatchOption swatch)
        {
            if (swatch == null || swatch.IsTaken) return;
            SelectedColorHex = swatch.Hex;
            foreach (var s in ColorSwatches) s.IsSelected = ReferenceEquals(s, swatch);
        }

        private async Task BuildColorSwatchesAsync()
        {
            ColorSwatches.Clear();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (App.PM != null)
            {
                var all = await App.PM.LoadAllCharactersInCampaignAsync();
                foreach (var c in all)
                {
                    if (_editing != null && c.Id == _editing.Id) continue;
                    if (!string.IsNullOrWhiteSpace(c.ColorHex)) taken.Add(c.ColorHex);
                }
            }

            foreach (var hex in TokenColorPalette)
                ColorSwatches.Add(new ColorSwatchOption(hex, taken.Contains(hex)));

            var mine = _editing?.ColorHex;
            var initial = !string.IsNullOrWhiteSpace(mine)
                ? mine
                : ColorSwatches.FirstOrDefault(s => !s.IsTaken)?.Hex ?? TokenColorPalette[0];

            var match = ColorSwatches.FirstOrDefault(s => string.Equals(s.Hex, initial, StringComparison.OrdinalIgnoreCase));
            if (match != null) { match.IsSelected = true; SelectedColorHex = match.Hex; }
            else SelectedColorHex = initial;
        }

        private async Task ApplyEditAsync(
            CharacterRuntime rt,
            List<string> chosenSkills,
            List<string> chosenExpertise,
            ProficiencyResolver.GrantedProficiencies granted,
            List<string> profList,
            List<string> chosenFeatures,
            List<string> invIds,
            Dictionary<string, int> bumps,
            List<string> asiPicks)
        {
            rt.Name = string.IsNullOrWhiteSpace(Name) ? rt.Name : Name.Trim();
            rt.Backstory = Backstory ?? "";
            rt.BackgroundId = SelectedBackground?.Id ?? "";
            if (IsSpellcaster) rt.PreparedSpellIds = Spellbook.Where(s => s.IsPrepared).Select(s => s.SpellId).ToList();
            rt.RaceId = SelectedRace?.Id;
            rt.SubraceId = SelectedSubrace?.Id;
            rt.ClassId = SelectedClass?.Id;
            rt.Level = Level;
            rt.Languages = LanguageIdsForSave();

            var mcLevels = MulticlassLevelsFromRows();
            if (mcLevels.Count >= 2)
            {
                rt.ClassLevels = mcLevels;
                rt.ClassId = ClassLevels.PrimaryClassId(mcLevels);
                rt.Level = ClassLevels.TotalLevel(mcLevels);
            }
            rt.MaxHp = MaxHp;
            if (rt.CurrentHp > MaxHp || rt.CurrentHp == 0) rt.CurrentHp = MaxHp;

            if (rt.AbilityBumps != null)
            {
                var cap = App.PM?.Rules?.AbilityScoreCap ?? int.MaxValue;
                var newBumps = new Dictionary<string, int>();
                foreach (var a in Abilities)
                {
                    if (string.IsNullOrEmpty(a.Id)) continue;
                    var bump = bumps.TryGetValue(a.Abbrev, out var b) ? b : 0;
                    rt.LevelUpBumps.TryGetValue(a.Id, out var lvl);
                    var total = Math.Min(cap, a.Score + bump + lvl);
                    rt.AbilityScores.Set(a.Id, total);
                    var delta = total - a.Score - lvl;
                    if (delta != 0) newBumps[a.Id] = delta;
                }
                rt.AbilityBumps = newBumps;
                rt.CreationAsiPicks = asiPicks;
                rt.BgSpread = BgSpread;
                rt.BgPlusTwo = BgPlusTwo ?? "";
                rt.BgPlusOne = BgPlusOne ?? "";
            }
            else
            {
                foreach (var a in Abilities)
                    if (!string.IsNullOrEmpty(a.Id)) rt.AbilityScores.Set(a.Id, a.Score);
            }

            if (chosenSkills.Count > 0) rt.ProficientSkills = rt.ProficientSkills.Union(chosenSkills).Distinct().ToList();
            if (chosenExpertise.Count > 0) rt.ExpertiseSkills = rt.ExpertiseSkills.Union(chosenExpertise).Distinct().ToList();
            if (granted.SaveIds.Count > 0) rt.ProficientSaves = granted.SaveIds;
            if (profList.Count > 0) rt.Proficiencies = profList;
            if (chosenFeatures.Count > 0) rt.Features = rt.Features.Union(chosenFeatures).Distinct().ToList();

            rt.InventoryInstanceIds = invIds;
            rt.ColorHex = SelectedColorHex;

            if (rt.LevelChoicesRecorded)
            {
                var editRules = App.PM?.Rules ?? new GameRules();
                foreach (var g in ChoiceGroups.Where(g => g.SelectedCount > 0))
                {
                    var key = editRules.AnsweredChoiceKey(g.Id, g.Level, g.StoreAs);
                    if (!rt.AnsweredLevelChoices.Contains(key)) rt.AnsweredLevelChoices.Add(key);
                }
            }

            if (App.PM?.GameDataRepo != null)
            {
                await App.PM.GameDataRepo.SaveCharacterAsync(CharacterMapper.ToRow(rt));
                if (mcLevels.Count >= 2) await App.PM.SaveClassLevelsAsync(rt.Id, mcLevels);
                await App.PM.BroadcastCharacterAsync(rt.Id);
            }
        }
    }

    public class AbilityEntry : ViewModelBase
    {
        private readonly Action _onChanged;

        public string Name { get; }
        public string Abbrev { get; }
        public string Id { get; }
        public int Min { get; set; } = 8;
        public int Max { get; set; } = 15;

        private int _score = 8;
        public int Score
        {
            get => _score;
            set
            {
                this.RaiseAndSetIfChanged(ref _score, value);
                this.RaisePropertyChanged(nameof(ModifierLabel));
                _onChanged?.Invoke();
            }
        }

        public string ModifierLabel
        {
            get
            {
                int m = App.PM?.AbilityMod(Score) ?? (int)Math.Floor((Score - 10) / 2.0);
                return m >= 0 ? $"+{m}" : m.ToString();
            }
        }

        public ReactiveCommand<Unit, Unit> IncreaseCommand { get; }
        public ReactiveCommand<Unit, Unit> DecreaseCommand { get; }
        public ReactiveCommand<Unit, Unit> RollCommand { get; }

        public AbilityEntry(string name, string abbrev, Action onChanged, string id = "", Func<AbilityEntry, bool>? canIncrease = null, Action<AbilityEntry>? roll = null)
        {
            Name = name;
            Abbrev = abbrev;
            Id = id;
            _onChanged = onChanged;
            IncreaseCommand = ReactiveCommand.Create(() => { if (Score < Max && (canIncrease == null || canIncrease(this))) Score++; });
            DecreaseCommand = ReactiveCommand.Create(() => { if (Score > Min) Score--; });
            RollCommand = ReactiveCommand.Create(() => { roll?.Invoke(this); });
        }
    }

    public class XpModeOption
    {
        public string Name { get; }
        public XpModeOption(string name) => Name = name;
    }

    public class ClassOption
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string HitDiceId { get; }
        public string PrimaryAbility { get; }
        public string SummaryLine => $"Hit Die {HitDiceId}  ·  {PrimaryAbility}";

        public ClassOption(string id, string name, string description, string hitDiceId, string primaryAbility)
        {
            Id = id; Name = name; Description = description; HitDiceId = hitDiceId; PrimaryAbility = primaryAbility;
        }
    }

    public class RaceOption
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Size { get; }
        public int Speed { get; }
        public string Version { get; }
        public bool HasVersion => Version.Length > 0;
        public string SummaryLine => $"{Size}  ·  {Speed} ft";
        public Dictionary<string, int> BonusValues { get; set; } = new();
        public List<string> LanguageIds { get; set; } = new();

        public RaceOption(string id, string name, string description, string size, int speed, string version = "")
        {
            Id = id; Name = name; Description = description; Size = size; Speed = speed; Version = version;
        }
    }

    public class MulticlassRow : ViewModelBase
    {
        private ClassOption? _selectedClass;
        public ClassOption? SelectedClass
        {
            get => _selectedClass;
            set => this.RaiseAndSetIfChanged(ref _selectedClass, value);
        }

        private int _level = 1;
        public int Level
        {
            get => _level;
            set => this.RaiseAndSetIfChanged(ref _level, Math.Max(1, value));
        }

        public int MaxLevel { get; } = App.PM?.Rules?.MaxLevel ?? 20;
    }

    public class LanguagePickRow : ViewModelBase
    {
        public IReadOnlyList<LanguageOption> Options { get; }

        private LanguageOption? _selected;
        public LanguageOption? Selected
        {
            get => _selected;
            set => this.RaiseAndSetIfChanged(ref _selected, value);
        }

        public LanguagePickRow(IReadOnlyList<LanguageOption> options) => Options = options;
    }

    public class SubraceOption
    {
        public string Id { get; }
        public string Name { get; }
        public string ParentRaceId { get; }
        public string Description { get; }
        public Dictionary<string, int> BonusValues { get; set; } = new();

        public SubraceOption(string id, string name, string parentRaceId, string description)
        {
            Id = id; Name = name; ParentRaceId = parentRaceId; Description = description;
        }
    }

    public class ItemOption : ViewModelBase
    {
        private readonly string _dataJson;
        private ItemDisplay? _display;

        public string Id { get; }
        public string Name { get; }
        public string ItemType { get; }
        public string DataJson => _dataJson;
        public ItemDisplay Display => _display ??= ItemDisplay.FromJson(Name, ItemType, _dataJson);

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public ItemOption(string id, string name, string itemType, string dataJson = "")
        {
            Id = id; Name = name; ItemType = itemType; _dataJson = dataJson ?? "";
        }
    }

    public class ColorSwatchOption : ViewModelBase
    {
        public string Hex { get; }
        public bool IsTaken { get; }
        public IBrush Brush { get; }
        public double SwatchOpacity => IsTaken ? 0.30 : 1.0;
        public string Tip => IsTaken ? "Already taken" : Hex;

        public ColorSwatchOption(string hex, bool isTaken)
        {
            Hex = hex;
            IsTaken = isTaken;
            IBrush brush;
            try { brush = new SolidColorBrush(Color.Parse(hex)); }
            catch { brush = Brushes.Gray; }
            Brush = brush;
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { this.RaiseAndSetIfChanged(ref _isSelected, value); this.RaisePropertyChanged(nameof(Outline)); }
        }

        public IBrush Outline => _isSelected
            ? new SolidColorBrush(Color.Parse("#FFD700"))
            : Brushes.Transparent;
    }
}
