using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Version;
using Launcher.Models;

namespace Launcher.Services
{
    public class MinecraftService : IMinecraftService
    {
        private readonly MinecraftPath _path;
        private readonly MinecraftLauncher _launcher;
        private readonly IAuthenticationService _authService;
        private readonly IMonitoringService _monitoringService;
        private readonly ILogService _log;

        public MinecraftService(IAuthenticationService authService, IMonitoringService monitoringService, ILogService log)
        {
            _authService = authService;
            _monitoringService = monitoringService;
            _log = log;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string gameDirectory = Path.Combine(appData, ".lrs_launcher");

            _path = new MinecraftPath(gameDirectory);
            _launcher = new MinecraftLauncher(_path);
        }

        // 1. Отримуємо версії
        public async Task<List<Launcher.Models.MinecraftVersion>> GetVersionsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var versions = await _launcher.GetAllVersionsAsync();

                return versions
                    .Where(v => !string.IsNullOrEmpty(v.Name))
                    .Select(v => new Launcher.Models.MinecraftVersion
                    {
                        Id = v.Name ?? string.Empty,
                        Type = v.Type ?? "release",
                        Url = "",
                    // CmlLib.GetAllVersionsAsync() не повертає дату релізу окремим полем
                    // (лише Name/Type), тож не показуємо вигадану дату — краще порожньо, ніж неправдиво.
                    ReleaseTime = default
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Помилка завантаження версій: {ex.Message}");
                return new List<Launcher.Models.MinecraftVersion>();
            }
        }

        // 2. Завантаження файлів гри (+ встановлення модлоадера, якщо обраний)
        /// <summary>
        /// FabricInstaller/QuiltInstaller.Install() повертає лише номер версії лоадера
        /// (напр. "0.19.3"), а не повний ID теки версії в versions/ - реальна тека
        /// на диску називається "{loaderPrefix}-{loaderVersion}-{mcVersion}"
        /// (перевірено вручну: fabric-loader-0.19.3-1.21.11). Формуємо цей ID і
        /// перевіряємо, що тека з ним справді існує на диску - якщо ні, шукаємо
        /// найновішу підходящу теку за префіксом, щоб не запускати гру наосліп
        /// на вигаданому ID.
        /// </summary>
        private string ResolveInstalledLoaderVersionId(MinecraftPath instancePath, string loaderPrefix, string loaderVersionReturned, string mcVersion)
        {
            var versionsDir = instancePath.Versions;

            var expectedId = $"{loaderPrefix}-{loaderVersionReturned}-{mcVersion}";
            if (System.IO.Directory.Exists(System.IO.Path.Combine(versionsDir, expectedId)))
            {
                return expectedId;
            }

            // Пошук по префіксу (loaderPrefix + mcVersion) - НАДІЙНІШИЙ за наступний блок,
            // бо вимагає точний префікс "fabric-loader-"/"quilt-loader-", тому йде першим
            // серед fallback-ів. Раніше тут була інша черга: спершу перевірявся "голий"
            // loaderVersionReturned як назва теки (без префіксу), і якщо там випадково
            // лежав будь-який .jar - код довіряв цій теці як готовому профілю лоадера.
            // Саме так гра одного разу запустилась на теці "0.18.4" (проміжний/непов'язаний
            // артефакт CmlLib, не справжній Fabric-профіль) і впала з
            // ClassNotFoundException: KnotClient, бо в класпаті не було бібліотек Fabric.
            if (System.IO.Directory.Exists(versionsDir))
            {
                var candidate = System.IO.Directory.GetDirectories(versionsDir)
                    .Select(d => System.IO.Path.GetFileName(d))
                    .Where(name => name.StartsWith(loaderPrefix, StringComparison.OrdinalIgnoreCase) && name.Contains(mcVersion))
                    .OrderByDescending(name => System.IO.Directory.GetCreationTime(System.IO.Path.Combine(versionsDir, name)))
                    .FirstOrDefault();

                if (candidate != null)
                {
                    _log.Warning("MinecraftService", $"Очікуваний ID '{expectedId}' не знайдено на диску, використовую знайдений '{candidate}'.");
                    return candidate;
                }
            }

            // Останній, найризикованіший запасний варіант - "голий" loaderVersionReturned
            // як назва теки, без префіксу. Лишаємо на випадок якщоїсь версії CmlLib, що
            // справді іменує теки так, але перевіряємо суворіше: не просто "є якийсь .jar",
            // а що в теці є ще й .json (справжній launch-профіль, а не самотній jar-артефакт).
            var bareDir = System.IO.Path.Combine(versionsDir, loaderVersionReturned);
            if (System.IO.Directory.Exists(bareDir))
            {
                var jarPath = System.IO.Path.Combine(bareDir, $"{loaderVersionReturned}.jar");
                var jsonPath = System.IO.Path.Combine(bareDir, $"{loaderVersionReturned}.json");
                if (System.IO.File.Exists(jarPath) && System.IO.File.Exists(jsonPath))
                {
                    _log.Warning("MinecraftService", $"Очікуваний ID '{expectedId}' і пошук по префіксу не знайшли нічого, використовую '{loaderVersionReturned}' як є (є і .jar, і .json).");
                    return loaderVersionReturned;
                }
            }

            _log.Error("MinecraftService", $"Не вдалося знайти встановлений профіль лоадера для {loaderPrefix} {mcVersion} на диску. Гра може не запуститись.");
            return expectedId;
        }

        public async Task InstallInstanceAsync(MinecraftInstance instance, IProgress<double> progress, CancellationToken cancellationToken = default)
        {
            _log.Info("MinecraftService", $"Встановлення {instance.Name} ({instance.Version}, {instance.Loader})...");

            var instancePath = string.IsNullOrEmpty(instance.GameDirectory)
                ? _path
                : new MinecraftPath(instance.GameDirectory);

            var launcherInstance = new MinecraftLauncher(instancePath);

            launcherInstance.FileProgressChanged += (sender, args) =>
            {
                if (args.TotalTasks > 0)
                {
                    double percent = (double)args.ProgressedTasks / args.TotalTasks * 100;
                    progress?.Report(percent);
                }
            };

            // Крок 1: завжди спершу ставимо ванільну базову версію — і Fabric, і Forge
            // патчать вже наявний ванільний клієнт, а не замінюють його.
            await launcherInstance.InstallAsync(instance.Version, cancellationToken: cancellationToken);

            // Крок 2: якщо обраний модлоадер — встановлюємо його поверх ванілі.
            switch (instance.Loader?.ToLowerInvariant())
            {
                case "fabric":
                    {
                        var fabricInstaller = new FabricInstaller(new System.Net.Http.HttpClient());
                        // Офіційна сигнатура: Install(mcVersion, loaderVersion, path) — версія лоадера
                        // йде ДРУГИМ аргументом, path третім.
                        var loaderVersionName = string.IsNullOrWhiteSpace(instance.LoaderVersion)
                            ? await fabricInstaller.Install(instance.Version, instancePath)
                            : await fabricInstaller.Install(instance.Version, instance.LoaderVersion, instancePath);

                        // ВАЖЛИВО: повернене значення - це лише номер версії лоадера (напр. "0.19.3"),
                        // а НЕ повний ID теки в versions/ - реальна тека на диску називається
                        // "fabric-loader-{loaderVersion}-{mcVersion}" (перевірено вручну на диску,
                        // раніше тут помилково вважали повернене значення вже готовим ID, через що
                        // гра запускала неіснуючий/порожній профіль і падала з
                        // ClassNotFoundException: KnotClient). Формуємо composite ID сам і звіряємо
                        // з реальною текою на диску, а не довіряємо сліпо.
                        instance.LaunchVersionId = ResolveInstalledLoaderVersionId(instancePath, "fabric-loader", loaderVersionName, instance.Version);
                        break;
                    }
                case "quilt":
                    {
                        var quiltInstaller = new QuiltInstaller(new System.Net.Http.HttpClient());
                        // Той самий порядок аргументів, що й у FabricInstaller (Quilt повторює API Fabric).
                        var loaderVersionName = string.IsNullOrWhiteSpace(instance.LoaderVersion)
                            ? await quiltInstaller.Install(instance.Version, instancePath)
                            : await quiltInstaller.Install(instance.Version, instance.LoaderVersion, instancePath);

                        instance.LaunchVersionId = ResolveInstalledLoaderVersionId(instancePath, "quilt-loader", loaderVersionName, instance.Version);
                        break;
                    }
                case "forge":
                    {
                        var forgeInstaller = new ForgeInstaller(launcherInstance);
                        var loaderVersionName = await forgeInstaller.Install(instance.Version);
                        instance.LaunchVersionId = loaderVersionName;
                        break;
                    }
                default:
                    // Vanilla — запускаємо ту саму версію, що встановили.
                    instance.LaunchVersionId = instance.Version;
                    break;
            }

            _log.Info("MinecraftService", $"Встановлення {instance.Name} завершено. Версія запуску: {instance.LaunchVersionId}");
        }

        // 3. Запуск гри
        public async Task LaunchInstanceAsync(MinecraftInstance instance, CancellationToken cancellationToken = default)
        {
            var instancePath = string.IsNullOrEmpty(instance.GameDirectory)
                ? _path
                : new MinecraftPath(instance.GameDirectory);

            var launcherInstance = new MinecraftLauncher(instancePath);

            MSession session;
            if (_authService.IsAuthenticated && !string.IsNullOrEmpty(_authService.AccessToken))
            {
                session = new MSession(
                    _authService.Username ?? "Player",
                    _authService.AccessToken,
                    _authService.UUID ?? Guid.NewGuid().ToString()
                );
            }
            else
            {
                session = MSession.CreateOfflineSession(_authService.Username ?? "Player");
            }

            var launchOption = new MLaunchOption
            {
                Session = session,
                MaximumRamMb = instance.AllocatedRAM > 0 ? instance.AllocatedRAM : 4096
            };

            var argumentsList = new List<MArgument>();

            if (!string.IsNullOrEmpty(instance.JvmArguments))
            {
                foreach (var arg in instance.JvmArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    argumentsList.Add(new MArgument(arg));
                }
            }

            if (_authService is AuthenticationService authService && authService.AuthType == "ElyBy")
            {
                argumentsList.Add(new MArgument("-Dminecraft.api.auth.host=https://authserver.ely.by"));
            }

            if (argumentsList.Count > 0)
            {
                launchOption.ExtraJvmArguments = argumentsList;
            }

            // Запускаємо саме той ідентифікатор версії, що вийшов після встановлення лоадера
            // (напр. "fabric-loader-0.15.11-1.21.1"), а не сиру версію гри — інакше
            // моди для Fabric/Forge просто не завантажаться.
            var versionToLaunch = !string.IsNullOrWhiteSpace(instance.LaunchVersionId)
                ? instance.LaunchVersionId
                : instance.Version;

            _log.Info("MinecraftService", $"Запуск {instance.Name}: версія '{versionToLaunch}', RAM {launchOption.MaximumRamMb}MB, тека '{instance.GameDirectory}'");

            System.Diagnostics.Process process;
            try
            {
                // ВАЖЛИВО (третя і остаточна спроба, цього разу спираючись на факти, а не
                // припущення): MinecraftLauncher.VersionLoader за замовчуванням - це САМЕ
                // MojangJsonVersionLoaderV2 (підтверджено логом попередньої спроби), який знає
                // тільки офіційні версії Mojang. Властивість VersionLoader на MinecraftLauncher
                // не має публічного сеттера, тож підмінити її неможливо. Замість цього -
                // резолвимо версію напряму через LocalJsonVersionLoader (реальний клас бібліотеки,
                // що сканує versions/ на диску) і викликаємо GetAndSaveVersionAsync - той самий
                // метод, що видно у стектрейсі помилки (значить, він публічний і саме так
                // бібліотека сама перетворює "ім'я версії" на повний IVersion з урахуванням
                // ланцюжка inheritsFrom). Отриманий IVersion передаємо в синхронний BuildProcess,
                // минаючи BuildProcessAsync(string,...) і його прив'язку до онлайн-каталогу.
                var localLoader = new CmlLib.Core.VersionLoader.LocalJsonVersionLoader(instancePath);
                var localMetadatas = await localLoader.GetVersionMetadatasAsync();
                var resolvedVersion = await localMetadatas.GetAndSaveVersionAsync(versionToLaunch, instancePath);

                process = launcherInstance.BuildProcess(resolvedVersion, launchOption);
            }
            catch (Exception ex)
            {
                // Найчастіша причина: версії "fabric-loader-..." немає в теці versions/ -
                // або тому, що встановлення реально не завершилось (LaunchVersionId
                // проставили, а файли не докачались), або тому, що вона встановлена
                // в ІНШУ теку, ніж та, з якої зараз запускаємо (instancePath).
                _log.Error("MinecraftService", $"BuildProcessAsync провалився для версії '{versionToLaunch}': {ex.Message}");
                throw new InvalidOperationException($"Не вдалося зібрати команду запуску для версії '{versionToLaunch}'. Перевірте, чи існує {Path.Combine(instance.GameDirectory, "versions", versionToLaunch)}. Деталі: {ex.Message}", ex);
            }

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    System.Diagnostics.Debug.WriteLine($"[Minecraft Out]: {e.Data}");
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[Minecraft Err]: {e.Data}");
                    // stderr від самого клієнта гри - реальні краш-стеки (напр. "UnsupportedClassVersionError"
                    // при невідповідній версії Java) варто бачити в Console Logs, а не лише в дебагері.
                    _log.Warning("Minecraft", e.Data);
                }
            };

            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) =>
            {
                // Якщо гра "запускається" і одразу закривається (типова причина - невідповідна
                // версія Java: MC 1.21+ вимагає Java 21+, а стоїть, скажімо, Java 8) - ExitCode
                // тут і є той сигнал, якого не було видно раніше через мовчазний UI.
                _log.Info("MinecraftService", $"Процес гри '{instance.Name}' завершився з кодом {process.ExitCode}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _log.Info("MinecraftService", $"Процес гри запущено, PID {process.Id}");

            _monitoringService.AttachToProcess(process.Id);
            _monitoringService.StartMonitoring();
        }
    }
}