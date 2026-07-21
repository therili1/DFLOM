using System;

namespace Launcher.Models
{
    /// <summary>Один запис у списку збережених акаунтів (офлайн, Ely.by, Microsoft).</summary>
    public class SavedAccount
    {
        public string Uuid { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string AuthType { get; set; } = "Offline"; // Offline, ElyBy, Microsoft
        public DateTime LastUsedAt { get; set; } = DateTime.Now;
    }
}
