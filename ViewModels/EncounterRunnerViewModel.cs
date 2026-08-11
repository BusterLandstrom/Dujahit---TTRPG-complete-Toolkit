using Dujahit.Models;
using Dujahit.Models.Database;
using Dujahit.Models.UI;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class EncounterRunnerViewModel : ViewModelBase
    {
        private readonly Dictionary<string, MonsterOption> _catalog = new(StringComparer.OrdinalIgnoreCase);

        public InitiativeTrackerViewModel Tracker { get; }
        public DmCombatPanelViewModel Combat { get; }

        public ObservableCollection<EncounterPresetRowViewModel> Presets { get; } = new();

        private EncounterPresetRowViewModel? _selectedPreset;
        public EncounterPresetRowViewModel? SelectedPreset
        {
            get => _selectedPreset;
            set => this.RaiseAndSetIfChanged(ref _selectedPreset, value);
        }

        public bool HasPresets => Presets.Count > 0;
        public bool IsEmpty => Tracker.Combatants.Count == 0;

        public ReactiveCommand<Unit, Unit> DropEncounterCommand { get; }
        public ReactiveCommand<Unit, Unit> RollInitiativeCommand { get; }
        public ReactiveCommand<CombatantViewModel, Unit> RemoveCombatantCommand { get; }

        public EncounterRunnerViewModel()
        {
            Tracker = new InitiativeTrackerViewModel { IsDm = true };
            Combat = new DmCombatPanelViewModel(Tracker);

            Tracker.Combatants.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(IsEmpty));

            DropEncounterCommand = ReactiveCommand.Create(DropEncounter);
            RollInitiativeCommand = ReactiveCommand.Create(RollInitiative);
            RemoveCombatantCommand = ReactiveCommand.Create<CombatantViewModel>(RemoveCombatant);
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;

            _catalog.Clear();
            var monsters = await App.PM.MonsterReader.ReadAsync(App.PM.GetActiveTemplateId());
            foreach (var m in monsters) _catalog[m.Id] = m;

            await LoadPresetsAsync();
        }

        private async Task LoadPresetsAsync()
        {
            if (App.PM == null) return;
            Presets.Clear();
            var saved = await App.PM.LoadEncounterPresetsAsync();
            foreach (var p in saved) Presets.Add(new EncounterPresetRowViewModel(p));
            if (SelectedPreset == null) SelectedPreset = Presets.FirstOrDefault();
            this.RaisePropertyChanged(nameof(HasPresets));
        }

        private void DropEncounter()
        {
            var preset = SelectedPreset?.Preset;
            if (preset == null) return;

            foreach (var entry in preset.Monsters)
            {
                _catalog.TryGetValue(entry.MonsterId, out var mon);
                var copies = Math.Max(1, entry.Count);
                for (var i = 0; i < copies; i++)
                {
                    var baseName = mon?.Name ?? entry.Name;
                    var dex = mon?.DexMod ?? 0;
                    var hp = mon != null ? mon.HitPoints : 1;
                    var ac = mon != null ? mon.ArmorClass : 10;

                    var c = new CombatantViewModel(Guid.NewGuid().ToString("N"), UniqueName(baseName), isPlayerCharacter: false)
                    {
                        Initiative = DiceManager.RollInitiativeDie() + dex,
                        MaxHp = hp,
                        CurrentHp = hp,
                        ArmorClass = ac,
                        DexMod = dex,
                        RevealExactHpToPlayers = false
                    };
                    if (mon != null)
                        foreach (var a in mon.Attacks)
                            c.Attacks.Add(new CombatantAttackViewModel(a.Name, a.ToHit, a.Damage, a.DamageType, a.RangeFeet, "", 0, a.AreaShape, a.AreaSizeFt, a.AreaWidthFt, a.SaveAbility, a.SaveDc));

                    Tracker.Combatants.Add(c);
                }
            }
            Tracker.NotifyStateChanged();
        }

        private void RollInitiative()
        {
            if (Tracker.Combatants.Count == 0) return;
            foreach (var c in Tracker.Combatants)
                c.Initiative = DiceManager.RollInitiativeDie() + c.DexMod;

            var sorted = Tracker.Combatants.OrderByDescending(c => c.Initiative).ToList();
            Tracker.Combatants.Clear();
            foreach (var c in sorted) Tracker.Combatants.Add(c);

            if (Tracker.CombatActive) Tracker.ActiveCombatant = Tracker.Combatants.FirstOrDefault();
            Tracker.NotifyStateChanged();
        }

        private void RemoveCombatant(CombatantViewModel combatant)
        {
            if (combatant == null) return;
            var wasActive = Tracker.ActiveCombatant == combatant;
            Tracker.Combatants.Remove(combatant);
            if (wasActive) Tracker.ActiveCombatant = Tracker.Combatants.FirstOrDefault();
            Tracker.NotifyStateChanged();
        }

        private string UniqueName(string baseName)
        {
            if (Tracker.Combatants.All(c => c.Name != baseName)) return baseName;
            for (var n = 2; ; n++)
            {
                var candidate = baseName + " " + n;
                if (Tracker.Combatants.All(c => c.Name != candidate)) return candidate;
            }
        }
    }
}
