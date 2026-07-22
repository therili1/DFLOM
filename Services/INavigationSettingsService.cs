using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    /// <summary>Положення панелі навігації - віддзеркалює NavigationView.PaneDisplayMode.</summary>
    public enum NavPosition { Left, Right, Top, Bottom }

    public interface INavigationSettingsService
    {
        ObservableCollection<NavigationItemSettings> Items { get; }
        NavPosition Position { get; set; }

        /// <summary>Спрацьовує, коли видимість/порядок/позиція змінились - MainWindow
        /// перебудовує NavigationView.MenuItems заново.</summary>
        event Action? NavigationChanged;

        void SetVisible(string id, bool visible);
        void ToggleFavorite(string id);
        void MoveUp(string id);
        void MoveDown(string id);
        void SetPosition(NavPosition position);

        Task LoadAsync();
        Task SaveAsync();
    }
}
