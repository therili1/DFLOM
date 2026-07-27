using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Launcher.Services;

namespace Launcher.ViewModels
{
    public partial class MonitoringViewModel : ObservableObject
    {
        private readonly IMonitoringService _monitoringService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private bool _isMonitoringActive;

        [ObservableProperty]
        private double _launcherCpu;

        [ObservableProperty]
        private double _launcherRam;

        [ObservableProperty]
        private double _launcherNetwork;

        [ObservableProperty]
        private double _minecraftCpu;

        [ObservableProperty]
        private double _minecraftRam;

        [ObservableProperty]
        private double _minecraftGpu;

        [ObservableProperty]
        private double _cpuTemp = 55.0;

        [ObservableProperty]
        private string _statusText = "Моніторинг вимкнено";

        public ObservableCollection<double> LauncherCpuHistory { get; } = new();
        public ObservableCollection<double> MinecraftCpuHistory { get; } = new();

        public MonitoringViewModel()
        {
            _monitoringService = App.GetService<IMonitoringService>();
            // GetForCurrentThread() returns null when called from a non-UI thread.
            // This ViewModel is a Singleton resolved lazily from MonitoringView's
            // constructor, which runs on the UI thread during Frame.Navigate().
            // Capture the queue here; null-guard every TryEnqueue() call below.
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            _monitoringService.UsageUpdated += OnUsageUpdated;
        }

        /// <summary>
        /// Дозволяє прив'язати моніторинг до конкретного процесу Minecraft
        /// (наприклад, одразу після LaunchInstanceAsync повернув Process.Id).
        /// </summary>
        public void AttachToMinecraftProcess(int processId) => _monitoringService.AttachToProcess(processId);

        [RelayCommand]
        public void ToggleMonitoring()
        {
            if (_isMonitoringActive)
            {
                _monitoringService.StopMonitoring();
                _isMonitoringActive = false;
                StatusText = "Моніторинг зупинено";
            }
            else
            {
                _monitoringService.StartMonitoring();
                _isMonitoringActive = true;
                StatusText = "Моніторинг активний";
            }
        }

        private void OnUsageUpdated(SystemUsageSnapshot snapshot)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                LauncherCpu = snapshot.LauncherCpu;
                LauncherRam = snapshot.LauncherRam;
                LauncherNetwork = snapshot.LauncherNetworkSpeed;

                MinecraftCpu = snapshot.MinecraftCpu;
                MinecraftRam = snapshot.MinecraftRam;
                MinecraftGpu = snapshot.MinecraftGpu;

                if (snapshot.CpuTemperature.HasValue)
                {
                    CpuTemp = snapshot.CpuTemperature.Value;
                }

                // Maintain histories
                LauncherCpuHistory.Add(LauncherCpu);
                if (LauncherCpuHistory.Count > 30) LauncherCpuHistory.RemoveAt(0);

                MinecraftCpuHistory.Add(MinecraftCpu);
                if (MinecraftCpuHistory.Count > 30) MinecraftCpuHistory.RemoveAt(0);
            });
        }

    }
}
