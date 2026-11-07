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
    string? Signer = null,
    bool HasAuthoritativeCreationTime = false,
    DateTime BaselineObservedUntilUtc = default)
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
    private readonly Dictionary<int, List<(long Generation, DateTime ExitedUtc, ulong KernelKey)>> _tombstones = new(1024);
    /// <summary>Ограничивает число tombstone независимо от частоты повторного использования PID.</summary>
    private readonly Queue<(int Pid, long Generation)> _tombstoneOrder = new();
    private readonly LinkedList<ProcessExitRecord> _history = new();
    private long _generationCounter;
    private long _totalStartedCount;
    private long _totalStoppedCount;
    private long _revision;
    /// <summary>Флаг неполной live-проекции; снимается только успешной полной сверкой без переполнения.</summary>
    private int _capacityExceeded;

    /// <summary>Максимальная емкость кэша живых процессов для предотвращения неконтролируемого роста памяти.</summary>
    public const int MaxLiveCapacity = 32768;

    /// <summary>Максимальный размер исторического буфера завершенных процессов.</summary>
    public const int MaxHistoryCapacity = 10000;

    /// <summary>Жёсткий предел tombstone; обычное вытеснение не означает потери ETW.</summary>
    public const int MaxTombstoneCapacity = 10000;

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

    /// <summary>Версия проекции для пропуска неизменившегося live-набора в executor.</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>При переполнении реестр не разрешает отключать независимый legacy-проход защиты.</summary>
    public bool IsCapacityExceeded => Volatile.Read(ref _capacityExceeded) != 0;

    /// <summary>Коалесцируемый сигнал изменения реестра; вызывается после освобождения его блокировки.</summary>
    public event Action? Changed;

    /// <summary>
    /// Обрабатывает событие жизненного цикла процессов от источника ETW.
    /// Метод потокобезопасен и выполняет идемпотентное слияние состояния.
    /// </summary>
    /// <param name="e">Событие жизненного цикла процесса.</param>
    /// <returns>Экземпляр записи процесса, созданный или обновленный событием (null при игнорировании/удалении).</returns>
    public ProcessInstanceRecord? ApplyEvent(ProcessLifecycleEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        ProcessInstanceRecord? result;
        lock (_stateLock)
        {
            PruneExpiredRecordsUnderLock(e.OccurredUtc);

            result = e.Kind switch
            {
                LifecycleEventKind.Started => HandleStartUnderLock(e),
                LifecycleEventKind.Stopped => HandleStopUnderLock(e),
                LifecycleEventKind.Rundown => HandleRundownUnderLock(e),
                _ => null
            };
        }
        NotifyChanged();
        return result;
    }

    /// <summary>
    /// Применяет начальный или контрольный снимок процессов (Baseline / Reconciliation).
    /// </summary>
    /// <param name="snapshotProcesses">Коллекция процессов из нативного системного снимка.</param>
    /// <param name="snapshotTimeUtc">Время получения снимка в UTC.</param>
    /// <param name="snapshotCompletedUtc">Верхняя временная граница накопленных во время снимка Start-событий.</param>
    public void ApplyBaseline(
        IEnumerable<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)> snapshotProcesses,
        DateTime snapshotTimeUtc,
        DateTime? snapshotCompletedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotProcesses);
        // Ошибка перечисления источника не должна оставить частично изменённый live-набор.
        var processes = snapshotProcesses.ToArray();

        lock (_stateLock)
        {
            PruneExpiredRecordsUnderLock(snapshotTimeUtc);

            var activePids = new HashSet<int>();
            var capacityExceeded = false;

            foreach (var item in processes)
            {
                if (item.Pid <= 0) continue;
                activePids.Add(item.Pid);

                // Stop, применённый после начала старого снимка, остаётся авторитетнее
                // этого снимка. Новый процесс после Stop имеет более поздний FILETIME.
                if (_tombstones.TryGetValue(item.Pid, out var retired) &&
                    retired.Any(t => t.ExitedUtc >= snapshotTimeUtc && item.CreationTimeUtc <= t.ExitedUtc))
                    continue;

                if (_liveProcesses.TryGetValue(item.Pid, out var existing))
                {
                    // Снимок начат до события нового поколения; более позднее событие имеет приоритет.
                    if (existing.CreationTimeUtc > snapshotTimeUtc) continue;
                    // Если процесс уже зарегистрирован и время создания совпадает — подтверждаем
                    // FILETIME сравнивается точно: допуск по времени скрывает быстрое повторное использование PID.
                    // Событие Start не является авторитетным FILETIME; снимок уточняет его без смены поколения,
                    // только если он не описывает более позднее создание процесса.
                    if (item.CreationTimeUtc == DateTime.MinValue || existing.CreationTimeUtc == item.CreationTimeUtc ||
                        (!existing.HasAuthoritativeCreationTime &&
                         (existing.CreationTimeUtc == DateTime.MinValue || existing.CreationTimeUtc >= item.CreationTimeUtc)))
                    {
                        _liveProcesses[item.Pid] = existing with
                        {
                            CreationTimeUtc = item.CreationTimeUtc == DateTime.MinValue ? existing.CreationTimeUtc : item.CreationTimeUtc,
                            ParentProcessId = item.ParentPid,
                            HasAuthoritativeCreationTime = existing.HasAuthoritativeCreationTime || item.CreationTimeUtc != DateTime.MinValue,
                            BaselineObservedUntilUtc = snapshotCompletedUtc ?? snapshotTimeUtc
                        };
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
                    ProcessMetadataStatus.Unknown,
                    HasAuthoritativeCreationTime: item.CreationTimeUtc != DateTime.MinValue,
                    BaselineObservedUntilUtc: snapshotCompletedUtc ?? snapshotTimeUtc);

                if (_liveProcesses.Count < MaxLiveCapacity)
                {
                    _liveProcesses[item.Pid] = record;
                    Interlocked.Increment(ref _totalStartedCount);
                }
                else capacityExceeded = true;
            }

            // Полный системный снимок подтверждает также отсутствие процессов. Записи более поздних
            // событий сохраняются: они могли появиться после начала захвата снимка.
            foreach (var missing in _liveProcesses.Values.Where(p =>
                         !activePids.Contains(p.ProcessId) && p.CreationTimeUtc <= snapshotTimeUtc).ToList())
            {
                RetireProcessUnderLock(missing, snapshotTimeUtc, "ReconciliationMissing");
            }
            Volatile.Write(ref _capacityExceeded, capacityExceeded ? 1 : 0);
        }
        NotifyChanged();
    }

    /// <summary>
    /// Обновляет статус и разрешенные метаданные процесса (путь, подпись).
    /// Применяется только при строгом совпадении InstanceId, исключая запоздалые результаты для переиспользованных PID.
    /// </summary>
    /// <param name="instanceId">Строгий идентификатор целевого экземпляра процесса.</param>
    /// <param name="status">Новый статус метаданных.</param>
    /// <param name="resolvedPath">Разрешенный полный путь к образу (если доступен).</param>
    /// <param name="signer">Субъект подписи Authenticode (если проверен).</param>
    /// <param name="verifiedCreationTimeUtc">Время с удерживаемого handle; при наличии проверяется до изменения metadata/identity.</param>
    /// <returns>Истина, если метаданные успешно применены к живому экземпляру.</returns>
    public bool TryUpdateMetadata(
        ProcessInstanceId instanceId,
        ProcessMetadataStatus status,
        string? resolvedPath = null,
        string? signer = null,
        DateTime? verifiedCreationTimeUtc = null)
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

            if (verifiedCreationTimeUtc.HasValue && (verifiedCreationTimeUtc.Value == DateTime.MinValue ||
                (existing.HasAuthoritativeCreationTime && existing.CreationTimeUtc != verifiedCreationTimeUtc.Value) ||
                (!existing.HasAuthoritativeCreationTime && existing.CreationTimeUtc != DateTime.MinValue &&
                 verifiedCreationTimeUtc.Value > existing.CreationTimeUtc)))
                return false;

            var updated = existing with
            {
                MetadataStatus = status > existing.MetadataStatus ? status : existing.MetadataStatus,
                ResolvedExecutablePath = resolvedPath ?? existing.ResolvedExecutablePath,
                Signer = signer ?? existing.Signer,
                CreationTimeUtc = verifiedCreationTimeUtc ?? existing.CreationTimeUtc,
                HasAuthoritativeCreationTime = verifiedCreationTimeUtc.HasValue || existing.HasAuthoritativeCreationTime
            };

            if (updated == existing) return true;
            _liveProcesses[instanceId.ProcessId] = updated;
        }
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Принимает отложенную подпись только для уже авторитетно подтверждённого FILETIME текущего поколения.
    /// Старый callback не может присвоить метаданные новому PID или неизвестному rundown-экземпляру.
    /// </summary>
    /// <param name="update">Собственные неизменяемые поля завершённого обогащения.</param>
    /// <returns>Истина для принятого результата либо его точного повтора.</returns>
    public bool TryUpdateVerifiedMetadata(ProcessMetadataUpdate update)
    {
        var current = GetLiveProcess(update.ProcessId);
        return current is { HasAuthoritativeCreationTime: true } && current.CreationTimeUtc == update.CreationTimeUtc &&
            TryUpdateMetadata(current.InstanceId, update.Status, update.ExecutablePath, update.Signer, update.CreationTimeUtc);
    }

    /// <summary>Изолирует ошибки подписчиков от однопоточного reducer и не передаёт payload в журнал.</summary>
    private void NotifyChanged()
    {
        Interlocked.Increment(ref _revision);
        var handlers = Changed;
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Ошибка подписчика изменения ETW registry"); }
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

    /// <summary>Возвращает текущий неизменяемый экземпляр PID для проверки pending-действия за O(1).</summary>
    /// <param name="processId">PID целевого процесса.</param>
    /// <returns>Текущая запись либо null, если процесс больше не зарегистрирован.</returns>
    public ProcessInstanceRecord? GetLiveProcess(int processId)
    {
        lock (_stateLock) return _liveProcesses.GetValueOrDefault(processId);
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

    /// <summary>Отбрасывает запоздалые старты и повторные события уже завершённого экземпляра.</summary>
    /// <param name="e">Скопированное событие начала процесса.</param>
    /// <returns>Живой экземпляр либо null для устаревшего события.</returns>
    private ProcessInstanceRecord? HandleStartUnderLock(ProcessLifecycleEvent e)
    {
        if (_tombstones.TryGetValue(e.ProcessId, out var retired) &&
            retired.Any(t => e.OccurredUtc <= t.ExitedUtc &&
                             (e.KernelKey == 0 || t.KernelKey == e.KernelKey)))
        {
            return null;
        }

        if (_liveProcesses.TryGetValue(e.ProcessId, out var existing))
        {
            if (e.OccurredUtc < existing.CreationTimeUtc)
            {
                return null;
            }
            // Start, накопленный во время захвата baseline, уточняет kernel key уже учтённого
            // экземпляра. Более поздний Start находится за watermark и означает новое поколение.
            if (existing.HasAuthoritativeCreationTime && existing.KernelKey == 0 &&
                e.OccurredUtc <= existing.BaselineObservedUntilUtc)
            {
                var confirmed = existing with
                {
                    KernelKey = e.KernelKey,
                    CreationQpc = e.OccurredQpc,
                    CommandLine = e.CommandLine
                };
                _liveProcesses[e.ProcessId] = confirmed;
                return confirmed;
            }
            // Проверяем, не дубликат ли это того же самого процесса (например, повторный callback)
            if (existing.KernelKey == e.KernelKey && e.KernelKey != 0 &&
                ((existing.CreationQpc != 0 && existing.CreationQpc == e.OccurredQpc) ||
                 existing.CreationTimeUtc == e.OccurredUtc || existing.CreationTimeUtc == DateTime.MinValue))
            {
                var duplicate = existing.CreationTimeUtc == DateTime.MinValue
                    ? existing with { CreationTimeUtc = e.OccurredUtc, CreationQpc = e.OccurredQpc } : existing;
                _liveProcesses[e.ProcessId] = duplicate;
                return duplicate;
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
            return record;
        }
        Volatile.Write(ref _capacityExceeded, 1);
        return null;
    }

    private ProcessInstanceRecord? HandleStopUnderLock(ProcessLifecycleEvent e)
    {
        if (!_liveProcesses.TryGetValue(e.ProcessId, out var existing))
        {
            // Процесс уже завершен или не был зарегистрирован в live-наборе
            return null;
        }

        // При отсутствующем KernelKey время всё равно защищает новый экземпляр от старого Stop.
        if (e.OccurredUtc < existing.CreationTimeUtc ||
            (e.OccurredQpc != 0 && existing.CreationQpc != 0 && e.OccurredQpc < existing.CreationQpc))
        {
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

    /// <summary>Сливает перечисление живого процесса, не воскрешая уже учтённый более поздний Stop.</summary>
    /// <param name="e">Событие перечисления; его время не является временем рождения.</param>
    /// <returns>Живая запись либо null для устаревшего rundown или неполной проекции.</returns>
    private ProcessInstanceRecord? HandleRundownUnderLock(ProcessLifecycleEvent e)
    {
        if (_liveProcesses.TryGetValue(e.ProcessId, out var existing))
        {
            return existing;
        }
        if (_tombstones.TryGetValue(e.ProcessId, out var retired) && retired.Any(t =>
            t.ExitedUtc >= e.OccurredUtc && (e.KernelKey == 0 || t.KernelKey == 0 || e.KernelKey == t.KernelKey)))
            return null;

        // Rundown сообщает о существующем процессе: создаем подтвержденный baseline-экземпляр
        var generation = Interlocked.Increment(ref _generationCounter);
        var instanceId = new ProcessInstanceId(_monitorEpoch, e.ProcessId, generation);
        var record = new ProcessInstanceRecord(
            instanceId,
            e.ParentProcessId,
            e.KernelKey,
            DateTime.MinValue,
            0,
            e.ImageName,
            e.CommandLine,
            IsFromBaseline: true,
            ProcessMetadataStatus.Unknown);

        if (_liveProcesses.Count < MaxLiveCapacity)
        {
            _liveProcesses[e.ProcessId] = record;
            Interlocked.Increment(ref _totalStartedCount);
            return record;
        }
        Volatile.Write(ref _capacityExceeded, 1);
        return null;
    }

    private void RetireProcessUnderLock(ProcessInstanceRecord process, DateTime exitTimeUtc, string reason)
    {
        _liveProcesses.Remove(process.ProcessId);
        Interlocked.Increment(ref _totalStoppedCount);

        // Добавляем надгробие (Tombstone) для фильтрации запаздывающих событий
        if (!_tombstones.TryGetValue(process.ProcessId, out var list))
        {
            list = new List<(long Generation, DateTime ExitedUtc, ulong KernelKey)>();
            _tombstones[process.ProcessId] = list;
        }
        list.Add((process.Generation, exitTimeUtc, process.KernelKey));
        _tombstoneOrder.Enqueue((process.ProcessId, process.Generation));
        while (_tombstoneOrder.Count > MaxTombstoneCapacity)
        {
            var expired = _tombstoneOrder.Dequeue();
            if (_tombstones.TryGetValue(expired.Pid, out var generations))
            {
                generations.RemoveAll(t => t.Generation == expired.Generation);
                if (generations.Count == 0) _tombstones.Remove(expired.Pid);
            }
        }

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

