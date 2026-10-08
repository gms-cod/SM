using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryStatus в цвет кисти для отображения статуса.
/// Вход: EntryStatus.
/// Выход: SolidColorBrush (зелёный для Enabled, серый для Disabled, красный для Blocked).
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Выбираем цвет в зависимости от статуса записи
        if (value is EntryStatus status)
        {
            var isLight = Services.ThemeService.CurrentTheme == Wpf.Ui.Appearance.ApplicationTheme.Light;
            return status switch
            {
                EntryStatus.Enabled => isLight 
                    ? new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)) 
                    : new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x6A)),
                EntryStatus.Disabled => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                EntryStatus.Blocked => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                EntryStatus.Delayed => isLight
                    ? new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06))
                    : new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                _ => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            };
        }
        return new SolidColorBrush(Color.FromRgb(0x9D, 0x9D, 0x9D));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
