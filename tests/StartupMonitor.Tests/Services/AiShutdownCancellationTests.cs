using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
public sealed class AiShutdownCancellationTests
{
    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    [Theory]
    [InlineData("StartupMonitor fake", "malware.exe")]
    [InlineData("Unrelated", "malware.exe --StartupMonitor")]
    public async Task ProductNameSpoofing_IsSentToConfiguredProvider(string name, string command)
    {
        using var fixture = new Fixture(_ => Task.FromResult(Rating()));
        var entry = new StartupEntry { Id = "spoof", Name = name, CommandLine = command, FilePath = @"C:\Apps\malware.exe" };
        fixture.Persist(entry);

        var result = await fixture.Service.ForceFetchRatingAsync(entry);

        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Equal(3, result?.Score);
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    [Theory]
    [InlineData("single", false)]
    [InlineData("batch", false)]
    [InlineData("force", false)]
    [InlineData("single", true)]
    [InlineData("batch", true)]
    [InlineData("force", true)]
    public async Task InFlightRating_DoesNotOverwriteChangedEntry(string operation, bool manual)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(_ => { started.TrySetResult(); return finish.Task; });
        var entry = Entry();
        fixture.Persist(entry);
        if (operation == "force" && manual)
            fixture.Cache.Set(entry, new StartupRating { Score = 2, Recommendation = "old" });
        var request = operation switch
        {
            "single" => fixture.Service.PrefetchAsync(entry),
            "batch" => fixture.Service.PrefetchAllAsync([entry]),
            _ => (Task)fixture.Service.ForceFetchRatingAsync(entry)
        };
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = Assert.Single(fixture.Store.Load().Entries);
        if (manual)
        {
            current.IsUserRating = true;
            current.AiRating = 8;
            current.AiRecommendation = "manual";
        }
        else current.CommandLine = "changed --argument";
        fixture.Store.UpdateEntry(current);
        finish.SetResult(Rating());
        await request.WaitAsync(TimeSpan.FromSeconds(5));
        if (operation == "force") Assert.Null(await (Task<StartupRating?>)request);
        var saved = Assert.Single(fixture.Store.Load().Entries);
        Assert.Equal(manual ? 8 : 0, saved.AiRating);
        Assert.Equal(manual, saved.IsUserRating);
        if (manual) Assert.Equal("manual", saved.AiRecommendation);
        else Assert.Equal("changed --argument", saved.CommandLine);
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    private static StartupEntry Entry() => new()
    {
        Id = "shutdown-ai-entry", Name = "Audit client", FilePath = @"C:\Apps\audit-client.exe"
    };

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    [Theory]
    [InlineData("batch")]
    [InlineData("single")]
    [InlineData("force")]
    [InlineData("connection")]
    public async Task Cancellation_AbortsOutstandingHttpWithoutRetryOrErrorLog(string operationName)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var httpCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(async ct =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { if (ct.IsCancellationRequested) httpCancelled.TrySetResult(); }
            throw new InvalidOperationException("Отменённый HTTP не может вернуть результат");
        });
        using var stopping = new CancellationTokenSource();
        var entry = Entry();
        Task operation = operationName switch
        {
            "batch" => fixture.Service.PrefetchAllAsync([entry], stopping.Token),
            "single" => fixture.Service.PrefetchAsync(entry, stopping.Token),
            "force" => fixture.Service.ForceFetchRatingAsync(entry, stopping.Token),
            _ => fixture.Service.TestConnectionAsync("unit-test", null, null, stopping.Token)
        };
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(operation.IsCanceled);
        await httpCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Null(fixture.Cache.Get(entry));
        Assert.DoesNotContain(fixture.Logger.Levels, level => level >= LogLevel.Warning);
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    [Fact]
    public async Task Cancellation_InterruptsMinuteBackoffAndReleasesBatchOwnership()
    {
        var calls = 0;
        using var fixture = new Fixture(_ => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? RateLimited() : Rating()));
        using var stopping = new CancellationTokenSource();
        var entry = Entry();
        fixture.Persist(entry);
        var operation = fixture.Service.PrefetchAllAsync([entry], stopping.Token);
        Assert.Equal(1, calls);
        Assert.False(operation.IsCompleted);
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, calls);
        Assert.Null(fixture.Cache.Get(entry));
        await fixture.Service.PrefetchAllAsync([entry]).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, calls);
        Assert.Equal(3, fixture.Cache.Get(entry)?.Score);
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    [Fact]
    public async Task Dispose_CancelsOutstandingBatchBackoff()
    {
        using var fixture = new Fixture(_ => Task.FromResult(RateLimited()));
        var operation = fixture.Service.PrefetchAllAsync([Entry()]);
        Assert.False(operation.IsCompleted);
        fixture.Service.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(operation.IsCanceled);
        Assert.Equal(1, fixture.Handler.Calls);
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    [Fact]
    public async Task CancelledBeforeStart_DoesNotSendOrRetainBatchOwnership()
    {
        using var fixture = new Fixture(_ => Task.FromResult(Rating()));
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.PrefetchAllAsync([Entry()], stopping.Token));
        Assert.Equal(0, fixture.Handler.Calls);
        await fixture.Service.PrefetchAllAsync([Entry()]).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, fixture.Handler.Calls);
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    private static HttpResponseMessage RateLimited()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
        return response;
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    private static HttpResponseMessage Rating() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = JsonSerializer.Serialize(new { score = 3, category = "test", risk = "low", recommendation = "allow" }) } } }
        }))
    };

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "sm-ai-shutdown-" + Guid.NewGuid().ToString("N"));
        internal Handler Handler { get; }
        internal RecordingLogger Logger { get; } = new();
        private readonly JsonDataStore _store;
        internal JsonDataStore Store => _store;
        internal RatingsCache Cache { get; }
        internal AiRatingService Service { get; }

        /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
        internal Fixture(Func<CancellationToken, Task<HttpResponseMessage>> send)
        {
            Handler = new Handler(send);
            _store = new JsonDataStore(_directory);
            Cache = new RatingsCache(_store);
            Service = new AiRatingService(Cache, Logger, "unit-test", apiUrl: "https://unit.test/chat", httpHandler: Handler);
        }

        /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
        /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
        internal void Persist(StartupEntry entry) => _store.AddEntry(entry);

        /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
        public void Dispose()
        {
            Service.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        internal int Calls;
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return send(cancellationToken);
        }
    }

    /// <summary>Verifies test scenario for AiShutdownCancellationTests.cs.</summary>
    private sealed class RecordingLogger : ILogger<AiRatingService>
    {
        internal List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Levels) Levels.Add(logLevel);
        }
    }
}
