using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models.Database
{
    public static class CatalogResolver
    {
        public static readonly string[] Kinds = { "Items", "Spells", "Races", "Subraces", "Classes", "Traits" };

        public static string LineagePrefixOf(string? templateId) =>
            Regex.Replace(templateId ?? "", @"-v\d+$", "", RegexOptions.IgnoreCase) + "-v";

        private const string Rank = """
            CASE WHEN {0}.TemplateId = $tid THEN 0
                 WHEN substr({0}.TemplateId, 1, LENGTH($lineage)) = $lineage THEN 1
                 ELSE 2 END, LENGTH({0}.TemplateId) DESC, {0}.TemplateId DESC
            """;

        public static string ResolvedJsonSql(string kind, string alias, string fallbackColumn = "DataJson") =>
            $"COALESCE((SELECT e.DataJson FROM CatalogEntries e " +
            $"WHERE e.Kind = '{kind}' AND e.EntryId = {alias}.Id AND e.DataJson IS NOT NULL " +
            $"ORDER BY {string.Format(Rank, "e")} LIMIT 1), {alias}.{fallbackColumn})";

        public static void BindScope(SqliteCommand cmd, string? templateId)
        {
            cmd.Parameters.AddWithValue("$tid", templateId ?? "");
            cmd.Parameters.AddWithValue("$lineage", LineagePrefixOf(templateId));
        }

        public static async Task<string?> TemplateIdOfCampaignAsync(SqliteConnection conn, string campaignId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(campaignId)) return null;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TemplateId FROM Campaigns WHERE Id = $cid LIMIT 1";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            return await cmd.ExecuteScalarAsync(ct) as string;
        }

        public static async Task<string?> EntryJsonAsync(SqliteConnection conn, string kind, string entryId, string? templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entryId)) return null;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT e.DataJson FROM CatalogEntries e
                    WHERE e.Kind = $kind AND e.EntryId = $eid AND e.DataJson IS NOT NULL
                    ORDER BY {string.Format(Rank, "e")}
                    LIMIT 1
                    """;
                Bind(cmd, kind, templateId);
                cmd.Parameters.AddWithValue("$eid", entryId);
                if (await cmd.ExecuteScalarAsync(ct) is string hit && !string.IsNullOrWhiteSpace(hit)) return hit;
            }

            return await PoolJsonAsync(conn, kind, entryId, ct);
        }

        public static async Task<Dictionary<string, string>> EntryJsonForKindAsync(SqliteConnection conn, string kind, string? templateId, CancellationToken ct = default)
        {
            var map = new Dictionary<string, string>();

            if (HasPoolJson(kind))
            {
                await using var pool = conn.CreateCommand();
                pool.CommandText = $"SELECT Id, DataJson FROM {kind} WHERE DataJson IS NOT NULL";
                await using var pr = await pool.ExecuteReaderAsync(ct);
                while (await pr.ReadAsync(ct))
                    if (!pr.IsDBNull(0) && !pr.IsDBNull(1)) map[pr.GetString(0)] = pr.GetString(1);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT e.EntryId, e.DataJson FROM CatalogEntries e
                    WHERE e.Kind = $kind AND e.DataJson IS NOT NULL
                      AND e.TemplateId = (SELECT b.TemplateId FROM CatalogEntries b
                                          WHERE b.Kind = e.Kind AND b.EntryId = e.EntryId AND b.DataJson IS NOT NULL
                                          ORDER BY {string.Format(Rank, "b")}
                                          LIMIT 1)
                    """;
                Bind(cmd, kind, templateId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    if (!r.IsDBNull(0) && !r.IsDBNull(1)) map[r.GetString(0)] = r.GetString(1);
            }

            return map;
        }

        private static void Bind(SqliteCommand cmd, string kind, string? templateId)
        {
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$tid", templateId ?? "");
            cmd.Parameters.AddWithValue("$lineage", LineagePrefixOf(templateId));
        }

        private static bool HasPoolJson(string kind) => kind != "Traits";

        private static async Task<string?> PoolJsonAsync(SqliteConnection conn, string kind, string entryId, CancellationToken ct)
        {
            if (!HasPoolJson(kind)) return null;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT DataJson FROM {kind} WHERE Id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", entryId);
            return await cmd.ExecuteScalarAsync(ct) as string;
        }
    }
}
