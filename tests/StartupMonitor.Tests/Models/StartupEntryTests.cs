using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>Verifies test scenario for StartupEntryTests.cs.</summary>
public class StartupEntryTests
{
    /// <summary>
    /// <summary>Verifies test scenario for StartupEntryTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CreateEntry_DefaultValues()
    {
        var entry = new StartupEntry();

        Assert.NotEmpty(entry.Id);
        Assert.Equal(EntryStatus.Enabled, entry.Status);
        Assert.NotEqual(default, entry.DetectedAt);
        Assert.False(entry.FromBlacklist);
    }

    /// <summary>
    /// <summary>Verifies test scenario for StartupEntryTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CreateEntry_WithValues()
    {
        var entry = new StartupEntry
        {
            Name = "TestApp",
            FilePath = @"C:\Program Files\Test\app.exe",
            Publisher = "Test Corp",
            Location = EntryLocation.RegistryRun,
            Status = EntryStatus.Blocked
        };

        Assert.Equal("TestApp", entry.Name);
        Assert.Equal(@"C:\Program Files\Test\app.exe", entry.FilePath);
        Assert.Equal("Test Corp", entry.Publisher);
        Assert.Equal(EntryLocation.RegistryRun, entry.Location);
        Assert.Equal(EntryStatus.Blocked, entry.Status);
    }

    /// <summary>
    /// <summary>Verifies test scenario for StartupEntryTests.cs.</summary>
    /// </summary>
    [Fact]
    public void EntryLocation_AllValuesCovered()
    {
        var values = Enum.GetValues<EntryLocation>();
        Assert.Equal(29, values.Length);
    }

    /// <summary>
    /// <summary>Verifies test scenario for StartupEntryTests.cs.</summary>
    /// </summary>
    [Fact]
    public void EntryStatus_AllValuesCovered()
    {
        var values = Enum.GetValues<EntryStatus>();
        Assert.Equal(5, values.Length);
    }

    /// <summary>
    /// <summary>Verifies test scenario for StartupEntryTests.cs.</summary>
    /// </summary>
    [Fact]
    public void IsUserRating_DefaultAndNotification_Works()
    {
        var entry = new StartupEntry();
        Assert.False(entry.IsUserRating);

        var changedProps = new List<string?>();
        entry.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        entry.IsUserRating = true;
        Assert.True(entry.IsUserRating);
        Assert.Contains(nameof(StartupEntry.IsUserRating), changedProps);
    }

    /// <summary>
    /// <summary>Verifies test scenario for StartupEntryTests.cs.</summary>
    /// </summary>
    [Fact]
    public void SetUserRatingRequest_PropertiesAssignment_Works()
    {
        var req = new SetUserRatingRequest
        {
            EntryId = "entry-123",
            Stars = 5,
            ResetToAi = false
        };

        Assert.Equal("entry-123", req.EntryId);
        Assert.Equal(5, req.Stars);
        Assert.False(req.ResetToAi);
    }
}
