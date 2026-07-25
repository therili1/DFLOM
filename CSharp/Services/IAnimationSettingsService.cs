using System;
using System.Threading.Tasks;

namespace Launcher.Services
{
    public enum GlowLevel { Off, Low, Medium, High, Ultra }

    public interface IAnimationSettingsService
    {
        bool AnimationsEnabled { get; }
        /// <summary>Множник тривалості: 1.0 = звичайна швидкість, менше = швидше.</summary>
        double AnimationSpeed { get; }
        GlowLevel Glow { get; }

        event Action? SettingsChanged;

        void SetAnimationsEnabled(bool enabled);
        void SetAnimationSpeed(double speed);
        void SetGlow(GlowLevel level);

        /// <summary>Тривалість "стандартної" анімації (200мс) із урахуванням Enabled/Speed -
        /// зручний хелпер, яким мають користуватись усі кастомні анімовані контролі,
        /// щоб не дублювати цю логіку по кожному месцю.</summary>
        TimeSpan GetDuration(TimeSpan baseDuration);

        Task LoadAsync();
        Task SaveAsync();
    }
}
