using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Docnet.Core;
using Docnet.Core.Models;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Dujahit.Models.Application
{
    // Pdfs become pngs at import and the app never opens one again, so nothing buried in there gets to run
    public static class PdfRasterizer
    {
        public static int RenderToPngs(string pdfPath, string outDir, string idPrefix, double scale = 2.0)
        {
            if (!File.Exists(pdfPath)) return 0;
            Directory.CreateDirectory(outDir);
            var bytes = File.ReadAllBytes(pdfPath);

            using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(scale));
            var count = docReader.GetPageCount();
            for (var i = 0; i < count; i++)
            {
                using var page = docReader.GetPageReader(i);
                var raw = page.GetImage();
                var w = page.GetPageWidth();
                var h = page.GetPageHeight();
                if (raw == null || w <= 0 || h <= 0 || raw.Length < w * h * 4) continue;
                FlattenOntoWhite(raw);
                SavePng(raw, w, h, Path.Combine(outDir, idPrefix + "_p" + i + ".png"));
            }
            return count;
        }

        private static void FlattenOntoWhite(byte[] bgra)
        {
            for (var p = 0; p + 3 < bgra.Length; p += 4)
            {
                var a = bgra[p + 3];
                if (a == 255) continue;
                bgra[p] = (byte)(bgra[p] * a / 255 + (255 - a));
                bgra[p + 1] = (byte)(bgra[p + 1] * a / 255 + (255 - a));
                bgra[p + 2] = (byte)(bgra[p + 2] * a / 255 + (255 - a));
                bgra[p + 3] = 255;
            }
        }

        private static void SavePng(byte[] bgra, int w, int h, string path)
        {
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
                Marshal.Copy(bgra, 0, fb.Address, Math.Min(bgra.Length, fb.RowBytes * h));
            bmp.Save(path);
        }
    }
}
