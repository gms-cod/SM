using System.Globalization;
using System.Windows.Data;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryLocation в локализованное отображаемое название.
/// Вход: EntryLocation.
/// Выход: строка локализации (например "Реестр", "Папка автозагрузки", "Служба").
/// </summary>
public class LocationToDisplayConverter : IValueConverter
{
    /// <summary>Convert: Выполняет преобразование данных .</summary>
    /// <param name="value">Входные данные: значение параметра value (object?).</param>
    /// <param name="targetType">Входные данные: значение параметра targetType (Type).</param>
    /// <param name="parameter">Входные данные: значение параметра parameter (object?).</param>
    /// <param name="culture">Входные данные: значение параметра culture (CultureInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа object.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Если значение не EntryLocation — возвращаем "Неизвестно"
        if (value is not EntryLocation loc)
            return LocalizationService.Instance.LocUnknown;

        // Выбираем локализованную строку в зависимости от типа расположения
        return loc switch
        {
            EntryLocation.RegistryRun => LocalizationService.Instance.LocRegistryRun,
            EntryLocation.RegistryRunOnce => LocalizationService.Instance.LocRegistryRunOnce,
            EntryLocation.RegistryRunServices => LocalizationService.Instance.LocRegistryRunServices,
            EntryLocation.RegistryPolicies => LocalizationService.Instance.LocRegistryPolicies,
            EntryLocation.StartupFolder => LocalizationService.Instance.LocStartupFolder,
            EntryLocation.Service => LocalizationService.Instance.LocService,
            EntryLocation.Driver => LocalizationService.Instance.LocDriver,
            EntryLocation.TaskScheduler => LocalizationService.Instance.LocTaskScheduler,
            EntryLocation.Winlogon => LocalizationService.Instance.LocWinlogon,
            EntryLocation.BootExecute => LocalizationService.Instance.LocBootExecute,
            EntryLocation.AppInitDll => LocalizationService.Instance.LocAppInit,
            EntryLocation.ShellExtension => LocalizationService.Instance.LocShellExtension,
            EntryLocation.ContextMenuHandler => LocalizationService.Instance.LocContextMenuHandler,
            EntryLocation.BrowserHelperObject => LocalizationService.Instance.LocBHO,
            EntryLocation.ImageFileExecutionOptions => LocalizationService.Instance.LocIFEO,
            EntryLocation.GpoStartupScript => LocalizationService.Instance.LocGpoStartup,
            EntryLocation.GpoLogonScript => LocalizationService.Instance.LocGpoLogon,
            EntryLocation.WmiEventSubscription => LocalizationService.Instance.LocWmi,
            EntryLocation.ComObject => LocalizationService.Instance.LocComObject,
            EntryLocation.BrowserPlugin => LocalizationService.Instance.LocBrowserPlugin,
            EntryLocation.NetworkProvider => LocalizationService.Instance.LocNetworkProvider,
            EntryLocation.PrintMonitor => LocalizationService.Instance.LocPrintMonitor,
            EntryLocation.LsaNotification => LocalizationService.Instance.LocLsa,
            EntryLocation.ProtocolCatalog => LocalizationService.Instance.LocProtocol,
            EntryLocation.AppPaths => LocalizationService.Instance.LocAppPaths,
            EntryLocation.KeyboardHook => LocalizationService.Instance.LocKeyboardHook,
            EntryLocation.AppxStartupTask => LocalizationService.Instance.LocAppxStartupTask,
            EntryLocation.ActiveSetup => LocalizationService.Instance.LocActiveSetup,
            EntryLocation.Drivers32 => LocalizationService.Instance.LocDrivers32,
            _ => LocalizationService.Instance.LocUnknown,
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
