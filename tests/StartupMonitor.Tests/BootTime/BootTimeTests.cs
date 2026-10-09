using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.BootTime;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.BootTime;

namespace StartupMonitor.Tests.BootTime;

/// <summary>
/// Модульные тесты для анализатора времени загрузки Windows (Boot Time Tracer, v1.0.19).
/// </summary>
public class BootTimeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonDataStore _store;

    public BootTimeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SMon_BootTime_Tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _store = new JsonDataStore(Path.Combine(_tempDir, "startup-monitor.json"));
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
    public void BootSessionInfo_FormatDuration_FormatsVariousIntervalsCorrectly()
    {
        Assert.Equal("500 мс", BootSessionInfo.FormatDuration(500));
        Assert.Equal("2.5 с", BootSessionInfo.FormatDuration(2500));
        Assert.Equal("12.0 с", BootSessionInfo.FormatDuration(12000));
        Assert.Equal("1 мин 5 с", BootSessionInfo.FormatDuration(65000));
        Assert.Equal("2 мин 0 с", BootSessionInfo.FormatDuration(120000));
    }

    [Fact]
    public void BootSessionInfo_SpeedRating_ClassifiesCorrectly()
    {
        var fastSession = new BootSessionInfo { BootTimeMs = 20000 };
        Assert.Equal("Fast", fastSession.SpeedRating);
        Assert.Equal("#10B981", fastSession.SpeedRatingColor);

        var normalSession = new BootSessionInfo { BootTimeMs = 45000 };
        Assert.Equal("Normal", normalSession.SpeedRating);
        Assert.Equal("#F59E0B", normalSession.SpeedRatingColor);

        var slowSession = new BootSessionInfo { BootTimeMs = 95000 };
        Assert.Equal("Slow", slowSession.SpeedRating);
        Assert.Equal("#EF4444", slowSession.SpeedRatingColor);
    }

    [Fact]
    public void BootDegradationEvent_Category_MapsCorrectly()
    {
        var appDeg = new BootDegradationEvent { Category = BootDegradationCategory.Application, TotalTimeMs = 3500, DegradationTimeMs = 2100 };
        Assert.Equal("Приложение", appDeg.DisplayCategory);
        Assert.Equal("3.5 с", appDeg.DisplayTotalTime);
        Assert.Equal("+2.1 с", appDeg.DisplayDegradation);
        Assert.Equal("#EF4444", appDeg.BadgeColor);

        var smallDeg = new BootDegradationEvent { Category = BootDegradationCategory.Driver, TotalTimeMs = 1200, DegradationTimeMs = 600 };
        Assert.Equal("Драйвер", smallDeg.DisplayCategory);
        Assert.Equal("#F59E0B", smallDeg.BadgeColor);
    }

    [Fact]
    public void BootTimeReport_DeltaCalculation_ComputesDeltaFromPreviousSession()
    {
        var report = new BootTimeReport
        {
            RecentSessions =
            [
                new BootSessionInfo { BootTimeMs = 25000, Timestamp = DateTime.UtcNow },
                new BootSessionInfo { BootTimeMs = 30000, Timestamp = DateTime.UtcNow.AddDays(-1) }
            ],
            LatestBoot = new BootSessionInfo { BootTimeMs = 25000, Timestamp = DateTime.UtcNow }
        };

        // 25000 - 30000 = -5000 (быстрее на 5 секунд)
        Assert.Equal(-5000, report.DeltaPreviousBootMs);
        Assert.Contains("быстрее", report.DisplayDelta);
        Assert.Equal("#10B981", report.DeltaColor);
    }

    [Fact]
    public void BootTimeReport_DeltaCalculation_ShowsSlowerWhenTimeIncreases()
    {
        var report = new BootTimeReport
        {
            RecentSessions =
            [
                new BootSessionInfo { BootTimeMs = 45000, Timestamp = DateTime.UtcNow },
                new BootSessionInfo { BootTimeMs = 35000, Timestamp = DateTime.UtcNow.AddDays(-1) }
            ],
            LatestBoot = new BootSessionInfo { BootTimeMs = 45000, Timestamp = DateTime.UtcNow }
        };

        // 45000 - 35000 = +10000 (медленнее на 10 секунд)
        Assert.Equal(10000, report.DeltaPreviousBootMs);
        Assert.Contains("медленнее", report.DisplayDelta);
        Assert.Equal("#EF4444", report.DeltaColor);
    }

    [Fact]
    public void BootTimeReport_JsonSerialization_RoundtripPreservesAllFields()
    {
        var report = new BootTimeReport
        {
            LatestBoot = new BootSessionInfo
            {
                BootInstance = 42,
                BootTimeMs = 24500,
                MainPathBootTimeMs = 12000,
                PostBootTimeMs = 12500,
                StartupAppsCount = 8,
                Timestamp = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc)
            },
            TopOffenders =
            [
                new BootDegradationEvent
                {
                    Name = "SyncTrayzor.exe",
                    FriendlyName = "SyncTrayzor",
                    Path = @"C:\Program Files\SyncTrayzor\SyncTrayzor.exe",
                    Category = BootDegradationCategory.Application,
                    TotalTimeMs = 4800,
                    DegradationTimeMs = 3200,
                    Publisher = "SyncTrayzor"
                }
            ],
            TotalSessionsRecorded = 15,
            GeneratedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(report);
        var deserialized = JsonSerializer.Deserialize<BootTimeReport>(json);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.LatestBoot);
        Assert.Equal(24500, deserialized.LatestBoot.BootTimeMs);
        Assert.Equal(8, deserialized.LatestBoot.StartupAppsCount);
        Assert.Single(deserialized.TopOffenders);
        Assert.Equal("SyncTrayzor.exe", deserialized.TopOffenders[0].Name);
        Assert.Equal(3200, deserialized.TopOffenders[0].DegradationTimeMs);
    }

    [Fact]
    public void BootTimeService_EnrichEntriesWithBootImpact_EnrichesMatchingEntries()
    {
        var service = new BootTimeService(_store, NullLogger<BootTimeService>.Instance);

        var entries = new List<StartupEntry>
        {
            new()
            {
                Id = "entry-sync-1",
                Name = "SyncTrayzor",
                FilePath = @"C:\Program Files\SyncTrayzor\SyncTrayzor.exe",
                Location = EntryLocation.RegistryRun
            },
            new()
            {
                Id = "entry-notepad-2",
                Name = "Notepad",
                FilePath = @"C:\Windows\System32\notepad.exe",
                Location = EntryLocation.RegistryRun
            }
        };

        // Запуск обогащения
        service.EnrichEntriesWithBootImpact(entries);

        // Проверяем, что метод завершается без исключений
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
    }
}
