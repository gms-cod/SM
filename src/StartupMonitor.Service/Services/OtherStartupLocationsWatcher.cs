using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Watcher для прочих точек автозапуска в реестре: Winlogon, AppInitDLL, IFEO и др.
/// Выполняет начальное сканирование и затем отслеживает изменения через RegNotifyChangeKeyValue.</summary>
public class OtherStartupLocationsWatcher : BackgroundService
{
    /// <summary>Хранилище записей автозапуска.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Менеджер чёрного списка.</summary>
    private readonly BlacklistManager _blacklist;
    /// <summary>Сервис уведомлений для отправки в UI.</summary>
    private readonly NotificationService _notifications;
    /// <summary>Логгер.</summary>
    private readonly ILogger<OtherStartupLocationsWatcher> _logger;
    /// <summary>Трекер начального сканирования.</summary>
    private readonly InitialScanTracker _tracker;
    private readonly MonitoringStateCoordinator _coordinator;
    // Ключи держим в поле — иначе GC соберёт их и закроет handle под P/Invoke
    private readonly List<RegistryKey> _watchedKeys = [];

    /// <summary>Список точек реестра для сканирования и наблюдения.</summary>
    /// Drivers32 (кодеки), NetworkProvider, PrintMonitors, LSA и IE Extensions — библиотеки
    /// по требованию и метаданные, не программы при входе — не отслеживаем.
    private static readonly (string Path, EntryLocation Location)[] WatchKeys =
    [
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", EntryLocation.Winlogon),
        (@"SYSTEM\CurrentControlSet\Control\Session Manager", EntryLocation.BootExecute),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", EntryLocation.AppInitDll),
        (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", EntryLocation.ImageFileExecutionOptions),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\Scripts\Startup", EntryLocation.GpoStartupScript),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\Scripts\Logon", EntryLocation.GpoLogonScript),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", EntryLocation.BrowserHelperObject),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellContextMenuHandlers", EntryLocation.ContextMenuHandler),
        (@"SOFTWARE\Microsoft\Active Setup\Installed Components", EntryLocation.ActiveSetup),
    ];

    public OtherStartupLocationsWatcher(
        JsonDataStore store,
        BlacklistManager blacklist,
        NotificationService notifications,
        ILogger<OtherStartupLocationsWatcher> logger,
        InitialScanTracker tracker,
        MonitoringStateCoordinator coordinator)
    {
        _store = store;
        _blacklist = blacklist;
        _notifications = notifications;
        _logger = logger;
        _tracker = tracker;
        _coordinator = coordinator;
    }

    /// <summary>Основной цикл: начальное сканирование → наблюдение.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OtherStartupLocationsWatcher started");
        _tracker.CurrentStage = "Сканирование остальных точек автозапуска...";
        await InitialScan(stoppingToken);
        _tracker.MarkCompleted();
        StartWatching(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>Начальное сканирование всех указанных точек реестра (HKLM + HKCU).</summary>
    private Task InitialScan(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                ScanHive(RegistryHive.LocalMachine, isInitial: true);
                ScanHive(RegistryHive.CurrentUser, isInitial: true);
                ScanAppx(isInitial: true);
                _logger.LogInformation("OtherStartupLocations initial scan complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OtherStartupLocations initial scan failed");
            }
        }, ct);
    }

    /// <summary>Сканирует StartupTask из манифестов AppX-пакетов (UWP autostart).
    /// Состояние хранится в HKCU\...\AppModel\SystemAppData — читаем его, чтобы отметить Enabled/Disabled.</summary>
    private void ScanAppx(bool isInitial)
    {
        var appsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        if (!Directory.Exists(appsRoot)) return;

        foreach (var pkgDir in Directory.GetDirectories(appsRoot))
        {
            try
            {
                var manifestPath = Path.Combine(pkgDir, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) continue;

                var (taskId, exeFile, displayName) = SystemScannerService.ReadStartupTaskFromManifest(manifestPath);
                if (string.IsNullOrEmpty(taskId)) continue;

                if (string.IsNullOrEmpty(exeFile))
                    exeFile = Directory.GetFiles(pkgDir, "*.exe").FirstOrDefault();
                if (string.IsNullOrEmpty(exeFile)) continue;

                // PFN = Name_hash (без версии/архитектуры), напр. MSTeams_8wekyb3d8bbwe
                var pkgDirName = Path.GetFileName(pkgDir);
                var pfn = pkgDirName.Split('_').Length >= 2
                    ? $"{pkgDirName.Split('_')[0]}_{pkgDirName.Split('_')[^1]}"
                    : pkgDirName;
                var state = AppxStartupTaskHelper.ReadState(pfn, taskId);

                var entry = new StartupEntry
                {
                    Name = displayName,
                    FilePath = Path.Combine(pkgDir, exeFile),
                    CommandLine = Path.Combine(pkgDir, exeFile),
                    Location = EntryLocation.AppxStartupTask,
                    LocationDetail = $"{taskId} ({Path.GetFileName(pkgDir)})",
                    Status = state ? EntryStatus.Enabled : EntryStatus.Disabled,
                    DetectedAt = DateTime.UtcNow,
                };
                entry.Id = WatcherHelper.ComputeStableId(entry);
                ProcessEntry(entry, isInitial);
            }
            catch
            {
                // Отдельный пакет может быть повреждён — пропускаем
            }
        }
    }

    /// <summary>Сканирует все точки автозапуска в указанном кусте реестра.</summary>
    private void ScanHive(RegistryHive hive, bool isInitial = false)
    {
        using var _profOther = ExecutionProfiler.Profile("Service.OtherLocationsScan");
        using var baseKey = hive == RegistryHive.LocalMachine
            ? Registry.LocalMachine
            : Registry.CurrentUser;

        foreach (var (keyPath, location) in WatchKeys)
        {
            if (hive == RegistryHive.CurrentUser && !IsHkcuKey(keyPath, location))
                continue;

            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) continue;

            ScanKey(key, keyPath, location, hive, isInitial);
        }
    }

    /// <summary>Сканирует значения и подразделы указанного ключа реестра.</summary>
    private void ScanKey(RegistryKey key, string keyPath, EntryLocation location, RegistryHive hive, bool isInitial)
    {
        var hivePrefix = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

        // Active Setup: автозапуск — только подразделы с непустым StubPath (без '>')
        if (location == EntryLocation.ActiveSetup)
        {
            ScanActiveSetupSubKeys(key, keyPath, hivePrefix, hive, isInitial);
            return;
        }

        // Прямые значения ключа
        foreach (var valueName in key.GetValueNames())
        {
            var raw = key.GetValue(valueName);
            if (raw is not string value || string.IsNullOrWhiteSpace(value) || value.Length > 2000) continue;
            // Winlogon: сканируем только Shell/Userinit (остальные ~15 значений — параметры конфигурации, не автозапуск)
            if (location == EntryLocation.Winlogon && valueName is not ("Shell" or "Userinit"))
                continue;
            if (location == EntryLocation.BootExecute &&
                !string.Equals(valueName, "BootExecute", StringComparison.OrdinalIgnoreCase))
                continue;
            // Метаданные и значения-не-команды (числа, "yes", имена DLL) — не автозапуск
            if (!WatcherHelper.LooksLikeCommand(value)) continue;

            var filePath = ParsePath(value);
            var entry = new StartupEntry
            {
                Name = valueName,
                FilePath = filePath,
                CommandLine = value,
                Location = location,
                LocationDetail = $@"{hivePrefix}\{keyPath}\{valueName}",
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            entry.DetectedAt = DateTime.UtcNow;
            EntryOperations.ResolveAndEnrichEntry(entry);
            ResolveContextMenuClsid(entry, value);
            ProcessEntry(entry, isInitial);
        }

        // Winlogon и BootExecute: подразделы (GPExtensions, UserDefaults, DOS Devices и т.д.)
        // не являются точками автозапуска — пропускаем их (синхронизировано с SystemScannerService)
        if (location is EntryLocation.Winlogon or EntryLocation.BootExecute)
            return;

        // Подразделы 1-го уровня
        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            if (subKey == null) continue;

            foreach (var valueName in subKey.GetValueNames())
            {
                var raw = subKey.GetValue(valueName);
                if (raw is not string value || string.IsNullOrWhiteSpace(value) || value.Length > 2000) continue;
                // Записи вида {GUID}\CLSID и значения-не-команды — не автозапуск
                if (!WatcherHelper.LooksLikeCommand(value)) continue;

                var filePath = ParsePath(value);
                var entry = new StartupEntry
                {
                    Name = $"{subKeyName}\\{valueName}",
                    FilePath = filePath,
                    CommandLine = value,
                    Location = location,
                    LocationDetail = $@"{hivePrefix}\{keyPath}\{subKeyName}\{valueName}",
                };
                entry.Id = WatcherHelper.ComputeStableId(entry);
                entry.DetectedAt = DateTime.UtcNow;
                EntryOperations.ResolveAndEnrichEntry(entry);
                ResolveContextMenuClsid(entry, value);
                ProcessEntry(entry, isInitial);
            }
        }
    }

    /// <summary>Сканирует компоненты Active Setup: одна запись = подраздел {GUID} с непустым
    /// StubPath (не деактивированный '>'). Метаданные (ComponentID, Locale, Version) и ключи
    /// без StubPath не выполняются при входе — пропускаются.</summary>
    private void ScanActiveSetupSubKeys(RegistryKey parentKey, string keyPath,
        string hivePrefix, RegistryHive hive, bool isInitial)
    {
        foreach (var subKeyName in parentKey.GetSubKeyNames())
        {
            // Деактивированный компонент (>GUID) не исполняется
            if (subKeyName.StartsWith('>')) continue;

            using var subKey = parentKey.OpenSubKey(subKeyName);
            if (subKey == null) continue;

            var stub = WatcherHelper.GetActiveSetupStubPath(subKey);
            if (stub == null) continue;

            var entry = new StartupEntry
            {
                Name = subKeyName,
                FilePath = WatcherHelper.ParsePath(stub),
                CommandLine = stub,
                Location = EntryLocation.ActiveSetup,
                LocationDetail = $@"{hivePrefix}\{keyPath}\{subKeyName}",
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            entry.DetectedAt = DateTime.UtcNow;
            EntryOperations.ResolveAndEnrichEntry(entry);
            ProcessEntry(entry, isInitial);
        }
    }

    /// <summary>Для ContextMenuHandler разрешает CLSID в читаемое имя и DLL.</summary>
    private static void ResolveContextMenuClsid(StartupEntry entry, string rawValue)
    {
        if (entry.Location != EntryLocation.ContextMenuHandler) return;
        if (!rawValue.StartsWith('{')) return;

        var (name, dllPath) = ClsidResolver.Resolve(rawValue);
        if (!string.IsNullOrEmpty(name) && name != rawValue)
            entry.Name = name;
        if (!string.IsNullOrEmpty(dllPath))
            entry.CommandLine = dllPath;
    }


    /// <summary>Обрабатывает запись: проверка ЧС, добавление в хранилище, уведомление.</summary>
    private void ProcessEntry(StartupEntry entry, bool isInitial)
    {
        if (string.IsNullOrWhiteSpace(entry.FilePath)) return;

        // Проверка на чёрный список: правило создал пользователь — применяем авто-воздействие,
        // но не во время initial scan (нет предупреждения, только пометка)
        var matched = _blacklist.MatchEntry(entry);
        if (matched != null)
        {
            if (!isInitial && matched.Action == BlacklistAction.NotifyAndRemove)
            {
                _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                return;
            }
            entry.Status = EntryStatus.Blocked;
            entry.FromBlacklist = true;
        }

        // Добавление/обновление в хранилище (если дубль — без уведомления)
        if (!WatcherHelper.AddOrUpdate(_store, _notifications, entry, matched, _blacklist)) return;

        // Запись из ЧС снова появилась — автоматически останавливаем процесс и удаляем (с предупреждением)
        if (matched != null)
        {
            _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
            return;
        }

        // Уведомляем UI о новой записи (только после InitialScan)
        if (!isInitial)
        {
            _notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.EntryAdded,
                Payload = System.Text.Json.JsonSerializer.Serialize(entry)
            });
        }
    }

    /// <summary>Запускает фоновое наблюдение за изменениями ключей реестра.</summary>
    private void StartWatching(CancellationToken ct)
    {
        // HKLM
        foreach (var (keyPath, _) in WatchKeys)
        {
            try
            {
                var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
                if (key == null) continue;
                _watchedKeys.Add(key);
                StartRegNotify(key.Handle, keyPath, RegistryHive.LocalMachine, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to watch registry key: {Key}", keyPath);
            }
        }

        // HKCU (только те ключи, которые есть в HKCU)
        foreach (var (keyPath, location) in WatchKeys)
        {
            if (!IsHkcuKey(keyPath, location))
                continue;

            try
            {
                var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
                if (key == null) continue;
                _watchedKeys.Add(key);
                StartRegNotify(key.Handle, keyPath, RegistryHive.CurrentUser, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to watch registry key: {Key}", keyPath);
            }
        }
    }

    /// <summary>Проверяет, присутствует ли указанный ключ также в HKCU.</summary>
    private static bool IsHkcuKey(string keyPath, EntryLocation location)
    {
        if (keyPath.StartsWith("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Group Policy", StringComparison.OrdinalIgnoreCase))
            return true;
        if (keyPath.StartsWith("SOFTWARE\\Classes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (keyPath.StartsWith("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", StringComparison.OrdinalIgnoreCase))
            return true;
        if (keyPath.StartsWith("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Notifications", StringComparison.OrdinalIgnoreCase))
            return true;
        if (location == EntryLocation.AppInitDll)
            return true;
        if (location == EntryLocation.ActiveSetup)
            return true;
        return false;
    }

    /// <summary>Запускает цикл уведомлений об изменениях ключа реестра через P/Invoke.</summary>
    private void StartRegNotify(SafeHandle regHandle, string keyPath, RegistryHive hive, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Блокирующий режим: возврат при изменении ключа или закрытии handle
                    var result = RegNotifyChangeKeyValue(
                        regHandle,
                        watchSubtree: true,
                        notifyFilter: RegNotifyFilter.NAME | RegNotifyFilter.VALUE | RegNotifyFilter.SET_VALUE,
                        fAsynchronous: false,
                        hEvent: IntPtr.Zero,
                        fImmediate: false);

                    if (ct.IsCancellationRequested) break;

                    if (result == 0)
                    {
                        if (_coordinator.IsPaused) continue;
                        // Изменение обнаружено — пересканируем ключ после небольшой паузы
                        await Task.Delay(500, ct);
                        _logger.LogInformation("Registry change detected: {Key}", keyPath);
                        ScanHive(hive);
                    }
                    else
                    {
                        // Ошибка нотификации — пауза перед повтором
                        await Task.Delay(1000, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Registry notification error for {Key}", keyPath);
                    await Task.Delay(5000, ct);
                }
            }
        }, ct);
    }

    /// <summary>Остановка: закрываем ключи реестра, чтобы разблокировать RegNotifyChangeKeyValue.</summary>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Закрытие ключей разблокирует RegNotifyChangeKeyValue в потоках наблюдения
        foreach (var key in _watchedKeys)
            try { key.Dispose(); } catch { }
        _watchedKeys.Clear();
        return base.StopAsync(cancellationToken);
    }

    /// <summary>Извлекает путь к файлу из командной строки (поддерживает кавычки и .exe/.dll/.com/.bat).</summary>
    private static string ParsePath(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;
        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed;
        }

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx <= 0) return trimmed;

        var exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0) return trimmed[..(exeIdx + 4)];

        var comIdx = trimmed.IndexOf(".com", StringComparison.OrdinalIgnoreCase);
        if (comIdx > 0) return trimmed[..(comIdx + 4)];

        var batIdx = trimmed.IndexOf(".bat", StringComparison.OrdinalIgnoreCase);
        if (batIdx > 0) return trimmed[..(batIdx + 4)];

        var dllIdx = trimmed.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
        if (dllIdx > 0) return trimmed[..(dllIdx + 4)];

        return trimmed[..spaceIdx];
    }

    /// <summary>P/Invoke для отслеживания изменений в реестре.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        RegNotifyFilter notifyFilter,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous,
        IntPtr hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fImmediate);

    /// <summary>Фильтры уведомлений реестра.</summary>
    [Flags]
    private enum RegNotifyFilter
    {
        NAME = 1,
        VALUE = 2,
        SET_VALUE = 4,
    }
}

/// <summary>Разрешает CLSID в читаемое имя и путь к DLL через реестр (HKCR\CLSID).</summary>
internal static class ClsidResolver
{
    internal static (string name, string dllPath) Resolve(string clsid)
    {
        try
        {
            if (!clsid.StartsWith('{')) return (clsid, string.Empty);
            using var clsidKey = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
            if (clsidKey == null) return (clsid, string.Empty);

            // Значение по умолчанию — читаемое имя
            var name = clsidKey.GetValue(null)?.ToString() ?? clsid;

            // Пробуем InprocServer32 (DLL)
            using var inProc = clsidKey.OpenSubKey("InprocServer32");
            if (inProc != null)
            {
                var dll = inProc.GetValue(null)?.ToString() ?? string.Empty;
                return (name, dll);
            }

            // Пробуем LocalServer32 (EXE)
            using var localSrv = clsidKey.OpenSubKey("LocalServer32");
            if (localSrv != null)
            {
                var exe = localSrv.GetValue(null)?.ToString() ?? string.Empty;
                return (name, exe);
            }

            return (name, string.Empty);
        }
        catch
        {
            return (clsid, string.Empty);
        }
    }
}
