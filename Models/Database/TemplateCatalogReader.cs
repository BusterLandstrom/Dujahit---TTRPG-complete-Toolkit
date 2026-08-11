using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Dujahit.Models.Database
{
    public class CatalogOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public CatalogOption() { }
        public CatalogOption(string id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    public class TemplateItemCatalogs
    {
        public List<CatalogOption> DamageTypes { get; set; } = new();
        public List<CatalogOption> WeaponProperties { get; set; } = new();
        public List<CatalogOption> ArmorTypes { get; set; } = new();
        public List<CatalogOption> Masteries { get; set; } = new();
        public List<CatalogOption> Dice { get; set; } = new();
        public List<CatalogOption> Conditions { get; set; } = new();
    }

    public class TemplateCatalogReader
    {
        private readonly DatabaseManager _db;
        public TemplateCatalogReader(DatabaseManager db) => _db = db;

        public async Task<TemplateItemCatalogs> ReadAsync(string templateId, CancellationToken ct = default)
        {
            var cats = new TemplateItemCatalogs();

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
            if (string.IsNullOrEmpty(json)) return cats;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                cats.DamageTypes = ReadList(root, "DamageTypes");
                cats.WeaponProperties = ReadList(root, "WeaponTypes");
                cats.ArmorTypes = ReadList(root, "ArmorTypes");
                cats.Masteries = ReadList(root, "WeaponMasteries");
                cats.Dice = ReadDice(root);
                cats.Conditions = ReadList(root, "Conditions");
            }
            catch (Exception)
            {
            }

            return cats;
        }

        private static List<CatalogOption> ReadList(JsonElement root, string prop)
        {
            var list = new List<CatalogOption>();
            if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in arr.EnumerateArray())
            {
                var id = e.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                var nm = e.TryGetProperty("Name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(id))
                    list.Add(new CatalogOption(id!, nm ?? id!));
            }
            return list;
        }

        private static List<CatalogOption> ReadDice(JsonElement root)
        {
            var list = new List<CatalogOption>();
            if (!root.TryGetProperty("Dices", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in arr.EnumerateArray())
            {
                var id = e.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;

                string label = id!;
                if (e.TryGetProperty("Dice", out var dn))
                {
                    if (dn.ValueKind == JsonValueKind.Number && dn.TryGetInt32(out var sides))
                        label = "d" + sides;
                    else if (dn.ValueKind == JsonValueKind.String)
                        label = dn.GetString() ?? id!;
                }
                list.Add(new CatalogOption(id!, label));
            }
            return list;
        }
    }
}
