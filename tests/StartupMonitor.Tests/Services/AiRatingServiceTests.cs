using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
/// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
public class AiRatingServiceTests : IDisposable
{
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}");
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    private readonly AiRatingService _service;

    public AiRatingServiceTests()
    {
        var store = new JsonDataStore(Path.Combine(_dir, "startup-monitor.json"));
        var cache = new RatingsCache(store);
        _service = new AiRatingService(cache, NullLogger<AiRatingService>.Instance, "test-key");
    }

    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    private static string Wrap(string content) =>
        "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + content + "}}]}";

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_ValidJson_ReturnsRating()
    {
        var json = Wrap("\"{\\\"score\\\": 8, \\\"category\\\": \\\"malware\\\", \\\"risk\\\": \\\"miner\\\", \\\"recommendation\\\": \\\"block\\\"}\"");

        var rating = _service.ParseResponse(json, "test");

        Assert.NotNull(rating);
        Assert.Equal(8, rating.Score);
        Assert.Equal("malware", rating.Category);
        Assert.Equal("block", rating.Recommendation);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_MarkdownWrapped_Parses()
    {
        var json = Wrap("\"```json\\n{\\\"score\\\": 2, \\\"category\\\": \\\"system\\\", \\\"risk\\\": \\\"ok\\\", \\\"recommendation\\\": \\\"allow\\\"}\\n```\"");

        var rating = _service.ParseResponse(json, "test");

        Assert.NotNull(rating);
        Assert.Equal(2, rating.Score);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_Garbage_ReturnsNull()
    {
        var json = Wrap("\"Sorry, I cannot rate this.\"");

        Assert.Null(_service.ParseResponse(json, "test"));
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_NotOpenAiFormat_ReturnsNull()
    {
        Assert.Null(_service.ParseResponse("{\"error\":\"bad\"}", "test"));
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(15, 10)]
    [InlineData(-3, 1)]
    public void ParseResponse_ScoreClampedTo1_10(int input, int expected)
    {
        var json = Wrap($"\"{{\\\"score\\\": {input}, \\\"category\\\": \\\"x\\\", \\\"risk\\\": \\\"y\\\", \\\"recommendation\\\": \\\"allow\\\"}}\"");

        var rating = _service.ParseResponse(json, "test");

        Assert.NotNull(rating);
        Assert.Equal(expected, rating.Score);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_WindowsPathsWithBackslashes_ParsesCorrectly()
    {
        var rawContent = "{\"score\": 1, \"category\": \"system\", \"risk\": \"Файл C:\\Windows\\system32\\SecurityHealthSystray.exe в папке C:\\Users\\test безопасен\", \"recommendation\": \"allow\"}";
        var json = Wrap(System.Text.Json.JsonSerializer.Serialize(rawContent));

        var rating = _service.ParseResponse(json, "SecurityHealth");

        Assert.NotNull(rating);
        Assert.Equal(1, rating.Score);
        Assert.Equal("system", rating.Category);
        Assert.Contains("SecurityHealthSystray.exe", rating.Risk);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_AlreadyEscapedWindowsPaths_DoesNotBreak()
    {
        var rawContent = "{\"score\": 2, \"category\": \"utility\", \"risk\": \"Файл C:\\\\Program Files\\\\OpenVPN\\\\bin\\\\openvpn-gui.exe надежен\", \"recommendation\": \"allow\"}";
        var json = Wrap(System.Text.Json.JsonSerializer.Serialize(rawContent));

        var rating = _service.ParseResponse(json, "OpenVPN-GUI");

        Assert.NotNull(rating);
        Assert.Equal(2, rating.Score);
        Assert.Equal("utility", rating.Category);
        Assert.Contains("openvpn-gui.exe", rating.Risk);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void BuildPrompt_Russian_IncludesFullMetadataAndRussianInstructions()
    {
        var entry = new StartupEntry
        {
            Name = "TelegramUpdater",
            CommandLine = "\"C:\\Users\\User\\AppData\\Roaming\\Telegram\\Telegram.exe\" -autostart -minimized",
            FilePath = "C:\\Users\\User\\AppData\\Roaming\\Telegram\\Telegram.exe",
            Location = EntryLocation.RegistryRun,
            LocationDetail = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            Publisher = "Telegram FZ-LLC",
            SHA256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            Language = "ru",
            ThreatInfo = new CliThreatInfo
            {
                HasThreat = true,
                ThreatLevel = CliThreatLevel.Low,
                BadgeText = "Фоновый запуск",
                Description = "Запуск с параметром -minimized в трей",
                Indicators = new List<string> { "-minimized" }
            }
        };

        var prompt = _service.BuildPrompt(entry);

        Assert.Contains("СТРОГО НА РУССКОМ ЯЗЫКЕ", prompt);
        Assert.Contains("TelegramUpdater", prompt);
        Assert.Contains("-autostart -minimized", prompt);
        Assert.Contains("Telegram.exe", prompt);
        Assert.Contains("Telegram FZ-LLC", prompt);
        Assert.Contains("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", prompt);
        Assert.Contains("ЧТО ЭТО ЗА ЭЛЕМЕНТ", prompt);
        Assert.Contains("РАЗБОР КОМАНДНОЙ СТРОКИ", prompt);
        Assert.Contains("НЕОБХОДИМОСТЬ В АВТОЗАПУСКЕ", prompt);
        Assert.Contains("ВЕРДИКТ БЕЗОПАСНОСТИ", prompt);
        Assert.Contains("Фоновый запуск", prompt);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void BuildPrompt_English_IncludesFullMetadataAndEnglishInstructions()
    {
        _service.UpdateUiLang("en");
        var entry = new StartupEntry
        {
            Name = "DiscordClient",
            CommandLine = "\"C:\\Users\\User\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe",
            FilePath = "C:\\Users\\User\\AppData\\Local\\Discord\\Update.exe",
            Location = EntryLocation.RegistryRun,
            LocationDetail = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            Publisher = "Discord Inc.",
            SHA256 = "112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00"
        };

        var prompt = _service.BuildPrompt(entry);

        Assert.Contains("STRICTLY IN ENGLISH", prompt);
        Assert.Contains("DiscordClient", prompt);
        Assert.Contains("--processStart Discord.exe", prompt);
        Assert.Contains("Discord.exe", prompt);
        Assert.Contains("Discord Inc.", prompt);
        Assert.Contains("WHAT THIS ELEMENT IS", prompt);
        Assert.Contains("COMMAND LINE ANALYSIS", prompt);
        Assert.Contains("BOOT NECESSITY", prompt);
        Assert.Contains("SECURITY VERDICT", prompt);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void UpdateUiLang_SwitchesPromptLanguage()
    {
        var entry = new StartupEntry { Name = "TestApp" };

        _service.UpdateUiLang("ru");
        var ruPrompt = _service.BuildPrompt(entry);
        Assert.Contains("СТРОГО НА РУССКОМ ЯЗЫКЕ", ruPrompt);

        _service.UpdateUiLang("en");
        var enPrompt = _service.BuildPrompt(entry);
        Assert.Contains("STRICTLY IN ENGLISH", enPrompt);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_WhenContentEmpty_ExtractsFromReasoning()
    {
        var rawJson = "{\"score\": 3, \"category\": \"utility\", \"risk\": \"Легитимный процесс мониторинга\", \"recommendation\": \"allow\"}";
        var reasoningText = "Рассуждаю о безопасности... Вот итоговый вердикт:\n```json\n" + rawJson + "\n```";
        
        var responsePayload = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = "",
                        reasoning = reasoningText
                    }
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(responsePayload);
        var rating = _service.ParseResponse(json, "TestEntry");

        Assert.NotNull(rating);
        Assert.Equal(3, rating.Score);
        Assert.Equal("utility", rating.Category);
        Assert.Equal("allow", rating.Recommendation);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ParseResponse_WhenContentEmpty_ExtractsFromThought()
    {
        var rawJson = "{\"score\": 1, \"category\": \"system\", \"risk\": \"Системный файл Windows\", \"recommendation\": \"allow\"}";
        var thoughtText = "Думаю над назначением программы... Результат: " + rawJson;
        
        var responsePayload = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        thought = thoughtText
                    }
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(responsePayload);
        var rating = _service.ParseResponse(json, "SystemFile");

        Assert.NotNull(rating);
        Assert.Equal(1, rating.Score);
        Assert.Equal("system", rating.Category);
    }

    /// <summary>Verifies test scenario for AiRatingServiceTests.cs.</summary>
    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
