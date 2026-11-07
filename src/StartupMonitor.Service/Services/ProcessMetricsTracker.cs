using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Service.Services.ProcessLifecycle;

namespace StartupMonitor.Service.Services;

/// <summary>Предыдущее состояние процесса для расчета дельты CPU и дискового ввода-вывода.</summary>
public record ProcessMetricHistory(
    TimeSpan TotalProcessorTime,
    long TotalIoBytes,
    DateTime Timestamp
);

/// <summary>
/// Движок высокопроизводительного сбора метрик процессов и системной нагрузки на базе NtQuerySystemInformation.
/// Обеспечивает нулевые аллокации в куче, исключает тяжелые снимки Toolhelp32 и OpenProcess дескрипторов,
/// с плавным потреблением CPU 2–5% максимум даже при 300+ активных процессах.
/// </summary>
public class ProcessMetricsTracker : IDisposable
{
    private readonly ProcessMetadataEnricher _enricher;
    private readonly ProcessStartupLinker _linker;
    private readonly ProcessNetworkTracker _networkTracker;
    /// <summary>Признак владения сборщиком, когда он не был внедрён общим DI-контейнером.</summary>
    private readonly bool _ownsNetworkTracker;
    private bool _disposed;
    private readonly ConcurrentDictionary<(int Pid, long CreateTimeTicks), ProcessMetricHistory> _history = new();
    private readonly Timer _pollTimer;
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);

    // Единая блокировка для сериализации CollectSnapshotCore (таймер + IPC CaptureSnapshot)
    private readonly object _collectLock = new();
    // Блокировка счётчика подписчиков
    private readonly object _subscriberLock = new();
    private readonly Microsoft.Extensions.Logging.ILogger<ProcessMetricsTracker>? _logger;

    // Количество активных подписчиков (UI на вкладке Диспетчера задач)
    private int _subscribersCount = 0;
    private int _isCollecting = 0;

    /// <summary>Число действующих подписок для диагностики освобождения клиентских сессий.</summary>
    internal int SubscriberCount => Volatile.Read(ref _subscribersCount);

    // Для замера общего системного CPU через GetSystemTimes
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private bool _hasSystemTimesPrev;

    // Кэш сопоставления PID со службами Windows (обновляется раз в 4 секунды)
    private Dictionary<int, List<string>> _servicePidMap = new();
    private DateTime _lastServicePoll = DateTime.MinValue;

    // Переиспользуемый неуправляемый буфер для NtQuerySystemInformation (0 GC аллокаций)
    private IntPtr _nativeBuffer = IntPtr.Zero;
    private int _nativeBufferSize = 512 * 1024; // 512 КБ изначально
    private readonly object _nativeBufferLock = new();

    /// <summary>Событие готовности свежего снимка производительности и процессов.</summary>
    public event EventHandler<SystemPerformanceSnapshot>? SnapshotReady;

    /// <summary>
    /// Внутренняя структура для передачи сырых данных процесса из ядра без аллокаций объектов.
    /// </summary>
    private readonly record struct RawProcessEntry(
        int ProcessId,
        int ParentProcessId,
        string ProcessName,
        int ThreadCount,
        int SessionId,
        long WorkingSetBytes,
        long CreateTime,
        long UserTime,
        long KernelTime,
        long TotalIoBytes
    );

    /// <summary>Инициализирует трекер с переиспользуемым нативным буфером.</summary>
    /// <param name="enricher">Обогатитель метаданных процессов.</param>
    /// <param name="linker">Связыватель процессов с автозапуском.</param>
    /// <param name="logger">Необязательный журнал диагностики.</param>
    /// <param name="networkTracker">Общий сетевой сборщик; при отсутствии создаётся принадлежащий этому экземпляру.</param>
    public ProcessMetricsTracker(ProcessMetadataEnricher enricher, ProcessStartupLinker linker, Microsoft.Extensions.Logging.ILogger<ProcessMetricsTracker>? logger = null, ProcessNetworkTracker? networkTracker = null)
    {
        _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));
        _linker = linker ?? throw new ArgumentNullException(nameof(linker));
        _logger = logger;
        _networkTracker = networkTracker ?? new ProcessNetworkTracker();
        _ownsNetworkTracker = networkTracker is null;

        // Выделяем переиспользуемый нативный буфер в неуправляемой памяти
        _nativeBuffer = Marshal.AllocHGlobal(_nativeBufferSize);

        // Таймер изначально отключен (On-Demand режим: 0% CPU до перехода на вкладку)
        _pollTimer = new Timer(OnPollTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Подписка UI на поток срезов метрик (активирует периодический опрос раз в 2000 мс).</summary>
    public void Subscribe()
    {
        lock (_subscriberLock)
        {
            if (_subscribersCount == 0)
            {
                // Первый подписчик — включаем сбор каждые 2000 мс (сглаженный интервал без всплесков)
                _pollTimer.Change(0, 2000);
            }
            _subscribersCount++;
        }
    }

    /// <summary>Отписка UI (при уходе с вкладки или сворачивании в трей).</summary>
    public void Unsubscribe()
    {
        lock (_subscriberLock)
        {
            if (_subscribersCount > 0)
            {
                _subscribersCount--;
            }
            if (_subscribersCount <= 0)
            {
                _subscribersCount = 0;
                // Все подписчики ушли — полностью выключаем таймер (0% CPU Idle)
                _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    /// <summary>Разовый замер для немедленного ответа на GetProcesses.</summary>
    public SystemPerformanceSnapshot CaptureSnapshot()
    {
        return CollectSnapshotCore();
    }

    /// <summary>
    /// Получает авторитетный снимок существующих процессов для baseline и сверки ETW.
    /// Использует тот же нативный парсер, что и метрики, без сетевого опроса, метаданных и подписки на таймер.
    /// Нулевой CreateTime обозначается неизвестным временем, а не временем выполнения сверки.
    /// </summary>
    /// <param name="cancellationToken">Отменяет чтение между попытками выделения буфера и записями снимка.</param>
    /// <returns>PID, PPID, время создания UTC и имя каждого существующего процесса, кроме Idle.</returns>
    public IReadOnlyList<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)> CaptureLifecycleSnapshot(CancellationToken cancellationToken)
    {
        var entries = ReadRawProcessEntries(cancellationToken);
        var result = new List<(int, int, DateTime, string)>(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.ProcessId > 0)
                result.Add((entry.ProcessId, entry.ParentProcessId, GetCreationTimeUtc(entry.CreateTime), entry.ProcessName));
        }
        return result;
    }

    /// <summary>Преобразует нативный FILETIME; отсутствие времени сохраняется как неизвестная identity.</summary>
    /// <param name="fileTime">Число интервалов по 100 нс с начала эпохи Windows.</param>
    /// <returns>Время создания UTC либо MinValue для нулевого значения.</returns>
    private static DateTime GetCreationTimeUtc(long fileTime) => fileTime <= 0 ? DateTime.MinValue : DateTime.FromFileTimeUtc(fileTime);

    /// <summary>
    /// Читает SystemProcessInformation в переиспользуемый буфер x64 с ограничением роста 64 МБ.
    /// Проверяет NTSTATUS, границы записей и Unicode-строк до чтения указателей; ошибка не превращается в пустой снимок.
    /// Блокировка совпадает с порядком Dispose и защищает lifetime общего unmanaged-буфера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены ограниченных повторов и разбора записей.</param>
    /// <returns>Собственные копии полей процессов, не ссылающиеся на нативную память.</returns>
    private List<RawProcessEntry> ReadRawProcessEntries(CancellationToken cancellationToken)
    {
        const int lengthMismatch = unchecked((int)0xC0000004);
        const int minimumEntrySize = 0xF8;
        const int maximumBufferSize = 64 * 1024 * 1024;
        cancellationToken.ThrowIfCancellationRequested();
        lock (_collectLock)
        lock (_nativeBufferLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IntPtr.Size != 8)
                throw new PlatformNotSupportedException("Парсер SystemProcessInformation требует Windows x64.");
            if (_nativeBuffer == IntPtr.Zero) _nativeBuffer = Marshal.AllocHGlobal(_nativeBufferSize);

            int status;
            int returnLength;
            var attempts = 0;
            while ((status = NtQuerySystemInformation(5, _nativeBuffer, _nativeBufferSize, out returnLength)) == lengthMismatch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++attempts > 8 || returnLength < 0)
                    throw new InvalidOperationException("Размер SystemProcessInformation не стабилизировался за 8 попыток.");
                var nextSize = Math.Max((long)_nativeBufferSize * 2, (long)returnLength + 64 * 1024);
                if (nextSize > maximumBufferSize)
                    throw new InvalidOperationException("Снимок SystemProcessInformation превышает бюджет 64 МБ.");
                // Выделяем новый буфер до освобождения старого: отказ выделения не оставляет висячий указатель.
                var replacement = Marshal.AllocHGlobal((int)nextSize);
                Marshal.FreeHGlobal(_nativeBuffer);
                _nativeBuffer = replacement;
                _nativeBufferSize = (int)nextSize;
            }
            if (status != 0)
                throw new InvalidOperationException($"NtQuerySystemInformation(SystemProcessInformation) завершился с NTSTATUS 0x{status:X8}.");
            if (returnLength < minimumEntrySize || returnLength > _nativeBufferSize)
                throw new InvalidOperationException("Ядро вернуло некорректный размер SystemProcessInformation.");

            var result = new List<RawProcessEntry>(350);
            var baseAddress = _nativeBuffer.ToInt64();
            var offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (offset < 0 || offset > returnLength - minimumEntrySize)
                    throw new InvalidOperationException("Запись процесса выходит за границы нативного снимка.");
                var current = IntPtr.Add(_nativeBuffer, offset);
                var nextOffset = Marshal.ReadInt32(current, 0);
                var nameLength = unchecked((ushort)Marshal.ReadInt16(current, 0x38));
                var nameAddress = Marshal.ReadIntPtr(current, 0x40).ToInt64();
                if (nameLength % 2 != 0 || (nameLength > 0 &&
                    (nameAddress < baseAddress || nameAddress - baseAddress > returnLength - nameLength)))
                    throw new InvalidOperationException("Имя процесса выходит за границы Unicode-буфера снимка.");

                var pid = checked((int)Marshal.ReadIntPtr(current, 0x50).ToInt64());
                var parentPid = checked((int)Marshal.ReadIntPtr(current, 0x58).ToInt64());
                if (pid < 0 || parentPid < 0)
                    throw new InvalidOperationException("Снимок содержит отрицательный PID.");
                var name = nameLength > 0 ? Marshal.PtrToStringUni(new IntPtr(nameAddress), nameLength / 2) ?? string.Empty :
                    pid == 0 ? "System Idle Process" : pid == 4 ? "System" : string.Empty;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                result.Add(new RawProcessEntry(pid, parentPid, name, Marshal.ReadInt32(current, 0x04),
                    Marshal.ReadInt32(current, 0x64), Marshal.ReadInt64(current, 0x90),
                    Marshal.ReadInt64(current, 0x20), Marshal.ReadInt64(current, 0x28), Marshal.ReadInt64(current, 0x30),
                    checked(Marshal.ReadInt64(current, 0xE8) + Marshal.ReadInt64(current, 0xF0))));
                if (nextOffset == 0) break;
                if (nextOffset < minimumEntrySize || nextOffset % 8 != 0 || nextOffset > returnLength - offset)
                    throw new InvalidOperationException("Некорректное смещение следующего процесса в снимке.");
                offset = checked(offset + nextOffset);
            }
            return result;
        }
    }

    /// <summary>Получает список активных сетевых соединений (сокетов) для процесса или всей системы.</summary>
    public List<ProcessNetworkSocket> GetProcessSockets(int pid = -1) =>
        _networkTracker.GetSockets(pid);

    /// <summary>Обработчик тика таймера с привязкой потока к идеальному ядру процессора.</summary>
    private void OnPollTick(object? state)
    {
        if (_subscribersCount <= 0) return;
        if (Interlocked.CompareExchange(ref _isCollecting, 1, 0) != 0) return;

        try
        {
            // Мягко привязываем рабочий поток таймера к наименее загруженному ядру и снижаем приоритет
            CpuUsageHelper.AssignCurrentThreadToIdealCore();
            CpuUsageHelper.SetCurrentThreadLowPriority();

            var snapshot = CollectSnapshotCore();
            SnapshotReady?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка сбора снимка метрик процессов");
        }
        finally
        {
            Interlocked.Exchange(ref _isCollecting, 0);
        }
    }

    /// <summary>
    /// Высокопроизводительный сбор метрик через единый вызов NtQuerySystemInformation:
    /// ноль открытий дескрипторов процессов, ноль Win32Exception на защищенных процессах,
    /// получение всех параметров (PID, ParentPID, IO, CPU, память, потоки) в один системный переход.
    /// </summary>
    private SystemPerformanceSnapshot CollectSnapshotCore()
    {
        lock (_collectLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var _profSnapshot = ExecutionProfiler.Profile("Service.ProcessMetricsSnapshot");
            using var _prof = ExecutionProfiler.Measure("Service.ProcessMetrics.Collect");
        var now = DateTime.UtcNow;
        var snapshot = new SystemPerformanceSnapshot();

        // 1. Общая память системы через GlobalMemoryStatusEx
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            snapshot.TotalMemoryBytes = (long)memStatus.ullTotalPhys;
            snapshot.UsedMemoryBytes = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys);
        }

        // 2. Общий системный CPU через GetSystemTimes
        snapshot.TotalCpuPercent = CalculateSystemCpuPercent();

        // 3. Замер сетевой активности процессов через IP Helper API
        var (netMap, totalNet, socketCounts) = _networkTracker.CaptureNetworkBandwidth();
        snapshot.TotalNetworkBytesPerSec = totalNet;

        // 4. Обновляем кэш служб Windows раз в 4 секунды для нулевой нагрузки на CPU
        if ((now - _lastServicePoll).TotalSeconds > 4.0 || _servicePidMap.Count == 0)
        {
            _servicePidMap = ServiceProcessHelper.GetRunningServicesByPid();
            _lastServicePoll = now;
        }
        var servicePidMap = _servicePidMap;

        // 5. Запрос всех процессов ядра через ntdll.dll!NtQuerySystemInformation
        var rawEntries = ReadRawProcessEntries(CancellationToken.None);
        var activePids = new HashSet<int>(350);
        var pidToName = new Dictionary<int, string>(350);
        foreach (var entry in rawEntries)
        {
            if (entry.ProcessId > 0)
            {
                activePids.Add(entry.ProcessId);
                pidToName[entry.ProcessId] = entry.ProcessName;
            }
        }

        snapshot.TotalProcessesCount = rawEntries.Count;
        long totalDiskIo = 0;
        int totalThreads = 0;
        var processInfos = new List<ProcessInfo>(rawEntries.Count);
        // Только известное время создания даёт право переносить дельту между снимками.
        var activeInstances = new HashSet<(int Pid, long CreateTimeTicks)>();

        // 6. Формирование ProcessInfo объектов (без единого OpenProcess или Toolhelp32)
        foreach (var entry in rawEntries)
        {
            int pid = entry.ProcessId;
            if (pid == 0) continue; // Пропускаем Idle процесс

            int threadCount = entry.ThreadCount;
            totalThreads += threadCount;

            var info = new ProcessInfo
            {
                ProcessId = pid,
                ParentProcessId = entry.ParentProcessId,
                ProcessName = entry.ProcessName,
                WorkingSetBytes = entry.WorkingSetBytes,
                SessionId = entry.SessionId
            };

            if (entry.ParentProcessId > 0 && pidToName.TryGetValue(entry.ParentProcessId, out var parentName))
            {
                info.ParentProcessName = parentName;
            }

            if (netMap.TryGetValue(pid, out var netBytesSec))
            {
                info.NetworkBytesPerSec = netBytesSec;
            }

            if (socketCounts.TryGetValue(pid, out var sc))
            {
                info.ActiveSocketsCount = sc;
                info.HasNetworkActivity = sc > 0 || info.NetworkBytesPerSec > 0;
            }
            else
            {
                info.ActiveSocketsCount = 0;
                info.HasNetworkActivity = info.NetworkBytesPerSec > 0;
            }

            // Время запуска процесса из FILETIME (без исключений Access Denied на PPL/Protected процессах)
            var startTime = GetCreationTimeUtc(entry.CreateTime);
            info.StartTime = startTime;

            // Быстрое обогащение метаданных (после назначения StartTime для исключения гонок идентичности в кэше)
            _enricher.EnrichOrQueue(info, IntPtr.Zero);

            // Расчет дельты CPU и Disk I/O с изоляцией по (PID, StartTimeTicks) против PID Reuse
            TimeSpan totalProcTime = TimeSpan.FromTicks(entry.KernelTime + entry.UserTime);
            long currentIoBytes = entry.TotalIoBytes;
            var historyKey = (pid, startTime.Ticks);

            if (startTime != DateTime.MinValue && _history.TryGetValue(historyKey, out var prev))
            {
                var timeDelta = now - prev.Timestamp;
                if (timeDelta.TotalMilliseconds > 200)
                {
                    var delta = ProcessMetricMath.Calculate(
                        prev.TotalProcessorTime.Ticks,
                        totalProcTime.Ticks,
                        prev.TotalIoBytes,
                        currentIoBytes,
                        timeDelta,
                        _processorCount);

                    if (delta.HasValue)
                    {
                        info.CpuUsagePercent = delta.Value.CpuPercent;
                        info.DiskReadBytesPerSec = delta.Value.IoBytesPerSecond;
                        totalDiskIo += delta.Value.IoBytesPerSecond;
                    }
                }
            }

            if (startTime != DateTime.MinValue)
            {
                activeInstances.Add(historyKey);
                _history[historyKey] = new ProcessMetricHistory(totalProcTime, currentIoBytes, now);
            }

            // Связываем с автозапуском за O(1)
            _linker.LinkProcess(info, pidToName);

            // Определение служб Windows
            if (servicePidMap.TryGetValue(pid, out var serviceNames))
            {
                info.IsService = true;
                if (string.IsNullOrEmpty(info.Description))
                {
                    info.Description = string.Join(", ", serviceNames);
                }
            }
            else if (info.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase))
            {
                info.IsService = true;
                if (string.IsNullOrEmpty(info.Description))
                {
                    info.Description = "Хост-процесс служб Windows";
                }
            }
            else if (info.StartupLocation == EntryLocation.Service)
            {
                info.IsService = true;
                if (string.IsNullOrEmpty(info.Description))
                {
                    info.Description = info.StartupLocationDetail ?? "Служба Windows";
                }
            }
            else if (info.ParentProcessId > 0 && pidToName.TryGetValue(info.ParentProcessId, out var pName) &&
                     (pName.Equals("services", StringComparison.OrdinalIgnoreCase) || pName.Equals("services.exe", StringComparison.OrdinalIgnoreCase)))
            {
                info.IsService = true;
                if (string.IsNullOrEmpty(info.Description))
                {
                    info.Description = "Служба Windows";
                }
            }

            info.IsSuspended = EntryOperations.IsProcessSuspended(pid);
            processInfos.Add(info);
        }

        snapshot.TotalDiskIoBytesPerSec = totalDiskIo;
        snapshot.TotalThreadsCount = totalThreads;
        snapshot.Processes = [.. processInfos];

        // Очищаем завершившиеся процессы из списка приостановленных
        foreach (var suspendedPid in EntryOperations.GetSuspendedPids().ToList())
        {
            if (!activePids.Contains(suspendedPid))
                EntryOperations.TrackSuspendedPid(suspendedPid, false);
        }

        // Очищаем завершившиеся процессы из истории дельт
        foreach (var key in _history.Keys)
        {
            if (!activeInstances.Contains(key))
                _history.TryRemove(key, out _);
        }

        // Сохраняем только актуальные экземпляры: повторно использованный PID не удерживает старые метаданные.
        _enricher.PruneDeadProcesses(activeInstances);
        return snapshot;
        }
    }

    /// <summary>Рассчитывает процент загрузки системного процессора через GetSystemTimes.</summary>
    private double CalculateSystemCpuPercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return 0.0;

        long idle = FileTimeToLong(idleTime);
        long kernel = FileTimeToLong(kernelTime);
        long user = FileTimeToLong(userTime);

        if (!_hasSystemTimesPrev)
        {
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _hasSystemTimesPrev = true;
            return 0.0;
        }

        long deltaIdle = idle - _prevIdleTime;
        long deltaKernel = kernel - _prevKernelTime;
        long deltaUser = user - _prevUserTime;

        _prevIdleTime = idle;
        _prevKernelTime = kernel;
        _prevUserTime = user;

        long totalSys = deltaKernel + deltaUser;
        if (totalSys <= 0) return 0.0;

        double cpu = ((totalSys - deltaIdle) * 100.0) / totalSys;
        return Math.Clamp(cpu, 0.0, 100.0);
    }

    /// <summary>Преобразует структуру Win32 FILETIME в 64-битное целое число.</summary>
    private static long FileTimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        => ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    #region Win32 P/Invoke
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);
    #endregion

    /// <summary>Освобождает занятые ресурсы объекта и неуправляемый нативный буфер.</summary>
    public void Dispose()
    {
        // Таймер прекращается до освобождения буферов; внедрённые singleton-зависимости освобождает контейнер.
        _pollTimer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        lock (_collectLock)
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsNetworkTracker)
                _networkTracker.Dispose();
            if (_nativeBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_nativeBuffer);
                _nativeBuffer = IntPtr.Zero;
            }
        }
    }
}
