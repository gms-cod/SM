using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты для проверки Smart Notification Deduplication:
/// предотвращение повторного спама всплывающими алертами при циклических триггерах автозапуска.
/// </summary>
public class NotificationDeduplicationTests
{
    /// <summary>
    /// Проверяет, что повторные одинаковые уведомления BlacklistMatch в пределах Cooldown-окна подавляются.
    /// </summary>
    [Fact]
    public void Enqueue_DuplicateBlacklistMatch_SuppressesDuplicate()
    {
        var tracker = new InitialScanTracker();
        for (int i = 0; i < 5; i++) tracker.MarkCompleted();

        using var svc = new NotificationService(tracker);
        var batch = new List<PipeMessage>();
        using var mre = new ManualResetEventSlim(false);
        svc.MessagesBatchReady += (_, msgs) =>
        {
            batch.AddRange(msgs);
            mre.Set();
        };

        var entry = new StartupEntry
        {
            Id = "adobe-test-id",
            Name = "AdobeAAMUpdater-1.0",
            CommandLine = "\"C:\\Test\\Updater.exe\""
        };
        var payload = System.Text.Json.JsonSerializer.Serialize(entry);

        // Отправляем одно и то же уведомление дважды
        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.BlacklistMatch, Payload = payload });
        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.BlacklistMatch, Payload = payload });

        mre.Wait(TimeSpan.FromSeconds(5));

        // Должно дойти ровно 1 сообщение, дубль отсечён
        Assert.Single(batch);
    }

    /// <summary>
    /// Проверяет, что если у заблокированной программы изменилась командная строка, уведомление не подавляется.
    /// </summary>
    [Fact]
    public void Enqueue_ChangedCommandLine_DoesNotSuppress()
    {
        var tracker = new InitialScanTracker();
        for (int i = 0; i < 5; i++) tracker.MarkCompleted();

        using var svc = new NotificationService(tracker);
        var batch = new List<PipeMessage>();
        using var mre = new ManualResetEventSlim(false);
        svc.MessagesBatchReady += (_, msgs) =>
        {
            batch.AddRange(msgs);
            if (batch.Count >= 2) mre.Set();
        };

        var entry1 = new StartupEntry
        {
            Id = "adobe-test-id",
            Name = "AdobeAAMUpdater-1.0",
            CommandLine = "\"C:\\Test\\Updater.exe\" -param1"
        };
        var entry2 = new StartupEntry
        {
            Id = "adobe-test-id",
            Name = "AdobeAAMUpdater-1.0",
            CommandLine = "\"C:\\Test\\Updater.exe\" -param2"
        };

        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.BlacklistMatch, Payload = System.Text.Json.JsonSerializer.Serialize(entry1) });
        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.BlacklistMatch, Payload = System.Text.Json.JsonSerializer.Serialize(entry2) });

        mre.Wait(TimeSpan.FromSeconds(5));

        Assert.Equal(2, batch.Count);
    }

    /// <summary>
    /// Проверяет, что повторные ActionNotice с одинаковым именем и действием также подавляются.
    /// </summary>
    [Fact]
    public void Enqueue_DuplicateActionNotice_SuppressesDuplicate()
    {
        var tracker = new InitialScanTracker();
        for (int i = 0; i < 5; i++) tracker.MarkCompleted();

        using var svc = new NotificationService(tracker);
        var batch = new List<PipeMessage>();
        using var mre = new ManualResetEventSlim(false);
        svc.MessagesBatchReady += (_, msgs) =>
        {
            batch.AddRange(msgs);
            mre.Set();
        };

        var notice = new ActionNoticePayload("AdobeAAMUpdater-1.0", "removedAgain");
        var payload = System.Text.Json.JsonSerializer.Serialize(notice);

        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.ActionNotice, Payload = payload });
        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.ActionNotice, Payload = payload });

        mre.Wait(TimeSpan.FromSeconds(5));

        Assert.Single(batch);
    }
}
