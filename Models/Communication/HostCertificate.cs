using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dujahit.Models.Database;

namespace Dujahit.Models.Communication
{
    public static class HostCertificate
    {
        private static readonly string _path = Path.Combine(GlobalVariables.AppDataLocal, "host-cert.pfx");
        private static X509Certificate2? _cached;

        public static X509Certificate2 LoadOrCreate()
        {
            if (_cached != null) return _cached;
            if (File.Exists(_path))
            {
                try
                {
                    _cached = new X509Certificate2(_path, "", X509KeyStorageFlags.Exportable);
                    return _cached;
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("Could not read the host certificate, minting a fresh one, players holding the old invite need a new one", ex);
                }
            }
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=Dujahit host", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var fresh = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, fresh.Export(X509ContentType.Pfx, ""));
            _cached = new X509Certificate2(_path, "", X509KeyStorageFlags.Exportable);
            return _cached;
        }

        public static string Fingerprint(X509Certificate2 cert) => cert.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();

        public static string ShortFingerprint(X509Certificate2 cert) => Fingerprint(cert)[..12];

        public static bool Matches(X509Certificate? cert, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected)) return cert != null;
            if (cert == null) return false;
            var hex = Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).ToLowerInvariant();
            var want = expected.Trim().ToLowerInvariant();
            return want.Length >= 12 && hex.StartsWith(want, StringComparison.Ordinal);
        }
    }
}
