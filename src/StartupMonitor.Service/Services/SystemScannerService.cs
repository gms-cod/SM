using System.Management;
using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Service.Services.Wmi;

namespace StartupMonitor.Service.Services;

/// <summary>Сканер системы: собирает полный снимок всех точек автозапуска
/// (реестр, папки, службы, планировщик) для верификации и очистки устаревших записей.
/// Используется также для сравнения при проверке актуальности записей (VerifyEntry).</summary>
public class SystemScannerService
{
    // Секции сканирования, упавшие с ошибкой в последнем ScanAll — cleanup не должен трогать их записи
    private readonly HashSet<EntryLocation> _failedLocations = [];
    /// <summary>Список секций, которые упали при последнем сканировании (WMI/COM недоступны и т.п.).</summary>
    public IReadOnlyCollection<EntryLocation> FailedLocations => _failedLocations;

    /// <summary>Пути стандартных точек реестра (Run, RunOnce, RunServices, Policies).</summary>
    private static readonly (string Path, EntryLocation Location)[] RegistryLocations =
    [
        (@"Software\Microsoft\Windows\CurrentVersion\Run", EntryLocation.RegistryRun),
        (@"Software\Microsoft\Windows\CurrentVersion\RunOnce", EntryLocation.RegistryRunOnce),
        (@"Software\Microsoft\Windows\CurrentVersion\RunServices", EntryLocation.RegistryRunServices),
        (@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", EntryLocation.RegistryPolicies),
        (@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", EntryLocation.RegistryRun),
        (@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", EntryLocation.RegistryRunOnce),
        (@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunServices", EntryLocation.RegistryRunServices),
        (@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", EntryLocation.RegistryPolicies),
    ];

    /// <summary>Прочие точки реестра (Winlogon, AppInitDll, IFEO и др.).</summary>
    /// Записи GPExtensions (289 штук) и AppPaths (53) — это системные расширения и
    /// регистрация путей, а не автозапуск — они только шумят в списке. Не сканируем.
    /// Drivers32 (кодеки), NetworkProvider, PrintMonitors, LSA — загружаемые по требованию
    /// библиотеки и параметры конфигурации, а не программы при входе — тоже не сканируем.
    /// IE Extensions — метаданные кнопок и CLSID-ссылки, не команды — не сканируем.
    private static readonly (string Path, EntryLocation Location)[] OtherLocations =
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

    /// <summary>Wow6432Node варианты для AppInitDll и IFEO.</summary>
    private static readonly string[] RegistryWow6432 =
    [
        @"SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Windows",
        @"SOFTWARE\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
    ];

    /// <summary>Выполняет полное сканирование точек автозапуска. Возвращает снимок системы.</summary>
    /// <param name="logger">Логгер</param>
    /// <param name="skipRegistry">Пропустить сканирование реестра (при высокой загрузке CPU)</param>
    public List<StartupEntry> ScanAll(Microsoft.Extensions.Logging.ILogger? logger = null, bool skipRegistry = false)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ScanAll");
        var results = new List<StartupEntry>();
        _failedLocations.Clear();

        if (skipRegistry)
        {
            logger?.LogWarning("Scanner: сканирование реестра пропущено (троттлинг по нагрузке CPU)");
            _failedLocations.Add(EntryLocation.RegistryRun);
            _failedLocations.Add(EntryLocation.RegistryRunOnce);
            _failedLocations.Add(EntryLocation.RegistryRunServices);
            _failedLocations.Add(EntryLocation.RegistryPolicies);
            _failedLocations.Add(EntryLocation.Winlogon);
            _failedLocations.Add(EntryLocation.BootExecute);
            _failedLocations.Add(EntryLocation.AppInitDll);
            _failedLocations.Add(EntryLocation.ImageFileExecutionOptions);
            _failedLocations.Add(EntryLocation.GpoStartupScript);
            _failedLocations.Add(EntryLocation.GpoLogonScript);
            _failedLocations.Add(EntryLocation.BrowserHelperObject);
            _failedLocations.Add(EntryLocation.ContextMenuHandler);
            _failedLocations.Add(EntryLocation.ActiveSetup);
        }
        else
        {
            var beforeReg = results.Count;
            ScanRegistry(results);
            logger?.LogDebug("Scanner: реестр (Run/..) — найдено {Count}", results.Count - beforeReg);

            var beforeOther = results.Count;
            ScanOtherLocations(results);
            logger?.LogDebug("Scanner: остальные точки реестра — найдено {Count}", results.Count - beforeOther);
        }

        var beforeFolders = results.Count;
        ScanStartupFolders(results);
        logger?.LogDebug("Scanner: папки автозапуска — найдено {Count}", results.Count - beforeFolders);

        var beforeServices = results.Count;
        ScanServices(results);
        logger?.LogDebug("Scanner: службы — найдено {Count}", results.Count - beforeServices);

        var beforeWmi = results.Count;
        ScanWmiSubscriptions(results);
        logger?.LogDebug("Scanner: WMI-подписки — найдено {Count}", results.Count - beforeWmi);

        var beforeTasks = results.Count;
        ScanTaskScheduler(results);
        logger?.LogDebug("Scanner: планировщик задач — найдено {Count}", results.Count - beforeTasks);

        var beforeAppx = results.Count;
        ScanAppxStartupTasks(results, logger);
        logger?.LogDebug("Scanner: AppX StartupTask — найдено {Count}", results.Count - beforeAppx);

        logger?.LogInformation("Scanner: всего просканировано {Total} записей", results.Count);

        if (logger != null && results.Count > 0)
        {
            // Разбивка по Location для диагностики
            var breakdown = results.GroupBy(e => e.Location)
                .Select(g => $"{g.Key}={g.Count()}");
            logger.LogInformation("Scanner: разбивка по Location — {Breakdown}",
                string.Join(", ", breakdown));

            // Дамп в отдельный файл — только при уровне Debug/Trace,
            // чтобы не дёргать диск на каждом фоновом скане каждые 5 минут
            if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                try
                {
                    var logsDir = Path.Combine(
                        AppContext.BaseDirectory,
                        "logs");
                    Directory.CreateDirectory(logsDir);
                    var dumpPath = Path.Combine(logsDir, "scanner-dump.txt");
                    var lines = results.Select(e =>
                        $"{e.Location,-8} | {e.Name,-50} | {e.FilePath,-80} | {e.LocationDetail}");
                    File.WriteAllLines(dumpPath, lines);
                    logger.LogInformation("Scanner: дамп записан в {Path} ({Count} строк)", dumpPath, results.Count);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Scanner: не удалось записать дамп");
                }
            }
        }

        return results;
    }

    /// <summary>Возвращает префикс куста реестра для отображения.</summary>
    private static string GetHivePrefix(RegistryHive hive) =>
        hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

    // ───────── Registry (Run, RunOnce, RunServices, Policies) ─────────

    /// <summary>Сканирует стандартные точки реестра (HKLM + HKCU + все пользовательские кусты).</summary>
    private static void ScanRegistry(List<StartupEntry> results)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ScanRegistry");
        ScanHiveRegistry(Registry.LocalMachine, "HKLM", allowWow64: true, results);
        ScanHiveRegistry(Registry.CurrentUser, "HKCU", allowWow64: false, results);
        foreach (var (sid, name) in WatcherHelper.GetUserProfiles())
        {
            try
            {
                using var userKey = Registry.Users.OpenSubKey(sid);
                if (userKey == null) continue;
                ScanHiveRegistry(userKey, $@"HKCU\{name}", allowWow64: false, results);
            }
            catch
            {
                // Чужой SID с приватным ACL или выгрузка куста при logoff (TOCTOU) —
                // пропускаем профиль, не роняя весь ScanAll
                continue;
            }
        }
    }

    /// <summary>Сканирует стандартные точки реестра в указанном базовом ключе.</summary>
    private static void ScanHiveRegistry(RegistryKey baseKey, string hivePrefix,
        bool allowWow64, List<StartupEntry> results)
    {
        foreach (var (keyPath, location) in RegistryLocations)
        {
            if (!allowWow64 && keyPath.Contains("Wow6432Node", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var key = baseKey.OpenSubKey(keyPath);
                if (key == null) continue;

                var detailPrefix = $@"{hivePrefix}\{keyPath}";

                string[] valueNames;
                try { valueNames = key.GetValueNames(); }
                catch { continue; }

                foreach (var valueName in valueNames)
                {
                    string? value;
                    try { value = key.GetValue(valueName) as string; }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    // Значения, не похожие на команду (числа, "yes", CLSID) — не автозапуск
                    if (!WatcherHelper.LooksLikeCommand(value)) continue;

                    var entry = MakeRegistryEntry(valueName, value, location, detailPrefix, keyPath);
                    results.Add(entry);
                }
            }
            catch
            {
                // Запрещённый ключ (AccessDenied) или выгрузка куста между открытием и чтением —
                // пропускаем точку, не роняя весь ScanAll
                continue;
            }
        }
    }

    /// <summary>Создаёт запись автозапуска из значения реестра.</summary>
    private static StartupEntry MakeRegistryEntry(string valueName, string value, EntryLocation location,
        string detailPrefix, string keyPath)
    {
        var filePath = WatcherHelper.ParsePath(value);
        var entry = new StartupEntry
        {
            Name = valueName,
            FilePath = filePath,
            CommandLine = value,
            Location = location,
            LocationDetail = $@"{detailPrefix}\{valueName}",
        };
        entry.Id = WatcherHelper.ComputeStableId(entry);
        EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
        return entry;
    }

    // ───────── Other Registry Locations (Winlogon, AppInitDll, etc.) ─────────

    /// <summary>Сканирует прочие точки реестра, включая подразделы и CLSID.</summary>
    private static void ScanOtherLocations(List<StartupEntry> results)
    {
        foreach (var hives in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        using (var baseKey = hives == RegistryHive.LocalMachine
               ? Registry.LocalMachine : Registry.CurrentUser)
        {
            foreach (var (keyPath, location) in OtherLocations)
            {
                if (hives == RegistryHive.CurrentUser && !IsHkcuOtherKey(keyPath))
                    continue;

                try
                {
                    using var key = baseKey.OpenSubKey(keyPath);
                    if (key == null) continue;

                    var hivePrefix = GetHivePrefix(hives);
                    ScanKeyValues(key, keyPath, location, hivePrefix, results);

                    // Winlogon: подразделы (GPExtensions, UserDefaults) — это системные
                    // расширения, а не автозапуск; BootExecute: подразделы (DOS Devices,
                    // Environment) — конфигурация, а не автозапуск. Их не сканируем.
                    if (location is EntryLocation.Winlogon or EntryLocation.BootExecute)
                        continue;

                    // Active Setup: автозапуском является только подраздел с непустым StubPath
                    // (без '>'), а не его значения-метаданные (ComponentID, Locale, Version)
                    if (location == EntryLocation.ActiveSetup)
                    {
                        ScanActiveSetupSubKeys(key, keyPath, location, hivePrefix, results);
                        continue;
                    }

                    // Subkeys: 1 уровень для всех локаций (как в OtherStartupLocationsWatcher)
                    ScanSubKeyValues(key, keyPath, location, hivePrefix, results);

                }
                catch
                {
                    // Пропускаем защищенные системные ключи, к которым нет доступа
                }
            }

            // Wow6432Node variants for specific keys
            foreach (var keyPath in RegistryWow6432)
            {
                if (hives == RegistryHive.CurrentUser) continue;
                try
                {
                    using var key = baseKey.OpenSubKey(keyPath);
                    if (key == null) continue;

                    var loc = keyPath.Contains("Image File Execution Options", StringComparison.OrdinalIgnoreCase)
                        ? EntryLocation.ImageFileExecutionOptions : EntryLocation.AppInitDll;
                    ScanKeyValues(key, keyPath, loc, "HKLM", results);
                    ScanSubKeyValues(key, keyPath, loc, "HKLM", results);
                }
                catch
                {
                    // Пропускаем недоступные ветки реестра
                }
            }
        }

        // Shell extensions: scan HKCR\CLSID for entries referenced in ContextMenuHandler
        ScanClsidEntries(results);
    }

    /// <summary>Сканирует прямые значения ключа реестра.
    /// Winlogon: сканируем только Shell/Userinit (остальные ~15 значений — параметры
    /// конфигурации, не автозапуск). BootExecute: только значение BootExecute
    /// (autochk) — остальное (BootShell и пр.) не является точкой автозапуска.</summary>
    private static void ScanKeyValues(RegistryKey key, string keyPath, EntryLocation location,
        string hivePrefix, List<StartupEntry> results)
    {
        var detailPrefix = $@"{hivePrefix}\{keyPath}";

        foreach (var valueName in key.GetValueNames())
        {
            var raw = key.GetValue(valueName);
            if (raw is not string value || string.IsNullOrWhiteSpace(value) || value.Length > 2000) continue;
            // Winlogon: остальные ~15 значений — параметры конфигурации, не автозапуск
            if (location == EntryLocation.Winlogon && valueName is not ("Shell" or "Userinit"))
                continue;
            if (location == EntryLocation.BootExecute &&
                !string.Equals(valueName, "BootExecute", StringComparison.OrdinalIgnoreCase))
                continue;
            // Метаданные и значения-не-команды (числа, "yes", имена DLL) не попадают в автозапуск
            if (!WatcherHelper.LooksLikeCommand(value)) continue;

            var filePath = WatcherHelper.ParsePath(value);
            var entry = new StartupEntry
            {
                Name = valueName,
                FilePath = filePath,
                CommandLine = value,
                Location = location,
                LocationDetail = $@"{detailPrefix}\{valueName}",
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
            results.Add(entry);
        }
    }

    /// <summary>Сканирует подразделы 1-го уровня ключа реестра.</summary>
    private static void ScanSubKeyValues(RegistryKey parentKey, string keyPath, EntryLocation location,
        string hivePrefix, List<StartupEntry> results)
    {
        string[] subKeyNames;
        try { subKeyNames = parentKey.GetSubKeyNames(); }
        catch { return; }

        foreach (var subKeyName in subKeyNames)
        {
            try
            {
                using var subKey = parentKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                foreach (var valueName in subKey.GetValueNames())
                {
                    var raw = subKey.GetValue(valueName);
                    if (raw is not string value || string.IsNullOrWhiteSpace(value) || value.Length > 2000) continue;
                    // Записи вида {GUID}\CLSID, метаданные кнопок и значения-не-команды — не автозапуск
                    if (!WatcherHelper.LooksLikeCommand(value)) continue;

                    var filePath = WatcherHelper.ParsePath(value);
                    var entry = new StartupEntry
                    {
                        Name = $@"{subKeyName}\{valueName}",
                        FilePath = filePath,
                        CommandLine = value,
                        Location = location,
                        LocationDetail = $@"{hivePrefix}\{keyPath}\{subKeyName}\{valueName}",
                    };
                    entry.Id = WatcherHelper.ComputeStableId(entry);
                    EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
                    results.Add(entry);
                }
            }
            catch
            {
                // Пропускаем защищенные подразделы (SecurityException / UnauthorizedAccessException)
            }
        }
    }

    /// <summary>Сканирует компоненты Active Setup: одна запись = подраздел {GUID} с непустым
    /// StubPath (не деактивированный '>'). Значения-метаданные (ComponentID, Locale, Version)
    /// и ключи без StubPath не выполняются при входе — пропускаются.</summary>
    private static void ScanActiveSetupSubKeys(RegistryKey parentKey, string keyPath,
        EntryLocation location, string hivePrefix, List<StartupEntry> results)
    {
        string[] subKeyNames;
        try { subKeyNames = parentKey.GetSubKeyNames(); }
        catch { return; }

        foreach (var subKeyName in subKeyNames)
        {
            // Деактивированный компонент (>GUID) не исполняется
            if (subKeyName.StartsWith('>')) continue;

            try
            {
                using var subKey = parentKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var stub = WatcherHelper.GetActiveSetupStubPath(subKey);
                if (stub == null) continue;

                var entry = new StartupEntry
                {
                    Name = subKeyName,
                    FilePath = WatcherHelper.ParsePath(stub),
                    CommandLine = stub,
                    Location = location,
                    LocationDetail = $@"{hivePrefix}\{keyPath}\{subKeyName}",
                };
                entry.Id = WatcherHelper.ComputeStableId(entry);
                EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
                results.Add(entry);
            }
            catch
            {
            }
        }
    }


    /// <summary>Сканирует ТОЛЬКО реально зарегистрированные в системе Shell Context Menu Handlers:
    /// собирает активные GUID из *\shellex\ContextMenuHandlers и родственных веток HKCR,
    /// затем читает путь к DLL/EXE только для этого множества из HKCR\CLSID\{GUID}\InprocServer32|LocalServer32.</summary>
    private static void ScanClsidEntries(List<StartupEntry> results)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ScanClsid");
        // Ветки HKCR, где регистрируются активные обработчики контекстного меню
        string[] handlerRoots =
        [
            @"*\shellex\ContextMenuHandlers",
            @"Directory\shellex\ContextMenuHandlers",
            @"Folder\shellex\ContextMenuHandlers",
            @"Drive\shellex\ContextMenuHandlers",
            @"AllFileSystemObjects\shellex\ContextMenuHandlers",
            @"Directory\Background\shellex\ContextMenuHandlers",
        ];

        // GUID -> имя обработчика (первое встретившееся, для поля Name)
        var activeHandlers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in handlerRoots)
        {
            try
            {
                using var rootKey = Registry.ClassesRoot.OpenSubKey(root);
                if (rootKey == null) continue;

                string[] handlerNames;
                try { handlerNames = rootKey.GetSubKeyNames(); }
                catch { continue; }

                foreach (var handlerName in handlerNames)
                {
                    try
                    {
                        string? guid = null;
                        var trimmedName = handlerName.Trim();
                        // Само имя подраздела может быть GUID
                        if (trimmedName.StartsWith('{') && trimmedName.EndsWith('}'))
                        {
                            guid = trimmedName;
                        }
                        else
                        {
                            using var handlerKey = rootKey.OpenSubKey(handlerName);
                            var defVal = handlerKey?.GetValue(null)?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(defVal))
                            {
                                // Значение по умолчанию — обычно "{GUID}", извлекаем GUID из строки
                                var open = defVal.IndexOf('{');
                                var close = open >= 0 ? defVal.IndexOf('}', open + 1) : -1;
                                if (open >= 0 && close > open)
                                    guid = defVal.Substring(open, close - open + 1);
                                else if (defVal.StartsWith('{'))
                                    guid = defVal;
                            }
                        }

                        if (string.IsNullOrEmpty(guid)) continue;
                        if (!activeHandlers.ContainsKey(guid))
                            activeHandlers[guid] = handlerName;
                    }
                    catch
                    {
                        // Пропускаем отдельный повреждённый обработчик
                    }
                }
            }
            catch
            {
                // Пропускаем недоступную ветку реестра
            }
        }

        if (activeHandlers.Count == 0) return;

        try
        {
            using var clsidRoot = Registry.ClassesRoot.OpenSubKey("CLSID");
            if (clsidRoot == null) return;

            foreach (var (guid, handlerName) in activeHandlers)
            {
                try
                {
                    using var clsidKey = clsidRoot.OpenSubKey(guid);
                    if (clsidKey == null) continue;

                    var displayName = clsidKey.GetValue(null)?.ToString();
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = handlerName;

                    foreach (var subKeyName in new[] { "InprocServer32", "LocalServer32" })
                    {
                        try
                        {
                            using var subKey = clsidKey.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var dllPath = subKey.GetValue(null)?.ToString();
                            if (string.IsNullOrWhiteSpace(dllPath)) continue;

                            var entry = new StartupEntry
                            {
                                Name = displayName,
                                FilePath = dllPath,
                                CommandLine = dllPath,
                                Location = EntryLocation.ContextMenuHandler,
                                LocationDetail = guid,
                            };
                            entry.Id = WatcherHelper.ComputeStableId(entry);
                            EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
                            results.Add(entry);
                            break; // достаточно одного сервера (InprocServer32 приоритетнее)
                        }
                        catch
                        {
                            // Пропускаем недоступный подраздел сервера
                        }
                    }
                }
                catch
                {
                    // Пропускаем отдельный повреждённый CLSID
                }
            }
        }
        catch { }
    }

    /// <summary>Проверяет, доступен ли указанный ключ реестра в HKCU.</summary>
    private static bool IsHkcuOtherKey(string keyPath)
    {
        return keyPath.StartsWith("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Group Policy", StringComparison.OrdinalIgnoreCase)
            || keyPath.StartsWith("SOFTWARE\\Classes", StringComparison.OrdinalIgnoreCase)
            || keyPath.StartsWith("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", StringComparison.OrdinalIgnoreCase)
            || keyPath == @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows"
            || keyPath == @"SOFTWARE\Microsoft\Active Setup\Installed Components";
    }

    // ───────── Startup Folders ─────────

    /// <summary>Сканирует папки автозапуска всех пользователей.</summary>
    private static void ScanStartupFolders(List<StartupEntry> results)
    {
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Общая папка (All Users)
        var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        if (!string.IsNullOrEmpty(commonStartup) && Directory.Exists(commonStartup))
            ScanFolder(commonStartup, results, scanned);

        // Текущий пользователь
        var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (!string.IsNullOrEmpty(userStartup) && Directory.Exists(userStartup))
            ScanFolder(userStartup, results, scanned);

        // Все остальные профили
        var profilesDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        profilesDir = Path.GetDirectoryName(profilesDir);
        if (profilesDir != null)
        {
            var usersDir = Path.Combine(profilesDir, "Users");
            if (!Directory.Exists(usersDir))
                usersDir = "C:\\Users";

            if (Directory.Exists(usersDir))
            {
                foreach (var userDir in Directory.GetDirectories(usersDir))
                {
                    var startup = Path.Combine(userDir, "AppData", "Roaming", "Microsoft",
                        "Windows", "Start Menu", "Programs", "Startup");
                    if (Directory.Exists(startup))
                        ScanFolder(startup, results, scanned);
                }
            }
        }
    }

    /// <summary>Сканирует одну папку автозапуска, добавляя файлы в результаты.</summary>
    private static void ScanFolder(string folder, List<StartupEntry> results, HashSet<string> scanned)
    {
        string[] files;
        try { files = Directory.GetFiles(folder); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        foreach (var file in files)
        {
            // Отключённые нами файлы (*.lnk.disabled) — не новые записи
            if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
            // desktop.ini — системный файл папки, не автозапуск
            if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            if (!scanned.Add(file)) continue;

            var fileInfo = new FileInfo(file);
            if (!fileInfo.Exists) continue;

            var entry = new StartupEntry
            {
                Name = fileInfo.Name,
                FilePath = fileInfo.FullName,
                Location = EntryLocation.StartupFolder,
                LocationDetail = fileInfo.DirectoryName ?? string.Empty,
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
            results.Add(entry);
        }
    }

    // ───────── Services ─────────

    /// <summary>Сканирует автоматические службы через WMI.</summary>
    private void ScanServices(List<StartupEntry> results)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ScanServices");
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PathName, DisplayName, StartMode, State FROM Win32_Service WHERE StartMode = 'Auto'");

            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? string.Empty;
                var path = obj["PathName"]?.ToString() ?? string.Empty;
                var displayName = obj["DisplayName"]?.ToString() ?? string.Empty;
                var state = obj["State"]?.ToString() ?? string.Empty;

                var filePath = WatcherHelper.ParseServicePath(path);

                var entry = new StartupEntry
                {
                    Name = displayName,
                    FilePath = filePath,
                    CommandLine = path,
                    Location = EntryLocation.Service,
                    LocationDetail = name,
                    Status = string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase)
                        ? EntryStatus.Enabled : EntryStatus.Disabled,
                };
                entry.Id = WatcherHelper.ComputeStableId(entry);
                EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
                results.Add(entry);
            }
        }
        catch
        {
            // Секция упала (WMI недоступен) — снимок неполный, cleanup пропустит службы
            _failedLocations.Add(EntryLocation.Service);
            _failedLocations.Add(EntryLocation.Driver);
        }
    }

    // ───────── WMI Event Subscriptions ─────────

    /// <summary>Сканирует подписки на события WMI (root\subscription) — постоянная точка
    /// автозапуска (EventFilter → CommandLineEventConsumer / ActiveScriptEventConsumer).</summary>
    private void ScanWmiSubscriptions(List<StartupEntry> results)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ScanWmi");
        try
        {
            var subs = WmiSubscriptionHelper.ScanSubscriptions();
            foreach (var sub in subs)
            {
                EntryOperations.ResolveAndEnrichEntry(sub, computeHash: false);
                results.Add(sub);
            }
        }
        catch
        {
            // root\subscription недоступен — не роняем скан
            _failedLocations.Add(EntryLocation.WmiEventSubscription);
        }
    }

    // ───────── Task Scheduler ─────────

    /// <summary>Сканирует задания в планировщике задач.</summary>
    private void ScanTaskScheduler(List<StartupEntry> results)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ScanTaskScheduler");
        try
        {
            using var ts = new TaskService();
            var tasks = ts.AllTasks;
            if (tasks == null) return;

            foreach (var task in tasks)
            {
                var taskCmd = FormatTaskAction(task);

                var entry = new StartupEntry
                {
                    Name = task.Name,
                    FilePath = taskCmd,
                    CommandLine = taskCmd,
                    Location = EntryLocation.TaskScheduler,
                    LocationDetail = task.Path ?? string.Empty,
                    Status = task.Enabled ? EntryStatus.Enabled : EntryStatus.Disabled,
                };
                entry.Id = WatcherHelper.ComputeStableId(entry);
                EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
                results.Add(entry);
            }
        }
        catch
        {
            // Секция упала — снимок неполный, cleanup пропустит задачи планировщика
            _failedLocations.Add(EntryLocation.TaskScheduler);
        }
    }

    /// <summary>Извлекает команду из действия задачи планировщика.</summary>
    private static string FormatTaskAction(Microsoft.Win32.TaskScheduler.Task task)
    {
        try
        {
            var action = task.Definition.Actions.FirstOrDefault();
            if (action is ExecAction exec)
            {
                var cmd = exec.Path?.Trim('"') ?? "";
                var args = exec.Arguments?.Trim() ?? "";
                return string.IsNullOrEmpty(args) ? cmd : $"{cmd} {args}";
            }
        }
        catch { }
        return task.Name;
    }

    // ───────── AppX Startup Tasks ─────────

    /// <summary>Сканирует автозапуск UWP/AppX-приложений: перебирает манифесты пакетов
    /// в WindowsApps, находит StartupTask-расширения и читает состояние (State) из
    /// реестра SystemAppData для текущего пользователя.</summary>
    private void ScanAppxStartupTasks(List<StartupEntry> results,
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        using var _prof = ExecutionProfiler.Measure("Service.ScanAppx");
        try
        {
            var appsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (!Directory.Exists(appsRoot)) return;

            logger?.LogInformation("Scanner: AppX StartupTask — пакетов {Count}", Directory.GetDirectories(appsRoot).Length);

            foreach (var pkgDir in Directory.GetDirectories(appsRoot))
            {
                try
                {
                    var manifestPath = Path.Combine(pkgDir, "AppxManifest.xml");
                    if (!File.Exists(manifestPath)) continue;

                    var (taskId, exeFile, displayName) = ReadStartupTaskFromManifest(manifestPath);
                    if (string.IsNullOrEmpty(taskId)) continue;

                    // Executable часто не указан в манифесте (берётся имплицитно) —
                    // подставляем первый .exe в корне пакета
                    if (string.IsNullOrEmpty(exeFile))
                        exeFile = Directory.GetFiles(pkgDir, "*.exe").FirstOrDefault();
                    if (string.IsNullOrEmpty(exeFile)) continue;

                    var filePath = Path.Combine(pkgDir, exeFile);
                    // PFN = Name_hash, напр. MSTeams_8wekyb3d8bbwe (без версии/архитектуры)
                    var pkgDirName = Path.GetFileName(pkgDir);
                    var pfn = pkgDirName.Split('_').Length >= 2
                        ? $"{pkgDirName.Split('_')[0]}_{pkgDirName.Split('_')[^1]}"
                        : pkgDirName;
                    var state = AppxStartupTaskHelper.ReadState(pfn, taskId);
                    logger?.LogInformation("Scanner: AppX StartupTask — найден {TaskId} в {Pkg} (exe={Exe}, state={State})",
                        taskId, Path.GetFileName(pkgDir), exeFile, state);

                    results.Add(new StartupEntry
                    {
                        Name = displayName,
                        FilePath = filePath,
                        CommandLine = filePath,
                        Location = EntryLocation.AppxStartupTask,
                        LocationDetail = $"{taskId} ({Path.GetFileName(pkgDir)})",
                        Status = state ? EntryStatus.Enabled : EntryStatus.Disabled,
                        DetectedAt = DateTime.UtcNow,
                    });
                }
                catch (Exception ex)
                {
                    // Отдельный пакет может быть повреждён — пропускаем, не роняя всю секцию
                    logger?.LogWarning(ex, "Scanner: AppX StartupTask — ошибка в пакете {Pkg}", Path.GetFileName(pkgDir));
                }
            }
        }
        catch (Exception ex)
        {
            // Секция может упасть при отсутствии прав на WindowsApps или повреждённом манифесте
            _failedLocations.Add(EntryLocation.AppxStartupTask);
            logger?.LogWarning(ex, "Scanner: AppX StartupTask — ошибка всей секции");
        }
    }

    /// <summary>Читает из манифеста AppX-пакета StartupTask: (TaskId, exe файл, отображаемое имя).
    /// Возвращает пустые строки, если StartupTask в манифесте нет.</summary>
    internal static (string, string, string) ReadStartupTaskFromManifest(string manifestPath)
    {
        try
        {
            var doc = XDocument.Load(manifestPath);
            var desk = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

            var ext = doc.Descendants(XName.Get("Extension", desk))
                .FirstOrDefault(e => e.Attribute("Category")?.Value == "windows.startupTask");
            if (ext == null) return (string.Empty, string.Empty, string.Empty);

            var task = ext.Descendants(XName.Get("StartupTask", desk)).FirstOrDefault();
            if (task == null) return (string.Empty, string.Empty, string.Empty);

            var taskId = task.Attribute("TaskId")?.Value ?? string.Empty;

            var app = ext.Ancestors().FirstOrDefault(e => e.Name.LocalName == "Application");
            var exe = task.Attribute("Executable")?.Value
                ?? app?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable")?.Value
                ?? string.Empty;

            // VisualElements может быть в разных namespace (uap/windows10) — ищем по local-name
            var ve = app?.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            var displayName = ve?.Attribute("DisplayName")?.Value ?? taskId;

            return (taskId, exe, displayName);
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    /// <summary>Пытается сопоставить запись из БД с записью из свежего скана.
    /// Использует несколько стратегий: LocationDetail, Name+FilePath, только FilePath, последний сегмент Name.</summary>
    public static bool TryMatch(StartupEntry dbEntry, StartupEntry scannedEntry)
    {
        if (dbEntry.Location != scannedEntry.Location)
            return false;

        // Primary: LocationDetail совпадает
        if (!string.IsNullOrEmpty(dbEntry.LocationDetail) &&
            !string.IsNullOrEmpty(scannedEntry.LocationDetail) &&
            string.Equals(dbEntry.LocationDetail, scannedEntry.LocationDetail, StringComparison.OrdinalIgnoreCase))
            return true;

        // Secondary: Name + FilePath совпадают
        if (!string.IsNullOrEmpty(dbEntry.Name) &&
            string.Equals(dbEntry.Name, scannedEntry.Name, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(dbEntry.FilePath) &&
            string.Equals(dbEntry.FilePath, scannedEntry.FilePath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Tertiary: последний сегмент Name совпадает (Winlogon subkey vs value name)
        if (!string.IsNullOrEmpty(dbEntry.Name) && dbEntry.Name.Contains('\\'))
        {
            var lastSeg = dbEntry.Name.Split('\\')[^1];
            if (string.Equals(lastSeg, scannedEntry.Name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(dbEntry.FilePath) &&
                string.Equals(dbEntry.FilePath, scannedEntry.FilePath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Кэш HashSet для быстрой проверки <see cref="VerifyInScan"/>:
    /// переиспользуется между вызовами по одной и той же ссылке списка скана,
    /// чтобы цикл CleanupStaleEntries работал за O(N+M), а не O(N*M).</summary>
    private static readonly object _verifyCacheLock = new();
    private static List<StartupEntry>? _verifyCacheSource;
    private static int _verifyCacheCount = -1;
    private static HashSet<string>? _verifyDetailSet;
    private static HashSet<string>? _verifyNamePathSet;

    /// <summary>Проверяет, находится ли запись в результатах скана.
    /// При scanResults &gt; 100 использует HashSet по (Location, LocationDetail) и
    /// (Location, Name, FilePath) вместо линейного Any(TryMatch).</summary>
    public static bool VerifyInScan(StartupEntry entry, List<StartupEntry> scanResults)
    {
        if (scanResults.Count <= 100)
            return scanResults.Any(s => TryMatch(entry, s));

        HashSet<string> detailSet;
        HashSet<string> namePathSet;
        lock (_verifyCacheLock)
        {
            if (!ReferenceEquals(_verifyCacheSource, scanResults)
                || _verifyCacheCount != scanResults.Count
                || _verifyDetailSet == null
                || _verifyNamePathSet == null)
            {
                var details = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var namePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in scanResults)
                {
                    if (!string.IsNullOrEmpty(s.LocationDetail))
                        details.Add($"{(int)s.Location}\0{s.LocationDetail}");
                    if (!string.IsNullOrEmpty(s.Name) && !string.IsNullOrEmpty(s.FilePath))
                        namePaths.Add($"{(int)s.Location}\0{s.Name}\0{s.FilePath}");
                }
                _verifyCacheSource = scanResults;
                _verifyCacheCount = scanResults.Count;
                _verifyDetailSet = details;
                _verifyNamePathSet = namePaths;
            }
            detailSet = _verifyDetailSet;
            namePathSet = _verifyNamePathSet;
        }

        // Primary: (Location, LocationDetail)
        if (!string.IsNullOrEmpty(entry.LocationDetail) &&
            detailSet.Contains($"{(int)entry.Location}\0{entry.LocationDetail}"))
            return true;

        // Secondary: (Location, Name, FilePath)
        if (!string.IsNullOrEmpty(entry.Name) && !string.IsNullOrEmpty(entry.FilePath) &&
            namePathSet.Contains($"{(int)entry.Location}\0{entry.Name}\0{entry.FilePath}"))
            return true;

        // Tertiary (редкий случай: Name вида "subkey\value"): только такие записи
        // проверяем линейно через полный TryMatch
        if (!string.IsNullOrEmpty(entry.Name) && entry.Name.Contains('\\'))
            return scanResults.Any(s => TryMatch(entry, s));

        return false;
    }

    /// <summary>Диагностика: логирует почему entry не найден в scanResults.</summary>
    public static void DebugMatch(StartupEntry dbEntry, List<StartupEntry> scanResults,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        logger.LogInformation("▶ DIAG: DB entry \"{Name}\" Location={Loc} Detail=\"{Detail}\" Path=\"{Path}\"",
            dbEntry.Name, dbEntry.Location, dbEntry.LocationDetail, dbEntry.FilePath);

        var sameLoc = scanResults.Where(s => s.Location == dbEntry.Location).ToList();
        logger.LogInformation("  В скане {Total} записей, из них с Location={Loc}: {Count}",
            scanResults.Count, dbEntry.Location, sameLoc.Count);

        if (sameLoc.Count == 0)
        {
            // Показываем все доступные Location в скане
            var locs = scanResults.Select(s => s.Location).Distinct().ToList();
            logger.LogInformation("  Доступные Location в скане: [{Locs}]",
                string.Join(", ", locs));
            return;
        }

        // Показываем первых 5 кандидатов с тем же Location
        var candidates = sameLoc.Take(5).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var reasons = new List<string>();

            if (!string.IsNullOrEmpty(dbEntry.LocationDetail) && !string.IsNullOrEmpty(c.LocationDetail))
            {
                if (string.Equals(dbEntry.LocationDetail, c.LocationDetail, StringComparison.OrdinalIgnoreCase))
                    reasons.Add("✓ Detail совпадает");
                else
                    reasons.Add($"✗ Detail: \"{dbEntry.LocationDetail}\" ≠ \"{c.LocationDetail}\"");
            }
            else
            {
                reasons.Add("⚠ Detail пустой");
            }

            if (!string.IsNullOrEmpty(dbEntry.Name) && !string.IsNullOrEmpty(c.Name))
            {
                if (string.Equals(dbEntry.Name, c.Name, StringComparison.OrdinalIgnoreCase))
                    reasons.Add("✓ Name совпадает");
                else
                    reasons.Add($"✗ Name: \"{dbEntry.Name}\" ≠ \"{c.Name}\"");
            }

            if (!string.IsNullOrEmpty(dbEntry.FilePath) && !string.IsNullOrEmpty(c.FilePath))
            {
                if (string.Equals(dbEntry.FilePath, c.FilePath, StringComparison.OrdinalIgnoreCase))
                    reasons.Add("✓ Path совпадает");
                else
                    reasons.Add($"✗ Path: \"{dbEntry.FilePath}\" ≠ \"{c.FilePath}\"");
            }

            // Winlogon subkey check
            if (dbEntry.Name.Contains('\\'))
            {
                var lastSeg = dbEntry.Name.Split('\\')[^1];
                if (string.Equals(lastSeg, c.Name, StringComparison.OrdinalIgnoreCase))
                    reasons.Add("✓ LastSeg совпадает");
                else
                    reasons.Add($"✗ LastSeg: \"{lastSeg}\" ≠ \"{c.Name}\"");
            }

            logger.LogInformation("  Канд[{Idx}] Name=\"{Name}\" Detail=\"{Detail}\" Path=\"{Path}\" → {Reasons}",
                i, c.Name, c.LocationDetail, c.FilePath, string.Join("; ", reasons));
        }
    }
}
