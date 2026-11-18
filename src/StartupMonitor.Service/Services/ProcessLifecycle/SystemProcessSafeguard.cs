using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Интерфейс службы защиты критических системных процессов и процессов StartupMonitor.
/// Реализует проверку на мимикрию (Masquerading) путем верификации путей к исполняемым файлам.
/// </summary>
public interface ISystemProcessSafeguard
{
    /// <summary>
    /// Определяет, является ли процесс критически важным для системы или для работы StartupMonitor.
    /// Защищенные процессы нельзя принудительно завершать или замораживать.
    /// </summary>
    /// <param name="pid">Идентификатор процесса (PID).</param>
    /// <param name="processName">Имя процесса.</param>
    /// <param name="imagePath">Полный путь к исполняемому файлу процесса.</param>
    /// <returns>True, если процесс защищен (критический системный или StartupMonitor). False — если процесс подлежит проверке/действию.</returns>
    bool IsCriticalSystemProcess(int pid, string? processName, string? imagePath);
}

/// <summary>
/// Реализация защиты системных процессов с защитой от мимикрии (Masquerading).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemProcessSafeguard : ISystemProcessSafeguard
{
    private readonly ILogger<SystemProcessSafeguard>? _logger;

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ядро и системные службы Windows
        "ntoskrnl.exe", "smss.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe",
        "svchost.exe", "fontdrvhost.exe", "dwm.exe", "explorer.exe", "winlogon.exe",
        "spoolsv.exe", "conhost.exe", "taskhostw.exe", "searchindexer.exe", "runtimebroker.exe",
        "startmenuexperiencehost.exe", "shellexperiencehost.exe", "userinit.exe",

        // Самозащита StartupMonitor (Service + UI + CLI)
        "smon.exe", "startupmonitor.service.exe", "startupmonitor.ui.exe"
    };

    private static readonly Dictionary<string, string> StrictPathRequirements = new(StringComparer.OrdinalIgnoreCase)
    {
        { "csrss.exe", @"\system32\" },
        { "svchost.exe", @"\system32\" },
        { "services.exe", @"\system32\" },
        { "lsass.exe", @"\system32\" }
    };

    /// <summary>SystemProcessSafeguard: Конструктор с логированием.</summary>
    /// <param name="logger">Входные данные: логгер.</param>
    public SystemProcessSafeguard(ILogger<SystemProcessSafeguard>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsCriticalSystemProcess(int pid, string? processName, string? imagePath)
    {
        // PID 4 = System
        if (pid <= 0 || pid == 4)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return true; // Fail-safe: неизвестный процесс защищаем по умолчанию
        }

        var normalizedName = processName.Trim();
        if (!normalizedName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName += ".exe";
        }

        // 1. Проверка по белому списку критических процессов
        if (!ProtectedProcessNames.Contains(normalizedName))
        {
            return false;
        }

        // 2. Анти-мимикрия (Masquerading Check) для критичных системных процессов
        if (StrictPathRequirements.TryGetValue(normalizedName, out var requiredDirFragment))
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                // Нет доступного пути из-за прав доступа -> системный защищенный процесс
                return true;
            }

            try
            {
                var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                var expectedDir = Path.GetFullPath(Path.Combine(systemRoot, requiredDirFragment.Trim('\\'))).TrimEnd('\\').ToLowerInvariant();
                var actualDir = Path.GetDirectoryName(Path.GetFullPath(imagePath))?.TrimEnd('\\').ToLowerInvariant() ?? string.Empty;

                if (!actualDir.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Обнаружена мимикрия: процесс {Name} запущен из {Actual} вместо {Expected}",
                        normalizedName, actualDir, expectedDir);
                    // Процесс имитирует имя системного, но находится в посторонней папке (AppData/Temp) -> НЕ защищаем!
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка проверки пути анти-мимикрии для {Name}", normalizedName);
                return true; // При ошибке чтения пути безопасный откат
            }
        }

        return true;
    }
}
