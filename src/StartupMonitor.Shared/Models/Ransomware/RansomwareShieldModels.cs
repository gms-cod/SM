using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models.Ransomware;

/// <summary>
/// Тип триггера обнаружения активности программы-вымогателя (Ransomware).
/// </summary>
public enum RansomwareTriggerType
{
    /// <summary>
    /// Попытка модификации или удаления теневых копий тома (VSS Tampering).
    /// </summary>
    VssTamper = 0,

    /// <summary>
    /// Срабатывание файла-приманки (Canary Trap modified/deleted/renamed).
    /// </summary>
    CanaryModified = 1,

    /// <summary>
    /// Всплеск энтропии данных в операциях записи файлов (Entropy Burst).
    /// </summary>
    EntropyBurst = 2,

    /// <summary>
    /// Обнаружение генерации записки о выкупе (Ransom Note).
    /// </summary>
    RansomNote = 3
}

/// <summary>
/// Тип защитного действия, предпринятого против вредоносного процесса.
/// </summary>
public enum RansomwareActionType
{
    /// <summary>
    /// Процесс мгновенно заморожен в ядре (NtSuspendProcess).
    /// </summary>
    Suspended = 0,

    /// <summary>
    /// Процесс изолирован от сети через брандмауэр WFP.
    /// </summary>
    NetworkQuarantined = 1,

    /// <summary>
    /// Процесс принудительно завершён (Kill/Terminate).
    /// </summary>
    Terminated = 2,

    /// <summary>
    /// Повреждённые файлы восстановлены из теневых копий VSS.
    /// </summary>
    Restored = 3,

    /// <summary>
    /// Процесс разморожен (NtResumeProcess) по решению пользователя.
    /// </summary>
    Unfrozen = 4,

    /// <summary>
    /// Операция процесса была превентивно заблокирована.
    /// </summary>
    Blocked = 5
}

/// <summary>
/// Конфигурация подсистемы анти-рансомваре защиты (Ransomware Shield).
/// </summary>
public sealed class RansomwareShieldConfigDto
{
    /// <summary>
    /// Общий статус включения щита защиты от программ-вымогателей.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Защита теневых копий Windows (Volume Shadow Copies) от удаления через CLI и WMI.
    /// </summary>
    [JsonPropertyName("protect_vss_shadow_copies")]
    public bool ProtectVssShadowCopies { get; set; } = true;

    /// <summary>
    /// Использование скрытых файлов-приманок (Canary Traps) для выявления начала шифрования.
    /// </summary>
    [JsonPropertyName("enable_canary_traps")]
    public bool EnableCanaryTraps { get; set; } = true;

    /// <summary>
    /// Автоматическая заморозка атакующего процесса через ядро (NtSuspendProcess).
    /// </summary>
    [JsonPropertyName("auto_suspend_process")]
    public bool AutoSuspendProcess { get; set; } = true;

    /// <summary>
    /// Мгновенная сетевая изоляция атакующего процесса при фиксации атаки.
    /// </summary>
    [JsonPropertyName("isolate_network_on_attack")]
    public bool IsolateNetworkOnAttack { get; set; } = true;

    /// <summary>
    /// Автоматический откат повреждённых файлов из теневых копий томов.
    /// </summary>
    [JsonPropertyName("auto_rollback_affected_files")]
    public bool AutoRollbackAffectedFiles { get; set; } = true;

    /// <summary>
    /// Доверие доверенным подписанным системным процессам (Microsoft, Windows).
    /// </summary>
    [JsonPropertyName("whitelist_trusted_signers")]
    public bool WhitelistTrustedSigners { get; set; } = true;

    /// <summary>
    /// Создаёт глубокую копию текущей конфигурации щита.
    /// </summary>
    /// <returns>Клонированный экземпляр конфигурации.</returns>
    public RansomwareShieldConfigDto Clone()
    {
        return new RansomwareShieldConfigDto
        {
            Enabled = Enabled,
            ProtectVssShadowCopies = ProtectVssShadowCopies,
            EnableCanaryTraps = EnableCanaryTraps,
            AutoSuspendProcess = AutoSuspendProcess,
            IsolateNetworkOnAttack = IsolateNetworkOnAttack,
            AutoRollbackAffectedFiles = AutoRollbackAffectedFiles,
            WhitelistTrustedSigners = WhitelistTrustedSigners
        };
    }
}

/// <summary>
/// Статистика и показатели работы подсистемы анти-рансомваре защиты.
/// </summary>
public sealed class RansomwareShieldStatsDto
{
    /// <summary>
    /// Активна ли служба защиты в данный момент.
    /// </summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Количество активных расставленных файлов-ловушек (Canary Traps).
    /// </summary>
    [JsonPropertyName("active_traps_count")]
    public int ActiveTrapsCount { get; set; }

    /// <summary>
    /// Количество защищаемых томов дисков со снимками VSS.
    /// </summary>
    [JsonPropertyName("protected_volumes_count")]
    public int ProtectedVolumesCount { get; set; }

    /// <summary>
    /// Общее количество заблокированных атак шифровальщиков.
    /// </summary>
    [JsonPropertyName("blocked_attacks_count")]
    public int BlockedAttacksCount { get; set; }

    /// <summary>
    /// Количество процессов, находящихся в замороженном состоянии.
    /// </summary>
    [JsonPropertyName("suspended_processes_count")]
    public int SuspendedProcessesCount { get; set; }

    /// <summary>
    /// Общее количество успешно восстановленных файлов из теневых копий.
    /// </summary>
    [JsonPropertyName("restored_files_count")]
    public int RestoredFilesCount { get; set; }

    /// <summary>
    /// Время последней зафиксированной атаки в формате UTC.
    /// </summary>
    [JsonPropertyName("last_attack_time_utc")]
    public DateTime? LastAttackTimeUtc { get; set; }

    /// <summary>
    /// Список недавних инцидентов и срабатываний защиты.
    /// </summary>
    [JsonPropertyName("recent_incidents")]
    public List<RansomwareIncidentDto> RecentIncidents { get; set; } = new();
}

/// <summary>
/// Сведения об инциденте атаки программы-вымогателя.
/// </summary>
public sealed class RansomwareIncidentDto
{
    /// <summary>
    /// Уникальный идентификатор инцидента.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Временная метка обнаружения инцидента в формате UTC.
    /// </summary>
    [JsonPropertyName("timestamp_utc")]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Идентификатор процесса (PID), вызвавшего срабатывание.
    /// </summary>
    [JsonPropertyName("process_id")]
    public int ProcessId { get; set; }

    /// <summary>
    /// Имя исполняемого файла процесса.
    /// </summary>
    [JsonPropertyName("process_name")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Полный путь к исполняемому файлу процесса.
    /// </summary>
    [JsonPropertyName("process_path")]
    public string ProcessPath { get; set; } = string.Empty;

    /// <summary>
    /// Причина срабатывания детектора (тип триггера).
    /// </summary>
    [JsonPropertyName("trigger_reason")]
    public RansomwareTriggerType TriggerReason { get; set; }

    /// <summary>
    /// Выполненное действие защиты.
    /// </summary>
    [JsonPropertyName("action_taken")]
    public RansomwareActionType ActionTaken { get; set; }

    /// <summary>
    /// Список файлов, затронутых или атакованных вредоносным процессом.
    /// </summary>
    [JsonPropertyName("affected_files")]
    public List<string> AffectedFiles { get; set; } = new();
}
