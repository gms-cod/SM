using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StartupMonitor.Service.Services.AntiRansomware;

/// <summary>
/// Результат выполнения операции заморозки или разморозки процесса в ядре.
/// </summary>
/// <param name="Success">Признак успешности операции.</param>
/// <param name="ErrorMessage">Текст ошибки в случае сбоя или null при успехе.</param>
/// <param name="CreationTimeUtc">Точное время создания процесса в формате UTC для защиты от PID Reuse.</param>
public record FreezeResult(bool Success, string? ErrorMessage = null, DateTime? CreationTimeUtc = null);

/// <summary>
/// Сервис мгновенной заморозки (Suspend) и разморозки (Resume) процессов в ядре Windows.
/// Использует системные вызовы ntdll!NtSuspendProcess / ntdll!NtResumeProcess с защитой от PID Reuse.
/// </summary>
public static class ProcessFreezer
{
    private const uint ProcessSuspendResume = 0x0800;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessTerminate = 0x0001;

    /// <summary>
    /// Вызов системной функции ядра для мгновенной приостановки всех потоков процесса.
    /// </summary>
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    /// <summary>
    /// Вызов системной функции ядра для возобновления всех потоков процесса.
    /// </summary>
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtResumeProcess(IntPtr processHandle);

    /// <summary>
    /// Открывает дескриптор существующего процесса с заданными правами доступа.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint processAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int processId);

    /// <summary>
    /// Извлекает временные метки жизненного цикла процесса.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle hProcess,
        out long lpCreationTime,
        out long lpExitTime,
        out long lpKernelTime,
        out long lpUserTime);

    /// <summary>
    /// Принудительно завершает процесс.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);

    /// <summary>
    /// Мгновенно замораживает процесс в ядре Windows через ntdll!NtSuspendProcess.
    /// Включает проверку CreationTime для защиты от PID Reuse (подмены процесса новым при совпадении PID).
    /// </summary>
    /// <param name="processId">Идентификатор процесса (PID).</param>
    /// <param name="expectedCreationTimeUtc">Ожидаемое время создания процесса (если известно) для верификации подлинности PID.</param>
    /// <returns>Результат выполнения операции.</returns>
    public static FreezeResult SuspendProcess(int processId, DateTime? expectedCreationTimeUtc = null)
    {
        if (processId <= 4)
        {
            return new FreezeResult(false, $"Заморозка критического системного процесса PID={processId} запрещена.");
        }

        uint desiredAccess = ProcessSuspendResume | ProcessQueryLimitedInformation;
        using SafeProcessHandle handle = OpenProcess(desiredAccess, false, processId);
        if (handle.IsInvalid)
        {
            int lastError = Marshal.GetLastWin32Error();
            return new FreezeResult(false, $"Не удалось открыть процесс PID={processId} для заморозки (Win32: {lastError}).");
        }

        // Защита от PID Reuse: проверяем время запуска процесса
        DateTime? actualCreationTime = ReadCreationTime(handle);
        if (expectedCreationTimeUtc.HasValue && actualCreationTime.HasValue)
        {
            if (Math.Abs((actualCreationTime.Value - expectedCreationTimeUtc.Value).TotalSeconds) > 3.0)
            {
                return new FreezeResult(
                    false,
                    $"Обнаружен PID Reuse для PID={processId}. Ожидаемое время: {expectedCreationTimeUtc.Value:O}, фактическое: {actualCreationTime.Value:O}.",
                    actualCreationTime);
            }
        }

        int status = NtSuspendProcess(handle.DangerousGetHandle());
        if (status < 0)
        {
            return new FreezeResult(false, $"NtSuspendProcess завершился со сбоем NTSTATUS: 0x{status:X8}.", actualCreationTime);
        }

        return new FreezeResult(true, null, actualCreationTime);
    }

    /// <summary>
    /// Размораживает ранее замороженный процесс в ядре Windows через ntdll!NtResumeProcess.
    /// </summary>
    /// <param name="processId">Идентификатор процесса (PID).</param>
    /// <param name="expectedCreationTimeUtc">Ожидаемое время создания процесса для защиты от PID Reuse.</param>
    /// <returns>Результат выполнения операции.</returns>
    public static FreezeResult ResumeProcess(int processId, DateTime? expectedCreationTimeUtc = null)
    {
        if (processId <= 4)
        {
            return new FreezeResult(false, $"Разморозка системного процесса PID={processId} не требуется.");
        }

        uint desiredAccess = ProcessSuspendResume | ProcessQueryLimitedInformation;
        using SafeProcessHandle handle = OpenProcess(desiredAccess, false, processId);
        if (handle.IsInvalid)
        {
            int lastError = Marshal.GetLastWin32Error();
            return new FreezeResult(false, $"Не удалось открыть процесс PID={processId} для разморозки (Win32: {lastError}).");
        }

        DateTime? actualCreationTime = ReadCreationTime(handle);
        if (expectedCreationTimeUtc.HasValue && actualCreationTime.HasValue)
        {
            if (Math.Abs((actualCreationTime.Value - expectedCreationTimeUtc.Value).TotalSeconds) > 3.0)
            {
                return new FreezeResult(
                    false,
                    $"Обнаружен PID Reuse при разморозке PID={processId}. Процесс был заменён ОС.",
                    actualCreationTime);
            }
        }

        int status = NtResumeProcess(handle.DangerousGetHandle());
        if (status < 0)
        {
            return new FreezeResult(false, $"NtResumeProcess завершился со сбоем NTSTATUS: 0x{status:X8}.", actualCreationTime);
        }

        return new FreezeResult(true, null, actualCreationTime);
    }

    /// <summary>
    /// Получает точное время создания процесса в UTC по его PID.
    /// </summary>
    /// <param name="processId">Идентификатор процесса.</param>
    /// <returns>Время создания в UTC либо null, если процесс завершился или доступ закрыт.</returns>
    public static DateTime? GetProcessCreationTime(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using SafeProcessHandle handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle.IsInvalid)
            {
                return null;
            }

            return ReadCreationTime(handle);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Принудительно завершает процесс (Kill) с предварительной проверкой подлинности PID.
    /// </summary>
    /// <param name="processId">Идентификатор процесса.</param>
    /// <param name="expectedCreationTimeUtc">Ожидаемое время создания для исключения непреднамеренного завершения чужого процесса.</param>
    /// <returns>True, если процесс был успешно завершён.</returns>
    public static bool ForceKillProcess(int processId, DateTime? expectedCreationTimeUtc = null)
    {
        if (processId <= 4)
        {
            return false;
        }

        try
        {
            uint desiredAccess = ProcessTerminate | ProcessQueryLimitedInformation;
            using SafeProcessHandle handle = OpenProcess(desiredAccess, false, processId);
            if (handle.IsInvalid)
            {
                return false;
            }

            DateTime? actualCreationTime = ReadCreationTime(handle);
            if (expectedCreationTimeUtc.HasValue && actualCreationTime.HasValue)
            {
                if (Math.Abs((actualCreationTime.Value - expectedCreationTimeUtc.Value).TotalSeconds) > 3.0)
                {
                    return false;
                }
            }

            return TerminateProcess(handle, 1);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Считывает точное время создания процесса из дескриптора.
    /// </summary>
    private static DateTime? ReadCreationTime(SafeProcessHandle handle)
    {
        if (GetProcessTimes(handle, out long creationTime, out _, out _, out _))
        {
            if (creationTime > 0)
            {
                try
                {
                    return DateTime.FromFileTimeUtc(creationTime);
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }
}
