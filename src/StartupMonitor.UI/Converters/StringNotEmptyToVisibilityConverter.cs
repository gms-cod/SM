using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StartupMonitor.UI.Converters;

/// <summary>Конвертирует непустую строку в Visible (иначе Collapsed).
/// Используется для кнопки очистки строки поиска.</summary>
public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}