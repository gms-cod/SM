using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StartupMonitor.Service.Services;

/// <summary>
/// Высокоскоростной помощник сопоставления процессов со службами Windows через Win32 API EnumServicesStatusEx.
/// Позволяет за ~10 мс сопоставить все PID запущенных служб и получить их читаемые имена.
/// </summary>
public static class ServiceProcessHelper
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    /// <summary>OpenSCManager: Выполняет операцию OpenSCManager.</summary>
    /// <param name="lpMachineName">Входные данные: значение параметра lpMachineName (string?).</param>
    /// <param name="lpDatabaseName">Входные данные: значение параметра lpDatabaseName (string?).</param>
    /// <param name="dwDesiredAccess">Входные данные: значение параметра dwDesiredAccess (uint).</param>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool EnumServicesStatusEx(
        IntPtr hSCManager,
        int infoLevel,
        uint dwServiceType,
        uint dwServiceState,
        IntPtr lpServices,
        uint cbBufSize,
        out uint pcbBytesNeeded,
        out uint lpServicesReturned,
        ref uint lpResumeHandle,
        string? pszGroupName);

    [DllImport("advapi32.dll", SetLastError = true)]
    /// <summary>CloseServiceHandle: Выполняет операцию CloseServiceHandle.</summary>
    /// <param name="hSCObject">Входные данные: значение параметра hSCObject (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ENUM_SERVICE_STATUS_PROCESS
    {
        public string lpServiceName;
        public string lpDisplayName;
        public SERVICE_STATUS_PROCESS ServiceStatusProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    private const int SC_ENUM_PROCESS_INFO = 0;
    private const uint SERVICE_WIN32 = 0x00000030; // SERVICE_WIN32_OWN_PROCESS | SERVICE_WIN32_SHARE_PROCESS
    private const uint SERVICE_STATE_ALL = 0x00000003;

    /// <summary>
    /// Возвращает словарь [PID -> список отображаемых имен служб] для всех запущенных служб Windows.
    /// Выполняется практически мгновенно на уровне C-вызовов ОС.
    /// </summary>
    public static Dictionary<int, List<string>> GetRunningServicesByPid()
    {
        var result = new Dictionary<int, List<string>>();
        IntPtr hSCM = OpenSCManager(null, null, SC_MANAGER_ENUMERATE_SERVICE);
        if (hSCM == IntPtr.Zero) return result;

        try
        {
            uint bytesNeeded = 0;
            uint servicesReturned = 0;
            uint resumeHandle = 0;

            EnumServicesStatusEx(hSCM, SC_ENUM_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL,
                IntPtr.Zero, 0, out bytesNeeded, out servicesReturned, ref resumeHandle, null);

            if (bytesNeeded == 0) return result;

            IntPtr buffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (EnumServicesStatusEx(hSCM, SC_ENUM_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL,
                    buffer, bytesNeeded, out bytesNeeded, out servicesReturned, ref resumeHandle, null))
                {
                    int structSize = Marshal.SizeOf<ENUM_SERVICE_STATUS_PROCESS>();
                    for (int i = 0; i < servicesReturned; i++)
                    {
                        IntPtr currentPtr = new IntPtr(buffer.ToInt64() + (i * structSize));
                        var ssp = Marshal.PtrToStructure<ENUM_SERVICE_STATUS_PROCESS>(currentPtr);
                        int pid = (int)ssp.ServiceStatusProcess.dwProcessId;
                        if (pid > 0)
                        {
                            if (!result.TryGetValue(pid, out var list))
                            {
                                list = new List<string>();
                                result[pid] = list;
                            }
                            var name = !string.IsNullOrWhiteSpace(ssp.lpDisplayName) ? ssp.lpDisplayName : ssp.lpServiceName;
                            if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name))
                            {
                                list.Add(name);
                            }
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Защита от сбоев вызова SCM
        }
        finally
        {
            CloseServiceHandle(hSCM);
        }

        return result;
    }
}

