using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.Remote;

/// <summary>
/// Сервис удалённых push-оповещений в Telegram и Discord Webhook.
/// Отправляет уведомления о новых записях автозапуска, подозрительных командах (CLI Threats)
/// и заблокированных нарушителях.
/// </summary>
public class RemoteNotificationService : IDisposable
{
    private readonly JsonDataStore _store;
    private readonly ILogger<RemoteNotificationService> _logger;
    private readonly HttpClient _http;
    private readonly bool _disposeClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RemoteNotificationService(
        JsonDataStore store,
        ILogger<RemoteNotificationService> logger,
        HttpClient? httpClient = null)
    {
        _store = store;
        _logger = logger;
        if (httpClient != null)
        {
            _http = httpClient;
            _disposeClient = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _disposeClient = true;
        }
    }

    /// <summary>Уведомляет о добавлении новой записи автозапуска.</summary>
    public async Task NotifyNewEntryAsync(StartupEntry entry)
    {
        var settings = _store.Load().Settings;
        if (!settings.RemoteNotifyOnNewEntry) return;

        var host = Environment.MachineName;
        var now = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

        var tasks = new List<Task>();

        if (settings.TelegramEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            var tgText = new StringBuilder()
                .AppendLine("🚀 <b>Startup Monitor: Новая запись автозапуска</b>")
                .AppendLine($"🖥 <b>Хост:</b> {EscapeHtml(host)}")
                .AppendLine($"📌 <b>Имя:</b> {EscapeHtml(entry.Name)}")
                .AppendLine($"📂 <b>Расположение:</b> {EscapeHtml(entry.LocationDetail ?? entry.Location.ToString())}")
                .AppendLine($"📄 <b>Путь:</b> <code>{EscapeHtml(entry.FilePath)}</code>");

            if (!string.IsNullOrWhiteSpace(entry.CommandLine) && entry.CommandLine != entry.FilePath)
            {
                tgText.AppendLine($"💻 <b>Команда:</b> <code>{EscapeHtml(entry.CommandLine)}</code>");
            }
            if (!string.IsNullOrWhiteSpace(entry.Publisher))
            {
                tgText.AppendLine($"🏢 <b>Издатель:</b> {EscapeHtml(entry.Publisher)}");
            }
            tgText.AppendLine($"🕒 <b>Время:</b> {now}");

            tasks.Add(SendTelegramAsync(settings.TelegramBotToken, settings.TelegramChatId, tgText.ToString()));
        }

        if (settings.DiscordWebhookEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            var fields = new List<DiscordField>
            {
                new("Объект", string.IsNullOrWhiteSpace(entry.Name) ? "Без имени" : entry.Name, true),
                new("Расположение", entry.LocationDetail ?? entry.Location.ToString(), true),
                new("Файл", string.IsNullOrWhiteSpace(entry.FilePath) ? "Не указан" : entry.FilePath, false)
            };

            if (!string.IsNullOrWhiteSpace(entry.CommandLine) && entry.CommandLine != entry.FilePath)
            {
                fields.Add(new("Команда", entry.CommandLine, false));
            }
            if (!string.IsNullOrWhiteSpace(entry.Publisher))
            {
                fields.Add(new("Издатель", entry.Publisher, true));
            }

            var embed = new DiscordEmbed(
                Title: "🚀 Новая запись автозапуска",
                Description: $"В автозапуске Windows обнаружен новый элемент: **{entry.Name}**",
                Color: 0x2ECC71, // Green
                Fields: fields,
                Footer: new DiscordFooter($"Startup Monitor • {host}"),
                Timestamp: DateTime.UtcNow.ToString("o")
            );

            tasks.Add(SendDiscordAsync(settings.DiscordWebhookUrl, new DiscordPayload("Startup Monitor", null, [embed])));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>Уведомляет об обнаружении подозрительной команды (CLI Threat Heuristics).</summary>
    public async Task NotifyThreatAsync(StartupEntry entry, CliThreatInfo threat)
    {
        var settings = _store.Load().Settings;
        if (!settings.RemoteNotifyOnThreat) return;

        var host = Environment.MachineName;
        var now = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
        var tasks = new List<Task>();

        if (settings.TelegramEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            var tgText = new StringBuilder()
                .AppendLine("⚠️ <b>Startup Monitor: ВНИМАНИЕ! Подозрительная команда</b>")
                .AppendLine($"🖥 <b>Хост:</b> {EscapeHtml(host)}")
                .AppendLine($"📌 <b>Имя:</b> {EscapeHtml(entry.Name)}")
                .AppendLine($"🚨 <b>Уровень риска:</b> {threat.ThreatLevel} ({EscapeHtml(threat.BadgeText)})")
                .AppendLine($"📝 <b>Эвристика:</b> {EscapeHtml(threat.Description)}")
                .AppendLine($"📄 <b>Путь:</b> <code>{EscapeHtml(entry.FilePath)}</code>")
                .AppendLine($"💻 <b>Команда:</b> <code>{EscapeHtml(entry.CommandLine)}</code>");

            if (threat.Indicators.Count > 0)
            {
                tgText.AppendLine($"🔍 <b>Индикаторы:</b> {EscapeHtml(string.Join(", ", threat.Indicators))}");
            }
            tgText.AppendLine($"🕒 <b>Время:</b> {now}");

            tasks.Add(SendTelegramAsync(settings.TelegramBotToken, settings.TelegramChatId, tgText.ToString()));
        }

        if (settings.DiscordWebhookEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            var fields = new List<DiscordField>
            {
                new("Объект", string.IsNullOrWhiteSpace(entry.Name) ? "Без имени" : entry.Name, true),
                new("Уровень риска", $"{threat.ThreatLevel} ({threat.BadgeText})", true),
                new("Описание", threat.Description, false),
                new("Путь", string.IsNullOrWhiteSpace(entry.FilePath) ? "Не указан" : entry.FilePath, false),
                new("Командная строка", string.IsNullOrWhiteSpace(entry.CommandLine) ? "Не указана" : entry.CommandLine, false)
            };

            if (threat.Indicators.Count > 0)
            {
                fields.Add(new("Индикаторы", string.Join("\n• ", threat.Indicators), false));
            }

            var embed = new DiscordEmbed(
                Title: "⚠️ Подозрительная команда в автозапуске (CLI Threat)",
                Description: $"Эвристический анализатор выявил потенциально опасные аргументы запуска у **{entry.Name}**.",
                Color: 0xE67E22, // Orange
                Fields: fields,
                Footer: new DiscordFooter($"Startup Monitor • {host}"),
                Timestamp: DateTime.UtcNow.ToString("o")
            );

            tasks.Add(SendDiscordAsync(settings.DiscordWebhookUrl, new DiscordPayload("Startup Monitor", null, [embed])));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>Уведомляет о блокировке записи Чёрным списком или режимом изоляции Lockdown.</summary>
    public async Task NotifyBlockedAsync(StartupEntry entry, string reason)
    {
        var settings = _store.Load().Settings;
        if (!settings.RemoteNotifyOnBlocked) return;

        var host = Environment.MachineName;
        var now = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
        var tasks = new List<Task>();

        if (settings.TelegramEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            var tgText = new StringBuilder()
                .AppendLine("🛑 <b>Startup Monitor: Заблокирован автозапуск</b>")
                .AppendLine($"🖥 <b>Хост:</b> {EscapeHtml(host)}")
                .AppendLine($"📌 <b>Имя:</b> {EscapeHtml(entry.Name)}")
                .AppendLine($"🛡 <b>Действие:</b> {EscapeHtml(reason)}")
                .AppendLine($"📂 <b>Расположение:</b> {EscapeHtml(entry.LocationDetail ?? entry.Location.ToString())}")
                .AppendLine($"📄 <b>Путь:</b> <code>{EscapeHtml(entry.FilePath)}</code>")
                .AppendLine($"🕒 <b>Время:</b> {now}");

            tasks.Add(SendTelegramAsync(settings.TelegramBotToken, settings.TelegramChatId, tgText.ToString()));
        }

        if (settings.DiscordWebhookEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
        {
            var fields = new List<DiscordField>
            {
                new("Объект", string.IsNullOrWhiteSpace(entry.Name) ? "Без имени" : entry.Name, true),
                new("Действие", reason, true),
                new("Расположение", entry.LocationDetail ?? entry.Location.ToString(), false),
                new("Файл", string.IsNullOrWhiteSpace(entry.FilePath) ? "Не указан" : entry.FilePath, false)
            };

            var embed = new DiscordEmbed(
                Title: "🛑 Заблокирована запись автозапуска",
                Description: $"Служба безопасности нейтрализовала запуск нежелательного объекта: **{entry.Name}**.",
                Color: 0xE74C3C, // Red
                Fields: fields,
                Footer: new DiscordFooter($"Startup Monitor • {host}"),
                Timestamp: DateTime.UtcNow.ToString("o")
            );

            tasks.Add(SendDiscordAsync(settings.DiscordWebhookUrl, new DiscordPayload("Startup Monitor", null, [embed])));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>Выполняет отправку тестового оповещения в Telegram.</summary>
    public async Task<(bool Success, string Message)> TestTelegramAsync(string? token = null, string? chatId = null, CancellationToken ct = default)
    {
        var settings = _store.Load().Settings;
        var actualToken = !string.IsNullOrWhiteSpace(token) ? token : settings.TelegramBotToken;
        var actualChatId = !string.IsNullOrWhiteSpace(chatId) ? chatId : settings.TelegramChatId;

        if (string.IsNullOrWhiteSpace(actualToken))
            return (false, "Токен Telegram бота не задан.");
        if (string.IsNullOrWhiteSpace(actualChatId))
            return (false, "Chat ID Telegram не задан.");

        var host = Environment.MachineName;
        var text = new StringBuilder()
            .AppendLine("🔔 <b>Startup Monitor: Тестовое уведомление</b>")
            .AppendLine($"🖥 <b>Хост:</b> {EscapeHtml(host)}")
            .AppendLine("✅ Интеграция с Telegram успешно настроена!")
            .AppendLine("Теперь вы будете получать мгновенные сигналы о новых записях и угрозах.")
            .AppendLine($"🕒 <b>Время:</b> {DateTime.Now:dd.MM.yyyy HH:mm:ss}")
            .ToString();

        try
        {
            var url = $"https://api.telegram.org/bot{actualToken}/sendMessage";
            var body = new
            {
                chat_id = actualChatId,
                text,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            using var resp = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);
            if (resp.IsSuccessStatusCode)
            {
                return (true, "Тестовое сообщение успешно доставлено в Telegram!");
            }

            var errContent = await resp.Content.ReadAsStringAsync(ct);
            return (false, $"Ошибка Telegram API ({resp.StatusCode}): {errContent}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка проверки Telegram уведомлений");
            return (false, $"Не удалось отправить сообщение в Telegram: {ex.Message}");
        }
    }

    /// <summary>Выполняет отправку тестового оповещения в Discord Webhook.</summary>
    public async Task<(bool Success, string Message)> TestDiscordAsync(string? webhookUrl = null, CancellationToken ct = default)
    {
        var settings = _store.Load().Settings;
        var actualUrl = !string.IsNullOrWhiteSpace(webhookUrl) ? webhookUrl : settings.DiscordWebhookUrl;

        if (string.IsNullOrWhiteSpace(actualUrl))
            return (false, "URL вебхука Discord не задан.");

        var host = Environment.MachineName;
        var fields = new List<DiscordField>
        {
            new("Статус проверки", "Успешно подключено", true),
            new("Компьютер", host, true),
            new("Интеграция", "Discord Webhook активен и готов к работе", false)
        };

        var embed = new DiscordEmbed(
            Title: "🔔 Startup Monitor: Тестовое уведомление",
            Description: "Канал оповещений Discord Webhook успешно сконфигурирован.",
            Color: 0x3498DB, // Blue
            Fields: fields,
            Footer: new DiscordFooter($"Startup Monitor • {host}"),
            Timestamp: DateTime.UtcNow.ToString("o")
        );

        var payload = new DiscordPayload("Startup Monitor", null, [embed]);

        try
        {
            using var resp = await _http.PostAsJsonAsync(actualUrl, payload, JsonOptions, ct);
            if (resp.IsSuccessStatusCode)
            {
                return (true, "Тестовое сообщение успешно доставлено в Discord!");
            }

            var errContent = await resp.Content.ReadAsStringAsync(ct);
            return (false, $"Ошибка Discord Webhook ({resp.StatusCode}): {errContent}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка проверки Discord вебхука");
            return (false, $"Не удалось отправить сообщение в Discord: {ex.Message}");
        }
    }

    /// <summary>Проверяет указанный канал ("telegram", "discord" или "all").</summary>
    public async Task<(bool Success, string Message)> TestChannelAsync(string channel, CancellationToken ct = default)
    {
        var target = channel?.Trim().ToLowerInvariant() ?? "all";
        if (target == "telegram")
        {
            return await TestTelegramAsync(ct: ct);
        }
        if (target == "discord")
        {
            return await TestDiscordAsync(ct: ct);
        }

        // Both
        var tgTask = TestTelegramAsync(ct: ct);
        var dcTask = TestDiscordAsync(ct: ct);
        await Task.WhenAll(tgTask, dcTask);

        var (tgOk, tgMsg) = tgTask.Result;
        var (dcOk, dcMsg) = dcTask.Result;

        if (tgOk && dcOk)
            return (true, "Оба канала (Telegram и Discord) успешно проверены!");
        if (tgOk && !dcOk)
            return (false, $"Telegram: успешно; Discord: {dcMsg}");
        if (!tgOk && dcOk)
            return (false, $"Telegram: {tgMsg}; Discord: успешно");

        return (false, $"Telegram: {tgMsg}; Discord: {dcMsg}");
    }

    private async Task SendTelegramAsync(string token, string chatId, string html)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var body = new
            {
                chat_id = chatId,
                text = html,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };
            using var resp = await _http.PostAsJsonAsync(url, body, JsonOptions, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("Telegram уведомление не отправлено ({Code}): {Error}", resp.StatusCode, err);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Исключение при отправке уведомления в Telegram");
        }
    }

    private async Task SendDiscordAsync(string webhookUrl, DiscordPayload payload)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await _http.PostAsJsonAsync(webhookUrl, payload, JsonOptions, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("Discord уведомление не отправлено ({Code}): {Error}", resp.StatusCode, err);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Исключение при отправке уведомления в Discord");
        }
    }

    public static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _http.Dispose();
        }
    }
}

// Вспомогательные модели сериализации для Discord Webhook
internal record DiscordPayload(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("embeds")] List<DiscordEmbed> Embeds
);

internal record DiscordEmbed(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("color")] int Color,
    [property: JsonPropertyName("fields")] List<DiscordField> Fields,
    [property: JsonPropertyName("footer")] DiscordFooter Footer,
    [property: JsonPropertyName("timestamp")] string Timestamp
);

internal record DiscordField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("inline")] bool Inline = false
);

internal record DiscordFooter(
    [property: JsonPropertyName("text")] string Text
);
