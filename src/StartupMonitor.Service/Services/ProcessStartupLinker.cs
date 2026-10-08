using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Сервис сопоставления запущенных процессов с базой автозапуска и определения их источников.</summary>
public class ProcessStartupLinker
{
    // Lock-free хеш-индексы для сопоставления за O(1).
    // FrozenDictionary: чтение полностью потокобезопасно без блокировок.
    // Обновление — атомарной подменой ссылки в UpdateIndex().
    private FrozenDictionary<string, StartupEntry> _pathToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;
    private FrozenDictionary<string, StartupEntry> _serviceNameToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;
    private FrozenDictionary<string, StartupEntry> _nameToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;

    /// <summary>Обновляет индекс записей автозапуска из текущего снимка базы.</summary>
    public void UpdateIndex(IEnumerable<StartupEntry> entries)
    {
        var pathMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
        var serviceMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
        var nameMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.FilePath))
            {
                var normPath = NormalizePath(entry.FilePath);
                if (!string.IsNullOrEmpty(normPath))
                    pathMap[normPath] = entry;
            }

            if (entry.Location == EntryLocation.Service && !string.IsNullOrWhiteSpace(entry.LocationDetail))
            {
                serviceMap[entry.LocationDetail.Trim()] = entry;
            }

            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                nameMap[entry.Name.Trim()] = entry;
            }
        }

        // Атомарная подмена ссылок: читатели в LinkProcess() видят либо старый,
        // либо новый снимок целиком, блокировки не требуются.
        _pathToEntryMap = pathMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _serviceNameToEntryMap = serviceMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _nameToEntryMap = nameMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Связывает процесс с записью автозапуска или определяет имя родительского процесса.</summary>
    public void LinkProcess(ProcessInfo proc, IReadOnlyDictionary<int, string>? processNamesByPid = null)
    {
        // 1. Поиск по нормализованному пути к файлу (Lock-Free, без lock)
        if (!string.IsNullOrWhiteSpace(proc.FilePath))
        {
            var norm = NormalizePath(proc.FilePath);
            if (!string.IsNullOrEmpty(norm) && _pathToEntryMap.TryGetValue(norm, out var entry))
            {
                SetStartupInfo(proc, entry);
                return;
            }
        }

        // 2. Для служб (svchost, services) или по совпадению имени процесса (Lock-Free, без lock)
        if (!string.IsNullOrWhiteSpace(proc.ProcessName)
            && _nameToEntryMap.TryGetValue(proc.ProcessName, out var entryByName))
        {
            SetStartupInfo(proc, entryByName);
            return;
        }

        // 3. Если совпадений с автозапуском нет — определяем родительский процесс
        if (proc.ParentProcessId > 0 && processNamesByPid != null)
        {
            if (processNamesByPid.TryGetValue(proc.ParentProcessId, out var parentName))
            {
                proc.ParentProcessName = parentName;
            }
        }

        // 4. Эвристический анализ командной строки и пути процесса
        if (proc.ThreatInfo == null || !proc.ThreatInfo.HasThreat)
        {
            var threat = StartupMonitor.Shared.Helpers.CliHeuristicAnalyzer.Analyze(proc.CommandLine, proc.FilePath);
            if (threat.HasThreat)
            {
                proc.ThreatInfo = threat;
            }
        }
    }

    /// <summary>Сопоставляет процесс с записью автозапуска (для проверок и тестов).</summary>
    public (bool isStartup, string? entryId, EntryLocation? location) MatchProcess(string? filePath, string? processName, bool isService = false)
    {
        var proc = new ProcessInfo
        {
            FilePath = filePath ?? string.Empty,
            ProcessName = processName ?? string.Empty,
            IsService = isService
        };
        LinkProcess(proc);
        return (proc.IsStartupEntry, proc.StartupEntryId, proc.StartupLocation);
    }

    /// <summary>SetStartupInfo: Устанавливает StartupInfo.</summary>
    /// <param name="proc">Входные данные: значение параметра proc (ProcessInfo).</param>
    /// <param name="entry">Входные данные: значение параметра entry (StartupEntry).</param>
    private static void SetStartupInfo(ProcessInfo proc, StartupEntry entry)
    {
        proc.StartupEntryId = entry.Id;
        proc.StartupLocation = entry.Location;
        proc.StartupLocationDetail = entry.LocationDetail;
        if (string.IsNullOrEmpty(proc.CommandLine) && !string.IsNullOrEmpty(entry.CommandLine))
        {
            proc.CommandLine = entry.CommandLine;
        }
        if (entry.ThreatInfo != null && entry.ThreatInfo.HasThreat)
        {
            proc.ThreatInfo = entry.ThreatInfo;
        }
    }

    /// <summary>Нормализует путь к файлу: извлекает путь из кавычек/командной строки, разворачивает переменные окружения.</summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var p = path.Trim();
            if (p.StartsWith('"'))
            {
                int closeQuote = p.IndexOf('"', 1);
                p = closeQuote > 1 ? p[1..closeQuote] : p.Trim('"');
            }
            else
            {
                int exeIdx = p.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx > 0 && exeIdx + 4 < p.Length && char.IsWhiteSpace(p[exeIdx + 4]))
                {
                    p = p[..(exeIdx + 4)];
                }
            }

            p = Environment.ExpandEnvironmentVariables(p.Trim());
            return Path.GetFullPath(p);
        }
        catch
        {
            return path.Trim('"', ' ');
        }
    }

    #region P/Invoke NtQueryInformationProcess для быстрого получения Parent PID

    [DllImport("ntdll.dll")]
    /// <summary>NtQueryInformationProcess: Выполняет операцию NtQueryInformationProcess.</summary>
    /// <param name="processHandle">Входные данные: значение параметра processHandle (IntPtr).</param>
    /// <param name="processInformationClass">Входные данные: значение параметра processInformationClass (int).</param>
    /// <param name="processInformation">Входные данные: значение параметра processInformation (ref PROCESS_BASIC_INFORMATION).</param>
    /// <param name="processInformationLength">Входные данные: значение параметра processInformationLength (int).</param>
    /// <param name="returnLength">Входные данные: значение параметра returnLength (out int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern int.</returns>
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    /// <summary>Быстро считывает Parent PID процесса через NtQueryInformationProcess без использования тяжелого WMI.</summary>
    public static int GetParentProcessId(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero) return 0;
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(processHandle, 0 /* ProcessBasicInformation */, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status == 0)
            {
                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
        }
        catch { }
        return 0;
    }

    #endregion

    #region P/Invoke CreateToolhelp32Snapshot для сбора полного снимка Parent PID всех процессов

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>CreateToolhelp32Snapshot: Выполняет операцию CreateToolhelp32Snapshot.</summary>
    /// <param name="dwFlags">Входные данные: значение параметра dwFlags (uint).</param>
    /// <param name="th32ProcessID">Входные данные: значение параметра th32ProcessID (uint).</param>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    /// <summary>Process32First: Выполняет операцию Process32First.</summary>
    /// <param name="hSnapshot">Входные данные: значение параметра hSnapshot (IntPtr).</param>
    /// <param name="lppe">Входные данные: значение параметра lppe (ref PROCESSENTRY32).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    /// <summary>Process32Next: Выполняет операцию Process32Next.</summary>
    /// <param name="hSnapshot">Входные данные: значение параметра hSnapshot (IntPtr).</param>
    /// <param name="lppe">Входные данные: значение параметра lppe (ref PROCESSENTRY32).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>CloseHandle: Выполняет операцию CloseHandle.</summary>
    /// <param name="hObject">Входные данные: значение параметра hObject (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th23DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    /// <summary>Компактная запись снимка процесса: Parent PID и число потоков из Toolhelp32.</summary>
    public readonly record struct ProcessSnapshotEntry(int ParentPid, int ThreadCount);

    /// <summary>
    /// Возвращает мгновенный снимок всей таблицы процессов Windows: Child PID -> (Parent PID, ThreadCount).
    /// Работает за ~1 мс, не требует открытия дескрипторов (OpenProcess) и покрывает даже защищённые процессы.
    /// </summary>
    public static Dictionary<int, ProcessSnapshotEntry> GetProcessSnapshotMap()
    {
        var map = new Dictionary<int, ProcessSnapshotEntry>();
        IntPtr snapshot = CreateToolhelp32Snapshot(0x00000002 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return map;

        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    if (entry.th32ProcessID > 0)
                    {
                        map[(int)entry.th32ProcessID] = new ProcessSnapshotEntry(
                            (int)entry.th32ParentProcessID,
                            (int)entry.cntThreads);
                    }
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        catch { }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    /// <summary>
    /// Возвращает мгновенный снимок всей таблицы процессов Windows: Child PID -> Parent PID.
    /// Работает за ~1 мс, не требует открытия дескрипторов (OpenProcess) и покрывает даже защищённые процессы.
    /// Оставлена для совместимости; внутри делегирует <see cref="GetProcessSnapshotMap"/>.
    /// </summary>
    public static Dictionary<int, int> GetParentPidMap()
    {
        var snapshotMap = GetProcessSnapshotMap();
        var map = new Dictionary<int, int>(snapshotMap.Count);
        foreach (var kv in snapshotMap)
        {
            map[kv.Key] = kv.Value.ParentPid;
        }

        return map;
    }

    #endregion
}

