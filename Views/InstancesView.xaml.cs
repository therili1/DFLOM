using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.ViewModels;
using Launcher.Models;
using System;

namespace Launcher.Views
{
    public sealed partial class InstancesView : Page
    {
        public InstancesViewModel ViewModel { get; }

        private ItemsWrapGrid? _instancesWrapGrid;

        public InstancesView()
        {
            // ВАЖЛИВО: так само, як у DownloadCenterPage - ComboBoxItem з IsSelected="True"
            // (тут їх два: SortCombo і CardSizeCombo) піднімає SelectionChanged ще під час
            // InitializeComponent(), тож ViewModel мусить існувати ДО цього виклику.
            this.ViewModel = App.GetService<InstancesViewModel>();
            this.InitializeComponent();

            // x:Name усередині ItemsPanelTemplate не генерує поле в code-behind (це окремий
            // шаблонний контекст, XAML-компілятор його туди не пробрасує) — тому знаходимо
            // реальну ItemsWrapGrid у візуальному дереві після того, як GridView побудує панель.
            InstancesGrid.Loaded += (_, __) => EnsureWrapGridResolved();

            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.CardSize))
                {
                    ApplyCardSize(ViewModel.CardSize);
                }
            };
        }

        private void EnsureWrapGridResolved()
        {
            if (_instancesWrapGrid != null) return;

            _instancesWrapGrid = FindDescendant<ItemsWrapGrid>(InstancesGrid);
            if (_instancesWrapGrid != null)
            {
                ApplyCardSize(ViewModel.CardSize);
            }
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;

                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void ApplyCardSize(string cardSize)
        {
            if (_instancesWrapGrid == null)
            {
                // Панель ще не побудована (GridView не встиг Loaded) — застосуємо, коли з'явиться.
                EnsureWrapGridResolved();
                return;
            }

            var (width, height) = cardSize switch
            {
                "Small" => (140.0, 160.0),
                "Large" => (240.0, 280.0),
                _ => (180.0, 210.0) // Medium
            };

            _instancesWrapGrid.ItemWidth = width;
            _instancesWrapGrid.ItemHeight = height;
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null) return;
            if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.SortMode = tag;
            }
        }

        private void CardSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null) return;
            if (CardSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.CardSize = tag;
            }
        }

        private void GridViewToggle_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsGridView = true;
            GridViewToggle.IsChecked = true;
            ListViewToggle.IsChecked = false;
        }

        private void ListViewToggle_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsGridView = false;
            GridViewToggle.IsChecked = false;
            ListViewToggle.IsChecked = true;
        }

        private async void CardLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MinecraftInstance instance) return;
            ViewModel.SelectedInstance = instance;
            await ViewModel.LaunchInstanceAsync(instance);
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.SaveSettingsAsync();

            var dialog = new ContentDialog
            {
                Title = "Налаштування збережено",
                Content = "Конфігурацію профілю успішно перезаписано на диск!",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = await dialog.ShowAsync();
        }

        private async void InstanceOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MinecraftInstance instance) return;

            var dialog = new InstanceSettingsDialog(instance)
            {
                XamlRoot = this.XamlRoot
            };
            _ = await dialog.ShowAsync();
        }

        private async void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedInstance != null)
            {
                var dialog = new ContentDialog
                {
                    Title = "Видалення профілю",
                    Content = $"Ви впевнені, що хочете остаточно видалити профіль '{ViewModel.SelectedInstance.Name}'? Це призведе до стирання всіх ігрових директорій профілю.",
                    PrimaryButtonText = "Так, видалити",
                    CloseButtonText = "Скасувати",
                    XamlRoot = this.XamlRoot
                };

                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    await ViewModel.DeleteInstanceAsync(ViewModel.SelectedInstance.Id);
                }
            }
        }

        private async void ShowCreateDialog_Click(object sender, RoutedEventArgs e)
        {
            // Build the UI elements for the creation dialog
            var txtName = new TextBox { Header = "Назва профілю", PlaceholderText = "Мій Кастомний Профіль" };

            var toggleSnapshots = new ToggleSwitch { Header = "Показувати тестові збірки (snapshots/beta/alpha)", IsOn = ViewModel.ShowSnapshots };

            var cbFolder = new ComboBox { Header = "Лінія версій (папка)", HorizontalAlignment = HorizontalAlignment.Stretch };
            var cbVersion = new ComboBox { Header = "Версія Minecraft", HorizontalAlignment = HorizontalAlignment.Stretch };

            void RebuildFolders()
            {
                cbFolder.Items.Clear();
                foreach (var g in ViewModel.AvailableVersionGroups) cbFolder.Items.Add(g);
                if (cbFolder.Items.Count > 0) cbFolder.SelectedIndex = 0;
            }

            cbFolder.SelectionChanged += (_, __) =>
            {
                cbVersion.Items.Clear();
                if (cbFolder.SelectedItem is Launcher.Models.VersionGroup group)
                {
                    foreach (var v in group.Versions) cbVersion.Items.Add(v);
                    if (cbVersion.Items.Count > 0) cbVersion.SelectedIndex = 0;
                }
            };

            toggleSnapshots.Toggled += (_, __) =>
            {
                ViewModel.ShowSnapshots = toggleSnapshots.IsOn;
                RebuildFolders();
            };

            RebuildFolders();

            var cbLoader = new ComboBox { Header = "Тип завантажувача", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var l in ViewModel.AvailableLoaders) cbLoader.Items.Add(l);
            cbLoader.SelectedIndex = 0;

            var sliderRam = new Slider { Header = "Виділена пам'ять (MB)", Minimum = 2048, Maximum = 16384, StepFrequency = 512, Value = 4096 };

            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(txtName);
            stack.Children.Add(toggleSnapshots);
            stack.Children.Add(cbFolder);
            stack.Children.Add(cbVersion);
            stack.Children.Add(cbLoader);
            stack.Children.Add(sliderRam);

            var dialog = new ContentDialog
            {
                Title = "Створити Новий Профіль",
                Content = new ScrollViewer { Content = stack, MaxHeight = 480 },
                PrimaryButtonText = "Створити",
                CloseButtonText = "Скасувати",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(txtName.Text))
            {
                ViewModel.NewProfileName = txtName.Text;
                ViewModel.NewProfileVersion = cbVersion.SelectedItem?.ToString() ?? "1.21.1";
                ViewModel.NewProfileLoader = cbLoader.SelectedItem?.ToString() ?? "Vanilla";
                ViewModel.NewProfileRam = (int)sliderRam.Value;

                await ViewModel.CreateInstanceAsync();
            }
        }
    }
}
