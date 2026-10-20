using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Shared.Models;

/// <summary>Главный конфиг приложения: записи автозапуска, чёрный список, история и настройки.</summary>
public class AppConfig
{
    /// <summary>Язык интерфейса ("ru", "en").</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru";

    /// <summary>Тема оформления ("dark", "light").</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    /// <summary>Список записей автозапуска.</summary>
    [JsonPropertyName("entries")]
    public List<StartupEntry> Entries { get; set; } = [];

    /// <summary>Правила чёрного списка.</summary>
    [JsonPropertyName("blacklist")]
    public List<BlacklistRule> Blacklist { get; set; } = [];

    /// <summary>Удалённые записи (бэкап для восстановления).</summary>
    [JsonPropertyName("removed")]
    public List<RemovedEntry> RemovedEntries { get; set; } = [];

    /// <summary>История изменений.</summary>
    [JsonPropertyName("history")]
    public List<HistoryEntry> History { get; set; } = [];

    /// <summary>Настройки поведения приложения.</summary>
    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();
}

/// <summary>Поведенческие настройки приложения.</summary>
public class AppSettings
{
    /// <summary>Показывать уведомление при появлении новой записи.</summary>
    [JsonPropertyName("notify_on_new_entry")]
    public bool NotifyOnNewEntry { get; set; } = true;

    /// <summary>Запускать UI вместе с Windows.</summary>
    [JsonPropertyName("start_with_windows")]
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Сворачивать UI в трей вместо закрытия.</summary>
    [JsonPropertyName("minimize_to_tray")]
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Строгий режим: усиленный контроль автозапуска.</summary>
    [JsonPropertyName("strict_mode")]
    public bool StrictMode { get; set; }

    /// <summary>Флаг глобальной приостановки мониторинга автозапуска.</summary>
    [JsonPropertyName("is_monitoring_paused")]
    public bool IsMonitoringPaused { get; set; }

    /// <summary>Троттлинг сканирования реестра при высокой загрузке CPU.</summary>
    [JsonPropertyName("cpu_throttle_enabled")]
    public bool CpuThrottleEnabled { get; set; }

    /// <summary>Порог загрузки CPU (%) выше которого сканирование реестра пропускается.</summary>
    [JsonPropertyName("cpu_throttle_threshold")]
    public int CpuThrottleThreshold { get; set; } = 70;

    /// <summary>Автоматически блокировать запись при удалении из системы.</summary>
    [JsonPropertyName("auto_block_on_remove")]
    public bool AutoBlockOnRemove { get; set; }

    /// <summary>Убивать процесс сразу при отключении записи (Disable).</summary>
    [JsonPropertyName("kill_on_disable")]
    public bool KillOnDisable { get; set; } = false;

    /// <summary>Сколько дней хранить историю изменений.</summary>
    [JsonPropertyName("history_days")]
    public int HistoryDays { get; set; } = 30;

    /// <summary>Текущий фильтр отображения в UI ("all", "enabled", "blocked", ...).</summary>
    [JsonPropertyName("filter")]
    public string Filter { get; set; } = "all";

    /// <summary>API-ключ для AI-рейтинга (Groq, OpenAI, Ollama и др.).</summary>
    [JsonPropertyName("ai_api_key")]
    public string AiApiKey { get; set; } = string.Empty;

    /// <summary>URL эндпоинта для AI-рейтинга (OpenAI-совместимый API).</summary>
    [JsonPropertyName("ai_api_url")]
    public string AiApiUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";

    /// <summary>Имя модели AI для оценки автозапуска.</summary>
    [JsonPropertyName("ai_model")]
    public string AiModel { get; set; } = "openai/gpt-oss-120b";

    /// <summary>Включены ли уведомления в Telegram.</summary>
    [JsonPropertyName("telegram_enabled")]
    public bool TelegramEnabled { get; set; }

    /// <summary>Токен Telegram бота (от @BotFather).</summary>
    [JsonPropertyName("telegram_bot_token")]
    public string TelegramBotToken { get; set; } = string.Empty;

    /// <summary>ID чата или канала Telegram для отправки уведомлений.</summary>
    [JsonPropertyName("telegram_chat_id")]
    public string TelegramChatId { get; set; } = string.Empty;

    /// <summary>Включены ли уведомления через Discord Webhook.</summary>
    [JsonPropertyName("discord_webhook_enabled")]
    public bool DiscordWebhookEnabled { get; set; }

    /// <summary>URL вебхука Discord для отправки уведомлений.</summary>
    [JsonPropertyName("discord_webhook_url")]
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    /// <summary>Отправлять удалённое уведомление при обнаружении новой записи.</summary>
    [JsonPropertyName("remote_notify_on_new_entry")]
    public bool RemoteNotifyOnNewEntry { get; set; } = true;

    /// <summary>Отправлять удалённое уведомление при обнаружении подозрительной команды (CLI Threat).</summary>
    [JsonPropertyName("remote_notify_on_threat")]
    public bool RemoteNotifyOnThreat { get; set; } = true;

    /// <summary>Отправлять удалённое уведомление при блокировке записи ЧС или строгим режимом.</summary>
    [JsonPropertyName("remote_notify_on_blocked")]
    public bool RemoteNotifyOnBlocked { get; set; } = true;

    /// <summary>Включена ли репутационная проверка через Kaspersky OpenTIP.</summary>
    [JsonPropertyName("kaspersky_enabled")]
    public bool KasperskyEnabled { get; set; } = true;

    /// <summary>API-токен для доступа к Kaspersky OpenTIP.</summary>
    [JsonPropertyName("kaspersky_api_key")]
    public string KasperskyApiKey { get; set; } = "Zem+jUJbQEiqHPpwsfjTyQ==";

    /// <summary>Базовый URL эндпоинта поиска по хешу Kaspersky OpenTIP.</summary>
    [JsonPropertyName("kaspersky_api_url")]
    public string KasperskyApiUrl { get; set; } = "https://opentip.kaspersky.com/api/v1/search/hash";

    /// <summary>Режим работы автоматических политик защиты ("AuditOnly", "EnforceBlock").</summary>
    [JsonPropertyName("auto_guard_mode")]
    public string AutoGuardMode { get; set; } = "AuditOnly";

    /// <summary>Политика: блокировать автозапуск из каталогов %TEMP%, %WINDIR%\Temp, Users\Public, PerfLogs.</summary>
    [JsonPropertyName("guard_block_temp_path")]
    public bool GuardBlockTempPath { get; set; } = false;

    /// <summary>Политика: блокировать бинарники без подлинной цифровой подписи Authenticode.</summary>
    [JsonPropertyName("guard_block_unsigned")]
    public bool GuardBlockUnsigned { get; set; } = false;

    /// <summary>Политика: блокировать запуск скриптов (.bat, .vbs, .js, .ps1, mshta, wscript, cscript).</summary>
    [JsonPropertyName("guard_block_scripts")]
    public bool GuardBlockScripts { get; set; } = false;

    /// <summary>Политика: блокировать запуск с критическими CLI-угрозами (Base64, IEX downloaders, маскировка расширений).</summary>
    [JsonPropertyName("guard_block_dangerous_cli")]
    public bool GuardBlockDangerousCli { get; set; } = false;

    /// <summary>Политика: блокировать регистрацию новых несистемных служб Windows.</summary>
    [JsonPropertyName("guard_block_new_services")]
    public bool GuardBlockNewServices { get; set; } = false;

    /// <summary>Автоматически проверять наличие обновлений на GitHub.</summary>
    [JsonPropertyName("auto_check_updates")]
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Автоматически скачивать обновления в фоне при обнаружении.</summary>
    [JsonPropertyName("auto_download_updates")]
    public bool AutoDownloadUpdates { get; set; } = false;

    /// <summary>Время последней проверки обновлений (UTC).</summary>
    [JsonPropertyName("last_update_check_utc")]
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Пропущенная версия обновления (пользователь выбрал "Пропустить эту версию").</summary>
    [JsonPropertyName("skipped_update_version")]
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>Конфигурация службы защиты портов (Port Defender).</summary>
    [JsonPropertyName("port_defender")]
    public PortDefenderConfigDto PortDefender { get; set; } = new();

    /// <summary>Список активных банов Port Defender.</summary>
    [JsonPropertyName("active_bans")]
    public List<ActiveBanDto> ActiveBans { get; set; } = new();

    /// <summary>Тихий режим для сокетов без определенного процесса (PID = 0): подавлять всплывающие алерты брандмауэра.</summary>
    [JsonPropertyName("firewall_silent_pid0")]
    public bool FirewallSilentPid0 { get; set; } = true;

    /// <summary>Список портов, разрешенных пользователем для исключения повторных всплывающих алертов.</summary>
    [JsonPropertyName("firewall_allowed_ports")]
    public List<int> FirewallAllowedPorts { get; set; } = [];

    /// <summary>Список приложений в белом списке сетевого брандмауэра (любые соединения разрешены без алертов).</summary>
    [JsonPropertyName("firewall_whitelisted_apps")]
    public List<string> FirewallWhitelistedApps { get; set; } = [];

    /// <summary>Создает глубокую независимую копию настроек приложения.</summary>
    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.PortDefender = new PortDefenderConfigDto
        {
            Enabled = PortDefender.Enabled,
            MaxAttempts = PortDefender.MaxAttempts,
            WindowUnit = PortDefender.WindowUnit,
            WindowSeconds = PortDefender.WindowSeconds,
            BanDurationMinutes = PortDefender.BanDurationMinutes,
            AutoEscalate = PortDefender.AutoEscalate,
            TrustPrivateNetworks = PortDefender.TrustPrivateNetworks,
            ProtectedPorts = [..PortDefender.ProtectedPorts],
            WhitelistIps = [..PortDefender.WhitelistIps],
            Rules = PortDefender.Rules.Select(r => new PortDefenderRuleConfig(
                r.LocalPort, r.ProcessPath, r.IsEnabled, r.ThresholdCount, r.WindowUnit, r.BanDurationMinutes, r.AutoEscalate, [..r.WhitelistIps])).ToList()
        };
        copy.ActiveBans = ActiveBans.Select(b => new ActiveBanDto
        {
            Ip = b.Ip,
            TargetPort = b.TargetPort,
            TargetProcess = b.TargetProcess,
            Protocol = b.Protocol,
            BanStartedUtc = b.BanStartedUtc
        }).ToList();
        copy.FirewallAllowedPorts = [..FirewallAllowedPorts];
        copy.FirewallWhitelistedApps = [..FirewallWhitelistedApps];
        return copy;
    }
}
public class HistoryEntry
{
    /// <summary>Уникальный идентификатор записи.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Время события (UTC).</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Локальное время события для корректного отображения в интерфейсе.</summary>
    [JsonIgnore]
    public DateTime LocalTimestamp => Timestamp.Kind == DateTimeKind.Utc
        ? Timestamp.ToLocalTime()
        : (Timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc).ToLocalTime()
            : Timestamp);

    /// <summary>Тип изменения (Added, Removed, Blocked, etc.).</summary>
    [JsonPropertyName("change_type")]
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Имя записи, к которой относится событие.</summary>
    [JsonPropertyName("entry_name")]
    public string EntryName { get; set; } = string.Empty;

    /// <summary>Путь к файлу записи.</summary>
    [JsonPropertyName("entry_path")]
    public string EntryPath { get; set; } = string.Empty;

    /// <summary>Местоположение записи в системе.</summary>
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    /// <summary>Выполненное действие (описание).</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>Создает независимую копию записи истории.</summary>
    public HistoryEntry Clone() => (HistoryEntry)MemberwiseClone();
}
