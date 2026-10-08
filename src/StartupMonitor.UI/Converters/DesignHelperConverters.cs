using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Converters;

/// <summary>Конвертер имени издателя для бейджа.</summary>
public class PublisherBadgeConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var publisher = value as string;
        if (!string.IsNullOrWhiteSpace(publisher))
        {
            return publisher;
        }
        return LocalizationService.Instance["PublisherUnverified"];
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертер влияния на запуск (Boot Impact): "Низкое", "Среднее", "Высокое".</summary>
public class LocationToImpactTextConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EntryLocation loc) return LocalizationService.Instance["ImpactLow"];

        return loc switch
        {
            EntryLocation.BootExecute or EntryLocation.AppInitDll or EntryLocation.Driver
                or EntryLocation.ImageFileExecutionOptions or EntryLocation.Drivers32 => LocalizationService.Instance["ImpactHigh"],

            EntryLocation.Service or EntryLocation.Winlogon or EntryLocation.TaskScheduler
                or EntryLocation.WmiEventSubscription or EntryLocation.ActiveSetup => LocalizationService.Instance["ImpactMedium"],

            _ => LocalizationService.Instance["ImpactLow"]
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертер цвета фона бейджа влияния на запуск.</summary>
public class LocationToImpactBackgroundConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EntryLocation loc)
            return Application.Current?.Resources["BadgeGreenBackgroundBrush"] as Brush ?? Brushes.Transparent;

        return loc switch
        {
            EntryLocation.BootExecute or EntryLocation.AppInitDll or EntryLocation.Driver
                or EntryLocation.ImageFileExecutionOptions or EntryLocation.Drivers32
                => Application.Current?.Resources["BadgeRedBackgroundBrush"] as Brush ?? Brushes.Transparent,

            EntryLocation.Service or EntryLocation.Winlogon or EntryLocation.TaskScheduler
                or EntryLocation.WmiEventSubscription or EntryLocation.ActiveSetup
                => Application.Current?.Resources["BadgeOrangeBackgroundBrush"] as Brush ?? Brushes.Transparent,

            _ => Application.Current?.Resources["BadgeGreenBackgroundBrush"] as Brush ?? Brushes.Transparent
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертер цвета текста бейджа влияния на запуск.</summary>
public class LocationToImpactForegroundConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EntryLocation loc)
            return Application.Current?.Resources["BadgeGreenTextBrush"] as Brush ?? Brushes.LimeGreen;

        return loc switch
        {
            EntryLocation.BootExecute or EntryLocation.AppInitDll or EntryLocation.Driver
                or EntryLocation.ImageFileExecutionOptions or EntryLocation.Drivers32
                => Application.Current?.Resources["BadgeRedTextBrush"] as Brush ?? Brushes.Crimson,

            EntryLocation.Service or EntryLocation.Winlogon or EntryLocation.TaskScheduler
                or EntryLocation.WmiEventSubscription or EntryLocation.ActiveSetup
                => Application.Current?.Resources["BadgeOrangeTextBrush"] as Brush ?? Brushes.DarkOrange,

            _ => Application.Current?.Resources["BadgeGreenTextBrush"] as Brush ?? Brushes.LimeGreen
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертер цвета границы бейджа влияния на запуск.</summary>
public class LocationToImpactBorderConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EntryLocation loc)
            return Application.Current?.Resources["BadgeGreenBorderBrush"] as Brush ?? Brushes.Transparent;

        return loc switch
        {
            EntryLocation.BootExecute or EntryLocation.AppInitDll or EntryLocation.Driver
                or EntryLocation.ImageFileExecutionOptions or EntryLocation.Drivers32
                => Application.Current?.Resources["BadgeRedBorderBrush"] as Brush ?? Brushes.Transparent,

            EntryLocation.Service or EntryLocation.Winlogon or EntryLocation.TaskScheduler
                or EntryLocation.WmiEventSubscription or EntryLocation.ActiveSetup
                => Application.Current?.Resources["BadgeOrangeBorderBrush"] as Brush ?? Brushes.Transparent,

            _ => Application.Current?.Resources["BadgeGreenBorderBrush"] as Brush ?? Brushes.Transparent
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Общий кэш замороженных кистей для строковых конвертеров.
/// Frozen Freezable потокобезопасен: кисть создаётся один раз,
/// замораживается и без аллокаций переиспользуется рендерером DirectX
/// из любого потока без межпоточных блокировок Dispatcher.
/// </summary>
internal static class FrozenBrushCache
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SolidColorBrush> _cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxEntries = 512;

    /// <summary>GetOrCreate: Возвращает OrCreate.</summary>
    /// <param name="key">Входные данные: значение параметра key (string).</param>
    /// <param name="colorFactory">Входные данные: значение параметра colorFactory (Func<Color>).</param>
    /// <returns>Выходные данные: результирующее значение типа SolidColorBrush.</returns>
    public static SolidColorBrush GetOrCreate(string key, Func<Color> colorFactory)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var brush = new SolidColorBrush(colorFactory());
        if (brush.CanFreeze)
            brush.Freeze();

        // Защита от unbounded growth: при переполнении отдаём кисть без кэширования.
        if (_cache.Count >= MaxEntries)
            return brush;

        return _cache.GetOrAdd(key, brush);
    }
}

/// <summary>Конвертер строки цвета (имя / #RRGGBB / #AARRGGBB) в SolidColorBrush с опциональной прозрачностью.</summary>
public class StringToBrushConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => HexToBrushConverter.ConvertCore(value as string, parameter);

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертер HEX-строки цвета (#RRGGBB) в SolidColorBrush с опциональной прозрачностью.</summary>
public class HexToBrushConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConvertCore(value as string, parameter);

    /// <summary>ConvertCore: Выполняет преобразование данных Core.</summary>
    /// <param name="hex">Входные данные: значение параметра hex (string?).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    internal static object ConvertCore(string? hex, object? parameter)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                string trimmed = hex.Trim();
                double opacity = 1.0;
                bool hasOpacity = parameter != null
                    && double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out opacity);
                if (hasOpacity)
                    opacity = Math.Clamp(opacity, 0.0, 1.0);

                // Канонический ключ: нормализованный hex + округлённая прозрачность.
                string key = hasOpacity
                    ? $"{trimmed}|{opacity:F3}"
                    : trimmed;

                return FrozenBrushCache.GetOrCreate(key, () =>
                {
                    var color = (Color)ColorConverter.ConvertFromString(trimmed)!;
                    if (hasOpacity && opacity < 1.0)
                    {
                        byte alpha = (byte)Math.Round(opacity * 255.0);
                        color = Color.FromArgb(alpha, color.R, color.G, color.B);
                    }
                    return color;
                });
            }
            catch
            {
                // Игнорируем ошибки парсинга невалидного цвета
            }
        }
        return Brushes.Transparent;
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

