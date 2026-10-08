using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services.ProcessInspector;

/// <summary>
/// Высокопроизводительный сервис инспекции загруженных динамических модулей (DLL) и открытых дескрипторов (Handles) процессов Windows.
/// </summary>
public class ProcessInspectorService
{
    private static readonly string TempPath = Path.GetTempPath().TrimEnd('\\', '/');
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');
    private static readonly string System32 = Environment.GetFolderPath(Environment.SpecialFolder.System).TrimEnd('\\', '/');
    private static readonly string SysWow64 = Path.Combine(WinDir, "SysWOW64");

    /// <summary>
    /// Выполняет детальную инспекцию процесса: список загруженных DLL с верификацией подписей и список открытых дескрипторов.
    /// </summary>
    public ProcessModulesResponse InspectProcess(int processId, bool includeHandles = true)
    {
        using var _prof = ExecutionProfiler.Measure("Service.Inspector.InspectProcess");
        var response = new ProcessModulesResponse
        {
            ProcessId = processId
        };

        if (processId <= 0)
        {
            response.ErrorMessage = "Неверный идентификатор процесса (PID).";
            return response;
        }

        try
        {
            using var proc = Process.GetProcessById(processId);
            response.ProcessName = proc.ProcessName;
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"Не удалось открыть процесс PID {processId}: {ex.Message}";
            return response;
        }

        // 1. Получение загруженных модулей (DLL)
        try
        {
            response.Modules = CollectModules(processId);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"Ошибка сбора модулей: {ex.Message}";
        }

        // 2. Получение дескрипторов (Handles), если запрошено
        if (includeHandles)
        {
            try
            {
                response.Handles = CollectHandles(processId);
            }
            catch (Exception ex)
            {
                // Не ломаем ответ, если только дескрипторы не удалось прочитать
                Trace.WriteLine($"[ProcessInspector] Ошибка сбора дескрипторов PID {processId}: {ex.Message}");
            }
        }

        return response;
    }

    #region Module Enumeration

    private List<ProcessModuleInfo> CollectModules(int processId)
    {
        var modulesDict = new Dictionary<string, ProcessModuleInfo>(StringComparer.OrdinalIgnoreCase);

        // Основной метод: CreateToolhelp32Snapshot
        IntPtr hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)processId);
        if (hSnapshot != IntPtr.Zero && hSnapshot != INVALID_HANDLE_VALUE)
        {
            try
            {
                var me = new MODULEENTRY32();
                me.dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>();

                if (Module32First(hSnapshot, ref me))
                {
                    do
                    {
                        var info = CreateModuleInfo(me.szModule, me.szExePath, me.modBaseAddr, me.modBaseSize);
                        if (!string.IsNullOrEmpty(info.ModuleName))
                        {
                            modulesDict[info.ModuleName] = info;
                        }
                    }
                    while (Module32Next(hSnapshot, ref me));
                }
            }
            finally
            {
                CloseHandle(hSnapshot);
            }
        }

        // Фоллбэк: если снимок вернул пустоту (например, WoW64 или PPL), используем Process.Modules
        if (modulesDict.Count == 0)
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                foreach (ProcessModule pm in proc.Modules)
                {
                    if (pm != null && !string.IsNullOrEmpty(pm.ModuleName) && !modulesDict.ContainsKey(pm.ModuleName))
                    {
                        var info = CreateModuleInfo(pm.ModuleName, pm.FileName, pm.BaseAddress, (uint)pm.ModuleMemorySize);
                        modulesDict[info.ModuleName] = info;
                    }
                }
            }
            catch { }
        }

        var list = new List<ProcessModuleInfo>(modulesDict.Values);
        // Сортировка: сначала модули с подозрительными путями (Temp), затем по имени
        list.Sort((a, b) =>
        {
            if (a.IsTempPath != b.IsTempPath)
                return b.IsTempPath ? 1 : -1;
            return string.Compare(a.ModuleName, b.ModuleName, StringComparison.OrdinalIgnoreCase);
        });

        return list;
    }

    private ProcessModuleInfo CreateModuleInfo(string? moduleName, string? exePath, IntPtr baseAddress, uint moduleSize)
    {
        var info = new ProcessModuleInfo
        {
            ModuleName = moduleName ?? string.Empty,
            FilePath = exePath ?? string.Empty,
            BaseAddress = $"0x{baseAddress.ToInt64():X16}",
            ModuleSize = moduleSize
        };

        if (string.IsNullOrEmpty(info.ModuleName) && !string.IsNullOrEmpty(info.FilePath))
        {
            info.ModuleName = Path.GetFileName(info.FilePath);
        }

        if (!string.IsNullOrEmpty(info.FilePath) && File.Exists(info.FilePath))
        {
            // Обогащение версией и описанием
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(info.FilePath);
                info.FileVersion = vi.FileVersion;
                info.FileDescription = vi.FileDescription;
                info.CompanyName = vi.CompanyName;
            }
            catch { }

            // Криптографическая верификация Authenticode с локальным кэшированием
            try
            {
                var sig = AuthenticodeVerifier.VerifyFile(info.FilePath);
                info.IsSigned = sig.IsSigned;
                info.SignatureStatus = sig.Status;
                info.SignerName = sig.SignerName;
                info.IssuerName = sig.IssuerName;
            }
            catch
            {
                info.SignatureStatus = DigitalSignatureStatus.Unknown;
            }

            // Анализ расположения
            info.IsTempPath = IsPathInTemp(info.FilePath);
            info.IsSystem = IsSystemPath(info.FilePath);
        }

        return info;
    }

    private static bool IsPathInTemp(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Replace('/', '\\');

        if (p.StartsWith(TempPath, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Contains(@"\AppData\Local\Temp", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Contains(@"\Windows\Temp", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Contains(@"\Users\Public", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Contains(@"\PerfLogs", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static bool IsSystemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Replace('/', '\\');

        if (p.StartsWith(System32, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.StartsWith(SysWow64, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Contains(@"\Windows\WinSxS", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    #endregion

    #region Handle Enumeration

    private const int SystemExtendedHandleInformation = 64;
    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_SAME_ACCESS = 0x0002;
    private const int ObjectTypeInformation = 2;
    private const int ObjectNameInformation = 1;
    private const uint FILE_NAME_NORMALIZED = 0x0;

    private List<ProcessHandleInfo> CollectHandles(int processId)
    {
        var result = new List<ProcessHandleInfo>(128);

        // Открываем процесс с правом дублирования дескрипторов
        IntPtr hProcess = OpenProcess(PROCESS_DUP_HANDLE, false, (uint)processId);
        if (hProcess == IntPtr.Zero)
        {
            return result;
        }

        IntPtr currentProcess = GetCurrentProcess();

        try
        {
            // Выделяем буфер под системные дескрипторы
            int length = 0x200000; // 2 MB
            IntPtr ptr = Marshal.AllocHGlobal(length);

            try
            {
                uint returnLength = 0;
                uint status;

                while ((status = NtQuerySystemInformation(SystemExtendedHandleInformation, ptr, (uint)length, out returnLength)) == STATUS_INFO_LENGTH_MISMATCH)
                {
                    Marshal.FreeHGlobal(ptr);
                    length = (int)Math.Max(length * 2, returnLength + 0x40000);
                    ptr = Marshal.AllocHGlobal(length);
                }

                if (status == 0) // STATUS_SUCCESS
                {
                    long handleCount = Marshal.ReadInt64(ptr);
                    IntPtr handleEntryPtr = new IntPtr(ptr.ToInt64() + 2 * IntPtr.Size);
                    int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();

                    var targetPidUlong = (ulong)processId;
                    int addedCount = 0;
                    const int maxHandlesToReturn = 1000;
                    var sw = Stopwatch.StartNew();

                    for (long i = 0; i < handleCount && addedCount < maxHandlesToReturn; i++)
                    {
                        if (sw.ElapsedMilliseconds > 1500) break; // Защита от длительных задержек

                        var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(handleEntryPtr);
                        handleEntryPtr = new IntPtr(handleEntryPtr.ToInt64() + entrySize);

                        if (entry.UniqueProcessId == targetPidUlong)
                        {
                            IntPtr hDup = IntPtr.Zero;
                            if (DuplicateHandle(hProcess, entry.HandleValue, currentProcess, out hDup, 0, false, DUPLICATE_SAME_ACCESS))
                            {
                                try
                                {
                                    var handleInfo = QueryHandleDetails(hDup, entry.HandleValue, entry.GrantedAccess);
                                    if (handleInfo != null && !string.IsNullOrEmpty(handleInfo.Name))
                                    {
                                        result.Add(handleInfo);
                                        addedCount++;
                                    }
                                }
                                finally
                                {
                                    CloseHandle(hDup);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }

        return result;
    }

    private ProcessHandleInfo? QueryHandleDetails(IntPtr hDup, IntPtr originalHandle, uint grantedAccess)
    {
        var info = new ProcessHandleInfo
        {
            HandleValue = $"0x{originalHandle.ToInt64():X8}",
            GrantedAccess = grantedAccess
        };

        // 1. Проверяем тип дескриптора через GetFileType
        uint fileType = GetFileType(hDup);
        if (fileType == 1) // FILE_TYPE_DISK: дисковый файл, безопасный вызов GetFinalPathNameByHandle
        {
            var sbPath = new StringBuilder(512);
            uint pathLen = GetFinalPathNameByHandle(hDup, sbPath, (uint)sbPath.Capacity, FILE_NAME_NORMALIZED);
            if (pathLen > 0 && pathLen < sbPath.Capacity)
            {
                info.HandleType = "File";
                string pathStr = sbPath.ToString();
                if (pathStr.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    info.Name = @"\\" + pathStr.Substring(8);
                else if (pathStr.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                    info.Name = pathStr.Substring(4);
                else
                    info.Name = pathStr;

                return info;
            }
            return null;
        }
        else if (fileType == 3) // FILE_TYPE_PIPE: именованный или анонимный канал — категорически не блокируем вызовом путей!
        {
            info.HandleType = "Pipe";
            info.Name = "(Pipe/Socket)";
            return info;
        }
        else if (fileType == 2) // FILE_TYPE_CHAR: консоль или символьное устройство
        {
            info.HandleType = "Device";
            info.Name = "(CharacterDevice)";
            return info;
        }

        // 2. fileType == 0 (Unknown): может быть Key, Mutant, Section, Event и т.п.
        string typeName = GetObjectTypeName(hDup);
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        info.HandleType = typeName;

        // Для безопасных объектов ядра читаем системное имя
        if (typeName == "Key" || typeName == "Mutant" || typeName == "Section" || typeName == "Event" || typeName == "Directory")
        {
            info.Name = GetObjectName(hDup);
        }

        return string.IsNullOrEmpty(info.Name) ? null : info;
    }

    private static string GetObjectTypeName(IntPtr handle)
    {
        int length = 0x1000;
        IntPtr ptr = Marshal.AllocHGlobal(length);
        try
        {
            if (NtQueryObject(handle, ObjectTypeInformation, ptr, (uint)length, out _) == 0)
            {
                // Структура PUBLIC_OBJECT_TYPE_INFORMATION:
                // UNICODE_STRING TypeName (ushort Length, ushort MaximumLength, IntPtr Buffer)
                var nameUni = Marshal.PtrToStructure<UNICODE_STRING>(ptr);
                if (nameUni.Buffer != IntPtr.Zero && nameUni.Length > 0)
                {
                    return Marshal.PtrToStringUni(nameUni.Buffer, nameUni.Length / 2) ?? string.Empty;
                }
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return string.Empty;
    }

    private static string GetObjectName(IntPtr handle)
    {
        int length = 0x2000;
        IntPtr ptr = Marshal.AllocHGlobal(length);
        try
        {
            if (NtQueryObject(handle, ObjectNameInformation, ptr, (uint)length, out _) == 0)
            {
                var nameUni = Marshal.PtrToStructure<UNICODE_STRING>(ptr);
                if (nameUni.Buffer != IntPtr.Zero && nameUni.Length > 0)
                {
                    return Marshal.PtrToStringUni(nameUni.Buffer, nameUni.Length / 2) ?? string.Empty;
                }
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return string.Empty;
    }

    #endregion

    #region Win32 P/Invoke

    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr hFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint GetFinalPathNameByHandle(IntPtr hFile, [Out] StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int query, IntPtr dataPtr, uint size, out uint returnedSize);

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(IntPtr handle, int query, IntPtr dataPtr, uint size, out uint returnedSize);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public ulong UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    #endregion
}

