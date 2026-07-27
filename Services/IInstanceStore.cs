using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    /// <summary>
    /// Єдине джерело правди про список інстансів (профілів) гри.
    /// Дані зберігаються у instances.json в теці лаунчера, а не тримаються
    /// в пам'яті кожної ViewModel окремо.
    /// </summary>
    public interface IInstanceStore
    {
        ObservableCollection<MinecraftInstance> Instances { get; }

        Task LoadAsync();
        Task SaveAsync();

        MinecraftInstance AddInstance(MinecraftInstance instance);
        void RemoveInstance(string id);

        /// <summary>
        /// Повертає реальну теку на диску, де лежать saves/mods/screenshots
        /// цього інстансу (аналог .minecraft для конкретного профілю).
        /// </summary>
        string GetInstanceDirectory(MinecraftInstance instance);

        string GetSavesDirectory(MinecraftInstance instance);
        string GetModsDirectory(MinecraftInstance instance);
        string GetScreenshotsDirectory(MinecraftInstance instance);
        string GetBackupsDirectory(MinecraftInstance instance);

        /// <summary>Кількість .jar-файлів у теці mods цього інстансу - для картки в Grid View.</summary>
        int GetModCount(MinecraftInstance instance);

        /// <summary>Кількість підтек у теці saves цього інстансу (кожна підтека = один світ).</summary>
        int GetWorldCount(MinecraftInstance instance);
    }
}
