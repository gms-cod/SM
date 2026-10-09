using System.Globalization;
using System.Windows.Data;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует AI-рейтинг (1–10) в строку звёзд (★/☆).
/// Вход: int score (1–10).
/// Выход: строка из 5 символов (★/☆), например score=1 → "★★★★★", score=10 → "★☆☆☆☆".
/// </summary>
public class RatingToStarsConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Конвертируем score (1-10) в количество закрашенных звёзд (1-5)
        if (value is int score && score > 0 && score <= 10)
        {
            var stars = ScoreToStars(score);
            return new string('★', stars) + new string('☆', 5 - stars);
        }
        return "☆☆☆☆☆";
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    // ponytail: score→stars mapping — 1→5★ зелёных (хорошо), 10→1★ красных (плохо)
    /// <summary>ScoreToStars: Выполняет операцию ScoreToStars.</summary>
    /// <param name="score">Входные данные: значение параметра score (int).</param>
    /// <returns>Выходные данные: результирующее значение типа int.</returns>
    public static int ScoreToStars(int score) => score switch
    {
        1 => 5,
        <= 3 => 4,
        <= 5 => 3,
        <= 7 => 2,
        _ => 1
    };
}

/// <summary>
/// Конвертирует AI-рейтинг (1–10) в цвет кисти (зелёный → красный).
/// Вход: int score (1–10).
/// Выход: SolidColorBrush (зелёный для 5★, красный для 1★).
/// </summary>
public class RatingToColorConverter : IValueConverter
{
    private static System.Windows.Media.SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly System.Windows.Media.SolidColorBrush DefaultBrush = CreateFrozen(0x94, 0xA3, 0xB8);

    private static readonly System.Windows.Media.SolidColorBrush Stars5Light = CreateFrozen(0x05, 0x96, 0x69);
    private static readonly System.Windows.Media.SolidColorBrush Stars5Dark = CreateFrozen(0x00, 0xCC, 0x6A);
    private static readonly System.Windows.Media.SolidColorBrush Stars4Light = CreateFrozen(0x16, 0xA3, 0x4A);
    private static readonly System.Windows.Media.SolidColorBrush Stars4Dark = CreateFrozen(0x8B, 0xC3, 0x4A);
    private static readonly System.Windows.Media.SolidColorBrush Stars3Light = CreateFrozen(0xD9, 0x77, 0x06);
    private static readonly System.Windows.Media.SolidColorBrush Stars3Dark = CreateFrozen(0xFF, 0x8C, 0x00);
    private static readonly System.Windows.Media.SolidColorBrush Stars2Light = CreateFrozen(0xEA, 0x58, 0x0C);
    private static readonly System.Windows.Media.SolidColorBrush Stars2Dark = CreateFrozen(0xF0, 0x50, 0x20);
    private static readonly System.Windows.Media.SolidColorBrush Stars1Light = CreateFrozen(0xDC, 0x26, 0x26);
    private static readonly System.Windows.Media.SolidColorBrush Stars1Dark = CreateFrozen(0xE8, 0x11, 0x23);

    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если score невалидный — аккуратный серый цвет
        if (value is not int score || score <= 0)
            return DefaultBrush;

        var isLight = Services.ThemeService.CurrentTheme == Wpf.Ui.Appearance.ApplicationTheme.Light;
        var stars = RatingToStarsConverter.ScoreToStars(score);

        // Цвет от зелёного (5★) до красного (1★)
        return stars switch
        {
            5 => isLight ? Stars5Light : Stars5Dark,
            4 => isLight ? Stars4Light : Stars4Dark,
            3 => isLight ? Stars3Light : Stars3Dark,
            2 => isLight ? Stars2Light : Stars2Dark,
            _ => isLight ? Stars1Light : Stars1Dark
        };
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует AI-рейтинг (1–10) в текст тултипа.
/// Вход: int score (1–10).
/// Выход: строка "Score: X/10" или "No rating".
/// </summary>
public class RatingToTooltipConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если score невалидный — "No rating"
        if (value is not int score || score <= 0)
            return "No rating";

        return $"Score: {score}/10";
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
