using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public class InstanceStore : IInstanceStore
    {
        private readonly string _baseDirectory;
        private readonly string _instancesFile;

        public ObservableCollection<MinecraftInstance> Instances { get; } = new();

        public InstanceStore()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _baseDirectory = Path.Combine(appData, ".lrs_launcher");
            Directory.CreateDirectory(_baseDirectory);
            _instancesFile = Path.Combine(_baseDirectory, "instances.json");

            // Синхронне початкове завантаження, щоб дані вже були в колекції
            // до того, як ViewModel-і встигнуть до них звернутись у конструкторах.
            LoadSync();
        }

        public Task LoadAsync()
        {
            LoadSync();
            return Task.CompletedTask;
        }

        private void LoadSync()
        {
            try
            {
                if (File.Exists(_instancesFile))
                {
                    var json = File.ReadAllText(_instancesFile);
                    var list = JsonSerializer.Deserialize<List<MinecraftInstance>>(json) ?? new();

                    Instances.Clear();
                    bool needsResave = false;
                    foreach (var inst in list)
                    {
                        // Міграція: раніше GameDirectory зберігався порожнім для всіх профілів
                        // (див. коментар у AddInstance) - проставляємо його заднім числом, інакше
                        // профілі, створені до фіксу, і далі ставитимуться/запускатимуться
                        // у спільну теку замість власної.
                        if (string.IsNullOrWhiteSpace(inst.GameDirectory))
                        {
                            inst.GameDirectory = GetInstanceDirectory(inst);
                            needsResave = true;
                        }
                        Instances.Add(inst);
                    }

                    if (needsResave)
                    {
                        SaveSync();
                    }
                }
                else if (Instances.Count == 0)
                {
                    SeedDefaultInstances();
                    SaveSync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося завантажити instances.json: {ex.Message}");
                if (Instances.Count == 0)
                {
                    SeedDefaultInstances();
                }
            }
        }

        public Task SaveAsync()
        {
            SaveSync();
            return Task.CompletedTask;
        }

        private void SaveSync()
        {
            try
            {
                var json = JsonSerializer.Serialize(Instances.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_instancesFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося зберегти instances.json: {ex.Message}");
            }
        }

        public MinecraftInstance AddInstance(MinecraftInstance instance)
        {
            if (string.IsNullOrWhiteSpace(instance.Id))
            {
                instance.Id = Guid.NewGuid().ToString();
            }

            // КРИТИЧНО: без цього GameDirectory лишався порожнім, і MinecraftService
            // (InstallInstanceAsync/LaunchInstanceAsync) використовував спільну теку _path
            // замість ізольованої теки профілю. Через це моди, додані в InstanceSettingsDialog
            // (вони кладуться саме в GetModsDirectory(_instance)), гра просто не бачила,
            // а кілька кастомних профілів з різними лоадерами/версіями ставились в ОДНУ й ту
            // саму спільну папку й патчили один одного (Forge/Fabric поверх чужого клієнта) -
            // звідси помилки запуску кастомних інстансів.
            if (string.IsNullOrWhiteSpace(instance.GameDirectory))
            {
                instance.GameDirectory = GetInstanceDirectory(instance);
            }

            Instances.Add(instance);

            // Створюємо структуру тек одразу, щоб saves/mods/screenshots існували з першого запуску.
            Directory.CreateDirectory(GetInstanceDirectory(instance));
            Directory.CreateDirectory(GetSavesDirectory(instance));
            Directory.CreateDirectory(GetModsDirectory(instance));
            Directory.CreateDirectory(GetScreenshotsDirectory(instance));
            Directory.CreateDirectory(GetBackupsDirectory(instance));

            SaveSync();
            return instance;
        }

        public void RemoveInstance(string id)
        {
            var inst = Instances.FirstOrDefault(i => i.Id == id);
            if (inst != null)
            {
                Instances.Remove(inst);
                SaveSync();
            }
        }

        public string GetInstanceDirectory(MinecraftInstance instance)
        {
            if (!string.IsNullOrWhiteSpace(instance.GameDirectory))
            {
                return instance.GameDirectory;
            }

            return Path.Combine(_baseDirectory, "instances", instance.Id);
        }

        public string GetSavesDirectory(MinecraftInstance instance) => Path.Combine(GetInstanceDirectory(instance), "saves");
        public string GetModsDirectory(MinecraftInstance instance) => Path.Combine(GetInstanceDirectory(instance), "mods");
        public string GetScreenshotsDirectory(MinecraftInstance instance) => Path.Combine(GetInstanceDirectory(instance), "screenshots");
        public string GetBackupsDirectory(MinecraftInstance instance) => Path.Combine(GetInstanceDirectory(instance), "backups");

        public int GetModCount(MinecraftInstance instance)
        {
            try
            {
                var dir = GetModsDirectory(instance);
                if (!Directory.Exists(dir)) return 0;
                return Directory.GetFiles(dir, "*.jar").Length;
            }
            catch
            {
                // Тека могла бути видалена/недоступна ззовні - картці досить показати 0,
                // а не валити всю сторінку "Збірки".
                return 0;
            }
        }

        public int GetWorldCount(MinecraftInstance instance)
        {
            try
            {
                var dir = GetSavesDirectory(instance);
                if (!Directory.Exists(dir)) return 0;
                return Directory.GetDirectories(dir).Length;
            }
            catch
            {
                return 0;
            }
        }

        private void SeedDefaultInstances()
        {
            var vanilla = new MinecraftInstance
            {
                Id = "vanilla-1-21",
                Name = "Minecraft 1.21.1 (Офіційний реліз)",
                Version = "1.21.1",
                Loader = "Vanilla",
                AllocatedRAM = 4096,
                Notes = "Чиста ванільна версія гри для виживання та тестів."
            };
            vanilla.GameDirectory = GetInstanceDirectory(vanilla);
            Instances.Add(vanilla);

            var fabric = new MinecraftInstance
            {
                Id = "fabric-1-21",
                Name = "Fabric Speedrun 1.21",
                Version = "1.21",
                Loader = "Fabric",
                LoaderVersion = "0.15.11",
                AllocatedRAM = 6144,
                Notes = "Збірка з оптимізаційними модами Sodium/Lithium для спідранів."
            };
            fabric.GameDirectory = GetInstanceDirectory(fabric);
            Instances.Add(fabric);
        }
    }
}
