namespace StartupMonitor.UI.Services;

/// <summary>Автозапуск текущего пользователя: повышенная интерактивная задача либо HKCU Run для обычного UI.</summary>
public static class AutostartService
{
    /// <summary>Имя значения в реестре, под которым регистрируется UI.</summary>
    public const string ValueName = "StartupMonitor.UI";

    /// <summary>Включает или отключает автозапуск UI при входе пользователя.</summary>
    /// <param name="enable">true — добавить/обновить запись, false — удалить.</param>
    /// <param name="valueName">Необязательное имя значения в реестре (по умолчанию ValueName).</param>
    public static void SetEnabled(bool enable, string? valueName = null)
        => SetEnabled(enable, valueName, new WindowsAutostartBackend());

    /// <summary>Применяет настройку через изолируемый адаптер; пользовательские имена сохранены для совместимости.</summary>
    /// <param name="enable">Требуемое состояние.</param>
    /// <param name="valueName">Имя записи либо каноническое значение по умолчанию.</param>
    /// <param name="backend">Реестр и планировщик текущего пользователя.</param>
    internal static void SetEnabled(bool enable, string? valueName, IUserAutostartBackend backend)
    {
        if (valueName == null || valueName == ValueName)
        {
            EnsureSingleAutostartEntry(enable, null, backend);
            return;
        }

        try
        {
            if (enable)
            {
                var exePath = backend.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath))
                    backend.SetRunValue(valueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                backend.DeleteRunValue(valueName);
            }
        }
        catch (Exception ex) { App.Log($"AutostartService.SetEnabled: {ex.Message}"); }
    }

    /// <summary>
    /// Регистрирует повышенную задачу до удаления bootstrap Run; обычный пользователь использует Run.
    /// Вне изолированного адаптера testhost не изменяет автозапуск настоящего пользователя.
    /// </summary>
    /// <param name="enable">true — включить автозапуск ровно в одном месте; false — удалить все записи.</param>
    /// <param name="targetExePath">Явный полный путь установленного UI либо путь текущего процесса.</param>
    /// <returns>Подтверждено ли требуемое состояние без удаления чужих записей или задач.</returns>
    public static bool EnsureSingleAutostartEntry(bool enable, string? targetExePath = null)
    {
        var backend = new WindowsAutostartBackend();
        var host = System.IO.Path.GetFileName(backend.ExecutablePath);
        if (targetExePath == null && (host.Equals("testhost.exe", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("vstest.console.exe", StringComparison.OrdinalIgnoreCase)))
        {
            App.Log("AutostartService: системный автозапуск не изменяется из testhost; сценарии проверяются через изолированный адаптер.");
            return false;
        }
        return EnsureSingleAutostartEntry(enable, targetExePath, backend);
    }

    /// <summary>Сериализует миграцию одного пользователя и не трогает Run при отказе регистрации повышенной задачи.</summary>
    /// <param name="enable">Включение или отключение автозапуска.</param>
    /// <param name="targetExePath">Точный путь запускаемого UI.</param>
    /// <param name="backend">Изолируемые операции реестра и планировщика.</param>
    /// <returns>True только после подтверждения требуемого состояния.</returns>
    internal static bool EnsureSingleAutostartEntry(bool enable, string? targetExePath, IUserAutostartBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        try
        {
            var taskName = GetTaskName(backend.UserSid);
            if (!enable)
            {
                backend.DeleteScheduledTask(taskName);
                RemoveOwnedRunEntries(backend);
                backend.CleanLegacyMachineRunEntries();
                return !backend.HasScheduledTask(taskName) && !backend.GetRunEntries().Any(pair => IsStartupMonitorUiEntry(pair.Key, pair.Value));
            }

            var currentExe = targetExePath ?? backend.ExecutablePath;
            if (string.IsNullOrWhiteSpace(currentExe) || !System.IO.Path.IsPathFullyQualified(currentExe) ||
                !string.Equals(System.IO.Path.GetFileName(currentExe), "StartupMonitor.UI.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Для автозапуска нужен полный путь StartupMonitor.UI.exe.");

            if (backend.IsElevated)
            {
                // Регистрация и проверка предшествуют удалению Run: при отказе bootstrap не теряется.
                backend.RegisterScheduledTask(taskName, WindowsAutostartBackend.BuildTaskXml(currentExe, backend.UserSid));
                if (!backend.HasScheduledTask(taskName))
                    throw new InvalidOperationException("Планировщик не подтвердил задачу автозапуска.");
                RemoveOwnedRunEntries(backend);
            }
            else if (backend.HasScheduledTask(taskName))
            {
                // Уже настроенная повышенная задача остаётся единственным источником запуска этого пользователя.
                RemoveOwnedRunEntries(backend);
            }
            else
            {
                var entries = backend.GetRunEntries();
                if (entries.TryGetValue(ValueName, out var existing) && !IsStartupMonitorUiEntry(ValueName, existing))
                    throw new InvalidOperationException("Имя записи автозапуска занято другой программой.");
                backend.SetRunValue(ValueName, $"\"{currentExe}\" --minimized");
                RemoveOwnedRunEntries(backend, preserveCanonical: true);
            }
            backend.CleanLegacyMachineRunEntries();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"AutostartService: требуемое состояние автозапуска не подтверждено: {ex.Message}");
            return false;
        }
    }

    /// <summary>Имя задачи включает SID: разные пользователи не перезаписывают интерактивный вход друг друга.</summary>
    /// <param name="sid">SID текущего пользователя.</param>
    /// <returns>Точное имя принадлежащей приложению задачи.</returns>
    internal static string GetTaskName(string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        return ValueName + "." + sid;
    }

    /// <summary>Относит команду к UI только по точному имени запускаемого файла, а не по произвольной подстроке.</summary>
    /// <param name="name">Имя значения реестра для контекста проверки.</param>
    /// <param name="commandValue">Команда запуска.</param>
    /// <returns>Принадлежит ли команда приложению.</returns>
    internal static bool IsStartupMonitorUiEntry(string name, string commandValue)
    {
        var command = (commandValue ?? string.Empty).Trim();
        if (command.Length == 0) return false;
        var end = command[0] == '"' ? command.IndexOf('"', 1) : command.IndexOf(' ');
        var executable = command[0] == '"' ? end > 1 ? command[1..end] : string.Empty
            : end < 0 ? command : command[..end];
        return string.Equals(System.IO.Path.GetFileName(executable), "StartupMonitor.UI.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Удаляет только команды самого UI, сохраняя чужие значения и необязательную каноническую запись.</summary>
    private static void RemoveOwnedRunEntries(IUserAutostartBackend backend, bool preserveCanonical = false)
    {
        foreach (var entry in backend.GetRunEntries())
            if (IsStartupMonitorUiEntry(entry.Key, entry.Value) && (!preserveCanonical || !entry.Key.Equals(ValueName, StringComparison.OrdinalIgnoreCase)))
                backend.DeleteRunValue(entry.Key);
    }

    /// <summary>Проверяет, зарегистрирован ли UI в автозагрузке.</summary>
    /// <param name="valueName">Необязательное имя значения (по умолчанию ValueName).</param>
    /// <returns>true, если запись существует в HKCU Run.</returns>
    public static bool IsEnabled(string? valueName = null)
        => IsEnabled(valueName, new WindowsAutostartBackend());

    /// <summary>Читает подтверждённое состояние из изолируемых Run и планировщика.</summary>
    internal static bool IsEnabled(string? valueName, IUserAutostartBackend backend)
    {
        try
        {
            if ((valueName == null || valueName == ValueName) && backend.HasScheduledTask(GetTaskName(backend.UserSid)))
                return true;
            return backend.GetRunEntries().ContainsKey(valueName ?? ValueName);
        }
        catch (Exception ex)
        {
            App.Log($"AutostartService.IsEnabled: {ex.Message}");
            return false;
        }
    }
}
