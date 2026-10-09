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
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
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

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
