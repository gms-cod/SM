namespace StartupMonitor.Shared.Models.Snapshot;

/// <summary>
/// Описывает изменения параметров записи автозапуска по сравнению со снимком.
/// </summary>
public class SnapshotModifiedEntryItem
{
    /// <summary>Текущее актуальное состояние записи.</summary>
    public StartupEntry CurrentEntry { get; set; } = new();

    /// <summary>Состояние записи на момент создания эталонного снимка.</summary>
    public StartupEntrySnapshotItem BaselineEntry { get; set; } = new();

    /// <summary>Список изменившихся свойств (например: CommandLine, FilePath, Status, DelaySeconds, Sha256).</summary>
    public List<string> ChangedProperties { get; set; } = [];

    /// <summary>Старые значения свойств в снимке.</summary>
    public Dictionary<string, string?> OldValues { get; set; } = [];

    /// <summary>Новые актуальные значения свойств.</summary>
    public Dictionary<string, string?> NewValues { get; set; } = [];

    /// <summary>Краткая сводка об обнаруженных различиях.</summary>
    public string Summary => ChangedProperties.Count > 0
        ? string.Join(", ", ChangedProperties)
        : string.Empty;
}

