using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dujahit.Models;

namespace Dujahit.Models.UI
{
    public enum SpellActionKind { Action, BonusAction, Reaction, Other }

    public record CastableSpell(string Id, string Name, int Level, string School, string EffectsJson, string CastingTime, string DataJson = "", bool Ritual = false, string Components = "", string Duration = "", bool Concentration = false)
    {
        public string LevelLabel => App.PM?.Rules?.SpellLevelName(Level) ?? (Level == 0 ? "Cantrip" : "Level " + Level);
        public bool IsCantrip => Level == 0;
        public bool HasComponents => !string.IsNullOrWhiteSpace(Components);
        public string ComponentsLabel => HasComponents ? Components : "";
        public bool NeedsMaterial => Components.IndexOf("M", StringComparison.OrdinalIgnoreCase) >= 0;
        public bool NeedsFreeHand => Components.IndexOf("S", StringComparison.OrdinalIgnoreCase) >= 0;
        public string CastNote =>
            string.Join(", ", new[] { ComponentsLabel, string.IsNullOrWhiteSpace(Duration) ? "" : Duration, Concentration ? "concentration" : "" }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public record SpellAoe(string Shape, double SizeFt, double WidthFt, string SaveAbility = "", int SaveDc = 0,
        int LastsRounds = 0, string Trigger = "", string Damage = "", string DamageType = "",
        string Terrain = "", string Condition = "", int ConditionRounds = 0, string Label = "");

    public static class SpellCaster
    {
        public static SpellAoe? ReadAoe(string? effectsJson)
        {
            if (string.IsNullOrWhiteSpace(effectsJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(effectsJson!);
                var root = doc.RootElement;
                JsonElement obj;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0) return null;
                    obj = root[0];
                }
                else if (root.ValueKind == JsonValueKind.Object) obj = root;
                else return null;

                if (!obj.TryGetProperty("Aoe", out var aoe) || aoe.ValueKind != JsonValueKind.Object) return null;
                var shape = aoe.TryGetProperty("Shape", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(shape)) return null;
                var size = aoe.TryGetProperty("SizeFt", out var sz) && sz.TryGetDouble(out var szv) ? szv : 0;
                var width = aoe.TryGetProperty("WidthFt", out var w) && w.TryGetDouble(out var wv) ? wv : 0;
                var save = obj.TryGetProperty("Save", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() ?? "" : "";

                if (!aoe.TryGetProperty("Lasts", out var lasts) || lasts.ValueKind != JsonValueKind.Object)
                    return new SpellAoe(shape, size, width, save);

                static string Str(JsonElement e, string name) =>
                    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                static int Num(JsonElement e, string name) =>
                    e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

                return new SpellAoe(shape, size, width, save, 0,
                    Num(lasts, "Rounds"),
                    Str(lasts, "Trigger") is { Length: > 0 } tr ? tr : "turn-start",
                    Str(lasts, "Damage"),
                    Str(lasts, "DamageType") is { Length: > 0 } dt ? dt : Str(obj, "DamageType"),
                    Str(lasts, "Terrain"),
                    Str(lasts, "Condition"),
                    Num(lasts, "ConditionRounds"),
                    Str(lasts, "Label"));
            }
            catch (JsonException) { return null; }
        }

        public static SpellActionKind ClassifyCastTime(string? castingTime)
        {
            var cost = CastCost(castingTime);
            if (cost.Length == 0) return SpellActionKind.Other;
            return GameRules.CostPool(cost) switch
            {
                "bonus" => SpellActionKind.BonusAction,
                "reaction" => SpellActionKind.Reaction,
                "action" => SpellActionKind.Action,
                _ => SpellActionKind.Other
            };
        }

        // Raw mapped value, so action:2 keeps its amount all the way to Pay.
        public static string CastCost(string? castingTime)
        {
            if (string.IsNullOrWhiteSpace(castingTime)) return "action";
            var ct = castingTime.ToLowerInvariant();
            var map = App.PM?.Rules?.CastingTimeKinds;
            if (map != null)
                foreach (var pair in map)
                    if (ct.Contains(pair.Key.ToLowerInvariant()))
                        return pair.Value;
            return "";
        }

        private class Effect
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
        }

        public static string Resolve(string casterName, string spellName, int spellLevel, string? effectsJson,
            int castAtLevel, int characterLevel, int abilityMod, int prof, int saveDc, int attackBonus, string? targetName)
        {
            var tgt = string.IsNullOrEmpty(targetName) ? "" : " -> " + targetName;
            var head = casterName + " casts " + spellName + (spellLevel > 0 ? " (L" + castAtLevel + ")" : "") + tgt;

            var eff = ParseFirst(effectsJson);
            if (eff == null) return head;

            var parts = new List<string>();
            var critDamage = false;

            if (eff.Roll == "attack")
            {
                var nat = DiceManager.RollCore(App.PM?.Rules?.AttackDie ?? 20);
                var bonusText = attackBonus >= 0 ? "+" + attackBonus : attackBonus.ToString();
                critDamage = App.PM?.Rules.IsCrit(nat) ?? nat == 20;
                var crit = critDamage ? " CRIT!" : (App.PM?.Rules.IsFumble(nat) ?? nat == 1) ? " (nat 1)" : "";
                parts.Add("attack 1d" + (App.PM?.Rules?.AttackDie ?? 20) + bonusText + " -> [" + nat + "]" + bonusText + " = " + (nat + attackBonus) + crit);
            }
            else if (eff.Roll == "save" && !string.IsNullOrEmpty(eff.Save))
            {
                parts.Add(eff.Save!.ToUpperInvariant() + " save DC " + saveDc + (eff.HalfOnSave ? " (half on save)" : ""));
            }

            var dmg = PickScale(eff, castAtLevel, characterLevel);
            if (!string.IsNullOrEmpty(dmg) && DiceManager.TryRoll(dmg!, critDamage, out var dr) && dr != null)
            {
                var type = string.IsNullOrEmpty(eff.DamageType) ? "" : " " + Title(eff.DamageType!);
                parts.Add(dmg + type + " -> [" + dr.Breakdown + "] = " + dr.Total);
            }

            var heal = PickHeal(eff, castAtLevel);
            if (!string.IsNullOrEmpty(heal))
            {
                var full = heal!;
                if (eff.AddModToHeal && abilityMod != 0)
                    full += abilityMod > 0 ? "+" + abilityMod : abilityMod.ToString();
                if (DiceManager.TryRoll(full, false, out var hr) && hr != null)
                    parts.Add("heal " + full + " -> [" + hr.Breakdown + "] = " + hr.Total);
            }

            return parts.Count == 0 ? head : head + ": " + string.Join(", ", parts);
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
