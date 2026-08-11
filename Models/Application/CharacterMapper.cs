using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dujahit.Models.Application
{
    public static class CharacterMapper
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

        public static CharacterRuntime ToRuntime(Character row)
        {
            var rt = new CharacterRuntime
            {
                Id = row.Id,
                CampaignId = row.CampaignId,
                OwnerUserId = row.OwnerUserId,
                Name = row.Name,
                RaceId = row.RaceId,
                SubraceId = row.SubraceId,
                ClassId = row.ClassId,
                Level = row.Level,
                CurrentHp = row.CurrentHp,
                MaxHp = row.MaxHp,
                CharacterKind = string.IsNullOrEmpty(row.CharacterKind) ? "pc" : row.CharacterKind,
                CreatedAt = row.CreatedAt,
                AbilityScores = string.IsNullOrWhiteSpace(row.AbilityScoresJson)
                    ? new AbilityScores()
                    : JsonSerializer.Deserialize<AbilityScores>(row.AbilityScoresJson) ?? new AbilityScores(),
                ClassLevels = ClassLevels.Read(row.ClassLevelsJson, row.ClassId, row.Level)
            };

            if (!string.IsNullOrWhiteSpace(row.StateJson))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<CharacterState>(row.StateJson);
                    if (state != null) ApplyState(rt, state);
                }
                catch (JsonException) { }
            }

            if (!string.IsNullOrWhiteSpace(row.InventoryJson))
            {
                try
                {
                    var inv = JsonSerializer.Deserialize<InventoryBlob>(row.InventoryJson);
                    if (inv != null) rt.InventoryInstanceIds = inv.InstanceIds;
                }
                catch (JsonException)
                {
                    try
                    {
                        var ids = JsonSerializer.Deserialize<List<string>>(row.InventoryJson);
                        if (ids != null) rt.InventoryInstanceIds = ids;
                    }
                    catch (JsonException) { }
                }
            }

            return rt;
        }

        public static Character ToRow(CharacterRuntime rt)
        {
            return new Character
            {
                Id = rt.Id,
                CampaignId = rt.CampaignId,
                OwnerUserId = rt.OwnerUserId,
                Name = rt.Name,
                RaceId = rt.RaceId,
                SubraceId = rt.SubraceId,
                ClassId = rt.ClassId,
                Level = rt.Level,
                CurrentHp = rt.CurrentHp,
                MaxHp = rt.MaxHp,
                CharacterKind = string.IsNullOrEmpty(rt.CharacterKind) ? "pc" : rt.CharacterKind,
                CreatedAt = rt.CreatedAt,
                AbilityScoresJson = JsonSerializer.Serialize(rt.AbilityScores, _opts),
                StateJson = JsonSerializer.Serialize(ExtractState(rt), _opts),
                InventoryJson = JsonSerializer.Serialize(
                    new InventoryBlob { InstanceIds = rt.InventoryInstanceIds }, _opts)
            };
        }

        private static void ApplyState(CharacterRuntime rt, CharacterState s)
        {
            rt.TempHp = s.TempHp;
            rt.ArmorClass = s.ArmorClass == 0 ? (App.PM?.Rules?.ArmorClassBase ?? 10) : s.ArmorClass;
            rt.Inspiration = s.Inspiration;
            rt.UsesMilestone = s.UsesMilestone;
            rt.CurrentXp = s.CurrentXp;
            rt.MilestoneNote = s.MilestoneNote ?? "";
            rt.ProficientSkills = s.ProficientSkills ?? new();
            rt.ExpertiseSkills = s.ExpertiseSkills ?? new();
            rt.ProficientSaves = s.ProficientSaves ?? new();
            rt.ProficientTools = s.ProficientTools ?? new();
            rt.Conditions = s.Conditions ?? new();
            rt.Senses = s.Senses ?? new();
            rt.Features = s.Features ?? new();
            rt.Proficiencies = s.Proficiencies ?? new();
            rt.Languages = s.Languages ?? new();
            rt.Backstory = s.Backstory ?? "";
            rt.BackgroundId = s.BackgroundId ?? "";
            rt.Notes = s.Notes ?? "";
            rt.SpellSlotsMax = s.SpellSlotsMax ?? new();
            rt.SpellSlotsUsed = s.SpellSlotsUsed ?? new();
            rt.PreparedSpellIds = s.PreparedSpellIds ?? new();
            rt.GrantedSpellIds = s.GrantedSpellIds ?? new();
            rt.HitDiceRemaining = s.HitDiceRemaining;
            rt.HitDiceInitialized = s.HitDiceInitialized;
            rt.WeaponProficiency = s.WeaponProficiency ?? new();
            if (!rt.HitDiceInitialized)
            {
                if (rt.HitDiceRemaining <= 0) rt.HitDiceRemaining = App.PM?.Rules?.MaxHitDiceForLevel(rt.Level) ?? rt.Level;
                rt.HitDiceInitialized = true;
            }
            rt.DeathSaveSuccesses = s.DeathSaveSuccesses;
            rt.DeathSaveFailures = s.DeathSaveFailures;
            rt.ExhaustionLevel = s.ExhaustionLevel;
            rt.Speed = s.Speed;
            rt.Concentration = s.Concentration;
            rt.ConcentrationSpell = s.ConcentrationSpell ?? "";
            rt.ResourcesMax = s.ResourcesMax ?? new();
            rt.ResourcesUsed = s.ResourcesUsed ?? new();
            rt.Wallet = s.Wallet ?? new();
            rt.LevelChoices = s.LevelChoices ?? new();
            rt.ColorHex = s.ColorHex ?? "";
            rt.TokenImagePath = s.TokenImagePath;
            rt.AbilityBumps = s.AbilityBumps;
            rt.LevelUpBumps = s.LevelUpBumps ?? new();
            rt.CreationAsiPicks = s.CreationAsiPicks ?? new();
            rt.AnsweredLevelChoices = s.AnsweredLevelChoices ?? new();
            rt.LevelChoicesRecorded = s.LevelChoicesRecorded;
            rt.BgSpread = s.BgSpread;
            rt.BgPlusTwo = s.BgPlusTwo ?? "";
            rt.BgPlusOne = s.BgPlusOne ?? "";
        }

        private static CharacterState ExtractState(CharacterRuntime rt) => new()
        {
            TempHp = rt.TempHp,
            ArmorClass = rt.ArmorClass,
            Inspiration = rt.Inspiration,
            UsesMilestone = rt.UsesMilestone,
            CurrentXp = rt.CurrentXp,
            MilestoneNote = rt.MilestoneNote,
            ProficientSkills = rt.ProficientSkills,
            ExpertiseSkills = rt.ExpertiseSkills,
            ProficientSaves = rt.ProficientSaves,
            ProficientTools = rt.ProficientTools,
            Conditions = rt.Conditions,
            Senses = rt.Senses,
            Features = rt.Features,
            Proficiencies = rt.Proficiencies,
            Languages = rt.Languages,
            Backstory = rt.Backstory,
            BackgroundId = rt.BackgroundId,
            Notes = rt.Notes,
            SpellSlotsMax = rt.SpellSlotsMax,
            SpellSlotsUsed = rt.SpellSlotsUsed,
            PreparedSpellIds = rt.PreparedSpellIds,
            GrantedSpellIds = rt.GrantedSpellIds,
            HitDiceRemaining = rt.HitDiceRemaining,
            HitDiceInitialized = rt.HitDiceInitialized,
            WeaponProficiency = rt.WeaponProficiency,
            DeathSaveSuccesses = rt.DeathSaveSuccesses,
            DeathSaveFailures = rt.DeathSaveFailures,
            ExhaustionLevel = rt.ExhaustionLevel,
            Speed = rt.Speed,
            Concentration = rt.Concentration,
            ConcentrationSpell = rt.ConcentrationSpell,
            ResourcesMax = rt.ResourcesMax,
            ResourcesUsed = rt.ResourcesUsed,
            Wallet = rt.Wallet,
            ColorHex = rt.ColorHex,
            TokenImagePath = rt.TokenImagePath,
            LevelChoices = rt.LevelChoices,
            AbilityBumps = rt.AbilityBumps,
            LevelUpBumps = rt.LevelUpBumps,
            CreationAsiPicks = rt.CreationAsiPicks,
            AnsweredLevelChoices = rt.AnsweredLevelChoices,
            LevelChoicesRecorded = rt.LevelChoicesRecorded,
            BgSpread = rt.BgSpread,
            BgPlusTwo = rt.BgPlusTwo,
            BgPlusOne = rt.BgPlusOne
        };

        private class CharacterState
        {
            public int TempHp { get; set; }
            public int ArmorClass { get; set; }
            public bool Inspiration { get; set; }
            public bool UsesMilestone { get; set; }
            public int CurrentXp { get; set; }
            public string? MilestoneNote { get; set; }
            public List<string>? ProficientSkills { get; set; }
            public List<string>? ExpertiseSkills { get; set; }
            public List<string>? ProficientSaves { get; set; }
            public List<string>? ProficientTools { get; set; }
            public List<string>? Conditions { get; set; }
            public List<string>? Senses { get; set; }
            public List<string>? Features { get; set; }
            public List<string>? Proficiencies { get; set; }
            public List<string>? Languages { get; set; }
            public string? Backstory { get; set; }
            public string? BackgroundId { get; set; }
            public string? Notes { get; set; }
            public Dictionary<int, int>? SpellSlotsMax { get; set; }
            public Dictionary<int, int>? SpellSlotsUsed { get; set; }
            public List<string>? PreparedSpellIds { get; set; }
            public List<string>? GrantedSpellIds { get; set; }
            public int HitDiceRemaining { get; set; }
            public bool HitDiceInitialized { get; set; }
            public Dictionary<string, bool>? WeaponProficiency { get; set; }
            public int DeathSaveSuccesses { get; set; }
            public int DeathSaveFailures { get; set; }
            public int ExhaustionLevel { get; set; }
            public int Speed { get; set; }
            public bool Concentration { get; set; }
            public string? ConcentrationSpell { get; set; }
            public Dictionary<string, int>? ResourcesMax { get; set; }
            public Dictionary<string, int>? ResourcesUsed { get; set; }
            public Dictionary<string, long>? Wallet { get; set; }
            public Dictionary<string, List<string>>? LevelChoices { get; set; }
            public string? ColorHex { get; set; }
            public string? TokenImagePath { get; set; }
            public Dictionary<string, int>? AbilityBumps { get; set; }
            public Dictionary<string, int>? LevelUpBumps { get; set; }
            public List<string>? CreationAsiPicks { get; set; }
            public List<string>? AnsweredLevelChoices { get; set; }
            public bool LevelChoicesRecorded { get; set; }
            public bool BgSpread { get; set; }
            public string? BgPlusTwo { get; set; }
            public string? BgPlusOne { get; set; }
        }

        private class InventoryBlob
        {
            public List<string> InstanceIds { get; set; } = new();
        }
    }
}
