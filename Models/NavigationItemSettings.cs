namespace Launcher.Models
{
    /// <summary>Один пункт бічного меню. Content/Icon/Tag - те саме, що раніше було
    /// захардкоджено в MainWindow.xaml як NavigationViewItem; тепер це дані, якими можна
    /// керувати з UI (приховати, переставити місцями, позначити як улюблене) і які
    /// зберігаються між запусками.</summary>
    public class NavigationItemSettings
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        /// <summary>Код гліфа Segoe Fluent Icons, напр. "\uE80F".</summary>
        public string Glyph { get; set; } = string.Empty;
        /// <summary>Той самий рядок, що раніше йшов у switch у MainWindow.xaml.cs для вибору сторінки.</summary>
        public string PageTag { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
        public bool IsFavorite { get; set; }
        public int Order { get; set; }
    }
}
