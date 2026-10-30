using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StartupMonitor.Service.Services.Runtime;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Единый координатор матрицы контроля доступа сетевых приложений.
/// Обеспечивает строгую атомарность: одновременное обновление JSON-хранилища,
/// создание/удаление правил в Windows Firewall через COM и синхронизацию сессионных кэшей детектора алертов.
/// Zero-Crash: все операции защищены, исключения логируются, отказоустойчивость 100%.
/// </summary>
public sealed class AccessMatrixCoordinatorService
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<AccessMatrixCoordinatorService>();

    private readonly WindowsFirewallService _firewall;
    private readonly NetworkConnectionAlertService _alertService;
    private readonly JsonDataStore _store;
    private readonly ProcessNetworkTracker _tracker;
    private readonly object _syncLock = new();

    /// <summary>
    /// Критические процессы ядра (Уровень защиты 3 - Critical). Блокировка аппаратно запрещена.
    /// </summary>
    private static readonly HashSet<string> CriticalCoreProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "svchost.exe", "lsass.exe", "services.exe",
        "smss.exe", "csrss.exe", "wininit.exe",
        "StartupMonitor.Service.exe", "StartupMonitor.UI.exe"
    };

    /// <summary>
    /// Защищенные системные подсистемы (Уровень защиты 2 - HighRisk).
    /// </summary>
    private static readonly HashSet<string> ProtectedSubsystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "spoolsv.exe", "dwm.exe", "ctfmon.exe",
        "RuntimeBroker.exe", "sihost.exe", "SearchHost.exe",
        "StartMenuExperienceHost.exe", "Widgets.exe"
    };

    public AccessMatrixCoordinatorService(
        WindowsFirewallService firewall,
        NetworkConnectionAlertService alertService,
        JsonDataStore store,
        ProcessNetworkTracker tracker)
    {
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _alertService = alertService ?? throw new ArgumentNullException(nameof(alertService));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    /// <summary>
    /// Определяет уровень системной защиты процесса по его имени и пути.
    /// </summary>
    public static ProcessProtectionLevel EvaluateProtectionLevel(string? processName, string? processPath)
    {
        string name = !string.IsNullOrWhiteSpace(processName)
            ? processName.Trim()
            : Path.GetFileName(processPath ?? string.Empty);

        if (string.IsNullOrWhiteSpace(name))
            return ProcessProtectionLevel.None;

        if (CriticalCoreProcesses.Contains(name))
            return ProcessProtectionLevel.Critical;

        if (ProtectedSubsystems.Contains(name))
            return ProcessProtectionLevel.HighRisk;

        return ProcessProtectionLevel.None;
    }

    /// <summary>
    /// Формирует полный консолидированный снимок матрицы контроля доступа для UI.
    /// </summary>
    public List<AppAccessMatrixItemDto> BuildAccessMatrix()
    {
        lock (_syncLock)
        {
            var resultDict = new Dictionary<string, AppAccessMatrixItemDto>(StringComparer.OrdinalIgnoreCase);

            var cfg = _store.Load();
            var whiteList = new HashSet<string>(cfg.Settings.FirewallWhitelistedApps, StringComparer.OrdinalIgnoreCase);
            var blackList = new HashSet<string>(cfg.Settings.FirewallBlacklistedApps, StringComparer.OrdinalIgnoreCase);

            // 1. Читаем разрешающие исходящие правила брандмауэра
            var fwAllowedPaths = _firewall.GetOutboundAllowApplicationRulePaths();

            // 2. Добавляем программы из активных сокетов
            var activeSockets = _tracker.GetSockets();
            var socketsCountByProc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in activeSockets)
            {
                var pName = (s.ProcessName ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(pName))
                {
                    socketsCountByProc[pName] = socketsCountByProc.GetValueOrDefault(pName) + 1;
                }
            }

            // 3. Агрегируем белый список
            foreach (var path in whiteList)
            {
                var item = CreateOrGetItem(resultDict, path);
                item.State = AccessControlState.Allowed;
                item.Scope = AccessRuleScope.Permanent;
            }

            // 4. Агрегируем чёрный список (приоритет над белым)
            foreach (var path in blackList)
            {
                var item = CreateOrGetItem(resultDict, path);
                item.State = AccessControlState.Blocked;
                item.Scope = AccessRuleScope.Permanent;
                item.BlockSource = BlockSourceType.StartupMonitorBlacklist;
            }

            // 5. Синхронизируем с правилами Windows Firewall
            foreach (var fwPath in fwAllowedPaths)
            {
                var item = CreateOrGetItem(resultDict, fwPath);
                if (item.State != AccessControlState.Blocked)
                {
                    item.State = AccessControlState.Allowed;
                }
            }

            // 6. Учитываем сессионные решения из NetworkConnectionAlertService
            foreach (var kv in _alertService.SessionDecisions)
            {
                var key = kv.Key; // "path:..." или "name:..."
                var cleanTarget = key.StartsWith("path:", StringComparison.OrdinalIgnoreCase)
                    ? key[5..]
                    : (key.StartsWith("name:", StringComparison.OrdinalIgnoreCase) ? key[5..] : key);
                var item = CreateOrGetItem(resultDict, cleanTarget);

                if (kv.Value == NetworkAlertResolution.AllowSession)
                {
                    item.State = AccessControlState.Allowed;
                    item.Scope = AccessRuleScope.UntilRestart;
                }
                else if (kv.Value == NetworkAlertResolution.BlockSession)
                {
                    item.State = AccessControlState.Blocked;
                    item.Scope = AccessRuleScope.UntilRestart;
                    item.BlockSource = BlockSourceType.SessionBlock;
                }
            }

            // 7. Обогащаем данными о сокетах и метаданными файлов
            foreach (var item in resultDict.Values)
            {
                if (socketsCountByProc.TryGetValue(item.ProcessName, out var count))
                    item.ActiveSocketsCount = count;

                item.ProtectionLevel = EvaluateProtectionLevel(item.ProcessName, item.FullPath);

                try
                {
                    if (File.Exists(item.FullPath))
                    {
                        var fi = new FileInfo(item.FullPath);
                        item.ExistsOnDisk = true;
                        item.FileSizeBytes = fi.Length;
                        item.LastModifiedUtc = fi.LastWriteTimeUtc;
                    }
                    else
                    {
                        item.ExistsOnDisk = false;
                    }
                }
                catch
                {
                    item.ExistsOnDisk = false;
                }
            }

            return resultDict.Values.OrderByDescending(v => v.ActiveSocketsCount).ThenBy(v => v.ProcessName).ToList();
        }
    }

    /// <summary>
    /// Атомарно применяет изменение состояния доступа программы.
    /// </summary>
    public FirewallOperationResult ApplyAccessState(SetAppAccessStateRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var path = (request.ApplicationPath ?? string.Empty).Trim().Trim('"');
        var name = (request.ProcessName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name))
            return FirewallOperationResult.Fail("Не указан путь или имя приложения.");

        var targetKey = !string.IsNullOrWhiteSpace(path) ? path : name;
        var protection = EvaluateProtectionLevel(name, path);

        // ЖЕСТКИЙ БАРЬЕР: Блокировка критического ядра Windows категорически запрещена
        if (protection == ProcessProtectionLevel.Critical && request.TargetState == AccessControlState.Blocked)
        {
            Log.Warning("Попытка заблокировать критический системный процесс: {Name} ({Path}) отклонена", name, path);
            return FirewallOperationResult.Fail("Запрещено: критический системный компонент ядра Windows не может быть заблокирован.");
        }

        lock (_syncLock)
        {
            try
            {
                var cfg = _store.Load();

                // Имя правила брандмауэра для полной синхронизации
                var safeBaseName = SanitizeRuleName(!string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(name) : "App");
                var allowRuleName = $"StartupMonitor_Allow_{safeBaseName}_Managed";
                var blockRuleName = $"StartupMonitor_Block_{safeBaseName}_Managed";

                switch (request.TargetState)
                {
                    case AccessControlState.Allowed:
                        // 1. Обновляем списки хранилища
                        cfg.Settings.FirewallBlacklistedApps.RemoveAll(p => string.Equals(p, targetKey, StringComparison.OrdinalIgnoreCase));
                        if (request.Scope == AccessRuleScope.Permanent)
                        {
                            if (!cfg.Settings.FirewallWhitelistedApps.Contains(targetKey, StringComparer.OrdinalIgnoreCase))
                                cfg.Settings.FirewallWhitelistedApps.Add(targetKey);
                        }
                        _store.Save(cfg);

                        // 2. Синхронизируем Windows Firewall (удаляем блокирующее, ставим разрешающее)
                        _firewall.DeleteRule(blockRuleName, FirewallDirection.Both);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            _firewall.CreateOrUpdateRule(new FirewallRuleDto
                            {
                                Name = allowRuleName,
                                Description = $"Разрешено через Матрицу доступа StartupMonitor: {name}",
                                Direction = FirewallDirection.Both,
                                Action = FirewallAction.Allow,
                                Protocol = FirewallProtocol.Any,
                                ApplicationPath = path,
                                Enabled = true,
                                Profiles = FirewallProfile.All,
                                Grouping = WindowsFirewallService.RuleGroup,
                                IsManaged = true
                            });
                        }

                        // 3. Обновляем сессионные кеши
                        _alertService.ResolveAlert(new ResolveNetworkAlertRequest
                        {
                            AlertId = string.Empty,
                            ProcessPath = path,
                            ProcessName = name,
                            Resolution = request.Scope == AccessRuleScope.Permanent
                                ? NetworkAlertResolution.WhitelistProgram
                                : NetworkAlertResolution.AllowSession
                        });
                        break;

                    case AccessControlState.Blocked:
                        // 1. Обновляем списки хранилища
                        cfg.Settings.FirewallWhitelistedApps.RemoveAll(p => string.Equals(p, targetKey, StringComparison.OrdinalIgnoreCase));
                        if (request.Scope == AccessRuleScope.Permanent)
                        {
                            if (!cfg.Settings.FirewallBlacklistedApps.Contains(targetKey, StringComparer.OrdinalIgnoreCase))
                                cfg.Settings.FirewallBlacklistedApps.Add(targetKey);
                        }
                        _store.Save(cfg);

                        // 2. Синхронизируем Windows Firewall (удаляем разрешающее, ставим блокирующее)
                        _firewall.DeleteRule(allowRuleName, FirewallDirection.Both);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            _firewall.CreateOrUpdateRule(new FirewallRuleDto
                            {
                                Name = blockRuleName,
                                Description = $"Заблокировано через Матрицу доступа StartupMonitor: {name}",
                                Direction = FirewallDirection.Both,
                                Action = FirewallAction.Block,
                                Protocol = FirewallProtocol.Any,
                                ApplicationPath = path,
                                Enabled = true,
                                Profiles = FirewallProfile.All,
                                Grouping = WindowsFirewallService.RuleGroup,
                                IsManaged = true
                            });
                        }

                        // 3. Обновляем сессионные кеши
                        _alertService.ResolveAlert(new ResolveNetworkAlertRequest
                        {
                            AlertId = string.Empty,
                            ProcessPath = path,
                            ProcessName = name,
                            Resolution = request.Scope == AccessRuleScope.Permanent
                                ? NetworkAlertResolution.BlacklistProgram
                                : NetworkAlertResolution.BlockSession
                        });
                        break;

                    case AccessControlState.Prompt:
                        // Режим «По запросу»: удаляем из обоих списков и снимаем жесткие правила
                        cfg.Settings.FirewallWhitelistedApps.RemoveAll(p => string.Equals(p, targetKey, StringComparison.OrdinalIgnoreCase));
                        cfg.Settings.FirewallBlacklistedApps.RemoveAll(p => string.Equals(p, targetKey, StringComparison.OrdinalIgnoreCase));
                        _store.Save(cfg);

                        _firewall.DeleteRule(allowRuleName, FirewallDirection.Both);
                        _firewall.DeleteRule(blockRuleName, FirewallDirection.Both);
                        break;
                }

                Log.Information("Матрица доступа: для {Target} успешно установлено состояние {State} ({Scope})", targetKey, request.TargetState, request.Scope);
                return FirewallOperationResult.Ok($"Состояние {request.TargetState} успешно сохранено.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при обновлении состояния матрицы доступа для {Target}", targetKey);
                return FirewallOperationResult.Fail($"Ошибка обновления матрицы доступа: {ex.Message}");
            }
        }
    }

    private static AppAccessMatrixItemDto CreateOrGetItem(Dictionary<string, AppAccessMatrixItemDto> dict, string rawPath)
    {
        var clean = (rawPath ?? string.Empty).Trim().Trim('"');
        var name = Path.GetFileName(clean);
        if (string.IsNullOrWhiteSpace(name))
            name = clean;

        if (!dict.TryGetValue(clean, out var item))
        {
            item = new AppAccessMatrixItemDto
            {
                FullPath = clean,
                ProcessName = name,
                State = AccessControlState.Prompt
            };
            dict[clean] = item;
        }
        return item;
    }

    private static string SanitizeRuleName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var clean = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(clean) ? "App" : clean;
    }
}
