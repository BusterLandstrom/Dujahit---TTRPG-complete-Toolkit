using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Dujahit.Models.Application
{
    public sealed class ItemWeightInfo
    {
        public double Weight { get; set; }
        public bool IsContainer { get; set; }
        public double Capacity { get; set; }
        public bool IgnoresContainedWeight { get; set; }
    }

    public sealed class InventoryNode
    {
        public ItemInstance Instance { get; set; } = new();
        public ItemWeightInfo Info { get; set; } = new();
        public List<InventoryNode> Children { get; } = new();

        public int Quantity => Instance.Quantity <= 0 ? 1 : Instance.Quantity;
        public double OwnWeight => Info.Weight * Quantity;

        public double InternalWeight => Children.Sum(c => c.OwnWeight + c.InternalWeight);

        public double CarriedWeight => OwnWeight + (Info.IgnoresContainedWeight ? 0 : Children.Sum(c => c.CarriedWeight));

        public bool OverCapacity => Info.IsContainer && Info.Capacity > 0 && InternalWeight > Info.Capacity;
    }

    public enum EncumbranceLevel
    {
        Normal,
        Encumbered,
        HeavilyEncumbered,
        OverCapacity
    }

    public static class InventoryEngine
    {
        public static ItemWeightInfo ReadWeight(string? dataJson)
        {
            var info = new ItemWeightInfo();
            if (string.IsNullOrWhiteSpace(dataJson)) return info;

            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("Weight", out var w) && w.TryGetDouble(out var wv)) info.Weight = wv;
                if (root.TryGetProperty("IsContainer", out var ic) && ic.ValueKind == JsonValueKind.True) info.IsContainer = true;
                if (root.TryGetProperty("Capacity", out var cap) && cap.TryGetDouble(out var cv)) info.Capacity = cv;
                if (root.TryGetProperty("ContainerIgnoresWeight", out var ig) && ig.ValueKind == JsonValueKind.True) info.IgnoresContainedWeight = true;
            }
            catch (JsonException)
            {
            }

            return info;
        }

        public static List<InventoryNode> BuildForCharacter(
            IEnumerable<ItemInstance> all,
            string characterId,
            Func<string, string?> dataJsonByItemId)
        {
            var mine = all.Where(i => i.OwnerCharacterId == characterId).ToList();
            var nodes = mine.ToDictionary(
                i => i.Id,
                i => new InventoryNode { Instance = i, Info = ReadWeight(dataJsonByItemId(i.BaseItemId)) });

            var roots = new List<InventoryNode>();
            foreach (var node in nodes.Values)
            {
                var parentId = node.Instance.ParentInstanceId;
                if (!string.IsNullOrEmpty(parentId) && nodes.TryGetValue(parentId, out var parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }
            return roots;
        }

        public static double TotalCarried(IEnumerable<InventoryNode> roots) => roots.Sum(r => r.CarriedWeight);

        public static double CarryCapacity(int strength) => strength * (App.PM?.Rules.CarryCapacityPerStrength ?? 15.0);

        public static EncumbranceLevel Evaluate(double carried, int strength)
        {
            var rules = App.PM?.Rules;
            if (carried > CarryCapacity(strength)) return EncumbranceLevel.OverCapacity;
            if (carried > strength * (rules?.HeavilyEncumberedPerStrength ?? 10.0)) return EncumbranceLevel.HeavilyEncumbered;
            if (carried > strength * (rules?.EncumberedPerStrength ?? 5.0)) return EncumbranceLevel.Encumbered;
            return EncumbranceLevel.Normal;
        }

        public static long ToBaseUnits(
            IReadOnlyDictionary<string, long> wallet,
            IReadOnlyDictionary<string, int> equalToBaseByCurrencyId)
        {
            long total = 0;
            foreach (var kv in wallet)
            {
                var ratio = equalToBaseByCurrencyId.TryGetValue(kv.Key, out var r) && r > 0 ? r : 1;
                total += kv.Value * ratio;
            }
            return total;
        }

        public static string FallbackGlyph(Currency c)
        {
            if (!string.IsNullOrWhiteSpace(c.Abbreviation)) return c.Abbreviation;
            return string.IsNullOrEmpty(c.Name) ? "?" : c.Name.Substring(0, 1).ToUpperInvariant();
        }
    }
}
