using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace StartupMonitor.Shared;

/// <summary>Управление жизненным циклом службы StartupMonitor и привязанного UI-процесса.</summary>
public static class ServiceManager
{
    /// <summary>Имя исполняемого файла службы.</summary>
    private static readonly string ServiceExeName = "StartupMonitor.Service.exe";
    /// <summary>Имя службы Windows (для SCM).</summary>
    private const string ServiceName = "StartupMonitor";

    /// <summary>Проверяет, запущен ли процесс с указанным именем (без .exe).</summary>
    /// <param name="processName">Имя процесса без расширения.</param>
    /// <returns>true, если хотя бы один процесс найден.</returns>
    public static bool IsProcessRunning(string processName)
    {
        var procs = Process.GetProcessesByName(processName);
        var found = procs.Length > 0;
        foreach (var proc in procs) proc.Dispose();
        return found;
    }

    /// <summary>Проверяет, запущен ли процесс службы StartupMonitor.Service.</summary>
    public static bool IsServiceRunning()
    {
        return IsProcessRunning("StartupMonitor.Service");
    }

    /// <summary>Проверяет, установлена ли служба StartupMonitor в SCM.</summary>
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

    /// <summary>Путь к exe установленной службы из реестра (ImagePath).</summary>
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

    /// <summary>Информация о регистрации и состоянии службы Windows.</summary>
    /// <param name="IsInstalled">Установлена ли служба в SCM.</param>
    /// <param name="IsAutoStart">Настроен ли автоматический запуск (start=auto).</param>
    /// <param name="IsPathCorrect">Соответствует ли ImagePath текущему расположению исполняемого файла.</param>
    /// <param name="IsRunning">Запущен ли процесс или служба со статусом Running.</param>
    /// <param name="CurrentPath">Текущий путь из реестра (ImagePath).</param>
    /// <param name="ExpectedPath">Ожидаемый путь к исполняемому файлу службы.</param>
    public record ServiceRegistrationStatus(
        bool IsInstalled,
        bool IsAutoStart,
        bool IsPathCorrect,
        bool IsRunning,
        string? CurrentPath,
        string ExpectedPath
    );

    /// <summary>Выполняет детальную проверку регистрации, автозапуска и активности службы.</summary>
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
            // Ошибка чтения реестра — используем ServiceController как запасной вариант
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
    /// Комплексно проверяет службу: если не установлена, не в автозапуске или путь устарел —
    /// настраивает её с start=auto, запускает и ожидает готовности, выводя статус на экран.
    /// </summary>
    /// <param name="reportProgress">Колбэк для вывода текстового статуса на экран.</param>
    /// <param name="timeoutMs">Максимальное время ожидания запуска (мс).</param>
    public static async Task EnsureServiceConfiguredAndRunningAsync(Action<string>? reportProgress = null, int timeoutMs = 45000)
    {
        var status = CheckServiceStatus();

        if (!File.Exists(status.ExpectedPath))
            throw new FileNotFoundException($"Исполняемый файл службы не найден: {status.ExpectedPath}", status.ExpectedPath);

        // 1. Если служба не установлена или путь устарел — настраиваем или создаем с start=auto
        if (!status.IsInstalled || !status.IsPathCorrect)
        {
            if (status.IsInstalled)
            {
                // Служба уже зарегистрирована в SCM, но путь изменился — обновляем конфигурацию атомарно без sc delete!
                reportProgress?.Invoke("Обновление пути к службе в реестре Windows...");
                await RunScElevated($"config \"{ServiceName}\" binPath=\"\\\"{status.ExpectedPath}\\\" --windows-service\" start=auto");
            }
            else
            {
                // Служба не зарегистрирована — создаем её с автозапуском
                reportProgress?.Invoke("Служба не зарегистрирована. Регистрация в автозапуск Windows...");
                try
                {
                    await RunScElevated($"create \"{ServiceName}\" binPath=\"\\\"{status.ExpectedPath}\\\" --windows-service\" start=auto");
                }
                catch (Exception ex) when (ex.Message.Contains("1072"))
                {
                    // 1072 = ERROR_SERVICE_MARKED_FOR_DELETE: служба ожидает закрытия дескрипторов после удаления
                    try { Process.GetProcessesByName("StartupMonitor.Service").ToList().ForEach(p => p.Kill()); } catch { }
                    await Task.Delay(2000);
                    await RunScElevated($"create \"{ServiceName}\" binPath=\"\\\"{status.ExpectedPath}\\\" --windows-service\" start=auto");
                }
            }

            // Настройка перезапуска при сбоях
            await RunScElevated($"failure \"{ServiceName}\" reset=86400 actions=restart/5000/restart/10000/restart/30000", checkExitCode: false);

            // Разрешаем интерактивным пользователям опрашивать, запускать и останавливать службу
            var sddl = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";
            await RunScElevated($"sdset \"{ServiceName}\" \"{sddl}\"", checkExitCode: false);

            status = CheckServiceStatus();
        }
        else if (!status.IsAutoStart)
        {
            // 2. Служба установлена, но режим автозапуска сбит (например, Manual или Disabled)
            reportProgress?.Invoke("Служба отключена в автозапуске. Настройка режима автозапуска...");
            await RunScElevated($"config \"{ServiceName}\" start=auto");
            status = CheckServiceStatus();
        }

        // 3. Запуск службы, если она не работает
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
                // Не хватило прав обычного пользователя — пробуем через повышенный sc start
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
                    // Если запуск через SCM не удался, проверяем не запущен ли процесс напрямую
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

            // 4. Ожидаем, пока служба выйдет в рабочий статус
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

    /// <summary>Ожидает запуска службы (до timeoutMs), при необходимости запуская процесс напрямую.</summary>
    public static async Task EnsureServiceRunningAsync(int timeoutMs = 60000)
    {
        await EnsureServiceConfiguredAndRunningAsync(null, timeoutMs);
    }

    /// <summary>Устанавливает или перерегистрирует службу StartupMonitor в SCM (требует прав администратора).</summary>
    public static async Task TryInstallServiceAsync()
    {
        await EnsureServiceConfiguredAndRunningAsync(null);
    }

    /// <summary>Запускает sc.exe с правами администратора (UAC) и ожидает завершения.</summary>
    /// <param name="arguments">Аргументы командной строки для sc.</param>
    /// <param name="checkExitCode">Проверять код возврата (выбрасывать исключение при ненулевом).</param>
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

    /// <summary>Останавливает службу StartupMonitor (через SCM или убивает процесс).</summary>
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

        // Fallback: убиваем процесс
        foreach (var proc in Process.GetProcessesByName("StartupMonitor.Service"))
        {
            try { proc.Kill(); } catch { }
            finally { proc.Dispose(); }
        }
    }

    /// <summary>Проверяет, запущен ли процесс UI.</summary>
    public static bool IsUiRunning()
    {
        return IsProcessRunning("StartupMonitor.UI");
    }
}
