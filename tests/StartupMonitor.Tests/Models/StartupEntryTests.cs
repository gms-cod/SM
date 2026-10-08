using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>
/// Тесты для модели StartupEntry: проверка значений по умолчанию, свойств при создании, длин enum'ов.
/// </summary>
public class StartupEntryTests
{
    /// <summary>
    /// Проверяет, что при создании пустой записи проставляются ID, статус Enabled и дата DetectedAt.
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
    /// Проверяет, что при создании записи с заполненными полями все значения сохраняются корректно.
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
    /// Проверяет количество значений в EntryLocation (29 типов автозапуска).
    /// </summary>
    [Fact]
    public void EntryLocation_AllValuesCovered()
    {
        var values = Enum.GetValues<EntryLocation>();
        Assert.Equal(29, values.Length);
    }

    /// <summary>
    /// Проверяет количество значений в EntryStatus (5 статусов: Enabled, Disabled, Blocked, Removed, Delayed).
    /// </summary>
    [Fact]
    public void EntryStatus_AllValuesCovered()
    {
        var values = Enum.GetValues<EntryStatus>();
        Assert.Equal(5, values.Length);
    }
}
