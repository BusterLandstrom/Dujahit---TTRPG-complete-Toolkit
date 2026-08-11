using Avalonia.Controls;
using Dujahit.Models.Application;
using Dujahit.Models.Communication;
using Dujahit.Models.Database;
using Dujahit.Models.Settings;
using Dujahit.Models.UI;
using Dujahit.ViewModels;
using Dujahit.Views;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Net;

namespace Dujahit.Models
{
    public class ProgramManager
    {
        public ThemeManager ThemeManager { get; set; }
        public SoundService Sound { get; private set; } = null!;
        private VersionManager _versionManager { get; set; } // Hmmm How should I handle this
        public VersionManager NewVersion {  get; set; }
        public CurrentCharacterService CurrentCharacterService { get; private set; }
        private CampaignManager _campaignManager;

        public string? CurrentTemplateId { get; private set; }

        private static readonly Dictionary<string, UserControl> _viewCache = new();

        public static event Action<UserControl> OnViewChange;

        public event Action<string, string>? OnGameDataChanged;

        private bool _isServer = false; // This might be set in future for off PC servers for now it is useless

        private DatabaseManager _dbManager;
        public DatabaseManager DbManager { get => _dbManager; internal set => _dbManager = value; }

        internal CampaignManager CampaignManagerForTests { get => _campaignManager; set => _campaignManager = value; }

        public CommunicationController ComController { get; private set; } // bla bla bla bla this I am not really doing this the cleanest way in all the connection scripts but whatever this is working so blabalbabllab
        public GameDataRepository GameDataRepo { get; private set; }
        public DmScreenRepository DmScreenRepo { get; private set; }
        public NotePageRepository NoteRepo { get; private set; }
        public MindmapRepository MindmapRepo { get; private set; }
        public CampaignChoiceRepository ChoiceRepo { get; private set; }
        public ProficiencyResolver ProfResolver { get; private set; }
        public TemplateCatalogReader CatalogReader { get; private set; }
        public MonsterCatalogReader MonsterReader { get; private set; }

        public ProgramManager()
        {
        }

        public async Task InitializeAsync(Action<string, double>? report = null)
        {
            report?.Invoke("Warming up the dice", 0.1);

            _dbManager = new DatabaseManager(
                Path.Combine(GlobalVariables.AppDataLocal,
                             GlobalVariables.AppName.ToLower() + ".db"))
            {
                AppVersion = App.CurrentVersion
            };
            _campaignManager = new CampaignManager(_dbManager);
            ComController = new CommunicationController();
            CurrentCharacterService = new CurrentCharacterService();
            Sound = new SoundService(this);
            Sound.Attach(ComController);

            report?.Invoke("Opening your data", 0.3);
            await _dbManager.InitializeAsync();

            report?.Invoke("Stocking the shelves", 0.6);
            GameDataRepo = new GameDataRepository(_dbManager);
            DmScreenRepo = new DmScreenRepository(_dbManager);
            NoteRepo = new NotePageRepository(_dbManager);
            MindmapRepo = new MindmapRepository(_dbManager);
            ChoiceRepo = new CampaignChoiceRepository(_dbManager);
            ProfResolver = new ProficiencyResolver(_dbManager);
            CatalogReader = new TemplateCatalogReader(_dbManager);
            MonsterReader = new MonsterCatalogReader(_dbManager);
            report?.Invoke("Loading your settings", 0.8);
            await LoadDmSettingsAsync();
            await Sound.LoadListenerSettingsAsync();
            ThemeManager = new ThemeManager(_dbManager, _campaignManager); // Input dbmanager here for update... or json so ppl can easily share idk lol or maybe store in db export and import using json, not sure yet
            await ThemeManager.EnsureDefaultThemeAsync();
            report?.Invoke("Applying your theme", 0.92);
            await ThemeManager.ApplyActiveThemeAsync();

            // NewVersion = await LoadConfigFromGitHubAsync();
            // _versionManager <-- init whebn needed
        }

        public string? LookupUserIdByName(string username)
        {
            return ComController.Members
                .FirstOrDefault(m => string.Equals(m.Username, username, StringComparison.OrdinalIgnoreCase))
                ?.UserId;
        }

        public async Task<List<Campaign>> ListMyCampaignsAsync()
        {
            return await _campaignManager.ListMyCampaignsAsync();
        }

        public async Task<List<User>> ListMyUsers() 
        {
            return await _campaignManager.ListMyUsersAsync();
        }
        public async Task<List<CharacterListEntry>> GetCampaignCharactersAsync(
                CancellationToken ct = default)
        {
            return await _campaignManager.GetCharactersForCampaignAsync(ct);
        }

        public async Task<CharacterListEntry?> CreateUnassignedCharacterAsync(
                CancellationToken ct = default)
        {
            var entry = await _campaignManager.CreateUnassignedCharacterAsync("Test", ct); // Implement this better with text from the little widget so the DM can either change the name or just have it blank "?"
            if (entry != null) await BroadcastCharacterAsync(entry.Id); // The quick one never broadcast either, so it sat local until this line.
            return entry;
        }

        public async Task<bool> IsCurrentUserDmAsync(CancellationToken ct = default)
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return false;

            var userId = GetUID();
            if (string.IsNullOrEmpty(userId)) return false;

            var role = await _campaignManager.GetRoleAsync(campaignId, userId);
            return string.Equals(role, "dm", StringComparison.OrdinalIgnoreCase);
        }

        // The rulebook that ships next to the exe. Null when somebody has emptied the folder, and then the picker is the only way in
        public static string? BundledTemplatePath()
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Templates");
                if (!Directory.Exists(dir)) return null;
                var files = Directory.GetFiles(dir, "*.json");
                return files.Length == 1 ? files[0] : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        public async Task LoadTemplateAsync(string templatePath) // I do not think I need the template path as a variable, I do not really have any use for it right now
        {
            // Load SRD template into the catalog tables when loading campaign (need to have a checker for sql template file instead when the campaign is already loaded into the db I do not have that right now)
            var loader = new TemplateLoader(_dbManager);

            Debug.WriteLine($"Looking for template at: {templatePath}");
            Debug.WriteLine($"File exists: {File.Exists(templatePath)}");

            if (File.Exists(templatePath))
            {
                Debug.WriteLine("Loading template...");
                CurrentTemplateId = await loader.LoadFromFileAsync(templatePath);
                Debug.WriteLine("Template loaded.");
            }
            else
            {
                Debug.WriteLine("Template file not found, skipping load.");
            }
        }

        public async Task<string> GetRoleAsync(string campaignId, string userId) 
        {
            return await _campaignManager.GetRoleAsync(campaignId, userId);
        }

        public User? GetCurrentUser() 
        {
            return _campaignManager.CurrentUser;
        }

        public async Task<List<CharacterRuntime>> LoadAllCharactersInCampaignAsync()
        {
            var result = new List<CharacterRuntime>();
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                        Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson,
                        StateJson, CharacterKind, Slug, Tags, CreatedAt, ClassLevelsJson
                FROM Characters
                WHERE CampaignId = $cid;
            ";
            cmd.Parameters.AddWithValue("$cid", campaign.Id);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = new Character
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
                    CreatedAt = DateTime.Parse(r.GetString(16)),
                    ClassLevelsJson = r.IsDBNull(17) ? null : r.GetString(17)
                };
                result.Add(CharacterMapper.ToRuntime(row));
            }
            return result;
        }

        public async Task ResolveCurrentUserCharacterAsync()
        {
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null) return;

            var userId = GetUID();

            await using var conn = await _dbManager.OpenAsync();

            await using var roleCmd = conn.CreateCommand();
            roleCmd.CommandText = @"
                SELECT Role, CharacterId
                FROM CampaignMembers
                WHERE CampaignId = $cid AND UserId = $uid;
            ";
            roleCmd.Parameters.AddWithValue("$cid", campaign.Id);
            roleCmd.Parameters.AddWithValue("$uid", userId);

            string? role = null;
            string? characterId = null;
            await using (var rr = await roleCmd.ExecuteReaderAsync())
            {
                if (await rr.ReadAsync())
                {
                    role = rr.GetString(0);
                    characterId = rr.IsDBNull(1) ? null : rr.GetString(1);
                }
            }

            if (role != "player")
            {
                CurrentCharacterService.Clear();
                return;
            }

            characterId = await GetPrimaryCharacterIdAsync()
                          ?? characterId
                          ?? await GetSoleOwnedCharacterIdAsync();

            if (string.IsNullOrEmpty(characterId))
            {
                CurrentCharacterService.Clear();
                return;
            }

            await using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                       Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson,
                       StateJson, CharacterKind, Slug, Tags, CreatedAt, ClassLevelsJson
                FROM Characters
                WHERE Id = $id;
            ";
            cmd2.Parameters.AddWithValue("$id", characterId);

            await using var r = await cmd2.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                CurrentCharacterService.Clear();
                return;
            }

            var row = new Character
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
                CreatedAt = DateTime.Parse(r.GetString(16)),
                ClassLevelsJson = r.IsDBNull(17) ? null : r.GetString(17)
            };

            var runtime = CharacterMapper.ToRuntime(row);
            CurrentCharacterService.Load(runtime);
        }

        public async Task<string?> GetPrimaryCharacterIdAsync()
        {
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null) return null;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CharacterId FROM PrimaryCharacters WHERE CampaignId = $cid AND UserId = $uid LIMIT 1";
            cmd.Parameters.AddWithValue("$cid", campaign.Id);
            cmd.Parameters.AddWithValue("$uid", GetUID());
            var id = await cmd.ExecuteScalarAsync() as string;
            if (string.IsNullOrEmpty(id)) return null;

            await using var chk = conn.CreateCommand();
            chk.CommandText = "SELECT 1 FROM Characters WHERE Id = $id LIMIT 1";
            chk.Parameters.AddWithValue("$id", id);
            return (await chk.ExecuteScalarAsync()) != null ? id : null;
        }

        public async Task<string?> GetSoleOwnedCharacterIdAsync()
        {
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null) return null;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id FROM Characters WHERE CampaignId = $cid AND OwnerUserId = $uid AND CharacterKind = 'pc'";
            cmd.Parameters.AddWithValue("$cid", campaign.Id);
            cmd.Parameters.AddWithValue("$uid", GetUID());

            string? only = null;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                if (only != null) return null;
                only = r.GetString(0);
            }
            return only;
        }

        public async Task SetPrimaryCharacterAsync(string characterId)
        {
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null || string.IsNullOrEmpty(characterId)) return;

            await using (var conn = await _dbManager.OpenAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO PrimaryCharacters (UserId, CampaignId, CharacterId)
                    VALUES ($uid, $cid, $char)
                    ON CONFLICT(UserId, CampaignId) DO UPDATE
                        SET CharacterId = excluded.CharacterId;
                    """;
                cmd.Parameters.AddWithValue("$uid", GetUID());
                cmd.Parameters.AddWithValue("$cid", campaign.Id);
                cmd.Parameters.AddWithValue("$char", characterId);
                await cmd.ExecuteNonQueryAsync();
            }

            await ResolveCurrentUserCharacterAsync(characterId);
        }

        public async Task<CharacterRuntime?> ResolveCurrentUserCharacterAsync(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                        Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson,
                        StateJson, CharacterKind, Slug, Tags, CreatedAt, ClassLevelsJson
                FROM Characters
                WHERE Id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", characterId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var row = new Character
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
                CreatedAt = DateTime.Parse(r.GetString(16)),
                ClassLevelsJson = r.IsDBNull(17) ? null : r.GetString(17)
            };

            var runtime = CharacterMapper.ToRuntime(row);
            CurrentCharacterService.Load(runtime);
            return runtime;
        }

        public async Task<CharacterRuntime?> LoadCharacterByIdAsync(string characterId) // ONLY USED FOR DM
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                       Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson,
                       StateJson, CharacterKind, Slug, Tags, CreatedAt, ClassLevelsJson
                FROM Characters
                WHERE Id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", characterId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var row = new Character
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
                CreatedAt = DateTime.Parse(r.GetString(16)),
                ClassLevelsJson = r.IsDBNull(17) ? null : r.GetString(17)
            };


            return CharacterMapper.ToRuntime(row);
        }

        public async Task<List<Map>> LoadMapsAsync()
        {
            var result = new List<Map>();
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, Name, Width, Height, Scale, MapPath, CreatedAt, PlayerVisible
                FROM Maps
                WHERE CampaignId = $cid
                ORDER BY CreatedAt ASC;";
            cmd.Parameters.AddWithValue("$cid", campaign.Id);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new Map
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Width = r.GetInt32(3),
                    Height = r.GetInt32(4),
                    Scale = r.GetDouble(5),
                    MapPath = r.GetString(6),
                    CreatedAt = DateTime.Parse(r.GetString(7)),
                    PlayerVisible = !r.IsDBNull(8) && r.GetInt32(8) != 0
                });
            }
            return result;
        }

        public async Task SaveMapAsync(Map map)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Maps (Id, CampaignId, Name, Width, Height, Scale, MapPath, CreatedAt, PlayerVisible)
                VALUES ($id, $cid, $name, $w, $h, $scale, $path, $created, $pv)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Width = excluded.Width,
                    Height = excluded.Height,
                    Scale = excluded.Scale,
                    MapPath = excluded.MapPath,
                    PlayerVisible = excluded.PlayerVisible;";
            cmd.Parameters.AddWithValue("$id", map.Id);
            cmd.Parameters.AddWithValue("$cid", map.CampaignId);
            cmd.Parameters.AddWithValue("$name", map.Name);
            cmd.Parameters.AddWithValue("$w", map.Width);
            cmd.Parameters.AddWithValue("$h", map.Height);
            cmd.Parameters.AddWithValue("$scale", map.Scale);
            cmd.Parameters.AddWithValue("$path", map.MapPath);
            cmd.Parameters.AddWithValue("$created", map.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$pv", map.PlayerVisible ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteMapAsync(string mapId)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Maps WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", mapId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetMapPlayerVisibleAsync(string mapId, bool visible)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Maps SET PlayerVisible = $pv WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$pv", visible ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", mapId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<(Encounter Encounter, List<EncounterCombatant> Combatants)?> LoadActiveEncounterAsync(string mapId)
        {
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null) return null;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, MapId, Name, Round, ActiveCombatantId, IsActive, CreatedAt, UpdatedAt
                FROM Encounters
                WHERE CampaignId = $cid AND MapId = $mid
                ORDER BY UpdatedAt DESC
                LIMIT 1;";
            cmd.Parameters.AddWithValue("$cid", campaign.Id);
            cmd.Parameters.AddWithValue("$mid", mapId);

            Encounter? enc = null;
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    enc = new Encounter
                    {
                        Id = r.GetString(0),
                        CampaignId = r.GetString(1),
                        MapId = r.IsDBNull(2) ? null : r.GetString(2),
                        Name = r.IsDBNull(3) ? null : r.GetString(3),
                        Round = r.GetInt32(4),
                        ActiveCombatantId = r.IsDBNull(5) ? null : r.GetString(5),
                        IsActive = r.GetInt32(6) == 1,
                        CreatedAt = DateTime.Parse(r.GetString(7)),
                        UpdatedAt = DateTime.Parse(r.GetString(8))
                    };
                }
            }
            if (enc == null) return null;

            var combatants = new List<EncounterCombatant>();
            await using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = @"
                SELECT Id, EncounterId, CharacterId, TokenId, Name, Initiative,
                       CurrentHp, MaxHp, IsPlayerCharacter, RevealExactHp, SortOrder, ConditionsJson,
                       MaxActions, ActionsRemaining, MaxBonusActions, BonusActionsRemaining, SpellSlotsJson, Concentration,
                       DeathSaveSuccesses, DeathSaveFailures, AttacksJson, IsFriendly, ExtrasJson
                FROM EncounterCombatants
                WHERE EncounterId = $eid
                ORDER BY SortOrder ASC;";
            cmd2.Parameters.AddWithValue("$eid", enc.Id);

            await using var r2 = await cmd2.ExecuteReaderAsync();
            while (await r2.ReadAsync())
            {
                combatants.Add(new EncounterCombatant
                {
                    Id = r2.GetString(0),
                    EncounterId = r2.GetString(1),
                    CharacterId = r2.IsDBNull(2) ? null : r2.GetString(2),
                    TokenId = r2.IsDBNull(3) ? null : r2.GetString(3),
                    Name = r2.GetString(4),
                    Initiative = r2.GetInt32(5),
                    CurrentHp = r2.GetInt32(6),
                    MaxHp = r2.GetInt32(7),
                    IsPlayerCharacter = r2.GetInt32(8) == 1,
                    RevealExactHp = r2.GetInt32(9) == 1,
                    SortOrder = r2.GetInt32(10),
                    ConditionsJson = r2.GetString(11),
                    MaxActions = r2.GetInt32(12),
                    ActionsRemaining = r2.GetInt32(13),
                    MaxBonusActions = r2.GetInt32(14),
                    BonusActionsRemaining = r2.GetInt32(15),
                    SpellSlotsJson = r2.IsDBNull(16) ? "" : r2.GetString(16),
                    Concentration = !r2.IsDBNull(17) && r2.GetInt32(17) == 1,
                    DeathSaveSuccesses = r2.IsDBNull(18) ? 0 : r2.GetInt32(18),
                    DeathSaveFailures = r2.IsDBNull(19) ? 0 : r2.GetInt32(19),
                    AttacksJson = r2.IsDBNull(20) ? "" : r2.GetString(20),
                    IsFriendly = !r2.IsDBNull(21) && r2.GetInt32(21) == 1,
                    ExtrasJson = r2.IsDBNull(22) ? "" : r2.GetString(22)
                });
            }

            return (enc, combatants);
        }

        public async Task SaveEncounterAsync(Encounter enc, List<EncounterCombatant> combatants)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var tx = conn.BeginTransaction();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO Encounters (Id, CampaignId, MapId, Name, Round, ActiveCombatantId, IsActive, CreatedAt, UpdatedAt)
                    VALUES ($id, $cid, $mid, $name, $round, $active, $isactive, $created, $updated)
                    ON CONFLICT(Id) DO UPDATE SET
                        Round = excluded.Round,
                        ActiveCombatantId = excluded.ActiveCombatantId,
                        IsActive = excluded.IsActive,
                        UpdatedAt = excluded.UpdatedAt;";
                cmd.Parameters.AddWithValue("$id", enc.Id);
                cmd.Parameters.AddWithValue("$cid", enc.CampaignId);
                cmd.Parameters.AddWithValue("$mid", (object?)enc.MapId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$name", (object?)enc.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$round", enc.Round);
                cmd.Parameters.AddWithValue("$active", (object?)enc.ActiveCombatantId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$isactive", enc.IsActive ? 1 : 0);
                cmd.Parameters.AddWithValue("$created", enc.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM EncounterCombatants WHERE EncounterId = $eid;";
                del.Parameters.AddWithValue("$eid", enc.Id);
                await del.ExecuteNonQueryAsync();
            }

            var order = 0;
            foreach (var c in combatants)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"
                    INSERT OR REPLACE INTO EncounterCombatants
                        (Id, EncounterId, CharacterId, TokenId, Name, Initiative,
                         CurrentHp, MaxHp, IsPlayerCharacter, RevealExactHp, SortOrder, ConditionsJson,
                         MaxActions, ActionsRemaining, MaxBonusActions, BonusActionsRemaining, SpellSlotsJson, Concentration,
                         DeathSaveSuccesses, DeathSaveFailures, AttacksJson, IsFriendly, ExtrasJson)
                    VALUES ($id, $eid, $charid, $tokenid, $name, $init, $hp, $maxhp, $ispc, $reveal, $order, $cond,
                            $maxact, $actrem, $maxbon, $bonrem, $slots, $conc, $dsucc, $dfail, $attacks, $friendly, $extras);";
                ins.Parameters.AddWithValue("$id", c.Id);
                ins.Parameters.AddWithValue("$eid", enc.Id);
                ins.Parameters.AddWithValue("$charid", (object?)c.CharacterId ?? DBNull.Value);
                ins.Parameters.AddWithValue("$tokenid", (object?)c.TokenId ?? DBNull.Value);
                ins.Parameters.AddWithValue("$name", c.Name);
                ins.Parameters.AddWithValue("$init", c.Initiative);
                ins.Parameters.AddWithValue("$hp", c.CurrentHp);
                ins.Parameters.AddWithValue("$maxhp", c.MaxHp);
                ins.Parameters.AddWithValue("$ispc", c.IsPlayerCharacter ? 1 : 0);
                ins.Parameters.AddWithValue("$reveal", c.RevealExactHp ? 1 : 0);
                ins.Parameters.AddWithValue("$order", order++);
                ins.Parameters.AddWithValue("$cond", c.ConditionsJson);
                ins.Parameters.AddWithValue("$maxact", c.MaxActions);
                ins.Parameters.AddWithValue("$actrem", c.ActionsRemaining);
                ins.Parameters.AddWithValue("$maxbon", c.MaxBonusActions);
                ins.Parameters.AddWithValue("$bonrem", c.BonusActionsRemaining);
                ins.Parameters.AddWithValue("$slots", (object?)(c.SpellSlotsJson ?? "") ?? "");
                ins.Parameters.AddWithValue("$conc", c.Concentration ? 1 : 0);
                ins.Parameters.AddWithValue("$dsucc", c.DeathSaveSuccesses);
                ins.Parameters.AddWithValue("$dfail", c.DeathSaveFailures);
                ins.Parameters.AddWithValue("$attacks", (object?)(c.AttacksJson ?? "") ?? "");
                ins.Parameters.AddWithValue("$friendly", c.IsFriendly ? 1 : 0);
                ins.Parameters.AddWithValue("$extras", (object?)(c.ExtrasJson ?? "") ?? "");
                await ins.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }

        public async Task EndEncounterAsync(string encounterId)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Encounters SET IsActive = 0, UpdatedAt = $now WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", encounterId);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<EncounterPreset>> LoadEncounterPresetsAsync()
        {
            var result = new List<EncounterPreset>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, Name, Notes, MonstersJson, SortOrder, CreatedAt, UpdatedAt
                FROM EncounterPresets
                WHERE CampaignId = $cid
                ORDER BY SortOrder ASC, UpdatedAt DESC;";
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var preset = new EncounterPreset
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Notes = r.IsDBNull(3) ? null : r.GetString(3),
                    SortOrder = r.GetInt32(5),
                    CreatedAt = DateTime.Parse(r.GetString(6)),
                    UpdatedAt = DateTime.Parse(r.GetString(7))
                };
                var monstersJson = r.IsDBNull(4) ? "[]" : r.GetString(4);
                try
                {
                    var entries = JsonSerializer.Deserialize<List<EncounterPresetEntry>>(monstersJson);
                    if (entries != null) preset.Monsters = entries;
                }
                catch (JsonException) { }
                result.Add(preset);
            }
            return result;
        }

        public async Task SaveEncounterPresetAsync(EncounterPreset preset)
        {
            if (preset == null) return;
            var monstersJson = JsonSerializer.Serialize(preset.Monsters ?? new List<EncounterPresetEntry>());

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO EncounterPresets (Id, CampaignId, Name, Notes, MonstersJson, SortOrder, CreatedAt, UpdatedAt)
                VALUES ($id, $cid, $name, $notes, $monsters, $sort, $created, $updated)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Notes = excluded.Notes,
                    MonstersJson = excluded.MonstersJson,
                    SortOrder = excluded.SortOrder,
                    UpdatedAt = excluded.UpdatedAt;";
            cmd.Parameters.AddWithValue("$id", preset.Id);
            cmd.Parameters.AddWithValue("$cid", preset.CampaignId);
            cmd.Parameters.AddWithValue("$name", preset.Name);
            cmd.Parameters.AddWithValue("$notes", (object?)preset.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$monsters", monstersJson);
            cmd.Parameters.AddWithValue("$sort", preset.SortOrder);
            cmd.Parameters.AddWithValue("$created", preset.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteEncounterPresetAsync(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM EncounterPresets WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", presetId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<Faction>> LoadFactionsAsync()
        {
            var result = new List<Faction>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, Name, Description, Color, NodeX, NodeY, CreatedAt
                FROM Factions
                WHERE CampaignId = $cid
                ORDER BY CreatedAt ASC;";
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new Faction
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    Color = r.IsDBNull(4) ? null : r.GetString(4),
                    NodeX = r.GetDouble(5),
                    NodeY = r.GetDouble(6),
                    CreatedAt = DateTime.Parse(r.GetString(7))
                });
            }
            return result;
        }

        public async Task SaveFactionAsync(Faction faction)
        {
            if (faction == null || string.IsNullOrEmpty(faction.Id)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Factions (Id, CampaignId, Name, Description, Color, NodeX, NodeY, CreatedAt)
                VALUES ($id, $cid, $name, $desc, $color, $x, $y, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Description = excluded.Description,
                    Color = excluded.Color,
                    NodeX = excluded.NodeX,
                    NodeY = excluded.NodeY;";
            cmd.Parameters.AddWithValue("$id", faction.Id);
            cmd.Parameters.AddWithValue("$cid", faction.CampaignId);
            cmd.Parameters.AddWithValue("$name", faction.Name);
            cmd.Parameters.AddWithValue("$desc", (object?)faction.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$color", (object?)faction.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$x", faction.NodeX);
            cmd.Parameters.AddWithValue("$y", faction.NodeY);
            cmd.Parameters.AddWithValue("$created", faction.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateFactionPositionAsync(string factionId, double x, double y)
        {
            if (string.IsNullOrEmpty(factionId)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Factions SET NodeX = $x, NodeY = $y WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$id", factionId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteFactionAsync(string factionId)
        {
            if (string.IsNullOrEmpty(factionId)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM FactionRelations WHERE FromFactionId = $id OR ToFactionId = $id;
                DELETE FROM Factions WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", factionId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<FactionRelation>> LoadFactionRelationsAsync()
        {
            var result = new List<FactionRelation>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, FromFactionId, ToFactionId, RelationType, Notes
                FROM FactionRelations
                WHERE CampaignId = $cid;";
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new FactionRelation
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    FromFactionId = r.GetString(2),
                    ToFactionId = r.GetString(3),
                    RelationType = r.GetString(4),
                    Notes = r.IsDBNull(5) ? null : r.GetString(5)
                });
            }
            return result;
        }

        public async Task SaveFactionRelationAsync(FactionRelation relation)
        {
            if (relation == null || string.IsNullOrEmpty(relation.Id)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO FactionRelations (Id, CampaignId, FromFactionId, ToFactionId, RelationType, Notes)
                VALUES ($id, $cid, $from, $to, $type, $notes)
                ON CONFLICT(Id) DO UPDATE SET
                    RelationType = excluded.RelationType,
                    Notes = excluded.Notes;";
            cmd.Parameters.AddWithValue("$id", relation.Id);
            cmd.Parameters.AddWithValue("$cid", relation.CampaignId);
            cmd.Parameters.AddWithValue("$from", relation.FromFactionId);
            cmd.Parameters.AddWithValue("$to", relation.ToFactionId);
            cmd.Parameters.AddWithValue("$type", relation.RelationType);
            cmd.Parameters.AddWithValue("$notes", (object?)relation.Notes ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteFactionRelationAsync(string relationId)
        {
            if (string.IsNullOrEmpty(relationId)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM FactionRelations WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", relationId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<SessionLogEntry>> LoadSessionLogAsync()
        {
            var result = new List<SessionLogEntry>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, SessionId, Timestamp, ActorUserId, ActorName, EventType, Summary, DetailJson
                FROM SessionLog
                WHERE CampaignId = $cid
                ORDER BY Timestamp DESC;";
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new SessionLogEntry
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    SessionId = r.IsDBNull(2) ? null : r.GetString(2),
                    Timestamp = DateTime.Parse(r.GetString(3)),
                    ActorUserId = r.IsDBNull(4) ? "" : r.GetString(4),
                    ActorName = r.IsDBNull(5) ? "" : r.GetString(5),
                    EventType = r.GetString(6),
                    Summary = r.GetString(7),
                    DetailJson = r.IsDBNull(8) ? null : r.GetString(8)
                });
            }
            return result;
        }

        public async Task LogEventAsync(string summary, string eventType = "event")
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId) || string.IsNullOrWhiteSpace(summary)) return;
            try
            {
                await using var conn = await _dbManager.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO SessionLog (Id, CampaignId, SessionId, Timestamp, ActorUserId, ActorName, EventType, Summary, DetailJson)
                    VALUES ($id, $cid, NULL, $ts, $auid, $an, $et, $sum, NULL);";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$cid", campaignId);
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$auid", GetUID());
                cmd.Parameters.AddWithValue("$an", GetUsername());
                cmd.Parameters.AddWithValue("$et", eventType);
                cmd.Parameters.AddWithValue("$sum", summary.Trim());
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { ErrorLog.Log("Writing a session log event failed", ex); }
        }

        public async Task<List<SearchHit>> SearchCampaignAsync(string query, bool isDm = true, string userId = "", CancellationToken ct = default)
        {
            var results = new List<SearchHit>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId) || string.IsNullOrWhiteSpace(query)) return results;
            var like = BuildLikePattern(query);

            await using var conn = await _dbManager.OpenAsync(ct);

            async Task RunAsync(string sql, Func<DbDataReader, SearchHit> map)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$cid", campaignId);
                cmd.Parameters.AddWithValue("$q", like);
                cmd.Parameters.AddWithValue("$uid", userId ?? "");
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) results.Add(map(r));
            }

            if (isDm)
            {
                await RunAsync(
                    "SELECT Id, Name FROM Characters WHERE CampaignId = $cid AND CharacterKind = 'npc' AND Name LIKE $q ESCAPE '\\' ORDER BY Name COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("npc", r.GetString(0), r.GetString(1), "NPC"));

                await RunAsync(
                    "SELECT Id, Name FROM Characters WHERE CampaignId = $cid AND CharacterKind != 'npc' AND Name LIKE $q ESCAPE '\\' ORDER BY Name COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("character", r.GetString(0), r.GetString(1), "Character"));

                await RunAsync(
                    "SELECT Id, Title FROM NotePages WHERE CampaignId = $cid AND Scope != 'campaign_story' AND Title LIKE $q ESCAPE '\\' ORDER BY Title COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("note", r.GetString(0), r.GetString(1), "Note"));

                await RunAsync(
                    "SELECT Id, Title FROM NotePages WHERE CampaignId = $cid AND Scope = 'campaign_story' AND Title LIKE $q ESCAPE '\\' ORDER BY Title COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("codex", r.GetString(0), r.GetString(1), "Codex"));

                await RunAsync(
                    "SELECT Id, Name FROM Maps WHERE CampaignId = $cid AND Name LIKE $q ESCAPE '\\' ORDER BY Name COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("map", r.GetString(0), r.GetString(1), "Map"));

                await RunAsync(
                    "SELECT Id, Name FROM Handouts WHERE CampaignId = $cid AND Name LIKE $q ESCAPE '\\' ORDER BY Name COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("handout", r.GetString(0), r.GetString(1), "Handout"));

                await RunAsync(
                    "SELECT Id, Name FROM EncounterPresets WHERE CampaignId = $cid AND Name LIKE $q ESCAPE '\\' ORDER BY Name COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("encounter", r.GetString(0), r.GetString(1), "Encounter"));

                await RunAsync(
                    "SELECT Id, Title FROM CalendarEvents WHERE CampaignId = $cid AND Title LIKE $q ESCAPE '\\' ORDER BY Title COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("calendar", r.GetString(0), r.GetString(1), "Calendar"));

                await RunAsync(
                    "SELECT Id, Title FROM TimelineEvents WHERE CampaignId = $cid AND Title LIKE $q ESCAPE '\\' ORDER BY Title COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("timeline", r.GetString(0), r.GetString(1), "Timeline"));

                await RunAsync(
                    "SELECT Id, Title FROM Mindmaps WHERE CampaignId = $cid AND Title LIKE $q ESCAPE '\\' ORDER BY Title COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("mindmap", r.GetString(0), r.GetString(1), "Mindmap"));
            }
            else
            {
                await RunAsync(
                    "SELECT Id, Name FROM Characters WHERE CampaignId = $cid AND CharacterKind != 'npc' AND OwnerUserId = $uid AND Name LIKE $q ESCAPE '\\' ORDER BY Name COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("character", r.GetString(0), r.GetString(1), "Character"));

                await RunAsync(
                    "SELECT Id, Name FROM Maps WHERE CampaignId = $cid AND PlayerVisible = 1 AND Name LIKE $q ESCAPE '\\' ORDER BY Name COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("map", r.GetString(0), r.GetString(1), "Map"));

                await RunAsync(
                    "SELECT Id, Title FROM Mindmaps WHERE CampaignId = $cid AND (OwnerUserId = $uid OR (Scope = 'shared' AND Id IN (SELECT MindmapId FROM MindmapShares WHERE UserId = $uid))) AND Title LIKE $q ESCAPE '\\' ORDER BY Title COLLATE NOCASE LIMIT 8;",
                    r => new SearchHit("mindmap", r.GetString(0), r.GetString(1), "Mindmap"));
            }

            await RunAsync(
                "SELECT i.Id, i.Name, i.ItemType FROM Items i JOIN CampaignItems ci ON ci.ItemId = i.Id WHERE ci.CampaignId = $cid AND i.Name LIKE $q ESCAPE '\\' ORDER BY i.Name COLLATE NOCASE LIMIT 8;",
                r => new SearchHit("item", r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "Item" : r.GetString(2)));

            await RunAsync(
                "SELECT s.Id, s.Name FROM Spells s JOIN CampaignSpells cs ON cs.SpellId = s.Id WHERE cs.CampaignId = $cid AND s.Name LIKE $q ESCAPE '\\' ORDER BY s.Name COLLATE NOCASE LIMIT 8;",
                r => new SearchHit("spell", r.GetString(0), r.GetString(1), "Spell"));

            return results;
        }

        public async Task<List<(string Id, string Name)>> LoadClassOptionsAsync()
        {
            var result = new List<(string, string)>();
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM Classes ORDER BY Name;";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) result.Add((r.GetString(0), r.GetString(1)));
            return result;
        }

        public async Task<string> TakeClassLevelAsync(CharacterRuntime runtime, string classId)
        {
            var already = runtime.ClassLevels.Any(c => string.Equals(c.ClassId, classId, StringComparison.OrdinalIgnoreCase));
            if (!already)
            {
                var blocker = MulticlassBlockerFor(classId, runtime);
                if (blocker.Length > 0) return blocker;

                // The class you are leaving has to still qualify too
                foreach (var c in runtime.ClassLevels)
                {
                    var leaving = MulticlassBlockerFor(c.ClassId, runtime);
                    if (leaving.Length > 0) return leaving;
                }
            }
            if (ClassLevels.TotalLevel(runtime.ClassLevels) >= Rules.MaxLevel) return "That character is already at the level cap.";

            var next = new List<ClassLevel>();
            foreach (var c in runtime.ClassLevels)
                next.Add(string.Equals(c.ClassId, classId, StringComparison.OrdinalIgnoreCase) ? c with { Level = c.Level + 1 } : c);
            if (!already) next.Add(new ClassLevel(classId, 1));

            await SaveClassLevelsAsync(runtime.Id, next);
            runtime.ClassLevels = next;
            runtime.ClassId = ClassLevels.PrimaryClassId(next);
            runtime.Level = ClassLevels.TotalLevel(next);
            return "";
        }

        public async Task<int> ResolveHitDieForClassAsync(string? classId)
        {
            if (string.IsNullOrEmpty(classId)) return 0;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT HitDiceId FROM Classes WHERE Id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", classId);
            var hitDiceId = await cmd.ExecuteScalarAsync() as string;
            if (string.IsNullOrEmpty(hitDiceId)) return 0;
            var digits = new string(hitDiceId.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var d) ? d : 0;
        }

        private static string BuildLikePattern(string query)
        {
            var q = query.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_")
                .Replace("*", "%");
            return "%" + q + "%";
        }

        public static string SoundsDir(string campaignId) =>
            Path.Combine(GlobalVariables.AppDataLocal, "assets", campaignId, "sounds");

        public string SoundFilePath(string campaignId, string fileName) =>
            Path.Combine(SoundsDir(campaignId), fileName);

        public async Task<List<SoundClip>> LoadSoundClipsAsync()
        {
            var result = new List<SoundClip>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, Name, Kind, FileName, IsFavourite, SortOrder, CreatedAt
                FROM SoundClips WHERE CampaignId = $cid
                ORDER BY Kind, Name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                result.Add(new SoundClip
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Name = r.GetString(2),
                    Kind = r.GetString(3),
                    FileName = r.GetString(4),
                    IsFavourite = r.GetInt32(5) == 1,
                    SortOrder = r.GetDouble(6),
                    CreatedAt = DateTime.Parse(r.GetString(7))
                });
            return result;
        }

        public async Task<SoundClip?> AddSoundClipFromFileAsync(string name, string kind, string sourcePath, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId) || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            var id = Guid.NewGuid().ToString("N");
            var fileName = id + Path.GetExtension(sourcePath);
            var dir = SoundsDir(campaignId);
            Directory.CreateDirectory(dir);
            await CopyWithProgressAsync(sourcePath, Path.Combine(dir, fileName), progress, ct);
            var clip = new SoundClip
            {
                Id = id,
                CampaignId = campaignId,
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(sourcePath) : name,
                Kind = kind == "music" ? "music" : "sfx",
                FileName = fileName,
                CreatedAt = DateTime.UtcNow
            };
            await UpsertSoundClipRowAsync(clip);
            return clip;
        }

        private static async Task CopyWithProgressAsync(string source, string dest, IProgress<double>? progress, CancellationToken ct)
        {
            await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var total = src.Length;
            var buffer = new byte[81920];
            long done = 0;
            int last = -1;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total <= 0) continue;
                var pct = (int)(done * 100 / total);
                if (pct == last) continue;
                last = pct;
                progress?.Report(pct);
            }
        }

        public async Task AppendSoundChunkAsync(string campaignId, string fileName, byte[] bytes, bool first)
        {
            if (string.IsNullOrEmpty(campaignId) || string.IsNullOrEmpty(fileName)) return;
            var dir = SoundsDir(campaignId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            await using var fs = new FileStream(path, first ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await fs.WriteAsync(bytes);
        }

        public Task SaveSoundClipRowAsync(SoundClip clip) => UpsertSoundClipRowAsync(clip);

        private async Task UpsertSoundClipRowAsync(SoundClip clip)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO SoundClips (Id, CampaignId, Name, Kind, FileName, IsFavourite, SortOrder, CreatedAt)
                VALUES ($id, $cid, $name, $kind, $file, $fav, $sort, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name, Kind = excluded.Kind, FileName = excluded.FileName, IsFavourite = excluded.IsFavourite;";
            cmd.Parameters.AddWithValue("$id", clip.Id);
            cmd.Parameters.AddWithValue("$cid", clip.CampaignId);
            cmd.Parameters.AddWithValue("$name", clip.Name);
            cmd.Parameters.AddWithValue("$kind", clip.Kind);
            cmd.Parameters.AddWithValue("$file", clip.FileName);
            cmd.Parameters.AddWithValue("$fav", clip.IsFavourite ? 1 : 0);
            cmd.Parameters.AddWithValue("$sort", clip.SortOrder);
            cmd.Parameters.AddWithValue("$created", clip.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetSoundFavouriteAsync(string id, bool fav)
        {
            if (string.IsNullOrEmpty(id)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE SoundClips SET IsFavourite = $f WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$f", fav ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteSoundClipAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var campaignId = GetCampaignId();
            await using var conn = await _dbManager.OpenAsync();
            string? file;
            await using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT FileName FROM SoundClips WHERE Id = $id LIMIT 1;";
                sel.Parameters.AddWithValue("$id", id);
                file = await sel.ExecuteScalarAsync() as string;
            }
            await using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM SoundClips WHERE Id = $id;";
                del.Parameters.AddWithValue("$id", id);
                await del.ExecuteNonQueryAsync();
            }
            if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(campaignId)
                && GlobalVariables.SafeChildPath(SoundsDir(campaignId), file) is string clip)
                try { File.Delete(clip); } catch { }
        }

        public async Task<List<RandomTable>> LoadRandomTablesAsync()
        {
            var list = new List<RandomTable>();
            try
            {
                var json = await GetActiveTemplateJsonAsync();
                if (!string.IsNullOrEmpty(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("RandomTables", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var t in arr.EnumerateArray())
                        {
                            var table = new RandomTable
                            {
                                Id = t.TryGetProperty("TemplateId", out var id) ? id.GetString() ?? "" : "",
                                Name = t.TryGetProperty("Name", out var nm) ? nm.GetString() ?? "" : "",
                                DiceExpression = t.TryGetProperty("DiceExpression", out var de) ? de.GetString() ?? "" : "",
                                IsTemplate = true
                            };
                            if (t.TryGetProperty("Entries", out var es) && es.ValueKind == JsonValueKind.Array)
                                foreach (var e in es.EnumerateArray())
                                    table.Entries.Add(new RandomTableEntry
                                    {
                                        Min = e.TryGetProperty("Min", out var mn) && mn.TryGetInt32(out var mnv) ? mnv : 0,
                                        Max = e.TryGetProperty("Max", out var mx) && mx.TryGetInt32(out var mxv) ? mxv : 0,
                                        Text = e.TryGetProperty("Text", out var tx) ? tx.GetString() ?? "" : ""
                                    });
                            if (!string.IsNullOrEmpty(table.Id) && table.Entries.Count > 0) list.Add(table);
                        }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[RandomTables] template read failed", ex);
            }

            list.AddRange(await GameDataRepo.LoadRandomTablesAsync(GetCampaignId()));
            return list;
        }

        public async Task SaveRandomTableAsync(RandomTable table)
        {
            if (string.IsNullOrEmpty(table.Id)) table.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(table.CampaignId)) table.CampaignId = GetCampaignId();
            await GameDataRepo.SaveRandomTableAsync(table);
        }

        public Task DeleteRandomTableAsync(string id) => GameDataRepo.DeleteRandomTableAsync(id);

        // A line is either plain text, or "3: text" or "2-5: text" to pin it onto the die, and plain lines just take the next free number.
        internal static List<RandomTableEntry> ParseTableEntries(string raw)
        {
            var entries = new List<RandomTableEntry>();
            if (string.IsNullOrWhiteSpace(raw)) return entries;
            var next = 1;
            foreach (var line in raw.Replace("\r", "").Split('\n'))
            {
                var text = line.Trim();
                if (text.Length == 0) continue;
                var m = Regex.Match(text, @"^(\d+)(?:\s*-\s*(\d+))?\s*:\s*(.+)$");
                if (m.Success)
                {
                    var a = int.Parse(m.Groups[1].Value);
                    var b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : a;
                    entries.Add(new RandomTableEntry { Min = Math.Min(a, b), Max = Math.Max(a, b), Text = m.Groups[3].Value.Trim() });
                    next = Math.Max(next, Math.Max(a, b) + 1);
                }
                else
                {
                    entries.Add(new RandomTableEntry { Min = next, Max = next, Text = text });
                    next++;
                }
            }
            return entries;
        }

        internal static string FormatTableEntries(List<RandomTableEntry> entries) =>
            string.Join("\n", entries.Select(e => (e.Min == e.Max ? e.Min.ToString() : e.Min + "-" + e.Max) + ": " + e.Text));

        internal static RandomTableEntry? TableEntryFor(RandomTable table, int roll) =>
            table.Entries.FirstOrDefault(e => roll >= e.Min && roll <= e.Max);

        public static (int Roll, string Text)? RollOnTable(RandomTable table)
        {
            if (table == null || table.Entries.Count == 0) return null;
            var expr = string.IsNullOrWhiteSpace(table.DiceExpression) ? "1d" + table.Entries.Max(e => e.Max) : table.DiceExpression;
            if (!DiceManager.TryRoll(expr, out var result) || result == null) return null;
            var hit = TableEntryFor(table, result.Total);
            return (result.Total, hit?.Text ?? "nothing, the table has a hole at " + result.Total);
        }

        public async Task<List<CalendarEvent>> LoadCalendarEventsAsync()
        {
            var result = new List<CalendarEvent>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, Title, Kind, EventDate, InWorldDate, Notes, CreatedAt
                FROM CalendarEvents
                WHERE CampaignId = $cid
                ORDER BY EventDate IS NULL, EventDate ASC, CreatedAt ASC;";
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new CalendarEvent
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Title = r.GetString(2),
                    Kind = r.IsDBNull(3) ? "session" : r.GetString(3),
                    EventDate = r.IsDBNull(4) ? null : r.GetString(4),
                    InWorldDate = r.IsDBNull(5) ? null : r.GetString(5),
                    Notes = r.IsDBNull(6) ? null : r.GetString(6),
                    CreatedAt = DateTime.Parse(r.GetString(7))
                });
            }
            return result;
        }

        public async Task SaveCalendarEventAsync(CalendarEvent ev)
        {
            if (ev == null || string.IsNullOrEmpty(ev.Id)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CalendarEvents (Id, CampaignId, Title, Kind, EventDate, InWorldDate, Notes, CreatedAt)
                VALUES ($id, $cid, $title, $kind, $date, $world, $notes, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Title = excluded.Title,
                    Kind = excluded.Kind,
                    EventDate = excluded.EventDate,
                    InWorldDate = excluded.InWorldDate,
                    Notes = excluded.Notes;";
            cmd.Parameters.AddWithValue("$id", ev.Id);
            cmd.Parameters.AddWithValue("$cid", ev.CampaignId);
            cmd.Parameters.AddWithValue("$title", ev.Title);
            cmd.Parameters.AddWithValue("$kind", ev.Kind);
            cmd.Parameters.AddWithValue("$date", (object?)ev.EventDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$world", (object?)ev.InWorldDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", (object?)ev.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", ev.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteCalendarEventAsync(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CalendarEvents WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", eventId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<TimelineEvent>> LoadTimelineEventsAsync()
        {
            var result = new List<TimelineEvent>();
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, Title, Description, InWorldDate, SortOrder, CreatedAt
                FROM TimelineEvents
                WHERE CampaignId = $cid
                ORDER BY SortOrder ASC, CreatedAt ASC;";
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new TimelineEvent
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    Title = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    InWorldDate = r.IsDBNull(4) ? null : r.GetString(4),
                    SortOrder = r.GetDouble(5),
                    CreatedAt = DateTime.Parse(r.GetString(6))
                });
            }
            return result;
        }

        public async Task SaveTimelineEventAsync(TimelineEvent ev)
        {
            if (ev == null || string.IsNullOrEmpty(ev.Id)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO TimelineEvents (Id, CampaignId, Title, Description, InWorldDate, SortOrder, CreatedAt)
                VALUES ($id, $cid, $title, $desc, $world, $sort, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Title = excluded.Title,
                    Description = excluded.Description,
                    InWorldDate = excluded.InWorldDate,
                    SortOrder = excluded.SortOrder;";
            cmd.Parameters.AddWithValue("$id", ev.Id);
            cmd.Parameters.AddWithValue("$cid", ev.CampaignId);
            cmd.Parameters.AddWithValue("$title", ev.Title);
            cmd.Parameters.AddWithValue("$desc", (object?)ev.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$world", (object?)ev.InWorldDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sort", ev.SortOrder);
            cmd.Parameters.AddWithValue("$created", ev.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteTimelineEventAsync(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TimelineEvents WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", eventId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task BackupDatabaseToFileAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            await using var source = await _dbManager.OpenAsync();
            await using var dest = new SqliteConnection($"Data Source={path}");
            await dest.OpenAsync();
            source.BackupDatabase(dest);
        }

        public string? StageDatabaseRestore(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return "No file was picked.";
            return _dbManager.StagePendingRestore(sourcePath);
        }

        public string GetBackupsDirectory() => _dbManager.BackupsDirectory;

        public async Task ExportCharacterToFileAsync(string characterId, string path)
        {
            if (string.IsNullOrEmpty(characterId) || string.IsNullOrWhiteSpace(path)) return;

            await using var conn = await _dbManager.OpenAsync();

            Character? ch = null;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                           Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson,
                           StateJson, CharacterKind, Slug, Tags, CreatedAt, ClassLevelsJson
                    FROM Characters WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", characterId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    ch = new Character
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
                        CreatedAt = DateTime.Parse(r.GetString(16)),
                        ClassLevelsJson = r.IsDBNull(17) ? null : r.GetString(17)
                    };
                }
            }
            if (ch == null) return;

            var instances = new List<ItemInstance>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, CampaignId, BaseItemId, OwnerCharacterId, Quantity, CustomName, ParentInstanceId, StateJson
                    FROM ItemInstances WHERE OwnerCharacterId = $id;";
                cmd.Parameters.AddWithValue("$id", characterId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    instances.Add(new ItemInstance
                    {
                        Id = r.GetString(0),
                        CampaignId = r.GetString(1),
                        BaseItemId = r.GetString(2),
                        OwnerCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                        Quantity = r.GetInt32(4),
                        CustomName = r.IsDBNull(5) ? null : r.GetString(5),
                        ParentInstanceId = r.IsDBNull(6) ? null : r.GetString(6),
                        StateJson = r.IsDBNull(7) ? null : r.GetString(7)
                    });
                }
            }

            var bundle = new CharacterExportBundle { Character = ch, Instances = instances, ExportedAt = DateTime.UtcNow };

            var itemIds = instances.Select(i => i.BaseItemId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            bundle.CustomItems = await LoadCustomItemsAsync(conn, itemIds);
            bundle.CustomSpells = await LoadCustomSpellsAsync(conn, PreparedSpellIdsFromState(ch.StateJson));
            bundle.CustomRaces = await LoadCustomRacesAsync(conn, SingleId(ch.RaceId));
            bundle.CustomSubraces = await LoadCustomSubracesAsync(conn, SingleId(ch.SubraceId));
            bundle.CustomClasses = await LoadCustomClassesAsync(conn, SingleId(ch.ClassId));

            var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        private static List<string> SingleId(string? id) => string.IsNullOrEmpty(id) ? new List<string>() : new List<string> { id };

        private static List<string> PreparedSpellIdsFromState(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return new List<string>();
            try
            {
                var arr = JsonNode.Parse(stateJson)?["PreparedSpellIds"]?.AsArray();
                if (arr == null) return new List<string>();
                return arr.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().ToList();
            }
            catch (Exception) { return new List<string>(); }
        }

        private static string InClause(string prefix, IReadOnlyList<string> ids, SqliteCommand cmd)
        {
            var names = new List<string>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                var p = "$" + prefix + i;
                names.Add(p);
                cmd.Parameters.AddWithValue(p, ids[i]);
            }
            return string.Join(", ", names);
        }

        private static async Task<List<Item>> LoadCustomItemsAsync(SqliteConnection conn, IReadOnlyList<string> ids)
        {
            var list = new List<Item>();
            if (ids.Count == 0) return list;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT Id, Name, ItemType, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Slug, Tags
                FROM Items WHERE Source = 'custom' AND Id IN ({InClause("it", ids, cmd)});";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Item
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    ItemType = r.IsDBNull(2) ? "Generic" : r.GetString(2),
                    Source = r.GetString(3),
                    OwnerUserId = r.IsDBNull(4) ? null : r.GetString(4),
                    TemplateId = r.IsDBNull(5) ? null : r.GetString(5),
                    RevisionNumber = r.GetInt32(6),
                    UpdatedAt = r.IsDBNull(7) ? "" : r.GetString(7),
                    DataJson = r.IsDBNull(8) ? "{}" : r.GetString(8),
                    Slug = r.IsDBNull(9) ? null : r.GetString(9),
                    Tags = r.IsDBNull(10) ? "[]" : r.GetString(10)
                });
            return list;
        }

        private static async Task<List<Spell>> LoadCustomSpellsAsync(SqliteConnection conn, IReadOnlyList<string> ids)
        {
            var list = new List<Spell>();
            if (ids.Count == 0) return list;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT Id, Name, Level, School, CastingTime, Duration, Range, Concentration, Ritual, Description,
                       Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson
                FROM Spells WHERE Source = 'custom' AND Id IN ({InClause("sp", ids, cmd)});";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Spell
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Level = r.GetInt32(2),
                    School = r.IsDBNull(3) ? "" : r.GetString(3),
                    CastingTime = r.IsDBNull(4) ? "" : r.GetString(4),
                    Duration = r.IsDBNull(5) ? "" : r.GetString(5),
                    Range = r.IsDBNull(6) ? "" : r.GetString(6),
                    Concentration = !r.IsDBNull(7) && r.GetInt32(7) != 0,
                    Ritual = !r.IsDBNull(8) && r.GetInt32(8) != 0,
                    Description = r.IsDBNull(9) ? "" : r.GetString(9),
                    Source = r.GetString(10),
                    OwnerUserId = r.IsDBNull(11) ? null : r.GetString(11),
                    TemplateId = r.IsDBNull(12) ? null : r.GetString(12),
                    RevisionNumber = r.GetInt32(13),
                    UpdatedAt = r.IsDBNull(14) ? "" : r.GetString(14),
                    DataJson = r.IsDBNull(15) ? "{}" : r.GetString(15)
                });
            return list;
        }

        private static async Task<List<Race>> LoadCustomRacesAsync(SqliteConnection conn, IReadOnlyList<string> ids)
        {
            var list = new List<Race>();
            if (ids.Count == 0) return list;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT Id, Name, Description, Size, Speed, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson
                FROM Races WHERE Source = 'custom' AND Id IN ({InClause("ra", ids, cmd)});";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Race
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? "" : r.GetString(2),
                    Size = r.IsDBNull(3) ? "" : r.GetString(3),
                    Speed = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    Source = r.GetString(5),
                    OwnerUserId = r.IsDBNull(6) ? null : r.GetString(6),
                    TemplateId = r.IsDBNull(7) ? null : r.GetString(7),
                    RevisionNumber = r.GetInt32(8),
                    UpdatedAt = r.IsDBNull(9) ? "" : r.GetString(9),
                    DataJson = r.IsDBNull(10) ? "{}" : r.GetString(10)
                });
            return list;
        }

        private static async Task<List<Subrace>> LoadCustomSubracesAsync(SqliteConnection conn, IReadOnlyList<string> ids)
        {
            var list = new List<Subrace>();
            if (ids.Count == 0) return list;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT Id, Name, ParentRaceId, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson
                FROM Subraces WHERE Source = 'custom' AND Id IN ({InClause("su", ids, cmd)});";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Subrace
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    ParentRaceId = r.IsDBNull(2) ? "" : r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    Source = r.GetString(4),
                    OwnerUserId = r.IsDBNull(5) ? null : r.GetString(5),
                    TemplateId = r.IsDBNull(6) ? null : r.GetString(6),
                    RevisionNumber = r.GetInt32(7),
                    UpdatedAt = r.IsDBNull(8) ? "" : r.GetString(8),
                    DataJson = r.IsDBNull(9) ? "{}" : r.GetString(9)
                });
            return list;
        }

        private static async Task<List<Class>> LoadCustomClassesAsync(SqliteConnection conn, IReadOnlyList<string> ids)
        {
            var list = new List<Class>();
            if (ids.Count == 0) return list;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT Id, Name, Description, HitDiceId, PrimaryAbility, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson
                FROM Classes WHERE Source = 'custom' AND Id IN ({InClause("cl", ids, cmd)});";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Class
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? "" : r.GetString(2),
                    HitDiceId = r.IsDBNull(3) ? "" : r.GetString(3),
                    PrimaryAbility = r.IsDBNull(4) ? "" : r.GetString(4),
                    Source = r.GetString(5),
                    OwnerUserId = r.IsDBNull(6) ? null : r.GetString(6),
                    TemplateId = r.IsDBNull(7) ? null : r.GetString(7),
                    RevisionNumber = r.GetInt32(8),
                    UpdatedAt = r.IsDBNull(9) ? "" : r.GetString(9),
                    DataJson = r.IsDBNull(10) ? "{}" : r.GetString(10)
                });
            return list;
        }

        public async Task<(string? NewId, List<string> Warnings)> ImportCharacterFromFileAsync(string path)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (null, warnings);
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return (null, warnings);

            CharacterExportBundle? bundle;
            try { bundle = JsonSerializer.Deserialize<CharacterExportBundle>(await File.ReadAllTextAsync(path)); }
            catch (JsonException) { return (null, warnings); }
            if (bundle?.Character == null || string.IsNullOrEmpty(bundle.Character.Id)) return (null, warnings);

            var ch = bundle.Character;
            var newId = Guid.NewGuid().ToString();

            var idMap = new Dictionary<string, string>();
            foreach (var inst in bundle.Instances)
                if (!string.IsNullOrEmpty(inst.Id) && !idMap.ContainsKey(inst.Id))
                    idMap[inst.Id] = Guid.NewGuid().ToString("N");

            await using var conn = await _dbManager.OpenAsync();

            var now = DateTime.UtcNow.ToString("o");
            // Same deal as the campaign import further down, one deferred fk transaction so a bad row takes the whole character with it
            await using (var begin = conn.CreateCommand()) { begin.CommandText = "BEGIN; PRAGMA defer_foreign_keys = ON;"; await begin.ExecuteNonQueryAsync(); }
            foreach (var it in bundle.CustomItems)
            {
                await InsertItemIfAbsentAsync(conn, it);
                await AddCampaignLinkAsync(conn, "CampaignItems", "ItemId", campaignId, it.Id, now);
            }
            foreach (var sp in bundle.CustomSpells)
            {
                await InsertSpellIfAbsentAsync(conn, sp);
                await AddCampaignLinkAsync(conn, "CampaignSpells", "SpellId", campaignId, sp.Id, now);
            }
            foreach (var ra in bundle.CustomRaces)
            {
                await InsertRaceIfAbsentAsync(conn, ra);
                await AddCampaignLinkAsync(conn, "CampaignRaces", "RaceId", campaignId, ra.Id, now);
            }
            foreach (var su in bundle.CustomSubraces)
                await InsertSubraceIfAbsentAsync(conn, su);
            foreach (var cl in bundle.CustomClasses)
            {
                await InsertClassIfAbsentAsync(conn, cl);
                await AddCampaignLinkAsync(conn, "CampaignClasses", "ClassId", campaignId, cl.Id, now);
            }

            var raceId = await ResolveRefOrWarnAsync(conn, "Races", ch.RaceId, "race", warnings);
            var subraceId = await ResolveRefOrWarnAsync(conn, "Subraces", ch.SubraceId, "subrace", warnings);
            var classId = await ResolveRefOrWarnAsync(conn, "Classes", ch.ClassId, "class", warnings);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Characters
                        (Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                         Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson,
                         CharacterKind, Slug, Tags, CreatedAt, ClassLevelsJson)
                    VALUES
                        ($id, $cid, NULL, $name, $rid, $srid, $clid,
                         $lvl, $chp, $mhp, $abil, $inv, $state,
                         $kind, NULL, $tags, $created, $cls);";
                cmd.Parameters.AddWithValue("$id", newId);
                cmd.Parameters.AddWithValue("$cid", campaignId);
                cmd.Parameters.AddWithValue("$cls", (object?)ch.ClassLevelsJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(ch.Name) ? "Imported character" : ch.Name + " (imported)");
                cmd.Parameters.AddWithValue("$rid", (object?)raceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$srid", (object?)subraceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$clid", (object?)classId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$lvl", ch.Level);
                cmd.Parameters.AddWithValue("$chp", ch.CurrentHp);
                cmd.Parameters.AddWithValue("$mhp", ch.MaxHp);
                cmd.Parameters.AddWithValue("$abil", string.IsNullOrWhiteSpace(ch.AbilityScoresJson) ? "{}" : ch.AbilityScoresJson);
                cmd.Parameters.AddWithValue("$inv", RemapInventoryJson(ch.InventoryJson, idMap));
                cmd.Parameters.AddWithValue("$state", (object?)ch.StateJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$kind", string.IsNullOrEmpty(ch.CharacterKind) ? "pc" : ch.CharacterKind);
                cmd.Parameters.AddWithValue("$tags", string.IsNullOrEmpty(ch.Tags) ? "[]" : ch.Tags);
                cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("o"));
                await cmd.ExecuteNonQueryAsync();
            }

            int droppedItems = 0;
            foreach (var inst in bundle.Instances)
            {
                if (string.IsNullOrEmpty(inst.Id) || !idMap.TryGetValue(inst.Id, out var newInstId)) continue;
                if (!await RowExistsAsync(conn, "Items", inst.BaseItemId)) { droppedItems++; continue; }
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO ItemInstances
                            (Id, CampaignId, BaseItemId, OwnerCharacterId, Quantity, CustomName, ParentInstanceId, StateJson)
                        VALUES ($id, $cid, $base, $owner, $qty, $cname, $parent, $state);";
                    cmd.Parameters.AddWithValue("$id", newInstId);
                    cmd.Parameters.AddWithValue("$cid", campaignId);
                    cmd.Parameters.AddWithValue("$base", inst.BaseItemId);
                    cmd.Parameters.AddWithValue("$owner", newId);
                    cmd.Parameters.AddWithValue("$qty", inst.Quantity);
                    cmd.Parameters.AddWithValue("$cname", (object?)inst.CustomName ?? DBNull.Value);
                    var parent = inst.ParentInstanceId != null && idMap.TryGetValue(inst.ParentInstanceId, out var np) ? np : null;
                    cmd.Parameters.AddWithValue("$parent", (object?)parent ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$state", (object?)inst.StateJson ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex) { droppedItems++; ErrorLog.Log($"[Import] skipped instance {inst.Id}", ex); }
            }
            if (droppedItems > 0) warnings.Add(droppedItems == 1 ? "1 inventory item" : droppedItems + " inventory items");

            var missingSpells = 0;
            foreach (var sid in PreparedSpellIdsFromState(ch.StateJson))
                if (!await RowExistsAsync(conn, "Spells", sid)) missingSpells++;
            if (missingSpells > 0) warnings.Add(missingSpells == 1 ? "1 prepared spell" : missingSpells + " prepared spells");

            await using (var commit = conn.CreateCommand()) { commit.CommandText = "COMMIT;"; await commit.ExecuteNonQueryAsync(); }
            return (newId, warnings);
        }

        private static async Task<bool> RowExistsAsync(SqliteConnection conn, string table, string? id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {table} WHERE Id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", id);
            return await cmd.ExecuteScalarAsync() != null;
        }

        private static async Task<string?> ResolveRefOrWarnAsync(SqliteConnection conn, string table, string? id, string label, List<string> warnings)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (await RowExistsAsync(conn, table, id)) return id;
            warnings.Add("a custom " + label);
            return null;
        }

        private static async Task AddCampaignLinkAsync(SqliteConnection conn, string table, string idColumn, string campaignId, string id, string now)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT OR IGNORE INTO {table} (CampaignId, {idColumn}, AddedAt, IsEnabled) VALUES ($cid, $id, $ts, 1);";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$ts", now);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertItemIfAbsentAsync(SqliteConnection conn, Item it)
        {
            if (string.IsNullOrEmpty(it.Id)) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Items
                    (Id, Name, ItemType, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Slug, Tags)
                VALUES ($Id, $Name, $ItemType, 'custom', NULL, NULL, $Rev, $Upd, $Data, $Slug, $Tags);";
            cmd.Parameters.AddWithValue("$Id", it.Id);
            cmd.Parameters.AddWithValue("$Name", it.Name);
            cmd.Parameters.AddWithValue("$ItemType", string.IsNullOrEmpty(it.ItemType) ? "Generic" : it.ItemType);
            cmd.Parameters.AddWithValue("$Rev", it.RevisionNumber);
            cmd.Parameters.AddWithValue("$Upd", string.IsNullOrEmpty(it.UpdatedAt) ? DateTime.UtcNow.ToString("o") : it.UpdatedAt);
            cmd.Parameters.AddWithValue("$Data", string.IsNullOrEmpty(it.DataJson) ? "{}" : it.DataJson);
            cmd.Parameters.AddWithValue("$Slug", (object?)it.Slug ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Tags", string.IsNullOrEmpty(it.Tags) ? "[]" : it.Tags);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertSpellIfAbsentAsync(SqliteConnection conn, Spell sp)
        {
            if (string.IsNullOrEmpty(sp.Id)) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Spells
                    (Id, Name, Level, School, CastingTime, Duration, Range, Concentration, Ritual, Description,
                     Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson)
                VALUES ($Id, $Name, $Level, $School, $Cast, $Dur, $Range, $Conc, $Rit, $Desc,
                     'custom', NULL, NULL, $Rev, $Upd, $Data);";
            cmd.Parameters.AddWithValue("$Id", sp.Id);
            cmd.Parameters.AddWithValue("$Name", sp.Name);
            cmd.Parameters.AddWithValue("$Level", sp.Level);
            cmd.Parameters.AddWithValue("$School", sp.School ?? "");
            cmd.Parameters.AddWithValue("$Cast", sp.CastingTime ?? "");
            cmd.Parameters.AddWithValue("$Dur", sp.Duration ?? "");
            cmd.Parameters.AddWithValue("$Range", sp.Range ?? "");
            cmd.Parameters.AddWithValue("$Conc", sp.Concentration ? 1 : 0);
            cmd.Parameters.AddWithValue("$Rit", sp.Ritual ? 1 : 0);
            cmd.Parameters.AddWithValue("$Desc", sp.Description ?? "");
            cmd.Parameters.AddWithValue("$Rev", sp.RevisionNumber);
            cmd.Parameters.AddWithValue("$Upd", string.IsNullOrEmpty(sp.UpdatedAt) ? DateTime.UtcNow.ToString("o") : sp.UpdatedAt);
            cmd.Parameters.AddWithValue("$Data", string.IsNullOrEmpty(sp.DataJson) ? "{}" : sp.DataJson);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertRaceIfAbsentAsync(SqliteConnection conn, Race ra)
        {
            if (string.IsNullOrEmpty(ra.Id)) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Races
                    (Id, Name, Description, Size, Speed, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson)
                VALUES ($Id, $Name, $Desc, $Size, $Speed, 'custom', NULL, NULL, $Rev, $Upd, $Data);";
            cmd.Parameters.AddWithValue("$Id", ra.Id);
            cmd.Parameters.AddWithValue("$Name", ra.Name);
            cmd.Parameters.AddWithValue("$Desc", ra.Description ?? "");
            cmd.Parameters.AddWithValue("$Size", ra.Size ?? "");
            cmd.Parameters.AddWithValue("$Speed", ra.Speed);
            cmd.Parameters.AddWithValue("$Rev", ra.RevisionNumber);
            cmd.Parameters.AddWithValue("$Upd", string.IsNullOrEmpty(ra.UpdatedAt) ? DateTime.UtcNow.ToString("o") : ra.UpdatedAt);
            cmd.Parameters.AddWithValue("$Data", string.IsNullOrEmpty(ra.DataJson) ? "{}" : ra.DataJson);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertSubraceIfAbsentAsync(SqliteConnection conn, Subrace su)
        {
            if (string.IsNullOrEmpty(su.Id)) return;
            if (!await RowExistsAsync(conn, "Races", su.ParentRaceId)) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Subraces
                    (Id, Name, ParentRaceId, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson)
                VALUES ($Id, $Name, $Parent, $Desc, 'custom', NULL, NULL, $Rev, $Upd, $Data);";
            cmd.Parameters.AddWithValue("$Id", su.Id);
            cmd.Parameters.AddWithValue("$Name", su.Name);
            cmd.Parameters.AddWithValue("$Parent", su.ParentRaceId);
            cmd.Parameters.AddWithValue("$Desc", (object?)su.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Rev", su.RevisionNumber);
            cmd.Parameters.AddWithValue("$Upd", string.IsNullOrEmpty(su.UpdatedAt) ? DateTime.UtcNow.ToString("o") : su.UpdatedAt);
            cmd.Parameters.AddWithValue("$Data", string.IsNullOrEmpty(su.DataJson) ? "{}" : su.DataJson);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertClassIfAbsentAsync(SqliteConnection conn, Class cl)
        {
            if (string.IsNullOrEmpty(cl.Id)) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Classes
                    (Id, Name, Description, HitDiceId, PrimaryAbility, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson)
                VALUES ($Id, $Name, $Desc, $Hit, $Prim, 'custom', NULL, NULL, $Rev, $Upd, $Data);";
            cmd.Parameters.AddWithValue("$Id", cl.Id);
            cmd.Parameters.AddWithValue("$Name", cl.Name);
            cmd.Parameters.AddWithValue("$Desc", cl.Description ?? "");
            cmd.Parameters.AddWithValue("$Hit", cl.HitDiceId ?? "");
            cmd.Parameters.AddWithValue("$Prim", cl.PrimaryAbility ?? "");
            cmd.Parameters.AddWithValue("$Rev", cl.RevisionNumber);
            cmd.Parameters.AddWithValue("$Upd", string.IsNullOrEmpty(cl.UpdatedAt) ? DateTime.UtcNow.ToString("o") : cl.UpdatedAt);
            cmd.Parameters.AddWithValue("$Data", string.IsNullOrEmpty(cl.DataJson) ? "{}" : cl.DataJson);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ExportCampaignToFileAsync(string path)
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId) || string.IsNullOrWhiteSpace(path)) return;

            await using var conn = await _dbManager.OpenAsync();
            var bundle = new CampaignExportBundle { ExportedAt = DateTime.UtcNow };

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Name, Description, Port, TemplateId, RulesVersion, CombatSettingsJson FROM Campaigns WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", campaignId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return;
                bundle.CampaignName = r.GetString(0);
                bundle.Description = r.IsDBNull(1) ? "" : r.GetString(1);
                bundle.Port = r.IsDBNull(2) ? "5555" : r.GetString(2);
                bundle.TemplateId = r.IsDBNull(3) ? "" : r.GetString(3);
                bundle.RulesVersion = r.IsDBNull(4) ? "both" : r.GetString(4);
                bundle.CombatSettingsJson = r.IsDBNull(5) ? null : r.GetString(5);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Name, SystemId, Version, JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1;";
                cmd.Parameters.AddWithValue("$tid", bundle.TemplateId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    bundle.TemplateName = r.IsDBNull(0) ? "" : r.GetString(0);
                    bundle.TemplateSystemId = r.IsDBNull(1) ? "" : r.GetString(1);
                    bundle.TemplateVersion = r.IsDBNull(2) ? 1 : r.GetInt32(2);
                    bundle.TemplateJsonContent = r.IsDBNull(3) ? "" : r.GetString(3);
                }
            }

            bundle.Characters = await LoadCampaignCharactersAsync(conn, campaignId);
            bundle.Instances = await LoadCampaignInstancesAsync(conn, campaignId);

            var itemIds = DistinctIds(bundle.Instances.Select(i => (string?)i.BaseItemId),
                await JunctionCustomIdsAsync(conn, "CampaignItems", "ItemId", "Items", campaignId));
            var spellIds = DistinctIds(bundle.Characters.SelectMany(c => PreparedSpellIdsFromState(c.StateJson)).Select(s => (string?)s),
                await JunctionCustomIdsAsync(conn, "CampaignSpells", "SpellId", "Spells", campaignId));
            var raceIds = DistinctIds(bundle.Characters.Select(c => c.RaceId),
                await JunctionCustomIdsAsync(conn, "CampaignRaces", "RaceId", "Races", campaignId));
            var classIds = DistinctIds(bundle.Characters.Select(c => c.ClassId),
                await JunctionCustomIdsAsync(conn, "CampaignClasses", "ClassId", "Classes", campaignId));
            var subraceIds = DistinctIds(bundle.Characters.Select(c => c.SubraceId));
            var traitIds = await JunctionCustomIdsAsync(conn, "CampaignTraits", "TraitId", "Traits", campaignId);

            bundle.CustomItems = await LoadCustomItemsAsync(conn, itemIds);
            bundle.CustomSpells = await LoadCustomSpellsAsync(conn, spellIds);
            bundle.CustomRaces = await LoadCustomRacesAsync(conn, raceIds);
            bundle.CustomSubraces = await LoadCustomSubracesAsync(conn, subraceIds);
            bundle.CustomClasses = await LoadCustomClassesAsync(conn, classIds);
            bundle.CustomTraits = await LoadCustomTraitsAsync(conn, traitIds);
            bundle.Currencies = await LoadCurrenciesForTemplateAsync(conn, bundle.TemplateId);

            bundle.Maps = await ExportMapsAsync(conn, campaignId);
            bundle.Handouts = await ExportHandoutsAsync(conn, campaignId);
            bundle.NotePages = await ExportNotePagesAsync(conn, campaignId);

            var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        private static string? FindAssetFile(string campaignId, string sub, string entityId)
        {
            var dir = Path.Combine(GlobalVariables.AppDataLocal, "assets", campaignId, sub);
            if (!Directory.Exists(dir)) return null;
            return Directory.EnumerateFiles(dir, entityId + ".*").FirstOrDefault();
        }

        private static async Task<(string? Base64, string Extension)> ReadAssetAsync(string? directPath, string campaignId, string sub, string entityId)
        {
            var file = !string.IsNullOrEmpty(directPath) && directPath != "none" && File.Exists(directPath)
                ? directPath
                : FindAssetFile(campaignId, sub, entityId);
            if (file == null || !File.Exists(file)) return (null, "");
            try
            {
                return (Convert.ToBase64String(await File.ReadAllBytesAsync(file)), Path.GetExtension(file));
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Export] could not read asset {file}", ex);
                return (null, "");
            }
        }

        private static async Task<List<ExportedMap>> ExportMapsAsync(SqliteConnection conn, string campaignId)
        {
            var maps = new List<ExportedMap>();
            var mapPaths = new Dictionary<string, string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Name, Width, Height, Scale, GridKind, MapPath, PlayerVisible, WallsEnabled, WallsJson, DifficultTerrainJson, MapObjectsJson FROM Maps WHERE CampaignId = $cid;";
                cmd.Parameters.AddWithValue("$cid", campaignId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var m = new ExportedMap
                    {
                        Id = r.GetString(0),
                        Name = r.GetString(1),
                        Width = r.GetInt32(2),
                        Height = r.GetInt32(3),
                        Scale = r.GetDouble(4),
                        GridKind = r.IsDBNull(5) ? "Squares" : r.GetString(5),
                        PlayerVisible = !r.IsDBNull(7) && r.GetInt32(7) != 0,
                        WallsEnabled = !r.IsDBNull(8) && r.GetInt32(8) != 0,
                        WallsJson = r.IsDBNull(9) ? "[]" : r.GetString(9),
                        DifficultTerrainJson = r.IsDBNull(10) ? "[]" : r.GetString(10),
                        MapObjectsJson = r.IsDBNull(11) ? "[]" : r.GetString(11)
                    };
                    mapPaths[m.Id] = r.IsDBNull(6) ? "" : r.GetString(6);
                    maps.Add(m);
                }
            }

            foreach (var m in maps)
            {
                (m.ImageBase64, m.ImageExtension) = await ReadAssetAsync(mapPaths.GetValueOrDefault(m.Id), campaignId, "maps", m.Id);

                await using (var fog = conn.CreateCommand())
                {
                    fog.CommandText = "SELECT Enabled, Cols, Rows, HiddenCells FROM MapFog WHERE MapId = $mid LIMIT 1;";
                    fog.Parameters.AddWithValue("$mid", m.Id);
                    await using var fr = await fog.ExecuteReaderAsync();
                    if (await fr.ReadAsync())
                    {
                        m.FogEnabled = !fr.IsDBNull(0) && fr.GetInt32(0) != 0;
                        m.FogCols = fr.IsDBNull(1) ? 0 : fr.GetInt32(1);
                        m.FogRows = fr.IsDBNull(2) ? 0 : fr.GetInt32(2);
                        m.FogHiddenCells = fr.IsDBNull(3) ? "" : fr.GetString(3);
                    }
                }

                await using (var tok = conn.CreateCommand())
                {
                    tok.CommandText = "SELECT Id, OwnerCharacterId, X, Y, TokenImagePath, Label, Scale, Rotation, SizeName, IsProp, Blocks, BlocksSight FROM MapTokens WHERE MapId = $mid;";
                    tok.Parameters.AddWithValue("$mid", m.Id);
                    await using var tr = await tok.ExecuteReaderAsync();
                    while (await tr.ReadAsync())
                    {
                        var t = new ExportedMapToken
                        {
                            Id = tr.GetString(0),
                            OwnerCharacterId = tr.IsDBNull(1) ? null : tr.GetString(1),
                            X = tr.GetInt32(2),
                            Y = tr.GetInt32(3),
                            Label = tr.IsDBNull(5) ? null : tr.GetString(5),
                            Scale = tr.IsDBNull(6) ? 1.0 : tr.GetDouble(6),
                            Rotation = tr.IsDBNull(7) ? 0.0 : tr.GetDouble(7),
                            SizeName = tr.IsDBNull(8) ? "Medium" : tr.GetString(8),
                            IsProp = !tr.IsDBNull(9) && tr.GetInt64(9) == 1,
                            Blocks = tr.IsDBNull(10) || tr.GetInt64(10) == 1,
                            BlocksSight = !tr.IsDBNull(11) && tr.GetInt64(11) == 1
                        };
                        var imgPath = tr.IsDBNull(4) ? "" : tr.GetString(4);
                        (t.ImageBase64, t.ImageExtension) = await ReadAssetAsync(imgPath, campaignId, "tokens", t.Id);
                        m.Tokens.Add(t);
                    }
                }

                await using (var dr = conn.CreateCommand())
                {
                    dr.CommandText = "SELECT StrokeDataJson FROM MapDrawings WHERE MapId = $mid;";
                    dr.Parameters.AddWithValue("$mid", m.Id);
                    await using var rr = await dr.ExecuteReaderAsync();
                    while (await rr.ReadAsync())
                        if (!rr.IsDBNull(0)) m.Drawings.Add(rr.GetString(0));
                }
            }
            return maps;
        }

        private static async Task<List<ExportedHandout>> ExportHandoutsAsync(SqliteConnection conn, string campaignId)
        {
            var list = new List<ExportedHandout>();
            var paths = new Dictionary<string, string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Name, HandoutPath FROM Handouts WHERE CampaignId = $cid;";
                cmd.Parameters.AddWithValue("$cid", campaignId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var h = new ExportedHandout { Id = r.GetString(0), Name = r.GetString(1) };
                    paths[h.Id] = r.IsDBNull(2) ? "" : r.GetString(2);
                    list.Add(h);
                }
            }
            foreach (var h in list)
            {
                var (b64, ext) = await ReadAssetAsync(paths.GetValueOrDefault(h.Id), campaignId, "handouts", h.Id);
                h.Base64 = b64;
                h.Extension = ext;
            }
            return list;
        }

        private static async Task<List<ExportedNotePage>> ExportNotePagesAsync(SqliteConnection conn, string campaignId)
        {
            var list = new List<ExportedNotePage>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, ParentPageId, Scope, Title, Slug, Icon, ContentMarkdown, SortOrder, PinnedToDashboard FROM NotePages WHERE CampaignId = $cid;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ExportedNotePage
                {
                    Id = r.GetString(0),
                    ParentPageId = r.IsDBNull(1) ? null : r.GetString(1),
                    Scope = r.IsDBNull(2) ? "campaign" : r.GetString(2),
                    Title = r.IsDBNull(3) ? "" : r.GetString(3),
                    Slug = r.IsDBNull(4) ? null : r.GetString(4),
                    Icon = r.IsDBNull(5) ? null : r.GetString(5),
                    ContentMarkdown = r.IsDBNull(6) ? "" : r.GetString(6),
                    SortOrder = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                    PinnedToDashboard = !r.IsDBNull(8) && r.GetInt32(8) != 0
                });
            return list;
        }

        public async Task<(string? NewCampaignId, List<string> Warnings)> ImportCampaignFromFileAsync(string path)
        {
            var warnings = new List<string>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (null, warnings);
            var user = GetCurrentUser();
            if (user == null || string.IsNullOrEmpty(user.Id)) return (null, warnings);

            CampaignExportBundle? bundle;
            try { bundle = JsonSerializer.Deserialize<CampaignExportBundle>(await File.ReadAllTextAsync(path)); }
            catch (JsonException) { return (null, warnings); }
            if (bundle == null || string.IsNullOrEmpty(bundle.TemplateId)) return (null, warnings);

            await using var conn = await _dbManager.OpenAsync();
            var now = DateTime.UtcNow.ToString("o");

            if (!await TemplateExistsAsync(conn, bundle.TemplateId))
            {
                if (string.IsNullOrEmpty(bundle.TemplateJsonContent)) return (null, warnings);
                await InsertTemplateIfAbsentAsync(conn, bundle);
                warnings.Add("the ruleset was new to this machine, srd content stays thin until you re-import that template");
            }

            var newCampaignId = Guid.NewGuid().ToString("N");
            // One transaction with foreign keys deferred to commit, so a throw rewinds the whole import on the connection close and a child instance ahead of its parent still commits.
            await using (var begin = conn.CreateCommand()) { begin.CommandText = "BEGIN; PRAGMA defer_foreign_keys = ON;"; await begin.ExecuteNonQueryAsync(); }
            await EnsureUserRowAsync(conn, user, now);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Campaigns (Id, UserId, Name, TemplateId, Description, CreatedAt, LastModified, Port, RulesVersion, CombatSettingsJson)
                    VALUES ($id, $uid, $name, $tid, $desc, $created, $modified, $port, $rules, $combat);";
                cmd.Parameters.AddWithValue("$id", newCampaignId);
                cmd.Parameters.AddWithValue("$uid", user.Id);
                cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(bundle.CampaignName) ? "Imported campaign" : bundle.CampaignName + " (imported)");
                cmd.Parameters.AddWithValue("$tid", bundle.TemplateId);
                cmd.Parameters.AddWithValue("$desc", bundle.Description ?? "");
                cmd.Parameters.AddWithValue("$created", now);
                cmd.Parameters.AddWithValue("$modified", now);
                cmd.Parameters.AddWithValue("$port", string.IsNullOrEmpty(bundle.Port) ? "5555" : bundle.Port);
                cmd.Parameters.AddWithValue("$rules", string.IsNullOrEmpty(bundle.RulesVersion) ? "both" : bundle.RulesVersion);
                cmd.Parameters.AddWithValue("$combat", (object?)bundle.CombatSettingsJson ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO CampaignMembers (CampaignId, UserId, Role, JoinedAt) VALUES ($cid, $uid, 'dm', $now);";
                cmd.Parameters.AddWithValue("$cid", newCampaignId);
                cmd.Parameters.AddWithValue("$uid", user.Id);
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO ChatChannels (Id, CampaignId, Name, Description, CreatedAt) VALUES ($id, $cid, 'general', 'Main channel', $now);";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("$cid", newCampaignId);
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var (junction, source, idCol) in new[]
            {
                ("CampaignItems", "Items", "ItemId"),
                ("CampaignSpells", "Spells", "SpellId"),
                ("CampaignRaces", "Races", "RaceId"),
                ("CampaignClasses", "Classes", "ClassId"),
                ("CampaignTraits", "Traits", "TraitId"),
            })
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"INSERT OR IGNORE INTO {junction} (CampaignId, {idCol}, AddedAt, IsEnabled) SELECT $cid, Id, $now, 1 FROM {source} WHERE TemplateId = $tid AND Source = 'srd';";
                cmd.Parameters.AddWithValue("$cid", newCampaignId);
                cmd.Parameters.AddWithValue("$tid", bundle.TemplateId);
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var it in bundle.CustomItems)
            {
                await InsertItemIfAbsentAsync(conn, it);
                await AddCampaignLinkAsync(conn, "CampaignItems", "ItemId", newCampaignId, it.Id, now);
            }
            foreach (var sp in bundle.CustomSpells)
            {
                await InsertSpellIfAbsentAsync(conn, sp);
                await AddCampaignLinkAsync(conn, "CampaignSpells", "SpellId", newCampaignId, sp.Id, now);
            }
            foreach (var ra in bundle.CustomRaces)
            {
                await InsertRaceIfAbsentAsync(conn, ra);
                await AddCampaignLinkAsync(conn, "CampaignRaces", "RaceId", newCampaignId, ra.Id, now);
            }
            foreach (var su in bundle.CustomSubraces)
                await InsertSubraceIfAbsentAsync(conn, su);
            foreach (var cl in bundle.CustomClasses)
            {
                await InsertClassIfAbsentAsync(conn, cl);
                await AddCampaignLinkAsync(conn, "CampaignClasses", "ClassId", newCampaignId, cl.Id, now);
            }
            foreach (var tr in bundle.CustomTraits)
            {
                await InsertTraitIfAbsentAsync(conn, tr);
                await AddCampaignLinkAsync(conn, "CampaignTraits", "TraitId", newCampaignId, tr.Id, now);
            }
            foreach (var cur in bundle.Currencies)
                await InsertCurrencyIfAbsentAsync(conn, cur);

            var charIdMap = new Dictionary<string, string>();
            foreach (var ch in bundle.Characters)
                if (!string.IsNullOrEmpty(ch.Id) && !charIdMap.ContainsKey(ch.Id))
                    charIdMap[ch.Id] = Guid.NewGuid().ToString();
            var instIdMap = new Dictionary<string, string>();
            foreach (var inst in bundle.Instances)
                if (!string.IsNullOrEmpty(inst.Id) && !instIdMap.ContainsKey(inst.Id))
                    instIdMap[inst.Id] = Guid.NewGuid().ToString("N");

            var missingCharRefs = 0;
            foreach (var ch in bundle.Characters)
            {
                if (string.IsNullOrEmpty(ch.Id) || !charIdMap.TryGetValue(ch.Id, out var newCharId)) continue;
                var rid = await RowExistsAsync(conn, "Races", ch.RaceId) ? ch.RaceId : null;
                var srid = await RowExistsAsync(conn, "Subraces", ch.SubraceId) ? ch.SubraceId : null;
                var clid = await RowExistsAsync(conn, "Classes", ch.ClassId) ? ch.ClassId : null;
                if ((ch.RaceId != null && rid == null) || (ch.SubraceId != null && srid == null) || (ch.ClassId != null && clid == null)) missingCharRefs++;

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Characters
                        (Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                         Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson, StateJson,
                         CharacterKind, Slug, Tags, VisibleToAll, CreatedAt, ClassLevelsJson)
                    VALUES
                        ($id, $cid, NULL, $name, $rid, $srid, $clid,
                         $lvl, $chp, $mhp, $abil, $inv, $state,
                         $kind, NULL, $tags, $vis, $created, $cls);";
                cmd.Parameters.AddWithValue("$id", newCharId);
                cmd.Parameters.AddWithValue("$cid", newCampaignId);
                cmd.Parameters.AddWithValue("$cls", (object?)ch.ClassLevelsJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(ch.Name) ? "Unnamed" : ch.Name);
                cmd.Parameters.AddWithValue("$rid", (object?)rid ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$srid", (object?)srid ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$clid", (object?)clid ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$lvl", ch.Level);
                cmd.Parameters.AddWithValue("$chp", ch.CurrentHp);
                cmd.Parameters.AddWithValue("$mhp", ch.MaxHp);
                cmd.Parameters.AddWithValue("$abil", string.IsNullOrWhiteSpace(ch.AbilityScoresJson) ? "{}" : ch.AbilityScoresJson);
                cmd.Parameters.AddWithValue("$inv", RemapInventoryJson(ch.InventoryJson, instIdMap));
                cmd.Parameters.AddWithValue("$state", (object?)ch.StateJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$kind", string.IsNullOrEmpty(ch.CharacterKind) ? "pc" : ch.CharacterKind);
                cmd.Parameters.AddWithValue("$tags", string.IsNullOrEmpty(ch.Tags) ? "[]" : ch.Tags);
                cmd.Parameters.AddWithValue("$vis", ch.VisibleToAll ? 1 : 0);
                cmd.Parameters.AddWithValue("$created", now);
                await cmd.ExecuteNonQueryAsync();
            }
            if (missingCharRefs > 0) warnings.Add(missingCharRefs == 1 ? "1 character had a race, subrace or class that didn't travel" : missingCharRefs + " characters had a race, subrace or class that didn't travel");

            var droppedItems = 0;
            foreach (var inst in bundle.Instances)
            {
                if (string.IsNullOrEmpty(inst.Id) || !instIdMap.TryGetValue(inst.Id, out var newInstId)) continue;
                if (!await RowExistsAsync(conn, "Items", inst.BaseItemId)) { droppedItems++; continue; }
                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO ItemInstances
                            (Id, CampaignId, BaseItemId, OwnerCharacterId, Quantity, CustomName, ParentInstanceId, StateJson)
                        VALUES ($id, $cid, $base, $owner, $qty, $cname, $parent, $state);";
                    cmd.Parameters.AddWithValue("$id", newInstId);
                    cmd.Parameters.AddWithValue("$cid", newCampaignId);
                    cmd.Parameters.AddWithValue("$base", inst.BaseItemId);
                    var owner = inst.OwnerCharacterId != null && charIdMap.TryGetValue(inst.OwnerCharacterId, out var no) ? no : null;
                    cmd.Parameters.AddWithValue("$owner", (object?)owner ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$qty", inst.Quantity);
                    cmd.Parameters.AddWithValue("$cname", (object?)inst.CustomName ?? DBNull.Value);
                    var parent = inst.ParentInstanceId != null && instIdMap.TryGetValue(inst.ParentInstanceId, out var np) ? np : null;
                    cmd.Parameters.AddWithValue("$parent", (object?)parent ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$state", (object?)inst.StateJson ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex) { droppedItems++; ErrorLog.Log($"[Import] skipped instance {inst.Id}", ex); }
            }
            if (droppedItems > 0) warnings.Add(droppedItems == 1 ? "1 inventory item" : droppedItems + " inventory items");

            await ImportMapsAsync(conn, bundle, newCampaignId, charIdMap, warnings, now);
            await ImportHandoutsAsync(conn, bundle, newCampaignId, warnings, now);
            await ImportNotePagesAsync(conn, bundle, newCampaignId, user.Id, now);

            await using (var commit = conn.CreateCommand()) { commit.CommandText = "COMMIT;"; await commit.ExecuteNonQueryAsync(); }
            return (newCampaignId, warnings);
        }

        private static string WriteAssetOrNone(string? base64, string extension, string campaignId, string sub, string entityId, List<string> warnings)
        {
            if (string.IsNullOrEmpty(base64)) return "none";
            try
            {
                var dir = Path.Combine(GlobalVariables.AppDataLocal, "assets", campaignId, sub);
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, entityId + (string.IsNullOrEmpty(extension) ? ".png" : extension));
                File.WriteAllBytes(file, Convert.FromBase64String(base64));
                return file;
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Import] asset write failed for {sub}/{entityId}", ex);
                warnings.Add("an image in " + sub + " did not survive the trip");
                return "none";
            }
        }

        private static async Task ImportMapsAsync(SqliteConnection conn, CampaignExportBundle bundle, string newCampaignId, Dictionary<string, string> charIdMap, List<string> warnings, string now)
        {
            foreach (var m in bundle.Maps)
            {
                if (string.IsNullOrEmpty(m.Id)) continue;
                var newMapId = Guid.NewGuid().ToString("N");
                var mapPath = WriteAssetOrNone(m.ImageBase64, m.ImageExtension ?? "", newCampaignId, "maps", newMapId, warnings);

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO Maps (Id, CampaignId, Name, Width, Height, Scale, GridKind, MapPath, PlayerVisible, WallsEnabled, WallsJson, DifficultTerrainJson, MapObjectsJson, CreatedAt)
                        VALUES ($id, $cid, $name, $w, $h, $scale, $grid, $path, $vis, $we, $walls, $terrain, $objects, $now);";
                    cmd.Parameters.AddWithValue("$id", newMapId);
                    cmd.Parameters.AddWithValue("$cid", newCampaignId);
                    cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(m.Name) ? "Imported map" : m.Name);
                    cmd.Parameters.AddWithValue("$w", m.Width);
                    cmd.Parameters.AddWithValue("$h", m.Height);
                    cmd.Parameters.AddWithValue("$scale", m.Scale);
                    cmd.Parameters.AddWithValue("$grid", string.IsNullOrEmpty(m.GridKind) ? "Squares" : m.GridKind);
                    cmd.Parameters.AddWithValue("$path", mapPath);
                    cmd.Parameters.AddWithValue("$vis", m.PlayerVisible ? 1 : 0);
                    cmd.Parameters.AddWithValue("$we", m.WallsEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("$walls", string.IsNullOrEmpty(m.WallsJson) ? "[]" : m.WallsJson);
                    cmd.Parameters.AddWithValue("$terrain", string.IsNullOrEmpty(m.DifficultTerrainJson) ? "[]" : m.DifficultTerrainJson);
                    cmd.Parameters.AddWithValue("$objects", string.IsNullOrEmpty(m.MapObjectsJson) ? "[]" : m.MapObjectsJson);
                    cmd.Parameters.AddWithValue("$now", now);
                    await cmd.ExecuteNonQueryAsync();
                }

                if (m.FogEnabled || m.FogCols > 0 || !string.IsNullOrEmpty(m.FogHiddenCells))
                {
                    await using var fog = conn.CreateCommand();
                    fog.CommandText = "INSERT INTO MapFog (MapId, CampaignId, Enabled, Cols, Rows, HiddenCells, UpdatedAt) VALUES ($mid, $cid, $en, $cols, $rows, $cells, $now);";
                    fog.Parameters.AddWithValue("$mid", newMapId);
                    fog.Parameters.AddWithValue("$cid", newCampaignId);
                    fog.Parameters.AddWithValue("$en", m.FogEnabled ? 1 : 0);
                    fog.Parameters.AddWithValue("$cols", m.FogCols);
                    fog.Parameters.AddWithValue("$rows", m.FogRows);
                    fog.Parameters.AddWithValue("$cells", m.FogHiddenCells ?? "");
                    fog.Parameters.AddWithValue("$now", now);
                    await fog.ExecuteNonQueryAsync();
                }

                foreach (var t in m.Tokens)
                {
                    var newTokenId = Guid.NewGuid().ToString("N");
                    var tokenPath = WriteAssetOrNone(t.ImageBase64, t.ImageExtension ?? "", newCampaignId, "tokens", newTokenId, warnings);
                    await using var tok = conn.CreateCommand();
                    tok.CommandText = @"
                        INSERT INTO MapTokens (Id, MapId, CampaignId, OwnerCharacterId, X, Y, TokenImagePath, Label, Scale, Rotation, SizeName, IsProp, Blocks, BlocksSight)
                        VALUES ($id, $mid, $cid, $owner, $x, $y, $path, $label, $scale, $rot, $size, $prop, $blocks, $bsight);";
                    tok.Parameters.AddWithValue("$id", newTokenId);
                    tok.Parameters.AddWithValue("$mid", newMapId);
                    tok.Parameters.AddWithValue("$cid", newCampaignId);
                    var owner = t.OwnerCharacterId != null && charIdMap.TryGetValue(t.OwnerCharacterId, out var no) ? no : null;
                    tok.Parameters.AddWithValue("$owner", (object?)owner ?? DBNull.Value);
                    tok.Parameters.AddWithValue("$x", t.X);
                    tok.Parameters.AddWithValue("$y", t.Y);
                    tok.Parameters.AddWithValue("$path", tokenPath);
                    tok.Parameters.AddWithValue("$label", (object?)t.Label ?? DBNull.Value);
                    tok.Parameters.AddWithValue("$scale", t.Scale);
                    tok.Parameters.AddWithValue("$rot", t.Rotation);
                    tok.Parameters.AddWithValue("$size", string.IsNullOrEmpty(t.SizeName) ? "Medium" : t.SizeName);
                    tok.Parameters.AddWithValue("$prop", t.IsProp ? 1 : 0);
                    tok.Parameters.AddWithValue("$blocks", t.Blocks ? 1 : 0);
                    tok.Parameters.AddWithValue("$bsight", t.BlocksSight ? 1 : 0);
                    await tok.ExecuteNonQueryAsync();
                }

                foreach (var stroke in m.Drawings)
                {
                    if (string.IsNullOrEmpty(stroke)) continue;
                    await using var dr = conn.CreateCommand();
                    dr.CommandText = "INSERT INTO MapDrawings (Id, MapId, UserId, StrokeDataJson, Timestamp) VALUES ($id, $mid, NULL, $data, $now);";
                    dr.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    dr.Parameters.AddWithValue("$mid", newMapId);
                    dr.Parameters.AddWithValue("$data", stroke);
                    dr.Parameters.AddWithValue("$now", now);
                    await dr.ExecuteNonQueryAsync();
                }
            }
        }

        private static async Task ImportHandoutsAsync(SqliteConnection conn, CampaignExportBundle bundle, string newCampaignId, List<string> warnings, string now)
        {
            foreach (var h in bundle.Handouts)
            {
                if (string.IsNullOrEmpty(h.Id)) continue;
                var newId = Guid.NewGuid().ToString("N");
                var path = WriteAssetOrNone(h.Base64, h.Extension, newCampaignId, "handouts", newId, warnings);
                if (path == "none") continue; // A handout is only its file, a row without one is just a dead name in the list.
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO Handouts (Id, CampaignId, Name, HandoutPath, CreatedAt) VALUES ($id, $cid, $name, $path, $now);";
                cmd.Parameters.AddWithValue("$id", newId);
                cmd.Parameters.AddWithValue("$cid", newCampaignId);
                cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(h.Name) ? "Handout" : h.Name);
                cmd.Parameters.AddWithValue("$path", path);
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task ImportNotePagesAsync(SqliteConnection conn, CampaignExportBundle bundle, string newCampaignId, string ownerUserId, string now)
        {
            var pageIdMap = new Dictionary<string, string>();
            foreach (var p in bundle.NotePages)
                if (!string.IsNullOrEmpty(p.Id) && !pageIdMap.ContainsKey(p.Id))
                    pageIdMap[p.Id] = Guid.NewGuid().ToString("N");

            foreach (var p in bundle.NotePages)
            {
                if (string.IsNullOrEmpty(p.Id) || !pageIdMap.TryGetValue(p.Id, out var newId)) continue;
                var parent = p.ParentPageId != null && pageIdMap.TryGetValue(p.ParentPageId, out var np) ? np : null;
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO NotePages (Id, CampaignId, OwnerUserId, ParentPageId, Scope, Title, Slug, Icon, ContentMarkdown, SortOrder, PinnedToDashboard, RevisionNumber, CreatedAt, UpdatedAt)
                    VALUES ($id, $cid, $owner, $parent, $scope, $title, $slug, $icon, $content, $sort, $pin, 1, $now, $now);";
                cmd.Parameters.AddWithValue("$id", newId);
                cmd.Parameters.AddWithValue("$cid", newCampaignId);
                cmd.Parameters.AddWithValue("$owner", ownerUserId);
                cmd.Parameters.AddWithValue("$parent", (object?)parent ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$scope", string.IsNullOrEmpty(p.Scope) ? "campaign" : p.Scope);
                cmd.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(p.Title) ? "Untitled" : p.Title);
                cmd.Parameters.AddWithValue("$slug", (object?)p.Slug ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$icon", (object?)p.Icon ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$content", p.ContentMarkdown ?? "");
                cmd.Parameters.AddWithValue("$sort", p.SortOrder);
                cmd.Parameters.AddWithValue("$pin", p.PinnedToDashboard ? 1 : 0);
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static List<string> DistinctIds(params IEnumerable<string?>[] sources)
            => sources.SelectMany(s => s).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().ToList();

        private static async Task<List<string>> JunctionCustomIdsAsync(SqliteConnection conn, string junction, string idCol, string catalog, string campaignId)
        {
            var ids = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT j.{idCol} FROM {junction} j JOIN {catalog} c ON c.Id = j.{idCol} WHERE j.CampaignId = $cid AND c.Source = 'custom';";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (!r.IsDBNull(0)) ids.Add(r.GetString(0));
            return ids;
        }

        private static async Task<List<Character>> LoadCampaignCharactersAsync(SqliteConnection conn, string campaignId)
        {
            var list = new List<Character>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId, Level, CurrentHp, MaxHp,
                       AbilityScoresJson, InventoryJson, StateJson, CharacterKind, Slug, Tags, VisibleToAll, CreatedAt, ClassLevelsJson
                FROM Characters WHERE CampaignId = $cid;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Character
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
                    VisibleToAll = !r.IsDBNull(16) && r.GetInt32(16) != 0,
                    CreatedAt = DateTime.Parse(r.GetString(17)),
                    ClassLevelsJson = r.IsDBNull(18) ? null : r.GetString(18)
                });
            return list;
        }

        private static async Task<List<ItemInstance>> LoadCampaignInstancesAsync(SqliteConnection conn, string campaignId)
        {
            var list = new List<ItemInstance>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, BaseItemId, OwnerCharacterId, Quantity, CustomName, ParentInstanceId, StateJson
                FROM ItemInstances WHERE CampaignId = $cid;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ItemInstance
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    BaseItemId = r.GetString(2),
                    OwnerCharacterId = r.IsDBNull(3) ? null : r.GetString(3),
                    Quantity = r.GetInt32(4),
                    CustomName = r.IsDBNull(5) ? null : r.GetString(5),
                    ParentInstanceId = r.IsDBNull(6) ? null : r.GetString(6),
                    StateJson = r.IsDBNull(7) ? null : r.GetString(7)
                });
            return list;
        }

        private static async Task<List<Trait>> LoadCustomTraitsAsync(SqliteConnection conn, IReadOnlyList<string> ids)
        {
            var list = new List<Trait>();
            if (ids.Count == 0) return list;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT Id, Name, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt
                FROM Traits WHERE Source = 'custom' AND Id IN ({InClause("tr", ids, cmd)});";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Trait
                {
                    Id = r.GetString(0),
                    Name = r.GetString(1),
                    Description = r.IsDBNull(2) ? "" : r.GetString(2),
                    Source = r.GetString(3),
                    OwnerUserId = r.IsDBNull(4) ? null : r.GetString(4),
                    TemplateId = r.IsDBNull(5) ? null : r.GetString(5),
                    RevisionNumber = r.GetInt32(6),
                    UpdatedAt = r.IsDBNull(7) ? "" : r.GetString(7)
                });
            return list;
        }

        private static async Task<List<Currency>> LoadCurrenciesForTemplateAsync(SqliteConnection conn, string templateId)
        {
            var list = new List<Currency>();
            if (string.IsNullOrEmpty(templateId)) return list;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, TemplateId, Name, Abbreviation, IsBase, EqualToBase, Color, IconSvg, SortOrder
                FROM Currencies WHERE TemplateId = $tid ORDER BY SortOrder;";
            cmd.Parameters.AddWithValue("$tid", templateId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new Currency
                {
                    Id = r.GetString(0),
                    TemplateId = r.IsDBNull(1) ? null : r.GetString(1),
                    Name = r.GetString(2),
                    Abbreviation = r.IsDBNull(3) ? "" : r.GetString(3),
                    IsBase = !r.IsDBNull(4) && r.GetInt32(4) != 0,
                    EqualToBase = r.IsDBNull(5) ? 1 : r.GetInt32(5),
                    Color = r.IsDBNull(6) ? null : r.GetString(6),
                    IconSvg = r.IsDBNull(7) ? null : r.GetString(7),
                    SortOrder = r.IsDBNull(8) ? 0 : r.GetInt32(8)
                });
            return list;
        }

        private static async Task<bool> TemplateExistsAsync(SqliteConnection conn, string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return false;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1;";
            cmd.Parameters.AddWithValue("$tid", templateId);
            return await cmd.ExecuteScalarAsync() != null;
        }

        private static async Task InsertTemplateIfAbsentAsync(SqliteConnection conn, CampaignExportBundle bundle)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO CampaignTemplates (TemplateId, Name, Description, SystemId, Version, ImportedAt, JsonContent)
                VALUES ($tid, $name, NULL, $sys, $ver, $imp, $json);";
            cmd.Parameters.AddWithValue("$tid", bundle.TemplateId);
            cmd.Parameters.AddWithValue("$name", string.IsNullOrEmpty(bundle.TemplateName) ? bundle.TemplateId : bundle.TemplateName);
            cmd.Parameters.AddWithValue("$sys", string.IsNullOrEmpty(bundle.TemplateSystemId) ? "dnd5e" : bundle.TemplateSystemId);
            cmd.Parameters.AddWithValue("$ver", bundle.TemplateVersion);
            cmd.Parameters.AddWithValue("$imp", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$json", bundle.TemplateJsonContent);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task EnsureUserRowAsync(SqliteConnection conn, User user, string now)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO Users (Id, Username, CreatedAt) VALUES ($id, $name, $now);";
            cmd.Parameters.AddWithValue("$id", user.Id);
            cmd.Parameters.AddWithValue("$name", string.IsNullOrEmpty(user.Username) ? user.Id : user.Username);
            cmd.Parameters.AddWithValue("$now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertTraitIfAbsentAsync(SqliteConnection conn, Trait tr)
        {
            if (string.IsNullOrEmpty(tr.Id)) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Traits
                    (Id, Name, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt)
                VALUES ($Id, $Name, $Desc, 'custom', NULL, NULL, $Rev, $Upd);";
            cmd.Parameters.AddWithValue("$Id", tr.Id);
            cmd.Parameters.AddWithValue("$Name", tr.Name);
            cmd.Parameters.AddWithValue("$Desc", tr.Description ?? "");
            cmd.Parameters.AddWithValue("$Rev", tr.RevisionNumber);
            cmd.Parameters.AddWithValue("$Upd", string.IsNullOrEmpty(tr.UpdatedAt) ? DateTime.UtcNow.ToString("o") : tr.UpdatedAt);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertCurrencyIfAbsentAsync(SqliteConnection conn, Currency cur)
        {
            if (string.IsNullOrEmpty(cur.Id)) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO Currencies
                    (Id, TemplateId, Name, Abbreviation, IsBase, EqualToBase, Color, IconSvg, SortOrder)
                VALUES ($Id, $Tid, $Name, $Abbr, $Base, $Eq, $Color, $Icon, $Sort);";
            cmd.Parameters.AddWithValue("$Id", cur.Id);
            cmd.Parameters.AddWithValue("$Tid", (object?)cur.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Name", cur.Name);
            cmd.Parameters.AddWithValue("$Abbr", cur.Abbreviation ?? "");
            cmd.Parameters.AddWithValue("$Base", cur.IsBase ? 1 : 0);
            cmd.Parameters.AddWithValue("$Eq", cur.EqualToBase);
            cmd.Parameters.AddWithValue("$Color", (object?)cur.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Icon", (object?)cur.IconSvg ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Sort", cur.SortOrder);
            await cmd.ExecuteNonQueryAsync();
        }

        private static string RemapInventoryJson(string? json, Dictionary<string, string> idMap)
        {
            if (string.IsNullOrWhiteSpace(json)) return "{\"InstanceIds\":[]}";
            try
            {
                var node = JsonNode.Parse(json);
                var arr = node as JsonArray ?? node?["InstanceIds"]?.AsArray();
                if (arr != null)
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var old = arr[i]?.GetValue<string>();
                        if (old != null && idMap.TryGetValue(old, out var nw)) arr[i] = nw;
                    }
                return node?.ToJsonString() ?? "{\"InstanceIds\":[]}";
            }
            catch (Exception) { return "{\"InstanceIds\":[]}"; }
        }

        private int _combatBaseActions = 1;
        private int _combatBaseBonusActions = 1;
        private bool _combatAutoFlanking = true;
        private double _combatMeleeReachCells = 1.5;
        private double _combatFlankAngleDegrees = 120;
        private int _combatBaseSpeedFeet = 30;
        private int _combatBaseReactions = 1;
        private double _combatDashMultiplier = 2.0;
        private string _combatFriendRingColor = "#4C8DFF";
        private string _combatEnemyRingColor = "#D9534F";
        private string _combatMoveHighlightColor = "#FFD700";
        private bool _combatSettingsLoaded;

        public bool CombatAutoFlanking => _combatAutoFlanking;
        public double CombatMeleeReachCells => _combatMeleeReachCells;
        public double CombatFlankAngleDegrees => _combatFlankAngleDegrees;
        public int CombatBaseSpeedFeet => _combatBaseSpeedFeet;
        public int CombatBaseReactions => _combatBaseReactions;
        public double CombatDashMultiplier => _combatDashMultiplier;
        public string CombatFriendRingColor => _combatFriendRingColor;
        public string CombatEnemyRingColor => _combatEnemyRingColor;
        public string CombatMoveHighlightColor => _combatMoveHighlightColor;

        public async Task<(int BaseActions, int BaseBonusActions)> GetCombatSettingsAsync()
        {
            if (_combatSettingsLoaded) return (_combatBaseActions, _combatBaseBonusActions);

            int baseA = 1, baseB = 1, speed = 30, reactions = 1;
            bool autoFlank = true;
            double reach = 1.5, flankAngle = 120, dash = 2.0;
            string friendColor = "#4C8DFF", enemyColor = "#D9534F", moveColor = "#FFD700";

            // Template is the ruleset default, then the campaign row overrides on top, so a homebrew can set either and neither wins by accident.
            var tmpl = await LoadActiveTemplateJsonAsync();
            if (!string.IsNullOrEmpty(tmpl))
            {
                try
                {
                    using var doc = JsonDocument.Parse(tmpl);
                    if (doc.RootElement.TryGetProperty("CombatSettings", out var cs) && cs.ValueKind == JsonValueKind.Object)
                        ReadCombatSettings(cs, ref baseA, ref baseB, ref autoFlank, ref reach, ref flankAngle, ref speed, ref reactions, ref dash, ref friendColor, ref enemyColor, ref moveColor);
                }
                catch (JsonException) { }
            }

            var campaignId = GetCampaignId();
            if (!string.IsNullOrEmpty(campaignId))
            {
                string? settingsJson;
                await using (var conn = await _dbManager.OpenAsync())
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT CombatSettingsJson FROM Campaigns WHERE Id = $id LIMIT 1";
                    cmd.Parameters.AddWithValue("$id", campaignId);
                    settingsJson = await cmd.ExecuteScalarAsync() as string;
                }
                if (!string.IsNullOrWhiteSpace(settingsJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(settingsJson);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            ReadCombatSettings(doc.RootElement, ref baseA, ref baseB, ref autoFlank, ref reach, ref flankAngle, ref speed, ref reactions, ref dash, ref friendColor, ref enemyColor, ref moveColor);
                    }
                    catch (JsonException) { }
                }
            }

            _combatBaseActions = baseA < 0 ? 0 : baseA;
            _combatBaseBonusActions = baseB < 0 ? 0 : baseB;
            _combatAutoFlanking = autoFlank;
            _combatMeleeReachCells = reach > 0 ? reach : 1.5;
            _combatFlankAngleDegrees = flankAngle;
            _combatBaseSpeedFeet = speed < 0 ? 0 : speed;
            _combatBaseReactions = reactions < 0 ? 0 : reactions;
            _combatDashMultiplier = dash > 0 ? dash : 2.0;
            _combatFriendRingColor = friendColor;
            _combatEnemyRingColor = enemyColor;
            _combatMoveHighlightColor = moveColor;
            _combatSettingsLoaded = true;
            return (_combatBaseActions, _combatBaseBonusActions);
        }

        private static void ReadCombatSettings(JsonElement cs, ref int baseA, ref int baseB, ref bool autoFlank, ref double reach, ref double flankAngle, ref int speed, ref int reactions, ref double dash, ref string friendColor, ref string enemyColor, ref string moveColor)
        {
            if (cs.TryGetProperty("DashMultiplier", out var dm) && dm.ValueKind == JsonValueKind.Number && dm.TryGetDouble(out var dmv)) dash = dmv;
            if (cs.TryGetProperty("BaseActions", out var ba) && ba.TryGetInt32(out var bav)) baseA = bav;
            if (cs.TryGetProperty("BaseBonusActions", out var bb) && bb.TryGetInt32(out var bbv)) baseB = bbv;
            if (cs.TryGetProperty("AutoFlanking", out var af) && (af.ValueKind == JsonValueKind.True || af.ValueKind == JsonValueKind.False)) autoFlank = af.GetBoolean();
            if (cs.TryGetProperty("MeleeReachCells", out var mr) && mr.ValueKind == JsonValueKind.Number && mr.TryGetDouble(out var mrv)) reach = mrv;
            if (cs.TryGetProperty("FlankAngleDegrees", out var fa) && fa.ValueKind == JsonValueKind.Number && fa.TryGetDouble(out var fav)) flankAngle = fav;
            if (cs.TryGetProperty("BaseSpeedFeet", out var sp) && sp.ValueKind == JsonValueKind.Number && sp.TryGetInt32(out var spv)) speed = spv;
            if (cs.TryGetProperty("BaseReactions", out var re) && re.ValueKind == JsonValueKind.Number && re.TryGetInt32(out var rev)) reactions = rev;
            if (cs.TryGetProperty("FriendRingColor", out var fc) && fc.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(fc.GetString())) friendColor = fc.GetString()!;
            if (cs.TryGetProperty("EnemyRingColor", out var ec) && ec.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ec.GetString())) enemyColor = ec.GetString()!;
            if (cs.TryGetProperty("MoveHighlightColor", out var mc) && mc.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(mc.GetString())) moveColor = mc.GetString()!;
        }

        private int _longRestHitDiceDivisor = 2;
        private bool _shortRestHitDieAverage = true;
        private bool _restSettingsLoaded;

        public async Task<(int LongRestHitDiceDivisor, bool ShortRestHitDieAverage)> GetRestSettingsAsync()
        {
            if (_restSettingsLoaded) return (_longRestHitDiceDivisor, _shortRestHitDieAverage);
            try
            {
                var tmpl = await LoadActiveTemplateJsonAsync();
                if (!string.IsNullOrEmpty(tmpl))
                {
                    using var doc = JsonDocument.Parse(tmpl);
                    if (doc.RootElement.TryGetProperty("CombatSettings", out var cs) && cs.ValueKind == JsonValueKind.Object)
                    {
                        if (cs.TryGetProperty("LongRestHitDiceDivisor", out var d) && d.TryGetInt32(out var dv) && dv > 0)
                            _longRestHitDiceDivisor = dv;
                        if (cs.TryGetProperty("ShortRestHitDieAverage", out var a) && (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False))
                            _shortRestHitDieAverage = a.GetBoolean();
                    }
                }
            }
            catch (JsonException) { }
            _restSettingsLoaded = true;
            return (_longRestHitDiceDivisor, _shortRestHitDieAverage);
        }

        public GameRules Rules { get; internal set; } = new();
        private bool _rulesLoaded;

        public int AbilityMod(int score) => Rules.Modifier(score);
        public int ProficiencyBonusForLevel(int level) => Rules.ProficiencyBonusForLevel(level);
        public void InvalidateRules() => _rulesLoaded = false;

        public async Task EnsureRulesLoadedAsync()
        {
            if (_rulesLoaded) return;
            var rules = new GameRules();
            try
            {
                var tmpl = await LoadActiveTemplateJsonAsync();
                if (!string.IsNullOrEmpty(tmpl))
                {
                    using var doc = JsonDocument.Parse(tmpl);
                    ReadTemplate(doc.RootElement, rules);
                }
            }
            catch (JsonException) { }
            await ReadDmGatesAsync(rules);
            Rules = rules;
            _rulesLoaded = true;
        }

        private async Task ReadDmGatesAsync(GameRules rules)
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId) || _dbManager == null) return;
            string? settingsJson;
            await using (var conn = await _dbManager.OpenAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT CombatSettingsJson FROM Campaigns WHERE Id = $id LIMIT 1";
                cmd.Parameters.AddWithValue("$id", campaignId);
                settingsJson = await cmd.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrWhiteSpace(settingsJson)) return;
            try
            {
                using var doc = JsonDocument.Parse(settingsJson);
                ApplyCampaignOverrides(doc.RootElement, rules);
            }
            catch (JsonException) { }
        }

        internal static void ApplyCampaignOverrides(JsonElement campaign, GameRules rules)
        {
            if (campaign.ValueKind != JsonValueKind.Object) return;
            ReadCombatRules(campaign, rules);
            if (campaign.TryGetProperty("MulticlassingAllowed", out var ma)
                && (ma.ValueKind == JsonValueKind.True || ma.ValueKind == JsonValueKind.False))
                rules.MulticlassingAllowedByDm = ma.GetBoolean();
            if (campaign.TryGetProperty("DmIgnoresMovementBudget", out var mb)
                && (mb.ValueKind == JsonValueKind.True || mb.ValueKind == JsonValueKind.False))
                rules.DmIgnoresMovementBudget = mb.GetBoolean();

        }

        // Variables have to land before anything that can name one, a condition writing "@rageDamage" resolves it at read time.
        internal static void ReadTemplate(JsonElement root, GameRules rules)
        {
            ReadSkills(root, rules);
            ReadAbilities(root, rules);
            ReadLevelTable(root, rules);
            ReadVariables(root, rules);
            ReadCasterAbilities(root, rules);
            ReadGridItems(root, rules);
            ReadArmorTypes(root, rules);
            ReadWeaponProperties(root, rules);
            ReadConditions(root, rules);
            ReadClassResources(root, rules);
            ReadMasteries(root, rules);
            ReadMulticlassing(root, rules);
            ReadSummons(root, rules);

            if (root.TryGetProperty("CombatSettings", out var cs) && cs.ValueKind == JsonValueKind.Object)
                ReadCombatRules(cs, rules);

            RecordDefaultedSections(root, rules);
        }

        // Absence is indistinguisable from a template that matched the default, so name the sections the engine had to fall back on, the load path pops these once.
        private static void RecordDefaultedSections(JsonElement root, GameRules rules)
        {
            rules.DefaultedSections.Clear();
            void Top(string key, string label)
            {
                if (!root.TryGetProperty(key, out var v) || (v.ValueKind != JsonValueKind.Array && v.ValueKind != JsonValueKind.Object))
                    rules.DefaultedSections.Add(label);
            }
            Top("Abilities", "abilities");
            Top("Level", "the level and proficiency table");

            if (!root.TryGetProperty("CombatSettings", out var cs) || cs.ValueKind != JsonValueKind.Object)
            {
                rules.DefaultedSections.Add("combat settings");
                return;
            }
            void Cs(string key, string label)
            {
                if (!cs.TryGetProperty(key, out _)) rules.DefaultedSections.Add(label);
            }
            Cs("TacticalActions", "tactical actions");
            Cs("AoeShapes", "area shapes");
            Cs("Senses", "senses");
            Cs("HealthStates", "health bands");
        }

        internal static void ReadSummons(JsonElement root, GameRules rules)
        {
            rules.Summons.Clear();
            if (!root.TryGetProperty("Summons", out var sm) || sm.ValueKind != JsonValueKind.Object) return;

            foreach (var p in sm.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                var monsterId = p.Value.TryGetProperty("MonsterId", out var mi) && mi.ValueKind == JsonValueKind.String ? mi.GetString() : null;
                if (string.IsNullOrWhiteSpace(monsterId)) continue;

                var rule = new SummonRule { MonsterId = monsterId! };
                if (p.Value.TryGetProperty("Count", out var c) && c.TryGetInt32(out var cv) && cv > 0) rule.Count = cv;
                if (p.Value.TryGetProperty("Controller", out var ct) && ct.ValueKind == JsonValueKind.String) rule.Controller = ct.GetString()!;
                if (p.Value.TryGetProperty("CountBySlotLevel", out var bs) && bs.ValueKind == JsonValueKind.Object)
                    foreach (var q in bs.EnumerateObject())
                        if (int.TryParse(q.Name, out var lvl) && q.Value.TryGetInt32(out var n) && n > 0) rule.CountBySlotLevel[lvl] = n;

                rules.Summons[p.Name] = rule;
            }
        }

        internal static void ReadMulticlassing(JsonElement root, GameRules rules)
        {
            rules.MulticlassingEnabled = false;
            rules.MulticlassPrerequisites.Clear();
            rules.MulticlassProficiencies.Clear();
            rules.MulticlassCasterContributions.Clear();
            if (!root.TryGetProperty("Multiclassing", out var mc) || mc.ValueKind != JsonValueKind.Object) return;

            if (mc.TryGetProperty("Enabled", out var en) && en.ValueKind == JsonValueKind.False) return;
            rules.MulticlassingEnabled = true;

            if (mc.TryGetProperty("SharedSlotTable", out var st) && st.ValueKind == JsonValueKind.String) rules.MulticlassSharedSlotTable = st.GetString()!;
            if (mc.TryGetProperty("PactSlotTable", out var pt) && pt.ValueKind == JsonValueKind.String) rules.PactSlotTable = pt.GetString()!;

            if (mc.TryGetProperty("CasterContributions", out var cc) && cc.ValueKind == JsonValueKind.Object)
                foreach (var p in cc.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) continue;
                    var c = new CasterContribution();
                    if (p.Value.TryGetProperty("Divisor", out var d) && d.TryGetInt32(out var dv)) c.Divisor = dv;
                    if (p.Value.TryGetProperty("RoundUp", out var ru)) c.RoundUp = ru.ValueKind == JsonValueKind.True;
                    if (p.Value.TryGetProperty("Excluded", out var ex)) c.Excluded = ex.ValueKind == JsonValueKind.True;
                    rules.MulticlassCasterContributions[p.Name] = c;
                }

            if (!mc.TryGetProperty("Classes", out var classes) || classes.ValueKind != JsonValueKind.Object) return;
            foreach (var p in classes.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;

                var needs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (p.Value.TryGetProperty("Prerequisites", out var pre) && pre.ValueKind == JsonValueKind.Object)
                    foreach (var q in pre.EnumerateObject())
                        if (q.Value.TryGetInt32(out var min)) needs[q.Name] = min;
                rules.MulticlassPrerequisites[p.Name] = needs;

                var profs = new List<string>();
                if (p.Value.TryGetProperty("Proficiencies", out var pr) && pr.ValueKind == JsonValueKind.Array)
                    foreach (var q in pr.EnumerateArray())
                        if (q.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(q.GetString())) profs.Add(q.GetString()!);
                rules.MulticlassProficiencies[p.Name] = profs;
            }
        }

        private static void ReadSkills(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("Proficiencies", out var profs) && profs.ValueKind == JsonValueKind.Array)
                foreach (var p in profs.EnumerateArray())
                {
                    if (!p.TryGetProperty("Type", out var pt) || pt.GetString() != "Skill") continue;
                    var pid = p.TryGetProperty("TemplateId", out var ii) ? ii.GetString() : null;
                    var pn = p.TryGetProperty("Name", out var nn) ? nn.GetString() : null;
                    var pab = p.TryGetProperty("Ability", out var aa) ? aa.GetString() : null;
                    if (!string.IsNullOrEmpty(pn) && !string.IsNullOrEmpty(pab)) rules.Skills.Add(new SkillDef(pid ?? "", pn!, pab!));
                }
        }

        private static void ReadAbilities(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("Abilities", out var abils) && abils.ValueKind == JsonValueKind.Array)
            {
                rules.Abilities.Clear();
                foreach (var a in abils.EnumerateArray())
                {
                    var aid = a.TryGetProperty("TemplateId", out var ai) ? ai.GetString() : null;
                    var an = a.TryGetProperty("Name", out var an2) ? an2.GetString() : null;
                    if (string.IsNullOrEmpty(aid) || string.IsNullOrEmpty(an)) continue;
                    var abbr = a.TryGetProperty("Short", out var sh) && sh.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(sh.GetString())
                        ? sh.GetString()!
                        : FallbackAbbrev(aid!);
                    rules.Abilities.Add(new AbilityDef { Id = aid!, Short = abbr, Name = an! });
                    if (abbr.Length > 0) rules.AbilityNames[abbr] = an!;
                }
            }
        }

        private static void ReadLevelTable(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("Level", out var levels) && levels.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in levels.EnumerateArray())
                {
                    if (!e.TryGetProperty("Level", out var lv) || !lv.TryGetInt32(out var l)) continue;
                    if (e.TryGetProperty("Bonus", out var bo) && bo.TryGetInt32(out var b)) rules.LevelBonus[l] = b;
                    if (e.TryGetProperty("XP", out var xp) && xp.TryGetInt32(out var x)) rules.LevelXp[l] = x;
                }
                if (rules.LevelBonus.Count > 0)
                {
                    int max = 1;
                    foreach (var k in rules.LevelBonus.Keys) if (k > max) max = k;
                    rules.MaxLevel = max;
                }
            }
        }

        private static void ReadVariables(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("CombatSettings", out var csEarly) && csEarly.ValueKind == JsonValueKind.Object
                && csEarly.TryGetProperty("Variables", out var vars) && vars.ValueKind == JsonValueKind.Object)
            {
                rules.Variables.Clear();
                foreach (var v in vars.EnumerateObject())
                    if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetDouble(out var dv))
                        rules.Variables[v.Name] = dv;
            }
        }

        private static void ReadCasterAbilities(JsonElement root, GameRules rules)
        {
            var casterAbilities = CasterAbilityIds(root);
            if (casterAbilities.Count > 0)
            {
                rules.CasterAbilityIds.Clear();
                rules.CasterAbilityIds.AddRange(casterAbilities);
            }
        }

        private static void ReadGridItems(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("GridItems", out var gridItems) && gridItems.ValueKind == JsonValueKind.Array)
            {
                rules.GridItems.Clear();
                foreach (var g in gridItems.EnumerateArray())
                {
                    if (!g.TryGetProperty("TemplateId", out var gid) || gid.ValueKind != JsonValueKind.String) continue;
                    var id = gid.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var gname = g.TryGetProperty("Name", out var gn) && gn.ValueKind == JsonValueKind.String ? (gn.GetString() ?? id!) : id!;
                    var blocksMove = g.TryGetProperty("BlocksMovement", out var bm) && bm.ValueKind == JsonValueKind.True;
                    var blocksSight = g.TryGetProperty("BlocksSight", out var bs) && bs.ValueKind == JsonValueKind.True;
                    rules.GridItems[id!] = new GridItemRule(id!, gname, blocksMove, blocksSight);
                }
            }

            if (root.TryGetProperty("TerrainCosts", out var terrainCosts) && terrainCosts.ValueKind == JsonValueKind.Array)
            {
                rules.TerrainCosts.Clear();
                foreach (var t in terrainCosts.EnumerateArray())
                {
                    if (!t.TryGetProperty("TemplateId", out var tid) || tid.ValueKind != JsonValueKind.String) continue;
                    var id = tid.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var tname = t.TryGetProperty("Name", out var tn) && tn.ValueKind == JsonValueKind.String ? (tn.GetString() ?? id!) : id!;
                    var mult = t.TryGetProperty("Multiplier", out var tm) && tm.TryGetDouble(out var tmv) && tmv > 0 ? tmv : rules.DifficultTerrainMultiplier;
                    rules.TerrainCosts[id!] = new TerrainCostRule(id!, tname, mult);
                }
            }

            if (root.TryGetProperty("CombatSettings", out var moveCs) && moveCs.ValueKind == JsonValueKind.Object
                && moveCs.TryGetProperty("StandFromProneSpeedFraction", out var sfp) && sfp.TryGetDouble(out var sfpv) && sfpv >= 0)
                rules.StandFromProneSpeedFraction = sfpv;

            if (root.TryGetProperty("Hazards", out var hazards) && hazards.ValueKind == JsonValueKind.Array)
            {
                rules.Hazards.Clear();
                foreach (var h in hazards.EnumerateArray())
                {
                    if (!h.TryGetProperty("TemplateId", out var hid) || hid.ValueKind != JsonValueKind.String) continue;
                    var id = hid.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var hname = h.TryGetProperty("Name", out var hn) && hn.ValueKind == JsonValueKind.String ? (hn.GetString() ?? id!) : id!;
                    var die = h.TryGetProperty("Die", out var hd) && hd.ValueKind == JsonValueKind.String ? (hd.GetString() ?? "") : "";
                    var per = h.TryGetProperty("PerFeet", out var hp) && hp.TryGetDouble(out var hpv) ? hpv : 0;
                    var max = h.TryGetProperty("MaxDice", out var hm) && hm.ValueKind == JsonValueKind.Number && hm.TryGetInt32(out var hmv) ? hmv : 0;
                    var dtype = h.TryGetProperty("DamageType", out var ht) && ht.ValueKind == JsonValueKind.String ? (ht.GetString() ?? "") : "";
                    var hsave = h.TryGetProperty("Save", out var hs) && hs.ValueKind == JsonValueKind.String ? (hs.GetString() ?? "").ToLowerInvariant() : "";
                    var hdc = h.TryGetProperty("SaveDc", out var hdcv) && hdcv.ValueKind == JsonValueKind.Number && hdcv.TryGetInt32(out var hdcn) ? hdcn : 0;
                    var half = h.TryGetProperty("HalfOnSave", out var hh) && hh.ValueKind == JsonValueKind.True;
                    var hcond = h.TryGetProperty("Condition", out var hc) && hc.ValueKind == JsonValueKind.String ? (hc.GetString() ?? "") : "";
                    rules.Hazards[id!] = new HazardRule(id!, hname, die, per, max, dtype, hsave, hdc, half, hcond);
                }
            }
        }

        private static void ReadArmorTypes(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("ArmorTypes", out var armors) && armors.ValueKind == JsonValueKind.Array)
            {
                rules.ArmorTypes.Clear();
                foreach (var a in armors.EnumerateArray())
                {
                    if (!a.TryGetProperty("TemplateId", out var aid) || aid.ValueKind != JsonValueKind.String) continue;
                    var id = aid.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var an = a.TryGetProperty("Name", out var av) && av.ValueKind == JsonValueKind.String ? (av.GetString() ?? id!) : id!;
                    var slot = a.TryGetProperty("EqSlotId", out var sv) && sv.ValueKind == JsonValueKind.String ? (sv.GetString() ?? "") : "";
                    rules.ArmorTypes[id!] = new ArmorTypeRule(id!, an, slot);
                }
            }
        }

        private static void ReadWeaponProperties(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("WeaponTypes", out var wtypes) && wtypes.ValueKind == JsonValueKind.Array)
            {
                var parsed = new Dictionary<string, WeaponPropertyRule>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in wtypes.EnumerateArray())
                {
                    if (!w.TryGetProperty("TemplateId", out var wid) || wid.ValueKind != JsonValueKind.String) continue;
                    var id = wid.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var wn = w.TryGetProperty("Name", out var wv) && wv.ValueKind == JsonValueKind.String ? (wv.GetString() ?? id!) : id!;
                    var abilities = new List<string>();
                    if (w.TryGetProperty("Effects", out var wfx) && wfx.ValueKind == JsonValueKind.Object
                        && wfx.TryGetProperty("AttackAbilities", out var aa) && aa.ValueKind == JsonValueKind.Array)
                        foreach (var x in aa.EnumerateArray())
                            if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                                abilities.Add(x.GetString()!.ToLowerInvariant());
                    parsed[id!] = new WeaponPropertyRule(id!, wn, abilities);
                }
                if (parsed.Values.Any(w => w.AttackAbilities.Count > 0))
                {
                    rules.WeaponProperties.Clear();
                    foreach (var kv in parsed) rules.WeaponProperties[kv.Key] = kv.Value;
                }
            }
        }

        private static void ReadConditions(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("Conditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
            {
                rules.Conditions.Clear();
                foreach (var c in conds.EnumerateArray())
                {
                    var cname = c.TryGetProperty("Name", out var cn) && cn.ValueKind == JsonValueKind.String ? (cn.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(cname)) continue;
                    var cid = c.TryGetProperty("TemplateId", out var ci) && ci.ValueKind == JsonValueKind.String ? (ci.GetString() ?? cname) : cname;
                    var cdesc = c.TryGetProperty("Description", out var cd) && cd.ValueKind == JsonValueKind.String ? (cd.GetString() ?? "") : "";

                    string atk = "", incoming = "", abilityCheck = "", saveRoll = "", expiresAt = "end", incomingBeyond = "";
                    bool blocks = false, stops = false, trackable = true, blocksReactions = false;
                    var resist = new List<string>();
                    int dmgBonus = 0, durationRounds = 0, endsOnDc = 0;
                    double speedMult = 1.0, maxHpMult = 1.0, incomingWithinFeet = 0;
                    string overTime = "", overTimeType = "", overTimeAt = "turn-start", endsOnAbility = "";
                    if (c.TryGetProperty("Trackable", out var tr) && (tr.ValueKind == JsonValueKind.True || tr.ValueKind == JsonValueKind.False)) trackable = tr.GetBoolean();
                    if (c.TryGetProperty("Effects", out var fx) && fx.ValueKind == JsonValueKind.Object)
                    {
                        if (fx.TryGetProperty("AttackRoll", out var ar) && ar.ValueKind == JsonValueKind.String) atk = (ar.GetString() ?? "").ToLowerInvariant();
                        if (fx.TryGetProperty("IncomingAttack", out var ia) && ia.ValueKind == JsonValueKind.String) incoming = (ia.GetString() ?? "").ToLowerInvariant();
                        if (fx.TryGetProperty("BlocksActions", out var ba) && (ba.ValueKind == JsonValueKind.True || ba.ValueKind == JsonValueKind.False)) blocks = ba.GetBoolean();
                        if (fx.TryGetProperty("StopsMovement", out var sm) && (sm.ValueKind == JsonValueKind.True || sm.ValueKind == JsonValueKind.False)) stops = sm.GetBoolean();
                        if (fx.TryGetProperty("DamageBonus", out var db))
                        {
                            if (db.ValueKind == JsonValueKind.Number && db.TryGetInt32(out var dbv)) dmgBonus = dbv;
                            else if (db.ValueKind == JsonValueKind.String) dmgBonus = rules.ResolveNumber(db.GetString() ?? "");
                        }
                        if (fx.TryGetProperty("Resistances", out var rs) && rs.ValueKind == JsonValueKind.Array)
                            foreach (var x in rs.EnumerateArray())
                                if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                                    resist.Add(x.GetString()!);
                        if (fx.TryGetProperty("AbilityCheckRoll", out var ac) && ac.ValueKind == JsonValueKind.String) abilityCheck = (ac.GetString() ?? "").ToLowerInvariant();
                        if (fx.TryGetProperty("SaveRoll", out var sr) && sr.ValueKind == JsonValueKind.String) saveRoll = (sr.GetString() ?? "").ToLowerInvariant();
                        if (fx.TryGetProperty("BlocksReactions", out var br) && (br.ValueKind == JsonValueKind.True || br.ValueKind == JsonValueKind.False)) blocksReactions = br.GetBoolean();
                        if (fx.TryGetProperty("SpeedMultiplier", out var sp) && sp.TryGetDouble(out var spv)) speedMult = spv;
                        if (fx.TryGetProperty("MaxHpMultiplier", out var mh) && mh.TryGetDouble(out var mhv)) maxHpMult = mhv;
                        if (fx.TryGetProperty("IncomingAttackBeyond", out var ib) && ib.ValueKind == JsonValueKind.String) incomingBeyond = (ib.GetString() ?? "").ToLowerInvariant();
                        if (fx.TryGetProperty("IncomingAttackWithinFeet", out var iw) && iw.TryGetDouble(out var iwv)) incomingWithinFeet = iwv;
                        if (fx.TryGetProperty("DamageOverTime", out var dot) && dot.ValueKind == JsonValueKind.String) overTime = dot.GetString() ?? "";
                        if (fx.TryGetProperty("DamageOverTimeType", out var dtt) && dtt.ValueKind == JsonValueKind.String) overTimeType = (dtt.GetString() ?? "").ToLowerInvariant();
                        if (fx.TryGetProperty("DamageOverTimeAt", out var dta) && dta.ValueKind == JsonValueKind.String) overTimeAt = (dta.GetString() ?? "turn-start").ToLowerInvariant();
                        if (fx.TryGetProperty("EndsOnSaveDc", out var esd) && esd.ValueKind == JsonValueKind.Number && esd.TryGetInt32(out var esdv)) endsOnDc = esdv;
                        if (fx.TryGetProperty("EndsOnSaveAbility", out var esa) && esa.ValueKind == JsonValueKind.String) endsOnAbility = (esa.GetString() ?? "").ToLowerInvariant();
                    }
                    if (c.TryGetProperty("DurationRounds", out var dr) && dr.ValueKind == JsonValueKind.Number && dr.TryGetInt32(out var drv)) durationRounds = drv;
                    if (c.TryGetProperty("ExpiresAt", out var ea) && ea.ValueKind == JsonValueKind.String) expiresAt = (ea.GetString() ?? "end").ToLowerInvariant();
                    rules.Conditions[cname] = new ConditionRule(cid, cname, cdesc, atk, incoming, blocks, stops, trackable, resist, dmgBonus,
                        speedMult, maxHpMult, abilityCheck, saveRoll, blocksReactions, durationRounds, expiresAt, incomingBeyond, incomingWithinFeet,
                        overTime, overTimeType, overTimeAt, endsOnDc, endsOnAbility);
                }
                ApplyConditionEffects(rules);
            }
        }

        private static void ReadClassResources(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("ClassResources", out var resources) && resources.ValueKind == JsonValueKind.Array)
            {
                rules.ClassResources.Clear();
                foreach (var r in resources.EnumerateArray())
                {
                    if (!r.TryGetProperty("Id", out var rid) || rid.ValueKind != JsonValueKind.String) continue;
                    var id = rid.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (!r.TryGetProperty("Effects", out var rfx) || rfx.ValueKind != JsonValueKind.Object) continue;
                    var heal = rfx.TryGetProperty("Heal", out var hv) && hv.ValueKind == JsonValueKind.String ? (hv.GetString() ?? "") : "";
                    var perPoint = rfx.TryGetProperty("HealPerPoint", out var pv) && pv.ValueKind == JsonValueKind.Number && pv.TryGetInt32(out var pvv) ? pvv : 0;
                    var condition = rfx.TryGetProperty("Condition", out var cnv) && cnv.ValueKind == JsonValueKind.String ? (cnv.GetString() ?? "") : "";
                    var inspire = rfx.TryGetProperty("InspireDie", out var inv) && inv.ValueKind == JsonValueKind.String ? (inv.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(heal) && perPoint <= 0 && string.IsNullOrWhiteSpace(condition) && string.IsNullOrWhiteSpace(inspire)) continue;
                    rules.ClassResources[id!] = new ClassResourceRule(id!, heal, perPoint, condition, inspire);
                }
            }
        }

        private static void ReadMasteries(JsonElement root, GameRules rules)
        {
            if (root.TryGetProperty("WeaponMasteries", out var masteries) && masteries.ValueKind == JsonValueKind.Array)
            {
                rules.Masteries.Clear();
                foreach (var m in masteries.EnumerateArray())
                {
                    if (!m.TryGetProperty("TemplateId", out var mid) || mid.ValueKind != JsonValueKind.String) continue;
                    var id = mid.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var effect = m.TryGetProperty("Effect", out var ef) && ef.ValueKind == JsonValueKind.String ? (ef.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(effect)) continue;
                    var mname = m.TryGetProperty("Name", out var mn) && mn.ValueKind == JsonValueKind.String ? (mn.GetString() ?? id!) : id!;
                    var slowFeet = m.TryGetProperty("SpeedPenaltyFeet", out var sp) && sp.TryGetInt32(out var spv) ? spv : 0;
                    var pushFeet = m.TryGetProperty("PushFeet", out var pf) && pf.TryGetInt32(out var pfv) ? pfv : 0;
                    var saveAb = m.TryGetProperty("SaveAbility", out var sab) && sab.ValueKind == JsonValueKind.String ? (sab.GetString() ?? "") : "";
                    var mcond = m.TryGetProperty("Condition", out var mc) && mc.ValueKind == JsonValueKind.String ? (mc.GetString() ?? "") : "";
                    rules.Masteries[id!] = new MasteryRule(id!, mname, effect.ToLowerInvariant(), slowFeet, pushFeet, saveAb, mcond);
                }
            }
        }

        private static void ReadCombatRules(JsonElement cs, GameRules rules)
        {
            ReadCombatNumbers(cs, rules);
            ReadExhaustion(cs, rules);
            ReadMovementAndCarry(cs, rules);
            ReadDamageModes(cs, rules);
            ReadCastingAndContestLists(cs, rules);
            ReadTacticalActions(cs, rules);
            ReadCheckDifficulties(cs, rules);
            ReadHealthStates(cs, rules);
            ReadResolutionShape(cs, rules);
            ReadSenses(cs, rules);
            ReadAoeShapes(cs, rules);
            ReadConditionLists(cs, rules);
            ReadEncounterTables(cs, rules);
            ReadCharacterCreation(cs, rules);
        }

        private static void ReadCombatNumbers(JsonElement cs, GameRules rules)
        {
            rules.AbilityModBaseline = ReadInt(cs, "AbilityModBaseline", rules.AbilityModBaseline);
            rules.AbilityModDivisor = ReadInt(cs, "AbilityModDivisor", rules.AbilityModDivisor);
            rules.DeathSaveSuccessesToStabilize = ReadInt(cs, "DeathSaveSuccessesToStabilize", rules.DeathSaveSuccessesToStabilize);
            rules.DeathSaveFailuresToDie = ReadInt(cs, "DeathSaveFailuresToDie", rules.DeathSaveFailuresToDie);
            rules.DeathSaveThreshold = ReadInt(cs, "DeathSaveThreshold", rules.DeathSaveThreshold);
            rules.DeathSaveCritHeal = ReadInt(cs, "DeathSaveCritHeal", rules.DeathSaveCritHeal);
            rules.DeathSaveFumbleFailures = ReadInt(cs, "DeathSaveFumbleFailures", rules.DeathSaveFumbleFailures);
            rules.ConcentrationDcFloor = ReadInt(cs, "ConcentrationDcFloor", rules.ConcentrationDcFloor);
            rules.ConcentrationDcDivisor = ReadInt(cs, "ConcentrationDcDivisor", rules.ConcentrationDcDivisor);
            rules.ConcentrationDcCap = ReadInt(cs, "ConcentrationDcCap", rules.ConcentrationDcCap);
            rules.SpellSaveDcBase = ReadInt(cs, "SpellSaveDcBase", rules.SpellSaveDcBase);
            rules.PassiveScoreBase = ReadInt(cs, "PassiveScoreBase", rules.PassiveScoreBase);
            rules.PerceptionSkill = ReadString(cs, "PerceptionSkill", rules.PerceptionSkill);
            rules.DefaultSpeed = ReadInt(cs, "DefaultSpeed", rules.DefaultSpeed);
            rules.DifficultTerrainMultiplier = ReadDouble(cs, "DifficultTerrainMultiplier", rules.DifficultTerrainMultiplier);
            rules.DashMultiplier = ReadDouble(cs, "DashMultiplier", rules.DashMultiplier);
            rules.JumpAbility = ReadString(cs, "JumpAbility", rules.JumpAbility);
            rules.JumpScoreMultiplier = ReadDouble(cs, "JumpScoreMultiplier", rules.JumpScoreMultiplier);
            rules.CoverHalfBonus = ReadInt(cs, "CoverHalfBonus", rules.CoverHalfBonus);
            rules.CoverThreeQuartersBonus = ReadInt(cs, "CoverThreeQuartersBonus", rules.CoverThreeQuartersBonus);
            rules.LongRangeDisadvantage = ReadBool(cs, "LongRangeDisadvantage", rules.LongRangeDisadvantage);
            rules.CoreRollExpression = ReadString(cs, "CoreRollExpression", rules.CoreRollExpression);
            rules.RollDirection = ReadString(cs, "RollDirection", rules.RollDirection);
            rules.ExplodingDiceLimit = ReadInt(cs, "ExplodingDiceLimit", rules.ExplodingDiceLimit);
            rules.ExpertiseMultiplier = ReadInt(cs, "ExpertiseMultiplier", rules.ExpertiseMultiplier);
            rules.CritNaturalRoll = ReadInt(cs, "CritNaturalRoll", rules.CritNaturalRoll);
            rules.FumbleRoll = ReadInt(cs, "FumbleRoll", rules.FumbleRoll);
            rules.CritDamageDiceMultiplier = ReadInt(cs, "CritDamageDiceMultiplier", rules.CritDamageDiceMultiplier);
            rules.InitiativeDie = ReadInt(cs, "InitiativeDie", rules.InitiativeDie);
            rules.AttackDie = ReadInt(cs, "AttackDie", rules.AttackDie);
            rules.FallbackAttackDamage = ReadString(cs, "FallbackAttackDamage", rules.FallbackAttackDamage);
            rules.DefaultHitDie = ReadInt(cs, "DefaultHitDie", rules.DefaultHitDie);
            rules.AbilityScoreCap = ReadInt(cs, "AbilityScoreCap", rules.AbilityScoreCap);
            rules.ArmorClassBase = ReadInt(cs, "ArmorClassBase", rules.ArmorClassBase);
            rules.AttunementLimit = ReadInt(cs, "AttunementLimit", rules.AttunementLimit);
            rules.RestoreFullHpOnLongRest = ReadBool(cs, "RestoreFullHpOnLongRest", rules.RestoreFullHpOnLongRest);
        }

        private static void ReadExhaustion(JsonElement cs, GameRules rules)
        {
            if (cs.TryGetProperty("Exhaustion", out var exh) && exh.ValueKind == JsonValueKind.Object)
            {
                if (exh.TryGetProperty("MaxLevel", out var ml) && ml.TryGetInt32(out var mlv)) rules.Exhaustion.MaxLevel = mlv;
                if (exh.TryGetProperty("ReducePerLongRest", out var rp) && rp.TryGetInt32(out var rpv)) rules.Exhaustion.ReducePerLongRest = rpv;
                if (exh.TryGetProperty("DeathAtMax", out var dm) && (dm.ValueKind == JsonValueKind.True || dm.ValueKind == JsonValueKind.False)) rules.Exhaustion.DeathAtMax = dm.GetBoolean();
                if (exh.TryGetProperty("Levels", out var lv) && lv.ValueKind == JsonValueKind.Object)
                {
                    rules.Exhaustion.Levels.Clear();
                    foreach (var p in lv.EnumerateObject())
                    {
                        if (!int.TryParse(p.Name, out var lvl)) continue;
                        if (p.Value.ValueKind == JsonValueKind.String)
                        {
                            rules.Exhaustion.Levels[lvl] = new ExhaustionLevelRule(p.Value.GetString() ?? "", 1.0, 1.0, "", "", "");
                            continue;
                        }
                        if (p.Value.ValueKind != JsonValueKind.Object) continue;
                        var text = p.Value.TryGetProperty("Text", out var tx) && tx.ValueKind == JsonValueKind.String ? (tx.GetString() ?? "") : "";
                        var speed = p.Value.TryGetProperty("SpeedMultiplier", out var sp) && sp.TryGetDouble(out var spv) ? spv : 1.0;
                        var maxHp = p.Value.TryGetProperty("MaxHpMultiplier", out var mh) && mh.TryGetDouble(out var mhv) ? mhv : 1.0;
                        var check = p.Value.TryGetProperty("AbilityCheckRoll", out var ac) && ac.ValueKind == JsonValueKind.String ? (ac.GetString() ?? "").ToLowerInvariant() : "";
                        var save = p.Value.TryGetProperty("SaveRoll", out var sv) && sv.ValueKind == JsonValueKind.String ? (sv.GetString() ?? "").ToLowerInvariant() : "";
                        var atk = p.Value.TryGetProperty("AttackRoll", out var ar) && ar.ValueKind == JsonValueKind.String ? (ar.GetString() ?? "").ToLowerInvariant() : "";
                        var d20Pen = p.Value.TryGetProperty("D20Penalty", out var dp) && dp.ValueKind == JsonValueKind.Number && dp.TryGetInt32(out var dpv) ? dpv : 0;
                        var speedPen = p.Value.TryGetProperty("SpeedPenaltyFeet", out var sf) && sf.ValueKind == JsonValueKind.Number && sf.TryGetInt32(out var sfv) ? sfv : 0;
                        rules.Exhaustion.Levels[lvl] = new ExhaustionLevelRule(text, speed, maxHp, check, save, atk, d20Pen, speedPen);
                    }
                }
            }
        }

        private static void ReadMovementAndCarry(JsonElement cs, GameRules rules)
        {
            rules.FeetPerSquare = ReadDouble(cs, "FeetPerSquare", rules.FeetPerSquare);
            rules.DiagonalCostSquares = ReadDouble(cs, "DiagonalCostSquares", rules.DiagonalCostSquares);
            rules.MovedThresholdFeet = ReadDouble(cs, "MovedThresholdFeet", rules.MovedThresholdFeet);
            rules.EnforceMovementBudget = ReadBool(cs, "EnforceMovementBudget", rules.EnforceMovementBudget);
            rules.DmIgnoresMovementBudget = ReadBool(cs, "DmIgnoresMovementBudget", rules.DmIgnoresMovementBudget);
            rules.RulerIgnoresWalls = ReadBool(cs, "RulerIgnoresWalls", rules.RulerIgnoresWalls);
            rules.RulerIgnoresOccupied = ReadBool(cs, "RulerIgnoresOccupied", rules.RulerIgnoresOccupied);
            rules.BlankMapWidthPx = ReadInt(cs, "BlankMapWidthPx", rules.BlankMapWidthPx);
            rules.BlankMapHeightPx = ReadInt(cs, "BlankMapHeightPx", rules.BlankMapHeightPx);
            ReadBlankMapPresets(cs, rules);
            rules.ConfineToMapBounds = ReadBool(cs, "ConfineToMapBounds", rules.ConfineToMapBounds);
            rules.WallsSnapToGrid = ReadBool(cs, "WallsSnapToGrid", rules.WallsSnapToGrid);
            rules.DefaultLegendaryActionsPerRound = ReadInt(cs, "DefaultLegendaryActionsPerRound", rules.DefaultLegendaryActionsPerRound);
            rules.LairActionInitiativeCount = ReadInt(cs, "LairActionInitiativeCount", rules.LairActionInitiativeCount);
            rules.ConeWidthRatio = ReadDouble(cs, "ConeWidthRatio", rules.ConeWidthRatio);
            rules.DefaultAoeSizeFeet = ReadDouble(cs, "DefaultAoeSizeFeet", rules.DefaultAoeSizeFeet);
            rules.DefaultAoeWidthFeet = ReadDouble(cs, "DefaultAoeWidthFeet", rules.DefaultAoeWidthFeet);
            rules.DefaultSaveDc = ReadInt(cs, "DefaultSaveDc", rules.DefaultSaveDc);
            rules.CubeOriginOnFace = ReadBool(cs, "CubeOriginOnFace", rules.CubeOriginOnFace);
            rules.MinDieSides = ReadInt(cs, "MinDieSides", rules.MinDieSides);
            rules.MaxDieSides = ReadInt(cs, "MaxDieSides", rules.MaxDieSides);
            rules.MaxDiceCount = ReadInt(cs, "MaxDiceCount", rules.MaxDiceCount);
            rules.CarryCapacityPerStrength = ReadDouble(cs, "CarryCapacityPerStrength", rules.CarryCapacityPerStrength);
            rules.EncumberedPerStrength = ReadDouble(cs, "EncumberedPerStrength", rules.EncumberedPerStrength);
            rules.HeavilyEncumberedPerStrength = ReadDouble(cs, "HeavilyEncumberedPerStrength", rules.HeavilyEncumberedPerStrength);
            rules.HpFirstLevelMax = ReadBool(cs, "HpFirstLevelMax", rules.HpFirstLevelMax);
            rules.HpPerLevelMode = ReadString(cs, "HpPerLevelMode", rules.HpPerLevelMode);
            rules.HpPerLevelMaxMode = ReadString(cs, "HpPerLevelMaxMode", rules.HpPerLevelMaxMode);
            rules.HitDicePerLevel = ReadInt(cs, "HitDicePerLevel", rules.HitDicePerLevel);
            rules.AbilityScoreIncrementPerAsi = ReadInt(cs, "AbilityScoreIncrementPerAsi", rules.AbilityScoreIncrementPerAsi);
            rules.AttunementFlagValue = ReadString(cs, "AttunementFlagValue", rules.AttunementFlagValue);
            rules.DamageTypeIdPrefix = ReadString(cs, "DamageTypeIdPrefix", rules.DamageTypeIdPrefix);
            rules.ScaleByCharacterToken = ReadString(cs, "ScaleByCharacterToken", rules.ScaleByCharacterToken);
            rules.SenseRangeUnit = ReadString(cs, "SenseRangeUnit", rules.SenseRangeUnit);
            rules.SkillStoreKey = ReadString(cs, "SkillStoreKey", rules.SkillStoreKey);
            rules.ExpertiseStoreKey = ReadString(cs, "ExpertiseStoreKey", rules.ExpertiseStoreKey);
            rules.FeatStoreKey = ReadString(cs, "FeatStoreKey", rules.FeatStoreKey);
            rules.SubclassStoreKey = ReadString(cs, "SubclassStoreKey", rules.SubclassStoreKey);
            rules.AsiTokenPrefix = ReadString(cs, "AsiTokenPrefix", rules.AsiTokenPrefix);
            rules.ToolProficiencyIdPrefix = ReadString(cs, "ToolProficiencyIdPrefix", rules.ToolProficiencyIdPrefix);
            rules.GrantedSpellIdPrefix = ReadString(cs, "GrantedSpellIdPrefix", rules.GrantedSpellIdPrefix);
            rules.FeatFeaturePrefix = ReadString(cs, "FeatFeaturePrefix", rules.FeatFeaturePrefix);
            rules.SubclassFeaturePrefix = ReadString(cs, "SubclassFeaturePrefix", rules.SubclassFeaturePrefix);
            rules.UnknownHpLabel = ReadString(cs, "UnknownHpLabel", rules.UnknownHpLabel);
            rules.DownHpLabel = ReadString(cs, "DownHpLabel", rules.DownHpLabel);
            rules.HealthyHpLabel = ReadString(cs, "HealthyHpLabel", rules.HealthyHpLabel);
            rules.DefaultLineWidthFeet = ReadDouble(cs, "DefaultLineWidthFeet", rules.DefaultLineWidthFeet);

            if (cs.TryGetProperty("CreatureSizeSquares", out var sizes) && sizes.ValueKind == JsonValueKind.Object)
                foreach (var p in sizes.EnumerateObject())
                    if (p.Value.TryGetDouble(out var sv)) rules.CreatureSizeSquares[p.Name.ToLowerInvariant()] = sv;

            if (cs.TryGetProperty("ActionCosts", out var costs) && costs.ValueKind == JsonValueKind.Object)
            {
                rules.ActionCosts.Clear();
                foreach (var p in costs.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String)
                        rules.ActionCosts[p.Name] = (p.Value.GetString() ?? "none").ToLowerInvariant();
            }
        }

        private static void ReadDamageModes(JsonElement cs, GameRules rules)
        {
            rules.ContestDcBase = ReadInt(cs, "ContestDcBase", rules.ContestDcBase);
            rules.CritDamageMode = ReadString(cs, "CritDamageMode", rules.CritDamageMode).ToLowerInvariant();
            rules.CritAlwaysHits = ReadBool(cs, "CritAlwaysHits", rules.CritAlwaysHits);
            rules.FumbleAlwaysMisses = ReadBool(cs, "FumbleAlwaysMisses", rules.FumbleAlwaysMisses);
            rules.AutoHitToHit = ReadInt(cs, "AutoHitToHit", rules.AutoHitToHit);
            rules.InitiativeTieBreakByModifier = ReadBool(cs, "InitiativeTieBreakByModifier", rules.InitiativeTieBreakByModifier);
            rules.ResistanceDivisor = ReadInt(cs, "ResistanceDivisor", rules.ResistanceDivisor);
            rules.VulnerabilityMultiplier = ReadInt(cs, "VulnerabilityMultiplier", rules.VulnerabilityMultiplier);
            rules.HalfOnSaveDivisor = ReadInt(cs, "HalfOnSaveDivisor", rules.HalfOnSaveDivisor);

            ReadIntSet(cs, "CritNaturalRolls", rules.CritNaturalRolls, rules.CritNaturalRoll);
            ReadIntSet(cs, "FumbleRolls", rules.FumbleRolls, rules.FumbleRoll);
            rules.AbilityScoreHardCap = ReadInt(cs, "AbilityScoreHardCap", rules.AbilityScoreHardCap);
            rules.BothRulesVersion = ReadString(cs, "BothRulesVersion", rules.BothRulesVersion);
            rules.MassiveDamageKills = ReadBool(cs, "MassiveDamageKills", rules.MassiveDamageKills);
            rules.MassiveDamageMaxHpMultiple = ReadDouble(cs, "MassiveDamageMaxHpMultiple", rules.MassiveDamageMaxHpMultiple);
            rules.InspirationGrantsAdvantage = ReadBool(cs, "InspirationGrantsAdvantage", rules.InspirationGrantsAdvantage);
            rules.PlayerRollsOwnSaves = ReadBool(cs, "PlayerRollsOwnSaves", rules.PlayerRollsOwnSaves);
            rules.RoundSeconds = ReadInt(cs, "RoundSeconds", rules.RoundSeconds);
            rules.ShortRestMinutes = ReadInt(cs, "ShortRestMinutes", rules.ShortRestMinutes);
            rules.LongRestMinutes = ReadInt(cs, "LongRestMinutes", rules.LongRestMinutes);
            rules.MinutesPerDay = ReadInt(cs, "MinutesPerDay", rules.MinutesPerDay);
            rules.InitiativeAbility = ReadString(cs, "InitiativeAbility", rules.InitiativeAbility);
            rules.ConcentrationAbility = ReadString(cs, "ConcentrationAbility", rules.ConcentrationAbility);
            rules.ArmorClassAbility = ReadString(cs, "ArmorClassAbility", rules.ArmorClassAbility);
            rules.HitPointAbility = ReadString(cs, "HitPointAbility", rules.HitPointAbility);
            rules.DefaultSaveAbility = ReadString(cs, "DefaultSaveAbility", rules.DefaultSaveAbility);
            rules.MeleeAttackAbility = ReadString(cs, "MeleeAttackAbility", rules.MeleeAttackAbility);
            rules.RangedAttackAbility = ReadString(cs, "RangedAttackAbility", rules.RangedAttackAbility);
            rules.OffHandSlotId = ReadString(cs, "OffHandSlotId", rules.OffHandSlotId);
            rules.OffHandWeaponProperty = ReadString(cs, "OffHandWeaponProperty", rules.OffHandWeaponProperty);
        }

        private static void ReadCastingAndContestLists(JsonElement cs, GameRules rules)
        {
            if (cs.TryGetProperty("FeatSpellListClassIds", out var fsc) && fsc.ValueKind == JsonValueKind.Array)
            {
                var ids = new List<string>();
                foreach (var x in fsc.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                        ids.Add(x.GetString()!);
                if (ids.Count > 0)
                {
                    rules.FeatSpellListClassIds.Clear();
                    rules.FeatSpellListClassIds.AddRange(ids);
                }
            }

            if (cs.TryGetProperty("CastingTimeKinds", out var ctk) && ctk.ValueKind == JsonValueKind.Object)
            {
                rules.CastingTimeKinds.Clear();
                foreach (var p in ctk.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                        rules.CastingTimeKinds[p.Name] = p.Value.GetString()!.ToLowerInvariant();
            }

            if (cs.TryGetProperty("ContestAbilities", out var ca) && ca.ValueKind == JsonValueKind.Array)
            {
                var abilities = new List<string>();
                foreach (var x in ca.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                        abilities.Add(x.GetString()!.ToLowerInvariant());
                if (abilities.Count > 0)
                {
                    rules.ContestAbilities.Clear();
                    rules.ContestAbilities.AddRange(abilities);
                }
            }

            if (cs.TryGetProperty("FallbackAttackAbilities", out var fa) && fa.ValueKind == JsonValueKind.Array)
            {
                var abilities = new List<string>();
                foreach (var x in fa.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                        abilities.Add(x.GetString()!.ToLowerInvariant());
                if (abilities.Count > 0)
                {
                    rules.FallbackAttackAbilities.Clear();
                    rules.FallbackAttackAbilities.AddRange(abilities);
                }
            }
        }

        private static void ReadTacticalActions(JsonElement cs, GameRules rules)
        {
            if (cs.TryGetProperty("TacticalActions", out var ta) && ta.ValueKind == JsonValueKind.Object)
            {
                rules.TacticalActions.Clear();
                foreach (var p in ta.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) continue;
                    var effect = p.Value.TryGetProperty("Effect", out var ev) && ev.ValueKind == JsonValueKind.String ? (ev.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(effect)) continue;
                    var name = p.Value.TryGetProperty("Name", out var nv) && nv.ValueKind == JsonValueKind.String ? (nv.GetString() ?? p.Name) : p.Name;
                    var cost = p.Value.TryGetProperty("Cost", out var kv) && kv.ValueKind == JsonValueKind.String ? (kv.GetString() ?? "action") : "action";
                    var cond = p.Value.TryGetProperty("Condition", out var cv) && cv.ValueKind == JsonValueKind.String ? (cv.GetString() ?? "") : "";
                    var saves = new List<string>();
                    if (p.Value.TryGetProperty("DefenderSaves", out var dsv) && dsv.ValueKind == JsonValueKind.Array)
                        foreach (var x in dsv.EnumerateArray())
                            if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                                saves.Add(x.GetString()!.ToLowerInvariant());
                    var push = 0;
                    if (p.Value.TryGetProperty("PushFeet", out var pfv))
                        push = pfv.ValueKind == JsonValueKind.Number && pfv.TryGetInt32(out var pfi) ? pfi
                             : pfv.ValueKind == JsonValueKind.String ? rules.ResolveNumber(pfv.GetString() ?? "") : 0;
                    var checkAbility = p.Value.TryGetProperty("CheckAbility", out var cav) && cav.ValueKind == JsonValueKind.String ? (cav.GetString() ?? "").ToLowerInvariant() : "";
                    var checkSkill = p.Value.TryGetProperty("CheckSkill", out var csv) && csv.ValueKind == JsonValueKind.String ? (csv.GetString() ?? "") : "";
                    var checkDifficulty = p.Value.TryGetProperty("CheckDifficulty", out var cdv) && cdv.ValueKind == JsonValueKind.String ? (cdv.GetString() ?? "") : "";
                    var checkDc = 0;
                    if (p.Value.TryGetProperty("CheckDc", out var cdcv))
                        checkDc = cdcv.ValueKind == JsonValueKind.Number && cdcv.TryGetInt32(out var cdci) ? cdci
                                : cdcv.ValueKind == JsonValueKind.String ? rules.ResolveNumber(cdcv.GetString() ?? "") : 0;
                    rules.TacticalActions[p.Name] = new TacticalActionRule(p.Name.ToLowerInvariant(), name, effect.ToLowerInvariant(), cost.ToLowerInvariant(), cond, saves, push, checkAbility, checkSkill, checkDifficulty, checkDc);
                }
            }
        }

        private static void ReadCheckDifficulties(JsonElement cs, GameRules rules)
        {
            if (!cs.TryGetProperty("CheckDifficulties", out var cd) || cd.ValueKind != JsonValueKind.Array) return;
            rules.CheckDifficulties.Clear();
            foreach (var e in cd.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var id = e.TryGetProperty("Id", out var iv) && iv.ValueKind == JsonValueKind.String ? (iv.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = e.TryGetProperty("Name", out var nv) && nv.ValueKind == JsonValueKind.String ? (nv.GetString() ?? id) : id;
                var dc = 0;
                if (e.TryGetProperty("Dc", out var dv))
                    dc = dv.ValueKind == JsonValueKind.Number && dv.TryGetInt32(out var di) ? di
                       : dv.ValueKind == JsonValueKind.String ? rules.ResolveNumber(dv.GetString() ?? "") : 0;
                if (dc <= 0) continue;
                rules.CheckDifficulties[id] = new CheckDifficultyRule(id, name, dc);
            }
        }

        private static void ReadResolutionShape(JsonElement cs, GameRules rules)
        {
            if (cs.TryGetProperty("OutcomeBands", out var ob) && ob.ValueKind == JsonValueKind.Array)
            {
                rules.OutcomeBands.Clear();
                foreach (var e in ob.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var id = e.TryGetProperty("Id", out var iv) && iv.ValueKind == JsonValueKind.String ? (iv.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var margin = e.TryGetProperty("MarginAtLeast", out var mv) && mv.ValueKind == JsonValueKind.Number && mv.TryGetInt32(out var mi) ? mi : 0;
                    var hits = e.TryGetProperty("Hits", out var hv) && hv.ValueKind == JsonValueKind.True;
                    var critDmg = e.TryGetProperty("CritDamage", out var cv) && cv.ValueKind == JsonValueKind.True;
                    rules.OutcomeBands.Add(new OutcomeBand(id, margin, hits, critDmg));
                }
            }

            if (cs.TryGetProperty("ProficiencyRanks", out var pr) && pr.ValueKind == JsonValueKind.Array)
            {
                rules.ProficiencyRanks.Clear();
                foreach (var e in pr.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var id = e.TryGetProperty("Id", out var iv) && iv.ValueKind == JsonValueKind.String ? (iv.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var mult = e.TryGetProperty("Multiplier", out var mv) && mv.ValueKind == JsonValueKind.Number && mv.TryGetDouble(out var md) ? md : 0;
                    rules.ProficiencyRanks[id] = new ProficiencyRank(id, mult);
                }
            }
        }

        private static void ReadHealthStates(JsonElement cs, GameRules rules)
        {
            if (!cs.TryGetProperty("HealthStates", out var hs) || hs.ValueKind != JsonValueKind.Array) return;
            var parsed = new List<HealthState>();
            foreach (var e in hs.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var label = e.TryGetProperty("Label", out var lv) && lv.ValueKind == JsonValueKind.String ? (lv.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(label)) continue;
                if (!e.TryGetProperty("Below", out var bv) || bv.ValueKind != JsonValueKind.Number || !bv.TryGetDouble(out var below)) continue;
                parsed.Add(new HealthState(label, below));
            }
            // The block being here at all is the intent, an empty one means drop the bands, not fall back to the built in ones
            parsed.Sort((a, b) => a.Below.CompareTo(b.Below));
            rules.HealthStates.Clear();
            rules.HealthStates.AddRange(parsed);
        }

        private static void ReadSenses(JsonElement cs, GameRules rules)
        {
            if (!cs.TryGetProperty("Senses", out var sn) || sn.ValueKind != JsonValueKind.Array) return;
            var parsed = new List<SenseDef>();
            foreach (var e in sn.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var match = e.TryGetProperty("MatchId", out var mv) && mv.ValueKind == JsonValueKind.String ? (mv.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(match)) continue;
                var name = e.TryGetProperty("Name", out var nv) && nv.ValueKind == JsonValueKind.String ? (nv.GetString() ?? match) : match;
                var range = e.TryGetProperty("RangeFeet", out var rv) && rv.ValueKind == JsonValueKind.Number && rv.TryGetInt32(out var rvv) ? rvv : 0;
                var upRange = e.TryGetProperty("UpgradeRangeFeet", out var uv) && uv.ValueKind == JsonValueKind.Number && uv.TryGetInt32(out var uvv) ? uvv : 0;
                var ups = new List<string>();
                if (e.TryGetProperty("UpgradeMatches", out var um) && um.ValueKind == JsonValueKind.Array)
                    foreach (var x in um.EnumerateArray())
                        if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())) ups.Add(x.GetString()!);
                parsed.Add(new SenseDef(match, name, range, ups, upRange));
            }
            rules.Senses.Clear();
            rules.Senses.AddRange(parsed);
        }

        // An entry naming a Geometry the engine has no hit test for is dropped rather than shown as a button that draws nothing.
        private static void ReadAoeShapes(JsonElement cs, GameRules rules)
        {
            if (!cs.TryGetProperty("AoeShapes", out var sh) || sh.ValueKind != JsonValueKind.Array) return;
            var known = new[] { "cone", "circle", "line", "cube" };
            var parsed = new List<AoeShapeDef>();
            foreach (var e in sh.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var id = e.TryGetProperty("Id", out var iv) && iv.ValueKind == JsonValueKind.String ? (iv.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(id) || !known.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
                var name = e.TryGetProperty("Name", out var nv) && nv.ValueKind == JsonValueKind.String ? (nv.GetString() ?? id) : id;
                var usesWidth = e.TryGetProperty("UsesWidth", out var wv) && wv.ValueKind == JsonValueKind.True;
                parsed.Add(new AoeShapeDef(id.ToLowerInvariant(), name, usesWidth));
            }
            if (parsed.Count == 0) return;
            rules.AoeShapes.Clear();
            rules.AoeShapes.AddRange(parsed);
        }

        private static void ReadConditionLists(JsonElement cs, GameRules rules)
        {
            ReadConditionList(cs, "ShortRestSlotCasterTypes", rules.ShortRestSlotCasterTypes);
            ReadConditionList(cs, "AttackDisadvantageConditions", rules.AttackDisadvantageConditions);
            ReadConditionList(cs, "AttackAdvantageConditions", rules.AttackAdvantageConditions);
            ReadConditionList(cs, "TargetAdvantageConditions", rules.TargetAdvantageConditions);
            ReadConditionList(cs, "TargetDisadvantageConditions", rules.TargetDisadvantageConditions);
            ReadConditionList(cs, "IncapacitatingConditions", rules.IncapacitatingConditions);
            ReadConditionList(cs, "MovementStoppingConditions", rules.MovementStoppingConditions);
        }

        private static void ReadEncounterTables(JsonElement cs, GameRules rules)
        {
            if (cs.TryGetProperty("CrXp", out var crx) && crx.ValueKind == JsonValueKind.Object)
            {
                rules.CrXp.Clear();
                foreach (var p in crx.EnumerateObject())
                    if (p.Value.TryGetInt32(out var xv)) rules.CrXp[p.Name] = xv;
            }

            if (cs.TryGetProperty("EncounterBudgets", out var eb) && eb.ValueKind == JsonValueKind.Object)
            {
                rules.EncounterBudgets.Clear();
                foreach (var p in eb.EnumerateObject())
                    if (int.TryParse(p.Name, out var lvl) && p.Value.ValueKind == JsonValueKind.Array)
                    {
                        int[] v = new int[3];
                        int i = 0;
                        foreach (var x in p.Value.EnumerateArray()) { if (i < 3 && x.TryGetInt32(out var xv)) v[i] = xv; i++; }
                        if (i >= 3) rules.EncounterBudgets[lvl] = (v[0], v[1], v[2]);
                    }
            }
        }

        private static void ReadCharacterCreation(JsonElement cs, GameRules rules)
        {
            rules.PointBuyBudget = ReadInt(cs, "PointBuyBudget", rules.PointBuyBudget);
            rules.PointBuyMinScore = ReadInt(cs, "PointBuyMinScore", rules.PointBuyMinScore);
            rules.PointBuyMaxScore = ReadInt(cs, "PointBuyMaxScore", rules.PointBuyMaxScore);
            rules.ManualMinScore = ReadInt(cs, "ManualMinScore", rules.ManualMinScore);
            rules.ManualMaxScore = ReadInt(cs, "ManualMaxScore", rules.ManualMaxScore);

            if (cs.TryGetProperty("PointBuyCosts", out var pbc) && pbc.ValueKind == JsonValueKind.Object)
            {
                rules.PointBuyCosts.Clear();
                foreach (var p in pbc.EnumerateObject())
                    if (int.TryParse(p.Name, out var sc) && p.Value.TryGetInt32(out var cost)) rules.PointBuyCosts[sc] = cost;
            }

            if (cs.TryGetProperty("StandardArray", out var sa) && sa.ValueKind == JsonValueKind.Array)
            {
                rules.StandardArray.Clear();
                foreach (var x in sa.EnumerateArray()) if (x.TryGetInt32(out var xv)) rules.StandardArray.Add(xv);
            }

            rules.AbilityRollDice = ReadString(cs, "AbilityRollDice", rules.AbilityRollDice);

            if (cs.TryGetProperty("SpellLevelNames", out var sln) && sln.ValueKind == JsonValueKind.Object)
            {
                rules.SpellLevelNames.Clear();
                foreach (var p in sln.EnumerateObject())
                    if (int.TryParse(p.Name, out var lvl) && p.Value.ValueKind == JsonValueKind.String) rules.SpellLevelNames[lvl] = p.Value.GetString() ?? "";
            }
        }

        private static int ReadInt(JsonElement cs, string key, int def) => cs.TryGetProperty(key, out var v) && v.TryGetInt32(out var iv) ? iv : def;
        private static double ReadDouble(JsonElement cs, string key, double def) => cs.TryGetProperty(key, out var v) && v.TryGetDouble(out var dv) ? dv : def;
        private static bool ReadBool(JsonElement cs, string key, bool def) => cs.TryGetProperty(key, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;
        private static string ReadString(JsonElement cs, string key, string def) => cs.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? def) : def;

        private static List<string> CasterAbilityIds(JsonElement root)
        {
            var list = new List<string>();
            if (!root.TryGetProperty("Spellcasting", out var sc) || sc.ValueKind != JsonValueKind.Object) return list;
            foreach (var p in sc.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                var type = p.Value.TryGetProperty("Type", out var ty) && ty.ValueKind == JsonValueKind.String ? (ty.GetString() ?? "none") : "none";
                if (string.Equals(type, "none", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Value.TryGetProperty("AbilityId", out var ab) && ab.ValueKind == JsonValueKind.String)
                {
                    var id = ab.GetString();
                    if (!string.IsNullOrWhiteSpace(id) && !list.Contains(id!)) list.Add(id!);
                }
            }
            return list;
        }

        private static void ApplyConditionEffects(GameRules rules)
        {
            bool anyDeclared = rules.Conditions.Values.Any(c =>
                !string.IsNullOrEmpty(c.AttackRoll) || !string.IsNullOrEmpty(c.IncomingAttack) || c.BlocksActions || c.StopsMovement);
            if (!anyDeclared) return;

            rules.AttackAdvantageConditions.Clear();
            rules.AttackDisadvantageConditions.Clear();
            rules.TargetAdvantageConditions.Clear();
            rules.TargetDisadvantageConditions.Clear();
            rules.IncapacitatingConditions.Clear();
            rules.MovementStoppingConditions.Clear();
            rules.TargetRangeSplitConditions.Clear();

            foreach (var c in rules.Conditions.Values)
            {
                if (c.AttackRoll == "advantage") rules.AttackAdvantageConditions.Add(c.Name);
                else if (c.AttackRoll == "disadvantage") rules.AttackDisadvantageConditions.Add(c.Name);
                if (c.IncomingAttack == "advantage") rules.TargetAdvantageConditions.Add(c.Name);
                else if (c.IncomingAttack == "disadvantage") rules.TargetDisadvantageConditions.Add(c.Name);
                if (!string.IsNullOrWhiteSpace(c.IncomingAttackBeyond) && c.IncomingAttackWithinFeet > 0)
                    rules.TargetRangeSplitConditions[c.Name] = new RangeSplitRule(c.IncomingAttackWithinFeet, c.IncomingAttackBeyond);
                if (c.BlocksActions) rules.IncapacitatingConditions.Add(c.Name);
                if (c.StopsMovement) rules.MovementStoppingConditions.Add(c.Name);
            }
        }

        private static void ReadBlankMapPresets(JsonElement cs, GameRules rules)
        {
            if (!cs.TryGetProperty("BlankMapPresets", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            var read = new List<BlankMapPreset>();
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                var cols = e.TryGetProperty("Cols", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
                var rows = e.TryGetProperty("Rows", out var r) && r.TryGetInt32(out var rv) ? rv : 0;
                if (string.IsNullOrWhiteSpace(name) || cols <= 0 || rows <= 0) continue;
                read.Add(new BlankMapPreset(name, cols, rows));
            }
            rules.BlankMapPresets.Clear();
            rules.BlankMapPresets.AddRange(read);
        }

        private static void ReadIntSet(JsonElement cs, string key, HashSet<int> target, int scalarFallback)
        {
            target.Clear();
            if (cs.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v)) target.Add(v);
            }
            if (target.Count == 0) target.Add(scalarFallback);
        }

        private static void ReadConditionList(JsonElement cs, string key, HashSet<string> target)
        {
            if (!cs.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            target.Clear();
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                {
                    var s = e.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) target.Add(s);
                }
        }

        public async Task<long> ReadCampaignClockAsync()
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return 0;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ElapsedMinutes FROM Campaigns WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", campaignId);
            var raw = await cmd.ExecuteScalarAsync();
            return raw == null || raw == DBNull.Value ? 0 : Convert.ToInt64(raw);
        }

        public async Task<long> AdvanceCampaignClockAsync(int minutes)
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId) || minutes == 0) return await ReadCampaignClockAsync();
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Campaigns SET ElapsedMinutes = MAX(0, COALESCE(ElapsedMinutes, 0) + $m) WHERE Id = $id";
            cmd.Parameters.AddWithValue("$m", minutes);
            cmd.Parameters.AddWithValue("$id", campaignId);
            await cmd.ExecuteNonQueryAsync();
            return await ReadCampaignClockAsync();
        }

        public async Task SaveCombatSettingsAsync(int baseActions, int baseBonusActions, bool autoFlanking, bool multiclassingAllowed, bool dmIgnoresMovementBudget,
            bool enforceMovementBudget, bool playerRollsOwnSaves)
        {
            _combatBaseActions = baseActions < 0 ? 0 : baseActions;
            _combatBaseBonusActions = baseBonusActions < 0 ? 0 : baseBonusActions;
            _combatAutoFlanking = autoFlanking;
            _combatSettingsLoaded = true;
            Rules.MulticlassingAllowedByDm = multiclassingAllowed;
            Rules.DmIgnoresMovementBudget = dmIgnoresMovementBudget;
            Rules.EnforceMovementBudget = enforceMovementBudget;
            Rules.PlayerRollsOwnSaves = playerRollsOwnSaves;

            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return;

            var json = "{\"BaseActions\":" + _combatBaseActions + ",\"BaseBonusActions\":" + _combatBaseBonusActions + ",\"AutoFlanking\":" + (_combatAutoFlanking ? "true" : "false") + ",\"MulticlassingAllowed\":" + (multiclassingAllowed ? "true" : "false") + ",\"DmIgnoresMovementBudget\":" + (dmIgnoresMovementBudget ? "true" : "false")
                + ",\"EnforceMovementBudget\":" + (enforceMovementBudget ? "true" : "false")
                + ",\"PlayerRollsOwnSaves\":" + (playerRollsOwnSaves ? "true" : "false") + "}";
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Campaigns SET CombatSettingsJson = $j WHERE Id = $id";
            cmd.Parameters.AddWithValue("$j", json);
            cmd.Parameters.AddWithValue("$id", campaignId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<string?> LoadActiveTemplateJsonAsync()
        {
            var tid = GetActiveTemplateId();
            await using var conn = await _dbManager.OpenAsync();
            string? json = null;
            if (!string.IsNullOrEmpty(tid))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", tid);
                json = await cmd.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync() as string;
            }
            return json;
        }

        public async Task<(int AddActions, int AddBonusActions, int SurgeActions, int SurgeBonusActions, int SurgeUses, Dictionary<string, string> CostOverrides)> ResolveActionEconomyAsync(string characterId)
        {
            var none = (0, 0, 0, 0, 0, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(characterId)) return none;

            var runtime = await LoadCharacterByIdAsync(characterId);
            if (runtime == null) return none;

            var ownedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddOwned(string? s)
            {
                if (string.IsNullOrEmpty(s)) return;
                ownedIds.Add(s);
                var idx = s.LastIndexOf(':');
                if (idx >= 0 && idx < s.Length - 1) ownedIds.Add(s[(idx + 1)..].Trim());
            }
            foreach (var f in runtime.Features) AddOwned(f);
            foreach (var kv in runtime.LevelChoices)
                foreach (var v in kv.Value) AddOwned(v);

            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return none;

            int addA = 0, addB = 0, surgeA = 0, surgeB = 0, surgeUses = 0;
            var costOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var bonusTarget = new Dictionary<string, (string Target, int Value, string Grant, string GrantValue)>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("Bonuses", out var barr) && barr.ValueKind == JsonValueKind.Array)
                    foreach (var b in barr.EnumerateArray())
                    {
                        var id = b.TryGetProperty("TemplateId", out var bi) ? bi.GetString() : null;
                        if (string.IsNullOrEmpty(id)) continue;
                        var tgt = b.TryGetProperty("TargetId", out var bt) ? bt.GetString() ?? "" : "";
                        var val = b.TryGetProperty("Value", out var bv) && bv.TryGetInt32(out var vv) ? vv : 0;
                        var grant = b.TryGetProperty("Grant", out var bg) && bg.ValueKind == JsonValueKind.String ? bg.GetString() ?? "" : "";
                        var gval = b.TryGetProperty("GrantValue", out var bgv) && bgv.ValueKind == JsonValueKind.String ? bgv.GetString() ?? "" : "";
                        bonusTarget[id!] = (tgt, val, grant, gval);
                    }

                var subclassOwner = ReadSubclassFeatureOwners(root);
                var classLevels = ClassLevelMap(runtime);
                foreach (var section in new[] { "Features", "Feats", "Traits" })
                {
                    if (!root.TryGetProperty(section, out var sec) || sec.ValueKind != JsonValueKind.Array) continue;
                    foreach (var f in sec.EnumerateArray())
                    {
                        var id = f.TryGetProperty("TemplateId", out var fi) ? fi.GetString() : null;
                        if (string.IsNullOrEmpty(id)) continue;

                        var owned = ownedIds.Contains(id!);
                        if (!owned && string.Equals(section, "Features", StringComparison.Ordinal))
                        {
                            var fcid = f.TryGetProperty("ClassId", out var c) ? c.GetString() : null;
                            var lvl = f.TryGetProperty("Level", out var l) && l.TryGetInt32(out var lv) ? lv : 1;
                            if (subclassOwner.TryGetValue(id!, out var sub))
                                owned = lvl <= LevelInClass(classLevels, sub.ParentClassId, runtime.Level) && (ownedIds.Contains(sub.Id) || ownedIds.Contains(sub.Name));
                            else if (!string.IsNullOrEmpty(fcid) && classLevels.TryGetValue(fcid!, out var inClass) && lvl <= inClass)
                                owned = true;
                        }
                        if (!owned) continue;

                        if (f.TryGetProperty("BonusIds", out var bids) && bids.ValueKind == JsonValueKind.Array)
                            foreach (var bidEl in bids.EnumerateArray())
                            {
                                var bid = bidEl.GetString();
                                if (string.IsNullOrEmpty(bid) || !bonusTarget.TryGetValue(bid!, out var info)) continue;
                                if (string.Equals(info.Target, "action", StringComparison.OrdinalIgnoreCase)) addA += info.Value;
                                else if (string.Equals(info.Target, "bonus-action", StringComparison.OrdinalIgnoreCase)) addB += info.Value;
                            }

                        if (f.TryGetProperty("ActionEconomy", out var ae) && ae.ValueKind == JsonValueKind.Object)
                        {
                            var activated = ae.TryGetProperty("Activated", out var av) && av.ValueKind == JsonValueKind.True;
                            var grantA = ae.TryGetProperty("GrantActions", out var ga) && ga.TryGetInt32(out var gav) ? gav : 0;
                            var grantB = ae.TryGetProperty("GrantBonusActions", out var gb) && gb.TryGetInt32(out var gbv) ? gbv : 0;
                            var uses = ae.TryGetProperty("Uses", out var u) && u.TryGetInt32(out var uv) ? uv : 0;
                            if (ae.TryGetProperty("CostOverrides", out var co) && co.ValueKind == JsonValueKind.Object)
                                foreach (var ov in co.EnumerateObject())
                                    if (ov.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ov.Value.GetString()))
                                        costOverrides[ov.Name] = ov.Value.GetString()!;
                            if (activated)
                            {
                                surgeA += grantA;
                                surgeB += grantB;
                                surgeUses += uses > 0 ? uses : 1;
                            }
                            else
                            {
                                addA += grantA;
                                addB += grantB;
                            }
                        }
                    }
                }
            }
            catch (JsonException) { }

            return (addA, addB, surgeA, surgeB, surgeUses, costOverrides);
        }

        public async Task<CharacterBonuses> ResolveCharacterBonusesAsync(string characterId)
        {
            var result = new CharacterBonuses();
            if (string.IsNullOrEmpty(characterId)) return result;

            var runtime = await LoadCharacterByIdAsync(characterId);
            if (runtime == null) return result;

            var ownedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddOwned(string? s)
            {
                if (string.IsNullOrEmpty(s)) return;
                ownedIds.Add(s);
                var idx = s.LastIndexOf(':');
                if (idx >= 0 && idx < s.Length - 1) ownedIds.Add(s[(idx + 1)..].Trim());
            }
            foreach (var f in runtime.Features) AddOwned(f);
            foreach (var kv in runtime.LevelChoices)
                foreach (var v in kv.Value) AddOwned(v);

            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var bonusTarget = new Dictionary<string, (string Target, int Value, string Grant, string GrantValue)>(StringComparer.OrdinalIgnoreCase);
                var bonusRider = new Dictionary<string, RiderRule>(StringComparer.OrdinalIgnoreCase);
                var bonusConditional = new Dictionary<string, ConditionalBonus>(StringComparer.OrdinalIgnoreCase);
                var bonusReaction = new Dictionary<string, GrantedReaction>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("Bonuses", out var barr) && barr.ValueKind == JsonValueKind.Array)
                    foreach (var b in barr.EnumerateArray())
                    {
                        var id = b.TryGetProperty("TemplateId", out var bi) ? bi.GetString() : null;
                        if (string.IsNullOrEmpty(id)) continue;
                        var tgt = b.TryGetProperty("TargetId", out var bt) ? bt.GetString() ?? "" : "";
                        var val = b.TryGetProperty("Value", out var bv) && bv.TryGetInt32(out var vv) ? vv : 0;
                        var grant = b.TryGetProperty("Grant", out var bg) && bg.ValueKind == JsonValueKind.String ? bg.GetString() ?? "" : "";
                        var gval = b.TryGetProperty("GrantValue", out var bgv) && bgv.ValueKind == JsonValueKind.String ? bgv.GetString() ?? "" : "";
                        bonusTarget[id!] = (tgt, val, grant, gval);
                        if (b.TryGetProperty("Rider", out var rid) && rid.ValueKind == JsonValueKind.Object) bonusRider[id!] = ReadRider(id!, rid);
                        if (b.TryGetProperty("When", out var wh) && wh.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tgt))
                            bonusConditional[id!] = new ConditionalBonus
                            {
                                Id = id!,
                                Name = b.TryGetProperty("Name", out var bn) && bn.ValueKind == JsonValueKind.String ? bn.GetString() ?? id! : id!,
                                Target = tgt,
                                Value = val,
                                When = (wh.GetString() ?? "always").ToLowerInvariant(),
                                WhenValue = b.TryGetProperty("WhenValue", out var wv) && wv.ValueKind == JsonValueKind.String ? wv.GetString() ?? "" : "",
                                HpFraction = b.TryGetProperty("HpFraction", out var hf) && hf.TryGetDouble(out var hfv) ? hfv : 0
                            };
                        if (b.TryGetProperty("Reaction", out var rx) && rx.ValueKind == JsonValueKind.Object)
                            bonusReaction[id!] = new GrantedReaction
                            {
                                Id = id!,
                                Name = rx.TryGetProperty("Name", out var rn) && rn.ValueKind == JsonValueKind.String ? rn.GetString() ?? id! : id!,
                                Description = rx.TryGetProperty("Description", out var rd) && rd.ValueKind == JsonValueKind.String ? rd.GetString() ?? "" : "",
                                Cost = rx.TryGetProperty("Cost", out var rc) && rc.ValueKind == JsonValueKind.String ? rc.GetString() ?? "reaction" : "reaction",
                                UsesPer = rx.TryGetProperty("UsesPer", out var rup) && rup.ValueKind == JsonValueKind.String ? rup.GetString() ?? "" : "",
                                Uses = rx.TryGetProperty("Uses", out var ru) && ru.TryGetInt32(out var ruv) ? ruv : 0
                            };
                    }

                var subclassOwner = ReadSubclassFeatureOwners(root);
                var classLevels = ClassLevelMap(runtime);
                foreach (var section in new[] { "Features", "Feats", "Traits" })
                {
                    if (!root.TryGetProperty(section, out var sec) || sec.ValueKind != JsonValueKind.Array) continue;
                    foreach (var f in sec.EnumerateArray())
                    {
                        var id = f.TryGetProperty("TemplateId", out var fi) ? fi.GetString() : null;
                        if (string.IsNullOrEmpty(id)) continue;

                        var owned = ownedIds.Contains(id!);
                        if (!owned && string.Equals(section, "Features", StringComparison.Ordinal))
                        {
                            var fcid = f.TryGetProperty("ClassId", out var c) ? c.GetString() : null;
                            var lvl = f.TryGetProperty("Level", out var l) && l.TryGetInt32(out var lv) ? lv : 1;
                            if (subclassOwner.TryGetValue(id!, out var sub))
                                owned = lvl <= LevelInClass(classLevels, sub.ParentClassId, runtime.Level) && (ownedIds.Contains(sub.Id) || ownedIds.Contains(sub.Name));
                            else if (!string.IsNullOrEmpty(fcid) && classLevels.TryGetValue(fcid!, out var inClass) && lvl <= inClass)
                                owned = true;
                        }
                        if (!owned) continue;

                        if (f.TryGetProperty("BonusIds", out var bids) && bids.ValueKind == JsonValueKind.Array)
                            foreach (var bidEl in bids.EnumerateArray())
                            {
                                var bid = bidEl.ValueKind == JsonValueKind.String
                                    ? bidEl.GetString()
                                    : bidEl.ValueKind == JsonValueKind.Object && bidEl.TryGetProperty("BonusId", out var boi) ? boi.GetString() : null;
                                if (string.IsNullOrEmpty(bid)) continue;
                                if (bonusRider.TryGetValue(bid!, out var rider)) { if (!result.Riders.Any(r => r.Id == rider.Id)) result.Riders.Add(rider); continue; }
                                if (bonusReaction.TryGetValue(bid!, out var reaction)) { if (!result.Reactions.Any(r => r.Id == reaction.Id)) result.Reactions.Add(reaction); continue; }
                                if (bonusConditional.TryGetValue(bid!, out var conditional)) { if (!result.Conditional.Any(r => r.Id == conditional.Id)) result.Conditional.Add(conditional); continue; }
                                if (!bonusTarget.TryGetValue(bid!, out var info)) continue;
                                if (!string.IsNullOrWhiteSpace(info.Grant)) AccumulateGrant(result, info.Grant, info.GrantValue, info.Value);
                                else AccumulateBonus(result, info.Target, info.Value);
                            }
                    }
                }
            }
            catch (JsonException) { }

            return result;
        }

        internal static HashSet<string> ReadBonusIdSet(JsonElement root)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("Bonuses", out var arr) || arr.ValueKind != JsonValueKind.Array) return ids;
            foreach (var b in arr.EnumerateArray())
                if (b.TryGetProperty("TemplateId", out var bi) && bi.ValueKind == JsonValueKind.String)
                {
                    var id = bi.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!);
                }
            return ids;
        }

        internal static bool EntryIsEngineFired(JsonElement entry, HashSet<string> bonusIds)
        {
            if (entry.TryGetProperty("ActionEconomy", out var ae) && ae.ValueKind == JsonValueKind.Object) return true;
            if (!entry.TryGetProperty("BonusIds", out var bids) || bids.ValueKind != JsonValueKind.Array) return false;
            foreach (var el in bids.EnumerateArray())
            {
                var bid = el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : el.ValueKind == JsonValueKind.Object && el.TryGetProperty("BonusId", out var boi) ? boi.GetString() : null;
                if (!string.IsNullOrWhiteSpace(bid) && bonusIds.Contains(bid!)) return true;
            }
            return false;
        }

        public async Task<HashSet<string>> ReadEngineFiredFeatIdsAsync()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return ids;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var bonusIds = ReadBonusIdSet(doc.RootElement);
                if (doc.RootElement.TryGetProperty("Feats", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var f in arr.EnumerateArray())
                    {
                        var id = f.TryGetProperty("TemplateId", out var fi) ? fi.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(id) && EntryIsEngineFired(f, bonusIds)) ids.Add(id!);
                    }
            }
            catch (JsonException) { }
            return ids;
        }

        internal static Dictionary<string, (string Id, string Name, string ParentClassId)> ReadSubclassFeatureOwners(JsonElement root)
        {
            var map = new Dictionary<string, (string Id, string Name, string ParentClassId)>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("Subclasses", out var arr) || arr.ValueKind != JsonValueKind.Array) return map;
            foreach (var s in arr.EnumerateArray())
            {
                var sid = s.TryGetProperty("TemplateId", out var si) ? si.GetString() : null;
                if (string.IsNullOrEmpty(sid) || !s.TryGetProperty("FeatsIds", out var fids) || fids.ValueKind != JsonValueKind.Array) continue;
                var name = s.TryGetProperty("Name", out var sn) ? sn.GetString() ?? "" : "";
                var parent = s.TryGetProperty("ParentClassId", out var pc) && pc.ValueKind == JsonValueKind.String ? pc.GetString() ?? "" : "";
                foreach (var fe in fids.EnumerateArray())
                {
                    var fid = fe.GetString();
                    if (!string.IsNullOrEmpty(fid)) map[fid!] = (sid!, name, parent);
                }
            }
            return map;
        }

        internal static Dictionary<string, int> ClassLevelMap(CharacterRuntime runtime)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cl in runtime.ClassLevels)
                if (!string.IsNullOrEmpty(cl.ClassId)) map[cl.ClassId] = cl.Level;
            if (map.Count == 0 && !string.IsNullOrEmpty(runtime.ClassId)) map[runtime.ClassId!] = runtime.Level;
            return map;
        }

        private static int LevelInClass(Dictionary<string, int> classLevels, string classId, int fallback) =>
            !string.IsNullOrEmpty(classId) && classLevels.TryGetValue(classId, out var lvl) ? lvl : fallback;

        internal static RiderRule ReadRider(string id, JsonElement rid)
        {
            string Str(string key) => rid.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            int Num(string key) => rid.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
            var when = Str("When");
            return new RiderRule
            {
                Id = id,
                Name = Str("Name"),
                Dice = Str("Dice"),
                DicePerLevels = Num("DicePerLevels"),
                When = string.IsNullOrWhiteSpace(when) ? "always" : when,
                DamageType = Str("DamageType"),
                OncePerTurn = rid.TryGetProperty("OncePerTurn", out var op) && op.ValueKind == JsonValueKind.True
            };
        }

        // A template picks one of these rather than pullin one from nowhere, same as the mastery Effect keys.
        internal static void AccumulateGrant(CharacterBonuses b, string grant, string grantValue, int value)
        {
            switch ((grant ?? "").ToLowerInvariant())
            {
                case "extra-attack": b.ExtraAttacks += value > 0 ? value : 1; break;
                case "resistance": if (!string.IsNullOrWhiteSpace(grantValue) && !b.Resistances.Contains(grantValue)) b.Resistances.Add(grantValue); break;
                case "advantage": if (!string.IsNullOrWhiteSpace(grantValue)) b.AdvantageOn.Add(grantValue); break;
                case "proficiency": if (!string.IsNullOrWhiteSpace(grantValue) && !b.Proficiencies.Contains(grantValue)) b.Proficiencies.Add(grantValue); break;
                case "offhand-ability-mod": b.OffHandAbilityMod = true; break;
            }
        }

        private static void AccumulateBonus(CharacterBonuses b, string target, int value)
        {
            var lower = (target ?? "").ToLowerInvariant();
            switch (lower)
            {
                case "armor-class": b.ArmorClass += value; break;
                case "attack-roll": b.AttackRoll += value; break;
                case "damage-roll": b.DamageRoll += value; break;
                case "saving-throw": b.SavingThrow += value; break;
                case "initiative": b.Initiative += value; break;
                case "max-hp-per-level": b.MaxHpPerLevel += value; break;
                default: b.Ability[lower] = b.AbilityBonus(lower) + value; break;
            }
        }

        public async Task<List<ClassLevel>> LoadClassLevelsAsync(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return new List<ClassLevel>();
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ClassLevelsJson, ClassId, Level FROM Characters WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", characterId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return new List<ClassLevel>();
            var json = r.IsDBNull(0) ? null : r.GetString(0);
            var classId = r.IsDBNull(1) ? null : r.GetString(1);
            return ClassLevels.Read(json, classId, r.GetInt32(2));
        }

        // ClassId and Level stay authoritative for every query that never heard of multiclassing, so they are written back in step
        public async Task SaveClassLevelsAsync(string characterId, IReadOnlyList<ClassLevel> classes)
        {
            if (string.IsNullOrEmpty(characterId) || classes.Count == 0) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Characters SET ClassLevelsJson = $j, ClassId = $cid, Level = $lvl WHERE Id = $id";
            cmd.Parameters.AddWithValue("$j", ClassLevels.Write(classes));
            cmd.Parameters.AddWithValue("$cid", (object?)ClassLevels.PrimaryClassId(classes) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lvl", ClassLevels.TotalLevel(classes));
            cmd.Parameters.AddWithValue("$id", characterId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Everything a level up screen needs to ask before it offers a class, without knowing anything about the template.
        public string MulticlassBlockerFor(string? classId, CharacterRuntime runtime)
        {
            if (!Rules.MulticlassingOn) return "Multiclassing is off in this campaign.";
            return Rules.MulticlassPrerequisiteFor(classId, sht => runtime.AbilityScores.Get(Rules.AbilityIdForShort(sht)));
        }

        public async Task<int> ResolveCasterLevelAsync(IEnumerable<ClassLevel> classes)
        {
            var pairs = new List<(string, int)>();
            foreach (var c in classes)
                pairs.Add((await ResolveCasterTypeAsync(c.ClassId), c.Level));
            return Rules.MulticlassCasterLevel(pairs);
        }

        public async Task<string> ResolveCasterTypeAsync(string? classId)
        {
            if (string.IsNullOrEmpty(classId)) return "none";
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return "none";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Spellcasting", out var sc) && sc.ValueKind == JsonValueKind.Object
                    && sc.TryGetProperty(classId!, out var entry) && entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("Type", out var ty) && ty.ValueKind == JsonValueKind.String)
                    return ty.GetString() ?? "none";
            }
            catch (JsonException) { }
            return "none";
        }

        public async Task<Dictionary<int, int>> ResolveSpellSlotsAsync(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return new Dictionary<int, int>();
            var runtime = await LoadCharacterByIdAsync(characterId);
            return runtime == null ? new Dictionary<int, int>() : await ResolveSpellSlotsAsync(runtime);
        }

        // A single class caster reads its own table at its own level, because a paladin 5 is not a caster level 2 wizard. Only a split falls back to the shared multiclass table
        public async Task<Dictionary<int, int>> ResolveSpellSlotsAsync(CharacterRuntime runtime)
        {
            if (runtime.ClassLevels.Count > 1)
            {
                var shared = await ResolveMulticlassSlotsAsync(runtime.ClassLevels);
                foreach (var kv in await ResolvePactSlotsAsync(runtime.ClassLevels))
                    shared[kv.Key] = (shared.TryGetValue(kv.Key, out var cur) ? cur : 0) + kv.Value;
                return shared;
            }

            var result = new Dictionary<int, int>();
            var classId = runtime.ClassId;
            var level = runtime.Level;
            if (string.IsNullOrEmpty(classId) || level < 1) return result;

            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string casterType = "none";
                if (root.TryGetProperty("Spellcasting", out var sc) && sc.ValueKind == JsonValueKind.Object
                    && sc.TryGetProperty(classId!, out var entry) && entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("Type", out var ty) && ty.ValueKind == JsonValueKind.String)
                    casterType = ty.GetString() ?? "none";
                if (string.Equals(casterType, "none", StringComparison.OrdinalIgnoreCase)) return result;

                if (!root.TryGetProperty("SpellSlotTables", out var tables) || tables.ValueKind != JsonValueKind.Object) return result;
                if (!tables.TryGetProperty(casterType, out var table) || table.ValueKind != JsonValueKind.Array) return result;

                var rows = table.EnumerateArray().ToList();
                var idx = level - 1;
                if (idx < 0 || idx >= rows.Count) return result;
                var row = rows[idx];
                if (row.ValueKind != JsonValueKind.Array) return result;

                var slotLevel = 0;
                foreach (var cell in row.EnumerateArray())
                {
                    slotLevel++;
                    if (cell.TryGetInt32(out var count) && count > 0) result[slotLevel] = count;
                }
            }
            catch (JsonException) { }

            return result;
        }

        // Off the full caster table at the combined level. Pact slots never pool so they are not in here.
        public async Task<Dictionary<int, int>> ResolveMulticlassSlotsAsync(IReadOnlyList<ClassLevel> classes)
        {
            var casterLevel = await ResolveCasterLevelAsync(classes);
            return casterLevel < 1 ? new Dictionary<int, int>() : await ReadSlotRowAsync(Rules.MulticlassSharedSlotTable, casterLevel);
        }

        public async Task<Dictionary<int, int>> ResolvePactSlotsAsync(IReadOnlyList<ClassLevel> classes)
        {
            var pactLevels = 0;
            foreach (var c in classes)
                if (Rules.IsPactCaster(await ResolveCasterTypeAsync(c.ClassId))) pactLevels += c.Level;
            return pactLevels < 1 ? new Dictionary<int, int>() : await ReadSlotRowAsync(Rules.PactSlotTable, pactLevels);
        }

        private async Task<Dictionary<int, int>> ReadSlotRowAsync(string tableName, int level)
        {
            var result = new Dictionary<int, int>();
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return result;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("SpellSlotTables", out var tables) || tables.ValueKind != JsonValueKind.Object) return result;
                if (!tables.TryGetProperty(tableName, out var table) || table.ValueKind != JsonValueKind.Array) return result;

                var rows = table.EnumerateArray().ToList();
                var idx = level - 1;
                if (idx < 0 || idx >= rows.Count) return result;
                var row = rows[idx];
                if (row.ValueKind != JsonValueKind.Array) return result;

                var slotLevel = 0;
                foreach (var cell in row.EnumerateArray())
                {
                    slotLevel++;
                    if (cell.TryGetInt32(out var count) && count > 0) result[slotLevel] = count;
                }
            }
            catch (JsonException) { }
            return result;
        }

        // Every class contributes its own hit die. Barbarian 3 wizard 2 carries three d12s and two d6s.
        public async Task<List<(int Die, int Count)>> ResolveHitDiceAsync(CharacterRuntime runtime)
        {
            var result = new List<(int, int)>();
            foreach (var c in runtime.ClassLevels)
            {
                var die = await ResolveHitDieForClassAsync(c.ClassId);
                if (die <= 0) die = Rules.DefaultHitDie;
                result.Add((die, c.Level * Rules.HitDicePerLevel));
            }
            return result;
        }

        // Book says the player picks. Here the class with the most levels wins, ties go to the bigger die.
        public async Task<int> ResolveShortRestHitDieAsync(CharacterRuntime runtime)
        {
            var best = 0;
            var bestLevels = 0;
            foreach (var c in runtime.ClassLevels)
            {
                var die = await ResolveHitDieForClassAsync(c.ClassId);
                if (die <= 0) die = Rules.DefaultHitDie;
                if (c.Level > bestLevels || (c.Level == bestLevels && die > best)) { best = die; bestLevels = c.Level; }
            }
            return best > 0 ? best : Rules.DefaultHitDie;
        }

        public async Task<List<ClassResourceDef>> ResolveClassResourcesAsync(CharacterRuntime runtime)
        {
            var result = new List<ClassResourceDef>();
            var classLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cl in runtime.ClassLevels) classLevels[cl.ClassId] = cl.Level;
            if (classLevels.Count == 0 && !string.IsNullOrEmpty(runtime.ClassId)) classLevels[runtime.ClassId!] = runtime.Level;
            if (classLevels.Count == 0) return result;

            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return result;

            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddOwned(string? s)
            {
                if (string.IsNullOrEmpty(s)) return;
                owned.Add(s!);
                var idx = s!.LastIndexOf(':');
                if (idx >= 0 && idx < s.Length - 1) owned.Add(s[(idx + 1)..].Trim());
            }
            foreach (var f in runtime.Features) AddOwned(f);
            foreach (var kv in runtime.LevelChoices)
                foreach (var v in kv.Value) AddOwned(v);

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("ClassResources", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;

                foreach (var e in arr.EnumerateArray())
                {
                    var resClassId = e.TryGetProperty("ClassId", out var cid) ? cid.GetString() : null;
                    var forClass = !string.IsNullOrEmpty(resClassId) && classLevels.ContainsKey(resClassId!);
                    var forFeat = e.TryGetProperty("FeatId", out var fid) && owned.Contains(fid.GetString() ?? "");
                    if (!forClass && !forFeat) continue;
                    var level = forClass ? classLevels[resClassId!] : runtime.Level;

                    int max = 0;
                    if (e.TryGetProperty("MaxByLevel", out var mbl) && mbl.ValueKind == JsonValueKind.Array)
                    {
                        var rows = mbl.EnumerateArray().ToList();
                        var idx = level - 1;
                        if (idx >= 0 && idx < rows.Count && rows[idx].TryGetInt32(out var m)) max = m;
                    }
                    else if (e.TryGetProperty("MaxAbility", out var ab) && ab.ValueKind == JsonValueKind.String)
                    {
                        max = Math.Max(1, AbilityModFor(runtime, ab.GetString() ?? ""));
                    }
                    else if (e.TryGetProperty("Max", out var mx) && mx.TryGetInt32(out var fixedMax))
                    {
                        max = fixedMax;
                    }
                    if (max <= 0) continue;

                    result.Add(new ClassResourceDef
                    {
                        Id = e.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "",
                        Name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                        ResetOn = e.TryGetProperty("ResetOn", out var ro) ? ro.GetString() ?? "long" : "long",
                        Max = max
                    });
                }
            }
            catch (JsonException) { }

            return result;
        }

        private static List<ChoiceOption> SpellsBySchool(JsonElement choice, List<(string Id, string Name, int Level, string School)> spells)
        {
            var schools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (choice.TryGetProperty("Schools", out var sc) && sc.ValueKind == JsonValueKind.Array)
                foreach (var s in sc.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String) schools.Add(s.GetString()!);

            var maxLevel = choice.TryGetProperty("MaxLevel", out var ml) && ml.TryGetInt32(out var mlv) ? mlv : 9;
            var minLevel = choice.TryGetProperty("MinLevel", out var nl) && nl.TryGetInt32(out var nlv) ? nlv : 0;

            return spells
                .Where(s => s.Level >= minLevel && s.Level <= maxLevel)
                .Where(s => schools.Count == 0 || schools.Contains(s.School))
                .Select(s => new ChoiceOption(s.Id, s.Name))
                .ToList();
        }

        private static List<ChoiceOption> ExplicitOptions(JsonElement choice, List<(string Id, string Name, int Level, string School)> spells)
        {
            if (!choice.TryGetProperty("OptionIds", out var ids) || ids.ValueKind != JsonValueKind.Array) return new List<ChoiceOption>();
            var wanted = new List<string>();
            foreach (var i in ids.EnumerateArray())
                if (i.ValueKind == JsonValueKind.String) wanted.Add(i.GetString()!);

            var byId = spells.ToDictionary(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase);
            return wanted.Select(id => new ChoiceOption(id, byId.TryGetValue(id, out var n) ? n : id)).ToList();
        }

        public async Task<List<ResolvedClassChoice>> ResolveFeatChoicesAsync(CharacterRuntime runtime)
        {
            var result = new List<ResolvedClassChoice>();
            if (runtime == null) return result;

            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddOwned(string? s)
            {
                if (string.IsNullOrEmpty(s)) return;
                owned.Add(s!);
                var idx = s!.LastIndexOf(':');
                if (idx >= 0 && idx < s.Length - 1) owned.Add(s[(idx + 1)..].Trim());
            }
            foreach (var f in runtime.Features) AddOwned(f);
            foreach (var kv in runtime.LevelChoices)
                foreach (var v in kv.Value) AddOwned(v);

            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return result;

            var skillOptions = Rules?.Skills == null
                ? new List<ChoiceOption>()
                : Rules.Skills.Select(s => new ChoiceOption(s.Id, s.Name)).ToList();

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var toolOptions = new List<ChoiceOption>();
                if (root.TryGetProperty("Proficiencies", out var profs) && profs.ValueKind == JsonValueKind.Array)
                    foreach (var p in profs.EnumerateArray())
                        if (p.TryGetProperty("Type", out var pt) && string.Equals(pt.GetString(), "Tool", StringComparison.OrdinalIgnoreCase))
                            toolOptions.Add(new ChoiceOption(
                                p.TryGetProperty("TemplateId", out var pid) ? pid.GetString() ?? "" : "",
                                p.TryGetProperty("Name", out var pn) ? pn.GetString() ?? "" : ""));

                var asiPrefix = Rules?.AsiTokenPrefix ?? "asi:";
                var abilityOptions = new List<ChoiceOption>();
                foreach (var a in Rules?.Abilities ?? new List<AbilityDef>())
                    if (!string.IsNullOrWhiteSpace(a.Short))
                        abilityOptions.Add(new ChoiceOption(asiPrefix + a.Short, a.Name));

                var damageTypeOptions = new List<ChoiceOption>();
                if (root.TryGetProperty("DamageTypes", out var dts) && dts.ValueKind == JsonValueKind.Array)
                    foreach (var d in dts.EnumerateArray())
                        damageTypeOptions.Add(new ChoiceOption(
                            d.TryGetProperty("TemplateId", out var di) ? di.GetString() ?? "" : "",
                            d.TryGetProperty("Name", out var dn) ? dn.GetString() ?? "" : ""));

                var weaponOptions = new List<ChoiceOption>();
                if (root.TryGetProperty("Items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    foreach (var it in itemsEl.EnumerateArray())
                        if (it.TryGetProperty("$type", out var ity) && string.Equals(ity.GetString(), "Weapon", StringComparison.OrdinalIgnoreCase))
                            weaponOptions.Add(new ChoiceOption(
                                it.TryGetProperty("TemplateId", out var ii) ? ii.GetString() ?? "" : "",
                                it.TryGetProperty("Name", out var inm) ? inm.GetString() ?? "" : ""));

                var featOptions = new List<ChoiceOption>();
                if (root.TryGetProperty("Feats", out var featsEl) && featsEl.ValueKind == JsonValueKind.Array)
                    foreach (var ft in featsEl.EnumerateArray())
                        featOptions.Add(new ChoiceOption(
                            ft.TryGetProperty("TemplateId", out var fi) ? fi.GetString() ?? "" : "",
                            ft.TryGetProperty("Name", out var fn) ? fn.GetString() ?? "" : ""));

                var languageOptions = new List<ChoiceOption>();
                if (root.TryGetProperty("Languages", out var langs) && langs.ValueKind == JsonValueKind.Array)
                    foreach (var l in langs.EnumerateArray())
                        languageOptions.Add(new ChoiceOption(
                            l.TryGetProperty("TemplateId", out var lid) ? lid.GetString() ?? "" : "",
                            l.TryGetProperty("Name", out var ln) ? ln.GetString() ?? "" : ""));

                var miClasses = new HashSet<string>(Rules?.FeatSpellListClassIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var miSpellIds = new HashSet<string>();
                if (root.TryGetProperty("Classes", out var classesEl) && classesEl.ValueKind == JsonValueKind.Array)
                    foreach (var c in classesEl.EnumerateArray())
                        if (c.TryGetProperty("TemplateId", out var ccid) && miClasses.Contains(ccid.GetString() ?? "")
                            && c.TryGetProperty("SpellListIds", out var csl) && csl.ValueKind == JsonValueKind.Array)
                            foreach (var s in csl.EnumerateArray())
                                if (s.ValueKind == JsonValueKind.String) miSpellIds.Add(s.GetString()!);

                var cantripOptions = new List<ChoiceOption>();
                var level1Options = new List<ChoiceOption>();
                if (miSpellIds.Count > 0 && root.TryGetProperty("Spells", out var spellsEl) && spellsEl.ValueKind == JsonValueKind.Array)
                    foreach (var sp in spellsEl.EnumerateArray())
                    {
                        if (!sp.TryGetProperty("TemplateId", out var sid) || sid.ValueKind != JsonValueKind.String) continue;
                        var sidv = sid.GetString()!;
                        if (!miSpellIds.Contains(sidv)) continue;
                        var lvl = sp.TryGetProperty("Level", out var lv) && lv.TryGetInt32(out var lvi) ? lvi : 0;
                        var sname = sp.TryGetProperty("Name", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString()! : sidv;
                        if (lvl == 0) cantripOptions.Add(new ChoiceOption(sidv, sname));
                        else if (lvl == 1) level1Options.Add(new ChoiceOption(sidv, sname));
                    }

                var spellByLevelAndSchool = new List<(string Id, string Name, int Level, string School)>();
                if (root.TryGetProperty("Spells", out var allSpells) && allSpells.ValueKind == JsonValueKind.Array)
                    foreach (var sp in allSpells.EnumerateArray())
                    {
                        var sid = sp.TryGetProperty("TemplateId", out var si) && si.ValueKind == JsonValueKind.String ? si.GetString()! : null;
                        if (string.IsNullOrEmpty(sid)) continue;
                        spellByLevelAndSchool.Add((
                            sid!,
                            sp.TryGetProperty("Name", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString()! : sid!,
                            sp.TryGetProperty("Level", out var sl) && sl.TryGetInt32(out var sli) ? sli : 0,
                            sp.TryGetProperty("School", out var ss) && ss.ValueKind == JsonValueKind.String ? ss.GetString()! : ""));
                    }

                var owners = new List<JsonElement>();
                foreach (var section in new[] { "Feats", "Features" })
                    if (root.TryGetProperty(section, out var sec) && sec.ValueKind == JsonValueKind.Array)
                        owners.AddRange(sec.EnumerateArray());
                if (owners.Count == 0) return result;

                foreach (var f in owners)
                {
                    var fid = f.TryGetProperty("TemplateId", out var ti) ? ti.GetString() : null;
                    var fname = f.TryGetProperty("Name", out var nm) ? nm.GetString() : null;
                    var isOwned = (!string.IsNullOrEmpty(fid) && owned.Contains(fid!)) || (!string.IsNullOrEmpty(fname) && owned.Contains(fname!));
                    if (!isOwned) continue;
                    if (!f.TryGetProperty("Choices", out var choices) || choices.ValueKind != JsonValueKind.Array) continue;

                    foreach (var ch in choices.EnumerateArray())
                    {
                        var cid = ch.TryGetProperty("Id", out var ci) ? ci.GetString() : null;
                        if (string.IsNullOrEmpty(cid) || runtime.LevelChoices.ContainsKey(cid!)) continue;

                        var optType = (ch.TryGetProperty("OptionType", out var ot) ? ot.GetString() ?? "skill" : "skill").ToLowerInvariant();
                        var opts = optType switch
                        {
                            "tool" => toolOptions,
                            "skill-or-tool" => skillOptions.Concat(toolOptions).ToList(),
                            "cantrip" => cantripOptions,
                            "spell1" => level1Options,
                            "language" => languageOptions,
                            "spell-school" => SpellsBySchool(ch, spellByLevelAndSchool),
                            "spell-ids" => ExplicitOptions(ch, spellByLevelAndSchool),
                            "ability" => abilityOptions,
                            "damage-type" => damageTypeOptions,
                            "weapon" => weaponOptions,
                            "feat" => featOptions,
                            _ => skillOptions
                        };
                        if (opts.Count == 0) continue;

                        result.Add(new ResolvedClassChoice
                        {
                            Id = cid!,
                            Kind = "featProficiency",
                            StoreAs = cid!,
                            ChooseCount = ch.TryGetProperty("ChooseCount", out var cc) && cc.TryGetInt32(out var n) ? n : 1,
                            Label = ch.TryGetProperty("Label", out var lb) ? lb.GetString() ?? "" : "",
                            Description = f.TryGetProperty("Description", out var de) ? de.GetString() ?? "" : "",
                            Options = opts
                        });
                    }
                }
            }
            catch (JsonException) { }

            return result;
        }

        public async Task<(int Cantrips, int Prepared)> ResolveSpellPrepLimitsAsync(CharacterRuntime runtime)
        {
            var classId = runtime.ClassId;
            var level = runtime.Level;
            if (string.IsNullOrEmpty(classId) || level < 1) return (0, 0);

            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return (0, 0);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("SpellPreparation", out var sp) || sp.ValueKind != JsonValueKind.Object) return (0, 0);
                if (!sp.TryGetProperty(classId!, out var entry) || entry.ValueKind != JsonValueKind.Object) return (0, 0);
                return (ReadLevelCell(entry, "Cantrips", level), ReadLevelCell(entry, "Prepared", level));
            }
            catch (JsonException) { return (0, 0); }
        }

        private static int ReadLevelCell(JsonElement entry, string prop, int level)
        {
            if (!entry.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return 0;
            var cells = arr.EnumerateArray().ToList();
            if (cells.Count == 0) return 0;
            var idx = Math.Clamp(level - 1, 0, cells.Count - 1);
            return cells[idx].TryGetInt32(out var v) ? v : 0;
        }

        public async Task<(int SaveDc, int AttackBonus, int AbilityMod, int Level)?> ResolveSpellcastingAsync(CharacterRuntime runtime)
        {
            var classId = runtime.ClassId;
            var json = await LoadActiveTemplateJsonAsync();
            if (!string.IsNullOrEmpty(classId) && !string.IsNullOrEmpty(json))
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Spellcasting", out var sc) && sc.ValueKind == JsonValueKind.Object
                        && sc.TryGetProperty(classId!, out var entry) && entry.ValueKind == JsonValueKind.Object
                        && !(entry.TryGetProperty("Type", out var ty) && ty.ValueKind == JsonValueKind.String && string.Equals(ty.GetString(), "none", StringComparison.OrdinalIgnoreCase))
                        && entry.TryGetProperty("AbilityId", out var ab) && ab.ValueKind == JsonValueKind.String)
                    {
                        var mod = AbilityModFor(runtime, ab.GetString() ?? "");
                        var prof = Rules.RankBonus(GameRules.RankIdFor(true), ProficiencyBonusForLevel(runtime.Level));
                        return (Rules.SpellSaveDcBase + prof + mod, prof + mod, mod, runtime.Level);
                    }
                }
                catch (JsonException) { }
            }

            if (runtime.GrantedSpellIds != null && runtime.GrantedSpellIds.Count > 0)
            {
                var best = Rules.CasterAbilityIds.Select(id => AbilityModFor(runtime, id)).DefaultIfEmpty(0).Max();
                var prof = Rules.RankBonus(GameRules.RankIdFor(true), ProficiencyBonusForLevel(runtime.Level));
                return (Rules.SpellSaveDcBase + prof + best, prof + best, best, runtime.Level);
            }

            return null;
        }

        private static string FallbackAbbrev(string abilityId) => abilityId switch
        {
            "ability-str" => "STR", "ability-dex" => "DEX", "ability-con" => "CON",
            "ability-int" => "INT", "ability-wis" => "WIS", "ability-cha" => "CHA",
            _ => abilityId.StartsWith("ability-", StringComparison.OrdinalIgnoreCase) ? abilityId.Substring("ability-".Length) : abilityId
        };

        private int AbilityModFor(CharacterRuntime r, string abilityId) => Rules.Modifier(r.AbilityScores.Get(abilityId));

        public async Task<List<CastableSpell>> LoadCastableSpellsAsync(CharacterRuntime runtime)
        {
            var list = new List<CastableSpell>();
            var classId = runtime.ClassId;
            if (string.IsNullOrEmpty(classId)) return list;
            var myClasses = runtime.ClassLevels.Count > 0
                ? new HashSet<string>(runtime.ClassLevels.Select(c => c.ClassId), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { classId! };
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return list;

            var slots = await ResolveSpellSlotsAsync(runtime);
            var maxLevel = slots.Count == 0 ? 0 : slots.Keys.Max();
            var granted = new HashSet<string>(runtime.GrantedSpellIds ?? new List<string>());

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var ids = new HashSet<string>();
                if (root.TryGetProperty("Classes", out var classes) && classes.ValueKind == JsonValueKind.Array)
                    foreach (var c in classes.EnumerateArray())
                        if (c.TryGetProperty("TemplateId", out var cid) && cid.ValueKind == JsonValueKind.String && myClasses.Contains(cid.GetString() ?? "")
                            && c.TryGetProperty("SpellListIds", out var sl) && sl.ValueKind == JsonValueKind.Array)
                            foreach (var s in sl.EnumerateArray())
                                if (s.ValueKind == JsonValueKind.String) ids.Add(s.GetString()!);

                if (ids.Count == 0 && granted.Count == 0) return list;

                if (root.TryGetProperty("Spells", out var spells) && spells.ValueKind == JsonValueKind.Array)
                    foreach (var sp in spells.EnumerateArray())
                    {
                        if (!sp.TryGetProperty("TemplateId", out var sid) || sid.ValueKind != JsonValueKind.String) continue;
                        var id = sid.GetString()!;
                        var isGranted = granted.Contains(id);
                        if (!ids.Contains(id) && !isGranted) continue;
                        var lvl = sp.TryGetProperty("Level", out var lv) && lv.TryGetInt32(out var lvi) ? lvi : 0;
                        if (!isGranted && lvl != 0 && lvl > maxLevel) continue;
                        var name = sp.TryGetProperty("Name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString()! : id;
                        var school = sp.TryGetProperty("School", out var schp) && schp.ValueKind == JsonValueKind.String ? schp.GetString()! : "";
                        var effJson = sp.TryGetProperty("Effects", out var ef) ? ef.GetRawText() : "[]";
                        var castTime = sp.TryGetProperty("CastingTime", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString()! : "";
                        var ritual = sp.TryGetProperty("Ritual", out var rit) && rit.ValueKind == JsonValueKind.True;
                        var comps = "";
                        if (sp.TryGetProperty("Components", out var cmp) && cmp.ValueKind == JsonValueKind.Array)
                            comps = string.Join(", ", cmp.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
                        var duration = sp.TryGetProperty("Duration", out var dur) && dur.ValueKind == JsonValueKind.String ? dur.GetString()! : "";
                        var conc = sp.TryGetProperty("Concentration", out var cc) && cc.ValueKind == JsonValueKind.True;
                        list.Add(new CastableSpell(id, name, lvl, school, effJson, castTime, sp.GetRawText(), ritual, comps, duration, conc));
                    }
            }
            catch (JsonException) { return list; }

            return list.OrderBy(s => s.Level).ThenBy(s => s.Name).ToList();
        }

        public async Task<string> ReadSpellCastingTimeAsync(string spellId)
        {
            if (string.IsNullOrEmpty(spellId)) return "";
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Spells", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var sp in arr.EnumerateArray())
                        if (sp.TryGetProperty("TemplateId", out var sid) && sid.ValueKind == JsonValueKind.String && string.Equals(sid.GetString(), spellId, StringComparison.OrdinalIgnoreCase))
                            return sp.TryGetProperty("CastingTime", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString() ?? "" : "";
            }
            catch (JsonException) { }
            return "";
        }

        public async Task<bool> ReadSpellConcentrationAsync(string spellId)
        {
            if (string.IsNullOrEmpty(spellId)) return false;
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Spells", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var sp in arr.EnumerateArray())
                        if (sp.TryGetProperty("TemplateId", out var sid) && sid.ValueKind == JsonValueKind.String && string.Equals(sid.GetString(), spellId, StringComparison.OrdinalIgnoreCase))
                            return sp.TryGetProperty("Concentration", out var co) && co.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { }
            return false;
        }

        public async Task<List<CastableSpell>> LoadPreparedSpellsAsync(CharacterRuntime runtime)
        {
            var all = await LoadCastableSpellsAsync(runtime);
            var set = new HashSet<string>(runtime.PreparedSpellIds ?? new List<string>());
            foreach (var g in runtime.GrantedSpellIds ?? new List<string>()) set.Add(g);
            if (set.Count == 0) return new List<CastableSpell>();
            return all.Where(s => set.Contains(s.Id)).ToList();
        }

        public async Task<List<BackstoryOption>> LoadBackstoriesAsync()
        {
            var list = new List<BackstoryOption>();
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Backstories", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var b in arr.EnumerateArray())
                    {
                        var title = b.TryGetProperty("Title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
                        var desc = b.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                        if (!string.IsNullOrEmpty(title)) list.Add(new BackstoryOption(title, desc));
                    }
            }
            catch (JsonException) { }
            return list;
        }

        public async Task<Dictionary<string, string>> LoadFeatNamesAsync()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return map;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Feats", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var f in arr.EnumerateArray())
                    {
                        var id = f.TryGetProperty("TemplateId", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "";
                        var name = f.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) map[id] = name;
                    }
            }
            catch (JsonException) { }
            return map;
        }

        public async Task<List<BackgroundOption>> LoadBackgroundsAsync()
        {
            var list = new List<BackgroundOption>();
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                var editionFilter = GetRulesVersionFilter();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Backgrounds", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var b in arr.EnumerateArray())
                    {
                        var ver = b.TryGetProperty("Version", out var vv) && vv.ValueKind == JsonValueKind.String ? vv.GetString()! : "2014";
                        if (!VersionVisible(ver, editionFilter)) continue;
                        var id = b.TryGetProperty("TemplateId", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "";
                        var name = b.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
                        var desc = b.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                        var skillIds = new List<string>();
                        if (b.TryGetProperty("SkillIds", out var si) && si.ValueKind == JsonValueKind.Array)
                            foreach (var sk in si.EnumerateArray())
                                if (sk.ValueKind == JsonValueKind.String) skillIds.Add(sk.GetString()!);
                        var abilityIds = new List<string>();
                        if (b.TryGetProperty("AbilityIds", out var ai) && ai.ValueKind == JsonValueKind.Array)
                            foreach (var ax in ai.EnumerateArray())
                                if (ax.ValueKind == JsonValueKind.String) abilityIds.Add(ax.GetString()!);
                        var featIds = new List<string>();
                        if (b.TryGetProperty("FeatsIds", out var fi) && fi.ValueKind == JsonValueKind.Array)
                            foreach (var fx in fi.EnumerateArray())
                                if (fx.ValueKind == JsonValueKind.String) featIds.Add(fx.GetString()!);
                        if (!string.IsNullOrEmpty(name)) list.Add(new BackgroundOption(id, name, desc, skillIds, abilityIds, featIds));
                    }
            }
            catch (JsonException) { }
            return list;
        }

        public async Task<List<LanguageOption>> LoadLanguagesAsync()
        {
            var list = new List<LanguageOption>();
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Languages", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var l in arr.EnumerateArray())
                    {
                        var id = l.TryGetProperty("TemplateId", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "";
                        var name = l.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
                        var script = l.TryGetProperty("Script", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "";
                        var desc = l.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) list.Add(new LanguageOption(id, name, script, desc));
                    }
            }
            catch (JsonException) { }
            return list;
        }

        public async Task<List<DiceRoll>> LoadRecentDiceRollsAsync(int limit = 50)
        {
            var result = new List<DiceRoll>();
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign == null) return result;

            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, UserId, Username, Expression, Total, Breakdown, Label, IsPrivate, Timestamp
                FROM DiceRolls
                WHERE CampaignId = $cid
                ORDER BY Timestamp DESC
                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$cid", campaign.Id);
            cmd.Parameters.AddWithValue("$limit", limit);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new DiceRoll
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    UserId = r.GetString(2),
                    Username = r.GetString(3),
                    Expression = r.GetString(4),
                    Total = r.GetInt32(5),
                    Breakdown = r.GetString(6),
                    Label = r.IsDBNull(7) ? null : r.GetString(7),
                    IsPrivate = r.GetInt32(8) == 1,
                    Timestamp = DateTime.Parse(r.GetString(9))
                });
            }
            return result;
        }

        public async Task<DiceRollResult?> RollDiceAsync(string expression, string? label = null, bool isPrivate = false)
        {
            if (!DiceManager.TryRoll(expression, out var result) || result == null)
                return null;

            var msg = new DiceRollMessage(
                RollId: Guid.NewGuid().ToString("N"),
                UserId: GetUID(),
                Username: GetUsername(),
                Expression: result.Expression,
                Total: result.Total,
                Breakdown: result.Breakdown,
                Label: label,
                IsPrivate: isPrivate);

            await ComController.SendDiceRollAsync(msg);
            return result;
        }

        // Clean this up, everything under here is chaos (or could maybe be better at least a bit)

        public async Task<string> CreateCharacterAsync(
            string name, string? raceId, string? subraceId, string? classId,
            int level, int currentHp, int maxHp, string abilityScoresJson,
            string? inventoryJson, string? stateJson,
            string? assignToUserId, string characterKind = "pc")
        {
            var id = await _campaignManager.CreateCharacterAsync(
                name, raceId, subraceId, classId, level, currentHp, maxHp,
                abilityScoresJson, inventoryJson, stateJson, assignToUserId, characterKind);
            await BroadcastCharacterAsync(id);
            return id;
        }

        public async Task BroadcastCharacterAsync(string characterId)
        {
            var row = await ReadCharacterRowAsync(characterId);
            if (row == null) return;

            var change = new ChangeNotification
            {
                EntityType = "Character",
                EntityId = row.Id,
                ChangeType = "updated",
                RevisionNumber = 0,
                Payload = JsonSerializer.Serialize(row)
            };
            await ComController.SubmitChangeAsync(change);
        }

        public async Task BroadcastInstanceAsync(ItemInstance inst)
        {
            if (inst == null) return;
            var change = new ChangeNotification
            {
                EntityType = "ItemInstance",
                EntityId = inst.Id,
                ChangeType = "updated",
                RevisionNumber = 0,
                Payload = JsonSerializer.Serialize(inst)
            };
            await ComController.SubmitChangeAsync(change);
        }

        public async Task BroadcastInstanceRemovedAsync(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return;
            var change = new ChangeNotification
            {
                EntityType = "ItemInstance",
                EntityId = instanceId,
                ChangeType = "removed",
                Payload = null
            };
            await ComController.SubmitChangeAsync(change);
        }

        private async Task<Character?> ReadCharacterRowAsync(string characterId)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Name, RaceId, SubraceId, ClassId,
                       Level, CurrentHp, MaxHp, AbilityScoresJson, InventoryJson,
                       StateJson, CharacterKind, Slug, Tags, CreatedAt, VisibleToAll
                FROM Characters
                WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", characterId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

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
                CreatedAt = DateTime.Parse(r.GetString(16)),
                VisibleToAll = !r.IsDBNull(17) && r.GetInt32(17) != 0
            };
        }

        public void ClearViewCache()
        {
            _viewCache.Clear();
        }

        public string GetUsername() 
        {
            return _campaignManager.CurrentUser.Username;
        }
        public string GetUID()
        {
            return _campaignManager.CurrentUser.Id;
        }

        public async Task AuthAcc(string name) 
        {
            await _campaignManager.SetCurrentUserAsync(name);
        }

        public async Task LoadCampaign(string campaignId, Action<string, double>? progress = null)
        {
            _isServer = true;
            progress?.Invoke("Unrolling the campaign scroll", 0.15);
            await _campaignManager.LoadCampaignAsync(campaignId);
            var campaign = _campaignManager.CurrentCampaign;
            if (campaign != null)
            {
                progress?.Invoke("Relinking the rules to the realm", 0.40);
                await _campaignManager.RelinkTemplateCatalogAsync(campaign.Id, campaign.TemplateId); // Failsafe for a few things, will remove this if I can
            }

            var ctx = new ActiveCampaignContext { CampaignId = campaign.Id };
            progress?.Invoke("Raising the host pavilion", 0.60);
            await ComController.StartServer("localhost", campaign.Port, _dbManager, ctx);

            var secret = await EnsureJoinSecretAsync(campaign.Id);

            WireCampaignEvents();
            progress?.Invoke("Taking your seat at the table", 0.74);
            await ComController.JoinCampaignAsync(
                userId: GetUID(),
                username: GetUsername(),
                serverIp: "localhost",
                serverPort: campaign.Port,
                joinSecret: secret,
                fingerprint: ComController.ServerFingerprint);
            Debug.WriteLine($"[PM] LoadCampaign called");
        }

        public async Task CreateNewCampaign(string name, string templateId, string port = "5555", Action<string, double>? progress = null)
        {
            _isServer = true;
            progress?.Invoke("Scribing the new campaign", 0.25);
            var campaign = await _campaignManager.CreateNewCampaignAsync(name, templateId, port: port);

            var ctx = new ActiveCampaignContext { CampaignId = campaign.Id };
            progress?.Invoke("Raising the host pavilion", 0.55);
            await ComController.StartServer("localhost", port, _dbManager, ctx);

            var secret = await EnsureJoinSecretAsync(campaign.Id);

            WireCampaignEvents();
            progress?.Invoke("Taking your seat at the table", 0.74);
            await ComController.JoinCampaignAsync(
                userId: GetUID(),
                username: GetUsername(),
                serverIp: "localhost",
                serverPort: port,
                joinSecret: secret,
                fingerprint: ComController.ServerFingerprint);
            Debug.WriteLine($"[PM] CreateNewCampaign called");
        }

        public async Task<bool> JoinCampaign(string networkPath, string joinCode = "", Action<string, double>? progress = null)
        {
            var fingerprint = "";
            var hashIdx = networkPath.IndexOf('#');
            if (hashIdx >= 0)
            {
                fingerprint = networkPath[(hashIdx + 1)..].Trim();
                networkPath = networkPath[..hashIdx];
            }

            var parts = networkPath.Split('/', ':');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                return false;
            if (!int.TryParse(parts[1], out var joinPort) || joinPort < 1 || joinPort > 65535)
                return false;

            var code = !string.IsNullOrEmpty(joinCode) ? joinCode : (parts.Length >= 3 ? parts[2] : "");

            _isServer = false;
            WireCampaignEvents();
            progress?.Invoke("Knocking on the host's door", 0.40);
            var payload = await ComController.JoinCampaignAsync(GetUID(), GetUsername(), parts[0], parts[1], code, fingerprint);
            progress?.Invoke("Waiting for the campaign to sync", 0.70);

            var remembered = $"{parts[0]}:{parts[1]}" + (fingerprint.Length > 0 ? "#" + fingerprint : "");
            await _campaignManager.RememberJoinedCampaignAsync(GetUID(), payload.CampaignId, payload.CampaignName, remembered, code);
            return true;
        }

        private string _joinSecret = "";
        public string GetJoinSecret() => _joinSecret;

        public string GetLanAddress()
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(u => u.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString())
                    .ToList();

                return candidates.FirstOrDefault(a => a.StartsWith("192.168.") || a.StartsWith("10."))
                       ?? candidates.FirstOrDefault()
                       ?? "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }

        public async Task<string> EnsureJoinSecretAsync(string campaignId)
        {
            if (string.IsNullOrEmpty(campaignId)) return "";
            await using var conn = await _dbManager.OpenAsync();
            string? secret;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT JoinSecret FROM Campaigns WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", campaignId);
                secret = await cmd.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrWhiteSpace(secret))
            {
                secret = GenerateJoinCode();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Campaigns SET JoinSecret = $s WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$s", secret);
                cmd.Parameters.AddWithValue("$id", campaignId);
                await cmd.ExecuteNonQueryAsync();
            }
            _joinSecret = secret;
            return secret;
        }

        public async Task<string> RegenerateJoinSecretAsync()
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return "";
            var secret = GenerateJoinCode();
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Campaigns SET JoinSecret = $s WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$s", secret);
            cmd.Parameters.AddWithValue("$id", campaignId);
            await cmd.ExecuteNonQueryAsync();
            _joinSecret = secret;
            return secret;
        }

        private string _rulesVersionFilter = "both";
        public string GetRulesVersionFilter() => _rulesVersionFilter;

        private bool VersionVisible(string? entryVersion, string? campaignFilter) => Rules.VisibleInEdition(entryVersion, campaignFilter);

        // No Version tag means 2014. That is the whole back catalog.
        internal static string VersionOf(JsonElement e) => e.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "2014" : "2014";

        private bool EntryVisible(JsonElement e, string filter) => VersionVisible(VersionOf(e), filter);

        // 2024 puts every subclass at level three, so the level is edition data and not a constant.
        internal static int SubclassLevelFor(JsonElement classEntry, string filter)
        {
            if (!string.IsNullOrWhiteSpace(filter) && classEntry.TryGetProperty("SubclassLevelByVersion", out var byVer) && byVer.ValueKind == JsonValueKind.Object)
                foreach (var p in byVer.EnumerateObject())
                    if (string.Equals(p.Name, filter, StringComparison.OrdinalIgnoreCase) && p.Value.TryGetInt32(out var vv)) return vv;
            return classEntry.TryGetProperty("SubclassLevel", out var sl) && sl.TryGetInt32(out var slv) ? slv : -1;
        }

        public async Task<string> LoadRulesVersionFilterAsync()
        {
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return _rulesVersionFilter;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT RulesVersion FROM Campaigns WHERE Id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", campaignId);
            var v = await cmd.ExecuteScalarAsync() as string;
            _rulesVersionFilter = string.IsNullOrWhiteSpace(v) ? "both" : v;
            return _rulesVersionFilter;
        }

        public async Task SetRulesVersionFilterAsync(string value)
        {
            var v = (value == "2014" || value == "2024") ? value : "both";
            _rulesVersionFilter = v;
            var campaignId = GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Campaigns SET RulesVersion = $v WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$v", v);
            cmd.Parameters.AddWithValue("$id", campaignId);
            await cmd.ExecuteNonQueryAsync();
        }

        private static string GenerateJoinCode()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var chars = new char[6];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            return new string(chars);
        }

        private bool _campaignEventsWired;

        private void WireCampaignEvents()
        {
            if (_campaignEventsWired) return; // ComController is a singleton and these lambdas can't be -='d off, so they wire exactly once.
            _campaignEventsWired = true;

            ComController.OnBootstrapReceived += async payload =>
            {
                await _campaignManager.ApplyBootstrapAsync(payload);
                InvalidateRules();
                await EnsureRulesLoadedAsync();
                await LoadRulesVersionFilterAsync();
            };

            ComController.OnChangeReceived += async change =>
            {
                await _campaignManager.ApplyChangeAsync(change);
                OnGameDataChanged?.Invoke(change.EntityType, change.EntityId);
            };

            ComController.OnChannelCreated += async channel =>
                await _campaignManager.SaveChannelAsync(channel);
        }

        public string GetCampaignId()
        {
            return _campaignManager.CurrentCampaign?.Id ?? "";
        }

        public async Task<bool> DeleteCampaignAsync(string campaignId, CancellationToken ct = default)
        {
            if (!await _campaignManager.DeleteCampaignAsync(campaignId, ct)) return false;

            DeleteCampaignAssets(campaignId);
            return true;
        }

        internal static string? ResolveCampaignAssetDir(string root, string campaignId)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            var assets = Path.Combine(root, "assets");
            return GlobalVariables.SafeChildPath(assets, campaignId);
        }

        private static void DeleteCampaignAssets(string campaignId)
        {
            var dir = ResolveCampaignAssetDir(GlobalVariables.AppDataLocal, campaignId);
            if (dir == null) return;

            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception ex) { ErrorLog.Log("[Delete] the campaign is gone but its art folder would not go with it", ex); }
        }

        public string GetActiveTemplateId()
        {
            return _campaignManager.CurrentCampaign?.TemplateId ?? CurrentTemplateId ?? "";
        }

        public Task<ProficiencyResolver.GrantedProficiencies> ResolveProficienciesAsync(string? classId, string? raceId)
        {
            return ProfResolver.ResolveForAsync(GetActiveTemplateId(), classId, raceId);
        }

        public async Task<Dictionary<string, string>> GetArmorProfMapAsync()
        {
            var map = new Dictionary<string, string>();
            var json = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(json)) return map;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ArmorTypes", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var a in arr.EnumerateArray())
                    {
                        var id = a.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                        var pr = a.TryGetProperty("ProfRequiredId", out var p) ? p.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(pr)) map[id!] = pr!;
                    }
            }
            catch (JsonException) { }
            return map;
        }

        public Task<TemplateItemCatalogs> ReadItemCatalogsAsync()
        {
            return CatalogReader.ReadAsync(GetActiveTemplateId());
        }

        public async Task<(string Name, string ItemType, string DataJson)?> LoadItemAsync(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || _dbManager == null) return null;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name, ItemType, DataJson FROM Items WHERE Id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", itemId);

            string name, itemType, poolJson;
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return null;
                name = r.GetString(0);
                itemType = r.IsDBNull(1) ? "" : r.GetString(1);
                poolJson = r.IsDBNull(2) ? "{}" : r.GetString(2);
            }

            var resolved = await CatalogResolver.EntryJsonAsync(conn, "Items", itemId, GetActiveTemplateId());
            return (name, itemType, resolved ?? poolJson);
        }

        public async Task<string?> ResolveEntryJsonAsync(string kind, string entryId)
        {
            if (_dbManager == null || string.IsNullOrWhiteSpace(entryId)) return null;
            await using var conn = await _dbManager.OpenAsync();
            return await CatalogResolver.EntryJsonAsync(conn, kind, entryId, GetActiveTemplateId());
        }

        public async Task SaveItemDataJsonAsync(string itemId, string dataJson)
        {
            if (string.IsNullOrEmpty(itemId) || _dbManager == null) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Items
                SET DataJson = $data,
                    RevisionNumber = RevisionNumber + 1,
                    UpdatedAt = $now
                WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$data", dataJson);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$id", itemId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<string?> GetActiveTemplateJsonAsync() => await LoadActiveTemplateJsonAsync();

        private static readonly HashSet<string> _tableBackedArrays = new(StringComparer.OrdinalIgnoreCase)
        {
            "Items", "Spells", "Races", "Subraces", "Classes", "Traits"
        };

        public async Task<string?> SaveTemplateEntryAsync(string arrayKey, string entryJson)
        {
            var tid = GetActiveTemplateId();
            if (string.IsNullOrEmpty(tid)) return "No active template to edit.";
            if (string.IsNullOrWhiteSpace(arrayKey)) return "No catalog section chosen.";

            JsonNode? entryNode;
            try { entryNode = JsonNode.Parse(entryJson); }
            catch (Exception ex) { return "Entry JSON is invalid: " + ex.Message; }
            if (entryNode is not JsonObject entry) return "An entry has to be a JSON object.";

            var idKey = arrayKey.Equals("ClassResources", StringComparison.OrdinalIgnoreCase) ? "Id" : "TemplateId";
            var id = TplStr(entry, idKey);
            if (string.IsNullOrWhiteSpace(id)) return $"Entry needs a {idKey}.";

            var blob = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(blob)) return "No template content found.";
            JsonNode? rootNode;
            try { rootNode = JsonNode.Parse(blob); }
            catch (Exception ex) { return "Stored template is unreadable: " + ex.Message; }
            if (rootNode is not JsonObject root) return "Stored template root is not an object.";

            if (root[arrayKey] is not JsonArray arr) { arr = new JsonArray(); root[arrayKey] = arr; }

            var replaceAt = -1;
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is JsonObject o && string.Equals(TplStr(o, idKey), id, StringComparison.OrdinalIgnoreCase)) { replaceAt = i; break; }

            var fresh = JsonNode.Parse(entryJson);
            if (replaceAt >= 0) arr[replaceAt] = fresh; else arr.Add(fresh);

            var updated = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await SaveActiveTemplateBlobAsync(tid, updated);

            if (_tableBackedArrays.Contains(arrayKey))
                await ReprojectRowAsync(arrayKey, entry, id!, tid);

            return null;
        }

        public async Task<string?> DeleteTemplateEntryAsync(string arrayKey, string templateId)
        {
            var tid = GetActiveTemplateId();
            if (string.IsNullOrEmpty(tid)) return "No active template.";
            if (string.IsNullOrWhiteSpace(arrayKey) || string.IsNullOrWhiteSpace(templateId)) return "Nothing selected to delete.";

            var blob = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(blob)) return "No template content.";
            JsonNode? rootNode;
            try { rootNode = JsonNode.Parse(blob); }
            catch (Exception ex) { return "Stored template is unreadable: " + ex.Message; }
            if (rootNode is not JsonObject root || root[arrayKey] is not JsonArray arr) return "Section not found.";

            var idKey = arrayKey.Equals("ClassResources", StringComparison.OrdinalIgnoreCase) ? "Id" : "TemplateId";
            for (int i = arr.Count - 1; i >= 0; i--)
                if (arr[i] is JsonObject o && string.Equals(TplStr(o, idKey), templateId, StringComparison.OrdinalIgnoreCase))
                    arr.RemoveAt(i);

            var updated = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await SaveActiveTemplateBlobAsync(tid, updated);

            if (_tableBackedArrays.Contains(arrayKey))
            {
                await using var conn = await _dbManager.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DELETE FROM {arrayKey} WHERE Id = $id AND Source = 'srd';";
                cmd.Parameters.AddWithValue("$id", templateId);
                await cmd.ExecuteNonQueryAsync();
            }
            return null;
        }

        // An existing campaign gets its names and descriptions and Version pulled fresh off the template, anything the dm made (Source='custom') is left alone
        public async Task<(int Rows, int Sections)> RefreshCatalogFromTemplateAsync()
        {
            var tid = GetActiveTemplateId();
            var blob = await LoadActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(blob)) return (0, 0);
            JsonNode? rootNode;
            try { rootNode = JsonNode.Parse(blob); }
            catch { return (0, 0); }
            if (rootNode is not JsonObject root) return (0, 0);

            int rows = 0, sections = 0;
            foreach (var arrayKey in _tableBackedArrays)
            {
                if (root[arrayKey] is not JsonArray arr) continue;
                sections++;
                foreach (var node in arr)
                {
                    if (node is not JsonObject e) continue;
                    var id = TplStr(e, "TemplateId");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    await ReprojectRowAsync(arrayKey, e, id!, tid);
                    rows++;
                }
            }
            return (rows, sections);
        }

        private async Task SaveActiveTemplateBlobAsync(string templateId, string json)
        {
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE CampaignTemplates SET JsonContent = $json WHERE TemplateId = $tid;";
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$tid", templateId);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ReprojectRowAsync(string arrayKey, JsonObject e, string id, string templateId)
        {
            var raw = e.ToJsonString();
            var now = DateTime.UtcNow.ToString("o");
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();

            switch (arrayKey.ToLowerInvariant())
            {
                case "items":
                    cmd.CommandText = @"
                        INSERT INTO Items (Id, Name, ItemType, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Slug, Tags, Version)
                        VALUES ($id, $name, $type, 'srd', NULL, $tid, COALESCE((SELECT RevisionNumber FROM Items WHERE Id=$id),0)+1, $now, $data, NULL, '[]', $ver)
                        ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, ItemType=excluded.ItemType, TemplateId=excluded.TemplateId, RevisionNumber=excluded.RevisionNumber, UpdatedAt=excluded.UpdatedAt, DataJson=excluded.DataJson, Version=excluded.Version;";
                    cmd.Parameters.AddWithValue("$name", TplStr(e, "Name"));
                    cmd.Parameters.AddWithValue("$type", FirstNonEmpty(TplStr(e, "ItemType"), TplStr(e, "$type"), "Generic"));
                    break;
                case "spells":
                    cmd.CommandText = @"
                        INSERT INTO Spells (Id, Name, Level, School, CastingTime, Duration, Range, Concentration, Ritual, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $level, $school, $cast, $dur, $range, $conc, $rit, $desc, 'srd', NULL, $tid, COALESCE((SELECT RevisionNumber FROM Spells WHERE Id=$id),0)+1, $now, $data, $ver)
                        ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Level=excluded.Level, School=excluded.School, CastingTime=excluded.CastingTime, Duration=excluded.Duration, Range=excluded.Range, Concentration=excluded.Concentration, Ritual=excluded.Ritual, Description=excluded.Description, TemplateId=excluded.TemplateId, RevisionNumber=excluded.RevisionNumber, UpdatedAt=excluded.UpdatedAt, DataJson=excluded.DataJson, Version=excluded.Version;";
                    cmd.Parameters.AddWithValue("$name", TplStr(e, "Name"));
                    cmd.Parameters.AddWithValue("$level", TplInt(e, "Level"));
                    cmd.Parameters.AddWithValue("$school", TplStr(e, "School"));
                    cmd.Parameters.AddWithValue("$cast", TplStr(e, "CastingTime"));
                    cmd.Parameters.AddWithValue("$dur", TplStr(e, "Duration"));
                    cmd.Parameters.AddWithValue("$range", TplStr(e, "Range"));
                    cmd.Parameters.AddWithValue("$conc", TplBool(e, "Concentration") ? 1 : 0);
                    cmd.Parameters.AddWithValue("$rit", TplBool(e, "Ritual") ? 1 : 0);
                    cmd.Parameters.AddWithValue("$desc", TplStr(e, "Description"));
                    break;
                case "races":
                    cmd.CommandText = @"
                        INSERT INTO Races (Id, Name, Description, Size, Speed, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $desc, $size, $speed, 'srd', NULL, $tid, COALESCE((SELECT RevisionNumber FROM Races WHERE Id=$id),0)+1, $now, $data, $ver)
                        ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Description=excluded.Description, Size=excluded.Size, Speed=excluded.Speed, TemplateId=excluded.TemplateId, RevisionNumber=excluded.RevisionNumber, UpdatedAt=excluded.UpdatedAt, DataJson=excluded.DataJson, Version=excluded.Version;";
                    cmd.Parameters.AddWithValue("$name", TplStr(e, "Name"));
                    cmd.Parameters.AddWithValue("$desc", TplStr(e, "Description"));
                    cmd.Parameters.AddWithValue("$size", TplStr(e, "Size"));
                    cmd.Parameters.AddWithValue("$speed", TplInt(e, "Speed"));
                    break;
                case "subraces":
                    cmd.CommandText = @"
                        INSERT INTO Subraces (Id, Name, ParentRaceId, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, COALESCE((SELECT ParentRaceId FROM Subraces WHERE Id=$id),''), $desc, 'srd', NULL, $tid, COALESCE((SELECT RevisionNumber FROM Subraces WHERE Id=$id),0)+1, $now, $data, $ver)
                        ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Description=excluded.Description, TemplateId=excluded.TemplateId, RevisionNumber=excluded.RevisionNumber, UpdatedAt=excluded.UpdatedAt, DataJson=excluded.DataJson, Version=excluded.Version;";
                    cmd.Parameters.AddWithValue("$name", TplStr(e, "Name"));
                    cmd.Parameters.AddWithValue("$desc", TplStr(e, "Description"));
                    break;
                case "classes":
                    cmd.CommandText = @"
                        INSERT INTO Classes (Id, Name, Description, HitDiceId, PrimaryAbility, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt, DataJson, Version)
                        VALUES ($id, $name, $desc, $hit, $prim, 'srd', NULL, $tid, COALESCE((SELECT RevisionNumber FROM Classes WHERE Id=$id),0)+1, $now, $data, $ver)
                        ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Description=excluded.Description, HitDiceId=excluded.HitDiceId, PrimaryAbility=excluded.PrimaryAbility, TemplateId=excluded.TemplateId, RevisionNumber=excluded.RevisionNumber, UpdatedAt=excluded.UpdatedAt, DataJson=excluded.DataJson, Version=excluded.Version;";
                    cmd.Parameters.AddWithValue("$name", TplStr(e, "Name"));
                    cmd.Parameters.AddWithValue("$desc", TplStr(e, "Description"));
                    cmd.Parameters.AddWithValue("$hit", TplStr(e, "HitDiceId"));
                    cmd.Parameters.AddWithValue("$prim", TplStr(e, "PrimaryAbilityId"));
                    break;
                case "traits":
                    cmd.CommandText = @"
                        INSERT INTO Traits (Id, Name, Description, Source, OwnerUserId, TemplateId, RevisionNumber, UpdatedAt)
                        VALUES ($id, $name, $desc, 'srd', NULL, $tid, COALESCE((SELECT RevisionNumber FROM Traits WHERE Id=$id),0)+1, $now)
                        ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, Description=excluded.Description, TemplateId=excluded.TemplateId, RevisionNumber=excluded.RevisionNumber, UpdatedAt=excluded.UpdatedAt;";
                    cmd.Parameters.AddWithValue("$name", TplStr(e, "Name"));
                    cmd.Parameters.AddWithValue("$desc", TplStr(e, "Description"));
                    break;
                default:
                    return;
            }

            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$tid", templateId);
            cmd.Parameters.AddWithValue("$now", now);
            if (!arrayKey.Equals("Traits", StringComparison.OrdinalIgnoreCase))
            {
                cmd.Parameters.AddWithValue("$data", raw);
                cmd.Parameters.AddWithValue("$ver", FirstNonEmpty(TplStr(e, "Version"), "2014"));
            }
            await cmd.ExecuteNonQueryAsync();
        }

        private static string FirstNonEmpty(params string[] xs) { foreach (var x in xs) if (!string.IsNullOrWhiteSpace(x)) return x; return ""; }
        private static string TplStr(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        private static int TplInt(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
        private static bool TplBool(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

        private bool _hideRolls;
        public bool HideRolls
        {
            get => _hideRolls;
            set { _hideRolls = value; _ = SetSettingAsync("HideRolls", value ? "1" : "0"); }
        }

        private string _dmPresenceColor = "#FFD700";
        public string DmPresenceColor
        {
            get => _dmPresenceColor;
            set { _dmPresenceColor = string.IsNullOrWhiteSpace(value) ? "#FFD700" : value; _ = SetSettingAsync("DmPresenceColor", _dmPresenceColor); }
        }

        public async Task LoadDmSettingsAsync()
        {
            var v = await GetSettingAsync("HideRolls");
            _hideRolls = v == "1";
            var pc = await GetSettingAsync("DmPresenceColor");
            if (!string.IsNullOrWhiteSpace(pc)) _dmPresenceColor = pc;
        }

        public async Task<string?> GetSettingAsync(string key)
        {
            if (_dbManager == null) return null;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $k LIMIT 1";
            cmd.Parameters.AddWithValue("$k", key);
            return await cmd.ExecuteScalarAsync() as string;
        }

        public async Task SetSettingAsync(string key, string value)
        {
            if (_dbManager == null) return;
            await using var conn = await _dbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ($k, $v)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<(string Id, string Name)>> ReadConditionsAsync()
        {
            var result = new List<(string, string)>();
            var tid = GetActiveTemplateId();

            await using var conn = await _dbManager.OpenAsync();

            string? json = null;
            if (!string.IsNullOrEmpty(tid))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", tid);
                json = await cmd.ExecuteScalarAsync() as string;
            }

            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Conditions", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var c in arr.EnumerateArray())
                    {
                        var id = c.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                        var nm = c.TryGetProperty("Name", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrEmpty(id)) result.Add((id!, nm ?? id!));
                    }
            }
            catch (JsonException) { }

            return result;
        }
        public Task<List<(string Name, string Description, int Level)>> ReadClassFeaturesAsync(string? classId, int currentLevel, string? chosenSubclass)
            => ReadClassFeaturesAsync(classId, currentLevel, string.IsNullOrEmpty(chosenSubclass) ? null : new[] { chosenSubclass });

        public async Task<List<(string Name, string Description, int Level)>> ReadClassFeaturesAsync(string? classId, int currentLevel, IEnumerable<string>? chosenSubclasses = null)
        {
            var rows = await ReadClassFeatureRowsAsync(classId, currentLevel, chosenSubclasses);
            return rows.Select(r => (r.Name, r.Description, r.Level)).ToList();
        }

        public async Task<List<(string Name, string Description, int Level, bool Enforced)>> ReadClassFeatureRowsAsync(string? classId, int currentLevel, IEnumerable<string>? chosenSubclasses = null)
        {
            var chosen = new HashSet<string>(chosenSubclasses ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            chosen.Remove("");
            var result = new List<(string, string, int, bool)>();
            var tid = GetActiveTemplateId();
            if (string.IsNullOrEmpty(classId)) return result;

            await using var conn = await _dbManager.OpenAsync();

            string? json = null;
            if (!string.IsNullOrEmpty(tid))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", tid);
                json = await cmd.ExecuteScalarAsync() as string;
            }

            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                var editionFilter = GetRulesVersionFilter();
                using var doc = JsonDocument.Parse(json);
                var subclassOwner = ReadSubclassFeatureOwners(doc.RootElement);
                var bonusIds = ReadBonusIdSet(doc.RootElement);
                if (doc.RootElement.TryGetProperty("Features", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var f in arr.EnumerateArray())
                    {
                        var fcid = f.TryGetProperty("ClassId", out var c) ? c.GetString() : null;
                        if (!string.Equals(fcid, classId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!EntryVisible(f, editionFilter)) continue;
                        var fid = f.TryGetProperty("TemplateId", out var fiid) ? fiid.GetString() : null;
                        if (!string.IsNullOrEmpty(fid) && subclassOwner.TryGetValue(fid!, out var sub)
                            && !(chosen.Contains(sub.Name) || chosen.Contains(sub.Id))) continue;
                        var lvl = f.TryGetProperty("Level", out var l) && l.TryGetInt32(out var lv) ? lv : 1;
                        if (lvl > currentLevel) continue;
                        var nm = f.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                        var desc = f.TryGetProperty("Description", out var de) ? de.GetString() ?? "" : "";
                        result.Add((nm, desc, lvl, EntryIsEngineFired(f, bonusIds)));
                    }
            }
            catch (JsonException) { }

            result.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            return result;
        }

        public async Task<Dictionary<string, string>> ReadChosenOptionDescriptionsAsync()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tid = GetActiveTemplateId();

            await using var conn = await _dbManager.OpenAsync();

            string? json = null;
            if (!string.IsNullOrEmpty(tid))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", tid);
                json = await cmd.ExecuteScalarAsync() as string;
            }

            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json)) return map;

            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var section in new[] { "Subclasses", "Feats" })
                {
                    if (!doc.RootElement.TryGetProperty(section, out var arr) || arr.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var e in arr.EnumerateArray())
                    {
                        var nm = e.TryGetProperty("Name", out var n) ? n.GetString() : null;
                        if (string.IsNullOrEmpty(nm)) continue;
                        var desc = e.TryGetProperty("Description", out var de) ? de.GetString() ?? "" : "";
                        map[nm] = desc;
                    }
                }
            }
            catch (JsonException) { }

            return map;
        }

        public async Task<string?> ReadSubclassSlotFeatureNameAsync(string? classId)
        {
            if (string.IsNullOrEmpty(classId)) return null;

            var tid = GetActiveTemplateId();
            await using var conn = await _dbManager.OpenAsync();

            string? json = null;
            if (!string.IsNullOrEmpty(tid))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", tid);
                json = await cmd.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var editionFilter = GetRulesVersionFilter();
                int slotLevel = -1;
                if (root.TryGetProperty("Classes", out var classes) && classes.ValueKind == JsonValueKind.Array)
                    foreach (var c in classes.EnumerateArray())
                    {
                        var cid = c.TryGetProperty("TemplateId", out var ci) ? ci.GetString() : null;
                        if (!string.Equals(cid, classId, StringComparison.OrdinalIgnoreCase)) continue;
                        slotLevel = SubclassLevelFor(c, editionFilter);
                        break;
                    }
                if (slotLevel < 0) return null;

                var subNames = new List<string>();
                if (root.TryGetProperty("Subclasses", out var subs) && subs.ValueKind == JsonValueKind.Array)
                    foreach (var s in subs.EnumerateArray())
                    {
                        var pid = s.TryGetProperty("ParentClassId", out var p) ? p.GetString() : null;
                        if (!string.Equals(pid, classId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!EntryVisible(s, editionFilter)) continue;
                        var nm = s.TryGetProperty("Name", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrEmpty(nm)) subNames.Add(nm!);
                    }

                var candidates = new List<(string Name, string Desc)>();
                if (root.TryGetProperty("Features", out var farr) && farr.ValueKind == JsonValueKind.Array)
                    foreach (var f in farr.EnumerateArray())
                    {
                        var fcid = f.TryGetProperty("ClassId", out var fc) ? fc.GetString() : null;
                        if (!string.Equals(fcid, classId, StringComparison.OrdinalIgnoreCase)) continue;
                        var lvl = f.TryGetProperty("Level", out var l) && l.TryGetInt32(out var lv) ? lv : -1;
                        if (lvl != slotLevel) continue;
                        var nm = f.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                        var de = f.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
                        candidates.Add((nm, de));
                    }

                if (candidates.Count == 0) return null;
                if (candidates.Count == 1) return candidates[0].Name;

                foreach (var cand in candidates)
                    foreach (var sn in subNames)
                        if (DescriptionMentionsSubclass(cand.Desc, sn))
                            return cand.Name;
            }
            catch (JsonException) { }

            return null;
        }

        private static bool DescriptionMentionsSubclass(string description, string subclassName)
        {
            if (string.IsNullOrEmpty(description) || string.IsNullOrEmpty(subclassName)) return false;
            var desc = description.ToLowerInvariant();
            var name = subclassName.ToLowerInvariant();
            if (desc.Contains(name)) return true;

            var core = name;
            if (core.StartsWith("the ")) core = core.Substring(4);
            var ofIdx = core.IndexOf(" of ");
            if (ofIdx >= 0)
            {
                var after = core.Substring(ofIdx + 4);
                if (after.StartsWith("the ")) after = after.Substring(4);
                core = after;
            }
            core = core.Trim();
            return core.Length > 0 && desc.Contains(core);
        }

        public async Task<List<LevelChoice>> ReadLevelChoicesAsync(string? classId, int level, bool includeEarlier = false)
        {
            var result = new List<LevelChoice>();
            if (string.IsNullOrEmpty(classId)) return result;

            var tid = GetActiveTemplateId();
            await using var conn = await _dbManager.OpenAsync();

            string? json = null;
            if (!string.IsNullOrEmpty(tid))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", tid);
                json = await cmd.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var editionFilter = GetRulesVersionFilter();
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var descs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var offEdition = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var section in new[] { "Feats", "Proficiencies", "Subclasses", "Abilities" })
                    if (root.TryGetProperty(section, out var sec) && sec.ValueKind == JsonValueKind.Array)
                        foreach (var e in sec.EnumerateArray())
                        {
                            var id = e.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                            var nm = e.TryGetProperty("Name", out var n) ? n.GetString() : null;
                            if (string.IsNullOrEmpty(id)) continue;
                            if (!EntryVisible(e, editionFilter)) offEdition.Add(id!);
                            if (!names.ContainsKey(id!)) names[id!] = nm ?? id!;
                            if (e.TryGetProperty("Description", out var de) && de.ValueKind == JsonValueKind.String)
                            {
                                var dv = de.GetString();
                                if (!string.IsNullOrWhiteSpace(dv) && !descs.ContainsKey(id!)) descs[id!] = dv!;
                            }
                        }

                var featLines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var section in new[] { "Feats", "Features" })
                    if (root.TryGetProperty(section, out var sec) && sec.ValueKind == JsonValueKind.Array)
                        foreach (var e in sec.EnumerateArray())
                        {
                            var id = e.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                            if (string.IsNullOrEmpty(id) || featLines.ContainsKey(id!)) continue;
                            var nm = e.TryGetProperty("Name", out var n) ? n.GetString() : id;
                            var fd = e.TryGetProperty("Description", out var d2) ? d2.GetString() : null;
                            featLines[id!] = string.IsNullOrWhiteSpace(fd) ? (nm ?? id!) : (nm + ": " + fd);
                        }

                var subFeats = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("Subclasses", out var subArr) && subArr.ValueKind == JsonValueKind.Array)
                    foreach (var s in subArr.EnumerateArray())
                    {
                        var id = s.TryGetProperty("TemplateId", out var si) ? si.GetString() : null;
                        if (string.IsNullOrEmpty(id)) continue;
                        var fids = new List<string>();
                        if (s.TryGetProperty("FeatsIds", out var fa) && fa.ValueKind == JsonValueKind.Array)
                            foreach (var f in fa.EnumerateArray())
                            {
                                var fid = f.GetString();
                                if (!string.IsNullOrEmpty(fid)) fids.Add(fid!);
                            }
                        if (fids.Count > 0) subFeats[id!] = fids;
                    }

                if (root.TryGetProperty("ClassChoices", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var c in arr.EnumerateArray())
                    {
                        var ccid = c.TryGetProperty("ClassId", out var ci) ? ci.GetString() : null;
                        if (!string.Equals(ccid, classId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!EntryVisible(c, editionFilter)) continue;
                        var lvl = c.TryGetProperty("Level", out var l) && l.TryGetInt32(out var lv) ? lv : 1;
                        if (includeEarlier ? lvl > level : lvl != level) continue;

                        var choice = new LevelChoice
                        {
                            Id = c.TryGetProperty("TemplateId", out var t) ? t.GetString() ?? "" : "",
                            Kind = c.TryGetProperty("Kind", out var k) ? k.GetString() ?? "" : "",
                            Label = c.TryGetProperty("Label", out var la) ? la.GetString() ?? "" : "",
                            Description = c.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "",
                            ChooseCount = c.TryGetProperty("ChooseCount", out var cc) && cc.TryGetInt32(out var ccv) ? ccv : 1,
                            StoreAs = c.TryGetProperty("StoreAs", out var sa) ? sa.GetString() ?? "" : "",
                            Level = lvl
                        };

                        if (c.TryGetProperty("OptionIds", out var opts) && opts.ValueKind == JsonValueKind.Array)
                            foreach (var o in opts.EnumerateArray())
                            {
                                var oid = o.GetString();
                                if (string.IsNullOrEmpty(oid) || offEdition.Contains(oid!)) continue;
                                var oname = names.TryGetValue(oid!, out var nm) ? nm : oid!;
                                var odesc = descs.TryGetValue(oid!, out var dd) ? dd : null;
                                string? omech = null;
                                if (subFeats.TryGetValue(oid!, out var fids))
                                {
                                    var lines = new List<string>();
                                    foreach (var fid in fids)
                                        if (featLines.TryGetValue(fid, out var fl)) lines.Add(fl);
                                    if (lines.Count > 0) omech = string.Join("\n", lines);
                                }
                                choice.Options.Add(new LevelChoiceOption(oid!, oname) { Description = odesc, Mechanics = omech });
                            }

                        if (string.Equals(choice.Kind, "abilityOrFeat", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrWhiteSpace(choice.Label))
                                choice.Label = "Ability Score Improvement or Feat (Level " + level + ")";
                            choice.Description = "Bump your ability scores or take a feat instead.";
                        }

                        result.Add(choice);
                    }
            }
            catch (JsonException) { }

            return CollapseSinglePickStores(result);
        }

        internal static List<LevelChoice> CollapseForTests(List<LevelChoice> choices) => CollapseSinglePickStores(choices);

        private static List<LevelChoice> CollapseSinglePickStores(List<LevelChoice> choices)
        {
            var seen = new Dictionary<string, LevelChoice>(StringComparer.OrdinalIgnoreCase);
            var order = new List<LevelChoice>();

            foreach (var c in choices)
            {
                if (!string.Equals(c.StoreAs, "subclass", StringComparison.OrdinalIgnoreCase) || c.ChooseCount != 1)
                {
                    order.Add(c);
                    continue;
                }

                if (!seen.TryGetValue(c.StoreAs, out var kept))
                {
                    seen[c.StoreAs] = c;
                    order.Add(c);
                    continue;
                }

                foreach (var o in c.Options)
                    if (!kept.Options.Any(x => string.Equals(x.Id, o.Id, StringComparison.OrdinalIgnoreCase)))
                        kept.Options.Add(o);
            }

            return order;
        }

        public async Task<List<ResolvedClassChoice>> ReadBuilderChoicesAsync(string? classId, int uptoLevel)
        {
            var result = new List<ResolvedClassChoice>();
            if (string.IsNullOrEmpty(classId)) return result;

            var tid = GetActiveTemplateId();
            await using var conn = await _dbManager.OpenAsync();

            string? json = null;
            if (!string.IsNullOrEmpty(tid))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates WHERE TemplateId = $tid LIMIT 1";
                cmd.Parameters.AddWithValue("$tid", tid);
                json = await cmd.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json))
            {
                await using var fb = conn.CreateCommand();
                fb.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                json = await fb.ExecuteScalarAsync() as string;
            }
            if (string.IsNullOrEmpty(json)) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var editionFilter = GetRulesVersionFilter();
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var descs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var offEdition = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var section in new[] { "Feats", "Proficiencies", "Subclasses", "Abilities" })
                    if (root.TryGetProperty(section, out var sec) && sec.ValueKind == JsonValueKind.Array)
                        foreach (var e in sec.EnumerateArray())
                        {
                            var id = e.TryGetProperty("TemplateId", out var i) ? i.GetString() : null;
                            var nm = e.TryGetProperty("Name", out var n) ? n.GetString() : null;
                            var de = e.TryGetProperty("Description", out var dd) ? dd.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !EntryVisible(e, editionFilter)) offEdition.Add(id!);
                            if (!string.IsNullOrEmpty(id) && !names.ContainsKey(id!))
                            {
                                names[id!] = nm ?? id!;
                                if (!string.IsNullOrEmpty(de)) descs[id!] = de!;
                            }
                        }

                if (root.TryGetProperty("ClassChoices", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var c in arr.EnumerateArray())
                    {
                        var ccid = c.TryGetProperty("ClassId", out var ci) ? ci.GetString() : null;
                        if (!string.Equals(ccid, classId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!EntryVisible(c, editionFilter)) continue;
                        var lvl = c.TryGetProperty("Level", out var l) && l.TryGetInt32(out var lv) ? lv : 1;
                        if (lvl > uptoLevel) continue;

                        var choice = new ResolvedClassChoice
                        {
                            Id = c.TryGetProperty("TemplateId", out var t) ? t.GetString() ?? "" : "",
                            ClassId = ccid ?? "",
                            Level = lvl,
                            Kind = c.TryGetProperty("Kind", out var k) ? k.GetString() ?? "" : "",
                            Label = c.TryGetProperty("Label", out var la) ? la.GetString() ?? "" : "",
                            Description = c.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "",
                            ChooseCount = c.TryGetProperty("ChooseCount", out var cc) && cc.TryGetInt32(out var ccv) ? ccv : 1,
                            StoreAs = c.TryGetProperty("StoreAs", out var sa) ? sa.GetString() ?? "" : ""
                        };

                        if (c.TryGetProperty("OptionIds", out var opts) && opts.ValueKind == JsonValueKind.Array)
                            foreach (var o in opts.EnumerateArray())
                            {
                                var oid = o.GetString();
                                if (string.IsNullOrEmpty(oid) || offEdition.Contains(oid!)) continue;
                                choice.Options.Add(new ChoiceOption(oid!, names.TryGetValue(oid!, out var nm) ? nm : oid!, descs.TryGetValue(oid!, out var de) ? de : ""));
                            }

                        result.Add(choice);
                    }
            }
            catch (JsonException) { }

            result.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.Compare(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public string GetCampaignName()
        {
            return _campaignManager.CurrentCampaign?.Name ?? "";
        }

        public VersionManager GetVersionManager()
        {
            return _versionManager;
        }

        public async Task<VersionManager?> LoadConfigFromGitHubAsync()
        {
            try
            {
                const string pathFile = "../../../jsonPath.txt";
                if (!File.Exists(pathFile))
                {
                    ErrorLog.Log("Update check: jsonPath.txt not found, skipping.");
                    return null;
                }
                var url = File.ReadAllText(pathFile).Trim().TrimStart((char)0xFEFF);
                if (string.IsNullOrWhiteSpace(url)) return null;

                if (url.Contains("github.com/") && url.Contains("/blob/"))
                    url = url.Replace("github.com/", "raw.githubusercontent.com/").Replace("/blob/", "/");

                using var http = new HttpClient();
                var jsonString = await http.GetStringAsync(url);
                return JsonSerializer.Deserialize<VersionManager>(jsonString);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Update check failed", ex);
                return null;
            }
        }
    }
    public class VersionManager // This "version management system" is basically created from a SharePoint update system I made at my previous job lol (I was not a dev at that job I just made internal software for the IT team as a side gig lol)
    {
        public string Version { get; set; } = "0.7";
        public bool IsBeta { get; set; } = true;
        public bool IsUrgent { get; set; } = false;
        public string? InstallPath { get; set; }
    }
}
