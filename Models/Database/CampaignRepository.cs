using Dujahit.Models.Application;
using Dujahit.Models.Communication;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using System.Diagnostics;

namespace Dujahit.Models.Database
{
    public class CampaignRepository
    {
        private readonly DatabaseManager _db;
        private readonly ActiveCampaignContext _ctx;

        public CampaignRepository(DatabaseManager db, ActiveCampaignContext ctx)
        {
            _db = db;
            _ctx = ctx;
        }

        public async Task<CampaignBootstrapPayload> BuildBootstrapAsync(
    string userId,
    CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);

            var campaign = await FetchActiveCampaignAsync(conn, ct);

            var racesTask = FetchEnabledRacesAsync(conn, campaign.Id, campaign.TemplateId, ct);
            var classesTask = FetchEnabledClassesAsync(conn, campaign.Id, campaign.TemplateId, ct);
            var spellsTask = FetchEnabledSpellsAsync(conn, campaign.Id, campaign.TemplateId, ct);
            var itemsTask = FetchEnabledItemsAsync(conn, campaign.Id, campaign.TemplateId, ct);
            var traitsTask = FetchEnabledTraitsAsync(conn, campaign.Id, ct);
            var membersTask = FetchMembersAsync(conn, campaign.Id, ct);
            var charactersTask = FetchPcRosterAsync(conn, campaign.Id, ct);
            var charTask = FetchCharacterForUserAsync(conn, campaign.Id, userId, ct);
            var mapTask = FetchActiveMapAsync(conn, campaign.Id, ct);
            var notesTask = FetchVisibleNotePagesAsync(conn, campaign.Id, userId, /*isDm:*/ await IsUserDmAsync(conn, campaign.Id, userId, ct), ct);
            var sharesTask = FetchUserSharesAsync(conn, userId, ct);

            var channelsTask = FetchChannelsAsync(conn, campaign.Id, ct);
            var instancesTask = FetchItemInstancesAsync(conn, campaign.Id, ct);
            var currenciesTask = FetchCurrenciesAsync(conn, ct);

            await Task.WhenAll(
                racesTask, classesTask, spellsTask,
                itemsTask, traitsTask, membersTask, charactersTask,
                charTask, mapTask, channelsTask, notesTask, sharesTask, instancesTask, currenciesTask);

            var activeMap = await mapTask;
            var tokens = activeMap != null
                ? await FetchTokensForMapAsync(conn, activeMap.Id, ct)
                : new List<MapToken>();

            var subraces = await FetchSubracesAsync(conn, campaign.TemplateId, ct);
            var template = await FetchTemplateAsync(conn, campaign.TemplateId, ct);
            var lastChangeId = await FetchMaxChangeIdAsync(conn, campaign.Id, ct);

            return new CampaignBootstrapPayload
            {
                CampaignId = campaign.Id,
                CampaignName = campaign.Name,
                TemplateId = campaign.TemplateId,
                TemplateName = template.Name,
                TemplateSystemId = template.SystemId,
                TemplateVersion = template.Version,
                TemplateJsonContent = template.Json,
                Description = campaign.Description ?? "",
                Port = campaign.Port,
                Races = await racesTask,
                Subraces = subraces,
                Classes = await classesTask,
                Spells = await spellsTask,
                Items = await itemsTask,
                Traits = await traitsTask,
                Members = await membersTask,
                OnlineUserIds = ConnectionTracker.OnlineUserIds(),
                Characters = await charactersTask,
                MyCharacter = await charTask,
                ActiveMap = activeMap,
                Tokens = tokens,
                Channels = await channelsTask,
                NotePages = await notesTask,
                NoteShares = await sharesTask,
                ItemInstances = await instancesTask,
                Currencies = await currenciesTask,
                LastChangeId = lastChangeId
            };
        }

        private static async Task<List<Character>> FetchPcRosterAsync(
            SqliteConnection conn, string campaignId, CancellationToken ct)
        {
            var result = new List<Character>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
               Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson,
               StateJson, CharacterKind, Slug, Tags, CreatedAt
        FROM Characters
        WHERE CampaignId = $cid AND CharacterKind = 'pc'
        """;
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                result.Add(new Character
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                    Name = r.GetString(3),
                    RaceId = r.IsDBNull(4) ? null : r.GetString(4),
                    SubraceId = r.IsDBNull(5) ? null : r.GetString(5),
                    ClassId = r.IsDBNull(6) ? null : r.GetString(6),
                    Level = r.GetInt32(7),
                    CurrentHp = r.GetInt32(8),
                    MaxHp = r.GetInt32(9),
                    AbilityScoresJson = r.GetString(10),
                    InventoryJson = r.IsDBNull(11) ? null : r.GetString(11),
                    StateJson = r.IsDBNull(12) ? null : r.GetString(12),
                    CharacterKind = r.IsDBNull(13) ? "pc" : r.GetString(13),
                    Slug = r.IsDBNull(14) ? null : r.GetString(14),
                    Tags = r.IsDBNull(15) ? "[]" : r.GetString(15),
                    CreatedAt = DateTime.Parse(r.GetString(16))
                });
            }
            return result;
        }

        private async Task<CampaignRow> FetchActiveCampaignAsync(
            SqliteConnection conn, CancellationToken ct)
        {
            Debug.WriteLine($"[REPO] Querying for CampaignId: '{_ctx.CampaignId}'");
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, TemplateId, Description, Port
                FROM Campaigns
                WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", _ctx.CampaignId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new InvalidOperationException(
                    "No campaign found on this server. DM must create one first.");

            return new CampaignRow
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                TemplateId = r.GetString(2),
                Description = r.IsDBNull(3) ? null : r.GetString(3),
                Port = r.GetString(4)
            };
        }

        private static async Task<CampaignRow> FetchCampaignAsync(
            SqliteConnection conn, string campaignId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, TemplateId, Description, Port
                FROM Campaigns
                WHERE Id = $CampaignId
            """;
            cmd.Parameters.AddWithValue("$CampaignId", campaignId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");

            return new CampaignRow
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                TemplateId = r.GetString(2),
                Description = r.IsDBNull(3) ? null : r.GetString(3),
                Port = r.GetString(4)
            };
        }

        private sealed class CampaignRow
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string TemplateId { get; set; } = "";
            public string? Description { get; set; }
            public string Port { get; set; } = "";
        }

        private static async Task<List<Race>> FetchEnabledRacesAsync(
    SqliteConnection conn, string campaignId, string? templateId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
        SELECT r.Id, r.Name, r.Description, r.Size, r.Speed,
               r.Source, r.OwnerUserId, r.TemplateId,
               r.RevisionNumber, r.UpdatedAt, {CatalogResolver.ResolvedJsonSql("Races", "r")}
        FROM Races r
        ORDER BY r.Name COLLATE NOCASE
    """;

            CatalogResolver.BindScope(cmd, templateId);

            var results = new List<Race>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new Race
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Description = r.GetString(2),
                    Size = r.GetString(3),
                    Speed = r.GetInt32(4),
                    Source = r.GetString(5),
                    OwnerUserId = r.IsDBNull(6) ? null : r.GetString(6),
                    TemplateId = r.IsDBNull(7) ? null : r.GetString(7),
                    RevisionNumber = r.GetInt32(8),
                    UpdatedAt = r.GetString(9),
                    DataJson = r.GetString(10)
                });
            }
            return results;
        }

        private static async Task<List<Class>> FetchEnabledClassesAsync(
    SqliteConnection conn, string campaignId, string? templateId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
        SELECT c.Id, c.Name, c.Description, c.HitDiceId, c.PrimaryAbility,
               c.Source, c.OwnerUserId, c.TemplateId,
               c.RevisionNumber, c.UpdatedAt, {CatalogResolver.ResolvedJsonSql("Classes", "c")}
        FROM Classes c
        ORDER BY c.Name COLLATE NOCASE
    """;

            CatalogResolver.BindScope(cmd, templateId);

            var results = new List<Class>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new Class
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Description = r.GetString(2),
                    HitDiceId = r.GetString(3),
                    PrimaryAbility = r.GetString(4),
                    Source = r.GetString(5),
                    OwnerUserId = r.IsDBNull(6) ? null : r.GetString(6),
                    TemplateId = r.IsDBNull(7) ? null : r.GetString(7),
                    RevisionNumber = r.GetInt32(8),
                    UpdatedAt = r.GetString(9),
                    DataJson = r.GetString(10)
                });
            }
            return results;
        }

        private static async Task<List<Spell>> FetchEnabledSpellsAsync(
    SqliteConnection conn, string campaignId, string? templateId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT s.Id, s.Name, s.Level, s.School, s.CastingTime,
                       s.Duration, s.Range, s.Concentration, s.Ritual,
                       s.Description, s.Source, s.OwnerUserId, s.TemplateId,
                       s.RevisionNumber, s.UpdatedAt, {CatalogResolver.ResolvedJsonSql("Spells", "s")}
                FROM Spells s
                ORDER BY s.Level, s.Name COLLATE NOCASE
            """;

            CatalogResolver.BindScope(cmd, templateId);

            var results = new List<Spell>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new Spell
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Level = r.GetInt32(2),
                    School = r.GetString(3),
                    CastingTime = r.GetString(4),
                    Duration = r.GetString(5),
                    Range = r.GetString(6),
                    Concentration = r.GetInt32(7) == 1,
                    Ritual = r.GetInt32(8) == 1,
                    Description = r.GetString(9),
                    Source = r.GetString(10),
                    OwnerUserId = r.IsDBNull(11) ? null : r.GetString(11),
                    TemplateId = r.IsDBNull(12) ? null : r.GetString(12),
                    RevisionNumber = r.GetInt32(13),
                    UpdatedAt = r.GetString(14),
                    DataJson = r.GetString(15)
                });
            }
            return results;
        }

        private static async Task<List<Item>> FetchEnabledItemsAsync(
            SqliteConnection conn, string campaignId, string? templateId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT i.Id, i.Name, i.ItemType, i.Source, i.OwnerUserId,
                       i.TemplateId, i.RevisionNumber, i.UpdatedAt, {CatalogResolver.ResolvedJsonSql("Items", "i")},
                       i.Slug, i.Tags
                FROM Items i
                ORDER BY i.Name COLLATE NOCASE
            """;

            CatalogResolver.BindScope(cmd, templateId);

            var results = new List<Item>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new Item
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    ItemType = r.GetString(2),
                    Source = r.GetString(3),
                    OwnerUserId = r.IsDBNull(4) ? null : r.GetString(4),
                    TemplateId = r.IsDBNull(5) ? null : r.GetString(5),
                    RevisionNumber = r.GetInt32(6),
                    UpdatedAt = r.GetString(7),
                    DataJson = r.GetString(8),
                    Slug = r.IsDBNull(9) ? null : r.GetString(9),
                    Tags = r.IsDBNull(10) ? "[]" : r.GetString(10)
                });
            }
            return results;
        }

        private static async Task<List<Trait>> FetchEnabledTraitsAsync(
            SqliteConnection conn, string campaignId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT t.Id, t.Name, t.Description, t.Source,
                       t.OwnerUserId, t.TemplateId, t.RevisionNumber, t.UpdatedAt
                FROM Traits t
                ORDER BY t.Name COLLATE NOCASE
            """;

            var results = new List<Trait>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new Trait
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Description = r.GetString(2),
                    Source = r.GetString(3),
                    OwnerUserId = r.IsDBNull(4) ? null : r.GetString(4),
                    TemplateId = r.IsDBNull(5) ? null : r.GetString(5),
                    RevisionNumber = r.GetInt32(6),
                    UpdatedAt = r.GetString(7)
                });
            }
            return results;
        }

        private static async Task<List<Subrace>> FetchSubracesAsync(
            SqliteConnection conn, string? templateId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT Id, Name, ParentRaceId, Description, Source,
                       OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, {CatalogResolver.ResolvedJsonSql("Subraces", "Subraces")}
                FROM Subraces
                ORDER BY Name COLLATE NOCASE
            """;

            CatalogResolver.BindScope(cmd, templateId);

            var results = new List<Subrace>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new Subrace
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    ParentRaceId = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    Source = r.GetString(4),
                    OwnerUserId = r.IsDBNull(5) ? null : r.GetString(5),
                    TemplateId = r.IsDBNull(6) ? null : r.GetString(6),
                    RevisionNumber = r.GetInt32(7),
                    UpdatedAt = r.GetString(8),
                    DataJson = r.IsDBNull(9) ? "{}" : r.GetString(9)
                });
            }
            return results;
        }

        private static async Task<(string Name, string SystemId, int Version, string Json)> FetchTemplateAsync(
            SqliteConnection conn, string templateId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Name, SystemId, Version, JsonContent
                FROM CampaignTemplates
                WHERE TemplateId = $tid
                LIMIT 1
            """;
            cmd.Parameters.AddWithValue("$tid", templateId ?? "");
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                if (await r.ReadAsync(ct))
                    return (r.GetString(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? "" : r.GetString(3));
            }

            await using var fb = conn.CreateCommand();
            fb.CommandText = "SELECT Name, SystemId, Version, JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
            await using (var r2 = await fb.ExecuteReaderAsync(ct))
            {
                if (await r2.ReadAsync(ct))
                    return (r2.GetString(0), r2.GetString(1), r2.GetInt32(2), r2.IsDBNull(3) ? "" : r2.GetString(3));
            }

            return ("", "", 1, "");
        }

        private static async Task<List<CampaignMember>> FetchMembersAsync(
            SqliteConnection conn, string campaignId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT cm.UserId, u.Username, cm.Role, cm.CharacterId, cm.JoinedAt
                FROM CampaignMembers cm
                INNER JOIN Users u ON u.Id = cm.UserId
                WHERE cm.CampaignId = $CampaignId
            """;
            cmd.Parameters.AddWithValue("$CampaignId", campaignId);

            var results = new List<CampaignMember>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new CampaignMember
                {
                    CampaignId = campaignId,
                    UserId = r.GetString(0),
                    Username = r.GetString(1),
                    Role = r.GetString(2),
                    CharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                    JoinedAt = DateTime.Parse(r.GetString(4))
                });
            }
            return results;
        }

        private static async Task<Character?> FetchCharacterForUserAsync(
            SqliteConnection conn, string campaignId, string userId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId,
                       ClassId, Level, CurrentHp, MaxHp,
                       AbilityScoresJson, InventoryJson, StateJson,
                       CharacterKind, Slug, Tags, CreatedAt
                FROM Characters
                WHERE CampaignId  = $CampaignId
                  AND OwnerUserId = $UserId
                LIMIT 1
            """;
            cmd.Parameters.AddWithValue("$CampaignId", campaignId);
            cmd.Parameters.AddWithValue("$UserId", userId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            return new Character
            {
                Id = r.GetString(0),
                CampaignId = r.GetString(1),
                OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                Name = r.GetString(3),
                RaceId = r.IsDBNull(4) ? null : r.GetString(4),
                SubraceId = r.IsDBNull(5) ? null : r.GetString(5),
                ClassId = r.IsDBNull(6) ? null : r.GetString(6),
                Level = r.GetInt32(7),
                CurrentHp = r.GetInt32(8),
                MaxHp = r.GetInt32(9),
                AbilityScoresJson = r.GetString(10),
                InventoryJson = r.IsDBNull(11) ? null : r.GetString(11),
                StateJson = r.IsDBNull(12) ? null : r.GetString(12),
                CharacterKind = r.IsDBNull(13) ? "pc" : r.GetString(13),
                Slug = r.IsDBNull(14) ? null : r.GetString(14),
                Tags = r.IsDBNull(15) ? "[]" : r.GetString(15),
                CreatedAt = DateTime.Parse(r.GetString(16))
            };
        }

        private static async Task<Map?> FetchActiveMapAsync(
            SqliteConnection conn, string campaignId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, Name, Width, Height, Scale, MapPath, CreatedAt
                FROM Maps
                WHERE CampaignId = $CampaignId
                ORDER BY CreatedAt DESC
                LIMIT 1
            """;
            cmd.Parameters.AddWithValue("$CampaignId", campaignId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            return new Map
            {
                Id = r.GetString(0),
                CampaignId = r.GetString(1),
                Name = r.GetString(2),
                Width = r.GetInt32(3),
                Height = r.GetInt32(4),
                Scale = r.GetDouble(5),
                MapPath = r.GetString(6),
                CreatedAt = DateTime.Parse(r.GetString(7))
            };
        }

        public async Task<List<PlayerMapSummary>> FetchPlayerVisibleMapsAsync(string campaignId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, GridKind, Scale
                FROM Maps
                WHERE CampaignId = $CampaignId AND PlayerVisible = 1
                ORDER BY CreatedAt ASC
            """;
            cmd.Parameters.AddWithValue("$CampaignId", campaignId);

            var result = new List<PlayerMapSummary>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                result.Add(new PlayerMapSummary(
                    r.GetString(0),
                    r.GetString(1),
                    r.IsDBNull(2) ? "Squares" : r.GetString(2),
                    r.IsDBNull(3) ? 1.0 : r.GetDouble(3)));
            }
            return result;
        }

        public async Task<FogStateMessage?> FetchMapFogMessageAsync(string campaignId, string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Enabled, Cols, Rows, HiddenCells FROM MapFog WHERE MapId = $mid AND CampaignId = $cid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return new FogStateMessage(mapId, false, 0, 0, new List<FogCellPoint>());
            var enabled = r.GetInt64(0) != 0;
            var cols = (int)r.GetInt64(1);
            var rows = (int)r.GetInt64(2);
            var hidden = new List<FogCellPoint>();
            var csv = r.IsDBNull(3) ? "" : r.GetString(3);
            foreach (var pair in csv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = pair.Split(',');
                if (bits.Length == 2 && int.TryParse(bits[0], out var c) && int.TryParse(bits[1], out var rr))
                    hidden.Add(new FogCellPoint(c, rr));
            }
            return new FogStateMessage(mapId, enabled, cols, rows, hidden);
        }

        public async Task<WallStateMessage?> FetchMapWallsMessageAsync(string campaignId, string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT WallsEnabled, WallsJson FROM Maps WHERE Id = $mid AND CampaignId = $cid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return new WallStateMessage(mapId, false, new List<WallSegment>());
            var enabled = r.GetInt64(0) != 0;
            var json = r.IsDBNull(1) ? "[]" : r.GetString(1);
            List<WallSegment> walls;
            try { walls = JsonSerializer.Deserialize<List<WallSegment>>(json) ?? new List<WallSegment>(); }
            catch (JsonException) { walls = new List<WallSegment>(); }
            return new WallStateMessage(mapId, enabled, walls);
        }

        public async Task<TerrainStateMessage?> FetchMapTerrainMessageAsync(string campaignId, string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DifficultTerrainJson FROM Maps WHERE Id = $mid AND CampaignId = $cid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return new TerrainStateMessage(mapId, new List<FogCellPoint>());
            var json = r.IsDBNull(0) ? "[]" : r.GetString(0);
            List<FogCellPoint> cells;
            try { cells = JsonSerializer.Deserialize<List<FogCellPoint>>(json) ?? new List<FogCellPoint>(); }
            catch (JsonException) { cells = new List<FogCellPoint>(); }
            return new TerrainStateMessage(mapId, cells);
        }

        public async Task<AoeTemplateStateMessage?> FetchMapAoeTemplatesMessageAsync(string campaignId, string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AoeTemplatesJson FROM Maps WHERE Id = $mid AND CampaignId = $cid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return new AoeTemplateStateMessage(mapId, new List<AoeTemplateMessage>());
            var json = r.IsDBNull(0) ? "[]" : r.GetString(0);
            List<AoeTemplateMessage> placed;
            try { placed = JsonSerializer.Deserialize<List<AoeTemplateMessage>>(json) ?? new List<AoeTemplateMessage>(); }
            catch (JsonException) { placed = new List<AoeTemplateMessage>(); }
            return new AoeTemplateStateMessage(mapId, placed);
        }

        public async Task<MapObjectStateMessage?> FetchMapObjectsMessageAsync(string campaignId, string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MapObjectsJson FROM Maps WHERE Id = $mid AND CampaignId = $cid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return new MapObjectStateMessage(mapId, new List<MapObjectPoint>());
            var json = r.IsDBNull(0) ? "[]" : r.GetString(0);
            List<MapObjectPoint> cells;
            try { cells = JsonSerializer.Deserialize<List<MapObjectPoint>>(json) ?? new List<MapObjectPoint>(); }
            catch (JsonException) { cells = new List<MapObjectPoint>(); }
            return new MapObjectStateMessage(mapId, cells);
        }

        public async Task<CombatStateMessage?> FetchCombatStateMessageAsync(string campaignId, string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);

            string encId = "";
            int round = 0;
            string? activeCombatantId = null;
            bool isActive = false;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Round, ActiveCombatantId, IsActive FROM Encounters WHERE CampaignId = $cid AND MapId = $mid ORDER BY UpdatedAt DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("$cid", campaignId);
                cmd.Parameters.AddWithValue("$mid", mapId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) return null;
                encId = r.GetString(0);
                round = r.GetInt32(1);
                activeCombatantId = r.IsDBNull(2) ? null : r.GetString(2);
                isActive = r.GetInt32(3) == 1;
            }

            var combatants = new List<CombatantSnapshot>();
            await using (var cmd2 = conn.CreateCommand())
            {
                cmd2.CommandText = @"
                    SELECT Id, TokenId, Name, Initiative, CurrentHp, MaxHp, IsPlayerCharacter, RevealExactHp,
                           MaxActions, ActionsRemaining, MaxBonusActions, BonusActionsRemaining, SpellSlotsJson,
                           Concentration, DeathSaveSuccesses, DeathSaveFailures, AttacksJson, IsFriendly, ExtrasJson
                    FROM EncounterCombatants WHERE EncounterId = $eid ORDER BY SortOrder ASC;";
                cmd2.Parameters.AddWithValue("$eid", encId);
                await using var r2 = await cmd2.ExecuteReaderAsync(ct);
                while (await r2.ReadAsync(ct))
                    combatants.Add(new CombatantSnapshot(
                        r2.GetString(0), r2.GetString(2), r2.GetInt32(3), r2.GetInt32(4), r2.GetInt32(5),
                        r2.GetInt32(6) == 1, r2.GetInt32(7) == 1, r2.IsDBNull(1) ? null : r2.GetString(1),
                        r2.GetInt32(8), r2.GetInt32(9), r2.GetInt32(10), r2.GetInt32(11),
                        r2.IsDBNull(12) ? null : r2.GetString(12), r2.GetInt32(13) == 1, r2.GetInt32(14), r2.GetInt32(15),
                        r2.IsDBNull(16) ? null : r2.GetString(16), !r2.IsDBNull(17) && r2.GetInt32(17) == 1,
                        r2.IsDBNull(18) ? null : r2.GetString(18)));
            }

            return new CombatStateMessage(encId, mapId, isActive && round > 0, round, activeCombatantId, combatants);
        }

        public async Task<List<MapToken>> LoadMapTokensAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            return await FetchTokensForMapAsync(conn, mapId, ct);
        }

        private static async Task<List<MapToken>> FetchTokensForMapAsync(
            SqliteConnection conn, string mapId, CancellationToken ct)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                SELECT Id, MapId, CampaignId, OwnerCharacterId,
                       X, Y, TokenImagePath, Label, Scale, Rotation, SizeName, IsProp, Blocks, BlocksSight
                FROM MapTokens
                WHERE MapId = $MapId
            """;
            cmd.Parameters.AddWithValue("$MapId", mapId);

            var results = new List<MapToken>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new MapToken
                {
                    Id = r.GetString(0),
                    MapId = r.GetString(1),
                    CampaignId = r.GetString(2),
                    OwnerCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                    X = r.GetInt32(4),
                    Y = r.GetInt32(5),
                    TokenImagePath = r.GetString(6),
                    Label = r.IsDBNull(7) ? null : r.GetString(7),
                    Scale = r.GetDouble(8),
                    Rotation = r.GetDouble(9),
                    SizeName = r.GetString(10),
                    IsProp = !r.IsDBNull(11) && r.GetInt64(11) == 1,
                    Blocks = r.IsDBNull(12) || r.GetInt64(12) == 1,
                    BlocksSight = !r.IsDBNull(13) && r.GetInt64(13) == 1
                });
            }
            return results;
        }

                private static async Task<List<ItemInstance>> FetchItemInstancesAsync(
            SqliteConnection conn, string campaignId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, BaseItemId, OwnerCharacterId, ParentInstanceId,
                       Quantity, CustomName, StateJson
                FROM ItemInstances
                WHERE CampaignId = $cid
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);

            var results = new List<ItemInstance>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new ItemInstance
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    BaseItemId = r.GetString(2),
                    OwnerCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                    ParentInstanceId = r.IsDBNull(4) ? null : r.GetString(4),
                    Quantity = r.GetInt32(5),
                    CustomName = r.IsDBNull(6) ? null : r.GetString(6),
                    StateJson = r.IsDBNull(7) ? null : r.GetString(7)
                });
            }
            return results;
        }

        private static async Task<List<Currency>> FetchCurrenciesAsync(
            SqliteConnection conn, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, TemplateId, Name, Abbreviation, IsBase, EqualToBase, Color, IconSvg, SortOrder
                FROM Currencies
                ORDER BY SortOrder
            """;

            var results = new List<Currency>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new Currency
                {
                    Id = r.GetString(0),
                    TemplateId = r.IsDBNull(1) ? null : r.GetString(1),
                    Name = r.GetString(2),
                    Abbreviation = r.GetString(3),
                    IsBase = r.GetInt32(4) == 1,
                    EqualToBase = r.GetInt32(5),
                    Color = r.IsDBNull(6) ? null : r.GetString(6),
                    IconSvg = r.IsDBNull(7) ? null : r.GetString(7),
                    SortOrder = r.GetInt32(8)
                });
            }
            return results;
        }

        private static async Task<bool> IsUserDmAsync(
    SqliteConnection conn, string campaignId, string userId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT 1 FROM CampaignMembers
        WHERE CampaignId = $cid AND UserId = $uid AND Role = 'dm'
        LIMIT 1;
    """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", userId);
            return (await cmd.ExecuteScalarAsync(ct)) != null;
        }

        private static async Task<List<NotePage>> FetchVisibleNotePagesAsync(
            SqliteConnection conn, string campaignId, string userId, bool isDm, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT Id, CampaignId, OwnerUserId, ParentPageId, Scope,
               Title, Icon, ContentMarkdown, SortOrder,
               RevisionNumber, CreatedAt, UpdatedAt
        FROM NotePages
        WHERE CampaignId = $cid
          AND (
                ($isDm = 1 AND Scope = 'campaign_story')
             OR (Scope = 'private' AND OwnerUserId = $uid)
             OR (Scope = 'shared'  AND (
                    OwnerUserId = $uid
                 OR Id IN (SELECT PageId FROM NotePageShares WHERE UserId = $uid)
                ))
              )
        ORDER BY Scope, SortOrder, CreatedAt;
    """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$isDm", isDm ? 1 : 0);

            var list = new List<NotePage>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new NotePage
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                    ParentPageId = r.IsDBNull(3) ? null : r.GetString(3),
                    Scope = r.GetString(4),
                    Title = r.GetString(5),
                    Icon = r.IsDBNull(6) ? null : r.GetString(6),
                    ContentMarkdown = r.GetString(7),
                    SortOrder = r.GetInt32(8),
                    RevisionNumber = r.GetInt32(9),
                    CreatedAt = DateTime.Parse(r.GetString(10)),
                    UpdatedAt = DateTime.Parse(r.GetString(11)),
                });
            }
            return list;
        }

        private static async Task<List<NotePageShare>> FetchUserSharesAsync(
            SqliteConnection conn, string userId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT PageId, UserId, Permission, SharedAt
                FROM NotePageShares
                WHERE UserId = $uid;
            """;
            cmd.Parameters.AddWithValue("$uid", userId);

            var list = new List<NotePageShare>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new NotePageShare
                {
                    PageId = r.GetString(0),
                    UserId = r.GetString(1),
                    Permission = r.GetString(2),
                    SharedAt = DateTime.Parse(r.GetString(3))
                });
            }
            return list;
        }

        public async Task SaveChatChannelAsync(ChatChannel channel, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ChatChannels (Id, CampaignId, Name, Description, CreatedAt)
                VALUES ($Id, $CampaignId, $Name, $Description, $CreatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    Name        = excluded.Name,
                    Description = excluded.Description
            """;
            cmd.Parameters.AddWithValue("$Id", channel.Id);
            cmd.Parameters.AddWithValue("$CampaignId", channel.CampaignId);
            cmd.Parameters.AddWithValue("$Name", channel.Name);
            cmd.Parameters.AddWithValue("$Description", (object?)channel.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$CreatedAt", channel.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task SaveChatMessageAsync(ChatMessage msg, CancellationToken ct = default)
        {
                await using var conn = await _db.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO ChatMessages (Id, CampaignId, ChannelId, UserId, Message, Timestamp)
                    VALUES ($Id, $CampaignId, $ChannelId, $UserId, $Message, $Timestamp)
                """;
            cmd.Parameters.AddWithValue("$Id", msg.Id);
            cmd.Parameters.AddWithValue("$CampaignId", msg.CampaignId);
            cmd.Parameters.AddWithValue("$ChannelId", msg.ChannelId);
            cmd.Parameters.AddWithValue("$UserId", msg.UserId);
            cmd.Parameters.AddWithValue("$Message", msg.Message);
            cmd.Parameters.AddWithValue("$Timestamp", msg.Timestamp.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task AppendSessionLogAsync(SessionLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) return;
            if (string.IsNullOrEmpty(entry.CampaignId)) entry.CampaignId = _ctx.CampaignId;

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO SessionLog (Id, CampaignId, SessionId, Timestamp, ActorUserId, ActorName, EventType, Summary, DetailJson)
                VALUES ($Id, $CampaignId, $SessionId, $Timestamp, $ActorUserId, $ActorName, $EventType, $Summary, $DetailJson)
            """;
            cmd.Parameters.AddWithValue("$Id", entry.Id);
            cmd.Parameters.AddWithValue("$CampaignId", entry.CampaignId);
            cmd.Parameters.AddWithValue("$SessionId", (object?)entry.SessionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Timestamp", entry.Timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("$ActorUserId", (object?)entry.ActorUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ActorName", (object?)entry.ActorName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$EventType", entry.EventType);
            cmd.Parameters.AddWithValue("$Summary", entry.Summary);
            cmd.Parameters.AddWithValue("$DetailJson", (object?)entry.DetailJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<string?> GetDmUserIdAsync(string campaignId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT UserId FROM CampaignMembers WHERE CampaignId = $cid AND Role = 'dm' LIMIT 1";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            return await cmd.ExecuteScalarAsync(ct) as string;
        }

        public Task<string?> GetActiveDmUserIdAsync(CancellationToken ct = default) => GetDmUserIdAsync(_ctx.CampaignId, ct);

        public async Task<string?> GetActiveJoinSecretAsync(CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT JoinSecret FROM Campaigns WHERE Id = $cid LIMIT 1";
            cmd.Parameters.AddWithValue("$cid", _ctx.CampaignId);
            return await cmd.ExecuteScalarAsync(ct) as string;
        }

        public async Task<bool> IsChangeAuthorizedAsync(string callerUserId, ChangeNotification change, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(callerUserId) || change == null) return false;
            var type = (change.EntityType ?? "").Trim();

            await using var conn = await _db.OpenAsync(ct);

            if (string.Equals(type, "Character", StringComparison.OrdinalIgnoreCase))
            {
                var (found, owner) = await OwnerOfAsync(conn, "SELECT OwnerUserId FROM Characters WHERE Id = $id LIMIT 1", change.EntityId, ct);
                if (found) return owner == callerUserId;
                var claimed = TryParse<Character>(change.Payload);
                return claimed != null && claimed.Id == change.EntityId && claimed.OwnerUserId == callerUserId;
            }

            if (string.Equals(type, "ItemInstance", StringComparison.OrdinalIgnoreCase))
            {
                var (found, owner) = await OwnerOfAsync(conn,
                    "SELECT c.OwnerUserId FROM ItemInstances i JOIN Characters c ON c.Id = i.OwnerCharacterId WHERE i.Id = $id LIMIT 1",
                    change.EntityId, ct);
                if (found) return owner == callerUserId;
                if (string.Equals(change.ChangeType, "removed", StringComparison.OrdinalIgnoreCase)) return true;
                var inst = TryParse<ItemInstance>(change.Payload);
                if (inst == null || string.IsNullOrEmpty(inst.OwnerCharacterId)) return false;
                var (cfound, cowner) = await OwnerOfAsync(conn, "SELECT OwnerUserId FROM Characters WHERE Id = $id LIMIT 1", inst.OwnerCharacterId, ct);
                return cfound && cowner == callerUserId;
            }

            return false;
        }

        public async Task<long> RecordChangeAsync(string campaignId, ChangeNotification change, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO ChangeLog (CampaignId, EntityType, EntityId, ChangeType, RevisionNumber, Timestamp, Payload)
                    VALUES ($cid, $etype, $eid, $ctype, $rev, $ts, $payload);";
                cmd.Parameters.AddWithValue("$cid", campaignId);
                cmd.Parameters.AddWithValue("$etype", change.EntityType ?? "");
                cmd.Parameters.AddWithValue("$eid", change.EntityId ?? "");
                cmd.Parameters.AddWithValue("$ctype", change.ChangeType ?? "");
                cmd.Parameters.AddWithValue("$rev", change.RevisionNumber);
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$payload", (object?)change.Payload ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            long id;
            await using (var idCmd = conn.CreateCommand())
            {
                idCmd.CommandText = "SELECT last_insert_rowid();";
                var result = await idCmd.ExecuteScalarAsync(ct);
                id = result == null ? 0 : Convert.ToInt64(result);
            }
            // Keep the log bounded, a client offline long enough to fall past this just re-bootstraps
            if (id > 0 && id % 500 == 0) await PruneChangeLogAsync(conn, campaignId, 10000, ct);
            return id;
        }

        internal static async Task PruneChangeLogAsync(SqliteConnection conn, string campaignId, int keep, CancellationToken ct = default)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM ChangeLog
                WHERE CampaignId = $cid
                  AND ChangeId NOT IN (
                    SELECT ChangeId FROM ChangeLog WHERE CampaignId = $cid ORDER BY ChangeId DESC LIMIT $keep
                  );";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$keep", keep);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<ChangeNotification>> GetChangesSinceAsync(string campaignId, long sinceChangeId, CancellationToken ct = default)
        {
            var list = new List<ChangeNotification>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ChangeId, EntityType, EntityId, ChangeType, RevisionNumber, Payload
                FROM ChangeLog WHERE CampaignId = $cid AND ChangeId > $since ORDER BY ChangeId;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$since", sinceChangeId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(new ChangeNotification
                {
                    ChangeId = r.GetInt64(0),
                    EntityType = r.GetString(1),
                    EntityId = r.GetString(2),
                    ChangeType = r.GetString(3),
                    RevisionNumber = r.GetInt32(4),
                    Payload = r.IsDBNull(5) ? null : r.GetString(5)
                });
            return list;
        }

        private static async Task<long> FetchMaxChangeIdAsync(SqliteConnection conn, string campaignId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(ChangeId), 0) FROM ChangeLog WHERE CampaignId = $cid;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result == null ? 0 : Convert.ToInt64(result);
        }

        public async Task<string?> GetTokenOwnerUserIdAsync(string tokenId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(tokenId)) return null;
            await using var conn = await _db.OpenAsync(ct);
            var (found, owner) = await OwnerOfAsync(conn,
                "SELECT c.OwnerUserId FROM MapTokens t JOIN Characters c ON c.Id = t.OwnerCharacterId WHERE t.Id = $id LIMIT 1",
                tokenId, ct);
            return found ? owner : null;
        }

        public async Task<string?> GetCharacterOwnerUserIdAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            await using var conn = await _db.OpenAsync(ct);
            var (found, owner) = await OwnerOfAsync(conn, "SELECT OwnerUserId FROM Characters WHERE Id = $id LIMIT 1", characterId, ct);
            return found ? owner : null;
        }

        public async Task<string> GetUsernameAsync(string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(userId)) return "";
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Username FROM Users WHERE Id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", userId);
            var name = await cmd.ExecuteScalarAsync(ct);
            return name as string ?? "";
        }

        private static async Task<(bool Found, string? Owner)> OwnerOfAsync(SqliteConnection conn, string sql, string id, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return (false, null);
            return (true, r.IsDBNull(0) ? null : r.GetString(0));
        }

        private static T? TryParse<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<T>(json); }
            catch (JsonException) { return null; }
        }

        public async Task EnsureMemberAsync(string userId, string username)
        {
            await using var conn = await _db.OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                await using (var u = conn.CreateCommand())
                {
                    u.Transaction = tx;
                    u.CommandText = """
                        INSERT OR IGNORE INTO Users (Id, Username, CreatedAt)
                        VALUES ($id, $name, $now)
                        """;
                    u.Parameters.AddWithValue("$id", userId);
                    u.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(username) ? userId : username);
                    u.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    await u.ExecuteNonQueryAsync();
                }

                await using (var m = conn.CreateCommand())
                {
                    m.Transaction = tx;
                    m.CommandText = """
                        INSERT OR IGNORE INTO CampaignMembers (CampaignId, UserId, Role, CharacterId, JoinedAt)
                        VALUES ($cid, $uid, 'player', NULL, $now)
                        """;
                    m.Parameters.AddWithValue("$cid", _ctx.CampaignId);
                    m.Parameters.AddWithValue("$uid", userId);
                    m.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    await m.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task SaveDiceRollAsync(string campaignId, DiceRollMessage roll, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO DiceRolls (Id, CampaignId, UserId, Username, Expression, Total, Breakdown, Label, IsPrivate, Timestamp)
                VALUES ($Id, $CampaignId, $UserId, $Username, $Expression, $Total, $Breakdown, $Label, $IsPrivate, $Timestamp)
            """;
            cmd.Parameters.AddWithValue("$Id", roll.RollId);
            cmd.Parameters.AddWithValue("$CampaignId", campaignId);
            cmd.Parameters.AddWithValue("$UserId", roll.UserId);
            cmd.Parameters.AddWithValue("$Username", roll.Username);
            cmd.Parameters.AddWithValue("$Expression", roll.Expression);
            cmd.Parameters.AddWithValue("$Total", roll.Total);
            cmd.Parameters.AddWithValue("$Breakdown", roll.Breakdown);
            cmd.Parameters.AddWithValue("$Label", (object?)roll.Label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$IsPrivate", roll.IsPrivate ? 1 : 0);
            cmd.Parameters.AddWithValue("$Timestamp", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<TradeApplyResult> ApplyTradeAsync(string campaignId, TradeOfferMessage offer, CancellationToken ct = default)
        {
            var res = new TradeApplyResult();
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                if (!string.IsNullOrEmpty(offer.TradeId))
                {
                    await using var dupe = conn.CreateCommand();
                    dupe.Transaction = tx;
                    dupe.CommandText = "SELECT 1 FROM TradeLog WHERE TradeId = $tid LIMIT 1";
                    dupe.Parameters.AddWithValue("$tid", offer.TradeId);
                    if (await dupe.ExecuteScalarAsync(ct) != null)
                    {
                        await tx.RollbackAsync(ct);
                        res.Result = new TradeResultMessage(offer.TradeId, true, "", "");
                        return res;
                    }
                }

                var fromRow = await ReadCharacterRowAsync(conn, tx, offer.From.CharacterId, ct);
                var toRow = await ReadCharacterRowAsync(conn, tx, offer.To.CharacterId, ct);
                if (fromRow == null || toRow == null)
                {
                    await tx.RollbackAsync(ct);
                    res.Result = new TradeResultMessage(offer.TradeId, false, "", "One of the characters no longer exists.");
                    return res;
                }

                var fromRt = CharacterMapper.ToRuntime(fromRow);
                var toRt = CharacterMapper.ToRuntime(toRow);

                var fromInstances = await LoadInstancesByIdsAsync(conn, tx, campaignId, offer.From.Items.Select(i => i.InstanceId), ct);
                var toInstances = await LoadInstancesByIdsAsync(conn, tx, campaignId, offer.To.Items.Select(i => i.InstanceId), ct);

                var reason = ValidateSide(offer.From, fromRt, fromInstances) ?? ValidateSide(offer.To, toRt, toInstances);
                if (reason != null)
                {
                    await tx.RollbackAsync(ct);
                    res.Result = new TradeResultMessage(offer.TradeId, false, "", reason);
                    return res;
                }

                foreach (var line in offer.From.Items)
                    await ReassignInstanceAsync(conn, tx, line.InstanceId, offer.To.CharacterId, ct);
                foreach (var line in offer.To.Items)
                    await ReassignInstanceAsync(conn, tx, line.InstanceId, offer.From.CharacterId, ct);

                bool anyCurrency = offer.From.Currency.Count > 0 || offer.To.Currency.Count > 0;
                foreach (var c in offer.From.Currency) { Adjust(fromRt.Wallet, c.CurrencyId, -c.Amount); Adjust(toRt.Wallet, c.CurrencyId, c.Amount); }
                foreach (var c in offer.To.Currency) { Adjust(toRt.Wallet, c.CurrencyId, -c.Amount); Adjust(fromRt.Wallet, c.CurrencyId, c.Amount); }

                if (anyCurrency)
                {
                    var fromState = CharacterMapper.ToRow(fromRt).StateJson;
                    var toState = CharacterMapper.ToRow(toRt).StateJson;
                    await UpdateStateJsonAsync(conn, tx, fromRow.Id, fromState, ct);
                    await UpdateStateJsonAsync(conn, tx, toRow.Id, toState, ct);
                    fromRow.StateJson = fromState;
                    toRow.StateJson = toState;
                    res.ChangedCharacters.Add(ToChange("Character", fromRow.Id, JsonSerializer.Serialize(fromRow)));
                    res.ChangedCharacters.Add(ToChange("Character", toRow.Id, JsonSerializer.Serialize(toRow)));
                }

                foreach (var inst in fromInstances)
                {
                    inst.OwnerCharacterId = offer.To.CharacterId;
                    inst.ParentInstanceId = null;
                    res.ChangedInstances.Add(ToChange("ItemInstance", inst.Id, JsonSerializer.Serialize(inst)));
                }
                foreach (var inst in toInstances)
                {
                    inst.OwnerCharacterId = offer.From.CharacterId;
                    inst.ParentInstanceId = null;
                    res.ChangedInstances.Add(ToChange("ItemInstance", inst.Id, JsonSerializer.Serialize(inst)));
                }

                var summary = BuildTradeSummary(offer);
                await InsertTradeLogAsync(conn, tx, campaignId, offer, summary, ct);

                await tx.CommitAsync(ct);
                res.Result = new TradeResultMessage(offer.TradeId, true, summary, null);
                return res;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                res.Result = new TradeResultMessage(offer.TradeId, false, "", ex.Message);
                return res;
            }
        }

        public async Task<List<TradeLogEntry>> LoadTradeLogAsync(string campaignId, int limit = 100, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, FromCharacterId, ToCharacterId, FromUserId, ToUserId, Summary, PayloadJson, CreatedAt
                FROM TradeLog
                WHERE CampaignId = $cid
                ORDER BY CreatedAt DESC
                LIMIT $limit
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$limit", limit);

            var results = new List<TradeLogEntry>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new TradeLogEntry
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    FromCharacterId = r.IsDBNull(2) ? null : r.GetString(2),
                    ToCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                    FromUserId = r.IsDBNull(4) ? null : r.GetString(4),
                    ToUserId = r.IsDBNull(5) ? null : r.GetString(5),
                    Summary = r.GetString(6),
                    PayloadJson = r.GetString(7),
                    CreatedAt = DateTime.Parse(r.GetString(8))
                });
            }
            return results;
        }

        private static async Task<Character?> ReadCharacterRowAsync(
            SqliteConnection conn, SqliteTransaction tx, string characterId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                       Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson,
                       CharacterKind, Slug, Tags, CreatedAt
                FROM Characters WHERE Id = $id
            """;
            cmd.Parameters.AddWithValue("$id", characterId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            return new Character
            {
                Id = r.GetString(0),
                CampaignId = r.GetString(1),
                OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                Name = r.GetString(3),
                RaceId = r.IsDBNull(4) ? null : r.GetString(4),
                SubraceId = r.IsDBNull(5) ? null : r.GetString(5),
                ClassId = r.IsDBNull(6) ? null : r.GetString(6),
                Level = r.GetInt32(7),
                CurrentHp = r.GetInt32(8),
                MaxHp = r.GetInt32(9),
                AbilityScoresJson = r.GetString(10),
                InventoryJson = r.IsDBNull(11) ? null : r.GetString(11),
                StateJson = r.IsDBNull(12) ? null : r.GetString(12),
                CharacterKind = r.IsDBNull(13) ? "pc" : r.GetString(13),
                Slug = r.IsDBNull(14) ? null : r.GetString(14),
                Tags = r.IsDBNull(15) ? "[]" : r.GetString(15),
                CreatedAt = DateTime.Parse(r.GetString(16))
            };
        }

        private static async Task<List<ItemInstance>> LoadInstancesByIdsAsync(
            SqliteConnection conn, SqliteTransaction tx, string campaignId,
            IEnumerable<string> ids, CancellationToken ct)
        {
            var idList = ids.ToList();
            var results = new List<ItemInstance>();
            if (idList.Count == 0) return results;

            var names = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            for (int i = 0; i < idList.Count; i++)
            {
                var pn = "$i" + i;
                names.Add(pn);
                cmd.Parameters.AddWithValue(pn, idList[i]);
            }
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.CommandText =
                "SELECT Id, CampaignId, BaseItemId, OwnerCharacterId, ParentInstanceId, Quantity, CustomName, StateJson " +
                "FROM ItemInstances WHERE CampaignId = $cid AND Id IN (" + string.Join(", ", names) + ")";

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new ItemInstance
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    BaseItemId = r.GetString(2),
                    OwnerCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                    ParentInstanceId = r.IsDBNull(4) ? null : r.GetString(4),
                    Quantity = r.GetInt32(5),
                    CustomName = r.IsDBNull(6) ? null : r.GetString(6),
                    StateJson = r.IsDBNull(7) ? null : r.GetString(7)
                });
            }
            return results;
        }

        private static string? ValidateSide(TradeSide side, CharacterRuntime rt, List<ItemInstance> instances)
        {
            foreach (var line in side.Items)
            {
                var inst = instances.FirstOrDefault(x => x.Id == line.InstanceId);
                if (inst == null) return $"{line.Name} is not available to trade.";
                if (inst.OwnerCharacterId != side.CharacterId) return $"{side.CharacterName} no longer owns {line.Name}.";
            }
            foreach (var c in side.Currency)
            {
                if (c.Amount <= 0) return "Trade has an invalid currency amount.";
                var have = rt.Wallet.TryGetValue(c.CurrencyId, out var v) ? v : 0;
                if (have < c.Amount) return $"{side.CharacterName} doesn't have enough {c.CurrencyId}.";
            }
            return null;
        }

        private static async Task ReassignInstanceAsync(
            SqliteConnection conn, SqliteTransaction tx, string instanceId, string newOwnerId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE ItemInstances SET OwnerCharacterId = $o, ParentInstanceId = NULL WHERE Id = $id";
            cmd.Parameters.AddWithValue("$o", newOwnerId);
            cmd.Parameters.AddWithValue("$id", instanceId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task UpdateStateJsonAsync(
            SqliteConnection conn, SqliteTransaction tx, string characterId, string? stateJson, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Characters SET StateJson = $s WHERE Id = $id";
            cmd.Parameters.AddWithValue("$s", (object?)stateJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", characterId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task InsertTradeLogAsync(
            SqliteConnection conn, SqliteTransaction tx, string campaignId, TradeOfferMessage offer, string summary, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO TradeLog (Id, CampaignId, FromCharacterId, ToCharacterId, FromUserId, ToUserId, Summary, PayloadJson, CreatedAt, TradeId)
                VALUES ($id, $cid, $fromC, $toC, $fromU, $toU, $sum, $payload, $now, $tid)
            """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$tid", (object?)offer.TradeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$fromC", (object?)offer.From.CharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$toC", (object?)offer.To.CharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$fromU", (object?)offer.From.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$toU", (object?)offer.To.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sum", summary);
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(offer));
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static string BuildTradeSummary(TradeOfferMessage offer)
        {
            return $"{offer.From.CharacterName} gave {TradeSideSummary(offer.From)}; {offer.To.CharacterName} gave {TradeSideSummary(offer.To)}";
        }

        private static string TradeSideSummary(TradeSide s)
        {
            var parts = new List<string>();
            if (s.Items.Count > 0) parts.Add($"{s.Items.Count} item(s)");
            foreach (var c in s.Currency) parts.Add($"{c.Amount} {c.CurrencyId}");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        private static void Adjust(Dictionary<string, long> wallet, string currencyId, long delta)
        {
            var current = wallet.TryGetValue(currencyId, out var v) ? v : 0;
            wallet[currencyId] = current + delta;
        }

        private static ChangeNotification ToChange(string entityType, string entityId, string payload) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = "updated",
            RevisionNumber = 0,
            Payload = payload
        };

        public sealed class TradeApplyResult
        {
            public TradeResultMessage Result { get; set; } = null!;
            public List<ChangeNotification> ChangedCharacters { get; } = new();
            public List<ChangeNotification> ChangedInstances { get; } = new();
        }

        public async Task<List<ChatChannel>> LoadChannelsAsync(string campaignId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            return await FetchChannelsAsync(conn, campaignId, ct);
        }

        public async Task<List<ChatMessage>> LoadMessagesAsync(
            string channelId, int limit = 200, CancellationToken ct = default)
                {
                    await using var conn = await _db.OpenAsync(ct);
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                SELECT m.Id, m.CampaignId, m.ChannelId, m.UserId,
                       u.Username, m.Message, m.Timestamp
                FROM ChatMessages m
                INNER JOIN Users u ON u.Id = m.UserId
                WHERE m.ChannelId = $ChannelId
                ORDER BY m.Timestamp DESC
                LIMIT $Limit
            """;
            cmd.Parameters.AddWithValue("$ChannelId", channelId);
            cmd.Parameters.AddWithValue("$Limit", limit);

            var results = new List<ChatMessage>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new ChatMessage
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    ChannelId = r.GetString(2),
                    UserId = r.GetString(3),
                    Sender = r.GetString(4),
                    Message = r.GetString(5),
                    Timestamp = DateTime.Parse(r.GetString(6))
                });
            }

            results.Reverse();
            return results;
        }

        private static async Task<List<ChatChannel>> FetchChannelsAsync(
            SqliteConnection conn, string campaignId, CancellationToken ct)
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                SELECT Id, CampaignId, Name, Description, CreatedAt
                FROM ChatChannels
                WHERE CampaignId = $CampaignId
                ORDER BY CreatedAt ASC
            """;
            cmd.Parameters.AddWithValue("$CampaignId", campaignId);

            var results = new List<ChatChannel>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                results.Add(new ChatChannel
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    CreatedAt = DateTime.Parse(r.GetString(4))
                });
            }
            return results;
        }
    }
}
