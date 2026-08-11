using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Dujahit.Models.Application
{
    public static class CharacterTokenRenderer
    {
        public static string Initials(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var t = name.Trim();
            return t.Length >= 2 ? t.Substring(0, 2).ToUpperInvariant() : t.ToUpperInvariant();
        }

        public static Bitmap? Resolve(string? name, string? colorHex, string? imagePath)
        {
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try { return new Bitmap(imagePath); }
                catch (Exception ex) { ErrorLog.Log($"[CharacterToken] image load failed", ex); }
            }
            return RenderInitials(name, colorHex);
        }

        public static Bitmap? RenderInitials(string? name, string? colorHex)
        {
            try
            {
                var col = Color.Parse(string.IsNullOrWhiteSpace(colorHex) ? "#90A4AE" : colorHex);
                const int s = 64;
                var rtb = new RenderTargetBitmap(new PixelSize(s, s), new Vector(96, 96));
                using (var ctx = rtb.CreateDrawingContext())
                {
                    var fill = new SolidColorBrush(col);
                    var ring = new Pen(new SolidColorBrush(Color.FromArgb(255, 20, 20, 30)), 3);
                    ctx.DrawEllipse(fill, ring, new Point(s / 2.0, s / 2.0), s / 2.0 - 3, s / 2.0 - 3);

                    var glyph = Initials(name);
                    var lum = 0.299 * col.R + 0.587 * col.G + 0.114 * col.B;
                    var ink = lum > 140 ? Colors.Black : Colors.White;
                    var ft = new FormattedText(
                        glyph,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        Typeface.Default,
                        28,
                        new SolidColorBrush(ink));
                    ctx.DrawText(ft, new Point((s - ft.Width) / 2.0, (s - ft.Height) / 2.0));
                }
                return rtb;
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[CharacterToken] initials render failed", ex);
                return null;
            }
        }
    }
}
