using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для механизмов самозащиты StartupMonitor и строгого режима автоблокировки (StrictMode).
/// </summary>
public class SelfProtectionAndLockdownTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonDataStore _store;
    private readonly NotificationService _notifications;

    public SelfProtectionAndLockdownTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"sm-lockdown-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new JsonDataStore(Path.Combine(_testDir, "startup-entries.json"));
        var tracker = new InitialScanTracker();
        tracker.MarkCompleted();
        _notifications = new NotificationService(tracker);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData("StartupMonitor", true)]
    [InlineData("StartupMonitor.Service", true)]
    [InlineData("startupmonitor", true)]
    [InlineData("OtherService", false)]
    [InlineData("", false)]
    public void IsSelfServiceName_IdentifiesCorrectly(string serviceName, bool expected)
    {
        Assert.Equal(expected, EntryOperations.IsSelfServiceName(serviceName));
    }

    [Fact]
    public void IsSelfEntry_ServiceEntry_ReturnsTrue()
    {
        var entry = new StartupEntry
        {
            Name = "StartupMonitor",
            Location = EntryLocation.Service,
            LocationDetail = "StartupMonitor",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.Service.exe"
        };

        Assert.True(EntryOperations.IsSelfEntry(entry));
    }

    [Fact]
    public void IsSelfEntry_UiRegistryEntry_ReturnsTrue()
    {
        var entry = new StartupEntry
        {
            Name = "StartupMonitor.UI",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.UI.exe"
        };

        Assert.True(EntryOperations.IsSelfEntry(entry));
    }

    [Fact]
    public void IsSelfEntry_ThirdPartyApp_ReturnsFalse()
    {
        var entry = new StartupEntry
        {
            Name = "Telegram",
            Location = EntryLocation.RegistryRun,
            FilePath = @"C:\Users\User\AppData\Roaming\Telegram Desktop\Telegram.exe"
        };

        Assert.False(EntryOperations.IsSelfEntry(entry));
    }

    [Fact]
    public void IsProtectedSystemEntry_IdentifiesCriticalServicesAndTasks()
    {
        var svchostEntry = new StartupEntry
        {
            Name = "Bits",
            Location = EntryLocation.Service,
            LocationDetail = "BITS",
            FilePath = @"C:\Windows\System32\svchost.exe"
        };
        Assert.True(EntryOperations.IsProtectedSystemEntry(svchostEntry));

        var userSvcEntry = new StartupEntry
        {
            Name = "cbdhsvc_12345",
            Location = EntryLocation.Service,
            LocationDetail = "cbdhsvc_12345"
        };
        Assert.True(EntryOperations.IsProtectedSystemEntry(userSvcEntry));

        var windowsTask = new StartupEntry
        {
            Name = "Defrag",
            Location = EntryLocation.TaskScheduler,
            LocationDetail = @"\Microsoft\Windows\Defrag\ScheduledDefrag"
        };
        Assert.True(EntryOperations.IsProtectedSystemEntry(windowsTask));

        var thirdParty = new StartupEntry
        {
            Name = "MaliciousApp",
            Location = EntryLocation.RegistryRun,
            FilePath = @"C:\Temp\bad.exe"
        };
        Assert.False(EntryOperations.IsProtectedSystemEntry(thirdParty));
    }

    [Fact]
    public void RemoveFromSystem_SelfEntry_BlockedBySelfProtection()
    {
        var selfService = new StartupEntry
        {
            Name = "StartupMonitor",
            Location = EntryLocation.Service,
            LocationDetail = "StartupMonitor",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.Service.exe"
        };

        var (success, error, payload) = EntryOperations.RemoveFromSystem(selfService);
        Assert.False(success);
        Assert.Contains("Самозащита", error);
        Assert.Null(payload);
    }

    [Fact]
    public void DisableFromSystem_SelfEntry_BlockedBySelfProtection()
    {
        var selfUi = new StartupEntry
        {
            Name = "StartupMonitor.UI",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.UI.exe"
        };

        var (success, error) = EntryOperations.DisableFromSystem(selfUi);
        Assert.False(success);
        Assert.Contains("Самозащита", error);
    }

    [Fact]
    public void AddOrUpdate_SelfEntry_NotBlockedWhenStrictModeEnabled()
    {
        var cfg = _store.Load();
        cfg.Settings.StrictMode = true;
        _store.Save(cfg);

        var selfEntry = new StartupEntry
        {
            Id = "self123",
            Name = "StartupMonitor.UI",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.UI.exe"
        };

        // Добавляем запись при включенном StrictMode
        var isNew = WatcherHelper.AddOrUpdate(_store, _notifications, selfEntry);

        Assert.True(isNew);
        // Запись должна быть в Entries, а не в RemovedEntries!
        Assert.NotNull(_store.FindEntry(e => e.Name == "StartupMonitor.UI"));
        Assert.Empty(_store.GetRemovedEntries());
    }

    [Fact]
    public void AddOrUpdate_LockdownBlocked_ReleasedWhenStrictModeDisabled()
    {
        var entryId = "app12345";
        var entry = new StartupEntry
        {
            Id = entryId,
            Name = "TestApp",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            FilePath = @"C:\Dummy\testapp.exe"
        };

        // Запись была заблокирована строгим режимом и попала в бэкап с RemovalReason = LockdownBlocked
        var removed = new RemovedEntry
        {
            Id = "rem1",
            OriginalEntryId = entryId,
            Name = entry.Name,
            Location = entry.Location,
            LocationDetail = entry.LocationDetail,
            FilePath = entry.FilePath,
            RemovalReason = "LockdownBlocked"
        };
        _store.AddRemovedEntry(removed);

        // StrictMode выключен!
        var cfg = _store.Load();
        cfg.Settings.StrictMode = false;
        _store.Save(cfg);

        // При повторном появлении записи
        var isNew = WatcherHelper.AddOrUpdate(_store, _notifications, entry);

        // Запись должна быть удалена из бэкапа и добавлена в список как обычная новая запись!
        Assert.True(isNew);
        Assert.NotNull(_store.FindEntry(e => e.Id == entryId));
        Assert.Null(_store.GetRemovedEntryByOriginalId(entryId));
    }

    [Fact]
    public void AddOrUpdate_DisabledEntrySelfRestores_DoesNotKillProcess()
    {
        var entryId = "opera123";
        var entry = new StartupEntry
        {
            Id = entryId,
            Name = "Opera Stable",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Opera Stable",
            FilePath = @"C:\Users\User\AppData\Local\Programs\Opera\opera.exe",
            Status = EntryStatus.Disabled
        };
        _store.AddEntry(entry);

        var cfg = _store.Load();
        cfg.Settings.KillOnDisable = true;
        _store.Save(cfg);

        // Повторное обнаружение ключа в системе (браузер запущен и пересоздал Run-ключ)
        var isNew = WatcherHelper.AddOrUpdate(_store, _notifications, entry);

        Assert.False(isNew);
        var stored = _store.FindEntry(e => e.Id == entryId);
        Assert.NotNull(stored);
        Assert.Equal(EntryStatus.Disabled, stored.Status);
    }

    [Fact]
    public void AddOrUpdate_UserRemovedEntrySelfRestores_NotBlacklisted_RemovesFromSystemWithoutKill()
    {
        var entryId = "yandex_disk_456";
        var entry = new StartupEntry
        {
            Id = entryId,
            Name = "YandexDisk2",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\YandexDisk2",
            FilePath = @"C:\Users\User\AppData\Roaming\Yandex\YandexDisk2\YandexDisk2.exe",
            Status = EntryStatus.Enabled
        };

        // Запись ранее была удалена пользователем
        var removed = new RemovedEntry
        {
            Id = "rem_yd",
            OriginalEntryId = entryId,
            Name = entry.Name,
            Location = entry.Location,
            LocationDetail = entry.LocationDetail,
            FilePath = entry.FilePath,
            RemovalReason = "UserRemoved"
        };
        _store.AddRemovedEntry(removed);

        // Повторное появление записи без правила в ЧС
        var isNew = WatcherHelper.AddOrUpdate(_store, _notifications, entry, matchedRule: null);

        Assert.False(isNew);
        // Запись не добавлена в активные, так как была снова снята с автозапуска
        Assert.Null(_store.FindEntry(e => e.Id == entryId));
        // Но бэкап удалённой записи остаётся
        Assert.NotNull(_store.GetRemovedEntryByOriginalId(entryId));
    }
}

