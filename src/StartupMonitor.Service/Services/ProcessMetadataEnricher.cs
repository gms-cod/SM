using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services;

/// <summary>Кэшированные неизменяемые метаданные процесса (путь, аргументы, издатель, признак системы, цифровая подпись).</summary>
public record CachedProcessMetadata(
    string FilePath,
    string CommandLine,
    string Publisher,
    int ParentProcessId,
    bool IsSystemProcess,
    string Description,
    CliThreatInfo? ThreatInfo = null,
    bool IsSigned = false,
    DigitalSignatureStatus SignatureStatus = DigitalSignatureStatus.Unknown,
    string? SignerName = null,
    string? SignatureIssuer = null
);

/// <summary>Сервис обогащения метаданных процессов с кэшированием и Time-Sliced квантованием (защита от скачков CPU).</summary>
public class ProcessMetadataEnricher : IDisposable
{
    // Кэш по ключу (PID, StartTimeTicks)
    private readonly ConcurrentDictionary<(int Pid, long StartTimeTicks), CachedProcessMetadata> _cache = new();
    // Очередь процессов, требующих обогащения метаданными
    private readonly ConcurrentQueue<ProcessInfo> _enrichQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;

    public ProcessMetadataEnricher()
    {
        _workerTask = Task.Run(ProcessEnrichmentLoopAsync);
    }

    /// <summary>Применяет метаданные из кэша (мгновенно). Если в кэше нет — ставит в Time-Sliced очередь.</summary>
    public void EnrichOrQueue(ProcessInfo proc, IntPtr processHandle)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ProcessEnrich.EnrichOrQueue");
        var key = (proc.ProcessId, proc.StartTime.Ticks);
        if (_cache.TryGetValue(key, out var cached))
        {
            proc.FilePath = cached.FilePath;
            proc.CommandLine = cached.CommandLine;
            proc.Publisher = cached.Publisher;
            proc.ParentProcessId = cached.ParentProcessId;
            proc.IsSystemProcess = cached.IsSystemProcess;
            proc.IsSigned = cached.IsSigned;
            proc.SignatureStatus = cached.SignatureStatus;
            proc.SignerName = cached.SignerName;
            proc.SignatureIssuer = cached.SignatureIssuer;
            if (string.IsNullOrEmpty(proc.Description) && !string.IsNullOrEmpty(cached.Description))
            {
                proc.Description = cached.Description;
            }
            if (proc.ThreatInfo == null && cached.ThreatInfo != null)
            {
                proc.ThreatInfo = cached.ThreatInfo;
            }
            return;
        }

        // Если процесс системный по имени — сразу помечаем
        if (EntryOperations.IsProtectedProcessName(proc.ProcessName))
        {
            proc.IsSystemProcess = true;
        }

        // Пытаемся быстро считать путь и аргументы через открытый дескриптор или по PID
        if (processHandle != IntPtr.Zero)
        {
            if (string.IsNullOrEmpty(proc.FilePath))
            {
                proc.FilePath = GetProcessPathFromHandle(processHandle);
            }
            if (proc.ParentProcessId == 0)
            {
                proc.ParentProcessId = ProcessStartupLinker.GetParentProcessId(processHandle);
            }
            if (string.IsNullOrEmpty(proc.CommandLine))
            {
                proc.CommandLine = TryGetProcessCommandLine(processHandle);
            }
        }
        else if (string.IsNullOrEmpty(proc.FilePath) && proc.ProcessId > 4)
        {
            proc.FilePath = TryGetProcessPath(proc.ProcessId);
        }

        if (proc.ThreatInfo == null || !proc.ThreatInfo.HasThreat)
        {
            var threat = StartupMonitor.Shared.Helpers.CliHeuristicAnalyzer.Analyze(proc.CommandLine, proc.FilePath);
            if (threat.HasThreat)
            {
                proc.ThreatInfo = threat;
            }
        }

        if (!string.IsNullOrEmpty(proc.FilePath))
        {
            _cache[key] = new CachedProcessMetadata(
                proc.FilePath,
                proc.CommandLine,
                proc.Publisher,
                proc.ParentProcessId,
                proc.IsSystemProcess,
                proc.Description,
                proc.ThreatInfo
            );
        }

        // Ставим в очередь на глубокое обогащение (цифровая подпись Authenticode)
        _enrichQueue.Enqueue(proc);
    }

    /// <summary>Фоновый сглаживающий цикл обработки очереди порциями по 5 процессов за такт с микропаузами.</summary>
    private async Task ProcessEnrichmentLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                int processedInBatch = 0;
                while (processedInBatch < 5 && _enrichQueue.TryDequeue(out var proc))
                {
                    var key = (proc.ProcessId, proc.StartTime.Ticks);
                    if (_cache.TryGetValue(key, out var cached))
                    {
                        proc.FilePath = cached.FilePath;
                        proc.CommandLine = cached.CommandLine;
                        proc.Publisher = cached.Publisher;
                        proc.ParentProcessId = cached.ParentProcessId;
                        proc.IsSystemProcess = cached.IsSystemProcess;
                        if (string.IsNullOrEmpty(proc.Description) && !string.IsNullOrEmpty(cached.Description))
                        {
                            proc.Description = cached.Description;
                        }
                        continue;
                    }

                    // Если путь ещё не определён, пытаемся открыть процесс
                    if (string.IsNullOrEmpty(proc.FilePath))
                    {
                        proc.FilePath = TryGetProcessPath(proc.ProcessId);
                    }

                    // Проверяем цифровую подпись Authenticode через WinVerifyTrust
                    if (!string.IsNullOrEmpty(proc.FilePath))
                    {
                        var sig = AuthenticodeVerifier.VerifyFile(proc.FilePath);
                        proc.IsSigned = sig.IsSigned;
                        proc.SignatureStatus = sig.Status;
                        proc.SignerName = sig.SignerName;
                        proc.SignatureIssuer = sig.IssuerName;

                        if (!string.IsNullOrEmpty(sig.SignerName))
                        {
                            proc.Publisher = sig.SignerName;
                        }
                        else if (string.IsNullOrEmpty(proc.Publisher))
                        {
                            proc.Publisher = TryGetCompanyName(proc.FilePath);
                        }
                    }

                    // Если описание ещё не задано (не служба), считываем FileDescription из метаданных файла
                    if (string.IsNullOrEmpty(proc.Description) && !string.IsNullOrEmpty(proc.FilePath))
                    {
                        try
                        {
                            if (File.Exists(proc.FilePath))
                            {
                                var vi = FileVersionInfo.GetVersionInfo(proc.FilePath);
                                if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                                {
                                    proc.Description = vi.FileDescription.Trim();
                                }
                            }
                        }
                        catch { }
                    }

                    // Проверяем защищенность процесса
                    bool isProtected = EntryOperations.IsProtectedProcessName(proc.ProcessName);
                    proc.IsSystemProcess = isProtected;

                    // Сохраняем в кэш
                    var metadata = new CachedProcessMetadata(
                        proc.FilePath,
                        proc.CommandLine,
                        proc.Publisher,
                        proc.ParentProcessId,
                        proc.IsSystemProcess,
                        proc.Description,
                        proc.ThreatInfo,
                        proc.IsSigned,
                        proc.SignatureStatus,
                        proc.SignerName,
                        proc.SignatureIssuer
                    );
                    _cache[key] = metadata;
                    processedInBatch++;
                }

                // Микропауза между квантами для сглаживания нагрузки на CPU
                await Task.Delay(processedInBatch > 0 ? 30 : 200, _cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(200, _cts.Token); }
        }
    }

    /// <summary>Очищает устаревшие записи из кэша для завершившихся процессов.</summary>
    public void PruneDeadProcesses(HashSet<int> activePids)
    {
        foreach (var key in _cache.Keys)
        {
            if (!activePids.Contains(key.Pid))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private static string TryGetCompanyName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return string.Empty;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            return vi.CompanyName ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    public static string TryGetProcessPath(int pid)
    {
        IntPtr hProcess = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (hProcess == IntPtr.Zero) return string.Empty;
        try
        {
            return GetProcessPathFromHandle(hProcess);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private static string GetProcessPathFromHandle(IntPtr hProcess)
    {
        var sb = new StringBuilder(1024);
        int size = sb.Capacity;
        if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
        {
            return sb.ToString();
        }
        return string.Empty;
    }

    #region P/Invoke
    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    private static string TryGetProcessCommandLine(IntPtr hProcess)
    {
        if (hProcess == IntPtr.Zero) return string.Empty;
        try
        {
            // ProcessCommandLineInformation = 60
            int status = NtQueryInformationProcess(hProcess, 60, IntPtr.Zero, 0, out int returnLength);
            if (returnLength <= 0) return string.Empty;

            IntPtr buf = Marshal.AllocHGlobal(returnLength);
            try
            {
                status = NtQueryInformationProcess(hProcess, 60, buf, returnLength, out _);
                if (status == 0)
                {
                    var uniStr = Marshal.PtrToStructure<UNICODE_STRING>(buf);
                    if (uniStr.Length > 0 && uniStr.Buffer != IntPtr.Zero)
                    {
                        return Marshal.PtrToStringUni(uniStr.Buffer, uniStr.Length / 2) ?? string.Empty;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        catch { }
        return string.Empty;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, [Out] StringBuilder lpExeName, ref int lpdwSize);
    #endregion

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

