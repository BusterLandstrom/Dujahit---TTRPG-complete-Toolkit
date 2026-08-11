using Dujahit.Models.Database;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Dujahit.Models.Application
{
    public static class FirewallHelper
    {

        /*
            This is not cross-platform supported lol I need to fix that and it's shit I do not think this is the best way, for now I do not want to force admin rights we'll see
        */

        public static void EnsureFirewallRules()
        {
            if (!OperatingSystem.IsWindows()) return;

            string exePath = Process.GetCurrentProcess().MainModule!.FileName;

            if (HasRunBefore(exePath)) return;

            string script = $"""
            $name = '{GlobalVariables.AppName}'
            $exe  = '{exePath}'
            Remove-NetFirewallRule -DisplayName "$name Inbound"  -ErrorAction SilentlyContinue
            Remove-NetFirewallRule -DisplayName "$name Outbound" -ErrorAction SilentlyContinue
            New-NetFirewallRule -DisplayName "$name Inbound"  -Direction Inbound  -Program $exe -Action Allow -Profile Any -Protocol TCP
            New-NetFirewallRule -DisplayName "$name Outbound" -Direction Outbound -Program $exe -Action Allow -Profile Any -Protocol TCP
            """;

            string scriptPath = Path.Combine(Path.GetTempPath(), "setup_firewall.ps1");
            File.WriteAllText(scriptPath, script);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                var p = Process.Start(psi)!;
                p.WaitForExit();
                MarkAsRun(exePath);
            }
            catch { }
            finally
            {
                File.Delete(scriptPath);
            }
        }

        private static bool HasRunBefore(string exePath)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\{GlobalVariables.AppName}");
            if (key is null) return false;
            string? stored = key.GetValue("FirewallConfiguredFor") as string;
            return string.Equals(stored, exePath, StringComparison.OrdinalIgnoreCase);
        }

        private static void MarkAsRun(string exePath)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\{GlobalVariables.AppName}");
            key.SetValue("FirewallConfiguredFor", exePath);
        }
    }
}
