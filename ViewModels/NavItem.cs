using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.UI;
using ReactiveUI;
using System;
using System.ComponentModel;
using System.Text.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Media;
using Dujahit.Models.Database;

namespace Dujahit.ViewModels
{
    public class NavItem : ViewModelBase
    {
        public string Label { get; }
        public string? IconKey { get; }      // Future implementation for when I change it so that the SideMenu is able to be minimized
        public UserRole[] AllowedRoles { get; }
        public ReactiveCommand<Unit, Unit> NavigateCommand { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public static Action<string>? NavError;

        public NavItem(string label, UserRole[] allowedRoles, Action onNavigate, string? iconKey = null)
        {
            Label = label;
            AllowedRoles = allowedRoles;
            IconKey = iconKey;
            NavigateCommand = ReactiveCommand.Create(onNavigate);
            NavigateCommand.ThrownExceptions.Subscribe(ex => ReportNavError(label, ex));
        }

        public NavItem(string label, UserRole[] allowedRoles, Func<Task> onNavigateAsync, string? iconKey = null)
        {
            Label = label;
            AllowedRoles = allowedRoles;
            IconKey = iconKey;
            NavigateCommand = ReactiveCommand.CreateFromTask(onNavigateAsync);
            NavigateCommand.ThrownExceptions.Subscribe(ex => ReportNavError(label, ex));
        }

        private static void ReportNavError(string label, Exception ex)
        {
            ErrorLog.Log($"Opening {label} failed", ex);
            NavError?.Invoke($"Couldn't open {label}, try again.");
        }

        public bool IsVisibleFor(UserRole role) => Array.IndexOf(AllowedRoles, role) >= 0;
    }


    // These are not needed no just using for test, this is just bullshit
    public class CampaignSettingsViewModel : ViewModelBase { }


    public record ClassOptionRow(string Id, string Name);

    public record ClassLevelRow(string Id, string Name, int Level);

    public class CharacterSheetViewModel : ViewModelBase
    {
        private readonly CharacterRuntime _character;

        public event Action<string> DmLogPing;

        public Action<string, bool>? RollToChat;

        private Bitmap? _tokenPreview;
        public Bitmap? TokenPreview
        {
            get => _tokenPreview;
            private set => this.RaiseAndSetIfChanged(ref _tokenPreview, value);
        }

        private bool _canManageToken;
        public bool CanManageToken
        {
            get => _canManageToken;
            set => this.RaiseAndSetIfChanged(ref _canManageToken, value);
        }

        
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                this.RaiseAndSetIfChanged(ref _name, value);
                _character.Name = value;
            }
        }

        private bool _nameEditing;
        public bool NameEditing
        {
            get => _nameEditing;
            set => this.RaiseAndSetIfChanged(ref _nameEditing, value);
        }

        public ReactiveCommand<Unit, Unit> BeginEditNameCommand { get; }
        public ReactiveCommand<Unit, Unit> EndEditNameCommand { get; }
        public ReactiveCommand<Unit, Unit> AddXpCommand { get; }
        public ReactiveCommand<Unit, Unit> SubtractXpCommand { get; }
        public ReactiveCommand<Unit, Unit> MilestoneLevelUpCommand { get; }
        public ReactiveCommand<Unit, Unit> TakeClassLevelCommand { get; }

        
        private string _race;
        public string Race { get => _race; set => this.RaiseAndSetIfChanged(ref _race, value); }

        private string _subrace = "";
        public string Subrace { get => _subrace; set { this.RaiseAndSetIfChanged(ref _subrace, value); this.RaisePropertyChanged(nameof(HasSubrace)); } }
        public bool HasSubrace => !string.IsNullOrWhiteSpace(_subrace);

        private string _class;
        public string Class { get => _class; set => this.RaiseAndSetIfChanged(ref _class, value); }

        private int _level;
        public int Level
        {
            get => _level;
            set
            {
                this.RaiseAndSetIfChanged(ref _level, value);
                _character.Level = value;
                ProficiencyBonus = App.PM?.ProficiencyBonusForLevel(value) ?? (2 + (value - 1) / 4);
                foreach (var a in Abilities) a.ProficiencyBonus = ProficiencyBonus;
                foreach (var s in Skills) s.ProficiencyBonus = ProficiencyBonus;
                this.RaisePropertyChanged(nameof(Abilities));
                this.RaisePropertyChanged(nameof(Skills));
                this.RaisePropertyChanged(nameof(MaxHpBonus));
                this.RaisePropertyChanged(nameof(EffectiveMaxHp));
                this.RaisePropertyChanged(nameof(HasMaxHpBonus));
                this.RaisePropertyChanged(nameof(MaxHpBonusLabel));
                this.RaisePropertyChanged(nameof(InitiativeDisplay));
                _ = ReloadFeaturesAsync();
                _ = ResolveProficienciesLiveAsync();
                _ = LoadSpellSlotsAsync();
                _ = LoadSpellbookAsync();
                _ = LoadResourcesAsync();
            }
        }

        private int _proficiencyBonus = 2;
        public int ProficiencyBonus
        {
            get => _proficiencyBonus;
            set
            {
                this.RaiseAndSetIfChanged(ref _proficiencyBonus, value);
                this.RaisePropertyChanged(nameof(ProficiencyBonusDisplay));
            }
        }
        public string ProficiencyBonusDisplay => $"+{_proficiencyBonus}";

        
        private int _currentHp;
        public int CurrentHp
        {
            get => _currentHp;
            set
            {
                var previous = _currentHp;
                this.RaiseAndSetIfChanged(ref _currentHp, value);
                _character.CurrentHp = value;
                this.RaisePropertyChanged(nameof(IsDowned));
                this.RaisePropertyChanged(nameof(ShowDeathCard));
                this.RaisePropertyChanged(nameof(IsStable));
                this.RaisePropertyChanged(nameof(DeathSaveStatus));
                this.RaisePropertyChanged(nameof(CanRollDeathSave));
                if (value > 0 && (_deathSaveSuccesses != 0 || _deathSaveFailures != 0))
                {
                    DeathSaveSuccesses = 0;
                    DeathSaveFailures = 0;
                }
                if (value <= 0 && _concentration) Concentration = false;
                else if (!_suppressSave && _concentration && value > 0 && value < previous) RollConcentrationSave(previous - value);
            }
        }

        private int _maxHp;
        public int MaxHp { get => _maxHp; set { this.RaiseAndSetIfChanged(ref _maxHp, value); _character.MaxHp = value; this.RaisePropertyChanged(nameof(EffectiveMaxHp)); } }

        public int MaxHpBonus => (_bonuses?.MaxHpPerLevel ?? 0) * Level;
        public int EffectiveMaxHp =>
            Math.Max(1, (int)Math.Floor((MaxHp + MaxHpBonus) * (App.PM?.Rules?.Exhaustion?.MaxHpMultiplier(_exhaustionLevel) ?? 1.0)));
        public bool HasMaxHpBonus => MaxHpBonus != 0;
        public string MaxHpBonusLabel => MaxHpBonus > 0 ? "+" + MaxHpBonus : (MaxHpBonus < 0 ? MaxHpBonus.ToString() : "");
        public string ResistancesLabel => string.Join(", ", _bonuses.Resistances);
        public bool HasResistances => _bonuses.Resistances.Count > 0;
        public string InitiativeDisplay
        {
            get { var m = AbilityMod(App.PM?.Rules?.InitiativeAbility ?? "dex") + (_bonuses?.Initiative ?? 0); return m >= 0 ? "+" + m : m.ToString(); }
        }

        private int _tempHp;
        public int TempHp { get => _tempHp; set { this.RaiseAndSetIfChanged(ref _tempHp, value); _character.TempHp = value; } }

        
        private int _armorClass = 10;
        public int ArmorClass { get => _armorClass; set { this.RaiseAndSetIfChanged(ref _armorClass, value); _character.ArmorClass = value; } }

        private bool _inspiration;
        public bool Inspiration { get => _inspiration; set { this.RaiseAndSetIfChanged(ref _inspiration, value); _character.Inspiration = value; } }

        
        private bool _usesMilestone;
        public bool UsesMilestone
        {
            get => _usesMilestone;
            set
            {
                this.RaiseAndSetIfChanged(ref _usesMilestone, value);
                _character.UsesMilestone = value;
                this.RaisePropertyChanged(nameof(IsXpMode));
            }
        }
        public bool IsXpMode => !UsesMilestone;

        private int _currentXp;
        public int CurrentXp { get => _currentXp; set { this.RaiseAndSetIfChanged(ref _currentXp, value); _character.CurrentXp = value; this.RaisePropertyChanged(nameof(XpProgress)); } }

        private int _xpStep = 100;
        public int XpStep { get => _xpStep; set => this.RaiseAndSetIfChanged(ref _xpStep, value < 1 ? 1 : value); }

        private int MaxLevel => App.PM?.Rules.MaxLevel ?? 20;

        public int XpForNextLevel => GetXpForLevel(Level + 1);
        public int XpForCurrentLevel => GetXpForLevel(Level);
        public double XpProgress
        {
            get
            {
                var span = XpForNextLevel - XpForCurrentLevel;
                if (span <= 0) return 0;
                return Math.Clamp((CurrentXp - XpForCurrentLevel) / (double)span, 0, 1) * 100;
            }
        }

        private string _milestoneNote = "";
        public string MilestoneNote { get => _milestoneNote; set { this.RaiseAndSetIfChanged(ref _milestoneNote, value); _character.MilestoneNote = value; } }

        
        public ObservableCollection<AbilityScoreViewModel> Abilities { get; } = new();
        public ObservableCollection<SkillViewModel> Skills { get; } = new();
        public ObservableCollection<InventoryItemViewModel> Inventory { get; } = new();
        public ObservableCollection<string> Conditions { get; } = new();
        public ObservableCollection<string> Senses { get; } = new();
        public ObservableCollection<FeatureEntry> Features { get; } = new();
        public ObservableCollection<FeatureEntry> RacialTraits { get; } = new();

        private bool _hasRacialTraits;
        public bool HasRacialTraits
        {
            get => _hasRacialTraits;
            private set => this.RaiseAndSetIfChanged(ref _hasRacialTraits, value);
        }

        public ObservableCollection<LevelSelectionEntry> LevelSelections { get; } = new();

        private bool _hasLevelSelections;
        public bool HasLevelSelections
        {
            get => _hasLevelSelections;
            private set => this.RaiseAndSetIfChanged(ref _hasLevelSelections, value);
        }

        public ObservableCollection<SpellPrepEntry> Spellbook { get; } = new();
        public ObservableCollection<SpellLevelGroup> SpellGroups { get; } = new();

        public event Action<SpellPrepEntry>? OpenSpellViewRequested;
        private void OnSpellView(SpellPrepEntry entry) => OpenSpellViewRequested?.Invoke(entry);

        private bool _hasSpellbook;
        public bool HasSpellbook
        {
            get => _hasSpellbook;
            private set => this.RaiseAndSetIfChanged(ref _hasSpellbook, value);
        }

        private int _preparedCount;
        public int PreparedCount
        {
            get => _preparedCount;
            private set { this.RaiseAndSetIfChanged(ref _preparedCount, value); this.RaisePropertyChanged(nameof(PreparedCountLabel)); }
        }

        private int _cantripCount;
        public int CantripCount
        {
            get => _cantripCount;
            private set { this.RaiseAndSetIfChanged(ref _cantripCount, value); this.RaisePropertyChanged(nameof(CantripCountLabel)); }
        }

        private int _cantripLimit;
        public int CantripLimit
        {
            get => _cantripLimit;
            private set { this.RaiseAndSetIfChanged(ref _cantripLimit, value); this.RaisePropertyChanged(nameof(CantripCountLabel)); }
        }

        private int _preparedLimit;
        public int PreparedLimit
        {
            get => _preparedLimit;
            private set { this.RaiseAndSetIfChanged(ref _preparedLimit, value); this.RaisePropertyChanged(nameof(PreparedCountLabel)); }
        }

        public string PreparedCountLabel => PreparedLimit > 0 ? PreparedCount + " / " + PreparedLimit + " prepared" : PreparedCount + " prepared";
        public string CantripCountLabel => CantripLimit > 0 ? CantripCount + " / " + CantripLimit + " cantrips" : CantripCount + " cantrips";

        public ObservableCollection<SheetSlotRow> SpellSlots { get; } = new();

        private bool _hasSpellSlots;
        public bool HasSpellSlots
        {
            get => _hasSpellSlots;
            private set => this.RaiseAndSetIfChanged(ref _hasSpellSlots, value);
        }

        public ObservableCollection<CastableSpell> Castables { get; } = new();

        private bool _hasCastables;
        public bool HasCastables
        {
            get => _hasCastables;
            private set
            {
                this.RaiseAndSetIfChanged(ref _hasCastables, value);
                this.RaisePropertyChanged(nameof(ShowCastList));
                this.RaisePropertyChanged(nameof(ShowSpellbookList));
                this.RaisePropertyChanged(nameof(ShowNoPreparedHint));
            }
        }

        private bool _editingSpells;
        public bool EditingSpells
        {
            get => _editingSpells;
            set
            {
                this.RaiseAndSetIfChanged(ref _editingSpells, value);
                this.RaisePropertyChanged(nameof(ShowCastList));
                this.RaisePropertyChanged(nameof(ShowSpellbookList));
                this.RaisePropertyChanged(nameof(ShowNoPreparedHint));
                this.RaisePropertyChanged(nameof(SpellToggleLabel));
            }
        }

        public bool ShowCastList => !EditingSpells;
        public bool ShowSpellbookList => EditingSpells;
        public bool ShowNoPreparedHint => !EditingSpells && !HasCastables;
        public string SpellToggleLabel => EditingSpells ? "Done" : "Prepare spells";

        public ReactiveCommand<Unit, Unit> ToggleSpellEditCommand { get; }

        private int _castSaveDc;
        private int _castAttackBonus;
        private int _castAbilityMod;
        private int _castCharLevel;

        public ReactiveCommand<CastableSpell, Unit> CastSpellCommand { get; }
        public ReactiveCommand<Unit, Unit> RollDeathSaveCommand { get; }

        public ObservableCollection<SheetResourceRow> ClassResources { get; } = new();

        private bool _hasClassResources;
        public bool HasClassResources
        {
            get => _hasClassResources;
            private set => this.RaiseAndSetIfChanged(ref _hasClassResources, value);
        }

        public ObservableCollection<ChoiceGroupViewModel> FeatChoices { get; } = new();

        private bool _hasFeatChoices;
        public bool HasFeatChoices
        {
            get => _hasFeatChoices;
            private set => this.RaiseAndSetIfChanged(ref _hasFeatChoices, value);
        }

        public ReactiveCommand<ChoiceGroupViewModel, Unit> ApplyFeatChoiceCommand { get; }

        private int _deathSaveSuccesses;
        public int DeathSaveSuccesses
        {
            get => _deathSaveSuccesses;
            private set { this.RaiseAndSetIfChanged(ref _deathSaveSuccesses, value); _character.DeathSaveSuccesses = value; RaiseDeathDerived(); }
        }

        private int _deathSaveFailures;
        public int DeathSaveFailures
        {
            get => _deathSaveFailures;
            private set { this.RaiseAndSetIfChanged(ref _deathSaveFailures, value); _character.DeathSaveFailures = value; RaiseDeathDerived(); }
        }

        public bool IsDowned => CurrentHp <= 0;
        public bool ShowDeathCard => IsDowned || IsDead;
        public ObservableCollection<DeathPipViewModel> DeathSuccessPips { get; } = new();
        public ObservableCollection<DeathPipViewModel> DeathFailurePips { get; } = new();
        private int DeathSuccessTarget => App.PM?.Rules.DeathSaveSuccessesToStabilize ?? 3;
        private int DeathFailTarget => App.PM?.Rules.DeathSaveFailuresToDie ?? 3;
        public bool IsStable => IsDowned && DeathSaveSuccesses >= DeathSuccessTarget && DeathSaveFailures < DeathFailTarget;
        private bool ExhaustionKills => (App.PM?.Rules?.Exhaustion?.DeathAtMax ?? true) && ExhaustionMax > 0 && _exhaustionLevel >= ExhaustionMax;
        public bool IsDead => DeathSaveFailures >= DeathFailTarget || ExhaustionKills;
        public string DeathSaveStatus => IsDead ? "Dead" : IsStable ? "Stable" : "Death Saves";

        private int _exhaustionLevel;
        public int ExhaustionLevel
        {
            get => _exhaustionLevel;
            private set
            {
                this.RaiseAndSetIfChanged(ref _exhaustionLevel, value);
                _character.ExhaustionLevel = value;
                this.RaisePropertyChanged(nameof(HasExhaustion));
                this.RaisePropertyChanged(nameof(ExhaustionLabel));
                this.RaisePropertyChanged(nameof(ExhaustionEffect));
                this.RaisePropertyChanged(nameof(EffectiveMaxHp));
                this.RaisePropertyChanged(nameof(EffectiveSpeed));
                RaiseDeathDerived();
                QueueSave();
            }
        }
        public string HitDiceLabel =>
            "Hit dice " + Math.Max(0, _character.HitDiceRemaining) + " / " + (App.PM?.Rules ?? new GameRules()).MaxHitDiceForLevel(Level);

        public int ExhaustionMax => App.PM?.Rules?.Exhaustion?.MaxLevel ?? 6;
        public bool HasExhaustion => _exhaustionLevel > 0;
        public string ExhaustionLabel => "Exhaustion " + _exhaustionLevel + " / " + ExhaustionMax;
        public string ExhaustionEffect => App.PM?.Rules?.Exhaustion?.EffectFor(_exhaustionLevel) ?? "";
        public ReactiveCommand<Unit, Unit> ExhaustionUpCommand { get; }
        public ReactiveCommand<Unit, Unit> ExhaustionDownCommand { get; }

        private int _speed;
        public int EffectiveSpeed =>
            Math.Max(0, (int)Math.Floor(Speed * (App.PM?.Rules?.Exhaustion?.SpeedMultiplier(_exhaustionLevel) ?? 1.0))
                        - (App.PM?.Rules?.Exhaustion?.SpeedPenaltyFeet(_exhaustionLevel) ?? 0));

        public int Speed
        {
            get => _speed > 0 ? _speed : (App.PM?.Rules?.DefaultSpeed ?? 30);
            set { this.RaiseAndSetIfChanged(ref _speed, value); _character.Speed = value; this.RaisePropertyChanged(nameof(DashDistance)); this.RaisePropertyChanged(nameof(DifficultTerrainDistance)); QueueSave(); }
        }
        public int DashDistance => (int)Math.Round(Speed * (App.PM?.Rules?.DashMultiplier ?? 2.0));
        public int DifficultTerrainDistance => (int)Math.Floor(Speed / (App.PM?.Rules?.DifficultTerrainMultiplier ?? 2.0));
        public int JumpDistance
        {
            get
            {
                var rules = App.PM?.Rules ?? new GameRules();
                var score = Abilities.FirstOrDefault(a => string.Equals(a.ShortName, rules.JumpAbility, StringComparison.OrdinalIgnoreCase))?.Score ?? 10;
                return (int)Math.Round(score * rules.JumpScoreMultiplier);
            }
        }

        private void BuildDeathPips()
        {
            DeathSuccessPips.Clear();
            DeathFailurePips.Clear();
            for (int i = 1; i <= DeathSuccessTarget; i++)
            {
                int n = i;
                DeathSuccessPips.Add(new DeathPipViewModel(n, () => DeathSaveSuccesses = DeathSaveSuccesses == n ? n - 1 : n));
            }
            for (int i = 1; i <= DeathFailTarget; i++)
            {
                int n = i;
                DeathFailurePips.Add(new DeathPipViewModel(n, () => DeathSaveFailures = DeathSaveFailures == n ? n - 1 : n));
            }
            UpdateDeathPips();
        }

        private void UpdateDeathPips()
        {
            foreach (var p in DeathSuccessPips) p.Filled = DeathSaveSuccesses >= p.Index;
            foreach (var p in DeathFailurePips) p.Filled = DeathSaveFailures >= p.Index;
        }

        private void RollDeathSave()
        {
            if (!IsDowned || IsStable || IsDead) return;
            var rules = App.PM?.Rules ?? new GameRules();
            int die = rules.AttackDie;
            int roll = DiceManager.RollCore(die);

            if (rules.IsCrit(roll))
            {
                CurrentHp = Math.Min(MaxHp, Math.Max(0, rules.DeathSaveCritHeal));
                RollToChat?.Invoke($"{Name} death save: d{die} [{roll}] natural, back up on {rules.DeathSaveCritHeal} HP.", false);
                return;
            }
            if (rules.IsFumble(roll))
            {
                DeathSaveFailures = Math.Min(DeathFailTarget, DeathSaveFailures + rules.DeathSaveFumbleFailures);
                RollToChat?.Invoke($"{Name} death save: d{die} [{roll}] natural, {rules.DeathSaveFumbleFailures} failures. {DeathSaveStatus}.", false);
                return;
            }
            if (rules.SaveSucceeds(roll, rules.DeathSaveThreshold))
            {
                DeathSaveSuccesses = Math.Min(DeathSuccessTarget, DeathSaveSuccesses + 1);
                RollToChat?.Invoke($"{Name} death save: d{die} [{roll}] success {DeathSaveSuccesses}/{DeathSuccessTarget}. {DeathSaveStatus}.", false);
            }
            else
            {
                DeathSaveFailures = Math.Min(DeathFailTarget, DeathSaveFailures + 1);
                RollToChat?.Invoke($"{Name} death save: d{die} [{roll}] failure {DeathSaveFailures}/{DeathFailTarget}. {DeathSaveStatus}.", false);
            }
        }

        private void RollConcentrationSave(int damage)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            int dc = Math.Min(rules.ConcentrationDcCap, Math.Max(rules.ConcentrationDcFloor, damage / Math.Max(1, rules.ConcentrationDcDivisor)));
            int die = rules.AttackDie;
            int roll = DiceManager.RollCore(die);
            var conAbility = rules.ConcentrationAbility;
            int bonus = AbilityMod(conAbility) + rules.RankBonus(GameRules.RankIdFor(_character.ProficientSaves.Contains(conAbility.ToLowerInvariant())), ProficiencyBonus);
            int total = roll + bonus;
            var bonusTxt = bonus >= 0 ? "+" + bonus : bonus.ToString();
            bool held = rules.SaveSucceeds(total, dc);
            RollToChat?.Invoke($"{Name} concentration save (DC {dc}): d{die} [{roll}]{bonusTxt} = {total}, {(held ? "held" : "lost concentration")}.", false);
            if (!held) Concentration = false;
        }

        private bool _concentration;
        public bool Concentration
        {
            get => _concentration;
            set
            {
                this.RaiseAndSetIfChanged(ref _concentration, value);
                _character.Concentration = value;
                if (!value && !string.IsNullOrEmpty(_concentrationSpell)) ConcentrationSpell = "";
            }
        }

        private string _concentrationSpell = "";
        public string ConcentrationSpell
        {
            get => _concentrationSpell;
            set { this.RaiseAndSetIfChanged(ref _concentrationSpell, value); _character.ConcentrationSpell = value; }
        }

        public ObservableCollection<string> Proficiencies { get; } = new();
        public ObservableCollection<ConditionToggle> ToggleableConditions { get; } = new();
        public ObservableCollection<WalletEntry> Wallet { get; } = new();
        public ObservableCollection<WalletAddRow> WalletAdds { get; } = new();

        private bool _showWalletEditor;
        public bool ShowWalletEditor { get => _showWalletEditor; set => this.RaiseAndSetIfChanged(ref _showWalletEditor, value); }
        public ObservableCollection<InventoryItemViewModel> EquippedItems { get; } = new();
        public int AttunedCount => Inventory.Count(i => i.IsAttuned);
        public string AttunementSummary => "Attuned " + AttunedCount + " / " + (App.PM?.Rules?.AttunementLimit ?? 3);

        
        private string _backstory = "";
        public string Backstory { get => _backstory; set { this.RaiseAndSetIfChanged(ref _backstory, value); _character.Backstory = value; QueueSave(); } }

        private string _notes = "";
        public string Notes { get => _notes; set { this.RaiseAndSetIfChanged(ref _notes, value); _character.Notes = value; QueueSave(); } }

        public ObservableCollection<BackstoryOption> BackstoryOptions { get; } = new();
        private BackstoryOption? _selectedBackstoryOption;
        public BackstoryOption? SelectedBackstoryOption { get => _selectedBackstoryOption; set => this.RaiseAndSetIfChanged(ref _selectedBackstoryOption, value); }
        public bool HasBackstoryOptions => BackstoryOptions.Count > 0;

        
        public ReactiveCommand<Unit, Unit> ShortRestCommand { get; }
        public ReactiveCommand<Unit, Unit> LongRestCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyBackstoryCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleInspirationCommand { get; }
        public ReactiveCommand<Unit, Unit> AddItemCommand { get; }
        public ReactiveCommand<Unit, Unit> EditBaseCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportCommand { get; }
        public ReactiveCommand<Unit, Unit> TradeCommand { get; }
        public ReactiveCommand<Unit, bool> ToggleWalletEditorCommand { get; }
        public ReactiveCommand<Unit, Unit> AddMoneyCommand { get; }
        public ReactiveCommand<Unit, Unit> SetPrimaryCommand { get; }

        public bool CanBePrimary =>
            string.Equals(_character.OwnerUserId, App.PM?.GetUID(), StringComparison.Ordinal)
            && string.Equals(_character.CharacterKind, "pc", StringComparison.OrdinalIgnoreCase);

        public event Action AddItemRequested;
        public event Func<string, List<CastLevelOption>, Task<int?>>? ChooseCastLevelRequested;

        public event Action EditBaseRequested;
        public event Action? ExportRequested;
        public event Action TradeRequested;

        public CharacterRuntime Runtime => _character;

        public CharacterSheetViewModel(CharacterRuntime character)
        {
            _character = character;
            _name = character.Name;
            _race = character.RaceId ?? "Unknown";
            _class = character.ClassId ?? "Unknown";
            _level = character.Level;
            _currentHp = character.CurrentHp;
            _maxHp = character.MaxHp;
            _tempHp = character.TempHp;
            _armorClass = character.ArmorClass;
            _inspiration = character.Inspiration;
            _usesMilestone = character.UsesMilestone;
            _currentXp = character.CurrentXp;
            _milestoneNote = character.MilestoneNote;
            _backstory = character.Backstory;
            _notes = character.Notes;
            _deathSaveSuccesses = character.DeathSaveSuccesses;
            _deathSaveFailures = character.DeathSaveFailures;
            _exhaustionLevel = character.ExhaustionLevel;
            _speed = character.Speed;
            _concentration = character.Concentration;
            _concentrationSpell = character.ConcentrationSpell;
            ProficiencyBonus = App.PM?.ProficiencyBonusForLevel(Level) ?? (2 + (Level - 1) / 4);
            RefreshTokenPreview();
            _ = LoadBackstoryOptionsAsync();

            BuildAbilities(character);
            BuildSkills();
            ApplySkillProficiencies(character);

            foreach (var c in character.Conditions) Conditions.Add(c);
            RefreshAttackEffect();
            foreach (var s in character.Senses) _baseSenses.Add(s);

            _ = InitialInventoryLoadAsync();
            _ = LoadLanguagesLabelAsync();
            _ = LoadDisplayDataAsync(character);
            _ = ReloadFeaturesAsync();
            _ = ResolveProficienciesLiveAsync();
            _ = ReloadFeatChoicesAsync();
            _ = LoadWalletAsync();
            _ = LoadSpellbookAsync();
            _ = LoadSpellSlotsAsync();
            _ = RefreshClassLevelsAsync();
            _ = LoadResourcesAsync();

            BeginEditNameCommand = ReactiveCommand.Create(() => { NameEditing = true; });
            EndEditNameCommand = ReactiveCommand.Create(() => { NameEditing = false; });

            AddXpCommand = ReactiveCommand.Create(() =>
            {
                CurrentXp += XpStep;
                SyncLevelToXp();
            });

            SubtractXpCommand = ReactiveCommand.Create(() =>
            {
                CurrentXp = Math.Max(0, CurrentXp - XpStep);
                SyncLevelToXp();
            });

            MilestoneLevelUpCommand = ReactiveCommand.Create(() =>
            {
                if (Level < MaxLevel)
                {
                    Level += 1;
                    _ = GrantLevelUpHpAsync(_character.ClassId, 1);
                    LevelUpChoicesRequested?.Invoke(_character.ClassId, PrimaryClassLevelAfterGain(1));
                }
            });

            TakeClassLevelCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var pick = SelectedClassOption;
                if (pick == null || App.PM == null) return;

                var refused = await App.PM.TakeClassLevelAsync(_character, pick.Id);
                if (refused.Length > 0)
                {
                    MulticlassBlocker = refused;
                    return;
                }

                MulticlassBlocker = "";
                Level = _character.Level;
                await GrantLevelUpHpAsync(pick.Id, 1);
                await RefreshClassLevelsAsync();
                var classLevel = _character.ClassLevels.FirstOrDefault(x => string.Equals(x.ClassId, pick.Id, StringComparison.OrdinalIgnoreCase))?.Level ?? Level;
                LevelUpChoicesRequested?.Invoke(pick.Id, classLevel);
                DmLogPing?.Invoke(Name + " took a level in " + pick.Name + ", now " + ClassSplitLabel + ".");
            });

            ShortRestCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (CurrentHp <= 0)
                {
                    DmLogPing?.Invoke($"{Name} can't short rest while down.");
                    return;
                }
                if (_character.HitDiceRemaining <= 0)
                {
                    DmLogPing?.Invoke($"{Name} has no hit dice left to spend, a long rest gets them back.");
                    return;
                }
                int die = App.PM != null ? await App.PM.ResolveShortRestHitDieAsync(_character) : 0;
                if (die <= 0) die = App.PM?.Rules.DefaultHitDie ?? 8;
                bool average = App.PM == null || (await App.PM.GetRestSettingsAsync()).ShortRestHitDieAverage;
                var hpRules = App.PM?.Rules ?? new GameRules();
                int hpScore = _character.AbilityScores.Get(hpRules.AbilityIdForShort(hpRules.HitPointAbility));
                int hpMod = App.PM?.AbilityMod(hpScore) ?? (int)Math.Floor((hpScore - 10) / 2.0);
                int perDie = hpRules.HitDieHeal(die, average);
                int heal = Math.Max(1, perDie + hpMod);
                _character.HitDiceRemaining--;
                this.RaisePropertyChanged(nameof(HitDiceLabel));
                CurrentHp = Math.Min(EffectiveMaxHp, CurrentHp + heal);
                foreach (var row in ClassResources)
                    if (row.ResetOn == "short") { row.Used = 0; _character.ResourcesUsed[row.Id] = 0; }
                RechargeItems("short");
                if (App.PM != null) await App.PM.AdvanceCampaignClockAsync(App.PM.Rules.ShortRestMinutes);

                bool slotsBack;
                if (_character.ClassLevels.Count > 1 && App.PM != null)
                {
                    var refund = new Dictionary<int, int>();
                    foreach (var cl in _character.ClassLevels)
                        if (hpRules.SlotsReturnOnShortRest(await App.PM.ResolveCasterTypeAsync(cl.ClassId)))
                            foreach (var kv in await App.PM.ResolvePactSlotsAsync(new List<ClassLevel> { cl }))
                                refund[kv.Key] = (refund.TryGetValue(kv.Key, out var cur) ? cur : 0) + kv.Value;
                    slotsBack = refund.Count > 0;
                    foreach (var row in SpellSlots)
                        if (refund.TryGetValue(row.Level, out var back))
                        {
                            row.Used = Math.Max(0, row.Used - back);
                            _character.SpellSlotsUsed[row.Level] = row.Used;
                        }
                }
                else
                {
                    var casterType = App.PM != null ? await App.PM.ResolveCasterTypeAsync(_character.ClassId) : "none";
                    slotsBack = hpRules.SlotsReturnOnShortRest(casterType);
                    if (slotsBack)
                        foreach (var row in SpellSlots)
                        {
                            row.Used = 0;
                            _character.SpellSlotsUsed[row.Level] = 0;
                        }
                }

                QueueSave();
                var slotNote = slotsBack ? ", spell slots back" : "";
                DmLogPing?.Invoke($"{Name} short rested, spent a d{die} hit die for {heal} hp ({_character.HitDiceRemaining} left){slotNote}.");
            });

            ApplyBackstoryCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedBackstoryOption != null) Backstory = SelectedBackstoryOption.Description;
            });

            LongRestCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (App.PM?.Rules?.RestoreFullHpOnLongRest ?? true)
                {
                    CurrentHp = EffectiveMaxHp;
                    TempHp = 0;
                }
                DeathSaveSuccesses = 0;
                DeathSaveFailures = 0;
                Concentration = false;
                int divisor = App.PM != null ? (await App.PM.GetRestSettingsAsync()).LongRestHitDiceDivisor : 2;
                int maxHitDice = (App.PM?.Rules ?? new GameRules()).MaxHitDiceForLevel(Level);
                if (Level > 0)
                    _character.HitDiceRemaining = Math.Min(maxHitDice, Math.Max(_character.HitDiceRemaining, 0) + Math.Max(1, Level / Math.Max(1, divisor)));
                this.RaisePropertyChanged(nameof(HitDiceLabel));
                foreach (var row in SpellSlots)
                {
                    row.Used = 0;
                    _character.SpellSlotsUsed[row.Level] = 0;
                }
                foreach (var row in ClassResources)
                {
                    row.Used = 0;
                    _character.ResourcesUsed[row.Id] = 0;
                }
                RechargeItems("long");
                if (App.PM != null) await App.PM.AdvanceCampaignClockAsync(App.PM.Rules.LongRestMinutes);
                if (ExhaustionLevel > 0)
                    ExhaustionLevel = Math.Max(0, ExhaustionLevel - (App.PM?.Rules?.Exhaustion?.ReducePerLongRest ?? 1));
                QueueSave();
                DmLogPing?.Invoke($"{Name} took a long rest, hp and spell slots back to full.");
            });

            BuildDeathPips();

            ToggleInspirationCommand = ReactiveCommand.Create(() => { Inspiration = !Inspiration; });

            CastSpellCommand = ReactiveCommand.CreateFromTask<CastableSpell>(CastSpell);
            RollDeathSaveCommand = ReactiveCommand.Create(RollDeathSave);
            ApplyFeatChoiceCommand = ReactiveCommand.Create<ChoiceGroupViewModel>(ApplyFeatChoice);
            ExhaustionUpCommand = ReactiveCommand.Create(() => { ExhaustionLevel = Math.Min(ExhaustionMax, ExhaustionLevel + 1); });
            ExhaustionDownCommand = ReactiveCommand.Create(() => { ExhaustionLevel = Math.Max(0, ExhaustionLevel - 1); });
            ToggleSpellEditCommand = ReactiveCommand.Create(() => { EditingSpells = !EditingSpells; });

            AddItemCommand = ReactiveCommand.Create(() => AddItemRequested?.Invoke());
            EditBaseCommand = ReactiveCommand.Create(() => EditBaseRequested?.Invoke());
            ExportCommand = ReactiveCommand.Create(() => ExportRequested?.Invoke());
            TradeCommand = ReactiveCommand.Create(() => TradeRequested?.Invoke());
            ToggleWalletEditorCommand = ReactiveCommand.Create(() => ShowWalletEditor = !ShowWalletEditor);
            AddMoneyCommand = ReactiveCommand.Create(ApplyAddMoney);
            SetPrimaryCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (App.PM != null) await App.PM.SetPrimaryCharacterAsync(_character.Id);
            });

            this.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CurrentHp) or nameof(MaxHp) or nameof(TempHp)
                    or nameof(ArmorClass) or nameof(Level) or nameof(CurrentXp)
                    or nameof(Inspiration) or nameof(Name) or nameof(Backstory)
                    or nameof(Notes) or nameof(MilestoneNote) or nameof(UsesMilestone)
                    or nameof(DeathSaveSuccesses) or nameof(DeathSaveFailures)
                    or nameof(Concentration) or nameof(ConcentrationSpell))
                    QueueSave();
            };
        }

        private CancellationTokenSource? _saveCts;
        private bool _suppressSave;
        private readonly Dictionary<string, ItemInstance> _instanceById = new();
        private readonly List<string> _baseSenses = new();

        private readonly Dictionary<string, int> _pendingFields = new(StringComparer.Ordinal);

        // A save fires 600ms after you stop and its broadcast lands later still, so a field being typed in is held against the echo.
        private void QueueSave([CallerMemberName] string? field = null)
        {
            if (_suppressSave) return;
            if (!string.IsNullOrEmpty(field))
                _pendingFields[field!] = _pendingFields.TryGetValue(field!, out var seq) ? seq + 1 : 1;
            _saveCts?.Cancel();
            _saveCts = new CancellationTokenSource();
            var token = _saveCts.Token;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(600, token); } catch { return; }
                if (token.IsCancellationRequested) return;
                await Dispatcher.UIThread.InvokeAsync(SaveAndBroadcastAsync);
            });
        }

        private bool IsPending(string field) => _pendingFields.ContainsKey(field);

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
                ErrorLog.Log($"[Sheet] backstory load failed", ex);
            }
        }

        public event Action<CharacterRuntime>? VitalsChanged;
        public event Action<string>? CombatConditionApplied;
        public event Action<string, string, string>? InspirationGranted;

        private async Task SaveAndBroadcastAsync()
        {
            if (App.PM?.GameDataRepo == null) return;
            var carried = new Dictionary<string, int>(_pendingFields, StringComparer.Ordinal);
            try
            {
                await App.PM.GameDataRepo.SaveCharacterAsync(CharacterMapper.ToRow(_character));
                await App.PM.BroadcastCharacterAsync(_character.Id);
                VitalsChanged?.Invoke(_character);
                foreach (var kv in carried)
                    if (_pendingFields.TryGetValue(kv.Key, out var now) && now == kv.Value) _pendingFields.Remove(kv.Key);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Saving character failed", ex);
                NavItem.NavError?.Invoke($"Couldn't save {_character.Name}, your last change may not have stuck.");
            }
        }

        private void RefreshTokenPreview()
        {
            TokenPreview = CharacterTokenRenderer.Resolve(_character.Name, _character.ColorHex, _character.TokenImagePath);
        }

        public async Task SetTokenImageAsync(byte[] raw)
        {
            if (App.PM == null) return;
            try
            {
                var path = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), raw);
                if (string.IsNullOrEmpty(path)) return;
                _character.TokenImagePath = path;
                RefreshTokenPreview();
                await SaveAndBroadcastAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Sheet] token upload failed", ex);
            }
        }

        public void ApplyRemoteUpdate(CharacterRuntime c)
        {
            _suppressSave = true;
            try
            {
                if (!IsPending(nameof(Name))) Name = c.Name;
                if (!IsPending(nameof(Level))) Level = c.Level;
                if (!IsPending(nameof(CurrentHp))) CurrentHp = c.CurrentHp;
                if (!IsPending(nameof(MaxHp))) MaxHp = c.MaxHp;
                if (!IsPending(nameof(TempHp))) TempHp = c.TempHp;
                if (!IsPending(nameof(ArmorClass))) ArmorClass = c.ArmorClass;
                if (!IsPending(nameof(Inspiration))) Inspiration = c.Inspiration;
                if (!IsPending(nameof(UsesMilestone))) UsesMilestone = c.UsesMilestone;
                if (!IsPending(nameof(CurrentXp))) CurrentXp = c.CurrentXp;
                if (!IsPending(nameof(MilestoneNote))) MilestoneNote = c.MilestoneNote;
                if (!IsPending(nameof(Backstory))) Backstory = c.Backstory;
                if (!IsPending(nameof(Notes))) Notes = c.Notes;

                foreach (var a in Abilities)
                    if (!string.IsNullOrEmpty(a.Id) && !IsPending(a.Id)) a.Score = c.AbilityScores.Get(a.Id);

                _character.Wallet = c.Wallet;
                _ = LoadWalletAsync();
                DeathSaveSuccesses = c.DeathSaveSuccesses;
                DeathSaveFailures = c.DeathSaveFailures;
                ExhaustionLevel = c.ExhaustionLevel;
                Concentration = c.Concentration;
                ConcentrationSpell = c.ConcentrationSpell;
                _character.SpellSlotsUsed = c.SpellSlotsUsed;
                _character.ResourcesUsed = c.ResourcesUsed;
                _ = LoadSpellSlotsAsync();
                _ = LoadResourcesAsync();
                RefreshAttackEffect();
            }
            finally { _suppressSave = false; }
        }

        private void BuildAbilities(CharacterRuntime c)
        {
            void Add(string full, string sht, int score, string saveId, string id)
            {
                var vm = new AbilityScoreViewModel(full, sht, score, id)
                {
                    ProficiencyBonus = ProficiencyBonus,
                    SaveProficient = c.ProficientSaves.Contains(saveId)
                };
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AbilityScoreViewModel.Score))
                    {
                        PersistAbilities();
                        RecalculateAc();
                        this.RaisePropertyChanged(nameof(InitiativeDisplay));
                        this.RaisePropertyChanged(nameof(JumpDistance));
                    }
                    else if (e.PropertyName == nameof(AbilityScoreViewModel.SaveProficient) && !_suppressSave)
                    {
                        if (vm.SaveProficient) { if (!_character.ProficientSaves.Contains(saveId)) _character.ProficientSaves.Add(saveId); }
                        else _character.ProficientSaves.Remove(saveId);
                        QueueSave();
                    }
                };
                Abilities.Add(vm);
            }
            var defs = App.PM?.Rules?.Abilities;
            if (defs != null && defs.Count > 0)
                foreach (var d in defs)
                    Add(d.Name, d.Short, c.AbilityScores.Get(d.Id), d.SaveId, d.Id);
            else
            {
                Add("Strength", "STR", c.AbilityScores.Strength, "str", "ability-str");
                Add("Dexterity", "DEX", c.AbilityScores.Dexterity, "dex", "ability-dex");
                Add("Constitution", "CON", c.AbilityScores.Constitution, "con", "ability-con");
                Add("Intelligence", "INT", c.AbilityScores.Intelligence, "int", "ability-int");
                Add("Wisdom", "WIS", c.AbilityScores.Wisdom, "wis", "ability-wis");
                Add("Charisma", "CHA", c.AbilityScores.Charisma, "cha", "ability-cha");
            }
        }

        private void RechargeItems(string rest)
        {
            foreach (var item in Inventory)
            {
                if (!item.HasCharges) continue;
                var before = item.Charges;
                item.RechargeFor(rest);
                if (item.Charges != before && App.PM?.GameDataRepo != null)
                    _ = App.PM.GameDataRepo.SetInstanceChargesAsync(item.InstanceId, item.Charges);
            }
        }

        private void PersistAbilities()
        {
            foreach (var a in Abilities)
                if (!string.IsNullOrEmpty(a.Id)) _character.AbilityScores.Set(a.Id, a.Score);
            QueueSave();
        }

        private void BuildSkills()
        {
            void Add(string n, string ab)
            {
                var a = Abilities.FirstOrDefault(x => x.ShortName == ab);
                if (a != null) Skills.Add(new SkillViewModel(n, ab, a) { ProficiencyBonus = ProficiencyBonus });
            }

            var defs = App.PM?.Rules?.Skills;
            if (defs == null) return;
            foreach (var d in defs) Add(d.Name, d.Ability);
        }

        private void ApplySkillProficiencies(CharacterRuntime c)
        {
            foreach (var s in Skills)
            {
                if (c.ProficientSkills.Contains(s.Name)) s.Proficient = true;
                if (c.ExpertiseSkills.Contains(s.Name)) s.Expertise = true;
            }
        }

        public event Action<FeatureEntry> OpenFeatureRequested;
        public event Action<string?, int> LevelUpChoicesRequested;

        public string? ClassId => _character.ClassId;

        public IEnumerable<string> ExpertiseCandidateSkills => Skills.Where(s => s.Proficient && !s.Expertise).Select(s => s.Name);

        public ObservableCollection<ClassOptionRow> ClassOptions { get; } = new();
        public ObservableCollection<ClassLevelRow> ClassLevelRows { get; } = new();

        private ClassOptionRow _selectedClassOption;
        public ClassOptionRow SelectedClassOption
        {
            get => _selectedClassOption;
            set => this.RaiseAndSetIfChanged(ref _selectedClassOption, value);
        }

        private string _multiclassBlocker = "";
        public string MulticlassBlocker
        {
            get => _multiclassBlocker;
            set { this.RaiseAndSetIfChanged(ref _multiclassBlocker, value); this.RaisePropertyChanged(nameof(HasMulticlassBlocker)); }
        }

        public bool HasMulticlassBlocker => !string.IsNullOrEmpty(MulticlassBlocker);
        public bool MulticlassingEnabled => App.PM?.Rules.MulticlassingOn ?? false;
        public bool IsMulticlassed => ClassLevelRows.Count > 1;
        public string ClassSplitLabel => string.Join(" / ", ClassLevelRows.Select(r => r.Name + " " + r.Level));

        public async Task RefreshClassLevelsAsync()
        {
            if (App.PM == null) return;
            var names = (await App.PM.LoadClassOptionsAsync()).ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

            ClassOptions.Clear();
            foreach (var (id, name) in names.OrderBy(kv => kv.Value)) ClassOptions.Add(new ClassOptionRow(id, name));

            ClassLevelRows.Clear();
            foreach (var c in _character.ClassLevels)
                ClassLevelRows.Add(new ClassLevelRow(c.ClassId, names.TryGetValue(c.ClassId, out var n) ? n : c.ClassId, c.Level));

            this.RaisePropertyChanged(nameof(IsMulticlassed));
            this.RaisePropertyChanged(nameof(ClassSplitLabel));
            this.RaisePropertyChanged(nameof(MulticlassingEnabled));
        }
        public string? RaceId => _character.RaceId;
        public string CharacterId => _character.Id;
        public string? OwnerUserId => _character.OwnerUserId;

        private string _attackRollEffectLabel = "";
        public string AttackRollEffectLabel
        {
            get => _attackRollEffectLabel;
            private set { this.RaiseAndSetIfChanged(ref _attackRollEffectLabel, value); this.RaisePropertyChanged(nameof(HasAttackEffect)); }
        }
        public bool HasAttackEffect => !string.IsNullOrEmpty(_attackRollEffectLabel);

        public static List<LevelChoice> UnmadeByLedger(IEnumerable<LevelChoice> all, ICollection<string> answered, GameRules rules)
        {
            var result = all.Where(ch => !answered.Contains(rules.AnsweredChoiceKey(ch.Id, ch.Level, ch.StoreAs))).ToList();
            result.Sort((a, b) => a.Level.CompareTo(b.Level));
            return result;
        }

        public static List<string> ChoicesToRecord(IEnumerable<LevelChoice> all, IEnumerable<LevelChoice> shown, IEnumerable<LevelChoice> answered, GameRules rules)
        {
            var shownKeys = new HashSet<string>(shown.Select(c => rules.AnsweredChoiceKey(c.Id, c.Level, c.StoreAs)));
            var answeredKeys = new HashSet<string>(answered.Select(c => rules.AnsweredChoiceKey(c.Id, c.Level, c.StoreAs)));
            var record = new List<string>();
            foreach (var ch in all)
            {
                var key = rules.AnsweredChoiceKey(ch.Id, ch.Level, ch.StoreAs);
                if (shownKeys.Contains(key) && !answeredKeys.Contains(key)) continue;
                if (!record.Contains(key)) record.Add(key);
            }
            return record;
        }

        public void RecordAnsweredLevelChoices(IEnumerable<LevelChoice> all, IEnumerable<LevelChoice> shown, IEnumerable<LevelChoice> answered)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            foreach (var key in ChoicesToRecord(all, shown, answered, rules))
                if (!_character.AnsweredLevelChoices.Contains(key)) _character.AnsweredLevelChoices.Add(key);
            _character.LevelChoicesRecorded = true;
            QueueSave();
        }

        public List<LevelChoice> FilterUnmadeLevelChoices(List<LevelChoice> all, int newLevel)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            if (_character.LevelChoicesRecorded)
                return UnmadeByLedger(all, _character.AnsweredLevelChoices, rules);

            var result = new List<LevelChoice>();
            var earlierFeats = new List<LevelChoice>();
            var earlierExpertise = new List<LevelChoice>();

            foreach (var ch in all)
            {
                if (ch.Level >= newLevel) { result.Add(ch); continue; }
                if (rules.IsSubclassStore(ch.StoreAs))
                {
                    var owned = ch.Options.Any(o => _character.Features.Contains(rules.SubclassFeatureLine(o.Id))
                        || _character.Features.Contains(rules.SubclassFeatureLine(o.Name)));
                    if (!owned) result.Add(ch);
                }
                else if (rules.IsFeatStore(ch.StoreAs)) earlierFeats.Add(ch);
                else if (rules.IsExpertiseStore(ch.StoreAs)) earlierExpertise.Add(ch);
            }

            if (earlierFeats.Count > 0)
            {
                var step = Math.Max(1, rules.AbilityScoreIncrementPerAsi);
                var asiTaken = _character.CreationAsiPicks.Count + _character.LevelUpBumps.Values.Sum() / step;
                var optionIds = new HashSet<string>(earlierFeats.SelectMany(c => c.Options.Select(o => o.Id)), StringComparer.OrdinalIgnoreCase);
                var featsOwned = optionIds.Count(id => rules.AsiAbilityFromToken(id) == null && _character.Features.Contains(rules.FeatFeatureLine(id)));
                var missing = earlierFeats.Count - Math.Min(earlierFeats.Count, featsOwned + asiTaken);
                for (var i = 0; i < missing; i++) result.Add(earlierFeats[i]);
            }

            var have = _character.ExpertiseSkills.Count;
            var cum = 0;
            foreach (var ch in earlierExpertise)
            {
                cum += ch.ChooseCount;
                if (have < cum) result.Add(ch);
            }

            result.Sort((a, b) => a.Level.CompareTo(b.Level));
            return result;
        }

        public async Task ApplyLevelChoicesAsync(Dictionary<string, List<string>> picked)
        {
            if (picked == null || picked.Count == 0) return;

            // Keyed by the choice's StoreAs, so this asks the template rather than sniffing the key for a substring.
            var choiceRules = App.PM?.Rules ?? new GameRules();

            foreach (var kv in picked)
            {
                var isSkillKey = choiceRules.IsSkillStore(kv.Key);
                var isExpertiseKey = choiceRules.IsExpertiseStore(kv.Key);
                var isFeatKey = choiceRules.IsFeatStore(kv.Key);
                var isSubclassKey = choiceRules.IsSubclassStore(kv.Key);

                foreach (var token in kv.Value)
                {
                    var bumped = choiceRules.AsiAbilityFromToken(token);
                    if (bumped != null)
                    {
                        ApplyAbilityBump(bumped);
                        continue;
                    }

                    if (!_character.LevelChoices.TryGetValue(kv.Key, out var list))
                    {
                        list = new List<string>();
                        _character.LevelChoices[kv.Key] = list;
                    }
                    if (!list.Contains(token)) list.Add(token);

                    if (isSkillKey && !_character.ProficientSkills.Contains(token))
                        _character.ProficientSkills.Add(token);

                    // Expertise only ever lands on a skill you already have, the level up pool is built from your proficient list so no need to add proficiency here
                    if (isExpertiseKey && !_character.ExpertiseSkills.Contains(token))
                        _character.ExpertiseSkills.Add(token);

                    if (isFeatKey)
                    {
                        var line = choiceRules.FeatFeatureLine(token);
                        if (!_character.Features.Contains(line)) _character.Features.Add(line);
                    }

                    if (isSubclassKey)
                    {
                        var subLine = choiceRules.SubclassFeatureLine(token);
                        if (!_character.Features.Contains(subLine)) _character.Features.Add(subLine);
                    }
                }
            }

            ApplySkillProficiencies(_character);
            QueueSave();
            await ResolveProficienciesLiveAsync();
            await ReloadFeaturesAsync();
        }

        private void ApplyAbilityBump(string abbrev)
        {
            var vm = Abilities.FirstOrDefault(a => string.Equals(a.ShortName, abbrev, StringComparison.OrdinalIgnoreCase));
            if (vm == null) return;
            var cap = App.PM?.Rules?.AbilityScoreCap ?? 20;
            var step = App.PM?.Rules?.AbilityScoreIncrementPerAsi ?? 1;
            if (vm.Score < cap)
            {
                var before = vm.Score;
                vm.Score = Math.Min(cap, vm.Score + step);
                var applied = vm.Score - before;
                if (applied > 0 && !string.IsNullOrEmpty(vm.Id))
                {
                    _character.LevelUpBumps.TryGetValue(vm.Id, out var cur);
                    _character.LevelUpBumps[vm.Id] = cur + applied;
                }
            }
        }
        public event Action<InventoryItemViewModel> OpenItemViewRequested;
        public event Action<InventoryItemViewModel> OpenItemEditRequested;

        private void OnViewRequested(InventoryItemViewModel item) => OpenItemViewRequested?.Invoke(item);
        private void OnEditRequested(InventoryItemViewModel item) => OpenItemEditRequested?.Invoke(item);

        public async Task ResolveProficienciesLiveAsync()
        {
            if (App.PM == null) return;
            var granted = await App.PM.ResolveProficienciesAsync(_character.ClassId, _character.RaceId);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Proficiencies.Clear();
                foreach (var p in granted.Armor.Concat(granted.Weapon).Concat(granted.Other).Concat(_character.ProficientTools).Distinct())
                    Proficiencies.Add(p);

                var inherentSaves = new HashSet<string>(granted.SaveIds, StringComparer.OrdinalIgnoreCase);
                foreach (var a in Abilities)
                {
                    var saveId = a.ShortName.ToLowerInvariant();
                    if (inherentSaves.Contains(saveId))
                    {
                        a.SaveProficient = true;
                        a.IsInherent = true;
                    }
                    else if (a.IsInherent)
                    {
                        a.IsInherent = false;
                    }
                }
            });
        }

        private async Task ReloadFeatChoicesAsync()
        {
            if (App.PM == null) return;
            var choices = await App.PM.ResolveFeatChoicesAsync(_character);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FeatChoices.Clear();
                foreach (var c in choices) FeatChoices.Add(new ChoiceGroupViewModel(c));
                HasFeatChoices = FeatChoices.Count > 0;
            });
        }

        private void ApplyFeatChoice(ChoiceGroupViewModel group)
        {
            if (group == null || !group.IsSatisfied) return;
            var selected = group.Selected.ToList();
            foreach (var opt in selected)
            {
                var optRules = App.PM?.Rules ?? new GameRules();
                if (optRules.IsToolProficiencyOption(opt.Id))
                {
                    if (!_character.ProficientTools.Contains(opt.Name)) _character.ProficientTools.Add(opt.Name);
                }
                else if (optRules.IsGrantedSpellOption(opt.Id))
                {
                    if (!_character.GrantedSpellIds.Contains(opt.Id)) _character.GrantedSpellIds.Add(opt.Id);
                }
                else if (!_character.ProficientSkills.Contains(opt.Name))
                {
                    _character.ProficientSkills.Add(opt.Name);
                }
            }
            _character.LevelChoices[group.Id] = selected.Select(o => o.Id).ToList();
            ApplySkillProficiencies(_character);
            _ = ResolveProficienciesLiveAsync();
            _ = LoadSpellbookAsync();
            QueueSave();
            FeatChoices.Remove(group);
            HasFeatChoices = FeatChoices.Count > 0;
        }

        private CharacterBonuses _bonuses = new();

        private void ApplyFeatureBonuses()
        {
            foreach (var a in Abilities)
            {
                a.BonusToScore = _bonuses.AbilityBonus(a.Id);
                a.SaveBonus = _bonuses.SavingThrow;
            }
            RecalculateAc();
            this.RaisePropertyChanged(nameof(InitiativeDisplay));
            this.RaisePropertyChanged(nameof(MaxHpBonus));
            this.RaisePropertyChanged(nameof(EffectiveMaxHp));
            this.RaisePropertyChanged(nameof(HasMaxHpBonus));
            this.RaisePropertyChanged(nameof(MaxHpBonusLabel));
            this.RaisePropertyChanged(nameof(ResistancesLabel));
            this.RaisePropertyChanged(nameof(HasResistances));
        }

        public async Task ReloadFeaturesAsync()
        {
            if (App.PM == null) return;
            var lineRules = App.PM.Rules;

            string chosenSubclass = "";
            var chosenSubclasses = new List<string>();
            foreach (var stored in _character.Features)
            {
                var sub = lineRules.SubclassFromFeatureLine(stored);
                if (sub == null) continue;
                if (chosenSubclass.Length == 0) chosenSubclass = sub;
                if (!chosenSubclasses.Contains(sub)) chosenSubclasses.Add(sub);
            }

            var classLevels = _character.ClassLevels != null && _character.ClassLevels.Count > 0
                ? _character.ClassLevels
                : new List<ClassLevel> { new ClassLevel(_character.ClassId ?? "", Level) };
            var feats = new List<(string Name, string Description, int Level, bool Enforced)>();
            var seenFeats = new HashSet<string>();
            foreach (var cl in classLevels)
                foreach (var f in await App.PM.ReadClassFeatureRowsAsync(cl.ClassId, cl.Level, chosenSubclasses))
                    if (seenFeats.Add(f.Name + "|" + f.Level)) feats.Add(f);
            feats.Sort((a, b) => a.Level.CompareTo(b.Level));
            var chosenDescs = await App.PM.ReadChosenOptionDescriptionsAsync();
            var slotFeatureName = await App.PM.ReadSubclassSlotFeatureNameAsync(_character.ClassId);
            var featNames = await App.PM.LoadFeatNamesAsync();
            var firedFeatIds = await App.PM.ReadEngineFiredFeatIdsAsync();

            var canFold = !string.IsNullOrEmpty(slotFeatureName)
                && !string.IsNullOrEmpty(chosenSubclass)
                && feats.Any(f => string.Equals(f.Name, slotFeatureName, StringComparison.OrdinalIgnoreCase));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Features.Clear();

                foreach (var stored in _character.Features)
                {
                    var isSubclass = lineRules.IsSubclassFeatureLine(stored);
                    var isFeat = lineRules.IsFeatFeatureLine(stored);
                    if (!isSubclass && !isFeat) continue;
                    if (isSubclass && canFold) continue;
                    if (isFeat)
                    {
                        var fid = lineRules.FeatFromFeatureLine(stored) ?? "";
                        var display = featNames.TryGetValue(fid, out var fnm) ? lineRules.FeatFeatureLine(fnm) : stored;
                        Features.Add(new FeatureEntry(display, "", 0, OnFeatureClicked, !firedFeatIds.Contains(fid)));
                        continue;
                    }
                    Features.Add(new FeatureEntry(stored, LookupChosenDesc(stored, chosenDescs), 0, OnFeatureClicked));
                }

                foreach (var (name, desc, lvl, enforced) in feats)
                {
                    if (canFold && string.Equals(name, slotFeatureName, StringComparison.OrdinalIgnoreCase))
                    {
                        var subDesc = chosenDescs.TryGetValue(chosenSubclass, out var sd) && !string.IsNullOrWhiteSpace(sd) ? sd : desc;
                        Features.Add(new FeatureEntry(name + ": " + chosenSubclass, subDesc, lvl, OnFeatureClicked));
                    }
                    else
                    {
                        Features.Add(new FeatureEntry(name, desc, lvl, OnFeatureClicked, !enforced));
                    }
                }
            });

            _bonuses = await App.PM.ResolveCharacterBonusesAsync(_character.Id);
            await Dispatcher.UIThread.InvokeAsync(ApplyFeatureBonuses);

            await ReloadLevelSelectionsAsync();
        }

        public async Task ReloadLevelSelectionsAsync()
        {
            if (App.PM == null) return;

            var defs = await App.PM.ReadBuilderChoicesAsync(_character.ClassId, Level);

            var optionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var storeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs)
            {
                if (!string.IsNullOrEmpty(d.StoreAs) && !string.IsNullOrWhiteSpace(d.Label) && !storeLabels.ContainsKey(d.StoreAs))
                    storeLabels[d.StoreAs] = d.Label;
                foreach (var o in d.Options)
                    if (!string.IsNullOrEmpty(o.Id) && !optionNames.ContainsKey(o.Id)) optionNames[o.Id] = o.Name;
            }

            var rows = new List<LevelSelectionEntry>();
            foreach (var kv in _character.LevelChoices)
            {
                if ((App.PM?.Rules ?? new GameRules()).IsFeatStore(kv.Key)) continue;

                var group = storeLabels.TryGetValue(kv.Key, out var lbl) && !string.IsNullOrWhiteSpace(lbl) ? lbl : kv.Key;
                foreach (var token in kv.Value)
                {
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    var value = optionNames.TryGetValue(token, out var nm) && !string.IsNullOrWhiteSpace(nm) ? nm : token;
                    rows.Add(new LevelSelectionEntry(group, value));
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LevelSelections.Clear();
                foreach (var row in rows) LevelSelections.Add(row);
                HasLevelSelections = LevelSelections.Count > 0;
            });
        }

        private static string LookupChosenDesc(string storedLine, Dictionary<string, string> descs)
        {
            var idx = storedLine.IndexOf(':');
            var bare = idx >= 0 ? storedLine[(idx + 1)..].Trim() : storedLine.Trim();
            return descs.TryGetValue(bare, out var d) ? d : "";
        }

        public async Task LoadSpellbookAsync()
        {
            if (App.PM == null) return;
            try
            {
                var reachable = await App.PM.LoadCastableSpellsAsync(_character);
                var limits = await App.PM.ResolveSpellPrepLimitsAsync(_character);
                var prepared = new HashSet<string>(_character.PreparedSpellIds ?? new List<string>());

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CantripLimit = limits.Cantrips;
                    PreparedLimit = limits.Prepared;
                    Spellbook.Clear();
                    foreach (var s in reachable)
                        Spellbook.Add(new SpellPrepEntry(s, prepared.Contains(s.Id), OnSpellPrepToggled, OnSpellView));
                    RebuildSpellGroups();
                    HasSpellbook = Spellbook.Count > 0;
                    RecountPrepared();
                });
                await LoadCastablesAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Sheet] spellbook load failed", ex);
            }
        }

        private void OnSpellPrepToggled(SpellPrepEntry entry)
        {
            if (_suppressSpellToggle) return;

            if (entry.IsPrepared)
            {
                if (entry.Level == 0 && CantripLimit > 0 && Spellbook.Count(s => s.Level == 0 && s.IsPrepared) > CantripLimit)
                {
                    _suppressSpellToggle = true;
                    entry.IsPrepared = false;
                    _suppressSpellToggle = false;
                    return;
                }
                if (entry.Level > 0 && PreparedLimit > 0 && Spellbook.Count(s => s.Level > 0 && s.IsPrepared) > PreparedLimit)
                {
                    _suppressSpellToggle = true;
                    entry.IsPrepared = false;
                    _suppressSpellToggle = false;
                    return;
                }
            }

            _character.PreparedSpellIds = Spellbook
                .Where(s => s.IsPrepared)
                .Select(s => s.SpellId)
                .ToList();
            RecountPrepared();
            QueueSave();
            _ = LoadCastablesAsync();
        }

        private bool _suppressSpellToggle;

        private void RecountPrepared()
        {
            CantripCount = Spellbook.Count(s => s.Level == 0 && s.IsPrepared);
            PreparedCount = Spellbook.Count(s => s.Level > 0 && s.IsPrepared);
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

        public async Task LoadCastablesAsync()
        {
            if (App.PM == null) return;
            try
            {
                var info = await App.PM.ResolveSpellcastingAsync(_character);
                if (info == null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { Castables.Clear(); HasCastables = false; });
                    return;
                }
                _castSaveDc = info.Value.SaveDc;
                _castAttackBonus = info.Value.AttackBonus;
                _castAbilityMod = info.Value.AbilityMod;
                _castCharLevel = info.Value.Level;

                var spells = await App.PM.LoadPreparedSpellsAsync(_character);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Castables.Clear();
                    foreach (var s in spells) Castables.Add(s);
                    HasCastables = Castables.Count > 0;
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Sheet] castable load failed", ex);
            }
        }

        private async Task CastSpell(CastableSpell spell)
        {
            if (spell == null) return;
            if (spell.Level == 0) { CastAtLevel(spell, 0); return; }

            var options = SpellSlots
                .Where(r => r.Level >= spell.Level && r.CanSpend)
                .Select(r => new CastLevelOption(r.Level, App.PM?.Rules?.SpellLevelName(r.Level) ?? ("Lv " + r.Level), r.Remaining))
                .ToList();
            if (options.Count == 0)
            {
                RollToChat?.Invoke($"{Name} has no slot left to cast {spell.Name}.", false);
                return;
            }

            int castLevel;
            if (options.Count == 1) castLevel = options[0].Level;
            else
            {
                var chosen = ChooseCastLevelRequested != null ? await ChooseCastLevelRequested(spell.Name, options) : (int?)options[0].Level;
                if (chosen == null) return;
                castLevel = chosen.Value;
            }
            CastAtLevel(spell, castLevel);
        }

        private void CastAtLevel(CastableSpell spell, int castLevel)
        {
            if (castLevel > 0)
            {
                var row = SpellSlots.FirstOrDefault(r => r.Level == castLevel && r.CanSpend);
                if (row == null) { RollToChat?.Invoke($"{Name} has no slot left at that level.", false); return; }
                SpendSlot(row);
            }
            var line = SpellCaster.Resolve(Name, spell.Name, spell.Level, spell.EffectsJson,
                castLevel, _castCharLevel, _castAbilityMod, ProficiencyBonus, _castSaveDc, _castAttackBonus, null);
            RollToChat?.Invoke(line, false);
        }

        public async Task LoadSpellSlotsAsync()
        {
            if (App.PM == null) return;
            try
            {
                var max = await App.PM.ResolveSpellSlotsAsync(_character);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SpellSlots.Clear();
                    foreach (var lvl in max.Keys.OrderBy(k => k))
                    {
                        if (max[lvl] <= 0) continue;
                        var used = _character.SpellSlotsUsed.TryGetValue(lvl, out var u) ? u : 0;
                        if (used > max[lvl]) used = max[lvl];
                        SpellSlots.Add(new SheetSlotRow(lvl, max[lvl], used, SpendSlot, RestoreSlot));
                    }
                    _character.SpellSlotsMax = max;
                    HasSpellSlots = SpellSlots.Count > 0;
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Sheet] spell slot load failed", ex);
            }
        }

        private void SpendSlot(SheetSlotRow row)
        {
            if (row == null || !row.CanSpend) return;
            row.Used++;
            _character.SpellSlotsUsed[row.Level] = row.Used;
            QueueSave();
        }

        private void RestoreSlot(SheetSlotRow row)
        {
            if (row == null || !row.CanRestore) return;
            row.Used--;
            _character.SpellSlotsUsed[row.Level] = row.Used;
            QueueSave();
        }

        public async Task LoadResourcesAsync()
        {
            if (App.PM == null) return;
            try
            {
                var defs = await App.PM.ResolveClassResourcesAsync(_character);
                var resourceRules = App.PM.Rules.ClassResources;
                bool Inspires(string id) => resourceRules.TryGetValue(id, out var fx) && !string.IsNullOrWhiteSpace(fx.InspireDie);
                var party = defs.Any(d => Inspires(d.Id))
                    ? (await App.PM.LoadAllCharactersInCampaignAsync()).Where(p => p.CharacterKind == "pc" && p.Id != _character.Id).ToList()
                    : new List<CharacterRuntime>();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ClassResources.Clear();
                    foreach (var d in defs)
                    {
                        var used = _character.ResourcesUsed.TryGetValue(d.Id, out var u) ? u : 0;
                        if (used > d.Max) used = d.Max;
                        var row = new SheetResourceRow(d.Id, d.Name, d.ResetOn, d.Max, used, SpendResource, RestoreResource);
                        if (Inspires(d.Id))
                        {
                            row.HasInspire = true;
                            foreach (var p in party) row.InspireTargets.Add(p);
                        }
                        ClassResources.Add(row);
                        _character.ResourcesMax[d.Id] = d.Max;
                    }
                    HasClassResources = ClassResources.Count > 0;
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Sheet] resource load failed", ex);
            }
        }

        private void SpendResource(SheetResourceRow row)
        {
            if (row == null || !row.CanSpend) return;
            row.Used++;
            _character.ResourcesUsed[row.Id] = row.Used;
            ApplyResourceEffect(row);
            QueueSave();
        }

        private void ApplyResourceEffect(SheetResourceRow row)
        {
            var rules = App.PM?.Rules;
            if (rules == null || !rules.ClassResources.TryGetValue(row.Id, out var effect)) return;

            var context = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["level"] = Level,
                ["prof"] = ProficiencyBonus
            };
            foreach (var a in Abilities) context[a.ShortName] = a.Modifier;

            if (!string.IsNullOrWhiteSpace(effect.Condition))
                CombatConditionApplied?.Invoke(effect.Condition);

            if (!string.IsNullOrWhiteSpace(effect.InspireDie) && row.SelectedInspireTarget != null)
            {
                var ally = row.SelectedInspireTarget;
                InspirationGranted?.Invoke(ally.Id, ally.Name, rules.Substitute(effect.InspireDie, context));
                row.SelectedInspireTarget = null;
            }

            var healed = 0;
            if (!string.IsNullOrWhiteSpace(effect.Heal))
            {
                var expr = rules.Substitute(effect.Heal, context);
                try { healed = Math.Max(0, DiceManager.Roll(expr).Total); }
                catch { healed = 0; }
            }
            else if (effect.HealPerPoint > 0) healed = effect.HealPerPoint;

            if (healed <= 0 || CurrentHp <= 0) return;
            CurrentHp = Math.Min(EffectiveMaxHp, CurrentHp + healed);
        }

        private void RestoreResource(SheetResourceRow row)
        {
            if (row == null || !row.CanRestore) return;
            row.Used--;
            _character.ResourcesUsed[row.Id] = row.Used;
            QueueSave();
        }

        private void RaiseDeathDerived()
        {
            UpdateDeathPips();
            this.RaisePropertyChanged(nameof(IsStable));
            this.RaisePropertyChanged(nameof(IsDead));
            this.RaisePropertyChanged(nameof(ShowDeathCard));
            this.RaisePropertyChanged(nameof(DeathSaveStatus));
            this.RaisePropertyChanged(nameof(CanRollDeathSave));
        }

        public bool CanRollDeathSave => IsDowned && !IsStable && !IsDead;

        private void OnFeatureClicked(FeatureEntry f) => OpenFeatureRequested?.Invoke(f);

        private async Task LoadDisplayDataAsync(CharacterRuntime character)
        {
            if (App.PM?.DbManager == null) return;

            string? raceName = null, subraceName = null, className = null;
            var senses = new List<string>();
            var traitIds = new List<string>();
            var traitEntries = new List<(string Name, string Desc)>();
            var conditionRows = new List<(string Id, string Name)>();

            await using (var conn = await App.PM.DbManager.OpenAsync())
            {
                if (!string.IsNullOrEmpty(character.RaceId))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT Name, {CatalogResolver.ResolvedJsonSql("Races", "Races")} FROM Races WHERE Id = $id LIMIT 1";
                    CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                    cmd.Parameters.AddWithValue("$id", character.RaceId);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        raceName = r.GetString(0);
                        if (!r.IsDBNull(1)) { CollectSenses(r.GetString(1), senses); CollectTraitIds(r.GetString(1), traitIds); }
                    }
                }

                if (!string.IsNullOrEmpty(character.SubraceId))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT Name, {CatalogResolver.ResolvedJsonSql("Subraces", "Subraces")} FROM Subraces WHERE Id = $id LIMIT 1";
                    CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                    cmd.Parameters.AddWithValue("$id", character.SubraceId);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        subraceName = r.GetString(0);
                        if (!r.IsDBNull(1)) { CollectSenses(r.GetString(1), senses); CollectTraitIds(r.GetString(1), traitIds); }
                    }
                }

                if (!string.IsNullOrEmpty(character.ClassId))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Name FROM Classes WHERE Id = $id LIMIT 1";
                    cmd.Parameters.AddWithValue("$id", character.ClassId);
                    var nm = await cmd.ExecuteScalarAsync();
                    className = nm as string;
                }

                if (traitIds.Count > 0)
                {
                    await using var cmd = conn.CreateCommand();
                    var ps = new List<string>();
                    for (int i = 0; i < traitIds.Count; i++) { ps.Add($"$t{i}"); cmd.Parameters.AddWithValue($"$t{i}", traitIds[i]); }
                    cmd.CommandText = $"SELECT Name, Description FROM Traits WHERE Id IN ({string.Join(",", ps)})";
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                        traitEntries.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
                }
            }

            if (!string.IsNullOrEmpty(character.BackgroundId) && App.PM != null)
            {
                var bg = (await App.PM.LoadBackgroundsAsync()).FirstOrDefault(b => b.Id == character.BackgroundId);
                if (bg != null) traitEntries.Insert(0, ($"{bg.Name} (background)", bg.Description));
            }

            var cats = App.PM != null ? await App.PM.ReadConditionsAsync() : new List<(string, string)>();
            conditionRows = cats;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!string.IsNullOrEmpty(raceName)) Race = raceName!;
                if (!string.IsNullOrEmpty(subraceName)) Subrace = subraceName!;
                if (!string.IsNullOrEmpty(className)) Class = className!;

                foreach (var s in senses)
                    if (!_baseSenses.Contains(s)) _baseSenses.Add(s);
                RecalculateSenses();

                RacialTraits.Clear();
                foreach (var (name, desc) in traitEntries)
                    RacialTraits.Add(new FeatureEntry(name, desc, 0, OnFeatureClicked));
                HasRacialTraits = RacialTraits.Count > 0;

                var active = new HashSet<string>(Conditions, StringComparer.OrdinalIgnoreCase);
                foreach (var (id, name) in conditionRows)
                    ToggleableConditions.Add(new ConditionToggle(name, active.Contains(name), OnConditionToggled));
            });
        }

        private static void CollectSenses(string raceDataJson, List<string> sink)
        {
            try
            {
                using var doc = JsonDocument.Parse(raceDataJson);
                if (doc.RootElement.TryGetProperty("TraitIds", out var traits) && traits.ValueKind == JsonValueKind.Array)
                {
                    var rules = App.PM?.Rules ?? new GameRules();
                    foreach (var t in traits.EnumerateArray())
                    {
                        var label = rules.SenseLabelFor(t.GetString());
                        if (label != null && !sink.Contains(label)) sink.Add(label);
                    }
                }
            }
            catch (JsonException) { }
        }

        private static void CollectTraitIds(string dataJson, List<string> sink)
        {
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.TryGetProperty("TraitIds", out var traits) && traits.ValueKind == JsonValueKind.Array)
                {
                    var rules = App.PM?.Rules ?? new GameRules();
                    foreach (var t in traits.EnumerateArray())
                    {
                        var id = t.GetString();
                        if (string.IsNullOrEmpty(id) || rules.IsSenseTrait(id)) continue;
                        if (!sink.Contains(id)) sink.Add(id);
                    }
                }
            }
            catch (JsonException) { }
        }

        private void OnConditionToggled(ConditionToggle toggle)
        {
            if (toggle.IsActive)
            {
                if (!Conditions.Contains(toggle.Name)) Conditions.Add(toggle.Name);
            }
            else
            {
                Conditions.Remove(toggle.Name);
            }
            _character.Conditions = Conditions.ToList();
            RefreshAttackEffect();
            QueueSave();
        }

        public async Task ReloadInventoryAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { Inventory.Clear(); EquippedItems.Clear(); });
            await LoadInventoryAsync();
        }

        private async Task LoadInventoryAsync()
        {
            if (App.PM?.GameDataRepo == null || App.PM?.DbManager == null) return;

            var instances = await App.PM.GameDataRepo.LoadInstancesForCharacterAsync(_character.Id);

            if (instances.Count == 0 && _character.InventoryInstanceIds != null && _character.InventoryInstanceIds.Count > 0)
            {
                foreach (var g in _character.InventoryInstanceIds.GroupBy(x => x))
                {
                    var migrated = new ItemInstance
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        CampaignId = _character.CampaignId,
                        BaseItemId = g.Key,
                        OwnerCharacterId = _character.Id,
                        Quantity = g.Count()
                    };
                    await App.PM.GameDataRepo.SaveInstanceAsync(migrated);
                }
                _character.InventoryInstanceIds.Clear();
                QueueSave();
                instances = await App.PM.GameDataRepo.LoadInstancesForCharacterAsync(_character.Id);
            }

            var metaById = new Dictionary<string, (string Name, string DataJson)>();
            await using (var conn = await App.PM.DbManager.OpenAsync())
            {
                foreach (var bid in instances.Select(i => i.BaseItemId).Distinct())
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT Name, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items WHERE Id = $id";
                    CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                    cmd.Parameters.AddWithValue("$id", bid);
                    await using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                        metaById[bid] = (r.GetString(0), r.IsDBNull(1) ? "{}" : r.GetString(1));
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _suppressSave = true;
                try
                {
                    _instanceById.Clear();
                    foreach (var inst in instances)
                    {
                        _instanceById[inst.Id] = inst;

                        string name, dataJson;
                        if (metaById.TryGetValue(inst.BaseItemId, out var meta)) { name = meta.Name; dataJson = meta.DataJson; }
                        else { name = inst.BaseItemId; dataJson = "{}"; }
                        if (!string.IsNullOrEmpty(inst.CustomName)) name = inst.CustomName!;

                        var vm = new InventoryItemViewModel(inst.Id, inst.BaseItemId, name, dataJson, inst.Quantity <= 0 ? 1 : inst.Quantity, inst.StateJson);
                        vm.IsEquipped = ReadEquipped(inst.StateJson);
                        vm.IsOffHand = ReadOffHand(inst.StateJson);
                        vm.IsAttuned = ReadAttuned(inst.StateJson);
                        if (_character.WeaponProficiency.TryGetValue(inst.Id, out var prof)) vm.IsProficient = prof;
                        AddInventoryItem(vm);
                    }
                    RecalculateAc();
                    RecalculateSenses();
                    RaiseAttunement();
                }
                finally { _suppressSave = false; }
            });
        }

        public async Task<List<SheetCatalogItem>> LoadCatalogItemsAsync()
        {
            var result = new List<SheetCatalogItem>();
            if (App.PM?.DbManager == null) return result;

            var cid = App.PM.GetCampaignId();

            await using var conn = await App.PM.DbManager.OpenAsync();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT i.Id, i.Name, i.ItemType, i.DataJson
                    FROM Items i
                    INNER JOIN CampaignItems ci ON ci.ItemId = i.Id
                    WHERE ci.CampaignId = $cid AND ci.IsEnabled = 1
                    ORDER BY i.Name COLLATE NOCASE
                    """;
                cmd.Parameters.AddWithValue("$cid", cid);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    result.Add(new SheetCatalogItem(
                        r.GetString(0), r.GetString(1),
                        r.IsDBNull(2) ? "" : r.GetString(2),
                        r.IsDBNull(3) ? "{}" : r.GetString(3)));
            }

            if (result.Count == 0)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Id, Name, ItemType, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    result.Add(new SheetCatalogItem(
                        r.GetString(0), r.GetString(1),
                        r.IsDBNull(2) ? "" : r.GetString(2),
                        r.IsDBNull(3) ? "{}" : r.GetString(3)));
            }

            return result;
        }

        public void AddCatalogItem(string itemId, string name, string dataJson)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            var inst = new ItemInstance
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = _character.CampaignId,
                BaseItemId = itemId,
                OwnerCharacterId = _character.Id,
                Quantity = 1
            };
            _instanceById[inst.Id] = inst;
            AddInventoryItem(new InventoryItemViewModel(inst.Id, itemId, name, dataJson, 1));
            _ = PersistAndBroadcastInstanceAsync(inst);
        }

        public async Task LoadWalletAsync()
        {
            if (App.PM?.GameDataRepo == null) return;
            var currencies = await App.PM.GameDataRepo.LoadCurrenciesAsync();
            await Dispatcher.UIThread.InvokeAsync(() => RebuildWallet(currencies));
        }

        private void RebuildWallet(List<Currency> currencies)
        {
            Wallet.Clear();
            foreach (var cur in currencies.OrderByDescending(c => c.EqualToBase))
            {
                var amount = _character.Wallet.TryGetValue(cur.Id, out var v) ? v : 0;
                Wallet.Add(new WalletEntry(cur, amount));
            }

            if (WalletAdds.Count == 0)
                foreach (var cur in currencies.OrderByDescending(c => c.EqualToBase))
                    WalletAdds.Add(new WalletAddRow(cur.Id, cur.Name, InventoryEngine.FallbackGlyph(cur)));
        }

        private void ApplyAddMoney()
        {
            bool any = false;
            foreach (var row in WalletAdds)
            {
                var add = (long)row.Amount;
                if (add == 0) continue;
                var current = _character.Wallet.TryGetValue(row.CurrencyId, out var v) ? v : 0;
                var next = current + add;
                if (next < 0) next = 0;
                _character.Wallet[row.CurrencyId] = next;
                any = true;
            }
            if (!any) return;
            foreach (var row in WalletAdds) row.Amount = 0;
            _ = LoadWalletAsync();
            QueueSave();
            ShowWalletEditor = false;
        }

        private void RefreshAttackEffect()
        {
            var mode = ConditionEffects.AttackMode(Conditions);
            if (mode == RollMode.Normal) { AttackRollEffectLabel = ""; return; }
            var names = ConditionEffects.RelevantConditions(Conditions);
            var word = mode == RollMode.Advantage ? "Advantage" : "Disadvantage";
            AttackRollEffectLabel = names.Count > 0 ? $"Attacks: {word} ({string.Join(", ", names)})" : $"Attacks: {word}";
        }

        public Dictionary<string, int> CurrentAbilities()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in Abilities) result[a.ShortName] = a.Score;
            return result;
        }

        public void ApplyBaseEdits(string name, int level, IReadOnlyDictionary<string, int> abilities)
        {
            Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
            var rules = App.PM?.Rules;
            Level = Math.Clamp(level, 1, rules?.MaxLevel ?? 20);

            var cap = rules?.AbilityScoreHardCap ?? 30;
            foreach (var a in Abilities)
                if (abilities.TryGetValue(a.ShortName, out var v))
                    a.Score = Math.Clamp(v, 1, cap);
        }

        public void AddInventoryItem(InventoryItemViewModel item)
        {
            item.DamageRolled += OnDamageRolled;
            item.AttackRolled += OnAttackRolled;
            item.CritRolled += OnCritRolled;
            item.RemoveRequested += OnRemoveRequested;
            item.ViewRequested += OnViewRequested;
            item.EditRequested += OnEditRequested;
            item.EquipToggled += OnEquipToggled;
            item.OffHandToggled += OnOffHandToggled;
            item.AttuneToggled += OnAttuneToggled;
            item.UseRequested += OnUseRequested;
            item.PropertyChanged += OnInventoryItemPropertyChanged;
            Inventory.Add(item);
            if (item.IsEquipped && !EquippedItems.Contains(item)) EquippedItems.Add(item);

            if (!_suppressSave) DmLogPing?.Invoke($"{Name} added {item.Name} to inventory.");
        }

        private void OnAttackRolled(InventoryItemViewModel item)
        {
            var bonus = WeaponAbilityMod(item) + (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(item.IsProficient), ProficiencyBonus) + item.HitBonus + _bonuses.AttackRoll;
            var mode = ConditionEffects.AttackMode(Conditions);
            var first = DiceManager.RollCore(App.PM?.Rules?.AttackDie ?? 20);
            int nat;
            string rollNote;
            if (mode == RollMode.Normal)
            {
                nat = first;
                rollNote = $"[{nat}]";
            }
            else
            {
                var second = DiceManager.RollCore(App.PM?.Rules?.AttackDie ?? 20);
                nat = mode == RollMode.Advantage ? Math.Max(first, second) : Math.Min(first, second);
                var word = mode == RollMode.Advantage ? "adv" : "dis";
                rollNote = $"[{first}, {second}] {word} -> [{nat}]";
            }
            var total = nat + bonus;
            var bonusText = bonus >= 0 ? $"+{bonus}" : bonus.ToString();
            var critNote = (App.PM?.Rules.IsCrit(nat) ?? nat == 20) ? " CRIT!" : (App.PM?.Rules.IsFumble(nat) ?? nat == 1) ? " (nat 1)" : "";
            var text = $"{Name} attacks with {item.Name}: 1d20{bonusText} -> {rollNote}{bonusText} = {total}{critNote}";
            RollToChat?.Invoke(text, false);
        }

        private void OnDamageRolled(InventoryItemViewModel item) => RollWeaponDamage(item, false);
        private void OnCritRolled(InventoryItemViewModel item) => RollWeaponDamage(item, true);

        private void RollWeaponDamage(InventoryItemViewModel item, bool crit)
        {
            var components = item.DamageValues.Where(d => !string.IsNullOrWhiteSpace(d.DiceId)).ToList();
            if (components.Count == 0)
            {
                DmLogPing?.Invoke($"{Name}'s {item.Name} has no damage dice to roll.");
                return;
            }

            var primaryMod = WeaponAbilityMod(item) + item.DamageBonus + _bonuses.DamageRoll;

            var lines = new List<string>();
            var grand = 0;
            for (int i = 0; i < components.Count; i++)
            {
                var d = components[i];
                var count = d.Count > 0 ? d.Count : 1;
                var mod = (i == 0 ? primaryMod : 0) + d.Flat;
                var expr = $"{count}{d.DiceId}";
                if (mod > 0) expr += $"+{mod}";
                else if (mod < 0) expr += mod.ToString();

                if (!DiceManager.TryRoll(expr, crit, out var result) || result == null) continue;
                grand += result.Total;
                var typeName = DamageTypeName(d.TypeId);
                lines.Add($"{typeName} = {result.Total} [{result.Breakdown}]");
            }

            if (lines.Count == 0) return;

            var label = crit ? "crit damage" : "damage";
            var critNote = crit ? " (dice doubled)" : "";
            var detail = string.Join(", ", lines);
            var total = components.Count > 1 ? $" | total {grand}" : "";
            var text = $"{Name} {item.Name} {label}{critNote}: {detail}{total}";
            RollToChat?.Invoke(text, false);
        }

        private static string DamageTypeName(string typeId)
        {
            if (string.IsNullOrWhiteSpace(typeId)) return "Damage";
            var bare = (App.PM?.Rules ?? new GameRules()).DamageTypeLabel(typeId);
            return bare.Length == 0 ? "Damage" : char.ToUpperInvariant(bare[0]) + bare.Substring(1);
        }

        private int WeaponAbilityMod(InventoryItemViewModel item)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            return rules.AttackAbilitiesFor(item.WeaponCategory, item.IsRanged).Select(AbilityMod).DefaultIfEmpty(0).Max();
        }

        private int AbilityMod(string shortName) =>
            Abilities.FirstOrDefault(a => string.Equals(a.ShortName, shortName, StringComparison.OrdinalIgnoreCase))?.Modifier ?? 0;

        private async void OnRemoveRequested(InventoryItemViewModel item)
        {
            try
            {
                item.DamageRolled -= OnDamageRolled;
                item.AttackRolled -= OnAttackRolled;
                item.CritRolled -= OnCritRolled;
                item.RemoveRequested -= OnRemoveRequested;
                item.EquipToggled -= OnEquipToggled;
                item.OffHandToggled -= OnOffHandToggled;
                item.AttuneToggled -= OnAttuneToggled;
                item.UseRequested -= OnUseRequested;
                item.PropertyChanged -= OnInventoryItemPropertyChanged;
                Inventory.Remove(item);
                EquippedItems.Remove(item);
                RecalculateAc();
                RecalculateSenses();
                RaiseAttunement();

                if (_instanceById.Remove(item.InstanceId))
                    await RemoveAndBroadcastInstanceAsync(item.InstanceId);

                DmLogPing?.Invoke($"{Name} removed {item.Name}.");
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnRemoveRequested", ex); }
        }

        private void RecalculateAc()
        {
            var acRules = App.PM?.Rules;
            var dexMod = AbilityMod(acRules?.ArmorClassAbility ?? "dex");
            var equippedArmor = EquippedItems.Where(i => i.IsArmor).ToList();
            bool OffHand(InventoryItemViewModel i) => acRules != null
                ? acRules.IsOffHandArmor(i.ArmorType)
                : i.ArmorType.Contains("shield", StringComparison.OrdinalIgnoreCase);
            var shields = equippedArmor.Where(OffHand).ToList();
            var body = equippedArmor.FirstOrDefault(i => !OffHand(i));

            int ac;
            if (body != null)
            {
                var dexBonus = body.AllowsDexBonus
                    ? (body.MaxDexBonus > 0 ? Math.Min(dexMod, body.MaxDexBonus) : dexMod)
                    : 0;
                ac = body.BaseAC + dexBonus + body.AcBonus;
            }
            else ac = (App.PM?.Rules?.ArmorClassBase ?? 10) + dexMod;

            foreach (var sh in shields) ac += sh.BaseAC + sh.AcBonus;
            ac += _bonuses.ArmorClass;
            ArmorClass = ac;
        }

        private void RecalculateSenses()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Senses.Clear();
            foreach (var s in _baseSenses) if (!string.IsNullOrWhiteSpace(s) && seen.Add(s)) Senses.Add(s);
            foreach (var item in EquippedItems)
                foreach (var g in item.GrantedSenses) if (!string.IsNullOrWhiteSpace(g) && seen.Add(g)) Senses.Add(g);
        }

        private string _languagesLabel = "";
        public string LanguagesLabel
        {
            get => _languagesLabel;
            private set { this.RaiseAndSetIfChanged(ref _languagesLabel, value); this.RaisePropertyChanged(nameof(HasLanguages)); }
        }
        public bool HasLanguages => _languagesLabel.Length > 0;

        private async Task LoadLanguagesLabelAsync()
        {
            if (App.PM == null || _character.Languages.Count == 0) return;
            try
            {
                // A language the template no longer names still shows as its raw id, better an honest lang-x than silently dropping what the sheet knows.
                var known = await App.PM.LoadLanguagesAsync();
                var names = _character.Languages.Select(id => known.FirstOrDefault(l => l.Id == id)?.Name ?? id).ToList();
                await Dispatcher.UIThread.InvokeAsync(() => LanguagesLabel = string.Join(", ", names));
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[CharacterSheet] languages load failed", ex);
            }
        }

        private async void OnEquipToggled(InventoryItemViewModel item)
        {
            try
            {
                if (item.IsEquipped)
                {
                    if (!EquippedItems.Contains(item)) EquippedItems.Add(item);
                }
                else
                {
                    EquippedItems.Remove(item);
                    item.IsOffHand = false;
                }

                RecalculateAc();
                RecalculateSenses();

                if (_instanceById.TryGetValue(item.InstanceId, out var inst))
                {
                    inst.StateJson = WriteEquipped(inst.StateJson, item.IsEquipped);
                    if (!item.IsEquipped) inst.StateJson = WriteOffHand(inst.StateJson, false);
                    await PersistAndBroadcastInstanceAsync(inst);
                }

                DmLogPing?.Invoke($"{Name} {(item.IsEquipped ? "equipped" : "unequipped")} {item.Name}.");
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnEquipToggled", ex); }
        }

        private async void OnOffHandToggled(InventoryItemViewModel item)
        {
            try
            {
                if (item.IsOffHand)
                {
                    var prop = App.PM?.Rules?.OffHandWeaponProperty ?? "wp-light";
                    if (!item.IsWeapon || !item.WeaponCategory.Contains(prop, StringComparer.OrdinalIgnoreCase))
                    {
                        item.IsOffHand = false;
                        RollToChat?.Invoke($"{Name} needs a light weapon for the off hand, {item.Name} is not one.", false);
                        return;
                    }

                    if (!item.IsEquipped)
                    {
                        item.IsEquipped = true;
                        if (!EquippedItems.Contains(item)) EquippedItems.Add(item);
                    }

                    foreach (var other in Inventory.Where(i => i.IsOffHand && !ReferenceEquals(i, item)).ToList())
                    {
                        other.IsOffHand = false;
                        if (_instanceById.TryGetValue(other.InstanceId, out var otherInst))
                        {
                            otherInst.StateJson = WriteOffHand(otherInst.StateJson, false);
                            await PersistAndBroadcastInstanceAsync(otherInst);
                        }
                    }

                    // One off hand, so a weapon there sends the shield back to the backpack.
                    foreach (var shield in EquippedItems.Where(i => i.IsArmor && (App.PM?.Rules?.IsOffHandArmor(i.ArmorType) ?? false)).ToList())
                    {
                        shield.IsEquipped = false;
                        EquippedItems.Remove(shield);
                        if (_instanceById.TryGetValue(shield.InstanceId, out var shieldInst))
                        {
                            shieldInst.StateJson = WriteEquipped(shieldInst.StateJson, false);
                            await PersistAndBroadcastInstanceAsync(shieldInst);
                        }
                    }

                    RecalculateAc();
                    RecalculateSenses();
                }

                if (_instanceById.TryGetValue(item.InstanceId, out var inst))
                {
                    inst.StateJson = WriteOffHand(inst.StateJson, item.IsOffHand);
                    if (item.IsEquipped) inst.StateJson = WriteEquipped(inst.StateJson, true);
                    await PersistAndBroadcastInstanceAsync(inst);
                }

                DmLogPing?.Invoke($"{Name} {(item.IsOffHand ? "moved" : "took")} {item.Name} {(item.IsOffHand ? "to" : "out of")} the off hand.");
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnOffHandToggled", ex); }
        }

        private async void OnAttuneToggled(InventoryItemViewModel item)
        {
            try
            {
                if (item.IsAttuned)
                {
                    int limit = App.PM?.Rules?.AttunementLimit ?? 3;
                    if (limit > 0 && Inventory.Count(i => i.IsAttuned) > limit)
                    {
                        item.IsAttuned = false;
                        RollToChat?.Invoke($"{Name} can only be attuned to {limit} items at once.", false);
                        RaiseAttunement();
                        return;
                    }
                }

                if (_instanceById.TryGetValue(item.InstanceId, out var inst))
                {
                    inst.StateJson = WriteAttuned(inst.StateJson, item.IsAttuned);
                    await PersistAndBroadcastInstanceAsync(inst);
                }

                RaiseAttunement();
                DmLogPing?.Invoke($"{Name} {(item.IsAttuned ? "attuned to" : "broke attunement with")} {item.Name}.");
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnAttuneToggled", ex); }
        }

        private void RaiseAttunement()
        {
            this.RaisePropertyChanged(nameof(AttunedCount));
            this.RaisePropertyChanged(nameof(AttunementSummary));
        }

        private async void OnUseRequested(InventoryItemViewModel item)
        {
            try
            {
                if (item.HasHealing)
                {
                    int total;
                    string breakdown;
                    if (!string.IsNullOrEmpty(item.HealDiceId) && item.HealDiceCount > 0)
                    {
                        var expr = $"{item.HealDiceCount}{item.HealDiceId}";
                        if (item.HealFlat > 0) expr += $"+{item.HealFlat}";
                        else if (item.HealFlat < 0) expr += item.HealFlat.ToString();

                        if (DiceManager.TryRoll(expr, out var result) && result != null)
                        {
                            total = result.Total;
                            breakdown = result.Breakdown;
                        }
                        else { total = item.HealFlat; breakdown = item.HealFlat.ToString(); }
                    }
                    else
                    {
                        total = item.HealFlat;
                        breakdown = item.HealFlat.ToString();
                    }

                    var before = CurrentHp;
                    CurrentHp = Math.Min(EffectiveMaxHp, CurrentHp + total);
                    var gained = CurrentHp - before;
                    RollToChat?.Invoke($"{Name} uses {item.Name}: heals {total} [{breakdown}], +{gained} HP -> {CurrentHp}/{MaxHp}", false);
                    DmLogPing?.Invoke($"{Name} used {item.Name}, healed {gained} HP.");
                }
                else
                {
                    var note = !string.IsNullOrWhiteSpace(item.UseText) ? item.UseText : item.Description;
                    var tail = string.IsNullOrWhiteSpace(note) ? "" : ": " + note;
                    RollToChat?.Invoke($"{Name} uses {item.Name}{tail}", false);
                    DmLogPing?.Invoke($"{Name} used {item.Name}.");
                }

                await ConsumeOneAsync(item);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnUseRequested", ex); }
        }

        private async Task ConsumeOneAsync(InventoryItemViewModel item)
        {
            if (item.Quantity > 1)
            {
                item.Quantity -= 1;
                return;
            }

            item.DamageRolled -= OnDamageRolled;
            item.AttackRolled -= OnAttackRolled;
            item.CritRolled -= OnCritRolled;
            item.RemoveRequested -= OnRemoveRequested;
            item.ViewRequested -= OnViewRequested;
            item.EditRequested -= OnEditRequested;
            item.EquipToggled -= OnEquipToggled;
            item.OffHandToggled -= OnOffHandToggled;
            item.AttuneToggled -= OnAttuneToggled;
            item.UseRequested -= OnUseRequested;
            item.PropertyChanged -= OnInventoryItemPropertyChanged;
            Inventory.Remove(item);
            EquippedItems.Remove(item);
            RaiseAttunement();

            if (_instanceById.Remove(item.InstanceId))
                await RemoveAndBroadcastInstanceAsync(item.InstanceId);
        }

        private async void OnInventoryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (_suppressSave) return;
                if (sender is not InventoryItemViewModel item) return;
                if (e.PropertyName == nameof(InventoryItemViewModel.IsProficient))
                {
                    _character.WeaponProficiency[item.InstanceId] = item.IsProficient;
                    RefreshAttackEffect();
                    QueueSave();
                    return;
                }
                if (e.PropertyName != nameof(InventoryItemViewModel.Quantity)) return;
                if (!_instanceById.TryGetValue(item.InstanceId, out var inst)) return;
                inst.Quantity = item.Quantity;
                await PersistAndBroadcastInstanceAsync(inst);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnInventoryItemPropertyChanged", ex); }
        }

        private bool IsOwnedByMe =>
            string.Equals(_character.OwnerUserId, App.PM?.GetUID(), StringComparison.Ordinal);

        private async Task InitialInventoryLoadAsync()
        {
            await LoadInventoryAsync();
            if (!IsOwnedByMe) return;
            if (App.PM?.ComController?.IsConnected != true) return;
            foreach (var inst in _instanceById.Values.ToList())
                await App.PM.BroadcastInstanceAsync(inst);
        }

        private async Task PersistAndBroadcastInstanceAsync(ItemInstance inst)
        {
            if (App.PM?.GameDataRepo == null) return;
            await App.PM.GameDataRepo.SaveInstanceAsync(inst);
            await App.PM.BroadcastInstanceAsync(inst);
        }

        private async Task RemoveAndBroadcastInstanceAsync(string instanceId)
        {
            if (App.PM?.GameDataRepo == null) return;
            await App.PM.GameDataRepo.DeleteInstanceAsync(instanceId);
            await App.PM.BroadcastInstanceRemovedAsync(instanceId);
        }

        private static bool ReadEquipped(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(stateJson);
                return doc.RootElement.TryGetProperty("equipped", out var e) && e.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        private static string WriteEquipped(string? stateJson, bool equipped)
        {
            var map = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(stateJson))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(stateJson);
                    if (existing != null) foreach (var kv in existing) map[kv.Key] = kv.Value;
                }
                catch { }
            }
            map["equipped"] = equipped;
            return JsonSerializer.Serialize(map);
        }

        private static bool ReadOffHand(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(stateJson);
                return doc.RootElement.TryGetProperty("offhand", out var e) && e.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        private static string WriteOffHand(string? stateJson, bool offHand)
        {
            var map = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(stateJson))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(stateJson);
                    if (existing != null) foreach (var kv in existing) map[kv.Key] = kv.Value;
                }
                catch { }
            }
            map["offhand"] = offHand;
            return JsonSerializer.Serialize(map);
        }

        private static bool ReadAttuned(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(stateJson);
                return doc.RootElement.TryGetProperty("attuned", out var e) && e.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        private static string WriteAttuned(string? stateJson, bool attuned)
        {
            var map = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(stateJson))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(stateJson);
                    if (existing != null) foreach (var kv in existing) map[kv.Key] = kv.Value;
                }
                catch { }
            }
            map["attuned"] = attuned;
            return JsonSerializer.Serialize(map);
        }

        private void SyncLevelToXp()
        {
            if (UsesMilestone) return;
            var target = Level;
            while (target < MaxLevel && CurrentXp >= GetXpForLevel(target + 1)) target++;
            while (target > 1 && CurrentXp < GetXpForLevel(target)) target--;
            if (target != Level)
            {
                var gained = target - Level;
                Level = target;
                if (gained > 0)
                {
                    _ = GrantLevelUpHpAsync(_character.ClassId, gained);
                    LevelUpChoicesRequested?.Invoke(_character.ClassId, PrimaryClassLevelAfterGain(gained));
                }
            }
        }

        private int PrimaryClassLevelAfterGain(int gained)
        {
            var idx = _character.ClassLevels.FindIndex(x => string.Equals(x.ClassId, _character.ClassId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return Level;
            var lvl = _character.ClassLevels[idx].Level + gained;
            _character.ClassLevels[idx] = _character.ClassLevels[idx] with { Level = lvl };
            if (_character.ClassLevels.Count > 1 && App.PM != null)
                _ = App.PM.SaveClassLevelsAsync(_character.Id, _character.ClassLevels);
            return lvl;
        }

        private async Task GrantLevelUpHpAsync(string? classId, int levelsGained)
        {
            if (levelsGained <= 0) return;
            var rules = App.PM?.Rules ?? new GameRules();
            int die = App.PM != null ? await App.PM.ResolveHitDieForClassAsync(classId) : rules.DefaultHitDie;
            if (die <= 0) die = rules.DefaultHitDie;
            int hpScore = _character.AbilityScores.Get(rules.AbilityIdForShort(rules.HitPointAbility));
            int hpMod = App.PM?.AbilityMod(hpScore) ?? (int)Math.Floor((hpScore - 10) / 2.0);
            int gain = Math.Max(1, rules.HitDieHeal(die, rules.HpUsesAverage) + hpMod) * levelsGained;
            MaxHp += gain;
            CurrentHp += gain;
            _character.HitDiceRemaining = Math.Min(rules.MaxHitDiceForLevel(Level), _character.HitDiceRemaining + levelsGained);
            _character.HitDiceInitialized = true;
            this.RaisePropertyChanged(nameof(HitDiceLabel));
            QueueSave();
        }

        private static int GetXpForLevel(int level) => App.PM?.Rules.XpForLevel(level) ?? new GameRules().XpForLevel(level);
    }

    public class DeathPipViewModel : ReactiveObject
    {
        public int Index { get; }
        private bool _filled;
        public bool Filled { get => _filled; set => this.RaiseAndSetIfChanged(ref _filled, value); }
        public ReactiveCommand<Unit, Unit> ToggleCommand { get; }
        public DeathPipViewModel(int index, Action onToggle)
        {
            Index = index;
            ToggleCommand = ReactiveCommand.Create(onToggle);
        }
    }

    public record CastLevelOption(int Level, string Label, int Remaining);

    public class SheetSlotRow : ReactiveObject
    {
        public int Level { get; }

        private int _max;
        public int Max { get => _max; set { this.RaiseAndSetIfChanged(ref _max, value); RaiseDerived(); } }

        private int _used;
        public int Used { get => _used; set { this.RaiseAndSetIfChanged(ref _used, value); RaiseDerived(); } }

        public int Remaining => Max - Used;
        public bool CanSpend => Used < Max;
        public bool CanRestore => Used > 0;
        public string Label => App.PM?.Rules?.SpellLevelName(Level) ?? ("Lv " + Level);
        public string Tally => Remaining + " / " + Max;

        public ReactiveCommand<Unit, Unit> SpendCommand { get; }
        public ReactiveCommand<Unit, Unit> RestoreCommand { get; }

        public SheetSlotRow(int level, int max, int used, Action<SheetSlotRow> onSpend, Action<SheetSlotRow> onRestore)
        {
            Level = level;
            _max = max;
            _used = used;
            SpendCommand = ReactiveCommand.Create(() => { onSpend?.Invoke(this); });
            RestoreCommand = ReactiveCommand.Create(() => { onRestore?.Invoke(this); });
        }

        private void RaiseDerived()
        {
            this.RaisePropertyChanged(nameof(Remaining));
            this.RaisePropertyChanged(nameof(CanSpend));
            this.RaisePropertyChanged(nameof(CanRestore));
            this.RaisePropertyChanged(nameof(Tally));
        }
    }

    public class SheetResourceRow : ReactiveObject
    {
        public string Id { get; }
        public string Name { get; }
        public string ResetOn { get; }

        private int _max;
        public int Max { get => _max; set { this.RaiseAndSetIfChanged(ref _max, value); RaiseDerived(); } }

        private int _used;
        public int Used { get => _used; set { this.RaiseAndSetIfChanged(ref _used, value); RaiseDerived(); } }

        public int Remaining => Max - Used;
        public bool CanSpend => Used < Max;
        public bool CanRestore => Used > 0;
        public string Tally => Remaining + " / " + Max;
        public string ResetLabel => ResetOn == "short" ? "short rest" : "long rest";

        public bool HasInspire { get; set; }
        public ObservableCollection<CharacterRuntime> InspireTargets { get; } = new();
        private CharacterRuntime? _selectedInspireTarget;
        public CharacterRuntime? SelectedInspireTarget { get => _selectedInspireTarget; set => this.RaiseAndSetIfChanged(ref _selectedInspireTarget, value); }

        public ReactiveCommand<Unit, Unit> SpendCommand { get; }
        public ReactiveCommand<Unit, Unit> RestoreCommand { get; }

        public SheetResourceRow(string id, string name, string resetOn, int max, int used, Action<SheetResourceRow> onSpend, Action<SheetResourceRow> onRestore)
        {
            Id = id;
            Name = name;
            ResetOn = resetOn;
            _max = max;
            _used = used;
            SpendCommand = ReactiveCommand.Create(() => { onSpend?.Invoke(this); });
            RestoreCommand = ReactiveCommand.Create(() => { onRestore?.Invoke(this); });
        }

        private void RaiseDerived()
        {
            this.RaisePropertyChanged(nameof(Remaining));
            this.RaisePropertyChanged(nameof(CanSpend));
            this.RaisePropertyChanged(nameof(CanRestore));
            this.RaisePropertyChanged(nameof(Tally));
        }
    }

    public class SpellPrepEntry : ReactiveObject
    {
        private readonly Action<SpellPrepEntry>? _onToggled;
        public string SpellId { get; }
        public string Name { get; }
        public int Level { get; }
        public string School { get; }
        public string DataJson { get; }
        public bool CanToggle => true;
        public string LevelLabel => App.PM?.Rules?.SpellLevelName(Level) ?? (Level == 0 ? "Cantrip" : "Lv " + Level);
        public string SchoolLabel => string.IsNullOrWhiteSpace(School) ? "" : School;
        public ReactiveCommand<Unit, Unit> ViewCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

        private bool _isPrepared;
        public bool IsPrepared
        {
            get => _isPrepared;
            set { this.RaiseAndSetIfChanged(ref _isPrepared, value); _onToggled?.Invoke(this); }
        }

        public SpellPrepEntry(CastableSpell spell, bool isPrepared, Action<SpellPrepEntry>? onToggled, Action<SpellPrepEntry>? onView = null)
        {
            SpellId = spell.Id;
            Name = spell.Name;
            Level = spell.Level;
            School = spell.School;
            DataJson = spell.DataJson;
            _isPrepared = isPrepared;
            _onToggled = onToggled;
            ViewCommand = ReactiveCommand.Create(() => onView?.Invoke(this));
            ToggleCommand = ReactiveCommand.Create(() => { IsPrepared = !IsPrepared; });
        }
    }

    public class SpellLevelGroup
    {
        public string Header { get; }
        public ObservableCollection<SpellPrepEntry> Spells { get; } = new();
        public SpellLevelGroup(string header) { Header = header; }
    }

    public class FeatureEntry : ReactiveObject
    {
        public string Name { get; }
        public string Description { get; }
        public int Level { get; }
        public bool IsProse { get; }
        public string LevelLabel => Level > 0 ? $"Lv {Level}" : "";
        public string? TooltipText => string.IsNullOrWhiteSpace(Description) ? null : Description;
        public ReactiveCommand<Unit, Unit> OpenCommand { get; }

        public FeatureEntry(string name, string description, int level, Action<FeatureEntry> onClick, bool isProse = false)
        {
            Name = name;
            Description = description;
            Level = level;
            IsProse = isProse;
            OpenCommand = ReactiveCommand.Create(() => onClick?.Invoke(this));
        }
    }

    public class LevelSelectionEntry
    {
        public string Group { get; }
        public string Value { get; }

        public LevelSelectionEntry(string group, string value)
        {
            Group = group;
            Value = value;
        }
    }

    public class ConditionToggle : ReactiveObject
    {
        private readonly Action<ConditionToggle> _onToggled;
        public string Name { get; }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { this.RaiseAndSetIfChanged(ref _isActive, value); _onToggled?.Invoke(this); }
        }

        public ConditionToggle(string name, bool isActive, Action<ConditionToggle> onToggled)
        {
            Name = name;
            _isActive = isActive;
            _onToggled = onToggled;
        }
    }

    public class WalletEntry : ReactiveObject
    {
        public string CurrencyId { get; }
        public string Name { get; }
        public string Glyph { get; }
        public string ColorHex { get; }
        public IBrush Brush { get; }

        private long _amount;
        public long Amount { get => _amount; set => this.RaiseAndSetIfChanged(ref _amount, value); }

        public WalletEntry(Currency c, long amount)
        {
            CurrencyId = c.Id;
            Name = c.Name;
            Glyph = InventoryEngine.FallbackGlyph(c);
            ColorHex = string.IsNullOrWhiteSpace(c.Color) ? "#C0C0C0" : c.Color!;
            IBrush brush;
            try { brush = new SolidColorBrush(Color.Parse(ColorHex)); }
            catch { brush = new SolidColorBrush(Color.Parse("#C0C0C0")); }
            Brush = brush;
            _amount = amount;
        }
    }

    public class WalletAddRow : ReactiveObject
    {
        public string CurrencyId { get; }
        public string Name { get; }
        public string Glyph { get; }
        private decimal _amount;
        public decimal Amount { get => _amount; set => this.RaiseAndSetIfChanged(ref _amount, value); }
        public WalletAddRow(string currencyId, string name, string glyph) { CurrencyId = currencyId; Name = name; Glyph = glyph; }
    }

    public class SheetCatalogItem
    {
        public string Id { get; }
        public string Name { get; }
        public string ItemType { get; }
        public string DataJson { get; }

        public SheetCatalogItem(string id, string name, string itemType, string dataJson)
        {
            Id = id;
            Name = name;
            ItemType = itemType;
            DataJson = dataJson;
        }
    }
}
