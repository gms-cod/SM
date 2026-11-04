using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Статус полноты метаданных экземпляра процесса.
/// </summary>
public enum ProcessMetadataStatus
{
    /// <summary>Метаданные еще не разрешались.</summary>
    Unknown,

    /// <summary>Базовые метаданные (путь и аргументы) получены.</summary>
    Basic,

    /// <summary>Цифровая подпись Authenticode проверена (независимо от того, валидна она или нет).</summary>
    SignatureChecked
}

/// <summary>
/// Неизменяемая запись активного экземпляра процесса в реестре жизненного цикла.
/// </summary>
public sealed record ProcessInstanceRecord(
    ProcessInstanceId InstanceId,
    int ParentProcessId,
    ulong KernelKey,
    DateTime CreationTimeUtc,
    long CreationQpc,
    string ImageName,
    string CommandLine,
    bool IsFromBaseline,
    ProcessMetadataStatus MetadataStatus,
    string? ResolvedExecutablePath = null,
    string? Signer = null)
{
    /// <summary>
    /// Идентификатор процесса (PID).
    /// </summary>
    public int ProcessId => InstanceId.ProcessId;

    /// <summary>
    /// Поколение PID, назначенное реестром.
    /// </summary>
    public long Generation => InstanceId.Generation;
}

/// <summary>
/// Историческая запись завершенного процесса с метаданными и причиной выхода.
/// </summary>
public sealed record ProcessExitRecord(
    ProcessInstanceId InstanceId,
    int ParentProcessId,
    DateTime CreationTimeUtc,
    DateTime ExitTimeUtc,
    string ImageName,
    string ExitReason);

/// <summary>
/// Реестр жизненного цикла процессов (Single Writer Registry).
/// Управляет активным набором процессов, разрешает коллизии быстрого переиспользования PID,
/// ведет ограниченную историю завершенных процессов и обеспечивает неблокирующее чтение через immutable-проекции.
/// </summary>
public sealed class ProcessLifecycleRegistry
{
    private readonly Guid _monitorEpoch;
    private readonly object _stateLock = new();
    private readonly Dictionary<int, ProcessInstanceRecord> _liveProcesses = new(1024);
    private readonly Dictionary<int, List<(long Generation, DateTime ExitedUtc)>> _tombstones = new(1024);
    private readonly LinkedList<ProcessExitRecord> _history = new();
    private long _generationCounter;
    private long _totalStartedCount;
    private long _totalStoppedCount;

    /// <summary>Максимальная емкость кэша живых процессов для предотвращения неконтролируемого роста памяти.</summary>
    public const int MaxLiveCapacity = 32768;

    /// <summary>Максимальный размер исторического буфера завершенных процессов.</summary>
    public const int MaxHistoryCapacity = 10000;

    /// <summary>Время удержания надгробий (tombstones) поколений PID для защиты от запоздалых событий.</summary>
    public static readonly TimeSpan TombstoneTtl = TimeSpan.FromSeconds(60);

    /// <summary>Максимальный возраст хранения исторической записи.</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Инициализирует новый реестр жизненного цикла с автоматически сгенерированной уникальной эпохой монитора.
    /// </summary>
    public ProcessLifecycleRegistry() : this(Guid.NewGuid())
    {
    }

    /// <summary>
    /// Инициализирует новый реестр жизненного цикла с уникальной эпохой монитора.
    /// </summary>
    /// <param name="monitorEpoch">Уникальная эпоха работы реестра.</param>
    public ProcessLifecycleRegistry(Guid monitorEpoch)
    {
        if (monitorEpoch == Guid.Empty)
        {
            throw new ArgumentException("Эпоха монитора не может быть пустой.", nameof(monitorEpoch));
        }

        _monitorEpoch = monitorEpoch;
    }

    /// <summary>
    /// Эпоха текущего экземпляра реестра.
    /// </summary>
    public Guid MonitorEpoch => _monitorEpoch;

    /// <summary>
    /// Текущее число активных процессов в памяти.
    /// </summary>
    public int ActiveProcessCount
    {
        get
        {
            lock (_stateLock)
            {
                return _liveProcesses.Count;
            }
        }
    }

    /// <summary>
    /// Общее количество зарегистрированных стартов процессов за все время работы.
    /// </summary>
    public long TotalStartedCount => Interlocked.Read(ref _totalStartedCount);

    /// <summary>
    /// Общее количество зарегистрированных завершений процессов за все время работы.
    /// </summary>
    public long TotalStoppedCount => Interlocked.Read(ref _totalStoppedCount);

    /// <summary>
    /// Обрабатывает событие жизненного цикла процессов от источника ETW.
    /// Метод потокобезопасен и выполняет идемпотентное слияние состояния.
    /// </summary>
    /// <param name="e">Событие жизненного цикла процесса.</param>
    /// <returns>Экземпляр записи процесса, созданный или обновленный событием (null при игнорировании/удалении).</returns>
    public ProcessInstanceRecord? ApplyEvent(ProcessLifecycleEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        lock (_stateLock)
        {
            PruneExpiredRecordsUnderLock(e.OccurredUtc);

            return e.Kind switch
            {
                LifecycleEventKind.Started => HandleStartUnderLock(e),
                LifecycleEventKind.Stopped => HandleStopUnderLock(e),
                LifecycleEventKind.Rundown => HandleRundownUnderLock(e),
                _ => null
            };
        }
    }

    /// <summary>
    /// Применяет начальный или контрольный снимок процессов (Baseline / Reconciliation).
    /// </summary>
    /// <param name="snapshotProcesses">Коллекция процессов из нативного системного снимка.</param>
    /// <param name="snapshotTimeUtc">Время получения снимка в UTC.</param>
    public void ApplyBaseline(
        IEnumerable<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)> snapshotProcesses,
        DateTime snapshotTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshotProcesses);

        lock (_stateLock)
        {
            PruneExpiredRecordsUnderLock(snapshotTimeUtc);

            var activePids = new HashSet<int>();

            foreach (var item in snapshotProcesses)
            {
                if (item.Pid <= 0) continue;
                activePids.Add(item.Pid);

                if (_liveProcesses.TryGetValue(item.Pid, out var existing))
                {
                    // Если процесс уже зарегистрирован и время создания совпадает — подтверждаем
                    if (Math.Abs((existing.CreationTimeUtc - item.CreationTimeUtc).TotalSeconds) < 2.0)
                    {
                        continue;
                    }

                    // Обнаружена смена экземпляра в одном PID во время сверки!
                    RetireProcessUnderLock(existing, snapshotTimeUtc, "ReconciliationMismatch");
                }

                // Добавляем новый подтвержденный процесс из baseline
                var generation = Interlocked.Increment(ref _generationCounter);
                var instanceId = new ProcessInstanceId(_monitorEpoch, item.Pid, generation);
                var record = new ProcessInstanceRecord(
                    instanceId,
                    item.ParentPid,
                    0,
                    item.CreationTimeUtc,
                    0,
                    item.ImageName,
                    string.Empty,
                    IsFromBaseline: true,
                    ProcessMetadataStatus.Unknown);

                if (_liveProcesses.Count < MaxLiveCapacity)
                {
                    _liveProcesses[item.Pid] = record;
                    Interlocked.Increment(ref _totalStartedCount);
                }
            }
        }
    }

    /// <summary>
    /// Обновляет статус и разрешенные метаданные процесса (путь, подпись).
    /// Применяется только при строгом совпадении InstanceId, исключая запоздалые результаты для переиспользованных PID.
    /// </summary>
    /// <param name="instanceId">Строгий идентификатор целевого экземпляра процесса.</param>
    /// <param name="status">Новый статус метаданных.</param>
    /// <param name="resolvedPath">Разрешенный полный путь к образу (если доступен).</param>
    /// <param name="signer">Субъект подписи Authenticode (если проверен).</param>
    /// <returns>Истина, если метаданные успешно применены к живому экземпляру.</returns>
    public bool TryUpdateMetadata(
        ProcessInstanceId instanceId,
        ProcessMetadataStatus status,
        string? resolvedPath = null,
        string? signer = null)
    {
        lock (_stateLock)
        {
            if (!_liveProcesses.TryGetValue(instanceId.ProcessId, out var existing))
            {
                return false;
            }

            if (existing.InstanceId != instanceId)
            {
                // Поколение не совпадает — запоздалый ответ для старого процесса
                return false;
            }

            var updated = existing with
            {
                MetadataStatus = status,
                ResolvedExecutablePath = resolvedPath ?? existing.ResolvedExecutablePath,
                Signer = signer ?? existing.Signer
            };

            _liveProcesses[instanceId.ProcessId] = updated;
            return true;
        }
    }

    /// <summary>
    /// Возвращает неизменяемый снимок всех текущих живых процессов в системе.
    /// Вызов не удерживает блокировку реестра во время дальнейшей обработки снимка клиентом.
    /// </summary>
    /// <returns>Список активных экземпляров процессов.</returns>
    public IReadOnlyList<ProcessInstanceRecord> GetLiveSnapshot()
    {
        lock (_stateLock)
        {
            return _liveProcesses.Values.ToList();
        }
    }

    /// <summary>
    /// Возвращает исторический срез завершенных процессов.
    /// </summary>
    /// <param name="sinceUtc">Опциональный фильтр: только процессы, завершившиеся после указанного времени.</param>
    /// <returns>Список исторических записей выхода.</returns>
    public IReadOnlyList<ProcessExitRecord> GetHistorySnapshot(DateTime? sinceUtc = null)
    {
        lock (_stateLock)
        {
            if (sinceUtc == null)
            {
                return _history.ToList();
            }

            return _history.Where(h => h.ExitTimeUtc >= sinceUtc.Value).ToList();
        }
    }

    #region Private Handlers under Lock

    private ProcessInstanceRecord HandleStartUnderLock(ProcessLifecycleEvent e)
    {
        if (_liveProcesses.TryGetValue(e.ProcessId, out var existing))
        {
            // Проверяем, не дубликат ли это того же самого процесса (например, повторный callback)
            if (existing.KernelKey == e.KernelKey && e.KernelKey != 0 &&
                Math.Abs((existing.CreationTimeUtc - e.OccurredUtc).TotalSeconds) < 2.0)
            {
                return existing;
            }

            // PID REUSE: Предыдущий процесс в этом PID не прислал Stop или завершился без события!
            RetireProcessUnderLock(existing, e.OccurredUtc, "PidReusedWithoutStop");
        }

        var generation = Interlocked.Increment(ref _generationCounter);
        var instanceId = new ProcessInstanceId(_monitorEpoch, e.ProcessId, generation);
        var record = new ProcessInstanceRecord(
            instanceId,
            e.ParentProcessId,
            e.KernelKey,
            e.OccurredUtc,
            e.OccurredQpc,
            e.ImageName,
            e.CommandLine,
            IsFromBaseline: false,
            ProcessMetadataStatus.Unknown);

        if (_liveProcesses.Count < MaxLiveCapacity)
        {
            _liveProcesses[e.ProcessId] = record;
            Interlocked.Increment(ref _totalStartedCount);
        }

        return record;
    }

    private ProcessInstanceRecord? HandleStopUnderLock(ProcessLifecycleEvent e)
    {
        if (!_liveProcesses.TryGetValue(e.ProcessId, out var existing))
        {
            // Процесс уже завершен или не был зарегистрирован в live-наборе
            return null;
        }

        // Проверяем совпадение KernelKey (если он был задан) для исключения удаления чужого поколения
        if (e.KernelKey != 0 && existing.KernelKey != 0 && e.KernelKey != existing.KernelKey)
        {
            // Запоздалый Stop от старого процесса — не удаляем активный новый процесс!
            return null;
        }

        RetireProcessUnderLock(existing, e.OccurredUtc, "NormalExit");
        return null;
    }

    private ProcessInstanceRecord HandleRundownUnderLock(ProcessLifecycleEvent e)
    {
        if (_liveProcesses.TryGetValue(e.ProcessId, out var existing))
        {
            return existing;
        }

        // Rundown сообщает о существующем процессе: создаем подтвержденный baseline-экземпляр
        var generation = Interlocked.Increment(ref _generationCounter);
        var instanceId = new ProcessInstanceId(_monitorEpoch, e.ProcessId, generation);
        var record = new ProcessInstanceRecord(
            instanceId,
            e.ParentProcessId,
            e.KernelKey,
            e.OccurredUtc,
            e.OccurredQpc,
            e.ImageName,
            e.CommandLine,
            IsFromBaseline: true,
            ProcessMetadataStatus.Unknown);

        if (_liveProcesses.Count < MaxLiveCapacity)
        {
            _liveProcesses[e.ProcessId] = record;
            Interlocked.Increment(ref _totalStartedCount);
        }

        return record;
    }

    private void RetireProcessUnderLock(ProcessInstanceRecord process, DateTime exitTimeUtc, string reason)
    {
        _liveProcesses.Remove(process.ProcessId);
        Interlocked.Increment(ref _totalStoppedCount);

        // Добавляем надгробие (Tombstone) для фильтрации запаздывающих событий
        if (!_tombstones.TryGetValue(process.ProcessId, out var list))
        {
            list = new List<(long Generation, DateTime ExitedUtc)>();
            _tombstones[process.ProcessId] = list;
        }
        list.Add((process.Generation, exitTimeUtc));

        // Добавляем в ограниченный буфер истории
        var exitRecord = new ProcessExitRecord(
            process.InstanceId,
            process.ParentProcessId,
            process.CreationTimeUtc,
            exitTimeUtc,
            process.ImageName,
            reason);

        _history.AddLast(exitRecord);
        while (_history.Count > MaxHistoryCapacity)
        {
            _history.RemoveFirst();
        }
    }

    private void PruneExpiredRecordsUnderLock(DateTime nowUtc)
    {
        // Очистка устаревших tombstone
        var expiredTombstoneThreshold = nowUtc - TombstoneTtl;
        var pidsToRemove = new List<int>();

        foreach (var (pid, list) in _tombstones)
        {
            list.RemoveAll(t => t.ExitedUtc < expiredTombstoneThreshold);
            if (list.Count == 0)
            {
                pidsToRemove.Add(pid);
            }
        }

        foreach (var pid in pidsToRemove)
        {
            _tombstones.Remove(pid);
        }

        // Очистка истории старше retention
        var expiredHistoryThreshold = nowUtc - HistoryRetention;
        while (_history.First != null && _history.First.Value.ExitTimeUtc < expiredHistoryThreshold)
        {
            _history.RemoveFirst();
        }
    }

    #endregion
}

