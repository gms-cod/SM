using System.Globalization;
using System.Windows.Data;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Обрезает длинный текст до 50 символов, добавляя "...".
/// Вход: строка произвольной длины.
/// Выход: строка до 50 символов (с "..." на конце, если было длиннее), иначе исходная строка.
/// </summary>
public class TruncateConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если текст пустой — возвращаем как есть
        if (value is not string text || string.IsNullOrEmpty(text))
            return value ?? "";

        // Обрезаем до 47 символов + "..."
        if (text.Length > 50)
            return text[..47] + "...";

        return text;
    }

    /// <summary>ConvertBack: Выполняет преобразование данных Back.</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
