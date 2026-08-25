using Dujahit.Models.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dujahit.Models.UI;
using Dujahit.Models.Database;

namespace Dujahit.Models
{
    public class Race
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Size { get; set; } = "";
        public int Speed { get; set; }
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
    }

    public class Subrace
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ParentRaceId { get; set; } = "";
        public string? Description { get; set; }
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
    }

    public class Class
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string HitDiceId { get; set; } = "";
        public string PrimaryAbility { get; set; } = "";
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
    }

    public class Spell
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public string School { get; set; } = "";
        public string CastingTime { get; set; } = "";
        public string Duration { get; set; } = "";
        public string Range { get; set; } = "";
        public bool Concentration { get; set; }
        public bool Ritual { get; set; }
        public string Description { get; set; } = "";
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
    }

    public record BackstoryOption(string Title, string Description);
    public record BackgroundOption(string Id, string Name, string Description, List<string> SkillIds, List<string> AbilityIds, List<string> FeatIds);
    public record LanguageOption(string Id, string Name, string Script, string Description);

    public class Trait
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string? Slug { get; set; }
        public string Tags { get; set; } = "[]";
    }

    public class Condition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
    }

    public class Background
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
    }

    public class Feat
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Prerequisite { get; set; }
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
    }

    public class Item
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ItemType { get; set; } = "Generic";
        public string Source { get; set; } = "srd";
        public string? OwnerUserId { get; set; }
        public string? TemplateId { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public string UpdatedAt { get; set; } = "";
        public string DataJson { get; set; } = "{}";
        public string? Slug { get; set; }
        public string Tags { get; set; } = "[]";
    }

    public class CampaignTemplate
    {
        public string TemplateId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string SystemId { get; set; } = "";
        public int Version { get; set; } = 1;
        public string ImportedAt { get; set; } = "";
        public string JsonContent { get; set; } = "{}";
    }

    public class Character
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? OwnerUserId { get; set; }
        public string Name { get; set; } = "";
        public string? RaceId { get; set; }
        public string? SubraceId { get; set; }
        public string? ClassId { get; set; }
        public int Level { get; set; } = 1;
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public string AbilityScoresJson { get; set; } = "{}";
        public string? InventoryJson { get; set; }
        public string? StateJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CharacterKind { get; set; } = "pc";
        public string? Slug { get; set; }
        public string Tags { get; set; } = "[]";
        public bool VisibleToAll { get; set; }
        public string? ClassLevelsJson { get; set; }
    }

    public record ClassLevel(string ClassId, int Level);

    public static class ClassLevels
    {
        // Empty column means it never multiclassed, so the whole level sits on the starting class.
        public static List<ClassLevel> Read(string? json, string? classId, int level)
        {
            var parsed = Parse(json);
            if (parsed.Count > 0) return parsed;
            return string.IsNullOrWhiteSpace(classId) ? new List<ClassLevel>() : new List<ClassLevel> { new(classId!, Math.Max(1, level)) };
        }

        public static List<ClassLevel> Parse(string? json)
        {
            var result = new List<ClassLevel>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var raw = JsonSerializer.Deserialize<List<ClassLevel>>(json!);
                if (raw == null) return result;
                foreach (var c in raw)
                    if (!string.IsNullOrWhiteSpace(c.ClassId) && c.Level > 0 && !result.Any(x => string.Equals(x.ClassId, c.ClassId, StringComparison.OrdinalIgnoreCase)))
                        result.Add(c);
            }
            catch (JsonException) { }
            return result;
        }

        public static string Write(IEnumerable<ClassLevel> classes) => JsonSerializer.Serialize(classes.Where(c => c.Level > 0).ToList());

        public static int TotalLevel(IEnumerable<ClassLevel> classes) => classes.Sum(c => c.Level);

        // The class you started as. Keeps the hit die and the saves even if you dabble somewhere else later.
        public static string? PrimaryClassId(IEnumerable<ClassLevel> classes) => classes.FirstOrDefault()?.ClassId;
    }

    public enum CreatureSize
    {
        Tiny,
        Small,
        Medium,
        Large,
        Huge,
        Gargantuan
    }

    public static class CreatureSizeExtensions
    {
        public static double ToPixels(this CreatureSize size)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            return rules.SquaresForSize(size.ToString()) * 50.0;
        }
    }

    public class ClassResourceDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ResetOn { get; set; } = "long";
        public int Max { get; set; }
    }

    public class CharacterRuntime
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? OwnerUserId { get; set; }
        public string Name { get; set; } = "";
        public string? RaceId { get; set; }
        public string? SubraceId { get; set; }
        public string? ClassId { get; set; }
        public int Level { get; set; } = 1;
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public string CharacterKind { get; set; } = "pc";

        // Read fills this in even for a single class character, so nothing downstream has to special case one
        public List<ClassLevel> ClassLevels { get; set; } = new();
        public AbilityScores AbilityScores { get; set; } = new();
        public DateTime CreatedAt { get; set; }

        public int TempHp { get; set; }
        public int ArmorClass { get; set; } = 10;
        public bool Inspiration { get; set; }
        public bool UsesMilestone { get; set; }
        public int CurrentXp { get; set; }
        public string MilestoneNote { get; set; } = "";
        public List<string> ProficientSkills { get; set; } = new();
        public List<string> ExpertiseSkills { get; set; } = new();
        public List<string> ProficientSaves { get; set; } = new();
        public List<string> ProficientTools { get; set; } = new();
        public List<string> Conditions { get; set; } = new();
        public List<string> Senses { get; set; } = new();
        public List<string> Features { get; set; } = new();
        public List<string> Proficiencies { get; set; } = new();
        public Dictionary<string, List<string>> LevelChoices { get; set; } = new();
        public List<string> Languages { get; set; } = new();
        public string Backstory { get; set; } = "";
        public string BackgroundId { get; set; } = "";
        public string Notes { get; set; } = "";

        public string ColorHex { get; set; } = "";
        public string? TokenImagePath { get; set; }

        public Dictionary<int, int> SpellSlotsMax { get; set; } = new();
        public Dictionary<int, int> SpellSlotsUsed { get; set; } = new();
        public List<string> PreparedSpellIds { get; set; } = new();
        public List<string> GrantedSpellIds { get; set; } = new();
        public int HitDiceRemaining { get; set; }
        public bool HitDiceInitialized { get; set; }
        public Dictionary<string, bool> WeaponProficiency { get; set; } = new();
        public int DeathSaveSuccesses { get; set; }
        public int DeathSaveFailures { get; set; }
        public int ExhaustionLevel { get; set; }
        public int Speed { get; set; }
        public bool Concentration { get; set; }
        public string ConcentrationSpell { get; set; } = "";
        public Dictionary<string, int> ResourcesMax { get; set; } = new();
        public Dictionary<string, int> ResourcesUsed { get; set; } = new();

        public List<string> InventoryInstanceIds { get; set; } = new();

        public Dictionary<string, long> Wallet { get; set; } = new();

        public Dictionary<string, int>? AbilityBumps { get; set; }
        public Dictionary<string, int> LevelUpBumps { get; set; } = new();
        public List<string> CreationAsiPicks { get; set; } = new();
        public List<string> AnsweredLevelChoices { get; set; } = new();
        public bool LevelChoicesRecorded { get; set; }
        public bool BgSpread { get; set; }
        public string BgPlusTwo { get; set; } = "";
        public string BgPlusOne { get; set; } = "";
    }
    // Keyed by ability id so a template can have however many abilities it wants, the six named getters are just there for all the old 5e code that reads them directly
    [JsonConverter(typeof(AbilityScoresConverter))]
    public class AbilityScores
    {
        public Dictionary<string, int> Scores { get; } = new();
        public int Get(string id) => Scores.TryGetValue(id, out var v) ? v : 10;
        public void Set(string id, int value) => Scores[id] = value;

        public int Strength { get => Get("ability-str"); set => Set("ability-str", value); }
        public int Dexterity { get => Get("ability-dex"); set => Set("ability-dex", value); }
        public int Constitution { get => Get("ability-con"); set => Set("ability-con", value); }
        public int Intelligence { get => Get("ability-int"); set => Set("ability-int", value); }
        public int Wisdom { get => Get("ability-wis"); set => Set("ability-wis", value); }
        public int Charisma { get => Get("ability-cha"); set => Set("ability-cha", value); }
    }

    public class AbilityScoresConverter : JsonConverter<AbilityScores>
    {
        // Old saves used the six C# property names so I map them to ids on read, otherwise everything resets. New ones are id-keyed already.
        private static readonly Dictionary<string, string> _legacy = new()
        {
            { "Strength", "ability-str" }, { "Dexterity", "ability-dex" }, { "Constitution", "ability-con" },
            { "Intelligence", "ability-int" }, { "Wisdom", "ability-wis" }, { "Charisma", "ability-cha" }
        };

        public override AbilityScores Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var a = new AbilityScores();
            if (reader.TokenType != JsonTokenType.StartObject) { reader.Skip(); return a; }
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var key = reader.GetString() ?? "";
                reader.Read();
                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var v))
                    a.Scores[_legacy.TryGetValue(key, out var mapped) ? mapped : key] = v;
            }
            return a;
        }

        // Flat, no wrapper object. Wrapping it throws the reader out and the sheet just dies.
        public override void Write(Utf8JsonWriter writer, AbilityScores value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kv in value.Scores) writer.WriteNumber(kv.Key, kv.Value);
            writer.WriteEndObject();
        }
    }

    public class CharacterBonuses
    {
        public Dictionary<string, int> Ability { get; } = new();
        public int ArmorClass { get; set; }
        public int AttackRoll { get; set; }
        public int DamageRoll { get; set; }
        public int SavingThrow { get; set; }
        public int Initiative { get; set; }
        public int MaxHpPerLevel { get; set; }

        public int ExtraAttacks { get; set; }
        public bool OffHandAbilityMod { get; set; }
        public List<string> Resistances { get; } = new();
        public HashSet<string> AdvantageOn { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Proficiencies { get; } = new();
        public List<RiderRule> Riders { get; } = new();
        public List<ConditionalBonus> Conditional { get; } = new();
        public List<GrantedReaction> Reactions { get; } = new();

        public int ConditionalBonusFor(string target, IEnumerable<string> activeConditions, int currentHp, int maxHp)
        {
            var total = 0;
            foreach (var c in Conditional)
                if (string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase) && c.Applies(activeConditions, currentHp, maxHp))
                    total += c.Value;
            return total;
        }

        public int AbilityBonus(string id) => Ability.TryGetValue(id, out var v) ? v : 0;

        public bool HasAdvantageOn(string what) => !string.IsNullOrWhiteSpace(what) && AdvantageOn.Contains(what);
    }

    public class ConditionalBonus
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Target { get; set; } = "";
        public int Value { get; set; }
        public string When { get; set; } = "always";
        public string WhenValue { get; set; } = "";
        public double HpFraction { get; set; }

        public bool Applies(IEnumerable<string> activeConditions, int currentHp, int maxHp)
        {
            switch ((When ?? "always").ToLowerInvariant())
            {
                case "always": return true;
                case "condition":
                    return !string.IsNullOrWhiteSpace(WhenValue)
                           && activeConditions.Any(c => string.Equals(c, WhenValue, StringComparison.OrdinalIgnoreCase));
                case "hp-below":
                    return maxHp > 0 && HpFraction > 0 && currentHp < maxHp * HpFraction;
                case "hp-full":
                    return maxHp > 0 && currentHp >= maxHp;
                default: return false;
            }
        }
    }

    public class GrantedReaction
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Cost { get; set; } = "reaction";
        public string UsesPer { get; set; } = "";
        public int Uses { get; set; }
    }

    public class RiderRule
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Dice { get; set; } = "";
        public int DicePerLevels { get; set; }
        public string When { get; set; } = "always";
        public string DamageType { get; set; } = "";
        public bool OncePerTurn { get; set; }

        public string DiceForLevel(int level)
        {
            if (string.IsNullOrWhiteSpace(Dice)) return "";
            if (DicePerLevels <= 0) return Dice;
            var idx = Dice.IndexOf('d');
            if (idx <= 0 || !int.TryParse(Dice[..idx], out var baseCount)) return Dice;
            var steps = (Math.Max(1, level) - 1) / DicePerLevels;
            return (baseCount + steps) + Dice[idx..];
        }
    }

    public sealed class CharacterListEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? OwnerUserId { get; set; }
        public int Level { get; set; }
        public string? RaceId { get; set; }
        public string? ClassId { get; set; }
        public string? RaceName { get; set; }
        public string? ClassName { get; set; }
        public bool VisibleToAll { get; set; }
    }

    public class ItemInstance
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string BaseItemId { get; set; } = "";
        public string? OwnerCharacterId { get; set; }
        public int Quantity { get; set; } = 1;
        public string? ParentInstanceId { get; set; }
        public string? CustomName { get; set; }
        public string? StateJson { get; set; }
    }

        public class Currency
    {
        public string Id { get; set; } = "";
        public string? TemplateId { get; set; }
        public string Name { get; set; } = "";
        public string Abbreviation { get; set; } = "";
        public bool IsBase { get; set; }
        public int EqualToBase { get; set; } = 1;
        public string? Color { get; set; }
        public string? IconSvg { get; set; }
        public int SortOrder { get; set; }
    }

    public class TradeLogEntry
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? FromCharacterId { get; set; }
        public string? ToCharacterId { get; set; }
        public string? FromUserId { get; set; }
        public string? ToUserId { get; set; }
        public string Summary { get; set; } = "";
        public string PayloadJson { get; set; } = "[]";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class GameLevel
    {
        public string Id { get; set; } = "";
        public int LevelValue { get; set; }
        public int XP { get; set; }
        public int Bonus { get; set; }
        public string CampaignId { get; set; } = "";
    }

    public class Note
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Content { get; set; } = "";
        public bool IsDmOnly { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Map
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public double Scale { get; set; } = 1.0;
        public GridKind GridKind { get; set; } = GridKind.Squares;
        public double GridOffsetX { get; set; }
        public double GridOffsetY { get; set; }
        public string MapPath { get; set; } = "";
        public bool PlayerVisible { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Handout
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public string HandoutPath { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MapFogState
    {
        public string MapId { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool DynamicVision { get; set; }
        public bool ClosesBehind { get; set; }
        public int Cols { get; set; }
        public int Rows { get; set; }
        public HashSet<(int Col, int Row)> Hidden { get; set; } = new();
        public HashSet<(int Col, int Row)> Seen { get; set; } = new();
    }

    public class MapDrawing
    {
        public string Id { get; set; } = "";
        public string MapId { get; set; } = "";
        public string? UserId { get; set; }
        public string StrokeDataJson { get; set; } = "{}";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class MapToken
    {
        public string Id { get; set; } = "";
        public string MapId { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? OwnerCharacterId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string TokenImagePath { get; set; } = "";
        public string? Label { get; set; }
        public double Scale { get; set; } = 1.0;
        public double Rotation { get; set; }
        public string SizeName { get; set; } = "Medium";
        public bool IsProp { get; set; }
        public bool Blocks { get; set; } = true;
        public bool BlocksSight { get; set; }
    }

    public class CampaignTokenAsset
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "image";
        public string? ImagePath { get; set; }
        public string? ColorHex { get; set; }
        public string? Glyph { get; set; }
        public string? MonsterKey { get; set; }
        public string SizeName { get; set; } = "Medium";
        public int? InitiativeOverride { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MapPing
    {
        public string Id { get; set; } = "";
        public string MapId { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string UserId { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ChatChannel
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ChatMessage
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string ChannelId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Sender { get; set; }
        public string TimeLabel
        {
            get
            {
                var t = Timestamp.Kind == DateTimeKind.Local ? Timestamp : DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc).ToLocalTime();
                return "[" + t.ToString("HH:mm") + "]";
            }
        }
    }

    public class Faction
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Color { get; set; }
        public double NodeX { get; set; }
        public double NodeY { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class FactionRelation
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string FromFactionId { get; set; } = "";
        public string ToFactionId { get; set; } = "";
        public string RelationType { get; set; } = "";
        public string? Notes { get; set; }
    }

    public class Mindmap
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? OwnerUserId { get; set; }
        public string Scope { get; set; } = "private";
        public string Title { get; set; } = "";
        public string? ColorHex { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MindmapShare
    {
        public string MindmapId { get; set; } = "";
        public string UserId { get; set; } = "";
        public DateTime SharedAt { get; set; } = DateTime.UtcNow;
    }

    public class MindmapNode
    {
        public string Id { get; set; } = "";
        public string MindmapId { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Kind { get; set; } = "blank";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string? ColorHex { get; set; }
        public double NodeX { get; set; }
        public double NodeY { get; set; }
        public string? Slug { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MindmapLink
    {
        public string Id { get; set; } = "";
        public string MindmapId { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string FromNodeId { get; set; } = "";
        public string ToNodeId { get; set; } = "";
        public string? Label { get; set; }
        public string RelationType { get; set; } = "";
    }

    public class MindmapSyncPayload
    {
        public Mindmap Map { get; set; } = new();
        public List<MindmapNode> Nodes { get; set; } = new();
        public List<MindmapLink> Links { get; set; } = new();
    }

    public record MindmapNodeOp(string MapId, MindmapNode Node);
    public record MindmapNodeMoveOp(string MapId, string NodeId, double X, double Y);
    public record MindmapNodeDeleteOp(string MapId, string NodeId);
    public record MindmapLinkOp(string MapId, MindmapLink Link);
    public record MindmapLinkDeleteOp(string MapId, string LinkId);

    public record ItemPopupRequest(string Id, string Name, string ItemType, string DataJson);

    public class SessionLogEntry
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? SessionId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ActorUserId { get; set; } = "";
        public string ActorName { get; set; } = "";
        public string EventType { get; set; } = "";
        public string Summary { get; set; } = "";
        public string? DetailJson { get; set; }
    }

    public class SearchHit
    {
        public string Type { get; set; } = "";
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";

        public SearchHit() { }

        public SearchHit(string type, string id, string title, string subtitle)
        {
            Type = type;
            Id = id;
            Title = title;
            Subtitle = subtitle;
        }
    }

    public class CalendarEvent
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "session";
        public string? EventDate { get; set; }
        public string? InWorldDate { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TimelineEvent
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? InWorldDate { get; set; }
        public double SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SoundClip
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "sfx";
        public string FileName { get; set; } = "";
        public bool IsFavourite { get; set; }
        public double SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class RandomTableEntry
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public string Text { get; set; } = "";
    }

    public class RandomTable
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public string DiceExpression { get; set; } = "";
        public List<RandomTableEntry> Entries { get; set; } = new();
        public bool IsTemplate { get; set; } // Comes off the rulebook json, so there is no db row to edit or delete.
    }

    public class CharacterExportBundle
    {
        public int Format { get; set; } = 2;
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public Character Character { get; set; } = new();
        public List<ItemInstance> Instances { get; set; } = new();

        public List<Item> CustomItems { get; set; } = new();
        public List<Spell> CustomSpells { get; set; } = new();
        public List<Race> CustomRaces { get; set; } = new();
        public List<Subrace> CustomSubraces { get; set; } = new();
        public List<Class> CustomClasses { get; set; } = new();
    }

    public class CampaignExportBundle
    {
        public int Format { get; set; } = 2;
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public string CampaignName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Port { get; set; } = "5555";
        public string RulesVersion { get; set; } = "both";
        public string? CombatSettingsJson { get; set; }
        public string TemplateId { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public string TemplateSystemId { get; set; } = "";
        public int TemplateVersion { get; set; } = 1;
        public string TemplateJsonContent { get; set; } = "";

        public List<Item> CustomItems { get; set; } = new();
        public List<Spell> CustomSpells { get; set; } = new();
        public List<Race> CustomRaces { get; set; } = new();
        public List<Subrace> CustomSubraces { get; set; } = new();
        public List<Class> CustomClasses { get; set; } = new();
        public List<Trait> CustomTraits { get; set; } = new();
        public List<Currency> Currencies { get; set; } = new();
        public List<Character> Characters { get; set; } = new();
        public List<ItemInstance> Instances { get; set; } = new();

        public List<ExportedMap> Maps { get; set; } = new();
        public List<ExportedHandout> Handouts { get; set; } = new();
        public List<ExportedNotePage> NotePages { get; set; } = new();
    }

    public class ExportedMap
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public double Scale { get; set; } = 1.0;
        public string GridKind { get; set; } = "Squares";
        public bool PlayerVisible { get; set; }
        public bool WallsEnabled { get; set; }
        public string WallsJson { get; set; } = "[]";
        public string DifficultTerrainJson { get; set; } = "[]";
        public string MapObjectsJson { get; set; } = "[]";
        public bool FogEnabled { get; set; }
        public bool FogDynamicVision { get; set; }
        public bool FogClosesBehind { get; set; }
        public int FogCols { get; set; }
        public int FogRows { get; set; }
        public string FogHiddenCells { get; set; } = "";
        public string FogSeenCells { get; set; } = "";
        public List<ExportedMapToken> Tokens { get; set; } = new();
        public List<string> Drawings { get; set; } = new();
        public string? ImageBase64 { get; set; }
        public string? ImageExtension { get; set; }
    }

    public class ExportedMapToken
    {
        public string Id { get; set; } = "";
        public string? OwnerCharacterId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string? Label { get; set; }
        public double Scale { get; set; } = 1.0;
        public double Rotation { get; set; }
        public string SizeName { get; set; } = "Medium";
        public bool IsProp { get; set; }
        public bool Blocks { get; set; } = true;
        public bool BlocksSight { get; set; }
        public string? ImageBase64 { get; set; }
        public string? ImageExtension { get; set; }
    }

    public class ExportedHandout
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Base64 { get; set; }
        public string Extension { get; set; } = "";
    }

    public class ExportedNotePage
    {
        public string Id { get; set; } = "";
        public string? ParentPageId { get; set; }
        public string Scope { get; set; } = "campaign";
        public string Title { get; set; } = "";
        public string? Slug { get; set; }
        public string? Icon { get; set; }
        public string ContentMarkdown { get; set; } = "";
        public int SortOrder { get; set; }
        public bool PinnedToDashboard { get; set; }
    }

    public class User
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Campaign
    {
        public string Id { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastModified { get; set; }
        public string Port { get; set; } = "5555";

        public bool IsRemote { get; set; }
        public string? HostAddress { get; set; }
        public string? JoinCode { get; set; }
    }

    public class CampaignMember
    {
        public string CampaignId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Role { get; set; } = "player";
        public string? CharacterId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public string? Username { get; set; }
        public string? Color { get; set; }
    }

    public class CampaignBootstrapPayload
    {
        public string CampaignId { get; set; } = "";
        public string CampaignName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Port { get; set; } = "";
        public string AssignedColor { get; set; } = "";
        public string SessionToken { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public string TemplateSystemId { get; set; } = "";
        public int TemplateVersion { get; set; } = 1;
        public string TemplateJsonContent { get; set; } = "";

        public List<Race> Races { get; set; } = new();
        public List<Subrace> Subraces { get; set; } = new();
        public List<Class> Classes { get; set; } = new();
        public List<Spell> Spells { get; set; } = new();
        public List<Item> Items { get; set; } = new();
        public List<Trait> Traits { get; set; } = new();
        public List<CampaignMember> Members { get; set; } = new();
        public List<string> OnlineUserIds { get; set; } = new();
        public List<Character> Characters { get; set; } = new();
        public Character? MyCharacter { get; set; }
        public Map? ActiveMap { get; set; }
        public List<MapToken> Tokens { get; set; } = new();
        public List<ChatChannel> Channels { get; set; } = new();
        public List<NotePage> NotePages { get; set; } = new();
        public List<NotePageShare> NoteShares { get; set; } = new();
        public List<Currency> Currencies { get; set; } = new();
        public List<ItemInstance> ItemInstances { get; set; } = new();
        public long LastChangeId { get; set; }
    }

    public class ChangeNotification
    {
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public string ChangeType { get; set; } = "";
        public int RevisionNumber { get; set; }
        public string? Payload { get; set; }
        public long ChangeId { get; set; }
    }

    public class ChangeLogEntry
    {
        public long ChangeId { get; set; }
        public string CampaignId { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public string ChangeType { get; set; } = "";
        public int RevisionNumber { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Payload { get; set; }
    }

    public class Encounter
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string? MapId { get; set; }
        public string? Name { get; set; }
        public int Round { get; set; }
        public string? ActiveCombatantId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class EncounterCombatant
    {
        public string Id { get; set; } = "";
        public string EncounterId { get; set; } = "";
        public string? CharacterId { get; set; }
        public string? TokenId { get; set; }
        public string Name { get; set; } = "";
        public int Initiative { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public bool IsPlayerCharacter { get; set; }
        public bool RevealExactHp { get; set; }
        public int SortOrder { get; set; }
        public string ConditionsJson { get; set; } = "[]";
        public int MaxActions { get; set; } = 1;
        public int ActionsRemaining { get; set; } = 1;
        public int MaxBonusActions { get; set; } = 1;
        public int BonusActionsRemaining { get; set; } = 1;
        public string SpellSlotsJson { get; set; } = "";
        public bool Concentration { get; set; }
        public int DeathSaveSuccesses { get; set; }
        public int DeathSaveFailures { get; set; }
        public string AttacksJson { get; set; } = "";
        public bool IsFriendly { get; set; }
        public string ExtrasJson { get; set; } = "";
    }

    public class EncounterPreset
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Notes { get; set; }
        public List<EncounterPresetEntry> Monsters { get; set; } = new();
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class EncounterPresetEntry
    {
        public string MonsterId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Cr { get; set; } = "";
        public int Xp { get; set; }
        public int Count { get; set; } = 1;
        // Empty falls back to the catalog at spawn. A list here is the dm's override.
        public List<MonsterAttackOption> Attacks { get; set; } = new();
    }

    public class DiceRoll
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
        public string Expression { get; set; } = "";
        public int Total { get; set; }
        public string Breakdown { get; set; } = "";
        public string? Label { get; set; }
        public bool IsPrivate { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class LevelChoice
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public int ChooseCount { get; set; } = 1;
        public string StoreAs { get; set; } = "";
        public int Level { get; set; } = 1;
        public List<LevelChoiceOption> Options { get; set; } = new();
    }

    public record LevelChoiceOption(string Id, string Name)
    {
        public string? Description { get; init; }
        public string? Mechanics { get; init; }
    }
}
