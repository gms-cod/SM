using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.Wmi;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>
/// Фоновый сервис непрерывного мониторинга постоянных подписок на события WMI (root\subscription).
/// Отслеживает создание, модификацию и удаление CommandLineEventConsumer, ActiveScriptEventConsumer
/// и связок __FilterToConsumerBinding в реальном времени.
/// </summary>
public class WmiSubscriptionWatcher : BackgroundService
{
    private readonly JsonDataStore _store;
    private readonly BlacklistManager _blacklist;
    private readonly NotificationService _notifications;
    private readonly ILogger<WmiSubscriptionWatcher> _logger;
    private readonly MonitoringStateCoordinator _coordinator;

    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    public WmiSubscriptionWatcher(
        JsonDataStore store,
        BlacklistManager blacklist,
        NotificationService notifications,
        ILogger<WmiSubscriptionWatcher> logger,
        MonitoringStateCoordinator coordinator)
    {
        _store = store;
        _blacklist = blacklist;
        _notifications = notifications;
        _logger = logger;
        _coordinator = coordinator;
    }

    /// <summary>ExecuteAsync: Выполняет операцию ExecuteAsync.</summary>
    /// <param name="stoppingToken">Входные данные: значение параметра stoppingToken (CancellationToken).</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WmiSubscriptionWatcher started");

        // 1. Начальное сканирование и синхронизация при старте службы
        await RescanSubscriptionsAsync("InitialScan", isInitialScan: true, stoppingToken);

        // 2. Настраиваем WMI-подписки на события внутри пространства root\subscription
        ManagementEventWatcher? createWatcher = null;
        ManagementEventWatcher? modWatcher = null;
        ManagementEventWatcher? delWatcher = null;

        try
        {
            var scope = new ManagementScope(@"root\subscription");
            scope.Connect();

            // Создание консьюмера или биндинга
            var createQuery = new WqlEventQuery(
                "__InstanceCreationEvent",
                TimeSpan.FromSeconds(30),
                "TargetInstance ISA '__EventConsumer' OR TargetInstance ISA '__FilterToConsumerBinding'");
            createWatcher = new ManagementEventWatcher(scope, createQuery);
            createWatcher.EventArrived += (s, e) => ScheduleRescan("Creation");
            createWatcher.Start();

            // Модификация существующего консьюмера или биндинга
            var modQuery = new WqlEventQuery(
                "__InstanceModificationEvent",
                TimeSpan.FromSeconds(30),
                "TargetInstance ISA '__EventConsumer' OR TargetInstance ISA '__FilterToConsumerBinding'");
            modWatcher = new ManagementEventWatcher(scope, modQuery);
            modWatcher.EventArrived += (s, e) => ScheduleRescan("Modification");
            modWatcher.Start();

            // Удаление консьюмера или биндинга
            var delQuery = new WqlEventQuery(
                "__InstanceDeletionEvent",
                TimeSpan.FromSeconds(30),
                "TargetInstance ISA '__EventConsumer' OR TargetInstance ISA '__FilterToConsumerBinding'");
            delWatcher = new ManagementEventWatcher(scope, delQuery);
            delWatcher.EventArrived += (s, e) => ScheduleRescan("Deletion");
            delWatcher.Start();

            _logger.LogInformation("WmiSubscriptionWatcher: WMI-события root\\subscription успешно активированы");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WmiSubscriptionWatcher: не удалось подключить события WMI root\\subscription, переход на резервный поллинг");
        }

        // 3. Резервный периодический поллинг (каждые 30 секунд) на случай пропущенных событий ядра
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_coordinator.IsPaused) continue;
                await RescanSubscriptionsAsync("PeriodicPolling", isInitialScan: false, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { createWatcher?.Stop(); createWatcher?.Dispose(); } catch { }
            try { modWatcher?.Stop(); modWatcher?.Dispose(); } catch { }
            try { delWatcher?.Stop(); delWatcher?.Dispose(); } catch { }
            _debounceCts?.Dispose();
            _logger.LogInformation("WmiSubscriptionWatcher штатно остановлен");
        }
    }

    /// <summary>
    /// Планирует повторное сканирование с дебаунсом в 1 секунду
    /// (позволяет скриптам завершить последовательное создание Filter -> Consumer -> Binding).
    /// </summary>
    private void ScheduleRescan(string reason)
    {
        if (_coordinator.IsPaused) return;

        try
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
        }
        catch { }

        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, ct);
                if (ct.IsCancellationRequested || _coordinator.IsPaused) return;

                await RescanSubscriptionsAsync($"WmiEvent:{reason}", isInitialScan: false, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WmiSubscriptionWatcher: ошибка отложенного пересканирования");
            }
        }, ct);
    }

    /// <summary>
    /// Выполняет дифференциальное сканирование WMI-подписок с оповещением и синхронизацией хранилища.
    /// </summary>
    private async Task RescanSubscriptionsAsync(string reason, bool isInitialScan, CancellationToken ct)
    {
        using var _profWmi = ExecutionProfiler.Profile("Service.WmiScan");
        if (!await _scanLock.WaitAsync(100, ct)) return;

        try
        {
            var scanned = WmiSubscriptionHelper.ScanSubscriptions();
            var scannedIds = new HashSet<string>(scanned.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

            // Обрабатываем найденные записи
            foreach (var entry in scanned)
            {
                if (ct.IsCancellationRequested) break;

                var matchedRule = _blacklist.MatchEntry(entry);
                bool isNew = WatcherHelper.AddOrUpdate(_store, _notifications, entry, matchedRule, _blacklist);

                if (isNew && !isInitialScan)
                {
                    _logger.LogInformation(
                        "WMI EventSubscription обнаружена [{Reason}]: {Name}, тип: {Type}, триггер: {Filter}",
                        reason,
                        entry.Name,
                        entry.RawData.GetValueOrDefault("ConsumerType", "Unknown"),
                        entry.RawData.GetValueOrDefault("FilterName", "Unbound"));
                }
            }

            // Выявляем удалённые записи WMI
            var currentStore = _store.Load();
            var existingWmi = currentStore.Entries
                .Where(e => e.Location == EntryLocation.WmiEventSubscription)
                .ToList();

            foreach (var oldEntry in existingWmi)
            {
                if (!scannedIds.Contains(oldEntry.Id))
                {
                    _store.RemoveEntry(oldEntry.Id);
                    _store.AddHistory(new HistoryEntry
                    {
                        ChangeType = "Removed",
                        EntryName = oldEntry.Name,
                        EntryPath = oldEntry.FilePath,
                        Location = oldEntry.Location.ToString(),
                        Action = "WMI subscription deleted from root\\subscription"
                    });

                    _notifications.Enqueue(new PipeMessage
                    {
                        Type = PipeMessageTypes.EntryDeleted,
                        Payload = JsonSerializer.Serialize(oldEntry)
                    });

                    _logger.LogInformation("WMI EventSubscription удалена из системы: {Name} (Id={Id})", oldEntry.Name, oldEntry.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WmiSubscriptionWatcher: сбой дифференциального пересканирования ({Reason})", reason);
        }
        finally
        {
            _scanLock.Release();
        }
    }
}
