using System;
using System.Collections.Generic;

namespace Dujahit.Models.Application
{
    public enum EncounterBand { Empty, NoParty, Trivial, Low, Moderate, High, Deadly }

    public static class EncounterMath
    {
        public static int CrToXp(string? cr) => (App.PM?.Rules ?? new GameRules()).CrToXp(cr);

        public static (int Low, int Moderate, int High) BudgetForLevel(int level) => (App.PM?.Rules ?? new GameRules()).BudgetForLevel(level);

        public static (int Low, int Moderate, int High) PartyBudget(IEnumerable<int> levels)
        {
            int low = 0, mod = 0, high = 0;
            foreach (var lvl in levels)
            {
                var b = BudgetForLevel(lvl);
                low += b.Low;
                mod += b.Moderate;
                high += b.High;
            }
            return (low, mod, high);
        }

        public static EncounterBand Classify(int totalXp, (int Low, int Moderate, int High) budget)
        {
            if (totalXp <= 0) return EncounterBand.Empty;
            if (budget.High <= 0) return EncounterBand.NoParty;
            if (totalXp <= budget.Low / 2) return EncounterBand.Trivial;
            if (totalXp <= budget.Low) return EncounterBand.Low;
            if (totalXp <= budget.Moderate) return EncounterBand.Moderate;
            if (totalXp <= budget.High) return EncounterBand.High;
            return EncounterBand.Deadly;
        }

        public static string Label(EncounterBand band) => band switch
        {
            EncounterBand.Empty => "No monsters",
            EncounterBand.NoParty => "No party picked",
            EncounterBand.Trivial => "Trivial",
            EncounterBand.Low => "Low",
            EncounterBand.Moderate => "Moderate",
            EncounterBand.High => "High",
            EncounterBand.Deadly => "Deadly",
            _ => ""
        };

        public static string ColorHex(EncounterBand band) => band switch
        {
            EncounterBand.Empty => "#3A3A4D",
            EncounterBand.NoParty => "#3A3A4D",
            EncounterBand.Trivial => "#5B8C5A",
            EncounterBand.Low => "#6FAE5A",
            EncounterBand.Moderate => "#FFD700",
            EncounterBand.High => "#E08A3C",
            EncounterBand.Deadly => "#BB4444",
            _ => "#3A3A4D"
        };
    }
}
