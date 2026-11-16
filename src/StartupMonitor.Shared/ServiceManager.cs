using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace StartupMonitor.Shared;

/// <summary>Manages the lifecycle of the StartupMonitor service and the associated UI process.</summary>
public static class ServiceManager
{
    /// <summary>Gets or sets the name of the service executable file.</summary>
    private static readonly string ServiceExeName = "StartupMonitor.Service.exe";
    /// <summary>Gets or sets the name of the Windows service (for SCM).</summary>
    private const string ServiceName = "StartupMonitor";
    /// <summary>Allows standard interactive users to query and start the service; stopping and modifying it is restricted to the system and administrators.</summary>
    private const string DefaultServiceSecurityDescriptor = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

    /// <summary>Checks whether a process with the specified name (without the .exe extension) is running.</summary>\
    /// <param name="processName">The process name without the extension.</param>\
    /// <returns><c>true</c> if at least one matching process is found; otherwise, <c>false</c>.</returns>
    public static bool IsProcessRunning(string processName)
    {
        var procs = Process.GetProcessesByName(processName);
        var found = procs.Length > 0;
        foreach (var proc in procs) proc.Dispose();
        return found;
    }

    /// <summary>Checks whether the StartupMonitor.Service process is running.</summary>
    public static bool IsServiceRunning()
    {
        return IsProcessRunning("StartupMonitor.Service");
    }

    /// <summary>Checks whether the StartupMonitor service is installed in the SCM.</summary>
    public static bool IsServiceInstalled()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            var _ = sc.Status;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Path to the installed service's executable from the registry (ImagePath).</summary>
    public static string? GetInstalledServicePath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            return key?.GetValue("ImagePath")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Information about the registration and state of the Windows service.</summary>
    /// <param name="IsInstalled">Whether the service is installed in the SCM.</param>
    /// <param name="IsAutoStart">Whether automatic startup is configured (start=auto).</param>
    /// <param name="IsPathCorrect">Whether ImagePath matches the current executable location.</param>
    /// <param name="IsRunning">Whether the process or service is running with the Running status.</param>
    /// <param name="CurrentPath">Current path from the registry (ImagePath).</param>
    /// <param name="ExpectedPath">Expected path to the service executable.</param>
    public record ServiceRegistrationStatus(
        bool IsInstalled,
        bool IsAutoStart,
        bool IsPathCorrect,
        bool IsRunning,
        string? CurrentPath,
        string ExpectedPath
    );

    /// <summary>Performs a detailed check of the service registration, auto-start, and activity.</summary>
    public static ServiceRegistrationStatus CheckServiceStatus()
    {
        var baseDir = AppContext.BaseDirectory;
        var expectedPath = Path.Combine(baseDir, ServiceExeName);
        var isProcessRunning = IsProcessRunning("StartupMonitor.Service");

        string? currentImagePath = null;
        var isAutoStart = false;
        var isInstalled = false;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            if (key != null)
            {
                isInstalled = true;
                currentImagePath = key.GetValue("ImagePath")?.ToString();
                var startVal = key.GetValue("Start");
                if (startVal is int startInt && startInt == 2)
                {
                    isAutoStart = true;
                }
            }
        }
        catch
        {
            // Registry read failed — falling back to ServiceController
        }

        if (!isInstalled && IsServiceInstalled())
        {
            isInstalled = true;
        }

        var isSvcRunning = isProcessRunning;
        if (isInstalled && !isSvcRunning)
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                isSvcRunning = (sc.Status == ServiceControllerStatus.Running);
                if (sc.StartType == ServiceStartMode.Automatic)
                    isAutoStart = true;
            }
            catch { }
        }

        var isPathCorrect = isInstalled && !string.IsNullOrEmpty(currentImagePath) &&
                            currentImagePath.Contains(expectedPath, StringComparison.OrdinalIgnoreCase);

        return new ServiceRegistrationStatus(
            IsInstalled: isInstalled,
            IsAutoStart: isAutoStart,
            IsPathCorrect: isPathCorrect,
            IsRunning: isSvcRunning,
            CurrentPath: currentImagePath,
            ExpectedPath: expectedPath
        );
    }

    /// <summary>
    /// Comprehensively checks the service: if it is not installed, not set to auto-start, or its path is outdated,
    /// configures it with start=auto, starts it, and waits for readiness, displaying the status on screen.
    /// </summary>
    /// <param name="reportProgress">Callback for displaying text status on the screen.</param>
    /// <param name="timeoutMs">Maximum time to wait for the service to start (ms).</param>
    public static async Task EnsureServiceConfiguredAndRunningAsync(Action<string>? reportProgress = null, int timeoutMs = 45000)
    {
        var status = CheckServiceStatus();

        if (!File.Exists(status.ExpectedPath))
            throw new FileNotFoundException($"Исполняемый файл службы не найден: {status.ExpectedPath}", status.ExpectedPath);

        // 1. If the service is not installed or the path is outdated — configure or create it with start=auto
        if (!status.IsInstalled || !status.IsPathCorrect)
        {
            if (status.IsInstalled)
            {
                // Service is already registered in SCM, but the path changed — update the configuration atomically without sc delete!
                reportProgress?.Invoke("Обновление пути к службе в реестре Windows...");
                await RunScElevated($"config \"{ServiceName}\" binPath=\"\\\"{status.ExpectedPath}\\\" --windows-service\" start=auto");
            }
            else
            {
                // Service is not registered — create it with auto-start
                reportProgress?.Invoke("Служба не зарегистрирована. Регистрация в автозапуск Windows...");
                try
                {
                    await RunScElevated($"create \"{ServiceName}\" binPath=\"\\\"{status.ExpectedPath}\\\" --windows-service\" start=auto");
                }
                catch (Exception ex) when (ex.Message.Contains("1072"))
                {
                    // 1072 = ERROR_SERVICE_MARKED_FOR_DELETE: the service is waiting for open handles to close after deletion
                    try { Process.GetProcessesByName("StartupMonitor.Service").ToList().ForEach(p => p.Kill()); } catch { }
                    await Task.Delay(2000);
                    await RunScElevated($"create \"{ServiceName}\" binPath=\"\\\"{status.ExpectedPath}\\\" --windows-service\" start=auto");
                }
            }

            // Configure restart on failure
            await RunScElevated($"failure \"{ServiceName}\" reset=86400 actions=restart/5000/restart/10000/restart/30000", checkExitCode: false);

            // SCM permissions must not allow bypassing administrative IPC authorization by directly stopping the service.
            await RunScElevated($"sdset \"{ServiceName}\" \"{DefaultServiceSecurityDescriptor}\"", checkExitCode: true);

            status = CheckServiceStatus();
        }
        else if (!status.IsAutoStart)
        {
            // 2. The service is installed, but its startup type is misconfigured (for example, Manual or Disabled)
            reportProgress?.Invoke("Служба отключена в автозапуске. Настройка режима автозапуска...");
            await RunScElevated($"config \"{ServiceName}\" start=auto");
            status = CheckServiceStatus();
        }

        // 3. Starts the service if it is not running
        if (!status.IsRunning)
        {
            reportProgress?.Invoke("Запуск службы StartupMonitor...");

            var startedViaSc = false;
            try
            {
                using var sc = new ServiceController(ServiceName);
                if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                {
                    sc.Start();
                    startedViaSc = true;
                }
            }
            catch
            {
                // Insufficient standard user privileges — retrying with elevated sc start
            }

            if (!startedViaSc)
            {
                try
                {
                    reportProgress?.Invoke("Запуск службы с повышенными правами...");
                    await RunScElevated($"start \"{ServiceName}\"");
                }
                catch (Exception ex)
                {
                    // If SCM start failed, check if the process is running directly
                    if (!IsServiceRunning())
                    {
                        reportProgress?.Invoke("Запуск фонового процесса службы...");
                        var psi = new ProcessStartInfo
                        {
                            FileName = status.ExpectedPath,
                            Arguments = "--console",
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true,
                        };
                        using var proc = Process.Start(psi);
                        if (proc == null)
                            throw new InvalidOperationException($"Не удалось запустить службу: {ex.Message}");
                    }
                }
            }

            // 4. Wait until the service reaches running state
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(500);
                var currentStatus = CheckServiceStatus();
                var elapsedSec = (int)(sw.ElapsedMilliseconds / 1000);
                reportProgress?.Invoke($"Ожидание готовности службы ({elapsedSec} с)...");

                if (currentStatus.IsRunning)
                {
                    reportProgress?.Invoke("Служба успешно запущена!");
                    return;
                }
            }

            throw new System.TimeoutException($"Служба StartupMonitor не перешла в состояние работы за {timeoutMs / 1000} секунд.");
        }

        reportProgress?.Invoke("Служба активна.");
    }

    /// <summary>Waits for the service to start (up to timeoutMs), launching the process directly if needed.</summary>
    public static async Task EnsureServiceRunningAsync(int timeoutMs = 60000)
    {
        await EnsureServiceConfiguredAndRunningAsync(null, timeoutMs);
    }

    /// <summary>Installs or re-registers the StartupMonitor service in SCM (requires administrator rights).</summary>
    public static async Task TryInstallServiceAsync()
    {
        await EnsureServiceConfiguredAndRunningAsync(null);
    }

    /// <summary>Runs sc.exe with administrator privileges (UAC) and waits for completion.</summary>
    /// <param name="arguments">Command-line arguments for sc.</param>
    /// <param name="checkExitCode">Whether to verify exit code (throws on non-zero).</param>
    private static async Task RunScElevated(string arguments, bool checkExitCode = true)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
                throw new OperationCanceledException("Действие отменено пользователем (требуются права администратора).");
            await proc.WaitForExitAsync();
            if (checkExitCode && proc.ExitCode != 0)
                throw new InvalidOperationException($"sc {arguments.Split(' ')[0]} завершился с кодом {proc.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Действие отменено пользователем (требуются права администратора).", ex);
        }
    }

    /// <summary>Stops the StartupMonitor service (via SCM or terminates process).</summary>
    public static void StopService()
    {
        if (IsServiceInstalled())
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                if (sc.Status != ServiceControllerStatus.Stopped)
                    sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                return;
            }
            catch { }
        }

        // Fallback: terminate process
        foreach (var proc in Process.GetProcessesByName("StartupMonitor.Service"))
        {
            try { proc.Kill(); } catch { }
            finally { proc.Dispose(); }
        }
    }

    /// <summary>Determines whether the UI process is running.</summary>
    public static bool IsUiRunning()
    {
        return IsProcessRunning("StartupMonitor.UI");
    }
}
