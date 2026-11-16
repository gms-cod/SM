using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>Verifies test scenario for ConfigurationMixedApiRegressionTests.cs.</summary>
public sealed class ConfigurationMixedApiRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StartupMonitorMixedApi_" + Guid.NewGuid().ToString("N"));

    /// <summary>Verifies test scenario for ConfigurationMixedApiRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for ConfigurationMixedApiRegressionTests.cs.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MixedApi_StatusReversalPreservesConcurrentRatingAndAddition(bool entryFirst)
    {
        var store = new JsonDataStore(_directory);
        store.AddEntry(new StartupEntry { Id = "subject", Status = EntryStatus.Enabled });
        var snapshot = store.Load();
        var entry = Assert.Single(snapshot.Entries);
        entry.Status = EntryStatus.Disabled;
        if (entryFirst) store.UpdateEntry(entry);
        else store.Save(snapshot);

        var concurrent = store.FindEntry(item => item.Id == "subject") ?? throw new InvalidOperationException("Запись не найдена.");
        concurrent.AiRating = 8;
        store.UpdateEntry(concurrent);
        store.AddEntry(new StartupEntry { Id = "concurrent", Name = "New" });

        entry.Status = EntryStatus.Enabled;
        if (entryFirst) store.Save(snapshot);
        else store.UpdateEntry(entry);

        var actual = store.Load();
        var subject = Assert.Single(actual.Entries, item => item.Id == "subject");
        Assert.Equal(EntryStatus.Enabled, subject.Status);
        Assert.Equal(8, subject.AiRating);
        Assert.Contains(actual.Entries, item => item.Id == "concurrent");
    }

    /// <summary>Verifies test scenario for ConfigurationMixedApiRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for ConfigurationMixedApiRegressionTests.cs.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MixedApi_RatingReversalPreservesConcurrentStatus(bool entryFirst)
    {
        var store = new JsonDataStore(_directory);
        store.AddEntry(new StartupEntry { Id = "subject", Status = EntryStatus.Enabled });
        var snapshot = store.Load();
        var entry = Assert.Single(snapshot.Entries);
        entry.AiRating = 7;
        if (entryFirst) store.UpdateEntry(entry);
        else store.Save(snapshot);

        var concurrent = store.FindEntry(item => item.Id == "subject") ?? throw new InvalidOperationException("Запись не найдена.");
        concurrent.Status = EntryStatus.Disabled;
        store.UpdateEntry(concurrent);
        entry.AiRating = 0;
        if (entryFirst) store.Save(snapshot);
        else store.UpdateEntry(entry);

        var actual = Assert.Single(store.Load().Entries);
        Assert.Equal(EntryStatus.Disabled, actual.Status);
        Assert.Equal(0, actual.AiRating);
    }

    /// <summary>Verifies test scenario for ConfigurationMixedApiRegressionTests.cs.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
