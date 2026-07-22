using System;
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

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            if (AppHost != null)
            {
                await AppHost.StartAsync();

                // Застосовуємо збережену тему ДО створення MainWindow, інакше сторінки
                // встигають один раз намалюватись дефолтними кольорами, а тоді миттєво
                // перефарбуватись - видимий "спалах" при кожному запуску.
                var animationService = AppHost.Services.GetRequiredService<IAnimationSettingsService>();
                await animationService.LoadAsync();

                var themeService = AppHost.Services.GetRequiredService<IThemeService>();
                await themeService.LoadAsync();

                var navService = AppHost.Services.GetRequiredService<INavigationSettingsService>();
                await navService.LoadAsync();
            }

            _mainWindow = AppHost?.Services.GetRequiredService<MainWindow>() ?? new MainWindow();
            _mainWindow.Activate();
        }

        public static T GetService<T>() where T : class
        {
            if (AppHost == null) throw new InvalidOperationException("App host is not initialized.");
            return AppHost.Services.GetRequiredService<T>();
        }
    }
}