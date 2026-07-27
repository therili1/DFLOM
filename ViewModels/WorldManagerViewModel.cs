using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.ViewModels
{
    public partial class WorldManagerViewModel : ObservableObject
    {
        private readonly IInstanceStore _instanceStore;
        private readonly List<WorldItem> _allWorlds = new();

        [ObservableProperty]
        private ObservableCollection<WorldItem> _worlds = new();

        public ObservableCollection<MinecraftInstance> Instances => _instanceStore.Instances;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _filterInstance = "all";

        [ObservableProperty]
        private string _filterGameMode = "all";

        [ObservableProperty]
        private string _sortBy = "last_played";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private MinecraftInstance? _selectedInstance;

        public WorldManagerViewModel()
        {
            _instanceStore = App.GetService<IInstanceStore>();
            SelectedInstance = Instances.FirstOrDefault();
            _ = LoadWorldsAsync();
        }

        partial void OnSearchQueryChanged(string value) => ApplyFilters();
        partial void OnFilterInstanceChanged(string value) => ApplyFilters();
        partial void OnFilterGameModeChanged(string value) => ApplyFilters();
        partial void OnSortByChanged(string value) => ApplyFilters();

        public async Task LoadWorldsAsync()
        {
            IsLoading = true;
            _allWorlds.Clear();

            await Task.Run(() =>
            {
                foreach (var instance in Instances)
                {
                    var savesDir = _instanceStore.GetSavesDirectory(instance);
                    if (!Directory.Exists(savesDir)) continue;

                    foreach (var worldDir in Directory.GetDirectories(savesDir))
                    {
                        var levelDatPath = Path.Combine(worldDir, "level.dat");
                        if (!File.Exists(levelDatPath)) continue;

                        try
                        {
                            var world = ReadWorldFromDisk(instance, worldDir, levelDatPath);
                            lock (_allWorlds) _allWorlds.Add(world);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Не вдалося прочитати світ {worldDir}: {ex.Message}");
                        }
                    }
                }
            });

            ApplyFilters();
            IsLoading = false;
        }

        private WorldItem ReadWorldFromDisk(MinecraftInstance instance, string worldDir, string levelDatPath)
        {
            var root = NbtReader.ReadCompoundFile(levelDatPath);
            var data = NbtReader.GetValue<Dictionary<string, object?>>(root, "Data") ?? new();

            string name = NbtReader.GetValue<string>(data, "LevelName", Path.GetFileName(worldDir)) ?? Path.GetFileName(worldDir);

            long? seed = NbtReader.GetValue<long?>(data, "RandomSeed");
            if (seed == null && data.TryGetValue("WorldGenSettings", out var wgsObj) && wgsObj is Dictionary<string, object?> wgs)
            {
                seed = NbtReader.GetValue<long?>(wgs, "seed");
            }

            int gameType = NbtReader.GetValue<int?>(data, "GameType") ?? 0;
            bool hardcore = (NbtReader.GetValue<byte?>(data, "hardcore") ?? 0) != 0;
            bool cheats = (NbtReader.GetValue<byte?>(data, "allowCommands") ?? 0) != 0;
            long lastPlayedMs = NbtReader.GetValue<long?>(data, "LastPlayed") ?? 0;

            var lastPlayed = lastPlayedMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(lastPlayedMs).LocalDateTime
                : Directory.GetLastWriteTime(worldDir);

            var created = Directory.GetCreationTime(worldDir);
            long sizeOnDisk = GetDirectorySize(worldDir);

            // Датапаки лежать у самій теці світу (world/datapacks/*.zip або розпакованою папкою).
            var datapacksDir = Path.Combine(worldDir, "datapacks");
            int datapackCount = Directory.Exists(datapacksDir)
                ? Directory.GetFileSystemEntries(datapacksDir).Length
                : 0;

            // Скріншоти в ваніль-Minecraft не прив'язані до конкретного світу - вони спільні
            // на весь профіль (instance/screenshots/). Показуємо загальну кількість і останній
            // скріншот профілю, до якого належить цей світ - це найближче до реальної поведінки гри.
            var screenshotsDir = _instanceStore.GetScreenshotsDirectory(instance);
            int screenshotCount = 0;
            string? latestScreenshot = null;
            if (Directory.Exists(screenshotsDir))
            {
                var shots = Directory.GetFiles(screenshotsDir, "*.png")
                    .OrderByDescending(File.GetCreationTime)
                    .ToList();
                screenshotCount = shots.Count;
                latestScreenshot = shots.FirstOrDefault();
            }

            string gamemode = gameType switch
            {
                0 => "Survival",
                1 => "Creative",
                2 => "Adventure",
                3 => "Spectator",
                _ => "Survival"
            };

            return new WorldItem
            {
                Id = Path.GetFileName(worldDir),
                InstanceId = instance.Id,
                Name = name,
                Icon = gamemode switch { "Creative" => "🏰", "Adventure" => "🗺️", "Spectator" => "👻", _ => "🌲" },
                Seed = seed?.ToString() ?? "невідомо",
                Gamemode = gamemode,
                Hardcore = hardcore,
                Cheats = cheats,
                Size = sizeOnDisk,
                CreatedAt = created,
                LastPlayed = lastPlayed,
                IsFavorite = false,
                DatapackCount = datapackCount,
                ScreenshotCount = screenshotCount,
                LatestScreenshotPath = latestScreenshot
            };
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        public async Task ImportDatapackAsync(WorldItem world, string filePath)
        {
            var instance = Instances.FirstOrDefault(i => i.Id == world.InstanceId);
            if (instance == null) return;

            var worldDir = Path.Combine(_instanceStore.GetSavesDirectory(instance), world.Id);
            var datapacksDir = Path.Combine(worldDir, "datapacks");
            Directory.CreateDirectory(datapacksDir);

            var fileName = Path.GetFileName(filePath);
            var destPath = Path.Combine(datapacksDir, fileName);

            try
            {
                await Task.Run(() => File.Copy(filePath, destPath, overwrite: true));
                StatusMessage = $"Датапак «{fileName}» додано до світу «{world.Name}».";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка додавання датапаку: {ex.Message}";
            }

            await LoadWorldsAsync();
        }

        public async Task DeleteWorldAsync(WorldItem world)
        {
            var instance = Instances.FirstOrDefault(i => i.Id == world.InstanceId);
            if (instance == null) return;

            var worldDir = Path.Combine(_instanceStore.GetSavesDirectory(instance), world.Id);
            try
            {
                if (Directory.Exists(worldDir))
                {
                    Directory.Delete(worldDir, recursive: true);
                }
                StatusMessage = $"Світ «{world.Name}» видалено.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка видалення світу: {ex.Message}";
            }

            await LoadWorldsAsync();
        }

        public async Task ToggleFavoriteAsync(WorldItem world)
        {
            // IsFavorite не зберігається в level.dat (це метадані самого лаунчера),
            // тож зберігаємо позначку в окремому невеликому json поряд зі світом.
            world.IsFavorite = !world.IsFavorite;

            var instance = Instances.FirstOrDefault(i => i.Id == world.InstanceId);
            if (instance != null)
            {
                var worldDir = Path.Combine(_instanceStore.GetSavesDirectory(instance), world.Id);
                var metaPath = Path.Combine(worldDir, "launcher_meta.json");
                try
                {
                    await File.WriteAllTextAsync(metaPath, System.Text.Json.JsonSerializer.Serialize(new { world.IsFavorite }));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Не вдалося зберегти launcher_meta.json: {ex.Message}");
                }
            }

            ApplyFilters();
        }

        public async Task BackupWorldAsync(WorldItem world)
        {
            var instance = Instances.FirstOrDefault(i => i.Id == world.InstanceId);
            if (instance == null) return;

            var worldDir = Path.Combine(_instanceStore.GetSavesDirectory(instance), world.Id);
            var backupsDir = _instanceStore.GetBackupsDirectory(instance);
            Directory.CreateDirectory(backupsDir);

            var zipPath = Path.Combine(backupsDir, $"{world.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            try
            {
                await Task.Run(() => ZipFile.CreateFromDirectory(worldDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true));
                StatusMessage = $"Резервну копію збережено: {zipPath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка створення резервної копії: {ex.Message}";
            }
        }

        public async Task ImportWorldZipAsync(string filePath)
        {
            var targetInstance = SelectedInstance ?? Instances.FirstOrDefault();
            if (targetInstance == null)
            {
                StatusMessage = "Немає жодного інстансу для імпорту світу. Спершу створіть інстанс.";
                return;
            }

            var savesDir = _instanceStore.GetSavesDirectory(targetInstance);
            Directory.CreateDirectory(savesDir);

            var worldName = Path.GetFileNameWithoutExtension(filePath);
            var destDir = Path.Combine(savesDir, worldName);

            try
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(filePath, destDir, overwriteFiles: true));
                StatusMessage = $"Світ «{worldName}» імпортовано в {targetInstance.Name}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка імпорту: {ex.Message}";
            }

            await LoadWorldsAsync();
        }

        private void ApplyFilters()
        {
            var filtered = _allWorlds.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(w => w.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || w.Seed.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            if (FilterInstance != "all")
            {
                filtered = filtered.Where(w => w.InstanceId == FilterInstance);
            }

            if (FilterGameMode != "all")
            {
                filtered = filtered.Where(w => w.Gamemode == FilterGameMode);
            }

            filtered = SortBy switch
            {
                "name" => filtered.OrderBy(w => w.Name),
                "size" => filtered.OrderByDescending(w => w.Size),
                _ => filtered.OrderByDescending(w => w.LastPlayed)
            };

            Worlds = new ObservableCollection<WorldItem>(filtered);
        }
    }
}
