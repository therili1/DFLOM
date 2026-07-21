using System.Collections.Generic;

namespace Launcher.Models
{
    /// <summary>"Папка" версій у дереві вибору - наприклад "1.21.x" чи "Знімки (Snapshots)".</summary>
    public class VersionGroup
    {
        public string FolderName { get; set; } = string.Empty;
        public List<string> Versions { get; set; } = new();
        public override string ToString() => FolderName;
    }
}
