using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.ViewModels
{
    /// <summary>
    /// Група версій для показу "по папках" (напр. "1.21.x", "2025", "Пререлізи") —
    /// саме для панелі фільтрів Marketplace. Названо окремо від Models.VersionGroup
    /// (те, що використовує InstancesView для вибору версії при створенні профілю),
    /// щоб уникнути конфлікту імен між двома різними моделями групування версій.
    /// </summary>
    public partial class MarketplaceVersionGroup : ObservableObject
    {
        public string GroupName { get; }
        public ObservableCollection<FilterOption> Versions { get; }

        public MarketplaceVersionGroup(string groupName, IEnumerable<FilterOption> versions)
        {
            GroupName = groupName;
            Versions = new ObservableCollection<FilterOption>(versions);
        }
    }

    /// <summary>
    /// Один пункт-чекбокс у панелі фільтрів (категорія або версія гри).
    /// </summary>
    public partial class FilterOption : ObservableObject
    {
        public string Value { get; }
        public string Label { get; }

        [ObservableProperty]
        private bool _isSelected;

        public FilterOption(string value, string? label = null)
        {
            Value = value;
            Label = label ?? value;
        }
    }

    /// <summary>
    /// Одна версія в списку деталей проєкту — з готовим для показу текстом
    /// на кшталт "14.0.0-beta.2 for 1.21 — [beta]", як на самому Modrinth.
    /// </summary>
    public partial class VersionListItem : ObservableObject
    {
        public MarketplaceVersion Version { get; }
        public string DisplayText { get; }

        public VersionListItem(MarketplaceVersion version)
        {
            Version = version;
            var mainGameVersion = version.GameVersions.FirstOrDefault() ?? "";
            DisplayText = $"{version.VersionNumber} for {mainGameVersion} — [{version.VersionType}]";
        }
    }

    public partial class MarketplaceViewModel : ObservableObject
    {
        private readonly IMarketplaceService _marketplaceService;
        private readonly IMinecraftService _minecraftService;
        private readonly IDownloadManager _downloadManager;
        private readonly IInstanceStore _instanceStore;
        private readonly IModpackInstaller _modpackInstaller;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private CancellationTokenSource? _searchCts;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedType = "mod"; // mod, resourcepack, shader

        [ObservableProperty]
        private string _selectedLoader = "all"; // fabric, forge, quilt, neoforge

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private MinecraftInstance? _selectedTargetInstance;

        [ObservableProperty]
        private ObservableCollection<MarketplaceProjectHeader> _projects = new();

        [ObservableProperty]
        private ObservableCollection<DownloadTask> _downloadQueue = new();

        // ==== Панель деталей вибраного проєкту ====

        [ObservableProperty]
        private MarketplaceProjectHeader? _selectedProject;

        [ObservableProperty]
        private MarketplaceProject? _projectDetail;

        [ObservableProperty]
        private ObservableCollection<VersionListItem> _projectVersions = new();

        [ObservableProperty]
        private string _versionSearchQuery = string.Empty;

        // Повний нефільтрований список версій обраного проєкту — з нього
        // будується ProjectVersions при пошуку/фільтрації.
        private List<VersionListItem> _allProjectVersions = new();

        [ObservableProperty]
        private VersionListItem? _selectedVersion;

        [ObservableProperty]
        private bool _isDetailLoading;

        [ObservableProperty]
        private string _detailStatusMessage = string.Empty;

        public ObservableCollection<MinecraftInstance> Instances => _instanceStore.Instances;
        public ObservableCollection<string> Loaders { get; } = new() { "all", "fabric", "forge", "quilt", "neoforge" };

        // Категорії Modrinth (найпоширеніший набір — той самий, що показує сайт).
        public ObservableCollection<FilterOption> Categories { get; } = new()
        {
            new("adventure", "Adventure"),
            new("challenging", "Challenging"),
            new("combat", "Combat"),
            new("decoration", "Decoration"),
            new("economy", "Economy"),
            new("equipment", "Equipment"),
            new("food", "Food"),
            new("game-mechanics", "Game Mechanics"),
            new("library", "Library"),
            new("magic", "Magic"),
            new("management", "Management"),
            new("minigame", "Minigame"),
            new("mobs", "Mobs"),
            new("multiplayer", "Multiplayer"),
            new("optimization", "Optimization"),
            new("quests", "Quests"),
            new("social", "Social"),
            new("storage", "Storage"),
            new("technology", "Technology"),
            new("transportation", "Transportation"),
            new("utility", "Utility"),
            new("worldgen", "World Generation"),
        };

        // Мультивибір версій гри — тепер реальні дані з Mojang-маніфесту (через IMinecraftService),
        // згруповані "по папках": релізи за мажорною гілкою (1.21.x, 1.20.x...), тестові збірки —
        // за роком (2025, 2024...) та окремо пререлізи/реліз-кандидати.
        [ObservableProperty]
        private ObservableCollection<MarketplaceVersionGroup> _releaseVersionGroups = new();

        [ObservableProperty]
        private ObservableCollection<MarketplaceVersionGroup> _snapshotVersionGroups = new();

        [ObservableProperty]
        private bool _showSnapshots;

        // Плаский список усіх чекбоксів версій (реліз+снапшот) — використовується
        // у SearchAsync для збору вибраних значень незалежно від групування в UI.
        private readonly List<FilterOption> _allVersionCheckboxes = new();

        public MarketplaceViewModel()
        {
            _marketplaceService = App.GetService<IMarketplaceService>();
            _downloadManager = App.GetService<IDownloadManager>();
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _instanceStore = App.GetService<IInstanceStore>();
            _modpackInstaller = App.GetService<IModpackInstaller>();
            _minecraftService = App.GetService<IMinecraftService>();

            foreach (var task in _downloadManager.Queue)
            {
                DownloadQueue.Add(task);
            }
            _downloadManager.QueueChanged += OnDownloadQueueChanged;

            foreach (var opt in Categories) opt.PropertyChanged += (_, __) => _ = SearchAsync();

            SelectedTargetInstance = Instances.FirstOrDefault();

            _ = LoadGameVersionsAsync();
            _ = SearchAsync();
        }

        /// <summary>
        /// Тягне повний реальний список версій Minecraft з офіційного Mojang-маніфесту
        /// (через IMinecraftService.GetVersionsAsync — той самий CmlLib-виклик, що й на
        /// сторінці Instances), і розкладає їх "по папках": релізи за мажорною гілкою
        /// (1.21.x, 1.20.x...), тестові збірки — за роком снапшота, пререлізи окремо.
        /// </summary>
        private async Task LoadGameVersionsAsync()
        {
            try
            {
                var versions = await _minecraftService.GetVersionsAsync();

                var releaseGroups = new Dictionary<string, List<FilterOption>>();
                var snapshotGroups = new Dictionary<string, List<FilterOption>>();
                var prereleaseGroup = new List<FilterOption>();

                foreach (var v in versions)
                {
                    var opt = new FilterOption(v.Id);
                    opt.PropertyChanged += (_, __) => _ = SearchAsync();
                    _allVersionCheckboxes.Add(opt);

                    if (v.Type == "release")
                    {
                        // "1.21.4" -> "1.21.x", "1.21" -> "1.21.x"
                        var match = System.Text.RegularExpressions.Regex.Match(v.Id, @"^(\d+)\.(\d+)");
                        var folder = match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.x" : "Інші релізи";

                        if (!releaseGroups.TryGetValue(folder, out var list))
                        {
                            list = new List<FilterOption>();
                            releaseGroups[folder] = list;
                        }
                        list.Add(opt);
                    }
                    else
                    {
                        // Пререлізи/реліз-кандидати ("1.21.2-pre1", "1.21.2-rc1") — окрема папка,
                        // не прив'язана до року, бо їх зазвичай шукають за номером версії.
                        if (v.Id.Contains("-pre", StringComparison.OrdinalIgnoreCase) || v.Id.Contains("-rc", StringComparison.OrdinalIgnoreCase))
                        {
                            prereleaseGroup.Add(opt);
                            continue;
                        }

                        // Тижневі снапшоти у форматі "25w14a" — перші 2 цифри це рік (20 + YY).
                        var snapMatch = System.Text.RegularExpressions.Regex.Match(v.Id, @"^(\d{2})w\d{2}[a-z]$");
                        var folder = snapMatch.Success ? $"20{snapMatch.Groups[1].Value}" : "Старі бета/альфа";

                        if (!snapshotGroups.TryGetValue(folder, out var list))
                        {
                            list = new List<FilterOption>();
                            snapshotGroups[folder] = list;
                        }
                        list.Add(opt);
                    }
                }

                // Найновіші папки зверху (1.21.x перед 1.20.x; 2025 перед 2024).
                ReleaseVersionGroups = new ObservableCollection<MarketplaceVersionGroup>(
                    releaseGroups.OrderByDescending(g => g.Key)
                                 .Select(g => new MarketplaceVersionGroup(g.Key, g.Value)));

                var snapshotVersionGroups = new List<MarketplaceVersionGroup>();
                if (prereleaseGroup.Count > 0)
                {
                    snapshotVersionGroups.Add(new MarketplaceVersionGroup("Пререлізи", prereleaseGroup));
                }
                snapshotVersionGroups.AddRange(
                    snapshotGroups.OrderByDescending(g => g.Key)
                                  .Select(g => new MarketplaceVersionGroup(g.Key, g.Value)));

                SnapshotVersionGroups = new ObservableCollection<MarketplaceVersionGroup>(snapshotVersionGroups);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Не вдалося завантажити список версій гри: {ex.Message}";
            }
        }

        partial void OnShowSnapshotsChanged(bool value) => _ = SearchAsync();

        private void OnDownloadQueueChanged(object? sender, DownloadQueueChangedEventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                var existing = DownloadQueue.FirstOrDefault(t => t.Id == e.Task.Id);
                if (existing == null)
                {
                    DownloadQueue.Insert(0, e.Task);
                }
                else
                {
                    var index = DownloadQueue.IndexOf(existing);
                    DownloadQueue[index] = e.Task;
                }
            });
        }

        partial void OnSelectedTypeChanged(string value) => _ = SearchAsync();
        partial void OnSelectedLoaderChanged(string value) => _ = SearchAsync();

        [RelayCommand]
        public void ClearFilters()
        {
            foreach (var opt in Categories) opt.IsSelected = false;
            foreach (var opt in _allVersionCheckboxes) opt.IsSelected = false;
            ShowSnapshots = false;
            SelectedLoader = "all";
            _ = SearchAsync();
        }

        [RelayCommand]
        public async Task SearchAsync()
        {
            _searchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchCts = cts;

            IsLoading = true;
            StatusMessage = string.Empty;

            try
            {
                var selectedCategories = Categories.Where(c => c.IsSelected).Select(c => c.Value).ToList();
                var selectedVersions = _allVersionCheckboxes.Where(v => v.IsSelected).Select(v => v.Value).ToList();

                var result = await _marketplaceService.SearchProjectsAsync(
                    SearchQuery,
                    projectType: SelectedType,
                    versions: selectedVersions,
                    loader: SelectedLoader,
                    categories: selectedCategories,
                    offset: 0,
                    limit: 30,
                    cancellationToken: cts.Token);

                if (cts.Token.IsCancellationRequested) return;

                Projects = new ObservableCollection<MarketplaceProjectHeader>(result.Hits);

                if (Projects.Count == 0)
                {
                    StatusMessage = "Нічого не знайдено. Спробуй іншу назву або зніми частину фільтрів.";
                }

                // Автоматично показуємо деталі першого результату, як на Modrinth App.
                SelectedProject = Projects.FirstOrDefault();
            }
            catch (OperationCanceledException)
            {
                // новий пошук перервав попередній — це нормально
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка пошуку на Modrinth: {ex.Message}";
            }
            finally
            {
                if (!cts.Token.IsCancellationRequested) IsLoading = false;
            }
        }

        partial void OnSelectedProjectChanged(MarketplaceProjectHeader? value)
        {
            VersionSearchQuery = string.Empty;
            _ = LoadProjectDetailAsync(value);
        }

        partial void OnVersionSearchQueryChanged(string value) => ApplyVersionFilter();

        private void ApplyVersionFilter()
        {
            if (string.IsNullOrWhiteSpace(VersionSearchQuery))
            {
                ProjectVersions = new ObservableCollection<VersionListItem>(_allProjectVersions);
                return;
            }

            var q = VersionSearchQuery.Trim();
            var filtered = _allProjectVersions.Where(v =>
                v.Version.VersionNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                v.Version.VersionType.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                v.Version.GameVersions.Any(gv => gv.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                v.Version.Loaders.Any(l => l.Contains(q, StringComparison.OrdinalIgnoreCase)));

            ProjectVersions = new ObservableCollection<VersionListItem>(filtered);
        }

        private async Task LoadProjectDetailAsync(MarketplaceProjectHeader? project)
        {
            ProjectDetail = null;
            ProjectVersions.Clear();
            _allProjectVersions.Clear();
            SelectedVersion = null;
            DetailStatusMessage = string.Empty;

            if (project == null) return;

            IsDetailLoading = true;
            try
            {
                var detail = await _marketplaceService.GetProjectAsync(project.ProjectId);
                var versions = await _marketplaceService.GetProjectVersionsAsync(project.ProjectId);

                ProjectDetail = detail;
                _allProjectVersions = versions.Select(v => new VersionListItem(v)).ToList();
                ProjectVersions = new ObservableCollection<VersionListItem>(_allProjectVersions);

                // За замовчуванням обираємо версію, сумісну з активним інстансом, якщо є.
                SelectedVersion = ProjectVersions.FirstOrDefault(v => SelectedTargetInstance != null && v.Version.GameVersions.Contains(SelectedTargetInstance.Version))
                                  ?? ProjectVersions.FirstOrDefault();

                if (ProjectVersions.Count == 0)
                {
                    DetailStatusMessage = "Для цього проєкту не знайдено жодної версії.";
                }
            }
            catch (Exception ex)
            {
                DetailStatusMessage = $"Не вдалося завантажити деталі проєкту: {ex.Message}";
            }
            finally
            {
                IsDetailLoading = false;
            }
        }

        [RelayCommand]
        public async Task InstallSelectedVersionAsync()
        {
            if (SelectedProject == null || SelectedVersion == null)
            {
                DetailStatusMessage = "Спершу обери версію зі списку.";
                return;
            }

            var file = SelectedVersion.Version.Files.FirstOrDefault();
            if (file == null)
            {
                DetailStatusMessage = "У цій версії немає файлів для завантаження.";
                return;
            }

            // Modpack (.mrpack) — це окремий формат: архів з manifest-файлом і списком
            // залежностей, тому встановлюється зовсім інакше, ніж один jar/zip мода.
            if (SelectedType == "modpack")
            {
                try
                {
                    IsDetailLoading = true;
                    DetailStatusMessage = $"Встановлення збірки «{SelectedProject.Title}»...";

                    var installedInstance = await _modpackInstaller.InstallModpackAsync(file.Url, SelectedProject.Title);

                    // ВАЖЛИВО: ModpackInstaller качає ЛИШЕ моди/конфіги/overrides збірки -
                    // саму гру (ванільний клієнт + Fabric/Forge/Quilt) він не встановлює.
                    // Без цього кроку тека профілю мала mods/config/saves, але НЕ мала
                    // versions/libraries/assets - тому запуск мовчки нічого не робив.
                    DetailStatusMessage = $"Встановлення Minecraft {installedInstance.Version} ({installedInstance.Loader})...";
                    var gameProgress = new Progress<double>(p => DetailStatusMessage = $"Встановлення гри: {Math.Round(p, 1)}%");
                    await _minecraftService.InstallInstanceAsync(installedInstance, gameProgress);

                    DetailStatusMessage = $"Збірку «{SelectedProject.Title}» встановлено в профіль «{installedInstance.Name}».";
                }
                catch (Exception ex)
                {
                    DetailStatusMessage = $"Помилка встановлення збірки: {ex.Message}";
                }
                finally
                {
                    IsDetailLoading = false;
                }
                return;
            }

            if (SelectedTargetInstance == null)
            {
                DetailStatusMessage = "Спершу оберіть інстанс, куди встановлювати мод.";
                return;
            }

            try
            {
                var destFolder = SelectedType switch
                {
                    "resourcepack" => Path.Combine(_instanceStore.GetInstanceDirectory(SelectedTargetInstance), "resourcepacks"),
                    "shader" => Path.Combine(_instanceStore.GetInstanceDirectory(SelectedTargetInstance), "shaderpacks"),
                    _ => _instanceStore.GetModsDirectory(SelectedTargetInstance)
                };

                Directory.CreateDirectory(destFolder);
                var destPath = Path.Combine(destFolder, file.FileName);

                var category = SelectedType switch
                {
                    "resourcepack" => DownloadCategory.ResourcePack,
                    "shader" => DownloadCategory.Shader,
                    _ => DownloadCategory.Mod
                };
                await _downloadManager.EnqueueAsync(file.Url, destPath, $"{SelectedProject.Title} ({file.FileName})", category, file.Hash);
                DetailStatusMessage = $"«{SelectedProject.Title}» ({SelectedVersion.Version.VersionNumber}) додано в чергу завантажень.";
            }
            catch (Exception ex)
            {
                DetailStatusMessage = $"Помилка встановлення: {ex.Message}";
            }
        }

        // Залишено для сумісності зі старим викликом з картки (швидке встановлення "найкращої" версії).
        [RelayCommand]
        public async Task InstallProjectAsync(MarketplaceProjectHeader project)
        {
            SelectedProject = project;
            await LoadProjectDetailAsync(project);
            await InstallSelectedVersionAsync();
            StatusMessage = DetailStatusMessage;
        }
    }
}
