using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Provides operations for this member.
/// </summary>
public sealed class SlidingWindowTrackerTests
{
    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void RecordConnection_BelowThreshold_DoesNotTrigger()
    {
        // Verifies component behavior and assertions.
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(60));
        var ip = "194.26.29.112";
        var baseTime = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        // Verifies component behavior and assertions.
        for (int i = 0; i < 4; i++)
        {
            var triggered = tracker.RecordConnection(ip, baseTime.AddSeconds(i));
            Assert.False(triggered, $"Соединение #{i + 1} не должно было вызвать срабатывание.");
        }

        Assert.Equal(4, tracker.GetConnectionCount(ip, baseTime.AddSeconds(4)));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void RecordConnection_ExactlyAtThreshold_Triggers()
    {
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(60));
        var ip = "194.26.29.112";
        var baseTime = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 4; i++)
        {
            Assert.False(tracker.RecordConnection(ip, baseTime.AddSeconds(i)));
        }

        // Verifies component behavior and assertions.
        var triggeredOnFifth = tracker.RecordConnection(ip, baseTime.AddSeconds(4));
        Assert.True(triggeredOnFifth);
        Assert.Equal(5, tracker.GetConnectionCount(ip, baseTime.AddSeconds(4)));

        // Verifies component behavior and assertions.
        var triggeredOnSixth = tracker.RecordConnection(ip, baseTime.AddSeconds(5));
        Assert.True(triggeredOnSixth);
        Assert.Equal(6, tracker.GetConnectionCount(ip, baseTime.AddSeconds(5)));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void WindowExpiration_OldTimestampsPruned_CounterResets()
    {
        // Verifies component behavior and assertions.
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(10));
        var ip = "45.142.214.9";
        var t0 = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        // Verifies component behavior and assertions.
        for (int i = 0; i < 4; i++)
        {
            tracker.RecordConnection(ip, t0.AddSeconds(i));
        }

        Assert.Equal(4, tracker.GetConnectionCount(ip, t0.AddSeconds(3)));

        // Verifies component behavior and assertions.
        var tExpired = t0.AddSeconds(15);

        // Verifies component behavior and assertions.
        var triggeredAfterExpiry = tracker.RecordConnection(ip, tExpired);
        Assert.False(triggeredAfterExpiry);
        Assert.Equal(1, tracker.GetConnectionCount(ip, tExpired));
    }

    [Theory]
    [InlineData(TimeWindowUnit.Second, 1)]
    [InlineData(TimeWindowUnit.Minute, 60)]
    [InlineData(TimeWindowUnit.Hour, 3600)]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void SupportedTimeWindowUnits_EvaluatesWithinAndOutsideWindow(TimeWindowUnit unit, int windowSeconds)
    {
        var tracker = new SlidingWindowTracker(threshold: 3, unit: unit);
        var ip = "91.240.118.4";
        var t0 = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        // Verifies component behavior and assertions.
        Assert.False(tracker.RecordConnection(ip, t0));
        Assert.False(tracker.RecordConnection(ip, t0.AddMilliseconds(100)));

        // Verifies component behavior and assertions.
        var insideWindowTime = t0.AddSeconds(windowSeconds * 0.5);
        Assert.True(tracker.RecordConnection(ip, insideWindowTime));

        // Verifies component behavior and assertions.
        var outsideWindowTime = insideWindowTime.AddSeconds(windowSeconds + 2);
        Assert.False(tracker.RecordConnection(ip, outsideWindowTime));
        Assert.Equal(1, tracker.GetConnectionCount(ip, outsideWindowTime));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void LruEviction_ExceedingCapacity_EvictsOldestInactiveEntry()
    {
        // Verifies component behavior and assertions.
        const int capacity = 3;
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(60), maxCapacity: capacity);
        var baseTime = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        var ip1 = "1.1.1.1";
        var ip2 = "2.2.2.2";
        var ip3 = "3.3.3.3";
        var ip4 = "4.4.4.4";

        tracker.RecordConnection(ip1, baseTime.AddSeconds(1));
        tracker.RecordConnection(ip2, baseTime.AddSeconds(2));
        tracker.RecordConnection(ip3, baseTime.AddSeconds(3));

        Assert.Equal(3, tracker.TrackedKeysCount);
        Assert.True(tracker.IsTracked(ip1));
        Assert.True(tracker.IsTracked(ip2));
        Assert.True(tracker.IsTracked(ip3));

        // Verifies component behavior and assertions.
        tracker.RecordConnection(ip1, baseTime.AddSeconds(4));

        // Verifies component behavior and assertions.
        tracker.RecordConnection(ip4, baseTime.AddSeconds(5));

        Assert.Equal(3, tracker.TrackedKeysCount);
        Assert.True(tracker.IsTracked(ip1));
        Assert.False(tracker.IsTracked(ip2)); // ip2 is evicted
        Assert.True(tracker.IsTracked(ip3));
        Assert.True(tracker.IsTracked(ip4));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ClearAndRemove_OperatesCorrectly()
    {
        var tracker = new SlidingWindowTracker(threshold: 2, window: TimeSpan.FromSeconds(10));
        var ip = "185.12.33.1";

        tracker.RecordConnection(ip);
        Assert.True(tracker.IsTracked(ip));

        var removed = tracker.Remove(ip);
        Assert.True(removed);
        Assert.False(tracker.IsTracked(ip));
        Assert.Equal(0, tracker.GetConnectionCount(ip));

        tracker.RecordConnection("10.0.0.1");
        tracker.RecordConnection("10.0.0.2");
        Assert.Equal(2, tracker.TrackedKeysCount);

        tracker.Clear();
        Assert.Equal(0, tracker.TrackedKeysCount);
    }
}
