using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Dujahit.Models.Database
{
    public class TemplateLoader
    {
        private readonly DatabaseManager _db;
        public TemplateLoader(DatabaseManager db) => _db = db;

        public async Task<string?> LoadFromFileAsync(string jsonPath, CancellationToken ct = default)
        {
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Template JSON not found.", jsonPath);

            var json = await File.ReadAllTextAsync(jsonPath, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object || EntryId(root) is not string templateId)
                throw new InvalidDataException("This rulebook has no TemplateId at the top level, so nothing can own its entries.");

            var name = Str(root, "Name");
            var desc = Str(root, "Description");
            var systemId = Str(root, "SystemId", "dnd5e");
            var now = DateTime.UtcNow.ToString("o");

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            try
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO CampaignTemplates (TemplateId, Name, Description, SystemId, Version, ImportedAt, JsonContent)
                        VALUES ($id, $name, $desc, $sys, 1, $now, $json)
                        ON CONFLICT(TemplateId) DO UPDATE SET
                            Name = excluded.Name,
                            Description = excluded.Description,
                            SystemId = excluded.SystemId,
                            Version = Version + 1,
                            ImportedAt = excluded.ImportedAt,
                            JsonContent = excluded.JsonContent
                        """;
                    cmd.Parameters.AddWithValue("$id", templateId);
                    cmd.Parameters.AddWithValue("$name", name);
                    cmd.Parameters.AddWithValue("$desc", desc);
                    cmd.Parameters.AddWithValue("$sys", systemId);
                    cmd.Parameters.AddWithValue("$now", now);
                    cmd.Parameters.AddWithValue("$json", json);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await ImportItemsAsync(conn, tx, root, templateId, now, ct);

                await ImportSpellsAsync(conn, tx, root, templateId, now, ct);

                await ImportRacesAsync(conn, tx, root, templateId, now, ct);

                await ImportSubracesAsync(conn, tx, root, templateId, now, ct);

                await ImportClassesAsync(conn, tx, root, templateId, now, ct);

                await ImportTraitsAsync(conn, tx, root, templateId, now, ct);

                await WriteCatalogEntriesAsync(conn, tx, root, templateId, now, ct);

                await using (var wipe = conn.CreateCommand())
                {
                    wipe.Transaction = tx;
                    wipe.CommandText = "DELETE FROM ClassChoices WHERE TemplateId = $tid";
                    wipe.Parameters.AddWithValue("$tid", templateId);
                    await wipe.ExecuteNonQueryAsync(ct);
                }

                await ImportClassChoicesAsync(conn, tx, root, templateId, ct);

                await using (var wipe = conn.CreateCommand())
                {
                    wipe.Transaction = tx;
                    wipe.CommandText = "DELETE FROM Currencies WHERE TemplateId = $tid";
                    wipe.Parameters.AddWithValue("$tid", templateId);
                    await wipe.ExecuteNonQueryAsync(ct);
                }

                await ImportCurrenciesAsync(conn, tx, root, templateId, ct);

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return templateId;
        }

        private static string LineageOf(string templateId) => CatalogResolver.LineagePrefixOf(templateId);

        private static bool IsEntry(JsonElement e) => e.ValueKind == JsonValueKind.Object;

        private static bool IsList(JsonElement e) => e.ValueKind == JsonValueKind.Array;

        private static string Str(JsonElement e, string prop, string fallback = "") =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? fallback) : fallback;

        private static int Int(JsonElement e, string prop, int fallback = 0) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;

        private static bool Flag(JsonElement e, string prop) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

        private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static string? EntryId(JsonElement e)
        {
            var id = Str(e, "TemplateId");
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        public static async Task BackfillChoicesAndCurrenciesAsync(SqliteConnection conn, CancellationToken ct = default)
        {
            var pending = new List<(string TemplateId, string Json)>();
            try
            {
                await using var find = conn.CreateCommand();
                find.CommandText = """
                    SELECT t.TemplateId, t.JsonContent FROM CampaignTemplates t
                    WHERE t.JsonContent IS NOT NULL AND json_valid(t.JsonContent)
                      AND (NOT EXISTS (SELECT 1 FROM ClassChoices c WHERE c.TemplateId = t.TemplateId)
                        OR NOT EXISTS (SELECT 1 FROM Currencies u WHERE u.TemplateId = t.TemplateId))
                    """;
                await using var r = await find.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) pending.Add((r.GetString(0), r.GetString(1)));
            }
            catch (SqliteException ex) { ErrorLog.Log("Looking for rulebooks missing their choices failed", ex); return; }

            foreach (var (templateId, json) in pending)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
                    try
                    {
                        await ImportClassChoicesAsync(conn, tx, doc.RootElement, templateId, ct);
                        await ImportCurrenciesAsync(conn, tx, doc.RootElement, templateId, ct);
                        await tx.CommitAsync(ct);
                    }
                    catch
                    {
                        await tx.RollbackAsync(ct);
                        throw;
                    }
                }
                catch (Exception ex) { ErrorLog.Log("Rebuilding choices and currencies for " + templateId + " failed", ex); }
            }
        }

        private static async Task WriteCatalogEntriesAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, string now, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO CatalogEntries (TemplateId, Kind, EntryId, Name, ItemType, Version, DataJson, UpdatedAt)
                VALUES ($tid, $kind, $eid, $name, $type, $ver, $json, $now)
                ON CONFLICT(TemplateId, Kind, EntryId) DO UPDATE SET
                    Name = excluded.Name, ItemType = excluded.ItemType, Version = excluded.Version,
                    DataJson = excluded.DataJson, UpdatedAt = excluded.UpdatedAt
                """;
            cmd.Parameters.AddWithValue("$tid", templateId);
            cmd.Parameters.AddWithValue("$now", now);
            var pKind = cmd.Parameters.Add("$kind", SqliteType.Text);
            var pEid = cmd.Parameters.Add("$eid", SqliteType.Text);
            var pName = cmd.Parameters.Add("$name", SqliteType.Text);
            var pType = cmd.Parameters.Add("$type", SqliteType.Text);
            var pVer = cmd.Parameters.Add("$ver", SqliteType.Text);
            var pJson = cmd.Parameters.Add("$json", SqliteType.Text);

            foreach (var kind in CatalogResolver.Kinds)
            {
                if (!root.TryGetProperty(kind, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var entry in arr.EnumerateArray())
                {
                    if (!IsEntry(entry)) continue;
                    if (EntryId(entry) is not string eid) continue;

                    pKind.Value = kind;
                    pEid.Value = eid;
                    pName.Value = Str(entry, "Name");
                    pType.Value = Str(entry, "$type");
                    pVer.Value = Str(entry, "Version");
                    pJson.Value = entry.GetRawText();
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private static async Task ImportItemsAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, string now, CancellationToken ct)
        {
                if (root.TryGetProperty("Items", out var items) && IsList(items))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO Items (Id, Name, ItemType, Source, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $type, 'srd', $tid, 1, $now, $json, $ver)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = excluded.Name, ItemType = excluded.ItemType, Source = excluded.Source,
                            TemplateId = excluded.TemplateId, RevisionNumber = Items.RevisionNumber + 1,
                            UpdatedAt = excluded.UpdatedAt, DataJson = excluded.DataJson, Version = excluded.Version
                        WHERE Items.Source = 'srd'
                          AND (Items.TemplateId IS excluded.TemplateId OR substr(Items.TemplateId, 1, LENGTH($lineage)) = $lineage)
                          AND (Items.DataJson IS NOT excluded.DataJson
                            OR Items.Name IS NOT excluded.Name
                            OR Items.ItemType IS NOT excluded.ItemType
                            OR Items.Version IS NOT excluded.Version)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pType = cmd.Parameters.Add("$type", SqliteType.Text);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    cmd.Parameters.AddWithValue("$lineage", LineageOf(templateId));
                    var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
                    var pJson = cmd.Parameters.Add("$json", SqliteType.Text);
                    var pVer = cmd.Parameters.Add("$ver", SqliteType.Text);

                    foreach (var item in items.EnumerateArray())
                    {
                        if (EntryId(item) is not string itemId) continue;
                        pId.Value = itemId;
                        pName.Value = Str(item, "Name");
                        pType.Value = Str(item, "$type", "Generic");
                        pTid.Value = templateId;
                        pNow.Value = now;
                        pJson.Value = item.GetRawText();
                        pVer.Value = Str(item, "Version", "2014");
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
        }

        private static async Task ImportSpellsAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, string now, CancellationToken ct)
        {
                if (root.TryGetProperty("Spells", out var spells) && IsList(spells))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO Spells (Id, Name, Level, School, CastingTime, Duration, Range,
                                            Concentration, Ritual, Description, Source, TemplateId,
                                            RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $lvl, $school, $ct, $dur, $rng, $conc, $rit, $desc,
                                'srd', $tid, 1, $now, $json, $ver)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = excluded.Name, Level = excluded.Level, School = excluded.School,
                            CastingTime = excluded.CastingTime, Duration = excluded.Duration, Range = excluded.Range,
                            Concentration = excluded.Concentration, Ritual = excluded.Ritual, Description = excluded.Description,
                            Source = excluded.Source, TemplateId = excluded.TemplateId,
                            RevisionNumber = Spells.RevisionNumber + 1, UpdatedAt = excluded.UpdatedAt,
                            DataJson = excluded.DataJson, Version = excluded.Version
                        WHERE Spells.Source = 'srd'
                          AND (Spells.TemplateId IS excluded.TemplateId OR substr(Spells.TemplateId, 1, LENGTH($lineage)) = $lineage)
                          AND (Spells.DataJson IS NOT excluded.DataJson
                            OR Spells.Name IS NOT excluded.Name
                            OR Spells.Version IS NOT excluded.Version)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pLvl = cmd.Parameters.Add("$lvl", SqliteType.Integer);
                    var pSchool = cmd.Parameters.Add("$school", SqliteType.Text);
                    var pCt = cmd.Parameters.Add("$ct", SqliteType.Text);
                    var pDur = cmd.Parameters.Add("$dur", SqliteType.Text);
                    var pRng = cmd.Parameters.Add("$rng", SqliteType.Text);
                    var pConc = cmd.Parameters.Add("$conc", SqliteType.Integer);
                    var pRit = cmd.Parameters.Add("$rit", SqliteType.Integer);
                    var pDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    cmd.Parameters.AddWithValue("$lineage", LineageOf(templateId));
                    var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
                    var pJson = cmd.Parameters.Add("$json", SqliteType.Text);
                    var pVer = cmd.Parameters.Add("$ver", SqliteType.Text);

                    foreach (var sp in spells.EnumerateArray())
                    {
                        if (EntryId(sp) is not string spellId) continue;
                        pId.Value = spellId;
                        pName.Value = Str(sp, "Name");
                        pLvl.Value = Int(sp, "Level");
                        pSchool.Value = Str(sp, "School");
                        pCt.Value = Str(sp, "CastingTime");
                        pDur.Value = Str(sp, "Duration");
                        pRng.Value = Str(sp, "Range");
                        pConc.Value = Flag(sp, "Concentration") ? 1 : 0;
                        pRit.Value = Flag(sp, "Ritual") ? 1 : 0;
                        pDesc.Value = Str(sp, "Description");
                        pTid.Value = templateId;
                        pNow.Value = now;
                        pJson.Value = sp.GetRawText();
                        pVer.Value = Str(sp, "Version", "2014");
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
        }

        private static async Task ImportRacesAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, string now, CancellationToken ct)
        {
                if (root.TryGetProperty("Races", out var races) && IsList(races))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO Races (Id, Name, Description, Size, Speed, Source, TemplateId,
                                           RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $desc, $size, $speed, 'srd', $tid, 1, $now, $json, $ver)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = excluded.Name, Description = excluded.Description, Size = excluded.Size,
                            Speed = excluded.Speed, Source = excluded.Source, TemplateId = excluded.TemplateId,
                            RevisionNumber = Races.RevisionNumber + 1, UpdatedAt = excluded.UpdatedAt,
                            DataJson = excluded.DataJson, Version = excluded.Version
                        WHERE Races.Source = 'srd'
                          AND (Races.TemplateId IS excluded.TemplateId OR substr(Races.TemplateId, 1, LENGTH($lineage)) = $lineage)
                          AND (Races.DataJson IS NOT excluded.DataJson
                            OR Races.Name IS NOT excluded.Name
                            OR Races.Version IS NOT excluded.Version)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
                    var pSize = cmd.Parameters.Add("$size", SqliteType.Text);
                    var pSpeed = cmd.Parameters.Add("$speed", SqliteType.Integer);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    cmd.Parameters.AddWithValue("$lineage", LineageOf(templateId));
                    var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
                    var pJson = cmd.Parameters.Add("$json", SqliteType.Text);
                    var pVer = cmd.Parameters.Add("$ver", SqliteType.Text);

                    foreach (var r in races.EnumerateArray())
                    {
                        if (EntryId(r) is not string raceRowId) continue;
                        pId.Value = raceRowId;
                        pName.Value = Str(r, "Name");
                        pDesc.Value = Str(r, "Description");
                        pSize.Value = Str(r, "Size");
                        pSpeed.Value = Int(r, "Speed");
                        pTid.Value = templateId;
                        pNow.Value = now;
                        pJson.Value = r.GetRawText();
                        pVer.Value = Str(r, "Version", "2014");
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
        }

        private static async Task ImportSubracesAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, string now, CancellationToken ct)
        {
                if (root.TryGetProperty("Subraces", out var subraces) && IsList(subraces))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO Subraces (Id, Name, ParentRaceId, Description, Source, TemplateId,
                                              RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $parent, $desc, 'srd', $tid, 1, $now, $json, $ver)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = excluded.Name, ParentRaceId = excluded.ParentRaceId, Description = excluded.Description,
                            Source = excluded.Source, TemplateId = excluded.TemplateId,
                            RevisionNumber = Subraces.RevisionNumber + 1, UpdatedAt = excluded.UpdatedAt,
                            DataJson = excluded.DataJson, Version = excluded.Version
                        WHERE Subraces.Source = 'srd'
                          AND (Subraces.TemplateId IS excluded.TemplateId OR substr(Subraces.TemplateId, 1, LENGTH($lineage)) = $lineage)
                          AND (Subraces.DataJson IS NOT excluded.DataJson
                            OR Subraces.Name IS NOT excluded.Name
                            OR Subraces.Version IS NOT excluded.Version)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pParent = cmd.Parameters.Add("$parent", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    cmd.Parameters.AddWithValue("$lineage", LineageOf(templateId));
                    var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
                    var pJson = cmd.Parameters.Add("$json", SqliteType.Text);
                    var pVer = cmd.Parameters.Add("$ver", SqliteType.Text);

                    var parentMap = new Dictionary<string, string>();
                    if (root.TryGetProperty("Races", out var racesForMap) && IsList(racesForMap))
                    {
                        foreach (var race in racesForMap.EnumerateArray())
                        {
                            if (EntryId(race) is not string raceId) continue;
                            if (race.TryGetProperty("SubraceIds", out var sids))
                            {
                                foreach (var sid in sids.EnumerateArray())
                                    parentMap[sid.GetString()!] = raceId;
                            }
                        }
                    }

                    foreach (var sr in subraces.EnumerateArray())
                    {
                        if (EntryId(sr) is not string srId) continue;
                        pId.Value = srId;
                        pName.Value = Str(sr, "Name");
                        pParent.Value = parentMap.TryGetValue(srId, out var pid) ? pid : "";
                        pDesc.Value = Str(sr, "Description");
                        pTid.Value = templateId;
                        pNow.Value = now;
                        pJson.Value = sr.GetRawText();
                        pVer.Value = Str(sr, "Version", "2014");
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
        }

        private static async Task ImportClassesAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, string now, CancellationToken ct)
        {
                if (root.TryGetProperty("Classes", out var classes) && IsList(classes))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO Classes (Id, Name, Description, HitDiceId, PrimaryAbility,
                                             Source, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $desc, $hd, $prim, 'srd', $tid, 1, $now, $json, $ver)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = excluded.Name, Description = excluded.Description, HitDiceId = excluded.HitDiceId,
                            PrimaryAbility = excluded.PrimaryAbility, Source = excluded.Source, TemplateId = excluded.TemplateId,
                            RevisionNumber = Classes.RevisionNumber + 1, UpdatedAt = excluded.UpdatedAt,
                            DataJson = excluded.DataJson, Version = excluded.Version
                        WHERE Classes.Source = 'srd'
                          AND (Classes.TemplateId IS excluded.TemplateId OR substr(Classes.TemplateId, 1, LENGTH($lineage)) = $lineage)
                          AND (Classes.DataJson IS NOT excluded.DataJson
                            OR Classes.Name IS NOT excluded.Name
                            OR Classes.Version IS NOT excluded.Version)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
                    var pHd = cmd.Parameters.Add("$hd", SqliteType.Text);
                    var pPrim = cmd.Parameters.Add("$prim", SqliteType.Text);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    cmd.Parameters.AddWithValue("$lineage", LineageOf(templateId));
                    var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
                    var pJson = cmd.Parameters.Add("$json", SqliteType.Text);
                    var pVer = cmd.Parameters.Add("$ver", SqliteType.Text);

                    foreach (var c in classes.EnumerateArray())
                    {
                        if (EntryId(c) is not string classRowId) continue;
                        pId.Value = classRowId;
                        pName.Value = Str(c, "Name");
                        pDesc.Value = Str(c, "Description");
                        pHd.Value = Str(c, "HitDiceId");
                        pPrim.Value = Str(c, "PrimaryAbilityId");
                        pTid.Value = templateId;
                        pNow.Value = now;
                        pJson.Value = c.GetRawText();
                        pVer.Value = Str(c, "Version", "2014");
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
        }

        private static async Task ImportTraitsAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, string now, CancellationToken ct)
        {
                if (root.TryGetProperty("Traits", out var traits) && IsList(traits))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO Traits (Id, Name, Description, Source, TemplateId,
                                            RevisionNumber, UpdatedAt, Version)
                        VALUES ($id, $name, $desc, 'srd', $tid, 1, $now, $ver)
                        ON CONFLICT(Id) DO UPDATE SET
                            Name = excluded.Name, Description = excluded.Description, Source = excluded.Source,
                            TemplateId = excluded.TemplateId, RevisionNumber = Traits.RevisionNumber + 1,
                            UpdatedAt = excluded.UpdatedAt, Version = excluded.Version
                        WHERE Traits.Source = 'srd'
                          AND (Traits.TemplateId IS excluded.TemplateId OR substr(Traits.TemplateId, 1, LENGTH($lineage)) = $lineage)
                          AND (Traits.Name IS NOT excluded.Name
                            OR Traits.Description IS NOT excluded.Description
                            OR Traits.Version IS NOT excluded.Version)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    cmd.Parameters.AddWithValue("$lineage", LineageOf(templateId));
                    var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
                    var pVer = cmd.Parameters.Add("$ver", SqliteType.Text);

                    foreach (var t in traits.EnumerateArray())
                    {
                        if (EntryId(t) is not string traitId) continue;
                        pId.Value = traitId;
                        pName.Value = Str(t, "Name");
                        pDesc.Value = Str(t, "Description");
                        pTid.Value = templateId;
                        pNow.Value = now;
                        pVer.Value = Str(t, "Version", "2014");
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
        }

        private static async Task ImportClassChoicesAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, CancellationToken ct)
        {
                if (root.TryGetProperty("ClassChoices", out var classChoices) && IsList(classChoices))
                {
                    var profName = new Dictionary<string, string>();
                    if (root.TryGetProperty("Proficiencies", out var profs) && IsList(profs))
                        foreach (var p in profs.EnumerateArray())
                            if (EntryId(p) is string profRowId) profName[profRowId] = Str(p, "Name");

                    var subName = new Dictionary<string, string>();
                    if (root.TryGetProperty("Subclasses", out var subs) && IsList(subs))
                        foreach (var sc in subs.EnumerateArray())
                            if (EntryId(sc) is string subRowId) subName[subRowId] = Str(sc, "Name");

                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT OR REPLACE INTO ClassChoices (Id, TemplateId, ClassId, Level, Kind, StoreAs, ChooseCount, Label, Description, OptionsJson)
                        VALUES ($id, $tid, $cid, $lvl, $kind, $store, $count, $label, $desc, $opts)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    var pCid = cmd.Parameters.Add("$cid", SqliteType.Text);
                    var pLvl = cmd.Parameters.Add("$lvl", SqliteType.Integer);
                    var pKind = cmd.Parameters.Add("$kind", SqliteType.Text);
                    var pStore = cmd.Parameters.Add("$store", SqliteType.Text);
                    var pCount = cmd.Parameters.Add("$count", SqliteType.Integer);
                    var pLabel = cmd.Parameters.Add("$label", SqliteType.Text);
                    var pDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
                    var pOpts = cmd.Parameters.Add("$opts", SqliteType.Text);

                    foreach (var ch in classChoices.EnumerateArray())
                    {
                        if (EntryId(ch) is not string choiceId) continue;

                        var source = Str(ch, "OptionSource");
                        var lookup = source == "subclass" ? subName : profName;

                        var resolved = new List<object>();
                        if (ch.TryGetProperty("OptionIds", out var oids) && IsList(oids))
                        {
                            foreach (var oid in oids.EnumerateArray())
                            {
                                var idStr = oid.ValueKind == JsonValueKind.String ? oid.GetString() : null;
                                if (string.IsNullOrWhiteSpace(idStr)) continue;
                                var nm = lookup.TryGetValue(idStr!, out var found) ? found : idStr!;
                                resolved.Add(new { Id = idStr!, Name = nm });
                            }
                        }

                        pId.Value = choiceId;
                        pTid.Value = templateId;
                        pCid.Value = Str(ch, "ClassId");
                        pLvl.Value = Int(ch, "Level", 1);
                        pKind.Value = Str(ch, "Kind");
                        pStore.Value = Str(ch, "StoreAs");
                        pCount.Value = Int(ch, "ChooseCount", 1);
                        pLabel.Value = Str(ch, "Label");
                        pDesc.Value = Str(ch, "Description");
                        pOpts.Value = JsonSerializer.Serialize(resolved);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
        }

        private static async Task ImportCurrenciesAsync(SqliteConnection conn, SqliteTransaction tx, JsonElement root, string templateId, CancellationToken ct)
        {
                if (root.TryGetProperty("Currencies", out var currencies) && IsList(currencies))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT OR REPLACE INTO Currencies (Id, TemplateId, Name, Abbreviation, IsBase, EqualToBase, Color, IconSvg, SortOrder)
                        VALUES ($id, $tid, $name, $abbr, $base, $eq, $color, $icon, $sort)
                        """;
                    var pId = cmd.Parameters.Add("$id", SqliteType.Text);
                    var pTid = cmd.Parameters.Add("$tid", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pAbbr = cmd.Parameters.Add("$abbr", SqliteType.Text);
                    var pBase = cmd.Parameters.Add("$base", SqliteType.Integer);
                    var pEq = cmd.Parameters.Add("$eq", SqliteType.Integer);
                    var pColor = cmd.Parameters.Add("$color", SqliteType.Text);
                    var pIcon = cmd.Parameters.Add("$icon", SqliteType.Text);
                    var pSort = cmd.Parameters.Add("$sort", SqliteType.Integer);

                    var order = 0;
                    foreach (var cur in currencies.EnumerateArray())
                    {
                        if (EntryId(cur) is not string currencyId) continue;
                        pId.Value = currencyId;
                        pTid.Value = templateId;
                        pName.Value = Str(cur, "Name");
                        pAbbr.Value = Str(cur, "Abbreviation");
                        pBase.Value = Flag(cur, "IsBase") ? 1 : 0;
                        pEq.Value = Int(cur, "EqualToBase", 1);
                        pColor.Value = (object?)NullIfBlank(Str(cur, "Color")) ?? DBNull.Value;
                        pIcon.Value = (object?)NullIfBlank(Str(cur, "IconSvg")) ?? DBNull.Value;
                        pSort.Value = Int(cur, "SortOrder", order);
                        await cmd.ExecuteNonQueryAsync(ct);
                        order++;
                    }
                }
        }
    }
}