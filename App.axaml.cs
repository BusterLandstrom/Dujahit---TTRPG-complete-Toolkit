using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.ViewModels;
using Dujahit.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;
        public static ProgramManager PM { get; internal set; } = null!;

        // Off, the published version.json is stale. Flip it when that is current.
        public const bool UpdateCheckEnabled = false;
        public const string CurrentVersion = "0.8";

        public static readonly Stopwatch BootClock = Stopwatch.StartNew();
        public static void BootLog(string stage) => ErrorLog.Log($"[boot +{BootClock.ElapsedMilliseconds}ms] {stage}");

        public override void Initialize()
        {
            BootLog("App.Initialize start");
            AvaloniaXamlLoader.Load(this);
            BootLog("App.Initialize xaml loaded");
            PM = new ProgramManager();

            var services = new ServiceCollection();
            services.AddSingleton<ProgramManager>(PM);
            Services = services.BuildServiceProvider();
            BootLog("App.Initialize done");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            BootLog("OnFrameworkInitializationCompleted");
            // NOT async, the window has to exist before this method returns
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                mainWindow.Opened += (_, _) => BootLog("splash visible (MainWindow opened)");
                desktop.MainWindow = mainWindow;

                var loading = new LoadingViewModel("Dujahit");
                var mainVM = new MainWindowViewModel(loading);
                mainWindow.DataContext = mainVM;

                _ = BootstrapAsync(mainWindow, mainVM, loading);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task BootstrapAsync(MainWindow mainWindow, MainWindowViewModel mainVM, LoadingViewModel loading)
        {
            BootLog("bootstrap: init starting");
            using var heartbeat = new CancellationTokenSource();
            _ = HeartbeatAsync(heartbeat.Token);
            try
            {
                void report(string msg, double frac) { BootLog($"stage: {msg}"); loading.Report(msg, frac); }

                await Task.Run(() => PM.InitializeAsync(report));
                BootLog("bootstrap: init done");

                if (PM.DbManager?.UpgradeError is string upgradeError)
                    ErrorLog.ShowDialog("Dujahit could not update your saved data", upgradeError);

                loading.Report("Ready", 1.0);
                await Dispatcher.UIThread.InvokeAsync(mainVM.ShowInitialView);
                BootLog("bootstrap: initial view shown");

                if (UpdateCheckEnabled)
                {
                    var remote = await PM.LoadConfigFromGitHubAsync();
                    if (remote != null && UpdateService.IsNewer(remote.Version, CurrentVersion))
                        UpdateService.ShowPrompt(remote, CurrentVersion);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Bootstrap failed", ex);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    mainWindow.Title = "Dujahit - startup failed";
                    loading.Fail("Could not finish starting. " + ex.Message);
                });
                ErrorLog.ShowDialog("Dujahit failed to start",
                    "Your data could not be read, so startup stopped here rather than carrying on with half of it.\n\n" + ex.Message);
            }
            finally
            {
                heartbeat.Cancel();
            }
        }

        private static async Task HeartbeatAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(1000, ct);
                    BootLog("still loading...");
                }
            }
            catch (TaskCanceledException) { }
        }
    }
}