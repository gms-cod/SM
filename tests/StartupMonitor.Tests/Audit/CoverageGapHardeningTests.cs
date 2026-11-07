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

/// <summary>
/// Регрессионные и стресс-тесты, закрывающие пробелы аудита покрытия:
/// границы IPC-кадров, повторные входы и гонки координатора состояния,
/// конкурентный доступ к трекеру подключений, граничные диапазоны истории
/// трафика и валидация аргументов на границах службы.
/// Все тесты детерминированы: реальные задержки и ожидание сети не используются.
/// </summary>
public sealed class CoverageGapHardeningTests : IDisposable
{
    /// <summary>Изолированный каталог для файловых зависимостей текущего теста.</summary>
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "StartupMonitorAudit_" + Guid.NewGuid().ToString("N"));

    /// <summary>Управляемые часы исключают зависимость граничных проверок от системного времени.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <summary>Возвращает момент, установленный тестом.</summary>
        /// <returns>Текущий тестовый момент UTC.</returns>
        public override DateTimeOffset GetUtcNow() => now;

        /// <summary>Сдвигает время без блокировки тестового потока.</summary>
        /// <param name="delta">Величина сдвига.</param>
        public void Advance(TimeSpan delta) => now += delta;
    }

    /// <summary>Создаёт хранилище в изолированном каталоге текущего теста.</summary>
    /// <returns>Хранилище, не разделяемое с другими тестами.</returns>
    private JsonDataStore CreateStore()
        => new(Path.Combine(_directory, "startup-monitor.json"));

    /// <summary>Проверяет, что все watcher'ы штатно завершают ожидание сканирования.</summary>
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

    /// <summary>Повторные или лишние отчёты не должны ломать счётчик и бросать исключения.</summary>
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

    /// <summary>Пустой IP не должен попадать в трекер и искажать счётчик подключений.</summary>
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

    /// <summary>Нулевые порог и емкость детерминированно нормализуются к единице.</summary>
    [Fact]
    public void SlidingWindowTracker_NonPositiveLimits_AreClamped()
    {
        var tracker = new SlidingWindowTracker(threshold: 0, window: TimeSpan.FromSeconds(1), maxCapacity: 0);

        Assert.Equal(1, tracker.Threshold);
        Assert.Equal(1, tracker.MaxCapacity);
    }

    /// <summary>Метка ровно на границе окна ещё учитывается: отсечение строго больше ширины окна.</summary>
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

    /// <summary>Сброс правил по адресу удаляет только составные ключи этого адреса.</summary>
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

    /// <summary>Пустой адрес в операции сброса правил отклоняется с аргументным исключением.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SlidingWindowTracker_ResetRulesForIp_RejectsBlankAddress(string? ip)
    {
        var tracker = new SlidingWindowTracker();

        Assert.ThrowsAny<ArgumentException>(() => tracker.ResetRulesForIp(ip!));
    }

    /// <summary>Стресс-тест: параллельные записи не теряют и не дублируют подключения одного IP.</summary>
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

    /// <summary>Стресс-тест: конкурентное добавление IP не превышает заданную емкость LRU-кэша.</summary>
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

    /// <summary>Повторный вход из обработчика не теряет переход и сохраняет итоговое состояние.</summary>
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

    /// <summary>Стресс-тест: конкурентные SetPaused не порождают подряд идущих одинаковых переходов.</summary>
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

    /// <summary>Ограниченный читатель принимает кадр ровно максимальной длины.</summary>
    [Fact]
    public async Task BoundedLineReader_FrameExactlyAtLimit_IsAccepted()
    {
        var reader = new BoundedLineReader(new StringReader("ABCDE\n"), maximumCharacters: 5);

        var frame = await reader.ReadLineAsync();

        Assert.Equal("ABCDE", frame);
    }

    /// <summary>Кадр сверх лимита отклоняется до накопления неограниченного буфера.</summary>
    [Fact]
    public async Task BoundedLineReader_FrameOverLimit_IsRejected()
    {
        var reader = new BoundedLineReader(new StringReader("ABCDEF\n"), maximumCharacters: 5);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
    }

    /// <summary>Незавершённый кадр в конце потока распознаётся как разрыв протокола, а не как валидные данные.</summary>
    [Fact]
    public async Task BoundedLineReader_IncompleteFrameAtEof_IsRejected()
    {
        var reader = new BoundedLineReader(new StringReader("partial"), maximumCharacters: 32);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
    }

    /// <summary>Штатный EOF между кадрами возвращает null и не считается ошибкой.</summary>
    [Fact]
    public async Task BoundedLineReader_EofBetweenFrames_ReturnsNull()
    {
        var reader = new BoundedLineReader(new StringReader(string.Empty), maximumCharacters: 32);

        Assert.Null(await reader.ReadLineAsync());
    }

    /// <summary>Перевод строки CRLF нормализуется, завершающий \r не попадает в кадр.</summary>
    [Fact]
    public async Task BoundedLineReader_CrlfFrame_RemovesCarriageReturn()
    {
        var reader = new BoundedLineReader(new StringReader("payload\r\n"), maximumCharacters: 32);

        Assert.Equal("payload", await reader.ReadLineAsync());
    }

    /// <summary>Предварительно отменённый токен немедленно прерывает чтение кадра.</summary>
    [Fact]
    public async Task BoundedLineReader_CancelledToken_ThrowsOperationCanceled()
    {
        var reader = new BoundedLineReader(new StringReader("payload\n"), maximumCharacters: 32);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await reader.ReadLineAsync(cts.Token));
    }

    /// <summary>Null-поток и неположительный лимит отклоняются при создании читателя.</summary>
    [Fact]
    public void BoundedLineReader_InvalidConstructorArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new BoundedLineReader(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLineReader(new StringReader(""), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLineReader(new StringReader(""), -1));
    }

    /// <summary>Null-запрос и пустой тип сообщения отклоняются до попытки подключения.</summary>
    [Fact]
    public async Task PipeClient_SendAsync_NullRequest_Throws()
    {
        using var client = new PipeClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendAsync(null!));
    }

    /// <summary>Пустой тип сообщения не должен уходить в транспортный обмен.</summary>
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

    /// <summary>После Dispose клиент не выполняет обмен и возвращает null без исключения.</summary>
    [Fact]
    public async Task PipeClient_SendAsync_AfterDispose_ReturnsNull()
    {
        var client = new PipeClient();
        client.Dispose();

        var response = await client.SendAsync(new PipeMessage { Type = PipeMessageTypes.GetStatus });

        Assert.Null(response);
        Assert.False(client.IsConnected);
    }

    /// <summary>Конструктор сервера событий отклоняет отсутствующие обязательные зависимости.</summary>
    [Fact]
    public void EventPipeServer_NullDependencies_AreRejected()
    {
        var logger = Substitute.For<ILogger<EventPipeServer>>();

        Assert.Throws<ArgumentNullException>(() => new EventPipeServer(null!, logger));

        using var notifications = new NotificationService(new InitialScanTracker(1));
        Assert.Throws<ArgumentNullException>(() => new EventPipeServer(notifications, null!));
    }

    /// <summary>Пустое имя событийного канала отклоняется с аргументным исключением.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EventPipeServer_BlankPipeName_IsRejected(string pipeName)
    {
        using var notifications = new NotificationService(new InitialScanTracker(1));
        var logger = Substitute.For<ILogger<EventPipeServer>>();

        Assert.ThrowsAny<ArgumentException>(() => new EventPipeServer(notifications, logger, pipeName));
    }

    /// <summary>Null-запрос истории и недопустимый диапазон отклоняются на IPC-границе.</summary>
    [Fact]
    public void NetworkTrafficHistory_NullRequest_IsRejected()
    {
        using var service = CreateTrafficService(new FixedClock(DateTimeOffset.UnixEpoch));

        Assert.Throws<ArgumentNullException>(() => service.GetHistory(null!));
    }

    /// <summary>Обратный и пустой диапазоны не должны приводить к делению на отрицательную ширину.</summary>
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

    /// <summary>Диапазон больше семи суток отклоняется, ровно семь суток допускаются.</summary>
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

    /// <summary>Null-логгер и null-снимок счётчиков отклоняются с контекстом аргумента.</summary>
    [Fact]
    public void NetworkTrafficHistory_NullLoggerAndCounters_AreRejected()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));

        Assert.Throws<ArgumentNullException>(() => new NetworkTrafficHistoryService(CreateStore(), null!));

        using var service = CreateTrafficService(clock);
        Assert.Throws<ArgumentNullException>(() => service.RecordCounters(clock.GetUtcNow().UtcDateTime, null!));
    }

    /// <summary>Мусорные элементы снимка не должны попадать в текущую базовую линию измерений.</summary>
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

    /// <summary>Ровно 30 секунд между снимками ещё считаются достоверным интервалом.</summary>
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

    /// <summary>Интервал больше 30 секунд не подменяется вымышленной средней скоростью.</summary>
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

    /// <summary>Повреждённый файл истории диагностируется через ILogger без падения конструктора.</summary>
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

    /// <summary>Создаёт сервис истории с управляемыми часами и без записи на диск.</summary>
    /// <param name="clock">Управляемые часы теста.</param>
    /// <returns>Сервис, готовый к подаче снимков счётчиков.</returns>
    private NetworkTrafficHistoryService CreateTrafficService(TimeProvider clock)
        => new(
            CreateStore(),
            NullLogger<NetworkTrafficHistoryService>.Instance,
            source: Substitute.For<INetworkTrafficCounterSource>(),
            timeProvider: clock);

    /// <summary>Удаляет только каталог, созданный текущим тестом.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
