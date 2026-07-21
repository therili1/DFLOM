using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Launcher.Services
{
    public class AnimationSettingsService : IAnimationSettingsService
    {
        private class PersistedState
        {
            public bool AnimationsEnabled { get; set; } = true;
            public double AnimationSpeed { get; set; } = 1.0;
            public GlowLevel Glow { get; set; } = GlowLevel.Medium;
        }

        private readonly string _file;
        private PersistedState _state = new();

        public bool AnimationsEnabled => _state.AnimationsEnabled;
        public double AnimationSpeed => _state.AnimationSpeed;
        public GlowLevel Glow => _state.Glow;
        public event Action? SettingsChanged;

        public AnimationSettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseDirectory = Path.Combine(appData, ".lrs_launcher");
            Directory.CreateDirectory(baseDirectory);
            _file = Path.Combine(baseDirectory, "animations.json");
        }

        public void SetAnimationsEnabled(bool enabled)
        {
            _state.AnimationsEnabled = enabled;
            SettingsChanged?.Invoke();
            _ = SaveAsync();
        }

        public void SetAnimationSpeed(double speed)
        {
            _state.AnimationSpeed = Math.Clamp(speed, 0.25, 3.0);
            SettingsChanged?.Invoke();
            _ = SaveAsync();
        }

        public void SetGlow(GlowLevel level)
        {
            _state.Glow = level;
            SettingsChanged?.Invoke();
            _ = SaveAsync();
        }

        public TimeSpan GetDuration(TimeSpan baseDuration)
        {
            if (!_state.AnimationsEnabled) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(baseDuration.TotalMilliseconds * _state.AnimationSpeed);
        }

        public async Task LoadAsync()
        {
            try
            {
                if (File.Exists(_file))
                {
                    var json = await File.ReadAllTextAsync(_file);
                    _state = JsonSerializer.Deserialize<PersistedState>(json) ?? new PersistedState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося завантажити animations.json: {ex.Message}");
                _state = new PersistedState();
            }

            SettingsChanged?.Invoke();
        }

        public async Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_file, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося зберегти animations.json: {ex.Message}");
            }
        }
    }
}
