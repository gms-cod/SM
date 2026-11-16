using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
/// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
public class ApplyBlacklistActionTests : IDisposable
{
    /// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}");
    /// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
    private readonly BlacklistManager _manager;
    /// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
    private readonly NotificationService _notifications;

    public ApplyBlacklistActionTests()
    {
        _store = new JsonDataStore(Path.Combine(_dir, "startup-monitor.json"));
        _manager = new BlacklistManager(_store);
        _notifications = new NotificationService(new InitialScanTracker(1));
    }

    /// <summary>
    /// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
    /// </summary>
    [Fact]
    public void NotifyAndRemove_DisablesFileAndKeepsEntryBlocked()
    {
        // Verifies component behavior and assertions.
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

        // Verifies component behavior and assertions.
        Assert.False(File.Exists(appFile));
        // Verifies component behavior and assertions.
        var stored = _store.FindEntry(e => e.Id == entry.Id);
        Assert.NotNull(stored);
        Assert.Equal(EntryStatus.Blocked, stored.Status);
        Assert.True(stored.FromBlacklist);
        Assert.True(File.Exists(appFile + ".disabled"));
        Assert.Equal("MZ fake", File.ReadAllText(appFile + ".disabled"));
        Assert.NotEmpty(stored.DisablePayload);
        Assert.Empty(_store.GetRemovedEntries());
    }

    /// <summary>
    /// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
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

        // Verifies component behavior and assertions.
        Assert.True(File.Exists(appFile));
        Assert.Equal(EntryStatus.Blocked, entry.Status);
        Assert.Empty(_store.GetRemovedEntries());
    }

    /// <summary>Verifies test scenario for ApplyBlacklistActionTests.cs.</summary>
    public void Dispose()
    {
        _notifications.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
