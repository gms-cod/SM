using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StartupMonitor.Shared.Helpers;

/// <summary>
/// Предоставляет методы для высокоточного замера общей загрузки процессора
/// и загрузки процессора сторонними процессами (с вычетом времени работы службы и UI).
/// Использует Win32 API GetSystemTimes с минимальным overhead.
/// </summary>
public static class CpuUsageHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>GetSystemTimes: Возвращает SystemTimes.</summary>
    /// <param name="lpIdleTime">Входные данные: значение параметра lpIdleTime (out long).</param>
    /// <param name="lpKernelTime">Входные данные: значение параметра lpKernelTime (out long).</param>
    /// <param name="lpUserTime">Входные данные: значение параметра lpUserTime (out long).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>OpenProcess: Выполняет операцию OpenProcess.</summary>
    /// <param name="dwDesiredAccess">Входные данные: значение параметра dwDesiredAccess (uint).</param>
    /// <param name="bInheritHandle">Входные данные: значение параметра bInheritHandle ([MarshalAs(UnmanagedType.Bool)] bool).</param>
    /// <param name="dwProcessId">Входные данные: значение параметра dwProcessId (int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>CloseHandle: Выполняет операцию CloseHandle.</summary>
    /// <param name="hObject">Входные данные: значение параметра hObject (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadIdealProcessor(IntPtr hThread, uint dwIdealProcessor);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    private const int SystemProcessorPerformanceInformation = 8;

    private static readonly object CoreSyncLock = new();
    private static SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[]? _prevCoreInfo;
    private static DateTime _prevCoreSampleTime = DateTime.MinValue;
    private static int _lastLeastLoadedCore;
    private static int _roundRobinIndex;

    private static readonly object SyncLock = new();
    private static long _prevIdleTime;
    private static long _prevKernelTime;
    private static long _prevUserTime;
    private static long _prevOurTicks;
    private static DateTime _prevSampleTime = DateTime.MinValue;

    private static int _lastTotalUsage;
    private static int _lastExternalUsage;

    // Отдельный кэш для точечного замера по PID (глубокий покой UI в трее, п. 2.1).
    // Хранится отдельно от общего кэша, чтобы чередование источников _prevOurTicks не искажало дельты.
    private static readonly object FastSyncLock = new();
    private static long _prevIdleTimeFast;
    private static long _prevKernelTimeFast;
    private static long _prevUserTimeFast;
    private static long _prevOurTicksFast;
    private static DateTime _prevSampleTimeFast = DateTime.MinValue;
    private static int _lastTotalUsageFast;
    private static int _lastExternalUsageFast;
    private static bool _fastInitialized;

    /// <summary>CpuUsageHelper: Выполняет операцию CpuUsageHelper.</summary>
    static CpuUsageHelper()
    {
        Initialize();
    }

    /// <summary>Инициализирует базовые отсчеты системного и процессорного времени.</summary>
    private static void Initialize()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _prevOurTicks = GetOurProcessesTicks();
            _prevSampleTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Возвращает текущий процент загрузки процессора.
    /// </summary>
    /// <param name="excludeSelf">
    /// Если true — из суммарного времени занятости вычитается процессорное время процессов
    /// StartupMonitor (сервиса и UI), возвращая процент нагрузки внешними процессами.
    /// </param>
    /// <returns>Загрузка процессора в процентах (0..100).</returns>
    public static int GetCpuUsage(bool excludeSelf = true)
    {
        lock (SyncLock)
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _prevSampleTime).TotalMilliseconds;

            // Если с предыдущего замера прошло меньше 200 мс, возвращаем кэшированное значение
            if (elapsedMs < 200 && _prevSampleTime != DateTime.MinValue)
            {
                return excludeSelf ? _lastExternalUsage : _lastTotalUsage;
            }

            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return excludeSelf ? _lastExternalUsage : _lastTotalUsage;
            }

            // Оптимизация: перечисляем процессы через GetOurProcessesTicks() ТОЛЬКО если запрошен excludeSelf.
            // При общем замере системы (excludeSelf == false) не тратим CPU на перечисление сотен процессов.
            var ourTicks = excludeSelf ? GetOurProcessesTicks() : 0L;

            var deltaIdle = idle - _prevIdleTime;
            var deltaKernel = kernel - _prevKernelTime;
            var deltaUser = user - _prevUserTime;
            var deltaOur = ourTicks - _prevOurTicks;

            // В Windows kernelTime уже включает в себя idleTime
            var deltaTotal = deltaKernel + deltaUser;

            if (deltaTotal > 0)
            {
                var deltaBusy = deltaTotal - deltaIdle;
                if (deltaBusy < 0) deltaBusy = 0;

                // Общая загрузка системы
                var totalPct = (int)Math.Round((double)deltaBusy / deltaTotal * 100.0);
                _lastTotalUsage = Math.Clamp(totalPct, 0, 100);

                // Загрузка системы за вычетом наших процессов
                var deltaExternalBusy = Math.Max(0, deltaBusy - deltaOur);
                var externalPct = (int)Math.Round((double)deltaExternalBusy / deltaTotal * 100.0);
                _lastExternalUsage = Math.Clamp(externalPct, 0, 100);

                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _prevOurTicks = ourTicks;
                _prevSampleTime = now;
            }

            return excludeSelf ? _lastExternalUsage : _lastTotalUsage;
        }
    }

    /// <summary>
    /// Выполняет синхронный замер нагрузки за указанный интервал ожидания.
    /// Полезно перед принятием решения в фоновом потоке, если сервис давно не опрашивал процессор.
    /// </summary>
    public static int MeasureUsage(int sampleDelayMs = 200, bool excludeSelf = true)
    {
        if (sampleDelayMs < 50) sampleDelayMs = 50;
        if (sampleDelayMs > 1000) sampleDelayMs = 1000;

        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
            return GetCpuUsage(excludeSelf);

        var our1 = GetOurProcessesTicks();

        Thread.Sleep(sampleDelayMs);

        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
            return GetCpuUsage(excludeSelf);

        var our2 = GetOurProcessesTicks();

        var deltaTotal = (kernel2 - kernel1) + (user2 - user1);
        if (deltaTotal <= 0) return 0;

        var deltaBusy = Math.Max(0, deltaTotal - (idle2 - idle1));
        if (excludeSelf)
        {
            var deltaOur = Math.Max(0, our2 - our1);
            deltaBusy = Math.Max(0, deltaBusy - deltaOur);
        }

        var pct = (int)Math.Round((double)deltaBusy / deltaTotal * 100.0);
        return Math.Clamp(pct, 0, 100);
    }

    /// <summary>
    /// Вычисляет процент загрузки процессора по переданным дельтам времени.
    /// Используется для тестирования без вызовов API ОС.
    /// </summary>
    public static int CalculateCpuPercentage(long deltaTotal, long deltaIdle, long deltaOur = 0, bool excludeSelf = true)
    {
        if (deltaTotal <= 0) return 0;
        var deltaBusy = Math.Max(0, deltaTotal - deltaIdle);
        if (excludeSelf && deltaOur > 0)
        {
            deltaBusy = Math.Max(0, deltaBusy - deltaOur);
        }
        var pct = (int)Math.Round((double)deltaBusy / deltaTotal * 100.0);
        return Math.Clamp(pct, 0, 100);
    }

    /// <summary>
    /// Суммирует процессорное время (в 100-нс тактах) для всех процессов StartupMonitor (сервис, UI).
    /// </summary>
    public static long GetOurProcessesTicks()
    {
        long totalTicks = 0;
        try
        {
            using var current = Process.GetCurrentProcess();
            totalTicks += current.TotalProcessorTime.Ticks;
            var currentId = current.Id;

            // Находим другие процессы с префиксом StartupMonitor
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id != currentId && p.ProcessName.StartsWith("StartupMonitor", StringComparison.OrdinalIgnoreCase))
                    {
                        totalTicks += p.TotalProcessorTime.Ticks;
                    }
                }
                catch
                {
                    // Процесс мог завершиться в момент перечисления
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // Игнорируем ошибки доступа при получении процессов
        }

        return totalTicks;
    }

    /// <summary>
    /// Возвращает процессорное время (в 100-нс тактах, kernel + user) одного процесса по PID
    /// без перечисления всех процессов через <see cref="Process.GetProcesses"/>.
    /// Использует P/Invoke GetProcessTimes с fallback на Process.GetProcessById.
    /// </summary>
    /// <param name="pid">Идентификатор процесса.</param>
    /// <returns>Сумма kernel + user времени в тактах; 0 если процесс недоступен.</returns>
    public static long GetProcessTicks(int pid)
    {
        if (pid <= 0) return 0;

        // Быстрый путь: прямой handle + GetProcessTimes (без создания Process-объекта и перечисления).
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                if (GetProcessTimes(handle, out _, out _, out var kernel, out var user))
                    return kernel + user;
            }
            catch
            {
                // Fallback ниже
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        // Fallback: прямое обращение по PID (тоже без GetProcesses()).
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.TotalProcessorTime.Ticks;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Суммирует процессорное время (в 100-нс тактах) для заданных PID
    /// без вызова тяжёлого <see cref="Process.GetProcesses"/>.
    /// </summary>
    /// <param name="pids">Идентификаторы процессов (UI + сервис).</param>
    /// <returns>Суммарное процессорное время в тактах.</returns>
    public static long GetProcessesTicks(IEnumerable<int> pids)
    {
        if (pids == null) return 0;
        long total = 0;
        foreach (var pid in pids)
        {
            total += GetProcessTicks(pid);
        }
        return total;
    }

    /// <summary>
    /// Точечный замер загрузки CPU с вычетом только указанных PID (UI + сервис),
    /// без перечисления всех процессов Windows. Предназначен для лёгкого таймера UI.
    /// </summary>
    /// <param name="ownPids">PID текущего UI-процесса и службы (если известен).</param>
    /// <param name="excludeSelf">true — вычесть время ownPids (внешняя нагрузка); false — общая.</param>
    /// <returns>Загрузка процессора в процентах (0..100).</returns>
    public static int GetCpuUsageForPids(IEnumerable<int> ownPids, bool excludeSelf = true)
    {
        lock (FastSyncLock)
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _prevSampleTimeFast).TotalMilliseconds;

            if (_fastInitialized && elapsedMs < 200)
            {
                return excludeSelf ? _lastExternalUsageFast : _lastTotalUsageFast;
            }

            if (!GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return excludeSelf ? _lastExternalUsageFast : _lastTotalUsageFast;
            }

            // Первый замер: только инициализируем базу, возвращаем кэш (0).
            if (!_fastInitialized)
            {
                _prevIdleTimeFast = idle;
                _prevKernelTimeFast = kernel;
                _prevUserTimeFast = user;
                _prevOurTicksFast = GetProcessesTicks(ownPids);
                _prevSampleTimeFast = now;
                _fastInitialized = true;
                return excludeSelf ? _lastExternalUsageFast : _lastTotalUsageFast;
            }

            var ourTicks = GetProcessesTicks(ownPids);

            var deltaIdle = idle - _prevIdleTimeFast;
            var deltaKernel = kernel - _prevKernelTimeFast;
            var deltaUser = user - _prevUserTimeFast;
            var deltaOur = ourTicks - _prevOurTicksFast;

            var deltaTotal = deltaKernel + deltaUser;

            if (deltaTotal > 0)
            {
                var deltaBusy = deltaTotal - deltaIdle;
                if (deltaBusy < 0) deltaBusy = 0;

                var totalPct = (int)Math.Round((double)deltaBusy / deltaTotal * 100.0);
                _lastTotalUsageFast = Math.Clamp(totalPct, 0, 100);

                var deltaExternalBusy = Math.Max(0, deltaBusy - deltaOur);
                var externalPct = (int)Math.Round((double)deltaExternalBusy / deltaTotal * 100.0);
                _lastExternalUsageFast = Math.Clamp(externalPct, 0, 100);

                _prevIdleTimeFast = idle;
                _prevKernelTimeFast = kernel;
                _prevUserTimeFast = user;
                _prevOurTicksFast = ourTicks;
                _prevSampleTimeFast = now;
            }

            return excludeSelf ? _lastExternalUsageFast : _lastTotalUsageFast;
        }
    }

    /// <summary>
    /// Возвращает пару (total, external) за один проход системного замера по указанным PID.
    /// Избегает двойного вызова GetSystemTimes/GetProcessTimes в тике таймера UI.
    /// </summary>
    public static (int Total, int External) GetCpuUsagePairForPids(IEnumerable<int> ownPids)
    {
        // Первый вызов обновляет кэш, второй забирает его же (внутри окна &lt;200 мс).
        var total = GetCpuUsageForPids(ownPids, excludeSelf: false);
        var external = GetCpuUsageForPids(ownPids, excludeSelf: true);
        return (total, external);
    }

    /// <summary>
    /// Возвращает индекс логического ядра процессора (0..N-1), имеющего наименьшую загрузку
    /// (максимальный процент простоя) на основе прямого системного замера через NtQuerySystemInformation.
    /// Накладные расходы составляют менее 1 микросекунды.
    /// </summary>
    /// <returns>Индекс наименее загруженного ядра (0..Environment.ProcessorCount - 1).</returns>
    public static int GetLeastLoadedProcessorCore()
    {
        lock (CoreSyncLock)
        {
            var coreCount = Environment.ProcessorCount;
            if (coreCount <= 1) return 0;

            var now = DateTime.UtcNow;
            // Кэшируем результат на 300 мс, чтобы исключить избыточные замеры при частых запросах
            if (_prevCoreInfo != null && (now - _prevCoreSampleTime).TotalMilliseconds < 300)
            {
                return _lastLeastLoadedCore;
            }

            var structSize = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
            var bufferSize = structSize * coreCount;
            var buffer = Marshal.AllocHGlobal(bufferSize);

            try
            {
                var status = NtQuerySystemInformation(SystemProcessorPerformanceInformation, buffer, bufferSize, out var returnLength);
                if (status != 0) // STATUS_SUCCESS = 0
                {
                    return _lastLeastLoadedCore;
                }

                var currentInfo = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[coreCount];
                for (int i = 0; i < coreCount; i++)
                {
                    var ptr = IntPtr.Add(buffer, i * structSize);
                    currentInfo[i] = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(ptr);
                }

                if (_prevCoreInfo == null || _prevCoreInfo.Length != coreCount)
                {
                    _prevCoreInfo = currentInfo;
                    _prevCoreSampleTime = now;
                    // При первичном замере распределяем потоки по ядрам через Round-Robin, чтобы не концентрировать всё на ядре 0
                    _lastLeastLoadedCore = (int)((uint)System.Threading.Interlocked.Increment(ref _roundRobinIndex) % (uint)coreCount);
                    return _lastLeastLoadedCore;
                }

                int bestCore = 0;
                double maxIdleRatio = -1.0;

                for (int i = 0; i < coreCount; i++)
                {
                    var deltaIdle = currentInfo[i].IdleTime - _prevCoreInfo[i].IdleTime;
                    var deltaKernel = currentInfo[i].KernelTime - _prevCoreInfo[i].KernelTime;
                    var deltaUser = currentInfo[i].UserTime - _prevCoreInfo[i].UserTime;
                    var deltaTotal = deltaKernel + deltaUser;

                    double idleRatio = deltaTotal > 0 ? (double)deltaIdle / deltaTotal : 1.0;
                    if (idleRatio > maxIdleRatio)
                    {
                        maxIdleRatio = idleRatio;
                        bestCore = i;
                    }
                }

                _prevCoreInfo = currentInfo;
                _prevCoreSampleTime = now;
                _lastLeastLoadedCore = bestCore;
                return bestCore;
            }
            catch
            {
                // При любой непредвиденной ошибке возвращаем текущее известное ядро
                return _lastLeastLoadedCore;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// Мягко привязывает текущий поток к указанному или наименее загруженному ядру процессора
    /// через Win32 SetThreadIdealProcessor. Планировщик Windows будет отдавать предпочтение этому ядру.
    /// </summary>
    /// <param name="coreIndex">Индекс ядра (если меньше 0, ядро выбирается автоматически).</param>
    public static void AssignCurrentThreadToIdealCore(int coreIndex = -1)
    {
        try
        {
            var targetCore = coreIndex >= 0 ? coreIndex : GetLeastLoadedProcessorCore();
            if (targetCore >= 0 && targetCore < Environment.ProcessorCount)
            {
                SetThreadIdealProcessor(GetCurrentThread(), (uint)targetCore);
            }
        }
        catch
        {
            // Безопасное подавление при недоступности прав на привязку
        }
    }

    /// <summary>
    /// Устанавливает низкий приоритет для текущего потока (BelowNormal), чтобы фоновые задачи
    /// гарантированно уступали ресурсы интерактивному пользовательскому интерфейсу и приложениям.
    /// </summary>
    public static void SetCurrentThreadLowPriority()
    {
        try
        {
            if (Thread.CurrentThread.Priority != ThreadPriority.BelowNormal)
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            }
        }
        catch
        {
            // Игнорируем в средах, где смена приоритета потока ограничена политикой
        }
    }

    /// <summary>
    /// Интеллектуальный шлюз загрузки: приостанавливает выполнение задачи, если процессор
    /// в данный момент перегружен сторонними процессами (например, играми или компиляцией).
    /// </summary>
    /// <param name="threshold">Порог загрузки CPU (по умолчанию 60%).</param>
    /// <param name="checkIntervalMs">Интервал повторной проверки (по умолчанию 500 мс).</param>
    /// <param name="maxWaitMs">Максимальное время ожидания освобождения процессора (по умолчанию 5000 мс).</param>
    /// <param name="ct">Токен отмены операции.</param>
    public static async Task WaitForCpuHeadroomAsync(
        double threshold = 60.0,
        int checkIntervalMs = 500,
        int maxWaitMs = 5000,
        CancellationToken ct = default)
    {
        var startTime = Stopwatch.GetTimestamp();
        while (!ct.IsCancellationRequested)
        {
            var totalCpu = GetCpuUsage(excludeSelf: false);
            if (totalCpu < threshold)
            {
                break;
            }

            if (Stopwatch.GetElapsedTime(startTime).TotalMilliseconds >= maxWaitMs)
            {
                break; // Не блокируем выполнение дольше максимального лимита
            }

            await Task.Delay(checkIntervalMs, ct).ConfigureAwait(false);
        }
    }

}
