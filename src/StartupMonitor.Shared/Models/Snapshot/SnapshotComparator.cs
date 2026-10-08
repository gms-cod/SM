namespace StartupMonitor.Shared.Models.Snapshot;

/// <summary>
/// Выполняет дифференциальное сравнение (Diff) между эталонным снимком и текущим состоянием записей автозапуска.
/// </summary>
public static class SnapshotComparator
{
    /// <summary>
    /// Сравнивает эталонный снимок с текущим набором записей автозапуска.
    /// </summary>
    public static SnapshotComparisonResult Compare(BaselineSnapshot baseline, IReadOnlyList<StartupEntry> currentEntries)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(currentEntries);

        var result = new SnapshotComparisonResult
        {
            SnapshotId = baseline.Id,
            SnapshotName = baseline.Name,
            SnapshotCreatedAt = baseline.CreatedAt,
            ComparedAt = DateTime.UtcNow,
            TotalBaselineEntries = baseline.Entries.Count,
            TotalCurrentEntries = currentEntries.Count
        };

        // Индексация эталона по Id и вторичному ключу (Location + Detail + Name)
        var baselineById = new Dictionary<string, StartupEntrySnapshotItem>(StringComparer.OrdinalIgnoreCase);
        var baselineByKey = new Dictionary<string, StartupEntrySnapshotItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var b in baseline.Entries)
        {
            if (!string.IsNullOrEmpty(b.Id))
            {
                baselineById[b.Id] = b;
            }
            var key = MakeCompositeKey(b.Location, b.LocationDetail, b.Name);
            baselineByKey[key] = b;
        }

        var matchedBaselineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Анализируем текущие записи: выявляем добавленные и модифицированные
        foreach (var current in currentEntries)
        {
            StartupEntrySnapshotItem? baselineMatch = null;

            if (!string.IsNullOrEmpty(current.Id) && baselineById.TryGetValue(current.Id, out var byIdMatch))
            {
                baselineMatch = byIdMatch;
            }
            else
            {
                var key = MakeCompositeKey(current.Location, current.LocationDetail, current.Name);
                if (baselineByKey.TryGetValue(key, out var byKeyMatch))
                {
                    baselineMatch = byKeyMatch;
                }
            }

            if (baselineMatch == null)
            {
                // Записи не было в эталонном снимке — новая!
                result.AddedEntries.Add(current);
            }
            else
            {
                matchedBaselineIds.Add(baselineMatch.Id);
                var diff = DetectModifications(current, baselineMatch);
                if (diff.ChangedProperties.Count > 0)
                {
                    result.ModifiedEntries.Add(diff);
                }
                else
                {
                    result.UnchangedCount++;
                }
            }
        }

        // Анализируем эталон: выявляем удалённые записи
        foreach (var b in baseline.Entries)
        {
            if (!matchedBaselineIds.Contains(b.Id))
            {
                result.RemovedEntries.Add(b);
            }
        }

        return result;
    }

    private static string MakeCompositeKey(EntryLocation location, string? detail, string? name)
    {
        var normDetail = JsonDataStore.NormalizeLocationDetail(detail ?? string.Empty);
        return $"{location}|{normDetail}|{name?.Trim() ?? string.Empty}".ToLowerInvariant();
    }

    private static SnapshotModifiedEntryItem DetectModifications(StartupEntry current, StartupEntrySnapshotItem baseline)
    {
        var mod = new SnapshotModifiedEntryItem
        {
            CurrentEntry = current,
            BaselineEntry = baseline
        };

        // Сравнение командной строки
        var curCmd = current.CommandLine?.Trim() ?? string.Empty;
        var baseCmd = baseline.CommandLine?.Trim() ?? string.Empty;
        if (!string.Equals(curCmd, baseCmd, StringComparison.Ordinal))
        {
            mod.ChangedProperties.Add("CommandLine");
            mod.OldValues["CommandLine"] = baseCmd;
            mod.NewValues["CommandLine"] = curCmd;
        }

        // Сравнение пути файла
        var curPath = current.FilePath?.Trim() ?? string.Empty;
        var basePath = baseline.FilePath?.Trim() ?? string.Empty;
        if (!string.Equals(curPath, basePath, StringComparison.OrdinalIgnoreCase))
        {
            mod.ChangedProperties.Add("FilePath");
            mod.OldValues["FilePath"] = basePath;
            mod.NewValues["FilePath"] = curPath;
        }

        // Сравнение статуса
        if (current.Status != baseline.Status)
        {
            mod.ChangedProperties.Add("Status");
            mod.OldValues["Status"] = baseline.Status.ToString();
            mod.NewValues["Status"] = current.Status.ToString();
        }

        // Сравнение задержки запуска (актуально только для отложенных записей)
        if ((current.Status == EntryStatus.Delayed || baseline.Status == EntryStatus.Delayed) &&
            current.DelaySeconds != baseline.DelaySeconds)
        {
            mod.ChangedProperties.Add("DelaySeconds");
            mod.OldValues["DelaySeconds"] = baseline.DelaySeconds.ToString();
            mod.NewValues["DelaySeconds"] = current.DelaySeconds.ToString();
        }

        // Сравнение хеша файла
        if (!string.IsNullOrEmpty(current.SHA256) && !string.IsNullOrEmpty(baseline.Sha256) &&
            !string.Equals(current.SHA256, baseline.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            mod.ChangedProperties.Add("SHA256");
            mod.OldValues["SHA256"] = baseline.Sha256;
            mod.NewValues["SHA256"] = current.SHA256;
        }

        return mod;
    }
}
