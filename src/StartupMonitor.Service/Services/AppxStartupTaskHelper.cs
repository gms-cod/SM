using Microsoft.Win32;

namespace StartupMonitor.Service.Services;

/// <summary>Работа с автозапуском UWP/AppX-приложений (StartupTask).
/// Состояние включённости хранится в реестре:
/// HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\&lt;PFN&gt;\&lt;TaskId&gt;\State.
/// State: 0/1 = выключено, 2 = включено, 3 = disabled по политике, 4 = enabled по политике.</summary>
public static class AppxStartupTaskHelper
{
    /// <summary>Базовый путь к SystemAppData в HKCU.</summary>
    private const string SystemAppDataPath =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    /// <summary>Читает состояние StartupTask. Возвращает true, если задача включена.
    /// Если ключ отсутствует — слушаем манифест (Enabled="true" означает включено).
    /// Считываем состояние текущей сессии (HKCU); для LocalSystem HKCU=SYSTEM,
    /// поэтому смотрим ещё и все пользовательские кусты и берём "наиболее включённое" из найденных.</summary>
    public static bool ReadState(string pfn, string taskId)
    {
        var anyFound = false;
        var anyEnabled = false;

        TryRead(Registry.CurrentUser);
        foreach (var (sid, _) in WatcherHelper.GetUserProfiles())
        using (var hive = Registry.Users.OpenSubKey(sid))
            if (hive != null) TryRead(hive);

        return anyFound ? anyEnabled : true;

        void TryRead(RegistryKey baseKey)
        {
            using var key = baseKey.OpenSubKey($@"{SystemAppDataPath}\{pfn}\{taskId}");
            if (key == null) return;
            var state = key.GetValue("State");
            if (state is int s)
            {
                anyFound = true;
                if (s is 2 or 4) anyEnabled = true;
            }
        }
    }

    /// <summary>Устанавливает состояние StartupTask. enabled=true пишет State=2,
    /// enabled=false — State=1 (отключено пользователем). Применяется к текущему
    /// пользователю (HKCU) и всем пользовательским кустам.</summary>
    public static bool WriteState(string pfn, string taskId, bool enabled)
    {
        var stateValue = enabled ? 2 : 1;
        var ok = false;

        ok |= TryWrite(Registry.CurrentUser);

        foreach (var (sid, _) in WatcherHelper.GetUserProfiles())
        using (var hive = Registry.Users.OpenSubKey(sid, writable: true))
            if (hive != null) ok |= TryWrite(hive);

        return ok;

        bool TryWrite(RegistryKey baseKey)
        {
            try
            {
                using var key = baseKey.CreateSubKey($@"{SystemAppDataPath}\{pfn}\{taskId}");
                key.SetValue("State", stateValue, RegistryValueKind.DWord);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}