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
    private readonly Guid _sourceEpoch = Guid.NewGuid();
    private readonly CancellationTokenSource _cts = new();

    private IOwnedProcessTraceSession? _session;
    private Task? _readerTask;
    private Task? _reducerTask;
    private Task? _healthCheckTask;
    private ProcessLifecycleHealthState _healthState = ProcessLifecycleHealthState.Starting;
    private int _disposed;
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
        ProcessLifecycleMode mode = ProcessLifecycleMode.EtwPreferred)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mode = mode;
    }

    /// <summary>
    /// Текущее состояние здоровья подсистемы ETW.
    /// </summary>
    public ProcessLifecycleHealthState HealthState => _healthState;

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
        if (_mode == ProcessLifecycleMode.Legacy)
        {
            _logger.LogInformation("Процессный мониторинг запущен в режиме Legacy (ETW отключен конфигурацией).");
            _healthState = ProcessLifecycleHealthState.Stopped;
            return Task.CompletedTask;
        }

        _logger.LogInformation("Инициализация ETW Process Lifecycle в режиме {Mode} (Эпоха источника: {Epoch})...", _mode, _sourceEpoch);

        try
        {
            // 1. Попытка создания изолированной сессии SystemTraceProvider
            if (!NativeProcessTraceSession.TryStart(
                session: out var ownedSession,
                errorMessage: out var errorMsg,
                errorCode: out var errorCode,
                sessionName: "StartupMonitor.ProcessLifecycle",
                logger: _logger))
            {
                _logger.LogWarning(
                    "Не удалось активировать native-сессию ETW (Код Win32: {ErrorCode}, Причина: {Error}). Активирован автоматический fallback.",
                    errorCode,
                    errorMsg);

                _healthState = ProcessLifecycleHealthState.Degraded;
                return Task.CompletedTask;
            }

            _session = ownedSession;
            _healthState = ProcessLifecycleHealthState.Synchronizing;

            var ct = _cts.Token;

            // 2. Запуск фонового читателя событий ядра
            _readerTask = Task.Factory.StartNew(
                () => RunReaderLoop(ownedSession!, _ingress, _sourceEpoch, _logger, ct),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            // 3. Запуск однопоточного редуктора состояния (Single Writer)
            _reducerTask = Task.Factory.StartNew(
                () => RunReducerLoopAsync(_ingress, _registry, ct),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();

            // 4. Запуск фонового цикла контроля здоровья и авто-сверки
            _healthCheckTask = Task.Run(() => RunHealthMonitoringLoopAsync(ct), ct);

            _healthState = ProcessLifecycleHealthState.Healthy;
            _logger.LogInformation("ETW Process Lifecycle успешно запущен и переведен в состояние Healthy.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Непредвиденное исключение при инициализации ETW Supervisor. Переход в режим Degraded.");
            _healthState = ProcessLifecycleHealthState.Degraded;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Штатно и безопасно останавливает сессию ETW и сопутствующие рабочие потоки.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

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
    private static void RunReaderLoop(
        IOwnedProcessTraceSession session,
        LifecycleIngress ingress,
        Guid sourceEpoch,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            EtwProcessReader.Run(session, ingress, sourceEpoch, logger, ct);
        }
        catch (OperationCanceledException)
        {
            // Ожидаемая штатная отмена
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Фатальный сбой потока-читателя ETW сессии {SessionName}. Потребуется сверка.", session.Name);
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
                        registry.ApplyEvent(ev);
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
        }
    }

    /// <summary>
    /// Фоновый цикл мониторинга здоровья, потерь буферов и запросов авто-сверки (Reconciliation).
    /// </summary>
    private async Task RunHealthMonitoringLoopAsync(CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                // 1. Опрос ошибок парсинга из ingress с троттлингом
                var diagnostic = _ingress.TakeParseFailureDiagnostic();
                if (diagnostic != null)
                {
                    _logger.LogWarning(
                        "Зафиксированы ошибки декодирования событий ETW (Вид: {Kind}, Тип: {Type}, Код: {Code}, Всего сбоев: {Total}).",
                        diagnostic.Kind,
                        diagnostic.ExceptionType,
                        diagnostic.ErrorCode,
                        _ingress.ParseFailures);
                }

                // 2. Опрос нативной статистики ядра
                if (_session is NativeProcessTraceSession nativeSession)
                {
                    try
                    {
                        var stats = nativeSession.QueryStatistics();
                        if (stats != null && (stats.EventsLost > _lastEventsLost || stats.RealTimeBuffersLost > _lastRealTimeBuffersLost))
                        {
                            var lostDelta = (stats.EventsLost - _lastEventsLost) + (stats.RealTimeBuffersLost - _lastRealTimeBuffersLost);
                            _lastEventsLost = stats.EventsLost;
                            _lastRealTimeBuffersLost = stats.RealTimeBuffersLost;

                            _logger.LogWarning(
                                "Обнаружена потеря {Delta} событий/буферов ядра ETW (Всего потеряно: {TotalLost}). Запрос сверки реестра.",
                                lostDelta,
                                stats.EventsLost + stats.RealTimeBuffersLost);

                            _healthState = ProcessLifecycleHealthState.Degraded;
                            _ingress.RequestReconciliation();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Не удалось опросить нативную статистику сессии ETW.");
                    }
                }

                // 3. Проверка необходимости сверки (Reconciliation)
                if (_ingress.NeedsReconciliation)
                {
                    var requestedGen = _ingress.RequestedReconciliation;
                    _logger.LogInformation("Выполняется сверка состояния процессов (Reconciliation Generation: {Gen})...", requestedGen);

                    try
                    {
                        // Захватываем текущие запущенные процессы через Toolhelp32/Process API для восстановления live-набора
                        var currentProcesses = Process.GetProcesses();
                        var baselineList = new List<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>(currentProcesses.Length);

                        var nowUtc = DateTime.UtcNow;
                        foreach (var p in currentProcesses)
                        {
                            DateTime startTime = nowUtc;
                            try { startTime = p.StartTime.ToUniversalTime(); } catch { }
                            baselineList.Add((p.Id, 0, startTime, p.ProcessName));
                            p.Dispose();
                        }

                        _registry.ApplyBaseline(baselineList, nowUtc);
                        _ingress.AcknowledgeReconciliation(requestedGen);

                        _healthState = ProcessLifecycleHealthState.Healthy;
                        _logger.LogInformation("Сверка состояния процессов успешно завершена (Подтверждена Generation: {Gen}).", requestedGen);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при выполнении сверки процессов (Reconciliation).");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная отмена
        }
        finally
        {
            timer.Dispose();
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

