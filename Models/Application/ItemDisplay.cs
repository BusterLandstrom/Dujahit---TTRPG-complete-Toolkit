using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Dujahit.Models.Application
{
    public class ItemDisplay
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "Generic";
        public string TypeLine { get; set; } = "";
        public string StatLine { get; set; } = "";
        public string PropertiesLine { get; set; } = "";
        public string MagicTag { get; set; } = "";
        public bool IsMagic { get; set; }
        public string Description { get; set; } = "";

        public bool HasStats => !string.IsNullOrEmpty(StatLine);
        public bool HasProperties => !string.IsNullOrEmpty(PropertiesLine);
        public bool HasMagic => !string.IsNullOrEmpty(MagicTag);

        public static ItemDisplay FromJson(string name, string itemType, string dataJson)
        {
            var d = new ItemDisplay { Name = name, Kind = itemType ?? "Generic" };
            if (string.IsNullOrWhiteSpace(dataJson)) return d;

            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("Description", out var desc))
                    d.Description = desc.GetString() ?? "";

                var type = root.TryGetProperty("$type", out var t) ? t.GetString() ?? d.Kind : d.Kind;
                d.Kind = type;
                d.TypeLine = type;

                if (root.TryGetProperty("IsMagic", out var im) && im.GetBoolean())
                {
                    d.IsMagic = true;
                    if (type == "Armor" && root.TryGetProperty("AcBonus", out var ac))
                        d.MagicTag = $"+{ac.GetInt32()} AC";
                    else if (root.TryGetProperty("HitBonus", out var hb))
                        d.MagicTag = $"+{hb.GetInt32()} hit/dmg";
                }

                if (type == "Weapon")
                {
                    var dmgParts = new List<string>();
                    if (root.TryGetProperty("DamageValues", out var dv) && dv.ValueKind == JsonValueKind.Array)
                        foreach (var e in dv.EnumerateArray())
                        {
                            var dice = e.TryGetProperty("DiceId", out var di) ? di.GetString() ?? "" : "";
                            var count = e.TryGetProperty("Count", out var cn) && cn.TryGetInt32(out var cv) ? cv : 1;
                            var flat = e.TryGetProperty("Flat", out var fl) && fl.TryGetInt32(out var fv) ? fv : 0;
                            var dtype = e.TryGetProperty("TypeId", out var ti) ? (ti.GetString() ?? "").Replace("dmg-", "") : "";
                            var term = string.IsNullOrEmpty(dice) ? "" : $"{count}{dice}";
                            if (flat > 0) term += $"+{flat}";
                            else if (flat < 0) term += flat.ToString();
                            dmgParts.Add($"{term} {dtype}".Trim());
                        }
                    var bonus = root.TryGetProperty("DamageBonus", out var dbn) ? dbn.GetInt32() : 0;
                    var bonusTxt = bonus > 0 ? $" +{bonus}" : "";
                    d.StatLine = dmgParts.Count > 0 ? string.Join(" + ", dmgParts) + bonusTxt : "";

                    var props = new List<string>();
                    if (root.TryGetProperty("WeaponCategory", out var wc) && wc.ValueKind == JsonValueKind.Array)
                        foreach (var c in wc.EnumerateArray())
                            props.Add((c.GetString() ?? "").Replace("wp-", ""));
                    if (root.TryGetProperty("Mastery", out var ma))
                    {
                        var m = (ma.GetString() ?? "").Replace("mast-", "");
                        if (!string.IsNullOrEmpty(m)) props.Add("mastery: " + m);
                    }
                    if (root.TryGetProperty("IsRanged", out var ir) && ir.GetBoolean())
                    {
                        var rn = root.TryGetProperty("RangeNormal", out var rnv) && rnv.TryGetInt32(out var a) ? a : 0;
                        var rm = root.TryGetProperty("RangeMax", out var rmv) && rmv.TryGetInt32(out var b) ? b : 0;
                        props.Add(rn > 0 ? $"ranged {rn}/{rm} ft" : "ranged");
                    }
                    else props.Add("melee");
                    d.PropertiesLine = string.Join(", ", props.Where(p => p.Length > 0));
                }
                else if (type == "Armor")
                {
                    var baseAc = root.TryGetProperty("BaseAC", out var ba) ? ba.GetInt32() : 0;
                    var acB = root.TryGetProperty("AcBonus", out var ab) ? ab.GetInt32() : 0;
                    d.StatLine = acB > 0 ? $"AC {baseAc} (+{acB})" : $"AC {baseAc}";

                    var props = new List<string>();
                    if (root.TryGetProperty("ArmorType", out var at))
                        props.Add((at.GetString() ?? "").Replace("atype-", ""));
                    if (root.TryGetProperty("AllowsDexBonus", out var adb) && adb.GetBoolean())
                    {
                        var md = root.TryGetProperty("MaxDexBonus", out var mdv) && mdv.TryGetInt32(out var mm) ? mm : 0;
                        props.Add(md > 0 ? $"dex bonus (max +{md})" : "dex bonus");
                    }
                    d.PropertiesLine = string.Join(", ", props.Where(p => p.Length > 0));
                }
            }
            catch (JsonException)
            {
            }

            return d;
        }
    }
}
