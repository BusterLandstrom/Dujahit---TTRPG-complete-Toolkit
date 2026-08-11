using Avalonia.Media;
using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class MonsterCatalogRowViewModel : ViewModelBase
    {
        public string Id { get; }
        public string Name { get; }
        public string Cr { get; }
        public int Xp { get; }
        public string Size { get; }
        public List<MonsterAttackOption> Attacks { get; } = new();
        public string CrLabel => string.IsNullOrWhiteSpace(Cr) ? "CR ?" : "CR " + Cr;
        public string XpLabel => Xp + " XP";

        public MonsterCatalogRowViewModel(MonsterOption m)
        {
            Id = m.Id;
            Name = m.Name;
            Cr = m.ChallengeRating;
            Xp = EncounterMath.CrToXp(m.ChallengeRating);
            Size = m.Size;
            Attacks = m.Attacks;
        }

        public MonsterCatalogRowViewModel(string id, string name, string cr, int xp, string size)
        {
            Id = id;
            Name = name;
            Cr = cr;
            Xp = xp;
            Size = size;
        }
    }

    public class EncounterLineViewModel : ViewModelBase
    {
        private readonly Action? _onChanged;
        public string MonsterId { get; }
        public string Name { get; }
        public string Cr { get; }
        public int Xp { get; }

        private int _count = 1;
        public int Count
        {
            get => _count;
            set
            {
                this.RaiseAndSetIfChanged(ref _count, value < 1 ? 1 : value);
                this.RaisePropertyChanged(nameof(LineXp));
                this.RaisePropertyChanged(nameof(LineLabel));
                _onChanged?.Invoke();
            }
        }

        public int LineXp => Xp * Count;
        public string CrLabel => string.IsNullOrWhiteSpace(Cr) ? "CR ?" : "CR " + Cr;
        public string LineLabel => Xp + " XP each, " + LineXp + " total";

        public ObservableCollection<CombatantAttackViewModel> Attacks { get; } = new();

        private bool _isEditingAttacks;
        public bool IsEditingAttacks
        {
            get => _isEditingAttacks;
            set => this.RaiseAndSetIfChanged(ref _isEditingAttacks, value);
        }

        public string AttacksSummary => Attacks.Count == 0 ? "No attacks" : string.Join(", ", Attacks.Select(a => a.Name));

        private string _newAttackName = "";
        public string NewAttackName { get => _newAttackName; set => this.RaiseAndSetIfChanged(ref _newAttackName, value); }
        private int _newAttackToHit;
        public int NewAttackToHit { get => _newAttackToHit; set => this.RaiseAndSetIfChanged(ref _newAttackToHit, value); }
        private string _newAttackDamage = "";
        public string NewAttackDamage { get => _newAttackDamage; set => this.RaiseAndSetIfChanged(ref _newAttackDamage, value); }
        private string _newAttackDamageType = "";
        public string NewAttackDamageType { get => _newAttackDamageType; set => this.RaiseAndSetIfChanged(ref _newAttackDamageType, value); }
        private int _newAttackRange;
        public int NewAttackRange { get => _newAttackRange; set => this.RaiseAndSetIfChanged(ref _newAttackRange, value); }

        public ReactiveCommand<Unit, Unit> ToggleAttacksCommand { get; }
        public ReactiveCommand<Unit, Unit> AddAttackCommand { get; }
        public ReactiveCommand<CombatantAttackViewModel, Unit> RemoveAttackCommand { get; }

        public EncounterLineViewModel(string monsterId, string name, string cr, int xp, int count, Action? onChanged, IEnumerable<MonsterAttackOption>? attacks = null)
        {
            MonsterId = monsterId;
            Name = name;
            Cr = cr;
            Xp = xp;
            _count = count < 1 ? 1 : count;
            _onChanged = onChanged;

            if (attacks != null)
                foreach (var a in attacks) Attacks.Add(new CombatantAttackViewModel(a.Name, a.ToHit, a.Damage, a.DamageType, a.RangeFeet, "", 0, a.AreaShape, a.AreaSizeFt, a.AreaWidthFt, a.SaveAbility, a.SaveDc));

            ToggleAttacksCommand = ReactiveCommand.Create(() => { IsEditingAttacks = !IsEditingAttacks; });
            AddAttackCommand = ReactiveCommand.Create(() =>
            {
                if (string.IsNullOrWhiteSpace(NewAttackName)) return;
                var dmg = string.IsNullOrWhiteSpace(NewAttackDamage) ? "0" : NewAttackDamage.Trim();
                var type = string.IsNullOrWhiteSpace(NewAttackDamageType) ? "" : NewAttackDamageType.Trim();
                Attacks.Add(new CombatantAttackViewModel(NewAttackName.Trim(), NewAttackToHit, dmg, type, NewAttackRange < 0 ? 0 : NewAttackRange));
                NewAttackName = "";
                NewAttackToHit = 0;
                NewAttackDamage = "";
                NewAttackDamageType = "";
                NewAttackRange = 0;
                this.RaisePropertyChanged(nameof(AttacksSummary));
            });
            RemoveAttackCommand = ReactiveCommand.Create<CombatantAttackViewModel>(atk =>
            {
                if (atk == null) return;
                Attacks.Remove(atk);
                this.RaisePropertyChanged(nameof(AttacksSummary));
            });
        }

        public List<MonsterAttackOption> ToAttackOptions() =>
            Attacks.Select(a => new MonsterAttackOption(a.Name, a.ToHit, a.Damage, a.DamageType, a.RangeFeet)).ToList();
    }

    public class PartyMemberRowViewModel : ViewModelBase
    {
        private readonly Action? _onChanged;
        public string Id { get; }
        public string Name { get; }
        public int RealLevel { get; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { this.RaiseAndSetIfChanged(ref _isSelected, value); _onChanged?.Invoke(); }
        }

        private int _level;
        public int Level
        {
            get => _level;
            set
            {
                this.RaiseAndSetIfChanged(ref _level, Math.Clamp(value, 1, 20));
                this.RaisePropertyChanged(nameof(LevelLabel));
                _onChanged?.Invoke();
            }
        }

        public string LevelLabel => "Lv " + Level;

        public PartyMemberRowViewModel(CharacterListEntry e, Action? onChanged)
        {
            Id = e.Id;
            Name = e.Name;
            RealLevel = e.Level < 1 ? 1 : e.Level;
            _level = RealLevel;
            _onChanged = onChanged;
        }
    }

    public class EncounterPresetRowViewModel : ViewModelBase
    {
        public EncounterPreset Preset { get; }
        public string Name => Preset.Name;
        public string Summary { get; }

        public EncounterPresetRowViewModel(EncounterPreset preset)
        {
            Preset = preset;
            var creatures = preset.Monsters.Sum(m => m.Count);
            var xp = preset.Monsters.Sum(m => m.Xp * m.Count);
            Summary = creatures + (creatures == 1 ? " creature, " : " creatures, ") + xp + " XP";
        }
    }

    public class EncounterBuilderViewModel : ViewModelBase
    {
        private readonly List<MonsterCatalogRowViewModel> _allMonsters = new();
        private bool _loading;
        private string? _editingPresetId;
        private readonly Random _rng = new();

        public ObservableCollection<MonsterCatalogRowViewModel> VisibleMonsters { get; } = new();
        public ObservableCollection<EncounterLineViewModel> Lines { get; } = new();
        public ObservableCollection<PartyMemberRowViewModel> Party { get; } = new();
        public ObservableCollection<EncounterPresetRowViewModel> Presets { get; } = new();

        public ObservableCollection<string> DifficultyTargets { get; } = new() { "Low", "Moderate", "High" };

        private string _selectedTarget = "Moderate";
        public string SelectedTarget
        {
            get => _selectedTarget;
            set => this.RaiseAndSetIfChanged(ref _selectedTarget, value);
        }

        private string _monsterSearch = "";
        public string MonsterSearch
        {
            get => _monsterSearch;
            set { this.RaiseAndSetIfChanged(ref _monsterSearch, value); ApplyFilter(); }
        }

        private string _encounterName = "";
        public string EncounterName
        {
            get => _encounterName;
            set => this.RaiseAndSetIfChanged(ref _encounterName, value);
        }

        private string _encounterNotes = "";
        public string EncounterNotes
        {
            get => _encounterNotes;
            set => this.RaiseAndSetIfChanged(ref _encounterNotes, value);
        }

        private int _totalXp;
        public int TotalXp
        {
            get => _totalXp;
            private set { this.RaiseAndSetIfChanged(ref _totalXp, value); this.RaisePropertyChanged(nameof(TotalXpLabel)); }
        }

        private int _budgetLow;
        private int _budgetModerate;
        private int _budgetHigh;

        public string TotalXpLabel => TotalXp + " XP";
        public string MonsterCountLabel
        {
            get
            {
                var n = Lines.Sum(l => l.Count);
                return n + (n == 1 ? " creature" : " creatures");
            }
        }

        public string BudgetLine => "Low " + _budgetLow + "    Moderate " + _budgetModerate + "    High " + _budgetHigh;

        public string PartySummary
        {
            get
            {
                var picked = Party.Count(p => p.IsSelected);
                return picked + (picked == 1 ? " hero in the fight" : " heroes in the fight");
            }
        }

        private string _difficultyLabel = "No monsters";
        public string DifficultyLabel
        {
            get => _difficultyLabel;
            private set => this.RaiseAndSetIfChanged(ref _difficultyLabel, value);
        }

        private IBrush _difficultyBrush = BrushFromHex("#3A3A4D");
        public IBrush DifficultyBrush
        {
            get => _difficultyBrush;
            private set => this.RaiseAndSetIfChanged(ref _difficultyBrush, value);
        }

        public bool HasLines => Lines.Count > 0;
        public bool HasPresets => Presets.Count > 0;

        public ReactiveCommand<MonsterCatalogRowViewModel, Unit> AddMonsterCommand { get; }
        public ReactiveCommand<EncounterLineViewModel, Unit> IncrementLineCommand { get; }
        public ReactiveCommand<EncounterLineViewModel, Unit> DecrementLineCommand { get; }
        public ReactiveCommand<EncounterLineViewModel, Unit> RemoveLineCommand { get; }
        public ReactiveCommand<PartyMemberRowViewModel, Unit> IncrementPartyLevelCommand { get; }
        public ReactiveCommand<PartyMemberRowViewModel, Unit> DecrementPartyLevelCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectAllPartyCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearPartyCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearMonstersCommand { get; }
        public ReactiveCommand<Unit, Unit> RandomizeCommand { get; }
        public ReactiveCommand<Unit, Unit> NewEncounterCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveEncounterCommand { get; }
        public ReactiveCommand<EncounterPresetRowViewModel, Unit> LoadPresetCommand { get; }
        public ReactiveCommand<EncounterPresetRowViewModel, Unit> DeletePresetCommand { get; }

        public EncounterBuilderViewModel()
        {
            AddMonsterCommand = ReactiveCommand.Create<MonsterCatalogRowViewModel>(AddMonster);
            IncrementLineCommand = ReactiveCommand.Create<EncounterLineViewModel>(l => { if (l != null) l.Count++; });
            DecrementLineCommand = ReactiveCommand.Create<EncounterLineViewModel>(DecrementLine);
            RemoveLineCommand = ReactiveCommand.Create<EncounterLineViewModel>(RemoveLine);
            IncrementPartyLevelCommand = ReactiveCommand.Create<PartyMemberRowViewModel>(p => { if (p != null) p.Level++; });
            DecrementPartyLevelCommand = ReactiveCommand.Create<PartyMemberRowViewModel>(p => { if (p != null) p.Level--; });
            SelectAllPartyCommand = ReactiveCommand.Create(() => SetAllPartySelected(true));
            ClearPartyCommand = ReactiveCommand.Create(() => SetAllPartySelected(false));
            ClearMonstersCommand = ReactiveCommand.Create(ClearMonsters);
            RandomizeCommand = ReactiveCommand.Create(Randomize);
            NewEncounterCommand = ReactiveCommand.Create(NewEncounter);
            SaveEncounterCommand = ReactiveCommand.CreateFromTask(SaveEncounterAsync);
            LoadPresetCommand = ReactiveCommand.Create<EncounterPresetRowViewModel>(LoadPreset);
            DeletePresetCommand = ReactiveCommand.CreateFromTask<EncounterPresetRowViewModel>(DeletePresetAsync);
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;
            _loading = true;
            try
            {
                _allMonsters.Clear();
                var monsters = await App.PM.MonsterReader.ReadAsync(App.PM.GetActiveTemplateId());
                foreach (var m in monsters.OrderBy(m => EncounterMath.CrToXp(m.ChallengeRating)).ThenBy(m => m.Name))
                    _allMonsters.Add(new MonsterCatalogRowViewModel(m));
                ApplyFilter();

                Party.Clear();
                var pcs = await App.PM.GetCampaignCharactersAsync();
                foreach (var pc in pcs.OrderBy(p => p.Name))
                    Party.Add(new PartyMemberRowViewModel(pc, Recompute));

                await LoadPresetsAsync();
            }
            finally
            {
                _loading = false;
            }
            Recompute();
        }

        private async Task LoadPresetsAsync()
        {
            if (App.PM == null) return;
            Presets.Clear();
            var saved = await App.PM.LoadEncounterPresetsAsync();
            foreach (var p in saved) Presets.Add(new EncounterPresetRowViewModel(p));
            this.RaisePropertyChanged(nameof(HasPresets));
        }

        private void ApplyFilter()
        {
            VisibleMonsters.Clear();
            var q = _monsterSearch?.Trim() ?? "";
            IEnumerable<MonsterCatalogRowViewModel> src = _allMonsters;
            if (q.Length > 0)
                src = _allMonsters.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                              || ("CR " + m.Cr).Contains(q, StringComparison.OrdinalIgnoreCase));
            foreach (var m in src) VisibleMonsters.Add(m);
        }

        private List<MonsterAttackOption> CatalogAttacks(string monsterId) =>
            _allMonsters.FirstOrDefault(r => r.Id == monsterId)?.Attacks ?? new List<MonsterAttackOption>();

        private void AddMonster(MonsterCatalogRowViewModel row)
        {
            if (row == null) return;
            var existing = Lines.FirstOrDefault(l => l.MonsterId == row.Id);
            if (existing != null) { existing.Count++; return; }
            Lines.Add(new EncounterLineViewModel(row.Id, row.Name, row.Cr, row.Xp, 1, Recompute, row.Attacks));
            Recompute();
        }

        private void DecrementLine(EncounterLineViewModel line)
        {
            if (line == null) return;
            if (line.Count <= 1) { RemoveLine(line); return; }
            line.Count--;
        }

        private void RemoveLine(EncounterLineViewModel line)
        {
            if (line == null) return;
            Lines.Remove(line);
            Recompute();
        }

        private void ClearMonsters()
        {
            Lines.Clear();
            Recompute();
        }

        private void SetAllPartySelected(bool selected)
        {
            _loading = true;
            try { foreach (var p in Party) p.IsSelected = selected; }
            finally { _loading = false; }
            Recompute();
        }

        private void Recompute()
        {
            if (_loading) return;
            TotalXp = Lines.Sum(l => l.LineXp);

            var levels = Party.Where(p => p.IsSelected).Select(p => p.Level).ToList();
            var budget = EncounterMath.PartyBudget(levels);
            _budgetLow = budget.Low;
            _budgetModerate = budget.Moderate;
            _budgetHigh = budget.High;

            var band = EncounterMath.Classify(TotalXp, budget);
            DifficultyLabel = EncounterMath.Label(band);
            DifficultyBrush = BrushFromHex(EncounterMath.ColorHex(band));

            this.RaisePropertyChanged(nameof(MonsterCountLabel));
            this.RaisePropertyChanged(nameof(BudgetLine));
            this.RaisePropertyChanged(nameof(PartySummary));
            this.RaisePropertyChanged(nameof(HasLines));
        }

        private void Randomize()
        {
            var levels = Party.Where(p => p.IsSelected).Select(p => p.Level).ToList();
            if (levels.Count == 0) return;

            var budget = EncounterMath.PartyBudget(levels);
            var target = _selectedTarget switch
            {
                "Low" => budget.Low,
                "High" => budget.High,
                _ => budget.Moderate
            };
            if (target <= 0) return;

            var pool = _allMonsters.Where(m => m.Xp > 0).ToList();
            if (pool.Count == 0) return;

            var chosen = new Dictionary<string, (MonsterCatalogRowViewModel Row, int Count)>();
            int spent = 0;
            int totalCreatures = 0;
            int guard = 0;

            while (spent < target * 0.9 && guard < 300)
            {
                guard++;
                var remaining = target - spent;
                var fits = pool.Where(m => m.Xp <= remaining * 1.2).ToList();
                if (fits.Count == 0)
                {
                    if (chosen.Count == 0)
                    {
                        var cheapest = pool.OrderBy(m => m.Xp).First();
                        chosen[cheapest.Id] = (cheapest, 1);
                    }
                    break;
                }

                var pick = fits[_rng.Next(fits.Count)];
                if (chosen.TryGetValue(pick.Id, out var cur)) chosen[pick.Id] = (pick, cur.Count + 1);
                else chosen[pick.Id] = (pick, 1);
                spent += pick.Xp;
                totalCreatures++;
                if (totalCreatures >= 14) break;
            }

            _loading = true;
            try
            {
                Lines.Clear();
                foreach (var kv in chosen.Values.OrderByDescending(v => v.Row.Xp))
                    Lines.Add(new EncounterLineViewModel(kv.Row.Id, kv.Row.Name, kv.Row.Cr, kv.Row.Xp, kv.Count, Recompute, kv.Row.Attacks));
            }
            finally { _loading = false; }
            Recompute();
        }

        private void NewEncounter()
        {
            _editingPresetId = null;
            EncounterName = "";
            EncounterNotes = "";
            ClearMonsters();
        }

        private void LoadPreset(EncounterPresetRowViewModel row)
        {
            if (row == null) return;
            _editingPresetId = row.Preset.Id;
            EncounterName = row.Preset.Name;
            EncounterNotes = row.Preset.Notes ?? "";

            _loading = true;
            try
            {
                Lines.Clear();
                foreach (var m in row.Preset.Monsters)
                {
                    var seed = m.Attacks != null && m.Attacks.Count > 0 ? m.Attacks : CatalogAttacks(m.MonsterId);
                    Lines.Add(new EncounterLineViewModel(m.MonsterId, m.Name, m.Cr, m.Xp, m.Count, Recompute, seed));
                }
            }
            finally { _loading = false; }
            Recompute();
        }

        private async Task SaveEncounterAsync()
        {
            if (App.PM == null) return;
            if (Lines.Count == 0) return;

            var name = string.IsNullOrWhiteSpace(EncounterName) ? "Untitled Encounter" : EncounterName.Trim();
            var preset = new EncounterPreset
            {
                Id = _editingPresetId ?? Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                Name = name,
                Notes = string.IsNullOrWhiteSpace(EncounterNotes) ? null : EncounterNotes.Trim(),
                Monsters = Lines.Select(l => new EncounterPresetEntry
                {
                    MonsterId = l.MonsterId,
                    Name = l.Name,
                    Cr = l.Cr,
                    Xp = l.Xp,
                    Count = l.Count,
                    Attacks = l.ToAttackOptions()
                }).ToList()
            };

            await App.PM.SaveEncounterPresetAsync(preset);
            _editingPresetId = preset.Id;
            EncounterName = name;
            await LoadPresetsAsync();
        }

        private async Task DeletePresetAsync(EncounterPresetRowViewModel row)
        {
            if (App.PM == null || row == null) return;
            await App.PM.DeleteEncounterPresetAsync(row.Preset.Id);
            if (_editingPresetId == row.Preset.Id) _editingPresetId = null;
            await LoadPresetsAsync();
        }

        private static IBrush BrushFromHex(string hex)
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return new SolidColorBrush(Color.Parse("#3A3A4D")); }
        }
    }
}
