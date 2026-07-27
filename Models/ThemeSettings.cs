using System;

namespace Launcher.Models
{
    /// <summary>
    /// Повний набір параметрів однієї теми лаунчера. Один об'єкт ThemeSettings
    /// описує вигляд усього застосунку - і вбудовані пресети (Modern, Glass, ...),
    /// і кастомну тему користувача, зібрану вручну в Редакторі Тем.
    /// </summary>
    public class ThemeSettings
    {
        /// <summary>Назва пресету ("Modern", "Glass", ...) або "Custom", якщо користувач
        /// відхилився від пресету, змінивши хоча б один колір/повзунок вручну.</summary>
        public string ThemeName { get; set; } = "Modern";

        // --- Кольори (усі застосовуються миттєво, без перезапуску) ---
        public string AccentColor { get; set; } = "#0ea5e9";
        public string BackgroundColor { get; set; } = "#090d16";
        public string GlowColor { get; set; } = "#38bdf8";
        public string HoverColor { get; set; } = "#1e293b";
        public string BorderColor { get; set; } = "#243244";
        public string CardColor { get; set; } = "#111827";
        public string TextColor { get; set; } = "#f8fafc";

        // --- Фон ---
        public string BackgroundUrl { get; set; } = string.Empty;
        public double Opacity { get; set; } = 0.85;
        public double Blur { get; set; } = 16.0;

        // --- Вікно ---
        public double CornerRadius { get; set; } = 8.0;

        // --- Шрифт ---
        public string FontFamily { get; set; } = "Segoe UI";

        public ThemeSettings Clone() => (ThemeSettings)MemberwiseClone();
    }
}
