namespace StartupMonitor.Shared.Models.Snapshot;

/// <summary>
/// Результат сравнительного анализа текущего состояния автозапуска с эталонным снимком («Было — Стало»).
/// </summary>
public class SnapshotComparisonResult
{
    /// <summary>Идентификатор эталонного снимка.</summary>
    public string SnapshotId { get; set; } = string.Empty;

    /// <summary>Имя эталонного снимка.</summary>
    public string SnapshotName { get; set; } = string.Empty;

    /// <summary>Дата создания эталонного снимка (UTC).</summary>
    public DateTime SnapshotCreatedAt { get; set; }

    /// <summary>Время выполнения сравнения (UTC).</summary>
    public DateTime ComparedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Общее количество записей в эталонном снимке.</summary>
    public int TotalBaselineEntries { get; set; }

    /// <summary>Общее количество записей в текущей системе.</summary>
    public int TotalCurrentEntries { get; set; }

    /// <summary>Новые записи, которые появились в системе после создания снимка.</summary>
    public List<StartupEntry> AddedEntries { get; set; } = [];

    /// <summary>Записи, которые присутствовали в снимке, но были удалены из системы.</summary>
    public List<StartupEntrySnapshotItem> RemovedEntries { get; set; } = [];

    /// <summary>Записи, параметры которых изменились со времени создания снимка.</summary>
    public List<SnapshotModifiedEntryItem> ModifiedEntries { get; set; } = [];

    /// <summary>Количество записей, оставшихся без каких-либо изменений.</summary>
    public int UnchangedCount { get; set; }

    /// <summary>Флаг наличия хотя бы одного отличия от снимка.</summary>
    public bool HasDifferences => AddedEntries.Count > 0 || RemovedEntries.Count > 0 || ModifiedEntries.Count > 0;
}

