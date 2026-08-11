using Avalonia;
using Avalonia.Media.Imaging;
using Dujahit.Models.Database;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dujahit.Models.Application
{
    public static class TokenImageGuard
    {
        private const int MaxInputBytes = 4 * 1024 * 1024;

        private const int MaxDimension = 256;

        private const int MaxOutputBytes = 1 * 1024 * 1024;

        // A creature is a circle an inch across, scenery can be a rug over six squares. It gets its own headroom.
        private const int MaxPropInputBytes = 16 * 1024 * 1024;

        private const int MaxPropDimension = 1024;

        private const int MaxPropOutputBytes = 4 * 1024 * 1024;

        public static string TokensDir(string campaignId) =>
            Path.Combine(GlobalVariables.AppDataLocal, "assets", campaignId, "tokens");

        public static byte[]? Sanitize(byte[]? raw) => Sanitize(raw, false);

        public static byte[]? Sanitize(byte[]? raw, bool isProp)
        {
            var maxIn = isProp ? MaxPropInputBytes : MaxInputBytes;
            var maxDim = isProp ? MaxPropDimension : MaxDimension;
            var maxOut = isProp ? MaxPropOutputBytes : MaxOutputBytes;

            if (raw == null || raw.Length == 0 || raw.Length > maxIn) return null;
            if (!LooksLikeImage(raw)) return null;

            try
            {
                using var inMs = new MemoryStream(raw, writable: false);
                using var bmp = new Bitmap(inMs);

                var clean = Fit(bmp, maxDim);
                using var outMs = new MemoryStream();
                clean.Save(outMs);
                if (!ReferenceEquals(clean, bmp)) clean.Dispose();

                var bytes = outMs.ToArray();
                return bytes.Length > maxOut ? null : bytes;
            }
            catch
            {
                return null;
            }
        }

        public static string? SaveForCampaign(string campaignId, byte[]? raw) => SaveForCampaign(campaignId, raw, false);

        public static string? SaveForCampaign(string campaignId, byte[]? raw, bool isProp)
        {
            var clean = Sanitize(raw, isProp);
            if (clean == null) return null;

            var dir = TokensDir(campaignId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Hash(clean) + ".png");
            if (!File.Exists(path)) File.WriteAllBytes(path, clean);
            return path;
        }

        public static string Hash(byte[] bytes)
        {
            var sha = SHA256.HashData(bytes);
            var sb = new StringBuilder(sha.Length * 2);
            foreach (var b in sha) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static Bitmap Fit(Bitmap bmp, int maxDimension)
        {
            var w = bmp.PixelSize.Width;
            var h = bmp.PixelSize.Height;
            if (w <= maxDimension && h <= maxDimension) return bmp;

            double factor = (double)maxDimension / Math.Max(w, h);
            var target = new PixelSize(
                Math.Max(1, (int)Math.Round(w * factor)),
                Math.Max(1, (int)Math.Round(h * factor)));
            return bmp.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        }

        private static bool LooksLikeImage(byte[] b)
        {
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;
            if (b.Length >= 4 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return true;
            if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
                && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return true;
            return false;
        }
    }
}
