using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Service.Services.Remote;
using StartupMonitor.Service.Services.VirusScan;
using StartupMonitor.Service.Services.ProcessInspector;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Service.Services.Firewall;
using Microsoft.Extensions.DependencyInjection;

namespace StartupMonitor.Service.Pipe;

/// <summary>
/// Главный сервер именованных каналов (named pipe) для обработки команд от UI.
/// Реализован как BackgroundService: слушает входящие подключения, читает команды
/// в формате JSON и возвращает результаты. Также запускает фоновые циклы очистки
/// устаревших записей и проверки хешей.
/// </summary>
public class PipeServer : BackgroundService
{
    // Хранилище конфигурации (секционные json-файлы)
    private readonly JsonDataStore _store;
    // Трекер завершения начального сканирования
    private readonly InitialScanTracker _scanTracker;
    // Логгер
    private readonly ILogger<PipeServer> _logger;
    // Сервис AI-рейтинга (Groq API)
    private readonly AiRatingService _aiRating;
    // Кеш AI-рейтингов (ConcurrentDictionary)
    private readonly RatingsCache _ratingsCache;
    // Сервис проверки хешей через CIRCL hashlookup
    private readonly HashLookupService _hashLookup;
    // Сервис проверки репутации хешей через Kaspersky OpenTIP API
    private readonly StartupMonitor.Service.Services.VirusScan.KasperskyScanService _kaspersky;
    // Системный сканер автозапуска (реестр, планировщик, папки, службы)
    private readonly SystemScannerService _scanner;
    // Менеджер чёрного списка (блокировка записей)
    private readonly BlacklistManager _blacklist;
    // Сервис уведомлений (toast-уведомления UI)
    private readonly NotificationService _notifications;
    // Сервис удалённых уведомлений (Telegram / Discord)
    private readonly RemoteNotificationService _remoteNotifications;
    // Менеджер отложенного запуска программ
    private readonly StartupMonitor.Service.Services.Delayed.DelayedStartupManager _delayedStartup;
    // Менеджер контрольных снимков автозапуска
    private readonly SnapshotManager _snapshotManager;
    // Анализатор времени загрузки Windows
    private readonly StartupMonitor.Service.Services.BootTime.BootTimeService _bootTimeService;
    // Трекер метрик процессов для Диспетчера задач
    private readonly ProcessMetricsTracker _metricsTracker;
    // Сопоставление процессов с базой автозапуска
    private readonly ProcessStartupLinker _linker;
    // Координатор глобальной паузы мониторинга
    private readonly MonitoringStateCoordinator _coordinator;
    // Сервис инспекции загруженных модулей и дескрипторов процессов
    private readonly ProcessInspectorService _processInspector = new();
    // Сервис самообновления приложения
    private readonly StartupMonitor.Service.Services.Update.GitHubUpdateService _updateService;
    // Служба Windows Firewall
    private readonly WindowsFirewallService _firewallService;
    // Служба Port Defender
    private readonly ProcessPortDefenderService? _portDefenderService;
    // Управление жизненным циклом хоста (для штатной остановки по запросу UI)
    private readonly IHostApplicationLifetime _lifetime;
    // Токен отмены для фоновых задач
    private readonly CancellationTokenSource _cts = new();
    // Счётчик записей при последнем GetAllEntries (для логирования изменений)
    private int _lastGetAllCount;
    // Счётчик AI-рейтингов при последней загрузке (для логирования)
    private int _lastRatingCount;
    // Ревизия записей автозапуска для дельта-протокола GetEntriesDelta
    private static long _entriesRevision = 1;

    /// <summary>
    /// Увеличивает ревизию записей при внешних мутациях (вотчеры, BlacklistManager).
    /// </summary>
    public static void NotifyExternalEntriesChanged() => Interlocked.Increment(ref _entriesRevision);
    // Настройки JSON-сериализации
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Имя именованного канала
    private const string PipeName = "StartupMonitor";

    /// <summary>
    /// Конструктор PipeServer. Инициализирует все сервисы, загружает API-ключ из конфига.
    /// </summary>
    public PipeServer(
        JsonDataStore store,
        InitialScanTracker scanTracker,
        ILogger<PipeServer> logger,
        ILogger<AiRatingService> aiLogger,
        HashLookupService hashLookup,
        StartupMonitor.Service.Services.VirusScan.KasperskyScanService kaspersky,
        SystemScannerService scanner,
        BlacklistManager blacklist,
        NotificationService notifications,
        RemoteNotificationService remoteNotifications,
        ProcessMetricsTracker metricsTracker,
        ProcessStartupLinker linker,
        StartupMonitor.Service.Services.Delayed.DelayedStartupManager delayedStartup,
        SnapshotManager snapshotManager,
        StartupMonitor.Service.Services.BootTime.BootTimeService bootTimeService,
        MonitoringStateCoordinator coordinator,
        StartupMonitor.Service.Services.Update.GitHubUpdateService updateService,
        WindowsFirewallService firewallService,
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime)
    {
        _store = store;
        _scanTracker = scanTracker;
        _logger = logger;
        _ratingsCache = new RatingsCache(_store);
        var cfg = _store.Load();
        var apiKey = cfg?.Settings?.AiApiKey ?? string.Empty;
        var apiUrl = cfg?.Settings?.AiApiUrl;
        var model = cfg?.Settings?.AiModel;
        _aiRating = new AiRatingService(_ratingsCache, aiLogger, apiKey, cfg?.Language ?? "ru", apiUrl, model);
        _hashLookup = hashLookup;
        _kaspersky = kaspersky;
        _scanner = scanner;
        _blacklist = blacklist;
        _notifications = notifications;
        _remoteNotifications = remoteNotifications;
        _metricsTracker = metricsTracker;
        _linker = linker;
        _delayedStartup = delayedStartup;
        _snapshotManager = snapshotManager;
        _bootTimeService = bootTimeService;
        _coordinator = coordinator;
        _updateService = updateService;
        _firewallService = firewallService ?? throw new ArgumentNullException(nameof(firewallService));
        _portDefenderService = serviceProvider.GetService<ProcessPortDefenderService>();
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

        if (cfg?.Entries != null)
        {
            _linker.UpdateIndex(cfg.Entries);
        }

        // Подписываем отправку срезов метрик в событийный pipe
        _metricsTracker.SnapshotReady += (_, snapshot) =>
        {
            try
            {
                _notifications.Enqueue(new PipeMessage
                {
                    Type = PipeMessageTypes.ProcessesSnapshot,
                    Payload = JsonSerializer.Serialize(snapshot, JsonOptions)
                });
            }
            catch { }
        };
    }

    /// <summary>
    /// Основной цикл службы: ожидает подключения UI-клиентов по named pipe.
    /// Для каждого подключения запускает асинхронную обработку HandleClientAsync.
    /// </summary>
    /// <param name="stoppingToken">Токен отмены остановки службы</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PipeServer запущен");

        // Запускаем фоновые циклы: очистка устаревших записей, проверка хешей и AI-оценка
        _ = BackgroundCleanupLoop(stoppingToken);
        _ = BackgroundHashScanLoop(stoppingToken);
        _ = BackgroundRatingLoop(stoppingToken);
        _updateService.StartBackgroundCheck();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Создаём экземпляр pipe-сервера с ACL-безопасностью
                var pipe = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 10,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    PipeSecurityFactory.CreateDefault());

                // Блокируемся до подключения клиента
                await pipe.WaitForConnectionAsync(stoppingToken);
                _logger.LogInformation("UI-клиент подключился");

                // Обработка клиента в фоновой задаче — освобождаем поток для новых подключений
                _ = HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка PipeServer при подключении клиента");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Обрабатывает одно подключение клиента: читает JSON-строки, десериализует,
    /// выполняет команду через ProcessMessage, сериализует ответ и отправляет обратно.
    /// </summary>
    /// <param name="pipe">Экземпляр подключённого pipe</param>
    /// <param name="ct">Токен отмены</param>
    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            var reader = new StreamReader(pipe);
            var writer = new StreamWriter(pipe) { AutoFlush = true };

            try
            {
                // Цикл чтения команд: пока клиент подключён и не запрошена отмена
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;

                    var message = JsonSerializer.Deserialize<PipeMessage>(line, JsonOptions);
                    if (message == null) continue;

                    var response = await ProcessMessage(message);
                    var json = JsonSerializer.Serialize(response, JsonOptions);
                    await writer.WriteLineAsync(json);
                }
            }
            finally
            {
                // Гарантированное освобождение ресурсов при отключении клиента
                try { writer.Dispose(); } catch { }
                try { reader.Dispose(); } catch { }
                try { pipe.Dispose(); } catch { }
            }
        }
        catch (IOException)
        {
            _logger.LogInformation("UI-клиент отключился");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Сервер остановлен");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки клиента PipeServer");
        }
    }

    /// <summary>
    /// Диспетчер команд. По полю Type сообщения выбирает ветку обработки:
    /// CRUD записей, управление чёрным списком, AI-рейтинги, очистка устаревших и т.д.
    /// </summary>
    /// <param name="message">Входящее сообщение от UI</param>
    /// <returns>Ответное сообщение с результатом выполнения</returns>
    private async Task<PipeMessage> ProcessMessage(PipeMessage message)
    {
        using var _profPipe = ExecutionProfiler.Profile("Service.PipeProcessMessage");
        // По умолчанию — успешный ответ
        var response = new PipeMessage { Type = PipeMessageTypes.Success };

        try
        {
            switch (message.Type)
            {
                case PipeMessageTypes.GetAllEntries:
                    // Ожидаем завершения начального сканирования (макс 60с)
                    try
                    {
                        await _scanTracker.WaitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("Таймаут ожидания завершения сканирования (60с)");
                    }
                    // Дедуплицируем записи перед отправкой
                    _store.DeduplicateEntries();
                    var config = _store.Load();
                    // Снимок: watcher'ы могут добавлять записи параллельно.
                    // Хеши уже посчитаны при добавлении записи — пересчитывать их
                    // при каждом 5-сек опросе UI не нужно (дисковый жор).
                    foreach (var e in config.Entries.ToList())
                        EntryOperations.ResolveAndEnrichEntry(e, computeHash: false);
                    _store.Save(config);
                    _linker.UpdateIndex(config.Entries);
                    var entriesCount = config.Entries.Count;
                    if (entriesCount != _lastGetAllCount)
                        _logger.LogInformation("GetAllEntries: отправлено {Count} записей", entriesCount);
                    _lastGetAllCount = entriesCount;
                    response.Type = PipeMessageTypes.GetAllEntriesResponse;
                    response.Payload = JsonSerializer.Serialize(config.Entries, JsonOptions);
                    break;

                case PipeMessageTypes.GetEntriesDelta:
                    // Дельта-опрос: клиент передаёт последнюю известную ревизию.
                    // Если ничего не изменилось — отвечаем лёгким NotModified без сериализации списка.
                    EntriesRevisionRequest? deltaReq = null;
                    if (!string.IsNullOrWhiteSpace(message.Payload))
                    {
                        try
                        {
                            deltaReq = JsonSerializer.Deserialize<EntriesRevisionRequest>(message.Payload, JsonOptions);
                        }
                        catch { }
                    }
                    var clientRev = deltaReq?.ClientRevision ?? 0;
                    var serverRev = Volatile.Read(ref _entriesRevision);
                    if (clientRev == serverRev && clientRev > 0)
                    {
                        response = new PipeMessage { Type = PipeMessageTypes.NotModified };
                        break;
                    }
                    try
                    {
                        await _scanTracker.WaitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("Таймаут ожидания завершения сканирования (60с)");
                    }
                    _store.DeduplicateEntries();
                    var deltaConfig = _store.Load();
                    foreach (var e in deltaConfig.Entries.ToList())
                        EntryOperations.ResolveAndEnrichEntry(e, computeHash: false);
                    _store.Save(deltaConfig);
                    _linker.UpdateIndex(deltaConfig.Entries);
                    var curRev = Volatile.Read(ref _entriesRevision);
                    response = new PipeMessage
                    {
                        Type = PipeMessageTypes.GetEntriesDeltaResponse,
                        Payload = JsonSerializer.Serialize(new EntriesDeltaResponse(curRev, true, deltaConfig.Entries), JsonOptions)
                    };
                    break;

                case PipeMessageTypes.DisableEntry:
                    // Отключение записи: KillProcess (по настройке) + DisableFromSystem + пометка Disabled
                    var disableId = JsonSerializer.Deserialize<string>(message.Payload, JsonOptions);
                    if (disableId != null)
                    {
                        var cfg = _store.Load();
                        var entry = cfg.Entries.Find(e => e.Id == disableId);
                        if (entry != null)
                        {
                            // Завершаем процесс (службу останавливаем), если запущен и включена настройка
                            var killEnabled = cfg.Settings.KillOnDisable;
                            if (killEnabled)
                                EntryOperations.KillProcess(entry);
                            var (sysOk, sysErr) = EntryOperations.DisableFromSystem(entry);
                            entry.Status = EntryStatus.Disabled;
                            if (!sysOk && sysErr != null)
                                _logger.LogWarning("DisableFromSystem для {Type} ({Name}): {Error}", entry.Location, entry.Name, sysErr);
                            _store.UpdateEntry(entry);
                            NotifyExternalEntriesChanged();
                            // Логируем в историю
                            _store.AddHistory(new HistoryEntry
                            {
                                ChangeType = "Disabled",
                                EntryName = entry.Name,
                                EntryPath = entry.FilePath,
                            });
                            // Уведомляем пользователя о выполненных действиях
                            var action = killEnabled ? "disabledKilled" : "disabledKillSkipped";
                            _notifications.Enqueue(new PipeMessage
                            {
                                Type = PipeMessageTypes.ActionNotice,
                                Payload = JsonSerializer.Serialize(new ActionNoticePayload(
                                    entry.Name ?? "", action),
                                    JsonOptions)
                            });
                        }
                    }
                    break;

                case PipeMessageTypes.EnableEntry:
                    // Включение записи: EnableFromSystem (если Disabled) или RestoreToSystem (если Blocked)
                    var enableId = JsonSerializer.Deserialize<string>(message.Payload, JsonOptions);
                    if (enableId != null)
                    {
                        var cfg = _store.Load();
                        var entry = cfg.Entries.Find(e => e.Id == enableId);
                        if (entry != null)
                        {
                            // Системный вызов нужен только для физически отключённых записей
                            if (entry.Status == EntryStatus.Disabled)
                            {
                                var (sysOk, sysErr) = EntryOperations.EnableFromSystem(entry);
                                if (!sysOk && sysErr != null)
                                    _logger.LogWarning("EnableFromSystem для {Type} ({Name}): {Error}", entry.Location, entry.Name, sysErr);
                            }
                            else if (entry.Status == EntryStatus.Blocked)
                            {
                                // Запись была удалена из системы блокировкой — восстанавливаем обратно
                                var removed = cfg.RemovedEntries.FirstOrDefault(r => r.OriginalEntryId == entry.Id);
                                if (removed != null)
                                {
                                    var (rok, rerr) = EntryOperations.RestoreToSystem(removed);
                                    if (!rok)
                                    {
                                        response.Type = PipeMessageTypes.Error;
                                        response.Payload = rerr ?? "Failed to restore blocked entry";
                                        break;
                                    }
                                    cfg.RemovedEntries.Remove(removed);
                                }
                            }
                            entry.Status = EntryStatus.Enabled;
                            entry.FromBlacklist = false;
                            _store.UpdateEntry(entry);
                            NotifyExternalEntriesChanged();
                            _store.AddHistory(new HistoryEntry
                            {
                                ChangeType = "Enabled",
                                EntryName = entry.Name,
                                EntryPath = entry.FilePath,
                            });
                            // Уведомляем пользователя о восстановлении
                            _notifications.Enqueue(new PipeMessage
                            {
                                Type = PipeMessageTypes.ActionNotice,
                                Payload = JsonSerializer.Serialize(new ActionNoticePayload(
                                    entry.Name ?? "", "restored"), JsonOptions)
                            });
                        }
                    }
                    break;

                case PipeMessageTypes.AddToBlacklist:
                    // Добавление правила в чёрный список и немедленное применение
                    var rule = JsonSerializer.Deserialize<BlacklistRule>(message.Payload, JsonOptions);
                    if (rule != null)
                    {
                        if (rule.MatchType is BlacklistMatchType.Path or BlacklistMatchType.PathWildcard &&
                            SystemProtectionHelper.IsProtectedProcessName(rule.Value))
                        {
                            response.Type = PipeMessageTypes.Error;
                            response.Payload = $"Запрещено добавлять системный процесс Windows ({rule.Value}) в Чёрный список";
                            break;
                        }

                        var cfg = _store.Load();
                        cfg.Blacklist.Add(rule);
                        _store.Save(cfg);
                        NotifyExternalEntriesChanged();
                        _store.AddHistory(new HistoryEntry
                        {
                            ChangeType = "BlacklistAdded",
                            EntryName = rule.Value ?? "",
                            EntryPath = "",
                            Action = "RuleAdded"
                        });
                        // Применяем правило к существующим записям
                        ReapplyBlacklist(rule);
                    }
                    break;

                case PipeMessageTypes.RemoveFromBlacklist:
                    // Удаление правила из чёрного списка
                    var removeId = JsonSerializer.Deserialize<string>(message.Payload, JsonOptions);
                    if (removeId != null)
                    {
                        var cfg = _store.Load();
                        var removedRule = cfg.Blacklist.FirstOrDefault(r => r.Id == removeId);
                        cfg.Blacklist.RemoveAll(r => r.Id == removeId);
                        _store.Save(cfg);
                        NotifyExternalEntriesChanged();
                        if (removedRule != null)
                        {
                            _store.AddHistory(new HistoryEntry
                            {
                                ChangeType = "BlacklistRemoved",
                                EntryName = removedRule.Value ?? "",
                                EntryPath = "",
                                Action = "RuleRemoved"
                            });
                        }
                    }
                    break;

                case PipeMessageTypes.RemoveFromSystem:
                    // Полное удаление записи из системы: убить процесс, удалить из реестра/папки/планировщика/службы
                    var removeSystemId = JsonSerializer.Deserialize<string>(message.Payload, JsonOptions);
                    if (removeSystemId != null)
                    {
                        var cfg = _store.Load();
                        var entry = cfg.Entries.Find(e => e.Id == removeSystemId);
                        if (entry != null)
                        {
                            // Завершаем процесс ТОЛЬКО если программа занесена в Чёрный список.
                            // Если программы нет в ЧС — процесс НЕ закрываем, только удаляем запись из автозапуска!
                            var isBlacklisted = _blacklist.MatchEntry(entry) != null;
                            if (isBlacklisted)
                            {
                                EntryOperations.KillProcess(entry);
                            }
                            var (ok, err, p) = EntryOperations.RemoveFromSystem(entry);
                            if (ok && p != null)
                            {
                                cfg.Entries.Remove(entry);
                                // Сохраняем информацию для возможного восстановления
                                var removed = new RemovedEntry
                                {
                                    OriginalEntryId = entry.Id,
                                    Name = entry.Name,
                                    FilePath = entry.FilePath,
                                    Publisher = entry.Publisher,
                                    Location = entry.Location,
                                    LocationDetail = entry.LocationDetail,
                                    DetectedAt = entry.DetectedAt,
                                    RestorePayload = p
                                };
                                cfg.RemovedEntries.Add(removed);
                                _store.Save(cfg);
                                NotifyExternalEntriesChanged();
                                _store.AddHistory(new HistoryEntry
                                {
                                    ChangeType = "Removed",
                                    EntryName = entry.Name,
                                    EntryPath = entry.FilePath,
                                    Action = "RemovedFromSystem"
                                });
                                response.Type = PipeMessageTypes.Success;
                                // Уведомляем пользователя об удалении
                                _notifications.Enqueue(new PipeMessage
                                {
                                    Type = PipeMessageTypes.ActionNotice,
                                    Payload = JsonSerializer.Serialize(new ActionNoticePayload(
                                        entry.Name ?? "", "removed"), JsonOptions)
                                });
                            }
                            else
                            {
                                response.Type = PipeMessageTypes.Error;
                                response.Payload = err ?? "Failed to remove entry from system";
                            }
                        }
                    }
                    break;

                case PipeMessageTypes.GetRemovedEntries:
                    // Возвращаем список удалённых записей (для возможности восстановления)
                    response.Type = PipeMessageTypes.GetRemovedEntriesResponse;
                    response.Payload = JsonSerializer.Serialize(_store.GetRemovedEntries(), JsonOptions);
                    break;

                case PipeMessageTypes.RestoreEntry:
                    // Восстановление ранее удалённой записи в систему
                    var restoreId = JsonSerializer.Deserialize<string>(message.Payload, JsonOptions);
                    if (restoreId != null)
                    {
                        var cfg = _store.Load();
                        var removed = cfg.RemovedEntries.Find(r => r.Id == restoreId);
                        if (removed != null)
                        {
                            var (ok, err) = EntryOperations.RestoreToSystem(removed);
                            if (ok)
                            {
                                cfg.RemovedEntries.Remove(removed);
                                _store.Save(cfg);
                                NotifyExternalEntriesChanged();
                                _store.AddHistory(new HistoryEntry
                                {
                                    ChangeType = "Restored",
                                    EntryName = removed.Name,
                                    EntryPath = removed.FilePath,
                                    Action = "RestoredToSystem"
                                });
                                response.Type = PipeMessageTypes.Success;
                            }
                            else
                            {
                                response.Type = PipeMessageTypes.Error;
                                response.Payload = err ?? "Failed to restore entry";
                            }
                        }
                    }
                    break;

                case PipeMessageTypes.GetStatus:
                    // Возвращаем текущую стадию сканирования
                    response.Type = PipeMessageTypes.StatusResponse;
                    response.Payload = _scanTracker.CurrentStage;
                    break;

                case PipeMessageTypes.GetHistory:
                    // Возвращаем всю историю изменений
                    var histCfg = _store.Load();
                    response.Type = PipeMessageTypes.GetHistoryResponse;
                    response.Payload = JsonSerializer.Serialize(histCfg.History, JsonOptions);
                    break;

                case PipeMessageTypes.GetBlacklist:
                    // Ожидаем завершения скана, затем возвращаем чёрный список
                    try
                    {
                        await _scanTracker.WaitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("Таймаут ожидания завершения сканирования (60с)");
                    }
                    var cfg2 = _store.Load();
                    response.Type = PipeMessageTypes.GetBlacklistResponse;
                    response.Payload = JsonSerializer.Serialize(cfg2.Blacklist, JsonOptions);
                    break;

                case PipeMessageTypes.GetRatings:
                    // Ожидаем завершения скана, возвращаем AI-рейтинги из кеша
                    try { await _scanTracker.WaitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
                    catch (TimeoutException) { _logger.LogWarning("Таймаут ожидания завершения сканирования (60с)"); }
                    _store.DeduplicateEntries();
                    var ratingsCfg = _store.Load();
                    // Синхронизируем язык интерфейса (settings.json) для ответов Groq
                    _aiRating.UpdateUiLang(ratingsCfg.Language);
                    var ratings = new Dictionary<string, StartupRating?>();
                    foreach (var e in ratingsCfg.Entries)
                    {
                        var r = _aiRating.GetCached(e);
                        if (r != null)
                            ratings[e.Id] = r;
                    }
                    response.Type = PipeMessageTypes.GetRatingsResponse;
                    response.Payload = JsonSerializer.Serialize(ratings, JsonOptions);

                    // Фоновая загрузка для ещё не кешированных записей — запускается
                    // по таймеру (BackgroundRatingLoop), а не при каждом 5-сек опросе UI
                    break;

                case PipeMessageTypes.CleanStale:
                    // Очистка устаревших записей (физически отсутствуют в системе)
                    var cleanCfg = _store.Load();
                    var cleanScanTime = DateTime.UtcNow;
                    var cleanSkipReg = cleanCfg.Settings.CpuThrottleEnabled
                        && CpuUsageHelper.GetCpuUsage(excludeSelf: true) >= cleanCfg.Settings.CpuThrottleThreshold;
                    var cleanScan = _scanner.ScanAll(_logger, cleanSkipReg);
                    EntryOperations.CleanupStaleEntries(cleanCfg.Entries, _store, cleanScan, _logger, cleanScanTime, _scanner.FailedLocations);
                    _store.Save(cleanCfg);
                    NotifyExternalEntriesChanged();
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.SetAiConfig:
                    // Обновление настроек для AI-рейтинга
                    var aiPayload = JsonSerializer.Deserialize<AiConfigPayload>(message.Payload, JsonOptions);
                    if (aiPayload != null)
                    {
                        var cfg = _store.Load();
                        cfg.Settings.AiApiKey = aiPayload.ApiKey;
                        if (!string.IsNullOrWhiteSpace(aiPayload.ApiUrl))
                            cfg.Settings.AiApiUrl = aiPayload.ApiUrl;
                        if (!string.IsNullOrWhiteSpace(aiPayload.Model))
                            cfg.Settings.AiModel = aiPayload.Model;
                        _store.Save(cfg);
                        _aiRating.UpdateConfig(aiPayload.ApiKey, aiPayload.ApiUrl, aiPayload.Model);
                        _aiRating.UpdateUiLang(cfg.Language);
                        _logger.LogInformation("AI конфигурация обновлена: Model={Model}, Url={Url}, Lang={Lang}",
                            cfg.Settings.AiModel, cfg.Settings.AiApiUrl, cfg.Language);
                        // Немедленно запускаем оценку: не ждём следующий 10-мин цикл
                        if (!string.IsNullOrWhiteSpace(aiPayload.ApiKey) && cfg.Entries.Count > 0)
                            _ = PrefetchAndLogAsync(cfg.Entries);
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.TestAiConnection:
                    // Тестовый запрос к AI эндпоинту
                    var testPayload = JsonSerializer.Deserialize<AiConfigPayload>(message.Payload, JsonOptions);
                    var testKey = testPayload?.ApiKey;
                    var testUrl = testPayload?.ApiUrl;
                    var testModel = testPayload?.Model;
                    var (testSuccess, testMsg) = await _aiRating.TestConnectionAsync(testKey, testUrl, testModel);
                    response.Type = PipeMessageTypes.TestAiConnectionResponse;
                    response.Payload = JsonSerializer.Serialize(new { success = testSuccess, message = testMsg }, JsonOptions);
                    break;

                case PipeMessageTypes.SetKillOnDisable:
                    // Переключение kill_on_disable
                    var kodValue = JsonSerializer.Deserialize<bool>(message.Payload, JsonOptions);
                    var cfgKod = _store.Load();
                    cfgKod.Settings.KillOnDisable = kodValue;
                    _store.Save(cfgKod);
                    _logger.LogInformation("KillOnDisable изменён на: {Value}", kodValue);
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.SetCpuThrottle:
                    // Настройки троттлинга сканирования реестра
                    var throttlePayload = JsonSerializer.Deserialize<CpuThrottlePayload>(message.Payload, JsonOptions);
                    if (throttlePayload != null)
                    {
                        var cfgTh = _store.Load();
                        cfgTh.Settings.CpuThrottleEnabled = throttlePayload.Enabled;
                        cfgTh.Settings.CpuThrottleThreshold = Math.Clamp(throttlePayload.Threshold, 10, 95);
                        _store.Save(cfgTh);
                        _logger.LogInformation("CpuThrottle обновлён: Enabled={Enabled}, Threshold={Threshold}%",
                            cfgTh.Settings.CpuThrottleEnabled, cfgTh.Settings.CpuThrottleThreshold);
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.SetStrictMode:
                    // Настройка строгого режима (блокировка всех новых записей автозапуска)
                    var strictPayload = JsonSerializer.Deserialize<StrictModePayload>(message.Payload, JsonOptions);
                    var strictEnabled = strictPayload != null ? strictPayload.Enabled : bool.TryParse(message.Payload, out var s) && s;
                    {
                        var cfg = _store.Load();
                        cfg.Settings.StrictMode = strictEnabled;
                        _store.Save(cfg);
                        _logger.LogInformation("StrictMode = {Value}", strictEnabled);

                        // Если строгий режим выключен, очищаем из бэкапа удаленных записи, заблокированные этим режимом
                        if (!strictEnabled)
                        {
                            var cleared = _store.RemoveRemovedEntriesByReason("LockdownBlocked");
                            if (cleared > 0)
                            {
                                _logger.LogInformation("StrictMode выключен: удалено {Count} записей LockdownBlocked из списка бэкапа", cleared);
                            }
                        }
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.SetLanguage:
                    // Установка языка интерфейса приложения ("ru", "en")
                    var lang = message.Payload?.Trim().ToLowerInvariant().Trim('"') ?? "ru";
                    if (lang == "ru" || lang == "en")
                    {
                        var cfgLang = _store.Load();
                        cfgLang.Language = lang;
                        _store.Save(cfgLang);
                        _aiRating.UpdateUiLang(lang);
                        _logger.LogInformation("Язык интерфейса синхронизирован через pipe: {Lang}", lang);
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.SetRemoteNotificationConfig:
                    // Обновление настроек удалённых уведомлений (Telegram / Discord)
                    var remotePayload = JsonSerializer.Deserialize<RemoteNotificationConfigPayload>(message.Payload, JsonOptions);
                    if (remotePayload != null)
                    {
                        var cfg = _store.Load();
                        cfg.Settings.TelegramEnabled = remotePayload.TelegramEnabled;
                        cfg.Settings.TelegramBotToken = remotePayload.TelegramBotToken;
                        cfg.Settings.TelegramChatId = remotePayload.TelegramChatId;
                        cfg.Settings.DiscordWebhookEnabled = remotePayload.DiscordWebhookEnabled;
                        cfg.Settings.DiscordWebhookUrl = remotePayload.DiscordWebhookUrl;
                        cfg.Settings.RemoteNotifyOnNewEntry = remotePayload.RemoteNotifyOnNewEntry;
                        cfg.Settings.RemoteNotifyOnThreat = remotePayload.RemoteNotifyOnThreat;
                        cfg.Settings.RemoteNotifyOnBlocked = remotePayload.RemoteNotifyOnBlocked;
                        _store.Save(cfg);
                        _logger.LogInformation("Удалённые уведомления обновлены: Telegram={Tg}, Discord={Dc}",
                            cfg.Settings.TelegramEnabled, cfg.Settings.DiscordWebhookEnabled);
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.TestRemoteNotification:
                    // Проверка отправки тестового удалённого уведомления
                    var testReq = JsonSerializer.Deserialize<TestRemoteNotificationPayload>(message.Payload, JsonOptions);
                    var channel = testReq?.Channel ?? "all";
                    var (testOk, testDetail) = await _remoteNotifications.TestChannelAsync(channel);
                    response.Type = PipeMessageTypes.TestRemoteNotificationResponse;
                    response.Payload = JsonSerializer.Serialize(new TestRemoteNotificationResultPayload(testOk, testDetail), JsonOptions);
                    break;

                case PipeMessageTypes.ReRateEntry:
                    // Принудительная переоценка одной записи: очищаем кеш и запускаем prefetch
                    var reRateId = JsonSerializer.Deserialize<string>(message.Payload, JsonOptions);
                    if (reRateId != null)
                    {
                        var cfg = _store.Load();
                        _aiRating.UpdateUiLang(cfg.Language);
                        var entry = cfg.Entries.Find(e => e.Id == reRateId);
                        if (entry != null)
                        {
                            _ratingsCache.Remove(entry);
                            NotifyExternalEntriesChanged();
                            _logger.LogInformation("ReRateEntry: очищен кеш для {Name} (Lang={Lang}), запускаю переоценку",
                                entry.Name, cfg.Language);
                            _ = _aiRating.PrefetchAsync(entry);
                        }
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.SubscribeProcessMetrics:
                    // Активация периодического сбора метрик процессов для Диспетчера задач (On-Demand)
                    _metricsTracker.Subscribe();
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.UnsubscribeProcessMetrics:
                    // Деактивация сбора метрик при уходе с вкладки Диспетчера задач (0% CPU)
                    _metricsTracker.Unsubscribe();
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.GetProcesses:
                    // Разовый снимок процессов и системной нагрузки
                    var snapshot = _metricsTracker.CaptureSnapshot();
                    response.Type = PipeMessageTypes.ProcessesSnapshot;
                    response.Payload = JsonSerializer.Serialize(snapshot, JsonOptions);
                    break;

                case PipeMessageTypes.KillProcessById:
                    // Принудительное завершение процесса по PID с проверкой защиты
                    var killPayload = JsonSerializer.Deserialize<KillProcessPayload>(message.Payload, JsonOptions);
                    if (killPayload != null)
                    {
                        var procName = killPayload.ProcessName ?? "";
                        if (EntryOperations.IsProtectedProcessName(procName))
                        {
                            response.Type = PipeMessageTypes.Error;
                            response.Payload = JsonSerializer.Serialize(
                                new ProcessActionResultPayload(false, $"Процесс {procName} является системным и защищён от завершения.", killPayload.ProcessId),
                                JsonOptions);
                            break;
                        }

                        try
                        {
                            using var proc = System.Diagnostics.Process.GetProcessById(killPayload.ProcessId);
                            if (EntryOperations.IsProtectedProcessName(proc.ProcessName))
                            {
                                response.Type = PipeMessageTypes.Error;
                                response.Payload = JsonSerializer.Serialize(
                                    new ProcessActionResultPayload(false, $"Процесс {proc.ProcessName} является системным и защищён от завершения.", killPayload.ProcessId),
                                    JsonOptions);
                                break;
                            }

                            proc.Kill(entireProcessTree: true);
                            _logger.LogInformation("Процесс {Name} (PID {Pid}) успешно завершён по запросу из Диспетчера задач", proc.ProcessName, killPayload.ProcessId);
                            response.Type = PipeMessageTypes.ProcessActionResponse;
                            response.Payload = JsonSerializer.Serialize(
                                new ProcessActionResultPayload(true, $"Процесс {proc.ProcessName} (PID {killPayload.ProcessId}) успешно завершён.", killPayload.ProcessId),
                                JsonOptions);
                        }
                        catch (ArgumentException)
                        {
                            // Процесс уже завершился
                            response.Type = PipeMessageTypes.ProcessActionResponse;
                            response.Payload = JsonSerializer.Serialize(
                                new ProcessActionResultPayload(true, $"Процесс (PID {killPayload.ProcessId}) уже завершён.", killPayload.ProcessId),
                                JsonOptions);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка при завершении процесса PID {Pid}", killPayload.ProcessId);
                            response.Type = PipeMessageTypes.Error;
                            response.Payload = JsonSerializer.Serialize(
                                new ProcessActionResultPayload(false, $"Не удалось завершить процесс: {ex.Message}", killPayload.ProcessId),
                                JsonOptions);
                        }
                    }
                    break;

                case PipeMessageTypes.SuspendProcess:
                    var suspendPayload = JsonSerializer.Deserialize<SuspendProcessPayload>(message.Payload, JsonOptions);
                    if (suspendPayload != null)
                    {
                        var (success, msg) = EntryOperations.SuspendProcess(suspendPayload.ProcessId, suspendPayload.ProcessName);
                        if (success)
                        {
                            _logger.LogInformation("Процесс {Name} (PID {Pid}) приостановлен", suspendPayload.ProcessName, suspendPayload.ProcessId);
                            response.Type = PipeMessageTypes.ProcessActionResponse;
                            response.Payload = JsonSerializer.Serialize(
                                /// <summary>ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.</summary>
                                /// <param name="true">Входные данные: значение параметра true ().</param>
                                /// <param name="msg">Входные данные: значение параметра msg ().</param>
                                /// <param name="suspendPayload.ProcessId">Входные данные: значение параметра suspendPayload.ProcessId ().</param>
                                new ProcessActionResultPayload(true, msg, suspendPayload.ProcessId),
                                JsonOptions);
                        }
                        else
                        {
                            response.Type = PipeMessageTypes.Error;
                            response.Payload = JsonSerializer.Serialize(
                                /// <summary>ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.</summary>
                                /// <param name="false">Входные данные: значение параметра false ().</param>
                                /// <param name="msg">Входные данные: значение параметра msg ().</param>
                                /// <param name="suspendPayload.ProcessId">Входные данные: значение параметра suspendPayload.ProcessId ().</param>
                                new ProcessActionResultPayload(false, msg, suspendPayload.ProcessId),
                                JsonOptions);
                        }
                    }
                    break;

                case PipeMessageTypes.ResumeProcess:
                    var resumePayload = JsonSerializer.Deserialize<ResumeProcessPayload>(message.Payload, JsonOptions);
                    if (resumePayload != null)
                    {
                        var (success, msg) = EntryOperations.ResumeProcess(resumePayload.ProcessId, resumePayload.ProcessName);
                        if (success)
                        {
                            _logger.LogInformation("Процесс {Name} (PID {Pid}) возобновлен", resumePayload.ProcessName, resumePayload.ProcessId);
                            response.Type = PipeMessageTypes.ProcessActionResponse;
                            response.Payload = JsonSerializer.Serialize(
                                /// <summary>ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.</summary>
                                /// <param name="true">Входные данные: значение параметра true ().</param>
                                /// <param name="msg">Входные данные: значение параметра msg ().</param>
                                /// <param name="resumePayload.ProcessId">Входные данные: значение параметра resumePayload.ProcessId ().</param>
                                new ProcessActionResultPayload(true, msg, resumePayload.ProcessId),
                                JsonOptions);
                        }
                        else
                        {
                            response.Type = PipeMessageTypes.Error;
                            response.Payload = JsonSerializer.Serialize(
                                /// <summary>ProcessActionResultPayload: Выполняет операцию ProcessActionResultPayload.</summary>
                                /// <param name="false">Входные данные: значение параметра false ().</param>
                                /// <param name="msg">Входные данные: значение параметра msg ().</param>
                                /// <param name="resumePayload.ProcessId">Входные данные: значение параметра resumePayload.ProcessId ().</param>
                                new ProcessActionResultPayload(false, msg, resumePayload.ProcessId),
                                JsonOptions);
                        }
                    }
                    break;

                case PipeMessageTypes.BlockProcess:
                    // Блокировка процесса через Чёрный список, удаление из автозапуска и завершение
                    var blockPayload = JsonSerializer.Deserialize<BlockProcessPayload>(message.Payload, JsonOptions);
                    if (blockPayload != null)
                    {
                        var procName = blockPayload.ProcessName ?? "";
                        if (EntryOperations.IsProtectedProcessName(procName) ||
                            (!string.IsNullOrEmpty(blockPayload.FilePath) && EntryOperations.IsProtectedProcessName(blockPayload.FilePath)))
                        {
                            response.Type = PipeMessageTypes.Error;
                            response.Payload = JsonSerializer.Serialize(
                                new ProcessActionResultPayload(false, $"Процесс {procName} является системным и защищён от блокировки.", blockPayload.ProcessId),
                                JsonOptions);
                            break;
                        }

                        var pattern = !string.IsNullOrWhiteSpace(blockPayload.FilePath) ? blockPayload.FilePath : (procName + ".exe");
                        var blockRule = new BlacklistRule
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Value = pattern,
                            MatchType = BlacklistMatchType.Path,
                            Action = BlacklistAction.NotifyAndRemove,
                            Comment = $"Заблокировано из Диспетчера задач ({procName})"
                        };

                        var cfg = _store.Load();
                        cfg.Blacklist.Add(blockRule);
                        _store.Save(cfg);
                        NotifyExternalEntriesChanged();
                        _store.AddHistory(new HistoryEntry
                        {
                            ChangeType = "BlacklistAdded",
                            EntryName = blockRule.Value,
                            EntryPath = blockPayload.FilePath ?? "",
                            Action = "BlockedFromTaskManager"
                        });

                        // Применяем правило ЧС: удаляет из автозапуска и завершает процесс
                        ReapplyBlacklist(blockRule);

                        // Дополнительно завершаем процесс по PID, если он ещё жив
                        try
                        {
                            if (blockPayload.ProcessId > 0)
                            {
                                using var proc = System.Diagnostics.Process.GetProcessById(blockPayload.ProcessId);
                                proc.Kill(entireProcessTree: true);
                            }
                        }
                        catch { }

                        // Обновляем сопоставление процессов со списком автозапуска
                        var updatedCfg = _store.Load();
                        _linker.UpdateIndex(updatedCfg.Entries);

                        response.Type = PipeMessageTypes.ProcessActionResponse;
                        response.Payload = JsonSerializer.Serialize(
                            new ProcessActionResultPayload(true, $"Процесс {procName} заблокирован в Чёрном списке и завершён.", blockPayload.ProcessId),
                            JsonOptions);
                    }
                    break;

                case PipeMessageTypes.SetDelayedEntry:
                    var setDelayedPayload = JsonSerializer.Deserialize<SetDelayedEntryPayload>(message.Payload, JsonOptions);
                    if (setDelayedPayload != null)
                    {
                        var (ok, err) = _delayedStartup.SetDelayed(setDelayedPayload.EntryId, setDelayedPayload.DelaySeconds);
                        if (ok) NotifyExternalEntriesChanged();
                        response.Type = ok ? PipeMessageTypes.Success : PipeMessageTypes.Error;
                        response.Payload = err ?? "";
                    }
                    break;

                case PipeMessageTypes.CancelDelayedEntry:
                    var cancelDelayedPayload = JsonSerializer.Deserialize<CancelDelayedEntryPayload>(message.Payload, JsonOptions);
                    if (cancelDelayedPayload != null)
                    {
                        var (ok, err) = _delayedStartup.CancelDelayed(cancelDelayedPayload.EntryId);
                        if (ok) NotifyExternalEntriesChanged();
                        response.Type = ok ? PipeMessageTypes.Success : PipeMessageTypes.Error;
                        response.Payload = err ?? "";
                    }
                    break;

                case PipeMessageTypes.LaunchDelayedEntry:
                    var launchDelayedPayload = JsonSerializer.Deserialize<LaunchDelayedEntryPayload>(message.Payload, JsonOptions);
                    if (launchDelayedPayload != null)
                    {
                        var (ok, msgText) = _delayedStartup.LaunchNow(launchDelayedPayload.EntryId);
                        if (ok) NotifyExternalEntriesChanged();
                        response.Type = ok ? PipeMessageTypes.Success : PipeMessageTypes.Error;
                        response.Payload = msgText;
                    }
                    break;

                case PipeMessageTypes.CreateSnapshot:
                    var snapPayload = JsonSerializer.Deserialize<CreateSnapshotPayload>(message.Payload, JsonOptions);
                    var created = _snapshotManager.CreateSnapshot(snapPayload?.Name, snapPayload?.Description);
                    response.Type = PipeMessageTypes.Success;
                    response.Payload = JsonSerializer.Serialize(created, JsonOptions);
                    break;

                case PipeMessageTypes.GetSnapshots:
                    var snaps = _snapshotManager.GetSnapshots();
                    response.Type = PipeMessageTypes.Success;
                    response.Payload = JsonSerializer.Serialize(snaps, JsonOptions);
                    break;

                case PipeMessageTypes.DeleteSnapshot:
                    var delSnapPayload = JsonSerializer.Deserialize<DeleteSnapshotPayload>(message.Payload, JsonOptions);
                    if (delSnapPayload != null)
                    {
                        var ok = _snapshotManager.DeleteSnapshot(delSnapPayload.SnapshotId);
                        response.Type = ok ? PipeMessageTypes.Success : PipeMessageTypes.Error;
                        response.Payload = ok ? "Снимок удален" : "Не удалось удалить снимок";
                    }
                    break;

                case PipeMessageTypes.CompareSnapshot:
                    var compPayload = JsonSerializer.Deserialize<CompareSnapshotPayload>(message.Payload, JsonOptions);
                    if (compPayload != null)
                    {
                        var (compOk, compErr, compResult) = _snapshotManager.CompareWithSnapshot(compPayload.SnapshotId);
                        if (compOk && compResult != null)
                        {
                            response.Type = PipeMessageTypes.Success;
                            response.Payload = JsonSerializer.Serialize(compResult, JsonOptions);
                        }
                        else
                        {
                            response.Type = PipeMessageTypes.Error;
                            response.Payload = compErr ?? "Снимок не найден";
                        }
                    }
                    break;

                case PipeMessageTypes.GetBootTimeReport:
                    var bootReport = _bootTimeService.GetReport();
                    response.Type = PipeMessageTypes.Success;
                    response.Payload = JsonSerializer.Serialize(bootReport, JsonOptions);
                    break;

                case PipeMessageTypes.PauseMonitoring:
                    _coordinator.SetPaused(true);
                    _logger.LogInformation("Глобальный мониторинг ПРИОСТАНОВЛЕН по запросу UI");
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.ResumeMonitoring:
                    _coordinator.SetPaused(false);
                    _logger.LogInformation("Глобальный мониторинг ВОЗОБНОВЛЕН по запросу UI");
                    _ = Task.Run(() => TriggerCatchUpScan());
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.GetMonitoringState:
                    response.Type = PipeMessageTypes.MonitoringStateResponse;
                    response.Payload = JsonSerializer.Serialize(new MonitoringStatePayload(_coordinator.IsPaused), JsonOptions);
                    break;

                case PipeMessageTypes.GetServiceInfo:
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    var version = typeof(PipeServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.25";
                    var uptime = DateTime.Now - currentProcess.StartTime;
                    var info = new ServiceInfoPayload(
                        Pid: Environment.ProcessId,
                        Version: version,
                        ScmStatus: "Running",
                        StartTime: currentProcess.StartTime,
                        Uptime: uptime,
                        IsPaused: _coordinator.IsPaused
                    );
                    response.Type = PipeMessageTypes.ServiceInfoResponse;
                    response.Payload = JsonSerializer.Serialize(info, JsonOptions);
                    break;

                case PipeMessageTypes.GetProcessNetworkConnections:
                    int targetPid = -1;
                    if (!string.IsNullOrWhiteSpace(message.Payload))
                    {
                        if (int.TryParse(message.Payload, out var parsedPid))
                        {
                            targetPid = parsedPid;
                        }
                        else
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(message.Payload);
                                if (doc.RootElement.TryGetProperty("processId", out var pidElem) ||
                                    doc.RootElement.TryGetProperty("ProcessId", out pidElem))
                                {
                                    targetPid = pidElem.GetInt32();
                                }
                            }
                            catch { }
                        }
                    }

                    var sockets = _metricsTracker.GetProcessSockets(targetPid);
                    response.Type = PipeMessageTypes.ProcessNetworkConnectionsResponse;
                    response.Payload = JsonSerializer.Serialize(sockets, JsonOptions);
                    break;

                case PipeMessageTypes.GetProcessModules:
                    int modPid = -1;
                    bool incHandles = true;
                    if (!string.IsNullOrWhiteSpace(message.Payload))
                    {
                        if (int.TryParse(message.Payload, out var parsedPid))
                        {
                            modPid = parsedPid;
                        }
                        else
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(message.Payload);
                                if (doc.RootElement.TryGetProperty("processId", out var pidElem) ||
                                    doc.RootElement.TryGetProperty("ProcessId", out pidElem))
                                {
                                    modPid = pidElem.GetInt32();
                                }
                                if (doc.RootElement.TryGetProperty("includeHandles", out var hElem) ||
                                    doc.RootElement.TryGetProperty("IncludeHandles", out hElem))
                                {
                                    incHandles = hElem.GetBoolean();
                                }
                            }
                            catch { }
                        }
                    }

                    var modResponse = _processInspector.InspectProcess(modPid, incHandles);
                    response.Type = PipeMessageTypes.ProcessModulesResponse;
                    response.Payload = JsonSerializer.Serialize(modResponse, JsonOptions);
                    break;

                case PipeMessageTypes.SetKasperskyConfig:
                    var kConfig = JsonSerializer.Deserialize<KasperskyConfigDto>(message.Payload, JsonOptions);
                    if (kConfig != null)
                    {
                        var cfg = _store.Load();
                        cfg.Settings.KasperskyEnabled = kConfig.Enabled;
                        if (!string.IsNullOrWhiteSpace(kConfig.ApiKey))
                            cfg.Settings.KasperskyApiKey = kConfig.ApiKey.Trim();
                        if (!string.IsNullOrWhiteSpace(kConfig.ApiUrl))
                            cfg.Settings.KasperskyApiUrl = kConfig.ApiUrl.Trim();
                        _store.Save(cfg);
                        _logger.LogInformation("Kaspersky конфигурация обновлена: Enabled={Enabled}, Url={Url}",
                            cfg.Settings.KasperskyEnabled, cfg.Settings.KasperskyApiUrl);
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.SetAutoGuardConfig:
                    var guardConfig = JsonSerializer.Deserialize<AutoGuardConfigPayload>(message.Payload, JsonOptions);
                    if (guardConfig != null)
                    {
                        var cfg = _store.Load();
                        cfg.Settings.AutoGuardMode = guardConfig.Mode ?? "AuditOnly";
                        cfg.Settings.GuardBlockTempPath = guardConfig.BlockTempPath;
                        cfg.Settings.GuardBlockUnsigned = guardConfig.BlockUnsigned;
                        cfg.Settings.GuardBlockScripts = guardConfig.BlockScripts;
                        cfg.Settings.GuardBlockDangerousCli = guardConfig.BlockDangerousCli;
                        cfg.Settings.GuardBlockNewServices = guardConfig.BlockNewServices;
                        _store.Save(cfg);
                        _logger.LogInformation("Auto-Guards конфигурация обновлена: Mode={Mode}, Temp={Temp}, Unsigned={Unsigned}, Scripts={Scripts}, DangerousCli={Cli}, Services={Services}",
                            cfg.Settings.AutoGuardMode,
                            cfg.Settings.GuardBlockTempPath,
                            cfg.Settings.GuardBlockUnsigned,
                            cfg.Settings.GuardBlockScripts,
                            cfg.Settings.GuardBlockDangerousCli,
                            cfg.Settings.GuardBlockNewServices);
                    }
                    NotifyExternalEntriesChanged();
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.TestKasperskyConnection:
                    var testKasperskyPayload = JsonSerializer.Deserialize<KasperskyConfigDto>(message.Payload, JsonOptions);
                    var kKey = testKasperskyPayload?.ApiKey;
                    var kUrl = testKasperskyPayload?.ApiUrl;
                    if (string.IsNullOrWhiteSpace(kKey))
                    {
                        var currentCfg = _store.Load();
                        kKey = currentCfg.Settings.KasperskyApiKey;
                        kUrl = string.IsNullOrWhiteSpace(kUrl) ? currentCfg.Settings.KasperskyApiUrl : kUrl;
                    }
                    var testKasperskyRes = await _kaspersky.TestConnectionAsync(kKey ?? "", kUrl);
                    response.Type = PipeMessageTypes.TestKasperskyConnectionResponse;
                    response.Payload = JsonSerializer.Serialize(testKasperskyRes, JsonOptions);
                    break;

                case PipeMessageTypes.ScanKasperskyEntry:
                    var entryId = JsonSerializer.Deserialize<string>(message.Payload, JsonOptions) ?? message.Payload.Trim('"');
                    var storeCfg = _store.Load();
                    var targetEntry = storeCfg.Entries.Find(e => e.Id == entryId);
                    if (targetEntry != null)
                    {
                        await _kaspersky.ScanEntryAsync(targetEntry);
                        NotifyExternalEntriesChanged();
                        var refreshedCfg = _store.Load();
                        var refreshedEntry = refreshedCfg.Entries.Find(e => e.Id == entryId) ?? targetEntry;
                        response.Type = PipeMessageTypes.ScanKasperskyEntryResponse;
                        response.Payload = JsonSerializer.Serialize(refreshedEntry, JsonOptions);
                    }
                    else
                    {
                        response.Type = PipeMessageTypes.Error;
                        response.Payload = "Запись не найдена";
                    }
                    break;

                case PipeMessageTypes.CheckForUpdates:
                    string? clientVer = null;
                    DateTime? clientBuildDate = null;
                    if (!string.IsNullOrWhiteSpace(message.Payload))
                    {
                        try
                        {
                            var updateReq = JsonSerializer.Deserialize<CheckForUpdatesRequest>(message.Payload, JsonOptions);
                            clientVer = updateReq?.ClientVersion;
                            clientBuildDate = updateReq?.ClientBuildDateUtc;
                        }
                        catch { }
                    }
                    var updateCheckRes = await _updateService.CheckForUpdatesAsync(isManual: true, CancellationToken.None, clientVer, clientBuildDate);
                    response.Type = PipeMessageTypes.CheckForUpdatesResponse;
                    response.Payload = JsonSerializer.Serialize(updateCheckRes, JsonOptions);
                    break;

                case PipeMessageTypes.DownloadUpdate:
                    _ = Task.Run(async () =>
                    {
                        await _updateService.DownloadUpdateAsync(progress =>
                        {
                            _notifications.Enqueue(new PipeMessage
                            {
                                Type = PipeMessageTypes.DownloadUpdateProgress,
                                Payload = JsonSerializer.Serialize(progress, JsonOptions)
                            });
                        }, CancellationToken.None);
                    });
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.InstallUpdate:
                    var (instOk, instMsg) = _updateService.InstallUpdate();
                    response.Type = PipeMessageTypes.InstallUpdateResponse;
                    response.Payload = JsonSerializer.Serialize(new { success = instOk, message = instMsg }, JsonOptions);
                    break;

                case PipeMessageTypes.GetUpdateStatus:
                    var updateStatus = _updateService.GetProgress();
                    response.Type = PipeMessageTypes.UpdateStatusResponse;
                    response.Payload = JsonSerializer.Serialize(updateStatus, JsonOptions);
                    break;

                case PipeMessageTypes.SetUpdateSettings:
                    var updateSettings = JsonSerializer.Deserialize<UpdateSettingsPayload>(message.Payload, JsonOptions);
                    if (updateSettings != null)
                    {
                        var cfg = _store.Load();
                        cfg.Settings.AutoCheckUpdates = updateSettings.AutoCheckUpdates;
                        cfg.Settings.AutoDownloadUpdates = updateSettings.AutoDownloadUpdates;
                        if (updateSettings.SkippedUpdateVersion != null)
                        {
                            cfg.Settings.SkippedUpdateVersion = updateSettings.SkippedUpdateVersion;
                        }
                        _store.Save(cfg);
                        _logger.LogInformation("Update settings saved: AutoCheck={AutoCheck}, AutoDownload={AutoDownload}, Skipped={Skipped}",
                            updateSettings.AutoCheckUpdates, updateSettings.AutoDownloadUpdates, updateSettings.SkippedUpdateVersion);
                    }
                    response.Type = PipeMessageTypes.Success;
                    break;

                case PipeMessageTypes.GetFirewallStatus:
                    try
                    {
                        var status = _firewallService.GetStatus();
                        response.Type = PipeMessageTypes.GetFirewallStatusResponse;
                        response.Payload = JsonSerializer.Serialize(status, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail($"Ошибка получения статуса брандмауэра: {ex.Message}"), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.BlockIp:
                    try
                    {
                        var blockIpReq = JsonSerializer.Deserialize<BlockIpRequest>(message.Payload, JsonOptions);
                        if (blockIpReq == null)
                        {
                            var p = JsonSerializer.Deserialize<BlockIpPayload>(message.Payload, JsonOptions);
                            if (p != null)
                                blockIpReq = new BlockIpRequest { Ip = p.Ip, Direction = p.Direction, Aggregated = p.Aggregated, Reason = p.Reason, BanDurationMinutes = p.BanDurationMinutes };
                        }
                        if (blockIpReq == null || string.IsNullOrWhiteSpace(blockIpReq.Ip))
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Не указан IP-адрес для блокировки."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.BlockIp(blockIpReq);
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.UnblockIp:
                    try
                    {
                        var unblockIpReq = JsonSerializer.Deserialize<UnblockIpRequest>(message.Payload, JsonOptions);
                        if (unblockIpReq == null)
                        {
                            var p = JsonSerializer.Deserialize<UnblockIpPayload>(message.Payload, JsonOptions);
                            if (p != null)
                                unblockIpReq = new UnblockIpRequest { Ip = p.Ip, Direction = p.Direction };
                        }
                        if (unblockIpReq == null || string.IsNullOrWhiteSpace(unblockIpReq.Ip))
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Не указан IP-адрес для разблокировки."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.UnblockIp(unblockIpReq);
                        _portDefenderService?.UnbanIp(unblockIpReq.Ip);

                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.BlockPort:
                    try
                    {
                        var blockPortReq = JsonSerializer.Deserialize<BlockPortRequest>(message.Payload, JsonOptions);
                        if (blockPortReq == null)
                        {
                            var p = JsonSerializer.Deserialize<BlockPortPayload>(message.Payload, JsonOptions);
                            if (p != null)
                                blockPortReq = new BlockPortRequest { Port = p.Port, Protocol = p.Protocol, Direction = p.Direction, ApplicationPath = p.ApplicationPath, Reason = p.Reason };
                        }
                        if (blockPortReq == null || blockPortReq.Port <= 0)
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Некорректный номер порта для блокировки."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.BlockPort(blockPortReq);
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.UnblockPort:
                    try
                    {
                        var unblockPortReq = JsonSerializer.Deserialize<UnblockPortRequest>(message.Payload, JsonOptions);
                        if (unblockPortReq == null)
                        {
                            var p = JsonSerializer.Deserialize<UnblockPortPayload>(message.Payload, JsonOptions);
                            if (p != null)
                                unblockPortReq = new UnblockPortRequest { Port = p.Port, Protocol = p.Protocol, Direction = p.Direction, ApplicationPath = p.ApplicationPath };
                        }
                        if (unblockPortReq == null || unblockPortReq.Port <= 0)
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Некорректный номер порта для разблокировки."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.UnblockPort(unblockPortReq);
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.GetPortDefenderConfig:
                    try
                    {
                        var defenderCurrentConfig = _portDefenderService?.GetConfig() ?? _store.Load().Settings.PortDefender ?? new PortDefenderConfigDto();
                        response.Type = PipeMessageTypes.GetPortDefenderConfig;
                        response.Payload = JsonSerializer.Serialize(defenderCurrentConfig, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.SetPortDefenderConfig:
                    try
                    {
                        var defenderConfig = JsonSerializer.Deserialize<PortDefenderConfigDto>(message.Payload, JsonOptions);
                        if (defenderConfig != null)
                        {
                            if (_portDefenderService != null)
                            {
                                _portDefenderService.SaveConfig(defenderConfig);
                            }
                            else
                            {
                                var cfg = _store.Load();
                                cfg.Settings.PortDefender = defenderConfig;
                                _store.Save(cfg);
                            }
                            response.Type = PipeMessageTypes.Success;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Ok("Конфигурация Port Defender успешно сохранена."), JsonOptions);
                        }
                        else
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Некорректная конфигурация PortDefenderConfigDto."), JsonOptions);
                        }
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.GetActiveBans:
                    try
                    {
                        var bans = _portDefenderService?.GetActiveBans() ?? new List<ActiveBanDto>();
                        response.Type = PipeMessageTypes.ActiveBansResponse;
                        response.Payload = JsonSerializer.Serialize(bans, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.ClearAllBans:
                    try
                    {
                        if (_portDefenderService != null)
                        {
                            int cleared = _portDefenderService.ClearAllBans();
                            response.Type = PipeMessageTypes.Success;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Ok($"Снято банов: {cleared}"), JsonOptions);
                        }
                        else
                        {
                            response.Type = PipeMessageTypes.Success;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Ok("Служба Port Defender не активна."), JsonOptions);
                        }
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.GetAllFirewallRules:
                    try
                    {
                        var rules = _firewallService.GetAllRules();
                        response.Type = PipeMessageTypes.GetAllFirewallRulesResponse;
                        response.Payload = JsonSerializer.Serialize(new FirewallRulesPayload(rules.ToList()), JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.CreateFirewallRule:
                    try
                    {
                        FirewallRuleDto? ruleDto = null;
                        try { ruleDto = JsonSerializer.Deserialize<CreateRulePayload>(message.Payload, JsonOptions)?.Rule; } catch { }
                        if (ruleDto == null)
                        {
                            try { ruleDto = JsonSerializer.Deserialize<CreateRuleRequest>(message.Payload, JsonOptions)?.Rule; } catch { }
                        }
                        if (ruleDto == null)
                        {
                            try { ruleDto = JsonSerializer.Deserialize<FirewallRuleDto>(message.Payload, JsonOptions); } catch { }
                        }
                        if (ruleDto == null || string.IsNullOrWhiteSpace(ruleDto.Name))
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Некорректное правило брандмауэра для создания."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.CreateOrUpdateRule(ruleDto);
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.DeleteFirewallRule:
                    try
                    {
                        string? ruleName = null;
                        var direction = FirewallDirection.Both;
                        try
                        {
                            var p = JsonSerializer.Deserialize<DeleteRulePayload>(message.Payload, JsonOptions);
                            if (p != null) { ruleName = p.Name; direction = p.Direction; }
                        }
                        catch { }
                        if (string.IsNullOrWhiteSpace(ruleName))
                        {
                            try
                            {
                                var r = JsonSerializer.Deserialize<DeleteRuleRequest>(message.Payload, JsonOptions);
                                if (r != null) { ruleName = r.Name; direction = r.Direction; }
                            }
                            catch { }
                        }
                        if (string.IsNullOrWhiteSpace(ruleName))
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Не указано имя правила для удаления."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.DeleteRule(ruleName, direction);
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.ToggleFirewallRule:
                    try
                    {
                        ToggleRulePayload? toggleReq = null;
                        try { toggleReq = JsonSerializer.Deserialize<ToggleRulePayload>(message.Payload, JsonOptions); } catch { }
                        string? toggleName = toggleReq?.Name;
                        var toggleDir = toggleReq?.Direction ?? FirewallDirection.Both;
                        var toggleEnable = toggleReq?.Enable ?? false;
                        var hasToggle = toggleReq != null && !string.IsNullOrWhiteSpace(toggleName);
                        if (!hasToggle)
                        {
                            try
                            {
                                var r = JsonSerializer.Deserialize<ToggleRuleRequest>(message.Payload, JsonOptions);
                                if (r != null && !string.IsNullOrWhiteSpace(r.Name))
                                {
                                    toggleName = r.Name;
                                    toggleDir = r.Direction;
                                    toggleEnable = r.Enable;
                                    hasToggle = true;
                                }
                            }
                            catch { }
                        }
                        if (!hasToggle || string.IsNullOrWhiteSpace(toggleName))
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Некорректный запрос переключения правила."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.ToggleRule(toggleName, toggleDir, toggleEnable);
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.SetFirewallFilteringMode:
                    try
                    {
                        FirewallFilteringMode? mode = null;
                        try { mode = JsonSerializer.Deserialize<FilteringModePayload>(message.Payload, JsonOptions)?.Mode; } catch { }
                        if (mode == null)
                        {
                            try { mode = JsonSerializer.Deserialize<SetFilteringModeRequest>(message.Payload, JsonOptions)?.Mode; } catch { }
                        }
                        if (mode == null)
                        {
                            try { mode = JsonSerializer.Deserialize<FirewallFilteringMode>(message.Payload, JsonOptions); } catch { }
                        }
                        if (mode == null)
                        {
                            response.Type = PipeMessageTypes.FirewallOperationResponse;
                            response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail("Некорректный режим фильтрации."), JsonOptions);
                            break;
                        }

                        var result = _firewallService.SetFilteringMode(mode.Value);
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(result, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.GetFirewallFilteringMode:
                    try
                    {
                        var mode = _firewallService.GetFilteringMode();
                        response.Type = PipeMessageTypes.GetFirewallFilteringModeResponse;
                        response.Payload = JsonSerializer.Serialize(new FilteringModePayload(mode), JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        response.Type = PipeMessageTypes.FirewallOperationResponse;
                        response.Payload = JsonSerializer.Serialize(FirewallOperationResult.Fail(ex.Message), JsonOptions);
                    }
                    break;

                case PipeMessageTypes.ShutdownService:
                    _logger.LogInformation("Получен запрос на остановку службы от UI (ShutdownService)");
                    response.Type = PipeMessageTypes.Success;
                    // Небольшая задержка, чтобы ответ успел уйти клиенту до остановки хоста.
                    _ = Task.Run(async () =>
                    {
                        try { await Task.Delay(200); } catch { }
                        try { _lifetime.StopApplication(); } catch { }
                    });
                    break;

                default:
                    response.Type = PipeMessageTypes.Error;
                    response.Payload = $"Неизвестный тип сообщения: {message.Type}";
                    break;
            }
        }
        catch (Exception ex)
        {
            response.Type = PipeMessageTypes.Error;
            response.Payload = ex.Message;
        }

        return response;
    }

    /// <summary>
    /// Фоновая загрузка AI-рейтингов для всех записей. Логирует количество загруженных.
    /// </summary>
    /// <param name="entries">Список записей для оценки</param>
    private async Task PrefetchAndLogAsync(List<StartupEntry> entries)
    {
        try
        {
            var cfg = _store.Load();
            _aiRating.UpdateUiLang(cfg.Language);
            var before = _ratingsCache.Count;
            await _aiRating.PrefetchAllAsync(entries);
            var after = _ratingsCache.Count;
            if (after != _lastRatingCount)
                _logger.LogInformation("AI-рейтинги: загружено {Count} из {Total} (язык: {Lang})",
                    after - before, entries.Count, cfg.Language);
            _lastRatingCount = after;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка фоновой загрузки AI-рейтингов");
        }
    }

    /// <summary>
    /// Догоняющее сканирование после возобновления мониторинга.
    /// </summary>
    private void TriggerCatchUpScan()
    {
        try
        {
            _logger.LogInformation("Запуск догоняющего сканирования автозапуска после возобновления...");
            var scan = _scanner.ScanAll(_logger);
            var cfg = _store.Load();
            _linker.UpdateIndex(cfg.Entries);
            _logger.LogInformation("Догоняющее сканирование завершено. Обнаружено записей: {Count}", scan.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выполнении догоняющего сканирования");
        }
    }

    /// <summary>
    /// Фоновый цикл AI-оценки записей: работает независимо от UI, пока есть неоценённые
    /// записи и задан API-ключ. Первый запуск через 20с после старта, далее каждые 10 минут.
    /// </summary>
    /// <param name="ct">Токен отмены</param>
    private async Task BackgroundRatingLoop(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            while (!ct.IsCancellationRequested)
            {
                if (_coordinator.IsPaused)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    continue;
                }

                try
                {
                    var cfg = _store.Load();
                    if (cfg.Entries.Count > 0 && !string.IsNullOrWhiteSpace(cfg.Settings.AiApiKey))
                    {
                        _aiRating.UpdateUiLang(cfg.Language);
                        _aiRating.UpdateConfig(cfg.Settings.AiApiKey, cfg.Settings.AiApiUrl, cfg.Settings.AiModel);
                        await PrefetchAndLogAsync(cfg.Entries);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "BackgroundRatingLoop: ошибка");
                }
                await Task.Delay(TimeSpan.FromMinutes(10), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Фоновый цикл очистки устаревших записей. Запускается через 30с после старта,
    /// затем повторяется каждые 60с. Удаляет записи, чьи файлы/ключи реестра/службы
    /// больше не существуют в системе.
    /// </summary>
    /// <param name="ct">Токен отмены</param>
    private async Task BackgroundCleanupLoop(CancellationToken ct)
    {
        try
        {
            // Первый запуск проверки процессов ЧС сразу через 5с после старта службы
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var lastFullScan = DateTime.MinValue;

            while (!ct.IsCancellationRequested)
            {
                if (_coordinator.IsPaused)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                // 1. Всегда и изолированно завершаем запрещенные процессы по правилам ЧС
                try
                {
                    var cfg = _store.Load();
                    EntryOperations.KillProcessesByRules(cfg.Blacklist, _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "BackgroundCleanupLoop: ошибка при проверке процессов по ЧС");
                }

                // 2. Очистка устаревших записей (полный скан) — раз в 5 минут
                if (DateTime.UtcNow - lastFullScan >= TimeSpan.FromMinutes(5))
                {
                    lastFullScan = DateTime.UtcNow;
                    try
                    {
                        var cfg = _store.Load();
                        var scanTime = DateTime.UtcNow;
                        var skipReg = cfg.Settings.CpuThrottleEnabled
                            && CpuUsageHelper.GetCpuUsage(excludeSelf: true) >= cfg.Settings.CpuThrottleThreshold;
                        if (skipReg)
                        {
                            _logger.LogInformation("BackgroundCleanupLoop: пропуск сканирования реестра (загрузка CPU сторонними процессами >= {Threshold}%)",
                                cfg.Settings.CpuThrottleThreshold);
                        }
                        var scan = _scanner.ScanAll(_logger, skipReg);
                        EntryOperations.CleanupStaleEntries(cfg.Entries, _store, scan, _logger, scanTime, _scanner.FailedLocations);
                        _store.Save(cfg);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BackgroundCleanupLoop: ошибка при очистке устаревших записей");
                    }
                }

                // Интервал 10 секунд для мгновенного реагирования на запуск процессов из ЧС (напр. ms-teams)
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Фоновый цикл проверки хешей (CIRCL hashlookup). Запускается через 10с после старта,
    /// затем повторяется каждые 5 минут. Обновляет KasperskyDetection у записей.
    /// </summary>
    /// <param name="ct">Токен отмены</param>
    private async Task BackgroundHashScanLoop(CancellationToken ct)
    {
        try
        {
            // Первый запуск через 10с после старта
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            while (!ct.IsCancellationRequested)
            {
                if (_coordinator.IsPaused)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    continue;
                }

                try
                {
                    _logger.LogInformation("KasperskyScan: начинаю фоновую проверку хешей через Kaspersky OpenTIP");
                    var cfg = _store.Load();
                    await _kaspersky.ScanAllAsync(cfg.Entries, ct);
                    _store.Save(cfg);
                    _logger.LogInformation("KasperskyScan: фоновая проверка завершена");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HashScan: ошибка");
                }
                // Интервал 5 минут между проверками
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Освобождение ресурсов: отмена токена, освобождение AI-сервиса.
    /// </summary>
    public override void Dispose()
    {
        _cts.Cancel();
        _aiRating.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Применяет правило чёрного списка ко всем существующим записям.
    /// Записи, подходящие под правило, блокируются (ApplyBlacklistAction).
    /// </summary>
    /// <param name="rule">Правило чёрного списка</param>
    private void ReapplyBlacklist(BlacklistRule rule)
    {
        try
        {
            // ToList: ApplyBlacklistAction может удалять записи из конфига (NotifyAndRemove)
            var cfg = _store.Load();
            foreach (var entry in cfg.Entries.ToList())
            {
                if (entry.FromBlacklist) continue;
                if (SystemProtectionHelper.IsProtectedSystemEntry(entry)) continue;
                if (BlacklistManager.IsMatchStatic(rule, entry))
                    _blacklist.ApplyBlacklistAction(entry, rule, _store, _notifications);
            }
            // Завершаем и процессы, у которых записи в списке нет (ms-teams и т.п.)
            EntryOperations.KillProcessesByRules(new[] { rule }, _logger);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ReapplyBlacklist failed");
        }
    }
}
