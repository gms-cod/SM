using System.Diagnostics;
using System.Reflection;
using StartupMonitor.Service.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for ProcessLifecycleSnapshotTests.cs.</summary>
/// <summary>Verifies test scenario for ProcessLifecycleSnapshotTests.cs.</summary>
public sealed class ProcessLifecycleSnapshotTests
{
    /// <summary>Verifies test scenario for ProcessLifecycleSnapshotTests.cs.</summary>
    [Fact]
    public void CaptureLifecycleSnapshot_ReturnsCurrentProcessWithoutStartingSamplingOrMetadata()
    {
        using var current = Process.GetCurrentProcess();
        using var enricher = new ProcessMetadataEnricher();
        using var tracker = new ProcessMetricsTracker(enricher, new ProcessStartupLinker());
        var notifications = 0;
        tracker.SnapshotReady += (_, _) => Interlocked.Increment(ref notifications);

        var snapshot = tracker.CaptureLifecycleSnapshot(CancellationToken.None);

        var own = Assert.Single(snapshot, process => process.Pid == current.Id);
        Assert.Equal(current.StartTime.ToUniversalTime(), own.CreationTimeUtc);
        Assert.True(own.ParentPid > 0);
        Assert.False(string.IsNullOrWhiteSpace(own.ImageName));
        Assert.Equal(0, tracker.SubscriberCount);
        Assert.Equal(0, notifications);
        var cacheField = typeof(ProcessMetadataEnricher).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException("Не найден кэш обогащения метаданных.");
        var cache = cacheField.GetValue(enricher) as System.Collections.IDictionary
            ?? throw new InvalidOperationException("Кэш метаданных не предоставляет проверяемую коллекцию.");
        Assert.Empty(cache);
    }

    /// <summary>Verifies test scenario for ProcessLifecycleSnapshotTests.cs.</summary>
    [Fact]
    public void CaptureLifecycleSnapshot_PreCanceled_ThrowsCancellation()
    {
        using var enricher = new ProcessMetadataEnricher();
        using var tracker = new ProcessMetricsTracker(enricher, new ProcessStartupLinker());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => tracker.CaptureLifecycleSnapshot(cancellation.Token));

        Assert.Equal(0, tracker.SubscriberCount);
    }

    /// <summary>Verifies test scenario for ProcessLifecycleSnapshotTests.cs.</summary>
    [Fact]
    public void CaptureLifecycleSnapshot_Disposed_ThrowsObjectDisposedException()
    {
        using var enricher = new ProcessMetadataEnricher();
        using var tracker = new ProcessMetricsTracker(enricher, new ProcessStartupLinker());
        tracker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tracker.CaptureLifecycleSnapshot(CancellationToken.None));
    }
}
