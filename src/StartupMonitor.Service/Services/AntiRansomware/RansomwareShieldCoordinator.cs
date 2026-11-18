using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.Shared.Models.Ransomware;

namespace StartupMonitor.Service.Services.AntiRansomware;

/// <summary>
/// Центральный координатор подсистемы защиты от вирусов-шифровальщиков (Anti-Ransomware Shield).
/// Объединяет эвристику командной строки, мониторинг ловушек Canary, атомарную заморозку процессов,
/// сетевую изоляцию через Windows Firewall и самовосстановление файлов из теневых копий VSS.
/// </summary>
public sealed class RansomwareShieldCoordinator : IHostedService, IDisposable
{
    private readonly JsonDataStore _store;
    private readonly CanaryTrapService _canaryService;
    private readonly WindowsFirewallService? _firewallService;
    private readonly ILogger<RansomwareShieldCoordinator>? _logger;

    private readonly object _syncRoot = new();
    private readonly List<RansomwareIncidentDto> _recentIncidents = [];
    private readonly ConcurrentDictionary<int, DateTime> _suspendedProcesses = new();
    private RansomwareShieldConfigDto _config;
    private int _blockedAttacksCount;
    private int _suspendedProcessesCount;
    private int _restoredFilesCount;
    private DateTime? _lastAttackTimeUtc;
    private int _disposed;

    /// <summary>
    /// Создает экземпляр координатора RansomwareShieldCoordinator.
    /// </summary>
    public RansomwareShieldCoordinator(
        JsonDataStore store,
        CanaryTrapService canaryService,
        WindowsFirewallService? firewallService = null,
        ILogger<RansomwareShieldCoordinator>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _canaryService = canaryService ?? throw new ArgumentNullException(nameof(canaryService));
        _firewallService = firewallService;
        _logger = logger;

        var loadedConfig = _store.Load();
        _config = loadedConfig.Settings.RansomwareShield ?? new RansomwareShieldConfigDto();
    }

    /// <summary>
    /// Запуск координатора и фоновых служб защиты.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Запуск RansomwareShieldCoordinator (Anti-Ransomware EDR-lite)...");

        _canaryService.TrapTriggered += OnCanaryTrapTriggered;

        if (_config.Enabled && _config.EnableCanaryTraps)
        {
            await _canaryService.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Остановка координатора.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Остановка RansomwareShieldCoordinator...");
        _canaryService.TrapTriggered -= OnCanaryTrapTriggered;
        await _canaryService.StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает текущую конфигурацию защиты.
    /// </summary>
    public RansomwareShieldConfigDto GetConfig()
    {
        lock (_syncRoot)
        {
            return _config;
        }
    }

    /// <summary>
    /// Обновляет параметры конфигурации и сохраняет их в хранилище.
    /// </summary>
    public void UpdateConfig(RansomwareShieldConfigDto newConfig)
    {
        ArgumentNullException.ThrowIfNull(newConfig);

        lock (_syncRoot)
        {
            _config = newConfig;

            var current = _store.Load();
            current.Settings.RansomwareShield = newConfig;
            _store.Save(current);

            if (_config.Enabled && _config.EnableCanaryTraps)
            {
                _ = _canaryService.StartAsync();
            }
            else
            {
                _ = _canaryService.StopAsync();
            }

            _logger?.LogInformation("Конфигурация Anti-Ransomware Shield обновлена (Enabled={Enabled})", newConfig.Enabled);
        }
    }

    /// <summary>
    /// Возвращает агрегированную статистику защиты.
    /// </summary>
    public RansomwareShieldStatsDto GetStats()
    {
        lock (_syncRoot)
        {
            return new RansomwareShieldStatsDto
            {
                IsActive = _config.Enabled,
                ActiveTrapsCount = _canaryService.ActiveTrapsCount,
                ProtectedVolumesCount = 1,
                BlockedAttacksCount = _blockedAttacksCount,
                SuspendedProcessesCount = _suspendedProcessesCount,
                RestoredFilesCount = _restoredFilesCount,
                LastAttackTimeUtc = _lastAttackTimeUtc,
                RecentIncidents = [.. _recentIncidents]
            };
        }
    }

    /// <summary>
    /// Точка входа для анализа запуска новых процессов (ETW ProcessStart).
    /// Перехватывает деструктивные команды уничтожения теневых копий до начала шифрования.
    /// </summary>
    public bool InspectProcessCommandLine(int pid, int parentPid, string commandLine, string imageName)
    {
        if (!_config.Enabled || !_config.ProtectVssShadowCopies)
            return false;

        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        var verdict = RansomwareCliDetector.Evaluate(Path.GetFileName(imageName), commandLine);
        if (!verdict.IsThreat)
            return false;

        _logger?.LogCritical("ОБНАРУЖЕНА ДЕСТРУКТИВНАЯ КОМАНДА: {Pattern} (PID {Pid}, ParentPID {ParentPid}): {Cmd}",
            verdict.MatchedPattern, pid, parentPid, commandLine);

        // 1. Атомарно замораживаем родительский процесс (сам шифровальщик/скрипт)
        if (parentPid > 4 && _config.AutoSuspendProcess)
        {
            var res = ProcessFreezer.SuspendProcess(parentPid);
            if (res.Success)
            {
                _suspendedProcesses[parentPid] = DateTime.UtcNow;
                Interlocked.Increment(ref _suspendedProcessesCount);
                _logger?.LogWarning("Родительский процесс атаки {ParentPid} успешно заморожен через NtSuspendProcess", parentPid);
            }
        }

        // 2. Сетевая изоляция родительского процесса через правило брандмауэра
        if (parentPid > 4 && _config.IsolateNetworkOnAttack && _firewallService != null)
        {
            try
            {
                var rule = new FirewallRuleDto
                {
                    Name = $"StartupMonitor_AntiRansomware_{parentPid}",
                    Direction = FirewallDirection.Outbound,
                    Action = FirewallAction.Block,
                    Enabled = true,
                    Grouping = "StartupMonitor"
                };
                _firewallService.CreateOrUpdateRule(rule);
            }
            catch { }
        }

        // 3. Завершаем дочернюю утилиту (vssadmin/bcdedit)
        try
        {
            var childProc = Process.GetProcessById(pid);
            childProc.Kill();
        }
        catch { }

        // 4. Регистрируем инцидент
        RegisterIncident(new RansomwareIncidentDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            ProcessId = parentPid > 0 ? parentPid : pid,
            ProcessName = Path.GetFileName(imageName),
            ProcessPath = imageName,
            TriggerReason = RansomwareTriggerType.VssTamper,
            ActionTaken = RansomwareActionType.Suspended,
            AffectedFiles = ["VSS Shadow Copies & Recovery BCD"]
        });

        return true;
    }

    private void OnCanaryTrapTriggered(string trapPath, int attackerPid)
    {
        if (!_config.Enabled)
            return;

        _logger?.LogCritical("СРАБАТЫВАНИЕ ЛОВУШКИ CANARY TRAP: {Path} (PID: {Pid})", trapPath, attackerPid);

        string procName = "Unknown";
        string procPath = string.Empty;

        // 1. Атомарная заморозка процесса-виновника
        if (attackerPid > 4 && _config.AutoSuspendProcess)
        {
            try
            {
                var proc = Process.GetProcessById(attackerPid);
                procName = proc.ProcessName;
                try { procPath = proc.MainModule?.FileName ?? string.Empty; } catch { }

                var res = ProcessFreezer.SuspendProcess(attackerPid);
                if (res.Success)
                {
                    _suspendedProcesses[attackerPid] = DateTime.UtcNow;
                    Interlocked.Increment(ref _suspendedProcessesCount);
                    _logger?.LogWarning("Атакующий процесс {Pid} ({Name}) заморожен через NtSuspendProcess", attackerPid, procName);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Не удалось заморозить процесс {Pid}", attackerPid);
            }
        }

        // 2. Сетевая изоляция
        if (attackerPid > 4 && _config.IsolateNetworkOnAttack && _firewallService != null)
        {
            try
            {
                var rule = new FirewallRuleDto
                {
                    Name = $"StartupMonitor_AntiRansomware_{attackerPid}",
                    Direction = FirewallDirection.Outbound,
                    Action = FirewallAction.Block,
                    Enabled = true,
                    Grouping = "StartupMonitor"
                };
                _firewallService.CreateOrUpdateRule(rule);
            }
            catch { }
        }

        // 3. Автоматический откат поврежденной ловушки/файла
        bool restored = false;
        if (_config.AutoRollbackAffectedFiles)
        {
            try
            {
                restored = VssVolumeShadowRestorer.RestoreFileFromShadowCopy(trapPath, out _, out _);
                if (restored)
                {
                    Interlocked.Increment(ref _restoredFilesCount);
                    _logger?.LogInformation("Файл {Path} успешно восстановлен из теневой копии VSS", trapPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка отката файла {Path} из VSS", trapPath);
            }
        }

        // 4. Фиксация инцидента
        RegisterIncident(new RansomwareIncidentDto
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            ProcessId = attackerPid,
            ProcessName = procName,
            ProcessPath = procPath,
            TriggerReason = RansomwareTriggerType.CanaryModified,
            ActionTaken = restored ? RansomwareActionType.Restored : RansomwareActionType.Suspended,
            AffectedFiles = [trapPath]
        });
    }

    private void RegisterIncident(RansomwareIncidentDto incident)
    {
        lock (_syncRoot)
        {
            _lastAttackTimeUtc = DateTime.UtcNow;
            _blockedAttacksCount++;

            _recentIncidents.Insert(0, incident);
            if (_recentIncidents.Count > 100)
            {
                _recentIncidents.RemoveAt(_recentIncidents.Count - 1);
            }
        }
    }

    /// <summary>
    /// Освобождение ресурсов.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _canaryService.TrapTriggered -= OnCanaryTrapTriggered;
        _canaryService.Dispose();
    }
}
