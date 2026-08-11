using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dujahit.Models.UI
{
    public record DiceRollResult(string Expression, int Total, string Breakdown);

    public class DiceManager
    {
        private static readonly Random _rng = new();

        private static int MaxDicePerTerm => App.PM?.Rules?.MaxDiceCount ?? 100;
        private static int MaxSides => App.PM?.Rules?.MaxDieSides ?? 1000;
        private static int MinSides => App.PM?.Rules?.MinDieSides ?? 2;
        private static int ExplodeLimit => App.PM?.Rules?.ExplodingDiceLimit ?? 20;

        public static int RollCore(int fallbackSides)
        {
            var expr = App.PM?.Rules?.CoreRollExpression ?? "";
            if (string.IsNullOrWhiteSpace(expr)) return RollSingle(fallbackSides);
            return TryRoll(expr, out var r) && r != null ? r.Total : RollSingle(fallbackSides);
        }

        public static int RollInitiativeDie(bool surprised = false)
        {
            var die = App.PM?.Rules?.InitiativeDie ?? 20;
            if (!surprised) return RollSingle(die);
            var first = RollSingle(die);
            var second = RollSingle(die);
            return (App.PM?.Rules?.RollsLow ?? false) ? Math.Max(first, second) : Math.Min(first, second);
        }

        public static int RollCore(int fallbackSides, bool advantage, bool disadvantage)
        {
            var first = RollCore(fallbackSides);
            if (advantage == disadvantage) return first;
            var second = RollCore(fallbackSides);
            var wantHigher = advantage ^ (App.PM?.Rules?.RollsLow ?? false);
            return wantHigher ? Math.Max(first, second) : Math.Min(first, second);
        }

        public static int RollSingle(int sides)
        {
            if (sides < MinSides) sides = MinSides;
            if (sides > MaxSides) sides = MaxSides;
            return _rng.Next(1, sides + 1);
        }

        public static DiceRollResult Roll(string expression) => Roll(expression, false);

        // Takes stuff like "2d20+5", "d6", "4d6+1d4-2", "2d20kh1" (advantage), "2d20kl1" (disadvantage). crit doubles the dice count on every dice term, flat mods are left alone. I should make the crit modifier changeable so you can easily configure it to work with other ttrpg games in the future
        public static DiceRollResult Roll(string expression, bool crit)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new FormatException("Empty dice expression.");

            var cleaned = expression.Replace(" ", "").ToLowerInvariant();

            var terms = new List<(int Sign, string Body)>();
            var sign = 1;
            var start = 0;
            for (var i = 0; i <= cleaned.Length; i++)
            {
                if (i == cleaned.Length || cleaned[i] == '+' || cleaned[i] == '-')
                {
                    if (i > start) terms.Add((sign, cleaned[start..i]));
                    else if (i != 0) throw new FormatException($"Dangling operator in '{expression}'.");
                    if (i < cleaned.Length) sign = cleaned[i] == '-' ? -1 : 1;
                    start = i + 1;
                }
            }

            if (terms.Count == 0)
                throw new FormatException($"Nothing to roll in '{expression}'.");

            var total = 0;
            var breakdown = new StringBuilder();

            foreach (var (termSign, body) in terms)
            {
                if (breakdown.Length > 0)
                    breakdown.Append(termSign < 0 ? " - " : " + ");
                else if (termSign < 0)
                    breakdown.Append('-');

                var dIndex = body.IndexOf('d');
                if (dIndex < 0)
                {
                    if (!int.TryParse(body, out var flat))
                        throw new FormatException($"'{body}' is not a number or a dice term.");
                    total += termSign * flat;
                    breakdown.Append(flat);
                    continue;
                }

                var countPart = body[..dIndex];
                var rest = body[(dIndex + 1)..];

                var poolTarget = 0;
                var poolIdx = rest.IndexOf(">=", StringComparison.Ordinal);
                if (poolIdx >= 0)
                {
                    var targetPart = rest[(poolIdx + 2)..];
                    if (!int.TryParse(targetPart, out poolTarget) || poolTarget < 1)
                        throw new FormatException($"Bad success target in '{body}'.");
                    rest = rest[..poolIdx];
                }

                var exploding = rest.EndsWith("!", StringComparison.Ordinal);
                if (exploding) rest = rest[..^1];

                var keepMode = '\0';
                var keepCount = 0;
                var khIdx = rest.IndexOf("kh", StringComparison.Ordinal);
                var klIdx = rest.IndexOf("kl", StringComparison.Ordinal);
                var kIdx = khIdx >= 0 ? khIdx : klIdx;
                var sidesPart = rest;
                if (kIdx >= 0)
                {
                    keepMode = khIdx >= 0 ? 'h' : 'l';
                    sidesPart = rest[..kIdx];
                    var keepPart = rest[(kIdx + 2)..];
                    if (!int.TryParse(keepPart, out keepCount) || keepCount < 1)
                        throw new FormatException($"Bad keep count in '{body}'.");
                }

                var count = 1;
                if (countPart.Length > 0 && !int.TryParse(countPart, out count))
                    throw new FormatException($"Bad dice count in '{body}'.");

                var fudge = sidesPart == "f";
                var sides = 0;
                if (!fudge && !int.TryParse(sidesPart, out sides))
                    throw new FormatException($"Bad die size in '{body}'.");

                if (crit && !CritDoublesTotal) count *= App.PM?.Rules.CritDamageDiceMultiplier ?? 2;

                if (count < 1 || count > MaxDicePerTerm || (!fudge && (sides < MinSides || sides > MaxSides)))
                    throw new FormatException($"'{body}' is out of range.");

                var rolls = new int[count];
                for (var d = 0; d < count; d++)
                    rolls[d] = fudge ? _rng.Next(-1, 2) : _rng.Next(1, sides + 1);

                if (exploding && !fudge)
                    for (var d = 0; d < count; d++)
                    {
                        var guard = 0;
                        while (rolls[d] % sides == 0 && guard++ < ExplodeLimit)
                            rolls[d] += _rng.Next(1, sides + 1);
                    }

                if (poolTarget > 0)
                {
                    var hits = rolls.Count(r => r >= poolTarget);
                    total += termSign * hits;
                    breakdown.Append($"({string.Join(", ", rolls)}) {hits} vs {poolTarget}+");
                    continue;
                }

                int sum;
                if (keepMode != '\0' && keepCount < count)
                {
                    var ordered = keepMode == 'h'
                        ? rolls.OrderByDescending(x => x).ToArray()
                        : rolls.OrderBy(x => x).ToArray();
                    var kept = ordered.Take(keepCount).ToArray();
                    sum = kept.Sum();
                    var dropped = ordered.Skip(keepCount);
                    breakdown.Append($"[{string.Join(", ", kept)}] (drop {string.Join(", ", dropped)})");
                }
                else
                {
                    sum = rolls.Sum();
                    breakdown.Append(count == 1 ? rolls[0].ToString() : $"({string.Join(" + ", rolls)})");
                }

                total += termSign * sum;
            }

            if (crit && CritDoublesTotal)
            {
                var mult = App.PM?.Rules.CritDamageDiceMultiplier ?? 2;
                total *= mult;
                breakdown.Append(" x" + mult);
            }

            return new DiceRollResult(expression.Trim(), total, breakdown.ToString());
        }

        private static bool CritDoublesTotal =>
            string.Equals(App.PM?.Rules.CritDamageMode, "total", StringComparison.OrdinalIgnoreCase);

        public static bool TryRoll(string expression, out DiceRollResult? result) =>
            TryRoll(expression, false, out result);

        public static bool TryRoll(string expression, bool crit, out DiceRollResult? result)
        {
            try
            {
                result = Roll(expression, crit);
                return true;
            }
            catch (FormatException)
            {
                result = null;
                return false;
            }
        }
    }
}
