using System.Globalization;
using System.Windows.Data;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Converters;

/// <summary>Метка типа записи, совпадающая с подписями кнопок фильтров.</summary>
public class LocationToFilterLabelConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если значение не EntryLocation — возвращаем "?"
        if (value is not EntryLocation loc)
            return "?";

        var strings = LocalizationService.Instance;
        // Группируем похожие расположения под одну метку фильтра
        return loc switch
        {
            EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce
                or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies => strings.FilterRegistry,
            EntryLocation.StartupFolder => strings.FilterFolder,
            EntryLocation.Service or EntryLocation.Driver => strings.FilterServices,
            EntryLocation.TaskScheduler => strings.FilterTasks,
            _ => strings.FilterOther,
        };
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
