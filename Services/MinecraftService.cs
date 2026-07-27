using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Xml.Linq;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
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

        private static readonly System.Net.Http.HttpClient _metaHttpClient = new();

        public async Task<List<string>> GetLoaderVersionsAsync(string loader, string mcVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                switch (loader?.ToLowerInvariant())
                {
                    case "fabric":
                        return await FetchJsonLoaderVersionsAsync(
                            $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}", cancellationToken);

                    case "quilt":
                        return await FetchJsonLoaderVersionsAsync(
                            $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}", cancellationToken);

                    case "forge":
                        return await FetchMavenLoaderVersionsAsync(
                            "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
                            prefix: $"{mcVersion}-", cancellationToken);

                    case "neoforge":
                        // NeoForge's own version scheme drops the leading "1." from the MC version
                        // and reformats the rest (e.g. MC "1.21.1" -> NeoForge versions "21.1.x").
                        // This is NeoForge's documented convention, not a guess - see
                        // https://docs.neoforged.net/docs/gettingstarted/versioning/
                        var neoForgePrefix = mcVersion.StartsWith("1.") ? mcVersion.Substring(2) : mcVersion;
                        return await FetchMavenLoaderVersionsAsync(
                            "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
                            prefix: neoForgePrefix, cancellationToken);

                    default:
                        return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _log.Warning("MinecraftService", $"Не вдалося отримати список версій лоадера {loader} для {mcVersion}: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>Fabric/Quilt meta API - JSON масив об'єктів з полем loader.version,
        /// вже відсортований найновішими першими самим API.</summary>
        private async Task<List<string>> FetchJsonLoaderVersionsAsync(string url, CancellationToken cancellationToken)
        {
            var json = await _metaHttpClient.GetStringAsync(url, cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            var result = new List<string>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("loader", out var loaderObj) &&
                    loaderObj.TryGetProperty("version", out var versionProp))
                {
                    var v = versionProp.GetString();
                    if (!string.IsNullOrEmpty(v)) result.Add(v);
                }
            }
            return result;
        }

        /// <summary>Forge/NeoForge maven-metadata.xml - список усіх версій усіх MC-релізів разом,
        /// фільтруємо за префіксом конкретної версії гри і повертаємо найновіші першими.</summary>
        private async Task<List<string>> FetchMavenLoaderVersionsAsync(string url, string prefix, CancellationToken cancellationToken)
        {
            var xml = await _metaHttpClient.GetStringAsync(url, cancellationToken);
            var doc = System.Xml.Linq.XDocument.Parse(xml);

            var allVersions = doc.Descendants("version").Select(v => v.Value).ToList();

            return allVersions
                .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Reverse() // maven-metadata.xml lists versions oldest-first
                .ToList();
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

            // ROOT CAUSE FIX: FabricInstaller.Install() returns just the bare loader number
            // (e.g. "0.19.3"), but QuiltInstaller.Install() returns the ALREADY-composed full
            // version id (e.g. "quilt-loader-0.20.0-beta.9-1.21.11") - the two installers do NOT
            // behave the same despite sharing the same method signature. The old code assumed
            // both return a bare number and blindly composed "{prefix}-{returned}-{mcVersion}"
            // on top, which for Quilt produced the doubled, invalid id
            // "quilt-loader-quilt-loader-0.20.0-beta.9-1.21.11-1.21.11" - a folder that never
            // existed on disk, so the launcher fell through fallbacks to whatever random folder
            // matched by prefix and launched without Quilt's actual libraries on the classpath
            // (hence "Could not find or load main class ...KnotClient").
            // Check this FIRST, before composing anything: if the raw returned value already
            // matches a real folder on disk, trust it as-is - do not rewrite it further.
            var rawAsFullId = System.IO.Path.Combine(versionsDir, loaderVersionReturned);
            if (System.IO.Directory.Exists(rawAsFullId) &&
                System.IO.File.Exists(System.IO.Path.Combine(rawAsFullId, $"{loaderVersionReturned}.json")))
            {
                // Loader profiles (Fabric/Quilt/Forge) normally have ONLY a .json - they inherit
                // the vanilla .jar via "inheritsFrom" rather than shipping their own, so we
                // deliberately do NOT require a matching .jar here (unlike vanilla version folders).
                return loaderVersionReturned;
            }

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
                    // Sort by folder name descending — creation time is unreliable (filesystem
                    // copies or extractions reset it), and loader folder names embed the version
                    // number so lexicographic descending picks the newest semantic version correctly
                    // (e.g. "fabric-loader-0.19.3-1.21.1" > "fabric-loader-0.15.11-1.21.1").
                    .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
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

                        // QuiltInstaller.Install() створює version JSON, але НЕ завантажує бібліотеки
                        // лоадера (quilt-loader-*.jar, intermediary, тощо) — вони розміщені на
                        // maven.quiltmc.org і потребують окремого проходу InstallAsync.
                        // Без цього кроку Java отримує classpath з посиланнями на відсутні JAR-и
                        // і падає з ClassNotFoundException: KnotClient одразу після старту.
                        // (Ідентична причина раніше ламала Fabric, там виправили через
                        // ResolveInstalledLoaderVersionId; для Quilt потрібен ще й цей крок.)
                        await launcherInstance.InstallAsync(instance.LaunchVersionId, cancellationToken: cancellationToken);
                        break;
                    }
                case "forge":
                    {
                        var forgeInstaller = new ForgeInstaller(launcherInstance);
                        var loaderVersionName = await forgeInstaller.Install(instance.Version);
                        instance.LaunchVersionId = loaderVersionName;
                        break;
                    }
                case "neoforge":
                    {
                        // Same author/codebase as ForgeInstaller above (CmlLib.Core.Installer.NeoForge
                        // is a fork of CmlLib.Core.Installer.Forge with the maven repo/download URLs
                        // swapped to NeoForge's own - per its README: "Automatic change of links to
                        // install Neoforge"). Same API, same call pattern, tested for MC 1.20.2-1.21.10.
                        var neoForgeInstaller = new NeoForgeInstaller(launcherInstance);
                        var loaderVersionName = await neoForgeInstaller.Install(instance.Version);
                        instance.LaunchVersionId = loaderVersionName;
                        break;
                    }
                default:
                    if (!string.Equals(instance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Error("MinecraftService", $"No installer implemented for loader '{instance.Loader}' - installing as vanilla only, mods will NOT work.");
                    }
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

            // ── (C) Виправлення шляху log4j конфігурації ───────────────────────────────
            // Prism: JavaUtils.cpp — передає шлях як QUrl::fromLocalFile(path).toString(),
            // що дає "file:///C:/path/to/client-1.21.2.xml".
            // CmlLib вставляє значення з version JSON як є — голий Windows-шлях
            // (C:\...\log_configs\client-1.21.2.xml). Log4j2 вимагає URI або classpath:
            // рядок; голий шлях дає "Error parsing URI" (але не крашить гру, бо log4j
            // fallback'ає на дефолтну конфігурацію). Замінюємо сирий шлях на file:///-URI
            // так само, як це робить Prism.
            //
            // Аргумент у рядку Arguments може бути в лапках або без, залежно від пробілів
            // у шляху, тому патерн охоплює обидва випадки.
            process.StartInfo.Arguments = Regex.Replace(
                process.StartInfo.Arguments,
                @"-Dlog4j2?\.configurationFile=(""?)([^""\s]+)(""?)",
                m =>
                {
                    var rawPath = m.Groups[2].Value;
                    // new Uri(windowsPath).AbsoluteUri → "file:///C:/path/to/file.xml"
                    if (Uri.TryCreate(rawPath, UriKind.Absolute, out var uri) && uri.IsFile)
                        return $"-Dlog4j2.configurationFile={uri.AbsoluteUri}";
                    if (File.Exists(rawPath))
                        return $"-Dlog4j2.configurationFile={new Uri(rawPath).AbsoluteUri}";
                    return m.Value; // не чіпаємо, якщо не можемо розпарсити
                });

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            // ── (A) WorkingDirectory ────────────────────────────────────────────────────
            // Prism: step->setWorkingDirectory(gameRoot()) [LauncherPartLaunch.cpp]
            //
            // Forge/NeoForge/Quilt use cpw.mods.bootstraplauncher / fabric-loader which
            // resolve some paths relative to the process working directory (e.g. game
            // assets, instance-local libraries). Vanilla's Main.java receives all paths
            // as absolute CLI arguments and is tolerant of any CWD.  Without this, a
            // WinUI3 launcher's CWD is typically its install directory or System32, not
            // the game folder — Forge bootstrap fails to locate its libraries and the JVM
            // crashes inside native code (atio6axx.dll) during the subsequent LWJGL
            // OpenGL context init.
            if (!string.IsNullOrEmpty(instance.GameDirectory))
                process.StartInfo.WorkingDirectory = instance.GameDirectory;

            // ── (B) Strip dangerous JVM environment variables ───────────────────────────
            // Prism: CleanEnviroment() in JavaUtils.cpp removes all of these.
            //
            // JAVA_TOOL_OPTIONS / _JAVA_OPTIONS / JAVA_OPTIONS: JVM flags injected
            // system-wide by IDEs (IntelliJ IDEA, Eclipse), anticheat tools, or global
            // Java config (e.g. -Xmx, -XX:+UseShenandoahGC, -javaagent:...) are
            // prepended to EVERY JVM invocation.  Forge/NeoForge/Quilt require strict
            // control of JVM flags for their module system (--add-opens, --module-path,
            // -XX:+UseG1GC, -Dfile.encoding=UTF-8).  A conflicting flag from
            // JAVA_TOOL_OPTIONS corrupts the module system; the JVM still "starts" but
            // then crashes inside native code at the first OpenGL call — exactly what
            // atio6axx.dll+0x192b60 looks like.  Vanilla has no bootstrap and is tolerant
            // of most extra flags, which is why only Forge/Quilt/NeoForge crash.
            //
            // CLASSPATH: if set, Java prepends it to the effective classpath. Forge's
            // ModLauncher depends on classpath isolation; extra jars break that and can
            // cause class-loading conflicts that surface as native crashes.
            //
            // JAVA_HOME / JRE_HOME: can redirect native library discovery to a different
            // JDK than the one being launched, causing DLL mismatches.
            var dangerousVars = new[]
            {
                "JAVA_ARGS", "CLASSPATH", "CONFIGPATH",
                "JAVA_HOME", "JRE_HOME",
                "_JAVA_OPTIONS", "JAVA_OPTIONS", "JAVA_TOOL_OPTIONS"
            };
            foreach (var v in dangerousVars)
            {
                if (process.StartInfo.EnvironmentVariables.ContainsKey(v))
                {
                    process.StartInfo.EnvironmentVariables.Remove(v);
                    _log.Info("MinecraftService", $"Stripped env var '{v}' (could interfere with Forge/Quilt module system)");
                }
            }

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

            // Full command line, exactly as requested - logged BEFORE Process.Start() so it's
            // captured even if the process crashes/exits instantly. Includes the resolved
            // classpath (-cp) and main class, which is exactly what's needed to diagnose
            // "Could not find or load main class" issues without guessing.
            _log.Info("MinecraftService", $"Java command: \"{process.StartInfo.FileName}\" {process.StartInfo.Arguments}");

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _log.Info("MinecraftService", $"Процес гри запущено, PID {process.Id}");

            _monitoringService.AttachToProcess(process.Id);
            _monitoringService.StartMonitoring();
        }
    }
}