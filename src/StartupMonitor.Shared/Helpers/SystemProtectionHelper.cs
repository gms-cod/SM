using System;
using System.Collections.Generic;
using System.IO;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Shared.Helpers;

/// <summary>
/// Централизованный помощник защиты критически важных компонентов операционной системы Windows
/// и собственных процессов StartupMonitor от случайного завершения, удаления или блокировки.
/// </summary>
public static class SystemProtectionHelper
{
    /// <summary>
    /// Имена процессов, завершение которых приводит к немедленному краху или дестабилизации Windows,
    /// а также собственные процессы мониторинга.
    /// </summary>
    public static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "lsass", "csrss", "smss", "wininit", "winlogon", "services",
        "system", "registry", "memory compression", "explorer", "dwm", "sihost",
        "taskhostw", "conhost", "dllhost", "rundll32", "wmiprvse", "msmpeng",
        "trustedinstaller", "fontdrvhost", "ctfmon",
        "startupmonitor.service", "startupmonitor.ui"
    };

    /// <summary>
    /// Исполняемые файлы хост-процессов (контейнеров служб и скриптов), которые никогда нельзя блокировать
    /// по чистому пути к файлу (Path), так как это затрагивает все остальные независимые службы и задачи.
    /// </summary>
    public static readonly HashSet<string> HostProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "rundll32", "dllhost", "cmd", "powershell", "pwsh", "mshta", "cscript", "wscript"
    };

    /// <summary>
    /// Проверяет, является ли указанное имя файла или путь защищённым системным процессом Windows.
    /// </summary>
    public static bool IsProtectedProcessName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return true;
        var name = Path.GetFileNameWithoutExtension(fileName.Trim().Trim('"'));
        return string.IsNullOrEmpty(name) || ProtectedProcessNames.Contains(name);
    }

    /// <summary>
    /// Проверяет, является ли процесс хостом служб/скриптов (svchost, rundll32 и др.).
    /// </summary>
    public static bool IsHostProcess(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var name = Path.GetFileNameWithoutExtension(fileName.Trim().Trim('"'));
        return !string.IsNullOrEmpty(name) && HostProcessNames.Contains(name);
    }

    /// <summary>
    /// Проверяет, относится ли имя службы к критически важным службам ядра Windows или Per-User службам.
    /// </summary>
    public static bool IsCoreWindowsService(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;
        var svc = serviceName.Trim();

        // 1. Критичные службы ядра ОС (удаление sc delete приводит к BSoD)
        if (string.Equals(svc, "rpcss", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "dcomlaunch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "plugplay", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "eventlog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "cryptsvc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "lanmanworkstation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "lanmanserver", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "schedule", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "winmgmt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "audiosrv", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "spooler", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "dhcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "dnscache", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "bfe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "mpssvc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "samss", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "termservice", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "bits", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(svc, "trustedinstaller", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Системные пользовательские службы Windows (Unistack / Per-User Services с суффиксами _LUID)
        if (svc.Contains("usersvc", StringComparison.OrdinalIgnoreCase) ||
            svc.StartsWith("cbdhsvc", StringComparison.OrdinalIgnoreCase) ||
            svc.StartsWith("cdpusersvc", StringComparison.OrdinalIgnoreCase) ||
            svc.StartsWith("wpnusersvc", StringComparison.OrdinalIgnoreCase) ||
            svc.StartsWith("onesyncsvc", StringComparison.OrdinalIgnoreCase) ||
            svc.StartsWith("pimstore", StringComparison.OrdinalIgnoreCase) ||
            svc.StartsWith("unistor", StringComparison.OrdinalIgnoreCase) ||
            svc.StartsWith("userdataaccess", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Проверяет, является ли запись критически важным компонентом ОС Windows (служба, задача, системный хост),
    /// отключение или удаление которого может дестабилизировать систему или вызвать BSoD.
    /// </summary>
    public static bool IsProtectedSystemEntry(StartupEntry? entry)
    {
        if (entry == null) return false;
        try
        {
            // 1. Системные бинарники из стоп-листа (svchost, lsass, services и др.)
            if (!string.IsNullOrEmpty(entry.FilePath) && IsProtectedProcessName(entry.FilePath))
                return true;

            if (string.IsNullOrEmpty(entry.FilePath) && !string.IsNullOrEmpty(entry.CommandLine) && IsProtectedProcessName(entry.CommandLine))
                return true;

            // 2. Критический системный реестр (Winlogon Userinit/Shell, BootExecute)
            if (entry.Location is EntryLocation.Winlogon or EntryLocation.BootExecute)
            {
                var name = entry.Name ?? "";
                if (name.Equals("Userinit", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Shell", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("BootExecute", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("autocheck", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 3. Службы Windows (системные или Unistack Per-User)
            if (entry.Location is EntryLocation.Service or EntryLocation.Driver)
            {
                var svc = entry.LocationDetail ?? entry.Name ?? "";
                if (IsCoreWindowsService(svc))
                    return true;
            }

            // 3. Задачи Windows из планировщика (\Microsoft\Windows\*)
            if (entry.Location == EntryLocation.TaskScheduler)
            {
                var taskPath = entry.LocationDetail ?? "";
                if (taskPath.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // 4. Системные WMI-подписки (SCM Event Log, BTreeNodeFilter и др.)
            if (entry.Location == EntryLocation.WmiEventSubscription)
            {
                if (IsProtectedWmiSubscription(entry.Name) || IsProtectedWmiSubscription(entry.LocationDetail))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Имена стандартных системных WMI-консьюмеров и фильтров Windows, удаление которых нарушает работу ОС.
    /// </summary>
    public static readonly HashSet<string> ProtectedWmiSubscriptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCM Event Log Consumer",
        "SCM Event Log Filter",
        "BTreeNodeFilter",
        "BTreeNodeConsumer",
        "TSLogonFilter",
        "TSLogonConsumer",
        "RAEventLogFilter",
        "RAEventLogConsumer",
        "KernCapFilter",
        "KernCapConsumer"
    };

    /// <summary>
    /// Проверяет, является ли WMI-консьюмер или фильтр защищённым системным компонентом Windows.
    /// </summary>
    public static bool IsProtectedWmiSubscription(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        return ProtectedWmiSubscriptionNames.Contains(trimmed);
    }
}

