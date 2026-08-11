using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models.Database
{
    public sealed record ColumnSpec(string Name, string Type, bool NotNull, string? Default);

    public sealed record TableSpec(string Name, List<ColumnSpec> Columns, string CreateBody);

    public sealed class SchemaDiff
    {
        public List<string> MissingTables { get; } = new();
        public List<(string Table, ColumnSpec Column)> MissingColumns { get; } = new();
        public List<(string Table, string Column, string Live, string Wanted)> TypeChanges { get; } = new();
        public List<(string Table, string Column)> ExtraColumns { get; } = new();

        public bool HasChanges => MissingTables.Count > 0 || MissingColumns.Count > 0 || TypeChanges.Count > 0;

        public string Summary()
        {
            var sb = new StringBuilder();
            if (MissingTables.Count > 0) sb.Append(MissingTables.Count).Append(" table(s) to add. ");
            if (MissingColumns.Count > 0) sb.Append(MissingColumns.Count).Append(" column(s) to add. ");
            if (TypeChanges.Count > 0) sb.Append(TypeChanges.Count).Append(" column type(s) to rebuild. ");
            if (ExtraColumns.Count > 0) sb.Append(ExtraColumns.Count).Append(" column(s) kept that this version does not use. ");
            return sb.Length == 0 ? "nothing to do" : sb.ToString().TrimEnd();
        }
    }

    public static class SchemaReconciler
    {
        private static readonly Regex _tableRx = new(
            @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<body>.*?)\)\s*;",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _columnRx = new(
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+(?<type>[A-Za-z]+(?:\s*\(\s*\d+(?:\s*,\s*\d+)?\s*\))?)(?<rest>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] ConstraintStarts =
            { "PRIMARY", "FOREIGN", "UNIQUE", "CHECK", "CONSTRAINT" };

        public static Dictionary<string, TableSpec> Parse(string schemaSql)
        {
            var tables = new Dictionary<string, TableSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in _tableRx.Matches(schemaSql))
            {
                var name = m.Groups["name"].Value;
                var body = m.Groups["body"].Value;
                var cols = new List<ColumnSpec>();
                foreach (var raw in SplitTopLevel(body))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (ConstraintStarts.Any(c => line.StartsWith(c, StringComparison.OrdinalIgnoreCase))) continue;
                    var cm = _columnRx.Match(line);
                    if (!cm.Success) continue;
                    var rest = cm.Groups["rest"].Value;
                    var def = Regex.Match(rest, @"DEFAULT\s+(?<v>'(?:[^']|'')*'|[^\s,]+)", RegexOptions.IgnoreCase);
                    cols.Add(new ColumnSpec(
                        cm.Groups["name"].Value,
                        Normalise(cm.Groups["type"].Value),
                        Regex.IsMatch(rest, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase),
                        def.Success ? def.Groups["v"].Value : null));
                }
                if (cols.Count > 0) tables[name] = new TableSpec(name, cols, body);
            }
            return tables;
        }

        private static IEnumerable<string> SplitTopLevel(string body)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return body[start..i];
                    start = i + 1;
                }
            }
            if (start < body.Length) yield return body[start..];
        }

        public static string Affinity(string declared)
        {
            var t = (declared ?? "").ToUpperInvariant();
            if (t.Contains("INT")) return "INTEGER";
            if (t.Contains("CHAR") || t.Contains("CLOB") || t.Contains("TEXT")) return "TEXT";
            if (t.Contains("BLOB") || t.Length == 0) return "BLOB";
            if (t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB")) return "REAL";
            return "NUMERIC";
        }

        private static string Normalise(string t) => Regex.Replace(t ?? "", @"\s+", "").ToUpperInvariant();

        public static async Task<Dictionary<string, TableSpec>> ReadLiveAsync(SqliteConnection conn, CancellationToken ct = default)
        {
            var names = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) names.Add(r.GetString(0));
            }

            var live = new Dictionary<string, TableSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var cols = new List<ColumnSpec>();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info(\"{name.Replace("\"", "\"\"")}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    cols.Add(new ColumnSpec(
                        r.GetString(1),
                        Normalise(r.IsDBNull(2) ? "" : r.GetString(2)),
                        !r.IsDBNull(3) && r.GetInt32(3) == 1,
                        r.IsDBNull(4) ? null : r.GetValue(4)?.ToString()));
                }
                live[name] = new TableSpec(name, cols, "");
            }
            return live;
        }

        public static SchemaDiff Diff(Dictionary<string, TableSpec> expected, Dictionary<string, TableSpec> live)
        {
            var diff = new SchemaDiff();
            foreach (var (name, want) in expected)
            {
                if (!live.TryGetValue(name, out var have))
                {
                    diff.MissingTables.Add(name);
                    continue;
                }
                foreach (var col in want.Columns)
                {
                    var mine = have.Columns.FirstOrDefault(c => string.Equals(c.Name, col.Name, StringComparison.OrdinalIgnoreCase));
                    if (mine == null) { diff.MissingColumns.Add((name, col)); continue; }
                    if (!string.Equals(Affinity(mine.Type), Affinity(col.Type), StringComparison.Ordinal))
                        diff.TypeChanges.Add((name, col.Name, mine.Type, col.Type));
                }
                foreach (var col in have.Columns)
                    if (!want.Columns.Any(c => string.Equals(c.Name, col.Name, StringComparison.OrdinalIgnoreCase)))
                        diff.ExtraColumns.Add((name, col.Name));
            }
            return diff;
        }

        public static async Task ApplyAsync(SqliteConnection conn, Dictionary<string, TableSpec> expected, SchemaDiff diff, CancellationToken ct = default)
        {
            foreach (var (table, col) in diff.MissingColumns)
            {
                var sql = new StringBuilder($"ALTER TABLE \"{table}\" ADD COLUMN \"{col.Name}\" {col.Type}");
                if (col.NotNull) sql.Append(" NOT NULL DEFAULT ").Append(col.Default ?? DefaultFor(col.Type));
                else if (col.Default != null) sql.Append(" DEFAULT ").Append(col.Default);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql.ToString();
                await cmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var table in diff.TypeChanges.Select(t => t.Table).Distinct(StringComparer.OrdinalIgnoreCase))
                await RebuildTableAsync(conn, expected[table], ct);
        }

        private static string DefaultFor(string type) => Affinity(type) switch
        {
            "INTEGER" => "0",
            "REAL" => "0",
            "NUMERIC" => "0",
            "BLOB" => "x''",
            _ => "''"
        };

        private static async Task RebuildTableAsync(SqliteConnection conn, TableSpec want, CancellationToken ct)
        {
            var live = await ReadLiveAsync(conn, ct);
            if (!live.TryGetValue(want.Name, out var have)) return;

            var extras = have.Columns
                .Where(c => !want.Columns.Any(w => string.Equals(w.Name, c.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var tmp = want.Name + "__rebuild";

            var columnDefs = new List<string>();
            var constraintDefs = new List<string>();
            foreach (var piece in SplitTopLevel(want.CreateBody))
            {
                var line = piece.Trim();
                if (line.Length == 0) continue;
                if (ConstraintStarts.Any(c => line.StartsWith(c, StringComparison.OrdinalIgnoreCase))) constraintDefs.Add(line);
                else columnDefs.Add(line);
            }
            foreach (var e in extras)
                columnDefs.Add("\"" + e.Name + "\" " + (string.IsNullOrEmpty(e.Type) ? "TEXT" : e.Type));

            var body = string.Join(",\n    ", columnDefs.Concat(constraintDefs));

            var indexes = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name=$t AND sql IS NOT NULL;";
                cmd.Parameters.AddWithValue("$t", want.Name);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) indexes.Add(r.GetString(0));
            }

            var carried = want.Columns
                .Where(w => have.Columns.Any(h => string.Equals(h.Name, w.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(w => w.Name)
                .Concat(extras.Select(e => e.Name))
                .ToList();

            var quoted = string.Join(", ", carried.Select(c => "\"" + c + "\""));
            var selected = string.Join(", ", carried.Select(c =>
            {
                var w = want.Columns.FirstOrDefault(x => string.Equals(x.Name, c, StringComparison.OrdinalIgnoreCase));
                return w == null ? "\"" + c + "\"" : $"CAST(\"{c}\" AS {Affinity(w.Type)})";
            }));

            await Exec(conn, $"CREATE TABLE \"{tmp}\" ({body});", ct);
            await Exec(conn, $"INSERT INTO \"{tmp}\" ({quoted}) SELECT {selected} FROM \"{want.Name}\";", ct);
            await Exec(conn, $"DROP TABLE \"{want.Name}\";", ct);
            await Exec(conn, $"ALTER TABLE \"{tmp}\" RENAME TO \"{want.Name}\";", ct);

            foreach (var sql in indexes)
                await Exec(conn, sql.Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase)
                                    .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase) + ";", ct);
        }

        private static async Task Exec(SqliteConnection conn, string sql, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
