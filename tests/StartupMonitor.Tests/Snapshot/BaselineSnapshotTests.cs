using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Snapshot;
using StartupMonitor.UI.Converters;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.Tests.Snapshot;

/// <summary>
/// Комплексные тесты для контрольных снимков системы («Было — Стало», v1.0.18).
/// </summary>
public class BaselineSnapshotTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonDataStore _store;
    private readonly SnapshotManager _snapshotManager;

    public BaselineSnapshotTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SMon_Snapshot_Tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _store = new JsonDataStore(Path.Combine(_tempDir, "startup-monitor.json"));
        _snapshotManager = new SnapshotManager(_store, NullLogger<SnapshotManager>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void StartupEntrySnapshotItem_FromEntry_CopiesAllRelevantFields()
    {
        var entry = new StartupEntry
        {
            Id = "abc123456789",
            Name = "Discord",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            FilePath = @"C:\Users\user\AppData\Local\Discord\Update.exe",
            CommandLine = @"--processStart Discord.exe",
            Publisher = "Discord Inc.",
            SHA256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            Status = EntryStatus.Enabled,
            DelaySeconds = 45,
            DetectedAt = new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc)
        };

        var snapshotItem = StartupEntrySnapshotItem.FromEntry(entry);

        Assert.Equal(entry.Id, snapshotItem.Id);
        Assert.Equal(entry.Name, snapshotItem.Name);
        Assert.Equal(entry.Location, snapshotItem.Location);
        Assert.Equal(entry.LocationDetail, snapshotItem.LocationDetail);
        Assert.Equal(entry.FilePath, snapshotItem.FilePath);
        Assert.Equal(entry.CommandLine, snapshotItem.CommandLine);
        Assert.Equal(entry.Publisher, snapshotItem.Publisher);
        Assert.Equal(entry.SHA256, snapshotItem.SHA256);
        Assert.Equal(entry.Status, snapshotItem.Status);
        Assert.Equal(entry.DelaySeconds, snapshotItem.DelaySeconds);
        Assert.Equal(entry.DetectedAt, snapshotItem.DetectedAt);
    }

    [Fact]
    public void BaselineSnapshot_CreateFromEntries_InitializesMetadata()
    {
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "App1", Location = EntryLocation.RegistryRun },
            new() { Id = "2", Name = "App2", Location = EntryLocation.StartupFolder }
        };

        var snapshot = BaselineSnapshot.Create("Чистая система", "После установки ОС", entries);

        Assert.NotNull(snapshot.Id);
        Assert.Equal("Чистая система", snapshot.Name);
        Assert.Equal("После установки ОС", snapshot.Description);
        Assert.Equal(2, snapshot.EntriesCount);
        Assert.Equal(2, snapshot.Entries.Count);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.MachineName));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OsVersion));
        Assert.True(snapshot.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void JsonDataStore_SaveAndGetSnapshots_RoundtripSucceeds()
    {
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "Telegram", Location = EntryLocation.RegistryRun, FilePath = @"C:\Telegram.exe" }
        };

        var snapshot = BaselineSnapshot.Create("Эталон 1", "Тестовый снимок", entries);

        _store.SaveSnapshot(snapshot);

        var allSnapshots = _store.GetSnapshots();
        Assert.Single(allSnapshots);
        Assert.Equal(snapshot.Id, allSnapshots[0].Id);
        Assert.Equal("Эталон 1", allSnapshots[0].Name);
        Assert.Equal("Тестовый снимок", allSnapshots[0].Description);

        var retrieved = _store.GetSnapshot(snapshot.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(snapshot.Id, retrieved.Id);
        Assert.Single(retrieved.Entries);
        Assert.Equal("Telegram", retrieved.Entries[0].Name);
    }

    [Fact]
    public void JsonDataStore_DeleteSnapshot_RemovesSnapshot()
    {
        var snapshot1 = BaselineSnapshot.Create("Снимок 1", "", []);
        var snapshot2 = BaselineSnapshot.Create("Снимок 2", "", []);

        _store.SaveSnapshot(snapshot1);
        _store.SaveSnapshot(snapshot2);

        Assert.Equal(2, _store.GetSnapshots().Count);

        var deleted = _store.DeleteSnapshot(snapshot1.Id);
        Assert.True(deleted);

        var remaining = _store.GetSnapshots();
        Assert.Single(remaining);
        Assert.Equal(snapshot2.Id, remaining[0].Id);

        var deleteNonExistent = _store.DeleteSnapshot("non-existent-id");
        Assert.False(deleteNonExistent);
    }

    [Fact]
    public void SnapshotComparator_Compare_DetectsAddedEntries()
    {
        var baselineItem = new StartupEntrySnapshotItem
        {
            Id = "1",
            Name = "Steam",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Run",
            FilePath = @"C:\Steam\steam.exe"
        };
        var baseline = new BaselineSnapshot
        {
            Id = "snap-1",
            Name = "Базовый",
            Entries = [baselineItem]
        };

        var currentEntries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "Steam", Location = EntryLocation.RegistryRun, LocationDetail = @"HKCU\Run", FilePath = @"C:\Steam\steam.exe" },
            new() { Id = "2", Name = "NewMalware", Location = EntryLocation.StartupFolder, LocationDetail = @"Startup", FilePath = @"C:\AppData\malware.exe" }
        };

        var diff = SnapshotComparator.Compare(baseline, currentEntries);

        Assert.True(diff.HasDifferences);
        Assert.Single(diff.AddedEntries);
        Assert.Equal("NewMalware", diff.AddedEntries[0].Name);
        Assert.Empty(diff.RemovedEntries);
        Assert.Empty(diff.ModifiedEntries);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public void SnapshotComparator_Compare_DetectsRemovedEntries()
    {
        var item1 = new StartupEntrySnapshotItem
        {
            Id = "1",
            Name = "App1",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Run"
        };
        var item2 = new StartupEntrySnapshotItem
        {
            Id = "2",
            Name = "App2",
            Location = EntryLocation.StartupFolder,
            LocationDetail = @"Startup"
        };
        var baseline = new BaselineSnapshot
        {
            Id = "snap-1",
            Name = "Базовый",
            Entries = [item1, item2]
        };

        var currentEntries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "App1", Location = EntryLocation.RegistryRun, LocationDetail = @"HKCU\Run" }
        };

        var diff = SnapshotComparator.Compare(baseline, currentEntries);

        Assert.True(diff.HasDifferences);
        Assert.Empty(diff.AddedEntries);
        Assert.Single(diff.RemovedEntries);
        Assert.Equal("App2", diff.RemovedEntries[0].Name);
        Assert.Empty(diff.ModifiedEntries);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public void SnapshotComparator_Compare_DetectsModifiedProperties()
    {
        var baselineItem = new StartupEntrySnapshotItem
        {
            Id = "1",
            Name = "Viber",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Run",
            CommandLine = "viber.exe",
            Status = EntryStatus.Enabled,
            DelaySeconds = 0,
            SHA256 = "AAAA"
        };
        var baseline = new BaselineSnapshot
        {
            Id = "snap-1",
            Name = "Базовый",
            Entries = [baselineItem]
        };

        var currentEntries = new List<StartupEntry>
        {
            new()
            {
                Id = "1",
                Name = "Viber",
                Location = EntryLocation.RegistryRun,
                LocationDetail = @"HKCU\Run",
                CommandLine = "viber.exe --StartMinimized",
                Status = EntryStatus.Delayed,
                DelaySeconds = 30,
                SHA256 = "BBBB"
            }
        };

        var diff = SnapshotComparator.Compare(baseline, currentEntries);

        Assert.True(diff.HasDifferences);
        Assert.Empty(diff.AddedEntries);
        Assert.Empty(diff.RemovedEntries);
        Assert.Single(diff.ModifiedEntries);

        var modified = diff.ModifiedEntries[0];
        Assert.Equal(4, modified.ChangedProperties.Count);
        Assert.Contains("CommandLine", modified.ChangedProperties);
        Assert.Contains("Status", modified.ChangedProperties);
        Assert.Contains("DelaySeconds", modified.ChangedProperties);
        Assert.Contains("SHA256", modified.ChangedProperties);
        Assert.False(string.IsNullOrWhiteSpace(modified.Summary));
    }

    [Fact]
    public void SnapshotComparator_Compare_UnchangedEntries_HasDifferencesFalse()
    {
        var baselineItem = new StartupEntrySnapshotItem
        {
            Id = "1",
            Name = "Chrome",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Run",
            FilePath = @"C:\Chrome\chrome.exe",
            CommandLine = "chrome.exe",
            Status = EntryStatus.Enabled,
            DelaySeconds = 0,
            SHA256 = "HASH123"
        };
        var baseline = new BaselineSnapshot
        {
            Id = "snap-1",
            Name = "Базовый",
            Entries = [baselineItem]
        };

        var currentEntries = new List<StartupEntry>
        {
            new()
            {
                Id = "1",
                Name = "Chrome",
                Location = EntryLocation.RegistryRun,
                LocationDetail = @"HKCU\Run",
                FilePath = @"C:\Chrome\chrome.exe",
                CommandLine = "chrome.exe",
                Status = EntryStatus.Enabled,
                DelaySeconds = 0,
                SHA256 = "HASH123"
            }
        };

        var diff = SnapshotComparator.Compare(baseline, currentEntries);

        Assert.False(diff.HasDifferences);
        Assert.Empty(diff.AddedEntries);
        Assert.Empty(diff.RemovedEntries);
        Assert.Empty(diff.ModifiedEntries);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public void SnapshotComparator_Compare_CompositeKeyFallback_MatchesEntriesWhenIdDiffers()
    {
        var baselineItem = new StartupEntrySnapshotItem
        {
            Id = "old_guid_123",
            Name = "OneDrive",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Run",
            FilePath = @"C:\OneDrive.exe"
        };
        var baseline = new BaselineSnapshot
        {
            Id = "snap-1",
            Name = "Базовый",
            Entries = [baselineItem]
        };

        // Запись та же самая по ключу (RegistryRun|HKCU\Run|OneDrive), но ID сгенерирован заново
        var currentEntries = new List<StartupEntry>
        {
            new()
            {
                Id = "new_guid_456",
                Name = "OneDrive",
                Location = EntryLocation.RegistryRun,
                LocationDetail = @"HKCU\Run",
                FilePath = @"C:\OneDrive.exe"
            }
        };

        var diff = SnapshotComparator.Compare(baseline, currentEntries);

        Assert.False(diff.HasDifferences);
        Assert.Empty(diff.AddedEntries);
        Assert.Empty(diff.RemovedEntries);
        Assert.Empty(diff.ModifiedEntries);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public void PipeMessage_SnapshotPayloads_JsonRoundtrip()
    {
        var createPayload = new CreateSnapshotPayload { Name = "До установки софта", Description = "Свежая ОС" };
        var createJson = JsonSerializer.Serialize(createPayload);
        var desCreate = JsonSerializer.Deserialize<CreateSnapshotPayload>(createJson);
        Assert.NotNull(desCreate);
        Assert.Equal(createPayload.Name, desCreate.Name);
        Assert.Equal(createPayload.Description, desCreate.Description);

        var delPayload = new DeleteSnapshotPayload("snap-999");
        var delJson = JsonSerializer.Serialize(delPayload);
        var desDel = JsonSerializer.Deserialize<DeleteSnapshotPayload>(delJson);
        Assert.NotNull(desDel);
        Assert.Equal("snap-999", desDel.SnapshotId);

        var compPayload = new CompareSnapshotPayload("snap-777");
        var compJson = JsonSerializer.Serialize(compPayload);
        var desComp = JsonSerializer.Deserialize<CompareSnapshotPayload>(compJson);
        Assert.NotNull(desComp);
        Assert.Equal("snap-777", desComp.SnapshotId);
    }

    [Fact]
    public void SnapshotManager_CreateAndCompare_Succeeds()
    {
        var cfg = _store.Load();
        cfg.Entries = [
            new() { Id = "1", Name = "BaseApp", Location = EntryLocation.RegistryRun, LocationDetail = "HKCU" }
        ];
        _store.Save(cfg);

        var snapshot = _snapshotManager.CreateSnapshot("Первый снимок", "Авто-тест");
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.EntriesCount);

        // Модифицируем запись в системе
        cfg = _store.Load();
        cfg.Entries = [
            new() { Id = "1", Name = "BaseApp", Location = EntryLocation.RegistryRun, LocationDetail = "HKCU" },
            new() { Id = "2", Name = "NewInstalledApp", Location = EntryLocation.StartupFolder, LocationDetail = "Startup" }
        ];
        _store.Save(cfg);

        var (success, error, diff) = _snapshotManager.CompareWithSnapshot(snapshot.Id);
        Assert.True(success);
        Assert.NotNull(diff);
        Assert.True(diff.HasDifferences);
        Assert.Single(diff.AddedEntries);
        Assert.Equal("NewInstalledApp", diff.AddedEntries[0].Name);
        Assert.Equal(1, diff.UnchangedCount);
    }

    [Fact]
    public void HexToBrushConverter_ConvertsColorWithAndWithoutOpacity()
    {
        var converter = new HexToBrushConverter();

        // Без прозрачности (#10B981)
        var resultSolid = converter.Convert("#10B981", typeof(Brush), null, CultureInfo.InvariantCulture);
        var brushSolid = Assert.IsType<SolidColorBrush>(resultSolid);
        Assert.Equal(255, brushSolid.Color.A);
        Assert.Equal(0x10, brushSolid.Color.R);
        Assert.Equal(0xB9, brushSolid.Color.G);
        Assert.Equal(0x81, brushSolid.Color.B);

        // С прозрачностью 0.15 (15% от 255 = 38)
        var resultAlpha = converter.Convert("#10B981", typeof(Brush), "0.15", CultureInfo.InvariantCulture);
        var brushAlpha = Assert.IsType<SolidColorBrush>(resultAlpha);
        Assert.Equal((byte)(0.15 * 255), brushAlpha.Color.A);

        // Невалидный ввод возвращает Transparent
        var resultInvalid = converter.Convert("invalid-color", typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal(Brushes.Transparent, resultInvalid);
    }
}
