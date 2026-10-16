using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Shared.Models.Toolbox;
using StartupMonitor.Service.Services;

namespace StartupMonitor.Service.Services.Toolbox;

/// <summary>
/// Сервис поиска процессов, блокирующих файлы или папки (File Locksmith / Unlocker)
/// на основе Windows Restart Manager API (rstrtmgr.dll).
/// </summary>
public class FileLocksmithService
{
    private const int CchRmSessionKey = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint pSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    private const int ERROR_MORE_DATA = 234;
    private const int ERROR_SUCCESS = 0;

    /// <summary>
    /// Находит процессы, удерживающие блокировку файла или папки.
    /// </summary>
    public FileLockResultDto FindLockingProcesses(string targetPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new FileLockResultDto { TargetPath = targetPath };

        if (string.IsNullOrWhiteSpace(targetPath) || (!File.Exists(targetPath) && !Directory.Exists(targetPath)))
        {
            result.ErrorMessage = "Файл или каталог не найден.";
            return result;
        }

        uint sessionHandle = 0;
        string sessionKey = Guid.NewGuid().ToString("N");

        int res = RmStartSession(out sessionHandle, 0, sessionKey);
        if (res != ERROR_SUCCESS)
        {
            result.ErrorMessage = $"Не удалось инициализировать сессию Restart Manager (код {res}).";
            return result;
        }

        try
        {
            string[] resources = [targetPath];
            res = RmRegisterResources(sessionHandle, 1, resources, 0, null, 0, null);
            if (res != ERROR_SUCCESS)
            {
                result.ErrorMessage = $"Не удалось зарегистрировать ресурс (код {res}).";
                return result;
            }

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint rebootReasons = 0;

            res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref rebootReasons);
            if (res == ERROR_MORE_DATA && pnProcInfoNeeded > 0)
            {
                var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref rebootReasons);
                if (res == ERROR_SUCCESS)
                {
                    // Обогащение PID метаданными выполняется параллельно на всех ядрах.
                    // Индексная запись в массив сохраняет исходный порядок процессов.
                    var enriched = new FileLockProcessInfo?[pnProcInfo];

                    Parallel.For(0, (int)pnProcInfo, new ParallelOptions { CancellationToken = ct }, i =>
                    {
                        var info = processInfo[i];
                        int pid = info.Process.dwProcessId;
                        if (pid <= 0) return;

                        ct.ThrowIfCancellationRequested();

                        string procName = info.strAppName;
                        string procPath = string.Empty;

                        try
                        {
                            procPath = ProcessMetadataEnricher.TryGetProcessPath(pid);
                            if (string.IsNullOrEmpty(procName))
                            {
                                using var p = Process.GetProcessById(pid);
                                procName = p.ProcessName;
                            }
                        }
                        catch { }

                        enriched[i] = new FileLockProcessInfo
                        {
                            ProcessId = pid,
                            ProcessName = string.IsNullOrEmpty(procName) ? $"PID {pid}" : procName,
                            ProcessPath = procPath,
                            ApplicationType = info.ApplicationType.ToString()
                        };
                    });

                    foreach (var processInfoItem in enriched)
                    {
                        if (processInfoItem is not null)
                            result.LockingProcesses.Add(processInfoItem);
                    }

                    result.IsLocked = result.LockingProcesses.Count > 0;
                }
            }
            else if (res == ERROR_SUCCESS)
            {
                result.IsLocked = false;
            }
            else
            {
                result.ErrorMessage = $"Ошибка получения списка блокирующих процессов (код {res}).";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            RmEndSession(sessionHandle);
        }

        return result;
    }

    /// <summary>
    /// Асинхронная версия <see cref="FindLockingProcesses"/> с поддержкой отмены.
    /// Позволяет прервать операцию при уходе пользователя с вкладки Toolbox.
    /// </summary>
    public Task<FileLockResultDto> FindLockingProcessesAsync(string targetPath, CancellationToken ct = default)
        => Task.Run(() => FindLockingProcesses(targetPath, ct), ct);
}

