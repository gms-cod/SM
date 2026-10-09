using Microsoft.Win32;

namespace StartupMonitor.UI.Services;

/// <summary>Автозапуск UI при входе пользователя (HKCU Run — прав администратора не нужно).</summary>
public static class AutostartService
{
    /// <summary>Путь к разделу реестра для автозапуска программ текущего пользователя.</summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Имя значения в реестре, под которым регистрируется UI.</summary>
    public const string ValueName = "StartupMonitor.UI";

    /// <summary>Включает или отключает автозапуск UI при входе пользователя.</summary>
    /// <param name="enable">true — добавить/обновить запись, false — удалить.</param>
    /// <param name="valueName">Необязательное имя значения в реестре (по умолчанию ValueName).</param>
    public static void SetEnabled(bool enable, string? valueName = null)
    {
        if (valueName == null || valueName == ValueName)
        {
            EnsureSingleAutostartEntry(enable);
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(valueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }

    /// <summary>
    /// Проверяет реестр на наличие дубликатов или устаревших записей автозапуска StartupMonitor.UI
    /// и гарантирует наличие ровно одной актуальной записи (если включено) или полное удаление (если выключено).
    /// </summary>
    /// <param name="enable">true — включить автозапуск ровно в одном месте; false — удалить все записи.</param>
    public static void EnsureSingleAutostartEntry(bool enable, string? targetExePath = null)
    {
        var currentExe = targetExePath ?? Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(currentExe)) return;

        // Если запущено под testhost без явного пути — используем канонический путь
        if (targetExePath == null &&
            (currentExe.EndsWith("testhost.exe", StringComparison.OrdinalIgnoreCase) ||
             currentExe.EndsWith("vstest.console.exe", StringComparison.OrdinalIgnoreCase)))
        {
            currentExe = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "StartupMonitor",
                "StartupMonitor.UI.exe");
        }

        var canonicalCommand = $"\"{currentExe}\" --minimized";
        App.Log($"AutostartService: EnsureSingleAutostartEntry(enable={enable}), currentExe='{currentExe}'");

        // 1. Проверяем и настраиваем HKCU Run
        try
        {
            using var hkcuRun = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                             ?? Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (hkcuRun != null)
            {
                var names = hkcuRun.GetValueNames();
                var canonicalFound = false;

                foreach (var name in names)
                {
                    var val = hkcuRun.GetValue(name)?.ToString() ?? string.Empty;
                    var isOurApp = IsStartupMonitorUiEntry(name, val);

                    if (!isOurApp) continue;

                    if (!enable)
                    {
                        // Автозапуск отключён — удаляем любые наши записи
                        try
                        {
                            hkcuRun.DeleteValue(name, throwOnMissingValue: false);
                            App.Log($"AutostartService: удалена запись автозапуска HKCU [{name}]");
                        }
                        catch (Exception ex)
                        {
                            App.Log($"AutostartService: ошибка удаления [{name}]: {ex.Message}");
                        }
                        continue;
                    }

                    // Автозапуск включён:
                    if (string.Equals(name, ValueName, StringComparison.OrdinalIgnoreCase))
                    {
                        canonicalFound = true;
                        // Если путь или аргументы изменились — обновляем
                        if (!string.Equals(val.Trim(), canonicalCommand.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            hkcuRun.SetValue(ValueName, canonicalCommand);
                            App.Log($"AutostartService: обновлена каноническая запись HKCU [{ValueName}] = {canonicalCommand}");
                        }
                        else
                        {
                            App.Log($"AutostartService: каноническая запись HKCU [{ValueName}] уже актуальна");
                        }
                    }
                    else
                    {
                        // Дублирующая запись под другим именем (например StartupMonitor, SMon и т.д.) — удаляем
                        try
                        {
                            hkcuRun.DeleteValue(name, throwOnMissingValue: false);
                            App.Log($"AutostartService: удален дубликат HKCU [{name}]");
                        }
                        catch { }
                    }
                }

                if (enable && !canonicalFound)
                {
                    hkcuRun.SetValue(ValueName, canonicalCommand);
                    App.Log($"AutostartService: создана каноническая запись HKCU [{ValueName}] = {canonicalCommand}");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"AutostartService HKCU ERROR: {ex.Message}");
        }

        // 2. Проверяем HKLM Run на наличие дубликатов (например, от старых инсталляций)
        CleanHklmDuplicates();
    }

    /// <summary>
    /// Проверяет, относится ли запись в реестре к StartupMonitor.UI (по имени значения или пути команды).
    /// </summary>
    private static bool IsStartupMonitorUiEntry(string name, string commandValue)
    {
        if (string.Equals(name, ValueName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "StartupMonitorUI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "StartupMonitor", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (commandValue.Contains("StartupMonitor.UI", StringComparison.OrdinalIgnoreCase) ||
            commandValue.Contains("StartupMonitor.UI.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Очищает дубликаты в разделах HKLM Run, если они существуют (например, если программа была прописана общесистемно).
    /// </summary>
    private static void CleanHklmDuplicates()
    {
        string[] hklmPaths =
        [
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
        ];

        foreach (var subPath in hklmPaths)
        {
            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(subPath, writable: true);
                if (hklmKey == null) continue;

                foreach (var name in hklmKey.GetValueNames())
                {
                    var val = hklmKey.GetValue(name)?.ToString() ?? string.Empty;
                    if (IsStartupMonitorUiEntry(name, val))
                    {
                        try { hklmKey.DeleteValue(name, throwOnMissingValue: false); } catch { }
                    }
                }
            }
            catch { } // Может не быть прав администратора на запись в HKLM
        }
    }

    /// <summary>Проверяет, зарегистрирован ли UI в автозагрузке.</summary>
    /// <param name="valueName">Необязательное имя значения (по умолчанию ValueName).</param>
    /// <returns>true, если запись существует в HKCU Run.</returns>
    public static bool IsEnabled(string? valueName = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(valueName ?? ValueName) != null;
        }
        catch
        {
            return false;
        }
    }
}
