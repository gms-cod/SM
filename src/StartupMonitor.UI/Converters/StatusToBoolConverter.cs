using System.Globalization;
using System.Windows.Data;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryStatus в bool (Enabled ↔ true) и обратно.
/// Прямое: Enabled → true, иначе false.
/// Обратное: true → Enabled, false → Disabled.
/// </summary>
public class StatusToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Только Enabled возвращает true
        if (value is EntryStatus status)
            return status == EntryStatus.Enabled;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // true → Enabled, false → Disabled
        if (value is bool isChecked)
            return isChecked ? EntryStatus.Enabled : EntryStatus.Disabled;
        return EntryStatus.Disabled;
    }
}
