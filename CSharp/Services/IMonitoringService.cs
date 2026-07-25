using System;

namespace Launcher.Services
{
    public interface IMonitoringService
    {
        event Action<SystemUsageSnapshot>? UsageUpdated;
        void StartMonitoring();
        void StopMonitoring();
        void AttachToProcess(int processId);
    }

    public class SystemUsageSnapshot
    {
        public double LauncherCpu { get; set; }
        public double LauncherRam { get; set; } // in MB
        public double LauncherDiskReadWrite { get; set; } // in MB/s
        public double LauncherNetworkSpeed { get; set; } // in MB/s

        public double MinecraftCpu { get; set; }
        public double MinecraftRam { get; set; } // in MB
        public double MinecraftVram { get; set; } // in MB
        public double MinecraftGpu { get; set; }
        public double MinecraftDiskReadWrite { get; set; } // in MB/s
        public double MinecraftNetworkSpeed { get; set; } // in MB/s
        public double? CpuTemperature { get; set; } // Optional if available
    }
}
