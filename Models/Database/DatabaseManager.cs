using Microsoft.Data.Sqlite;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("Dujahit.Tests")]

namespace Dujahit.Models.Database
{
    /*
        Semi-crossplatform.... lol
    */
    public static class GlobalVariables
    {
        public static string AppName { get; set; } = "Dujahit";

        public static string? DataHomeOverride { get; set; }

        public static string AppDataLocal => Path.Combine(DataHomeOverride ?? ResolveDataHome(), AppName);

        private static string ResolveDataHome()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // NOT TESTED AND OTHER PARTS OF THE CODE IS NOT MADE FOR MAC YET
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "Library", "Application Support");

            // Linux, FreeBSD, etc. NOT TESTED AND OTHER PARTS OF THE CODE IS NOT MADE FOR LINUX YET
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "share")
                : xdg;
        }

        public static string? SafeChildPath(string parent, string name)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name)) return null;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (name.Trim().Trim('.').Length == 0) return null;

            string root, full;
            try
            {
                root = Path.GetFullPath(parent);
                full = Path.GetFullPath(Path.Combine(root, name));
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }

            var inside = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(inside, StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return null;

            return full;
        }

        private static readonly Dictionary<string, StreamGeometry> _geoCache = new();
        private static StreamGeometry Geo(string path)
        {
            if (!_geoCache.TryGetValue(path, out var g))
            {
                g = StreamGeometry.Parse(path);
                _geoCache[path] = g;
            }
            return g;
        }

        public const string IconDrawPath = "M3,17.25 V21 H6.75 L17.81,9.94 L14.06,6.19 L3,17.25 Z M20.71,7.04 C21.1,6.65 21.1,6.02 20.71,5.63 L18.37,3.29 C17.98,2.9 17.35,2.9 16.96,3.29 L15.13,5.12 L18.88,8.87 L20.71,7.04 Z";
        public const string IconTokenPath = "M12,3 C9.79,3 8,4.79 8,7 C8,9.21 9.79,11 12,11 C14.21,11 16,9.21 16,7 C16,4.79 14.21,3 12,3 Z M5,21 C5,16.58 8.13,13 12,13 C15.87,13 19,16.58 19,21 L5,21 Z";
        public const string IconPingPath = "F0 M2,12 A10,10 0 1,1 22,12 A10,10 0 1,1 2,12 Z M6,12 A6,6 0 1,1 18,12 A6,6 0 1,1 6,12 Z M9.5,12 A2.5,2.5 0 1,1 14.5,12 A2.5,2.5 0 1,1 9.5,12 Z";
        public const string IconUploadPath = "M12,3 L18.5,9.5 L14,9.5 L14,14 L10,14 L10,9.5 L5.5,9.5 L12,3 Z M5,17 L19,17 L19,20 L5,20 L5,17 Z";
        public const string IconRotateLeftPath = "M12.5,8 C9.85,8 7.45,8.99 5.6,10.6 L2,7 L2,16 L11,16 L7.38,12.38 C8.77,11.22 10.54,10.5 12.5,10.5 C16.04,10.5 19.05,12.81 20.1,16 L22.47,15.22 C21.08,11.03 17.15,8 12.5,8 Z";
        public const string IconRotateRightPath = "M18.4,10.6 C16.55,8.99 14.15,8 11.5,8 C6.85,8 2.92,11.03 1.54,15.22 L3.9,16 C4.95,12.81 7.96,10.5 11.5,10.5 C13.46,10.5 15.23,11.22 16.62,12.38 L13,16 L22,16 L22,7 L18.4,10.6 Z";
        public const string IconClosePath = "M6.4,4.99 L4.99,6.4 L10.59,12 L4.99,17.6 L6.4,19.01 L12,13.41 L17.6,19.01 L19.01,17.6 L13.41,12 L19.01,6.4 L17.6,4.99 L12,10.59 L6.4,4.99 Z";
        public const string IconChevronUpPath = "M7.41,15.41 L12,10.83 L16.59,15.41 L18,14 L12,8 L6,14 L7.41,15.41 Z";
        public const string IconChevronDownPath = "M7.41,8.59 L12,13.17 L16.59,8.59 L18,10 L12,16 L6,10 L7.41,8.59 Z";
        public const string IconLinkPath = "M3.9,12 C3.9,10.29 5.29,8.9 7,8.9 L11,8.9 L11,7 L7,7 C4.24,7 2,9.24 2,12 C2,14.76 4.24,17 7,17 L11,17 L11,15.1 L7,15.1 C5.29,15.1 3.9,13.71 3.9,12 Z M8,13 L16,13 L16,11 L8,11 L8,13 Z M17,7 L13,7 L13,8.9 L17,8.9 C18.71,8.9 20.1,10.29 20.1,12 C20.1,13.71 18.71,15.1 17,15.1 L13,15.1 L13,17 L17,17 C19.76,17 22,14.76 22,12 C22,9.24 19.76,7 17,7 Z";
        public const string IconPalettePath = "F0 M12,2 C6.49,2 2,6.49 2,12 C2,17.51 6.49,22 12,22 C12.93,22 13.7,21.25 13.7,20.31 C13.7,19.87 13.53,19.48 13.27,19.18 C13.02,18.88 12.85,18.5 12.85,18.07 C12.85,17.14 13.61,16.38 14.54,16.38 L16.54,16.38 C19.55,16.38 22,13.93 22,10.92 C22,5.99 17.51,2 12,2 Z M6.5,12 C5.67,12 5,11.33 5,10.5 C5,9.67 5.67,9 6.5,9 C7.33,9 8,9.67 8,10.5 C8,11.33 7.33,12 6.5,12 Z M9.5,8 C8.67,8 8,7.33 8,6.5 C8,5.67 8.67,5 9.5,5 C10.33,5 11,5.67 11,6.5 C11,7.33 10.33,8 9.5,8 Z M14.5,8 C13.67,8 13,7.33 13,6.5 C13,5.67 13.67,5 14.5,5 C15.33,5 16,5.67 16,6.5 C16,7.33 15.33,8 14.5,8 Z M17.5,12 C16.67,12 16,11.33 16,10.5 C16,9.67 16.67,9 17.5,9 C18.33,9 19,9.67 19,10.5 C19,11.33 18.33,12 17.5,12 Z";
        public const string IconTrashPath = "M6,19 C6,20.1 6.9,21 8,21 L16,21 C17.1,21 18,20.1 18,19 L18,7 L6,7 L6,19 Z M19,4 L15.5,4 L14.5,3 L9.5,3 L8.5,4 L5,4 L5,6 L19,6 L19,4 Z";
        public const string IconPlusPath = "M19,11 L13,11 L13,5 L11,5 L11,11 L5,11 L5,13 L11,13 L11,19 L13,19 L13,13 L19,13 L19,11 Z";
        public const string IconHighlightPath = "M12,2 L13.8,9.2 L21,11 L13.8,12.8 L12,20 L10.2,12.8 L3,11 L10.2,9.2 L12,2 Z";
        public const string IconAlignLeftPath = "M3,5 L21,5 L21,7 L3,7 Z M3,9 L15,9 L15,11 L3,11 Z M3,13 L21,13 L21,15 L3,15 Z M3,17 L15,17 L15,19 L3,19 Z";
        public const string IconAlignCenterPath = "M3,5 L21,5 L21,7 L3,7 Z M6,9 L18,9 L18,11 L6,11 Z M3,13 L21,13 L21,15 L3,15 Z M6,17 L18,17 L18,19 L6,19 Z";
        public const string IconAlignRightPath = "M3,5 L21,5 L21,7 L3,7 Z M9,9 L21,9 L21,11 L9,11 Z M3,13 L21,13 L21,15 L3,15 Z M9,17 L21,17 L21,19 L9,19 Z";
        public const string IconQuotePath = "M6,7 L11,7 L11,12 C11,15 9,17 6.5,17 L6.5,15 C8,15 9,14 9,12.5 L6,12.5 L6,7 Z M14,7 L19,7 L19,12 C19,15 17,17 14.5,17 L14.5,15 C16,15 17,14 17,12.5 L14,12.5 L14,7 Z";
        public const string IconFogPath = "M6.5,20 C4.01,20 2,17.99 2,15.5 C2,13.26 3.64,11.4 5.78,11.06 C6.34,8.72 8.44,7 11,7 C13.91,7 16.3,9.23 16.5,12.07 C18.49,12.27 20,13.95 20,16 C20,18.21 18.21,20 16,20 L6.5,20 Z";
        public const string IconWallPath = "M3,6 H21 V9 H3 Z M3,11 H10 V14 H3 Z M12,11 H21 V14 H12 Z M3,16 H21 V19 H3 Z";
        public const string IconDoorPath = "F0 M6,3 H18 V21 H6 Z M8,5 H16 V19 H8 Z M14,11 H16 V13 H14 Z";
        public const string IconTaskListPath = "F0 M19,3 L5,3 C3.9,3 3,3.9 3,5 L3,19 C3,20.1 3.9,21 5,21 L19,21 C20.1,21 21,20.1 21,19 L21,5 C21,3.9 20.1,3 19,3 Z M19,19 L5,19 L5,5 L19,5 L19,19 Z M17.99,9 L16.58,7.58 L10.5,13.67 L7.91,11.09 L6.5,12.5 L10.5,16.5 L17.99,9 Z";

        public const string IconImagePath = "F0 M21,19 L21,5 C21,3.9 20.1,3 19,3 L5,3 C3.9,3 3,3.9 3,5 L3,19 C3,20.1 3.9,21 5,21 L19,21 C20.1,21 21,20.1 21,19 Z M8.5,13.5 L11,16.51 L14.5,12 L19,18 L5,18 L8.5,13.5 Z";

        public const string IconSearchPath = "F0 M9.5,3 C13.09,3 16,5.91 16,9.5 C16,11.11 15.41,12.59 14.44,13.73 L14.71,14 L15.5,14 L20.5,19 L19,20.5 L14,15.5 L14,14.71 L13.73,14.44 C12.59,15.41 11.11,16 9.5,16 C5.91,16 3,13.09 3,9.5 C3,5.91 5.91,3 9.5,3 Z M9.5,5 C7,5 5,7 5,9.5 C5,12 7,14 9.5,14 C12,14 14,12 14,9.5 C14,7 12,5 9.5,5 Z";

        public const string IconHelpPath = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M11,17H13V15H11V17M12,6.5C10.5,6.5 9.3,7.4 9,8.7L10.8,9.2C10.9,8.7 11.4,8.3 12,8.3C12.7,8.3 13.3,8.9 13.3,9.6C13.3,11 11,11 11,13H13C13,11.5 15,11.3 15,9.5C15,7.9 13.7,6.5 12,6.5Z";
        public const string IconGearPath = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z";

        public static StreamGeometry IconHelp => Geo(IconHelpPath);
        public static StreamGeometry IconGear => Geo(IconGearPath);
        public static StreamGeometry IconDraw => Geo(IconDrawPath);
        public static StreamGeometry IconToken => Geo(IconTokenPath);
        public static StreamGeometry IconPing => Geo(IconPingPath);
        public static StreamGeometry IconUpload => Geo(IconUploadPath);
        public static StreamGeometry IconRotateLeft => Geo(IconRotateLeftPath);
        public static StreamGeometry IconRotateRight => Geo(IconRotateRightPath);
        public static StreamGeometry IconClose => Geo(IconClosePath);
        public static StreamGeometry IconChevronUp => Geo(IconChevronUpPath);
        public static StreamGeometry IconChevronDown => Geo(IconChevronDownPath);
        public static StreamGeometry IconLink => Geo(IconLinkPath);
        public static StreamGeometry IconPalette => Geo(IconPalettePath);
        public static StreamGeometry IconTrash => Geo(IconTrashPath);
        public static StreamGeometry IconPlus => Geo(IconPlusPath);
        public static StreamGeometry IconHighlight => Geo(IconHighlightPath);
        public static StreamGeometry IconAlignLeft => Geo(IconAlignLeftPath);
        public static StreamGeometry IconAlignCenter => Geo(IconAlignCenterPath);
        public static StreamGeometry IconAlignRight => Geo(IconAlignRightPath);
        public static StreamGeometry IconQuote => Geo(IconQuotePath);
        public static StreamGeometry IconFog => Geo(IconFogPath);
        public static StreamGeometry IconWall => Geo(IconWallPath);
        public static StreamGeometry IconDoor => Geo(IconDoorPath);
        public static StreamGeometry IconImage => Geo(IconImagePath);
        public static StreamGeometry IconTaskList => Geo(IconTaskListPath);
        public static StreamGeometry IconSearch => Geo(IconSearchPath);
    }

    public class ActiveCampaignContext
    {
        public string CampaignId { get; set; } = "";
    }

    /*
        I need to make a backup system here also that clones the db file, especially when running updates
    */
    public class DatabaseManager
    {
        public string DatabasePath { get; }
        public string ConnectionString { get; }

        public DatabaseManager(string? dbPath = null)
        {
            DatabasePath = dbPath ?? Path.Combine(
                GlobalVariables.AppDataLocal,
                GlobalVariables.AppName.ToLowerInvariant() + ".db");

            ConnectionString = $"Data Source={DatabasePath};Foreign Keys=True";
        }

        private const int SchemaVersion = 3;

        public string BackupsDirectory => Path.Combine(Path.GetDirectoryName(DatabasePath) ?? GlobalVariables.AppDataLocal, "backups");
        private string PendingRestorePath => DatabasePath + ".pending";

        public string AppVersion { get; set; } = "";

        public string? LastBackupPath { get; private set; }
        public string? UpgradeError { get; private set; }
        public string? UpgradeNote { get; private set; }

        private const string AppVersionKey = "db_app_version";

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            var dir = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            ApplyPendingRestoreIfAny();

            await using var conn = new SqliteConnection(ConnectionString);
            await conn.OpenAsync(ct);

            await ApplyPragmasAsync(conn, ct);

            int dbVersion = await ReadUserVersionAsync(conn, ct);
            bool hasData = await HasAnyTableAsync(conn, ct);
            string knownVersion = await ReadAppVersionAsync(conn, ct);

            var expected = SchemaReconciler.Parse(SchemaSql);
            var before = await SchemaReconciler.ReadLiveAsync(conn, ct);
            var diff = SchemaReconciler.Diff(expected, before);

            bool upgrading = hasData &&
                (dbVersion < SchemaVersion
                 || diff.HasChanges
                 || !string.Equals(knownVersion, AppVersion, StringComparison.Ordinal));

            if (upgrading)
            {
                LastBackupPath = await SnapshotBeforeMigrateAsync(conn, dbVersion, ct);
                UpgradeNote = diff.Summary();
            }

            await ApplySchemaAsync(conn, ct);
            await EnsureAddedColumnsAsync(conn, ct);
            await ReconcileSchemaAsync(conn, expected, ct);
            await RunMigrationsAsync(conn, dbVersion, Migrations, ct);

            await BackfillVersionsFromDataJsonAsync(conn, ct);
            await BackfillCatalogEntriesAsync(conn, ct);
            await TemplateLoader.BackfillChoicesAndCurrenciesAsync(conn, ct);

            if (dbVersion != SchemaVersion)
                await SetUserVersionAsync(conn, SchemaVersion, ct);
            await SetAppVersionAsync(conn, AppVersion, ct);

            Debug.WriteLine($"[DB] DatabasePath = {DatabasePath}");
        }

        private async Task ReconcileSchemaAsync(SqliteConnection conn, Dictionary<string, TableSpec> expected, CancellationToken ct)
        {
            var live = await SchemaReconciler.ReadLiveAsync(conn, ct);
            var diff = SchemaReconciler.Diff(expected, live);
            if (!diff.HasChanges) return;

            await Exec(conn, "PRAGMA foreign_keys = OFF;", ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await SchemaReconciler.ApplyAsync(conn, expected, diff, ct);
                await tx.CommitAsync(ct);
                ErrorLog.Log("Database upgraded in place, " + diff.Summary());
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                UpgradeError = "Your data was left exactly as it was and nothing has been changed."
                    + (LastBackupPath != null ? " A copy is at " + LastBackupPath + "." : "")
                    + " The database could not be brought up to date automatically, so it needs doing by hand before this version will run properly. What went wrong: " + ex.Message;
                ErrorLog.Log("Schema reconcile failed and was rolled back. " + UpgradeError, ex);
            }
            finally
            {
                await Exec(conn, "PRAGMA foreign_keys = ON;", ct);
            }
        }

        private static async Task Exec(SqliteConnection conn, string sql, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<string> ReadAppVersionAsync(SqliteConnection conn, CancellationToken ct)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $k LIMIT 1;";
                cmd.Parameters.AddWithValue("$k", AppVersionKey);
                return (await cmd.ExecuteScalarAsync(ct)) as string ?? "";
            }
            catch (SqliteException) { return ""; }
        }

        private static async Task SetAppVersionAsync(SqliteConnection conn, string version, CancellationToken ct)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO AppSettings (Key, Value) VALUES ($k, $v)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                    """;
                cmd.Parameters.AddWithValue("$k", AppVersionKey);
                cmd.Parameters.AddWithValue("$v", version ?? "");
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqliteException ex) { ErrorLog.Log("Stamping the db app version failed", ex); }
        }

        private static async Task<int> ReadUserVersionAsync(SqliteConnection conn, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            var v = await cmd.ExecuteScalarAsync(ct);
            return v == null ? 0 : Convert.ToInt32(v);
        }

        private static async Task SetUserVersionAsync(SqliteConnection conn, int version, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {version};";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public sealed record SchemaMigration(int Version, string Name, Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> Apply);

        // Ordered (ascending Version) steps for the non-additive changes EnsureColumnAsync can't express (rename, drop, backfill, type change), each runs once in its own transaction when the db is below its Version and must be safe to re-run. Empty until the first such change.
        private static readonly SchemaMigration[] Migrations =
        {
            new(3, "ClassChoices and Currencies keyed per rulebook", RekeyPerTemplateAsync),
        };

        private static async Task RekeyPerTemplateAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken ct)
        {
            foreach (var (table, body) in new[]
            {
                ("ClassChoices", @"
                    Id TEXT NOT NULL,
                    TemplateId TEXT NOT NULL,
                    ClassId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    Kind TEXT NOT NULL,
                    StoreAs TEXT NOT NULL,
                    ChooseCount INTEGER NOT NULL DEFAULT 1,
                    Label TEXT NOT NULL,
                    Description TEXT,
                    OptionsJson TEXT NOT NULL DEFAULT '[]',
                    PRIMARY KEY (TemplateId, Id)"),
                ("Currencies", @"
                    Id TEXT NOT NULL,
                    TemplateId TEXT NOT NULL DEFAULT '',
                    Name TEXT NOT NULL,
                    Abbreviation TEXT NOT NULL,
                    IsBase INTEGER NOT NULL DEFAULT 0,
                    EqualToBase INTEGER NOT NULL DEFAULT 1,
                    Color TEXT,
                    IconSvg TEXT,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (TemplateId, Id)"),
            })
            {
                var columns = await ReadColumnNamesAsync(conn, tx, table, ct);
                if (columns.Count == 0) continue;

                var list = string.Join(", ", columns);
                foreach (var sql in new[]
                {
                    $"CREATE TABLE {table}_rekeyed ({body});",
                    $"INSERT OR IGNORE INTO {table}_rekeyed ({list}) SELECT {list} FROM {table} WHERE TemplateId IS NOT NULL;",
                    $"DROP TABLE {table};",
                    $"ALTER TABLE {table}_rekeyed RENAME TO {table};",
                })
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }

            await using (var idx = conn.CreateCommand())
            {
                idx.Transaction = tx;
                idx.CommandText = "CREATE INDEX IF NOT EXISTS idx_classchoices_lookup ON ClassChoices(ClassId, Level);";
                await idx.ExecuteNonQueryAsync(ct);
            }
        }

        private static async Task<List<string>> ReadColumnNamesAsync(SqliteConnection conn, SqliteTransaction? tx, string table, CancellationToken ct)
        {
            var names = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA table_info({table});";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) names.Add(r.GetString(1));
            return names;
        }

        internal static async Task RunMigrationsAsync(SqliteConnection conn, int fromVersion, IEnumerable<SchemaMigration> migrations, CancellationToken ct)
        {
            foreach (var m in migrations)
            {
                if (fromVersion >= m.Version) continue;
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
                try
                {
                    await m.Apply(conn, tx, ct);
                    await tx.CommitAsync(ct);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    ErrorLog.Log("Schema migration to v" + m.Version + " (" + m.Name + ") failed, rolled back", ex);
                    throw;
                }
                await SetUserVersionAsync(conn, m.Version, ct);
            }
        }

        private static async Task BackfillCatalogEntriesAsync(SqliteConnection conn, CancellationToken ct)
        {
            foreach (var kind in CatalogResolver.Kinds)
            {
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $@"
                        INSERT OR IGNORE INTO CatalogEntries (TemplateId, Kind, EntryId, Name, ItemType, Version, DataJson, UpdatedAt)
                        SELECT t.TemplateId, $kind,
                               json_extract(v.value, '$.TemplateId'),
                               COALESCE(json_extract(v.value, '$.Name'), ''),
                               json_extract(v.value, '$.""$type""'),
                               json_extract(v.value, '$.Version'),
                               v.value,
                               COALESCE(t.ImportedAt, '')
                        FROM (SELECT TemplateId, JsonContent, ImportedAt FROM CampaignTemplates
                              WHERE JsonContent IS NOT NULL AND json_valid(JsonContent)
                                AND json_type(JsonContent, '$.{kind}') = 'array'
                                AND NOT EXISTS (SELECT 1 FROM CatalogEntries c
                                                WHERE c.TemplateId = CampaignTemplates.TemplateId AND c.Kind = $kind)) t,
                             json_each(t.JsonContent, '$.{kind}') v
                        WHERE json_type(v.value) = 'object'
                          AND json_extract(v.value, '$.TemplateId') IS NOT NULL
                          AND TRIM(json_extract(v.value, '$.TemplateId')) <> ''";
                    cmd.Parameters.AddWithValue("$kind", kind);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (SqliteException ex) { ErrorLog.Log("Backfilling CatalogEntries for " + kind + " failed", ex); }
            }
        }

        private static async Task BackfillVersionsFromDataJsonAsync(SqliteConnection conn, CancellationToken ct)
        {
            foreach (var table in new[] { "Items", "Spells", "Races", "Subraces", "Classes" })
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    UPDATE {table}
                    SET Version = json_extract(DataJson, '$.Version')
                    WHERE DataJson IS NOT NULL
                      AND json_valid(DataJson)
                      AND json_extract(DataJson, '$.Version') IS NOT NULL
                      AND Version <> json_extract(DataJson, '$.Version');";
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        private static async Task<bool> HasAnyTableAsync(SqliteConnection conn, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            var n = await cmd.ExecuteScalarAsync(ct);
            return n != null && Convert.ToInt32(n) > 0;
        }

        private async Task<string?> SnapshotBeforeMigrateAsync(SqliteConnection conn, int fromVersion, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(BackupsDirectory);
                var name = Path.GetFileNameWithoutExtension(DatabasePath)
                           + "-premigrate-v" + fromVersion + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db";
                var dest = Path.Combine(BackupsDirectory, name);
                var safe = dest.Replace("'", "''");
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"VACUUM INTO '{safe}';";
                await cmd.ExecuteNonQueryAsync(ct);
                PruneBackups(12);
                ErrorLog.Log("Pre-migration backup written to " + dest);
                return dest;
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Pre-migration backup FAILED, continuing without it", ex);
                return null;
            }
        }

        private void PruneBackups(int keep)
        {
            try
            {
                var files = new DirectoryInfo(BackupsDirectory).GetFiles("*-premigrate-*.db");
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = keep; i < files.Length; i++) files[i].Delete();
            }
            catch { }
        }

        public static string? DescribeRestoreCandidate(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return "That file is not there any more.";

            try
            {
                // Pooling off, a pooled connection keeps the file open after Dispose and the restore then cannot move it
                using var conn = new SqliteConnection("Data Source=" + sourcePath + ";Mode=ReadOnly;Pooling=False");
                conn.Open();

                using var check = conn.CreateCommand();
                check.CommandText = "PRAGMA quick_check;";
                if (check.ExecuteScalar() as string != "ok")
                    return "That database is damaged, so it would not be safe to restore from.";

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Campaigns','Characters','CampaignTemplates');";
                if (Convert.ToInt32(cmd.ExecuteScalar()) < 3)
                    return "That is a database but it is not a Dujahit one, it has no campaigns in it.";
            }
            catch (SqliteException) { return "That file is not a database Dujahit can read."; }
            catch (IOException) { return "That file could not be opened."; }

            return null;
        }

        public string? StagePendingRestore(string sourcePath)
        {
            var problem = DescribeRestoreCandidate(sourcePath);
            if (problem != null) return problem;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PendingRestorePath)!);
                File.Copy(sourcePath, PendingRestorePath, overwrite: true);
                return null;
            }
            catch (IOException ex) { return "The backup could not be staged, " + ex.Message; }
            catch (UnauthorizedAccessException ex) { return "The backup could not be staged, " + ex.Message; }
        }

        private void SnapshotBeforeRestore()
        {
            if (!File.Exists(DatabasePath)) return;

            try
            {
                Directory.CreateDirectory(BackupsDirectory);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var name = Path.GetFileNameWithoutExtension(DatabasePath) + "-beforerestore-" + stamp;

                File.Copy(DatabasePath, Path.Combine(BackupsDirectory, name + ".db"), overwrite: true);
                foreach (var side in new[] { "-wal", "-shm" })
                    if (File.Exists(DatabasePath + side))
                        File.Copy(DatabasePath + side, Path.Combine(BackupsDirectory, name + ".db" + side), overwrite: true);
            }
            catch (Exception ex) { ErrorLog.Log("Snapshotting the database before a restore failed", ex); }
        }

        private void ApplyPendingRestoreIfAny()
        {
            var aside = DatabasePath + ".replaced";

            try
            {
                if (!File.Exists(PendingRestorePath)) return;

                if (DescribeRestoreCandidate(PendingRestorePath) is string problem)
                {
                    ErrorLog.Log("Refused a staged restore, " + problem);
                    try { File.Delete(PendingRestorePath); } catch (IOException) { }
                    return;
                }

                SnapshotBeforeRestore();

                // The live db is moved out of the way rather than deleted, so a failed swap can put it straight back
                if (File.Exists(aside)) File.Delete(aside);
                if (File.Exists(DatabasePath)) File.Move(DatabasePath, aside);

                // The old side files belong to the db that just moved aside, pairing them with the incoming one would corrupt it
                foreach (var side in new[] { DatabasePath + "-wal", DatabasePath + "-shm" })
                    if (File.Exists(side)) File.Delete(side);

                try
                {
                    File.Move(PendingRestorePath, DatabasePath);
                }
                catch
                {
                    if (File.Exists(aside) && !File.Exists(DatabasePath)) File.Move(aside, DatabasePath);
                    throw;
                }

                try { File.Delete(aside); } catch (IOException) { }
                ErrorLog.Log("Restored database from a staged backup at launch");
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Pending restore failed", ex);
            }
        }

        public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
        {
            var conn = new SqliteConnection(ConnectionString);
            await conn.OpenAsync(ct);

            await using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY;";
                await pragma.ExecuteNonQueryAsync(ct);
            }

            return conn;
        }

        private static async Task ApplyPragmasAsync(SqliteConnection conn, CancellationToken ct)
        {
            foreach (var pragma in new[]
            {
                "PRAGMA journal_mode = WAL;",
                "PRAGMA synchronous = NORMAL;",
                "PRAGMA foreign_keys = ON;",
                "PRAGMA temp_store = MEMORY;"
            })
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = pragma;
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        private static async Task ApplySchemaAsync(SqliteConnection conn, CancellationToken ct)
        {
            Debug.WriteLine($"[SCHEMA] DB path: {conn.DataSource}");
            Debug.WriteLine($"[SCHEMA] Schema SQL length: {SchemaSql.Length}");

            await using (var debugCmd = conn.CreateCommand())
            {
                debugCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
                await using var r = await debugCmd.ExecuteReaderAsync(ct);
                var existing = new List<string>();
                while (await r.ReadAsync(ct)) existing.Add(r.GetString(0));
                Debug.WriteLine($"[SCHEMA] Tables BEFORE: [{string.Join(", ", existing)}]");
            }

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = SchemaSql;
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
                Debug.WriteLine("[SCHEMA] Schema applied successfully.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                ErrorLog.Log($"[SCHEMA] FAILED", ex);
                throw;
            }

            await using (var debugCmd = conn.CreateCommand())
            {
                debugCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
                await using var r = await debugCmd.ExecuteReaderAsync(ct);
                var existing = new List<string>();
                while (await r.ReadAsync(ct)) existing.Add(r.GetString(0));
                Debug.WriteLine($"[SCHEMA] Tables AFTER: [{string.Join(", ", existing)}]");
            }
        }

        private static async Task EnsureAddedColumnsAsync(SqliteConnection conn, CancellationToken ct)
        {
            await EnsureColumnAsync(conn, ct, "Items", "Slug", "TEXT");
            await EnsureColumnAsync(conn, ct, "Items", "Tags", "TEXT NOT NULL DEFAULT '[]'");
            await EnsureColumnAsync(conn, ct, "Characters", "CharacterKind", "TEXT NOT NULL DEFAULT 'pc'");
            await EnsureColumnAsync(conn, ct, "Characters", "Slug", "TEXT");
            await EnsureColumnAsync(conn, ct, "Characters", "Tags", "TEXT NOT NULL DEFAULT '[]'");
            await EnsureColumnAsync(conn, ct, "Characters", "VisibleToAll", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "Characters", "ClassLevelsJson", "TEXT");
            await EnsureColumnAsync(conn, ct, "ItemInstances", "ParentInstanceId", "TEXT");
            await EnsureColumnAsync(conn, ct, "MapTokens", "Scale", "REAL NOT NULL DEFAULT 1");
            await EnsureColumnAsync(conn, ct, "MapTokens", "Rotation", "REAL NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "MapTokens", "SizeName", "TEXT NOT NULL DEFAULT 'Medium'");
            await EnsureColumnAsync(conn, ct, "CampaignTokenLibrary", "InitiativeOverride", "INTEGER");
            await EnsureColumnAsync(conn, ct, "Campaigns", "CombatSettingsJson", "TEXT");
            await EnsureColumnAsync(conn, ct, "Campaigns", "ElapsedMinutes", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "MaxActions", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "ActionsRemaining", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "MaxBonusActions", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "BonusActionsRemaining", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "SpellSlotsJson", "TEXT");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "Concentration", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "DeathSaveSuccesses", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "DeathSaveFailures", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "AttacksJson", "TEXT");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "IsFriendly", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "EncounterCombatants", "ExtrasJson", "TEXT");
            await EnsureColumnAsync(conn, ct, "Maps", "PlayerVisible", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "Maps", "WallsEnabled", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(conn, ct, "Maps", "GridOffsetX", "REAL NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "Maps", "GridOffsetY", "REAL NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "Maps", "WallsJson", "TEXT NOT NULL DEFAULT '[]'");
            await EnsureColumnAsync(conn, ct, "Maps", "DifficultTerrainJson", "TEXT NOT NULL DEFAULT '[]'");
            await EnsureColumnAsync(conn, ct, "Maps", "MapObjectsJson", "TEXT NOT NULL DEFAULT '[]'");
            await EnsureColumnAsync(conn, ct, "Maps", "AoeTemplatesJson", "TEXT NOT NULL DEFAULT '[]'");
            await EnsureColumnAsync(conn, ct, "TradeLog", "TradeId", "TEXT");
            await EnsureColumnAsync(conn, ct, "Campaigns", "JoinSecret", "TEXT");
            await EnsureColumnAsync(conn, ct, "JoinedCampaigns", "JoinCode", "TEXT");
            await EnsureColumnAsync(conn, ct, "Campaigns", "RulesVersion", "TEXT NOT NULL DEFAULT 'both'");
            await EnsureColumnAsync(conn, ct, "Items", "Version", "TEXT NOT NULL DEFAULT '2014'");
            await EnsureColumnAsync(conn, ct, "Spells", "Version", "TEXT NOT NULL DEFAULT '2014'");
            await EnsureColumnAsync(conn, ct, "Races", "Version", "TEXT NOT NULL DEFAULT '2014'");
            await EnsureColumnAsync(conn, ct, "Subraces", "Version", "TEXT NOT NULL DEFAULT '2014'");
            await EnsureColumnAsync(conn, ct, "Classes", "Version", "TEXT NOT NULL DEFAULT '2014'");
            await EnsureColumnAsync(conn, ct, "Traits", "Version", "TEXT NOT NULL DEFAULT '2014'");
            await EnsureColumnAsync(conn, ct, "MindmapLinks", "RelationType", "TEXT NOT NULL DEFAULT ''");
            await EnsureColumnAsync(conn, ct, "Themes", "Muted", "TEXT NOT NULL DEFAULT '#8A8A99'");
            await EnsureColumnAsync(conn, ct, "MapTokens", "IsProp", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "MapTokens", "Blocks", "INTEGER NOT NULL DEFAULT 1");
            await EnsureColumnAsync(conn, ct, "MapTokens", "BlocksSight", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "NotePages", "CrdtState", "BLOB");
            await EnsureColumnAsync(conn, ct, "MapFog", "DynamicVision", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "MapFog", "ClosesBehind", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync(conn, ct, "MapFog", "SeenCells", "TEXT NOT NULL DEFAULT ''");
        }

        private static async Task EnsureColumnAsync(SqliteConnection conn, CancellationToken ct, string table, string column, string definition)
        {
            bool exists = false;
            await using (var check = conn.CreateCommand())
            {
                check.CommandText = $"PRAGMA table_info({table});";
                await using var r = await check.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }
            if (exists) return;

            await using var add = conn.CreateCommand();
            add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            await add.ExecuteNonQueryAsync(ct);
            Debug.WriteLine($"[SCHEMA] added missing column {table}.{column}");
        }

        // DB schema, maybe put this in the .db file later, having here for easy ctrl + f lookups while debugging and creating functions
        internal const string SchemaSql = """
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS LocalUsers (
                UserId TEXT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS Themes(
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                Background TEXT NOT NULL,
                Foreground TEXT NOT NULL,
                Widget TEXT NOT NULL,
                WidgetForeground TEXT NOT NULL,
                AccentColor TEXT NOT NULL,
                AccentHover TEXT NOT NULL,
                Divider TEXT NOT NULL,
                Danger TEXT NOT NULL,
                Muted TEXT NOT NULL DEFAULT '#8A8A99'
            );

            CREATE TABLE IF NOT EXISTS CampaignTemplates (
                TemplateId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                SystemId TEXT NOT NULL,
                Version INTEGER NOT NULL DEFAULT 1,
                ImportedAt TEXT NOT NULL,
                JsonContent TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Campaigns (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                Name TEXT NOT NULL,
                TemplateId TEXT NOT NULL,
                Description TEXT,
                CreatedAt TEXT NOT NULL,
                LastModified TEXT,
                Port TEXT NOT NULL,
                JoinSecret TEXT,
                FOREIGN KEY (TemplateId) REFERENCES CampaignTemplates(TemplateId),
                FOREIGN KEY (UserId)     REFERENCES Users(Id)
            );
            CREATE INDEX IF NOT EXISTS idx_campaigns_user ON Campaigns(UserId);

            CREATE TABLE IF NOT EXISTS CampaignMembers (
                CampaignId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                Role TEXT NOT NULL,
                CharacterId TEXT,
                JoinedAt TEXT NOT NULL,
                PRIMARY KEY (CampaignId, UserId),
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId)     REFERENCES Users(Id)     ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_members_user ON CampaignMembers(UserId);

            CREATE TABLE IF NOT EXISTS JoinedCampaigns (
                UserId TEXT NOT NULL,
                CampaignId TEXT NOT NULL,
                CampaignName TEXT,
                HostAddress TEXT NOT NULL,
                LastJoinedAt TEXT,
                JoinCode TEXT,
                PRIMARY KEY (UserId, CampaignId)
            );

            CREATE TABLE IF NOT EXISTS PrimaryCharacters (
                UserId TEXT NOT NULL,
                CampaignId TEXT NOT NULL,
                CharacterId TEXT NOT NULL,
                PRIMARY KEY (UserId, CampaignId)
            );

            CREATE TABLE IF NOT EXISTS Items (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ItemType TEXT NOT NULL,
                Source TEXT NOT NULL,
                OwnerUserId TEXT, 
                TemplateId TEXT,
                RevisionNumber INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL,
                DataJson TEXT NOT NULL,
                Slug TEXT,
                Tags TEXT NOT NULL DEFAULT '[]',
                FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE SET NULL,
                FOREIGN KEY (TemplateId)  REFERENCES CampaignTemplates(TemplateId)
            );
            CREATE INDEX IF NOT EXISTS idx_items_type ON Items(ItemType);
            CREATE INDEX IF NOT EXISTS idx_items_slug ON Items(Slug);

            CREATE TABLE IF NOT EXISTS Spells (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Level INTEGER NOT NULL,
                School TEXT NOT NULL,
                CastingTime TEXT NOT NULL,
                Duration TEXT NOT NULL,
                Range TEXT NOT NULL,
                Concentration INTEGER NOT NULL,
                Ritual INTEGER NOT NULL,
                Description TEXT NOT NULL,
                Source TEXT NOT NULL,
                OwnerUserId TEXT,
                TemplateId TEXT,
                RevisionNumber INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL,
                DataJson TEXT NOT NULL,
                FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE SET NULL,
                FOREIGN KEY (TemplateId)  REFERENCES CampaignTemplates(TemplateId)
            );
            CREATE INDEX IF NOT EXISTS idx_spells_level  ON Spells(Level);
            CREATE INDEX IF NOT EXISTS idx_spells_school ON Spells(School);

            CREATE TABLE IF NOT EXISTS Races (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                Size TEXT NOT NULL,
                Speed INTEGER NOT NULL,
                Source TEXT NOT NULL,
                OwnerUserId TEXT,
                TemplateId TEXT,
                RevisionNumber INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL,
                DataJson TEXT NOT NULL,
                FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE SET NULL,
                FOREIGN KEY (TemplateId)  REFERENCES CampaignTemplates(TemplateId)
            );

                CREATE TABLE IF NOT EXISTS Subraces (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ParentRaceId TEXT NOT NULL,
                Description TEXT,
                Source TEXT NOT NULL,
                OwnerUserId TEXT,
                TemplateId TEXT,
                RevisionNumber INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL,
                DataJson TEXT NOT NULL,
                FOREIGN KEY (ParentRaceId) REFERENCES Races(Id) ON DELETE CASCADE,
                FOREIGN KEY (TemplateId) REFERENCES CampaignTemplates(TemplateId)
            );

            CREATE TABLE IF NOT EXISTS Classes (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                HitDiceId TEXT NOT NULL,
                PrimaryAbility TEXT NOT NULL,
                Source TEXT NOT NULL,
                OwnerUserId TEXT,
                TemplateId TEXT,
                RevisionNumber INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL,
                DataJson TEXT NOT NULL,
                FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE SET NULL,
                FOREIGN KEY (TemplateId)  REFERENCES CampaignTemplates(TemplateId)
            );

            CREATE TABLE IF NOT EXISTS Traits (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                Source TEXT NOT NULL,
                OwnerUserId TEXT,
                TemplateId TEXT,
                RevisionNumber INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (OwnerUserId) REFERENCES Users(Id) ON DELETE SET NULL,
                FOREIGN KEY (TemplateId)  REFERENCES CampaignTemplates(TemplateId)
            );

            CREATE TABLE IF NOT EXISTS CatalogEntries (
                TemplateId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                EntryId TEXT NOT NULL,
                Name TEXT NOT NULL DEFAULT '',
                ItemType TEXT,
                Version TEXT,
                DataJson TEXT,
                UpdatedAt TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (TemplateId, Kind, EntryId)
            );

            CREATE INDEX IF NOT EXISTS IX_CatalogEntries_Lookup ON CatalogEntries (Kind, EntryId);

            CREATE TABLE IF NOT EXISTS CampaignItems (
                CampaignId TEXT NOT NULL,
                ItemId TEXT NOT NULL,
                AddedAt TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (CampaignId, ItemId),
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (ItemId)     REFERENCES Items(Id)     ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CampaignSpells (
                CampaignId TEXT NOT NULL,
                SpellId TEXT NOT NULL,
                AddedAt TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (CampaignId, SpellId),
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (SpellId)    REFERENCES Spells(Id)    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CampaignRaces (
                CampaignId TEXT NOT NULL,
                RaceId TEXT NOT NULL,
                AddedAt TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (CampaignId, RaceId),
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (RaceId)     REFERENCES Races(Id)     ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CampaignClasses (
                CampaignId TEXT NOT NULL,
                ClassId TEXT NOT NULL,
                AddedAt TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (CampaignId, ClassId),
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (ClassId)    REFERENCES Classes(Id)   ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CampaignTraits (
                CampaignId TEXT NOT NULL,
                TraitId TEXT NOT NULL,
                AddedAt TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                PRIMARY KEY (CampaignId, TraitId),
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (TraitId)    REFERENCES Traits(Id)    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Characters (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                OwnerUserId TEXT,
                Name TEXT NOT NULL,
                RaceId TEXT,
                SubraceId TEXT,
                ClassId TEXT,
                Level INTEGER NOT NULL DEFAULT 1,
                CurrentHp INTEGER NOT NULL DEFAULT 0,
                MaxHp INTEGER NOT NULL DEFAULT 0,
                AbilityScoresJson TEXT NOT NULL,
                InventoryJson TEXT,
                StateJson TEXT,
                CharacterKind TEXT NOT NULL DEFAULT 'pc',
                Slug TEXT,
                Tags TEXT NOT NULL DEFAULT '[]',
                VisibleToAll INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId)  REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (OwnerUserId) REFERENCES Users(Id)      ON DELETE SET NULL,
                FOREIGN KEY (RaceId)      REFERENCES Races(Id),
                FOREIGN KEY (ClassId)     REFERENCES Classes(Id)
            );
            CREATE INDEX IF NOT EXISTS idx_characters_campaign ON Characters(CampaignId);
            CREATE INDEX IF NOT EXISTS idx_characters_owner    ON Characters(OwnerUserId);
            CREATE INDEX IF NOT EXISTS idx_characters_kind_slug ON Characters(CampaignId, CharacterKind, Slug);

            CREATE TABLE IF NOT EXISTS ItemInstances (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                BaseItemId TEXT NOT NULL,
                OwnerCharacterId TEXT,
                Quantity INTEGER NOT NULL DEFAULT 1,
                CustomName TEXT,
                ParentInstanceId TEXT,
                StateJson TEXT,
                FOREIGN KEY (CampaignId)       REFERENCES Campaigns(Id)   ON DELETE CASCADE,
                FOREIGN KEY (BaseItemId)       REFERENCES Items(Id),
                FOREIGN KEY (ParentInstanceId) REFERENCES ItemInstances(Id) ON DELETE SET NULL,
                FOREIGN KEY (OwnerCharacterId) REFERENCES Characters(Id)  ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS idx_iteminstances_campaign ON ItemInstances(CampaignId);
            CREATE INDEX IF NOT EXISTS idx_iteminstances_owner    ON ItemInstances(CampaignId, OwnerCharacterId);
            CREATE INDEX IF NOT EXISTS idx_iteminstances_parent   ON ItemInstances(ParentInstanceId);

            CREATE TABLE IF NOT EXISTS Levels (
                Id TEXT PRIMARY KEY,
                LevelValue INTEGER NOT NULL,
                XP INTEGER NOT NULL,
                Bonus INTEGER NOT NULL,
                CampaignId TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS NotePages (
                Id              TEXT PRIMARY KEY,
                CampaignId      TEXT NOT NULL,
                OwnerUserId     TEXT,
                ParentPageId    TEXT,
                Scope           TEXT NOT NULL,
                Title           TEXT NOT NULL,
                Slug            TEXT,
                Icon            TEXT,
                ContentMarkdown TEXT NOT NULL DEFAULT '',
                SortOrder       INTEGER NOT NULL DEFAULT 0,
                PinnedToDashboard INTEGER NOT NULL DEFAULT 0,
                RevisionNumber  INTEGER NOT NULL DEFAULT 1,
                CreatedAt       TEXT NOT NULL,
                UpdatedAt       TEXT NOT NULL,
                FOREIGN KEY (CampaignId)   REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (OwnerUserId)  REFERENCES Users(Id)     ON DELETE SET NULL,
                FOREIGN KEY (ParentPageId) REFERENCES NotePages(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_pages_campaign_scope ON NotePages(CampaignId, Scope);
            CREATE INDEX IF NOT EXISTS idx_pages_parent         ON NotePages(ParentPageId);
            CREATE INDEX IF NOT EXISTS idx_pages_owner          ON NotePages(OwnerUserId);
            CREATE INDEX IF NOT EXISTS idx_pages_slug           ON NotePages(CampaignId, Scope, Slug);
 
            CREATE TABLE IF NOT EXISTS NotePageShares (
                PageId      TEXT NOT NULL,
                UserId      TEXT NOT NULL,
                Permission  TEXT NOT NULL DEFAULT 'edit',
                SharedAt    TEXT NOT NULL,
                PRIMARY KEY (PageId, UserId),
                FOREIGN KEY (PageId) REFERENCES NotePages(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId) REFERENCES Users(Id)     ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS NotePageUpdates (
                Seq         INTEGER PRIMARY KEY AUTOINCREMENT,
                PageId      TEXT NOT NULL,
                Payload     BLOB NOT NULL,
                CreatedAt   TEXT NOT NULL,
                FOREIGN KEY (PageId) REFERENCES NotePages(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_note_updates_page ON NotePageUpdates(PageId, Seq);

            CREATE TABLE IF NOT EXISTS ChatChannels (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                ChannelId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                Message TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id)    ON DELETE CASCADE,
                FOREIGN KEY (ChannelId)  REFERENCES ChatChannels(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId)     REFERENCES Users(Id)
            );
            CREATE INDEX IF NOT EXISTS idx_msgs_channel_time ON ChatMessages(ChannelId, Timestamp);

            CREATE TABLE IF NOT EXISTS Handouts (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL,
                HandoutPath TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Maps (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                Scale REAL NOT NULL,
                GridKind TEXT NOT NULL DEFAULT 'Squares',
                GridOffsetX REAL NOT NULL DEFAULT 0,
                GridOffsetY REAL NOT NULL DEFAULT 0,
                MapPath TEXT NOT NULL,
                PlayerVisible INTEGER NOT NULL DEFAULT 0,
                WallsEnabled INTEGER NOT NULL DEFAULT 1,
                WallsJson TEXT NOT NULL DEFAULT '[]',
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS MapDrawings (
                Id TEXT PRIMARY KEY,
                MapId TEXT NOT NULL,
                UserId TEXT,
                StrokeDataJson TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                FOREIGN KEY (MapId)  REFERENCES Maps(Id)  ON DELETE CASCADE,
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS MapTokens (
                Id TEXT PRIMARY KEY,
                MapId TEXT NOT NULL,
                CampaignId TEXT NOT NULL,
                OwnerCharacterId TEXT,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                TokenImagePath TEXT NOT NULL,
                Label TEXT,
                Scale REAL NOT NULL DEFAULT 1,
                Rotation REAL NOT NULL DEFAULT 0,
                SizeName TEXT NOT NULL DEFAULT 'Medium',
                FOREIGN KEY (MapId)            REFERENCES Maps(Id)       ON DELETE CASCADE,
                FOREIGN KEY (CampaignId)       REFERENCES Campaigns(Id)  ON DELETE CASCADE,
                FOREIGN KEY (OwnerCharacterId) REFERENCES Characters(Id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS idx_tokens_map ON MapTokens(MapId);

            CREATE TABLE IF NOT EXISTS MapPings (
                Id TEXT PRIMARY KEY,
                MapId TEXT NOT NULL,
                CampaignId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                X INTEGER NOT NULL,
                Y INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                FOREIGN KEY (MapId)      REFERENCES Maps(Id)      ON DELETE CASCADE,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId)     REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS ChangeLog (
                ChangeId INTEGER PRIMARY KEY AUTOINCREMENT,
                CampaignId TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                EntityId TEXT NOT NULL,
                ChangeType TEXT NOT NULL,
                RevisionNumber INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                Payload TEXT,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_changelog_lookup ON ChangeLog(CampaignId, ChangeId);

            CREATE TABLE IF NOT EXISTS Currencies (
                Id TEXT NOT NULL,
                TemplateId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL,
                Abbreviation TEXT NOT NULL,
                IsBase INTEGER NOT NULL DEFAULT 0,
                EqualToBase INTEGER NOT NULL DEFAULT 1,
                Color TEXT,
                IconSvg TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (TemplateId, Id)
            );

            CREATE TABLE IF NOT EXISTS TradeLog (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                FromCharacterId TEXT,
                ToCharacterId TEXT,
                FromUserId TEXT,
                ToUserId TEXT,
                Summary TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_tradelog_campaign ON TradeLog(CampaignId, CreatedAt);

            CREATE TABLE IF NOT EXISTS DmScreenPanels (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                UserId TEXT,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS idx_dmscreen_campaign ON DmScreenPanels(CampaignId, SortOrder);

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS DiceMacros (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Expression TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId)     REFERENCES Users(Id)     ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_macros_user ON DiceMacros(CampaignId, UserId, Name);

            CREATE TABLE IF NOT EXISTS DiceRolls (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                Username TEXT NOT NULL,
                Expression TEXT NOT NULL,
                Total INTEGER NOT NULL,
                Breakdown TEXT NOT NULL,
                Label TEXT,
                IsPrivate INTEGER NOT NULL DEFAULT 0,
                Timestamp TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_dicerolls_campaign ON DiceRolls(CampaignId, Timestamp);

            CREATE TABLE IF NOT EXISTS ClassChoices (
                Id TEXT NOT NULL,
                TemplateId TEXT NOT NULL,
                ClassId TEXT NOT NULL,
                Level INTEGER NOT NULL,
                Kind TEXT NOT NULL,
                StoreAs TEXT NOT NULL,
                ChooseCount INTEGER NOT NULL DEFAULT 1,
                Label TEXT NOT NULL,
                Description TEXT,
                OptionsJson TEXT NOT NULL DEFAULT '[]',
                PRIMARY KEY (TemplateId, Id)
            );
            CREATE INDEX IF NOT EXISTS idx_classchoices_lookup ON ClassChoices(ClassId, Level);

            CREATE TABLE IF NOT EXISTS Encounters (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                MapId TEXT,
                Name TEXT,
                Round INTEGER NOT NULL DEFAULT 0,
                ActiveCombatantId TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (MapId)      REFERENCES Maps(Id)      ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS idx_encounters_lookup ON Encounters(CampaignId, MapId, IsActive);

            CREATE TABLE IF NOT EXISTS EncounterCombatants (
                Id TEXT PRIMARY KEY,
                EncounterId TEXT NOT NULL,
                CharacterId TEXT,
                TokenId TEXT,
                Name TEXT NOT NULL,
                Initiative INTEGER NOT NULL DEFAULT 0,
                CurrentHp INTEGER NOT NULL DEFAULT 0,
                MaxHp INTEGER NOT NULL DEFAULT 0,
                IsPlayerCharacter INTEGER NOT NULL DEFAULT 0,
                RevealExactHp INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                ConditionsJson TEXT NOT NULL DEFAULT '[]',
                FOREIGN KEY (EncounterId) REFERENCES Encounters(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_combatants_encounter ON EncounterCombatants(EncounterId, SortOrder);

            CREATE TABLE IF NOT EXISTS CampaignTokenLibrary (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL DEFAULT '',
                Kind TEXT NOT NULL DEFAULT 'image',
                ImagePath TEXT,
                ColorHex TEXT,
                Glyph TEXT,
                MonsterKey TEXT,
                SizeName TEXT NOT NULL DEFAULT 'Medium',
                InitiativeOverride INTEGER,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_tokenlib_campaign ON CampaignTokenLibrary(CampaignId);
            CREATE INDEX IF NOT EXISTS idx_tokenlib_monster ON CampaignTokenLibrary(CampaignId, MonsterKey);

            CREATE TABLE IF NOT EXISTS MapFog (
                MapId TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 0,
                DynamicVision INTEGER NOT NULL DEFAULT 0,
                ClosesBehind INTEGER NOT NULL DEFAULT 0,
                Cols INTEGER NOT NULL DEFAULT 0,
                Rows INTEGER NOT NULL DEFAULT 0,
                HiddenCells TEXT NOT NULL DEFAULT '',
                SeenCells TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (MapId)      REFERENCES Maps(Id)      ON DELETE CASCADE,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_mapfog_campaign ON MapFog(CampaignId);

            CREATE TABLE IF NOT EXISTS EncounterPresets (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Notes TEXT,
                MonstersJson TEXT NOT NULL DEFAULT '[]',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_encounterpresets_campaign ON EncounterPresets(CampaignId, SortOrder);

            CREATE TABLE IF NOT EXISTS Factions (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT,
                Color TEXT,
                NodeX REAL NOT NULL DEFAULT 0,
                NodeY REAL NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_factions_campaign ON Factions(CampaignId);

            CREATE TABLE IF NOT EXISTS FactionRelations (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                FromFactionId TEXT NOT NULL,
                ToFactionId TEXT NOT NULL,
                RelationType TEXT NOT NULL,
                Notes TEXT,
                FOREIGN KEY (CampaignId)    REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (FromFactionId) REFERENCES Factions(Id)  ON DELETE CASCADE,
                FOREIGN KEY (ToFactionId)   REFERENCES Factions(Id)  ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_factionrel_campaign ON FactionRelations(CampaignId);

            CREATE TABLE IF NOT EXISTS Mindmaps (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                OwnerUserId TEXT,
                Scope TEXT NOT NULL DEFAULT 'private',
                Title TEXT NOT NULL,
                ColorHex TEXT,
                RevisionNumber INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId)  REFERENCES Campaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (OwnerUserId) REFERENCES Users(Id)     ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS idx_mindmaps_campaign ON Mindmaps(CampaignId, Scope);
            CREATE INDEX IF NOT EXISTS idx_mindmaps_owner ON Mindmaps(OwnerUserId);

            CREATE TABLE IF NOT EXISTS MindmapShares (
                MindmapId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                SharedAt TEXT NOT NULL,
                PRIMARY KEY (MindmapId, UserId),
                FOREIGN KEY (MindmapId) REFERENCES Mindmaps(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId)    REFERENCES Users(Id)    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS MindmapNodes (
                Id TEXT PRIMARY KEY,
                MindmapId TEXT NOT NULL,
                CampaignId TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'blank',
                Title TEXT NOT NULL DEFAULT '',
                Body TEXT NOT NULL DEFAULT '',
                ColorHex TEXT,
                NodeX REAL NOT NULL DEFAULT 0,
                NodeY REAL NOT NULL DEFAULT 0,
                Slug TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (MindmapId)  REFERENCES Mindmaps(Id)  ON DELETE CASCADE,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_mindnodes_map ON MindmapNodes(MindmapId);
            CREATE INDEX IF NOT EXISTS idx_mindnodes_slug ON MindmapNodes(CampaignId, Slug);

            CREATE TABLE IF NOT EXISTS MindmapLinks (
                Id TEXT PRIMARY KEY,
                MindmapId TEXT NOT NULL,
                CampaignId TEXT NOT NULL,
                FromNodeId TEXT NOT NULL,
                ToNodeId TEXT NOT NULL,
                Label TEXT,
                FOREIGN KEY (MindmapId)  REFERENCES Mindmaps(Id)     ON DELETE CASCADE,
                FOREIGN KEY (FromNodeId) REFERENCES MindmapNodes(Id) ON DELETE CASCADE,
                FOREIGN KEY (ToNodeId)   REFERENCES MindmapNodes(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_mindlinks_map ON MindmapLinks(MindmapId);

            CREATE TABLE IF NOT EXISTS SessionLog (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                SessionId TEXT,
                Timestamp TEXT NOT NULL,
                ActorUserId TEXT,
                ActorName TEXT,
                EventType TEXT NOT NULL,
                Summary TEXT NOT NULL,
                DetailJson TEXT,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_sessionlog_campaign ON SessionLog(CampaignId, Timestamp);

            CREATE TABLE IF NOT EXISTS CalendarEvents (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'session',
                EventDate TEXT,
                InWorldDate TEXT,
                Notes TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_calendar_campaign ON CalendarEvents(CampaignId);

            CREATE TABLE IF NOT EXISTS TimelineEvents (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT,
                InWorldDate TEXT,
                SortOrder REAL NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_timeline_campaign ON TimelineEvents(CampaignId);

            CREATE TABLE IF NOT EXISTS SoundClips (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'sfx',
                FileName TEXT NOT NULL,
                IsFavourite INTEGER NOT NULL DEFAULT 0,
                SortOrder REAL NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_soundclips_campaign ON SoundClips(CampaignId);

            CREATE TABLE IF NOT EXISTS RandomTables (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                Name TEXT NOT NULL,
                DiceExpression TEXT NOT NULL DEFAULT '',
                EntriesJson TEXT NOT NULL DEFAULT '[]',
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES Campaigns(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_randomtables_campaign ON RandomTables(CampaignId);
        """;
    }
}
