using System;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>
/// Вердикт системы защиты по сетевому подключению.
/// </summary>
public enum NetworkConnectionVerdict
{
    /// <summary>Ожидает решения пользователя (всплывающий алерт).</summary>
    PendingAlert,
    /// <summary>Разрешено существующим правилом брандмауэра Windows.</summary>
    Allowed,
    /// <summary>Заблокировано существующим правилом брандмауэра Windows.</summary>
    Blocked,
    /// <summary>Временно разрешено пользователем на текущую сессию.</summary>
    AllowSession,
    /// <summary>Временно заблокировано пользователем на текущую сессию.</summary>
    BlockSession,
    /// <summary>Разрешено, так как процесс находится в белом списке.</summary>
    Whitelisted,
    /// <summary>Заблокировано, так как процесс находится в чёрном списке.</summary>
    Blacklisted,
    /// <summary>Создано постоянное правило брандмауэра по решению пользователя.</summary>
    RuleCreated
}

/// <summary>
/// Запись журнала истории сетевых подключений программ.
/// Содержит полную телеметрию соединения, временную метку и итоговый вердикт безопасности.
/// </summary>
public sealed class NetworkConnectionHistoryItem
{
    /// <summary>Уникальный идентификатор записи истории.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Временная метка обнаружения подключения (UTC).</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Локальное время обнаружения для отображения в интерфейсе.</summary>
    public DateTime LocalTimestamp => Timestamp.Kind == DateTimeKind.Utc
        ? Timestamp.ToLocalTime()
        : Timestamp;

    /// <summary>Идентификатор процесса (PID).</summary>
    public int ProcessId { get; set; }

    /// <summary>Имя исполняемого файла процесса.</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Полный абсолютный путь к исполняемому файлу программы на диске.</summary>
    public string ProcessPath { get; set; } = string.Empty;

    /// <summary>Используемый транспортный сетевой протокол (TCP или UDP).</summary>
    public string Protocol { get; set; } = "TCP";

    /// <summary>Локальный IP-адрес конечной точки.</summary>
    public string LocalAddress { get; set; } = string.Empty;

    /// <summary>Локальный порт.</summary>
    public int LocalPort { get; set; }

    /// <summary>Удаленный IP-адрес назначения.</summary>
    public string RemoteAddress { get; set; } = string.Empty;

    /// <summary>Удаленный порт назначения.</summary>
    public int RemotePort { get; set; }

    /// <summary>Название страны удаленного узла по данным базы GeoIP.</summary>
    public string CountryName { get; set; } = string.Empty;

    /// <summary>Эмодзи-флаг страны назначения.</summary>
    public string CountryFlag { get; set; } = "🌐";

    /// <summary>Вердикт подсистемы сетевой безопасности.</summary>
    public NetworkConnectionVerdict Verdict { get; set; } = NetworkConnectionVerdict.PendingAlert;

    /// <summary>Направление трафика (Outbound — исходящий, Inbound — входящий).</summary>
    public string Direction { get; set; } = "Outbound";

    /// <summary>Имя сработавшего правила или пояснение вердикта.</summary>
    public string Details { get; set; } = string.Empty;
}

