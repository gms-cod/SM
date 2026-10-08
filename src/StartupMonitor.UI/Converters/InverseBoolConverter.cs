using System;
using System.Globalization;
using System.Windows.Data;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Инвертирует логическое значение (true -> false, false -> true).
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}

