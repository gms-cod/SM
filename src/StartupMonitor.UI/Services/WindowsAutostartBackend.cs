using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace StartupMonitor.UI.Services;

/// <summary>Изолируемые операции автозапуска только текущего пользователя и собственных задач приложения.</summary>
internal interface IUserAutostartBackend
{
    /// <summary>Полный путь текущего приложения.</summary>
    string ExecutablePath { get; }
    /// <summary>SID текущего пользователя, определяющий владельца задачи.</summary>
    string UserSid { get; }
    /// <summary>Подтверждённое повышение текущего токена.</summary>
    bool IsElevated { get; }
    /// <summary>Получает независимый снимок HKCU Run.</summary>
    Dictionary<string, string> GetRunEntries();
    /// <summary>Записывает конкретное значение HKCU Run.</summary>
    void SetRunValue(string name, string value);
    /// <summary>Удаляет конкретное значение HKCU Run.</summary>
    void DeleteRunValue(string name);
    /// <summary>Регистрирует собственную задачу с проверкой владельца существующего имени.</summary>
    void RegisterScheduledTask(string name, string xml);
    /// <summary>Проверяет существование включённой собственной задачи.</summary>
    bool HasScheduledTask(string name);
    /// <summary>Удаляет только собственную задачу; чужой marker отклоняется.</summary>
    void DeleteScheduledTask(string name);
    /// <summary>Удаляет только подтверждённые старые команды UI из машинного Run при наличии прав.</summary>
    void CleanLegacyMachineRunEntries();
}

/// <summary>Настоящие HKCU/Task Scheduler операции без пароля и без запуска задачи при регистрации.</summary>
internal sealed class WindowsAutostartBackend : IUserAutostartBackend
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <inheritdoc />
    public string ExecutablePath => Environment.ProcessPath ?? string.Empty;
    /// <inheritdoc />
    public string UserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new InvalidOperationException("Не определён SID пользователя.");
        }
    }
    /// <inheritdoc />
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <inheritdoc />
    public Dictionary<string, string> GetRunEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValueNames().ToDictionary(name => name, name => key.GetValue(name)?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
            ?? new(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void SetRunValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть HKCU Run для записи.");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void DeleteRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <inheritdoc />
    public void RegisterScheduledTask(string name, string xml)
    {
        WithFolder(folder =>
        {
            object? previous = null;
            object? registered = null;
            try
            {
                previous = FindTask(folder, name);
                if (previous != null) RequireOwnership((string)((dynamic)previous).Xml);
                registered = folder.RegisterTask(name, xml, 6, UserSid, null, 3, null);
                RequireOwnership((string)((dynamic)registered).Xml);
                if (!(bool)((dynamic)registered).Enabled) throw new InvalidOperationException("Задача автозапуска отключена.");
                var expected = XDocument.Parse(xml).Descendants().Single(e => e.Name.LocalName == "Command").Value;
                var actual = XDocument.Parse((string)((dynamic)registered).Xml).Descendants().Single(e => e.Name.LocalName == "Command").Value;
                if (!string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Планировщик не подтвердил путь запуска UI.");
                return true;
            }
            finally { Release(registered); Release(previous); }
        });
    }

    /// <inheritdoc />
    public bool HasScheduledTask(string name) => WithFolder(folder =>
    {
        object? task = null;
        try
        {
            task = FindTask(folder, name);
            if (task == null) return false;
            RequireOwnership((string)((dynamic)task).Xml);
            return (bool)((dynamic)task).Enabled;
        }
        finally { Release(task); }
    });

    /// <inheritdoc />
    public void DeleteScheduledTask(string name)
    {
        WithFolder(folder =>
        {
            object? task = null;
            try
            {
                task = FindTask(folder, name);
                if (task == null) return true;
                RequireOwnership((string)((dynamic)task).Xml);
                folder.DeleteTask(name, 0);
                return true;
            }
            finally { Release(task); }
        });
    }

    /// <inheritdoc />
    public void CleanLegacyMachineRunEntries()
    {
        if (!IsElevated) return;
        foreach (var path in new[] { RunPath, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
            if (key == null) continue;
            foreach (var name in key.GetValueNames())
                if (AutostartService.IsStartupMonitorUiEntry(name, key.GetValue(name)?.ToString() ?? ""))
                    key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    /// <summary>Создаёт XML интерактивной задачи входа текущего SID с повышением без сохранённого пароля.</summary>
    /// <param name="executable">Полный путь UI; XML-библиотека экранирует специальные символы.</param>
    /// <param name="sid">SID владельца единственной задачи.</param>
    /// <returns>Схема Task Scheduler для регистрации без выполнения.</returns>
    internal static string BuildTaskXml(string executable, string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        return new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.2"),
            // URI планировщик заменяет путём задачи; Source сохраняет проверяемый marker владельца.
            new XElement(ns + "RegistrationInfo", new XElement(ns + "Source", "urn:startupmonitor:ui-autostart:" + sid)),
            new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", "true"), new XElement(ns + "UserId", sid))),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "User"),
                new XElement(ns + "UserId", sid), new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"), new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "Enabled", "true"), new XElement(ns + "ExecutionTimeLimit", "PT0S")),
            new XElement(ns + "Actions", new XAttribute("Context", "User"), new XElement(ns + "Exec",
                new XElement(ns + "Command", executable), new XElement(ns + "Arguments", "--minimized"),
                new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(executable)))))).ToString();
    }

    /// <summary>Проверяет marker и владельца до изменения задачи с совпавшим именем.</summary>
    private void RequireOwnership(string xml)
    {
        var task = XDocument.Parse(xml);
        var marker = task.Descendants().FirstOrDefault(e => e.Name.LocalName == "Source")?.Value;
        if (marker != "urn:startupmonitor:ui-autostart:" + UserSid)
            throw new InvalidOperationException("Имя задачи занято объектом, который не принадлежит Startup Monitor текущего пользователя.");
        var principal = task.Descendants().FirstOrDefault(e => e.Name.LocalName == "Principal");
        var owner = principal?.Elements().FirstOrDefault(e => e.Name.LocalName == "UserId")?.Value;
        if (!string.IsNullOrWhiteSpace(owner) && !owner.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            owner = ((SecurityIdentifier)new NTAccount(owner).Translate(typeof(SecurityIdentifier))).Value;
        if (owner != UserSid) throw new InvalidOperationException("Владелец задачи не совпадает с текущим пользователем.");
    }

    /// <summary>Возвращает задачу либо только подтверждённое отсутствие, не скрывая отказ прав или службы планировщика.</summary>
    private static object? FindTask(dynamic folder, string name)
    {
        try { return folder.GetTask(name); }
        // COM binder .NET может преобразовать ERROR_FILE_NOT_FOUND в FileNotFoundException вместо COMException.
        catch (FileNotFoundException) { return null; }
        catch (COMException ex) when (ex.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070490)) { return null; }
    }

    /// <summary>Удерживает COM-объекты в пределах одной операции и освобождает их при любом исходе.</summary>
    private static T WithFolder<T>(Func<dynamic, T> operation)
    {
        object? scheduler = null;
        object? folder = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
            scheduler = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Task Scheduler недоступен.");
            ((dynamic)scheduler).Connect();
            folder = ((dynamic)scheduler).GetFolder("\\");
            return operation(folder);
        }
        finally { Release(folder); Release(scheduler); }
    }

    /// <summary>Освобождает только настоящий COM-объект текущей операции.</summary>
    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
