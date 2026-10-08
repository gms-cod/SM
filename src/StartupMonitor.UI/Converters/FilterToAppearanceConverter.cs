using System.Globalization;
using System.Windows.Data;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует строку текущего фильтра в название стиля кнопки (Primary/Secondary).
/// Вход: value = текущий выбранный фильтр (string), parameter = фильтр кнопки (string).
/// Выход: "Primary" если совпадает, иначе "Secondary".
/// </summary>
public class FilterToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Сравниваем значение текущего фильтра с фильтром кнопки
        if (value is string current && parameter is string target)
            return current == target ? "Primary" : "Secondary";
        return "Secondary";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
