using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for NotificationDeduplicationTests.cs.</summary>
/// <summary>Verifies test scenario for NotificationDeduplicationTests.cs.</summary>
public class NotificationDeduplicationTests
{
    /// <summary>
    /// <summary>Verifies test scenario for NotificationDeduplicationTests.cs.</summary>
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

        // Verifies component behavior and assertions.
        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.BlacklistMatch, Payload = payload });
        svc.Enqueue(new PipeMessage { Type = PipeMessageTypes.BlacklistMatch, Payload = payload });

        mre.Wait(TimeSpan.FromSeconds(5));

        // Verifies component behavior and assertions.
        Assert.Single(batch);
    }

    /// <summary>
    /// <summary>Verifies test scenario for NotificationDeduplicationTests.cs.</summary>
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
    /// <summary>Verifies test scenario for NotificationDeduplicationTests.cs.</summary>
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
