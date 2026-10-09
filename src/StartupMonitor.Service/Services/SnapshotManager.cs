using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Snapshot;

namespace StartupMonitor.Service.Services;

/// <summary>
/// Управляет созданием, хранением и сравнением контрольных снимков состояния автозапуска системы.
/// </summary>
public class SnapshotManager
{
    private readonly JsonDataStore _store;
    private readonly ILogger<SnapshotManager> _logger;

    /// <summary>SnapshotManager: Выполняет операцию SnapshotManager.</summary>
    /// <param name="store">Входные данные: значение параметра store (JsonDataStore).</param>
    /// <param name="logger">Входные данные: значение параметра logger (ILogger<SnapshotManager>).</param>
    public SnapshotManager(JsonDataStore store, ILogger<SnapshotManager> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт новый контрольный снимок автозапуска из текущей конфигурации системы.
    /// </summary>
    public BaselineSnapshot CreateSnapshot(string? name = null, string? description = null)
    {
        var cfg = _store.Load();
        var entries = cfg.Entries.ToList();

        var snapshotName = !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : $"Снимок {DateTime.Now:dd.MM.yyyy HH:mm}";

        var snapshot = new BaselineSnapshot
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = snapshotName,
            Description = description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.ToString(),
            Entries = entries.Select(StartupEntrySnapshotItem.FromStartupEntry).ToList()
        };

        _store.SaveSnapshot(snapshot);
        _logger.LogInformation("Создан контрольный снимок автозапуска '{Name}' (ID: {Id}, записей: {Count})",
            snapshot.Name, snapshot.Id, snapshot.EntriesCount);

        return snapshot;
    }

    /// <summary>
    /// Возвращает список всех сохранённых снимков автозапуска.
    /// </summary>
    public List<BaselineSnapshot> GetSnapshots()
    {
        return _store.GetSnapshots();
    }

    /// <summary>
    /// Удаляет снимок по указанному идентификатору.
    /// </summary>
    public bool DeleteSnapshot(string snapshotId)
    {
        var result = _store.DeleteSnapshot(snapshotId);
        if (result)
        {
            _logger.LogInformation("Удалён контрольный снимок ID: {Id}", snapshotId);
        }
        return result;
    }

    /// <summary>
    /// Сравнивает текущее состояние системы с указанным снимком («Было — Стало»).
    /// </summary>
    public (bool Success, string? Error, SnapshotComparisonResult? Result) CompareWithSnapshot(string snapshotId)
    {
        var snapshot = _store.GetSnapshot(snapshotId);
        if (snapshot == null)
        {
            _logger.LogWarning("Запрос на сравнение с несуществующим снимком ID: {Id}", snapshotId);
            return (false, $"Контрольный снимок с ID '{snapshotId}' не найден.", null);
        }

        var cfg = _store.Load();
        var currentEntries = cfg.Entries.ToList();

        var diff = SnapshotComparator.Compare(snapshot, currentEntries);
        _logger.LogInformation("Выполнено сравнение со снимком '{Name}': +{Added} новых, -{Removed} удалено, ~{Modified} изменено, {Unchanged} без изменений",
            snapshot.Name, diff.AddedEntries.Count, diff.RemovedEntries.Count, diff.ModifiedEntries.Count, diff.UnchangedCount);

        return (true, null, diff);
    }
}

