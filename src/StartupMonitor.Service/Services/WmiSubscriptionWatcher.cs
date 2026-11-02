using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
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
    /// <summary>Одна очередь коалесцирует шторм WMI-событий без создания CTS и Task на каждое событие.</summary>
    private readonly Channel<string> _rescanRequests = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest
    });

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

    /// <summary>Мониторит WMI и гарантированно завершает debounce и подписки даже при отмене первоначального сканирования.</summary>
    /// <param name="stoppingToken">Токен остановки всей службы.</param>
    /// <returns>Задача жизненного цикла мониторинга, включая освобождение ресурсов.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WmiSubscriptionWatcher started");
        using var lifecycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var debounceTask = ProcessRescanRequestsAsync(lifecycle.Token);
        ManagementEventWatcher? createWatcher = null;
        ManagementEventWatcher? modWatcher = null;
        ManagementEventWatcher? delWatcher = null;

        // Полный жизненный цикл начинается до первой отменяемой операции, а не после начального сканирования.
        try
        {
            await RescanSubscriptionsAsync("InitialScan", isInitialScan: true, stoppingToken);
            stoppingToken.ThrowIfCancellationRequested();

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

            // Резервный поллинг дополняет callbacks при пропущенных WMI-событиях.
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_coordinator.IsPaused) continue;
                await RescanSubscriptionsAsync("PeriodicPolling", isInitialScan: false, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("WmiSubscriptionWatcher: мониторинг завершён штатной отменой службы");
        }
        finally
        {
            await lifecycle.CancelAsync();
            ReleaseWatcher(createWatcher, "Creation");
            ReleaseWatcher(modWatcher, "Modification");
            ReleaseWatcher(delWatcher, "Deletion");
            _rescanRequests.Writer.TryComplete();
            await debounceTask;
            _logger.LogInformation("WmiSubscriptionWatcher штатно остановлен");
        }
    }

    /// <summary>Останавливает и освобождает подписку независимо: отказ Stop не препятствует попытке Dispose.</summary>
    /// <param name="watcher">Созданная подписка либо null при неполной инициализации.</param>
    /// <param name="kind">Тип событий для диагностики отказа очистки.</param>
    private void ReleaseWatcher(ManagementEventWatcher? watcher, string kind)
    {
        if (watcher is null) return;
        try { watcher.Stop(); }
        catch (Exception ex) { _logger.LogWarning(ex, "WmiSubscriptionWatcher: не удалось остановить подписку {Kind}", kind); }
        finally
        {
            try { watcher.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "WmiSubscriptionWatcher: не удалось освободить подписку {Kind}", kind); }
        }
    }

    /// <summary>
    /// Планирует повторное сканирование с дебаунсом в 1 секунду
    /// (позволяет скриптам завершить последовательное создание Filter -> Consumer -> Binding).
    /// </summary>
    private void ScheduleRescan(string reason)
    {
        if (_coordinator.IsPaused) return;
        _rescanRequests.Writer.TryWrite(reason);
    }

    /// <summary>Последовательно обрабатывает объединённые запросы; остановка ожидает завершения этого единственного worker.</summary>
    /// <param name="stoppingToken">Токен завершения службы.</param>
    /// <returns>Задача жизненного цикла debounce-обработчика.</returns>
    private async Task ProcessRescanRequestsAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _rescanRequests.Reader.WaitToReadAsync(stoppingToken))
            {
                if (!_rescanRequests.Reader.TryRead(out var reason)) continue;
                while (true)
                {
                    await Task.Delay(1000, stoppingToken);
                    var changed = false;
                    while (_rescanRequests.Reader.TryRead(out var next)) { reason = next; changed = true; }
                    if (!changed) break;
                }
                if (!_coordinator.IsPaused)
                    await RescanSubscriptionsAsync($"WmiEvent:{reason}", false, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("WmiSubscriptionWatcher: debounce завершён отменой службы");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WmiSubscriptionWatcher: ошибка debounce-обработчика");
        }
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
