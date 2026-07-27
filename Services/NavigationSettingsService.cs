using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public class NavigationSettingsService : INavigationSettingsService
    {
        private class PersistedState
        {
            public List<NavigationItemSettings> Items { get; set; } = new();
            public NavPosition Position { get; set; } = NavPosition.Left;
        }

        private readonly string _navFile;

        public ObservableCollection<NavigationItemSettings> Items { get; } = new();
        public NavPosition Position { get; set; } = NavPosition.Left;
        public event Action? NavigationChanged;

        public NavigationSettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseDirectory = Path.Combine(appData, ".lrs_launcher");
            Directory.CreateDirectory(baseDirectory);
            _navFile = Path.Combine(baseDirectory, "navigation.json");
        }

        public void SetVisible(string id, bool visible)
        {
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;
            item.IsVisible = visible;
            NavigationChanged?.Invoke();
            _ = SaveAsync();
        }

        public void ToggleFavorite(string id)
        {
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;
            item.IsFavorite = !item.IsFavorite;
            NavigationChanged?.Invoke();
            _ = SaveAsync();
        }

        public void MoveUp(string id) => Reorder(id, -1);
        public void MoveDown(string id) => Reorder(id, 1);

        private void Reorder(string id, int direction)
        {
            var ordered = Items.OrderBy(i => i.Order).ToList();
            int index = ordered.FindIndex(i => i.Id == id);
            int targetIndex = index + direction;
            if (index < 0 || targetIndex < 0 || targetIndex >= ordered.Count) return;

            (ordered[index], ordered[targetIndex]) = (ordered[targetIndex], ordered[index]);
            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;

            // ObservableCollection треба перебудувати, щоб UI, прив'язаний до Items
            // напряму (напр. ListView у SettingsView), теж перечитав новий порядок.
            Items.Clear();
            foreach (var item in ordered) Items.Add(item);

            NavigationChanged?.Invoke();
            _ = SaveAsync();
        }

        public void SetPosition(NavPosition position)
        {
            Position = position;
            NavigationChanged?.Invoke();
            _ = SaveAsync();
        }

        public async Task LoadAsync()
        {
            PersistedState state;
            try
            {
                if (File.Exists(_navFile))
                {
                    var json = await File.ReadAllTextAsync(_navFile);
                    state = JsonSerializer.Deserialize<PersistedState>(json) ?? new PersistedState { Items = DefaultItems() };
                }
                else
                {
                    state = new PersistedState { Items = DefaultItems() };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося завантажити navigation.json: {ex.Message}");
                state = new PersistedState { Items = DefaultItems() };
            }

            // Якщо в збереженому файлі бракує пункту (наприклад, додали нову сторінку
            // в новій версії лаунчера після того, як користувач уже зберіг свою навігацію) -
            // домальовуємо його в кінець, щоб нова сторінка не "зникла" назавжди.
            var defaults = DefaultItems();
            foreach (var def in defaults)
            {
                if (!state.Items.Any(i => i.Id == def.Id))
                {
                    def.Order = state.Items.Count;
                    state.Items.Add(def);
                }
            }

            Items.Clear();
            foreach (var item in state.Items.OrderBy(i => i.Order)) Items.Add(item);
            Position = state.Position;

            NavigationChanged?.Invoke();
        }

        public async Task SaveAsync()
        {
            try
            {
                var state = new PersistedState { Items = Items.ToList(), Position = Position };
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_navFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося зберегти navigation.json: {ex.Message}");
            }
        }

        private static List<NavigationItemSettings> DefaultItems() => new()
        {
            new() { Id = "home", Title = "Home", Glyph = "\uE80F", PageTag = "home", Order = 0 },
            new() { Id = "instances", Title = "Instances", Glyph = "\uE8B7", PageTag = "instances", Order = 1 },
            new() { Id = "download_center", Title = "Завантаження", Glyph = "\uE896", PageTag = "download_center", Order = 2 },
            new() { Id = "marketplace", Title = "Marketplace", Glyph = "\uE719", PageTag = "marketplace", Order = 3 },
            new() { Id = "worlds", Title = "Світи (Saves)", Glyph = "\uE774", PageTag = "worlds", Order = 4 },
            new() { Id = "screenshots", Title = "Скріншоти", Glyph = "\uE722", PageTag = "screenshots", Order = 5 },
            new() { Id = "theme_editor", Title = "Редактор Тем", Glyph = "\uE790", PageTag = "theme_editor", Order = 6 },
            new() { Id = "update_center", Title = "Оновлення", Glyph = "\uE895", PageTag = "update_center", Order = 7 },
            new() { Id = "monitor", Title = "Resource Monitor", Glyph = "\uE9D9", PageTag = "monitor", Order = 8 },
            new() { Id = "logs", Title = "Console Logs", Glyph = "\uE8A5", PageTag = "logs", Order = 9 },
            new() { Id = "community_hub", Title = "Community Hub", Glyph = "\uE716", PageTag = "community_hub", Order = 10 },
        };
    }
}
