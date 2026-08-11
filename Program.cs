using Avalonia;
using Avalonia.ReactiveUI;
using Dujahit.Models;
using System;

namespace Dujahit
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            ErrorLog.Initialize();
            App.BootLog("Program.Main, building Avalonia");
            try
            {
                BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Fatal at startup", ex);
                throw;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI();
    }
}
