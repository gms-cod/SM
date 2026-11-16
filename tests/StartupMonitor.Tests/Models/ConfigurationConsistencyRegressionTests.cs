using System.Text.Json;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Models;

/// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
public sealed class ConfigurationConsistencyRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StartupMonitorConsistency_" + Guid.NewGuid().ToString("N"));

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    private JsonDataStore CreateStore() => new(Path.Combine(_directory, "startup-monitor.json"));

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Theory]
    [InlineData("{\"settings\":null}")]
    [InlineData("{\"settings\":{\"port_defender\":null}}")]
    [InlineData("{\"settings\":{\"history_days\":2147483647}}")]
    [InlineData("{\"settings\":{\"active_bans\":[null]}}")]
    [InlineData("{\"settings\":{\"port_defender\":{\"rules\":[null]}}}")]
    public void SemanticCorruption_RecoversValidatedBackup(string corrupted)
    {
        CreateStore();
        File.WriteAllText(Path.Combine(_directory, "settings.json"), corrupted);
        File.WriteAllText(Path.Combine(_directory, "settings.json.backup"), "{\"theme\":\"light\",\"settings\":{\"history_days\":14}}");
        var config = CreateStore().Load();
        Assert.Equal("light", config.Theme);
        Assert.Equal(14, config.Settings.HistoryDays);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.json.corrupt")));
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void NullEntry_IsRejectedBeforeSnapshotClone()
    {
        CreateStore();
        File.WriteAllText(Path.Combine(_directory, "startup-entries.json"), "[null]");
        Assert.Empty(CreateStore().Load().Entries);
        Assert.True(File.Exists(Path.Combine(_directory, "startup-entries.json.corrupt")));
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void StaleSettingsSave_PreservesConcurrentEntryAndRepeatedSave()
    {
        var store = CreateStore();
        var snapshot = store.Load();
        store.AddEntry(new StartupEntry { Id = "concurrent", Name = "Concurrent" });
        snapshot.Theme = "light";
        store.Save(snapshot);
        snapshot.Language = "en";
        store.Save(snapshot);
        var current = store.Load();
        Assert.Equal("concurrent", Assert.Single(current.Entries).Id);
        Assert.Equal("light", current.Theme);
        Assert.Equal("en", current.Language);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void ParallelSnapshots_MergeIndependentSettings()
    {
        var store = CreateStore();
        var first = store.Load();
        var second = store.Load();
        first.Settings.StrictMode = true;
        second.Settings.NotifyOnNewEntry = false;
        store.Save(first);
        store.Save(second);
        var settings = store.GetSettings();
        Assert.True(settings.StrictMode);
        Assert.False(settings.NotifyOnNewEntry);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void ConflictingSnapshots_RejectSecondValueWithoutOverwritingFirst()
    {
        var store = CreateStore();
        var first = store.Load();
        var second = store.Load();
        first.Theme = "light";
        second.Theme = "system";
        store.Save(first);
        Assert.Throws<InvalidOperationException>(() => store.Save(second));
        Assert.Equal("light", store.Load().Theme);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void ConcurrentDelete_IsNotUndoneByOldSnapshot()
    {
        var store = CreateStore();
        store.AddEntry(new StartupEntry { Id = "removed", Name = "Old" });
        var old = store.Load();
        store.RemoveEntry("removed");
        old.Entries[0].AiRating = 8;
        store.Save(old);
        Assert.Empty(store.Load().Entries);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void EntrySnapshots_MergeRatingAndStatus()
    {
        var store = CreateStore();
        store.AddEntry(new StartupEntry { Id = "entry" });
        var rating = store.FindEntry(item => item.Id == "entry") ?? throw new InvalidOperationException();
        var status = store.FindEntry(item => item.Id == "entry") ?? throw new InvalidOperationException();
        status.Status = EntryStatus.Disabled;
        store.UpdateEntry(status);
        rating.AiRating = 7;
        store.UpdateEntry(rating);
        var actual = Assert.Single(store.Load().Entries);
        Assert.Equal(EntryStatus.Disabled, actual.Status);
        Assert.Equal(7, actual.AiRating);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void ReputationBatch_PreservesCurrentStatusAndUnrelatedFields()
    {
        var store = CreateStore();
        store.AddEntry(new StartupEntry { Id = "entry", Name = "Original" });
        var reputation = store.Load().Entries[0];
        var updated = store.Load().Entries[0];
        updated.Status = EntryStatus.Blocked;
        updated.Name = "Current";
        store.UpdateEntry(updated);
        reputation.KasperskyZone = "Green";
        reputation.KasperskyFileStatus = "Trusted";
        store.UpdateEntryReputations([reputation]);
        var actual = Assert.Single(store.Load().Entries);
        Assert.Equal("Current", actual.Name);
        Assert.Equal(EntryStatus.Blocked, actual.Status);
        Assert.Equal("Green", actual.KasperskyZone);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void ExpiredHistory_IsNotAppendedToDisk()
    {
        var store = CreateStore();
        store.AddHistory(new HistoryEntry { Id = "fresh", Timestamp = DateTime.UtcNow });
        store.AddHistory(new HistoryEntry { Id = "expired", Timestamp = DateTime.UtcNow.AddDays(-40) });
        var content = File.ReadAllText(Path.Combine(_directory, "history.jsonl"));
        Assert.Contains("fresh", content);
        Assert.DoesNotContain("expired", content);
        Assert.Single(CreateStore().Load().History);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    [Fact]
    public void SettingsClone_PreservesAllBanFields()
    {
        var expires = DateTime.UtcNow.AddMinutes(15);
        var settings = new AppSettings { ActiveBans = [new ActiveBanDto
        {
            Ip = "203.0.113.1", ExpiresAtUtc = expires, AttackRate = 12,
            Reason = "probe", RepeatCount = 3, RuleName = "rule"
        }] };
        var ban = Assert.Single(settings.Clone().ActiveBans);
        Assert.Equal(expires, ban.ExpiresAtUtc);
        Assert.Equal(12, ban.AttackRate);
        Assert.Equal("probe", ban.Reason);
        Assert.Equal(3, ban.RepeatCount);
        Assert.Equal("rule", ban.RuleName);
    }

    /// <summary>Verifies test scenario for ConfigurationConsistencyRegressionTests.cs.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
