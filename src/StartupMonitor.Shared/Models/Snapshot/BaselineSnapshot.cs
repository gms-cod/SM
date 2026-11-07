namespace StartupMonitor.Shared.Models.Snapshot;

/// <summary>
/// Контрольный снимок (Baseline Snapshot) состояния точек автозапуска системы.
/// Позволяет сохранять эталонное состояние и выявлять любые изменения («Было — Стало»).
/// </summary>
public class BaselineSnapshot
{
    /// <summary>Уникальный идентификатор снимка.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Пользовательское наименование снимка.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Необязательное описание или комментарий к снимку.</summary>
    public string? Description { get; set; }

    /// <summary>Дата и время создания снимка (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Имя компьютера, на котором снят снимок.</summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>Версия операционной системы Windows.</summary>
    public string OsVersion { get; set; } = Environment.OSVersion.ToString();

    /// <summary>Список сохранённых записей автозапуска.</summary>
    public List<StartupEntrySnapshotItem> Entries { get; set; } = [];

    /// <summary>Количество сохранённых записей в снимке.</summary>
    public int EntriesCount => Entries?.Count ?? 0;

    /// <summary>Создает новый контрольный снимок автозапуска из списка записей.</summary>
    public static BaselineSnapshot Create(string name, string? description, IEnumerable<StartupEntry> entries)
    {
        return new BaselineSnapshot
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.ToString(),
            Entries = entries.Select(StartupEntrySnapshotItem.FromStartupEntry).ToList()
        };
    }
}

