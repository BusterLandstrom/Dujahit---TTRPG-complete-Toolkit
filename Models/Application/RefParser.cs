using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace Dujahit.Models.Application
{
    public static class RefParser
    {
        private static readonly Regex _pattern = new(
            """<ref\s+type=['"](?<type>[^'"]+)['"].*?id=['"](?<id>[^'"]+)['"](?:\s+text=['"](?<text>[^'"]*)['"])?\s*(?:/)>""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<ParsedRef> ParseAll(string markdown)
        {
            var result = new List<ParsedRef>();
            if (string.IsNullOrEmpty(markdown)) return result;
            foreach (Match m in _pattern.Matches(markdown))
            {
                result.Add(new ParsedRef(
                    Type: m.Groups["type"].Value.ToLowerInvariant(),
                    Id: m.Groups["id"].Value,
                    Text: m.Groups["text"].Success ? UnescapeAttr(m.Groups["text"].Value) : null,
                    Start: m.Index,
                    Length: m.Length,
                    Raw: m.Value));
            }
            return result;
        }

        private static string UnescapeAttr(string s)
            => s.Replace("&quot;", "\"").Replace("&#39;", "'").Replace("\\n", "\n");
    }

    // One parsed ref tag, could be better maybe but it's easy to pass around
    public record ParsedRef(
        string Type, string Id, string? Text,
        int Start, int Length, string Raw);
}