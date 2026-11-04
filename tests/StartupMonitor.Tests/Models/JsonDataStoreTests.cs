using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>
/// Тесты для JsonDataStore: загрузка, сохранение, добавление/удаление/обновление записей,
/// работа с историей, конкурентный доступ, обработка повреждённых файлов, миграция, дедупликация.
/// </summary>
public class JsonDataStoreTests : IDisposable
{
    /// <summary>Временная директория для тестового конфига.</summary>
    private readonly string _testDir;
    /// <summary>Путь к тестовому конфигу.</summary>
    private readonly string _testFilePath;
    /// <summary>Хранилище данных.</summary>
    private readonly JsonDataStore _store;

    public JsonDataStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}");
        _testFilePath = Path.Combine(_testDir, "startup-monitor.json");
        _store = new JsonDataStore(_testFilePath);
    }

    /// <summary>
    /// Проверяет, что Load() создаёт конфиг по умолчанию, если файла нет.
    /// </summary>
    [Fact]
    public void Load_CreatesDefaultConfig()
    {
        var config = _store.Load();

        Assert.NotNull(config);
        Assert.Equal("ru", config.Language);
        Assert.Equal("dark", config.Theme);
        Assert.Empty(config.Entries);
        Assert.Empty(config.Blacklist);
        Assert.Empty(config.History);
        Assert.True(config.Settings.NotifyOnNewEntry);
    }

    /// <summary>
    /// Проверяет, что AddEntry сохраняет запись в файл.
    /// </summary>
    [Fact]
    public void AddEntry_PersistsToFile()
    {
        var entry = new StartupEntry
        {
            Name = "TestApp",
            FilePath = @"C:\test.exe",
            Location = EntryLocation.RegistryRun
        };

        _store.AddEntry(entry);

        var config = _store.Load();
        var saved = Assert.Single(config.Entries);
        Assert.Equal("TestApp", saved.Name);
        Assert.Equal(@"C:\test.exe", saved.FilePath);
    }

    /// <summary>
    /// Проверяет, что RemoveEntry удаляет правильную запись (по ID).
    /// </summary>
    [Fact]
    public void RemoveEntry_RemovesCorrectly()
    {
        var entry1 = new StartupEntry { Name = "App1" };
        var entry2 = new StartupEntry { Name = "App2" };

        _store.AddEntry(entry1);
        _store.AddEntry(entry2);
        _store.RemoveEntry(entry1.Id);

        var config = _store.Load();
        Assert.Single(config.Entries);
        Assert.Equal("App2", config.Entries[0].Name);
    }

    /// <summary>
    /// Проверяет, что UpdateEntry обновляет существующую запись.
    /// </summary>
    [Fact]
    public void UpdateEntry_ModifiesCorrectly()
    {
        var entry = new StartupEntry
        {
            Name = "OldName",
            Status = EntryStatus.Enabled
        };
        _store.AddEntry(entry);

        entry.Name = "NewName";
        entry.Status = EntryStatus.Disabled;
        _store.UpdateEntry(entry);

        var config = _store.Load();
        var updated = config.Entries[0];
        Assert.Equal("NewName", updated.Name);
        Assert.Equal(EntryStatus.Disabled, updated.Status);
    }

    /// <summary>
    /// Проверяет, что AddHistory удаляет записи старше указанного в настройках срока.
    /// </summary>
    [Fact]
    public void AddHistory_TrimExcess()
    {
        var settings = new AppSettings { HistoryDays = 1 };

        var config = _store.Load();
        config.Settings = settings;
        _store.Save(config);

        _store.AddHistory(new HistoryEntry { ChangeType = "fresh" });
        _store.AddHistory(new HistoryEntry { ChangeType = "old", Timestamp = DateTime.UtcNow.AddDays(-2) });

        var loaded = _store.Load();
        var entry = Assert.Single(loaded.History);
        Assert.Equal("fresh", entry.ChangeType);
    }

    /// <summary>
    /// Проверяет, что конкурентная запись 10 записей не приводит к повреждению данных.
    /// </summary>
    [Fact]
    public async Task ConcurrentAccess_DoesNotCorrupt()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                _store.AddEntry(new StartupEntry { Name = $"App{idx}" });
            }));
        }

        await Task.WhenAll(tasks);

        var config = _store.Load();
        Assert.Equal(10, config.Entries.Count);
    }

    /// <summary>
    /// Проверяет, что повреждённый секционный файл переименовывается в .corrupt и стартует с чистого листа.
    /// </summary>
    [Fact]
    public void Load_CorruptSectionFile_RenamesAndStartsFresh()
    {
        // Пишем мусор в секционный файл записей — Load не должен упасть
        var entriesPath = Path.Combine(_testDir, "startup-entries.json");
        File.WriteAllText(entriesPath, "{ это не json !!!");

        var store = new JsonDataStore(_testFilePath);
        var config = store.Load();

        Assert.NotNull(config);
        Assert.Empty(config.Entries);
        Assert.True(File.Exists(entriesPath + ".corrupt"));
    }

    /// <summary>
    /// Проверяет, что старый единый конфиг мигрирует в секционные файлы и переименовывается.
    /// </summary>
    [Fact]
    public void Load_LegacyFile_MigratesToSections()
    {
        // Старый единый конфиг мигрирует в секционные файлы и переименовывается
        var legacyJson = """
        {
          "language": "ru", "theme": "dark",
          "entries": [{ "id": "e1", "name": "LegacyApp", "file_path": "C:\\legacy.exe" }],
          "blacklist": [{ "id": "b1", "value": "C:\\bad.exe" }]
        }
        """;
        File.WriteAllText(_testFilePath, legacyJson);

        var store = new JsonDataStore(_testFilePath);
        var config = store.Load();

        Assert.Single(config.Entries);
        Assert.Equal("LegacyApp", config.Entries[0].Name);
        Assert.Single(config.Blacklist);
        Assert.True(File.Exists(_testFilePath + ".migrated"));
        Assert.True(File.Exists(Path.Combine(_testDir, "startup-entries.json")));
        Assert.True(File.Exists(Path.Combine(_testDir, "blacklist.json")));
    }

    /// <summary>
    /// Проверяет, что повторный Save без изменений не перезаписывает файлы (SHA256-оптимизация).
    /// </summary>
    [Fact]
    public void Save_SkipsUnchangedSections()
    {
        _store.AddEntry(new StartupEntry { Name = "App1" });
        _store.Save(_store.Load()); // первый Save — создаёт недостающие секции
        var entriesPath = Path.Combine(_testDir, "startup-entries.json");
        var settingsPath = Path.Combine(_testDir, "settings.json");
        var entriesTime = File.GetLastWriteTimeUtc(entriesPath);
        var settingsTime = File.GetLastWriteTimeUtc(settingsPath);

        // Повторный Save без изменений — файлы не перезаписываются
        Thread.Sleep(50);
        _store.Save(_store.Load());

        Assert.Equal(entriesTime, File.GetLastWriteTimeUtc(entriesPath));
        Assert.Equal(settingsTime, File.GetLastWriteTimeUtc(settingsPath));
    }

    /// <summary>
    /// Проверяет, что после сохранения не остаётся временных .tmp-файлов.
    /// </summary>
    [Fact]
    public void Save_DoesNotLeaveTmpFiles()
    {
        _store.AddEntry(new StartupEntry { Name = "App1" });

        Assert.Empty(Directory.GetFiles(_testDir, "*.tmp"));
        Assert.True(File.Exists(Path.Combine(_testDir, "startup-entries.json")));
    }

    /// <summary>
    /// Проверяет, что AddRemovedEntry дедуплицируется по OriginalEntryId.
    /// </summary>
    [Fact]
    public void AddRemovedEntry_DeduplicatesByOriginalEntry()
    {
        _store.AddRemovedEntry(new RemovedEntry { OriginalEntryId = "e1", Name = "App" });
        _store.AddRemovedEntry(new RemovedEntry { OriginalEntryId = "e1", Name = "App" });

        Assert.Single(_store.GetRemovedEntries());
    }

    /// <summary>
    /// Проверяет, что DeduplicateEntries объединяет дубликаты HKCU\user\ и HKCU\.
    /// </summary>
    [Fact]
    public void DeduplicateEntries_MergesUserAndRootHkcu()
    {
        var entry1 = new StartupEntry
        {
            Name = "StartupMonitor.UI",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\gms\Software\Microsoft\Windows\CurrentVersion\Run\StartupMonitor.UI",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.UI.exe",
            AiRating = 5
        };
        var entry2 = new StartupEntry
        {
            Name = "StartupMonitor.UI",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\StartupMonitor.UI",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.UI.exe",
            AiRating = 0
        };

        _store.AddEntry(entry1, checkDuplicate: false);
        _store.AddEntry(entry2, checkDuplicate: false);

        Assert.Equal(2, _store.Load().Entries.Count);

        _store.DeduplicateEntries();

        var entries = _store.Load().Entries;
        var single = Assert.Single(entries);
        Assert.Equal("StartupMonitor.UI", single.Name);
        Assert.Equal(5, single.AiRating);
        Assert.Equal(@"HKCU\gms\Software\Microsoft\Windows\CurrentVersion\Run\StartupMonitor.UI", single.LocationDetail);
    }

    /// <summary>
    /// Проверяет работу метода NormalizeLocationDetail.
    /// </summary>
    [Theory]
    [InlineData(@"HKCU\gms\Software\Microsoft\Run", @"HKCU\Software\Microsoft\Run")]
    [InlineData(@"HKCU\Software\Microsoft\Run", @"HKCU\Software\Microsoft\Run")]
    [InlineData(@"HKLM\Software\Microsoft\Run", @"HKLM\Software\Microsoft\Run")]
    [InlineData(null, "")]
    public void NormalizeLocationDetail_NormalizesCorrectly(string? input, string expected)
    {
        var result = JsonDataStore.NormalizeLocationDetail(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadHistory_HandlesSingleLineJsonl()
    {
        var entry1 = new HistoryEntry { EntryName = "App1", ChangeType = "Added" };
        var entry2 = new HistoryEntry { EntryName = "App2", ChangeType = "Removed" };

        _store.AddHistory(entry1);
        _store.AddHistory(entry2);

        var historyPath = Path.Combine(_testDir, "history.jsonl");
        Assert.True(File.Exists(historyPath));

        var lines = File.ReadAllLines(historyPath);
        Assert.Equal(2, lines.Length); // Строго по одной строке на запись!

        var read = _store.ReadHistory();
        Assert.Equal(2, read.Count);
        Assert.Equal("App1", read[0].EntryName);
        Assert.Equal("App2", read[1].EntryName);
    }

    [Fact]
    public void ReadHistory_HandlesMultiLineLegacyJsonl_AndAutoMigrates()
    {
        var historyPath = Path.Combine(_testDir, "history.jsonl");
        // Старый формат, когда WriteIndented был true: каждый объект разбивался на несколько строк
        var legacyContent = @"{
  ""id"": ""1"",
  ""entry_name"": ""LegacyApp1"",
  ""change_type"": ""Added""
}
{
  ""id"": ""2"",
  ""entry_name"": ""LegacyApp2"",
  ""change_type"": ""Modified""
}";
        File.WriteAllText(historyPath, legacyContent);

        var read = _store.ReadHistory();
        Assert.Equal(2, read.Count);
        Assert.Equal("LegacyApp1", read[0].EntryName);
        Assert.Equal("LegacyApp2", read[1].EntryName);

        // Проверяем, что файл автоматически мигрирован в однострочный JSONL
        var migratedLines = File.ReadAllLines(historyPath);
        Assert.Equal(2, migratedLines.Length);
    }

    /// <summary>
    /// Проверяет, что атомарная запись создаёт JSON строго в UTF-8 БЕЗ BOM (RFC 8259).
    /// </summary>
    [Fact]
    public void WriteSectionAtomic_CreatesPureUtf8WithoutBom()
    {
        var entry = new StartupEntry { Name = "NoBomTest", FilePath = @"C:\nobom.exe" };
        _store.AddEntry(entry);

        var entriesPath = Path.Combine(_testDir, "startup-entries.json");
        Assert.True(File.Exists(entriesPath));

        var bytes = File.ReadAllBytes(entriesPath);
        Assert.True(bytes.Length >= 3);
        // Проверяем, что первых трёх байт UTF-8 BOM (0xEF, 0xBB, 0xBF) НЕТ
        bool hasBom = bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.False(hasBom, "Файл JSON содержит UTF-8 BOM, что противоречит стандарту RFC 8259!");
        // Проверяем, что файл начинается с '[' (0x5B) или '{' (0x7B)
        Assert.Equal((byte)'[', bytes[0]);
    }

    /// <summary>
    /// Проверяет, что чтение секций бесшовно обрабатывает UTF-8 как с BOM-маркером, так и без него.
    /// </summary>
    [Fact]
    public void ReadSection_SeamlesslyReadsUtf8WithAndWithoutBom()
    {
        var customDir = Path.Combine(Path.GetTempPath(), $"sm-bom-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(customDir);
            var settingsPath = Path.Combine(customDir, "settings.json");

            // Записываем файл С BOM-маркером вручную
            var bomBytes = new byte[] { 0xEF, 0xBB, 0xBF };
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"language\": \"ru\", \"theme\": \"light\"}");
            var combined = new byte[bomBytes.Length + jsonBytes.Length];
            Buffer.BlockCopy(bomBytes, 0, combined, 0, bomBytes.Length);
            Buffer.BlockCopy(jsonBytes, 0, combined, bomBytes.Length, jsonBytes.Length);
            File.WriteAllBytes(settingsPath, combined);

            var customStore = new JsonDataStore(Path.Combine(customDir, "startup-monitor.json"));
            var loaded = customStore.Load();

            Assert.NotNull(loaded);
            Assert.Equal("ru", loaded.Language);
            Assert.Equal("light", loaded.Theme);
        }
        finally
        {
            try { Directory.Delete(customDir, recursive: true); } catch { }
        }
    }

    /// <summary>Удаляет временную директорию тестов.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
