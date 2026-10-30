using System;
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>
/// Состояние доступа приложения к сетевому трафику.
/// </summary>
public enum AccessControlState
{
    /// <summary>🟢 Трафик разрешен (белый список, разрешающее правило брандмауэра).</summary>
    Allowed = 1,

    /// <summary>🟡 Интерактивный режим по запросу (алерты при новых подключениях).</summary>
    Prompt = 2,

    /// <summary>🔴 Трафик заблокирован (черный список, блокирующее правило брандмауэра).</summary>
    Blocked = 3
}

/// <summary>
/// Область действия правила контроля доступа.
/// </summary>
public enum AccessRuleScope
{
    /// <summary>Постоянное системное правило (переживает перезагрузки).</summary>
    Permanent = 0,

    /// <summary>Сессионное правило до перезагрузки ПК или службы.</summary>
    UntilRestart = 1,

    /// <summary>Временное правило с автоматической экспирацией по таймеру.</summary>
    TimedTtl = 2
}

/// <summary>
/// Уровень системной защищенности исполняемого файла от случайной блокировки.
/// </summary>
public enum ProcessProtectionLevel
{
    /// <summary>Обычное стороннее приложение пользователя.</summary>
    None = 0,

    /// <summary>Защищенная подсистема Windows (требуется модальное подтверждение).</summary>
    HighRisk = 1,

    /// <summary>Критическое ядро ОС (блокировка аппаратно запрещена в UI).</summary>
    Critical = 2
}

/// <summary>
/// Источник сетевого запрета или ограничения.
/// </summary>
public enum BlockSourceType
{
    /// <summary>Ограничений нет.</summary>
    None = 0,

    /// <summary>Правило Windows Advanced Firewall (GPO, ручное, стороннее).</summary>
    WindowsFirewall = 1,

    /// <summary>Правило чёрного списка StartupMonitor.</summary>
    StartupMonitorBlacklist = 2,

    /// <summary>Активный бан подсистемы защиты портов Port Defender.</summary>
    PortDefenderBan = 3,

    /// <summary>Глобальный режим изоляции Killswitch.</summary>
    KillswitchActive = 4,

    /// <summary>Сессионное блокирование текущего пользователя.</summary>
    SessionBlock = 5
}

/// <summary>
/// Консолидированный DTO элемент Матрицы контроля доступа приложения.
/// </summary>
public sealed class AppAccessMatrixItemDto
{
    /// <summary>Полный канонический путь к исполняемому файлу на диске.</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>Имя исполняемого файла (например, chrome.exe).</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Отображаемое имя продукта или описание из PE-заголовка.</summary>
    public string ProductDescription { get; set; } = string.Empty;

    /// <summary>Текущее состояние доступа [🟢 Разрешено] / [🟡 По запросу] / [🔴 Заблокировано].</summary>
    public AccessControlState State { get; set; } = AccessControlState.Prompt;

    /// <summary>Область действия правила (Постоянно / До перезагрузки / По таймеру).</summary>
    public AccessRuleScope Scope { get; set; } = AccessRuleScope.Permanent;

    /// <summary>Время окончания действия временного правила (UTC).</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Уровень системной защиты процесса от случайной блокировки.</summary>
    public ProcessProtectionLevel ProtectionLevel { get; set; } = ProcessProtectionLevel.None;

    /// <summary>Источник текущего сетевого запрета (если применимо).</summary>
    public BlockSourceType BlockSource { get; set; } = BlockSourceType.None;

    /// <summary>Количество активных сетевых сокетов в данный момент.</summary>
    public int ActiveSocketsCount { get; set; }

    /// <summary>Флаг физического присутствия файла на диске.</summary>
    public bool ExistsOnDisk { get; set; } = true;

    /// <summary>Размер исполняемого файла в байтах.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Дата последней модификации файла.</summary>
    public DateTime? LastModifiedUtc { get; set; }
}

/// <summary>
/// Запрос на изменение политики доступа приложения через IPC.
/// </summary>
public sealed class SetAppAccessStateRequest
{
    /// <summary>Полный путь к приложению.</summary>
    public string ApplicationPath { get; set; } = string.Empty;

    /// <summary>Имя процесса.</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Целевое состояние доступа (Allowed / Prompt / Blocked).</summary>
    public AccessControlState TargetState { get; set; } = AccessControlState.Prompt;

    /// <summary>Область действия правила.</summary>
    public AccessRuleScope Scope { get; set; } = AccessRuleScope.Permanent;

    /// <summary>Время действия в минутах (если TimedTtl).</summary>
    public int TtlMinutes { get; set; } = 0;

    public SetAppAccessStateRequest() { }

    public SetAppAccessStateRequest(string appPath, string procName, AccessControlState targetState, AccessRuleScope scope = AccessRuleScope.Permanent, int ttlMinutes = 0)
    {
        ApplicationPath = appPath ?? string.Empty;
        ProcessName = procName ?? string.Empty;
        TargetState = targetState;
        Scope = scope;
        TtlMinutes = ttlMinutes;
    }
}


/// <summary>
/// Утилита классификации и оценки уровня системной защиты процессов Windows.
/// </summary>
public static class ProcessProtectionHelper
{
    private static readonly System.Collections.Generic.HashSet<string> CriticalCoreProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "ntoskrnl.exe", "svchost.exe", "lsass.exe", "services.exe",
        "smss.exe", "csrss.exe", "wininit.exe", "StartupMonitor.Service.exe", "StartupMonitor.UI.exe"
    };

    private static readonly System.Collections.Generic.HashSet<string> ProtectedSubsystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "spoolsv.exe", "dwm.exe", "ctfmon.exe", "RuntimeBroker.exe",
        "sihost.exe", "SearchHost.exe", "StartMenuExperienceHost.exe", "Widgets.exe"
    };

    /// <summary>
    /// Определяет уровень защиты процесса по его имени и пути.
    /// </summary>
    /// <param name="processName">Имя процесса.</param>
    /// <param name="processPath">Полный путь к исполняемому файлу.</param>
    /// <returns>Уровень защиты (Critical, HighRisk, None).</returns>
    public static ProcessProtectionLevel EvaluateProtectionLevel(string? processName, string? processPath)
    {
        string name = !string.IsNullOrWhiteSpace(processName)
            ? processName.Trim()
            : System.IO.Path.GetFileName(processPath ?? string.Empty);

        if (string.IsNullOrWhiteSpace(name))
            return ProcessProtectionLevel.None;

        if (CriticalCoreProcesses.Contains(name))
            return ProcessProtectionLevel.Critical;

        if (ProtectedSubsystems.Contains(name))
            return ProcessProtectionLevel.HighRisk;

        return ProcessProtectionLevel.None;
    }
}
