using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.UI.Converters;

/// <summary>
/// Конвертирует EntryLocation в иконку Fluent UI SymbolRegular.
/// Вход: EntryLocation.
/// Выход: SymbolRegular (иконка из WPF UI).
/// </summary>
public class LocationToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если значение не EntryLocation — иконка вопроса
        if (value is not EntryLocation loc)
            return SymbolRegular.QuestionCircle24;

        // Подбираем иконку в зависимости от типа расположения
        return loc switch
        {
            EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies => SymbolRegular.Database24,
            EntryLocation.StartupFolder => SymbolRegular.Folder24,
            EntryLocation.Service => SymbolRegular.Server24,
            EntryLocation.Driver => SymbolRegular.DeveloperBoard24,
            EntryLocation.TaskScheduler => SymbolRegular.CalendarClock24,
            EntryLocation.Winlogon => SymbolRegular.Shield24,
            EntryLocation.BootExecute => SymbolRegular.Power24,
            EntryLocation.AppInitDll or EntryLocation.WmiEventSubscription => SymbolRegular.Code24,
            EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler => SymbolRegular.Box24,
            EntryLocation.BrowserHelperObject or EntryLocation.BrowserPlugin => SymbolRegular.Globe24,
            EntryLocation.ImageFileExecutionOptions => SymbolRegular.Image24,
            EntryLocation.GpoStartupScript or EntryLocation.GpoLogonScript => SymbolRegular.Script24,
            EntryLocation.ComObject => SymbolRegular.KeyCommand24,
            EntryLocation.NetworkProvider => SymbolRegular.NetworkCheck24,
            EntryLocation.PrintMonitor => SymbolRegular.Print24,
            EntryLocation.LsaNotification => SymbolRegular.LockClosed24,
            EntryLocation.ProtocolCatalog => SymbolRegular.ProtocolHandler24,
            EntryLocation.AppPaths => SymbolRegular.Document24,
            EntryLocation.KeyboardHook => SymbolRegular.Keyboard24,
            EntryLocation.AppxStartupTask => SymbolRegular.Apps24,
            EntryLocation.ActiveSetup => SymbolRegular.PlayingCards20,
            EntryLocation.Drivers32 => SymbolRegular.MusicNote220,
            _ => SymbolRegular.QuestionCircle24,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
