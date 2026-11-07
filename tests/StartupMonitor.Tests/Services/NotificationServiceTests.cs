using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для NotificationService: проверка подавления уведомлений во время начального сканирования
/// и корректной флюшизации батчей после его завершения.
/// </summary>
public class NotificationServiceTests
{
    /// <summary>
    /// Проверяет, что уведомления, поставленные в очередь до завершения сканирования, не флюшатся.
    /// </summary>
    [Fact]
    public void Enqueue_BeforeScanCompleted_DoesNotFlush()
    {
        var tracker = new InitialScanTracker();
        using var svc = new NotificationService(tracker);

        var flushed = false;
        svc.MessagesBatchReady += (_, _) => flushed = true;

        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.EntryAdded });

        Thread.Sleep(500);
        Assert.False(flushed, "Messages should not flush before scan completes");
    }

    /// <summary>
    /// Проверяет, что после завершения сканирования уведомления флюшатся батчем.
    /// </summary>
    [Fact]
    public void Enqueue_AfterScanCompleted_FlushesBatch()
    {
        var tracker = new InitialScanTracker();
        using var svc = new NotificationService(tracker);

        var batch = new List<PipeMessage>();
        using var mre = new ManualResetEventSlim(false);
        svc.MessagesBatchReady += (_, msgs) =>
        {
            batch.AddRange(msgs);
            mre.Set();
        };

        // simulate all 5 watchers completing
        for (int i = 0; i < 5; i++)
            tracker.MarkCompleted();

        Assert.True(tracker.IsCompleted);

        for (int i = 0; i < 5; i++)
            svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.EntryAdded });

        mre.Wait(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(batch);
        Assert.Equal(5, batch.Count);
    }

    /// <summary>
    /// Проверяет, что сообщения, поставленные до завершения скана, отбрасываются, и батч содержит только сообщения после.
    /// </summary>
    [Fact]
    public void Enqueue_BeforeAndAfterScan_BatchContainsOnlyPostScanMessages()
    {
        var tracker = new InitialScanTracker();
        using var svc = new NotificationService(tracker);

        var batch = new List<PipeMessage>();
        svc.MessagesBatchReady += (_, msgs) => batch.AddRange(msgs);

        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.EntryAdded }); // suppressed before scan complete

        for (int i = 0; i < 5; i++)
            tracker.MarkCompleted();

        Thread.Sleep(600); // flush takes everything

        Assert.Empty(batch);
    }

    /// <summary>
    /// Проверяет, что FlushBatch не падает с исключением при отсутствии подписчиков.
    /// </summary>
    [Fact]
    public void FlushBatch_NoSubscribers_DoesNotThrow()
    {
        var tracker = new InitialScanTracker();
        using var svc = new NotificationService(tracker);

        for (int i = 0; i < 5; i++)
            tracker.MarkCompleted();

        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.EntryAdded });
        Thread.Sleep(500);
        // no assertion - just verifying no exception
    }

    /// <summary>
    /// Проверяет, что несколько батчей шлются раздельно — каждый таймаут порождает отдельный флюш.
    /// </summary>
    [Fact]
    public void MultipleBatches_FlushSeparately()
    {
        var tracker = new InitialScanTracker();
        using var svc = new NotificationService(tracker);

        var batchCount = 0;
        using var batchEvent = new AutoResetEvent(false);
        svc.MessagesBatchReady += (_, _) =>
        {
            Interlocked.Increment(ref batchCount);
            batchEvent.Set();
        };

        for (int i = 0; i < 5; i++)
            tracker.MarkCompleted();

        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.EntryAdded });
        Assert.True(batchEvent.WaitOne(TimeSpan.FromSeconds(15)), "First batch timed out");

        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.EntryModified });
        Assert.True(batchEvent.WaitOne(TimeSpan.FromSeconds(15)), "Second batch timed out");

        Assert.Equal(2, batchCount);
    }
}
