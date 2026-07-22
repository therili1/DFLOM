using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Launcher.Services
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Level { get; set; } = "INFO"; // INFO, WARNING, ERROR
        public string Source { get; set; } = "Launcher";
        public string Message { get; set; } = string.Empty;

        public string FormattedLine => $"[{Timestamp:HH:mm:ss}] [{Level}] [{Source}]: {Message}";
    }

    /// <summary>
    /// Реальний журнал подій лаунчера (не фейкові дані) — сюди пишуть DownloadManager,
    /// MinecraftService та інші сервіси про фактичні дії: старт/завершення завантажень,
    /// встановлення інстансів, запуск гри, помилки мережі.
    /// </summary>
    public interface ILogService
    {
        IReadOnlyList<LogEntry> Entries { get; }
        event EventHandler<LogEntry>? EntryAdded;

        void Info(string source, string message);
        void Warning(string source, string message);
        void Error(string source, string message);
        void Clear();
    }

    public class LogService : ILogService
    {
        // ВАЖЛИВО: List, а не ObservableCollection - нам не потрібен CollectionChanged
        // (для сповіщень уже є подія EntryAdded), а звичайний List простіше безпечно
        // захистити блокуванням. Без цього LogsView (foreach по Entries на UI-потоці)
        // падав з InvalidOperationException щоразу, коли MinecraftService/DownloadManager
        // писали новий запис з фонового потоку (напр. вивід консолі гри) саме під час
        // перебору - "Collection was modified; enumeration operation may not execute."
        private readonly List<LogEntry> _entries = new();
        private readonly object _lock = new();

        // Повертаємо атомарний знімок під блокуванням, а не живе посилання на список -
        // тоді enumerable, який отримує викликач, вже ніколи не зміниться під час перебору,
        // навіть якщо в цей момент інший потік додає новий запис.
        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToList();
                }
            }
        }

        public event EventHandler<LogEntry>? EntryAdded;

        public void Info(string source, string message) => Add("INFO", source, message);
        public void Warning(string source, string message) => Add("WARNING", source, message);
        public void Error(string source, string message) => Add("ERROR", source, message);

        private void Add(string level, string source, string message)
        {
            var entry = new LogEntry { Level = level, Source = source, Message = message };
            lock (_lock)
            {
                _entries.Add(entry);
            }
            EntryAdded?.Invoke(this, entry);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }
    }
}
