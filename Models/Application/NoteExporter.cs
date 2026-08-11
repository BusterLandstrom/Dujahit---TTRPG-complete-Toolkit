using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Dujahit.Models.Application
{
    public record NoteExportPage(int Depth, string Title, string Content);

    public static class NoteExporter
    {
        static NoteExporter()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public static string ToMarkdown(IReadOnlyList<NoteExportPage> pages)
        {
            var sb = new StringBuilder();
            foreach (var p in pages)
            {
                var level = Math.Clamp(p.Depth + 1, 1, 6);
                sb.Append('\n').Append(new string('#', level)).Append(' ').Append(p.Title.Trim()).Append('\n').Append('\n');
                if (!string.IsNullOrWhiteSpace(p.Content))
                    sb.Append(p.Content.Trim()).Append('\n');
            }
            return sb.ToString().Trim() + "\n";
        }

        public static void ToPdf(string path, IReadOnlyList<NoteExportPage> pages)
        {
            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Margin(42);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(t => t.FontSize(11).LineHeight(1.35f));
                    page.Content().Column(col =>
                    {
                        col.Spacing(4);
                        foreach (var p in pages) RenderPage(col, p);
                    });
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf(path);
        }

        private static void RenderPage(ColumnDescriptor col, NoteExportPage p)
        {
            var titleSize = p.Depth switch { 0 => 21f, 1 => 17f, 2 => 14f, _ => 12f };
            col.Item().PaddingTop(p.Depth == 0 ? 2 : 12).Text(p.Title.Trim()).FontSize(titleSize).Bold();
            RenderBody(col, p.Content ?? "");
        }

        private static void RenderBody(ColumnDescriptor col, string content)
        {
            var lines = content.Replace("\r\n", "\n").Split('\n');
            var i = 0;
            while (i < lines.Length)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("```"))
                {
                    var code = new List<string>();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```")) { code.Add(lines[i]); i++; }
                    i++;
                    col.Item().PaddingVertical(3).Background("#F2F2F5").Padding(6)
                        .Text(string.Join("\n", code)).FontFamily(Fonts.CourierNew).FontSize(10);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed)) { i++; continue; }

                if (Regex.IsMatch(trimmed, @"^(---+|\*\*\*+|___+)$"))
                {
                    col.Item().PaddingVertical(4).LineHorizontal(1).LineColor("#CCCCCC");
                    i++;
                    continue;
                }

                var head = Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$");
                if (head.Success)
                {
                    var lvl = head.Groups[1].Value.Length;
                    col.Item().PaddingTop(4).Text(Strip(head.Groups[2].Value)).FontSize(16f - lvl).Bold();
                    i++;
                    continue;
                }

                if (trimmed.StartsWith("> "))
                {
                    col.Item().BorderLeft(2).BorderColor("#BFA050").PaddingLeft(8)
                        .Text(Strip(trimmed.Substring(2))).Italic();
                    i++;
                    continue;
                }

                if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
                {
                    col.Item().Text(trimmed).FontFamily(Fonts.CourierNew).FontSize(10);
                    i++;
                    continue;
                }

                var bullet = Regex.Match(trimmed, @"^[-*+]\s+(.*)$");
                if (bullet.Success)
                {
                    col.Item().Text("-  " + Strip(bullet.Groups[1].Value));
                    i++;
                    continue;
                }

                var numbered = Regex.Match(trimmed, @"^(\d+)\.\s+(.*)$");
                if (numbered.Success)
                {
                    col.Item().Text(numbered.Groups[1].Value + ".  " + Strip(numbered.Groups[2].Value));
                    i++;
                    continue;
                }

                col.Item().Text(Strip(trimmed));
                i++;
            }
        }

        // Best effort, the pdf is flat text so the inline markers just come off and a link keeps its target in parentheses.
        private static string Strip(string s)
        {
            s = Regex.Replace(s, @"<ref\b[^>]*>", "");
            s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)");
            s = Regex.Replace(s, @"(\*\*|__)(.+?)\1", "$2");
            s = Regex.Replace(s, @"(\*|_)(.+?)\1", "$2");
            s = Regex.Replace(s, @"~~(.+?)~~", "$1");
            s = s.Replace("`", "");
            return s.Trim();
        }
    }
}
