using System.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Watcher для служб Windows: начальное сканирование через WMI
/// и отслеживание новых автоматических служб через WMI-события.</summary>
public class ServiceWatcher : BackgroundService
{
    /// <summary>Хранилище записей автозапуска.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Менеджер чёрного списка.</summary>
    private readonly BlacklistManager _blacklist;
    /// <summary>Сервис уведомлений для отправки в UI.</summary>
    private readonly NotificationService _notifications;
    /// <summary>Логгер.</summary>
    private readonly ILogger<ServiceWatcher> _logger;
    /// <summary>Трекер начального сканирования.</summary>
    private readonly InitialScanTracker _tracker;
    private readonly MonitoringStateCoordinator _coordinator;

    public ServiceWatcher(
        JsonDataStore store,
        BlacklistManager blacklist,
        NotificationService notifications,
        ILogger<ServiceWatcher> logger,
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

    /// <summary>Основной цикл: начальное сканирование через WMI → подписка на события создания служб.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ServiceWatcher started");
        _tracker.CurrentStage = "Сканирование служб...";
        await InitialScan(stoppingToken);
        _tracker.MarkCompleted();

        // WMI-запрос на создание новых экземпляров Win32_Service
        var createQuery = new WqlEventQuery(
            "__InstanceCreationEvent",
            TimeSpan.FromSeconds(5),
            "TargetInstance ISA 'Win32_Service'");

        using var createWatcher = new ManagementEventWatcher(createQuery);
        createWatcher.EventArrived += OnServiceCreated;
        createWatcher.Start();

        // WMI-запрос на изменение существующих служб (перевод в режим автозапуска)
        var modQuery = new WqlEventQuery(
            "__InstanceModificationEvent",
            TimeSpan.FromSeconds(5),
            "TargetInstance ISA 'Win32_Service' AND TargetInstance.StartMode = 'Auto' AND PreviousInstance.StartMode != 'Auto'");

        using var modWatcher = new ManagementEventWatcher(modQuery);
        modWatcher.EventArrived += OnServiceCreated;
        modWatcher.Start();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        finally
        {
            try { createWatcher.Stop(); } catch { }
            try { modWatcher.Stop(); } catch { }
        }
    }

    /// <summary>Начальное сканирование: WMI-запрос всех авто-служб.</summary>
    private Task InitialScan(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var _profServices = ExecutionProfiler.Profile("Service.ServicesScan");
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
                            ? EntryStatus.Enabled
                            : EntryStatus.Disabled
                    };
                    entry.Id = WatcherHelper.ComputeStableId(entry);
                    entry.DetectedAt = DateTime.UtcNow;
                    EntryOperations.ResolveAndEnrichEntry(entry);

                    // Проверка чёрного списка
                    var matched = _blacklist.MatchEntry(entry);
                    if (matched != null)
                    {
                        entry.Status = EntryStatus.Blocked;
                        entry.FromBlacklist = true;
                    }

                    _store.AddEntry(entry);
                }

                _logger.LogInformation("Services initial scan complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Services initial scan failed");
            }
        }, ct);
    }

    /// <summary>Обработчик WMI-события создания новой службы.</summary>
    private void OnServiceCreated(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (_coordinator.IsPaused) return;

            using var targetInstance = e.NewEvent["TargetInstance"] as ManagementBaseObject;
            if (targetInstance == null) return;

            var name = targetInstance["Name"]?.ToString() ?? string.Empty;
            var path = targetInstance["PathName"]?.ToString() ?? string.Empty;
            var displayName = targetInstance["DisplayName"]?.ToString() ?? string.Empty;
            var startMode = targetInstance["StartMode"]?.ToString() ?? string.Empty;

            // Интересуют только автоматические службы
            if (!string.Equals(startMode, "Auto", StringComparison.OrdinalIgnoreCase)) return;

            var filePath = WatcherHelper.ParseServicePath(path);

            var entry = new StartupEntry
            {
                Name = displayName,
                FilePath = filePath,
                CommandLine = path,
                Location = EntryLocation.Service,
                LocationDetail = name,
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            entry.DetectedAt = DateTime.UtcNow;
            EntryOperations.ResolveAndEnrichEntry(entry);

            var matched = _blacklist.MatchEntry(entry);
            if (matched != null)
            {
                if (matched.Action == BlacklistAction.NotifyAndRemove)
                {
                    _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                    _logger.LogInformation("Blacklist: служба {Name} заблокирована и удалена", name);
                    return;
                }
                entry.Status = EntryStatus.Blocked;
                entry.FromBlacklist = true;
            }

            // Дубликат (гонка со сканом) — не уведомляем повторно;
            // повторная регистрация заблокированной службы — правило применится в AddOrUpdate
            if (!WatcherHelper.AddOrUpdate(_store, _notifications, entry, matched, _blacklist)) return;

            _logger.LogInformation("New auto-start service detected: {Name}", name);

            // Запись из ЧС снова появилась — автоматически останавливаем процесс и удаляем (с предупреждением)
            if (matched != null)
            {
                _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                return;
            }

            _notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.EntryAdded,
                Payload = System.Text.Json.JsonSerializer.Serialize(entry)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing new service event");
        }
    }

}
