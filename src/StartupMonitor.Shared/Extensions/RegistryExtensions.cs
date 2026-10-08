using System.Diagnostics;
using Microsoft.Win32;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Shared.Extensions;

public static class RegistryExtensions
{
    private static readonly (string Key, string? Name, EntryLocation Location, string Label)[] RegistryLocations =
    [
        (@"Software\Microsoft\Windows\CurrentVersion\Run", null, EntryLocation.RegistryRun, @"HKLM\...\Run"),
        (@"Software\Microsoft\Windows\CurrentVersion\RunOnce", null, EntryLocation.RegistryRunOnce, @"HKLM\...\RunOnce"),
        (@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", null, EntryLocation.RegistryRun, @"HKLM\...\Run (WOW)"),
        (@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", null, EntryLocation.RegistryRunOnce, @"HKLM\...\RunOnce (WOW)"),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", null, EntryLocation.RegistryPolicies, @"HKLM\...\Policies\Run"),
    ];

    /// <summary>ScanHklmRunKeys: Выполняет операцию ScanHklmRunKeys.</summary>
    /// <returns>Выходные данные: результирующее значение типа List<StartupEntry>.</returns>
    public static List<StartupEntry> ScanHklmRunKeys()
    {
        var entries = new List<StartupEntry>();

        using var baseKey = Registry.LocalMachine;

        foreach (var (keyPath, _, location, label) in RegistryLocations)
        {
            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) continue;

            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                entries.Add(new StartupEntry
                {
                    Name = valueName,
                    FilePath = ParsePath(value),
                    CommandLine = value,
                    Location = location,
                    LocationDetail = $@"HKLM\{keyPath}\{valueName}",
                    Publisher = GetFilePublisher(ParsePath(value))
                });
            }
        }

        return entries;
    }

    /// <summary>ScanHkcuRunKeys: Выполняет операцию ScanHkcuRunKeys.</summary>
    /// <returns>Выходные данные: результирующее значение типа List<StartupEntry>.</returns>
    public static List<StartupEntry> ScanHkcuRunKeys()
    {
        var entries = new List<StartupEntry>();

        using var baseKey = Registry.CurrentUser;

        var userLocations = new[]
        {
            (@"Software\Microsoft\Windows\CurrentVersion\Run", EntryLocation.RegistryRun),
            (@"Software\Microsoft\Windows\CurrentVersion\RunOnce", EntryLocation.RegistryRunOnce),
        };

        foreach (var (keyPath, location) in userLocations)
        {
            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) continue;

            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                entries.Add(new StartupEntry
                {
                    Name = valueName,
                    FilePath = ParsePath(value),
                    CommandLine = value,
                    Location = location,
                    LocationDetail = $@"HKCU\{keyPath}\{valueName}",
                    Publisher = GetFilePublisher(ParsePath(value))
                });
            }
        }

        return entries;
    }

    /// <summary>ParsePath: Выполняет операцию ParsePath.</summary>
    /// <param name="commandLine">Входные данные: значение параметра commandLine (string).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string ParsePath(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;

        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed;
        }

        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
    }

    /// <summary>GetFilePublisher: Возвращает FilePublisher.</summary>
    /// <param name="filePath">Входные данные: значение параметра filePath (string).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string GetFilePublisher(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return string.Empty;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);
            return info.CompanyName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
