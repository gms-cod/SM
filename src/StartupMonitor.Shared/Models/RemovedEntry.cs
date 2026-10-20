namespace StartupMonitor.Shared.Models;

/// <summary>Бэкап записи, удалённой из системы (для возможности восстановления).</summary>
public class RemovedEntry
{
    /// <summary>Уникальный идентификатор бэкапа.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Id оригинальной записи StartupEntry.</summary>
    public string OriginalEntryId { get; set; } = string.Empty;
    /// <summary>Имя программы/скрипта.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Путь к исполняемому файлу.</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>Издатель (цифровая подпись).</summary>
    public string Publisher { get; set; } = string.Empty;
    /// <summary>Местоположение записи в системе.</summary>
    public EntryLocation Location { get; set; }
    /// <summary>Детали местоположения (путь в реестре, имя задачи, и т.д.).</summary>
    public string LocationDetail { get; set; } = string.Empty;
    /// <summary>Дата первого обнаружения записи.</summary>
    public DateTime DetectedAt { get; set; }
    /// <summary>Дата удаления записи из системы.</summary>
    public DateTime RemovedAt { get; set; } = DateTime.UtcNow;
    /// <summary>JSON-строка с данными, необходимыми для восстановления записи.</summary>
    public string RestorePayload { get; set; } = string.Empty;
    /// <summary>Причина удаления: "UserRemoved", "LockdownBlocked", "Blacklist".</summary>
    public string RemovalReason { get; set; } = "UserRemoved";
    /// <summary>Создает независимую копию бэкапа удаленной записи.</summary>
    public RemovedEntry Clone() => (RemovedEntry)MemberwiseClone();
}