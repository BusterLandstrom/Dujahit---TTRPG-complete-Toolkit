using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dujahit.Models
{
    public class SummonRule
    {
        public string MonsterId { get; set; } = "";
        public int Count { get; set; } = 1;
        public Dictionary<int, int> CountBySlotLevel { get; } = new();
        public string Controller { get; set; } = "caster";

        // Upcast conjure animals brings more wolves, so the biggest slot at or below what you spent wins.
        public int CountForSlot(int slotLevel)
        {
            if (CountBySlotLevel.Count == 0) return Math.Max(1, Count);
            var best = 0;
            foreach (var kv in CountBySlotLevel)
                if (kv.Key <= slotLevel && kv.Key > best) best = kv.Key;
            return best == 0 ? Math.Max(1, Count) : Math.Max(1, CountBySlotLevel[best]);
        }
    }

    public class CasterContribution
    {
        public int Divisor { get; set; } = 1;
        public bool RoundUp { get; set; }
        public bool Excluded { get; set; }
    }

    public class GameRules
    {
        public Dictionary<int, int> LevelBonus { get; } = new();
        public Dictionary<int, int> LevelXp { get; } = new();

        // Sections the template left out, so the engine is running on a built in default for them, surfaced once at load instead of silently.
        public List<string> DefaultedSections { get; } = new();

        public int MaxLevel { get; set; } = 20;

        public int AbilityModBaseline { get; set; } = 10;
        public int AbilityModDivisor { get; set; } = 2;

        public int DeathSaveSuccessesToStabilize { get; set; } = 3;
        public int DeathSaveFailuresToDie { get; set; } = 3;
        public int DeathSaveThreshold { get; set; } = 10;
        public int DeathSaveCritHeal { get; set; } = 1;
        public int DeathSaveFumbleFailures { get; set; } = 2;
        public int ConcentrationDcFloor { get; set; } = 10;
        public int ConcentrationDcDivisor { get; set; } = 2;
        public int ConcentrationDcCap { get; set; } = 30;

        public int SpellSaveDcBase { get; set; } = 8;
        public int PassiveScoreBase { get; set; } = 10;
        public string PerceptionSkill { get; set; } = "Perception";

        public int DefaultSpeed { get; set; } = 30;
        public double DifficultTerrainMultiplier { get; set; } = 2.0;
        public Dictionary<string, TerrainCostRule> TerrainCosts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double StandFromProneSpeedFraction { get; set; } = 0.5;
        public string RechargeDie { get; set; } = "d6";
        public Dictionary<string, HazardRule> Hazards { get; } = new(StringComparer.OrdinalIgnoreCase);

        public double TerrainMultiplier(string terrain)
        {
            if (string.IsNullOrWhiteSpace(terrain)) return 1.0;
            return TerrainCosts.TryGetValue(terrain, out var rule) ? rule.Multiplier : DifficultTerrainMultiplier;
        }
        public double DashMultiplier { get; set; } = 2.0;
        public bool EnforceMovementBudget { get; set; } = true;
        public bool DmIgnoresMovementBudget { get; set; }
        public string JumpAbility { get; set; } = "str";
        public double JumpScoreMultiplier { get; set; } = 1.0;
        public int CoverHalfBonus { get; set; } = 2;
        public int CoverThreeQuartersBonus { get; set; } = 5;
        public bool LongRangeDisadvantage { get; set; } = true;
        public string CoreRollExpression { get; set; } = "";
        public string RollDirection { get; set; } = "high";
        public int ExplodingDiceLimit { get; set; } = 20;

        public bool RollsLow => string.Equals(RollDirection, "low", StringComparison.OrdinalIgnoreCase);

        public int MarginOf(int total, int dc) => RollsLow ? dc - total : total - dc;
        public int ExpertiseMultiplier { get; set; } = 2;

        public int CritNaturalRoll { get; set; } = 20;
        public int FumbleRoll { get; set; } = 1;
        public int CritDamageDiceMultiplier { get; set; } = 2;

        public int InitiativeDie { get; set; } = 20;
        public int AttackDie { get; set; } = 20;
        public int DefaultHitDie { get; set; } = 8;
        public string FallbackAttackDamage { get; set; } = "1d8";
        public int AbilityScoreCap { get; set; } = 20;
        public int ArmorClassBase { get; set; } = 10;
        public int AttunementLimit { get; set; } = 3;
        public bool RestoreFullHpOnLongRest { get; set; } = true;

        // Damage that spills past zero by the whole hit point maximum kills outright rather than dropping you to death saves.
        public bool MassiveDamageKills { get; set; } = true;
        public double MassiveDamageMaxHpMultiple { get; set; } = 1.0;

        public bool InspirationGrantsAdvantage { get; set; } = true;

        public bool PlayerRollsOwnSaves { get; set; }

        // The clock. Six seconds, an hour, eight hours, all the ruleset's opinion and not mine.
        public int RoundSeconds { get; set; } = 6;
        public int ShortRestMinutes { get; set; } = 60;
        public int LongRestMinutes { get; set; } = 480;
        public int MinutesPerDay { get; set; } = 1440;

        public int RoundsIn(int minutes) => RoundSeconds <= 0 ? 0 : minutes * 60 / RoundSeconds;

        public string ClockLabel(long totalMinutes)
        {
            var day = MinutesPerDay <= 0 ? 1440 : MinutesPerDay;
            var days = totalMinutes / day;
            var rest = totalMinutes % day;
            var hours = rest / 60;
            var mins = rest % 60;
            return "Day " + (days + 1) + ", " + hours.ToString("00") + ":" + mins.ToString("00");
        }

        // A statblock can name its own budget, this is only what a monster gets when it lists legendary options without saying how many it may spend.
        public int DefaultLegendaryActionsPerRound { get; set; } = 3;

        // Lair actions get their own count and lose ties. A statblock can name a different one.
        public int LairActionInitiativeCount { get; set; } = 20;

        // Spell id to whatever it drops on the map. Most spells have no entry.
        public Dictionary<string, SummonRule> Summons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public SummonRule? SummonFor(string? spellId) =>
            !string.IsNullOrWhiteSpace(spellId) && Summons.TryGetValue(spellId!, out var r) ? r : null;

        public bool MulticlassingEnabled { get; set; }

        // The template says whether the ruleset supports multiclassing at all, the dm gate below is the campaign saying no thanks on top of it.
        public bool MulticlassingAllowedByDm { get; set; } = true;
        public bool MulticlassingOn => MulticlassingEnabled && MulticlassingAllowedByDm;

        // Which SpellSlotTables row a split caster shares, and which one pact magic keeps to itself.
        public string MulticlassSharedSlotTable { get; set; } = "full";
        public string PactSlotTable { get; set; } = "pact";

        public bool IsPactCaster(string? casterType) =>
            !string.IsNullOrWhiteSpace(casterType) && MulticlassCasterContributions.TryGetValue(casterType!, out var c) && c.Excluded && string.Equals(casterType, PactSlotTable, StringComparison.OrdinalIgnoreCase);

        // Class id to the ability score minimums you need before you are allowed in. A class with no entry is a class nobody may multiclass into
        public Dictionary<string, Dictionary<string, int>> MulticlassPrerequisites { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> MulticlassProficiencies { get; } = new(StringComparer.OrdinalIgnoreCase);

        // How many caster levels one level of a given caster type is worth. Pact magic is excluded because its slots never pool with the rest.
        public Dictionary<string, CasterContribution> MulticlassCasterContributions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string MulticlassPrerequisiteFor(string? classId, Func<string, int> scoreOf)
        {
            if (string.IsNullOrWhiteSpace(classId)) return "There is no such class.";
            if (!MulticlassPrerequisites.TryGetValue(classId!, out var needs)) return "Nobody may multiclass into that.";
            var missing = needs.Where(kv => scoreOf(kv.Key) < kv.Value).Select(kv => kv.Key.ToUpperInvariant() + " " + kv.Value).ToList();
            return missing.Count == 0 ? "" : "You need " + string.Join(" and ", missing) + ".";
        }

        public bool MeetsMulticlassPrerequisite(string? classId, Func<string, int> scoreOf) => MulticlassPrerequisiteFor(classId, scoreOf).Length == 0;

        // Wizard 3 ranger 2 casts off caster level 4, not 5.
        public int MulticlassCasterLevel(IEnumerable<(string CasterType, int Level)> classes)
        {
            var total = 0;
            foreach (var (type, level) in classes)
            {
                if (level < 1) continue;
                if (!MulticlassCasterContributions.TryGetValue(type ?? "", out var c) || c.Excluded || c.Divisor < 1) continue;
                total += c.RoundUp ? (level + c.Divisor - 1) / c.Divisor : level / c.Divisor;
            }
            return total;
        }

        public List<string> MulticlassProficienciesFor(string? classId) =>
            !string.IsNullOrWhiteSpace(classId) && MulticlassProficiencies.TryGetValue(classId!, out var p) ? p : new List<string>();

        public double FeetPerSquare { get; set; } = 5.0;
        public double DiagonalCostSquares { get; set; } = 1.0;
        public double MovedThresholdFeet { get; set; } = 2.5;
        public bool RulerIgnoresWalls { get; set; } = true;
        public bool RulerIgnoresOccupied { get; set; } = true;
        public int BlankMapWidthPx { get; set; } = 2560;
        public int BlankMapHeightPx { get; set; } = 1600;

        public List<BlankMapPreset> BlankMapPresets { get; } = new()
        {
            new BlankMapPreset("Small", 20, 14),
            new BlankMapPreset("Medium", 32, 20),
            new BlankMapPreset("Large", 51, 32)
        };
        public bool ConfineToMapBounds { get; set; } = true;
        public bool WallsSnapToGrid { get; set; } = true;

        // Cone mouth is its length times this. A 5e cone as wide as it is long sits at a half.
        public double ConeWidthRatio { get; set; } = 0.5;
        public double DefaultAoeSizeFeet { get; set; } = 15.0;
        public double DefaultAoeWidthFeet { get; set; } = 5.0;
        public int DefaultSaveDc { get; set; } = 10;

        // 5e puts a cube's origin on a face. Full side away from you, not half each way.
        public bool CubeOriginOnFace { get; set; } = true;

        // Geometry is the Id and the code only knows these four, so a template can rename them but not invent a new one
        public List<AoeShapeDef> AoeShapes { get; } = new()
        {
            new AoeShapeDef("cone", "Cone", false),
            new AoeShapeDef("circle", "Circle", false),
            new AoeShapeDef("line", "Line", true),
            new AoeShapeDef("cube", "Cube", false)
        };

        public bool AoeShapeUsesWidth(string? id) =>
            AoeShapes.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase) && s.UsesWidth);

        public string AoeShapeName(string? id) =>
            AoeShapes.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? (id ?? "");

        public int MinDieSides { get; set; } = 2;
        public int MaxDieSides { get; set; } = 1000;
        public int MaxDiceCount { get; set; } = 100;

        public double StepCostSquares(int dc, int dr) => dc != 0 && dr != 0 ? DiagonalCostSquares : 1.0;

        // Octile distance, so a diagonal is worth whatever the template says, 1 gives plain 5e chebyshev and 1.5 gives the variant rule
        public double PathCostSquares(double dxSquares, double dySquares)
        {
            var a = Math.Abs(dxSquares);
            var b = Math.Abs(dySquares);
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            return hi + (DiagonalCostSquares - 1.0) * lo;
        }

        public double CarryCapacityPerStrength { get; set; } = 15.0;
        public double EncumberedPerStrength { get; set; } = 5.0;
        public double HeavilyEncumberedPerStrength { get; set; } = 10.0;

        public bool HpFirstLevelMax { get; set; } = true;
        public string HpPerLevelMode { get; set; } = "average";
        public string HpPerLevelMaxMode { get; set; } = "max";
        public bool HpUsesAverage => !string.Equals(HpPerLevelMode, HpPerLevelMaxMode, StringComparison.OrdinalIgnoreCase);
        public int HitDicePerLevel { get; set; } = 1;
        public int AbilityScoreIncrementPerAsi { get; set; } = 1;
        public string AttunementFlagValue { get; set; } = "Attunement";
        public string DamageTypeIdPrefix { get; set; } = "dmg-";
        public string ScaleByCharacterToken { get; set; } = "char";
        public string SenseRangeUnit { get; set; } = "ft";

        public string BothRulesVersion { get; set; } = "both";

        // Pact slots come back on a short rest. Anything named here does.
        public HashSet<string> ShortRestSlotCasterTypes { get; } = new(StringComparer.OrdinalIgnoreCase) { "pact" };

        public bool SlotsReturnOnShortRest(string? casterType) =>
            !string.IsNullOrWhiteSpace(casterType) && ShortRestSlotCasterTypes.Contains(casterType!);

        public bool VisibleInEdition(string? entryVersion, string? campaignFilter)
        {
            if (string.IsNullOrWhiteSpace(campaignFilter) || Eq(campaignFilter, BothRulesVersion)) return true;
            if (string.IsNullOrWhiteSpace(entryVersion) || Eq(entryVersion, BothRulesVersion)) return true;
            return Eq(entryVersion, campaignFilter);
        }

        public string SkillStoreKey { get; set; } = "proficientSkill";
        public string ExpertiseStoreKey { get; set; } = "expertiseSkill";
        public string FeatStoreKey { get; set; } = "feat";
        public string SubclassStoreKey { get; set; } = "subclass";
        public string AsiTokenPrefix { get; set; } = "asi:";
        public string ToolProficiencyIdPrefix { get; set; } = "prof-tool-";
        public string GrantedSpellIdPrefix { get; set; } = "spell-";
        public string FeatFeaturePrefix { get; set; } = "Feat:";
        public string SubclassFeaturePrefix { get; set; } = "Subclass:";

        private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public bool IsSkillStore(string? key) => Eq(key, SkillStoreKey);
        public bool IsExpertiseStore(string? key) => Eq(key, ExpertiseStoreKey);
        public bool IsFeatStore(string? key) => Eq(key, FeatStoreKey);
        public bool IsSubclassStore(string? key) => Eq(key, SubclassStoreKey);

        public bool IsToolProficiencyOption(string? optionId) => Starts(optionId, ToolProficiencyIdPrefix);
        public bool IsGrantedSpellOption(string? optionId) => Starts(optionId, GrantedSpellIdPrefix);

        public string FeatFeatureLine(string featId) => FeatFeaturePrefix + " " + featId;
        public string SubclassFeatureLine(string name) => SubclassFeaturePrefix + " " + name;

        public string AnsweredChoiceKey(string? choiceId, int level, string? storeAs) =>
            !string.IsNullOrEmpty(choiceId) ? choiceId! : (storeAs ?? "") + "@" + level;

        // Legacy spelling is accepted forever, saved characters carry these strings.
        public string? AsiAbilityFromToken(string? token) => After(token, AsiTokenPrefix, "asi:");
        public string? FeatFromFeatureLine(string? line) => After(line, FeatFeaturePrefix, "Feat:");
        public string? SubclassFromFeatureLine(string? line) => After(line, SubclassFeaturePrefix, "Subclass:");

        public bool IsFeatFeatureLine(string? line) => FeatFromFeatureLine(line) != null;
        public bool IsSubclassFeatureLine(string? line) => SubclassFromFeatureLine(line) != null;

        private static bool Starts(string? s, string prefix) =>
            !string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(prefix) && s!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static string? After(string? s, string configured, string legacy)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            foreach (var prefix in new[] { configured, legacy })
                if (Starts(s, prefix)) return s!.Substring(prefix.Length).Trim();
            return null;
        }

        public List<SenseDef> Senses { get; } = new()
        {
            new SenseDef("darkvision", "Darkvision", 60, new List<string> { "120", "superior" }, 120),
            new SenseDef("blindsight", "Blindsight", 0, new List<string>(), 0),
            new SenseDef("tremorsense", "Tremorsense", 0, new List<string>(), 0),
            new SenseDef("truesight", "Truesight", 0, new List<string>(), 0)
        };

        public string? SenseLabelFor(string? traitId)
        {
            if (string.IsNullOrWhiteSpace(traitId)) return null;
            foreach (var s in Senses)
            {
                if (string.IsNullOrWhiteSpace(s.MatchId) || traitId!.IndexOf(s.MatchId, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var range = s.RangeFeet;
                foreach (var up in s.UpgradeMatches)
                    if (!string.IsNullOrWhiteSpace(up) && traitId.IndexOf(up, StringComparison.OrdinalIgnoreCase) >= 0) { range = s.UpgradeRangeFeet; break; }
                return range > 0 ? s.Name + " " + range + " " + SenseRangeUnit : s.Name;
            }
            return null;
        }

        public bool IsSenseTrait(string? traitId) => SenseLabelFor(traitId) != null;

        public string DamageTypeLabel(string? typeId) =>
            string.IsNullOrEmpty(typeId) ? "" :
            typeId!.StartsWith(DamageTypeIdPrefix, StringComparison.OrdinalIgnoreCase) ? typeId.Substring(DamageTypeIdPrefix.Length) : typeId;
        public int MaxHitDiceForLevel(int level) => Math.Max(0, level * HitDicePerLevel);

        public string UnknownHpLabel { get; set; } = "Unknown";
        public string DownHpLabel { get; set; } = "Down";
        public string HealthyHpLabel { get; set; } = "Healthy";

        // Walked in order and the first band the hp fraction falls under wins, the template reader sorts them so nobody has to get the json order right by hand
        public List<HealthState> HealthStates { get; } = new()
        {
            new HealthState("Near Death", 0.25),
            new HealthState("Bloodied", 0.5),
            new HealthState("Wounded", 1.0)
        };

        public string HpLabelFor(int currentHp, int maxHp)
        {
            if (maxHp <= 0) return UnknownHpLabel;
            var pct = (double)currentHp / maxHp;
            if (pct <= 0) return DownHpLabel;
            foreach (var s in HealthStates)
                if (pct < s.Below) return s.Label;
            return HealthyHpLabel;
        }

        public List<SkillDef> Skills { get; } = new();
        public Dictionary<string, string> AbilityNames { get; } = new();
        public List<AbilityDef> Abilities { get; } = new();

        public string AbilityName(string abbrev, string fallback) => AbilityNames.TryGetValue(abbrev, out var n) && !string.IsNullOrEmpty(n) ? n : fallback;

        public Dictionary<string, double> CreatureSizeSquares { get; } = new()
        {
            ["tiny"] = 0.5,
            ["small"] = 1,
            ["medium"] = 1,
            ["large"] = 2,
            ["huge"] = 3,
            ["gargantuan"] = 4
        };

        public double SquaresForSize(string size) => CreatureSizeSquares.TryGetValue((size ?? "").ToLowerInvariant(), out var s) ? s : 1;

        public HashSet<string> AttackDisadvantageConditions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "blinded",
            "frightened",
            "poisoned",
            "prone",
            "restrained"
        };

        public HashSet<string> AttackAdvantageConditions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "invisible"
        };

        public HashSet<string> TargetAdvantageConditions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "blinded",
            "paralyzed",
            "petrified",
            "prone",
            "restrained",
            "stunned",
            "unconscious"
        };

        public HashSet<string> TargetDisadvantageConditions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "invisible"
        };

        public Dictionary<string, RangeSplitRule> TargetRangeSplitConditions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["prone"] = new RangeSplitRule(5, "disadvantage")
        };

        public HashSet<string> IncapacitatingConditions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "incapacitated",
            "paralyzed",
            "petrified",
            "stunned",
            "unconscious"
        };

        public HashSet<string> MovementStoppingConditions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "grappled",
            "paralyzed",
            "petrified",
            "restrained",
            "stunned",
            "unconscious"
        };

        public Dictionary<string, MasteryRule> Masteries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ConditionRule> Conditions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ClassResourceRule> ClassResources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> ActionCosts { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["attack"] = "action",
            ["save"] = "none",
        };

        public string CostFor(string key, string fallback = "action") =>
            !string.IsNullOrWhiteSpace(key) && ActionCosts.TryGetValue(key, out var c) && !string.IsNullOrWhiteSpace(c) ? c : fallback;

        public static string CostPool(string cost)
        {
            if (string.IsNullOrWhiteSpace(cost)) return "action";
            var idx = cost.IndexOf(':');
            return (idx < 0 ? cost : cost[..idx]).Trim();
        }

        public static int CostAmount(string cost)
        {
            if (string.IsNullOrWhiteSpace(cost)) return 1;
            var idx = cost.IndexOf(':');
            return idx >= 0 && int.TryParse(cost[(idx + 1)..].Trim(), out var n) ? Math.Max(0, n) : 1;
        }

        public HashSet<int> CritNaturalRolls { get; } = new() { 20 };
        public HashSet<int> FumbleRolls { get; } = new() { 1 };
        public bool IsCrit(int naturalRoll) => CritNaturalRolls.Contains(naturalRoll);
        public bool IsFumble(int naturalRoll) => FumbleRolls.Contains(naturalRoll);

        public string CritDamageMode { get; set; } = "dice";
        public bool CritAlwaysHits { get; set; } = true;
        public bool FumbleAlwaysMisses { get; set; } = true;

        public List<OutcomeBand> OutcomeBands { get; } = new();

        public OutcomeBand? OutcomeBandFor(int margin)
        {
            OutcomeBand? best = null;
            foreach (var b in OutcomeBands)
                if (margin >= b.MarginAtLeast && (best == null || b.MarginAtLeast > best.MarginAtLeast)) best = b;
            return best;
        }

        public (bool Hit, bool Crit) ResolveAttackOutcome(int natural, int total, int dc)
        {
            var margin = MarginOf(total, dc);
            var band = OutcomeBandFor(margin);
            var hit = OutcomeBands.Count == 0 ? margin >= 0 : band?.Hits ?? false;
            var critDamage = IsCrit(natural) || (band?.CritDamage ?? false);
            if (IsCrit(natural) && CritAlwaysHits) hit = true;
            if (IsFumble(natural) && FumbleAlwaysMisses) { hit = false; critDamage = false; }
            return (hit, hit && critDamage);
        }

        public bool SaveSucceeds(int total, int dc)
        {
            var margin = MarginOf(total, dc);
            var band = OutcomeBandFor(margin);
            return OutcomeBands.Count == 0 ? margin >= 0 : band?.Hits ?? false;
        }

        public Dictionary<string, CheckDifficultyRule> CheckDifficulties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int CheckDcFor(string difficultyId, int explicitDc = 0)
        {
            if (explicitDc > 0) return explicitDc;
            if (!string.IsNullOrWhiteSpace(difficultyId) && CheckDifficulties.TryGetValue(difficultyId, out var d)) return d.Dc;
            return 0;
        }

        public string CheckDifficultyName(string difficultyId) =>
            !string.IsNullOrWhiteSpace(difficultyId) && CheckDifficulties.TryGetValue(difficultyId, out var d) ? d.Name : "";

        public Dictionary<string, ProficiencyRank> ProficiencyRanks { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int RankBonus(string rankId, int profBonus)
        {
            if (!string.IsNullOrWhiteSpace(rankId) && ProficiencyRanks.TryGetValue(rankId, out var r))
                return (int)Math.Round(r.Multiplier * profBonus);
            return string.Equals(rankId, "expertise", StringComparison.OrdinalIgnoreCase) ? ExpertiseMultiplier * profBonus
                 : string.Equals(rankId, "proficient", StringComparison.OrdinalIgnoreCase) ? profBonus
                 : 0;
        }

        public static string RankIdFor(bool proficient, bool expertise = false) => expertise ? "expertise" : proficient ? "proficient" : "none";

        public int AutoHitToHit { get; set; } = 99;
        public bool InitiativeTieBreakByModifier { get; set; } = true;
        public int ResistanceDivisor { get; set; } = 2;
        public int VulnerabilityMultiplier { get; set; } = 2;
        public int HalfOnSaveDivisor { get; set; } = 2;

        public double Var(string name, double fallback = 0) =>
            !string.IsNullOrWhiteSpace(name) && Variables.TryGetValue(name.TrimStart('@'), out var v) ? v : fallback;

        public int VarInt(string name, int fallback = 0) => (int)Math.Round(Var(name, fallback));

        public int ResolveNumber(string raw, int fallback = 0)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var s = raw.Trim();
            if (int.TryParse(s, out var direct)) return direct;
            return VarInt(s, fallback);
        }

        public string Substitute(string expression, IReadOnlyDictionary<string, int>? context = null)
        {
            if (string.IsNullOrWhiteSpace(expression) || !expression.Contains('@')) return expression ?? "";
            return Regex.Replace(expression, @"@([A-Za-z_][A-Za-z0-9_-]*)", m =>
            {
                var key = m.Groups[1].Value;
                if (context != null && context.TryGetValue(key, out var cv)) return cv.ToString();
                if (Variables.TryGetValue(key, out var vv)) return ((int)Math.Round(vv)).ToString();
                return "0";
            });
        }

        public static bool TryTypedAmount(string entry, string damageType, out int amount)
        {
            amount = 0;
            if (string.IsNullOrWhiteSpace(entry)) return false;
            var idx = entry.IndexOf(':');
            var type = idx < 0 ? entry : entry[..idx];
            if (!string.Equals(type.Trim(), damageType, StringComparison.OrdinalIgnoreCase)) return false;
            if (idx >= 0 && int.TryParse(entry[(idx + 1)..].Trim(), out var a)) amount = a;
            return true;
        }

        public List<string> ResistancesFromConditions(IEnumerable<string> active)
        {
            var result = new List<string>();
            foreach (var c in active)
                if (!string.IsNullOrWhiteSpace(c) && Conditions.TryGetValue(c, out var rule))
                    foreach (var r in rule.Resistances)
                        if (!result.Contains(r, StringComparer.OrdinalIgnoreCase)) result.Add(r);
            return result;
        }

        public int DamageBonusFromConditions(IEnumerable<string> active)
        {
            int total = 0;
            foreach (var c in active)
                if (!string.IsNullOrWhiteSpace(c) && Conditions.TryGetValue(c, out var rule))
                    total += rule.DamageBonus;
            return total;
        }

        public double SpeedMultiplierFrom(IEnumerable<string> active)
        {
            var mult = 1.0;
            foreach (var c in active)
                if (!string.IsNullOrWhiteSpace(c) && Conditions.TryGetValue(c, out var rule)) mult *= rule.SpeedMultiplier;
            return mult < 0 ? 0 : mult;
        }

        public double MaxHpMultiplierFrom(IEnumerable<string> active)
        {
            var mult = 1.0;
            foreach (var c in active)
                if (!string.IsNullOrWhiteSpace(c) && Conditions.TryGetValue(c, out var rule)) mult *= rule.MaxHpMultiplier;
            return mult < 0 ? 0 : mult;
        }

        public string AbilityCheckModeFrom(IEnumerable<string> active) => RollModeFrom(active, r => r.AbilityCheckRoll);

        public string SaveRollModeFrom(IEnumerable<string> active) => RollModeFrom(active, r => r.SaveRoll);

        public bool BlocksReactions(IEnumerable<string> active)
        {
            foreach (var c in active)
                if (!string.IsNullOrWhiteSpace(c) && Conditions.TryGetValue(c, out var rule) && rule.BlocksReactions) return true;
            return false;
        }

        private string RollModeFrom(IEnumerable<string> active, Func<ConditionRule, string> pick)
        {
            bool adv = false, dis = false;
            foreach (var c in active)
            {
                if (string.IsNullOrWhiteSpace(c) || !Conditions.TryGetValue(c, out var rule)) continue;
                var mode = pick(rule);
                if (mode == "advantage") adv = true;
                else if (mode == "disadvantage") dis = true;
            }
            return adv == dis ? "" : adv ? "advantage" : "disadvantage";
        }

        public List<string> CasterAbilityIds { get; } = new() { "ability-int", "ability-wis", "ability-cha" };

        public List<string> FeatSpellListClassIds { get; } = new()
        {
            "class-bard", "class-cleric", "class-druid", "class-sorcerer", "class-warlock", "class-wizard"
        };

        public int AbilityScoreHardCap { get; set; } = 30;
        public string InitiativeAbility { get; set; } = "dex";
        public string ConcentrationAbility { get; set; } = "con";
        public string ArmorClassAbility { get; set; } = "dex";
        public string HitPointAbility { get; set; } = "con";
        public string DefaultSaveAbility { get; set; } = "dex";
        public string MeleeAttackAbility { get; set; } = "str";
        public string RangedAttackAbility { get; set; } = "dex";
        public string OffHandSlotId { get; set; } = "slot-offhand";
        public string OffHandWeaponProperty { get; set; } = "wp-light";

        public Dictionary<string, ArmorTypeRule> ArmorTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, GridItemRule> GridItems { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool GridItemBlocksMovement(string? id) =>
            !string.IsNullOrWhiteSpace(id) && GridItems.TryGetValue(id!, out var g) && g.BlocksMovement;

        public bool GridItemBlocksSight(string? id) =>
            !string.IsNullOrWhiteSpace(id) && GridItems.TryGetValue(id!, out var g) && g.BlocksSight;

        public string GridItemName(string? id) =>
            !string.IsNullOrWhiteSpace(id) && GridItems.TryGetValue(id!, out var g) ? g.Name : (id ?? "");

        public Dictionary<string, WeaponPropertyRule> WeaponProperties { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["wp-finesse"] = new WeaponPropertyRule("wp-finesse", "Finesse", new List<string> { "str", "dex" })
        };

        public Dictionary<string, string> CastingTimeKinds { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bonus"] = "bonus",
            ["reaction"] = "reaction",
            ["action"] = "action"
        };

        public bool IsOffHandArmor(string? armorTypeId) =>
            !string.IsNullOrWhiteSpace(armorTypeId)
            && ArmorTypes.TryGetValue(armorTypeId, out var a)
            && string.Equals(a.EqSlotId, OffHandSlotId, StringComparison.OrdinalIgnoreCase);

        public string AbilityIdForShort(string shortName)
        {
            var hit = Abilities.FirstOrDefault(a => string.Equals(a.Short, shortName, StringComparison.OrdinalIgnoreCase));
            return hit?.Id ?? "ability-" + (shortName ?? "").ToLowerInvariant();
        }

        public List<string> AttackAbilitiesFor(IEnumerable<string>? categories, bool isRanged)
        {
            if (categories != null)
                foreach (var c in categories)
                    if (!string.IsNullOrWhiteSpace(c) && WeaponProperties.TryGetValue(c, out var wp) && wp.AttackAbilities.Count > 0)
                        return wp.AttackAbilities;
            return new List<string> { isRanged ? RangedAttackAbility : MeleeAttackAbility };
        }

        public int ContestDcBase { get; set; } = 8;

        public List<string> ContestAbilities { get; } = new() { "str", "dex" };

        public List<string> FallbackAttackAbilities { get; } = new() { "str", "dex" };

        public Dictionary<string, TacticalActionRule> TacticalActions { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dodge"] = new TacticalActionRule("dodge", "Dodge", "dodge", "action", "", new List<string>()),
            ["disengage"] = new TacticalActionRule("disengage", "Disengage", "disengage", "action", "", new List<string>()),
            ["hide"] = new TacticalActionRule("hide", "Hide", "hide", "action", "", new List<string>()),
            ["help"] = new TacticalActionRule("help", "Help", "help", "action", "", new List<string>()),
            ["grapple"] = new TacticalActionRule("grapple", "Grapple", "contest", "action", "grappled", new List<string> { "str", "dex" }),
            ["shove"] = new TacticalActionRule("shove", "Shove", "contest", "action", "prone", new List<string> { "str", "dex" })
        };

        public Dictionary<string, int> CrXp { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = 10,
            ["1/8"] = 25, ["0.125"] = 25,
            ["1/4"] = 50, ["0.25"] = 50,
            ["1/2"] = 100, ["0.5"] = 100,
            ["1"] = 200, ["2"] = 450, ["3"] = 700, ["4"] = 1100, ["5"] = 1800,
            ["6"] = 2300, ["7"] = 2900, ["8"] = 3900, ["9"] = 5000, ["10"] = 5900,
            ["11"] = 7200, ["12"] = 8400, ["13"] = 10000, ["14"] = 11500, ["15"] = 13000,
            ["16"] = 15000, ["17"] = 18000, ["18"] = 20000, ["19"] = 22000, ["20"] = 25000,
            ["21"] = 33000, ["22"] = 41000, ["23"] = 50000, ["24"] = 62000, ["25"] = 75000,
            ["26"] = 90000, ["27"] = 105000, ["28"] = 120000, ["29"] = 135000, ["30"] = 155000
        };

        public int PointBuyBudget { get; set; } = 27;
        public int PointBuyMinScore { get; set; } = 8;
        public int PointBuyMaxScore { get; set; } = 15;
        public int ManualMinScore { get; set; } = 3;
        public int ManualMaxScore { get; set; } = 20;
        public Dictionary<int, int> PointBuyCosts { get; } = new()
        {
            { 8, 0 }, { 9, 1 }, { 10, 2 }, { 11, 3 }, { 12, 4 }, { 13, 5 }, { 14, 7 }, { 15, 9 }
        };
        public List<int> StandardArray { get; } = new() { 15, 14, 13, 12, 10, 8 };
        public string AbilityRollDice { get; set; } = "4d6kh3";

        public Dictionary<int, string> SpellLevelNames { get; } = new();
        public string SpellLevelName(int level) => SpellLevelNames.TryGetValue(level, out var n) && !string.IsNullOrEmpty(n) ? n : (level == 0 ? "Cantrip" : "Level " + level);

        public ExhaustionConfig Exhaustion { get; } = new();

        public Dictionary<int, (int Low, int Moderate, int High)> EncounterBudgets { get; } = new()
        {
            [1] = (50, 75, 100),
            [2] = (100, 150, 200),
            [3] = (150, 225, 400),
            [4] = (250, 375, 500),
            [5] = (500, 750, 1100),
            [6] = (600, 1000, 1400),
            [7] = (750, 1300, 1700),
            [8] = (1000, 1700, 2100),
            [9] = (1300, 2000, 2600),
            [10] = (1600, 2300, 3100),
            [11] = (1900, 2900, 4100),
            [12] = (2200, 3700, 4700),
            [13] = (2600, 4200, 5400),
            [14] = (2900, 4900, 6200),
            [15] = (3300, 5400, 7800),
            [16] = (3800, 6100, 9800),
            [17] = (4500, 7200, 11700),
            [18] = (5000, 8700, 14200),
            [19] = (5500, 10700, 17200),
            [20] = (6400, 13200, 22000)
        };

        public int CrToXp(string? cr)
        {
            if (string.IsNullOrWhiteSpace(cr)) return 0;
            var key = cr.Trim();
            if (CrXp.TryGetValue(key, out var xp)) return xp;
            if (int.TryParse(key, out var whole) && CrXp.TryGetValue(whole.ToString(), out var wxp)) return wxp;
            return 0;
        }

        public (int Low, int Moderate, int High) BudgetForLevel(int level)
        {
            if (EncounterBudgets.Count == 0) return (0, 0, 0);
            int lvl = level < 1 ? 1 : level;
            if (EncounterBudgets.TryGetValue(lvl, out var b)) return b;
            int maxKey = 1;
            foreach (var k in EncounterBudgets.Keys) if (k > maxKey) maxKey = k;
            return EncounterBudgets.TryGetValue(lvl > maxKey ? maxKey : lvl, out var bb) ? bb : (0, 0, 0);
        }

        public double DefaultLineWidthFeet { get; set; } = 5.0;

        public int Modifier(int score)
        {
            var div = AbilityModDivisor == 0 ? 2 : AbilityModDivisor;
            return (int)Math.Floor((score - AbilityModBaseline) / (double)div);
        }

        public int ProficiencyBonusForLevel(int level)
        {
            var l = Math.Max(1, level);
            if (LevelBonus.TryGetValue(l, out var b)) return b;
            return 2 + (l - 1) / 4;
        }

        public int XpForLevel(int level)
        {
            if (LevelXp.TryGetValue(level, out var x)) return x;
            return FallbackXp(level);
        }

        private static int FallbackXp(int level) => level switch
        {
            <= 1 => 0,
            2 => 300,
            3 => 900,
            4 => 2700,
            5 => 6500,
            6 => 14000,
            7 => 23000,
            8 => 34000,
            9 => 48000,
            10 => 64000,
            11 => 85000,
            12 => 100000,
            13 => 120000,
            14 => 140000,
            15 => 165000,
            16 => 195000,
            17 => 225000,
            18 => 265000,
            19 => 305000,
            _ => 355000
        };

        public int HitDieHeal(int die, bool average) => average ? die / 2 + 1 : die;
    }

    public record SkillDef(string Id, string Name, string Ability);

    public record MasteryRule(string Id, string Name, string Effect, int SpeedPenaltyFeet, int PushFeet, string SaveAbility, string Condition);

    public record TacticalActionRule(string Id, string Name, string Effect, string Cost, string Condition, List<string> DefenderSaves, int PushFeet = 0, string CheckAbility = "", string CheckSkill = "", string CheckDifficulty = "", int CheckDc = 0);

    public record CheckDifficultyRule(string Id, string Name, int Dc);

    public record ConditionRule(string Id, string Name, string Description, string AttackRoll, string IncomingAttack, bool BlocksActions, bool StopsMovement, bool Trackable, List<string> Resistances, int DamageBonus, double SpeedMultiplier = 1.0, double MaxHpMultiplier = 1.0, string AbilityCheckRoll = "", string SaveRoll = "", bool BlocksReactions = false, int DurationRounds = 0, string ExpiresAt = "end", string IncomingAttackBeyond = "", double IncomingAttackWithinFeet = 0, string DamageOverTime = "", string DamageOverTimeType = "", string DamageOverTimeAt = "turn-start", int EndsOnSaveDc = 0, string EndsOnSaveAbility = "");

    public record TerrainCostRule(string Id, string Name, double Multiplier);

    public record HazardRule(string Id, string Name, string Die, double PerFeet, int MaxDice, string DamageType,
        string SaveAbility = "", int SaveDc = 0, bool HalfOnSave = false, string Condition = "");

    public record RangeSplitRule(double WithinFeet, string Beyond);

    public record ClassResourceRule(string Id, string Heal, int HealPerPoint, string Condition = "", string InspireDie = "");

    public record ArmorTypeRule(string Id, string Name, string EqSlotId);

    public record GridItemRule(string Id, string Name, bool BlocksMovement, bool BlocksSight);

    public record WeaponPropertyRule(string Id, string Name, List<string> AttackAbilities);

    public record HealthState(string Label, double Below);

    public record OutcomeBand(string Id, int MarginAtLeast, bool Hits, bool CritDamage = false);

    public record ProficiencyRank(string Id, double Multiplier);

    public record SenseDef(string MatchId, string Name, int RangeFeet, List<string> UpgradeMatches, int UpgradeRangeFeet);

    public record AoeShapeDef(string Id, string Name, bool UsesWidth);

    public record BlankMapPreset(string Name, int Cols, int Rows);

    public class AbilityDef
    {
        public string Id { get; set; } = "";
        public string Short { get; set; } = "";
        public string Name { get; set; } = "";
        public string SaveId => (Short ?? "").ToLowerInvariant();
    }

    public record ExhaustionLevelRule(string Text, double SpeedMultiplier, double MaxHpMultiplier, string AbilityCheckRoll, string SaveRoll, string AttackRoll, int D20Penalty = 0, int SpeedPenaltyFeet = 0);

    public class ExhaustionConfig
    {
        public int MaxLevel { get; set; } = 6;
        public int ReducePerLongRest { get; set; } = 1;
        public bool DeathAtMax { get; set; } = true;

        public Dictionary<int, ExhaustionLevelRule> Levels { get; } = new()
        {
            { 1, new ExhaustionLevelRule("Disadvantage on ability checks", 1.0, 1.0, "disadvantage", "", "") },
            { 2, new ExhaustionLevelRule("Speed halved", 0.5, 1.0, "", "", "") },
            { 3, new ExhaustionLevelRule("Disadvantage on attack rolls and saving throws", 1.0, 1.0, "", "disadvantage", "disadvantage") },
            { 4, new ExhaustionLevelRule("Hit point maximum halved", 1.0, 0.5, "", "", "") },
            { 5, new ExhaustionLevelRule("Speed reduced to 0", 0.0, 1.0, "", "", "") },
            { 6, new ExhaustionLevelRule("Death", 1.0, 1.0, "", "", "") }
        };

        public string EffectFor(int level) => Levels.TryGetValue(level, out var e) ? e.Text : "";

        // Exhaustion stacks, hmmmm so every band at or under the level you are on is on you at once
        private IEnumerable<ExhaustionLevelRule> Through(int level)
        {
            for (var i = 1; i <= level; i++)
                if (Levels.TryGetValue(i, out var rule)) yield return rule;
        }

        public double SpeedMultiplier(int level)
        {
            var m = 1.0;
            foreach (var r in Through(level)) m *= r.SpeedMultiplier;
            return m < 0 ? 0 : m;
        }

        public double MaxHpMultiplier(int level)
        {
            var m = 1.0;
            foreach (var r in Through(level)) m *= r.MaxHpMultiplier;
            return m < 0 ? 0 : m;
        }

        public bool AbilityChecksAtDisadvantage(int level) => Through(level).Any(r => r.AbilityCheckRoll == "disadvantage");
        public bool SavesAtDisadvantage(int level) => Through(level).Any(r => r.SaveRoll == "disadvantage");
        public bool AttacksAtDisadvantage(int level) => Through(level).Any(r => r.AttackRoll == "disadvantage");

        public int D20Penalty(int level) => Through(level).Sum(r => r.D20Penalty);
        public int SpeedPenaltyFeet(int level) => Through(level).Sum(r => r.SpeedPenaltyFeet);
    }
}
