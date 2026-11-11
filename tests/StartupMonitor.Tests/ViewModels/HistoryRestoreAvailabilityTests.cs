using System.Text.Json;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

/// <summary>Проверяет привязку восстановления к конкретной резервной копии события.</summary>
public sealed class HistoryRestoreAvailabilityTests
{
    /// <summary>Повторное удаление той же записи не разрешает откат старого события по новой копии.</summary>
    [Fact]
    public void RestoreRequiresExactBackupIdAndSurvivesSerialization()
    {
        var oldEvent = JsonSerializer.Deserialize<HistoryEntry>(JsonSerializer.Serialize(new HistoryEntry
        {
            ChangeType = "Removed", EntryName = "App", RestoreBackupId = "old-backup"
        }));
        Assert.NotNull(oldEvent);
        var newEvent = new HistoryEntry { ChangeType = "Removed", EntryName = "App", RestoreBackupId = "new-backup" };
        var legacyEvent = new HistoryEntry { ChangeType = "Removed", EntryName = "App" };

        MainViewModel.UpdateHistoryRestoreAvailability([oldEvent, newEvent, legacyEvent], [new RemovedEntry { Id = "new-backup" }]);

        Assert.False(oldEvent.CanRestore);
        Assert.True(newEvent.CanRestore);
        Assert.False(legacyEvent.CanRestore);
    }
}
