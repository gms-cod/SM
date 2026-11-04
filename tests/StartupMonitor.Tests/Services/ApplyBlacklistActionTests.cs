using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для ApplyBlacklistAction: проверка двух режимов — NotifyAndRemove (удаление файла)
/// и NotifyOnly (только пометка блокировки).
/// </summary>
public class ApplyBlacklistActionTests : IDisposable
{
    /// <summary>Временная директория для тестовых файлов.</summary>
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}");
    /// <summary>Хранилище данных для тестов.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Менеджер чёрного списка.</summary>
    private readonly BlacklistManager _manager;
    /// <summary>Сервис уведомлений (не используется, но требуется конструктором).</summary>
    private readonly NotificationService _notifications;

    public ApplyBlacklistActionTests()
    {
        _store = new JsonDataStore(Path.Combine(_dir, "startup-monitor.json"));
        _manager = new BlacklistManager(_store);
        _notifications = new NotificationService(new InitialScanTracker(1));
    }

    /// <summary>
    /// Проверяет, что NotifyAndRemove удаляет файл из системы, но запись остаётся как Blocked с бэкапом.
    /// </summary>
    [Fact]
    public void NotifyAndRemove_RemovesFileButKeepsEntryBlocked()
    {
        // Файл в "папке автозапуска"
        Directory.CreateDirectory(_dir);
        var appFile = Path.Combine(_dir, "badapp.exe");
        File.WriteAllText(appFile, "MZ fake");
        var entry = new StartupEntry
        {
            Name = "badapp.exe",
            FilePath = appFile,
            Location = EntryLocation.StartupFolder,
        };
        _store.AddEntry(entry);
        var rule = new BlacklistRule
        {
            Value = appFile,
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyAndRemove,
        };

        _manager.ApplyBlacklistAction(entry, rule, _store, _notifications);

        // Файл удалён из системы
        Assert.False(File.Exists(appFile));
        // Запись ОСТАЛАСЬ в списке как Blocked
        var stored = _store.FindEntry(e => e.Id == entry.Id);
        Assert.NotNull(stored);
        Assert.Equal(EntryStatus.Blocked, stored.Status);
        Assert.True(stored.FromBlacklist);
        // Бэкап для восстановления создан
        var removed = _store.GetRemovedEntries();
        Assert.Single(removed);
        Assert.Equal(entry.Id, removed[0].OriginalEntryId);
        Assert.NotEmpty(removed[0].RestorePayload);
    }

    /// <summary>
    /// Проверяет, что NotifyOnly только помечает запись как Blocked, не трогая файл.
    /// </summary>
    [Fact]
    public void NotifyOnly_JustMarksBlocked()
    {
        var appFile = Path.Combine(_dir, "app2.exe");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(appFile, "MZ fake");
        var entry = new StartupEntry
        {
            Name = "app2.exe",
            FilePath = appFile,
            Location = EntryLocation.StartupFolder,
        };
        _store.AddEntry(entry);
        var rule = new BlacklistRule
        {
            Value = appFile,
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyOnly,
        };

        _manager.ApplyBlacklistAction(entry, rule, _store, _notifications);

        // Файл НЕ тронут, запись помечена
        Assert.True(File.Exists(appFile));
        Assert.Equal(EntryStatus.Blocked, entry.Status);
        Assert.Empty(_store.GetRemovedEntries());
    }

    /// <summary>Удаляет временные файлы тестов.</summary>
    public void Dispose()
    {
        _notifications.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
