using System;

namespace Launcher.Models
{
    public class ScreenshotItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string InstanceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long Size { get; set; } // in bytes
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        public string ImageUrl { get; set; } = string.Empty;
    }
}
