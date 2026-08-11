using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Collections.Generic;
using System.Linq;

namespace Dujahit.Models.Application
{
    public record PdfAbility(string Name, int Score, string Modifier, string Save);
    public record PdfSkill(string Name, string Bonus, string Mark);

    public record CharacterSheetPdf(
        string Name, string Subtitle,
        string Hp, string Ac, string Prof, string Initiative,
        List<PdfAbility> Abilities, List<PdfSkill> Skills,
        List<string> Attacks, List<string> Features, List<string> Spells,
        List<string> Inventory, List<string> Proficiencies, string Backstory);

    public static class CharacterPdfExporter
    {
        static CharacterPdfExporter()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public static void Write(string path, CharacterSheetPdf c)
        {
            Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(t => t.FontSize(10).LineHeight(1.3f));
                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Text(string.IsNullOrWhiteSpace(c.Name) ? "Character" : c.Name).FontSize(22).Bold();
                        if (!string.IsNullOrWhiteSpace(c.Subtitle))
                            col.Item().Text(c.Subtitle).FontSize(11).FontColor("#555555");

                        col.Item().PaddingTop(4).Row(r =>
                        {
                            Stat(r, "HP", c.Hp);
                            Stat(r, "AC", c.Ac);
                            Stat(r, "Prof", c.Prof);
                            Stat(r, "Initiative", c.Initiative);
                        });

                        if (c.Abilities.Count > 0)
                        {
                            Heading(col, "Abilities");
                            for (var start = 0; start < c.Abilities.Count; start += 6)
                            {
                                var slice = c.Abilities.Skip(start).Take(6).ToList();
                                col.Item().Row(r =>
                                {
                                    foreach (var a in slice) AbilityCell(r, a);
                                    for (var pad = slice.Count; pad < 6; pad++) r.RelativeItem();
                                });
                            }
                        }

                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(left =>
                            {
                                if (c.Skills.Count > 0)
                                {
                                    Heading(left, "Skills");
                                    foreach (var s in c.Skills)
                                        left.Item().Text(t =>
                                        {
                                            t.Span(s.Bonus + "  ").Bold();
                                            t.Span(s.Name);
                                            if (!string.IsNullOrEmpty(s.Mark)) t.Span("  (" + s.Mark + ")").FontSize(8).FontColor("#777777");
                                        });
                                }
                            });
                            r.ConstantItem(16);
                            r.RelativeItem().Column(right =>
                            {
                                Bullets(right, "Attacks", c.Attacks);
                                Bullets(right, "Proficiencies", c.Proficiencies);
                            });
                        });

                        Bullets(col, "Features", c.Features);
                        Bullets(col, "Prepared spells", c.Spells);
                        Bullets(col, "Inventory", c.Inventory);

                        if (!string.IsNullOrWhiteSpace(c.Backstory))
                        {
                            Heading(col, "Backstory");
                            col.Item().Text(c.Backstory);
                        }
                    });
                    page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
                });
            }).GeneratePdf(path);
        }

        private static void AbilityCell(RowDescriptor r, PdfAbility a)
        {
            r.RelativeItem().Border(1).BorderColor("#DDDDDD").Padding(4).Column(cc =>
            {
                cc.Item().AlignCenter().Text(a.Name).Bold().FontSize(9);
                cc.Item().AlignCenter().Text(a.Score.ToString()).FontSize(15);
                cc.Item().AlignCenter().Text(a.Modifier).FontColor("#555555");
                cc.Item().AlignCenter().Text("save " + a.Save).FontSize(8).FontColor("#777777");
            });
        }

        private static void Stat(RowDescriptor r, string label, string value)
        {
            r.RelativeItem().Border(1).BorderColor("#DDDDDD").Padding(6).Column(c =>
            {
                c.Item().AlignCenter().Text(label).FontSize(8).FontColor("#777777");
                c.Item().AlignCenter().Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(13).Bold();
            });
        }

        private static void Heading(ColumnDescriptor col, string text)
        {
            col.Item().PaddingTop(6).Text(text).FontSize(13).Bold().FontColor("#8a6d1f");
        }

        private static void Bullets(ColumnDescriptor col, string title, List<string> items)
        {
            if (items == null || items.Count == 0) return;
            Heading(col, title);
            foreach (var i in items)
                if (!string.IsNullOrWhiteSpace(i))
                    col.Item().Text("- " + i);
        }
    }
}
