using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
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
public class ProcessMetadataEnricher : IDisposable, IAsyncDisposable
{
    // Кэш по ключу (PID, StartTimeTicks)
    private readonly ConcurrentDictionary<(int Pid, long StartTimeTicks), CachedProcessMetadata> _cache = new();
    // Очередь процессов, требующих обогащения метаданными
    /// <summary>Ограниченная очередь будит единственный worker только при поступлении работы.</summary>
    private readonly Channel<ProcessInfo> _enrichQueue = Channel.CreateBounded<ProcessInfo>(new BoundedChannelOptions(1024)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    /// <summary>Защищает отмену и освобождение CTS от повторного Dispose через владельцев DI.</summary>
    private int _disposed;

    /// <summary>ProcessMetadataEnricher: Выполняет операцию ProcessMetadataEnricher.</summary>
    public ProcessMetadataEnricher()
    {
        var ct = _cts.Token;
        _workerTask = Task.Run(() => ProcessEnrichmentLoopAsync(ct));
    }

    /// <summary>Применяет метаданные из кэша (мгновенно). Если в кэше нет — ставит в Time-Sliced очередь.</summary>
    public void EnrichOrQueue(ProcessInfo proc, IntPtr processHandle)
    {
        ArgumentNullException.ThrowIfNull(proc);
        if (Volatile.Read(ref _disposed) != 0) return;
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
        if (!_enrichQueue.Writer.TryWrite(proc))
            Serilog.Log.Debug("Очередь метаданных заполнена или остановлена; процесс {Pid} будет проверен при следующем снимке", proc.ProcessId);
    }

    /// <summary>Обрабатывает очередь порциями; пустой канал не создаёт периодических пробуждений CPU.</summary>
    /// <param name="ct">Захваченный токен жизненного цикла worker, независимый от освобождения общего CTS.</param>
    /// <returns>Задача завершения обработчика.</returns>
    private async Task ProcessEnrichmentLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await _enrichQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) break;
                int processedInBatch = 0;
                while (processedInBatch < 5 && _enrichQueue.Reader.TryRead(out var proc))
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
                        catch (Exception ex)
                        {
                            // Диагностика доступна только при включённом Debug; командная строка процесса не журналируется.
                            Serilog.Log.Debug(ex, "Не удалось прочитать описание файла процесса PID {Pid}", proc.ProcessId);
                        }
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
                if (processedInBatch > 0)
                    await Task.Delay(30, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Serilog.Log.Debug("Обогащение метаданных остановлено");
                break;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Не удалось обработать элемент очереди метаданных процессов");
            }
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

    /// <summary>Читает имя издателя из версии файла; недоступность метаданных отмечается на уровне Debug.</summary>
    /// <param name="filePath">Путь исполняемого файла.</param>
    /// <returns>Имя издателя либо пустая строка при отсутствии или недоступности метаданных.</returns>
    private static string TryGetCompanyName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return string.Empty;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            return vi.CompanyName ?? string.Empty;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Не удалось прочитать издателя файла {FilePath}", filePath);
            return string.Empty;
        }
    }

    /// <summary>TryGetProcessPath: Выполняет операцию TryGetProcessPath.</summary>
    /// <param name="pid">Входные данные: значение параметра pid (int).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
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

    /// <summary>GetProcessPathFromHandle: Возвращает ProcessPathFromHandle.</summary>
    /// <param name="hProcess">Входные данные: значение параметра hProcess (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
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

    /// <summary>Читает командную строку по дескриптору; отказы диагностирует без раскрытия её содержимого.</summary>
    /// <param name="hProcess">Дескриптор процесса с правом запроса сведений.</param>
    /// <returns>Командная строка либо пустая строка, если завершившийся или защищённый процесс недоступен.</returns>
    private static string TryGetProcessCommandLine(IntPtr hProcess)
    {
        if (hProcess == IntPtr.Zero) return string.Empty;
        try
        {
            // ProcessCommandLineInformation = 60
            int status = NtQueryInformationProcess(hProcess, 60, IntPtr.Zero, 0, out int returnLength);
            if (returnLength <= 0)
            {
                // Ожидаемые отказы защищённых и завершившихся процессов видны лишь в подробном Trace-журнале.
                Serilog.Log.Verbose("Размер командной строки процесса недоступен: NTSTATUS {Status:X8}", status);
                return string.Empty;
            }

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
                else
                {
                    Serilog.Log.Verbose("Не удалось запросить командную строку процесса: NTSTATUS {Status:X8}", status);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Ошибка чтения метаданных командной строки процесса по дескриптору");
        }
        return string.Empty;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>OpenProcess: Выполняет операцию OpenProcess.</summary>
    /// <param name="processAccess">Входные данные: значение параметра processAccess (int).</param>
    /// <param name="bInheritHandle">Входные данные: значение параметра bInheritHandle (bool).</param>
    /// <param name="processId">Входные данные: значение параметра processId (int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>CloseHandle: Выполняет операцию CloseHandle.</summary>
    /// <param name="hObject">Входные данные: значение параметра hObject (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    /// <summary>QueryFullProcessImageName: Выполняет операцию QueryFullProcessImageName.</summary>
    /// <param name="hProcess">Входные данные: значение параметра hProcess (IntPtr).</param>
    /// <param name="flags">Входные данные: значение параметра flags (int).</param>
    /// <param name="lpExeName">Входные данные: значение параметра lpExeName ([Out] StringBuilder).</param>
    /// <param name="lpdwSize">Входные данные: значение параметра lpdwSize (ref int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, [Out] StringBuilder lpExeName, ref int lpdwSize);
    #endregion

    /// <summary>Идемпотентно останавливает worker и освобождает CTS после завершения его использования.</summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Отменяет единственный обработчик до освобождения токена; повторный вызов безопасен.</summary>
    /// <returns>Задача завершения отмены и освобождения ресурсов.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _enrichQueue.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await _workerTask.ConfigureAwait(false); }
        finally { _cts.Dispose(); }
    }
}

