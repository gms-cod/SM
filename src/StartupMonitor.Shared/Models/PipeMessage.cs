using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Shared.Models;

/// <summary>Сообщение для обмена между службой и UI через именованный канал (Named Pipe).</summary>
public class PipeMessage
{
    /// <summary>Тип сообщения (команда/событие).</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON-строка с данными сообщения.</summary>
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    /// <summary>Время отправки сообщения (UTC).</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>Константы типов сообщений для обмена через именованный канал.</summary>
public static class PipeMessageTypes
{
    /// <summary>Запрос всех записей автозапуска.</summary>
    public const string GetAllEntries = "GetAllEntries";
    /// <summary>Ответ со списком всех записей.</summary>
    public const string GetAllEntriesResponse = "GetAllEntriesResponse";
    /// <summary>Запрос дельты записей автозапуска по ревизии клиента.</summary>
    public const string GetEntriesDelta = "GetEntriesDelta";
    /// <summary>Ответ с дельтой записей автозапуска (или признаком отсутствия изменений).</summary>
    public const string GetEntriesDeltaResponse = "GetEntriesDeltaResponse";
    /// <summary>Признак отсутствия изменений (ревизия клиента актуальна).</summary>
    public const string NotModified = "NotModified";
    /// <summary>Отключить запись (удалить из автозапуска, сохранив конфиг).</summary>
    public const string DisableEntry = "DisableEntry";
    /// <summary>Восстановить запись в автозапуске.</summary>
    public const string EnableEntry = "EnableEntry";
    /// <summary>Добавить запись в чёрный список.</summary>
    public const string AddToBlacklist = "AddToBlacklist";
    /// <summary>Удалить запись из чёрного списка.</summary>
    public const string RemoveFromBlacklist = "RemoveFromBlacklist";
    /// <summary>Запрос чёрного списка.</summary>
    public const string GetBlacklist = "GetBlacklist";
    /// <summary>Ответ с чёрным списком.</summary>
    public const string GetBlacklistResponse = "GetBlacklistResponse";
    /// <summary>Событие: добавлена новая запись автозапуска.</summary>
    public const string EntryAdded = "EntryAdded";
    /// <summary>Событие: запись изменена.</summary>
    public const string EntryModified = "EntryModified";
    /// <summary>Событие: запись удалена.</summary>
    public const string EntryDeleted = "EntryDeleted";
    /// <summary>Событие: сработало правило чёрного списка.</summary>
    public const string BlacklistMatch = "BlacklistMatch";
    /// <summary>Событие: выполнено действие над записью (отключено, процесс завершён, удалено и т.п.).</summary>
    public const string ActionNotice = "ActionNotice";
    /// <summary>Установить настройку "убивать процесс при отключении" (KillOnDisable).</summary>
    public const string SetKillOnDisable = "SetKillOnDisable";
    /// <summary>Запрос статуса службы.</summary>
    public const string GetStatus = "GetStatus";
    /// <summary>Ответ со статусом службы.</summary>
    public const string StatusResponse = "StatusResponse";
    /// <summary>Полностью удалить запись из системы (с бэкапом).</summary>
    public const string RemoveFromSystem = "RemoveFromSystem";
    /// <summary>Запрос списка удалённых записей.</summary>
    public const string GetRemovedEntries = "GetRemovedEntries";
    /// <summary>Ответ со списком удалённых записей.</summary>
    public const string GetRemovedEntriesResponse = "GetRemovedEntriesResponse";
    /// <summary>Восстановить ранее удалённую запись.</summary>
    public const string RestoreEntry = "RestoreEntry";
    /// <summary>Запрос истории изменений.</summary>
    public const string GetHistory = "GetHistory";
    /// <summary>Ответ с историей изменений.</summary>
    public const string GetHistoryResponse = "GetHistoryResponse";
    /// <summary>Запрос AI-рейтингов записей.</summary>
    public const string GetRatings = "GetRatings";
    /// <summary>Ответ с AI-рейтингами записей.</summary>
    public const string GetRatingsResponse = "GetRatingsResponse";
    /// <summary>Очистка устаревших записей (CleanStale).</summary>
    public const string CleanStale = "CleanStale";
    /// <summary>Установка AI-конфига (ключ API, URL, модель).</summary>
    public const string SetAiConfig = "SetAiConfig";
    /// <summary>Тестовый запрос к AI для проверки ключа и настроек.</summary>
    public const string TestAiConnection = "TestAiConnection";
    /// <summary>Ответ на тестовый запрос к AI.</summary>
    public const string TestAiConnectionResponse = "TestAiConnectionResponse";
    /// <summary>Переоценка одной записи AI.</summary>
    public const string ReRateEntry = "ReRateEntry";
    /// <summary>Установка настроек троттлинга процессора.</summary>
    public const string SetCpuThrottle = "SetCpuThrottle";
    /// <summary>Установка строгого режима (блокировка новых записей).</summary>
    public const string SetStrictMode = "SetStrictMode";
    /// <summary>Подписка на поток срезов метрик процессов (активируется на вкладке Диспетчера задач).</summary>
    public const string SubscribeProcessMetrics = "SubscribeProcessMetrics";
    /// <summary>Отписка от потока срезов метрик процессов (при уходе с вкладки или сворачивании в трей).</summary>
    public const string UnsubscribeProcessMetrics = "UnsubscribeProcessMetrics";
    /// <summary>Разовый запрос актуального снимка процессов.</summary>
    public const string GetProcesses = "GetProcesses";
    /// <summary>Ответ службы со снимком процессов и системной нагрузки.</summary>
    public const string ProcessesSnapshot = "ProcessesSnapshot";
    /// <summary>Завершить процесс по идентификатору (PID).</summary>
    public const string KillProcessById = "KillProcessById";
    /// <summary>Приостановить процесс по идентификатору (PID).</summary>
    public const string SuspendProcess = "SuspendProcess";
    /// <summary>Возобновить процесс по идентификатору (PID).</summary>
    public const string ResumeProcess = "ResumeProcess";
    /// <summary>Заблокировать процесс в Чёрном списке и завершить его.</summary>
    public const string BlockProcess = "BlockProcess";
    /// <summary>Ответ на действие над процессом (завершение/блокировка).</summary>
    public const string ProcessActionResponse = "ProcessActionResponse";
    /// <summary>Установка конфигурации удалённых уведомлений (Telegram / Discord).</summary>
    public const string SetRemoteNotificationConfig = "SetRemoteNotificationConfig";
    /// <summary>Тестовая отправка удалённого уведомления.</summary>
    public const string TestRemoteNotification = "TestRemoteNotification";
    /// <summary>Ответ на тестовую отправку удалённого уведомления.</summary>
    public const string TestRemoteNotificationResponse = "TestRemoteNotificationResponse";
    /// <summary>Установка языка интерфейса приложения ("ru", "en").</summary>
    public const string SetLanguage = "SetLanguage";
    /// <summary>Успешный ответ на команду.</summary>
    public const string Success = "Success";
    /// <summary>Ошибка при выполнении команды.</summary>
    public const string Error = "Error";
    /// <summary>Установить отложенный запуск записи автозапуска.</summary>
    public const string SetDelayedEntry = "SetDelayedEntry";
    /// <summary>Отменить отложенный запуск записи автозапуска.</summary>
    public const string CancelDelayedEntry = "CancelDelayedEntry";
    /// <summary>Принудительно запустить отложенную запись прямо сейчас.</summary>
    public const string LaunchDelayedEntry = "LaunchDelayedEntry";
    /// <summary>Создать контрольный снимок состояния автозапуска.</summary>
    public const string CreateSnapshot = "CreateSnapshot";
    /// <summary>Получить список сохранённых снимков.</summary>
    public const string GetSnapshots = "GetSnapshots";
    /// <summary>Удалить контрольный снимок.</summary>
    public const string DeleteSnapshot = "DeleteSnapshot";
    /// <summary>Сравнить текущее состояние со снимком.</summary>
    public const string CompareSnapshot = "CompareSnapshot";
    /// <summary>Запрос отчёта анализатора времени загрузки Windows.</summary>
    public const string GetBootTimeReport = "GetBootTimeReport";
    /// <summary>Приостановить глобальный мониторинг автозапуска.</summary>
    public const string PauseMonitoring = "PauseMonitoring";
    /// <summary>Возобновить глобальный мониторинг автозапуска.</summary>
    public const string ResumeMonitoring = "ResumeMonitoring";
    /// <summary>Запрос состояния мониторинга (активен/на паузе).</summary>
    public const string GetMonitoringState = "GetMonitoringState";
    /// <summary>Ответ службы с состоянием мониторинга.</summary>
    public const string MonitoringStateResponse = "MonitoringStateResponse";
    /// <summary>Событие изменения состояния мониторинга (рассылается всем клиентам).</summary>
    public const string MonitoringStateChanged = "MonitoringStateChanged";
    /// <summary>Запрос подробной информации о службе (PID, версия, статус, аптайм).</summary>
    public const string GetServiceInfo = "GetServiceInfo";
    /// <summary>Ответ с подробной информацией о службе.</summary>
    public const string ServiceInfoResponse = "ServiceInfoResponse";
    /// <summary>Запрос списка активных сетевых соединений (сокетов) процесса или всей системы.</summary>
    public const string GetProcessNetworkConnections = "GetProcessNetworkConnections";
    /// <summary>Ответ службы со списком активных сетевых соединений.</summary>
    public const string ProcessNetworkConnectionsResponse = "ProcessNetworkConnectionsResponse";
    /// <summary>Установка конфигурации Kaspersky OpenTIP.</summary>
    public const string SetKasperskyConfig = "SetKasperskyConfig";
    /// <summary>Тестовый запрос к API Kaspersky OpenTIP для проверки токена.</summary>
    public const string TestKasperskyConnection = "TestKasperskyConnection";
    /// <summary>Ответ на тестовый запрос к Kaspersky OpenTIP.</summary>
    public const string TestKasperskyConnectionResponse = "TestKasperskyConnectionResponse";
    /// <summary>Запрос немедленного сканирования записи через Kaspersky OpenTIP.</summary>
    public const string ScanKasperskyEntry = "ScanKasperskyEntry";
    /// <summary>Ответ со сканированной записью Kaspersky OpenTIP.</summary>
    public const string ScanKasperskyEntryResponse = "ScanKasperskyEntryResponse";
    /// <summary>Установка конфигурации автоматических политик защиты (Auto-Guards).</summary>
    public const string SetAutoGuardConfig = "SetAutoGuardConfig";
    /// <summary>Запрос списка загруженных модулей (DLL) и дескрипторов процесса.</summary>
    public const string GetProcessModules = "GetProcessModules";
    /// <summary>Ответ службы со списком загруженных модулей и дескрипторов процесса.</summary>
    public const string ProcessModulesResponse = "ProcessModulesResponse";
    /// <summary>Запрос проверки обновлений на GitHub.</summary>
    public const string CheckForUpdates = "CheckForUpdates";
    /// <summary>Ответ службы с результатом проверки обновлений.</summary>
    public const string CheckForUpdatesResponse = "CheckForUpdatesResponse";
    /// <summary>Команда скачивания пакета обновления.</summary>
    public const string DownloadUpdate = "DownloadUpdate";
    /// <summary>Событие прогресса загрузки обновления (рассылается клиентам).</summary>
    public const string DownloadUpdateProgress = "DownloadUpdateProgress";
    /// <summary>Команда тихой установки скачанного обновления.</summary>
    public const string InstallUpdate = "InstallUpdate";
    /// <summary>Ответ на команду установки обновления.</summary>
    public const string InstallUpdateResponse = "InstallUpdateResponse";
    /// <summary>Запрос текущего статуса обновления и загруженного файла.</summary>
    public const string GetUpdateStatus = "GetUpdateStatus";
    /// <summary>Ответ службы со статусом обновления.</summary>
    public const string UpdateStatusResponse = "UpdateStatusResponse";
    /// <summary>Установка настроек автообновления.</summary>
    public const string SetUpdateSettings = "SetUpdateSettings";
    /// <summary>Запрос статуса брандмауэра Windows.</summary>
    public const string GetFirewallStatus = "GetFirewallStatus";
    /// <summary>Ответ со статусом брандмауэра Windows.</summary>
    public const string GetFirewallStatusResponse = "GetFirewallStatusResponse";
    /// <summary>Запрос блокировки IP-адреса.</summary>
    public const string BlockIp = "BlockIp";
    /// <summary>Запрос разблокировки IP-адреса.</summary>
    public const string UnblockIp = "UnblockIp";
    /// <summary>Запрос блокировки порта.</summary>
    public const string BlockPort = "BlockPort";
    /// <summary>Запрос разблокировки порта.</summary>
    public const string UnblockPort = "UnblockPort";
    /// <summary>Ответ на операцию брандмауэра.</summary>
    public const string FirewallOperationResponse = "FirewallOperationResponse";
    /// <summary>Запрос конфигурации Port Defender.</summary>
    public const string GetPortDefenderConfig = "GetPortDefenderConfig";
    /// <summary>Сохранение конфигурации Port Defender.</summary>
    public const string SetPortDefenderConfig = "SetPortDefenderConfig";
    /// <summary>Запрос списка активных блокировок Port Defender.</summary>
    public const string GetActiveBans = "GetActiveBans";
    /// <summary>Ответ со списком активных блокировок Port Defender.</summary>
    public const string ActiveBansResponse = "ActiveBansResponse";
    /// <summary>Снятие всех активных блокировок Port Defender.</summary>
    public const string ClearAllBans = "ClearAllBans";
    /// <summary>Событие: заблокирована сетевая атака через Port Defender.</summary>
    public const string PortDefenderAttackBlocked = "PortDefenderAttackBlocked";
    /// <summary>Запрос штатной остановки фоновой службы (инициируется UI при выходе).</summary>
    public const string ShutdownService = "ShutdownService";
    /// <summary>Запрос всех правил брандмауэра Windows.</summary>
    public const string GetAllFirewallRules = "GetAllFirewallRules";
    /// <summary>Ответ со списком всех правил брандмауэра Windows.</summary>
    public const string GetAllFirewallRulesResponse = "GetAllFirewallRulesResponse";
    /// <summary>Создание/обновление правила брандмауэра.</summary>
    public const string CreateFirewallRule = "CreateFirewallRule";
    /// <summary>Удаление правила брандмауэра по имени.</summary>
    public const string DeleteFirewallRule = "DeleteFirewallRule";
    /// <summary>Включение/выключение правила брандмауэра.</summary>
    public const string ToggleFirewallRule = "ToggleFirewallRule";
    /// <summary>Установка режима фильтрации брандмауэра.</summary>
    public const string SetFirewallFilteringMode = "SetFirewallFilteringMode";
    /// <summary>Запрос режима фильтрации брандмауэра.</summary>
    public const string GetFirewallFilteringMode = "GetFirewallFilteringMode";
    /// <summary>Ответ с режимом фильтрации брандмауэра.</summary>
    public const string GetFirewallFilteringModeResponse = "GetFirewallFilteringModeResponse";
    /// <summary>Запрос ожидающих алертов о новых сетевых подключениях программ.</summary>
    public const string GetPendingNetworkAlerts = "GetPendingNetworkAlerts";
    /// <summary>Ответ службы со списком ожидающих сетевых алертов.</summary>
    public const string GetPendingNetworkAlertsResponse = "GetPendingNetworkAlertsResponse";
    /// <summary>Решение пользователя по сетевому алерту (разрешить/блокировать).</summary>
    public const string ResolveNetworkAlert = "ResolveNetworkAlert";
    /// <summary>Ответ на решение по сетевому алерту.</summary>
    public const string ResolveNetworkAlertResponse = "ResolveNetworkAlertResponse";
    /// <summary>Включение/выключение детектора новых сетевых подключений.</summary>
    public const string SetAlertsEnabled = "SetAlertsEnabled";
    /// <summary>Запрос состояния детектора новых сетевых подключений.</summary>
    public const string GetAlertsEnabled = "GetAlertsEnabled";
    /// <summary>Ответ службы с состоянием детектора новых сетевых подключений.</summary>
    public const string GetAlertsEnabledResponse = "GetAlertsEnabledResponse";

    /// <summary>Установка тихого режима для сокетов без определенного процесса (PID = 0).</summary>
    public const string SetFirewallSilentPid0 = "SetFirewallSilentPid0";
    /// <summary>Запрос статуса тихого режима для PID = 0.</summary>
    public const string GetFirewallSilentPid0 = "GetFirewallSilentPid0";
    /// <summary>Ответ со статусом тихого режима для PID = 0.</summary>
    public const string GetFirewallSilentPid0Response = "GetFirewallSilentPid0Response";
    /// <summary>Запрос экспорта правил брандмауэра в бэкап.</summary>
    public const string ExportFirewallRules = "ExportFirewallRules";
    /// <summary>Ответ службы с бэкапом правил брандмауэра.</summary>
    public const string ExportFirewallRulesResponse = "ExportFirewallRulesResponse";
    /// <summary>Запрос импорта правил брандмауэра из бэкапа.</summary>
    public const string ImportFirewallRules = "ImportFirewallRules";
    /// <summary>Ответ службы с результатом импорта правил брандмауэра.</summary>
    public const string ImportFirewallRulesResponse = "ImportFirewallRulesResponse";
    /// <summary>Запрос применения готового пресета безопасности.</summary>
    public const string ApplySecurityPreset = "ApplySecurityPreset";
    /// <summary>Ответ службы с результатом применения пресета безопасности.</summary>
    public const string ApplySecurityPresetResponse = "ApplySecurityPresetResponse";

}

/// <summary>Полезная нагрузка для сообщения SetAiConfig: ключ API, URL эндпоинта и имя модели.</summary>
public record AiConfigPayload(string ApiKey, string? ApiUrl = null, string? Model = null);

/// <summary>Полезная нагрузка для ответа TestAiConnectionResponse: результат проверки связи с AI.</summary>
public record TestAiResultPayload(bool Success, string Message);

/// <summary>Полезная нагрузка для настройки троттлинга процессора.</summary>
public record CpuThrottlePayload(bool Enabled, int Threshold);

/// <summary>Полезная нагрузка для настройки строгого режима.</summary>
public record StrictModePayload(bool Enabled);

/// <summary>Полезная нагрузка для уведомления ActionNotice: имя записи и код действия
/// ("killed", "disabled", "autoDisabled", "removed", "restored", "lockdownBlocked").</summary>
public record ActionNoticePayload(string Name, string Action);

/// <summary>Полезная нагрузка для завершения процесса по PID.</summary>
public record KillProcessPayload(int ProcessId, string? ProcessName = null);

/// <summary>Полезная нагрузка для блокировки процесса через Чёрный список.</summary>
public record BlockProcessPayload(int ProcessId, string FilePath, string? ProcessName = null);

/// <summary>Полезная нагрузка для приостановки процесса по PID.</summary>
public record SuspendProcessPayload(int ProcessId, string? ProcessName = null);

/// <summary>Полезная нагрузка для возобновления процесса по PID.</summary>
public record ResumeProcessPayload(int ProcessId, string? ProcessName = null);

/// <summary>Полезная нагрузка ответа на действие над процессом.</summary>
public record ProcessActionResultPayload(bool Success, string Message, int ProcessId = 0);

/// <summary>Полезная нагрузка для сохранения настроек удалённых уведомлений.</summary>
public record RemoteNotificationConfigPayload(
    bool TelegramEnabled,
    string TelegramBotToken,
    string TelegramChatId,
    bool DiscordWebhookEnabled,
    string DiscordWebhookUrl,
    bool RemoteNotifyOnNewEntry,
    bool RemoteNotifyOnThreat,
    bool RemoteNotifyOnBlocked
);

/// <summary>Полезная нагрузка запроса проверки канала удалённых уведомлений ("telegram", "discord", "all").</summary>
public record TestRemoteNotificationPayload(string Channel);

/// <summary>Полезная нагрузка ответа на запрос проверки канала удалённых уведомлений.</summary>
public record TestRemoteNotificationResultPayload(bool Success, string Message);

/// <summary>Полезная нагрузка установки отложенного запуска записи автозапуска.</summary>
public record SetDelayedEntryPayload(string EntryId, int DelaySeconds);

/// <summary>Полезная нагрузка отмены отложенного запуска записи автозапуска.</summary>
public record CancelDelayedEntryPayload(string EntryId);

/// <summary>Полезная нагрузка принудительного запуска отложенной записи.</summary>
public record LaunchDelayedEntryPayload(string EntryId);

/// <summary>Полезная нагрузка создания контрольного снимка автозапуска.</summary>
public class CreateSnapshotPayload
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>CreateSnapshotPayload: Выполняет операцию CreateSnapshotPayload.</summary>
    public CreateSnapshotPayload() { }
    /// <summary>CreateSnapshotPayload: Выполняет операцию CreateSnapshotPayload.</summary>
    /// <param name="name">Входные данные: значение параметра name (string?).</param>
    /// <param name="description">Входные данные: значение параметра description (string?).</param>
    public CreateSnapshotPayload(string? name, string? description) { Name = name; Description = description; }
}

/// <summary>Полезная нагрузка удаления контрольного снимка.</summary>
public record DeleteSnapshotPayload(string SnapshotId);

/// <summary>Полезная нагрузка сравнения текущего состояния с контрольным снимком.</summary>
public record CompareSnapshotPayload(string SnapshotId);

/// <summary>Полезная нагрузка состояния глобального мониторинга (активен/на паузе).</summary>
public record MonitoringStatePayload
{
    [JsonPropertyName("IsPaused")]
    public bool IsPaused { get; init; }

    [JsonPropertyName("is_paused")]
    [JsonInclude]
    public bool? IsPausedSnake
    {
        get => null;
        init
        {
            if (value.HasValue) IsPaused = value.Value;
        }
    }

    /// <summary>MonitoringStatePayload: Выполняет операцию MonitoringStatePayload.</summary>
    public MonitoringStatePayload() { }
    /// <summary>MonitoringStatePayload: Выполняет операцию MonitoringStatePayload.</summary>
    /// <param name="isPaused">Входные данные: значение параметра isPaused (bool).</param>
    public MonitoringStatePayload(bool isPaused)
    {
        IsPaused = isPaused;
    }

    /// <summary>Deconstruct: Выполняет операцию Deconstruct.</summary>
    /// <param name="isPaused">Входные данные: значение параметра isPaused (out bool).</param>
    public void Deconstruct(out bool isPaused) => isPaused = IsPaused;
}

/// <summary>Полезная нагрузка с расширенной информацией о службе.</summary>
public record ServiceInfoPayload(
    int Pid,
    string Version,
    string ScmStatus,
    DateTime StartTime,
    TimeSpan Uptime,
    bool IsPaused
);

/// <summary>Полезная нагрузка настройки автоматических политик защиты (Auto-Guards).</summary>
public record AutoGuardConfigPayload(
    string Mode,
    bool BlockTempPath,
    bool BlockUnsigned,
    bool BlockScripts,
    bool BlockDangerousCli,
    bool BlockNewServices
);

/// <summary>Запрос проверки обновлений. Клиент передаёт свою версию UI и дату сборки для точной оценки.</summary>
public sealed class CheckForUpdatesRequest
{
    /// <summary>Версия UI-клиента (например "1.4.0"). Null — старый клиент без поддержки.</summary>
    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }

    /// <summary>Дата сборки/создания исполняемого файла UI-клиента (UTC).</summary>
    [JsonPropertyName("clientBuildDateUtc")]
    public DateTime? ClientBuildDateUtc { get; set; }

    public CheckForUpdatesRequest() { }
    public CheckForUpdatesRequest(string? clientVersion, DateTime? clientBuildDateUtc = null)
    {
        ClientVersion = clientVersion;
        ClientBuildDateUtc = clientBuildDateUtc;
    }
}

/// <summary>Запрос дельты записей автозапуска. Клиент передаёт последнюю известную ревизию.</summary>
public sealed class EntriesRevisionRequest
{
    /// <summary>Последняя известная клиенту ревизия (0 = запросить полный снимок).</summary>
    [JsonPropertyName("clientRevision")]
    public long ClientRevision { get; set; }

    /// <summary>EntriesRevisionRequest: Выполняет операцию EntriesRevisionRequest.</summary>
    public EntriesRevisionRequest() { }
    /// <summary>EntriesRevisionRequest: Выполняет операцию EntriesRevisionRequest.</summary>
    /// <param name="clientRevision">Входные данные: значение параметра clientRevision (long).</param>
    public EntriesRevisionRequest(long clientRevision) { ClientRevision = clientRevision; }
}

/// <summary>Ответ с дельтой записей автозапуска. При IsModified=false список Entries пуст/отсутствует.</summary>
public sealed class EntriesDeltaResponse
{
    /// <summary>Актуальная ревизия на стороне службы.</summary>
    [JsonPropertyName("newRevision")]
    public long NewRevision { get; set; }

    /// <summary>True — данные изменились, Entries содержит актуальный снимок/дельту.</summary>
    [JsonPropertyName("isModified")]
    public bool IsModified { get; set; }

    /// <summary>Актуальный список записей (null/пусто при NotModified).</summary>
    [JsonPropertyName("entries")]
    public List<StartupEntry>? Entries { get; set; }

    /// <summary>EntriesDeltaResponse: Выполняет операцию EntriesDeltaResponse.</summary>
    public EntriesDeltaResponse() { }
    /// <summary>EntriesDeltaResponse: Выполняет операцию EntriesDeltaResponse.</summary>
    /// <param name="newRevision">Входные данные: значение параметра newRevision (long).</param>
    /// <param name="isModified">Входные данные: значение параметра isModified (bool).</param>
    /// <param name="entries">Входные данные: значение параметра entries (List<StartupEntry>?).</param>
    public EntriesDeltaResponse(long newRevision, bool isModified, List<StartupEntry>? entries = null)
    {
        NewRevision = newRevision;
        IsModified = isModified;
        Entries = entries;
    }
}

/// <summary>Полезная нагрузка запроса блокировки IP-адреса.</summary>
public record BlockIpPayload(
    string Ip,
    FirewallDirection Direction = FirewallDirection.Both,
    bool Aggregated = true,
    int? BanDurationMinutes = null,
    string? Reason = null
);

/// <summary>Полезная нагрузка запроса разблокировки IP-адреса.</summary>
public record UnblockIpPayload(
    string Ip,
    FirewallDirection Direction = FirewallDirection.Both
);

/// <summary>Полезная нагрузка запроса блокировки порта.</summary>
public record BlockPortPayload(
    int Port,
    FirewallProtocol Protocol = FirewallProtocol.Tcp,
    FirewallDirection Direction = FirewallDirection.Inbound,
    string? ApplicationPath = null,
    string? Reason = null
);

/// <summary>Полезная нагрузка запроса разблокировки порта.</summary>
public record UnblockPortPayload(
    int Port,
    FirewallProtocol Protocol = FirewallProtocol.Tcp,
    FirewallDirection Direction = FirewallDirection.Inbound,
    string? ApplicationPath = null
);

/// <summary>Полезная нагрузка ответа со списком активных банов Port Defender.</summary>
public record ActiveBansPayload(List<ActiveBanDto> Bans);

/// <summary>Полезная нагрузка ответа со списком всех правил брандмауэра.</summary>
public record FirewallRulesPayload(List<FirewallRuleDto> Rules);

/// <summary>Полезная нагрузка запроса создания/обновления правила брандмауэра.</summary>
public record CreateRulePayload(FirewallRuleDto Rule);

/// <summary>Полезная нагрузка запроса удаления правила брандмауэра.</summary>
public record DeleteRulePayload(string Name, FirewallDirection Direction = FirewallDirection.Both);

/// <summary>Полезная нагрузка запроса включения/выключения правила брандмауэра.</summary>
public record ToggleRulePayload(string Name, FirewallDirection Direction, bool Enable);

/// <summary>Полезная нагрузка режима фильтрации брандмауэра.</summary>
public record FilteringModePayload(FirewallFilteringMode Mode);

/// <summary>Полезная нагрузка ответа со списком ожидающих сетевых алертов.</summary>
public record NetworkAlertsPayload(List<NetworkConnectionAlertDto> Alerts);

/// <summary>Полезная нагрузка состояния детектора новых сетевых подключений.</summary>
public record AlertsEnabledPayload(bool Enabled);

/// <summary>Полезная нагрузка тихого режима для PID = 0.</summary>
public record FirewallSilentPid0Payload(bool Silent);

/// <summary>Полезная нагрузка ответа на решение по сетевому алерту.</summary>
public record ResolveNetworkAlertResultPayload(bool Success, string Message);

/// <summary>Полезная нагрузка запроса импорта правил брандмауэра из бэкапа.</summary>
public sealed class ImportFirewallRulesRequest
{
    /// <summary>Бэкап правил для импорта.</summary>
    [JsonPropertyName("backup")]
    public FirewallBackupDto Backup { get; set; } = new();

    /// <summary>Перезаписывать существующие правила с тем же именем.</summary>
    [JsonPropertyName("overwriteExisting")]
    public bool OverwriteExisting { get; set; }

    /// <summary>ImportFirewallRulesRequest: пустой конструктор для десериализации.</summary>
    public ImportFirewallRulesRequest() { }

    /// <summary>ImportFirewallRulesRequest: создаёт запрос.</summary>
    /// <param name="backup">Входные данные: значение параметра backup (FirewallBackupDto).</param>
    /// <param name="overwriteExisting">Входные данные: значение параметра overwriteExisting (bool).</param>
    public ImportFirewallRulesRequest(FirewallBackupDto backup, bool overwriteExisting = false)
    {
        Backup = backup;
        OverwriteExisting = overwriteExisting;
    }
}

/// <summary>Полезная нагрузка ответа с результатом импорта правил брандмауэра.</summary>
public record FirewallImportResultPayload(int Imported, int Skipped);

/// <summary>Полезная нагрузка запроса применения готового пресета безопасности.</summary>
public sealed class ApplySecurityPresetRequest
{
    /// <summary>Применяемый пресет.</summary>
    [JsonPropertyName("preset")]
    public SecurityPresetType Preset { get; set; } = SecurityPresetType.MaxPrivacy;

    /// <summary>Алиас пресета (совместимость с контрактом presetType).</summary>
    [JsonPropertyName("presetType")]
    public SecurityPresetType PresetType
    {
        get => Preset;
        set => Preset = value;
    }

    /// <summary>ApplySecurityPresetRequest: пустой конструктор для десериализации.</summary>
    public ApplySecurityPresetRequest() { }

    /// <summary>ApplySecurityPresetRequest: создаёт запрос.</summary>
    /// <param name="preset">Входные данные: значение параметра preset (SecurityPresetType).</param>
    public ApplySecurityPresetRequest(SecurityPresetType preset) => Preset = preset;
}

/// <summary>Полезная нагрузка ответа с результатом применения пресета безопасности.</summary>
public record SecurityPresetResultPayload(SecurityPresetType Preset, int AppliedCount, bool Success, string Message);

