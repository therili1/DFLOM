using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.Models;
using Launcher.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Launcher.Views
{
    /// <summary>
    /// Один рядок мода/ресурспаку в діалозі налаштувань — на відміну від старого
    /// ModpackBuilder, тут список завжди відфільтрований по конкретному InstanceId,
    /// тож не змішує моди різних профілів між собою.
    /// </summary>
    public partial class InstanceModRow : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isEnabled;
    }

    public sealed partial class InstanceSettingsDialog : ContentDialog
    {
        private const string DisabledSuffix = ".disabled";

        private readonly IInstanceStore _instanceStore;
        private readonly MinecraftInstance _instance;

        private readonly ObservableCollection<InstanceModRow> _mods = new();
        private readonly ObservableCollection<InstanceModRow> _resourcePacks = new();

        public InstanceSettingsDialog(MinecraftInstance instance)
        {
            this.InitializeComponent();
            _instance = instance;
            _instanceStore = App.GetService<IInstanceStore>();

            InstanceHeaderText.Text = $"{_instance.Name} — {_instance.Version} ({_instance.Loader})";
            ModsList.ItemsSource = _mods;
            ResourcePacksList.ItemsSource = _resourcePacks;

            // Безпечно ставити тут, бо _instance/_instanceStore вже присвоєні рядками вище -
            // навіть якщо ця зміна IsOn синхронно тригерне AmdWorkaroundToggle_Toggled
            // (як це буває з деякими контролами всередині InitializeComponent), поля вже готові.
            AmdWorkaroundToggle.IsOn = _instance.DisableForgeEarlyWindow;

            RefreshMods();
            RefreshResourcePacks();
            RefreshIconPreview();
        }

        private async void AmdWorkaroundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            _instance.DisableForgeEarlyWindow = AmdWorkaroundToggle.IsOn;
            await _instanceStore.SaveAsync();
        }

        private void RefreshMods()
        {
            _mods.Clear();
            var modsDir = _instanceStore.GetModsDirectory(_instance);
            if (!Directory.Exists(modsDir)) return;

            foreach (var file in Directory.GetFiles(modsDir))
            {
                bool disabled = file.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                var realExt = Path.GetExtension(disabled ? file[..^DisabledSuffix.Length] : file).ToLowerInvariant();
                if (realExt != ".jar") continue;

                var displayName = Path.GetFileNameWithoutExtension(disabled ? file[..^DisabledSuffix.Length] : file);

                _mods.Add(new InstanceModRow
                {
                    FilePath = file,
                    Name = displayName,
                    FileName = Path.GetFileName(file),
                    IsEnabled = !disabled
                });
            }
        }

        private void RefreshResourcePacks()
        {
            _resourcePacks.Clear();
            var rpDir = Path.Combine(_instanceStore.GetInstanceDirectory(_instance), "resourcepacks");
            if (!Directory.Exists(rpDir)) return;

            foreach (var file in Directory.GetFiles(rpDir))
            {
                if (Path.GetExtension(file).ToLowerInvariant() != ".zip") continue;

                _resourcePacks.Add(new InstanceModRow
                {
                    FilePath = file,
                    Name = Path.GetFileNameWithoutExtension(file),
                    FileName = Path.GetFileName(file),
                    IsEnabled = true
                });
            }
        }

        private void ModToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggle || toggle.Tag is not InstanceModRow row) return;

            try
            {
                bool isCurrentlyDisabled = row.FilePath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                // ToggleSwitch.IsOn вже змінився (це подія Toggled, після зміни стану) —
                // якщо зараз увімкнено, а файл ще позначений як .disabled, знімаємо позначку.
                string newPath = toggle.IsOn
                    ? (isCurrentlyDisabled ? row.FilePath[..^DisabledSuffix.Length] : row.FilePath)
                    : (isCurrentlyDisabled ? row.FilePath : row.FilePath + DisabledSuffix);

                if (newPath != row.FilePath && File.Exists(row.FilePath))
                {
                    File.Move(row.FilePath, newPath, overwrite: true);
                    row.FilePath = newPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося перемкнути мод: {ex.Message}");
            }
        }

        private void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not InstanceModRow row) return;

            try
            {
                if (File.Exists(row.FilePath)) File.Delete(row.FilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося видалити мод: {ex.Message}");
            }

            RefreshMods();
        }

        private void DeleteResourcePack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not InstanceModRow row) return;

            try
            {
                if (File.Exists(row.FilePath)) File.Delete(row.FilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося видалити ресурспак: {ex.Message}");
            }

            RefreshResourcePacks();
        }

        private async void AddMod_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".jar");

            var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                var modsDir = _instanceStore.GetModsDirectory(_instance);
                Directory.CreateDirectory(modsDir);
                var destPath = Path.Combine(modsDir, Path.GetFileName(file.Path));
                File.Copy(file.Path, destPath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося додати мод: {ex.Message}");
            }

            RefreshMods();
        }

        // ==== Іконка профілю: пікер / drag&drop / кроп ====

        private void RefreshIconPreview()
        {
            bool hasIcon = !string.IsNullOrWhiteSpace(_instance.CustomIcon) && File.Exists(_instance.CustomIcon);
            NoIconPlaceholder.Visibility = hasIcon ? Visibility.Collapsed : Visibility.Visible;
            IconPreview.Visibility = hasIcon ? Visibility.Visible : Visibility.Collapsed;
            if (hasIcon)
            {
                // Новий BitmapImage з file:// URI, а не просто File.ReadAllBytes у пам'ять —
                // так превью одразу підхоплює зміни на диску без кешування старого файлу під тим же ім'ям.
                IconPreview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_instance.CustomIcon + "?t=" + DateTime.Now.Ticks));
            }
        }

        private async void ChangeIcon_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");

            var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await CropAndApplyIconAsync(file.Path);
        }

        private void IconDropZone_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Встановити як іконку";
        }

        private async void IconDropZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;

            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count == 0) return;

            var path = items[0].Path;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            {
                System.Diagnostics.Debug.WriteLine($"Перетягнутий файл не є зображенням: {path}");
                return;
            }

            await CropAndApplyIconAsync(path);
        }

        /// <summary>Спільний пайплайн для пікера й drag&drop: відкриває кроп-діалог над обраним
        /// файлом, і якщо юзер підтвердив - зберігає результат у _instance.CustomIcon.</summary>
        private async System.Threading.Tasks.Task CropAndApplyIconAsync(string sourcePath)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var iconsDir = Path.Combine(appData, ".lrs_launcher", "icons");
            Directory.CreateDirectory(iconsDir);
            var destPath = Path.Combine(iconsDir, $"{_instance.Id}.png");

            var cropDialog = new IconCropDialog(sourcePath, destPath) { XamlRoot = this.XamlRoot };
            var result = await cropDialog.ShowAsync();

            if (result == ContentDialogResult.Primary && cropDialog.ResultPath != null)
            {
                _instance.CustomIcon = cropDialog.ResultPath;
                await _instanceStore.SaveAsync();
                RefreshIconPreview();
            }
        }

        private async void ResetIcon_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_instance.CustomIcon) && File.Exists(_instance.CustomIcon))
            {
                try { File.Delete(_instance.CustomIcon); } catch { /* не критично, якщо не вдалось прибрати старий файл */ }
            }
            _instance.CustomIcon = string.Empty;
            await _instanceStore.SaveAsync();
            RefreshIconPreview();
        }
    }
}
