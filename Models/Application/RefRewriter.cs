using Dujahit.Models.Database;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System;

namespace Dujahit.Models.Application
{
    public static class RefRewriter
    {
        public static async Task<string> RewriteForRenderAsync(string markdown, bool viewerOwnsPage = true)
        {
            if (string.IsNullOrEmpty(markdown)) return markdown ?? "";
            if (!RefResolver.IsReady) return markdown;

            var refs = RefParser.ParseAll(markdown);
            if (refs.Count == 0) return markdown;

            var resolved = new ResolvedRef?[refs.Count];
            for (var i = 0; i < refs.Count; i++)
                resolved[i] = await RefResolver.ResolveAsync(refs[i], viewerOwnsPage);

            var sb = new StringBuilder(markdown);
            for (var i = refs.Count - 1; i >= 0; i--)
            {
                var r = refs[i];
                var rr = resolved[i];

                string replacement;
                if (rr == null)
                {
                    replacement = $"[{r.Type}:{r.Id}]";
                }
                else
                {
                    replacement = MarkdownLink(rr);
                }

                sb.Remove(r.Start, r.Length);
                sb.Insert(r.Start, replacement);
            }

            return sb.ToString();
        }

        private static string MarkdownLink(ResolvedRef rr)
        {
            var display = EscapeForLinkText(rr.DisplayText);
            if (!rr.IsClickable) return display;
            var tip = EscapeForLinkTitle(rr.Tooltip);
            return $"[{display}](dujahit://ref/{rr.Type}/{rr.Id} \"{tip}\")";
        }

        private static string EscapeForLinkText(string s)
            => s.Replace("[", "\\[").Replace("]", "\\]");

        private static string EscapeForLinkTitle(string s)
            => s.Replace("\"", "\\\"").Replace("\n", " ");

        public static async Task<string> RewriteForSaveAsync(
            string markdown,
            string campaignId,
            NotePageRepository repo,
            Func<string, string, Task>? broadcast = null,
            bool editorOwnsPage = true)
        {
            if (string.IsNullOrEmpty(markdown)) return markdown ?? "";

            // Just makin sure that references in shared pages are intact and shows up right
            if (!editorOwnsPage) return markdown;

            var refs = RefParser.ParseAll(markdown);
            if (refs.Count == 0) return markdown;

            var sb = new StringBuilder(markdown);
            var seenWriteBacks = new HashSet<string>();

            for (var i = refs.Count - 1; i >= 0; i--)
            {
                var r = refs[i];
                if (r.Type != "quicknote") continue;

                var page = await repo.GetQuickNoteBySlugAsync(campaignId, r.Id);
                if (page == null) continue;

                var sourceText = page.ContentMarkdown ?? "";

                if (r.Text == null)
                {
                    var replaced = $"<ref type=\"quicknote\" id=\"{r.Id}\" text=\"{EscapeAttr(sourceText)}\"/>";
                    sb.Remove(r.Start, r.Length);
                    sb.Insert(r.Start, replaced);
                }
                else if (r.Text != sourceText && !seenWriteBacks.Contains(r.Id))
                {
                    seenWriteBacks.Add(r.Id);
                    var updated = await repo.UpdateQuickNoteTextAsync(page.Id, r.Text);
                    if (updated != null)
                    {
                        RefResolver.Invalidate("quicknote", r.Id);
                        if (broadcast != null)
                            await broadcast(updated.Id, "updated");
                    }
                }
            }

            return sb.ToString();
        }

        private static string EscapeAttr(string s)
            => s.Replace("\"", "&quot;").Replace("'", "&#39;").Replace("\n", "\\n");

        // Refs nest, one page drags a chain behind it. The visited set is what stops a loop.
        public static async Task<(List<NotePage> PrivateNotes, List<string> QuickNoteSlugs)> CollectPrivateRefsAsync(
            NotePage page, string ownerUserId, NotePageRepository repo)
        {
            var privateNotes = new List<NotePage>();
            var quickNotes = new List<string>();
            var visited = new HashSet<string> { page.Id };
            var queue = new Queue<string>();
            queue.Enqueue(page.ContentMarkdown ?? "");

            while (queue.Count > 0)
            {
                foreach (var r in RefParser.ParseAll(queue.Dequeue()))
                {
                    if (r.Type == "quicknote")
                    {
                        if (!quickNotes.Contains(r.Id)) quickNotes.Add(r.Id);
                        continue;
                    }
                    if (r.Type != "note" || !visited.Add(r.Id)) continue;

                    var target = await repo.GetByIdAsync(r.Id);
                    if (target == null) continue;
                    if (target.Scope == NotePageScope.Private
                        && string.Equals(target.OwnerUserId, ownerUserId, StringComparison.Ordinal))
                    {
                        privateNotes.Add(target);
                        queue.Enqueue(target.ContentMarkdown ?? "");
                    }
                }
            }
            return (privateNotes, quickNotes);
        }
    }
}