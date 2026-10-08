using System.Globalization;
using System.Windows.Data;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryStatus в текст на кнопке переключения статуса.
/// Вход: EntryStatus.
/// Выход: локализованная строка (например "Выключить", "Включить", "Разблокировать").
/// </summary>
public class StatusToToggleTextConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Выбираем текст действия в зависимости от текущего статуса
        if (value is EntryStatus s)
            return s switch
            {
                EntryStatus.Enabled => LocalizationService.Instance.ToggleDisable,
                EntryStatus.Disabled => LocalizationService.Instance.ToggleEnable,
                EntryStatus.Blocked => LocalizationService.Instance.ToggleUnblock,
                _ => LocalizationService.Instance.ToggleEnable,
            };
        return LocalizationService.Instance.ToggleEnable;
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
