using System.Diagnostics;
using System.Reflection;
using StartupMonitor.Service.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Проверяет настоящий read-only снимок NtQuerySystemInformation для ETW baseline.
/// Сессии ETW, подписки UI, сетевые правила и внешние процессы не создаются.
/// </summary>
public sealed class ProcessLifecycleSnapshotTests
{
    /// <summary>Проверяет точный FILETIME собственного процесса без включения метрик и обогащения метаданных.</summary>
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

    /// <summary>Проверяет отмену до нативного запроса без включения периодического сбора.</summary>
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

    /// <summary>Проверяет явный отказ после освобождения буфера вместо обращения к освобождённой памяти.</summary>
    [Fact]
    public void CaptureLifecycleSnapshot_Disposed_ThrowsObjectDisposedException()
    {
        using var enricher = new ProcessMetadataEnricher();
        using var tracker = new ProcessMetricsTracker(enricher, new ProcessStartupLinker());
        tracker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tracker.CaptureLifecycleSnapshot(CancellationToken.None));
    }
}
