using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Remote;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Remote;

/// <summary>
/// Модульные тесты для RemoteNotificationService: проверка форматирования Telegram HTML,
/// Discord Webhook JSON Embeds, фильтрации событий и безопасной обработки сетевых ошибок.
/// </summary>
public class RemoteNotificationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonDataStore _store;

    public RemoteNotificationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SMon_RemoteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new JsonDataStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> SentRequests { get; } = new();
        public List<string> SentBodies { get; } = new();
        public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
        public string ResponseBodyToReturn { get; set; } = "{\"ok\":true}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                SentBodies.Add(body);
            }
            return new HttpResponseMessage(StatusCodeToReturn)
            {
                Content = new StringContent(ResponseBodyToReturn)
            };
        }
    }

    [Fact]
    public void EscapeHtml_ReplacesSpecialCharsProperly()
    {
        var input = "powershell -enc <script> & 'hello'";
        var escaped = RemoteNotificationService.EscapeHtml(input);
        Assert.Equal("powershell -enc &lt;script&gt; &amp; 'hello'", escaped);
    }

    [Fact]
    public async Task NotifyNewEntry_WhenTelegramEnabled_SendsExpectedHtml()
    {
        var cfg = _store.Load();
        cfg.Settings.TelegramEnabled = true;
        cfg.Settings.TelegramBotToken = "12345:TOKEN";
        cfg.Settings.TelegramChatId = "987654";
        cfg.Settings.DiscordWebhookEnabled = false;
        _store.Save(cfg);

        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        using var service = new RemoteNotificationService(_store, NullLogger<RemoteNotificationService>.Instance, client);

        var entry = new StartupEntry
        {
            Name = "SuperApp <Test>",
            FilePath = "C:\\Tools\\app.exe",
            CommandLine = "C:\\Tools\\app.exe --arg & flag",
            Location = EntryLocation.RegistryRun,
            LocationDetail = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run"
        };

        await service.NotifyNewEntryAsync(entry);
        await Task.Delay(100); // give fire-and-forget task time to dispatch

        Assert.Single(handler.SentRequests);
        var req = handler.SentRequests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Contains("12345:TOKEN/sendMessage", req.RequestUri?.ToString() ?? "");

        var body = handler.SentBodies[0];
        Assert.Contains("SuperApp &lt;Test&gt;", body);
        Assert.Contains("app.exe --arg &amp; flag", body);
        Assert.Contains("987654", body);
        Assert.Contains("HTML", body);
    }

    [Fact]
    public async Task NotifyThreat_WhenDiscordEnabled_SendsExpectedEmbed()
    {
        var cfg = _store.Load();
        cfg.Settings.TelegramEnabled = false;
        cfg.Settings.DiscordWebhookEnabled = true;
        cfg.Settings.DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc";
        _store.Save(cfg);

        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        using var service = new RemoteNotificationService(_store, NullLogger<RemoteNotificationService>.Instance, client);

        var entry = new StartupEntry
        {
            Name = "BadScript",
            FilePath = "C:\\Windows\\System32\\cmd.exe",
            CommandLine = "powershell -enc SQBFAFgA"
        };

        var threat = new CliThreatInfo
        {
            HasThreat = true,
            ThreatLevel = CliThreatLevel.High,
            BadgeText = "Base64 скрипт",
            Description = "Обнаружена закодированная команда PowerShell",
            Indicators = new List<string> { "PowerShell -EncodedCommand" }
        };

        await service.NotifyThreatAsync(entry, threat);
        await Task.Delay(100);

        Assert.Single(handler.SentRequests);
        var req = handler.SentRequests[0];
        Assert.Equal("https://discord.com/api/webhooks/123/abc", req.RequestUri?.ToString());

        var body = handler.SentBodies[0];
        Assert.Contains("BadScript", body);
        Assert.Contains("15105570", body); // Orange color 0xE67E22
        Assert.Contains("Base64 скрипт", body);
        Assert.Contains("PowerShell -EncodedCommand", body);
    }

    [Fact]
    public async Task NotifyBlocked_WhenBothChannelsEnabled_DispatchesToBoth()
    {
        var cfg = _store.Load();
        cfg.Settings.TelegramEnabled = true;
        cfg.Settings.TelegramBotToken = "TG_TOKEN";
        cfg.Settings.TelegramChatId = "TG_CHAT";
        cfg.Settings.DiscordWebhookEnabled = true;
        cfg.Settings.DiscordWebhookUrl = "https://discord.com/webhook";
        _store.Save(cfg);

        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        using var service = new RemoteNotificationService(_store, NullLogger<RemoteNotificationService>.Instance, client);

        var entry = new StartupEntry
        {
            Name = "MaliciousMiner.exe",
            FilePath = "C:\\Temp\\miner.exe",
            Location = EntryLocation.StartupFolder
        };

        await service.NotifyBlockedAsync(entry, "Заблокировано Чёрным списком");
        await Task.Delay(150);

        Assert.Equal(2, handler.SentRequests.Count);
        Assert.Contains(handler.SentRequests, r => r.RequestUri!.ToString().Contains("api.telegram.org"));
        Assert.Contains(handler.SentRequests, r => r.RequestUri!.ToString().Contains("discord.com"));

        var discordBody = handler.SentBodies.First(b => b.Contains("embeds"));
        Assert.Contains("15158332", discordBody); // Red color 0xE74C3C
    }

    [Fact]
    public async Task Notify_WhenChannelsDisabled_DoesNotSendRequests()
    {
        var cfg = _store.Load();
        cfg.Settings.TelegramEnabled = false;
        cfg.Settings.DiscordWebhookEnabled = false;
        _store.Save(cfg);

        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        using var service = new RemoteNotificationService(_store, NullLogger<RemoteNotificationService>.Instance, client);

        var entry = new StartupEntry { Name = "App", FilePath = "C:\\app.exe" };
        await service.NotifyNewEntryAsync(entry);
        await Task.Delay(50);

        Assert.Empty(handler.SentRequests);
    }

    [Fact]
    public async Task Notify_WhenEventToggledOff_SuppressesNotification()
    {
        var cfg = _store.Load();
        cfg.Settings.TelegramEnabled = true;
        cfg.Settings.TelegramBotToken = "T";
        cfg.Settings.TelegramChatId = "C";
        cfg.Settings.RemoteNotifyOnNewEntry = false; // toggled off!
        _store.Save(cfg);

        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        using var service = new RemoteNotificationService(_store, NullLogger<RemoteNotificationService>.Instance, client);

        var entry = new StartupEntry { Name = "App", FilePath = "C:\\app.exe" };
        await service.NotifyNewEntryAsync(entry);
        await Task.Delay(50);

        Assert.Empty(handler.SentRequests);
    }

    [Fact]
    public async Task TestTelegramAsync_SuccessAndFailureHandling()
    {
        var cfg = _store.Load();
        _store.Save(cfg);

        var handler = new MockHttpMessageHandler { StatusCodeToReturn = HttpStatusCode.OK };
        var client = new HttpClient(handler);
        using var service = new RemoteNotificationService(_store, NullLogger<RemoteNotificationService>.Instance, client);

        // Success test
        var (ok, msg) = await service.TestTelegramAsync("TOKEN", "CHAT");
        Assert.True(ok);
        Assert.Contains("успешно", msg);

        // Error test (e.g. 401 Unauthorized)
        handler.StatusCodeToReturn = HttpStatusCode.Unauthorized;
        handler.ResponseBodyToReturn = "{\"description\":\"Unauthorized\"}";
        var (failOk, failMsg) = await service.TestTelegramAsync("TOKEN", "CHAT");
        Assert.False(failOk);
        Assert.Contains("Unauthorized", failMsg);
    }

    [Fact]
    public async Task TestDiscordAsync_SuccessAndFailureHandling()
    {
        var cfg = _store.Load();
        _store.Save(cfg);

        var handler = new MockHttpMessageHandler { StatusCodeToReturn = HttpStatusCode.NoContent };
        var client = new HttpClient(handler);
        using var service = new RemoteNotificationService(_store, NullLogger<RemoteNotificationService>.Instance, client);

        // Success test
        var (ok, msg) = await service.TestDiscordAsync("https://discord.com/api/webhooks/test");
        Assert.True(ok);
        Assert.Contains("успешно", msg);

        // Error test (e.g. 404 Not Found)
        handler.StatusCodeToReturn = HttpStatusCode.NotFound;
        handler.ResponseBodyToReturn = "{\"message\": \"Unknown Webhook\"}";
        var (failOk, failMsg) = await service.TestDiscordAsync("https://discord.com/api/webhooks/bad");
        Assert.False(failOk);
        Assert.Contains("Unknown Webhook", failMsg);
    }

    [Fact]
    public void RemoteNotificationPayloads_Serialization_RoundTrip()
    {
        var payload = new RemoteNotificationConfigPayload(
            TelegramEnabled: true,
            TelegramBotToken: "tok123",
            TelegramChatId: "chat456",
            DiscordWebhookEnabled: true,
            DiscordWebhookUrl: "https://hook.url",
            RemoteNotifyOnNewEntry: true,
            RemoteNotifyOnThreat: true,
            RemoteNotifyOnBlocked: false
        );

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<RemoteNotificationConfigPayload>(json);

        Assert.NotNull(restored);
        Assert.True(restored.TelegramEnabled);
        Assert.Equal("tok123", restored.TelegramBotToken);
        Assert.Equal("chat456", restored.TelegramChatId);
        Assert.True(restored.DiscordWebhookEnabled);
        Assert.Equal("https://hook.url", restored.DiscordWebhookUrl);
        Assert.False(restored.RemoteNotifyOnBlocked);

        var testReq = new TestRemoteNotificationPayload("telegram");
        var testReqJson = JsonSerializer.Serialize(testReq);
        var restoredReq = JsonSerializer.Deserialize<TestRemoteNotificationPayload>(testReqJson);
        Assert.Equal("telegram", restoredReq?.Channel);

        var testRes = new TestRemoteNotificationResultPayload(true, "Готово");
        var testResJson = JsonSerializer.Serialize(testRes);
        var restoredRes = JsonSerializer.Deserialize<TestRemoteNotificationResultPayload>(testResJson);
        Assert.True(restoredRes?.Success);
        Assert.Equal("Готово", restoredRes?.Message);
    }
}
