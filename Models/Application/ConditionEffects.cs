using System;
using System.Collections.Generic;
using System.Linq;

namespace Dujahit.Models.Application
{
    public enum RollMode
    {
        Normal,
        Advantage,
        Disadvantage
    }

    public static class ConditionEffects
    {
        // Only reached before App.PM exists, and it holds the same 5e sets GameRules declares, so there is one copy of them and not two.
        private static readonly GameRules _fallback = new();

        private static GameRules Rules => App.PM?.Rules ?? _fallback;

        private static HashSet<string> DisadvantageOnAttacks => Rules.AttackDisadvantageConditions;
        private static HashSet<string> AdvantageOnAttacks => Rules.AttackAdvantageConditions;
        private static HashSet<string> TargetGrantsAdvantage => Rules.TargetAdvantageConditions;
        private static HashSet<string> TargetGrantsDisadvantage => Rules.TargetDisadvantageConditions;
        private static HashSet<string> Incapacitating => Rules.IncapacitatingConditions;
        private static HashSet<string> MovementStopping => Rules.MovementStoppingConditions;

        public static bool IsIncapacitated(IEnumerable<string> conditions) => conditions.Any(c => !string.IsNullOrWhiteSpace(c) && Incapacitating.Contains(c));
        public static bool StopsMovement(IEnumerable<string> conditions) => conditions.Any(c => !string.IsNullOrWhiteSpace(c) && MovementStopping.Contains(c));

        public static RollMode AttackMode(IEnumerable<string> activeConditions)
        {
            bool adv = false, dis = false;
            foreach (var c in activeConditions)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                if (AdvantageOnAttacks.Contains(c)) adv = true;
                if (DisadvantageOnAttacks.Contains(c)) dis = true;
            }
            if (adv && dis) return RollMode.Normal;
            if (adv) return RollMode.Advantage;
            if (dis) return RollMode.Disadvantage;
            return RollMode.Normal;
        }

        // The conditions the target is under that swing the attacker's roll, prone or stunned hand advantage, an invisible target hands disadvantage
        public static RollMode DefenderMode(IEnumerable<string> targetConditions) => DefenderMode(targetConditions, -1);

        public static RollMode DefenderMode(IEnumerable<string> targetConditions, double distanceFeet)
        {
            bool adv = false, dis = false;
            foreach (var c in targetConditions)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var split = distanceFeet >= 0 && Rules.TargetRangeSplitConditions.TryGetValue(c, out var s) && distanceFeet > s.WithinFeet ? s.Beyond : "";
                if (split.Length > 0)
                {
                    if (split == "advantage") adv = true;
                    else if (split == "disadvantage") dis = true;
                    continue;
                }
                if (TargetGrantsAdvantage.Contains(c)) adv = true;
                if (TargetGrantsDisadvantage.Contains(c)) dis = true;
            }
            if (adv && dis) return RollMode.Normal;
            if (adv) return RollMode.Advantage;
            if (dis) return RollMode.Disadvantage;
            return RollMode.Normal;
        }

        public static List<string> RelevantConditions(IEnumerable<string> activeConditions)
        {
            return activeConditions
                .Where(c => !string.IsNullOrWhiteSpace(c) && (AdvantageOnAttacks.Contains(c) || DisadvantageOnAttacks.Contains(c)))
                .ToList();
        }
    }
}