using System;
using System.Collections.Generic;
using System.Linq;

namespace Dujahit.Models.Communication
{
    public static class PlayerColorRegistry
    {
        private static readonly object _gate = new();
        private static readonly Dictionary<string, string> _byUser = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] _palette =
        {
            "#FFD700", "#4FC3F7", "#E57373", "#81C784", "#BA68C8", "#FFB74D",
            "#4DD0E1", "#F06292", "#AED581", "#9575CD", "#FF8A65", "#A1887F"
        };

        public static string GetOrAssign(string userId)
        {
            lock (_gate)
            {
                if (_byUser.TryGetValue(userId, out var existing)) return existing;
                var used = new HashSet<string>(_byUser.Values, StringComparer.OrdinalIgnoreCase);
                var free = _palette.FirstOrDefault(c => !used.Contains(c)) ?? FallbackFor(userId);
                _byUser[userId] = free;
                return free;
            }
        }

        public static bool TryChange(string userId, string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return false;
            lock (_gate)
            {
                var taken = _byUser.FirstOrDefault(kv => string.Equals(kv.Value, hex, StringComparison.OrdinalIgnoreCase));
                if (taken.Key != null && !string.Equals(taken.Key, userId, StringComparison.OrdinalIgnoreCase))
                    return false;
                _byUser[userId] = hex;
                return true;
            }
        }

        public static string? Get(string userId)
        {
            lock (_gate) { return _byUser.TryGetValue(userId, out var c) ? c : null; }
        }

        public static IReadOnlyDictionary<string, string> Snapshot()
        {
            lock (_gate) { return new Dictionary<string, string>(_byUser, StringComparer.OrdinalIgnoreCase); }
        }

        public static void Remove(string userId)
        {
            lock (_gate) { _byUser.Remove(userId); }
        }

        private static string FallbackFor(string seed)
        {
            int h = 0;
            foreach (var ch in seed) h = (h * 31 + ch) & 0x7FFFFFFF;
            return HslToHex(h % 360, 0.6, 0.6);
        }

        private static string HslToHex(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            int ri = (int)Math.Round((r + m) * 255);
            int gi = (int)Math.Round((g + m) * 255);
            int bi = (int)Math.Round((b + m) * 255);
            return $"#{ri:X2}{gi:X2}{bi:X2}";
        }
    }
}
