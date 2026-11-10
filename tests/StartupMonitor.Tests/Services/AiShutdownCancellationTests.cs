using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет прекращение реальной AI-задачи при остановке, включая незавершённый HTTP и минутный Retry-After, без сетевых запросов.</summary>
public sealed class AiShutdownCancellationTests
{
    /// <summary>Формирует неоценённую пользовательскую запись, которая не попадает в системные shortcut.</summary>
    private static StartupEntry Entry() => new()
    {
        Id = "shutdown-ai-entry", Name = "Audit client", FilePath = @"C:\Apps\audit-client.exe"
    };

    /// <summary>Остановка доходит до HTTP каждого публичного пути и отменяет исходную задачу вместо оставления её в фоне.</summary>
    /// <param name="operationName">Проверяемый путь UI/фоновой оценки.</param>
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

    /// <summary>Отмена Retry-After не ждёт минуту, не запускает повтор и освобождает semaphore для следующего пакета.</summary>
    [Fact]
    public async Task Cancellation_InterruptsMinuteBackoffAndReleasesBatchOwnership()
    {
        var calls = 0;
        using var fixture = new Fixture(_ => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? RateLimited() : Rating()));
        using var stopping = new CancellationTokenSource();
        var entry = Entry();
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

    /// <summary>Даже пакет без явно переданного токена прекращается при Dispose владельца AI, не удерживая работающий retry.</summary>
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

    /// <summary>Уже отменённый запуск не захватывает semaphore и не обращается к провайдеру.</summary>
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

    /// <summary>Возвращает управляемое минутное ограничение, совпадающее с исходным shutdown-дефектом.</summary>
    private static HttpResponseMessage RateLimited()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
        return response;
    }

    /// <summary>Возвращает OpenAI-совместимый подтверждённый рейтинг для проверки освобождения batch semaphore.</summary>
    private static HttpResponseMessage Rating() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = JsonSerializer.Serialize(new { score = 3, category = "test", risk = "low", recommendation = "allow" }) } } }
        }))
    };

    /// <summary>Владеет отдельным хранилищем и оценщиком; каждый HTTP направляется только в управляемый обработчик.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "sm-ai-shutdown-" + Guid.NewGuid().ToString("N"));
        internal Handler Handler { get; }
        internal RecordingLogger Logger { get; } = new();
        internal RatingsCache Cache { get; }
        internal AiRatingService Service { get; }

        /// <summary>Подключает источник HTTP-ответов без внешней сети и настоящих API-ключей.</summary>
        internal Fixture(Func<CancellationToken, Task<HttpResponseMessage>> send)
        {
            Handler = new Handler(send);
            Cache = new RatingsCache(new JsonDataStore(_directory));
            Service = new AiRatingService(Cache, Logger, "unit-test", apiUrl: "https://unit.test/chat", httpHandler: Handler);
        }

        /// <summary>Отменяет оценщик и удаляет только собственные временные данные теста.</summary>
        public void Dispose()
        {
            Service.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>Фиксирует число HTTP-попыток и наблюдает настоящий токен HttpClient.</summary>
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return send(cancellationToken);
        }
    }

    /// <summary>Позволяет проверить, что штатная отмена не журналируется как ошибка/тайм-аут.</summary>
    private sealed class RecordingLogger : ILogger<AiRatingService>
    {
        internal List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Levels) Levels.Add(logLevel);
        }
    }
}
