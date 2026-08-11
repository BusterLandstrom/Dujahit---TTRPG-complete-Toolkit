using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class CampaignDashboardViewModel : ViewModelBase
    {
        public ObservableCollection<DashboardPlayer> Players { get; } = new();
        public ObservableCollection<DashboardCharacter> Characters { get; } = new();
        public ObservableCollection<DashboardStickyNote> PinnedNotes { get; } = new();

        private string _campaignName = "";
        public string CampaignName
        {
            get => _campaignName;
            private set => this.RaiseAndSetIfChanged(ref _campaignName, value);
        }

        private int _onlineCount;
        public int OnlineCount
        {
            get => _onlineCount;
            private set
            {
                this.RaiseAndSetIfChanged(ref _onlineCount, value);
                this.RaisePropertyChanged(nameof(PresenceLabel));
            }
        }

        public string PresenceLabel => $"{OnlineCount} online  ·  {Players.Count} in the party";

        public bool HasPlayers => Players.Count > 0;
        public bool HasCharacters => Characters.Count > 0;
        public bool HasPinnedNotes => PinnedNotes.Count > 0;

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public CampaignDashboardViewModel()
        {
            RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
            RefreshCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[Dashboard] refresh failed", ex));
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;

            CampaignName = App.PM.GetCampaignName();
            var campaignId = App.PM.GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return;

            var uid = App.PM.GetUID();
            var isDm = await App.PM.IsCurrentUserDmAsync();

            var online = App.PM.ComController?.OnlineUserIds
                ?.Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Players.Clear();
            Characters.Clear();
            PinnedNotes.Clear();

            await using var conn = await App.PM.DbManager.OpenAsync();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT cm.UserId, u.Username, cm.Role
                    FROM CampaignMembers cm
                    JOIN Users u ON u.Id = cm.UserId
                    WHERE cm.CampaignId = $cid
                    ORDER BY cm.Role, u.Username COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("$cid", campaignId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var userId = r.GetString(0);
                    Players.Add(new DashboardPlayer(
                        userId, r.GetString(1), r.GetString(2), online.Contains(userId)));
                }
            }

            var statsById = (await App.PM.LoadAllCharactersInCampaignAsync())
                .ToDictionary(rt => rt.Id);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT c.Id, c.Name, c.Level, c.CurrentHp, c.MaxHp,
                           r.Name, cl.Name, u.Username
                    FROM Characters c
                    LEFT JOIN Races r    ON r.Id  = c.RaceId
                    LEFT JOIN Classes cl ON cl.Id = c.ClassId
                    LEFT JOIN Users u    ON u.Id  = c.OwnerUserId
                    WHERE c.CampaignId = $cid AND c.CharacterKind = 'pc'
                    ORDER BY c.Name COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("$cid", campaignId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var id = r.GetString(0);
                    var level = r.GetInt32(2);

                    var armorClass = App.PM?.Rules?.ArmorClassBase ?? 10;
                    var passivePerception = App.PM?.Rules?.PassiveScoreBase ?? 10;
                    if (statsById.TryGetValue(id, out var rt))
                    {
                        armorClass = rt.ArmorClass;
                        var percName = App.PM?.Rules?.PerceptionSkill ?? "Perception";
                        var perDef = App.PM?.Rules?.Skills?.FirstOrDefault(s => string.Equals(s.Name, percName, StringComparison.OrdinalIgnoreCase));
                        var perAb = perDef?.Ability ?? "WIS";
                        var perAbId = App.PM?.Rules?.AbilityIdForShort(perAb) ?? "ability-wis";
                        int perScore = rt.AbilityScores.Get(perAbId);
                        var perMod = App.PM?.AbilityMod(perScore) ?? (int)Math.Floor((perScore - 10) / 2.0);
                        var profBonus = App.PM?.ProficiencyBonusForLevel(rt.Level) ?? (2 + (rt.Level - 1) / 4);
                        var perceptionProf = rt.ProficientSkills.Any(s => string.Equals(s, percName, StringComparison.OrdinalIgnoreCase));
                        var perceptionExp = rt.ExpertiseSkills.Any(s => string.Equals(s, percName, StringComparison.OrdinalIgnoreCase));
                        passivePerception = (App.PM?.Rules.PassiveScoreBase ?? 10) + perMod + (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(perceptionProf, perceptionExp), profBonus);
                    }

                    Characters.Add(new DashboardCharacter(
                        id, r.GetString(1), level, r.GetInt32(3), r.GetInt32(4),
                        r.IsDBNull(5) ? null : r.GetString(5),
                        r.IsDBNull(6) ? null : r.GetString(6),
                        r.IsDBNull(7) ? null : r.GetString(7),
                        armorClass, passivePerception));
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = isDm
                    ? @"SELECT Id, Title, ContentMarkdown FROM NotePages
                        WHERE CampaignId = $cid AND PinnedToDashboard = 1
                        ORDER BY UpdatedAt DESC;"
                    : @"SELECT Id, Title, ContentMarkdown FROM NotePages
                        WHERE CampaignId = $cid AND PinnedToDashboard = 1
                          AND (OwnerUserId = $uid OR Scope = 'campaign_story')
                        ORDER BY UpdatedAt DESC;";
                cmd.Parameters.AddWithValue("$cid", campaignId);
                if (!isDm) cmd.Parameters.AddWithValue("$uid", uid);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    PinnedNotes.Add(new DashboardStickyNote(
                        r.GetString(0), r.GetString(1),
                        r.IsDBNull(2) ? "" : r.GetString(2),
                        UnpinAsync));
                }
            }

            OnlineCount = Players.Count(p => p.IsOnline);
            this.RaisePropertyChanged(nameof(HasPlayers));
            this.RaisePropertyChanged(nameof(HasCharacters));
            this.RaisePropertyChanged(nameof(HasPinnedNotes));
            this.RaisePropertyChanged(nameof(PresenceLabel));
        }

        private async Task UnpinAsync(DashboardStickyNote note)
        {
            if (App.PM == null || note == null) return;

            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE NotePages SET PinnedToDashboard = 0 WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", note.Id);
            await cmd.ExecuteNonQueryAsync();

            PinnedNotes.Remove(note);
            this.RaisePropertyChanged(nameof(HasPinnedNotes));
        }
    }

    public class DashboardPlayer
    {
        public string UserId { get; }
        public string Username { get; }
        public string Role { get; }
        public bool IsOnline { get; }

        public string RoleLabel => string.IsNullOrEmpty(Role)
            ? ""
            : char.ToUpper(Role[0]) + Role.Substring(1);

        public string Initial => string.IsNullOrEmpty(Username)
            ? "?"
            : Username.Substring(0, 1).ToUpperInvariant();

        public DashboardPlayer(string userId, string username, string role, bool isOnline)
        {
            UserId = userId;
            Username = username;
            Role = role ?? "";
            IsOnline = isOnline;
        }
    }

    public class DashboardCharacter
    {
        public string Id { get; }
        public string Name { get; }
        public int Level { get; }
        public int CurrentHp { get; }
        public int MaxHp { get; }
        public string? RaceName { get; }
        public string? ClassName { get; }
        public string? OwnerName { get; }

        public string SubLine
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(RaceName)) parts.Add(RaceName);
                if (!string.IsNullOrEmpty(ClassName)) parts.Add(ClassName);
                parts.Add($"Lv {Level}");
                return string.Join("  ·  ", parts);
            }
        }

        public string HpLabel => $"{CurrentHp} / {MaxHp} HP";
        public string OwnerLabel => string.IsNullOrEmpty(OwnerName) ? "Unassigned" : OwnerName;

        public int ArmorClass { get; }
        public int PassivePerception { get; }
        public string AcLabel => $"AC {ArmorClass}";
        public string PpLabel => $"PP {PassivePerception}";

        public DashboardCharacter(string id, string name, int level, int currentHp, int maxHp,
                                  string? raceName, string? className, string? ownerName,
                                  int armorClass, int passivePerception)
        {
            Id = id; Name = name; Level = level; CurrentHp = currentHp; MaxHp = maxHp;
            RaceName = raceName; ClassName = className; OwnerName = ownerName;
            ArmorClass = armorClass; PassivePerception = passivePerception;
        }
    }

    public class DashboardStickyNote : ViewModelBase
    {
        public string Id { get; }
        public string Title { get; }
        public string Preview { get; }
        public ReactiveCommand<Unit, Unit> UnpinCommand { get; }

        public DashboardStickyNote(string id, string title, string content, Func<DashboardStickyNote, Task> onUnpin)
        {
            Id = id;
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
            Preview = MakePreview(content);
            UnpinCommand = ReactiveCommand.CreateFromTask(() => onUnpin(this));
            UnpinCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[Dashboard] sticky unpin failed", ex));
        }

        private static string MakePreview(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            var clean = content.Replace("\r", "").Trim();
            return clean.Length > 600 ? clean.Substring(0, 600) + "..." : clean;
        }
    }
}
