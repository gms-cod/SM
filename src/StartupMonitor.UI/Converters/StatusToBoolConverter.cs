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
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Только Enabled возвращает true
        if (value is EntryStatus status)
            return status == EntryStatus.Enabled;
        return false;
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // true → Enabled, false → Disabled
        if (value is bool isChecked)
            return isChecked ? EntryStatus.Enabled : EntryStatus.Disabled;
        return EntryStatus.Disabled;
    }
}
