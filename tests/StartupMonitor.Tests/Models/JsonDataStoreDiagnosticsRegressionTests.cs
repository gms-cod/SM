using System.Text.Json;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>Проверяет диагностику восстановления файлов без потери корректной конфигурации и истории.</summary>
public sealed class JsonDataStoreDiagnosticsRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StartupMonitorDiagnostics_" + Guid.NewGuid().ToString("N"));

    /// <summary>Отказ резервного копирования не скрывается и не препятствует атомарному сохранению основной секции.</summary>
    [Fact]
    public void LockedBackup_IsReportedAndCurrentSettingsRemainPersisted()
    {
        var warnings = new List<string>();
        var store = new JsonDataStore(_directory, (message, _) => warnings.Add(message));
        var config = store.Load();
        config.Theme = "light";
        store.Save(config);
        var backup = Path.Combine(_directory, "settings.json.backup");
        using (var locked = new FileStream(backup, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            config.Theme = "dark";
            store.Save(config);
        }
        Assert.Contains(warnings, message => message.Contains("резервную копию секции settings.json", StringComparison.Ordinal));
        Assert.Equal("dark", new JsonDataStore(_directory).Load().Theme);
    }

    /// <summary>Повторное сохранение допускает возврат к исходному значению без удаления неизвестных параллельных данных.</summary>
    [Fact]
    public void ReusedSnapshot_RevertsFieldWithoutDeletingConcurrentEntry()
    {
        var store = new JsonDataStore(_directory);
        var config = store.Load();
        var originalTheme = config.Theme;
        store.AddEntry(new StartupEntry { Id = "concurrent" });
        config.Theme = "light";
        store.Save(config);
        config.Theme = originalTheme;
        store.Save(config);
        Assert.Equal(originalTheme, store.Load().Theme);
        Assert.Equal("concurrent", Assert.Single(store.Load().Entries).Id);
    }

    /// <summary>Повторная запись того же DTO может отменить свой рейтинг, сохраняя чужое изменение статуса.</summary>
    [Fact]
    public void ReusedEntrySnapshot_RevertsRatingWithoutRevertingConcurrentStatus()
    {
        var store = new JsonDataStore(_directory);
        store.AddEntry(new StartupEntry { Id = "entry", AiRating = 0 });
        var rating = store.FindEntry(entry => entry.Id == "entry") ?? throw new InvalidOperationException();
        rating.AiRating = 7;
        store.UpdateEntry(rating);
        var status = store.FindEntry(entry => entry.Id == "entry") ?? throw new InvalidOperationException();
        status.Status = EntryStatus.Disabled;
        store.UpdateEntry(status);
        rating.AiRating = 0;
        store.UpdateEntry(rating);
        var actual = Assert.Single(store.Load().Entries);
        Assert.Equal(0, actual.AiRating);
        Assert.Equal(EntryStatus.Disabled, actual.Status);
    }

    /// <summary>Повреждённые фрагменты не удаляют соседние корректные события и оставляют содержательную диагностику.</summary>
    [Fact]
    public void CorruptHistoryFragments_AreReportedWithoutDiscardingValidEvents()
    {
        Directory.CreateDirectory(_directory);
        var entry = new HistoryEntry { Id = "valid", Timestamp = DateTime.UtcNow };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        File.WriteAllText(Path.Combine(_directory, "history.jsonl"),
            "{broken\n" + JsonSerializer.Serialize(entry, options) + "\n{unfinished");
        var warnings = new List<string>();
        var store = new JsonDataStore(_directory, (message, _) => warnings.Add(message));
        Assert.Equal("valid", Assert.Single(store.ReadHistory()).Id);
        Assert.Contains(warnings, message => message.Contains("повреждённый фрагмент", StringComparison.Ordinal));
        Assert.Contains(warnings, message => message.Contains("незавершённой записью", StringComparison.Ordinal));
    }

    /// <summary>Первое добавление после срока компактации удаляет просроченные строки с диска без перезапуска.</summary>
    [Fact]
    public void CompactionDuringRuntime_RemovesExpiredDiskRowsAndKeepsNewEventOnce()
    {
        var store = new JsonDataStore(_directory);
        store.AddHistory(new HistoryEntry { Id = "before", Timestamp = DateTime.UtcNow });
        var history = Path.Combine(_directory, "history.jsonl");
        File.AppendAllText(history, "{\"id\":\"expired\",\"timestamp\":\"2000-01-01T00:00:00Z\"}\n");
        var lastCompaction = typeof(JsonDataStore).GetField("_lastHistoryCompactionUtc",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Отсутствует время компактации истории.");
        lastCompaction.SetValue(store, DateTime.UtcNow.AddHours(-2));
        store.AddHistory(new HistoryEntry { Id = "after", Timestamp = DateTime.UtcNow });
        var rows = File.ReadAllLines(history);
        Assert.Equal(2, rows.Length);
        Assert.DoesNotContain(rows, row => row.Contains("expired", StringComparison.Ordinal));
        Assert.Equal(1, rows.Count(row => row.Contains("after", StringComparison.Ordinal)));
    }

    /// <summary>Удаляет только выделенный этому тесту каталог после освобождения файлов.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
