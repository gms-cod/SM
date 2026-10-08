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
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Сравниваем значение текущего фильтра с фильтром кнопки
        if (value is string current && parameter is string target)
            return current == target ? "Primary" : "Secondary";
        return "Secondary";
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
