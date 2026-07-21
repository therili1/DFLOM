using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.Services
{
    public interface IDownloadManager
    {
        IReadOnlyList<DownloadTask> Queue { get; }
        event EventHandler<DownloadQueueChangedEventArgs>? QueueChanged;

        Task<DownloadTask> EnqueueAsync(string url, string destinationPath, string displayName, DownloadCategory category, string sha1Hash = "", string sha256Hash = "");

        void Pause(string taskId);
        void Resume(string taskId);
        void Retry(string taskId);
        void Cancel(string taskId);

        void PauseAll();
        void ResumeAll();
        void CancelAll();
    }

    public enum DownloadCategory
    {
        Minecraft, Java, Mod, Modpack, World, Datapack, ResourcePack, Shader, Loader
    }

    public enum DownloadStatus
    {
        Waiting,
        Downloading,
        Installing,
        Extracting,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    // ObservableObject замість звичайного класу — щоб UI (ProgressBar тощо)
    // реально оновлювався наживо під час завантаження, а не лише при заміні
    // всього об'єкта в списку (звідси й був binding-warning WMC1105).
    public partial class DownloadTask : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Url { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DownloadCategory Category { get; set; } = DownloadCategory.Minecraft;
        public DateTime EnqueuedAt { get; set; } = DateTime.Now;

        [ObservableProperty]
        private DownloadStatus _status = DownloadStatus.Waiting;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Progress))]
        private long _bytesDownloaded;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Progress))]
        private long _totalBytes;

        public double Progress => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;

        [ObservableProperty]
        private double _speed; // Bytes per second

        [ObservableProperty]
        private TimeSpan _eta;

        [ObservableProperty]
        private string _errorMessage = string.Empty;
    }

    public class DownloadQueueChangedEventArgs : EventArgs
    {
        public DownloadTask Task { get; }
        public DownloadQueueChangedEventArgs(DownloadTask task) => Task = task;
    }
}
