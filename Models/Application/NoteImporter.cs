using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dujahit.Models.Application
{
    public class ImportedNote
    {
        public string Title { get; set; } = "Untitled";
        public string Markdown { get; set; } = "";
        public int SortOrder { get; set; }
        public List<ImportedNote> Children { get; } = new();
    }

    public class NoteImportResult
    {
        public List<ImportedNote> Roots { get; } = new();
        public List<string> Warnings { get; } = new();
        public int PageCount { get; set; }
    }

    public static class NoteImporter
    {
        // Caps so a daft zip cannot eat the app.
        public const int MaxDepth = 6;
        public const int MaxPages = 500;  // Arbitrary, felt like plenty
        public const long MaxEntryBytes = 2 * 1024 * 1024;
        public const long MaxTotalBytes = 32 * 1024 * 1024;

        // This one is the folder's own text, it does not become a page of its own.
        public static readonly string[] IndexNames = { "index.md", "_index.md" };

        private static readonly Regex _orderPrefix = new(@"^\s*(\d{1,4})\s*[-_.)]?\s+", RegexOptions.Compiled);

        public static NoteImportResult ReadZip(string zipPath)
        {
            using var stream = File.OpenRead(zipPath);
            return ReadZip(stream);
        }

        public static NoteImportResult ReadZip(Stream stream)
        {
            var result = new NoteImportResult();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            var folders = new Dictionary<string, ImportedNote>(StringComparer.OrdinalIgnoreCase);
            var bodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<(string Dir, string Name, ZipArchiveEntry Entry)>();
            long total = 0;

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var full = entry.FullName.Replace('\\', '/');
                // Belt and braces, nothing here writes a file out.
                if (full.Split('/').Any(p => p == ".." || p == ".")) { result.Warnings.Add("Skipped " + full + ", the path tries to escape the zip"); continue; }
                if (full.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

                if (entry.Length > MaxEntryBytes) { result.Warnings.Add("Skipped " + full + ", it is over 2 MB"); continue; }
                total += entry.Length;
                if (total > MaxTotalBytes) { result.Warnings.Add("Stopped reading, the zip is over 32 MB of markdown"); break; }

                var slash = full.LastIndexOf('/');
                var dir = slash < 0 ? "" : full.Substring(0, slash);
                var name = slash < 0 ? full : full.Substring(slash + 1);

                if (Depth(dir) > MaxDepth) { result.Warnings.Add("Skipped " + full + ", nested deeper than " + MaxDepth); continue; }

                if (IndexNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    if (dir.Length > 0) bodies[dir] = Read(entry);
                    continue;
                }
                files.Add((dir, name, entry));
            }

            foreach (var (dir, _, _) in files) EnsureFolders(dir, folders, result);
            foreach (var d in bodies.Keys) EnsureFolders(d, folders, result);

            foreach (var kv in bodies)
                if (folders.TryGetValue(kv.Key, out var node)) node.Markdown = kv.Value;

            foreach (var (dir, name, entry) in files)
            {
                var raw = name.Substring(0, name.Length - 3);
                var page = new ImportedNote
                {
                    Title = CleanTitle(raw, out var order),
                    SortOrder = order,
                    Markdown = Read(entry)
                };
                if (dir.Length == 0) result.Roots.Add(page);
                else if (folders.TryGetValue(dir, out var parent)) parent.Children.Add(page);
            }

            foreach (var kv in folders)
            {
                var slash = kv.Key.LastIndexOf('/');
                if (slash < 0) { if (!result.Roots.Contains(kv.Value)) result.Roots.Add(kv.Value); }
                else if (folders.TryGetValue(kv.Key.Substring(0, slash), out var parent) && !parent.Children.Contains(kv.Value))
                    parent.Children.Add(kv.Value);
            }

            Sort(result.Roots);
            result.PageCount = Count(result.Roots);

            if (result.PageCount == 0) result.Warnings.Add("No markdown files were found in the zip");
            if (result.PageCount > MaxPages)
            {
                result.Warnings.Add("The zip holds " + result.PageCount + " pages and the limit is " + MaxPages);
                result.Roots.Clear();
                result.PageCount = 0;
            }
            return result;
        }

        private static void EnsureFolders(string dir, Dictionary<string, ImportedNote> folders, NoteImportResult result)
        {
            if (dir.Length == 0) return;
            var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var path = "";
            foreach (var p in parts)
            {
                path = path.Length == 0 ? p : path + "/" + p;
                if (folders.ContainsKey(path)) continue;
                folders[path] = new ImportedNote { Title = CleanTitle(p, out var order), SortOrder = order };
            }
        }

        private static int Depth(string dir) => dir.Length == 0 ? 0 : dir.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;

        private static string Read(ZipArchiveEntry entry)
        {
            using var s = entry.Open();
            using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            // Trim eats a leading indent so a code block right at the top loses it, nobody has hit that yet.
            return r.ReadToEnd().Replace("\r\n", "\n").Trim();
        }

        public static string CleanTitle(string raw, out int order)
        {
            // No number on the front means it sorts last.
            order = int.MaxValue;
            var t = raw.Trim();
            var m = _orderPrefix.Match(t);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            {
                order = n;
                t = t.Substring(m.Length).Trim();
            }
            t = t.Replace('_', ' ').Trim();
            return string.IsNullOrWhiteSpace(t) ? "Untitled" : t;
        }

        private static void Sort(List<ImportedNote> list)
        {
            list.Sort((a, b) =>
            {
                var c = a.SortOrder.CompareTo(b.SortOrder);
                return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var n in list) Sort(n.Children);
        }

        private static int Count(List<ImportedNote> list)
        {
            var n = 0;
            foreach (var x in list) { n++; n += Count(x.Children); }
            return n;
        }

        public static void WriteExampleZip(string path)
        {
            using var fs = File.Create(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            void Add(string entryPath, string body)
            {
                var e = zip.CreateEntry(entryPath);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(body.Replace("\r\n", "\n"));
            }

            // The readme in here IS the guide, keep the two in step.
            Add("README.md", ExampleReadme);
            Add("01 Start here.md", ExampleStart);
            Add("02 Locations/index.md", ExampleLocationsIndex);
            Add("02 Locations/01 Waterdeep.md", ExampleWaterdeep);
            Add("02 Locations/02 Neverwinter/index.md", ExampleNeverwinter);
            Add("02 Locations/02 Neverwinter/01 The Docks.md", ExampleDocks);
            Add("03 People/01 Old Tom.md", ExamplePeople);
        }

        private const string ExampleReadme =
"# How to structure an import zip\n\n" +
"Every .md file in here becomes a note page. Every folder becomes a note page too, and everything inside\n" +
"the folder becomes its subpages. Nest folders as deep as you like up to six levels.\n\n" +
"## The rules\n\n" +
"1. A .md file becomes a page. The file name is the page title, without the .md.\n" +
"2. A folder becomes a page. Put an index.md inside it to give that page its own text, or leave it out and the page starts empty.\n" +
"3. A number in front of the name sets the order, so 01, 02, 03. The number is stripped off the title.\n" +
"4. Underscores in a name turn into spaces.\n" +
"5. Anything that is not a .md file is ignored, so images and pdfs are dropped.\n\n" +
"## What this example builds\n\n" +
"```\n" +
"Start here\n" +
"Locations\n" +
"  Waterdeep\n" +
"  Neverwinter\n" +
"    The Docks\n" +
"People\n" +
"  Old Tom\n" +
"```\n\n" +
"Locations and Neverwinter get their text from the index.md sitting inside them. People has no index.md,\n" +
"so it comes in as an empty page with one subpage under it.\n\n" +
"This README becomes a page as well. Delete it before you import if you do not want it.\n";

        private const string ExampleStart =
"This is a top level page because the file sits at the root of the zip.\n\n" +
"Markdown comes through as written, so headings, **bold**, lists and tables all survive.\n\n" +
"- a list item\n" +
"- another one\n\n" +
"> A quote block.\n";

        private const string ExampleLocationsIndex =
"This text belongs to the Locations page itself, because it came from index.md inside the Locations folder.\n\n" +
"The pages under it are its subpages.\n";

        private const string ExampleWaterdeep =
"A subpage of Locations.\n\n" +
"## Notable spots\n\n" +
"The Yawning Portal, the Field Ward, and whatever else your table cares about.\n";

        private const string ExampleNeverwinter =
"Neverwinter is a folder as well as a page, so it can hold its own subpages.\n";

        private const string ExampleDocks =
"Two levels deep. Folder inside a folder.\n";

        private const string ExamplePeople =
"The People folder has no index.md, so the People page arrives empty and this sits underneath it.\n";
    }
}
