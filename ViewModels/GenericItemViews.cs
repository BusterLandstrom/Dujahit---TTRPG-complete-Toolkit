using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class CurrentCharacterService : ViewModelBase
    {
        private CharacterRuntime? _current;
        public CharacterRuntime? Current
        {
            get => _current;
            set => this.RaiseAndSetIfChanged(ref _current, value);
        }

        public void Load(CharacterRuntime character) => Current = character;
        public void Clear() => Current = null;
    }
    public class InventoryItemViewModel : ViewModelBase
    {
        public string InstanceId { get; }
        public string BaseItemId { get; }
        public string Name { get; }
        public string Description { get; }
        public ItemKind Kind { get; }
        public string RawDataJson { get; }

        public List<WeaponDamage> DamageValues { get; } = new();
        public bool IsRanged { get; }
        public int RangeNormal { get; }
        public int RangeMax { get; }
        public List<string> WeaponCategory { get; } = new();
        public string Mastery { get; } = "";

        public int BaseAC { get; }
        public bool AllowsDexBonus { get; }
        public int MaxDexBonus { get; }
        public string ArmorType { get; } = "";
        public List<string> GrantedSenses { get; } = new();

        public int HitBonus { get; private set; }
        public int DamageBonus { get; private set; }
        public int AcBonus { get; private set; }
        public bool IsMagic { get; private set; }
        public bool RequiresAttunement { get; private set; }
        public string MagicTagDisplay => IsMagic ? (Kind == ItemKind.Armor ? $"+{AcBonus} AC" : $"+{HitBonus} hit") : "";

        private int _quantity = 1;
        public int Quantity
        {
            get => _quantity;
            set => this.RaiseAndSetIfChanged(ref _quantity, value);
        }

        private bool _isProficient = true;
        public bool IsProficient
        {
            get => _isProficient;
            set => this.RaiseAndSetIfChanged(ref _isProficient, value);
        }

        private bool _isEquipped;
        public bool IsEquipped
        {
            get => _isEquipped;
            set { this.RaiseAndSetIfChanged(ref _isEquipped, value); this.RaisePropertyChanged(nameof(EquipLabel)); }
        }
        public string EquipLabel => IsEquipped ? "Unequip" : "Equip";

        private bool _isOffHand;
        public bool IsOffHand
        {
            get => _isOffHand;
            set { this.RaiseAndSetIfChanged(ref _isOffHand, value); this.RaisePropertyChanged(nameof(OffHandLabel)); }
        }
        public string OffHandLabel => IsOffHand ? "Main hand" : "Off hand";

        private bool _isAttuned;
        public bool IsAttuned
        {
            get => _isAttuned;
            set { this.RaiseAndSetIfChanged(ref _isAttuned, value); this.RaisePropertyChanged(nameof(AttuneLabel)); }
        }
        public string AttuneLabel => IsAttuned ? "Unattune" : "Attune";

        public bool IsWeapon => Kind == ItemKind.Weapon;
        public bool IsArmor => Kind == ItemKind.Armor;
        public bool IsConsumable => Kind == ItemKind.Consumable;
        public bool IsEquippable => Kind == ItemKind.Weapon || Kind == ItemKind.Armor;

        public string UseText { get; private set; } = "";
        public bool HasHealing { get; private set; }
        public string HealDiceId { get; private set; } = "";
        public int HealDiceCount { get; private set; }
        public int HealFlat { get; private set; }
        public string DamageSummary
        {
            get
            {
                var parts = new List<string>();
                foreach (var d in DamageValues)
                {
                    var count = d.Count > 0 ? d.Count : 1;
                    var term = $"{count}{d.DiceId}";
                    if (d.Flat > 0) term += $"+{d.Flat}";
                    else if (d.Flat < 0) term += d.Flat.ToString();
                    parts.Add($"{term} {(App.PM?.Rules ?? new GameRules()).DamageTypeLabel(d.TypeId)}");
                }
                return string.Join(" + ", parts);
            }
        }
        public string RangeSummary => IsRanged ? $"{RangeNormal}/{RangeMax} ft." : "Melee";

        public ReactiveCommand<Unit, Unit> RollDamageCommand { get; }
        public ReactiveCommand<Unit, Unit> AttackCommand { get; }
        public ReactiveCommand<Unit, Unit> RollCritCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewCommand { get; }
        public ReactiveCommand<Unit, Unit> EditCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleEquipCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleOffHandCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleAttuneCommand { get; }
        public ReactiveCommand<Unit, Unit> UseCommand { get; }

        public event Action<InventoryItemViewModel> DamageRolled;
        public event Action<InventoryItemViewModel> AttackRolled;
        public event Action<InventoryItemViewModel> CritRolled;
        public event Action<InventoryItemViewModel> RemoveRequested;
        public event Action<InventoryItemViewModel> ViewRequested;
        public event Action<InventoryItemViewModel> EditRequested;
        public event Action<InventoryItemViewModel> EquipToggled;
        public event Action<InventoryItemViewModel> OffHandToggled;
        public event Action<InventoryItemViewModel> AttuneToggled;
        public event Action<InventoryItemViewModel> UseRequested;

        public int MaxCharges { get; private set; }
        public string RechargeOn { get; private set; } = "";
        public string AmmoItemId { get; private set; } = "";

        private int _charges;
        public int Charges
        {
            get => _charges;
            set
            {
                this.RaiseAndSetIfChanged(ref _charges, Math.Max(0, value));
                this.RaisePropertyChanged(nameof(ChargesLabel));
                this.RaisePropertyChanged(nameof(CanSpendCharge));
            }
        }

        public bool HasCharges => MaxCharges > 0;
        public bool CanSpendCharge => MaxCharges > 0 && _charges > 0;
        public string ChargesLabel => MaxCharges > 0 ? _charges + " / " + MaxCharges + " charges" : "";
        public bool NeedsAmmo => !string.IsNullOrWhiteSpace(AmmoItemId);

        public bool SpendCharge()
        {
            if (!CanSpendCharge) return false;
            Charges--;
            return true;
        }

        public void RechargeFor(string rest)
        {
            if (MaxCharges <= 0) return;
            if (!string.Equals(RechargeOn, rest, StringComparison.OrdinalIgnoreCase)) return;
            Charges = MaxCharges;
        }

        public InventoryItemViewModel(string instanceId, string baseItemId, string name, string dataJson, int quantity = 1, string? stateJson = null)
        {
            InstanceId = instanceId;
            BaseItemId = baseItemId;
            Name = name;
            Quantity = quantity;
            RawDataJson = dataJson;

            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("Description", out var desc))
                    Description = desc.GetString() ?? "";

                var type = root.TryGetProperty("$type", out var t) ? t.GetString() : null;
                Kind = type switch
                {
                    "Weapon" => ItemKind.Weapon,
                    "Armor" => ItemKind.Armor,
                    "Consumable" => ItemKind.Consumable,
                    _ => ItemKind.Generic
                };

                if (Kind == ItemKind.Weapon)
                {
                    if (root.TryGetProperty("DamageValues", out var dv) && dv.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in dv.EnumerateArray())
                        {
                            var count = entry.TryGetProperty("Count", out var cnt) && cnt.TryGetInt32(out var cv) ? cv : 1;
                            var flat = entry.TryGetProperty("Flat", out var fl) && fl.TryGetInt32(out var fv) ? fv : 0;
                            DamageValues.Add(new WeaponDamage(
                                entry.GetProperty("TypeId").GetString() ?? "",
                                entry.GetProperty("DiceId").GetString() ?? "",
                                count, flat));
                        }
                    }
                    if (root.TryGetProperty("IsRanged", out var ir)) IsRanged = ir.GetBoolean();
                    if (root.TryGetProperty("RangeNormal", out var rn)) RangeNormal = rn.GetInt32();
                    if (root.TryGetProperty("RangeMax", out var rm)) RangeMax = rm.GetInt32();
                    if (root.TryGetProperty("WeaponCategory", out var wc) && wc.ValueKind == JsonValueKind.Array)
                        foreach (var cat in wc.EnumerateArray()) WeaponCategory.Add(cat.GetString() ?? "");
                    if (root.TryGetProperty("Mastery", out var mst)) Mastery = mst.GetString() ?? "";
                }
                else if (Kind == ItemKind.Armor)
                {
                    if (root.TryGetProperty("BaseAC", out var ba)) BaseAC = ba.GetInt32();
                    if (root.TryGetProperty("AllowsDexBonus", out var adb)) AllowsDexBonus = adb.GetBoolean();
                    if (root.TryGetProperty("MaxDexBonus", out var mdb)) MaxDexBonus = mdb.GetInt32();
                    if (root.TryGetProperty("ArmorType", out var at)) ArmorType = at.GetString() ?? "";
                }
                else if (Kind == ItemKind.Consumable)
                {
                    if (root.TryGetProperty("UseText", out var ut)) UseText = ut.GetString() ?? "";
                    ParseHealing(root);
                }

                if (root.TryGetProperty("HitBonus", out var hb)) HitBonus = hb.GetInt32();
                if (root.TryGetProperty("DamageBonus", out var db)) DamageBonus = db.GetInt32();
                if (root.TryGetProperty("AcBonus", out var ab)) AcBonus = ab.GetInt32();
                // The named magic items keep their numbers under MechanicalProperties, only the specific weapons put them at the root
                if (root.TryGetProperty("MechanicalProperties", out var mp) && mp.ValueKind == JsonValueKind.Object)
                {
                    if (mp.TryGetProperty("AttackBonus", out var mab) && mab.ValueKind == JsonValueKind.Number) HitBonus = mab.GetInt32();
                    if (mp.TryGetProperty("DamageBonus", out var mdb) && mdb.ValueKind == JsonValueKind.Number) DamageBonus = mdb.GetInt32();
                    if (mp.TryGetProperty("ACBonus", out var macb) && macb.ValueKind == JsonValueKind.Number) AcBonus = macb.GetInt32();
                }
                if (root.TryGetProperty("IsMagic", out var im)) IsMagic = im.GetBoolean();
                if (root.TryGetProperty("Attunement", out var attn) && attn.ValueKind == JsonValueKind.String)
                    RequiresAttunement = string.Equals(attn.GetString(), App.PM?.Rules?.AttunementFlagValue ?? "Attunement", StringComparison.OrdinalIgnoreCase);
                if (root.TryGetProperty("Senses", out var sn) && sn.ValueKind == JsonValueKind.Array)
                    foreach (var x in sn.EnumerateArray()) GrantedSenses.Add(x.GetString() ?? "");

                if (root.TryGetProperty("MaxCharges", out var mc) && mc.ValueKind == JsonValueKind.Number) MaxCharges = mc.GetInt32();
                if (root.TryGetProperty("RechargeOn", out var ro) && ro.ValueKind == JsonValueKind.String) RechargeOn = ro.GetString() ?? "";
                if (root.TryGetProperty("AmmoItemId", out var ai) && ai.ValueKind == JsonValueKind.String) AmmoItemId = ai.GetString() ?? "";
                if (root.TryGetProperty("MechanicalProperties", out var mp2) && mp2.ValueKind == JsonValueKind.Object)
                {
                    if (mp2.TryGetProperty("MaxCharges", out var mmc) && mmc.ValueKind == JsonValueKind.Number) MaxCharges = mmc.GetInt32();
                    if (mp2.TryGetProperty("RechargeOn", out var mro) && mro.ValueKind == JsonValueKind.String) RechargeOn = mro.GetString() ?? "";
                    if (mp2.TryGetProperty("AmmoItemId", out var mai) && mai.ValueKind == JsonValueKind.String) AmmoItemId = mai.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[InventoryItem] Failed to parse {name}", ex);
            }

            _charges = MaxCharges;
            if (!string.IsNullOrWhiteSpace(stateJson))
            {
                try
                {
                    using var st = JsonDocument.Parse(stateJson!);
                    if (st.RootElement.TryGetProperty("Charges", out var ch) && ch.ValueKind == JsonValueKind.Number) _charges = ch.GetInt32();
                }
                catch (JsonException) { }
            }

            RollDamageCommand = ReactiveCommand.Create(() => DamageRolled?.Invoke(this));
            AttackCommand = ReactiveCommand.Create(() => AttackRolled?.Invoke(this));
            RollCritCommand = ReactiveCommand.Create(() => CritRolled?.Invoke(this));
            RemoveCommand = ReactiveCommand.Create(() => RemoveRequested?.Invoke(this));
            ViewCommand = ReactiveCommand.Create(() => ViewRequested?.Invoke(this));
            EditCommand = ReactiveCommand.Create(() => EditRequested?.Invoke(this));
            ToggleEquipCommand = ReactiveCommand.Create(() => { IsEquipped = !IsEquipped; EquipToggled?.Invoke(this); });
            ToggleOffHandCommand = ReactiveCommand.Create(() => { IsOffHand = !IsOffHand; OffHandToggled?.Invoke(this); });
            ToggleAttuneCommand = ReactiveCommand.Create(() => { IsAttuned = !IsAttuned; AttuneToggled?.Invoke(this); });
            UseCommand = ReactiveCommand.Create(() => UseRequested?.Invoke(this));
        }

        private void ParseHealing(JsonElement root)
        {
            JsonElement heal = default;
            var found = false;

            if (root.TryGetProperty("MechanicalProperties", out var mp)
                && mp.ValueKind == JsonValueKind.Object
                && mp.TryGetProperty("Healing", out var h1)
                && h1.ValueKind == JsonValueKind.Object)
            {
                heal = h1;
                found = true;
            }
            else if (root.TryGetProperty("Healing", out var h2) && h2.ValueKind == JsonValueKind.Object)
            {
                heal = h2;
                found = true;
            }

            if (!found) return;

            if (heal.TryGetProperty("DiceId", out var di)) HealDiceId = di.GetString() ?? "";
            if (heal.TryGetProperty("DiceCount", out var dc) && dc.TryGetInt32(out var dcv)) HealDiceCount = dcv;
            if (heal.TryGetProperty("Flat", out var fl) && fl.TryGetInt32(out var flv)) HealFlat = flv;

            HasHealing = (!string.IsNullOrEmpty(HealDiceId) && HealDiceCount > 0) || HealFlat > 0;
        }
    }

    public class AbilityScoreViewModel : ViewModelBase
    {
        public string Name { get; }
        public string ShortName { get; }
        public string Id { get; }

        private int _score;
        public int Score
        {
            get => _score;
            set
            {
                this.RaiseAndSetIfChanged(ref _score, value);
                this.RaisePropertyChanged(nameof(Modifier));
                this.RaisePropertyChanged(nameof(ModifierDisplay));
                this.RaisePropertyChanged(nameof(SaveBonusDisplay));
            }
        }

        private int _bonusToScore;
        public int BonusToScore
        {
            get => _bonusToScore;
            set
            {
                this.RaiseAndSetIfChanged(ref _bonusToScore, value);
                this.RaisePropertyChanged(nameof(Modifier));
                this.RaisePropertyChanged(nameof(ModifierDisplay));
                this.RaisePropertyChanged(nameof(SaveBonusDisplay));
            }
        }

        private int _saveBonus;
        public int SaveBonus
        {
            get => _saveBonus;
            set
            {
                this.RaiseAndSetIfChanged(ref _saveBonus, value);
                this.RaisePropertyChanged(nameof(SaveBonusDisplay));
            }
        }

        public int Modifier => App.PM?.AbilityMod(Score + BonusToScore) ?? (int)Math.Floor((Score + BonusToScore - 10) / 2.0);
        public string ModifierDisplay => Modifier >= 0 ? $"+{Modifier}" : Modifier.ToString();

        private bool _saveProficient;
        public bool SaveProficient
        {
            get => _saveProficient;
            set
            {
                this.RaiseAndSetIfChanged(ref _saveProficient, value);
                this.RaisePropertyChanged(nameof(SaveBonusDisplay));
            }
        }

        private bool _isInherent;
        public bool IsInherent
        {
            get => _isInherent;
            set
            {
                this.RaiseAndSetIfChanged(ref _isInherent, value);
                this.RaisePropertyChanged(nameof(CanEditSave));
            }
        }
        public bool CanEditSave => !_isInherent;

        public int ProficiencyBonus { get; set; } = 2;

        public int SaveTotal => Modifier + (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(SaveProficient), ProficiencyBonus) + SaveBonus;

        public string SaveBonusDisplay
        {
            get
            {
                var b = SaveTotal;
                return b >= 0 ? $"+{b}" : b.ToString();
            }
        }

        public event Action<AbilityScoreViewModel>? CheckRolled;
        public event Action<AbilityScoreViewModel>? SaveRolled;

        public ReactiveCommand<Unit, Unit> RollCheckCommand { get; }
        public ReactiveCommand<Unit, Unit> RollSaveCommand { get; }

        public AbilityScoreViewModel(string name, string shortName, int score = 10, string id = "")
        {
            Name = name;
            ShortName = shortName;
            Id = id;
            _score = score;

            RollCheckCommand = ReactiveCommand.Create(() => CheckRolled?.Invoke(this));
            RollSaveCommand = ReactiveCommand.Create(() => SaveRolled?.Invoke(this));
        }
    }

    public class SkillViewModel : ViewModelBase
    {
        public string Name { get; }
        public string AbilityShortName { get; }
        private readonly AbilityScoreViewModel _ability;

        private bool _proficient;
        public bool Proficient
        {
            get => _proficient;
            set
            {
                this.RaiseAndSetIfChanged(ref _proficient, value);
                this.RaisePropertyChanged(nameof(BonusDisplay));
            }
        }

        private bool _expertise;
        public bool Expertise
        {
            get => _expertise;
            set
            {
                this.RaiseAndSetIfChanged(ref _expertise, value);
                this.RaisePropertyChanged(nameof(BonusDisplay));
            }
        }

        public int ProficiencyBonus { get; set; } = 2;

        public string BonusDisplay
        {
            get
            {
                var mod = _ability.Modifier + (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(Proficient, Expertise), ProficiencyBonus);
                return mod >= 0 ? $"+{mod}" : mod.ToString();
            }
        }

        public SkillViewModel(string name, string abilityShort, AbilityScoreViewModel ability)
        {
            Name = name;
            AbilityShortName = abilityShort;
            _ability = ability;
            _ability.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AbilityScoreViewModel.Modifier))
                    this.RaisePropertyChanged(nameof(BonusDisplay));
            };
        }
    }
}
