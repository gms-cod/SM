using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует зону Kaspersky (Red/Yellow/Green/Grey) в символ-индикатор.
/// Вход: строка с названием зоны.
/// Выход: символ (☠/⚠/✓/◌).
/// </summary>
public class KasperskyIndicatorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Определяем символ по названию зоны
        var zone = value?.ToString() ?? "";
        return zone switch
        {
            "Red" => "☠",
            "Yellow" => "⚠",
            "Green" => "✓",
            "Grey" => "◌",
            _ => ""
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует зону Kaspersky в цвет кисти.
/// Вход: строка с названием зоны.
/// Выход: SolidColorBrush (красный/жёлтый/зелёный/серый).
/// </summary>
public class KasperskyColorConverter : IValueConverter
{
    // Статические замороженные кисти: ноль аллокаций в Gen 0 при биндинге,
    // Frozen Freezable потокобезопасен и не требует Dispatcher.
    private static readonly SolidColorBrush RedBrush = CreateFrozen(0xE8, 0x11, 0x23);
    private static readonly SolidColorBrush YellowBrush = CreateFrozen(0xFF, 0xB9, 0x00);
    private static readonly SolidColorBrush GreenBrush = CreateFrozen(0x00, 0xCC, 0x6A);
    private static readonly SolidColorBrush GreyBrush = CreateFrozen(0x80, 0x80, 0x80);

    private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Выбираем готовую замороженную кисть по названию зоны (без new).
        var zone = value?.ToString() ?? "";
        return zone switch
        {
            "Red" => RedBrush,
            "Yellow" => YellowBrush,
            "Green" => GreenBrush,
            "Grey" => GreyBrush,
            _ => Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
