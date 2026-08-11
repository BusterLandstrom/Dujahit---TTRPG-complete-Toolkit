using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;

namespace Dujahit.Models
{
    public static class UpdateService
    {
        public static bool IsNewer(string? remote, string current)
        {
            if (string.IsNullOrWhiteSpace(remote)) return false;
            if (Version.TryParse(Pad(remote), out var r) && Version.TryParse(Pad(current), out var c))
                return r > c;
            return !string.Equals(remote.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string Pad(string v)
        {
            v = v.Trim();
            return v.Split('.').Length < 2 ? v + ".0" : v;
        }

        public static void ShowPrompt(VersionManager remote, string current)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var fg = new SolidColorBrush(Color.Parse("#ECEAF0"));
                        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12 };
                        panel.Children.Add(new TextBlock
                        {
                            Text = "A new version is available",
                            FontSize = 18,
                            Foreground = new SolidColorBrush(Color.Parse("#FFD700"))
                        });
                        panel.Children.Add(new TextBlock
                        {
                            Text = $"You have {current}. Version {remote.Version} is out" + (remote.IsUrgent ? " (urgent update)." : "."),
                            Foreground = fg,
                            TextWrapping = TextWrapping.Wrap
                        });

                        var win = new Window
                        {
                            Title = "Update available",
                            Width = 460,
                            Height = 220,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Background = new SolidColorBrush(Color.Parse("#0F0F1A")),
                            Content = panel
                        };

                        var row = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            HorizontalAlignment = HorizontalAlignment.Right
                        };
                        var later = new Button { Content = "Later" };
                        later.Click += (_, _) => win.Close();
                        row.Children.Add(later);

                        if (!string.IsNullOrWhiteSpace(remote.InstallPath))
                        {
                            var get = new Button { Content = "Get update" };
                            get.Click += (_, _) =>
                            {
                                try { Process.Start(new ProcessStartInfo { FileName = remote.InstallPath, UseShellExecute = true }); }
                                catch (Exception ex) { ErrorLog.Log("Opening update link failed", ex); }
                                win.Close();
                            };
                            row.Children.Add(get);
                        }
                        panel.Children.Add(row);
                        win.Show();
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
