using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Models;
using Launcher.Services;

namespace Launcher.ViewModels
{
    public partial class ThemeEditorViewModel : ObservableObject
    {
        private readonly IThemeService _themeService;

        // Прапорець, щоб під час програмного завантаження пресету/збереженої теми
        // OnXChanged-хендлери не почали одне за одним застосовувати "часткову" тему
        // (наприклад, новий AccentColor разом зі старим BackgroundColor) і не позначили
        // пресет як "Custom" ще до того, як усі поля встигли оновитись.
        private bool _isLoadingPreset;

        public IReadOnlyList<string> PresetNames => _themeService.PresetNames;

        [ObservableProperty]
        private string _activeThemeName = "Modern";

        [ObservableProperty]
        private string _accentColor = "#0ea5e9";

        [ObservableProperty]
        private string _backgroundColor = "#090d16";

        [ObservableProperty]
        private string _glowColor = "#38bdf8";

        [ObservableProperty]
        private string _hoverColor = "#1e293b";

        [ObservableProperty]
        private string _borderColor = "#243244";

        [ObservableProperty]
        private string _cardColor = "#111827";

        [ObservableProperty]
        private string _textColor = "#f8fafc";

        [ObservableProperty]
        private string _backgroundUrl = string.Empty;

        [ObservableProperty]
        private double _opacity = 0.85;

        [ObservableProperty]
        private double _blur = 16.0;

        [ObservableProperty]
        private double _cornerRadius = 8.0;

        [ObservableProperty]
        private string _fontFamily = "Segoe UI";

        // Гарантовано встановлений у Windows. Решта (Inter/Roboto/JetBrains Mono/Minecraft Seven)
        // НЕ вшиті в застосунок як файли шрифтів - якщо їх немає в системі, WinUI мовчки
        // відкотиться на дефолтний шрифт. Чесно позначаємо це в UI (див. XAML), а не прикидаємось,
        // що вони гарантовано спрацюють як Segoe UI.
        public IReadOnlyList<string> AvailableFonts { get; } = new[]
        {
            "Segoe UI",
            "Segoe UI Variable",
            "Inter",
            "Roboto",
            "JetBrains Mono",
            "Minecraft Seven",
            "Custom"
        };

        /// <summary>Чи поточний FontFamily не входить у список іменованих пресетів шрифту -
        /// показує вільне текстове поле для введення будь-якого шрифту, встановленого в системі.</summary>
        public bool IsCustomFont => !AvailableFonts.Contains(FontFamily) || FontFamily == "Custom";

        /// <summary>Значення для ComboBox: якщо поточний шрифт не з відомого списку - показуємо "Custom".</summary>
        public string SelectedFontOption
        {
            get => AvailableFonts.Contains(FontFamily) ? FontFamily : "Custom";
            set
            {
                if (value == "Custom")
                {
                    // Не чіпаємо FontFamily одразу - чекаємо, поки юзер щось введе у вільне поле,
                    // інакше миттєво застосується буквальний рядок "Custom" як назва шрифту.
                    OnPropertyChanged(nameof(IsCustomFont));
                }
                else
                {
                    FontFamily = value;
                }
            }
        }

        [ObservableProperty]
        private bool _hasUnsavedChanges;

        public ThemeEditorViewModel()
        {
            _themeService = App.GetService<IThemeService>();
            LoadFrom(_themeService.CurrentTheme);
        }

        [RelayCommand]
        public void ApplyPreset(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return;
            LoadFrom(_themeService.GetPreset(presetName));
            ApplyLive();
            HasUnsavedChanges = true;
        }

        [RelayCommand]
        public void ResetToDefaults() => ApplyPreset("Modern");

        [RelayCommand]
        public async Task SaveThemeAsync()
        {
            await _themeService.SaveAsync();
            HasUnsavedChanges = false;
        }

        private void LoadFrom(ThemeSettings theme)
        {
            _isLoadingPreset = true;

            ActiveThemeName = theme.ThemeName;
            AccentColor = theme.AccentColor;
            BackgroundColor = theme.BackgroundColor;
            GlowColor = theme.GlowColor;
            HoverColor = theme.HoverColor;
            BorderColor = theme.BorderColor;
            CardColor = theme.CardColor;
            TextColor = theme.TextColor;
            BackgroundUrl = theme.BackgroundUrl;
            Opacity = theme.Opacity;
            Blur = theme.Blur;
            CornerRadius = theme.CornerRadius;
            FontFamily = theme.FontFamily;

            _isLoadingPreset = false;
        }

        /// <summary>Збирає поточні поля ViewModel у ThemeSettings і штовхає в ThemeService,
        /// який миттєво перефарбовує весь застосунок. Викликається з кожного OnXChanged.</summary>
        private void ApplyLive()
        {
            if (_isLoadingPreset) return;

            var theme = new ThemeSettings
            {
                ThemeName = ActiveThemeName,
                AccentColor = AccentColor,
                BackgroundColor = BackgroundColor,
                GlowColor = GlowColor,
                HoverColor = HoverColor,
                BorderColor = BorderColor,
                CardColor = CardColor,
                TextColor = TextColor,
                BackgroundUrl = BackgroundUrl,
                Opacity = Opacity,
                Blur = Blur,
                CornerRadius = CornerRadius,
                FontFamily = FontFamily
            };

            _themeService.ApplyTheme(theme);
        }

        // Будь-яка ручна зміна поля (не через ApplyPreset) означає, що це вже не
        // "чистий" пресет - позначаємо тему як Custom, щоб у галереї не лишалась
        // хибно підсвіченою вибрана раніше картка пресету.
        partial void OnAccentColorChanged(string value) => MarkCustomAndApply();
        partial void OnBackgroundColorChanged(string value) => MarkCustomAndApply();
        partial void OnGlowColorChanged(string value) => MarkCustomAndApply();
        partial void OnHoverColorChanged(string value) => MarkCustomAndApply();
        partial void OnBorderColorChanged(string value) => MarkCustomAndApply();
        partial void OnCardColorChanged(string value) => MarkCustomAndApply();
        partial void OnTextColorChanged(string value) => MarkCustomAndApply();
        partial void OnBackgroundUrlChanged(string value) => MarkCustomAndApply();
        partial void OnOpacityChanged(double value) => MarkCustomAndApply();
        partial void OnBlurChanged(double value) => MarkCustomAndApply();
        partial void OnCornerRadiusChanged(double value) => MarkCustomAndApply();

        /// <summary>x:Bind не робить неявних перетворень типів (на відміну від звичайного Binding) -
        /// CornerRadius у ViewModel лишається double (бо Slider працює саме з double), а
        /// Border.CornerRadius в XAML очікує структуру Microsoft.UI.Xaml.CornerRadius.
        /// Ця функція - явний міст між ними для function-binding у XAML.</summary>
        public Microsoft.UI.Xaml.CornerRadius ToCornerRadius(double value) => new(value);
        partial void OnFontFamilyChanged(string value)
        {
            OnPropertyChanged(nameof(SelectedFontOption));
            OnPropertyChanged(nameof(IsCustomFont));
            MarkCustomAndApply();
        }

        private void MarkCustomAndApply()
        {
            if (_isLoadingPreset) return;
            ActiveThemeName = "Custom";
            HasUnsavedChanges = true;
            ApplyLive();
        }
    }
}
