using System.Globalization;
using System.Windows.Data;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryLocation в строковый символ-иконку (Unicode).
/// Вход: EntryLocation.
/// Выход: строка с одним Unicode-символом (например ⚙, 📂, 📅).
/// </summary>
public class LocationToIconStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если значение не EntryLocation — возвращаем "?"
        if (value is not EntryLocation loc)
            return "?";

        // Подбираем Unicode-символ в зависимости от типа расположения
        return loc switch
        {
            EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies => "\u2699",
            EntryLocation.StartupFolder => "\U0001F4C2",
            EntryLocation.Service => "\u2699",
            EntryLocation.TaskScheduler => "\U0001F4C5",
            EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler => "\u25C7",
            EntryLocation.Driver => "\u26A1",
            EntryLocation.BrowserHelperObject or EntryLocation.BrowserPlugin => "\U0001F310",
            EntryLocation.AppInitDll or EntryLocation.WmiEventSubscription => "\U0001F4CB",
            EntryLocation.Winlogon => "\U0001F510",
            EntryLocation.BootExecute => "\u26A1",
            EntryLocation.GpoStartupScript or EntryLocation.GpoLogonScript => "\U0001F4DC",
            EntryLocation.ComObject => "\U0001F504",
            EntryLocation.NetworkProvider => "\U0001F4E1",
            EntryLocation.PrintMonitor => "\U0001F5A8",
            EntryLocation.LsaNotification => "\U0001F512",
            EntryLocation.ProtocolCatalog => "\U0001F310",
            EntryLocation.AppPaths => "\U0001F4C4",
            EntryLocation.KeyboardHook => "\u2328",
            EntryLocation.AppxStartupTask => "\U0001F3E1",
            EntryLocation.ActiveSetup => "\u2691",
            EntryLocation.Drivers32 => "\U0001F3B5",
            _ => "?",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
