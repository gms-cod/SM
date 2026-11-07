using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>Удерживает единственный проверенный handle процесса до завершения автоматического действия.</summary>
public interface IProcessBlacklistTarget : IDisposable
{
    /// <summary>Авторитетное время создания из GetProcessTimes того же handle.</summary>
    DateTime CreationTimeUtc { get; }
    /// <summary>Имя текущего процесса для сохранения host/protected исключений.</summary>
    string ProcessName { get; }
    /// <summary>Путь, прочитанный на удерживаемом handle, либо null при недоступности.</summary>
    string? ExecutablePath { get; }
    /// <summary>Признак завершения удерживаемого экземпляра.</summary>
    bool HasExited { get; }
    /// <summary>Завершает проверенный процесс с прежней политикой entireProcessTree.</summary>
    void Kill();
}

/// <summary>
/// Выполняет автоматические правила ЧС вне callback ETW. Сигнал ёмкостью один означает полный catch-up,
/// поэтому его коалесцирование не теряет pending-процессы. Legacy и ETW действия сериализованы одним gate.
/// </summary>
public sealed class ProcessBlacklistEnforcer : BackgroundService
{
    private readonly JsonDataStore _store;
    private readonly MonitoringStateCoordinator _coordinator;
    private readonly ProcessLifecycleRegistry _registry;
    private readonly LifecycleIngress _ingress;
    private readonly ProcessLifecycleMode _mode;
    private readonly Func<ProcessLifecycleHealthState> _healthSource;
    private readonly Func<CancellationToken, IReadOnlyList<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>> _snapshotSource;
    private readonly Func<int, IProcessBlacklistTarget> _targetFactory;
    private readonly ILogger<ProcessBlacklistEnforcer> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _executionGate = new();
    private readonly Dictionary<ProcessInstanceId, (long Version, long Resume, string? Path, ProcessMetadataStatus Metadata)> _completed = new();
    /// <summary>Ограниченный live-набор отказов: повтор допускается по таймеру либо после изменения политики/метаданных.</summary>
    private readonly Dictionary<ProcessInstanceId, RetryState> _retries = new();
    /// <summary>Сохраняет причину отказа и экспоненциальную задержку без ложного кэширования успешного Kill.</summary>
    private sealed record RetryState((long Version, long Resume, string? Path, ProcessMetadataStatus Metadata) Stamp,
        DateTimeOffset RetryAfter, int Attempts, string Reason);
    private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite,
        AllowSynchronousContinuations = false
    });
    private long _changes;
    private long _resumeGeneration;
    private long _caughtUpVersion = -1;
    private int _caughtUp;
    private bool _wasPaused;
    private long _lastProcessedRegistryRevision = -1;
    private long _lastProcessedResume = -1;
    private long _rulesVersion = -1;
    private IReadOnlyList<BlacklistRule> _activeRules = Array.Empty<BlacklistRule>();

    /// <summary>Создаёт executor с явными источниками здоровья, снимков и удерживаемых process handles.</summary>
    /// <param name="store">Хранилище с отдельной версией правил ЧС.</param>
    /// <param name="coordinator">Координатор паузы.</param>
    /// <param name="registry">Реестр экземпляров процессов.</param>
    /// <param name="ingress">Состояние потерь и незавершённой сверки.</param>
    /// <param name="mode">Legacy, Shadow либо EtwPreferred.</param>
    /// <param name="healthSource">Текущее здоровье reader/reducer/baseline.</param>
    /// <param name="snapshotSource">Общий native-снимок без CPU/network/Authenticode.</param>
    /// <param name="logger">Журнал применения и отказов.</param>
    /// <param name="targetFactory">Фабрика handle-владельца; по умолчанию реальные Windows-процессы.</param>
    /// <param name="timeProvider">Источник времени для ограниченных повторов; по умолчанию системный.</param>
    public ProcessBlacklistEnforcer(JsonDataStore store, MonitoringStateCoordinator coordinator,
        ProcessLifecycleRegistry registry, LifecycleIngress ingress, ProcessLifecycleMode mode,
        Func<ProcessLifecycleHealthState> healthSource,
        Func<CancellationToken, IReadOnlyList<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>> snapshotSource,
        ILogger<ProcessBlacklistEnforcer> logger, Func<int, IProcessBlacklistTarget>? targetFactory = null,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
        _healthSource = healthSource ?? throw new ArgumentNullException(nameof(healthSource));
        _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _mode = mode;
        _targetFactory = targetFactory ?? (pid => new WindowsProcessBlacklistTarget(pid, _snapshotSource,
            _registry.GetLiveProcess(pid)?.ImageName));
    }

    /// <summary>Разрешает пропуск legacy sweep только после полного catch-up текущей версии без потерь.</summary>
    public bool CanSuppressLegacySweep => _mode == ProcessLifecycleMode.EtwPreferred &&
        Volatile.Read(ref _caughtUp) != 0 && !_coordinator.IsPaused &&
        _healthSource() == ProcessLifecycleHealthState.Healthy && !_ingress.NeedsReconciliation && !_registry.IsCapacityExceeded &&
        Interlocked.Read(ref _caughtUpVersion) == _store.BlacklistRevision;

    /// <summary>Сериализует legacy fallback с ETW-действиями и повторно проверяет паузу под общим gate.</summary>
    /// <param name="sweep">Прежний полный проход ЧС, выполняемый только при необходимости fallback.</param>
    public void RunFallbackSweep(Action sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        lock (_executionGate)
        {
            if (!_coordinator.IsPaused && !CanSuppressLegacySweep) sweep();
        }
    }

    /// <summary>Неблокирующий коалесцируемый запрос полного catch-up после события, правила или сверки.</summary>
    private void Signal()
    {
        Volatile.Write(ref _caughtUp, 0);
        Interlocked.Increment(ref _changes);
        _wake.Writer.TryWrite(0);
    }

    /// <summary>После resume старые отрицательные результаты больше не заменяют проверку live-набора.</summary>
    private void OnMonitoringStateChanged(object? sender, bool paused)
    {
        if (!paused) Interlocked.Increment(ref _resumeGeneration);
        Signal();
    }

    /// <summary>Обслуживает coalesced-сигналы и повторяет отказы без системного перечисления в idle.</summary>
    /// <param name="stoppingToken">Отмена Host.</param>
    /// <returns>Задача завершения worker с освобождением подписок.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_mode == ProcessLifecycleMode.Legacy) return;
        _registry.Changed += Signal;
        _store.BlacklistChanged += Signal;
        _coordinator.StateChanged += OnMonitoringStateChanged;
        Signal();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                waitCts.CancelAfter(TimeSpan.FromSeconds(1));
                try { await _wake.Reader.WaitToReadAsync(waitCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { _logger.LogTrace("Контрольный tick executor ЧС"); }
                while (_wake.Reader.TryRead(out _)) { /* Сигнал означает весь live-набор; отдельного payload нет. */ }
                await Task.Delay(75, stoppingToken).ConfigureAwait(false);
                await ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("ETW executor ЧС остановлен по запросу Host.");
        }
        finally
        {
            Volatile.Write(ref _caughtUp, 0);
            _registry.Changed -= Signal;
            _store.BlacklistChanged -= Signal;
            _coordinator.StateChanged -= OnMonitoringStateChanged;
        }
    }

    /// <summary>Применяет один полный catch-up; failed kill остаётся pending и включает legacy fallback.</summary>
    /// <param name="ct">Отмена партии.</param>
    /// <returns>Завершённая задача после синхронных bounded операций партии вне ETW callback.</returns>
    internal Task ProcessPendingAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_executionGate)
        {
            if (_mode == ProcessLifecycleMode.Legacy) return Task.CompletedTask;
            // Idle tick читает только атомарные версии. Нет cloning правил/реестра и системного
            // перечисления, пока не изменились процессы, правила, pause или здоровье reader.
            if (Volatile.Read(ref _caughtUp) != 0 && !_coordinator.IsPaused &&
                _healthSource() == ProcessLifecycleHealthState.Healthy && !_ingress.NeedsReconciliation && !_registry.IsCapacityExceeded &&
                Interlocked.Read(ref _caughtUpVersion) == _store.BlacklistRevision &&
                _lastProcessedRegistryRevision == _registry.Revision &&
                _lastProcessedResume == Interlocked.Read(ref _resumeGeneration))
                return Task.CompletedTask;
            Volatile.Write(ref _caughtUp, 0);
            if (_coordinator.IsPaused)
            {
                _wasPaused = true;
                return Task.CompletedTask;
            }
            if (_wasPaused)
            {
                _completed.Clear();
                _retries.Clear();
                _wasPaused = false;
            }
            if (_healthSource() != ProcessLifecycleHealthState.Healthy || _ingress.NeedsReconciliation || _registry.IsCapacityExceeded)
                return Task.CompletedTask;

            try
            {
                var changeGeneration = Interlocked.Read(ref _changes);
                var resume = Interlocked.Read(ref _resumeGeneration);
                if (_rulesVersion != _store.BlacklistRevision)
                {
                    var snapshot = _store.GetBlacklistSnapshot();
                    _activeRules = new BlacklistRuleSnapshot(snapshot.Version, snapshot.Rules).ActiveRules;
                    _rulesVersion = snapshot.Version;
                }
                var version = _rulesVersion;
                var activeRules = _activeRules;
                var registryRevision = _registry.Revision;
                var live = activeRules.Count == 0 ? Array.Empty<ProcessInstanceRecord>() : _registry.GetLiveSnapshot();
                var liveIds = live.Select(p => p.InstanceId).ToHashSet();
                foreach (var expired in _completed.Keys.Where(id => !liveIds.Contains(id)).ToArray()) _completed.Remove(expired);
                foreach (var expired in _retries.Keys.Where(id => !liveIds.Contains(id)).ToArray()) _retries.Remove(expired);
                Dictionary<int, DateTime>? authoritative = null;
                var allCompleted = true;

                if (activeRules.Count != 0)
                {
                    foreach (var record in live)
                    {
                        ct.ThrowIfCancellationRequested();
                        var stamp = (version, resume, record.ResolvedExecutablePath, record.MetadataStatus);
                        if (_completed.TryGetValue(record.InstanceId, out var previous) && previous == stamp) continue;
                        if (_retries.TryGetValue(record.InstanceId, out var retry) && retry.Stamp == stamp &&
                            retry.RetryAfter > _timeProvider.GetUtcNow())
                        {
                            allCompleted = false;
                            continue;
                        }
                        if (ProcessRuleEvaluator.IsProtected(record.ImageName, record.ResolvedExecutablePath))
                        {
                            _completed[record.InstanceId] = stamp;
                            continue;
                        }
                        if (!ProcessRuleEvaluator.CouldMatchProcessName(record.ImageName, activeRules))
                        {
                            _completed[record.InstanceId] = stamp;
                            continue;
                        }

                        var targetOpened = false;
                        try
                        {
                            var creationUtc = record.CreationTimeUtc;
                            if (!record.HasAuthoritativeCreationTime)
                            {
                                authoritative ??= _snapshotSource(ct).ToDictionary(p => p.Pid, p => p.CreationTimeUtc);
                                if (!authoritative.TryGetValue(record.ProcessId, out creationUtc))
                                {
                                    _completed[record.InstanceId] = stamp;
                                    continue;
                                }
                                if (record.CreationTimeUtc != DateTime.MinValue && creationUtc > record.CreationTimeUtc)
                                {
                                    _ingress.RequestReconciliation();
                                    allCompleted = false;
                                    continue;
                                }
                            }
                            if (creationUtc == DateTime.MinValue)
                            {
                                allCompleted = false;
                                continue;
                            }

                            using var target = _targetFactory(record.ProcessId);
                            targetOpened = true;
                            if (target.HasExited)
                            {
                                _completed[record.InstanceId] = stamp;
                                continue;
                            }
                            if (target.CreationTimeUtc != creationUtc)
                            {
                                _ingress.RequestReconciliation();
                                allCompleted = false;
                                continue;
                            }
                            var actualName = target.ProcessName;
                            var path = target.ExecutablePath;
                            // Подтверждённый handle завершает ленивую Basic-стадию lifecycle metadata.
                            // Только текущая InstanceId принимает результат; запоздалый PID не изменяется.
                            if (!_registry.TryUpdateMetadata(record.InstanceId,
                                string.IsNullOrEmpty(path) ? ProcessMetadataStatus.Unknown : ProcessMetadataStatus.Basic, path,
                                verifiedCreationTimeUtc: creationUtc))
                            {
                                _ingress.RequestReconciliation();
                                allCompleted = false;
                                continue;
                            }
                            var confirmed = _registry.GetLiveProcess(record.ProcessId);
                            if (confirmed != null && confirmed.InstanceId == record.InstanceId)
                                stamp = (version, resume, confirmed.ResolvedExecutablePath, confirmed.MetadataStatus);
                            var match = ProcessRuleEvaluator.Evaluate(actualName, path, activeRules);
                            if (!match.IsMatched)
                            {
                                // Unknown-path оценивается тем же правилом, что и legacy: только имя без каталогов.
                                // Это завершённая оценка текущих доступных данных, а не утверждение, что путь известен.
                                // Новые метаданные, версия ЧС или resume меняют stamp и повторяют проверку.
                                _completed[record.InstanceId] = stamp;
                                _retries.Remove(record.InstanceId);
                                continue;
                            }

                            // Проверки выполняются непосредственно перед действием на том же target/handle.
                            if (_coordinator.IsPaused || _store.BlacklistRevision != version ||
                                _registry.GetLiveProcess(record.ProcessId)?.InstanceId != record.InstanceId ||
                                _healthSource() != ProcessLifecycleHealthState.Healthy || _ingress.NeedsReconciliation || _registry.IsCapacityExceeded)
                            {
                                allCompleted = false;
                                continue;
                            }
                            if (_mode == ProcessLifecycleMode.Shadow)
                                _logger.LogDebug("Shadow ЧС: совпадение PID {Pid}, версия {Version}; действие оставлено legacy.", record.ProcessId, version);
                            else
                            {
                                target.Kill();
                                _logger.LogInformation("ETW ЧС: завершён PID {Pid}, поколение {Generation}, версия правил {Version}.", record.ProcessId, record.Generation, version);
                            }
                            _completed[record.InstanceId] = stamp;
                            _retries.Remove(record.InstanceId);
                        }
                        catch (ArgumentException ex) when (!targetOpened)
                        {
                            // Process.GetProcessById сообщает об уже завершённом PID; следующая Start имеет другой InstanceId.
                            _logger.LogDebug(ex, "Pending ЧС PID {Pid} уже отсутствует.", record.ProcessId);
                            _completed[record.InstanceId] = stamp;
                            _retries.Remove(record.InstanceId);
                        }
                        catch (Win32Exception ex) when (!targetOpened && ex.NativeErrorCode == 87)
                        {
                            // OpenProcess сообщает ERROR_INVALID_PARAMETER, когда PID уже исчез из ОС.
                            _logger.LogDebug(ex, "Pending ЧС PID {Pid} уже отсутствует при открытии.", record.ProcessId);
                            _completed[record.InstanceId] = stamp;
                            _retries.Remove(record.InstanceId);
                        }
                        catch (Win32Exception ex) when (!targetOpened && ex.NativeErrorCode == 5 &&
                            !ProcessRuleEvaluator.Evaluate(record.ImageName, record.ResolvedExecutablePath, activeRules).IsMatched)
                        {
                            // PPL и Secure System могут запрещать даже QUERY_LIMITED_INFORMATION. Если доступные
                            // сведения не совпадают с политикой unknown-path, права завершения не запрашиваются.
                            _logger.LogDebug(ex, "ETW ЧС PID {Pid}: чтение запрещено ОС, доступные данные не совпали с правилом; метаданные остаются Unknown.", record.ProcessId);
                            _completed[record.InstanceId] = stamp;
                            _retries.Remove(record.InstanceId);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            allCompleted = false;
                            ScheduleRetry(record, stamp, ex);
                        }
                    }
                }

                if (allCompleted && !_coordinator.IsPaused && _store.BlacklistRevision == version &&
                    Interlocked.Read(ref _changes) == changeGeneration && Interlocked.Read(ref _resumeGeneration) == resume &&
                    _healthSource() == ProcessLifecycleHealthState.Healthy && !_ingress.NeedsReconciliation && !_registry.IsCapacityExceeded)
                {
                    Interlocked.Exchange(ref _caughtUpVersion, version);
                    _lastProcessedRegistryRevision = registryRevision;
                    _lastProcessedResume = resume;
                    Volatile.Write(ref _caughtUp, 1);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Партия ETW ЧС прервана; защитный legacy sweep сохранён.");
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Повторяет реальный отказ с задержкой 1–30 секунд; предупреждает только о первом отказе или смене причины.</summary>
    /// <param name="record">Живой экземпляр, для которого fallback остаётся включённым.</param>
    /// <param name="stamp">Версия правил, resume и метаданных данной попытки.</param>
    /// <param name="exception">Контекст отказа открытия совпавшей цели либо завершения процесса.</param>
    private void ScheduleRetry(ProcessInstanceRecord record,
        (long Version, long Resume, string? Path, ProcessMetadataStatus Metadata) stamp, Exception exception)
    {
        var reason = exception is Win32Exception native ? $"Win32:{native.NativeErrorCode}" : exception.GetType().FullName ?? exception.GetType().Name;
        _retries.TryGetValue(record.InstanceId, out var previous);
        var sameFailure = previous != null && previous.Stamp == stamp && previous.Reason == reason;
        var attempts = sameFailure && previous != null ? Math.Min(previous.Attempts + 1, 6) : 1;
        var delay = TimeSpan.FromSeconds(Math.Min(1 << (attempts - 1), 30));
        _retries[record.InstanceId] = new RetryState(stamp, _timeProvider.GetUtcNow() + delay, attempts, reason);
        if (sameFailure)
            _logger.LogDebug(exception, "ETW ЧС PID {Pid}: повторный отказ, следующая попытка через {Delay}s; legacy fallback сохранён.", record.ProcessId, delay.TotalSeconds);
        else
            _logger.LogWarning(exception, "ETW ЧС не завершил проверку PID {Pid}; повтор через {Delay}s и legacy fallback сохранены.", record.ProcessId, delay.TotalSeconds);
    }

    /// <summary>
    /// Удерживает query-only handle для чтения. Только совпавшее правило открывает отдельные action handles;
    /// FILETIME проверяется на каждом из них перед TerminateProcess, включая потомков из одного native-снимка.
    /// </summary>
    internal sealed class WindowsProcessBlacklistTarget : IProcessBlacklistTarget
    {
        private const uint QueryLimitedInformation = 0x1000;
        private const uint Terminate = 0x0001;
        private const uint Synchronize = 0x00100000;
        private readonly int _processId;
        private readonly string _fallbackName;
        private readonly SafeProcessHandle _handle;
        private readonly Func<CancellationToken, IReadOnlyList<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>> _snapshotSource;

        /// <summary>Открывает экземпляр исключительно для чтения; отказ не запрашивает ALL_ACCESS или TERMINATE.</summary>
        /// <param name="processId">Положительный PID проверяемого процесса.</param>
        /// <param name="snapshotSource">Общий bounded native-снимок, используемый только при явном Kill дерева.</param>
        /// <param name="processName">Имя из registry для политики unknown-path; вызывающий проверяет FILETIME до оценки.</param>
        public WindowsProcessBlacklistTarget(int processId,
            Func<CancellationToken, IReadOnlyList<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)>> snapshotSource,
            string? processName = null)
        {
            if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
            _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
            _processId = processId;
            _fallbackName = processName ?? string.Empty;
            // SYNCHRONIZE нужен только для точного HasExited: код выхода 259 может быть реальным кодом завершения.
            _handle = OpenChecked(processId, QueryLimitedInformation | Synchronize);
            try { CreationTimeUtc = ReadCreationTime(_handle); }
            catch { _handle.Dispose(); throw; } // Частичный владелец освобождается; исключение журналирует executor.
        }

        /// <inheritdoc />
        public DateTime CreationTimeUtc { get; }
        /// <inheritdoc />
        public string ProcessName => Path.GetFileName(ExecutablePath) is { Length: > 0 } name ? name : _fallbackName;
        /// <inheritdoc />
        public bool HasExited
        {
            get
            {
                return HasHandleExited(_handle);
            }
        }
        /// <inheritdoc />
        public string? ExecutablePath
        {
            get
            {
                var path = new StringBuilder(1024);
                var size = path.Capacity;
                if (QueryFullProcessImageName(_handle, 0, path, ref size)) return path.ToString();
                var error = Marshal.GetLastWin32Error();
                // ERROR_GEN_FAILURE также означает отсутствие доступного Win32 image у системного экземпляра
                // (например, изолированного процесса VBS); это Unknown path, а не ошибка применения правила.
                if (error is 5 or 31 or 299) return null;
                if (error != 122) throw new Win32Exception(error);
                path = new StringBuilder(32768);
                size = path.Capacity;
                if (QueryFullProcessImageName(_handle, 0, path, ref size)) return path.ToString();
                error = Marshal.GetLastWin32Error();
                if (error is 5 or 31 or 299) return null;
                throw new Win32Exception(error);
            }
        }

        /// <summary>
        /// Сохраняет политику завершения дерева без ALL_ACCESS. Сначала получает снимок и проверяет handles
        /// всех потомков, затем завершает родителя и потомков. Сбой подготовки не изменяет дерево и допускает retry.
        /// </summary>
        public void Kill()
        {
            using var root = OpenActionHandle(_processId, CreationTimeUtc);
            var snapshot = _snapshotSource(CancellationToken.None);
            var descendants = snapshot.ToLookup(p => p.ParentPid);
            var pending = new Queue<(int Pid, DateTime Created)>();
            var visited = new HashSet<int> { _processId };
            var handles = new List<SafeProcessHandle>();
            pending.Enqueue((_processId, CreationTimeUtc));
            try
            {
                while (pending.TryDequeue(out var parent))
                {
                    foreach (var child in descendants[parent.Pid])
                    {
                        // PPID может ссылаться на давно завершившегося родителя; время ребёнка отсеивает reuse.
                        if (child.CreationTimeUtc == DateTime.MinValue || child.CreationTimeUtc < parent.Created ||
                            !visited.Add(child.Pid)) continue;
                        if (child.Pid == Environment.ProcessId || _processId == Environment.ProcessId)
                            throw new InvalidOperationException("Завершение дерева, содержащего саму службу, запрещено.");
                        var childHandle = OpenDescendantHandle(child.Pid, child.CreationTimeUtc);
                        if (childHandle == null) continue; // Исчезнувший либо повторно использованный PID уже не является этим ребёнком.
                        handles.Add(childHandle);
                        pending.Enqueue((child.Pid, child.CreationTimeUtc));
                    }
                }
                if (_processId == Environment.ProcessId)
                    throw new InvalidOperationException("Завершение самой службы запрещено.");
                // Ошибки snapshot/identity/open возникают до первого TerminateProcess, исключая ложный успех.
                TerminateChecked(root);
                foreach (var handle in handles) TerminateChecked(handle);
            }
            finally
            {
                foreach (var handle in handles) handle.Dispose();
            }
        }
        /// <inheritdoc />
        public void Dispose() => _handle.Dispose();

        /// <summary>Открывает bounded action handle и проверяет FILETIME до любого деструктивного вызова.</summary>
        /// <param name="processId">PID родителя или его подтверждённого потомка.</param>
        /// <param name="expectedCreationTime">Точное время из query handle или системного снимка.</param>
        /// <returns>Принадлежащий вызывающему handle с минимальными правами завершения и чтения.</returns>
        private static SafeProcessHandle OpenActionHandle(int processId, DateTime expectedCreationTime)
        {
            var handle = OpenChecked(processId, QueryLimitedInformation | Synchronize | Terminate);
            try
            {
                if (ReadCreationTime(handle) != expectedCreationTime)
                    throw new InvalidOperationException(EntryOperations.ProcessInstanceChangedReason);
                return handle;
            }
            catch { handle.Dispose(); throw; } // Владелец не передан вызывающему; executor журналирует отказ.
        }

        /// <summary>Подтверждает потомка из снимка; естественный выход или PID reuse не блокируют завершение исходного дерева.</summary>
        /// <param name="processId">PID потомка из единственного снимка дерева.</param>
        /// <param name="expectedCreationTime">Точный FILETIME исходного экземпляра ребёнка.</param>
        /// <returns>Action handle подтверждённого ребёнка либо null, когда исходного экземпляра больше нет.</returns>
        private static SafeProcessHandle? OpenDescendantHandle(int processId, DateTime expectedCreationTime)
        {
            var handle = OpenProcess(QueryLimitedInformation | Synchronize | Terminate, false, processId);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (error == 87) return null;
                throw new Win32Exception(error);
            }
            try
            {
                if (ReadCreationTime(handle) == expectedCreationTime) return handle;
                handle.Dispose();
                return null;
            }
            catch { handle.Dispose(); throw; } // Ошибки чтения передаются executor; естественный reuse обработан явно.
        }

        /// <summary>Открывает процесс с точной маской прав либо передаёт исходный код Win32 обработчику повторов.</summary>
        /// <param name="processId">Идентификатор процесса.</param>
        /// <param name="access">Минимальные права чтения либо завершения.</param>
        /// <returns>Владелец действительного kernel handle.</returns>
        private static SafeProcessHandle OpenChecked(int processId, uint access)
        {
            var handle = OpenProcess(access, false, processId);
            if (!handle.IsInvalid) return handle;
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        /// <summary>Получает авторитетное время создания с дескриптора, удерживаемого до действия.</summary>
        /// <param name="handle">Query-only или action handle проверяемого экземпляра.</param>
        /// <returns>Точный FILETIME UTC; ошибку API не подменяет пустым временем.</returns>
        private static DateTime ReadCreationTime(SafeProcessHandle handle)
        {
            if (!GetProcessTimes(handle, out var created, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return DateTime.FromFileTimeUtc(created);
        }

        /// <summary>Завершает именно проверенный экземпляр; естественный выход между проверкой и действием считается успехом.</summary>
        /// <param name="handle">Удерживаемый action handle, чей FILETIME уже проверен.</param>
        private static void TerminateChecked(SafeProcessHandle handle)
        {
            if (TerminateProcess(handle, -1)) return;
            var error = Marshal.GetLastWin32Error();
            if (error == 5 && HasHandleExited(handle)) return;
            throw new Win32Exception(error);
        }

        /// <summary>Проверяет signal процесса без ожидания; корректно отличает выход с кодом 259 от работающего процесса.</summary>
        /// <param name="handle">Дескриптор с недеструктивным правом SYNCHRONIZE.</param>
        /// <returns>Истина для завершённого экземпляра; сбой ожидания передаётся executor с кодом Win32.</returns>
        private static bool HasHandleExited(SafeProcessHandle handle)
        {
            var status = WaitForSingleObject(handle, 0);
            return status switch
            {
                0 => true,
                258 => false,
                uint.MaxValue => throw new Win32Exception(Marshal.GetLastWin32Error()),
                _ => throw new InvalidOperationException($"Неожиданный статус ожидания процесса: {status}.")
            };
        }

        /// <summary>Открывает процесс, не запрашивая лишних прав либо повышения привилегий.</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        /// <summary>Читает времена жизни проверяемого экземпляра без повторного поиска по PID.</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);

        /// <summary>Применяет завершение непосредственно к проверенному action handle.</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(SafeProcessHandle process, int exitCode);

        /// <summary>Читает состояние kernel object процесса с нулевым таймаутом, не блокируя worker.</summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        /// <summary>Читает имя образа исключительно по удерживаемому дескриптору.</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder path, ref int size);
    }
}
