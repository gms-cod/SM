using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models.Snapshot;

/// <summary>
/// Копия состояния записи автозапуска на момент создания контрольного снимка системы.
/// </summary>
public class StartupEntrySnapshotItem
{
    /// <summary>Уникальный стабильный идентификатор записи.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Отображаемое имя записи.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Тип расположения точки автозапуска.</summary>
    public EntryLocation Location { get; set; }

    /// <summary>Детали расположения (путь к ключу реестра, путь к папке и т.д.).</summary>
    public string LocationDetail { get; set; } = string.Empty;

    /// <summary>Исполняемый путь к файлу.</summary>
    public string? FilePath { get; set; }

    /// <summary>Полная командная строка запуска с аргументами.</summary>
    public string? CommandLine { get; set; }

    /// <summary>Издатель программы из цифровой подписи.</summary>
    public string? Publisher { get; set; }

    /// <summary>SHA-256 хеш исполняемого файла.</summary>
    public string? Sha256 { get; set; }

    /// <summary>Алиас SHA-256 хеша для обратной совместимости.</summary>
    [JsonIgnore]
    public string? SHA256 { get => Sha256; set => Sha256 = value; }

    /// <summary>Статус записи на момент снимка.</summary>
    public EntryStatus Status { get; set; } = EntryStatus.Enabled;

    /// <summary>Интервал задержки в секундах, если запись была отложена.</summary>
    public int DelaySeconds { get; set; } = 30;

    /// <summary>Время первоначального обнаружения записи.</summary>
    public DateTime DetectedAt { get; set; }

    /// <summary>Создает снимок записи из актуальной модели StartupEntry (алиас для FromStartupEntry).</summary>
    public static StartupEntrySnapshotItem FromEntry(StartupEntry entry) => FromStartupEntry(entry);

    /// <summary>Создает снимок записи из актуальной модели StartupEntry.</summary>
    public static StartupEntrySnapshotItem FromStartupEntry(StartupEntry entry)
    {
        return new StartupEntrySnapshotItem
        {
            Id = entry.Id,
            Name = entry.Name,
            Location = entry.Location,
            LocationDetail = entry.LocationDetail,
            FilePath = entry.FilePath,
            CommandLine = entry.CommandLine,
            Publisher = entry.Publisher,
            Sha256 = entry.SHA256,
            Status = entry.Status,
            DelaySeconds = entry.DelaySeconds,
            DetectedAt = entry.DetectedAt
        };
    }
}
