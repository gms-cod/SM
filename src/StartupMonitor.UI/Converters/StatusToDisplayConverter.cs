using System.Globalization;
using System.Windows.Data;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryStatus в краткую локализованную строку для отображения.
/// Вход: EntryStatus.
/// Выход: локализованная строка (например "Вкл", "Выкл", "Заблокировано").
/// </summary>
public class StatusToDisplayConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Выбираем локализованную строку в зависимости от статуса
        if (value is EntryStatus status)
            return status switch
            {
                EntryStatus.Enabled => LocalizationService.Instance.StatusEnabledShort,
                EntryStatus.Disabled => LocalizationService.Instance.StatusDisabledShort,
                EntryStatus.Blocked => LocalizationService.Instance.StatusBlockedShort,
                EntryStatus.Delayed => LocalizationService.Instance.StatusDelayedShort,
                _ => "",
            };
        return "";
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
