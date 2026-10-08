using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертер пути файла в кэшированную системную иконку WPF ImageSource.
/// </summary>
public class FilePathToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            return AppIconService.GetIcon(path);
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

