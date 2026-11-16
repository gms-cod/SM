namespace StartupMonitor.Shared.Models.Snapshot;

/// <summary>
/// Describes modifications to startup entry parameters compared to baseline snapshot.
/// </summary>
public class SnapshotModifiedEntryItem
{
    /// <summary>Current active entry state.</summary>
    public StartupEntry CurrentEntry { get; set; } = new();

    /// <summary>Entry state at baseline snapshot creation time.</summary>
    public StartupEntrySnapshotItem BaselineEntry { get; set; } = new();

    /// <summary>Список изменившихся свойств (например: CommandLine, FilePath, Status, DelaySeconds, Sha256).</summary>
    public List<string> ChangedProperties { get; set; } = [];

    /// <summary>Previous property values in snapshot.</summary>
    public Dictionary<string, string?> OldValues { get; set; } = [];

    /// <summary>New active property values.</summary>
    public Dictionary<string, string?> NewValues { get; set; } = [];

    /// <summary>Summary of detected differences.</summary>
    public string Summary => ChangedProperties.Count > 0
        ? string.Join(", ", ChangedProperties)
        : string.Empty;
}

