using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for StaleOwnedShortcutTests.cs.</summary>
public sealed class StaleOwnedShortcutTests
{
    /// <summary>Verifies test scenario for StaleOwnedShortcutTests.cs.</summary>
    [Fact]
    public void MissingOwnedShortcut_RequiresExactIdentityAndNoBackup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "startup-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var entry = new StartupEntry
            {
                Name = "Startup Monitor.lnk", FilePath = Path.Combine(folder, "Startup Monitor.lnk"),
                LocationDetail = folder, Location = EntryLocation.StartupFolder, Status = EntryStatus.Disabled
            };
            Assert.True(EntryOperations.IsStaleOwnedStartupShortcut(entry, folder));
            entry.DisablePayload = "saved backup";
            Assert.False(EntryOperations.IsStaleOwnedStartupShortcut(entry, folder));
            entry.DisablePayload = "";
            entry.Status = EntryStatus.Enabled;
            Assert.False(EntryOperations.IsStaleOwnedStartupShortcut(entry, folder));
            entry.Status = EntryStatus.Disabled;
            entry.Name = "Another.lnk";
            Assert.False(EntryOperations.IsStaleOwnedStartupShortcut(entry, folder));
            entry.Name = "Startup Monitor.lnk";
            entry.FilePath = Path.Combine(folder, "other", "Startup Monitor.lnk");
            Assert.False(EntryOperations.IsStaleOwnedStartupShortcut(entry, folder));
            entry.FilePath = Path.Combine(folder, "Startup Monitor.lnk");
            entry.LocationDetail = Path.Combine(folder, "other");
            Assert.False(EntryOperations.IsStaleOwnedStartupShortcut(entry, folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>Verifies test scenario for StaleOwnedShortcutTests.cs.</summary>
    /// <summary>Verifies test scenario for StaleOwnedShortcutTests.cs.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(".disabled")]
    public void ExistingFileOrMissingDirectory_PreservesEntry(string suffix)
    {
        var folder = Path.Combine(Path.GetTempPath(), "startup-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var entry = new StartupEntry
            {
                Name = "Startup Monitor.lnk", FilePath = Path.Combine(folder, "Startup Monitor.lnk"),
                LocationDetail = folder, Location = EntryLocation.StartupFolder, Status = EntryStatus.Disabled
            };
            File.WriteAllText(entry.FilePath + suffix, "test");
            Assert.False(EntryOperations.IsStaleOwnedStartupShortcut(entry, folder));
            Assert.True(File.Exists(entry.FilePath + suffix));
            Assert.False(EntryOperations.IsStaleOwnedStartupShortcut(entry, Path.Combine(folder, "missing")));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
