using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.Views
{
    /// <summary>Одна версія-чекбокс у флайауті фільтра версій цього діалогу.</summary>
    public partial class DatapackVersionOption : ObservableObject
    {
        public string Value { get; }

        [ObservableProperty]
        private bool _isSelected;

        public DatapackVersionOption(string value) => Value = value;
    }

    public sealed partial class DatapackSearchDialog : ContentDialog
    {
        private readonly IMarketplaceService _marketplaceService;
        private readonly IDownloadManager _downloadManager;
        private readonly IInstanceStore _instanceStore;
        private readonly WorldItem _world;
        private readonly MinecraftInstance? _instance;

        private readonly ObservableCollection<MarketplaceProjectHeader> _results = new();
        private readonly ObservableCollection<DatapackVersionOption> _versionOptions = new()
        {
            new("1.21.4"), new("1.21.3"), new("1.21.1"), new("1.21"),
            new("1.20.6"), new("1.20.4"), new("1.20.1"),
            new("1.19.4"), new("1.19.2"), new("1.18.2"),
        };

        private CancellationTokenSource? _searchCts;

        public DatapackSearchDialog(WorldItem world, MinecraftInstance? instance)
        {
            this.InitializeComponent();
            _world = world;
            _instance = instance;

            _marketplaceService = App.GetService<IMarketplaceService>();
            _downloadManager = App.GetService<IDownloadManager>();
            _instanceStore = App.GetService<IInstanceStore>();

            WorldHeaderText.Text = $"Світ: {_world.Name}" + (instance != null ? $" ({instance.Version})" : "");
            ResultsList.ItemsSource = _results;
            VersionFilterList.ItemsSource = _versionOptions;

            // Якщо відомий інстанс цього світу — одразу відмічаємо його версію,
            // щоб пошук за замовчуванням показував сумісні датапаки.
            if (instance != null)
            {
                var match = _versionOptions.FirstOrDefault(v => v.Value == instance.Version);
                if (match != null) match.IsSelected = true;
            }

            _ = RunSearchAsync();
        }

        private void Search_Click(object sender, RoutedEventArgs e) => _ = RunSearchAsync();

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => _ = RunSearchAsync();

        private async System.Threading.Tasks.Task RunSearchAsync()
        {
            _searchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _searchCts = cts;

            LoadingRing.IsActive = true;
            StatusText.Text = string.Empty;
            _results.Clear();

            try
            {
                var selectedVersions = _versionOptions.Where(v => v.IsSelected).Select(v => v.Value).ToList();

                var result = await _marketplaceService.SearchProjectsAsync(
                    SearchBox.Text,
                    projectType: "datapack",
                    versions: selectedVersions,
                    loader: "all",
                    categories: null,
                    offset: 0,
                    limit: 30,
                    cancellationToken: cts.Token);

                if (cts.Token.IsCancellationRequested) return;

                foreach (var hit in result.Hits) _results.Add(hit);

                if (_results.Count == 0)
                {
                    StatusText.Text = "Нічого не знайдено. Спробуй іншу назву або зніми фільтр версії.";
                }
            }
            catch (OperationCanceledException)
            {
                // новий пошук перервав попередній
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Помилка пошуку: {ex.Message}";
            }
            finally
            {
                if (!cts.Token.IsCancellationRequested) LoadingRing.IsActive = false;
            }
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not MarketplaceProjectHeader project) return;

            try
            {
                var versions = await _marketplaceService.GetProjectVersionsAsync(project.ProjectId);
                var best = _instance != null
                    ? versions.FirstOrDefault(v => v.GameVersions.Contains(_instance.Version)) ?? versions.FirstOrDefault()
                    : versions.FirstOrDefault();

                var file = best?.Files.FirstOrDefault();
                if (file == null)
                {
                    StatusText.Text = $"Для «{project.Title}» немає файлів для завантаження.";
                    return;
                }

                if (_instance == null)
                {
                    StatusText.Text = "Не вдалося визначити інстанс цього світу — встанови вручну через кнопку імпорту.";
                    return;
                }

                var worldDir = Path.Combine(_instanceStore.GetSavesDirectory(_instance), _world.Id);
                var datapacksDir = Path.Combine(worldDir, "datapacks");
                Directory.CreateDirectory(datapacksDir);

                var destPath = Path.Combine(datapacksDir, file.FileName);
                await _downloadManager.EnqueueAsync(file.Url, destPath, $"{project.Title} ({file.FileName})", DownloadCategory.Datapack, file.Hash);

                StatusText.Text = $"«{project.Title}» додано в чергу завантажень для світу «{_world.Name}».";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Помилка встановлення: {ex.Message}";
            }
        }
    }
}
