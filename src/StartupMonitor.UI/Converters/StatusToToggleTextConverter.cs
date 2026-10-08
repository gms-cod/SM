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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
