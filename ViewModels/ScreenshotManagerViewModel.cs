using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.ViewModels
{
    public partial class ScreenshotManagerViewModel : ObservableObject
    {
        private readonly IInstanceStore _instanceStore;
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        private readonly List<ScreenshotItem> _allScreenshots = new();

        [ObservableProperty]
        private ObservableCollection<ScreenshotItem> _screenshots = new();

        public ObservableCollection<MinecraftInstance> Instances => _instanceStore.Instances;

        [ObservableProperty]
        private string _filterInstance = "all";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ScreenshotManagerViewModel()
        {
            _instanceStore = App.GetService<IInstanceStore>();
            _ = LoadScreenshotsAsync();
        }

        partial void OnFilterInstanceChanged(string value) => ApplyFilters();

        public async Task LoadScreenshotsAsync()
        {
            IsLoading = true;
            _allScreenshots.Clear();

            await Task.Run(() =>
            {
                foreach (var instance in Instances)
                {
                    var dir = _instanceStore.GetScreenshotsDirectory(instance);
                    if (!Directory.Exists(dir)) continue;

                    foreach (var file in Directory.GetFiles(dir))
                    {
                        if (!ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;

                        var info = new FileInfo(file);
                        lock (_allScreenshots)
                        {
                            _allScreenshots.Add(new ScreenshotItem
                            {
                                Id = file,
                                InstanceId = instance.Id,
                                Name = info.Name,
                                FilePath = info.FullName,
                                Size = info.Length,
                                CapturedAt = info.LastWriteTime,
                                ImageUrl = new Uri(info.FullName).AbsoluteUri
                            });
                        }
                    }
                }
            });

            ApplyFilters();
            IsLoading = false;
        }

        public async Task DeleteScreenshotAsync(string id)
        {
            var ss = _allScreenshots.FirstOrDefault(s => s.Id == id);
            if (ss == null) return;

            try
            {
                if (File.Exists(ss.FilePath)) File.Delete(ss.FilePath);
                StatusMessage = $"Скріншот {ss.Name} видалено.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Не вдалося видалити скріншот: {ex.Message}";
            }

            await LoadScreenshotsAsync();
        }

        public async Task ImportScreenshotAsync(string sourceFilePath, MinecraftInstance targetInstance)
        {
            var dir = _instanceStore.GetScreenshotsDirectory(targetInstance);
            Directory.CreateDirectory(dir);

            try
            {
                var destPath = Path.Combine(dir, Path.GetFileName(sourceFilePath));
                File.Copy(sourceFilePath, destPath, overwrite: true);
                StatusMessage = "Скріншот додано.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Не вдалося додати скріншот: {ex.Message}";
            }

            await LoadScreenshotsAsync();
        }

        private void ApplyFilters()
        {
            var filtered = _allScreenshots.AsEnumerable();

            if (FilterInstance != "all")
            {
                filtered = filtered.Where(s => s.InstanceId == FilterInstance);
            }

            filtered = filtered.OrderByDescending(s => s.CapturedAt);

            Screenshots = new ObservableCollection<ScreenshotItem>(filtered);
        }
    }
}
