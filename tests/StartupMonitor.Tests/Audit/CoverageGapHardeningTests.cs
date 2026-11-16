using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Audit;

/// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
/// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
/// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
/// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
/// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
public sealed class CoverageGapHardeningTests : IDisposable
{
    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "StartupMonitorAudit_" + Guid.NewGuid().ToString("N"));

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
        /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
        public override DateTimeOffset GetUtcNow() => now;

        /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
        /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
        public void Advance(TimeSpan delta) => now += delta;
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    private JsonDataStore CreateStore()
        => new(Path.Combine(_directory, "startup-monitor.json"));

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task InitialScanTracker_AllWatchersReport_CompletesWait()
    {
        var tracker = new InitialScanTracker(watchersCount: 3);

        tracker.MarkCompleted();
        tracker.MarkCompleted();
        Assert.False(tracker.IsCompleted);

        tracker.MarkCompleted();

        Assert.True(tracker.IsCompleted);
        Assert.Equal("Завершено", tracker.CurrentStage);
        await tracker.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task InitialScanTracker_ExtraReports_DoNotThrowAndKeepCompletedState()
    {
        var tracker = new InitialScanTracker(watchersCount: 2);

        tracker.MarkCompleted();
        tracker.MarkCompleted();
        tracker.MarkCompleted();
        tracker.MarkCompleted();

        Assert.True(tracker.IsCompleted);
        await tracker.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SlidingWindowTracker_BlankIp_IsRejected(string? ip)
    {
        var tracker = new SlidingWindowTracker();

        var triggered = tracker.RecordConnectionAndCheckThreshold(
            ip!, threshold: 5, window: TimeSpan.FromSeconds(10), out var currentRate);

        Assert.False(triggered);
        Assert.Equal(0, currentRate);
        Assert.Equal(0, tracker.TrackedKeysCount);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void SlidingWindowTracker_NonPositiveLimits_AreClamped()
    {
        var tracker = new SlidingWindowTracker(threshold: 0, window: TimeSpan.FromSeconds(1), maxCapacity: 0);

        Assert.Equal(1, tracker.Threshold);
        Assert.Equal(1, tracker.MaxCapacity);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void SlidingWindowTracker_TimestampExactlyAtWindowBoundary_IsRetained()
    {
        var tracker = new SlidingWindowTracker(threshold: 5, window: TimeSpan.FromSeconds(60));
        var ip = "203.0.113.7";
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        tracker.RecordConnection(ip, start);
        tracker.RecordConnection(ip, start.AddSeconds(60));

        Assert.Equal(2, tracker.GetConnectionCount(ip, start.AddSeconds(60)));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void SlidingWindowTracker_ResetRulesForIp_RemovesOnlyMatchingPrefix()
    {
        var tracker = new SlidingWindowTracker();
        tracker.RecordConnection("10.1.1.1|rule-a");
        tracker.RecordConnection("10.1.1.1|rule-b");
        tracker.RecordConnection("10.2.2.2|rule-a");

        tracker.ResetRulesForIp("10.1.1.1");

        Assert.False(tracker.IsTracked("10.1.1.1|rule-a"));
        Assert.False(tracker.IsTracked("10.1.1.1|rule-b"));
        Assert.True(tracker.IsTracked("10.2.2.2|rule-a"));
        Assert.Equal(1, tracker.TrackedKeysCount);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SlidingWindowTracker_ResetRulesForIp_RejectsBlankAddress(string? ip)
    {
        var tracker = new SlidingWindowTracker();

        Assert.ThrowsAny<ArgumentException>(() => tracker.ResetRulesForIp(ip!));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task SlidingWindowTracker_ConcurrentRecording_CountsEveryConnection()
    {
        const int workers = 8;
        const int perWorker = 250;
        var tracker = new SlidingWindowTracker(threshold: int.MaxValue, window: TimeSpan.FromMinutes(10));
        var ip = "198.51.100.23";

        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
        {
            for (var index = 0; index < perWorker; index++)
                tracker.RecordConnection(ip);
        })));

        Assert.Equal(workers * perWorker, tracker.GetConnectionCount(ip));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task SlidingWindowTracker_ConcurrentUniqueIps_NeverExceedsCapacity()
    {
        const int capacity = 64;
        var tracker = new SlidingWindowTracker(
            threshold: 1, window: TimeSpan.FromMinutes(1), maxCapacity: capacity);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 100; index++)
                tracker.RecordConnection($"10.20.{worker}.{index}");
        })));

        Assert.Equal(capacity, tracker.TrackedKeysCount);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void MonitoringStateCoordinator_ReentrantHandler_DeliversTransitionsInOrder()
    {
        var coordinator = new MonitoringStateCoordinator(CreateStore());
        var states = new List<bool>();
        var reentered = 0;

        coordinator.StateChanged += (_, paused) =>
        {
            lock (states) states.Add(paused);
            if (paused && Interlocked.Exchange(ref reentered, 1) == 0)
                coordinator.SetPaused(false);
        };

        coordinator.SetPaused(true);

        Assert.False(coordinator.IsPaused);
        lock (states) Assert.Equal([true, false], states);
        Assert.False(CreateStore().Load().Settings.IsMonitoringPaused);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task MonitoringStateCoordinator_ConcurrentSetPaused_KeepsAlternatingSequence()
    {
        var coordinator = new MonitoringStateCoordinator(CreateStore());
        var lastState = -1;
        var duplicateTransitions = 0;
        var gate = new object();

        coordinator.StateChanged += (_, paused) =>
        {
            var state = paused ? 1 : 0;
            lock (gate)
            {
                if (lastState == state) duplicateTransitions++;
                lastState = state;
            }
        };

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 50; index++)
                coordinator.SetPaused(((worker + index) & 1) == 0);
        })));

        coordinator.SetPaused(true);
        coordinator.SetPaused(false);

        Assert.False(coordinator.IsPaused);
        Assert.False(CreateStore().Load().Settings.IsMonitoringPaused);
        Assert.Equal(0, duplicateTransitions);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task BoundedLineReader_FrameExactlyAtLimit_IsAccepted()
    {
        var reader = new BoundedLineReader(new StringReader("ABCDE\n"), maximumCharacters: 5);

        var frame = await reader.ReadLineAsync();

        Assert.Equal("ABCDE", frame);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task BoundedLineReader_FrameOverLimit_IsRejected()
    {
        var reader = new BoundedLineReader(new StringReader("ABCDEF\n"), maximumCharacters: 5);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task BoundedLineReader_IncompleteFrameAtEof_IsRejected()
    {
        var reader = new BoundedLineReader(new StringReader("partial"), maximumCharacters: 32);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task BoundedLineReader_EofBetweenFrames_ReturnsNull()
    {
        var reader = new BoundedLineReader(new StringReader(string.Empty), maximumCharacters: 32);

        Assert.Null(await reader.ReadLineAsync());
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task BoundedLineReader_CrlfFrame_RemovesCarriageReturn()
    {
        var reader = new BoundedLineReader(new StringReader("payload\r\n"), maximumCharacters: 32);

        Assert.Equal("payload", await reader.ReadLineAsync());
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task BoundedLineReader_CancelledToken_ThrowsOperationCanceled()
    {
        var reader = new BoundedLineReader(new StringReader("payload\n"), maximumCharacters: 32);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await reader.ReadLineAsync(cts.Token));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void BoundedLineReader_InvalidConstructorArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new BoundedLineReader(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLineReader(new StringReader(""), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLineReader(new StringReader(""), -1));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task PipeClient_SendAsync_NullRequest_Throws()
    {
        using var client = new PipeClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendAsync(null!));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PipeClient_SendAsync_BlankType_Throws(string? type)
    {
        using var client = new PipeClient();
        var request = new PipeMessage { Type = type! };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.SendAsync(request));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public async Task PipeClient_SendAsync_AfterDispose_ReturnsNull()
    {
        var client = new PipeClient();
        client.Dispose();

        var response = await client.SendAsync(new PipeMessage { Type = PipeMessageTypes.GetStatus });

        Assert.Null(response);
        Assert.False(client.IsConnected);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void EventPipeServer_NullDependencies_AreRejected()
    {
        var logger = Substitute.For<ILogger<EventPipeServer>>();

        Assert.Throws<ArgumentNullException>(() => new EventPipeServer(null!, logger));

        using var notifications = new NotificationService(new InitialScanTracker(1));
        Assert.Throws<ArgumentNullException>(() => new EventPipeServer(notifications, null!));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EventPipeServer_BlankPipeName_IsRejected(string pipeName)
    {
        using var notifications = new NotificationService(new InitialScanTracker(1));
        var logger = Substitute.For<ILogger<EventPipeServer>>();

        Assert.ThrowsAny<ArgumentException>(() => new EventPipeServer(notifications, logger, pipeName));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void NetworkTrafficHistory_NullRequest_IsRejected()
    {
        using var service = CreateTrafficService(new FixedClock(DateTimeOffset.UnixEpoch));

        Assert.Throws<ArgumentNullException>(() => service.GetHistory(null!));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NetworkTrafficHistory_NonPositiveRange_IsRejected(int deltaSeconds)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        using var service = CreateTrafficService(clock);
        var now = clock.GetUtcNow().UtcDateTime;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.GetHistory(new NetworkTrafficHistoryRequest(now, now.AddSeconds(deltaSeconds), 1)));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void NetworkTrafficHistory_RetentionBoundary_IsEnforced()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        using var service = CreateTrafficService(clock);
        var now = clock.GetUtcNow().UtcDateTime;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.GetHistory(new NetworkTrafficHistoryRequest(now, now.AddDays(7).AddSeconds(1), 240)));

        var points = service.GetHistory(new NetworkTrafficHistoryRequest(now, now.AddDays(7), 240));
        Assert.Equal(240, points.Count);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void NetworkTrafficHistory_NullLoggerAndCounters_AreRejected()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));

        Assert.Throws<ArgumentNullException>(() => new NetworkTrafficHistoryService(CreateStore(), null!));

        using var service = CreateTrafficService(clock);
        Assert.Throws<ArgumentNullException>(() => service.RecordCounters(clock.GetUtcNow().UtcDateTime, null!));
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void NetworkTrafficHistory_InvalidCounterEntries_AreFiltered()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        using var service = CreateTrafficService(clock);
        var now = clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 100, 100)]);

        var invalidCounters = new NetworkInterfaceTrafficCounter[]
        {
            null!,
            new(string.Empty, 200, 200),
            new("adapter", -1, 150)
        };
        service.RecordCounters(now.AddSeconds(5), invalidCounters);

        var point = Assert.Single(service.GetHistory(new(now, now.AddSeconds(5), 1)));
        Assert.False(point.HasMeasurement);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void NetworkTrafficHistory_ThirtySecondInterval_IsMeasured()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        using var service = CreateTrafficService(clock);
        var now = clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 0, 0)]);
        service.RecordCounters(now.AddSeconds(30), [new("adapter", 300, 30)]);

        var point = Assert.Single(service.GetHistory(new(now, now.AddSeconds(30), 1)));

        Assert.True(point.HasMeasurement);
        Assert.Equal(10, point.InboundBps, 8);
        Assert.Equal(1, point.OutboundBps, 8);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void NetworkTrafficHistory_OverThirtySeconds_IsUnmeasured()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        using var service = CreateTrafficService(clock);
        var now = clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 0, 0)]);
        service.RecordCounters(now.AddMilliseconds(30_001), [new("adapter", 300, 30)]);

        var point = Assert.Single(service.GetHistory(new(now, now.AddMilliseconds(30_001), 1)));

        Assert.False(point.HasMeasurement);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    [Fact]
    public void NetworkTrafficHistory_CorruptFile_LogsWarning()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "network-traffic-history.json"),
            "[invalid-json");
        var logger = Substitute.For<ILogger<NetworkTrafficHistoryService>>();

        using var service = new NetworkTrafficHistoryService(CreateStore(), logger);

        Assert.Contains(logger.ReceivedCalls(), call =>
            call.GetMethodInfo().Name == nameof(ILogger.Log) &&
            call.GetArguments().Length > 0 &&
            call.GetArguments()[0] is LogLevel.Warning);
    }

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    private NetworkTrafficHistoryService CreateTrafficService(TimeProvider clock)
        => new(
            CreateStore(),
            NullLogger<NetworkTrafficHistoryService>.Instance,
            source: Substitute.For<INetworkTrafficCounterSource>(),
            timeProvider: clock);

    /// <summary>Verifies test scenario for CoverageGapHardeningTests.cs.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
