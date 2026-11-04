using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Service.Services.ProcessLifecycle;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты для доменной модели, контрактов, инвариантов очередей и расчета метрик жизненного цикла процессов.
/// Проверяет отсутствие блокировок, потокобезопасность, граничные условия и защиту от повреждения памяти.
/// </summary>
public sealed class ProcessLifecycleTests
{
    #region Фрагмент E плана ETW_IMPLEMENTATION_PLAN.md

    /// <summary>
    /// Переполнение очереди не блокирует производителя (producer) и отражается в счётчике потерь со сверкой.
    /// </summary>
    [Fact]
    public void FullIngress_RejectsAndRequestsRecovery()
    {
        var ingress = new LifecycleIngress(capacity: 1, byteBudget: 1024);
        var item = new ProcessLifecycleEvent(
            Guid.NewGuid(),
            1,
            LifecycleEventKind.Started,
            42,
            4,
            0,
            100,
            DateTime.UnixEpoch,
            "helper.exe",
            "helper.exe");

        Assert.True(ingress.TryPublish(item));
        Assert.False(ingress.TryPublish(item with { Sequence = 2 }));
        Assert.Equal(1L, ingress.Dropped);
        Assert.True(ingress.NeedsReconciliation);
        Assert.True(ingress.TryRead(out var actual));
        Assert.Equal(item, actual);
        Assert.Equal(0L, ingress.RetainedBytes);
    }

    /// <summary>
    /// Длинный payload (например, раздутая командная строка) отклоняется байтовым лимитом даже при свободных слотах очереди.
    /// </summary>
    [Fact]
    public void OversizedPayload_IsRejectedEvenWhenCountHasRoom()
    {
        var ingress = new LifecycleIngress(capacity: 10, byteBudget: 256);
        var item = new ProcessLifecycleEvent(
            Guid.NewGuid(),
            1,
            LifecycleEventKind.Started,
            42,
            4,
            0,
            100,
            DateTime.UnixEpoch,
            "helper.exe",
            new string('x', 1000));

        Assert.False(ingress.TryPublish(item));
        Assert.Equal(0L, ingress.RetainedBytes);
        Assert.Equal(1L, ingress.Dropped);
        Assert.True(ingress.NeedsReconciliation);
    }

    /// <summary>
    /// Подтверждение старого снимка сверки не стирает новый запрос, поступивший во время выполнения сверки.
    /// </summary>
    [Fact]
    public void ReconciliationCommit_PreservesNewRequest()
    {
        var ingress = new LifecycleIngress();
        var captured = ingress.RequestReconciliation();
        var later = ingress.RequestReconciliation();

        ingress.AcknowledgeReconciliation(captured);
        Assert.True(ingress.NeedsReconciliation);

        ingress.AcknowledgeReconciliation(later);
        Assert.False(ingress.NeedsReconciliation);
    }

    /// <summary>
    /// Одно CPU-ядро за 1 секунду на 8-ядерной машине дает ровно 12.5% общей мощности. Сброс счетчиков возвращает null.
    /// </summary>
    [Fact]
    public void Metrics_AreNormalizedAndRejectCounterReset()
    {
        var delta = ProcessMetricMath.Calculate(
            0,
            TimeSpan.TicksPerSecond,
            0,
            1024,
            TimeSpan.FromSeconds(1),
            8);

        Assert.True(delta.HasValue);
        Assert.Equal(12.5, delta.GetValueOrDefault().CpuPercent, 6);
        Assert.Equal(1024L, delta.GetValueOrDefault().IoBytesPerSecond);

        // Сброс счетчика CPU (текущее меньше предыдущего) -> null
        Assert.Null(ProcessMetricMath.Calculate(100, 10, 0, 1, TimeSpan.FromSeconds(1), 8));

        // Нулевой интервал времени -> null
        Assert.Null(ProcessMetricMath.Calculate(0, 10, 0, 1, TimeSpan.Zero, 8));
    }

    /// <summary>
    /// Ошибка разбора коалесцируется без сохранения чувствительных данных, текста исключения или командных строк.
    /// </summary>
    [Fact]
    public void ParseFailure_CoalescesWithoutRetainingSensitivePayload()
    {
        var ingress = new LifecycleIngress();
        var epoch = Guid.NewGuid();

        ingress.ReportParseFailure(new ArgumentException("secret-command-line"), LifecycleEventKind.Started, epoch);
        ingress.ReportParseFailure(new InvalidOperationException("secret-command-line"), LifecycleEventKind.Stopped, epoch);

        var diagnostic = ingress.TakeParseFailureDiagnostic();
        Assert.NotNull(diagnostic);
        Assert.Equal(epoch, diagnostic.SourceEpoch);
        Assert.Equal(typeof(InvalidOperationException).FullName, diagnostic.ExceptionType);
        Assert.DoesNotContain("secret-command-line", diagnostic.ToString());

        Assert.Null(ingress.TakeParseFailureDiagnostic());
        Assert.Equal(2L, ingress.ParseFailures);
        Assert.True(ingress.NeedsReconciliation);
    }

    #endregion

    #region Граничные тесты валидации контрактов ProcessInstanceId

    /// <summary>
    /// Проверяет, что пустая эпоха для ProcessInstanceId вызывает исключение ArgumentException.
    /// </summary>
    [Fact]
    public void ProcessInstanceId_EmptyEpoch_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ProcessInstanceId(Guid.Empty, 1234, 1));
    }

    /// <summary>
    /// Проверяет, что отрицательный PID для ProcessInstanceId вызывает исключение ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public void ProcessInstanceId_NegativePid_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessInstanceId(Guid.NewGuid(), -1, 1));
    }

    /// <summary>
    /// Проверяет, что поколение <= 0 для ProcessInstanceId вызывает исключение ArgumentOutOfRangeException.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ProcessInstanceId_NonPositiveGeneration_ThrowsArgumentOutOfRangeException(long generation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessInstanceId(Guid.NewGuid(), 1234, generation));
    }

    /// <summary>
    /// Проверяет корректное создание и деконструкцию ProcessInstanceId при валидных входных аргументах.
    /// </summary>
    [Fact]
    public void ProcessInstanceId_ValidArguments_PropertiesAndDeconstructMatch()
    {
        var epoch = Guid.NewGuid();
        var id = new ProcessInstanceId(epoch, 4567, 42);

        Assert.Equal(epoch, id.MonitorEpoch);
        Assert.Equal(4567, id.ProcessId);
        Assert.Equal(42L, id.Generation);

        var (deconstructedEpoch, deconstructedPid, deconstructedGen) = id;
        Assert.Equal(epoch, deconstructedEpoch);
        Assert.Equal(4567, deconstructedPid);
        Assert.Equal(42L, deconstructedGen);
    }

    #endregion

    #region Граничные тесты валидации контрактов ProcessLifecycleEvent

    /// <summary>
    /// Проверяет отклонение недопустимых аргументов при конструировании ProcessLifecycleEvent.
    /// </summary>
    [Fact]
    public void ProcessLifecycleEvent_InvalidArguments_ThrowsExpectedExceptions()
    {
        var validEpoch = Guid.NewGuid();

        // Пустая эпоха
        Assert.Throws<ArgumentException>(() => new ProcessLifecycleEvent(
            Guid.Empty, 1, LifecycleEventKind.Started, 10, 4, 0, 100, DateTime.UtcNow, "app.exe", ""));

        // Sequence <= 0
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessLifecycleEvent(
            validEpoch, 0, LifecycleEventKind.Started, 10, 4, 0, 100, DateTime.UtcNow, "app.exe", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessLifecycleEvent(
            validEpoch, -1, LifecycleEventKind.Started, 10, 4, 0, 100, DateTime.UtcNow, "app.exe", ""));

        // ProcessId < 0
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessLifecycleEvent(
            validEpoch, 1, LifecycleEventKind.Started, -1, 4, 0, 100, DateTime.UtcNow, "app.exe", ""));

        // ParentProcessId < 0
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessLifecycleEvent(
            validEpoch, 1, LifecycleEventKind.Started, 10, -5, 0, 100, DateTime.UtcNow, "app.exe", ""));

        // OccurredQpc < 0
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessLifecycleEvent(
            validEpoch, 1, LifecycleEventKind.Started, 10, 4, 0, -1, DateTime.UtcNow, "app.exe", ""));

        // Null ImageName
        Assert.Throws<ArgumentNullException>(() => new ProcessLifecycleEvent(
            validEpoch, 1, LifecycleEventKind.Started, 10, 4, 0, 100, DateTime.UtcNow, null!, ""));

        // Null CommandLine
        Assert.Throws<ArgumentNullException>(() => new ProcessLifecycleEvent(
            validEpoch, 1, LifecycleEventKind.Started, 10, 4, 0, 100, DateTime.UtcNow, "app.exe", null!));
    }

    /// <summary>
    /// Проверяет корректное преобразование локального времени в UTC и деконструкцию события.
    /// </summary>
    [Fact]
    public void ProcessLifecycleEvent_ValidConstruction_NormalizesUtcAndDeconstructs()
    {
        var epoch = Guid.NewGuid();
        var localTime = new DateTime(2026, 11, 4, 15, 30, 0, DateTimeKind.Local);

        var ev = new ProcessLifecycleEvent(
            epoch,
            100,
            LifecycleEventKind.Started,
            1234,
            5678,
            99999UL,
            500000L,
            localTime,
            "calc.exe",
            "-arg1");

        Assert.Equal(DateTimeKind.Utc, ev.OccurredUtc.Kind);
        Assert.Equal(localTime.ToUniversalTime(), ev.OccurredUtc);

        var (e, seq, kind, pid, ppid, kkey, qpc, utc, img, cmd) = ev;
        Assert.Equal(epoch, e);
        Assert.Equal(100L, seq);
        Assert.Equal(LifecycleEventKind.Started, kind);
        Assert.Equal(1234, pid);
        Assert.Equal(5678, ppid);
        Assert.Equal(99999UL, kkey);
        Assert.Equal(500000L, qpc);
        Assert.Equal(localTime.ToUniversalTime(), utc);
        Assert.Equal("calc.exe", img);
        Assert.Equal("-arg1", cmd);
    }

    /// <summary>
    /// Проверяет, что синтаксис record 'with' с невалидными полями вызывает валидационные исключения.
    /// </summary>
    [Fact]
    public void ProcessLifecycleEvent_WithInvalidField_ThrowsValidationException()
    {
        var ev = new ProcessLifecycleEvent(
            Guid.NewGuid(), 1, LifecycleEventKind.Started, 10, 4, 0, 100, DateTime.UtcNow, "app.exe", "");

        Assert.Throws<ArgumentOutOfRangeException>(() => ev with { Sequence = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => ev with { ProcessId = -1 });
        Assert.Throws<ArgumentException>(() => ev with { SourceEpoch = Guid.Empty });
    }

    #endregion

    #region Граничные тесты ParseFailureDiagnostic

    /// <summary>
    /// Проверяет валидацию параметров при создании ParseFailureDiagnostic.
    /// </summary>
    [Fact]
    public void ParseFailureDiagnostic_InvalidArguments_ThrowsExpectedExceptions()
    {
        Assert.Throws<ArgumentException>(() => new ParseFailureDiagnostic(Guid.Empty, LifecycleEventKind.Started, "SomeException", 1));
        Assert.Throws<ArgumentException>(() => new ParseFailureDiagnostic(Guid.NewGuid(), LifecycleEventKind.Started, "", 1));
        Assert.Throws<ArgumentException>(() => new ParseFailureDiagnostic(Guid.NewGuid(), LifecycleEventKind.Started, "   ", 1));
        Assert.Throws<ArgumentNullException>(() => new ParseFailureDiagnostic(Guid.NewGuid(), LifecycleEventKind.Started, null!, 1));
    }

    #endregion

    #region Граничные тесты LifecycleIngress

    /// <summary>
    /// Проверяет валидацию аргументов конструктора LifecycleIngress.
    /// </summary>
    [Fact]
    public void LifecycleIngress_ConstructorValidation_ThrowsOnInvalidLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LifecycleIngress(capacity: 0, byteBudget: 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LifecycleIngress(capacity: -1, byteBudget: 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LifecycleIngress(capacity: 10, byteBudget: 255));
    }

    /// <summary>
    /// Проверяет валидацию поколений в AcknowledgeReconciliation.
    /// </summary>
    [Fact]
    public void LifecycleIngress_AcknowledgeReconciliation_ValidatesBoundaries()
    {
        var ingress = new LifecycleIngress();
        ingress.RequestReconciliation(); // Requested becomes 1

        Assert.Throws<ArgumentOutOfRangeException>(() => ingress.AcknowledgeReconciliation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ingress.AcknowledgeReconciliation(2)); // > Requested

        ingress.AcknowledgeReconciliation(1); // Valid
        Assert.False(ingress.NeedsReconciliation);
    }

    /// <summary>
    /// Проверяет, что закрытие очереди Complete() отклоняет дальнейшие TryPublish,
    /// но позволяет вычитать ранее помещенные элементы.
    /// </summary>
    [Fact]
    public void LifecycleIngress_Complete_RejectsNewAndDrainsExisting()
    {
        var ingress = new LifecycleIngress();
        var item1 = new ProcessLifecycleEvent(
            Guid.NewGuid(), 1, LifecycleEventKind.Started, 10, 4, 0, 100, DateTime.UtcNow, "app.exe", "");
        var item2 = new ProcessLifecycleEvent(
            Guid.NewGuid(), 2, LifecycleEventKind.Started, 20, 4, 0, 200, DateTime.UtcNow, "app2.exe", "");

        Assert.True(ingress.TryPublish(item1));
        ingress.Complete();

        // Новые публикации отклоняются
        Assert.False(ingress.TryPublish(item2));

        // Существующие вычитываются
        Assert.True(ingress.TryRead(out var readItem));
        Assert.Equal(item1, readItem);

        // Очередь пуста
        Assert.False(ingress.TryRead(out var emptyItem));
        Assert.Null(emptyItem);
        Assert.Equal(0L, ingress.RetainedBytes);
    }

    /// <summary>
    /// Проверяет, что ReportParseFailure выбрасывает исключения на недопустимые аргументы.
    /// </summary>
    [Fact]
    public void LifecycleIngress_ReportParseFailure_ValidatesArguments()
    {
        var ingress = new LifecycleIngress();
        Assert.Throws<ArgumentNullException>(() => ingress.ReportParseFailure(null!, LifecycleEventKind.Started, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ingress.ReportParseFailure(new Exception(), LifecycleEventKind.Started, Guid.Empty));
    }

    #endregion

    #region Граничные тесты ProcessMetricMath

    /// <summary>
    /// Проверяет валидацию и защиту от сбоев в ProcessMetricMath при некорректных входных параметрах.
    /// </summary>
    [Fact]
    public void ProcessMetricMath_BoundaryConditions_HandledCleanly()
    {
        // Невалидный processorCount <= 0
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessMetricMath.Calculate(
            0, 100, 0, 100, TimeSpan.FromSeconds(1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessMetricMath.Calculate(
            0, 100, 0, 100, TimeSpan.FromSeconds(1), -2));

        // Отрицательный elapsed
        Assert.Null(ProcessMetricMath.Calculate(0, 100, 0, 100, TimeSpan.FromSeconds(-1), 4));

        // Отрицательные предыдущие счетчики
        Assert.Null(ProcessMetricMath.Calculate(-1, 100, 0, 100, TimeSpan.FromSeconds(1), 4));
        Assert.Null(ProcessMetricMath.Calculate(0, 100, -5, 100, TimeSpan.FromSeconds(1), 4));

        // Сброс счетчика I/O (current < previous)
        Assert.Null(ProcessMetricMath.Calculate(0, 100, 500, 200, TimeSpan.FromSeconds(1), 4));

        // 100% нагрузка на 1 CPU (за 1 секунду использована 1 секунда тиков)
        var fullCpu = ProcessMetricMath.Calculate(
            0,
            TimeSpan.TicksPerSecond,
            0,
            0,
            TimeSpan.FromSeconds(1),
            1);
        Assert.NotNull(fullCpu);
        Assert.Equal(100.0, fullCpu.Value.CpuPercent, 6);
        Assert.Equal(0L, fullCpu.Value.IoBytesPerSecond);

        // Переполнение I/O (скорость свыше long.MaxValue) плавно ограничивается long.MaxValue
        var hugeIo = ProcessMetricMath.Calculate(
            0,
            0,
            0,
            long.MaxValue,
            TimeSpan.FromMilliseconds(0.1),
            4);
        Assert.NotNull(hugeIo);
        Assert.Equal(long.MaxValue, hugeIo.Value.IoBytesPerSecond);
    }

    #endregion

    #region Многопоточный стресс-тест LifecycleIngress

    /// <summary>
    /// Проверяет потокобезопасность LifecycleIngress при параллельной публикации и чтении из множества потоков.
    /// Гарантирует корректность атомарного баланса RetainedBytes, учет Dropped и отсутствие утечек или зависаний.
    /// </summary>
    [Fact]
    public async Task LifecycleIngress_ConcurrentPublishAndRead_MaintainsBudgetAndIntegrity()
    {
        const int capacity = 100;
        const long byteBudget = 16 * 1024; // 16 KB
        const int producerCount = 8;
        const int itemsPerProducer = 500;

        var ingress = new LifecycleIngress(capacity, byteBudget);
        var sourceEpoch = Guid.NewGuid();
        var readItems = new ConcurrentBag<ProcessLifecycleEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Фоновый читатель
        var readerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (ingress.TryRead(out var ev))
                {
                    if (ev != null)
                    {
                        readItems.Add(ev);
                    }
                }
                else
                {
                    try
                    {
                        var hasData = await ingress.WaitToReadAsync(cts.Token);
                        if (!hasData)
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            // Дочитываем оставшееся
            while (ingress.TryRead(out var remaining))
            {
                if (remaining != null)
                {
                    readItems.Add(remaining);
                }
            }
        });

        // Параллельные производители
        var producers = new Task[producerCount];
        for (var p = 0; p < producerCount; p++)
        {
            var producerId = p;
            producers[p] = Task.Run(() =>
            {
                for (var i = 1; i <= itemsPerProducer; i++)
                {
                    var ev = new ProcessLifecycleEvent(
                        sourceEpoch,
                        (long)producerId * 10000 + i,
                        LifecycleEventKind.Started,
                        1000 + producerId,
                        4,
                        0,
                        i * 10,
                        DateTime.UtcNow,
                        $"worker_{producerId}.exe",
                        $"--task {i}");

                    ingress.TryPublish(ev);
                }
            });
        }

        await Task.WhenAll(producers);
        ingress.Complete();
        await readerTask;

        var totalAttempts = producerCount * itemsPerProducer;
        var totalProcessed = readItems.Count + ingress.Dropped;

        Assert.Equal(totalAttempts, totalProcessed);
        Assert.Equal(0L, ingress.RetainedBytes);
    }

    #endregion

    #region Тест контракта IOwnedProcessTraceSession

    /// <summary>
    /// Проверяет контракт интерфейса IOwnedProcessTraceSession на идемпотентность и корректность освобождения ресурсов.
    /// </summary>
    [Fact]
    public void IOwnedProcessTraceSession_MockImplementation_BehavesCorrectly()
    {
        var session = new TestTraceSession("StartupMonitor.ProcessLifecycle.Test");
        Assert.Equal("StartupMonitor.ProcessLifecycle.Test", session.Name);
        Assert.False(session.IsStopped);

        session.Stop();
        Assert.True(session.IsStopped);

        // Идемпотентный повторный Stop
        session.Stop();
        Assert.True(session.IsStopped);

        session.Dispose();
        Assert.True(session.IsDisposed);
    }

    private sealed class TestTraceSession : IOwnedProcessTraceSession
    {
        private int _stopped;
        private int _disposed;

        public string Name { get; }

        public bool IsStopped => Volatile.Read(ref _stopped) == 1;
        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public TestTraceSession(string name)
        {
            Name = name;
        }

        public void Stop()
        {
            Interlocked.Exchange(ref _stopped, 1);
        }

        public void Dispose()
        {
            Stop();
            Interlocked.Exchange(ref _disposed, 1);
        }
    }

    #endregion

    #region Тесты реестра ProcessLifecycleRegistry

    /// <summary>
    /// Проверяет добавление процесса при Start, сохранение в активном наборе и корректное удаление при Stop.
    /// </summary>
    [Fact]
    public void ProcessLifecycleRegistry_StartAndStop_TracksActiveAndHistory()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var now = DateTime.UtcNow;

        var startEv = new ProcessLifecycleEvent(
            epoch, 1, LifecycleEventKind.Started, 1001, 500, 12345UL, 1000L, now, "notepad.exe", "notepad.exe file.txt");

        var record = registry.ApplyEvent(startEv);
        Assert.NotNull(record);
        Assert.Equal(1001, record.ProcessId);
        Assert.Equal(1L, record.Generation);
        Assert.Equal(1, registry.ActiveProcessCount);
        Assert.Equal(1L, registry.TotalStartedCount);

        var live = registry.GetLiveSnapshot();
        Assert.Single(live);
        Assert.Equal(1001, live[0].ProcessId);

        var stopEv = new ProcessLifecycleEvent(
            epoch, 2, LifecycleEventKind.Stopped, 1001, 500, 12345UL, 2000L, now.AddSeconds(1), "notepad.exe", "");

        var stopped = registry.ApplyEvent(stopEv);
        Assert.Null(stopped);
        Assert.Equal(0, registry.ActiveProcessCount);
        Assert.Equal(1L, registry.TotalStoppedCount);

        var history = registry.GetHistorySnapshot();
        Assert.Single(history);
        Assert.Equal(1001, history[0].InstanceId.ProcessId);
        Assert.Equal("NormalExit", history[0].ExitReason);
    }

    /// <summary>
    /// Проверяет, что повторное событие Start для того же процесса является идемпотентным и не плодит поколений.
    /// </summary>
    [Fact]
    public void ProcessLifecycleRegistry_DuplicateStart_IsIdempotent()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var now = DateTime.UtcNow;

        var start1 = new ProcessLifecycleEvent(
            epoch, 1, LifecycleEventKind.Started, 2002, 100, 555UL, 1000L, now, "calc.exe", "");
        var start2 = new ProcessLifecycleEvent(
            epoch, 2, LifecycleEventKind.Started, 2002, 100, 555UL, 1001L, now.AddMilliseconds(100), "calc.exe", "");

        var r1 = registry.ApplyEvent(start1);
        var r2 = registry.ApplyEvent(start2);

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(r1.Generation, r2.Generation);
        Assert.Equal(1, registry.ActiveProcessCount);
        Assert.Equal(1L, registry.TotalStartedCount);
    }

    /// <summary>
    /// Проверяет быстрое переиспользование PID: появление нового Start в том же PID
    /// корректно списывает предыдущий процесс в историю и присваивает новое поколение.
    /// </summary>
    [Fact]
    public void ProcessLifecycleRegistry_PidReuse_RetiresOldGenerationAndAssignsNew()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var now = DateTime.UtcNow;

        var proc1 = new ProcessLifecycleEvent(
            epoch, 1, LifecycleEventKind.Started, 3003, 100, 111UL, 1000L, now, "workerA.exe", "");
        var proc2 = new ProcessLifecycleEvent(
            epoch, 2, LifecycleEventKind.Started, 3003, 200, 222UL, 5000L, now.AddSeconds(10), "workerB.exe", "");

        var r1 = registry.ApplyEvent(proc1);
        var r2 = registry.ApplyEvent(proc2);

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(1L, r1.Generation);
        Assert.Equal(2L, r2.Generation);
        Assert.Equal(1, registry.ActiveProcessCount);
        Assert.Equal(2L, registry.TotalStartedCount);
        Assert.Equal(1L, registry.TotalStoppedCount);

        var live = registry.GetLiveSnapshot();
        Assert.Single(live);
        Assert.Equal("workerB.exe", live[0].ImageName);
        Assert.Equal(2L, live[0].Generation);

        var history = registry.GetHistorySnapshot();
        Assert.Single(history);
        Assert.Equal("workerA.exe", history[0].ImageName);
        Assert.Equal("PidReusedWithoutStop", history[0].ExitReason);
    }

    /// <summary>
    /// Проверяет защиту от запоздалого Stop: завершение старого поколения не удаляет новый активный процесс с тем же PID.
    /// </summary>
    [Fact]
    public void ProcessLifecycleRegistry_StopWithOldKernelKey_DoesNotKillNewGeneration()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var now = DateTime.UtcNow;

        var startOld = new ProcessLifecycleEvent(
            epoch, 1, LifecycleEventKind.Started, 4004, 10, 1000UL, 1000L, now, "old.exe", "");
        registry.ApplyEvent(startOld);

        var startNew = new ProcessLifecycleEvent(
            epoch, 2, LifecycleEventKind.Started, 4004, 20, 2000UL, 2000L, now.AddSeconds(5), "new.exe", "");
        registry.ApplyEvent(startNew);

        // Приходит запоздалый Stop от старого процесса (с ключом 1000UL)
        var lateStop = new ProcessLifecycleEvent(
            epoch, 3, LifecycleEventKind.Stopped, 4004, 10, 1000UL, 2100L, now.AddSeconds(6), "old.exe", "");
        var result = registry.ApplyEvent(lateStop);

        Assert.Null(result);
        Assert.Equal(1, registry.ActiveProcessCount);

        var live = registry.GetLiveSnapshot();
        Assert.Single(live);
        Assert.Equal("new.exe", live[0].ImageName);
        Assert.Equal(2000UL, live[0].KernelKey);
    }

    /// <summary>
    /// Проверяет слияние системного baseline-снимка.
    /// </summary>
    [Fact]
    public void ProcessLifecycleRegistry_ApplyBaseline_MergesExistingAndAddsNew()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var now = DateTime.UtcNow;

        // Предварительно зарегистрированный через Start процесс
        var startEv = new ProcessLifecycleEvent(
            epoch, 1, LifecycleEventKind.Started, 5001, 1, 999UL, 100L, now, "service.exe", "");
        registry.ApplyEvent(startEv);

        var baseline = new List<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>
        {
            (5001, 1, now, "service.exe"),
            (5002, 1, now.AddMinutes(-5), "explorer.exe"),
            (5003, 1, now.AddHours(-1), "svchost.exe")
        };

        registry.ApplyBaseline(baseline, now);

        Assert.Equal(3, registry.ActiveProcessCount);
        var live = registry.GetLiveSnapshot();
        Assert.Contains(live, p => p.ProcessId == 5001 && !p.IsFromBaseline);
        Assert.Contains(live, p => p.ProcessId == 5002 && p.IsFromBaseline);
        Assert.Contains(live, p => p.ProcessId == 5003 && p.IsFromBaseline);
    }

    /// <summary>
    /// Проверяет, что метаданные применяются только к строго соответствующему поколению InstanceId.
    /// </summary>
    [Fact]
    public void ProcessLifecycleRegistry_TryUpdateMetadata_OnlyAppliesToMatchingInstanceId()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var now = DateTime.UtcNow;

        var startEv = new ProcessLifecycleEvent(
            epoch, 1, LifecycleEventKind.Started, 6001, 1, 111UL, 100L, now, "app.exe", "");
        var record = registry.ApplyEvent(startEv)!;

        // Попытка применить с неверным поколением
        var wrongInstanceId = new ProcessInstanceId(epoch, 6001, record.Generation + 99);
        var failed = registry.TryUpdateMetadata(wrongInstanceId, ProcessMetadataStatus.SignatureChecked, "C:\\app.exe", "ValidSigner");
        Assert.False(failed);

        // Применение с правильным InstanceId
        var success = registry.TryUpdateMetadata(record.InstanceId, ProcessMetadataStatus.SignatureChecked, "C:\\app.exe", "ValidSigner");
        Assert.True(success);

        var live = registry.GetLiveSnapshot();
        Assert.Equal(ProcessMetadataStatus.SignatureChecked, live[0].MetadataStatus);
        Assert.Equal("C:\\app.exe", live[0].ResolvedExecutablePath);
        Assert.Equal("ValidSigner", live[0].Signer);
    }

    #endregion
}
