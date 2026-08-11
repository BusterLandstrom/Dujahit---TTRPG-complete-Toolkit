using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Dujahit.Models.Application
{
    public static class SlugHelper
    {
        private static readonly Regex Valid =
            new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        public static string Suggest(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            var normalized = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            var s = sb.ToString().ToLowerInvariant();

            s = Regex.Replace(s, "[^a-z0-9]+", "-");
            return s.Trim('-');
        }

        public static bool IsValid(string? slug) =>
            !string.IsNullOrWhiteSpace(slug) && Valid.IsMatch(slug);

        public static string EnsureUnique(string baseSlug, HashSet<string> existing)
        {
            if (!existing.Contains(baseSlug)) return baseSlug;
            for (var i = 2; i < 10000; i++)
            {
                var candidate = $"{baseSlug}-{i}";
                if (!existing.Contains(candidate)) return candidate;
            }
            throw new InvalidOperationException("Could not find a unique slug.");
        }
    }

    public static class TagsJson
    {
        public static List<string> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static string Serialise(IEnumerable<string> tags)
        {
            var clean = tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .OrderBy(t => t)
                .ToList();
            return JsonSerializer.Serialize(clean);
        }
    }
}