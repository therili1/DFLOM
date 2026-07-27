using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public interface IModpackInstaller
    {
        /// <summary>
        /// Завантажує .mrpack файл, парсить modrinth.index.json, тягне всі перелічені
        /// моди та копіює вміст "overrides" (конфіги/ресурспаки, що йдуть у складі збірки)
        /// у теку цільового інстансу. Повертає інстанс, куди фактично встановлено збірку
        /// (може бути новостворений, якщо жоден наявний не підходить за версією/лоадером).
        /// </summary>
        Task<MinecraftInstance> InstallModpackAsync(string mrpackUrl, string modpackName, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    }

    public class ModpackInstaller : IModpackInstaller
    {
        private readonly IInstanceStore _instanceStore;
        private readonly ILogService _log;
        private readonly HttpClient _httpClient;

        public ModpackInstaller(IInstanceStore instanceStore, ILogService log)
        {
            _instanceStore = instanceStore;
            _log = log;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SkyLightLauncher/1.0 (+https://github.com)");
        }

        public async Task<MinecraftInstance> InstallModpackAsync(string mrpackUrl, string modpackName, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            _log.Info("ModpackInstaller", $"Завантаження збірки «{modpackName}»...");

            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mrpack");
            // Stream to a temp file instead of loading the whole archive into RAM —
            // mrpacks can be hundreds of MB and GetByteArrayAsync caused OOM on large modpacks.
            await using (var httpStream = await _httpClient.GetStreamAsync(mrpackUrl, cancellationToken))
            await using (var tempFileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await httpStream.CopyToAsync(tempFileStream, cancellationToken);
            }

            try
            {
                using var archive = ZipFile.OpenRead(tempFile);

                var indexEntry = archive.GetEntry("modrinth.index.json")
                    ?? throw new InvalidDataException("У .mrpack файлі відсутній modrinth.index.json — це не валідна збірка Modrinth.");

                MrpackIndex index;
                using (var indexStream = indexEntry.Open())
                {
                    index = await JsonSerializer.DeserializeAsync<MrpackIndex>(indexStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
                            ?? throw new InvalidDataException("Не вдалося розпарсити modrinth.index.json.");
                }

                var mcVersion = index.Dependencies?.GetValueOrDefault("minecraft") ?? "";
                var loader = "Vanilla";
                var loaderVersion = "";

                if (index.Dependencies != null)
                {
                    if (index.Dependencies.TryGetValue("fabric-loader", out var fabricVer)) { loader = "Fabric"; loaderVersion = fabricVer; }
                    else if (index.Dependencies.TryGetValue("quilt-loader", out var quiltVer)) { loader = "Quilt"; loaderVersion = quiltVer; }
                    else if (index.Dependencies.TryGetValue("forge", out var forgeVer)) { loader = "Forge"; loaderVersion = forgeVer; }
                    else if (index.Dependencies.TryGetValue("neoforge", out var neoforgeVer)) { loader = "NeoForge"; loaderVersion = neoforgeVer; }
                }

                // Матчимо ТІЛЬКИ якщо це повторна установка/оновлення того самого модпаку
                // (профіль з такою ж назвою вже існує) - інакше завжди створюємо новий,
                // окремий профіль. Раніше тут матчився БУДЬ-ЯКИЙ існуючий профіль з тією
                // самою версією гри й лоадером - через це модпак міг звалитись у чужий
                // профіль просто тому, що в юзера вже була, наприклад, якась Fabric 1.21 збірка.
                var targetInstance = _instanceStore.Instances.FirstOrDefault(i =>
                    string.Equals(i.Name, modpackName, StringComparison.OrdinalIgnoreCase) &&
                    i.Version == mcVersion && string.Equals(i.Loader, loader, StringComparison.OrdinalIgnoreCase));

                if (targetInstance == null)
                {
                    targetInstance = _instanceStore.AddInstance(new MinecraftInstance
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = modpackName,
                        Version = mcVersion,
                        Loader = loader,
                        LoaderVersion = loaderVersion,
                        AllocatedRAM = 4096,
                        Notes = $"Автоматично створено при встановленні збірки «{modpackName}» з Modrinth."
                    });
                    _log.Info("ModpackInstaller", $"Створено новий профіль «{modpackName}» ({mcVersion}, {loader}).");
                }

                var instanceDir = _instanceStore.GetInstanceDirectory(targetInstance);
                Directory.CreateDirectory(instanceDir);

                // Завантажуємо всі перелічені файли (моди, шейдери, ресурспаки збірки).
                var files = index.Files ?? new List<MrpackFile>();
                int done = 0;
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var url = file.Downloads?.FirstOrDefault();
                    if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(file.Path)) continue;

                    var destPath = Path.Combine(instanceDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    // Stream each mod file directly to disk — loading every file into RAM
                    // caused OOM when a modpack contained large files (e.g. resource packs).
                    await using (var fileHttpStream = await _httpClient.GetStreamAsync(url, cancellationToken))
                    await using (var destFileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        await fileHttpStream.CopyToAsync(destFileStream, cancellationToken);
                    }

                    done++;
                    progress?.Report(files.Count > 0 ? (double)done / files.Count * 100 : 100);
                }

                // Розпаковуємо "overrides" — це конфіги/ресурси, що йдуть прямо в архіві,
                // а не завантажуються окремо з CDN.
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(entry.Name)) continue; // це тека, не файл

                    var relativePath = entry.FullName.Substring("overrides/".Length);
                    var destPath = Path.Combine(instanceDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    entry.ExtractToFile(destPath, overwrite: true);
                }

                _log.Info("ModpackInstaller", $"Збірку «{modpackName}» встановлено: {done} файлів + overrides у {targetInstance.Name}.");
                return targetInstance;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* тимчасовий файл, не критично */ }
            }
        }

        // ==== Структура modrinth.index.json ====

        private class MrpackIndex
        {
            [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("versionId")] public string? VersionId { get; set; }
            [JsonPropertyName("files")] public List<MrpackFile>? Files { get; set; }
            [JsonPropertyName("dependencies")] public Dictionary<string, string>? Dependencies { get; set; }
        }

        private class MrpackFile
        {
            [JsonPropertyName("path")] public string? Path { get; set; }
            [JsonPropertyName("downloads")] public List<string>? Downloads { get; set; }
        }
    }
}
