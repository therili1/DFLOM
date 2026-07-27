using System;

namespace Launcher.Models
{
    public class ModItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string InstanceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Type { get; set; } = "Mod"; // Mod, ResourcePack, Shader
        public string Version { get; set; } = "1.0.0";
        public long Size { get; set; } // in bytes
        public bool IsEnabled { get; set; } = true;
        public string ModrinthId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
