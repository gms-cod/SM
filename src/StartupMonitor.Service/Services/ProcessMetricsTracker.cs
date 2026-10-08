using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services;

/// <summary>Предыдущее состояние процесса для расчета дельты CPU и дискового ввода-вывода.</summary>
public record ProcessMetricHistory(
    TimeSpan TotalProcessorTime,
    long TotalIoBytes,
    DateTime Timestamp
);

/// <summary>Движок высокопроизводительного сбора метрик процессов и системной нагрузки с распределением по ядрам и On-Demand активацией.</summary>
public class ProcessMetricsTracker : IDisposable
{
    private readonly ProcessMetadataEnricher _enricher;
    private readonly ProcessStartupLinker _linker;
    private readonly ProcessNetworkTracker _networkTracker = new();
    private readonly ConcurrentDictionary<int, ProcessMetricHistory> _history = new();
    private readonly Timer _pollTimer;
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
    private readonly ParallelOptions _parallelOptions;

    // Количество активных подписчиков (UI на вкладке Диспетчера задач)
    private int _subscribersCount = 0;
    private int _isCollecting = 0;

    // Для замера общего системного CPU через GetSystemTimes
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private bool _hasSystemTimesPrev;
    // Кэш сопоставления PID со службами Windows (обновляется раз в 4 секунды)
    private Dictionary<int, List<string>> _servicePidMap = new();
    private DateTime _lastServicePoll = DateTime.MinValue;
    // PID процессов, для которых TotalProcessorTime/StartTime недоступны
    // (системные/PPL процессы, бросающие Win32Exception Access Denied) —
    // не вызываем эти свойства повторно до завершения процесса.
    private readonly ConcurrentDictionary<int, byte> _inaccessiblePids = new();

    /// <summary>Событие готовности свежего снимка производительности и процессов.</summary>
    public event EventHandler<SystemPerformanceSnapshot>? SnapshotReady;

    public ProcessMetricsTracker(ProcessMetadataEnricher enricher, ProcessStartupLinker linker)
    {
        _enricher = enricher;
        _linker = linker;
        _parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(_processorCount / 2, 2, 6)
        };
        // Таймер изначально отключен (On-Demand режим: 0% CPU до перехода на вкладку)
        _pollTimer = new Timer(OnPollTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Подписка UI на поток срезов метрик (активирует периодический опрос раз в 1.5 с).</summary>
    public void Subscribe()
    {
        if (Interlocked.Increment(ref _subscribersCount) == 1)
        {
            // Первый подписчик — включаем сбор каждые 1500 мс
            _pollTimer.Change(0, 1500);
        }
    }

    /// <summary>Отписка UI (при уходе с вкладки или сворачивании в трей).</summary>
    public void Unsubscribe()
    {
        if (Interlocked.Decrement(ref _subscribersCount) <= 0)
        {
            _subscribersCount = 0;
            // Все подписчики ушли — полностью выключаем таймер (0% CPU)
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>Разовый замер для немедленного ответа на GetProcesses.</summary>
    public SystemPerformanceSnapshot CaptureSnapshot()
    {
        return CollectSnapshotCore();
    }

    /// <summary>Получает список активных сетевых соединений (сокетов) для процесса или всей системы.</summary>
    public List<ProcessNetworkSocket> GetProcessSockets(int pid = -1) =>
        _networkTracker.GetSockets(pid);

    private void OnPollTick(object? state)
    {
        if (_subscribersCount <= 0) return;
        if (Interlocked.CompareExchange(ref _isCollecting, 1, 0) != 0) return;

        try
        {
            var snapshot = CollectSnapshotCore();
            SnapshotReady?.Invoke(this, snapshot);
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _isCollecting, 0);
        }
    }

    private SystemPerformanceSnapshot CollectSnapshotCore()
    {
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

        // 3. Получаем список всех процессов ОС
        var rawProcesses = Process.GetProcesses();
        snapshot.TotalProcessesCount = rawProcesses.Length;

        // Обновляем кэш служб Windows раз в 4 секунды для нулевой нагрузки на CPU
        if ((now - _lastServicePoll).TotalSeconds > 4.0 || _servicePidMap.Count == 0)
        {
            _servicePidMap = ServiceProcessHelper.GetRunningServicesByPid();
            _lastServicePoll = now;
        }
        var servicePidMap = _servicePidMap;

        var activePids = new HashSet<int>(rawProcesses.Length);
        var pidToName = new Dictionary<int, string>(rawProcesses.Length);
        foreach (var p in rawProcesses)
        {
            activePids.Add(p.Id);
            pidToName[p.Id] = p.ProcessName;
        }

        var processInfos = new ConcurrentBag<ProcessInfo>();
        long totalDiskIo = 0;
        int totalThreads = 0;
        // 4. Замер сетевой активности процессов через IP Helper API
        var (netMap, totalNet, socketCounts) = _networkTracker.CaptureNetworkBandwidth();
        snapshot.TotalNetworkBytesPerSec = totalNet;

        // Карта снимка процессов (Parent PID + ThreadCount) через CreateToolhelp32Snapshot
        var snapshotMap = ProcessStartupLinker.GetProcessSnapshotMap();

        // 5. Параллельный сбор метрик по ядрам процессора
        Parallel.ForEach(rawProcesses, _parallelOptions, proc =>
        {
            try
            {
                int pid = proc.Id;
                if (pid == 0) return; // Пропускаем Idle процесс

                int sessionId = 0;
                try { sessionId = proc.SessionId; } catch { }

                int parentPid = 0;
                int threadCount = 0;
                if (snapshotMap.TryGetValue(pid, out var snapshotEntry))
                {
                    parentPid = snapshotEntry.ParentPid;
                    threadCount = snapshotEntry.ThreadCount;
                }

                var info = new ProcessInfo
                {
                    ProcessId = pid,
                    ParentProcessId = parentPid,
                    ProcessName = proc.ProcessName,
                    WorkingSetBytes = proc.WorkingSet64,
                    SessionId = sessionId
                };

                if (parentPid > 0 && pidToName.TryGetValue(parentPid, out var parentName))
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

                Interlocked.Add(ref totalThreads, threadCount);

                // Пытаемся получить дескриптор для чтения IO_COUNTERS, Parent PID и пути к образу
                IntPtr hProcess = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
                long currentIoBytes = 0;
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        if (GetProcessIoCounters(hProcess, out var io))
                        {
                            currentIoBytes = (long)(io.ReadTransferCount + io.WriteTransferCount);
                        }
                        _enricher.EnrichOrQueue(info, hProcess);
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
                else
                {
                    _enricher.EnrichOrQueue(info, IntPtr.Zero);
                }

                // Время процессора и расчет дельт CPU%
                // Недоступные PID (системные/PPL) пропускаем без повторных Win32Exception.
                TimeSpan totalProcTime = TimeSpan.Zero;
                DateTime startTime = DateTime.MinValue;
                if (!_inaccessiblePids.ContainsKey(pid))
                {
                    try
                    {
                        totalProcTime = proc.TotalProcessorTime;
                        startTime = proc.StartTime;
                    }
                    catch
                    {
                        _inaccessiblePids.TryAdd(pid, 0);
                        totalProcTime = TimeSpan.Zero;
                        startTime = DateTime.MinValue;
                    }
                }

                info.StartTime = startTime;

                // Расчет дельты CPU и Disk I/O
                if (_history.TryGetValue(pid, out var prev))
                {
                    var timeDelta = (now - prev.Timestamp).TotalMilliseconds;
                    if (timeDelta > 200)
                    {
                        var cpuDeltaMs = (totalProcTime - prev.TotalProcessorTime).TotalMilliseconds;
                        info.CpuUsagePercent = Math.Clamp((cpuDeltaMs / (timeDelta * _processorCount)) * 100.0, 0.0, 100.0);

                        var ioDelta = currentIoBytes - prev.TotalIoBytes;
                        if (ioDelta >= 0)
                        {
                            long ioSec = (long)(ioDelta / (timeDelta / 1000.0));
                            info.DiskReadBytesPerSec = ioSec;
                            Interlocked.Add(ref totalDiskIo, ioSec);
                        }
                    }
                }

                _history[pid] = new ProcessMetricHistory(totalProcTime, currentIoBytes, now);

                // Связываем с автозапуском за O(1)
                _linker.LinkProcess(info, pidToName);

                // Определение служб Windows и установка их описаний
                if (servicePidMap.TryGetValue(pid, out var serviceNames))
                {
                    info.IsService = true;
                    if (string.IsNullOrEmpty(info.Description))
                    {
                        info.Description = string.Join(", ", serviceNames);
                    }
                }
                else if (proc.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase))
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
            catch { }
            finally
            {
                proc.Dispose();
            }
        });

        snapshot.TotalDiskIoBytesPerSec = totalDiskIo;
        snapshot.TotalThreadsCount = totalThreads;
        snapshot.Processes = [.. processInfos];

        // Очищаем завершившиеся процессы из списка приостановленных
        foreach (var suspendedPid in EntryOperations.GetSuspendedPids().ToList())
        {
            if (!activePids.Contains(suspendedPid))
                EntryOperations.TrackSuspendedPid(suspendedPid, false);
        }

        // Очищаем завершившиеся процессы из истории
        foreach (var pid in _history.Keys)
        {
            if (!activePids.Contains(pid))
                _history.TryRemove(pid, out _);
        }
        // Очищаем завершившиеся PID из кэша недоступных (TotalProcessorTime/StartTime)
        foreach (var pid in _inaccessiblePids.Keys)
        {
            if (!activePids.Contains(pid))
                _inaccessiblePids.TryRemove(pid, out _);
        }
        _enricher.PruneDeadProcesses(activePids);

        return snapshot;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);
    #endregion

    public void Dispose()
    {
        _pollTimer.Dispose();
    }
}

