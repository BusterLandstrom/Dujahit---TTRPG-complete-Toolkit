using Dujahit.Models.Database;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Dujahit.Models.Application
{
    public static class RefResolver
    {
        private static DatabaseManager? _db;
        private static NotePageRepository? _notes;
        private static string? _campaignId;
        private static string? _userId;

        private static readonly ConcurrentDictionary<string, ResolvedRef> _cache = new();

        public static event Action<string, string>? NavigateRequested;

        public static bool IsReady => _db != null && _notes != null && _campaignId != null;

        public static void Init(DatabaseManager db, NotePageRepository notes,
                                string campaignId, string userId)
        {
            _db = db;
            _notes = notes;
            _campaignId = campaignId;
            _userId = userId;
            _cache.Clear();
        }

        public static void Reset()
        {
            _db = null;
            _notes = null;
            _campaignId = null;
            _userId = null;
            _cache.Clear();
        }

        public static void Invalidate(string type, string id)
        {
            _cache.TryRemove($"{type}|{id}|True", out _);
            _cache.TryRemove($"{type}|{id}|False", out _);
        }

        public static void InvalidateAll() => _cache.Clear();

        public static void RaiseNavigateRequested(string type, string id)
            => NavigateRequested?.Invoke(type, id);

        public static async Task<ResolvedRef?> ResolveAsync(ParsedRef r, bool viewerOwnsPage = true)
        {
            if (!IsReady) return null;

            // Owning the page changes the answer so it has to be in the key.
            var cacheKey = $"{r.Type}|{r.Id}|{viewerOwnsPage}";
            if (_cache.TryGetValue(cacheKey, out var hit))
            {
                return hit;
            }

            // Quicknote slugs are per user, so on somebody else's page qn-3 hits MY qn-3. Embedded text is all that ref can mean.
            if (!viewerOwnsPage && r.Type == "quicknote")
            {
                var frozen = new ResolvedRef("quicknote", r.Id, r.Text ?? Humanize(r.Id), "Something private to the page's owner.", IsClickable: false);
                _cache[cacheKey] = frozen;
                return frozen;
            }

            var resolved = r.Type switch
            {
                "npc" => await ResolveNpcAsync(r.Id),
                "character" => await ResolveCharacterAsync(r.Id),
                "item" => await ResolveItemAsync(r.Id),
                "note" => await ResolveNoteAsync(r.Id),
                "codex" => await ResolveCodexAsync(r.Id),
                "mindmap" => await ResolveMindmapNodeAsync(r.Id),
                "quicknote" => await ResolveQuickNoteAsync(r.Id),
                _ => new ResolvedRef(r.Type, r.Id, $"[unknown type: {r.Type}]", "Unknown ref type", false)
            };

            // On somebody else's page a missing target is usually just private to them and not deleted, so it drops to plain words. No point marking it broken for a reader who was never meant to follow it.
            if (resolved is { IsBroken: true } && !viewerOwnsPage)
                resolved = new ResolvedRef(r.Type, r.Id, r.Text ?? Humanize(r.Id), "Something private to the page's owner.", IsClickable: false);

            if (resolved != null) _cache[cacheKey] = resolved;
            return resolved;
        }

        private static string Humanize(string id) => (id ?? "").Replace('-', ' ');

        private static async Task<ResolvedRef?> ResolveNpcAsync(string slug)
        {
            try
            {
                await using var conn = await _db!.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Name, Tags FROM Characters
                    WHERE CampaignId = $cid AND CharacterKind = 'npc' AND Slug = $slug
                    LIMIT 1
                """;
                cmd.Parameters.AddWithValue("$cid", _campaignId!);
                cmd.Parameters.AddWithValue("$slug", slug);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return Broken("npc", slug);

                var name = r.GetString(0);
                var tags = r.IsDBNull(1) ? "" : r.GetString(1);
                var tip = string.IsNullOrEmpty(tags) || tags == "[]"
                    ? "NPC"
                    : $"NPC - {tags.Trim('[', ']', '"')}";
                return new ResolvedRef("npc", slug, name, tip, IsClickable: true);
            }
            catch { return Broken("npc", slug); }
        }

        private static async Task<ResolvedRef?> ResolveCharacterAsync(string slug)
        {
            try
            {
                await using var conn = await _db!.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Name FROM Characters
                    WHERE CampaignId = $cid AND CharacterKind = 'pc' AND Slug = $slug
                    LIMIT 1
                """;
                cmd.Parameters.AddWithValue("$cid", _campaignId!);
                cmd.Parameters.AddWithValue("$slug", slug);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return Broken("character", slug);

                return new ResolvedRef("character", slug, r.GetString(0), "Character", IsClickable: true);
            }
            catch { return Broken("character", slug); }
        }

        private static async Task<ResolvedRef?> ResolveItemAsync(string slug)
        {
            try
            {
                await using var conn = await _db!.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Name, ItemType FROM Items WHERE Slug = $slug LIMIT 1
                """;
                cmd.Parameters.AddWithValue("$slug", slug);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return Broken("item", slug);

                var name = r.GetString(0);
                var type = r.GetString(1);
                return new ResolvedRef("item", slug, name, $"Item - {type}", IsClickable: true);
            }
            catch { return Broken("item", slug); }
        }

        private static async Task<ResolvedRef?> ResolveNoteAsync(string id)
        {
            try
            {
                await using var conn = await _db!.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Title, Scope FROM NotePages
                    WHERE CampaignId = $cid AND Id = $id
                      AND Scope IN ('private','shared','campaign_story')
                    LIMIT 1
                """;
                cmd.Parameters.AddWithValue("$cid", _campaignId!);
                cmd.Parameters.AddWithValue("$id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return Broken("note", id);

                var title = r.GetString(0);
                var scope = r.GetString(1);
                return new ResolvedRef("note", id, title, $"Note - {scope}", IsClickable: true);
            }
            catch { return Broken("note", id); }
        }

        // Same table as a note, scope pinned. Otherwise you cannot tell the two apart.
        private static async Task<ResolvedRef?> ResolveCodexAsync(string id)
        {
            try
            {
                await using var conn = await _db!.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Title FROM NotePages
                    WHERE CampaignId = $cid AND Id = $id AND Scope = 'campaign_story'
                    LIMIT 1
                """;
                cmd.Parameters.AddWithValue("$cid", _campaignId!);
                cmd.Parameters.AddWithValue("$id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return Broken("codex", id);
                return new ResolvedRef("codex", id, r.GetString(0), "Codex chapter", IsClickable: true);
            }
            catch { return Broken("codex", id); }
        }

        private static async Task<ResolvedRef?> ResolveMindmapNodeAsync(string slug)
        {
            try
            {
                await using var conn = await _db!.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Title, Kind FROM MindmapNodes WHERE CampaignId = $cid AND Slug = $slug LIMIT 1";
                cmd.Parameters.AddWithValue("$cid", _campaignId!);
                cmd.Parameters.AddWithValue("$slug", slug);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return Broken("mindmap", slug);

                var title = r.GetString(0);
                var kind = r.IsDBNull(1) ? "note" : r.GetString(1);
                return new ResolvedRef("mindmap", slug, string.IsNullOrWhiteSpace(title) ? slug : title, $"Mindmap - {kind}", IsClickable: true);
            }
            catch { return Broken("mindmap", slug); }
        }

        private static async Task<ResolvedRef?> ResolveQuickNoteAsync(string slug)
        {
            try
            {
                var page = await _notes!.GetQuickNoteBySlugAsync(_campaignId!, slug);
                if (page == null) return Broken("quicknote", slug);

                var content = string.IsNullOrEmpty(page.ContentMarkdown)
                    ? "(empty)"
                    : page.ContentMarkdown;
                var display = content.Length > 60 ? content.Substring(0, 57) + "..." : content;
                return new ResolvedRef("quicknote", slug, display, $"Quick note {slug}\n{content}",
                                       IsClickable: false);
            }
            catch { return Broken("quicknote", slug); }
        }

        private static ResolvedRef Broken(string type, string id)
            => new(type, id, $"[broken {type}: {id}]", "This reference points to something that no longer exists.", IsClickable: false, IsBroken: true);
    }

    public record ResolvedRef(
        string Type, string Id, string DisplayText, string Tooltip, bool IsClickable, bool IsBroken = false);
}