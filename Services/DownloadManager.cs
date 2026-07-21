using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Services
{
    public class DownloadManager : IDownloadManager
    {
        private readonly HttpClient _httpClient;
        private readonly ILogService _log;
        private readonly ConcurrentDictionary<string, DownloadTask> _tasks = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();
        private readonly ConcurrentDictionary<string, bool> _pausedTasks = new();
        // Зберігаємо параметри задачі окремо від DownloadTask, щоб Retry() міг
        // перезапустити те саме завантаження без повторного виклику EnqueueAsync ззовні.
        private readonly ConcurrentDictionary<string, (string Sha1, string Sha256)> _hashByTaskId = new();

        public event EventHandler<DownloadQueueChangedEventArgs>? QueueChanged;

        public IReadOnlyList<DownloadTask> Queue => _tasks.Values.OrderByDescending(t => t.EnqueuedAt).ToList();

        public DownloadManager(ILogService log)
        {
            _log = log;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SkyLightLauncher/1.0 (+https://github.com)");
        }

        public async Task<DownloadTask> EnqueueAsync(string url, string destinationPath, string displayName, DownloadCategory category, string sha1Hash = "", string sha256Hash = "")
        {
            var task = new DownloadTask
            {
                Url = url,
                DestinationPath = destinationPath,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(destinationPath) : displayName,
                Category = category,
                Status = DownloadStatus.Waiting
            };

            _tasks[task.Id] = task;
            _hashByTaskId[task.Id] = (sha1Hash, sha256Hash);
            RaiseChanged(task);
            _log.Info("DownloadManager", $"Додано в чергу: {task.DisplayName}");

            var cts = new CancellationTokenSource();
            _cts[task.Id] = cts;

            // Запускаємо завантаження у фоні, не блокуючи виклик EnqueueAsync.
            _ = RunDownloadAsync(task, sha1Hash, sha256Hash, cts.Token);

            return task;
        }

        private async Task RunDownloadAsync(DownloadTask task, string sha1Hash, string sha256Hash, CancellationToken token)
        {
            try
            {
                task.Status = DownloadStatus.Downloading;
                task.ErrorMessage = string.Empty;
                RaiseChanged(task);

                var dir = Path.GetDirectoryName(task.DestinationPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var tmpPath = task.DestinationPath + ".part";
                // Продовжуємо з місця, де зупинились, якщо .part-файл вже існує (напр. після Resume) -
                // сервер має підтримувати Range-запити; якщо ні, просто перезаписуємо з нуля.
                long resumeFrom = File.Exists(tmpPath) ? new FileInfo(tmpPath).Length : 0;

                using var request = new HttpRequestMessage(HttpMethod.Get, task.Url);
                if (resumeFrom > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
                }

                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    bool serverSupportsResume = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    if (!serverSupportsResume) resumeFrom = 0;

                    response.EnsureSuccessStatusCode();
                    task.TotalBytes = (response.Content.Headers.ContentLength ?? 0) + resumeFrom;
                    task.BytesDownloaded = resumeFrom;

                    await using var httpStream = await response.Content.ReadAsStreamAsync(token);
                    await using var fileStream = new FileStream(tmpPath, serverSupportsResume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920];
                    int read;
                    long lastBytes = task.BytesDownloaded;
                    var lastTime = DateTime.UtcNow;

                    while ((read = await httpStream.ReadAsync(buffer, token)) > 0)
                    {
                        // Пауза конкретної задачі: спін-очікування, поки її не знімуть через Resume(id).
                        while (_pausedTasks.TryGetValue(task.Id, out var isPaused) && isPaused && !token.IsCancellationRequested)
                        {
                            task.Status = DownloadStatus.Paused;
                            RaiseChanged(task);
                            await Task.Delay(200, token);
                        }

                        if (token.IsCancellationRequested) break;

                        await fileStream.WriteAsync(buffer.AsMemory(0, read), token);
                        task.BytesDownloaded += read;

                        var now = DateTime.UtcNow;
                        var elapsed = (now - lastTime).TotalSeconds;
                        if (elapsed >= 0.5)
                        {
                            task.Speed = (task.BytesDownloaded - lastBytes) / elapsed;
                            task.Eta = task.Speed > 0 && task.TotalBytes > 0
                                ? TimeSpan.FromSeconds((task.TotalBytes - task.BytesDownloaded) / task.Speed)
                                : TimeSpan.Zero;
                            lastBytes = task.BytesDownloaded;
                            lastTime = now;
                            task.Status = DownloadStatus.Downloading;
                            RaiseChanged(task);
                        }
                    }
                }

                if (token.IsCancellationRequested)
                {
                    // .part навмисно НЕ видаляємо тут, якщо це була пауза, а не справжнє скасування -
                    // Cancel() викликається окремо і сам вирішує, чи прибирати частковий файл.
                    task.Status = DownloadStatus.Cancelled;
                    RaiseChanged(task);
                    return;
                }

                task.Status = DownloadStatus.Installing;
                RaiseChanged(task);

                if (!string.IsNullOrEmpty(sha1Hash) && !VerifyHash(tmpPath, sha1Hash, SHA1.Create()))
                {
                    throw new InvalidDataException("SHA-1 контрольна сума не збігається — файл пошкоджено.");
                }
                if (!string.IsNullOrEmpty(sha256Hash) && !VerifyHash(tmpPath, sha256Hash, SHA256.Create()))
                {
                    throw new InvalidDataException("SHA-256 контрольна сума не збігається — файл пошкоджено.");
                }

                if (File.Exists(task.DestinationPath)) File.Delete(task.DestinationPath);
                File.Move(tmpPath, task.DestinationPath);

                task.Status = DownloadStatus.Completed;
                task.BytesDownloaded = task.TotalBytes > 0 ? task.TotalBytes : task.BytesDownloaded;
                RaiseChanged(task);
                _log.Info("DownloadManager", $"Завершено: {task.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                task.Status = DownloadStatus.Cancelled;
                RaiseChanged(task);
                _log.Warning("DownloadManager", $"Скасовано: {task.DisplayName}");
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                task.ErrorMessage = ex.Message;
                RaiseChanged(task);
                _log.Error("DownloadManager", $"Помилка завантаження {task.DisplayName}: {ex.Message}");
            }
        }

        public void Pause(string taskId)
        {
            _pausedTasks[taskId] = true;
        }

        public void Resume(string taskId)
        {
            if (!_tasks.TryGetValue(taskId, out var task)) return;

            // Резюм зі стану Paused (спін-цикл усередині ще живий) - просто знімаємо прапорець.
            if (task.Status == DownloadStatus.Paused)
            {
                _pausedTasks[taskId] = false;
                return;
            }

            // Резюм зі стану Cancelled/Failed, коли попередній цикл RunDownloadAsync уже вийшов -
            // потрібен новий CancellationTokenSource і новий виклик RunDownloadAsync (файл .part
            // лишився на диску, тому фактично це продовження, а не завантаження з нуля).
            if (task.Status is DownloadStatus.Cancelled or DownloadStatus.Failed)
            {
                Retry(taskId);
            }
        }

        public void Retry(string taskId)
        {
            if (!_tasks.TryGetValue(taskId, out var task)) return;

            var (sha1, sha256) = _hashByTaskId.TryGetValue(taskId, out var hashes) ? hashes : (string.Empty, string.Empty);

            _pausedTasks[taskId] = false;
            task.Status = DownloadStatus.Waiting;
            task.ErrorMessage = string.Empty;
            RaiseChanged(task);

            var cts = new CancellationTokenSource();
            _cts[taskId] = cts;
            _ = RunDownloadAsync(task, sha1, sha256, cts.Token);
        }

        public void Cancel(string taskId)
        {
            if (_cts.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
            }
            _pausedTasks[taskId] = false;

            if (_tasks.TryGetValue(taskId, out var task))
            {
                TryDeleteFile(task.DestinationPath + ".part");
            }
        }

        private static bool VerifyHash(string filePath, string expectedHex, HashAlgorithm algorithm)
        {
            using (algorithm)
            {
                using var stream = File.OpenRead(filePath);
                var hash = algorithm.ComputeHash(stream);
                var hex = Convert.ToHexString(hash);
                return string.Equals(hex, expectedHex.Replace("-", ""), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }

        public void PauseAll()
        {
            foreach (var id in _tasks.Keys) Pause(id);
        }

        public void ResumeAll()
        {
            foreach (var id in _tasks.Keys) Resume(id);
        }

        public void CancelAll()
        {
            foreach (var id in _tasks.Keys) Cancel(id);
        }

        private void RaiseChanged(DownloadTask task)
        {
            QueueChanged?.Invoke(this, new DownloadQueueChangedEventArgs(task));
        }
    }
}
