using System;

namespace Launcher.Models
{
    public class WorldItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string InstanceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "🌲";
        public string Version { get; set; } = "1.21";
        public string Seed { get; set; } = string.Empty;
        public string Gamemode { get; set; } = "Survival";
        public bool Hardcore { get; set; }
        public bool Cheats { get; set; }
        public long Size { get; set; } // in bytes
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastPlayed { get; set; } = DateTime.Now;
        public bool IsFavorite { get; set; }
        public int DatapackCount { get; set; }
        public int ScreenshotCount { get; set; }
        public string? LatestScreenshotPath { get; set; }
    }
}
