using System;
using System.Collections.Generic;
using System.Text.Json;
using Dujahit.Models.UI;

namespace Dujahit.Models.Application
{
    public enum ActionOutcomeKind { None, Damage, Heal }

    public sealed class ActionResolution
    {
        public string Line { get; set; } = "";
        public ActionOutcomeKind Kind { get; set; } = ActionOutcomeKind.None;
        public int Amount { get; set; }
        public int HpDelta { get; set; }
        public string? DamageType { get; set; }
        public bool AutoApply { get; set; }
        public bool AttackResolved { get; set; }
        public bool AttackHit { get; set; }
        public string? SaveAbility { get; set; }
        public int SaveDc { get; set; }
        public bool HalfOnSave { get; set; }
        public string? Note { get; set; }
        public string? Condition { get; set; }
        public int ConditionRounds { get; set; }
        public string? ConditionExpiresAt { get; set; }
        public bool ConditionOnSaveToo { get; set; }
        public string? Buff { get; set; }
        public int BuffValue { get; set; }
        public int BuffRounds { get; set; }
        public string? BuffExpiresAt { get; set; }
        public string? BuffDice { get; set; }
    }

    public static class ActionProcessor
    {
        private sealed class Effect
        {
            public string Roll = "none";
            public string? Attack;
            public string? Save;
            public bool HalfOnSave;
            public string? DamageType;
            public string? ScaleBy;
            public Dictionary<string, string>? Scale;
            public Dictionary<string, string>? HealScale;
            public bool AddModToHeal;
            public string? Condition;
            public int ConditionRounds;
            public string? ConditionExpiresAt;
            public bool ConditionOnSaveToo;
            public string? Buff;
            public int BuffValue;
            public int BuffRounds;
            public string? BuffExpiresAt;
            public string? BuffDice;
        }

        public static ActionResolution Resolve(
            string casterName, string actionName, int actionLevel, string? effectsJson,
            int castAtLevel, int characterLevel, int abilityMod, int prof,
            int saveDc, int attackBonus, string? targetName, int targetAc)
        {
            var tgt = string.IsNullOrEmpty(targetName) ? "" : " -> " + targetName;
            var head = casterName + " casts " + actionName + (actionLevel > 0 ? " (L" + castAtLevel + ")" : "") + tgt;

            var res = new ActionResolution();
            var eff = ParseFirst(effectsJson);
            if (eff == null) { res.Line = head; return res; }

            var parts = new List<string>();
            var hit = true;
            var critDamage = false;
            var attackRolled = false;

            if (eff.Roll == "attack")
            {
                attackRolled = true;
                var nat = DiceManager.RollCore(App.PM?.Rules?.AttackDie ?? 20);
                bool isCrit = App.PM?.Rules.IsCrit(nat) ?? nat == 20;
                bool isFumble = App.PM?.Rules.IsFumble(nat) ?? nat == 1;
                var bonusText = attackBonus >= 0 ? "+" + attackBonus : attackBonus.ToString();
                var total = nat + attackBonus;

                if (targetAc > 0) { (hit, critDamage) = (App.PM?.Rules ?? new GameRules()).ResolveAttackOutcome(nat, total, targetAc); res.AttackResolved = true; }
                else if (isFumble && (App.PM?.Rules?.FumbleAlwaysMisses ?? true)) { hit = false; res.AttackResolved = true; }
                else if (isCrit && (App.PM?.Rules?.CritAlwaysHits ?? true)) { hit = true; critDamage = true; res.AttackResolved = true; }
                else { hit = false; res.AttackResolved = false; }

                var crit = critDamage ? " CRIT!" : isFumble ? " (nat 1)" : "";
                parts.Add("attack 1d" + (App.PM?.Rules?.AttackDie ?? 20) + bonusText + " -> [" + nat + "]" + bonusText + " = " + total + crit);
                res.AttackHit = hit;
            }
            else if (eff.Roll == "save" && !string.IsNullOrEmpty(eff.Save))
            {
                res.SaveAbility = eff.Save;
                res.SaveDc = saveDc;
                res.HalfOnSave = eff.HalfOnSave;
                parts.Add(eff.Save!.ToUpperInvariant() + " save DC " + saveDc + (eff.HalfOnSave ? " (half on save)" : ""));
            }

            if (!string.IsNullOrWhiteSpace(eff.Buff) && (eff.BuffValue != 0 || !string.IsNullOrWhiteSpace(eff.BuffDice)))
            {
                res.Buff = eff.Buff;
                res.BuffValue = eff.BuffValue;
                res.BuffRounds = eff.BuffRounds;
                res.BuffExpiresAt = eff.BuffExpiresAt;
                res.BuffDice = eff.BuffDice;
                var amount = string.IsNullOrWhiteSpace(eff.BuffDice)
                    ? (eff.BuffValue >= 0 ? "+" : "") + eff.BuffValue
                    : eff.BuffDice!;
                parts.Add(amount + " " + eff.Buff + (eff.BuffRounds > 0 ? " for " + eff.BuffRounds + " rounds" : ""));
            }

            if (!string.IsNullOrWhiteSpace(eff.Condition))
            {
                res.Condition = eff.Condition;
                res.ConditionRounds = eff.ConditionRounds;
                res.ConditionExpiresAt = eff.ConditionExpiresAt;
                res.ConditionOnSaveToo = eff.ConditionOnSaveToo;
                parts.Add(eff.Condition + (eff.ConditionRounds > 0 ? " for " + eff.ConditionRounds + " rounds" : ""));
            }

            var dmgExpr = PickScale(eff, castAtLevel, characterLevel);
            if (!string.IsNullOrEmpty(dmgExpr) && DiceManager.TryRoll(dmgExpr!, critDamage && hit, out var dr) && dr != null)
            {
                var type = string.IsNullOrEmpty(eff.DamageType) ? "" : " " + Title(eff.DamageType!);
                parts.Add(dmgExpr + type + " -> [" + dr.Breakdown + "] = " + dr.Total);
                res.Kind = ActionOutcomeKind.Damage;
                res.Amount = dr.Total;
                res.DamageType = eff.DamageType;

                if (eff.Roll == "attack")
                {
                    if (res.AttackResolved)
                    {
                        res.HpDelta = hit ? -dr.Total : 0;
                        res.AutoApply = true;
                    }
                    else
                    {
                        res.HpDelta = -dr.Total;
                        res.AutoApply = false;
                        res.Note = "unknown target ac, dm to apply";
                    }
                }
                else if (eff.Roll == "save")
                {
                    res.HpDelta = -dr.Total;
                    res.AutoApply = false;
                    res.Note = "save effect, dm applies full or half";
                }
                else
                {
                    res.HpDelta = -dr.Total;
                    res.AutoApply = true;
                }
            }

            var healExpr = PickHeal(eff, castAtLevel);
            if (!string.IsNullOrEmpty(healExpr))
            {
                var full = healExpr!;
                if (eff.AddModToHeal && abilityMod != 0)
                    full += abilityMod > 0 ? "+" + abilityMod : abilityMod.ToString();
                if (DiceManager.TryRoll(full, false, out var hr) && hr != null)
                {
                    parts.Add("heal " + full + " -> [" + hr.Breakdown + "] = " + hr.Total);
                    res.Kind = ActionOutcomeKind.Heal;
                    res.Amount = hr.Total;
                    res.HpDelta = hr.Total;
                    res.AutoApply = true;
                }
            }

            if (attackRolled && res.Kind == ActionOutcomeKind.None) res.Note ??= "attack with no damage roll on the effect";

            res.Line = parts.Count == 0 ? head : head + ": " + string.Join(", ", parts);
            return res;
        }

        private static Effect? ParseFirst(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json!);
                var root = doc.RootElement;
                JsonElement obj;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return null;
                    obj = root[0];
                }
                else if (root.ValueKind == JsonValueKind.Object) obj = root;
                else return null;

                var e = new Effect();
                if (obj.TryGetProperty("Roll", out var r) && r.ValueKind == JsonValueKind.String) e.Roll = r.GetString() ?? "none";
                if (obj.TryGetProperty("Attack", out var a) && a.ValueKind == JsonValueKind.String) e.Attack = a.GetString();
                if (obj.TryGetProperty("Save", out var sv) && sv.ValueKind == JsonValueKind.String) e.Save = sv.GetString();
                if (obj.TryGetProperty("HalfOnSave", out var h) && (h.ValueKind == JsonValueKind.True || h.ValueKind == JsonValueKind.False)) e.HalfOnSave = h.GetBoolean();
                if (obj.TryGetProperty("DamageType", out var dt) && dt.ValueKind == JsonValueKind.String) e.DamageType = dt.GetString();
                if (obj.TryGetProperty("ScaleBy", out var sb) && sb.ValueKind == JsonValueKind.String) e.ScaleBy = sb.GetString();
                if (obj.TryGetProperty("Scale", out var sc) && sc.ValueKind == JsonValueKind.Object) e.Scale = ReadMap(sc);
                if (obj.TryGetProperty("HealScale", out var hs) && hs.ValueKind == JsonValueKind.Object) e.HealScale = ReadMap(hs);
                if (obj.TryGetProperty("AddModToHeal", out var am) && (am.ValueKind == JsonValueKind.True || am.ValueKind == JsonValueKind.False)) e.AddModToHeal = am.GetBoolean();
                if (obj.TryGetProperty("Condition", out var cn) && cn.ValueKind == JsonValueKind.String) e.Condition = cn.GetString();
                if (obj.TryGetProperty("ConditionRounds", out var cr) && cr.ValueKind == JsonValueKind.Number && cr.TryGetInt32(out var crv)) e.ConditionRounds = crv;
                if (obj.TryGetProperty("ConditionExpiresAt", out var ce) && ce.ValueKind == JsonValueKind.String) e.ConditionExpiresAt = ce.GetString();
                if (obj.TryGetProperty("ConditionOnSaveToo", out var cs2) && (cs2.ValueKind == JsonValueKind.True || cs2.ValueKind == JsonValueKind.False)) e.ConditionOnSaveToo = cs2.GetBoolean();
                if (obj.TryGetProperty("Buff", out var bf) && bf.ValueKind == JsonValueKind.String) e.Buff = bf.GetString();
                if (obj.TryGetProperty("BuffValue", out var bv) && bv.ValueKind == JsonValueKind.Number && bv.TryGetInt32(out var bvv)) e.BuffValue = bvv;
                if (obj.TryGetProperty("BuffRounds", out var brd) && brd.ValueKind == JsonValueKind.Number && brd.TryGetInt32(out var brv)) e.BuffRounds = brv;
                if (obj.TryGetProperty("BuffExpiresAt", out var be) && be.ValueKind == JsonValueKind.String) e.BuffExpiresAt = be.GetString();
                if (obj.TryGetProperty("BuffDice", out var bd) && bd.ValueKind == JsonValueKind.String) e.BuffDice = bd.GetString();
                return e;
            }
            catch (JsonException) { return null; }
        }

        private static Dictionary<string, string> ReadMap(JsonElement obj)
        {
            var d = new Dictionary<string, string>();
            foreach (var prop in obj.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String) d[prop.Name] = prop.Value.GetString() ?? "";
            return d;
        }

        private static string? PickScale(Effect e, int castAtLevel, int characterLevel)
        {
            if (e.Scale == null || e.Scale.Count == 0) return null;
            var by = string.Equals(e.ScaleBy, App.PM?.Rules?.ScaleByCharacterToken ?? "char", StringComparison.OrdinalIgnoreCase) ? characterLevel : castAtLevel;
            return PickByLevel(e.Scale, by);
        }

        private static string? PickHeal(Effect e, int castAtLevel)
        {
            if (e.HealScale == null || e.HealScale.Count == 0) return null;
            return PickByLevel(e.HealScale, castAtLevel);
        }

        private static string? PickByLevel(Dictionary<string, string> map, int level)
        {
            string? best = null;
            var bestKey = int.MinValue;
            foreach (var kv in map)
            {
                if (!int.TryParse(kv.Key, out var k)) continue;
                if (k <= level && k > bestKey) { bestKey = k; best = kv.Value; }
            }
            if (best == null)
            {
                var lowest = int.MaxValue;
                foreach (var kv in map)
                    if (int.TryParse(kv.Key, out var k) && k < lowest) { lowest = k; best = kv.Value; }
            }
            return best;
        }

        private static string Title(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
