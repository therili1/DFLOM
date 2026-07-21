using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Launcher.Converters
{
    /// <summary>value.ToString() == parameter -> Visible, інакше Collapsed.</summary>
    public class StringEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var valueStr = value?.ToString() ?? string.Empty;
            var paramStr = parameter?.ToString() ?? string.Empty;
            return string.Equals(valueStr, paramStr, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>value.ToString() != parameter -> Visible, інакше Collapsed.</summary>
    public class StringNotEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var valueStr = value?.ToString() ?? string.Empty;
            var paramStr = parameter?.ToString() ?? string.Empty;
            return string.Equals(valueStr, paramStr, StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return !b;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isNull = value == null;
            bool invert = parameter as string == "Invert";
            bool visible = invert ? isNull : !isNull;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class RamMbConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int ram)
            {
                return $"{ram / 1024.0:F1} GB";
            }
            if (value is double d)
            {
                return $"{d / 1024.0:F1} GB";
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class IntToDownloadsStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int i)
            {
                if (i >= 1000000) return $"{i / 1000000.0:F1}M завантажень";
                if (i >= 1000) return $"{i / 1000.0:F1}K завантажень";
                return $"{i} завантажень";
            }
            if (value is long l)
            {
                if (l >= 1000000) return $"{l / 1000000.0:F1}M завантажень";
                if (l >= 1000) return $"{l / 1000.0:F1}K завантажень";
                return $"{l} завантажень";
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class PercentDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                return $"{d:F1}%";
            }
            return "0.0%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class MbDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                if (d >= 1024) return $"{d / 1024.0:F2} GB";
                return $"{d:F0} MB";
            }
            return "0 MB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class CelsiusDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                return $"{d:F1}°C";
            }
            return "0.0°C";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class StringFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (parameter is string format)
            {
                return string.Format(format, value);
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class DateTimeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dt)
            {
                return dt.ToString("dd.MM.yyyy HH:mm");
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isEmpty = string.IsNullOrEmpty(value as string);
            bool invert = parameter as string == "Invert";
            bool visible = invert ? isEmpty : !isEmpty;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>Шлях до файлу на диску -> BitmapImage для &lt;Image Source=.../&gt;.</summary>
    public class PathToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path && !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>int &gt; 0 -> Visible, інакше Collapsed (для показу мініатюри лише коли скріншоти є).</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is int count && count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>int == 0 -> Visible, інакше Collapsed (точна інверсія CountToVisibilityConverter -
    /// для показу заглушки "немає скріншотів/датапаків" саме коли лічильник нульовий).</summary>
    public class ZeroCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is int count && count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>MinecraftInstance -> кількість .jar у теці mods цього профілю (для картки Grid View).</summary>
    public class ModCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Launcher.Models.MinecraftInstance instance)
            {
                var store = Launcher.App.GetService<Launcher.Services.IInstanceStore>();
                return $"{store.GetModCount(instance)} модів";
            }
            return "0 модів";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>MinecraftInstance -> кількість світів у теці saves цього профілю (для картки Grid View).</summary>
    public class WorldCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Launcher.Models.MinecraftInstance instance)
            {
                var store = Launcher.App.GetService<Launcher.Services.IInstanceStore>();
                return $"{store.GetWorldCount(instance)} світів";
            }
            return "0 світів";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>Назва лоадера -> дефолтна іконка-емодзі, коли CustomIcon не вибрано.</summary>
    public class LoaderIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value as string) switch
            {
                "Fabric" => "🧵",
                "Forge" => "🔨",
                "NeoForge" => "🔥",
                "Quilt" => "🧶",
                _ => "📦"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>DateTime? -> "Ще не запускався" або дата останнього запуску.</summary>
    public class NullableDateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dt) return dt.ToString("dd.MM.yyyy HH:mm");
            return "Ще не запускався";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>"Small"/"Medium"/"Large" -> ширина картки в пікселях для GridView.ItemWidth.</summary>
    public class CardSizeToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value as string) switch
            {
                "Small" => 180.0,
                "Large" => 320.0,
                _ => 240.0
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>Будь-який enum -> його .ToString() - явний конвертер, бо x:Bind не робить
    /// це неявно для властивостей типу string (Category/Status у DownloadTask).</summary>
    public class EnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) => value?.ToString() ?? string.Empty;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    /// <summary>DownloadTask -> "43.2% · ETA 00:12 · 1.4 MB/s" - один рядок замість
    /// кількох Run-біндингів з типами, що не конвертуються неявно (TimeSpan/double -> string).</summary>
    public class DownloadStatusLineConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not Launcher.Services.DownloadTask task) return string.Empty;

            string speedStr = task.Speed >= 1024 * 1024
                ? $"{task.Speed / (1024 * 1024):F1} MB/s"
                : $"{task.Speed / 1024:F0} KB/s";

            string etaStr = task.Eta.TotalHours >= 1
                ? task.Eta.ToString(@"h\:mm\:ss")
                : task.Eta.ToString(@"mm\:ss");

            return $"{task.Progress:F1}% · ETA {etaStr} · {speedStr}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
