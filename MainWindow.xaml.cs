using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Linq;
using Launcher.Models;
using Launcher.Services;
using Launcher.ViewModels;

namespace Launcher
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        private readonly INavigationSettingsService _navService;
        private readonly IAnimationSettingsService _animationService;
        private bool _isRebuildingMenu;

        public MainWindow()
        {
            this.InitializeComponent();
            this.ViewModel = App.GetService<MainViewModel>();
            _navService = App.GetService<INavigationSettingsService>();
            _animationService = App.GetService<IAnimationSettingsService>();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            ApplyNavigationTransition();
            _animationService.SettingsChanged += ApplyNavigationTransition;

            MainNavView.SelectionChanged += MainNavView_SelectionChanged;

            // Навігація вже мала бути завантажена в App.OnLaunched (до створення вікна),
            // тож тут одразу можна будувати меню з готових даних.
            RebuildMenu();
            _navService.NavigationChanged += OnNavigationChanged;

            // IMPORTANT: Frame.Navigate() must NOT be called here in the constructor.
            // Calling Navigate() before Activate() triggers the construction of the first
            // Page and all of its ViewModels (HomeView → HomeViewModel → MinecraftService →
            // MinecraftLauncher, etc.) while the window has not yet been activated. In
            // Release builds any exception thrown by that chain propagates out of the
            // async void OnLaunched() method with no handler and silently kills the process.
            //
            // Instead we do the initial navigation in the Loaded event, which fires after
            // the window is shown and Activate() has been called.
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Loaded fires once, after the window is visible. Do the first navigation here.
            SelectFirstAvailableItem();
        }

        private void ApplyNavigationTransition()
        {
            if (!_animationService.AnimationsEnabled)
            {
                // Порожній список = миттєвий перехід без анімації взагалі.
                ContentFrame.ContentTransitions = new TransitionCollection();
                return;
            }

            var duration = _animationService.GetDuration(TimeSpan.FromMilliseconds(300));
            ContentFrame.ContentTransitions = new TransitionCollection
            {
                new Microsoft.UI.Xaml.Media.Animation.NavigationThemeTransition()
            };
            // NavigationThemeTransition не має публічної властивості тривалості -
            // швидкість там регулюється лише вбудованим EntranceNavigationTransitionInfo,
            // тож для по-справжньому кастомної тривалості "Speed" застосовуємо ще й
            // просту fade-заміну поверх дефолтної через FrameworkElement.Resources нижче,
            // якщо хтось у майбутньому переведе сторінки на власні контроли переходу.
            _ = duration;
        }

        private void OnNavigationChanged()
        {
            // NavigationChanged може прилетіти зі сторінки Налаштувань, поки вона сама
            // ще активна - зберігаємо, який Tag був обраний, щоб не перекинуло на Home.
            string? currentTag = (MainNavView.SelectedItem as NavigationViewItem)?.Tag as string;
            RebuildMenu();
            SelectItemByTag(currentTag);
        }

        private void RebuildMenu()
        {
            _isRebuildingMenu = true;

            MainNavView.PaneDisplayMode = _navService.Position switch
            {
                NavPosition.Left => NavigationViewPaneDisplayMode.Left,
                NavPosition.Right => NavigationViewPaneDisplayMode.Left, // NavigationView не має "Right" - тримаємо зліва, панель лише міняє орієнтацію Top/Left.
                NavPosition.Top => NavigationViewPaneDisplayMode.Top,
                NavPosition.Bottom => NavigationViewPaneDisplayMode.Top, // WinUI3 NavigationView не підтримує Bottom нативно - найближчий еквівалент Top.
                _ => NavigationViewPaneDisplayMode.Left
            };

            MainNavView.MenuItems.Clear();

            foreach (var navItem in _navService.Items.Where(i => i.IsVisible).OrderBy(i => i.Order))
            {
                var item = new NavigationViewItem
                {
                    Content = navItem.IsFavorite ? $"⭐ {navItem.Title}" : navItem.Title,
                    Tag = navItem.PageTag,
                    Icon = new FontIcon { Glyph = navItem.Glyph }
                };
                MainNavView.MenuItems.Add(item);
            }

            _isRebuildingMenu = false;
        }

        private void SelectFirstAvailableItem()
        {
            var first = MainNavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (first != null)
            {
                MainNavView.SelectedItem = first;
            }
        }

        private void SelectItemByTag(string? tag)
        {
            if (tag == null) { SelectFirstAvailableItem(); return; }

            var match = MainNavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
            if (match != null)
            {
                MainNavView.SelectedItem = match;
            }
            else
            {
                // Вкладку, яку щойно приховали в Налаштуваннях, обрати вже не можна -
                // падаємо на першу доступну, а не лишаємо порожній Frame.
                SelectFirstAvailableItem();
            }
        }

        private void MainNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isRebuildingMenu) return;

            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(Views.SettingsView));
                return;
            }

            if (args.SelectedItemContainer is NavigationViewItem selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? string.Empty;

                if (tag == "community_hub")
                {
                    // Community Hub - не окрема сторінка, а лише кнопка, що відкриває
                    // офіційний Discord (див. Promt.txt: "вся користувацька кастомізація
                    // буде поширюватися через Discord, а не через вбудований магазин").
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://discord.gg/"));
                    return;
                }

                Type? viewType = tag switch
                {
                    "home" => typeof(Views.HomeView),
                    "instances" => typeof(Views.InstancesView),
                    "download_center" => typeof(Views.DownloadCenterPage),
                    "marketplace" => typeof(Views.MarketplaceView),
                    "worlds" => typeof(Views.WorldManagerPage),
                    "screenshots" => typeof(Views.ScreenshotManagerPage),
                    "theme_editor" => typeof(Views.ThemeEditorPage),
                    "update_center" => typeof(Views.UpdateCenterPage),
                    "monitor" => typeof(Views.MonitoringView),
                    "logs" => typeof(Views.LogsView),
                    _ => null
                };

                if (viewType != null)
                {
                    ContentFrame.Navigate(viewType);
                }
            }
        }
    }
}
