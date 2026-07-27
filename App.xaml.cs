using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Launcher.Services;
using Launcher.ViewModels;

namespace Launcher
{
    public sealed partial class App : Application
    {
        public static IHost? AppHost { get; private set; }
        private Window? _mainWindow;

        // ── Trace helpers ────────────────────────────────────────────────────────
        // Every write is immediately flushed so the log always reflects the last
        // line actually executed before a hang or crash.
        private static readonly string _traceLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".lrs_launcher", "startup_trace.log");

        private static void Trace(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_traceLogPath)!);
                using var fs = new FileStream(_traceLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs) { AutoFlush = true };
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
            catch { /* never suppress the caller for a log failure */ }
        }

        private static void TraceEx(string stage, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_traceLogPath)!);
                using var fs = new FileStream(_traceLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs) { AutoFlush = true };
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] EXCEPTION in {stage}");
                sw.WriteLine($"  Type   : {ex.GetType().FullName}");
                sw.WriteLine($"  Message: {ex.Message}");
                sw.WriteLine($"  Inner  : {ex.InnerException?.GetType().FullName}: {ex.InnerException?.Message ?? "none"}");
                sw.WriteLine($"  Stack  :");
                sw.WriteLine(ex.StackTrace);
                sw.WriteLine();
            }
            catch { }
        }
        // ────────────────────────────────────────────────────────────────────────

        public App()
        {
            // Wipe the previous trace so the file only contains the current run.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_traceLogPath)!);
                File.WriteAllText(_traceLogPath, $"=== Launcher startup trace {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
            }
            catch { }

            Trace("App() constructor: start");

            this.InitializeComponent();
            Trace("App() constructor: InitializeComponent done");

            try
            {
                Trace("App() constructor: before Host.CreateDefaultBuilder");

                AppHost = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        Trace("ConfigureServices: start");

                        services.AddSingleton<IAuthenticationService, Services.AuthenticationService>();
                        Trace("ConfigureServices: IAuthenticationService registered");

                        services.AddSingleton<IMinecraftService, Services.MinecraftService>();
                        Trace("ConfigureServices: IMinecraftService registered");

                        services.AddSingleton<IMonitoringService, Services.MonitoringService>();
                        Trace("ConfigureServices: IMonitoringService registered");

                        services.AddSingleton<IInstanceStore, Services.InstanceStore>();
                        Trace("ConfigureServices: IInstanceStore registered");

                        services.AddSingleton<ILogService, Services.LogService>();
                        Trace("ConfigureServices: ILogService registered");

                        services.AddSingleton<IAnimationSettingsService, Services.AnimationSettingsService>();
                        Trace("ConfigureServices: IAnimationSettingsService registered");

                        services.AddSingleton<IThemeService, Services.ThemeService>();
                        Trace("ConfigureServices: IThemeService registered");

                        services.AddSingleton<INavigationSettingsService, Services.NavigationSettingsService>();
                        Trace("ConfigureServices: INavigationSettingsService registered");

                        services.AddSingleton<IMarketplaceService, Services.MarketplaceService>();
                        Trace("ConfigureServices: IMarketplaceService registered");

                        services.AddSingleton<IDownloadManager, Services.DownloadManager>();
                        Trace("ConfigureServices: IDownloadManager registered");

                        services.AddSingleton<IModpackInstaller, Services.ModpackInstaller>();
                        Trace("ConfigureServices: IModpackInstaller registered");

                        services.AddSingleton<MainWindow>();
                        Trace("ConfigureServices: MainWindow registered");

                        services.AddSingleton<MainViewModel>();
                        services.AddSingleton<HomeViewModel>();
                        services.AddSingleton<InstancesViewModel>();
                        services.AddSingleton<MarketplaceViewModel>();
                        services.AddSingleton<MonitoringViewModel>();
                        services.AddSingleton<WorldManagerViewModel>();
                        services.AddSingleton<ScreenshotManagerViewModel>();
                        services.AddSingleton<ThemeEditorViewModel>();
                        services.AddSingleton<UpdateCenterViewModel>();
                        services.AddSingleton<DownloadCenterViewModel>();
                        Trace("ConfigureServices: all ViewModels registered");
                    })
                    .ConfigureLogging((context, logging) =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Debug);
                    })
                    .Build();

                Trace("App() constructor: Host.Build() completed");
            }
            catch (Exception ex)
            {
                TraceEx("App() constructor / Host.Build", ex);
                WriteStartupLog("App() constructor / Host.Build", ex);
                throw;
            }

            Trace("App() constructor: end");
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            Trace("OnLaunched: start");

            try
            {
                // ── Stage 1: start the DI host ───────────────────────────────
                if (AppHost != null)
                {
                    Trace("OnLaunched: before AppHost.StartAsync");
                    try
                    {
                        await AppHost.StartAsync();
                        Trace("OnLaunched: after AppHost.StartAsync");
                    }
                    catch (Exception ex)
                    {
                        TraceEx("AppHost.StartAsync", ex);
                        WriteStartupLog("AppHost.StartAsync", ex);
                        throw;
                    }
                }
                else
                {
                    Trace("OnLaunched: AppHost is null — skipping StartAsync");
                }

                // ── Stage 2: load settings that affect first paint ────────────
                if (AppHost != null)
                {
                    Trace("OnLaunched: before AnimationSettingsService.LoadAsync");
                    try
                    {
                        var animService = AppHost.Services.GetRequiredService<IAnimationSettingsService>();
                        Trace("OnLaunched: IAnimationSettingsService resolved");
                        await animService.LoadAsync();
                        Trace("OnLaunched: after AnimationSettingsService.LoadAsync");
                    }
                    catch (Exception ex)
                    {
                        TraceEx("AnimationSettingsService.LoadAsync", ex);
                        WriteStartupLog("AnimationSettingsService.LoadAsync", ex);
                        // Non-fatal: continue with defaults.
                    }

                    Trace("OnLaunched: before ThemeService.LoadAsync");
                    try
                    {
                        var themeService = AppHost.Services.GetRequiredService<IThemeService>();
                        Trace("OnLaunched: IThemeService resolved");
                        await themeService.LoadAsync();
                        Trace("OnLaunched: after ThemeService.LoadAsync");
                    }
                    catch (Exception ex)
                    {
                        TraceEx("ThemeService.LoadAsync", ex);
                        WriteStartupLog("ThemeService.LoadAsync", ex);
                        // Non-fatal: continue with default theme.
                    }

                    Trace("OnLaunched: before NavigationSettingsService.LoadAsync");
                    try
                    {
                        var navService = AppHost.Services.GetRequiredService<INavigationSettingsService>();
                        Trace("OnLaunched: INavigationSettingsService resolved");
                        await navService.LoadAsync();
                        Trace("OnLaunched: after NavigationSettingsService.LoadAsync");
                    }
                    catch (Exception ex)
                    {
                        TraceEx("NavigationSettingsService.LoadAsync", ex);
                        WriteStartupLog("NavigationSettingsService.LoadAsync", ex);
                        // Non-fatal: continue with default navigation.
                    }
                }

                // ── Stage 3: create and show the window ──────────────────────
                Trace("OnLaunched: before MainWindow()");
                try
                {
                    _mainWindow = AppHost?.Services.GetRequiredService<MainWindow>() ?? new MainWindow();
                    Trace("OnLaunched: after MainWindow() — window object created");
                }
                catch (Exception ex)
                {
                    TraceEx("Create MainWindow", ex);
                    WriteStartupLog("Create MainWindow", ex);
                    throw; // Fatal — caught by outer catch below.
                }

                Trace("OnLaunched: before Activate()");
                try
                {
                    _mainWindow.Activate();
                    Trace("OnLaunched: after Activate()");
                }
                catch (Exception ex)
                {
                    TraceEx("MainWindow.Activate", ex);
                    WriteStartupLog("MainWindow.Activate", ex);
                    throw; // Fatal — caught by outer catch below.
                }

                Trace("OnLaunched: completed successfully");
            }
            catch (Exception ex)
            {
                TraceEx("OnLaunched (fatal)", ex);
                WriteStartupLog("OnLaunched (fatal — window did not open)", ex);

                // Without a visible window the WinUI message loop keeps the process alive
                // indefinitely — it appears in Task Manager with ~0% CPU and never exits.
                // Exit() shuts the message loop cleanly so the user sees the process disappear
                // rather than hanging as a zombie. The log file already contains the full
                // exception chain for post-mortem diagnosis.
                // Do NOT rethrow — rethrowing from async void calls Environment.FailFast
                // which produces a different (harder to read) crash report.
                Trace("OnLaunched: calling Application.Current.Exit() to prevent zombie process");
                Application.Current.Exit();
            }
        }

        public static T GetService<T>() where T : class
        {
            if (AppHost == null) throw new InvalidOperationException("App host is not initialized.");
            return AppHost.Services.GetRequiredService<T>();
        }

        private static void WriteStartupLog(string stage, Exception ex)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ".lrs_launcher");
                Directory.CreateDirectory(dir);
                var logPath = Path.Combine(dir, "startup_crash.log");

                var entry =
                    $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===\n" +
                    $"Stage  : {stage}\n" +
                    $"Type   : {ex.GetType().FullName}\n" +
                    $"Message: {ex.Message}\n" +
                    $"Inner  : {ex.InnerException?.GetType().FullName}: {ex.InnerException?.Message ?? "none"}\n" +
                    $"Stack  :\n{ex.StackTrace}\n\n";

                File.AppendAllText(logPath, entry);
            }
            catch { }
        }
    }
}
