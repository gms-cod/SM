using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace StartupMonitor.Service.Services.AntiRansomware;

/// <summary>
/// Информация о процессе, удерживающем блокировку на файл.
/// </summary>
/// <param name="ProcessId">Идентификатор процесса (PID).</param>
/// <param name="ApplicationName">Имя приложения или исполняемого файла.</param>
/// <param name="ServiceShortName">Короткое системное имя службы Windows (если применимо).</param>
/// <param name="StartTimeUtc">Время запуска процесса (UTC) для защиты от повторного использования PID.</param>
public record FileLockingProcessInfo(
    int ProcessId,
    string ApplicationName,
    string ServiceShortName,
    DateTime? StartTimeUtc);

/// <summary>
/// Разрешитель блокировок файлов на основе Windows Restart Manager API (rstrtmgr.dll).
/// Позволяет мгновенно определить PID процесса, открывшего или начавшего перезаписывать файл-ловушку.
/// </summary>
public static class RestartManagerResolver
{
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        out uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint dwSessionHandle);

    /// <summary>
    /// Находит идентификаторы процессов (PID), удерживающих файловую блокировку на указанный путь.
    /// </summary>
    /// <param name="filePath">Полный путь к целевому файлу.</param>
    /// <returns>Список PID процессов, открывших файл.</returns>
    public static IReadOnlyList<int> FindProcessesLockingFile(string filePath)
    {
        IReadOnlyList<FileLockingProcessInfo> infos = GetProcessesLockingFile(filePath);
        if (infos.Count == 0)
        {
            return Array.Empty<int>();
        }

        var result = new List<int>(infos.Count);
        for (int i = 0; i < infos.Count; i++)
        {
            if (infos[i].ProcessId > 0 && !result.Contains(infos[i].ProcessId))
            {
                result.Add(infos[i].ProcessId);
            }
        }

        return result;
    }

    /// <summary>
    /// Получает расширенную информацию о процессах, удерживающих указанный файл, через сессию Restart Manager.
    /// Гарантирует вызов RmEndSession в блоке finally.
    /// </summary>
    /// <param name="filePath">Полный путь к проверяемому файлу.</param>
    /// <returns>Список сведений о процессах или пустой список при отсутствии блокировок.</returns>
    public static IReadOnlyList<FileLockingProcessInfo> GetProcessesLockingFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Array.Empty<FileLockingProcessInfo>();
        }

        string sessionKey = Guid.NewGuid().ToString("N");
        int res = RmStartSession(out uint sessionHandle, 0, sessionKey);
        if (res != ErrorSuccess)
        {
            return Array.Empty<FileLockingProcessInfo>();
        }

        try
        {
            string[] resources = [filePath];
            res = RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null);
            if (res != ErrorSuccess)
            {
                return Array.Empty<FileLockingProcessInfo>();
            }

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint lpdwRebootReasons = 0;

            res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, out lpdwRebootReasons);
            if (res == ErrorSuccess || pnProcInfoNeeded == 0)
            {
                return Array.Empty<FileLockingProcessInfo>();
            }

            if (res != ErrorMoreData)
            {
                return Array.Empty<FileLockingProcessInfo>();
            }

            var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
            pnProcInfo = pnProcInfoNeeded;

            res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, out lpdwRebootReasons);
            if (res != ErrorSuccess)
            {
                return Array.Empty<FileLockingProcessInfo>();
            }

            var list = new List<FileLockingProcessInfo>((int)pnProcInfo);
            for (int i = 0; i < pnProcInfo; i++)
            {
                int pid = processInfo[i].Process.dwProcessId;
                DateTime? startTime = ConvertFileTimeToUtc(processInfo[i].Process.ProcessStartTime);
                string appName = processInfo[i].strAppName ?? string.Empty;
                string svcName = processInfo[i].strServiceShortName ?? string.Empty;

                list.Add(new FileLockingProcessInfo(pid, appName, svcName, startTime));
            }

            return list;
        }
        catch
        {
            return Array.Empty<FileLockingProcessInfo>();
        }
        finally
        {
            // Гарантированное закрытие сессии Restart Manager
            _ = RmEndSession(sessionHandle);
        }
    }

    /// <summary>
    /// Преобразует нативную структуру FILETIME в DateTime UTC.
    /// </summary>
    private static DateTime? ConvertFileTimeToUtc(FILETIME fileTime)
    {
        long high = (long)fileTime.dwHighDateTime << 32;
        long low = (uint)fileTime.dwLowDateTime;
        long combined = high | low;

        if (combined <= 0)
        {
            return null;
        }

        try
        {
            return DateTime.FromFileTimeUtc(combined);
        }
        catch
        {
            return null;
        }
    }
}
