using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.Delayed;

/// <summary>
/// Обеспечивает запуск приложений в активной интерактивной сессии пользователя Windows
/// из контекста системной службы (Session 0, NT AUTHORITY\SYSTEM).
/// Использует Win32 API WTSQueryUserToken, DuplicateTokenEx, CreateEnvironmentBlock и CreateProcessAsUser.
/// При запуске вне службы или в тестах использует безопасный фолбэк Process.Start.
/// </summary>
public static class UserSessionLauncher
{
    private const uint WTS_CURRENT_SESSION = 0xFFFFFFFF;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int SecurityIdentification = 2;
    private const int TokenPrimary = 1;

    private const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>WTSGetActiveConsoleSessionId: Выполняет операцию WTSGetActiveConsoleSessionId.</summary>
    /// <returns>Выходные данные: результирующее значение типа extern uint.</returns>
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    /// <summary>WTSQueryUserToken: Выполняет операцию WTSQueryUserToken.</summary>
    /// <param name="sessionId">Входные данные: значение параметра sessionId (uint).</param>
    /// <param name="phToken">Входные данные: значение параметра phToken (out IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    /// <summary>CreateEnvironmentBlock: Выполняет операцию CreateEnvironmentBlock.</summary>
    /// <param name="lpEnvironment">Входные данные: значение параметра lpEnvironment (out IntPtr).</param>
    /// <param name="hToken">Входные данные: значение параметра hToken (IntPtr).</param>
    /// <param name="bInherit">Входные данные: значение параметра bInherit (bool).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    /// <summary>DestroyEnvironmentBlock: Выполняет операцию DestroyEnvironmentBlock.</summary>
    /// <param name="lpEnvironment">Входные данные: значение параметра lpEnvironment (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>CloseHandle: Выполняет операцию CloseHandle.</summary>
    /// <param name="hObject">Входные данные: значение параметра hObject (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Разбирает и подготавливает исполняемый путь и командную строку.
    /// </summary>
    public static (string ExePath, string FullCommandLine, string? WorkingDir) PrepareExecutionTarget(string? filePath, string? commandLine)
    {
        var rawTarget = !string.IsNullOrWhiteSpace(commandLine) ? commandLine.Trim() : (filePath?.Trim() ?? string.Empty);

        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            return (string.Empty, string.Empty, null);
        }

        // Если это ярлык .lnk, определяем рабочую папку
        string exe = filePath?.Trim() ?? string.Empty;
        string cmd = rawTarget;

        if (string.IsNullOrEmpty(exe))
        {
            if (rawTarget.StartsWith('\"'))
            {
                var nextQuote = rawTarget.IndexOf('\"', 1);
                if (nextQuote > 0)
                {
                    exe = rawTarget[1..nextQuote].Trim();
                }
            }
            else
            {
                var space = rawTarget.IndexOf(' ');
                exe = space > 0 ? rawTarget[..space].Trim() : rawTarget;
            }
        }

        string? workingDir = null;
        try
        {
            if (File.Exists(exe))
            {
                workingDir = Path.GetDirectoryName(exe);
            }
        }
        catch { }

        return (exe, cmd, workingDir);
    }

    /// <summary>
    /// Запускает программу в активной сессии интерактивного пользователя.
    /// </summary>
    public static (bool Success, string Message, int ProcessId) Launch(string? filePath, string? commandLine, ILogger? logger = null)
    {
        var (exePath, fullCommandLine, workingDir) = PrepareExecutionTarget(filePath, commandLine);
        if (string.IsNullOrEmpty(fullCommandLine))
        {
            return (false, "Путь к исполняемому файлу или командная строка пусты.", 0);
        }

        // Проверяем возможность запуска через сессионный токен (режим службы Windows)
        uint activeSessionId = WTSGetActiveConsoleSessionId();
        if (activeSessionId != 0xFFFFFFFF && activeSessionId != 0)
        {
            var sessionResult = LaunchInSession(activeSessionId, exePath, fullCommandLine, workingDir, logger);
            if (sessionResult.Success)
            {
                return sessionResult;
            }
            logger?.LogWarning("Запуск через WTSQueryUserToken сессии {SessionId} завершился: {Msg}.",
                activeSessionId, sessionResult.Message);
        }

        // Защита от LPE (Session 0 execution under SYSTEM account):
        // Если служба работает неинтерактивно в сессии 0, прямой запуск через Process.Start
        // от имени SYSTEM недопустим.
        if (!Environment.UserInteractive)
        {
            logger?.LogWarning("Отказ в запуске процесса '{Target}': активная сессия пользователя недоступна, запуск от имени SYSTEM запрещён.", fullCommandLine);
            return (false, "Интерактивная сессия пользователя недоступна. Запуск от имени SYSTEM запрещён политикой безопасности.", 0);
        }

        // Фолбэк исключительно для интерактивного режима / тестов при запуске от пользователя
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = string.IsNullOrEmpty(exePath) ? fullCommandLine : exePath,
                UseShellExecute = true
            };

            if (!string.IsNullOrEmpty(exePath) && fullCommandLine.Length > exePath.Length)
            {
                var args = fullCommandLine[exePath.Length..].TrimStart('\"', ' ');
                if (!string.IsNullOrWhiteSpace(args))
                {
                    psi.Arguments = args;
                }
            }

            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
            {
                psi.WorkingDirectory = workingDir;
            }

            var proc = Process.Start(psi);
            if (proc != null)
            {
                return (true, $"Процесс запущен успешно (PID {proc.Id})", proc.Id);
            }
            return (false, "Не удалось получить дескриптор созданного процесса.", 0);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Ошибка при стандартном запуске процесса '{Target}'", fullCommandLine);
            return (false, $"Ошибка запуска: {ex.Message}", 0);
        }
    }

    private static (bool Success, string Message, int ProcessId) LaunchInSession(
        uint sessionId,
        string exePath,
        string commandLine,
        string? workingDir,
        ILogger? logger)
    {
        IntPtr hUserToken = IntPtr.Zero;
        IntPtr hPrimaryToken = IntPtr.Zero;
        IntPtr lpEnv = IntPtr.Zero;
        PROCESS_INFORMATION pi = default;

        try
        {
            if (!WTSQueryUserToken(sessionId, out hUserToken))
            {
                var err = Marshal.GetLastWin32Error();
                return (false, $"WTSQueryUserToken failed (error {err}): {new Win32Exception(err).Message}", 0);
            }

            if (!DuplicateTokenEx(
                hUserToken,
                MAXIMUM_ALLOWED,
                IntPtr.Zero,
                SecurityIdentification,
                TokenPrimary,
                out hPrimaryToken))
            {
                var err = Marshal.GetLastWin32Error();
                return (false, $"DuplicateTokenEx failed (error {err}): {new Win32Exception(err).Message}", 0);
            }

            CreateEnvironmentBlock(out lpEnv, hPrimaryToken, false);

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default"
            };

            uint creationFlags = NORMAL_PRIORITY_CLASS | CREATE_NEW_CONSOLE;
            if (lpEnv != IntPtr.Zero)
            {
                creationFlags |= CREATE_UNICODE_ENVIRONMENT;
            }

            // Защита от CWE-428 (Unquoted Search Path)
            string? appName = !string.IsNullOrEmpty(exePath) && File.Exists(exePath) ? exePath : null;

            bool created = CreateProcessAsUser(
                hPrimaryToken,
                appName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                lpEnv,
                workingDir,
                ref si,
                out pi);

            if (!created)
            {
                var err = Marshal.GetLastWin32Error();
                return (false, $"CreateProcessAsUser failed (error {err}): {new Win32Exception(err).Message}", 0);
            }

            return (true, $"Процесс запущен в сессии {sessionId} (PID {pi.dwProcessId})", pi.dwProcessId);
        }
        catch (Exception ex)
        {
            return (false, $"Исключение при вызове Win32 API: {ex.Message}", 0);
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (lpEnv != IntPtr.Zero) DestroyEnvironmentBlock(lpEnv);
            if (hPrimaryToken != IntPtr.Zero) CloseHandle(hPrimaryToken);
            if (hUserToken != IntPtr.Zero) CloseHandle(hUserToken);
        }
    }
}

