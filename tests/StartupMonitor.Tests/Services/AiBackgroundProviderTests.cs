using StartupMonitor.Service.Services.Ai;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет доступность фоновой оценки для локальных и удалённых AI-провайдеров.</summary>
public sealed class AiBackgroundProviderTests
{
    /// <summary>Локальный адрес допускается без ключа, удалённый требует заданного ключа.</summary>
    [Theory]
    [InlineData("", "http://localhost:11434/v1/chat/completions", true)]
    [InlineData("", "http://127.0.0.1:11434/v1/chat/completions", true)]
    [InlineData("", "http://127.5.4.3:11434/v1", true)]
    [InlineData("", "http://[::1]:11434/v1", true)]
    [InlineData("", "http://0.0.0.0:11434/v1", true)]
    [InlineData("", "https://example.com/v1/chat/completions", false)]
    [InlineData("", "https://localhost.example.com/v1", false)]
    [InlineData("", "https://example.com/localhost", false)]
    [InlineData("", "https://localhost@example.com/v1", false)]
    [InlineData("", "file://localhost/path", false)]
    [InlineData("key", "https://example.com/v1/chat/completions", true)]
    [InlineData(" ", "", false)]
    public void CanRequestRatings_RequiresKeyUnlessProviderIsLocal(string key, string url, bool expected)
    {
        Assert.Equal(expected, AiRatingService.CanRequestRatings(key, url));
    }
}
