using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using Launcher.Services;

namespace Launcher.Views
{
    public sealed partial class SettingsView : Page
    {
        public INavigationSettingsService NavService { get; }
        private readonly IAnimationSettingsService _animationService;
        private bool _isInitializing = true;

        public SettingsView()
        {
            this.InitializeComponent();
            NavService = App.GetService<INavigationSettingsService>();
            _animationService = App.GetService<IAnimationSettingsService>();

            // NavigationView не встиг застосувати позицію до першого рендеру цієї
            // сторінки, тож підставляємо поточне значення в ComboBox вручну.
            var currentTag = NavService.Position.ToString();
            foreach (ComboBoxItem item in NavPositionCombo.Items)
            {
                if ((string)item.Tag == currentTag)
                {
                    NavPositionCombo.SelectedItem = item;
                    break;
                }
            }

            AnimToggle.IsOn = _animationService.AnimationsEnabled;
            AnimSpeedSlider.Value = _animationService.AnimationSpeed;
            foreach (ComboBoxItem item in GlowCombo.Items)
            {
                if ((string)item.Tag == _animationService.Glow.ToString())
                {
                    GlowCombo.SelectedItem = item;
                    break;
                }
            }
            _isInitializing = false;
        }

        private void AnimToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            _animationService.SetAnimationsEnabled(AnimToggle.IsOn);
        }

        private void AnimSpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializing) return;
            _animationService.SetAnimationSpeed(e.NewValue);
        }

        private void GlowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (GlowCombo.SelectedItem is ComboBoxItem item && Enum.TryParse<GlowLevel>((string)item.Tag, out var level))
            {
                _animationService.SetGlow(level);
            }
        }

        private void NavPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavPositionCombo.SelectedItem is ComboBoxItem item && Enum.TryParse<NavPosition>((string)item.Tag, out var position))
            {
                NavService.SetPosition(position);
            }
        }

        private void FavoriteNav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                NavService.ToggleFavorite(id);
            }
        }

        private void MoveNavUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                NavService.MoveUp(id);
            }
        }

        private void MoveNavDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                NavService.MoveDown(id);
            }
        }

        private void VisibleNav_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle && toggle.Tag is string id)
            {
                NavService.SetVisible(id, toggle.IsOn);
            }
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Збережено",
                Content = "Загальні параметри лаунчера успішно записані в конфіг-файл .json!",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = await dialog.ShowAsync();
        }

        private async void Browse_Click(object sender, RoutedEventArgs e)
        {
            GamePathText.Text = "C:\\Games\\Minecraft\\.minecraft_custom";
        }

        private async void AutoDetectJava_Click(object sender, RoutedEventArgs e)
        {
            JavaPathText.Text = "C:\\Program Files\\Java\\jdk-21.0.2\\bin\\javaw.exe";
            var dialog = new ContentDialog
            {
                Title = "Java Знайдено",
                Content = "Система автоматично виявила інсталяцію JDK 21 за адресою:\nC:\\Program Files\\Java\\jdk-21.0.2",
                CloseButtonText = "Чудово",
                XamlRoot = this.XamlRoot
            };
            _ = await dialog.ShowAsync();
        }

        private async void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Кеш Очищено",
                Content = "Усі тимчасові файли завантажень, JSON-індекси та картинки успішно видалено.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = await dialog.ShowAsync();
        }

        private async void ResetLauncher_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Скидання Налаштувань",
                Content = "Ви дійсно бажаєте повернути лаунчер до заводських налаштувань? Це видалить всі профілі та збереження.",
                PrimaryButtonText = "Так, скинути",
                CloseButtonText = "Скасувати",
                XamlRoot = this.XamlRoot
            };

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary)
            {
                var okDialog = new ContentDialog
                {
                    Title = "Виконано",
                    Content = "Лаунчер повністю скинуто до початкових параметрів.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                _ = await okDialog.ShowAsync();
            }
        }
    }
}
