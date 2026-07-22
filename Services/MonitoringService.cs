using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Services
{
    public class MonitoringService : IMonitoringService
    {
        private readonly Process _launcherProcess;
        private Process? _minecraftProcess;
        private Timer? _monitoringTimer;
        private bool _isMonitoring;

        public event Action<SystemUsageSnapshot>? UsageUpdated;

        public MonitoringService()
        {
            _launcherProcess = Process.GetCurrentProcess();
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;

            // Запускаємо таймер, який кожну секунду (1000 мс) зніматиме показники системи
            _monitoringTimer = new Timer(UpdateMetrics, null, 0, 1000);
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _monitoringTimer?.Dispose();
            _monitoringTimer = null;
        }

        public void AttachToProcess(int processId)
        {
            try
            {
                _minecraftProcess = Process.GetProcessById(processId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не вдалося прикріпитися до процесу Minecraft: {ex.Message}");
            }
        }

        private void UpdateMetrics(object? state)
        {
            if (!_isMonitoring) return;

            var snapshot = new SystemUsageSnapshot();

            try
            {
                // 1. Оновлюємо метрики ЛАУНЧЕРА
                _launcherProcess.Refresh();
                snapshot.LauncherRam = _launcherProcess.WorkingSet64 / (1024.0 * 1024.0); // RAM в MB
                snapshot.LauncherCpu = GetCpuUsageForProcess(_launcherProcess);

                // 2. Оновлюємо метрики MINECRAFT (якщо гра запущена)
                if (_minecraftProcess != null && !_minecraftProcess.HasExited)
                {
                    _minecraftProcess.Refresh();
                    snapshot.MinecraftRam = _minecraftProcess.WorkingSet64 / (1024.0 * 1024.0); // RAM в MB
                    snapshot.MinecraftCpu = GetCpuUsageForProcess(_minecraftProcess);
                    
                    // GPU та VRAM (спрощене отримання або дефолт, якщо немає доступу до PerformanceCounters)
                    snapshot.MinecraftGpu = 0; // Для точного GPU потрібен PerformanceCounter WMI
                    snapshot.MinecraftVram = 0; 
                }
                else
                {
                    _minecraftProcess = null; // Процес завершився
                }

                // Передаємо свіжі дані в UI (ViewModel підпишеться на цю подію)
                UsageUpdated?.Invoke(snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Помилка підрахунку метрик: {ex.Message}");
            }
        }

        // Допоміжний метод для розрахунку % використання CPU конкретним процесом.
        // ВАЖЛИВО: раніше тут були спільні поля _lastTime/_lastCpuTime для ОБОХ процесів
        // (лаунчера і гри) одразу — це псувало розрахунок, оскільки другий виклик
        // у тому ж тику використовував базову точку від першого процесу. Тепер кожен
        // процес має власний трекер стану за PID.
        private readonly Dictionary<int, (DateTime lastTime, TimeSpan lastCpuTime)> _cpuTrackers = new();

        private double GetCpuUsageForProcess(Process process)
        {
            try
            {
                var now = DateTime.UtcNow;
                var cpuTime = process.TotalProcessorTime;

                if (!_cpuTrackers.TryGetValue(process.Id, out var previous))
                {
                    // Перший замір цього процесу — немає з чим порівнювати, повертаємо 0.
                    _cpuTrackers[process.Id] = (now, cpuTime);
                    return 0;
                }

                var timeWindow = now - previous.lastTime;
                var systemTimePassed = timeWindow.TotalMilliseconds * Environment.ProcessorCount;
                var cpuTimePassed = (cpuTime - previous.lastCpuTime).TotalMilliseconds;

                _cpuTrackers[process.Id] = (now, cpuTime);

                if (systemTimePassed <= 0) return 0;

                double percent = (cpuTimePassed / systemTimePassed) * 100;
                return Math.Round(Math.Max(0, percent), 1);
            }
            catch
            {
                return 0;
            }
        }
    }
}