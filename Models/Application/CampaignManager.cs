using Dujahit.Models;
using Dujahit.Models.Communication;
using Dujahit.Models.Database;
using Dujahit.Models.Settings;
using DynamicData;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models.Application
{
    public class CampaignManager
    {
        private readonly DatabaseManager _db;

        public Campaign? CurrentCampaign { get; private set; }
        public User? CurrentUser { get; private set; }

        public CampaignManager(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task SetCurrentUserAsync(string username, CancellationToken ct = default)
        {
            CurrentUser = await EnsureLocalUserAsync(username, ct); // I am not sure if this is the best way to handle them now... I should be using GUIDs when applicable (like during login to a known account). I'll rewrite this
        }

        private static async Task<string> ReadDefaultRulesVersionAsync(SqliteConnection conn, SqliteTransaction tx, string templateId, CancellationToken ct)
        {
            string? json;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", templateId);
                json = await cmd.ExecuteScalarAsync(ct) as string;
            }
            if (string.IsNullOrWhiteSpace(json)) return "both";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("DefaultRulesVersion", out var v)
                    && v.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(v.GetString()))
                    return v.GetString()!;
            }
            catch (JsonException) { }
            return "both";
        }

        public async Task<Campaign> CreateNewCampaignAsync(
            string name, string templateId, string description = "",
            string port = "5555", CancellationToken ct = default)
        {
            if (CurrentUser is null)
                throw new InvalidOperationException("InitializeAsync must be called first.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Campaign name is required.", nameof(name));

            var campaign = new Campaign
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = CurrentUser.Id,
                Name = name.Trim(),
                TemplateId = templateId,
                Description = description ?? "",
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Port = port
            };

            await using var conn = await _db.OpenAsync(ct);

            await using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                check.Parameters.AddWithValue("$tid", templateId);
                if (await check.ExecuteScalarAsync(ct) is null)
                    throw new InvalidOperationException(
                        $"Template '{templateId}' not found. Import it first via TemplateLoader.");
            }

            

            // Make sure CurrentUser exists in the Users table (need to rework this and have another "validate user" for login purposes)
            await EnsureUsersRowAsync(conn, CurrentUser, ct);

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                var rulesVersion = await ReadDefaultRulesVersionAsync(conn, tx, campaign.TemplateId, ct);

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO Campaigns
                            (Id, UserId, Name, TemplateId, Description, CreatedAt, LastModified, Port, RulesVersion)
                        VALUES
                            ($id, $uid, $name, $tid, $desc, $created, $modified, $port, $rules)
                        """;
                    cmd.Parameters.AddWithValue("$id", campaign.Id);
                    cmd.Parameters.AddWithValue("$uid", campaign.UserId);
                    cmd.Parameters.AddWithValue("$name", campaign.Name);
                    cmd.Parameters.AddWithValue("$tid", campaign.TemplateId);
                    cmd.Parameters.AddWithValue("$desc", campaign.Description);
                    cmd.Parameters.AddWithValue("$created", campaign.CreatedAt.ToString("o"));
                    cmd.Parameters.AddWithValue("$modified", campaign.LastModified?.ToString("o") ?? "");
                    cmd.Parameters.AddWithValue("$port", campaign.Port);
                    cmd.Parameters.AddWithValue("$rules", rulesVersion);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO CampaignMembers (CampaignId, UserId, Role, JoinedAt)
                        VALUES ($cid, $uid, 'dm', $now)
                        """;
                    cmd.Parameters.AddWithValue("$cid", campaign.Id);
                    cmd.Parameters.AddWithValue("$uid", CurrentUser.Id);
                    cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                foreach (var (junction, source, idCol) in new[]
                {
                    ("CampaignItems",   "Items",   "ItemId"),
                    ("CampaignSpells",  "Spells",  "SpellId"),
                    ("CampaignRaces",   "Races",   "RaceId"),
                    ("CampaignClasses", "Classes", "ClassId"),
                    ("CampaignTraits",  "Traits",  "TraitId"),
                })
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"""
                        INSERT OR IGNORE INTO {junction} (CampaignId, {idCol}, AddedAt, IsEnabled)
                        SELECT $cid, s.Id, $now, 1 FROM {source} s
                        WHERE s.Source = 'srd'
                          AND (s.Id IN (SELECT json_extract(value, '$.TemplateId')
                                        FROM json_each((SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid), '$.{source}'))
                            OR (s.TemplateId = $tid
                                AND NOT EXISTS (SELECT 1
                                    FROM json_each((SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid), '$.{source}'))))
                        """;
                    cmd.Parameters.AddWithValue("$cid", campaign.Id);
                    cmd.Parameters.AddWithValue("$tid", templateId);
                    cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO ChatChannels (Id, CampaignId, Name, Description, CreatedAt)
                        VALUES ($id, $cid, 'general', 'Main channel', $now)
                        """;
                    cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    cmd.Parameters.AddWithValue("$cid", campaign.Id);
                    cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            CurrentCampaign = campaign;
            Debug.WriteLine($"Campaign created: {campaign.Name} ({campaign.Id})");
            return campaign;
        }

        public async Task<bool> IsUserDmOfAsync(string campaignId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(campaignId) || CurrentUser is null) return false;

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM CampaignMembers
                WHERE CampaignId = $cid AND UserId = $uid AND Role = 'dm'
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", CurrentUser.Id);
            return await cmd.ExecuteScalarAsync(ct) is not null;
        }

        public async Task<bool> DeleteCampaignAsync(string campaignId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(campaignId)) return false;
            if (!await IsUserDmOfAsync(campaignId, ct)) return false;

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var sql in new[]
                {
                    "DELETE FROM JoinedCampaigns WHERE CampaignId = $cid",
                    "DELETE FROM PrimaryCharacters WHERE CampaignId = $cid",
                    "DELETE FROM Campaigns WHERE Id = $cid",
                })
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.Parameters.AddWithValue("$cid", campaignId);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            if (string.Equals(CurrentCampaign?.Id, campaignId, StringComparison.Ordinal)) CurrentCampaign = null;
            return true;
        }

        public async Task SaveChannelAsync(ChatChannel channel)
        {
            await using var conn = await _db.OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                await UpsertChannelAsync(conn, tx, channel);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<Campaign>> ListMyCampaignsAsync(CancellationToken ct = default)
        {
            if (CurrentUser is null)
                throw new InvalidOperationException("InitializeAsync must be called first.");

            var list = new List<Campaign>();
            await using var conn = await _db.OpenAsync(ct);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, UserId, Name, TemplateId, Description, CreatedAt, LastModified, Port
                    FROM Campaigns
                    WHERE UserId = $uid
                    ORDER BY COALESCE(LastModified, CreatedAt) DESC
                    """;
                cmd.Parameters.AddWithValue("$uid", CurrentUser.Id);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    list.Add(new Campaign
                    {
                        Id = r.GetString(0),
                        UserId = r.GetString(1),
                        Name = r.GetString(2),
                        TemplateId = r.GetString(3),
                        Description = r.IsDBNull(4) ? "" : r.GetString(4),
                        CreatedAt = DateTime.Parse(r.GetString(5)),
                        LastModified = r.IsDBNull(6) || string.IsNullOrEmpty(r.GetString(6))
                                           ? null : DateTime.Parse(r.GetString(6)),
                        Port = r.GetString(7)
                    });
                }
            }

            var owned = new HashSet<string>(list.Select(c => c.Id), StringComparer.Ordinal);

            await using (var jc = conn.CreateCommand())
            {
                jc.CommandText = """
                    SELECT CampaignId, CampaignName, HostAddress, JoinCode
                    FROM JoinedCampaigns
                    WHERE UserId = $uid
                    ORDER BY LastJoinedAt DESC
                    """;
                jc.Parameters.AddWithValue("$uid", CurrentUser.Id);

                await using var jr = await jc.ExecuteReaderAsync(ct);
                while (await jr.ReadAsync(ct))
                {
                    var cid = jr.GetString(0);
                    if (owned.Contains(cid)) continue;
                    list.Add(new Campaign
                    {
                        Id = cid,
                        Name = jr.IsDBNull(1) ? "" : jr.GetString(1),
                        HostAddress = jr.IsDBNull(2) ? null : jr.GetString(2),
                        JoinCode = jr.IsDBNull(3) ? null : jr.GetString(3),
                        IsRemote = true
                    });
                }
            }

            return list;
        }

        public async Task RememberJoinedCampaignAsync(string userId, string campaignId, string campaignName, string hostAddress, string? joinCode = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(campaignId)) return;

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO JoinedCampaigns
                    (UserId, CampaignId, CampaignName, HostAddress, LastJoinedAt, JoinCode)
                VALUES
                    ($uid, $cid, $name, $host, $when, $code)
                """;
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(campaignName) ? campaignId : campaignName);
            cmd.Parameters.AddWithValue("$host", hostAddress);
            cmd.Parameters.AddWithValue("$when", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$code", (object?)joinCode ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }



        public async Task<List<CharacterListEntry>> GetCharactersForCampaignAsync(
            CancellationToken ct = default)
        {

            {
                await using var conn = await _db.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                SELECT c.Id, c.Name, c.OwnerUserId, c.Level,
                       c.RaceId, c.ClassId,
                       r.Name  AS RaceName,
                       cl.Name AS ClassName,
                       c.VisibleToAll
                FROM Characters c
                LEFT JOIN Races   r  ON r.Id  = c.RaceId
                LEFT JOIN Classes cl ON cl.Id = c.ClassId
                WHERE c.CampaignId = $CampaignId
                  AND c.CharacterKind = 'pc'
                ORDER BY c.Name COLLATE NOCASE
            """;
                cmd.Parameters.AddWithValue("$CampaignId", CurrentCampaign.Id);

                var results = new List<CharacterListEntry>();
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    results.Add(new CharacterListEntry
                    {
                        Id = r.GetString(0),
                        Name = r.GetString(1),
                        OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                        Level = r.GetInt32(3),
                        RaceId = r.IsDBNull(4) ? null : r.GetString(4),
                        ClassId = r.IsDBNull(5) ? null : r.GetString(5),
                        RaceName = r.IsDBNull(6) ? null : r.GetString(6),
                        ClassName = r.IsDBNull(7) ? null : r.GetString(7),
                        VisibleToAll = !r.IsDBNull(8) && r.GetInt32(8) != 0,
                    });
                }
                return results;
            }
        }

        public async Task<CharacterListEntry> CreateUnassignedCharacterAsync(
            string name = "New Character", CancellationToken ct = default)
        {
            var id = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("o");

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                        INSERT INTO Characters
                            (Id, CampaignId, OwnerUserId, Name, Level,
                             CurrentHp, MaxHp, AbilityScoresJson, CreatedAt)
                        VALUES
                            ($Id, $CampaignId, NULL, $Name, 1,
                             0, 0, '{}', $CreatedAt)
                        """;
            cmd.Parameters.AddWithValue("$Id", id);
            cmd.Parameters.AddWithValue("$CampaignId", CurrentCampaign.Id);
            cmd.Parameters.AddWithValue("$Name", name);
            cmd.Parameters.AddWithValue("$CreatedAt", now);
            await cmd.ExecuteNonQueryAsync(ct);

            return new CharacterListEntry
            {
                Id = id,
                Name = name,
                OwnerUserId = null,
                Level = 1
            };
        }

        public async Task<List<User>> ListMyUsersAsync(CancellationToken ct = default)
        {
            var list = new List<User>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT u.Id, u.Username, u.CreatedAt
                FROM Users u
                JOIN LocalUsers lu ON lu.UserId = u.Id
                ORDER BY u.Username COLLATE NOCASE
                """;

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new User
                {
                    Id = r.GetString(0),
                    Username = r.GetString(1),
                    CreatedAt = r.GetDateTime(2)
                });
            }
            return list;
        }

        public async Task<Campaign?> LoadCampaignAsync(string campaignId, CancellationToken ct = default)
        {
            Debug.WriteLine($"[ApplyBootstrap] payload.CampaignId = {campaignId}");
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, UserId, Name, TemplateId, Description, CreatedAt, LastModified, Port
                FROM Campaigns WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", campaignId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            var c = new Campaign
            {
                Id = r.GetString(0),
                UserId = r.GetString(1),
                Name = r.GetString(2),
                TemplateId = r.GetString(3),
                Description = r.IsDBNull(4) ? "" : r.GetString(4),
                CreatedAt = DateTime.Parse(r.GetString(5)),
                LastModified = r.IsDBNull(6) || string.IsNullOrEmpty(r.GetString(6))
                                   ? null : DateTime.Parse(r.GetString(6)),
                Port = r.GetString(7)
            };
            CurrentCampaign = c;
            return c;
        }



        private async Task<User> EnsureLocalUserAsync(string username, CancellationToken ct)
        {
            await using var conn = await _db.OpenAsync(ct);

            User? user = null;

            await using (var get = conn.CreateCommand())
            {
                get.CommandText = "SELECT Id, Username, CreatedAt FROM Users WHERE Username = $name LIMIT 1";
                get.Parameters.AddWithValue("$name", username);
                await using var r = await get.ExecuteReaderAsync(ct);
                if (await r.ReadAsync(ct))
                {
                    user = new User
                    {
                        Id = r.GetString(0),
                        Username = r.GetString(1),
                        CreatedAt = DateTime.Parse(r.GetString(2))
                    };
                }
            }

            if (user is null)
            {
                user = new User
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Username = username,
                    CreatedAt = DateTime.UtcNow
                };

                await using var insert = conn.CreateCommand();
                insert.CommandText = """
                    INSERT INTO Users (Id, Username, CreatedAt)
                    VALUES ($id, $name, $created)
                    """;
                insert.Parameters.AddWithValue("$id", user.Id);
                insert.Parameters.AddWithValue("$name", user.Username);
                insert.Parameters.AddWithValue("$created", user.CreatedAt.ToString("o"));
                await insert.ExecuteNonQueryAsync(ct);
            }

            await using (var local = conn.CreateCommand())
            {
                local.CommandText = "INSERT OR IGNORE INTO LocalUsers (UserId) VALUES ($id)";
                local.Parameters.AddWithValue("$id", user.Id);
                await local.ExecuteNonQueryAsync(ct);
            }

            return user;
        }

        public async Task<string> GetRoleAsync(string campaignId, string userId)
        {
            await using var conn = await _db.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Role FROM CampaignMembers
                        WHERE CampaignId = $cid AND UserId = $uid;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", userId);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "spectator";
        }
        private async Task EnsureUsersRowAsync(
            SqliteConnection conn, User user, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Users (Id, Username, CreatedAt)
                VALUES ($id, $name, $created)
                ON CONFLICT(Id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("$id", user.Id);
            cmd.Parameters.AddWithValue("$name", user.Username);
            cmd.Parameters.AddWithValue("$created", user.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<string> CreateCharacterAsync(
            string name, string? raceId, string? subraceId, string? classId,
            int level, int currentHp, int maxHp, string abilityScoresJson,
            string? inventoryJson, string? stateJson,
            string? assignToUserId, string characterKind = "pc", CancellationToken ct = default)
        {
            if (CurrentCampaign is null)
                throw new InvalidOperationException("No campaign loaded.");

            var id = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("o");

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                INSERT INTO Characters
                    (Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                     Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson,
                     CharacterKind, CreatedAt)
                VALUES
                    ($Id, $CampaignId, $OwnerUserId, $Name, $RaceId, $SubraceId, $ClassId,
                     $Level, $CurrentHp, $MaxHp, $AbilityScoresJson, $InventoryJson, $StateJson,
                     $CharacterKind, $CreatedAt)
                """;
                    cmd.Parameters.AddWithValue("$Id", id);
                    cmd.Parameters.AddWithValue("$CampaignId", CurrentCampaign.Id);
                    cmd.Parameters.AddWithValue("$OwnerUserId", (object?)assignToUserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$Name", name);
                    cmd.Parameters.AddWithValue("$CharacterKind", string.IsNullOrEmpty(characterKind) ? "pc" : characterKind);
                    cmd.Parameters.AddWithValue("$RaceId", (object?)raceId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$SubraceId", (object?)subraceId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$ClassId", (object?)classId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$Level", level);
                    cmd.Parameters.AddWithValue("$CurrentHp", currentHp);
                    cmd.Parameters.AddWithValue("$MaxHp", maxHp);
                    cmd.Parameters.AddWithValue("$AbilityScoresJson", abilityScoresJson);
                    cmd.Parameters.AddWithValue("$InventoryJson", (object?)inventoryJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$StateJson", (object?)stateJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$CreatedAt", now);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                if (!string.IsNullOrEmpty(assignToUserId))
                {
                    await using var link = conn.CreateCommand();
                    link.Transaction = tx;
                    link.CommandText = """
                UPDATE CampaignMembers
                SET CharacterId = $cid
                WHERE CampaignId = $camp AND UserId = $uid
                """;
                    link.Parameters.AddWithValue("$cid", id);
                    link.Parameters.AddWithValue("$camp", CurrentCampaign.Id);
                    link.Parameters.AddWithValue("$uid", assignToUserId);
                    await link.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            return id;
        }

        public async Task ApplyBootstrapAsync(CampaignBootstrapPayload payload)
        {
            await using var conn = await _db.OpenAsync();

            await using (var fk = conn.CreateCommand())
            {
                fk.CommandText = "PRAGMA foreign_keys = OFF;";
                await fk.ExecuteNonQueryAsync();
            }

            Debug.WriteLine($"[ApplyBootstrap] payload.CampaignId = {payload.CampaignId}, name = {payload.CampaignName}");

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {

                await UpsertCampaignRowAsync(conn, tx, payload);
                await UpsertTemplateAsync(conn, tx, payload);

                foreach (var race in payload.Races)
                    await UpsertRaceAsync(conn, tx, race);

                foreach (var sub in payload.Subraces)
                    await UpsertSubraceAsync(conn, tx, sub);

                foreach (var trait in payload.Traits)
                    await UpsertTraitAsync(conn, tx, trait);

                foreach (var cls in payload.Classes)
                    await UpsertClassAsync(conn, tx, cls);

                foreach (var spell in payload.Spells)
                    await UpsertSpellAsync(conn, tx, spell);

                foreach (var item in payload.Items)
                    await UpsertItemAsync(conn, tx, item);

                foreach (var channel in payload.Channels)
                    await UpsertChannelAsync(conn, tx, channel);

                foreach (var member in payload.Members)
                    await UpsertMemberAsync(conn, tx, member);

                foreach (var ch in payload.Characters)
                    await UpsertCharacterAsync(conn, tx, ch);

                if (payload.MyCharacter != null)
                    await UpsertCharacterAsync(conn, tx, payload.MyCharacter);

                if (payload.ActiveMap != null)
                {
                    await UpsertMapAsync(conn, tx, payload.ActiveMap);
                    foreach (var token in payload.Tokens)
                        await UpsertTokenAsync(conn, tx, token);
                }

                foreach (var page in payload.NotePages)
                    await UpsertNotePageAsync(conn, tx, page);

                foreach (var share in payload.NoteShares)
                    await UpsertNoteShareAsync(conn, tx, share);

                foreach (var cur in payload.Currencies)
                    await UpsertCurrencyAsync(conn, tx, cur);

                foreach (var inst in payload.ItemInstances)
                    await UpsertItemInstanceAsync(conn, tx, inst);

                

                await tx.CommitAsync();
                CurrentCampaign = new Campaign { Id = payload.CampaignId, Name = payload.CampaignName, TemplateId = payload.TemplateId };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task ApplyChangeAsync(ChangeNotification change)
        {
            if (change.ChangeType == "removed")
            {
                await DeleteEntityAsync(change.EntityType, change.EntityId);
                return;
            }

            await using var conn = await _db.OpenAsync();

            await using (var fk = conn.CreateCommand())
            {
                fk.CommandText = "PRAGMA foreign_keys = OFF;";
                await fk.ExecuteNonQueryAsync();
            }

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                switch (change.EntityType)
                {
                    case "Item":
                        var item = JsonSerializer.Deserialize<Item>(change.Payload!);
                        await UpsertItemAsync(conn, tx, item!);
                        break;
                    case "Spell":
                        var spell = JsonSerializer.Deserialize<Spell>(change.Payload!);
                        await UpsertSpellAsync(conn, tx, spell!);
                        break;
                    case "Character":
                        var character = JsonSerializer.Deserialize<Character>(change.Payload!);
                        await UpsertCharacterAsync(conn, tx, character!);
                        break;
                    case "ItemInstance":
                        var instance = JsonSerializer.Deserialize<ItemInstance>(change.Payload!);
                        await UpsertItemInstanceAsync(conn, tx, instance!);
                        break;
                    case "MapToken":
                        var token = JsonSerializer.Deserialize<MapToken>(change.Payload!);
                        await UpsertTokenAsync(conn, tx, token!);
                        break;
                    case "NotePage":
                        var notePage = JsonSerializer.Deserialize<NotePage>(change.Payload!);
                        await UpsertNotePageAsync(conn, tx, notePage!);
                        break;
                    case "Theme":
                        var theme = JsonSerializer.Deserialize<Theme>(change.Payload!);
                        await UpsertThemeAsync(conn, tx, theme!);
                        break;
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }


        private async Task DeleteEntityAsync(string entityType, string entityId)
        {
            await using var conn = await _db.OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                cmd.CommandText = entityType switch
                {
                    "Item" => "DELETE FROM Items      WHERE Id = $Id",
                    "Spell" => "DELETE FROM Spells     WHERE Id = $Id",
                    "Race" => "DELETE FROM Races      WHERE Id = $Id",
                    "Class" => "DELETE FROM Classes    WHERE Id = $Id",
                    "Trait" => "DELETE FROM Traits     WHERE Id = $Id",
                    "Character" => "DELETE FROM Characters WHERE Id = $Id",
                    "MapToken" => "DELETE FROM MapTokens  WHERE Id = $Id",
                    "ItemInstance" => "DELETE FROM ItemInstances WHERE Id = $Id",
                    "NotePage" => "DELETE FROM NotePages WHERE Id = $Id",
                    "Map" => "DELETE FROM Maps       WHERE Id = $Id",
                    "Note" => "DELETE FROM Notes      WHERE Id = $Id",
                    "Theme" => "DELETE FROM Themes      WHERE Id = $Id",
                    _ => throw new ArgumentException($"Unknown entity type '{entityType}' - cannot delete.")
                };

                cmd.Parameters.AddWithValue("$Id", entityId);
                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private static string VersionFromDataJson(string? dataJson)
        {
            if (string.IsNullOrWhiteSpace(dataJson)) return "2014";
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("Version", out var v)
                    && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? "2014";
            }
            catch (JsonException) { }
            return "2014";
        }

        private static async Task UpsertItemAsync(
    SqliteConnection conn, SqliteTransaction tx, Item item)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
            INSERT OR REPLACE INTO Items
                (Id, Name, ItemType, Source, OwnerUserId, TemplateId,
                    RevisionNumber, UpdatedAt, DataJson, Slug, Tags, Version)
            VALUES
                ($Id, $Name, $ItemType, $Source, $OwnerUserId, $TemplateId,
                    $RevisionNumber, $UpdatedAt, $DataJson, $Slug, $Tags, $Version)
            """;
            cmd.Parameters.AddWithValue("$Id", item.Id);
            cmd.Parameters.AddWithValue("$Name", item.Name);
            cmd.Parameters.AddWithValue("$ItemType", item.ItemType);
            cmd.Parameters.AddWithValue("$Source", item.Source);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)item.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$TemplateId", (object?)item.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$RevisionNumber", item.RevisionNumber);
            cmd.Parameters.AddWithValue("$UpdatedAt", item.UpdatedAt);
            cmd.Parameters.AddWithValue("$DataJson", item.DataJson);
            cmd.Parameters.AddWithValue("$Slug", (object?)item.Slug ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Tags", string.IsNullOrEmpty(item.Tags) ? "[]" : item.Tags);
            cmd.Parameters.AddWithValue("$Version", VersionFromDataJson(item.DataJson));
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task UpsertThemeAsync(
            SqliteConnection conn, SqliteTransaction tx, Theme theme)
        {
            var name = theme.Name;
            await using (var taken = conn.CreateCommand())
            {
                taken.Transaction = tx;
                taken.CommandText = "SELECT COUNT(*) FROM Themes WHERE Name = $Name AND Id <> $Id";
                taken.Parameters.AddWithValue("$Name", name);
                taken.Parameters.AddWithValue("$Id", theme.Id);
                if (Convert.ToInt64(await taken.ExecuteScalarAsync() ?? 0L) > 0)
                    name = $"{name} ({theme.Id[..Math.Min(6, theme.Id.Length)]})";
            }

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Themes
                    (Id, Name, Background, Foreground, Widget, WidgetForeground,
                        AccentColor, AccentHover, Divider, Danger, Muted)
                VALUES
                    ($Id, $Name, $Background, $Foreground, $Widget, $WidgetForeground,
                        $AccentColor, $AccentHover, $Divider, $Danger, $Muted)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Background = excluded.Background,
                    Foreground = excluded.Foreground,
                    Widget = excluded.Widget,
                    WidgetForeground = excluded.WidgetForeground,
                    AccentColor = excluded.AccentColor,
                    AccentHover = excluded.AccentHover,
                    Divider = excluded.Divider,
                    Danger = excluded.Danger,
                    Muted = excluded.Muted
            """;
            cmd.Parameters.AddWithValue("$Id", theme.Id);
            cmd.Parameters.AddWithValue("$Name", name);
            cmd.Parameters.AddWithValue("$Background", theme.Background);
            cmd.Parameters.AddWithValue("$Foreground", theme.Foreground);
            cmd.Parameters.AddWithValue("$Widget", theme.Widget);
            cmd.Parameters.AddWithValue("$WidgetForeground", theme.WidgetForeground);
            cmd.Parameters.AddWithValue("$AccentColor", theme.AccentColor);
            cmd.Parameters.AddWithValue("$AccentHover", theme.AccentHover);
            cmd.Parameters.AddWithValue("$Divider", theme.Divider);
            cmd.Parameters.AddWithValue("$Danger", theme.Danger);
            cmd.Parameters.AddWithValue("$Muted", string.IsNullOrWhiteSpace(theme.Muted) ? "#8A8A99" : theme.Muted);
            await cmd.ExecuteNonQueryAsync();
        }
        public async Task SaveThemeAsync(Theme theme, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(theme.Id))
                theme.Id = Guid.NewGuid().ToString("N");

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await UpsertThemeAsync(conn, tx, theme);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task RelinkTemplateCatalogAsync(string campaignId, string templateId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var (junction, source, idCol) in new[]
                {
            ("CampaignItems",   "Items",   "ItemId"),
            ("CampaignSpells",  "Spells",  "SpellId"),
            ("CampaignRaces",   "Races",   "RaceId"),
            ("CampaignClasses", "Classes", "ClassId"),
            ("CampaignTraits",  "Traits",  "TraitId"),
        })
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"""
                INSERT OR IGNORE INTO {junction} (CampaignId, {idCol}, AddedAt, IsEnabled)
                SELECT $cid, s.Id, $now, 1 FROM {source} s
                WHERE s.Source = 'srd'
                  AND (s.Id IN (SELECT json_extract(value, '$.TemplateId')
                                FROM json_each((SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid), '$.{source}'))
                    OR (s.TemplateId = $tid
                        AND NOT EXISTS (SELECT 1
                            FROM json_each((SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid), '$.{source}'))))
                """;
                    cmd.Parameters.AddWithValue("$cid", campaignId);
                    cmd.Parameters.AddWithValue("$tid", templateId);
                    cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
            }
            catch { await tx.RollbackAsync(ct); throw; }
        }

        private static async Task UpsertNotePageAsync(
    SqliteConnection conn, SqliteTransaction tx, NotePage p)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
            INSERT INTO NotePages
                (Id, CampaignId, OwnerUserId, ParentPageId, Scope,
                 Title, Icon, ContentMarkdown, SortOrder,
                 RevisionNumber, CreatedAt, UpdatedAt)
            VALUES
                ($Id, $CampaignId, $OwnerUserId, $ParentPageId, $Scope,
                 $Title, $Icon, $ContentMarkdown, $SortOrder,
                 $RevisionNumber, $CreatedAt, $UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                OwnerUserId     = excluded.OwnerUserId,
                ParentPageId    = excluded.ParentPageId,
                Scope           = excluded.Scope,
                Title           = excluded.Title,
                Icon            = excluded.Icon,
                ContentMarkdown = excluded.ContentMarkdown,
                SortOrder       = excluded.SortOrder,
                RevisionNumber  = excluded.RevisionNumber,
                UpdatedAt       = excluded.UpdatedAt
            """;
            cmd.Parameters.AddWithValue("$Id", p.Id);
            cmd.Parameters.AddWithValue("$CampaignId", p.CampaignId);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)p.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ParentPageId", (object?)p.ParentPageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Scope", p.Scope);
            cmd.Parameters.AddWithValue("$Title", p.Title);
            cmd.Parameters.AddWithValue("$Icon", (object?)p.Icon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ContentMarkdown", p.ContentMarkdown);
            cmd.Parameters.AddWithValue("$SortOrder", p.SortOrder);
            cmd.Parameters.AddWithValue("$RevisionNumber", p.RevisionNumber);
            cmd.Parameters.AddWithValue("$CreatedAt", p.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$UpdatedAt", p.UpdatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertNoteShareAsync(
            SqliteConnection conn, SqliteTransaction tx, NotePageShare s)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO NotePageShares (PageId, UserId, Permission, SharedAt)
                VALUES ($PageId, $UserId, $Permission, $SharedAt)
                ON CONFLICT(PageId, UserId) DO UPDATE SET
                    Permission = excluded.Permission
            """;
            cmd.Parameters.AddWithValue("$PageId", s.PageId);
            cmd.Parameters.AddWithValue("$UserId", s.UserId);
            cmd.Parameters.AddWithValue("$Permission", s.Permission);
            cmd.Parameters.AddWithValue("$SharedAt", s.SharedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task UpsertCampaignRowAsync(
            SqliteConnection conn, SqliteTransaction tx, CampaignBootstrapPayload payload)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO Campaigns
                    (Id, UserId, Name, TemplateId, Description, CreatedAt, Port)
                VALUES
                    ($Id, '', $Name, $TemplateId, $Description, $CreatedAt, $Port)
                ON CONFLICT(Id) DO UPDATE SET
                    Name        = excluded.Name,
                    TemplateId  = excluded.TemplateId,
                    Description = excluded.Description,
                    Port        = excluded.Port
                """;
            cmd.Parameters.AddWithValue("$Id", payload.CampaignId);
            cmd.Parameters.AddWithValue("$Name", payload.CampaignName);
            cmd.Parameters.AddWithValue("$TemplateId", payload.TemplateId);
            cmd.Parameters.AddWithValue("$Description", (object?)payload.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$CreatedAt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$Port", payload.Port);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertChannelAsync(
        SqliteConnection conn, SqliteTransaction tx, ChatChannel channel)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO ChatChannels
                    (Id, CampaignId, Name, Description, CreatedAt)
                VALUES
                    ($Id, $CampaignId, $Name, $Description, $CreatedAt)
                """;
            cmd.Parameters.AddWithValue("$Id", channel.Id);
            cmd.Parameters.AddWithValue("$CampaignId", channel.CampaignId);
            cmd.Parameters.AddWithValue("$Name", channel.Name);
            cmd.Parameters.AddWithValue("$Description", (object?)channel.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$CreatedAt", channel.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertRaceAsync(
            SqliteConnection conn, SqliteTransaction tx, Race race)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO Races
                    (Id, Name, Description, Size, Speed, Source,
                     OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                VALUES
                    ($Id, $Name, $Description, $Size, $Speed, $Source,
                     $OwnerUserId, $TemplateId, $RevisionNumber, $UpdatedAt, $DataJson, $Version)
                """;
            cmd.Parameters.AddWithValue("$Id", race.Id);
            cmd.Parameters.AddWithValue("$Name", race.Name);
            cmd.Parameters.AddWithValue("$Description", race.Description);
            cmd.Parameters.AddWithValue("$Size", race.Size);
            cmd.Parameters.AddWithValue("$Speed", race.Speed);
            cmd.Parameters.AddWithValue("$Source", race.Source);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)race.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$TemplateId", (object?)race.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$RevisionNumber", race.RevisionNumber);
            cmd.Parameters.AddWithValue("$UpdatedAt", race.UpdatedAt);
            cmd.Parameters.AddWithValue("$DataJson", race.DataJson);
            cmd.Parameters.AddWithValue("$Version", VersionFromDataJson(race.DataJson));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertTemplateAsync(
            SqliteConnection conn, SqliteTransaction tx, CampaignBootstrapPayload payload)
        {
            if (string.IsNullOrEmpty(payload.TemplateId) || string.IsNullOrEmpty(payload.TemplateJsonContent))
                return;

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO CampaignTemplates
                    (TemplateId, Name, Description, SystemId, Version, ImportedAt, JsonContent)
                VALUES
                    ($TemplateId, $Name, $Description, $SystemId, $Version, $ImportedAt, $JsonContent)
                """;
            cmd.Parameters.AddWithValue("$TemplateId", payload.TemplateId);
            cmd.Parameters.AddWithValue("$Name", string.IsNullOrEmpty(payload.TemplateName) ? payload.TemplateId : payload.TemplateName);
            cmd.Parameters.AddWithValue("$Description", DBNull.Value);
            cmd.Parameters.AddWithValue("$SystemId", string.IsNullOrEmpty(payload.TemplateSystemId) ? "dnd5e" : payload.TemplateSystemId);
            cmd.Parameters.AddWithValue("$Version", payload.TemplateVersion);
            cmd.Parameters.AddWithValue("$ImportedAt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$JsonContent", payload.TemplateJsonContent);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertSubraceAsync(
            SqliteConnection conn, SqliteTransaction tx, Subrace sub)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO Subraces
                    (Id, Name, ParentRaceId, Description, Source,
                     OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                VALUES
                    ($Id, $Name, $ParentRaceId, $Description, $Source,
                     $OwnerUserId, $TemplateId, $RevisionNumber, $UpdatedAt, $DataJson, $Version)
                """;
            cmd.Parameters.AddWithValue("$Id", sub.Id);
            cmd.Parameters.AddWithValue("$Name", sub.Name);
            cmd.Parameters.AddWithValue("$ParentRaceId", sub.ParentRaceId);
            cmd.Parameters.AddWithValue("$Description", (object?)sub.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Source", sub.Source);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)sub.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$TemplateId", (object?)sub.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$RevisionNumber", sub.RevisionNumber);
            cmd.Parameters.AddWithValue("$UpdatedAt", sub.UpdatedAt);
            cmd.Parameters.AddWithValue("$DataJson", sub.DataJson);
            cmd.Parameters.AddWithValue("$Version", VersionFromDataJson(sub.DataJson));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertTraitAsync(
            SqliteConnection conn, SqliteTransaction tx, Trait trait)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO Traits
                    (Id, Name, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt)
                VALUES
                    ($Id, $Name, $Description, $Source, $OwnerUserId, $TemplateId, $RevisionNumber, $UpdatedAt)
                """;
            cmd.Parameters.AddWithValue("$Id", trait.Id);
            cmd.Parameters.AddWithValue("$Name", trait.Name);
            cmd.Parameters.AddWithValue("$Description", trait.Description);
            cmd.Parameters.AddWithValue("$Source", trait.Source);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)trait.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$TemplateId", (object?)trait.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$RevisionNumber", trait.RevisionNumber);
            cmd.Parameters.AddWithValue("$UpdatedAt", trait.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertClassAsync(
            SqliteConnection conn, SqliteTransaction tx, Class cls)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO Classes
                    (Id, Name, Description, HitDiceId, PrimaryAbility, Source,
                     OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                VALUES
                    ($Id, $Name, $Description, $HitDiceId, $PrimaryAbility, $Source,
                     $OwnerUserId, $TemplateId, $RevisionNumber, $UpdatedAt, $DataJson, $Version)
                """;
            cmd.Parameters.AddWithValue("$Id", cls.Id);
            cmd.Parameters.AddWithValue("$Name", cls.Name);
            cmd.Parameters.AddWithValue("$Description", cls.Description);
            cmd.Parameters.AddWithValue("$HitDiceId", cls.HitDiceId);
            cmd.Parameters.AddWithValue("$PrimaryAbility", cls.PrimaryAbility);
            cmd.Parameters.AddWithValue("$Source", cls.Source);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)cls.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$TemplateId", (object?)cls.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$RevisionNumber", cls.RevisionNumber);
            cmd.Parameters.AddWithValue("$UpdatedAt", cls.UpdatedAt);
            cmd.Parameters.AddWithValue("$DataJson", cls.DataJson);
            cmd.Parameters.AddWithValue("$Version", VersionFromDataJson(cls.DataJson));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertSpellAsync(
            SqliteConnection conn, SqliteTransaction tx, Spell spell)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO Spells
                    (Id, Name, Level, School, CastingTime, Duration, Range,
                     Concentration, Ritual, Description, Source,
                     OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                VALUES
                    ($Id, $Name, $Level, $School, $CastingTime, $Duration, $Range,
                     $Concentration, $Ritual, $Description, $Source,
                     $OwnerUserId, $TemplateId, $RevisionNumber, $UpdatedAt, $DataJson, $Version)
                """;
            cmd.Parameters.AddWithValue("$Id", spell.Id);
            cmd.Parameters.AddWithValue("$Name", spell.Name);
            cmd.Parameters.AddWithValue("$Level", spell.Level);
            cmd.Parameters.AddWithValue("$School", spell.School);
            cmd.Parameters.AddWithValue("$CastingTime", spell.CastingTime);
            cmd.Parameters.AddWithValue("$Duration", spell.Duration);
            cmd.Parameters.AddWithValue("$Range", spell.Range);
            cmd.Parameters.AddWithValue("$Concentration", spell.Concentration ? 1 : 0);
            cmd.Parameters.AddWithValue("$Ritual", spell.Ritual ? 1 : 0);
            cmd.Parameters.AddWithValue("$Description", spell.Description);
            cmd.Parameters.AddWithValue("$Source", spell.Source);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)spell.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$TemplateId", (object?)spell.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$RevisionNumber", spell.RevisionNumber);
            cmd.Parameters.AddWithValue("$UpdatedAt", spell.UpdatedAt);
            cmd.Parameters.AddWithValue("$DataJson", spell.DataJson);
            cmd.Parameters.AddWithValue("$Version", VersionFromDataJson(spell.DataJson));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertMemberAsync(SqliteConnection conn, SqliteTransaction tx, CampaignMember member)
        {
            await using (var userCmd = conn.CreateCommand())
            {
                userCmd.Transaction = tx;
                userCmd.CommandText = """
                    INSERT OR IGNORE INTO Users (Id, Username, CreatedAt)
                    VALUES ($Id, $Username, $CreatedAt)
                    """;
                userCmd.Parameters.AddWithValue("$Id", member.UserId);
                userCmd.Parameters.AddWithValue("$Username", member.Username ?? member.UserId);
                userCmd.Parameters.AddWithValue("$CreatedAt", member.JoinedAt.ToString("o"));
                await userCmd.ExecuteNonQueryAsync();
            }

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO CampaignMembers
                    (CampaignId, UserId, Role, CharacterId, JoinedAt)
                VALUES
                    ($CampaignId, $UserId, $Role, $CharacterId, $JoinedAt)
                """;
            cmd.Parameters.AddWithValue("$CampaignId", member.CampaignId);
            cmd.Parameters.AddWithValue("$UserId", member.UserId);
            cmd.Parameters.AddWithValue("$Role", member.Role);
            cmd.Parameters.AddWithValue("$CharacterId", (object?)member.CharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$JoinedAt", member.JoinedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertCharacterAsync(
    SqliteConnection conn, SqliteTransaction tx, Character c)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
        INSERT INTO Characters
            (Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
             Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson,
             CharacterKind, Slug, Tags, CreatedAt, VisibleToAll)
        VALUES
            ($Id, $CampaignId, $OwnerUserId, $Name, $RaceId, $SubraceId, $ClassId,
             $Level, $CurrentHp, $MaxHp, $AbilityScoresJson, $InventoryJson, $StateJson,
             $CharacterKind, $Slug, $Tags, $CreatedAt, $VisibleToAll)
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
            StateJson = excluded.StateJson,
            CharacterKind = excluded.CharacterKind,
            Slug = excluded.Slug,
            Tags = excluded.Tags,
            VisibleToAll = excluded.VisibleToAll
        """;
            cmd.Parameters.AddWithValue("$Id", c.Id);
            cmd.Parameters.AddWithValue("$CampaignId", c.CampaignId);
            cmd.Parameters.AddWithValue("$OwnerUserId", (object?)c.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Name", c.Name);
            cmd.Parameters.AddWithValue("$RaceId", (object?)c.RaceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$SubraceId", (object?)c.SubraceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ClassId", (object?)c.ClassId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Level", c.Level);
            cmd.Parameters.AddWithValue("$CurrentHp", c.CurrentHp);
            cmd.Parameters.AddWithValue("$MaxHp", c.MaxHp);
            cmd.Parameters.AddWithValue("$AbilityScoresJson", c.AbilityScoresJson);
            cmd.Parameters.AddWithValue("$InventoryJson", (object?)c.InventoryJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$StateJson", (object?)c.StateJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$CharacterKind", string.IsNullOrEmpty(c.CharacterKind) ? "pc" : c.CharacterKind);
            cmd.Parameters.AddWithValue("$Slug", (object?)c.Slug ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Tags", string.IsNullOrEmpty(c.Tags) ? "[]" : c.Tags);
            cmd.Parameters.AddWithValue("$CreatedAt", c.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$VisibleToAll", c.VisibleToAll ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertMapAsync(
            SqliteConnection conn, SqliteTransaction tx, Map map)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Maps
                    (Id, CampaignId, Name, Width, Height, Scale, MapPath, CreatedAt)
                VALUES
                    ($Id, $CampaignId, $Name, $Width, $Height, $Scale, $MapPath, $CreatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Width = excluded.Width,
                    Height = excluded.Height,
                    Scale = excluded.Scale,
                    MapPath = excluded.MapPath
                """;
            cmd.Parameters.AddWithValue("$Id", map.Id);
            cmd.Parameters.AddWithValue("$CampaignId", map.CampaignId);
            cmd.Parameters.AddWithValue("$Name", map.Name);
            cmd.Parameters.AddWithValue("$Width", map.Width);
            cmd.Parameters.AddWithValue("$Height", map.Height);
            cmd.Parameters.AddWithValue("$Scale", map.Scale);
            cmd.Parameters.AddWithValue("$MapPath", map.MapPath);
            cmd.Parameters.AddWithValue("$CreatedAt", map.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

                private static async Task UpsertCurrencyAsync(
            SqliteConnection conn, SqliteTransaction tx, Currency cur)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO Currencies
                    (Id, TemplateId, Name, Abbreviation, IsBase, EqualToBase, Color, IconSvg, SortOrder)
                VALUES
                    ($Id, $TemplateId, $Name, $Abbreviation, $IsBase, $EqualToBase, $Color, $IconSvg, $SortOrder)
            """;
            cmd.Parameters.AddWithValue("$Id", cur.Id);
            cmd.Parameters.AddWithValue("$TemplateId", (object?)cur.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Name", cur.Name);
            cmd.Parameters.AddWithValue("$Abbreviation", cur.Abbreviation);
            cmd.Parameters.AddWithValue("$IsBase", cur.IsBase ? 1 : 0);
            cmd.Parameters.AddWithValue("$EqualToBase", cur.EqualToBase);
            cmd.Parameters.AddWithValue("$Color", (object?)cur.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$IconSvg", (object?)cur.IconSvg ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$SortOrder", cur.SortOrder);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertItemInstanceAsync(
            SqliteConnection conn, SqliteTransaction tx, ItemInstance inst)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO ItemInstances
                    (Id, CampaignId, BaseItemId, OwnerCharacterId, ParentInstanceId, Quantity, CustomName, StateJson)
                VALUES
                    ($Id, $CampaignId, $BaseItemId, $OwnerCharacterId, $ParentInstanceId, $Quantity, $CustomName, $StateJson)
            """;
            cmd.Parameters.AddWithValue("$Id", inst.Id);
            cmd.Parameters.AddWithValue("$CampaignId", inst.CampaignId);
            cmd.Parameters.AddWithValue("$BaseItemId", inst.BaseItemId);
            cmd.Parameters.AddWithValue("$OwnerCharacterId", (object?)inst.OwnerCharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ParentInstanceId", (object?)inst.ParentInstanceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Quantity", inst.Quantity);
            cmd.Parameters.AddWithValue("$CustomName", (object?)inst.CustomName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$StateJson", (object?)inst.StateJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpsertTokenAsync(
            SqliteConnection conn, SqliteTransaction tx, MapToken token)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO MapTokens
                    (Id, MapId, CampaignId, OwnerCharacterId, X, Y, TokenImagePath, Label, Scale, Rotation, SizeName, IsProp, Blocks, BlocksSight)
                VALUES
                    ($Id, $MapId, $CampaignId, $OwnerCharacterId, $X, $Y, $TokenImagePath, $Label, $Scale, $Rotation, $SizeName, $IsProp, $Blocks, $BlocksSight)
                """;
            cmd.Parameters.AddWithValue("$Id", token.Id);
            cmd.Parameters.AddWithValue("$MapId", token.MapId);
            cmd.Parameters.AddWithValue("$CampaignId", token.CampaignId);
            cmd.Parameters.AddWithValue("$OwnerCharacterId", (object?)token.OwnerCharacterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$X", token.X);
            cmd.Parameters.AddWithValue("$Y", token.Y);
            cmd.Parameters.AddWithValue("$TokenImagePath", token.TokenImagePath);
            cmd.Parameters.AddWithValue("$Label", (object?)token.Label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Scale", token.Scale);
            cmd.Parameters.AddWithValue("$Rotation", token.Rotation);
            cmd.Parameters.AddWithValue("$SizeName", token.SizeName);
            cmd.Parameters.AddWithValue("$IsProp", token.IsProp ? 1 : 0);
            cmd.Parameters.AddWithValue("$Blocks", token.Blocks ? 1 : 0);
            cmd.Parameters.AddWithValue("$BlocksSight", token.BlocksSight ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }        
    }    
}