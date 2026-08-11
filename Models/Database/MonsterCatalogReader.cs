using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models.Database
{
    public class MonsterOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Size { get; set; } = "Medium";
        public string? Description { get; set; }
        public string? DefaultColor { get; set; }
        public string ChallengeRating { get; set; } = "";
        public int ArmorClass { get; set; }
        public int HitPoints { get; set; }
        public int DexMod { get; set; }        // Despite the name this carries the mod for whatever ability the template points initiative at.
        public int Speed { get; set; }
        public int AttacksPerAction { get; set; } = 1;
        public int LegendaryPerRound { get; set; }
        public int LairInitiative { get; set; }
        public Dictionary<string, int> Saves { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<MonsterAttackOption> Attacks { get; set; } = new();
        public List<LegendaryActionOption> LegendaryActions { get; set; } = new();
        public List<LairActionOption> LairActions { get; set; } = new();
        public List<string> Resistances { get; set; } = new();
        public List<string> Immunities { get; set; } = new();
        public List<string> Vulnerabilities { get; set; } = new();
        public override string ToString() => Name;
    }

    public record MonsterAttackOption(string Name, int ToHit, string Damage, string DamageType, int RangeFeet = 0,
        string AreaShape = "", double AreaSizeFt = 0, double AreaWidthFt = 0, string SaveAbility = "", int SaveDc = 0, int RechargeOn = 0);

    // AttackName points at one of the monster's own attacks, when it is set the option swings that instead of just printing its text.
    public record LegendaryActionOption(string Name, int Cost = 1, string Description = "", string AttackName = "");

    public record LairActionOption(string Name, string Description = "", string AttackName = "");

    public class MonsterCatalogReader
    {
        private readonly DatabaseManager _db;
        public MonsterCatalogReader(DatabaseManager db) => _db = db;

        public async Task<List<MonsterOption>> ReadAsync(string templateId, CancellationToken ct = default)
        {
            var list = new List<MonsterOption>();
            await using var conn = await _db.OpenAsync(ct);

            string? json = null;
            if (!string.IsNullOrEmpty(templateId))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", templateId);
                json = await cmd.ExecuteScalarAsync(ct) as string;
            }
            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync(ct) as string;
            }
            if (string.IsNullOrEmpty(json)) return list;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("Monsters", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return list;

                foreach (var e in arr.EnumerateArray())
                {
                    var id = e.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;

                    var m = new MonsterOption { Id = id! };
                    if (e.TryGetProperty("Name", out var n)) m.Name = n.GetString() ?? id!;
                    if (e.TryGetProperty("Size", out var sz)) m.Size = sz.GetString() ?? "Medium";
                    if (e.TryGetProperty("Description", out var d)) m.Description = d.GetString();
                    if (e.TryGetProperty("DefaultTokenColor", out var c)) m.DefaultColor = c.GetString();
                    if (e.TryGetProperty("ChallengeRating", out var cr) && cr.ValueKind == JsonValueKind.String)
                        m.ChallengeRating = cr.GetString() ?? "";
                    if (e.TryGetProperty("ArmorClass", out var ac) && ac.ValueKind == JsonValueKind.Number && ac.TryGetInt32(out var acv))
                        m.ArmorClass = acv;
                    if (e.TryGetProperty("HitPoints", out var hp) && hp.ValueKind == JsonValueKind.Number && hp.TryGetInt32(out var hpv))
                        m.HitPoints = hpv;

                    var rules = App.PM?.Rules;
                    var abilityDefs = rules?.Abilities;
                    if (e.TryGetProperty("Abilities", out var ab) && ab.ValueKind == JsonValueKind.Object)
                    {
                        m.DexMod = AbilityModOf(ab, rules != null ? rules.AbilityIdForShort(rules.InitiativeAbility) : "ability-dex");
                        if (abilityDefs != null && abilityDefs.Count > 0)
                            foreach (var def in abilityDefs)
                                m.Saves[def.Short] = AbilityModOf(ab, def.Id);
                        else
                            foreach (var (abilityId, sht) in new[] { ("ability-str", "STR"), ("ability-dex", "DEX"), ("ability-con", "CON"), ("ability-int", "INT"), ("ability-wis", "WIS"), ("ability-cha", "CHA") })
                                m.Saves[sht] = AbilityModOf(ab, abilityId);
                    }

                    if (e.TryGetProperty("SavingThrows", out var st) && st.ValueKind == JsonValueKind.Object)
                        foreach (var key in m.Saves.Keys.ToList())
                            m.Saves[key] = SaveOverride(st, key, m.Saves[key]);

                    if (e.TryGetProperty("AttacksPerAction", out var apa) && apa.ValueKind == JsonValueKind.Number && apa.TryGetInt32(out var apav) && apav > 0)
                        m.AttacksPerAction = apav;

                    if (e.TryGetProperty("Speed", out var spd) && spd.ValueKind == JsonValueKind.Number && spd.TryGetInt32(out var spv))
                        m.Speed = spv;

                    if (e.TryGetProperty("Attacks", out var atks) && atks.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var at in atks.EnumerateArray())
                        {
                            var an = at.TryGetProperty("Name", out var ann) ? ann.GetString() ?? "" : "";
                            var ah = at.TryGetProperty("ToHit", out var ahh) && ahh.ValueKind == JsonValueKind.Number && ahh.TryGetInt32(out var ahv) ? ahv : 0;
                            var adm = at.TryGetProperty("Damage", out var add) ? add.GetString() ?? "" : "";
                            var aty = at.TryGetProperty("DamageType", out var att) ? att.GetString() ?? "" : "";
                            var arg = at.TryGetProperty("Range", out var arn) && arn.ValueKind == JsonValueKind.Number && arn.TryGetInt32(out var argv) ? argv : 0;
                            var ash = at.TryGetProperty("AreaShape", out var ash1) ? ash1.GetString() ?? "" : "";
                            var asz = at.TryGetProperty("AreaSizeFt", out var asz1) && asz1.TryGetDouble(out var aszv) ? aszv : 0;
                            var awd = at.TryGetProperty("AreaWidthFt", out var awd1) && awd1.TryGetDouble(out var awdv) ? awdv : 0;
                            var asv = at.TryGetProperty("Save", out var asv1) ? asv1.GetString() ?? "" : "";
                            var adc = at.TryGetProperty("SaveDc", out var adc1) && adc1.ValueKind == JsonValueKind.Number && adc1.TryGetInt32(out var adcv) ? adcv : 0;
                            var arc = at.TryGetProperty("RechargeOn", out var arc1) && arc1.ValueKind == JsonValueKind.Number && arc1.TryGetInt32(out var arcv) ? arcv : 0;
                            if (!string.IsNullOrEmpty(an)) m.Attacks.Add(new MonsterAttackOption(an, ah, adm, aty, arg, ash, asz, awd, asv, adc, arc));
                        }
                    }

                    ReadLegendary(e, m);
                    ReadLair(e, m);

                    ReadTypeList(e, "Resistances", m.Resistances);
                    ReadTypeList(e, "DamageResistances", m.Resistances);
                    ReadTypeList(e, "Immunities", m.Immunities);
                    ReadTypeList(e, "DamageImmunities", m.Immunities);
                    ReadTypeList(e, "Vulnerabilities", m.Vulnerabilities);
                    ReadTypeList(e, "DamageVulnerabilities", m.Vulnerabilities);

                    list.Add(m);
                }
            }
            catch (Exception)
            {
            }

            return list;
        }

        internal static void ReadLegendary(JsonElement entry, MonsterOption m)
        {
            if (!entry.TryGetProperty("LegendaryActions", out var la) || la.ValueKind != JsonValueKind.Object) return;
            if (!la.TryGetProperty("Options", out var opts) || opts.ValueKind != JsonValueKind.Array) return;

            foreach (var o in opts.EnumerateArray())
            {
                var name = o.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                var cost = o.TryGetProperty("Cost", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var cv) && cv > 0 ? cv : 1;
                var desc = o.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
                var atk = o.TryGetProperty("Attack", out var a) ? a.GetString() ?? "" : "";
                m.LegendaryActions.Add(new LegendaryActionOption(name, cost, desc, atk));
            }
            if (m.LegendaryActions.Count == 0) return;

            m.LegendaryPerRound = la.TryGetProperty("PerRound", out var pr) && pr.ValueKind == JsonValueKind.Number && pr.TryGetInt32(out var prv) && prv > 0
                ? prv
                : App.PM?.Rules?.DefaultLegendaryActionsPerRound ?? 3;
        }

        internal static void ReadLair(JsonElement entry, MonsterOption m)
        {
            if (!entry.TryGetProperty("LairActions", out var la) || la.ValueKind != JsonValueKind.Object) return;
            if (!la.TryGetProperty("Options", out var opts) || opts.ValueKind != JsonValueKind.Array) return;

            foreach (var o in opts.EnumerateArray())
            {
                var name = o.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                var desc = o.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
                var atk = o.TryGetProperty("Attack", out var a) ? a.GetString() ?? "" : "";
                m.LairActions.Add(new LairActionOption(name, desc, atk));
            }
            if (m.LairActions.Count == 0) return;

            m.LairInitiative = la.TryGetProperty("Initiative", out var ic) && ic.ValueKind == JsonValueKind.Number && ic.TryGetInt32(out var icv) && icv > 0
                ? icv
                : App.PM?.Rules?.LairActionInitiativeCount ?? 20;
        }

        private static int AbilityModOf(JsonElement abilities, string key)
        {
            if (abilities.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var sc))
                return App.PM?.AbilityMod(sc) ?? (int)Math.Floor((sc - 10) / 2.0);
            return 0;
        }

        private static int SaveOverride(JsonElement st, string key, int fallback) =>
            st.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var sv) ? sv : fallback;

        private static void ReadTypeList(JsonElement entry, string prop, List<string> into)
        {
            if (!entry.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var t in arr.EnumerateArray())
            {
                var s = t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (!string.IsNullOrWhiteSpace(s) && !into.Contains(s!)) into.Add(s!);
            }
        }
    }

    // Book statblocks come in a handful of shapes and a pdf paste wraps lines wherever it feels like, so everything here is a tolerant scan and a miss just leaves the field out
    public static class StatblockParser
    {
        private static readonly (string Short, string Id)[] _abilityOrder =
        {
            ("STR", "ability-str"), ("DEX", "ability-dex"), ("CON", "ability-con"),
            ("INT", "ability-int"), ("WIS", "ability-wis"), ("CHA", "ability-cha")
        };

        private static readonly Dictionary<string, int> _countWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6
        };

        public static JsonObject? Parse(string raw, out string error)
        {
            error = "";
            var lines = (raw ?? "").Replace("\r", "").Replace('−', '-').Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count < 3)
            {
                error = "Paste the whole block, starting at the creature's name.";
                return null;
            }

            var name = lines[0];
            var all = string.Join("\n", lines);
            var o = new JsonObject
            {
                ["TemplateId"] = "monster-" + Slug(name),
                ["Name"] = name,
                ["DefaultTokenColor"] = "#7D7D7D"
            };

            var meta = Regex.Match(lines[1], @"^(Tiny|Small|Medium|Large|Huge|Gargantuan)\b[ ]*([^,(]*)(?:\([^)]*\))?\s*(?:,\s*(.+))?$", RegexOptions.IgnoreCase);
            if (meta.Success)
            {
                o["Size"] = Cap(meta.Groups[1].Value);
                if (meta.Groups[2].Value.Trim().Length > 0) o["Type"] = Cap(meta.Groups[2].Value.Trim());
                if (meta.Groups[3].Success && meta.Groups[3].Value.Trim().Length > 0) o["Alignment"] = Cap(meta.Groups[3].Value.Trim());
            }

            var ac = Regex.Match(all, @"\b(?:Armor Class|AC)\b[ :]*(\d+)", RegexOptions.IgnoreCase);
            if (ac.Success) o["ArmorClass"] = int.Parse(ac.Groups[1].Value);

            var hp = Regex.Match(all, @"\b(?:Hit Points|HP)\b[ :]*(\d+)\s*(?:\(([^)]+)\))?", RegexOptions.IgnoreCase);
            if (hp.Success)
            {
                o["HitPoints"] = int.Parse(hp.Groups[1].Value);
                if (hp.Groups[2].Success) o["HitDice"] = hp.Groups[2].Value.Replace(" ", "");
            }

            var speed = Regex.Match(all, @"\bSpeed\b[ :]*(\d+)\s*ft", RegexOptions.IgnoreCase);
            if (speed.Success) o["Speed"] = int.Parse(speed.Groups[1].Value);

            var cr = Regex.Match(all, @"\b(?:Challenge|CR)\b[ :]*([\d/]+)", RegexOptions.IgnoreCase);
            if (cr.Success) o["ChallengeRating"] = cr.Groups[1].Value;

            var abilities = ReadAbilities(lines, all);
            if (abilities != null) o["Abilities"] = abilities;

            var saveLine = lines.FirstOrDefault(l => Regex.IsMatch(l, @"^Saving Throws?\b", RegexOptions.IgnoreCase));
            if (saveLine != null)
            {
                var saves = new JsonObject();
                foreach (Match sm in Regex.Matches(saveLine, @"\b(Str|Dex|Con|Int|Wis|Cha)\w*\.?\s*([+-]\s*\d+)"))
                    saves[sm.Groups[1].Value.ToUpperInvariant()] = int.Parse(sm.Groups[2].Value.Replace(" ", ""));
                if (saves.Count > 0) o["SavingThrows"] = saves;
            }

            AddTypeList(o, lines, "Resistances", @"^(?:Damage )?Resistances?\b");
            AddTypeList(o, lines, "Immunities", @"^(?:Damage )?Immunities\b");
            AddTypeList(o, lines, "Vulnerabilities", @"^(?:Damage )?Vulnerabilit(?:y|ies)\b");

            var multi = Regex.Match(all, @"\bMultiattack\b.{0,120}?\bmakes\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (multi.Success)
            {
                var word = multi.Groups[1].Value;
                if (_countWords.TryGetValue(word, out var n) || int.TryParse(word, out n))
                    if (n > 1) o["AttacksPerAction"] = n;
            }

            var attacks = ReadAttacks(all);
            if (attacks.Count > 0) o["Attacks"] = attacks;

            var traits = TraitBlock(lines);
            if (traits.Length > 0) o["Description"] = traits;

            if (!o.ContainsKey("ArmorClass") && !o.ContainsKey("HitPoints") && abilities == null)
            {
                error = "Could not find an armor class, hit points or an ability line in that, is it really a statblock?";
                return null;
            }
            return o;
        }

        private static JsonObject? ReadAbilities(List<string> lines, string all)
        {
            var result = new JsonObject();

            // The classic layout is a STR..CHA header with the six scores on the next line, anything else falls through to a per name scan.
            var headerIdx = lines.FindIndex(l => Regex.IsMatch(l, @"^STR\b.*\bDEX\b.*\bCHA\b", RegexOptions.IgnoreCase));
            if (headerIdx >= 0)
            {
                var scores = new List<int>();
                for (var i = headerIdx + 1; i < lines.Count && i <= headerIdx + 3 && scores.Count < 6; i++)
                    foreach (Match m in Regex.Matches(lines[i], @"(\d+)\s*\("))
                        if (scores.Count < 6) scores.Add(int.Parse(m.Groups[1].Value));
                if (scores.Count == 6)
                {
                    for (var i = 0; i < 6; i++) result[_abilityOrder[i].Id] = scores[i];
                    return result;
                }
            }

            foreach (var (sht, id) in _abilityOrder)
            {
                var m = Regex.Match(all, @"\b" + sht + @"\b\D{0,4}(\d+)", RegexOptions.IgnoreCase);
                if (m.Success) result[id] = int.Parse(m.Groups[1].Value);
            }
            return result.Count == 6 ? result : null;
        }

        private static void AddTypeList(JsonObject o, List<string> lines, string key, string pattern)
        {
            var line = lines.FirstOrDefault(l => Regex.IsMatch(l, pattern, RegexOptions.IgnoreCase));
            if (line == null) return;
            var rest = Regex.Replace(line, pattern, "", RegexOptions.IgnoreCase).Trim();
            var arr = new JsonArray();
            foreach (var part in rest.Split(',', ';'))
            {
                var t = part.Trim();
                if (t.StartsWith("and ", StringComparison.OrdinalIgnoreCase)) t = t[4..].Trim();
                var cut = t.IndexOf(" from ", StringComparison.OrdinalIgnoreCase);
                if (cut < 0) cut = t.IndexOf(" (", StringComparison.Ordinal);
                if (cut > 0) t = t[..cut];
                t = t.Trim().ToLowerInvariant();
                if (t.Length > 0 && t.Length <= 24) arr.Add(t);
            }
            if (arr.Count > 0) o[key] = arr;
        }

        private static JsonArray ReadAttacks(string all)
        {
            var attacks = new JsonArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(all,
                @"(?<name>[A-Z][A-Za-z' /()+-]{0,40}?)\.\s*(?<kind>Melee|Ranged)[^:\n]{0,60}?Attack(?:\s*Roll)?:\s*\+?(?<hit>-?\d+)(?=(?<rest>.{0,260}))",
                RegexOptions.Singleline))
            {
                var name = m.Groups["name"].Value.Trim();
                if (name.Length == 0 || !seen.Add(name)) continue;
                var rest = m.Groups["rest"].Value;

                var dmg = "";
                var type = "";
                var hitDice = Regex.Match(rest, @"Hit:\s*\d+\s*\(([^)]+)\)\s*([A-Za-z]+)\s+damage", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (hitDice.Success)
                {
                    dmg = hitDice.Groups[1].Value.Replace(" ", "").Replace("\n", "");
                    type = hitDice.Groups[2].Value.ToLowerInvariant();
                }
                else
                {
                    var hitFlat = Regex.Match(rest, @"Hit:\s*(\d+)\s+([A-Za-z]+)\s+damage", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (hitFlat.Success)
                    {
                        dmg = hitFlat.Groups[1].Value;
                        type = hitFlat.Groups[2].Value.ToLowerInvariant();
                    }
                }

                var range = 0;
                if (string.Equals(m.Groups["kind"].Value, "Ranged", StringComparison.OrdinalIgnoreCase))
                {
                    var rr = Regex.Match(rest, @"range\s+(\d+)", RegexOptions.IgnoreCase);
                    if (rr.Success) range = int.Parse(rr.Groups[1].Value);
                }
                else
                {
                    var reach = Regex.Match(rest, @"reach\s+(\d+)", RegexOptions.IgnoreCase);
                    if (reach.Success && int.Parse(reach.Groups[1].Value) > 5) range = int.Parse(reach.Groups[1].Value);
                }

                var atk = new JsonObject
                {
                    ["Name"] = name,
                    ["ToHit"] = int.Parse(m.Groups["hit"].Value),
                    ["Damage"] = dmg,
                    ["DamageType"] = type
                };
                if (range > 0) atk["Range"] = range;
                attacks.Add(atk);
            }

            foreach (var breath in ReadBreathWeapons(all, seen)) attacks.Add(breath);
            return attacks;
        }

        private static IEnumerable<JsonObject> ReadBreathWeapons(string all, HashSet<string> seen)
        {
            foreach (Match m in Regex.Matches(all,
                @"(?<name>[A-Z][A-Za-z0-9' /()+,-]{0,40}?)\.\s*(?<body>[^.]{0,120}?(?<size>\d+)[- ]foot\s*(?<shape>cone|line|cube|radius|sphere)(?<rest>.{0,320}))",
                RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var name = m.Groups["name"].Value.Trim();
                if (name.Length == 0 || !seen.Add(name)) continue;

                var recharge = Regex.Match(name, @"\(\s*Recharge\s*(?<low>\d)\s*(?:-\s*\d)?\s*\)", RegexOptions.IgnoreCase);
                if (recharge.Success) name = Regex.Replace(name, @"\s*\(\s*Recharge[^)]*\)", "", RegexOptions.IgnoreCase).Trim();

                var rest = m.Groups["rest"].Value;
                var save = Regex.Match(rest, @"DC\s*(?<dc>\d+)\s*(?<ability>Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)", RegexOptions.IgnoreCase);
                if (!save.Success) { seen.Remove(name); continue; }

                var hit = Regex.Match(rest, @"(?:taking|takes)\s*\d+\s*\(([^)]+)\)\s*([A-Za-z]+)\s+damage", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!hit.Success) { seen.Remove(name); continue; }

                var shape = m.Groups["shape"].Value.ToLowerInvariant() switch
                {
                    "cone" => "cone",
                    "line" => "line",
                    "cube" => "cube",
                    _ => "circle"
                };

                var atk = new JsonObject
                {
                    ["Name"] = name,
                    ["ToHit"] = 0,
                    ["Damage"] = hit.Groups[1].Value.Replace(" ", "").Replace("\n", ""),
                    ["DamageType"] = hit.Groups[2].Value.ToLowerInvariant(),
                    ["AreaShape"] = shape,
                    ["AreaSizeFt"] = double.Parse(m.Groups["size"].Value),
                    ["Save"] = save.Groups["ability"].Value.Substring(0, 3).ToLowerInvariant(),
                    ["SaveDc"] = int.Parse(save.Groups["dc"].Value)
                };

                if (recharge.Success) atk["RechargeOn"] = int.Parse(recharge.Groups["low"].Value);

                if (shape == "line")
                {
                    var wide = Regex.Match(m.Groups["body"].Value + rest, @"(\d+)\s*(?:-|\s)?(?:foot|feet)[- ]wide", RegexOptions.IgnoreCase);
                    atk["AreaWidthFt"] = wide.Success ? double.Parse(wide.Groups[1].Value) : 5.0;
                }

                yield return atk;
            }
        }

        // The prose between the stat lines and the Actions header is the traits, Pack Tactics and friends, worth keeping even though nothing mechanical fires off it
        private static string TraitBlock(List<string> lines)
        {
            var statPattern = new Regex(@"^(Armor Class|AC\b|Hit Points|HP\b|Speed|Saving Throws?|Skills|Damage |Resistances?|Immunities|Vulnerabilit|Condition |Senses|Languages|Challenge|CR\b|Proficiency Bonus|Initiative|STR\b|DEX\b|CON\b|INT\b|WIS\b|CHA\b|Str\s+\d|Dex\s+\d|Con\s+\d|Int\s+\d|Wis\s+\d|Cha\s+\d|\d+\s*\()", RegexOptions.IgnoreCase);
            var kept = new List<string>();
            var pastStats = false;
            for (var i = 2; i < lines.Count; i++)
            {
                var l = lines[i];
                if (Regex.IsMatch(l, @"^(Actions|Bonus Actions|Reactions|Legendary Actions|Lair Actions)\b", RegexOptions.IgnoreCase)) break;
                if (statPattern.IsMatch(l)) { pastStats = true; continue; }
                if (pastStats) kept.Add(l);
            }
            return string.Join(" ", kept).Trim();
        }

        private static string Slug(string name)
        {
            var s = Regex.Replace(name.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');
            return s.Length > 0 ? s : "imported";
        }

        private static string Cap(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
