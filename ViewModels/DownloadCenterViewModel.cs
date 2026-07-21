using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Services;

namespace Launcher.ViewModels
{
    public partial class DownloadCenterViewModel : ObservableObject
    {
        private readonly IDownloadManager _downloadManager;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _sortMode = "Newest"; // Newest / Name / Status

        public ObservableCollection<DownloadTask> FilteredQueue { get; } = new();

        public DownloadCenterViewModel()
        {
            _downloadManager = App.GetService<IDownloadManager>();
            // ObservableCollection можна змінювати лише з UI-потоку, а QueueChanged
            // прилітає з фонового Task'а в DownloadManager.RunDownloadAsync - без цього
            // застосунок падав би з COMException при кожному тику прогресу завантаження.
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _downloadManager.QueueChanged += (_, _) => _dispatcherQueue.TryEnqueue(Rebuild);
            Rebuild();
        }

        partial void OnSearchTextChanged(string value) => Rebuild();
        partial void OnSortModeChanged(string value) => Rebuild();

        /// <summary>Перечитує чергу з DownloadManager (це "історія завантажень" одночасно -
        /// завершені/скасовані/помилкові задачі лишаються в списку, а не зникають) з
        /// урахуванням пошуку й сортування.</summary>
        private void Rebuild()
        {
            IEnumerable<DownloadTask> query = _downloadManager.Queue;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var needle = SearchText.Trim();
                query = query.Where(t => t.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            query = SortMode switch
            {
                "Name" => query.OrderBy(t => t.DisplayName),
                "Status" => query.OrderBy(t => t.Status),
                _ => query.OrderByDescending(t => t.EnqueuedAt)
            };

            var result = query.ToList();

            FilteredQueue.Clear();
            foreach (var task in result) FilteredQueue.Add(task);
        }

        [RelayCommand]
        public void PauseTask(string taskId) => _downloadManager.Pause(taskId);

        [RelayCommand]
        public void ResumeTask(string taskId) => _downloadManager.Resume(taskId);

        [RelayCommand]
        public void RetryTask(string taskId) => _downloadManager.Retry(taskId);

        [RelayCommand]
        public void CancelTask(string taskId) => _downloadManager.Cancel(taskId);

        [RelayCommand]
        public void PauseAll() => _downloadManager.PauseAll();

        [RelayCommand]
        public void ResumeAll() => _downloadManager.ResumeAll();

        [RelayCommand]
        public void CancelAll() => _downloadManager.CancelAll();

        public void OpenFolder(DownloadTask task)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(task.DestinationPath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                {
                    _ = Windows.System.Launcher.LaunchFolderPathAsync(dir);
                }
            }
            catch
            {
                // Тека могла бути видалена вручну - мовчки ігноруємо, не валимо UI через це.
            }
        }
    }
}
