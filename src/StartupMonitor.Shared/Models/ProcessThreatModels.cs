using System;
using System.Collections.Generic;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Действие, применяемое к активному процессу при обнаружении критической угрозы.
/// </summary>
public enum ProcessThreatAction
{
    /// <summary>Только выводить предупреждение (UI Toast, push, запись в журнал).</summary>
    AlertOnly = 0,

    /// <summary>Автоматически завершать (Kill) вредоносный процесс.</summary>
    AutoKill = 1,

    /// <summary>Автоматически замораживать (Suspend) процесс через NtSuspendProcess.</summary>
    AutoSuspend = 2
}

/// <summary>
/// DTO конфигурации защиты активных процессов (ИИ и Kaspersky OpenTIP).
/// </summary>
public class ProcessThreatConfigDto
{
    /// <summary>Включена ли общая проверка процессов на вредоносность.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Включена ли проверка через Kaspersky OpenTIP.</summary>
    public bool KasperskyEnabled { get; set; } = true;

    /// <summary>Включен ли интеллектуальный анализ поведения через ИИ.</summary>
    public bool AiEnabled { get; set; } = true;

    /// <summary>Действие при обнаружении критической угрозы.</summary>
    public ProcessThreatAction Action { get; set; } = ProcessThreatAction.AlertOnly;

    /// <summary>Порог риска ИИ (0-100), при котором срабатывает действие.</summary>
    public int RiskThreshold { get; set; } = 80;

    /// <summary>Белый список доверенных процессов (имена или пути).</summary>
    public List<string> Whitelist { get; set; } = new();

    /// <summary>Создает независимую копию конфигурации защиты процессов.</summary>
    public ProcessThreatConfigDto Clone() => new()
    {
        Enabled = Enabled,
        KasperskyEnabled = KasperskyEnabled,
        AiEnabled = AiEnabled,
        Action = Action,
        RiskThreshold = RiskThreshold,
        Whitelist = [..Whitelist]
    };
}

/// <summary>
/// Полезная нагрузка IPC-события об обнаружении подозрительного или вредоносного процесса.
/// </summary>
public record ProcessThreatAlertPayload(
    int ProcessId,
    string ProcessName,
    string FilePath,
    string Sha256,
    string KasperskyZone,
    string? KasperskyDetection,
    int? AiRiskScore,
    string? AiVerdict,
    string? AiExplanation,
    string ActionTaken,
    DateTime TimestampUtc
);
