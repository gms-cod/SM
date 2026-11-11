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
    /// <summary>Поддельное имя или аргумент с названием продукта не дают автоматического доверенного рейтинга.</summary>
    /// <param name="name">Имя проверяемой записи.</param>
    /// <param name="command">Командная строка проверяемой записи.</param>
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

    /// <summary>Ответ на старый контекст не меняет запись после редактирования или установки ручной оценки.</summary>
    /// <param name="operation">Путь фонового или ручного обращения к поставщику.</param>
    /// <param name="manual">Признак установки ручной оценки во время HTTP.</param>
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
        private readonly JsonDataStore _store;
        internal JsonDataStore Store => _store;
        internal RatingsCache Cache { get; }
        internal AiRatingService Service { get; }

        /// <summary>Подключает источник HTTP-ответов без внешней сети и настоящих API-ключей.</summary>
        internal Fixture(Func<CancellationToken, Task<HttpResponseMessage>> send)
        {
            Handler = new Handler(send);
            _store = new JsonDataStore(_directory);
            Cache = new RatingsCache(_store);
            Service = new AiRatingService(Cache, Logger, "unit-test", apiUrl: "https://unit.test/chat", httpHandler: Handler);
        }

        /// <summary>Добавляет реальную запись в тестовое хранилище до проверки условной записи ответа.</summary>
        /// <param name="entry">Проверяемая запись автозапуска.</param>
        internal void Persist(StartupEntry entry) => _store.AddEntry(entry);

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
