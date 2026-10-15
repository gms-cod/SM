using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Service.Services.Wmi;

namespace StartupMonitor.Service.Services;

/// <summary>Статические операции над записями автозапуска: реестр, файлы, службы, планировщик, kill-процессов.</summary>
public static class EntryOperations
{
    /// <summary>Настройки сериализации JSON: snake_case, с отступами.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    /// <summary>Директория для бэкапов удалённых файлов автозапуска.</summary>
    private static readonly string BackupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StartupMonitor", "backups");

    // ───────────────────────────── Registry ─────────────────────────────

    /// <summary>DTO для сохранения/восстановления значения реестра (ключ, имя значения, данные).</summary>
    public record RegistryPayload(string Key, string ValueName, string ValueData);

    /// <summary>Возвращает путь в реестре для указанного типа расположения.</summary>
    private static string GetRegistryKeyPath(EntryLocation loc)
    {
        return loc switch
        {
            EntryLocation.RegistryRun => @"Software\Microsoft\Windows\CurrentVersion\Run",
            EntryLocation.RegistryRunOnce => @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            EntryLocation.RegistryRunServices => @"Software\Microsoft\Windows\CurrentVersion\RunServices",
            EntryLocation.RegistryPolicies => @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
            EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler => @"CLSID",
            EntryLocation.AppInitDll => @"Software\Microsoft\Windows NT\CurrentVersion\Windows",
            EntryLocation.ImageFileExecutionOptions => @"Software\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
            EntryLocation.Winlogon => @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon",
            EntryLocation.BootExecute => @"System\CurrentControlSet\Control\Session Manager",
            EntryLocation.ActiveSetup => @"Software\Microsoft\Active Setup\Installed Components",
            EntryLocation.Drivers32 => @"Software\Microsoft\Windows NT\CurrentVersion\Drivers32",
            _ => throw new NotSupportedException($"Registry operations not supported for {loc}")
        };
    }

    /// <summary>Определяет куст реестра (HKCU/HKCR/HKLM) по типу расположения.</summary>
    private static RegistryHive GetHive(EntryLocation loc)
    {
        return loc switch
        {
            EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler => RegistryHive.ClassesRoot,
            EntryLocation.AppInitDll or EntryLocation.ImageFileExecutionOptions
                or EntryLocation.Winlogon or EntryLocation.BootExecute
                or EntryLocation.ActiveSetup or EntryLocation.Drivers32 => RegistryHive.LocalMachine,
            _ => RegistryHive.CurrentUser
        };
    }

    /// <summary>Возвращает строковое имя куста реестра для отображения.</summary>
    private static string GetHiveName(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        _ => hive.ToString()
    };

    /// <summary>Формирует строку пути в реестре для указанного типа расположения (для DisablePayload).</summary>
    public static string BuildRegistryPayload(EntryLocation loc)
    {
        var hive = GetHiveName(GetHive(loc));
        return $@"{hive}\{GetRegistryKeyPath(loc)}";
    }

    /// <summary>Открывает раздел реестра для указанной записи. Возвращает (успех, ошибка).
    /// Для записей пользовательских кустов (LocationDetail вида "HKCU\Имя\...") открывает
    /// HKEY_USERS\&lt;SID&gt;, т.к. служба работает как LocalSystem и её HKCU — профиль SYSTEM.</summary>
    private static (bool, string?) OpenRegistry(StartupEntry entry, bool writable, out RegistryKey? key, string? subKey = null)
    {
        key = null;
        try
        {
            var keyPath = GetRegistryKeyPath(entry.Location);
            if (!string.IsNullOrEmpty(subKey))
                keyPath += @"\" + subKey;

            if (entry.LocationDetail?.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) == true)
            {
                var userName = entry.LocationDetail["HKCU\\".Length..].Split('\\')[0];
                var profile = WatcherHelper.GetUserProfiles().FirstOrDefault(p => p.Name == userName);
                if (profile.Sid != null)
                {
                    var fullPath = $@"{profile.Sid}\{keyPath}";
                    key = Registry.Users.OpenSubKey(fullPath, writable: writable);
                    if (key == null)
                        return (false, $"Registry key not found: HKEY_USERS\\{fullPath}");
                    return (true, null);
                }
            }

            var hive = GetHive(entry.Location);
            key = RegistryKey.OpenBaseKey(hive, RegistryView.Default)
                .OpenSubKey(keyPath, writable: writable);
            if (key == null)
                return (false, $"Registry key not found: {GetHiveName(hive)}\\{keyPath}");
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Формирует полный путь ключа для payload (с учётом пользовательского куста).</summary>
    private static string BuildPayloadPath(StartupEntry entry, string? subKey)
    {
        var keyPath = GetRegistryKeyPath(entry.Location)
            + (subKey != null ? $@"\{subKey}" : "");

        if (entry.LocationDetail?.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) == true)
        {
            var userName = entry.LocationDetail["HKCU\\".Length..].Split('\\')[0];
            var profile = WatcherHelper.GetUserProfiles().FirstOrDefault(p => p.Name == userName);
            if (profile.Sid != null)
                return $@"HKEY_USERS\{profile.Sid}\{keyPath}";
        }

        return $@"{GetHiveName(GetHive(entry.Location))}\{keyPath}";
    }

    /// <summary>Открывает ключ реестра из сохранённого payload (с поддержкой HKEY_USERS).</summary>
    private static (bool, string?) OpenPayloadKey(string payloadKey, bool writable, out RegistryKey? key)
    {
        key = null;
        try
        {
            if (payloadKey.StartsWith("HKEY_USERS\\", StringComparison.OrdinalIgnoreCase))
            {
                key = Registry.Users.OpenSubKey(payloadKey["HKEY_USERS\\".Length..], writable: writable);
            }
            else
            {
                var parts = payloadKey.Replace("HKEY_CURRENT_USER\\", "")
                    .Replace("HKEY_CLASSES_ROOT\\", "")
                    .Replace("HKEY_LOCAL_MACHINE\\", "");
                var hive = payloadKey switch
                {
                    string k when k.StartsWith("HKEY_CLASSES_ROOT") => RegistryHive.ClassesRoot,
                    string k when k.StartsWith("HKEY_LOCAL_MACHINE") => RegistryHive.LocalMachine,
                    _ => RegistryHive.CurrentUser
                };
                key = RegistryKey.OpenBaseKey(hive, RegistryView.Default)
                    .OpenSubKey(parts, writable: writable);
            }

            if (key == null) return (false, $"Cannot open key: {payloadKey}");
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Разбивает имя записи на путь подключа и имя значения
    /// (для subkey-записей: IFEO "app.exe\Debugger", ContextMenuHandlers и т.п.).</summary>
    private static (string? subKey, string valueName) SplitSubKeyName(string name)
    {
        var idx = name.LastIndexOf('\\');
        return idx > 0 ? (name[..idx], name[(idx + 1)..]) : (null, name);
    }

    /// <summary>Отключает запись в реестре: удаляет значение, сохраняет бэкап в entry.DisablePayload</summary>
    public static (bool success, string? error) DisableRegistry(StartupEntry entry)
    {
        var (subKey, valueName) = SplitSubKeyName(entry.Name);
        var (ok, err) = OpenRegistry(entry, writable: true, out var key, subKey);
        if (!ok || key == null) return (false, err ?? "Failed to open registry key");
        using (key)
        {
            // Сохраняем текущее значение перед удалением
            var existing = key.GetValue(valueName)?.ToString() ?? "";
            key.DeleteValue(valueName, throwOnMissingValue: false);
            var fullPath = BuildPayloadPath(entry, subKey);
            // Сериализуем бэкап в DisablePayload для последующего восстановления
            entry.DisablePayload = JsonSerializer.Serialize(
                new RegistryPayload(fullPath, valueName, existing),
                JsonOptions);
        }
        return (true, null);
    }

    /// <summary>Включает запись в реестре: восстанавливает значение из DisablePayload</summary>
    public static (bool success, string? error) EnableRegistry(StartupEntry entry)
    {
        if (string.IsNullOrEmpty(entry.DisablePayload))
            return (false, "No disable payload to restore registry entry");
        var rp = JsonSerializer.Deserialize<RegistryPayload>(entry.DisablePayload, JsonOptions);
        if (rp == null) return (false, "Invalid registry payload");

        try
        {
            var (ok, err) = OpenPayloadKey(rp.Key, writable: true, out var key);
            if (!ok || key == null) return (false, err ?? "Failed to open registry key");
            using (key)
            {
                key.SetValue(rp.ValueName, rp.ValueData);
                // После восстановления очищаем payload
                entry.DisablePayload = "";
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Удаляет запись из реестра навсегда, возвращает payload для возможного восстановления.</summary>
    public static (bool success, string? error) RemoveRegistry(StartupEntry entry, out string payload)
    {
        var (subKey, valueName) = SplitSubKeyName(entry.Name);
        var (ok, err) = OpenRegistry(entry, writable: true, out var key, subKey);
        if (!ok || key == null) { payload = ""; return (false, err ?? "Failed to open registry key"); }

        try
        {
            using (key)
            {
                // Сохраняем значение перед удалением
                var existing = key.GetValue(valueName)?.ToString() ?? "";
                key.DeleteValue(valueName, throwOnMissingValue: false);

                var fullPath = BuildPayloadPath(entry, subKey);
                var regPayload = new RegistryPayload(fullPath, valueName, existing);
                payload = JsonSerializer.Serialize(regPayload, JsonOptions);
                return (true, null);
            }
        }
        catch (Exception ex)
        {
            payload = "";
            return (false, ex.Message);
        }
    }

    /// <summary>Восстанавливает удалённую запись реестра из payload.</summary>
    public static (bool success, string? error) RestoreRegistry(string payloadJson)
    {
        try
        {
            var rp = JsonSerializer.Deserialize<RegistryPayload>(payloadJson, JsonOptions);
            if (rp == null) return (false, "Invalid registry payload");

            var (ok, err) = OpenPayloadKey(rp.Key, writable: true, out var key);
            if (!ok || key == null) return (false, err ?? "Failed to open registry key");
            using (key)
            {
                key.SetValue(rp.ValueName, rp.ValueData);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ───────────────────────────── StartupFolder ─────────────────────────────

    /// <summary>DTO для бэкапа файла: путь к бэкапу и оригинальный путь.</summary>
    public record FilePayload(string BackupPath, string OriginalPath);

    /// <summary>Отключает файл в папке автозапуска: переименовывает в .disabled</summary>
    public static (bool success, string? error) DisableStartupFile(StartupEntry entry)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(entry.FilePath) || SystemProtectionHelper.IsProtectedProcessName(entry.FilePath))
                return (false, "Недопустимый или защищённый путь к файлу");

            if (!File.Exists(entry.FilePath))
                return (false, $"File not found: {entry.FilePath}");

            // Переименовываем .exe/.lnk → .exe.disabled/.lnk.disabled
            var disabledPath = entry.FilePath + ".disabled";
            if (File.Exists(disabledPath))
                File.Delete(disabledPath);
            File.Move(entry.FilePath, disabledPath);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Включает файл в папке автозапуска: переименовывает обратно из .disabled</summary>
    public static (bool success, string? error) EnableStartupFile(StartupEntry entry)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(entry.FilePath) || SystemProtectionHelper.IsProtectedProcessName(entry.FilePath))
                return (false, "Недопустимый или защищённый путь к файлу");

            var disabledPath = entry.FilePath + ".disabled";
            if (!File.Exists(disabledPath))
                return (false, $"Disabled file not found: {disabledPath}");

            // Удаляем существующий файл, если есть, и переименовываем .disabled обратно
            if (File.Exists(entry.FilePath))
                File.Delete(entry.FilePath);
            File.Move(disabledPath, entry.FilePath);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Удаляет файл из папки автозапуска с бэкапом в BackupDir.</summary>
    public static (bool success, string? error) RemoveStartupFile(StartupEntry entry, out string payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(entry.FilePath) || SystemProtectionHelper.IsProtectedProcessName(entry.FilePath))
            {
                payload = "";
                return (false, "Недопустимый или защищённый путь к файлу");
            }

            if (!File.Exists(entry.FilePath))
            {
                payload = "";
                return (false, $"File not found: {entry.FilePath}");
            }

            // Копируем в бэкап перед удалением
            Directory.CreateDirectory(BackupDir);
            var backupPath = Path.Combine(BackupDir, $"startup_{Guid.NewGuid():N}{Path.GetExtension(entry.FilePath)}");
            File.Copy(entry.FilePath, backupPath);
            File.Delete(entry.FilePath);

            var filePayload = new FilePayload(backupPath, entry.FilePath);
            payload = JsonSerializer.Serialize(filePayload, JsonOptions);
            return (true, null);
        }
        catch (Exception ex)
        {
            payload = "";
            return (false, ex.Message);
        }
    }

    /// <summary>Восстанавливает файл автозапуска из бэкапа.</summary>
    public static (bool success, string? error) RestoreFile(string payloadJson)
    {
        try
        {
            var fp = JsonSerializer.Deserialize<FilePayload>(payloadJson, JsonOptions);
            if (fp == null) return (false, "Invalid file payload");
            if (string.IsNullOrWhiteSpace(fp.OriginalPath) || SystemProtectionHelper.IsProtectedProcessName(fp.OriginalPath))
                return (false, "Недопустимый или защищённый путь для восстановления файла");
            if (!File.Exists(fp.BackupPath)) return (false, $"Backup not found: {fp.BackupPath}");

            // Создаём целевую директорию при необходимости и копируем файл обратно
            var dir = Path.GetDirectoryName(fp.OriginalPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.Copy(fp.BackupPath, fp.OriginalPath, overwrite: true);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ───────────────────────────── Service ─────────────────────────────

    /// <summary>DTO для сохранения/восстановления службы: имя, путь к бинарнику, тип запуска.</summary>
    public record ServicePayload(string ServiceName, string BinaryPath, string StartType, string? DisplayName);

    /// <summary>Запускает sc.exe с указанными аргументами и возвращает exit code + вывод.</summary>
    private static (int exitCode, string output) RunSc(string args, int timeoutMs = 10000)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("sc", args)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return (-1, "");
        // Читаем оба потока — иначе при выводе >4КБ процесс заблокируется на записи
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(timeoutMs))
        {
            // Процесс завис — принудительно завершаем
            try { proc.Kill(); } catch { }
            return (-1, "");
        }
        return (proc.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>Имя службы приходит из реестра/WMI — отсекаем инъекцию аргументов sc.</summary>
    private static bool IsValidServiceName(string name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains('"');

    /// <summary>Текущий тип запуска службы из `sc qc` (для бэкапа перед удалением).</summary>
    private static string GetServiceStartType(string svcName)
    {
        var (code, output) = RunSc($"qc \"{svcName}\"", 10000);
        if (code != 0) return "auto";
        if (output.Contains("BOOT_START", StringComparison.OrdinalIgnoreCase)) return "boot";
        if (output.Contains("SYSTEM_START", StringComparison.OrdinalIgnoreCase)) return "system";
        if (output.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase)) return "demand";
        if (output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase)) return "disabled";
        return "auto";
    }

    /// <summary>Отключает службу: sc stop + sc config start=disabled</summary>
    public static (bool success, string? error) DisableService(StartupEntry entry)
    {
        try
        {
            var svcName = entry.LocationDetail;
            if (!IsValidServiceName(svcName))
                return (false, $"Invalid service name: {svcName}");
            if (IsSelfEntry(entry) || IsSelfServiceName(svcName))
                return (false, "Самозащита StartupMonitor: отключение собственной службы программы запрещено");
            if (SystemProtectionHelper.IsCoreWindowsService(svcName))
                return (false, "Отключение критически важных системных служб Windows запрещено в целях безопасности ОС");
            RunSc($"stop \"{svcName}\"", 5000);
            var (exitCode, _) = RunSc($"config \"{svcName}\" start=disabled", 10000);
            if (exitCode != 0)
                return (false, $"sc config failed with exit code {exitCode}");
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Включает службу: sc config start=auto + sc start</summary>
    public static (bool success, string? error) EnableService(StartupEntry entry)
    {
        try
        {
            var svcName = entry.LocationDetail;
            if (!IsValidServiceName(svcName))
                return (false, $"Invalid service name: {svcName}");
            var (exitCode, _) = RunSc($"config \"{svcName}\" start=auto", 10000);
            if (exitCode != 0)
                return (false, $"sc config failed with exit code {exitCode}");
            RunSc($"start \"{svcName}\"", 5000);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Удаляет службу через sc delete, сохраняя payload для восстановления.</summary>
    public static (bool success, string? error) RemoveService(StartupEntry entry, out string payload)
    {
        try
        {
            var svcName = entry.LocationDetail;
            if (!IsValidServiceName(svcName))
            {
                payload = "";
                return (false, $"Invalid service name: {svcName}");
            }
            if (IsSelfEntry(entry) || IsSelfServiceName(svcName))
            {
                payload = "";
                return (false, "Самозащита StartupMonitor: удаление собственной службы программы запрещено");
            }
            if (SystemProtectionHelper.IsCoreWindowsService(svcName))
            {
                payload = "";
                return (false, "Удаление критически важных системных служб Windows запрещено в целях безопасности ОС");
            }
            // Сохраняем реальный тип запуска, чтобы Restore вернул как было
            var svcPayload = new ServicePayload(svcName, entry.FilePath, GetServiceStartType(svcName), null);
            payload = JsonSerializer.Serialize(svcPayload, JsonOptions);

            RunSc($"stop \"{svcName}\"", 5000);
            var (exitCode, _) = RunSc($"delete \"{svcName}\"", 10000);
            if (exitCode != 0 && exitCode != 1060) // 1060 = service doesn't exist
                return (false, $"sc delete failed with exit code {exitCode}");

            return (true, null);
        }
        catch (Exception ex)
        {
            payload = "";
            return (false, ex.Message);
        }
    }

    /// <summary>Восстанавливает удалённую службу через sc create.</summary>
    public static (bool success, string? error) RestoreService(string payloadJson)
    {
        try
        {
            var sp = JsonSerializer.Deserialize<ServicePayload>(payloadJson, JsonOptions);
            if (sp == null) return (false, "Invalid service payload");

            var args = $"create \"{sp.ServiceName}\" binPath=\"{sp.BinaryPath}\" start={sp.StartType}";
            if (!string.IsNullOrEmpty(sp.DisplayName))
                args += $" displayName=\"{sp.DisplayName}\"";

            var (exitCode, _) = RunSc(args, 15000);
            return exitCode == 0
                ? (true, null)
                : (false, $"sc create exited with code {exitCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ───────────────────────────── TaskScheduler ─────────────────────────────

    /// <summary>DTO для сохранения/восстановления задачи планировщика: XML и путь.</summary>
    public record TaskPayload(string TaskXml, string TaskPath);

    /// <summary>Отключает задачу в планировщике: task.Enabled = false</summary>
    public static (bool success, string? error) DisableTask(StartupEntry entry)
    {
        try
        {
            var taskPath = entry.LocationDetail;
            using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
            var task = ts.GetTask(taskPath);
            if (task == null) return (false, $"Task not found: {taskPath}");
            task.Enabled = false;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Включает задачу в планировщике: task.Enabled = true</summary>
    public static (bool success, string? error) EnableTask(StartupEntry entry)
    {
        try
        {
            var taskPath = entry.LocationDetail;
            using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
            var task = ts.GetTask(taskPath);
            if (task == null) return (false, $"Task not found: {taskPath}");
            task.Enabled = true;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Удаляет задачу из планировщика с сохранением XML для восстановления.</summary>
    public static (bool success, string? error) RemoveTask(StartupEntry entry, out string payload)
    {
        try
        {
            var taskPath = entry.LocationDetail;
            using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
            var task = ts.GetTask(taskPath);
            if (task == null)
            {
                payload = "";
                return (false, $"Task not found: {taskPath}");
            }

            // Сохраняем XML задачи и полный путь перед удалением
            var taskXml = task.Xml;
            var realPath = task.Path; // полный путь задачи в планировщике (\Folder\Name)
            task.Folder.DeleteTask(task.Name, false);

            var tPayload = new TaskPayload(taskXml, realPath);
            payload = JsonSerializer.Serialize(tPayload, JsonOptions);
            return (true, null);
        }
        catch (Exception ex)
        {
            payload = "";
            return (false, ex.Message);
        }
    }

    /// <summary>Восстанавливает задачу в планировщике из сохранённого XML.</summary>
    public static (bool success, string? error) RestoreTask(string payloadJson)
    {
        try
        {
            var tp = JsonSerializer.Deserialize<TaskPayload>(payloadJson, JsonOptions);
            if (tp == null) return (false, "Invalid task payload");

            using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
            // RegisterTask принимает путь задачи целиком (\Folder\Name)
            ts.RootFolder.RegisterTask(tp.TaskPath, tp.TaskXml);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ───────────────────────────── KillProcess ─────────────────────────────

    /// <summary>Проверяет, защищено ли имя процесса от завершения (системные и собственные процессы).</summary>
    public static bool IsProtectedProcessName(string fileName) =>
        SystemProtectionHelper.IsProtectedProcessName(fileName);

    /// <summary>Завершает процесс по имени файла. Если передан полный путь —
    /// завершаются только процессы, чей образ совпадает с этим путём.</summary>
    public static void KillProcessByImage(string fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName) || IsProtectedProcessName(fileName)) return;
            var name = Path.GetFileNameWithoutExtension(fileName);

            // Полный путь → сверяем путь образа, чтобы не убить одноимённый системный процесс
            var fullPath = Path.IsPathRooted(fileName) ? Path.GetFullPath(fileName) : null;

            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(name))
            {
                try
                {
                    if (fullPath != null)
                    {
                        var procPathSafe = ProcessMetadataEnricher.TryGetProcessPath(proc.Id);
                        if (string.IsNullOrEmpty(procPathSafe))
                            continue; // нет доступа к пути образа — не трогаем процесс
                        if (!string.Equals(procPathSafe, fullPath, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    proc.Kill(entireProcessTree: true);
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
    }

    /// <summary>Завершает службу по имени.</summary>
    public static void StopService(string serviceName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(serviceName) || !IsValidServiceName(serviceName)) return;
            if (IsSelfServiceName(serviceName) || SystemProtectionHelper.IsCoreWindowsService(serviceName)) return; // Самозащита и защита ядра Windows
            RunSc($"stop \"{serviceName}\"", 5000);
        }
        catch { }
    }

    /// <summary>Завершает процесс/службу по записи автозапуска.</summary>
    public static void KillProcess(StartupEntry entry)
    {
        if (entry == null || IsSelfEntry(entry) || SystemProtectionHelper.IsProtectedSystemEntry(entry)) return; // Самозащита и защита системы

        // Для служб — sc stop
        if (entry.Location is EntryLocation.Service or EntryLocation.Driver)
        {
            StopService(entry.LocationDetail);
            return;
        }

        // Для процессов — Kill по имени файла
        if (!string.IsNullOrEmpty(entry.FilePath))
        {
            KillProcessByImage(entry.FilePath);
        }
        else if (!string.IsNullOrEmpty(entry.CommandLine))
        {
            // Если нет FilePath, пробуем взять из командной строки первый аргумент
            var parts = entry.CommandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                KillProcessByImage(parts[0].Trim('"'));
        }
    }

    /// <summary>При старте службы завершает процессы заблокированных записей
    /// (Status == Blocked && FromBlacklist).</summary>
    public static void TerminateAllBlocked(List<StartupEntry> entries)
    {
        foreach (var entry in entries.ToList())
        {
            if (entry.Status == EntryStatus.Blocked && entry.FromBlacklist)
                KillProcess(entry);
        }
    }

    /// <summary>Завершает процессы, чей путь к образу совпадает с правилом ЧС
    /// (Path/PathWildcard). Срабатывает даже если записи в списке нет — проблема ms-teams,
    /// когда правило в ЧС есть, а запись удалена из системы.</summary>
    public static void KillProcessesByRules(IEnumerable<BlacklistRule> rules, Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        using var _prof = ExecutionProfiler.Measure("Service.EntryOps.KillByRules");
        if (rules == null) return;

        var activeRules = rules
            .Where(r => r.Action == BlacklistAction.NotifyAndRemove &&
                        r.MatchType is BlacklistMatchType.Path or BlacklistMatchType.PathWildcard)
            .ToList();

        if (activeRules.Count == 0) return;

        Process[]? procs = null;
        try
        {
            procs = Process.GetProcesses();
        }
        catch { return; }

        try
        {
            foreach (var proc in procs)
            {
                try
                {
                    var procName = proc.ProcessName;
                    if (string.IsNullOrEmpty(procName)) continue;
                    var nameWithExe = procName + ".exe";
                    if (IsProtectedProcessName(nameWithExe) || SystemProtectionHelper.IsHostProcess(nameWithExe) || SystemProtectionHelper.IsHostProcess(procName)) continue;

                    string? procPath = ProcessMetadataEnricher.TryGetProcessPath(proc.Id);
                    if (string.IsNullOrEmpty(procPath)) procPath = null;

                    var testPath = procPath ?? nameWithExe;
                    if (procPath != null && (IsProtectedProcessName(testPath) || SystemProtectionHelper.IsHostProcess(testPath))) continue;

                    foreach (var rule in activeRules)
                    {
                        // Если путь образа неизвестен (procPath == null), нельзя сопоставлять
                        // path-правила против testPath ("имя.exe"): иначе маска вроде *chrome.exe
                        // убьёт легитимный Chrome в Program Files. Без пути сопоставляем только
                        // правила уровня имени файла (без слэшей и двоеточий).
                        bool matched;
                        if (procPath != null)
                        {
                            matched = BlacklistManager.MatchesFilePath(rule, procPath)
                                      || BlacklistManager.MatchesFilePath(rule, testPath);
                        }
                        else
                        {
                            matched = rule.Value != null
                                      && !rule.Value.Contains('/') && !rule.Value.Contains('\\') && !rule.Value.Contains(':')
                                      && BlacklistManager.MatchesFilePath(rule, testPath);
                        }

                        if (matched)
                        {
                            try
                            {
                                proc.Kill(entireProcessTree: true);
                                logger?.LogInformation("Завершен запрещенный процесс {ProcessName} (PID {Pid}, путь: {Path}) по правилу ЧС {Rule}",
                                    proc.ProcessName, proc.Id, procPath ?? proc.ProcessName, rule.Value);
                            }
                            catch (Exception ex)
                            {
                                logger?.LogWarning(ex, "Не удалось завершить запрещенный процесс {ProcessName} (PID {Pid})", proc.ProcessName, proc.Id);
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
        }
        finally
        {
            foreach (var proc in procs)
            {
                try { proc.Dispose(); } catch { }
            }
        }
    }

    // ───────────────────────────── Enrich ─────────────────────────────

    /// <summary>Кэшированный результат обогащения записи (для <see cref="ResolveAndEnrichEntry"/>).</summary>
    private sealed record EnrichCacheValue(
        string ResolvedPath,
        CliThreatInfo? ThreatInfo,
        bool IsSigned,
        DigitalSignatureStatus SignatureStatus,
        string? SignerName,
        string? SignatureIssuer,
        string Publisher,
        string Language,
        string Sha256,
        DateTime FileLastWriteUtc,
        DateTime CacheTimestamp);

    /// <summary>Максимальный размер кэша обогащения (защита от разрастания памяти).</summary>
    private const int EnrichCacheCapacity = 5000;

    /// <summary>Потокобезопасный кэш обогащения: ключ — "Id\0CommandLine\0FilePath".</summary>
    private static readonly ConcurrentDictionary<string, EnrichCacheValue> _enrichCache = new(StringComparer.Ordinal);

    /// <summary>Строит ключ кэша обогащения из идентификатора и исходных путей записи.</summary>
    private static string BuildEnrichCacheKey(StartupEntry entry) =>
        string.Concat(entry.Id, "\0", entry.CommandLine ?? string.Empty, "\0", entry.FilePath ?? string.Empty);

    /// <summary>Глубоко клонирует <see cref="CliThreatInfo"/> (защита от разделяемого мутабельного списка).</summary>
    private static CliThreatInfo? CloneThreatInfo(CliThreatInfo? src) =>
        src == null ? null : new CliThreatInfo
        {
            HasThreat = src.HasThreat,
            ThreatLevel = src.ThreatLevel,
            BadgeText = src.BadgeText,
            Description = src.Description,
            Indicators = new List<string>(src.Indicators)
        };

    /// <summary>Сохраняет значение в кэш с eviction самых старых записей при переполнении.</summary>
    private static void AddToEnrichCache(string key, EnrichCacheValue value)
    {
        _enrichCache[key] = value;
        if (_enrichCache.Count > EnrichCacheCapacity)
        {
            try
            {
                var oldest = _enrichCache
                    .OrderBy(kv => kv.Value.CacheTimestamp)
                    .Take(500)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in oldest)
                    _enrichCache.TryRemove(k, out _);
            }
            catch { }
        }
    }

    /// <summary>Находит исполняемый файл, вычисляет SHA256 и определяет издателя.
    /// computeHash=false — для массовых verify-сканов (хеш не нужен для сопоставления).</summary>
    public static void ResolveAndEnrichEntry(StartupEntry entry, bool computeHash = true)
    {
        using var _prof = ExecutionProfiler.Measure("Service.EntryOps.ResolveEnrich");
        try
        {
            if (entry == null) return;
            var cacheKey = BuildEnrichCacheKey(entry);

            // 0. Быстрый путь: попадание в кэш при неизменном файле на диске
            if (_enrichCache.TryGetValue(cacheKey, out var cached))
            {
                // Негативный кэш: ранее файл не был найден — входы те же (ключ),
                // переиспользуем только эвристику без дорогих дисковых операций.
                if (string.IsNullOrEmpty(cached.ResolvedPath))
                {
                    if (cached.ThreatInfo?.HasThreat == true &&
                        (entry.ThreatInfo == null || !entry.ThreatInfo.HasThreat))
                        entry.ThreatInfo = CloneThreatInfo(cached.ThreatInfo);
                    _enrichCache[cacheKey] = cached with { CacheTimestamp = DateTime.UtcNow };
                    return;
                }

                try
                {
                    if (File.Exists(cached.ResolvedPath) &&
                        File.GetLastWriteTimeUtc(cached.ResolvedPath) == cached.FileLastWriteUtc)
                    {
                        if (cached.ThreatInfo?.HasThreat == true &&
                            (entry.ThreatInfo == null || !entry.ThreatInfo.HasThreat))
                            entry.ThreatInfo = CloneThreatInfo(cached.ThreatInfo);

                        // Для ShellExtension/ContextMenuHandler FilePath хранит CLSID — не заменяем
                        if (entry.Location is not EntryLocation.ShellExtension and not EntryLocation.ContextMenuHandler)
                        {
                            if (entry.FilePath != cached.ResolvedPath)
                                entry.FilePath = cached.ResolvedPath;
                        }

                        if (computeHash && string.IsNullOrEmpty(entry.SHA256) && !string.IsNullOrEmpty(cached.Sha256))
                            entry.SHA256 = cached.Sha256;

                        entry.IsSigned = cached.IsSigned;
                        entry.SignatureStatus = cached.SignatureStatus;
                        entry.SignerName = cached.SignerName;
                        entry.SignatureIssuer = cached.SignatureIssuer;
                        if (!string.IsNullOrEmpty(cached.Publisher))
                            entry.Publisher = cached.Publisher;
                        if (!string.IsNullOrEmpty(cached.Language) && string.IsNullOrEmpty(entry.Language))
                            entry.Language = cached.Language;

                        var refreshed = cached with { CacheTimestamp = DateTime.UtcNow };

                        // Кэш был собран с computeHash=false, а сейчас хеш нужен —
                        // вычисляем только хеш без полного переобогащения.
                        if (computeHash && string.IsNullOrEmpty(entry.SHA256))
                        {
                            entry.SHA256 = ComputeSha256(cached.ResolvedPath);
                            if (!string.IsNullOrEmpty(entry.SHA256))
                                refreshed = refreshed with { Sha256 = entry.SHA256 };
                        }

                        _enrichCache[cacheKey] = refreshed;
                        return;
                    }
                }
                catch { /* файл недоступен — идём на полное обогащение */ }
            }

            // 1. Эвристический анализ командной строки и путей
            if (entry.ThreatInfo == null || !entry.ThreatInfo.HasThreat)
            {
                var threat = StartupMonitor.Shared.Helpers.CliHeuristicAnalyzer.Analyze(entry.CommandLine, entry.FilePath);
                if (threat.HasThreat)
                {
                    entry.ThreatInfo = threat;
                }
            }

            var resolvedPath = ResolveExecutablePath(entry);
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
            {
                // Сохраняем негативный результат (только эвристика), чтобы не
                // повторять ResolveExecutablePath для отсутствующих файлов.
                AddToEnrichCache(cacheKey, new EnrichCacheValue(
                    string.Empty,
                    CloneThreatInfo(entry.ThreatInfo),
                    entry.IsSigned,
                    entry.SignatureStatus,
                    entry.SignerName,
                    entry.SignatureIssuer,
                    entry.Publisher ?? string.Empty,
                    entry.Language ?? string.Empty,
                    entry.SHA256 ?? string.Empty,
                    DateTime.MinValue,
                    DateTime.UtcNow));
                return;
            }

            // Повторный анализ с уточнённым путем, если угроза ещё не была выявлена
            if (entry.ThreatInfo == null || !entry.ThreatInfo.HasThreat)
            {
                var threat = StartupMonitor.Shared.Helpers.CliHeuristicAnalyzer.Analyze(entry.CommandLine, resolvedPath);
                if (threat.HasThreat)
                {
                    entry.ThreatInfo = threat;
                }
            }

            // Обновляем FilePath, если нашли более точный путь
            if (entry.FilePath != resolvedPath)
            {
                // Для ShellExtension/ContextMenuHandler FilePath хранит CLSID,
                // его нельзя заменять на путь к DLL
                if (entry.Location is not EntryLocation.ShellExtension and not EntryLocation.ContextMenuHandler)
                    entry.FilePath = resolvedPath;
            }

            // Вычисляем SHA256, если ещё нет
            if (computeHash && string.IsNullOrEmpty(entry.SHA256))
                entry.SHA256 = ComputeSha256(resolvedPath);

            // Верифицируем цифровую подпись Authenticode через WinVerifyTrust
            if (File.Exists(resolvedPath))
            {
                try
                {
                    var sig = AuthenticodeVerifier.VerifyFile(resolvedPath);
                    entry.IsSigned = sig.IsSigned;
                    entry.SignatureStatus = sig.Status;
                    entry.SignerName = sig.SignerName;
                    entry.SignatureIssuer = sig.IssuerName;

                    if (!string.IsNullOrEmpty(sig.SignerName))
                    {
                        entry.Publisher = sig.SignerName;
                    }
                    else if (string.IsNullOrEmpty(entry.Publisher))
                    {
                        var vi = FileVersionInfo.GetVersionInfo(resolvedPath);
                        entry.Publisher = vi.CompanyName ?? string.Empty;
                    }
                }
                catch { }
            }

            // Определяем язык программы
            if (string.IsNullOrEmpty(entry.Language))
                entry.Language = Ai.ProgramLangDetector.Detect(resolvedPath);

            // Сохраняем результат в кэш
            DateTime lastWrite = DateTime.MinValue;
            try
            {
                if (File.Exists(resolvedPath))
                    lastWrite = File.GetLastWriteTimeUtc(resolvedPath);
            }
            catch { }
            AddToEnrichCache(cacheKey, new EnrichCacheValue(
                resolvedPath,
                CloneThreatInfo(entry.ThreatInfo),
                entry.IsSigned,
                entry.SignatureStatus,
                entry.SignerName,
                entry.SignatureIssuer,
                entry.Publisher ?? string.Empty,
                entry.Language ?? string.Empty,
                entry.SHA256 ?? string.Empty,
                lastWrite,
                DateTime.UtcNow));
        }
        catch { }
    }

    /// <summary>Пытается найти исполняемый путь из FilePath, CommandLine или CLSID.</summary>
    private static string ResolveExecutablePath(StartupEntry entry)
    {
        var candidates = new List<string>();

        // Для ShellExtension/ContextMenuHandler реальный путь в CommandLine
        if (entry.Location is EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler)
        {
            if (!string.IsNullOrEmpty(entry.CommandLine))
                candidates.Add(WatcherHelper.ParsePath(entry.CommandLine));
        }

        // FilePath как есть
        if (!string.IsNullOrEmpty(entry.FilePath))
            candidates.Add(entry.FilePath);

        // Парсим CommandLine
        if (!string.IsNullOrEmpty(entry.CommandLine))
        {
            var parsed = WatcherHelper.ParsePath(entry.CommandLine);
            if (!string.IsNullOrEmpty(parsed))
                candidates.Add(parsed);
        }

        // Перебираем кандидатов и возвращаем первый существующий файл
        foreach (var raw in candidates)
        {
            var expanded = ExpandPath(raw);
            if (!string.IsNullOrEmpty(expanded) && File.Exists(expanded))
                return expanded;
        }

        return string.Empty;
    }

    /// <summary>Разворачивает путь: убирает префикс \??\ и раскрывает %variables%.</summary>
    private static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var result = path.Trim();

        // Убираем префикс \??\ (NT namespace)
        if (result.StartsWith(@"\??\"))
            result = result[4..];

        // Разворачиваем переменные окружения
        if (result.Contains('%'))
        {
            try { result = Environment.ExpandEnvironmentVariables(result); }
            catch { }
        }

        return result;
    }

    /// <summary>Вычисляет SHA256 хеш файла в нижнем регистре.</summary>
    private static string ComputeSha256(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = System.Security.Cryptography.SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch { return string.Empty; }
    }

    // ───────────────────────────── Verify ─────────────────────────────

    /// <summary>Проверяет, существует ли запись автозапуска в системе,
    /// сравнивая со снимком всех активных автозагрузок.</summary>
    public static bool VerifyEntry(StartupEntry entry, List<StartupEntry> scanResults,
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        try
        {
            var found = SystemScannerService.VerifyInScan(entry, scanResults);
            if (!found)
                logger?.LogInformation("VerifyEntry: {Name} ({Loc}, {Detail}) — НЕ НАЙДЕНА в снимке системы",
                    entry.Name, entry.Location, entry.LocationDetail);
            return found;
        }
        catch (Exception ex)
        {
            // При ошибке проверки считаем запись валидной (чтобы не удалить по ошибке)
            logger?.LogWarning(ex, "VerifyEntry: ошибка при проверке {Name} ({Loc})", entry.Name, entry.Location);
            return true;
        }
    }

    /// <summary>Удаляет из списка записи, которых больше нет в системе.
    /// Записи, добавленные после scanTimestamp, пропускаются — они могли не попасть в снимок
    /// из-за race condition между сканером и вотчерами.</summary>
    public static void CleanupStaleEntries(List<StartupEntry> entries, JsonDataStore store,
        List<StartupEntry> scanResults, Microsoft.Extensions.Logging.ILogger? logger,
        DateTime? scanTimestamp = null,
        IReadOnlyCollection<EntryLocation>? failedLocations = null)
    {
        var swAll = Stopwatch.StartNew();
        var before = entries.Count;
        var scanTime = scanTimestamp ?? DateTime.UtcNow;
        logger?.LogInformation("CleanupStaleEntries: начало — {Count} записей, снимок системы — {ScanCount}, скан от {ScanTime:O}",
            before, scanResults?.Count ?? 0, scanTime);

        // Fail-closed guard: пустой снимок при непустой БД почти наверняка означает
        // упавший/неполный скан (AccessDenied, выгрузка куста, недоступность WMI/COM),
        // а не одновременное удаление всех программ. Без этого одно падение скана
        // вытирает всю БД, что далее выглядит как «все угрозы новые».
        if (scanResults == null)
        {
            logger?.LogWarning("CleanupStaleEntries: снимок системы отсутствует (null) — пропуск очистки (fail-closed)");
            swAll.Stop();
            logger?.LogInformation("CleanupStaleEntries: завершено (fail-closed, null-снимок) — было {Before}, пропущено {Skipped}, удалено 0, осталось {After} [{Elapsed}ms]",
                before, before, entries.Count, swAll.ElapsedMilliseconds);
            return;
        }
        if (scanResults.Count == 0)
        {
            logger?.LogWarning("CleanupStaleEntries: пустой снимок системы — возможно, сканирование завершилось с ошибкой");
            // Если известны упавшие секции и ВСЕ записи относятся к ним —
            // удалять нечего, выходим без изменений.
            if (failedLocations != null && failedLocations.Count > 0
                && entries.Count > 0 && entries.All(e => failedLocations.Contains(e.Location)))
            {
                swAll.Stop();
                logger?.LogInformation("CleanupStaleEntries: завершено (fail-closed, пустой снимок) — было {Before}, пропущено {Skipped}, удалено 0, осталось {After} [{Elapsed}ms]",
                    before, before, entries.Count, swAll.ElapsedMilliseconds);
                return;
            }
            // Иначе (legacy-вызовы без failedLocations / частичное покрытие) —
            // продолжаем как раньше: поштучные пропуски failed-секций и
            // scannedLocations-гард ниже сохраняют защиту без ломки поведения.
        }

        // Локации, реально представленные в снимке. Секция, молча упавшая без записи
        // в failedLocations (перехват catch{} в сканере), оставит свою Location
        // отсутствующей здесь — такие записи трогать нельзя.
        // Активен только при непустом снимке, чтобы пустой скан обрабатывался
        // веткой fail-closed выше.
        HashSet<EntryLocation>? scannedLocations = scanResults.Count > 0
            ? new HashSet<EntryLocation>(scanResults.Select(s => s.Location))
            : null;

        var results = new List<(StartupEntry e, bool valid)>(entries.Count);
        var skipped = 0;
        // Снимок списка: watcher'ы могут добавлять записи параллельно с циклом
        foreach (var entry in entries.ToList())
        {
            // Запись удалена пользователем из системы (в списке восстановления) —
            // в общем списке ей не место, даже если статус Blocked
            if (store.IsRemoved(entry.Id))
            {
                store.RemoveEntry(entry.Id);
                logger?.LogInformation("CleanupStaleEntries: удалена из общего списка {Name} ({Loc}) — в списке восстановления",
                    entry.Name, entry.Location);
                continue;
            }

            // Отключённые/заблокированные нами записи физически отсутствуют в системе
            // (значение реестра удалено, файл переименован, служба disabled) — это НЕ stale
            if (entry.Status != EntryStatus.Enabled)
            {
                skipped++;
                results.Add((entry, true));
                continue;
            }

            // Секция сканирования упала — снимок неполный, запись не трогаем
            if (failedLocations != null && failedLocations.Contains(entry.Location))
            {
                skipped++;
                results.Add((entry, true));
                continue;
            }

            // Локация отсутствует в снимке целиком (секция не сканировалась или
            // молча упала без записи в failedLocations) — запись не трогаем
            if (scannedLocations != null && !scannedLocations.Contains(entry.Location))
            {
                skipped++;
                results.Add((entry, true));
                continue;
            }

            // Пропускаем записи, добавленные после старта скана (race condition)
            if (entry.DetectedAt > scanTime)
            {
                skipped++;
                results.Add((entry, true));
                continue;
            }

            var sw = Stopwatch.StartNew();
            var valid = VerifyEntry(entry, scanResults, logger);
            sw.Stop();
            results.Add((entry, valid));
            if (!valid)
            {
                logger?.LogInformation("  ✗ устарела: {Name} ({Loc}, {Detail}) [{Elapsed}ms]",
                    entry.Name, entry.Location, entry.LocationDetail, sw.ElapsedMilliseconds);
                if (logger != null)
                    SystemScannerService.DebugMatch(entry, scanResults, logger);
            }
        }

        // Удаляем устаревшие записи из хранилища (в историю не пишем — это не действие пользователя)
        var stale = results.Where(r => !r.valid).Select(r => r.e).ToList();
        foreach (var entry in stale)
        {
            store.RemoveEntry(entry.Id);
            entries.RemoveAll(e => e.Id == entry.Id);
            logger?.LogInformation("CleanupStaleEntries: удалена {Name} ({Loc})", entry.Name, entry.Location);
        }

        swAll.Stop();
        logger?.LogInformation("CleanupStaleEntries: завершено — было {Before}, пропущено {Skipped}, удалено {Removed}, осталось {After} [{Elapsed}ms]",
            before, skipped, stale.Count, entries.Count, swAll.ElapsedMilliseconds);
    }

    // ───────────────────────────── SelfProtection ─────────────────────────────

    /// <summary>Проверяет, является ли имя службы нашей собственной службой StartupMonitor.</summary>
    public static bool IsSelfServiceName(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;
        var clean = serviceName.Trim();
        return string.Equals(clean, "StartupMonitor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "StartupMonitor.Service", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Проверяет, относится ли запись к собственным компонентам StartupMonitor (служба, UI, задачи, ключи реестра).</summary>
    public static bool IsSelfEntry(StartupEntry? entry)
    {
        if (entry == null) return false;
        try
        {
            // 1. Проверка по имени службы
            if (entry.Location is EntryLocation.Service or EntryLocation.Driver)
            {
                if (IsSelfServiceName(entry.LocationDetail) || IsSelfServiceName(entry.Name))
                    return true;
            }

            // 2. Проверка по имени исполняемого файла
            var fileName = Path.GetFileName(entry.FilePath);
            if (string.IsNullOrEmpty(fileName))
            {
                var parts = (entry.CommandLine ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    fileName = Path.GetFileName(parts[0].Trim('"'));
            }

            if (string.Equals(fileName, "StartupMonitor.Service.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "StartupMonitor.UI.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Проверка по имени ключа автозапуска или значению в реестре
            if (string.Equals(entry.Name, "StartupMonitor.UI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, "StartupMonitor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, "StartupMonitorUI", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 4. Проверка по совпадению с базовой директорией программы
            var baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            if (!string.IsNullOrEmpty(entry.FilePath) && entry.FilePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Проверяет, является ли запись критически важной системной службой или компонентом ОС Windows,
    /// которые недопустимо удалять или глушить в автоматическом режиме Lockdown.</summary>
    public static bool IsProtectedSystemEntry(StartupEntry? entry) =>
        SystemProtectionHelper.IsProtectedSystemEntry(entry);

    /// <summary>Проверяет, является ли запись нашим собственным сервисом StartupMonitor.Service.exe (для обратной совместимости).</summary>
    public static bool IsOwnService(StartupEntry entry) => IsSelfEntry(entry);

    // ───────────────────────────── Dispatchers ─────────────────────────────

    /// <summary>Удаляет запись из системы (реестр/файл/службу/задачу) в зависимости от Location.</summary>
    public static (bool success, string? error, string? payload) RemoveFromSystem(StartupEntry entry)
    {
        if (IsSelfEntry(entry))
        {
            return (false, "Самозащита StartupMonitor: удаление собственных компонентов программы запрещено", null);
        }
        if (SystemProtectionHelper.IsProtectedSystemEntry(entry))
        {
            return (false, "Удаление защищённых системных компонентов Windows запрещено в целях безопасности ОС", null);
        }

        return entry.Location switch
        {
            EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce
                or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies
                or EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler
                or EntryLocation.AppInitDll or EntryLocation.ImageFileExecutionOptions
                or EntryLocation.Winlogon or EntryLocation.BootExecute
                or EntryLocation.ActiveSetup or EntryLocation.Drivers32 =>
                RemoveRegistry(entry, out var p) switch
                {
                    (true, _) => (true, null, p),
                    (_, var err) => (false, err, null)
                },

            EntryLocation.StartupFolder =>
                RemoveStartupFile(entry, out var p) switch
                {
                    (true, _) => (true, null, p),
                    (_, var err) => (false, err, null)
                },

            EntryLocation.Service or EntryLocation.Driver =>
                RemoveService(entry, out var p) switch
                {
                    (true, _) => (true, null, p),
                    (_, var err) => (false, err, null)
                },

            EntryLocation.TaskScheduler =>
                RemoveTask(entry, out var p) switch
                {
                    (true, _) => (true, null, p),
                    (_, var err) => (false, err, null)
                },

            // AppX StartupTask: удаление пакета не поддерживается — отключаем задачу
            EntryLocation.AppxStartupTask =>
                DisableFromSystem(entry) switch
                {
                    (true, _) => (true, null, null),
                    (_, var err) => (false, err, null)
                },

            EntryLocation.WmiEventSubscription =>
                WmiSubscriptionHelper.RemoveSubscription(entry),

            _ => (false, $"RemoveFromSystem not supported for {entry.Location}", null)
        };
    }

    /// <summary>Восстанавливает удалённую запись в системе по сохранённому payload.</summary>
    public static (bool success, string? error) RestoreToSystem(RemovedEntry entry)
    {
        return entry.Location switch
        {
            EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce
                or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies
                or EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler
                or EntryLocation.AppInitDll or EntryLocation.ImageFileExecutionOptions
                or EntryLocation.Winlogon or EntryLocation.BootExecute
                or EntryLocation.ActiveSetup or EntryLocation.Drivers32 =>
                RestoreRegistry(entry.RestorePayload),

            EntryLocation.StartupFolder =>
                RestoreFile(entry.RestorePayload),

            EntryLocation.Service or EntryLocation.Driver =>
                RestoreService(entry.RestorePayload),

            EntryLocation.TaskScheduler =>
                RestoreTask(entry.RestorePayload),

            EntryLocation.WmiEventSubscription =>
                WmiSubscriptionHelper.RestoreSubscription(entry.RestorePayload),

            _ => (false, $"RestoreToSystem not supported for {entry.Location}")
        };
    }

    /// <summary>Отключает запись в системе (без удаления, с возможностью включить обратно).</summary>
    public static (bool success, string? error) DisableFromSystem(StartupEntry entry)
    {
        if (IsSelfEntry(entry))
        {
            return (false, "Самозащита StartupMonitor: отключение собственных компонентов программы запрещено");
        }
        if (SystemProtectionHelper.IsProtectedSystemEntry(entry))
        {
            return (false, "Отключение защищённых системных компонентов Windows запрещено во избежание сбоев ОС");
        }

        switch (entry.Location)
        {
            case EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce
                or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies
                or EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler
                or EntryLocation.AppInitDll or EntryLocation.ImageFileExecutionOptions
                or EntryLocation.Winlogon or EntryLocation.BootExecute
                or EntryLocation.ActiveSetup or EntryLocation.Drivers32:
                return DisableRegistry(entry);

            case EntryLocation.StartupFolder:
                return DisableStartupFile(entry);

            case EntryLocation.Service or EntryLocation.Driver:
                return DisableService(entry);

            case EntryLocation.TaskScheduler:
                return DisableTask(entry);

            // AppX StartupTask: отключить = записать State=1 в SystemAppData
            case EntryLocation.AppxStartupTask:
                var pfn = ParseAppxPfn(entry);
                var taskId = ParseAppxTaskId(entry);
                if (string.IsNullOrEmpty(pfn) || string.IsNullOrEmpty(taskId))
                    return (false, "Не удалось определить PFN/TaskId записи AppX");
                return AppxStartupTaskHelper.WriteState(pfn, taskId, enabled: false)
                    ? (true, null)
                    : (false, "Не удалось отключить StartupTask");

            case EntryLocation.WmiEventSubscription:
                return WmiSubscriptionHelper.DisableSubscription(entry);

            default:
                return (false, $"DisableFromSystem not supported for {entry.Location}");
        }
    }

    /// <summary>Включает запись в системе (восстанавливает из отключённого состояния).</summary>
    public static (bool success, string? error) EnableFromSystem(StartupEntry entry)
    {
        return entry.Location switch
        {
            EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce
                or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies
                or EntryLocation.ShellExtension or EntryLocation.ContextMenuHandler
                or EntryLocation.AppInitDll or EntryLocation.ImageFileExecutionOptions
                or EntryLocation.Winlogon or EntryLocation.BootExecute
                or EntryLocation.ActiveSetup or EntryLocation.Drivers32 =>
                EnableRegistry(entry),

            EntryLocation.StartupFolder =>
                EnableStartupFile(entry),

            EntryLocation.Service or EntryLocation.Driver =>
                EnableService(entry),

            EntryLocation.TaskScheduler =>
                EnableTask(entry),

            // AppX StartupTask: включить = записать State=2 в SystemAppData
            EntryLocation.AppxStartupTask =>
                EnableAppxTask(entry),

            EntryLocation.WmiEventSubscription =>
                WmiSubscriptionHelper.EnableSubscription(entry),

            _ => (false, $"EnableFromSystem not supported for {entry.Location}")
        };
    }

    /// <summary>Включает AppX StartupTask (State=2 в SystemAppData).</summary>
    private static (bool, string?) EnableAppxTask(StartupEntry entry)
    {
        var pfn = ParseAppxPfn(entry);
        var taskId = ParseAppxTaskId(entry);
        if (string.IsNullOrEmpty(pfn) || string.IsNullOrEmpty(taskId))
            return (false, "Не удалось определить PFN/TaskId записи AppX");
        return AppxStartupTaskHelper.WriteState(pfn, taskId, enabled: true)
            ? (true, null)
            : (false, "Не удалось включить StartupTask");
    }

    /// <summary>Извлекает PFN из LocationDetail записи AppX (формат "TaskId (PFN)").</summary>
    private static string ParseAppxPfn(StartupEntry entry)
    {
        var detail = entry.LocationDetail ?? string.Empty;
        var open = detail.LastIndexOf('(');
        var close = detail.LastIndexOf(')');
        if (open < 0 || close <= open) return string.Empty;
        return detail[(open + 1)..close].Trim();
    }

    /// <summary>Извлекает TaskId из LocationDetail записи AppX (формат "TaskId (PFN)").</summary>
    private static string ParseAppxTaskId(StartupEntry entry)
    {
        var detail = entry.LocationDetail ?? string.Empty;
        var open = detail.LastIndexOf('(');
        return open < 0 ? detail.Trim() : detail[..open].Trim();
    }

    // ───────────────────────────── Process Suspend / Resume ─────────────────────────────

    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("ntdll.dll", SetLastError = true)]
    /// <summary>NtSuspendProcess: Выполняет операцию NtSuspendProcess.</summary>
    /// <param name="processHandle">Входные данные: значение параметра processHandle (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern int.</returns>
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    /// <summary>NtResumeProcess: Выполняет операцию NtResumeProcess.</summary>
    /// <param name="processHandle">Входные данные: значение параметра processHandle (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern int.</returns>
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    /// <summary>OpenProcess: Выполняет операцию OpenProcess.</summary>
    /// <param name="processAccess">Входные данные: значение параметра processAccess (uint).</param>
    /// <param name="bInheritHandle">Входные данные: значение параметра bInheritHandle (bool).</param>
    /// <param name="processId">Входные данные: значение параметра processId (int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>CloseHandle: Выполняет операцию CloseHandle.</summary>
    /// <param name="hObject">Входные данные: значение параметра hObject (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly ConcurrentDictionary<int, bool> _suspendedPids = new();

    /// <summary>Проверяет, был ли процесс приостановлен службой.</summary>
    public static bool IsProcessSuspended(int pid) => _suspendedPids.ContainsKey(pid);

    /// <summary>Возвращает список всех известных приостановленных PID.</summary>
    public static IEnumerable<int> GetSuspendedPids() => _suspendedPids.Keys;

    /// <summary>Устанавливает или снимает отметку о приостановке процесса.</summary>
    public static void TrackSuspendedPid(int pid, bool suspended)
    {
        if (suspended)
            _suspendedPids[pid] = true;
        else
            _suspendedPids.TryRemove(pid, out _);
    }

    /// <summary>Приостанавливает выполнение процесса по PID через NtSuspendProcess.</summary>
    public static (bool success, string message) SuspendProcess(int processId, string? processName = null)
    {
        if (!string.IsNullOrEmpty(processName) && IsProtectedProcessName(processName))
            return (false, $"Процесс {processName} является системным и защищён от приостановки.");

        try
        {
            using var proc = Process.GetProcessById(processId);
            if (IsProtectedProcessName(proc.ProcessName))
                return (false, $"Процесс {proc.ProcessName} является системным и защищён от приостановки.");

            var hProcess = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                return (false, $"Не удалось открыть процесс PID {processId} (код Win32 {err}).");
            }

            try
            {
                var status = NtSuspendProcess(hProcess);
                if (status == 0)
                {
                    TrackSuspendedPid(processId, true);
                    return (true, $"Процесс {proc.ProcessName} (PID {processId}) успешно приостановлен.");
                }
                return (false, $"Ошибка NtSuspendProcess: 0x{status:X8}");
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
        catch (ArgumentException)
        {
            TrackSuspendedPid(processId, false);
            return (false, $"Процесс с PID {processId} не найден или уже завершился.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка приостановки процесса PID {processId}: {ex.Message}");
        }
    }

    /// <summary>Возобновляет выполнение процесса по PID через NtResumeProcess.</summary>
    public static (bool success, string message) ResumeProcess(int processId, string? processName = null)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            var hProcess = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                return (false, $"Не удалось открыть процесс PID {processId} (код Win32 {err}).");
            }

            try
            {
                var status = NtResumeProcess(hProcess);
                if (status == 0)
                {
                    TrackSuspendedPid(processId, false);
                    return (true, $"Процесс {proc.ProcessName} (PID {processId}) успешно возобновлен.");
                }
                return (false, $"Ошибка NtResumeProcess: 0x{status:X8}");
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
        catch (ArgumentException)
        {
            TrackSuspendedPid(processId, false);
            return (false, $"Процесс с PID {processId} не найден или уже завершился.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка возобновления процесса PID {processId}: {ex.Message}");
        }
    }
}
