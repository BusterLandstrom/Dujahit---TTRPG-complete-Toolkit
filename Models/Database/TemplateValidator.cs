using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Dujahit.Models.Database
{
    public record TemplateIssue(string Severity, string Where, string Message);

    public static class TemplateValidator
    {
        private static readonly (string Array, string Field, bool IsList, string[] Targets)[] Rules =
        {
            ("Classes", "PrimaryAbilityId", false, new[] { "Abilities" }),
            ("Classes", "HitDiceId", false, new[] { "Dices" }),
            ("Classes", "SavingThrowIds", true, new[] { "Proficiencies" }),
            ("Classes", "ArmorProficiencyIds", true, new[] { "Proficiencies" }),
            ("Classes", "WeaponProficiencyIds", true, new[] { "Proficiencies" }),
            ("Classes", "FeatsIds", true, new[] { "Feats", "Features" }),
            ("Classes", "SubclassIds", true, new[] { "Subclasses" }),
            ("Classes", "SpellListIds", true, new[] { "Spells" }),
            ("Subclasses", "ParentClassId", false, new[] { "Classes" }),
            ("Subclasses", "FeatsIds", true, new[] { "Feats", "Features" }),
            ("Features", "ClassId", false, new[] { "Classes" }),
            ("Features", "BonusIds", true, new[] { "Bonuses" }),
            ("Feats", "BonusIds", true, new[] { "Bonuses" }),
            ("Backgrounds", "FeatsIds", true, new[] { "Feats" }),
            ("Races", "SubraceIds", true, new[] { "Subraces" }),
            ("Races", "TraitIds", true, new[] { "Traits" }),
            ("Races", "LanguageIds", true, new[] { "Languages" }),
            ("Subraces", "TraitIds", true, new[] { "Traits" }),
            ("ClassResources", "ClassId", false, new[] { "Classes" }),
            ("ClassChoices", "ClassId", false, new[] { "Classes" }),
            ("ClassChoices", "OptionIds", true, new[] { "Subclasses", "Feats", "Proficiencies", "Features", "Abilities" }),
        };

        public static List<TemplateIssue> Validate(string? json)
        {
            var issues = new List<TemplateIssue>();
            if (string.IsNullOrWhiteSpace(json)) { issues.Add(new("error", "template", "No template content to validate.")); return issues; }

            JsonElement root;
            try { using var doc = JsonDocument.Parse(json); root = doc.RootElement.Clone(); }
            catch (JsonException ex) { issues.Add(new("error", "template", "Template JSON does not parse: " + ex.Message)); return issues; }
            if (root.ValueKind != JsonValueKind.Object) { issues.Add(new("error", "template", "Template root is not an object.")); return issues; }

            var sets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                var idKey = prop.Name.Equals("ClassResources", StringComparison.OrdinalIgnoreCase) ? "Id" : "TemplateId";
                if (prop.Name.Equals("Level", StringComparison.OrdinalIgnoreCase)) continue;

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var id = el.TryGetProperty(idKey, out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        var nm = el.TryGetProperty("Name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString() : "(unnamed)";
                        issues.Add(new("error", prop.Name, $"Entry '{nm}' has no {idKey}."));
                        continue;
                    }
                    if (!set.Add(id!))
                        issues.Add(new("error", prop.Name, $"Duplicate {idKey} '{id}'."));
                }
                sets[prop.Name] = set;
            }

            HashSet<string> Pool(string[] targets)
            {
                var pool = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in targets)
                    if (sets.TryGetValue(t, out var s)) pool.UnionWith(s);
                return pool;
            }

            foreach (var rule in Rules)
            {
                if (!root.TryGetProperty(rule.Array, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                var pool = Pool(rule.Targets);
                var targetLabel = string.Join("/", rule.Targets);
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var owner = el.TryGetProperty("TemplateId", out var oi) && oi.ValueKind == JsonValueKind.String ? oi.GetString()
                        : el.TryGetProperty("Id", out var oi2) && oi2.ValueKind == JsonValueKind.String ? oi2.GetString() : "(?)";
                    if (!el.TryGetProperty(rule.Field, out var field)) continue;

                    if (rule.IsList)
                    {
                        if (field.ValueKind != JsonValueKind.Array) continue;
                        foreach (var refEl in field.EnumerateArray())
                        {
                            var refId = RefId(refEl);
                            if (string.IsNullOrEmpty(refId)) continue;
                            if (!pool.Contains(refId!))
                                issues.Add(new("warning", rule.Array, $"{owner}.{rule.Field} points at '{refId}' which is not in {targetLabel}."));
                        }
                    }
                    else if (field.ValueKind == JsonValueKind.String)
                    {
                        var refId = field.GetString();
                        if (!string.IsNullOrEmpty(refId) && !pool.Contains(refId!))
                            issues.Add(new("warning", rule.Array, $"{owner}.{rule.Field} points at '{refId}' which is not in {targetLabel}."));
                    }
                }
            }

            if (root.TryGetProperty("CombatSettings", out var cs) && cs.ValueKind == JsonValueKind.Object
                && cs.TryGetProperty("ActionCosts", out var costs) && costs.ValueKind == JsonValueKind.Object)
            {
                var readKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "attack", "offhand-attack", "save", "cast", "dash", "ready" };
                var pools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "action", "bonus", "reaction", "none" };
                foreach (var p in costs.EnumerateObject())
                {
                    if (!readKeys.Contains(p.Name))
                        issues.Add(new("warning", "CombatSettings", $"ActionCosts.{p.Name} is a key nothing reads, only attack, offhand-attack, save, cast, dash and ready are consulted."));
                    var pool = GameRules.CostPool(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "");
                    if (!pools.Contains(pool))
                        issues.Add(new("warning", "CombatSettings", $"ActionCosts.{p.Name} spends from '{pool}' which is not a pool, it will price against the action pool."));
                }
            }

            return issues.OrderBy(i => i.Severity == "error" ? 0 : 1).ThenBy(i => i.Where).ToList();
        }

        private static string? RefId(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Object => el.TryGetProperty("BonusId", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()
                                    : el.TryGetProperty("TemplateId", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            _ => null
        };
    }
}
