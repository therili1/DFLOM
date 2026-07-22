using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public interface IMinecraftService
    {
        Task<List<MinecraftVersion>> GetVersionsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task InstallInstanceAsync(MinecraftInstance instance, IProgress<double> progress, CancellationToken cancellationToken = default);
        Task LaunchInstanceAsync(MinecraftInstance instance, CancellationToken cancellationToken = default);
    }
}
namespace Launcher.Models
{
    public class MinecraftVersion
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // release, snapshot, old_beta, old_alpha
        public string Url { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public DateTime ReleaseTime { get; set; }
    }

    public class MinecraftInstance
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Loader { get; set; } = "Vanilla"; // Vanilla, Forge, Fabric, NeoForge, Quilt
        public string LoaderVersion { get; set; } = string.Empty;
        // Реальний ідентифікатор версії для запуску (напр. "fabric-loader-0.15.11-1.21.1").
        // Заповнюється після встановлення лоадера; для Vanilla дорівнює Version.
        public string LaunchVersionId { get; set; } = string.Empty;
        public int AllocatedRAM { get; set; } = 4096; // in MB
        public string JvmArguments { get; set; } = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions";
        public string GameDirectory { get; set; } = string.Empty;
        public string CustomIcon { get; set; } = string.Empty;
        public string CustomBackground { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime? LastLaunched { get; set; }
    }
}
