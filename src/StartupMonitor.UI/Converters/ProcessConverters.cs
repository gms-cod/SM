using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StartupMonitor.UI.Converters;

/// <summary>Конвертер для подсветки активных процессов по потреблению ЦП.</summary>
public class CpuUsageToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush RedBrush = new((Color)ColorConverter.ConvertFromString("#EF4444"));
    private static readonly SolidColorBrush OrangeBrush = new((Color)ColorConverter.ConvertFromString("#F59E0B"));
    private static readonly SolidColorBrush CyanBrush = new((Color)ColorConverter.ConvertFromString("#00D2D3"));
    private static readonly SolidColorBrush MutedBrush = new((Color)ColorConverter.ConvertFromString("#94A3B8"));

    static CpuUsageToColorConverter()
    {
        RedBrush.Freeze();
        OrangeBrush.Freeze();
        CyanBrush.Freeze();
        MutedBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double cpu)
        {
            if (cpu >= 50.0) return RedBrush;
            if (cpu >= 15.0) return OrangeBrush;
            if (cpu >= 2.0) return CyanBrush;
        }
        return MutedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw freshNotSupported();
    private static NotSupportedException freshNotSupported() => new();
}

/// <summary>Фоновая кисть бейджа статуса автозапуска процесса.</summary>
public class StartupStatusBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush AutostartBrush = new((Color)ColorConverter.ConvertFromString("#1810B981"));
    private static readonly SolidColorBrush NormalBrush = new((Color)ColorConverter.ConvertFromString("#1064748B"));

    static StartupStatusBackgroundConverter()
    {
        AutostartBrush.Freeze();
        NormalBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? AutostartBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Цвет рамки бейджа статуса автозапуска процесса.</summary>
public class StartupStatusBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush AutostartBrush = new((Color)ColorConverter.ConvertFromString("#3510B981"));
    private static readonly SolidColorBrush NormalBrush = new((Color)ColorConverter.ConvertFromString("#2064748B"));

    static StartupStatusBorderConverter()
    {
        AutostartBrush.Freeze();
        NormalBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? AutostartBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Цвет текста бейджа статуса автозапуска процесса.</summary>
public class StartupStatusForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush AutostartBrush = new((Color)ColorConverter.ConvertFromString("#10B981"));
    private static readonly SolidColorBrush NormalBrush = new((Color)ColorConverter.ConvertFromString("#94A3B8"));

    static StartupStatusForegroundConverter()
    {
        AutostartBrush.Freeze();
        NormalBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? AutostartBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Инвертированный конвертер bool в Visibility (true -> Collapsed, false -> Visible).</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Фоновая кисть бейджа угрозы CLI по уровню риска.</summary>
public class ThreatLevelToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush RedBg = new((Color)ColorConverter.ConvertFromString("#26EF4444"));
    private static readonly SolidColorBrush OrangeBg = new((Color)ColorConverter.ConvertFromString("#26F59E0B"));
    private static readonly SolidColorBrush BlueBg = new((Color)ColorConverter.ConvertFromString("#263B82F6"));

    static ThreatLevelToBackgroundConverter()
    {
        RedBg.Freeze();
        OrangeBg.Freeze();
        BlueBg.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StartupMonitor.Shared.Models.CliThreatLevel level)
        {
            return level switch
            {
                StartupMonitor.Shared.Models.CliThreatLevel.High => RedBg,
                StartupMonitor.Shared.Models.CliThreatLevel.Medium => OrangeBg,
                StartupMonitor.Shared.Models.CliThreatLevel.Low => BlueBg,
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Кисть рамки бейджа угрозы CLI по уровню риска.</summary>
public class ThreatLevelToBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush RedBorder = new((Color)ColorConverter.ConvertFromString("#4DEF4444"));
    private static readonly SolidColorBrush OrangeBorder = new((Color)ColorConverter.ConvertFromString("#4DF59E0B"));
    private static readonly SolidColorBrush BlueBorder = new((Color)ColorConverter.ConvertFromString("#4D3B82F6"));

    static ThreatLevelToBorderConverter()
    {
        RedBorder.Freeze();
        OrangeBorder.Freeze();
        BlueBorder.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StartupMonitor.Shared.Models.CliThreatLevel level)
        {
            return level switch
            {
                StartupMonitor.Shared.Models.CliThreatLevel.High => RedBorder,
                StartupMonitor.Shared.Models.CliThreatLevel.Medium => OrangeBorder,
                StartupMonitor.Shared.Models.CliThreatLevel.Low => BlueBorder,
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Цвет текста/иконки бейджа угрозы CLI по уровню риска.</summary>
public class ThreatLevelToForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush RedFg = new((Color)ColorConverter.ConvertFromString("#F87171"));
    private static readonly SolidColorBrush OrangeFg = new((Color)ColorConverter.ConvertFromString("#FBBF24"));
    private static readonly SolidColorBrush BlueFg = new((Color)ColorConverter.ConvertFromString("#60A5FA"));
    private static readonly SolidColorBrush MutedFg = new((Color)ColorConverter.ConvertFromString("#94A3B8"));

    static ThreatLevelToForegroundConverter()
    {
        RedFg.Freeze();
        OrangeFg.Freeze();
        BlueFg.Freeze();
        MutedFg.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StartupMonitor.Shared.Models.CliThreatLevel level)
        {
            return level switch
            {
                StartupMonitor.Shared.Models.CliThreatLevel.High => RedFg,
                StartupMonitor.Shared.Models.CliThreatLevel.Medium => OrangeFg,
                StartupMonitor.Shared.Models.CliThreatLevel.Low => BlueFg,
                _ => MutedFg
            };
        }
        return MutedFg;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Конвертер для локализованного отображения источника автозапуска процесса (ProcessInfo).</summary>
public class ProcessStartupOriginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not StartupMonitor.Shared.Models.ProcessInfo proc) return string.Empty;

        var loc = StartupMonitor.UI.Services.LocalizationService.Instance;

        if (proc.StartupLocation.HasValue)
        {
            return proc.StartupLocation.Value switch
            {
                StartupMonitor.Shared.Models.EntryLocation.RegistryRun => loc["OriginRegistryRun"],
                StartupMonitor.Shared.Models.EntryLocation.RegistryRunOnce => loc["OriginRegistryRunOnce"],
                StartupMonitor.Shared.Models.EntryLocation.RegistryRunServices => loc["OriginRegistryRunServices"],
                StartupMonitor.Shared.Models.EntryLocation.RegistryPolicies => loc["OriginRegistryPolicies"],
                StartupMonitor.Shared.Models.EntryLocation.StartupFolder => loc["OriginStartupFolder"],
                StartupMonitor.Shared.Models.EntryLocation.Service => loc["OriginService"],
                StartupMonitor.Shared.Models.EntryLocation.Driver => loc["OriginDriver"],
                StartupMonitor.Shared.Models.EntryLocation.TaskScheduler => loc["OriginTaskScheduler"],
                StartupMonitor.Shared.Models.EntryLocation.Winlogon => loc["OriginWinlogon"],
                StartupMonitor.Shared.Models.EntryLocation.ActiveSetup => loc["OriginActiveSetup"],
                StartupMonitor.Shared.Models.EntryLocation.AppxStartupTask => loc["OriginAppx"],
                _ => loc["OriginAutostart"]
            };
        }

        if (!string.IsNullOrEmpty(proc.ParentProcessName))
        {
            return proc.ParentProcessName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
                ? loc["OriginManualExplorer"]
                : string.Format(loc["OriginParentPrefix"], proc.ParentProcessName);
        }

        return loc["OriginBackgroundProcess"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Конвертер отступа уровня дерева в Thickness (Left indent).</summary>
public class TreeIndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double indent = 0;
        if (value is double d) indent = d;
        else if (value is int i) indent = i * 18.0;
        return new System.Windows.Thickness(4.0 + indent, 0, 8.0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
