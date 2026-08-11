using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dujahit.Models;
using Dujahit.Models.UI;
using Dujahit.Models.Application;
using Dujahit.Models.Communication;
using System.Text.Json;

namespace Dujahit.Models.Database
{
    public class GameDataRepository
    {
        private readonly DatabaseManager _db;
        public GameDataRepository(DatabaseManager db) => _db = db;

        public async Task<List<Character>> LoadCharactersAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<Character>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                       Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson, CharacterKind, Slug, Tags, CreatedAt
                FROM Characters
                WHERE CampaignId = $cid
                """;
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new Character
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                    Name = r.GetString(3),
                    RaceId = r.IsDBNull(4) ? null : r.GetString(4),
                    SubraceId = r.IsDBNull(5) ? null : r.GetString(5),
                    ClassId = r.IsDBNull(6) ? null : r.GetString(6),
                    Level = (int)r.GetInt64(7),
                    CurrentHp = (int)r.GetInt64(8),
                    MaxHp = (int)r.GetInt64(9),
                    AbilityScoresJson = r.GetString(10),
                    InventoryJson = r.IsDBNull(11) ? null : r.GetString(11),
                    StateJson = r.IsDBNull(12) ? null : r.GetString(12),
                    CharacterKind = r.GetString(13),
                    Slug = r.IsDBNull(14) ? null : r.GetString(14),
                    Tags = r.GetString(15),
                    CreatedAt = DateTime.Parse(r.GetString(16))
                });
            }
            return list;
        }

        public async Task<Character?> LoadCharacterAsync(string characterId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                       Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson, CharacterKind, Slug, Tags, CreatedAt
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
                Level = (int)r.GetInt64(7),
                CurrentHp = (int)r.GetInt64(8),
                MaxHp = (int)r.GetInt64(9),
                AbilityScoresJson = r.GetString(10),
                InventoryJson = r.IsDBNull(11) ? null : r.GetString(11),
                StateJson = r.IsDBNull(12) ? null : r.GetString(12),
                CharacterKind = r.GetString(13),
                Slug = r.IsDBNull(14) ? null : r.GetString(14),
                Tags = r.GetString(15),
                CreatedAt = DateTime.Parse(r.GetString(16))
            };
        }

        public async Task SaveCharacterAsync(Character c, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Characters
                    (Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                     Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson, CreatedAt)
                VALUES
                    ($id, $cid, $oid, $name, $rid, $srid, $clid,
                     $lvl, $chp, $mhp, $abil, $inv, $state, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    RaceId = excluded.RaceId,
                    SubraceId = excluded.SubraceId,
                    ClassId = excluded.ClassId,
                    Level = excluded.Level,
                    CurrentHp = excluded.CurrentHp,
                    MaxHp = excluded.MaxHp,
                    AbilityScoresJson = excluded.AbilityScoresJson,
                    InventoryJson = excluded.InventoryJson,
                    StateJson = excluded.StateJson
                """;
            cmd.Parameters.AddWithValue("$id", c.Id);
            cmd.Parameters.AddWithValue("$cid", c.CampaignId);
            cmd.Parameters.AddWithValue("$oid", (object?)c.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$name", c.Name);
            cmd.Parameters.AddWithValue("$rid", (object?)c.RaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$srid", (object?)c.SubraceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$clid", (object?)c.ClassId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lvl", c.Level);
            cmd.Parameters.AddWithValue("$chp", c.CurrentHp);
            cmd.Parameters.AddWithValue("$mhp", c.MaxHp);
            cmd.Parameters.AddWithValue("$abil", c.AbilityScoresJson);
            cmd.Parameters.AddWithValue("$inv", (object?)c.InventoryJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$state", (object?)c.StateJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", c.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<Item>> LoadCampaignItemsAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<Item>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT i.Id, i.Name, i.ItemType, i.Source, i.RevisionNumber, i.DataJson
                FROM Items i
                JOIN CampaignItems ci ON ci.ItemId = i.Id
                WHERE ci.CampaignId = $cid AND ci.IsEnabled = 1
                ORDER BY i.Name
                """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new Item
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    ItemType = r.GetString(2),
                    Source = r.GetString(3),
                    RevisionNumber = (int)r.GetInt64(4),
                    DataJson = r.GetString(5)
                });
            }
            return list;
        }

        public async Task UpdateItemAsync(string itemId, string name, string dataJson, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Items
                   SET Name = $name,
                       DataJson = $json,
                       RevisionNumber = RevisionNumber + 1,
                       UpdatedAt = $now
                 WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", itemId);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$json", dataJson);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<Map>> LoadMapsAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<Map>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, Name, Width, Height, Scale, MapPath, CreatedAt, GridKind
                FROM Maps WHERE CampaignId = $cid
                """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new Map
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Width = (int)r.GetInt64(3),
                    Height = (int)r.GetInt64(4),
                    Scale = r.GetDouble(5),
                    MapPath = r.GetString(6),
                    CreatedAt = DateTime.Parse(r.GetString(7)),
                    GridKind = Enum.TryParse<GridKind>(r.GetString(8), out var gk) ? gk : GridKind.Squares
                });
            }
            return list;
        }

        public async Task SaveMapAsync(Map m, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Maps (Id, CampaignId, Name, Width, Height, Scale, MapPath, CreatedAt, GridKind)
                VALUES ($id, $cid, $name, $w, $h, $s, $path, $created, $grid)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Width = excluded.Width,
                    Height = excluded.Height,
                    Scale = excluded.Scale,
                    MapPath = excluded.MapPath,
                    GridKind = excluded.GridKind
                """;
            cmd.Parameters.AddWithValue("$id", m.Id);
            cmd.Parameters.AddWithValue("$cid", m.CampaignId);
            cmd.Parameters.AddWithValue("$name", m.Name);
            cmd.Parameters.AddWithValue("$w", m.Width);
            cmd.Parameters.AddWithValue("$h", m.Height);
            cmd.Parameters.AddWithValue("$s", m.Scale);
            cmd.Parameters.AddWithValue("$path", m.MapPath);
            cmd.Parameters.AddWithValue("$created", m.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$grid", m.GridKind.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<Handout>> ListHandoutsAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<Handout>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, Name, HandoutPath, CreatedAt
                FROM Handouts WHERE CampaignId = $cid
                ORDER BY CreatedAt
                """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new Handout
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    HandoutPath = r.GetString(3),
                    CreatedAt = DateTime.Parse(r.GetString(4))
                });
            }
            return list;
        }

        public async Task AddHandoutAsync(Handout h, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Handouts (Id, CampaignId, Name, HandoutPath, CreatedAt)
                VALUES ($id, $cid, $name, $path, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    HandoutPath = excluded.HandoutPath
                """;
            cmd.Parameters.AddWithValue("$id", h.Id);
            cmd.Parameters.AddWithValue("$cid", h.CampaignId);
            cmd.Parameters.AddWithValue("$name", h.Name);
            cmd.Parameters.AddWithValue("$path", h.HandoutPath);
            cmd.Parameters.AddWithValue("$created", h.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task UpdateHandoutPathAsync(string handoutId, string path, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Handouts SET HandoutPath = $path WHERE Id = $id";
            cmd.Parameters.AddWithValue("$path", path);
            cmd.Parameters.AddWithValue("$id", handoutId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteHandoutAsync(string handoutId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Handouts WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", handoutId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<MapToken>> LoadTokensAsync(string mapId, CancellationToken ct = default)
        {
            var list = new List<MapToken>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, MapId, CampaignId, OwnerCharacterId, X, Y, TokenImagePath, Label, Scale, Rotation, SizeName, IsProp, Blocks, BlocksSight
                FROM MapTokens WHERE MapId = $mid
                """;
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new MapToken
                {
                    Id = r.GetString(0),
                    MapId = r.GetString(1),
                    CampaignId = r.GetString(2),
                    OwnerCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                    X = (int)r.GetInt64(4),
                    Y = (int)r.GetInt64(5),
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
            return list;
        }

        public async Task SaveTokenAsync(MapToken t, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO MapTokens (Id, MapId, CampaignId, OwnerCharacterId, X, Y, TokenImagePath, Label, Scale, Rotation, SizeName, IsProp, Blocks, BlocksSight)
                VALUES ($id, $mid, $cid, $oid, $x, $y, $img, $label, $scale, $rot, $size, $prop, $blocks, $bsight)
                ON CONFLICT(Id) DO UPDATE SET
                    OwnerCharacterId = excluded.OwnerCharacterId,
                    X = excluded.X,
                    Y = excluded.Y,
                    TokenImagePath = excluded.TokenImagePath,
                    Label = excluded.Label,
                    Scale = excluded.Scale,
                    Rotation = excluded.Rotation,
                    SizeName = excluded.SizeName,
                    IsProp = excluded.IsProp,
                    Blocks = excluded.Blocks,
                    BlocksSight = excluded.BlocksSight
                """;
            cmd.Parameters.AddWithValue("$id", t.Id);
            cmd.Parameters.AddWithValue("$mid", t.MapId);
            cmd.Parameters.AddWithValue("$cid", t.CampaignId);
            cmd.Parameters.AddWithValue("$oid", (object?)t.OwnerCharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$x", t.X);
            cmd.Parameters.AddWithValue("$y", t.Y);
            cmd.Parameters.AddWithValue("$img", t.TokenImagePath);
            cmd.Parameters.AddWithValue("$label", (object?)t.Label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$scale", t.Scale);
            cmd.Parameters.AddWithValue("$rot", t.Rotation);
            cmd.Parameters.AddWithValue("$size", t.SizeName);
            cmd.Parameters.AddWithValue("$prop", t.IsProp ? 1 : 0);
            cmd.Parameters.AddWithValue("$blocks", t.Blocks ? 1 : 0);
            cmd.Parameters.AddWithValue("$bsight", t.BlocksSight ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task SaveStrokeAsync(string mapId, StrokeMessage stroke, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO MapDrawings (Id, MapId, UserId, StrokeDataJson, Timestamp)
                VALUES ($id, $mid, $uid, $json, $ts)
                ON CONFLICT(Id) DO UPDATE SET StrokeDataJson = excluded.StrokeDataJson
                """;
            cmd.Parameters.AddWithValue("$id", stroke.StrokeId);
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$uid", (object?)stroke.OwnerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(stroke));
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<StrokeMessage>> LoadStrokesAsync(string mapId, CancellationToken ct = default)
        {
            var result = new List<StrokeMessage>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT StrokeDataJson FROM MapDrawings WHERE MapId = $mid ORDER BY Timestamp";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<StrokeMessage>(r.GetString(0));
                    if (s != null) result.Add(s);
                }
                catch (JsonException) { }
            }
            return result;
        }

        public async Task DeleteStrokeAsync(string strokeId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM MapDrawings WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", strokeId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteTokenAsync(string tokenId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM MapTokens WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", tokenId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<MapFogState> LoadFogAsync(string mapId, CancellationToken ct = default)
        {
            var state = new MapFogState { MapId = mapId };
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CampaignId, Enabled, Cols, Rows, HiddenCells FROM MapFog WHERE MapId = $mid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return state;
            state.CampaignId = r.GetString(0);
            state.Enabled = r.GetInt64(1) != 0;
            state.Cols = (int)r.GetInt64(2);
            state.Rows = (int)r.GetInt64(3);
            var csv = r.IsDBNull(4) ? "" : r.GetString(4);
            foreach (var pair in csv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = pair.Split(',');
                if (bits.Length == 2 && int.TryParse(bits[0], out var c) && int.TryParse(bits[1], out var rr))
                    state.Hidden.Add((c, rr));
            }
            return state;
        }

        public async Task SaveFogAsync(MapFogState state, CancellationToken ct = default)
        {
            var csv = string.Join(';', state.Hidden.Select(c => c.Col + "," + c.Row));
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO MapFog (MapId, CampaignId, Enabled, Cols, Rows, HiddenCells, UpdatedAt)
                VALUES ($mid, $cid, $en, $cols, $rows, $cells, $now)
                ON CONFLICT(MapId) DO UPDATE SET
                    CampaignId = excluded.CampaignId,
                    Enabled = excluded.Enabled,
                    Cols = excluded.Cols,
                    Rows = excluded.Rows,
                    HiddenCells = excluded.HiddenCells,
                    UpdatedAt = excluded.UpdatedAt
                """;
            cmd.Parameters.AddWithValue("$mid", state.MapId);
            cmd.Parameters.AddWithValue("$cid", state.CampaignId);
            cmd.Parameters.AddWithValue("$en", state.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$cols", state.Cols);
            cmd.Parameters.AddWithValue("$rows", state.Rows);
            cmd.Parameters.AddWithValue("$cells", csv);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task SaveWallsAsync(string mapId, bool enabled, string wallsJson, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Maps SET WallsEnabled = $en, WallsJson = $json WHERE Id = $mid;";
            cmd.Parameters.AddWithValue("$en", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$json", wallsJson);
            cmd.Parameters.AddWithValue("$mid", mapId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task SaveDifficultTerrainAsync(string mapId, string cellsJson, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Maps SET DifficultTerrainJson = $json WHERE Id = $mid;";
            cmd.Parameters.AddWithValue("$json", cellsJson);
            cmd.Parameters.AddWithValue("$mid", mapId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<FogCellPoint>> LoadDifficultTerrainAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DifficultTerrainJson FROM Maps WHERE Id = $mid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return new List<FogCellPoint>();
            var json = r.IsDBNull(0) ? "[]" : r.GetString(0);
            try { return JsonSerializer.Deserialize<List<FogCellPoint>>(json) ?? new List<FogCellPoint>(); }
            catch (JsonException) { return new List<FogCellPoint>(); }
        }

        public async Task SaveAoeTemplatesAsync(string mapId, string templatesJson, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Maps SET AoeTemplatesJson = $json WHERE Id = $mid;";
            cmd.Parameters.AddWithValue("$json", templatesJson);
            cmd.Parameters.AddWithValue("$mid", mapId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<AoeTemplateMessage>> LoadAoeTemplatesAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AoeTemplatesJson FROM Maps WHERE Id = $mid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return new List<AoeTemplateMessage>();
            var json = r.IsDBNull(0) ? "[]" : r.GetString(0);
            try { return JsonSerializer.Deserialize<List<AoeTemplateMessage>>(json) ?? new List<AoeTemplateMessage>(); }
            catch (JsonException) { return new List<AoeTemplateMessage>(); }
        }

        public async Task SaveMapObjectsAsync(string mapId, string cellsJson, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Maps SET MapObjectsJson = $json WHERE Id = $mid;";
            cmd.Parameters.AddWithValue("$json", cellsJson);
            cmd.Parameters.AddWithValue("$mid", mapId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<MapObjectPoint>> LoadMapObjectsAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MapObjectsJson FROM Maps WHERE Id = $mid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return new List<MapObjectPoint>();
            var json = r.IsDBNull(0) ? "[]" : r.GetString(0);
            try { return JsonSerializer.Deserialize<List<MapObjectPoint>>(json) ?? new List<MapObjectPoint>(); }
            catch (JsonException) { return new List<MapObjectPoint>(); }
        }

        public async Task<(bool Enabled, List<WallSegment> Walls)> LoadWallsAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT WallsEnabled, WallsJson FROM Maps WHERE Id = $mid LIMIT 1";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return (false, new List<WallSegment>());
            var enabled = r.GetInt64(0) != 0;
            var json = r.IsDBNull(1) ? "[]" : r.GetString(1);
            List<WallSegment> walls;
            try { walls = JsonSerializer.Deserialize<List<WallSegment>>(json) ?? new List<WallSegment>(); }
            catch (JsonException) { walls = new List<WallSegment>(); }
            return (enabled, walls);
        }

        public async Task<List<CampaignTokenAsset>> LoadTokenLibraryAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<CampaignTokenAsset>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, Name, Kind, ImagePath, ColorHex, Glyph, MonsterKey, SizeName, InitiativeOverride, CreatedAt
                FROM CampaignTokenLibrary WHERE CampaignId = $cid ORDER BY CreatedAt
                """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new CampaignTokenAsset
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Kind = r.GetString(3),
                    ImagePath = r.IsDBNull(4) ? null : r.GetString(4),
                    ColorHex = r.IsDBNull(5) ? null : r.GetString(5),
                    Glyph = r.IsDBNull(6) ? null : r.GetString(6),
                    MonsterKey = r.IsDBNull(7) ? null : r.GetString(7),
                    SizeName = r.GetString(8),
                    InitiativeOverride = r.IsDBNull(9) ? (int?)null : r.GetInt32(9),
                    CreatedAt = DateTime.TryParse(r.GetString(10), out var dt) ? dt : DateTime.UtcNow
                });
            }
            return list;
        }

        public async Task SaveTokenLibraryAsync(CampaignTokenAsset a, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CampaignTokenLibrary (Id, CampaignId, Name, Kind, ImagePath, ColorHex, Glyph, MonsterKey, SizeName, InitiativeOverride, CreatedAt)
                VALUES ($id, $cid, $name, $kind, $img, $color, $glyph, $mk, $size, $init, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Kind = excluded.Kind,
                    ImagePath = excluded.ImagePath,
                    ColorHex = excluded.ColorHex,
                    Glyph = excluded.Glyph,
                    MonsterKey = excluded.MonsterKey,
                    SizeName = excluded.SizeName,
                    InitiativeOverride = excluded.InitiativeOverride
                """;
            cmd.Parameters.AddWithValue("$id", a.Id);
            cmd.Parameters.AddWithValue("$cid", a.CampaignId);
            cmd.Parameters.AddWithValue("$name", a.Name);
            cmd.Parameters.AddWithValue("$kind", a.Kind);
            cmd.Parameters.AddWithValue("$img", (object?)a.ImagePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$color", (object?)a.ColorHex ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$glyph", (object?)a.Glyph ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mk", (object?)a.MonsterKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$size", a.SizeName);
            cmd.Parameters.AddWithValue("$init", (object?)a.InitiativeOverride ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", a.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteTokenLibraryAsync(string assetId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CampaignTokenLibrary WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", assetId);
            await cmd.ExecuteNonQueryAsync(ct);
        }


        public async Task<List<ChatChannel>> LoadChannelsAsync(
    string campaignId, CancellationToken ct = default)
        {
            var list = new List<ChatChannel>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, Name, Description, CreatedAt
                FROM ChatChannels
                WHERE CampaignId = $cid
                ORDER BY CreatedAt ASC
                """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new ChatChannel
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    CreatedAt = DateTime.Parse(r.GetString(4))
                });
            }
            return list;
        }

        public async Task<List<ChatMessage>> LoadMessagesAsync(
            string channelId, int limit = 100, CancellationToken ct = default)
        {
            var list = new List<ChatMessage>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT m.Id, m.CampaignId, m.ChannelId, m.UserId,
                       m.Message, m.Timestamp, u.Username
                FROM ChatMessages m
                LEFT JOIN Users u ON u.Id = m.UserId
                WHERE m.ChannelId = $cid
                ORDER BY m.Timestamp DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$cid", channelId);
            cmd.Parameters.AddWithValue("$limit", limit);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new ChatMessage
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    ChannelId = r.GetString(2),
                    UserId = r.GetString(3),
                    Message = r.GetString(4),
                    Timestamp = DateTime.Parse(r.GetString(5)),
                    Sender = r.IsDBNull(6) ? null : r.GetString(6)
                });
            }
            list.Reverse();
            return list;
        }

        public async Task SaveMacroAsync(string campaignId, string userId, string name, string expression, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO DiceMacros (Id, CampaignId, UserId, Name, Expression)
                VALUES ($id, $cid, $uid, $name, $expr)
                ON CONFLICT(Id) DO UPDATE SET Expression = excluded.Expression";
            cmd.Parameters.AddWithValue("$id", $"{campaignId}:{userId}:{name.ToLowerInvariant()}");
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$expr", expression);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteMacroAsync(string campaignId, string userId, string name, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM DiceMacros WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", $"{campaignId}:{userId}:{name.ToLowerInvariant()}");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<RandomTable>> LoadRandomTablesAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<RandomTable>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, CampaignId, Name, DiceExpression, EntriesJson FROM RandomTables WHERE CampaignId = $cid ORDER BY Name";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var table = new RandomTable
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    DiceExpression = r.IsDBNull(3) ? "" : r.GetString(3)
                };
                try
                {
                    var entries = JsonSerializer.Deserialize<List<RandomTableEntry>>(r.IsDBNull(4) ? "[]" : r.GetString(4));
                    if (entries != null) table.Entries = entries;
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"[RandomTables] bad entries blob on {table.Name}", ex);
                }
                list.Add(table);
            }
            return list;
        }

        public async Task SaveRandomTableAsync(RandomTable table, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO RandomTables (Id, CampaignId, Name, DiceExpression, EntriesJson, CreatedAt)
                VALUES ($id, $cid, $name, $dice, $entries, $now)
                ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, DiceExpression = excluded.DiceExpression, EntriesJson = excluded.EntriesJson";
            cmd.Parameters.AddWithValue("$id", table.Id);
            cmd.Parameters.AddWithValue("$cid", table.CampaignId);
            cmd.Parameters.AddWithValue("$name", table.Name);
            cmd.Parameters.AddWithValue("$dice", table.DiceExpression ?? "");
            cmd.Parameters.AddWithValue("$entries", JsonSerializer.Serialize(table.Entries));
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteRandomTableAsync(string id, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM RandomTables WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<ItemInstance>> LoadInstancesForCharacterAsync(string characterId, CancellationToken ct = default)
        {
            var list = new List<ItemInstance>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, BaseItemId, OwnerCharacterId, ParentInstanceId, Quantity, CustomName, StateJson
                FROM ItemInstances WHERE OwnerCharacterId = $oid
            """;
            cmd.Parameters.AddWithValue("$oid", characterId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) list.Add(MapInstance(r));
            return list;
        }

        public async Task<List<ItemInstance>> LoadInstancesForCampaignAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<ItemInstance>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, BaseItemId, OwnerCharacterId, ParentInstanceId, Quantity, CustomName, StateJson
                FROM ItemInstances WHERE CampaignId = $cid
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) list.Add(MapInstance(r));
            return list;
        }

        public async Task SaveInstanceAsync(ItemInstance inst, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ItemInstances (Id, CampaignId, BaseItemId, OwnerCharacterId, ParentInstanceId, Quantity, CustomName, StateJson)
                VALUES ($id, $cid, $bid, $oid, $pid, $qty, $cn, $state)
                ON CONFLICT(Id) DO UPDATE SET
                    OwnerCharacterId = excluded.OwnerCharacterId,
                    ParentInstanceId = excluded.ParentInstanceId,
                    Quantity = excluded.Quantity,
                    CustomName = excluded.CustomName,
                    StateJson = excluded.StateJson
            """;
            cmd.Parameters.AddWithValue("$id", inst.Id);
            cmd.Parameters.AddWithValue("$cid", inst.CampaignId);
            cmd.Parameters.AddWithValue("$bid", inst.BaseItemId);
            cmd.Parameters.AddWithValue("$oid", (object?)inst.OwnerCharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pid", (object?)inst.ParentInstanceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qty", inst.Quantity);
            cmd.Parameters.AddWithValue("$cn", (object?)inst.CustomName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$state", (object?)inst.StateJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task SetInstanceChargesAsync(string instanceId, int charges, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ItemInstances SET StateJson = json_set(COALESCE(NULLIF(StateJson, ''), '{}'), '$.Charges', $c) WHERE Id = $id";
            cmd.Parameters.AddWithValue("$c", charges);
            cmd.Parameters.AddWithValue("$id", instanceId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> SpendAmmoAsync(string ownerCharacterId, string ammoBaseItemId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var find = conn.CreateCommand();
            find.CommandText = "SELECT Id, Quantity FROM ItemInstances WHERE OwnerCharacterId = $o AND BaseItemId = $b AND Quantity > 0 ORDER BY Quantity LIMIT 1";
            find.Parameters.AddWithValue("$o", ownerCharacterId);
            find.Parameters.AddWithValue("$b", ammoBaseItemId);

            string? id = null;
            var left = 0;
            await using (var r = await find.ExecuteReaderAsync(ct))
                if (await r.ReadAsync(ct)) { id = r.GetString(0); left = r.GetInt32(1); }

            if (id == null) return -1;

            await using var upd = conn.CreateCommand();
            upd.CommandText = left <= 1
                ? "DELETE FROM ItemInstances WHERE Id = $id"
                : "UPDATE ItemInstances SET Quantity = Quantity - 1 WHERE Id = $id";
            upd.Parameters.AddWithValue("$id", id);
            await upd.ExecuteNonQueryAsync(ct);
            return left - 1;
        }

        public async Task DeleteInstanceAsync(string instanceId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ItemInstances WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", instanceId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task MoveInstanceAsync(string instanceId, string? parentInstanceId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ItemInstances SET ParentInstanceId = $pid WHERE Id = $id";
            cmd.Parameters.AddWithValue("$pid", (object?)parentInstanceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", instanceId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task SetInstanceOwnerAsync(string instanceId, string? ownerCharacterId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ItemInstances SET OwnerCharacterId = $oid WHERE Id = $id";
            cmd.Parameters.AddWithValue("$oid", (object?)ownerCharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", instanceId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static ItemInstance MapInstance(SqliteDataReader r) => new()
        {
            Id = r.GetString(0),
            CampaignId = r.GetString(1),
            BaseItemId = r.GetString(2),
            OwnerCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
            ParentInstanceId = r.IsDBNull(4) ? null : r.GetString(4),
            Quantity = (int)r.GetInt64(5),
            CustomName = r.IsDBNull(6) ? null : r.GetString(6),
            StateJson = r.IsDBNull(7) ? null : r.GetString(7)
        };

        public async Task<List<Currency>> LoadCurrenciesAsync(CancellationToken ct = default)
        {
            var list = new List<Currency>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, TemplateId, Name, Abbreviation, IsBase, EqualToBase, Color, IconSvg, SortOrder
                FROM Currencies ORDER BY SortOrder
            """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new Currency
                {
                    Id = r.GetString(0),
                    TemplateId = r.IsDBNull(1) ? null : r.GetString(1),
                    Name = r.GetString(2),
                    Abbreviation = r.GetString(3),
                    IsBase = r.GetInt64(4) == 1,
                    EqualToBase = (int)r.GetInt64(5),
                    Color = r.IsDBNull(6) ? null : r.GetString(6),
                    IconSvg = r.IsDBNull(7) ? null : r.GetString(7),
                    SortOrder = (int)r.GetInt64(8)
                });
            }
            return list;
        }

        public async Task<Dictionary<string, long>> LoadWalletAsync(string characterId, CancellationToken ct = default)
        {
            var row = await LoadCharacterAsync(characterId, ct);
            return row == null ? new() : CharacterMapper.ToRuntime(row).Wallet;
        }

        public async Task<long> AdjustWalletAsync(string characterId, string currencyId, long delta, CancellationToken ct = default)
        {
            var row = await LoadCharacterAsync(characterId, ct);
            if (row == null) return 0;
            var rt = CharacterMapper.ToRuntime(row);
            var current = rt.Wallet.TryGetValue(currencyId, out var v) ? v : 0;
            var next = current + delta;
            if (next < 0) next = 0;
            rt.Wallet[currencyId] = next;
            await SaveCharacterAsync(CharacterMapper.ToRow(rt), ct);
            return next;
        }

        public async Task SetWalletAmountAsync(string characterId, string currencyId, long amount, CancellationToken ct = default)
        {
            var row = await LoadCharacterAsync(characterId, ct);
            if (row == null) return;
            var rt = CharacterMapper.ToRuntime(row);
            rt.Wallet[currencyId] = amount < 0 ? 0 : amount;
            await SaveCharacterAsync(CharacterMapper.ToRow(rt), ct);
        }

        public async Task<Dictionary<string, string>> LoadMacrosAsync(string campaignId, string userId, CancellationToken ct = default)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name, Expression FROM DiceMacros WHERE CampaignId = $cid AND UserId = $uid";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", userId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                map[r.GetString(0)] = r.GetString(1);
            return map;
        }
    }
}