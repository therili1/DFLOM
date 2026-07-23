using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.ViewModels
{
    public partial class InstancesViewModel : ObservableObject
    {
        private readonly IMinecraftService _minecraftService;
        private readonly ILogService _log;
        private readonly IInstanceStore _instanceStore;

        public ObservableCollection<MinecraftInstance> Instances => _instanceStore.Instances;

        [ObservableProperty]
        private MinecraftInstance? _selectedInstance;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private double _installProgress;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _newProfileName = string.Empty;

        [ObservableProperty]
        private string _newProfileVersion = "1.21.1";

        [ObservableProperty]
        private string _newProfileLoader = "Vanilla";

        [ObservableProperty]
        private int _newProfileRam = 4096;

        [ObservableProperty]
        private bool _showSnapshots;

        // --- Grid View (сторінка "Збірки") ---
        [ObservableProperty]
        private bool _isGridView = true;

        [ObservableProperty]
        private string _cardSize = "Medium"; // Small / Medium / Large

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _sortMode = "Name"; // Name / LastLaunched / Version

        public ObservableCollection<MinecraftInstance> FilteredInstances { get; } = new();

        public ObservableCollection<string> AvailableVersions { get; } = new();
        public ObservableCollection<VersionGroup> AvailableVersionGroups { get; } = new();
        public ObservableCollection<string> AvailableLoaders { get; } = new() { "Vanilla", "Fabric", "Forge", "NeoForge", "Quilt" };

        private List<MinecraftVersion> _allVersions = new();

        public InstancesViewModel()
        {
            _minecraftService = App.GetService<IMinecraftService>();
            _instanceStore = App.GetService<IInstanceStore>();
            _log = App.GetService<ILogService>();

            SelectedInstance = Instances.FirstOrDefault();
            Instances.CollectionChanged += (_, _) => RebuildFiltered();
            RebuildFiltered();

            _ = LoadAvailableVersionsAsync();
        }

        partial void OnSearchTextChanged(string value) => RebuildFiltered();
        partial void OnSortModeChanged(string value) => RebuildFiltered();

        /// <summary>Перебудовує FilteredInstances - те, до чого прив'язана Grid/List View -
        /// із урахуванням пошуку (по назві/версії/лоадеру) і сортування. Викликається щоразу,
        /// як міняється сам список профілів, пошуковий рядок чи режим сортування.</summary>
        private void RebuildFiltered()
        {
            IEnumerable<MinecraftInstance> query = Instances;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var needle = SearchText.Trim();
                query = query.Where(i =>
                    i.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    i.Version.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    i.Loader.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            query = SortMode switch
            {
                "LastLaunched" => query.OrderByDescending(i => i.LastLaunched ?? DateTime.MinValue),
                "Version" => query.OrderByDescending(i => i.Version),
                _ => query.OrderBy(i => i.Name)
            };

            var result = query.ToList();
            FilteredInstances.Clear();
            foreach (var inst in result) FilteredInstances.Add(inst);
        }

        /// <summary>Кількість модів для картки - делегує в InstanceStore (рахує .jar на диску).</summary>
        public int GetModCount(MinecraftInstance instance) => _instanceStore.GetModCount(instance);

        /// <summary>Кількість світів для картки - делегує в InstanceStore (рахує підтеки в saves).</summary>
        public int GetWorldCount(MinecraftInstance instance) => _instanceStore.GetWorldCount(instance);

        /// <summary>Іконка за замовчуванням, коли CustomIcon не вибрано - за лоадером,
        /// як просить список кастомізації ("Vanilla/Fabric/Forge/NeoForge/Quilt").</summary>
        public string GetLoaderIcon(string loader) => loader switch
        {
            "Fabric" => "🧵",
            "Forge" => "🔨",
            "NeoForge" => "🔥",
            "Quilt" => "🧶",
            _ => "📦"
        };

        partial void OnShowSnapshotsChanged(bool value) => RebuildVersionCollections();

        private async Task LoadAvailableVersionsAsync()
        {
            try
            {
                _allVersions = (await _minecraftService.GetVersionsAsync()).ToList();
                RebuildVersionCollections();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Не вдалося завантажити список версій: {ex.Message}";
            }
        }

        /// <summary>Перебудовує AvailableVersions (плаский список) і AvailableVersionGroups (по "папках")
        /// з урахуванням ShowSnapshots. Викликається при завантаженні і при перемиканні тумблера.</summary>
        private void RebuildVersionCollections()
        {
            var filtered = ShowSnapshots
                ? _allVersions
                : _allVersions.Where(v => v.Type == "release");

            var versionList = filtered.Select(v => v.Id).ToList();

            AvailableVersions.Clear();
            foreach (var id in versionList) AvailableVersions.Add(id);

            // Групуємо release-версії по "папках" мажорної лінії (1.21.x, 1.20.x, ...),
            // а всі не-релізи (снапшоти/бети/альфи) - в окрему папку "Тестові збірки",
            // щоб не змішувати їх з релізними лініями.
            var groups = new List<VersionGroup>();
            var releaseLines = filtered
                .Where(v => v.Type == "release")
                .GroupBy(v => GetReleaseLine(v.Id))
                .OrderByDescending(g => g.Key);

            foreach (var line in releaseLines)
            {
                groups.Add(new VersionGroup { FolderName = $"{line.Key}.x", Versions = line.Select(v => v.Id).ToList() });
            }

            if (ShowSnapshots)
            {
                var testBuilds = filtered.Where(v => v.Type != "release").Select(v => v.Id).ToList();
                if (testBuilds.Count > 0)
                {
                    groups.Insert(0, new VersionGroup { FolderName = "🧪 Тестові збірки (snapshots/beta/alpha)", Versions = testBuilds });
                }
            }

            AvailableVersionGroups.Clear();
            foreach (var g in groups) AvailableVersionGroups.Add(g);

            if (AvailableVersions.Count > 0 && !AvailableVersions.Contains(NewProfileVersion))
            {
                NewProfileVersion = AvailableVersions.First();
            }
        }

        /// <summary>"1.21.4" -> "1.21", "1.20.1" -> "1.20" - лінія релізу для групування у папку.</summary>
        private static string GetReleaseLine(string versionId)
        {
            var parts = versionId.Split('.');
            return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : versionId;
        }

        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            // Name/AllocatedRAM/JvmArguments/Notes уже змінені в пам'яті напряму через
            // x:Bind TwoWay (той самий об'єкт, що лежить у _instanceStore.Instances) —
            // тут лишається тільки фактично записати колекцію на диск у instances.json.
            await _instanceStore.SaveAsync();
        }

        [RelayCommand]
        public async Task LoadInstancesAsync()
        {
            IsLoading = true;
            await _instanceStore.LoadAsync();
            SelectedInstance ??= Instances.FirstOrDefault();
            IsLoading = false;
        }

        [RelayCommand]
        public async Task CreateInstanceAsync()
        {
            if (string.IsNullOrWhiteSpace(NewProfileName)) return;

            var newInst = new MinecraftInstance
            {
                Id = Guid.NewGuid().ToString(),
                Name = NewProfileName,
                Version = NewProfileVersion,
                Loader = NewProfileLoader,
                AllocatedRAM = NewProfileRam,
                // Порожній LoaderVersion - MinecraftService.InstallInstanceAsync сприймає це
                // як "автоматично взяти найновішу версію лоадера" через CmlLib.
                // Раніше тут стояв буквальний текст "Latest", який передавався в
                // FabricInstaller/QuiltInstaller як РЕАЛЬНА версія лоадера і завжди падав,
                // бо такої версії не існує - через це працювали лише захардкоджені профілі.
                LoaderVersion = string.Empty
            };

            _instanceStore.AddInstance(newInst);
            SelectedInstance = newInst;

            NewProfileName = string.Empty;
            NewProfileVersion = "1.21.1";
            NewProfileLoader = "Vanilla";
            NewProfileRam = 4096;

            await Task.CompletedTask;
        }

        [RelayCommand]
        public async Task DeleteInstanceAsync(string id)
        {
            var inst = Instances.FirstOrDefault(i => i.Id == id);
            if (inst == null) return;

            try
            {
                var dir = _instanceStore.GetInstanceDirectory(inst);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Не вдалося видалити теку інстансу: {ex.Message}";
            }

            _instanceStore.RemoveInstance(id);

            if (SelectedInstance?.Id == id)
            {
                SelectedInstance = Instances.FirstOrDefault();
            }

            await Task.CompletedTask;
        }

        [RelayCommand]
        public async Task InstallInstanceAsync(MinecraftInstance instance)
        {
            IsLoading = true;
            InstallProgress = 0;
            StatusMessage = $"Встановлення {instance.Version}...";

            try
            {
                var progress = new Progress<double>(p => InstallProgress = p);
                await _minecraftService.InstallInstanceAsync(instance, progress);
                StatusMessage = "Встановлення завершено.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка встановлення: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task LaunchInstanceAsync(MinecraftInstance instance)
        {
            try
            {
                // ВАЖЛИВО: раніше цей метод одразу викликав LaunchInstanceAsync, минаючи
                // встановлення - тобто кнопка "Грати" на картці Grid View ніколи нічого не
                // качала. Якщо версія ще не стояла (або LaunchVersionId був застарілий),
                // запуск просто мовчки нічого не робив. HomeViewModel.LaunchGameAsync цей
                // крок робив правильно - тепер обидва шляхи однакові.
                StatusMessage = $"Перевірка файлів {instance.Name}...";
                var progress = new Progress<double>(percent => StatusMessage = $"Встановлення {instance.Name}: {Math.Round(percent, 1)}%");
                await _minecraftService.InstallInstanceAsync(instance, progress);

                StatusMessage = $"Запуск {instance.Name}...";
                await _minecraftService.LaunchInstanceAsync(instance);

                instance.LastLaunched = DateTime.Now;
                await _instanceStore.SaveAsync();

                StatusMessage = "Гру запущено.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка запуску: {ex.Message}";
                _log.Error("InstancesViewModel", $"Запуск '{instance.Name}' провалився: {ex}");
            }
        }
    }
}
