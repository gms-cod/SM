using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

public sealed class SlidingWindowTrackerTests
{
    [Fact]
    public void RecordConnection_BelowThreshold_DoesNotTrigger()
    {
        // Порог: 5 подключений за 60 секунд
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(60));
        var ip = "194.26.29.112";
        var baseTime = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        // Регистрация 4 соединений (< 5)
        for (int i = 0; i < 4; i++)
        {
            var triggered = tracker.RecordConnection(ip, baseTime.AddSeconds(i));
            Assert.False(triggered, $"Соединение #{i + 1} не должно было вызвать срабатывание.");
        }

        Assert.Equal(4, tracker.GetConnectionCount(ip, baseTime.AddSeconds(4)));
    }

    [Fact]
    public void RecordConnection_ExactlyAtThreshold_Triggers()
    {
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(60));
        var ip = "194.26.29.112";
        var baseTime = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 4; i++)
        {
            Assert.False(tracker.RecordConnection(ip, baseTime.AddSeconds(i)));
        }

        // 5-е соединение ровно на пороге -> должно вернуть True
        var triggeredOnFifth = tracker.RecordConnection(ip, baseTime.AddSeconds(4));
        Assert.True(triggeredOnFifth);
        Assert.Equal(5, tracker.GetConnectionCount(ip, baseTime.AddSeconds(4)));

        // 6-е соединение (выше порога) -> также True
        var triggeredOnSixth = tracker.RecordConnection(ip, baseTime.AddSeconds(5));
        Assert.True(triggeredOnSixth);
        Assert.Equal(6, tracker.GetConnectionCount(ip, baseTime.AddSeconds(5)));
    }

    [Fact]
    public void WindowExpiration_OldTimestampsPruned_CounterResets()
    {
        // Окно 10 секунд, порог 5
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(10));
        var ip = "45.142.214.9";
        var t0 = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        // Добавляем 4 пакета в момент t0
        for (int i = 0; i < 4; i++)
        {
            tracker.RecordConnection(ip, t0.AddSeconds(i));
        }

        Assert.Equal(4, tracker.GetConnectionCount(ip, t0.AddSeconds(3)));

        // Смещаем время за пределы окна: t0 + 15 секунд (окно 10с истекло для первых пакетов)
        var tExpired = t0.AddSeconds(15);

        // Старые 4 метки должны быть вытеснены, счетчик только 1 пакет
        var triggeredAfterExpiry = tracker.RecordConnection(ip, tExpired);
        Assert.False(triggeredAfterExpiry);
        Assert.Equal(1, tracker.GetConnectionCount(ip, tExpired));
    }

    [Theory]
    [InlineData(TimeWindowUnit.Second, 1)]
    [InlineData(TimeWindowUnit.Minute, 60)]
    [InlineData(TimeWindowUnit.Hour, 3600)]
    public void SupportedTimeWindowUnits_EvaluatesWithinAndOutsideWindow(TimeWindowUnit unit, int windowSeconds)
    {
        var tracker = new SlidingWindowTracker(threshold: 3, unit: unit);
        var ip = "91.240.118.4";
        var t0 = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        // 2 соединения в начале окна
        Assert.False(tracker.RecordConnection(ip, t0));
        Assert.False(tracker.RecordConnection(ip, t0.AddMilliseconds(100)));

        // Соединение внутри окна -> 3-е соединение ровно на пороге триггерит бан
        var insideWindowTime = t0.AddSeconds(windowSeconds * 0.5);
        Assert.True(tracker.RecordConnection(ip, insideWindowTime));

        // Соединение строго за пределами окна -> старые соединения истекли
        var outsideWindowTime = insideWindowTime.AddSeconds(windowSeconds + 2);
        Assert.False(tracker.RecordConnection(ip, outsideWindowTime));
        Assert.Equal(1, tracker.GetConnectionCount(ip, outsideWindowTime));
    }

    [Fact]
    public void LruEviction_ExceedingCapacity_EvictsOldestInactiveEntry()
    {
        // Емкость: максимум 3 уникальных IP
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

        // Обращаемся к ip1 (обновляем LastAccessed), теперь ip2 становится самым старым (LRU)
        tracker.RecordConnection(ip1, baseTime.AddSeconds(4));

        // Добавляем 4-й IP при емкости 3 -> ip2 должен быть вытеснен
        tracker.RecordConnection(ip4, baseTime.AddSeconds(5));

        Assert.Equal(3, tracker.TrackedKeysCount);
        Assert.True(tracker.IsTracked(ip1));
        Assert.False(tracker.IsTracked(ip2)); // ip2 вытеснен
        Assert.True(tracker.IsTracked(ip3));
        Assert.True(tracker.IsTracked(ip4));
    }

    [Fact]
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
