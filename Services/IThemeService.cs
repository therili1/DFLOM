using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Launcher.Models;

namespace Launcher.Services
{
    public interface IThemeService
    {
        ThemeSettings CurrentTheme { get; }

        /// <summary>Спрацьовує щоразу, коли тема застосована (пресет обрано або будь-яке
        /// поле CurrentTheme змінено) - Views/ViewModels можуть підписатись, щоб оновити
        /// власні прив'язки, що не покриваються глобальними ресурсами.</summary>
        event Action? ThemeChanged;

        /// <summary>Назви всіх вбудованих пресетів у порядку показу в галереї.</summary>
        IReadOnlyList<string> PresetNames { get; }

        ThemeSettings GetPreset(string name);

        /// <summary>Застосовує тему одразу до Application.Resources (усі відкриті сторінки
        /// перемальовуються миттєво, без перезапуску) і робить її поточною. Не зберігає на диск.</summary>
        void ApplyTheme(ThemeSettings theme);

        /// <summary>Завантажує востаннє збережену тему з диску (або пресет "Modern", якщо
        /// це перший запуск) і одразу застосовує її. Викликається один раз при старті застосунку,
        /// до показу MainWindow, щоб не було "спалаху" дефолтних кольорів.</summary>
        Task LoadAsync();

        /// <summary>Записує CurrentTheme на диск як тему, яка буде застосована при наступному запуску.</summary>
        Task SaveAsync();
    }
}
