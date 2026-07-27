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

        public App()
        {
            this.InitializeComponent();

            try
            {
                AppHost = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        // Реєстрація наших нових РЕАЛЬНИХ сервісів
                        services.AddSingleton<IAuthenticationService, Services.AuthenticationService>();
                        services.AddSingleton<IMinecraftService, Services.MinecraftService>();
                        services.AddSingleton<IMonitoringService, Services.MonitoringService>();
                        services.AddSingleton<IInstanceStore, Services.InstanceStore>();
                        services.AddSingleton<ILogService, Services.LogService>();
                        services.AddSingleton<IAnimationSettingsService, Services.AnimationSettingsService>();
                        services.AddSingleton<IThemeService, Services.ThemeService>();
                        services.AddSingleton<INavigationSettingsService, Services.NavigationSettingsService>();

                        services.AddSingleton<IMarketplaceService, Services.MarketplaceService>();
                        services.AddSingleton<IDownloadManager, Services.DownloadManager>();
                        services.AddSingleton<IModpackInstaller, Services.ModpackInstaller>();

                        // Реєстрація MainWindow (потрібна для запуску інтерфейсу)
                        services.AddSingleton<MainWindow>();

                        // MVVM ViewModels registration
                        // ВАЖЛИВО: Singleton, а не Transient. ContentFrame.Navigate() у MainWindow
                        // створює нову сторінку (Page) при кожному перемиканні вкладки, а кожна
                        // сторінка сама запитує свою ViewModel через App.GetService<T>() у конструкторі.
                        // З Transient це означало нову "порожню" ViewModel щоразу -> увесь стан
                        // (вибраний профіль, незбережені поля форми, прогрес завантаження) губився
                        // при кожному переключенні вкладок. Singleton гарантує, що це той самий
                        // об'єкт ViewModel, тож стан живе, поки не закриють застосунок.
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
                    })
                    .ConfigureLogging((context, logging) =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Debug);
                    })
                    .Build();
            }
            catch (Exception ex)
            {
                WriteStartupLog("App() constructor / Host.Build", ex);
                throw; // Let WinUI propagate — at least the log file will exist.
            }
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            // IMPORTANT: This method is async void. In Release any unhandled exception
            // silently terminates the process — no dialog, no crash report, nothing.
            // Every stage must be wrapped so an exception in one stage cannot kill the
            // whole process before the window even appears.
            try
            {
                // ── Stage 1: start the DI host ───────────────────────────────────────
                if (AppHost != null)
                {
                    try
                    {
                        await AppHost.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        WriteStartupLog("AppHost.StartAsync", ex);
                        throw;
                    }
                }

                // ── Stage 2: pre-load settings that affect first paint ────────────────
                // Animation, Theme, and Navigation settings are loaded BEFORE the window
                // is created so that:
                //  - NavigationSettingsService.Items is populated before MainWindow
                //    calls RebuildMenu() in its constructor (empty Items → empty menu).
                //  - ThemeService resources are applied before any Page renders,
                //    preventing a visible flash of default colours at startup.
                // These three LoadAsync calls are very fast (local JSON files) and safe
                // to run before Activate(). Each has its own catch so one failure does
                // not block the window from appearing.
                if (AppHost != null)
                {
                    try
                    {
                        var animService = AppHost.Services.GetRequiredService<IAnimationSettingsService>();
                        await animService.LoadAsync();
                    }
                    catch (Exception ex)
                    {
                        WriteStartupLog("AnimationSettingsService.LoadAsync", ex);
                        // Non-fatal: launcher uses defaults.
                    }

                    try
                    {
                        var themeService = AppHost.Services.GetRequiredService<IThemeService>();
                        await themeService.LoadAsync();
                    }
                    catch (Exception ex)
                    {
                        WriteStartupLog("ThemeService.LoadAsync", ex);
                        // Non-fatal: launcher uses default theme.
                    }

                    try
                    {
                        var navService = AppHost.Services.GetRequiredService<INavigationSettingsService>();
                        await navService.LoadAsync();
                    }
                    catch (Exception ex)
                    {
                        WriteStartupLog("NavigationSettingsService.LoadAsync", ex);
                        // Non-fatal: launcher uses default navigation layout.
                    }
                }

                // ── Stage 3: create and show the window ──────────────────────────────
                try
                {
                    _mainWindow = AppHost?.Services.GetRequiredService<MainWindow>() ?? new MainWindow();
                }
                catch (Exception ex)
                {
                    WriteStartupLog("Create MainWindow", ex);
                    throw; // Fatal: cannot proceed without a window.
                }

                try
                {
                    _mainWindow.Activate();
                }
                catch (Exception ex)
                {
                    WriteStartupLog("MainWindow.Activate", ex);
                    throw; // Fatal: window must be visible.
                }

                // Window is now visible. Heavy service initialisation (Minecraft path
                // setup, instance loading, etc.) happens lazily when the user navigates
                // to each page, not here. Nothing else needs to run at startup.
            }
            catch (Exception ex)
            {
                // Last-resort catch: we reach here only if a *fatal* stage threw.
                // The log file at %AppData%\.lrs_launcher\startup_crash.log will contain
                // the full exception chain so the developer can diagnose the issue.
                WriteStartupLog("OnLaunched (fatal — window did not open)", ex);

                // Nothing else we can do: the window never appeared. The log is the
                // only artifact. Do NOT rethrow — rethrowing from async void calls
                // Environment.FailFast which produces a different (harder to find) error.
            }
        }

        public static T GetService<T>() where T : class
        {
            if (AppHost == null) throw new InvalidOperationException("App host is not initialized.");
            return AppHost.Services.GetRequiredService<T>();
        }

        /// <summary>
        /// Appends a structured entry to %AppData%\.lrs_launcher\startup_crash.log.
        /// Called whenever a startup stage throws so the developer can see the exact
        /// file/line/inner exception without attaching a debugger.
        /// </summary>
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
            catch
            {
                // If we cannot write the log there is nothing else we can do.
            }
        }
    }
}
