using System.Globalization;
using System.Windows;
using System.Windows.Data;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryStatus в Visibility. Параметр задаёт целевой статус для отображения.
/// Вход: value = EntryStatus, parameter = строка с именем статуса (например "Blocked") или "!Disabled" для инверсии.
/// Выход: Visible если статус совпадает (с учётом инверсии), иначе Collapsed.
/// </summary>
public class StatusToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Сравниваем статус с целевым значением из parameter, поддерживаем инверсию через "!"
        if (value is EntryStatus status && parameter is string raw)
        {
            var invert = raw.StartsWith('!');
            var targetStr = invert ? raw[1..] : raw;
            if (Enum.TryParse<EntryStatus>(targetStr, out var target))
            {
                var match = status == target;
                return (invert ? !match : match) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
