using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Dujahit.Models
{
    public static class ErrorLog
    {
        private static readonly object _gate = new();
        private static bool _hooked;

        public static string LogDirectory => Path.Combine(GlobalVariables.AppDataLocal, "logs");

        public static void Initialize()
        {
            if (_hooked) return;
            _hooked = true;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Write("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex => Write("ReactiveCommand", ex));
        }

        public static void Log(string context, Exception? ex) => Write(context, ex);
        public static void Log(string message) => Write(message, null);

        private static void Write(string context, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var file = Path.Combine(LogDirectory, GlobalVariables.AppName.ToLower() + "-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + context + (ex != null ? ": " + ex : "") + Environment.NewLine;
                lock (_gate) File.AppendAllText(file, line);
            }
            catch { }
            Debug.WriteLine($"[ERR] {context}: {ex?.Message}");
        }

        public static void ShowDialog(string title, string message)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var body = new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#ECEAF0"))
                        };
                        var hint = new TextBlock
                        {
                            Text = "A full trace was written to " + LogDirectory,
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 12,
                            Opacity = 0.6,
                            Margin = new Avalonia.Thickness(0, 12, 0, 0),
                            Foreground = new SolidColorBrush(Color.Parse("#ECEAF0"))
                        };
                        var win = new Window
                        {
                            Title = title,
                            Width = 520,
                            Height = 260,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Background = new SolidColorBrush(Color.Parse("#0F0F1A")),
                            Content = new StackPanel
                            {
                                Margin = new Avalonia.Thickness(24),
                                Children = { body, hint }
                            }
                        };
                        win.Show();
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
