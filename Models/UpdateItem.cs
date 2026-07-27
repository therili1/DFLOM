using System;

namespace Launcher.Models
{
    public class UpdateItem
    {
        public string Version { get; set; } = "1.0.0";
        public string Title { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; } = DateTime.Now;
        public string DownloadUrl { get; set; } = string.Empty;
        public long Size { get; set; } // in bytes
        public bool IsCritical { get; set; }
    }
}
