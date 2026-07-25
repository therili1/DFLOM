using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Launcher.Models;

namespace Launcher.Services
{
    public class ThemeService : IThemeService
    {
        private readonly string _themeFile;
        private readonly Dictionary<string, ThemeSettings> _presets;
        private readonly IAnimationSettingsService _animationService;

        public ThemeSettings CurrentTheme { get; private set; } = new();
        public event Action? ThemeChanged;
        public IReadOnlyList<string> PresetNames { get; }

        public ThemeService(IAnimationSettingsService animationService)
        {
            _animationService = animationService;
            _animationService.SettingsChanged += () =>
            {
                // Рівень Glow живе в AnimationSettingsService (там само, де інша "інтенсивність
                // ефектів" зі списку кастомізації), але ВПЛИВАЄ саме на колір/яскравість -
                // тому будь-яка зміна рівня має одразу перезастосувати поточну тему.
                if (CurrentTheme != null) ApplyToResources(CurrentTheme);
            };

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseDirectory = Path.Combine(appData, ".lrs_launcher");
            Directory.CreateDirectory(baseDirectory);
            _themeFile = Path.Combine(baseDirectory, "theme.json");

            _presets = BuildPresets();
            PresetNames = new List<string>(_presets.Keys);
        }

        public ThemeSettings GetPreset(string name)
        {
            return _presets.TryGetValue(name, out var preset) ? preset.Clone() : _presets["Modern"].Clone();
        }

        public void ApplyTheme(ThemeSettings theme)
        {
            CurrentTheme = theme;
            ApplyToResources(theme);
            ThemeChanged?.Invoke();
        }

        public async Task LoadAsync()
        {
            ThemeSettings theme;
            try
            {
                if (File.Exists(_themeFile))
                {
                    var json = await File.ReadAllTextAsync(_themeFile);
                    theme = JsonSerializer.Deserialize<ThemeSettings>(json) ?? GetPreset("Modern");
                }
                else
                {
                    theme = GetPreset("Modern");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося завантажити theme.json: {ex.Message}");
                theme = GetPreset("Modern");
            }

            ApplyTheme(theme);
        }

        public async Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(CurrentTheme, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_themeFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не вдалося зберегти theme.json: {ex.Message}");
            }
        }

        /// <summary>Серце Theme Engine: переписує ключі в Application.Current.Resources.
        /// Оскільки всі сторінки лаунчера прив'язані до цих ключів через {ThemeResource ...},
        /// XAML-рушій сам перемальовує кожен відкритий елемент - перезапуск не потрібен.</summary>
        private void ApplyToResources(ThemeSettings theme)
        {
            var resources = Application.Current.Resources;

            var accent = ColorFromHex(theme.AccentColor);
            var background = ColorFromHex(theme.BackgroundColor);
            var glow = ColorFromHex(theme.GlowColor);
            var hover = ColorFromHex(theme.HoverColor);
            var border = ColorFromHex(theme.BorderColor);
            var card = ColorFromHex(theme.CardColor);
            var text = ColorFromHex(theme.TextColor);

            // --- Акцентний колір: перекриваємо системну "родину" акценту, від якої
            // залежить AccentButtonStyle і всі control-и, що використовують ThemeResource
            // SystemAccentColor / AccentFillColorDefaultBrush.
            resources["SystemAccentColor"] = accent;
            resources["SystemAccentColorLight1"] = Lighten(accent, 0.15);
            resources["SystemAccentColorLight2"] = Lighten(accent, 0.30);
            resources["SystemAccentColorLight3"] = Lighten(accent, 0.45);
            resources["SystemAccentColorDark1"] = Darken(accent, 0.15);
            resources["SystemAccentColorDark2"] = Darken(accent, 0.30);
            resources["SystemAccentColorDark3"] = Darken(accent, 0.45);

            resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(accent);
            resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Lighten(accent, 0.10));
            resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Darken(accent, 0.10));

            // --- Фон/картки/рамки/текст - основні поверхні, які використовує майже кожна сторінка.
            resources["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(background);
            resources["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(card);
            resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(border);
            resources["SystemControlPageTextBaseMediumBrush"] = new SolidColorBrush(text) { Opacity = 0.75 };
            resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(text);

            // --- Кастомні ключі під Glow/Hover - системних еквівалентів немає, тому
            // це власні ресурси. Кнопки/картки з ефектами світіння чи ховера (наступний
            // етап роботи над кастомізацією) прив'язуватимуться саме до них.
            // Інтенсивність (Off/Low/.../Ultra) керується окремо в AnimationSettingsService
            // (там-таки живе повзунок "Glow" зі списку візуальних ефектів) і масштабує
            // прозорість кольору світіння.
            double glowOpacity = _animationService.Glow switch
            {
                GlowLevel.Off => 0.0,
                GlowLevel.Low => 0.25,
                GlowLevel.Medium => 0.5,
                GlowLevel.High => 0.75,
                GlowLevel.Ultra => 1.0,
                _ => 0.5
            };
            resources["AppGlowBrush"] = new SolidColorBrush(glow) { Opacity = glowOpacity };
            resources["AppHoverBrush"] = new SolidColorBrush(hover);

            // --- Радіус закруглення вікна/контролів.
            var radius = new CornerRadius(theme.CornerRadius);
            resources["ControlCornerRadius"] = radius;
            resources["OverlayCornerRadius"] = radius;

            // --- Шрифт: перекриваємо дефолтний шрифт контролів, яким користується
            // більшість TextBlock/Button у застосунку, якщо не заданий свій FontFamily.
            if (!string.IsNullOrWhiteSpace(theme.FontFamily))
            {
                resources["ContentControlThemeFontFamily"] = new FontFamily(theme.FontFamily);
            }
        }

        private static Color ColorFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Colors.Gray;
            hex = hex.TrimStart('#');

            try
            {
                if (hex.Length == 6)
                {
                    byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
                    return Color.FromArgb(255, r, g, b);
                }
                if (hex.Length == 8)
                {
                    byte a = System.Convert.ToByte(hex.Substring(0, 2), 16);
                    byte r = System.Convert.ToByte(hex.Substring(2, 2), 16);
                    byte g = System.Convert.ToByte(hex.Substring(4, 2), 16);
                    byte b = System.Convert.ToByte(hex.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch
            {
                // Невалідний hex від користувача (наприклад, недописаний у TextBox) -
                // просто ігноруємо і лишаємо попередній колір, а не валимо застосунок.
            }

            return Colors.Gray;
        }

        private static Color Lighten(Color c, double amount)
        {
            byte Blend(byte channel) => (byte)(channel + (255 - channel) * amount);
            return Color.FromArgb(c.A, Blend(c.R), Blend(c.G), Blend(c.B));
        }

        private static Color Darken(Color c, double amount)
        {
            byte Blend(byte channel) => (byte)(channel * (1 - amount));
            return Color.FromArgb(c.A, Blend(c.R), Blend(c.G), Blend(c.B));
        }

        private static Dictionary<string, ThemeSettings> BuildPresets()
        {
            return new Dictionary<string, ThemeSettings>
            {
                ["Modern"] = new ThemeSettings
                {
                    ThemeName = "Modern",
                    AccentColor = "#0ea5e9",
                    BackgroundColor = "#090d16",
                    GlowColor = "#38bdf8",
                    HoverColor = "#1e293b",
                    BorderColor = "#243244",
                    CardColor = "#111827",
                    TextColor = "#f8fafc",
                    Opacity = 0.85,
                    Blur = 16,
                    CornerRadius = 8,
                    FontFamily = "Segoe UI"
                },
                ["Modern Glow"] = new ThemeSettings
                {
                    ThemeName = "Modern Glow",
                    AccentColor = "#a855f7",
                    BackgroundColor = "#0b0715",
                    GlowColor = "#d946ef",
                    HoverColor = "#2e1065",
                    BorderColor = "#3b1d63",
                    CardColor = "#150e26",
                    TextColor = "#f5f3ff",
                    Opacity = 0.80,
                    Blur = 24,
                    CornerRadius = 12,
                    FontFamily = "Segoe UI"
                },
                ["Fluent"] = new ThemeSettings
                {
                    ThemeName = "Fluent",
                    AccentColor = "#0078D4",
                    BackgroundColor = "#1f1f1f",
                    GlowColor = "#60cdff",
                    HoverColor = "#2b2b2b",
                    BorderColor = "#3a3a3a",
                    CardColor = "#272727",
                    TextColor = "#ffffff",
                    Opacity = 0.90,
                    Blur = 20,
                    CornerRadius = 8,
                    FontFamily = "Segoe UI Variable"
                },
                ["Glass"] = new ThemeSettings
                {
                    ThemeName = "Glass",
                    AccentColor = "#14b8a6",
                    BackgroundColor = "#0a1210",
                    GlowColor = "#2dd4bf",
                    HoverColor = "#134e4a",
                    BorderColor = "#1f4d47",
                    CardColor = "#0f1f1c",
                    TextColor = "#ecfdf5",
                    Opacity = 0.55,
                    Blur = 32,
                    CornerRadius = 16,
                    FontFamily = "Segoe UI"
                },
                ["Minimal"] = new ThemeSettings
                {
                    ThemeName = "Minimal",
                    AccentColor = "#404040",
                    BackgroundColor = "#fafafa",
                    GlowColor = "#a3a3a3",
                    HoverColor = "#e5e5e5",
                    BorderColor = "#d4d4d4",
                    CardColor = "#ffffff",
                    TextColor = "#171717",
                    Opacity = 1.0,
                    Blur = 0,
                    CornerRadius = 2,
                    FontFamily = "Segoe UI"
                },
                ["Dark"] = new ThemeSettings
                {
                    ThemeName = "Dark",
                    AccentColor = "#6366f1",
                    BackgroundColor = "#0f0f10",
                    GlowColor = "#818cf8",
                    HoverColor = "#27272a",
                    BorderColor = "#3f3f46",
                    CardColor = "#18181b",
                    TextColor = "#fafafa",
                    Opacity = 1.0,
                    Blur = 8,
                    CornerRadius = 6,
                    FontFamily = "Segoe UI"
                },
                ["Light"] = new ThemeSettings
                {
                    ThemeName = "Light",
                    AccentColor = "#2563eb",
                    BackgroundColor = "#f8fafc",
                    GlowColor = "#60a5fa",
                    HoverColor = "#e2e8f0",
                    BorderColor = "#cbd5e1",
                    CardColor = "#ffffff",
                    TextColor = "#0f172a",
                    Opacity = 1.0,
                    Blur = 8,
                    CornerRadius = 8,
                    FontFamily = "Segoe UI"
                },
                ["Minecraft Theme"] = new ThemeSettings
                {
                    ThemeName = "Minecraft Theme",
                    AccentColor = "#5b8731",
                    BackgroundColor = "#1a1310",
                    GlowColor = "#8bc34a",
                    HoverColor = "#3e2f22",
                    BorderColor = "#4a3728",
                    CardColor = "#2b2117",
                    TextColor = "#f1f1f1",
                    Opacity = 0.92,
                    Blur = 4,
                    CornerRadius = 0,
                    FontFamily = "Minecraft Seven"
                }
            };
        }
    }
}
