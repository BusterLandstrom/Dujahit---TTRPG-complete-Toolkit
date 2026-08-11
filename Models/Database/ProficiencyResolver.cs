using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models.Database
{
    public class ProficiencyResolver
    {
        private readonly DatabaseManager _db;
        public ProficiencyResolver(DatabaseManager db) => _db = db;

        public class GrantedProficiencies
        {
            public List<string> Armor { get; set; } = new();
            public List<string> Weapon { get; set; } = new();
            public List<string> SaveIds { get; set; } = new();
            public List<string> Other { get; set; } = new();
            public HashSet<string> AllIds { get; } = new();
        }

        // Armor makes you go through ArmorTypes to reach the same id a weapon just hands you, no idea why I did it that way
        public static string? RequiredProfId(string dataJson, IReadOnlyDictionary<string, string> armorTypeToProf)
        {
            if (string.IsNullOrWhiteSpace(dataJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                var type = root.TryGetProperty("$type", out var t) ? t.GetString() : null;
                if (type == "Weapon")
                    return root.TryGetProperty("ProfRequiredId", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                if (type == "Armor" && root.TryGetProperty("ArmorType", out var at) && at.ValueKind == JsonValueKind.String)
                {
                    var atype = at.GetString();
                    if (!string.IsNullOrEmpty(atype) && armorTypeToProf.TryGetValue(atype!, out var pr)) return pr;
                }
                return null;
            }
            catch (JsonException) { return null; }
        }

        public static bool ItemUsable(string dataJson, ISet<string> heldProfIds, IReadOnlyDictionary<string, string> armorTypeToProf)
        {
            var req = RequiredProfId(dataJson, armorTypeToProf);
            if (string.IsNullOrEmpty(req) || heldProfIds.Contains(req!)) return true;
            var own = SpecificProfIdFor(dataJson);
            return own != null && heldProfIds.Contains(own);
        }

        public static string? SpecificProfIdFor(string dataJson)
        {
            if (string.IsNullOrWhiteSpace(dataJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                var id = root.TryGetProperty("TemplateId", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                return string.IsNullOrWhiteSpace(id) ? null : "prof-weapon-" + id;
            }
            catch (JsonException) { return null; }
        }

        public async Task<GrantedProficiencies> ResolveForAsync(string templateId, string? classId, string? raceId, CancellationToken ct = default)
        {
            var g = new GrantedProficiencies();

            await using var conn = await _db.OpenAsync(ct);

            var profName = await LoadProfNamesAsync(conn, templateId, ct);

            if (!string.IsNullOrEmpty(classId))
            {
                var dj = await CatalogResolver.EntryJsonAsync(conn, "Classes", classId!, templateId, ct);
                if (dj != null)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(dj);
                        var root = doc.RootElement;
                        AddNames(root, "ArmorProficiencyIds", profName, g.Armor, g.AllIds);
                        AddNames(root, "WeaponProficiencyIds", profName, g.Weapon, g.AllIds);
                        AddSaves(root, "SavingThrowIds", g.SaveIds);
                    }
                    catch (JsonException) { }
                }
            }

            if (!string.IsNullOrEmpty(raceId))
            {
                var dj = await CatalogResolver.EntryJsonAsync(conn, "Races", raceId!, templateId, ct);
                if (dj != null)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(dj);
                        var root = doc.RootElement;
                        AddNames(root, "ProficiencyIds", profName, g.Other, g.AllIds);
                        AddNames(root, "WeaponProficiencyIds", profName, g.Weapon, g.AllIds);
                        AddNames(root, "ArmorProficiencyIds", profName, g.Armor, g.AllIds);
                    }
                    catch (JsonException) { }
                }
            }

            return g;
        }

        private static void AddNames(JsonElement root, string prop, IReadOnlyDictionary<string, string> names, List<string> sink, HashSet<string>? idSink = null)
        {
            if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var e in arr.EnumerateArray())
            {
                var id = e.GetString();
                if (string.IsNullOrEmpty(id)) continue;
                idSink?.Add(id);
                sink.Add(names.TryGetValue(id, out var nm) ? nm : id);
            }
        }

        private static void AddSaves(JsonElement root, string prop, List<string> sink)
        {
            if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var e in arr.EnumerateArray())
            {
                var id = e.GetString();
                if (string.IsNullOrEmpty(id)) continue;
                var bare = id.Replace("prof-", "").Replace("-save", "");
                sink.Add(bare);
            }
        }

        private static async Task<Dictionary<string, string>> LoadProfNamesAsync(SqliteConnection conn, string templateId, CancellationToken ct)
        {
            var map = new Dictionary<string, string>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
            cmd.Parameters.AddWithValue("$tid", templateId);
            var json = await cmd.ExecuteScalarAsync(ct) as string;

            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync(ct) as string;
            }
            if (string.IsNullOrEmpty(json)) return map;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Proficiencies", out var profs) && profs.ValueKind == JsonValueKind.Array)
                    foreach (var p in profs.EnumerateArray())
                    {
                        var id = p.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                        var nm = p.TryGetProperty("Name", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrEmpty(id)) map[id!] = nm ?? id!;
                    }
            }
            catch (JsonException) { }
            return map;
        }
    }
}
