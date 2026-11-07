using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Режим работы подсистемы отслеживания жизненного цикла процессов.
/// </summary>
public enum ProcessLifecycleMode
{
    /// <summary>Только классический опрос (legacy polling). Сессия ETW не запускается.</summary>
    Legacy,

    /// <summary>Теневой режим: ETW события наполняют реестр, но действия ЧС выполняет legacy цикл.</summary>
    Shadow,

    /// <summary>Основной режим ETW с автоматической деградацией до legacy polling при сбоях.</summary>
    EtwPreferred
}

/// <summary>
/// Состояние здоровья подсистемы ETW Process Lifecycle.
/// </summary>
public enum ProcessLifecycleHealthState
{
    /// <summary>Инициализация и запуск сессии.</summary>
    Starting,

    /// <summary>Первичная синхронизация со снимком процессов (Baseline).</summary>
    Synchronizing,

    /// <summary>Штатная работа: события ядра принимаются без потерь.</summary>
    Healthy,

    /// <summary>Деградированное состояние: обнаружены потери буферов или сбой сессии, активен fallback.</summary>
    Degraded,

    /// <summary>Остановка сервиса.</summary>
    Stopping,

    /// <summary>Сервис полностью остановлен.</summary>
    Stopped
}

/// <summary>
/// Супервизор жизненного цикла процессов на базе Event Tracing for Windows (ETW).
/// Управляет запуском и остановкой native-сессии SystemTraceProvider, координирует ограниченную очередь Ingress,
/// однопоточный редуктор (Single Writer Registry), фоновый мониторинг здоровья и автоматическую сверку (Reconciliation).
/// При любых ошибках прав доступа или ядра плавно переводит систему в режим безопасного фоллбэка (Zero-Crash).
/// </summary>
public sealed class ProcessLifecycleSupervisor : IHostedService, IDisposable
{
    private readonly ProcessLifecycleMode _mode;
    private readonly ProcessLifecycleRegistry _registry;
    private readonly LifecycleIngress _ingress;
    private readonly ILogger<ProcessLifecycleSupervisor> _logger;
    private Guid _sourceEpoch = Guid.NewGuid();
    private readonly CancellationTokenSource _cts = new();
    /// <summary>Сериализует применение событий и полного снимка одним логическим writer.</summary>
    private readonly object _registryWriteGate = new();
    private readonly Func<CancellationToken, IReadOnlyList<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>>? _snapshotSource;
    private readonly Func<IOwnedProcessTraceSession>? _sessionFactory;

    private IOwnedProcessTraceSession? _session;
    private Task? _readerTask;
    private Task? _reducerTask;
    private Task? _healthCheckTask;
    private volatile ProcessLifecycleHealthState _healthState = ProcessLifecycleHealthState.Starting;
    private int _disposed;
    private int _stopped;
    private int _readerFailed;
    private long _historicalGapCount;
    private long _lastDropped;
    private uint _lastEventsLost;
    private uint _lastRealTimeBuffersLost;

    /// <summary>
    /// Инициализирует новый супервизор жизненного цикла процессов.
    /// </summary>
    /// <param name="mode">Режим работы (по умолчанию EtwPreferred).</param>
    /// <param name="registry">Реестр процессов (Single Writer).</param>
    /// <param name="ingress">Ограниченная очередь приёма событий ядра.</param>
    /// <param name="logger">Интерфейс структурированного логирования.</param>
    public ProcessLifecycleSupervisor(
        ProcessLifecycleRegistry registry,
        LifecycleIngress ingress,
        ILogger<ProcessLifecycleSupervisor> logger,
        ProcessLifecycleMode mode = ProcessLifecycleMode.EtwPreferred,
        Func<CancellationToken, IReadOnlyList<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>>? snapshotSource = null,
        Func<IOwnedProcessTraceSession>? sessionFactory = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mode = mode;
        _snapshotSource = snapshotSource;
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// Текущее состояние здоровья подсистемы ETW.
    /// </summary>
    public ProcessLifecycleHealthState HealthState => _healthState;

    /// <summary>Режим, выбранный при запуске; Legacy sweep сохраняет защиту независимо от reader.</summary>
    public ProcessLifecycleMode Mode => _mode;

    /// <summary>Число обнаруженных пробелов истории; успешная сверка live-набора их не удаляет.</summary>
    public long HistoricalGapCount => Interlocked.Read(ref _historicalGapCount);

    /// <summary>
    /// Реестр активных и исторических процессов.
    /// </summary>
    public ProcessLifecycleRegistry Registry => _registry;

    /// <summary>
    /// Очередь событий жизненного цикла.
    /// </summary>
    public LifecycleIngress Ingress => _ingress;

    /// <summary>
    /// Запускает подсистему жизненного цикла процессов в соответствии с выбранным режимом.
    /// При сбое native-сессии служба не падает, а переходит в режим Degraded с логированием причины.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode == ProcessLifecycleMode.Legacy)
        {
            _logger.LogInformation("Процессный мониторинг запущен в режиме Legacy (ETW отключен конфигурацией).");
            _healthState = ProcessLifecycleHealthState.Stopped;
            return Task.CompletedTask;
        }

        _logger.LogInformation("Инициализация ETW Process Lifecycle в режиме {Mode} (Эпоха источника: {Epoch})...", _mode, _sourceEpoch);

        // Host.StartAsync не ждёт native mutex/consumer. Здоровье подтверждается только после
        // готовности reader, успешного native QUERY и baseline; защитный sweep остаётся доступным.
        var ct = _cts.Token;
        _reducerTask = Task.Run(() => RunReducerLoopAsync(_ingress, _registry, ct), ct);
        _healthCheckTask = Task.Run(() => RunHealthMonitoringLoopAsync(ct), ct);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Штатно и безопасно останавливает сессию ETW и сопутствующие рабочие потоки.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;

        _healthState = ProcessLifecycleHealthState.Stopping;
        _logger.LogInformation("Остановка ETW Process Lifecycle Supervisor...");

        try
        {
            // Идемпотентно завершаем прием очереди
            _ingress.Complete();

            // Отменяем токен для фоновых задач
            _cts.Cancel();

            // Безопасно останавливаем native-сессию
            if (_session != null)
            {
                try
                {
                    _session.Stop();
                    _session.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при остановке native-сессии ETW во время shutdown.");
                }
            }

            // Ожидаем завершения рабочих задач с таймаутом
            var tasksToWait = new List<Task>();
            if (_readerTask != null) tasksToWait.Add(_readerTask);
            if (_reducerTask != null) tasksToWait.Add(_reducerTask);
            if (_healthCheckTask != null) tasksToWait.Add(_healthCheckTask);

            if (tasksToWait.Count > 0)
            {
                var combined = Task.WhenAll(tasksToWait);
                var completed = await Task.WhenAny(combined, Task.Delay(3000, cancellationToken)).ConfigureAwait(false);
                if (completed != combined)
                {
                    _logger.LogWarning("Таймаут ожидания завершения фоновых потоков ETW Supervisor.");
                }
                else
                {
                    await combined.ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Исключение при остановке ETW Process Lifecycle Supervisor.");
        }
        finally
        {
            _healthState = ProcessLifecycleHealthState.Stopped;
            _logger.LogInformation("ETW Process Lifecycle Supervisor полностью остановлен.");
        }
    }

    /// <summary>
    /// Цикл фонового чтения ETW на выделенном рабочем потоке.
    /// </summary>
    private void RunReaderLoop(
        IOwnedProcessTraceSession session,
        LifecycleIngress ingress,
        Guid sourceEpoch,
        ILogger logger,
        CancellationToken ct,
        Action onReady)
    {
        try
        {
            EtwProcessReader.Run(session, ingress, sourceEpoch, logger, ct, onReady);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Ожидаемая штатная отмена
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Фатальный сбой потока-читателя ETW сессии {SessionName}. Потребуется сверка.", session.Name);
            Volatile.Write(ref _readerFailed, 1);
            _healthState = ProcessLifecycleHealthState.Degraded;
            Interlocked.Increment(ref _historicalGapCount);
            ingress.RequestReconciliation();
        }
    }

    /// <summary>
    /// Однопоточный редуктор событий жизненного цикла (Single Writer).
    /// </summary>
    private async Task RunReducerLoopAsync(
        LifecycleIngress ingress,
        ProcessLifecycleRegistry registry,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var hasData = await ingress.WaitToReadAsync(ct).ConfigureAwait(false);
                if (!hasData) break;

                while (ingress.TryRead(out var ev))
                {
                    if (ev != null)
                    {
                        lock (_registryWriteGate)
                        {
                            if (ev.SourceEpoch == _sourceEpoch) registry.ApplyEvent(ev);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная отмена
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Критическая ошибка в цикле обработки событий реестра процессов.");
            _healthState = ProcessLifecycleHealthState.Degraded;
            Interlocked.Increment(ref _historicalGapCount);
            ingress.RequestReconciliation();
        }
    }

    /// <summary>
    /// Фоновый цикл мониторинга здоровья, потерь буферов и запросов авто-сверки (Reconciliation).
    /// </summary>
    private async Task RunHealthMonitoringLoopAsync(CancellationToken ct)
    {
        var lastReconciliation = 0L;
        var failures = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_reducerTask?.IsCompleted == true)
                        throw new IOException("Reducer ETW остановлен; реестр не может считаться согласованным.");

                    if (_session == null || Volatile.Read(ref _readerFailed) != 0 || _readerTask?.IsCompleted == true)
                    {
                        // Старый reader прекращается только после подтверждённого STOP. При отказе
                        // handle/lease сохраняются, следующий проход повторяет безопасную остановку.
                        if (_session != null)
                        {
                            _session.Stop();
                            if (_readerTask != null)
                                await _readerTask.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                            _session.Dispose();
                            _session = null;
                        }

                        _healthState = ProcessLifecycleHealthState.Starting;
                        var session = _sessionFactory?.Invoke() ?? NativeProcessTraceSession.Start(logger: _logger);
                        _session = session;
                        lock (_registryWriteGate) _sourceEpoch = Guid.NewGuid();
                        var sourceEpoch = _sourceEpoch;
                        _lastEventsLost = 0;
                        _lastRealTimeBuffersLost = 0;
                        Volatile.Write(ref _readerFailed, 0);
                        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _readerTask = Task.Factory.StartNew(
                            () => RunReaderLoop(session, _ingress, sourceEpoch, _logger, ct, () => ready.TrySetResult(true)),
                            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                        _healthState = ProcessLifecycleHealthState.Synchronizing;
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                        _ingress.RequestReconciliation();
                    }

                    if (_readerTask?.IsCompleted != false || Volatile.Read(ref _readerFailed) != 0)
                        throw new IOException("Reader ETW недоступен; требуется перезапуск источника.");

                    var diagnostic = _ingress.TakeParseFailureDiagnostic();
                    if (diagnostic != null)
                        _logger.LogWarning("Ошибки ETW payload: {Kind}, {Type}, {Code}; всего {Count}",
                            diagnostic.Kind, diagnostic.ExceptionType, diagnostic.ErrorCode, _ingress.ParseFailures);

                    if (_session is NativeProcessTraceSession nativeSession)
                    {
                        var stats = nativeSession.QueryStatistics()
                            ?? throw new IOException("Native QUERY не подтвердил активность ETW-сессии.");
                        // Вычитание накопительных unsigned счётчиков учитывает один оборот.
                        var eventsDelta = unchecked(stats.EventsLost - _lastEventsLost);
                        var buffersDelta = unchecked(stats.RealTimeBuffersLost - _lastRealTimeBuffersLost);
                        _lastEventsLost = stats.EventsLost;
                        _lastRealTimeBuffersLost = stats.RealTimeBuffersLost;
                        if (eventsDelta != 0 || buffersDelta != 0)
                        {
                            Interlocked.Increment(ref _historicalGapCount);
                            _healthState = ProcessLifecycleHealthState.Degraded;
                            _ingress.RequestReconciliation();
                            _logger.LogWarning("ETW потерял {Events} событий и {Buffers} real-time буферов; история содержит пробел.",
                                eventsDelta, buffersDelta);
                        }
                    }

                    var dropped = _ingress.Dropped;
                    if (dropped != _lastDropped || diagnostic != null)
                    {
                        _lastDropped = dropped;
                        Interlocked.Increment(ref _historicalGapCount);
                        _healthState = ProcessLifecycleHealthState.Degraded;
                    }

                    if (lastReconciliation == 0 || Stopwatch.GetElapsedTime(lastReconciliation) >= TimeSpan.FromSeconds(60))
                        _ingress.RequestReconciliation();

                    if (_registry.IsCapacityExceeded)
                    {
                        _healthState = ProcessLifecycleHealthState.Degraded;
                        _ingress.RequestReconciliation();
                    }

                    if (_ingress.NeedsReconciliation)
                    {
                        _healthState = ProcessLifecycleHealthState.Synchronizing;
                        var generation = _ingress.RequestedReconciliation;
                        var snapshotSource = _snapshotSource
                            ?? throw new InvalidOperationException("Не зарегистрирован авторитетный native snapshot source.");
                        lock (_registryWriteGate)
                        {
                            // Снимок и слияние сериализованы с событиями; callback ETW продолжает
                            // писать в bounded ingress без ожидания метаданных, сети или IPC.
                            var startedUtc = DateTime.UtcNow;
                            var processes = snapshotSource(ct);
                            _registry.ApplyBaseline(processes, startedUtc, DateTime.UtcNow);
                            _ingress.AcknowledgeReconciliation(generation);
                        }
                        lastReconciliation = Stopwatch.GetTimestamp();
                    }

                    // Сверка live-набора не восстанавливает историю потерянных коротких процессов.
                    if (_readerTask?.IsCompleted != false || Volatile.Read(ref _readerFailed) != 0 ||
                        _reducerTask?.IsCompleted != false)
                        throw new IOException("ETW worker остановлен во время синхронизации.");
                    if (_registry.IsCapacityExceeded)
                    {
                        _healthState = ProcessLifecycleHealthState.Degraded;
                        _logger.LogWarning("Live-реестр ETW превышает ёмкость {Capacity}; legacy-защита сохранена до полной сверки.",
                            ProcessLifecycleRegistry.MaxLiveCapacity);
                    }
                    else if (!_ingress.NeedsReconciliation)
                    {
                        if (_healthState != ProcessLifecycleHealthState.Healthy)
                            _logger.LogInformation("ETW Healthy: reader и native QUERY подтверждены, baseline применён; historical gaps {Gaps}.",
                                HistoricalGapCount);
                        _healthState = ProcessLifecycleHealthState.Healthy;
                    }
                    failures = 0;
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _healthState = ProcessLifecycleHealthState.Degraded;
                    _logger.LogWarning(ex, "ETW Degraded; защитный legacy sweep сохранён, выполняется повторная попытка.");
                    if (_reducerTask?.IsCompleted == true)
                    {
                        _reducerTask = Task.Run(() => RunReducerLoopAsync(_ingress, _registry, ct), ct);
                        _ingress.RequestReconciliation();
                    }
                    if (_session is NativeProcessTraceSession)
                        Volatile.Write(ref _readerFailed, 1);
                    failures = Math.Min(failures + 1, 6);
                    var delay = failures switch { 1 => 1, 2 => 2, 3 => 5, 4 => 10, _ => 30 };
                    await Task.Delay(TimeSpan.FromSeconds(delay), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Контроль здоровья ETW остановлен по запросу.");
        }
        finally
        {
            try { _session?.Stop(); }
            catch (Exception ex) { _logger.LogError(ex, "Остановка ETW при завершении supervisor не подтверждена; lease сохранён."); }
        }
    }


    /// <summary>
    /// Освобождает ресурсы супервизора.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _cts.Cancel();
        _cts.Dispose();
        _session?.Dispose();
    }
}

