namespace StartupMonitor.Tests.Services;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

/// <summary>
/// Модульные тесты для метода <see cref="AiRatingService.RateTrafficAnomalyAsync"/>.
/// Проверяют валидацию входных данных, кэширование по связке SHA256/IP/Port,
/// fallback на <see cref="StartupMonitor.Service.Services.Firewall.OfflineHeuristicEngine"/>
/// при отключенном LLM, сбоях сети и таймаутах.
/// </summary>
public sealed class AiRatingServiceAnomalyTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"sm-anomaly-tests-{Guid.NewGuid():N}");
    private readonly RatingsCache _cache;

    public AiRatingServiceAnomalyTests()
    {
        var store = new JsonDataStore(Path.Combine(_tempDir, "ratings.json"));
        _cache = new RatingsCache(store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private static IncidentTelemetryDto CreateIncident(
        string sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        string ip = "185.220.101.5",
        int port = 443,
        bool isBeaconing = false) => new()
    {
        IncidentId = "inc-unit-test",
        Process = new IncidentTelemetryDto.ProcessTelemetry
        {
            Pid = 4321,
            Name = "agent.exe",
            Path = @"C:\Program Files\App\agent.exe",
            Sha256 = sha256,
            Signer = "Valid Cert",
            IsSystem = false
        },
        Traffic = new IncidentTelemetryDto.TrafficTelemetry
        {
            RemoteAddress = ip,
            RemotePort = port,
            Direction = "Outbound",
            Protocol = "TCP",
            JitterPercent = 40.0,
            RiskScore = 15
        },
        Heuristics = new IncidentTelemetryDto.HeuristicsTelemetry
        {
            IsBeaconing = isBeaconing,
            IsNonStandardPort = false,
            IsUserWritablePath = false
        }
    };

    [Fact]
    public async Task RateTrafficAnomalyAsync_NullIncident_ThrowsArgumentNullException()
    {
        using var service = new AiRatingService(_cache, NullLogger<AiRatingService>.Instance, "key");
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RateTrafficAnomalyAsync(null!));
    }

    [Fact]
    public async Task RateTrafficAnomalyAsync_NullProcess_ThrowsArgumentNullException()
    {
        using var service = new AiRatingService(_cache, NullLogger<AiRatingService>.Instance, "key");
        var incident = CreateIncident();
        incident.Process = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RateTrafficAnomalyAsync(incident));
    }

    [Fact]
    public async Task RateTrafficAnomalyAsync_NoApiKeyAndRemoteUrl_FallsBackToOfflineHeuristic()
    {
        using var service = new AiRatingService(_cache, NullLogger<AiRatingService>.Instance, apiKey: "", apiUrl: "https://api.openai.com/v1/chat/completions");
        var incident = CreateIncident(port: 4444);

        var verdict = await service.RateTrafficAnomalyAsync(incident);

        Assert.NotNull(verdict);
        Assert.Equal("OfflineHeuristic", verdict.Source);
        Assert.Equal(5, verdict.RiskLevel);
        Assert.Equal("BlockAlways", verdict.RecommendedAction);
    }

    [Fact]
    public async Task RateTrafficAnomalyAsync_SuccessfulLlmResponse_ReturnsLlmVerdict()
    {
        var responseContent = """
        {
          "choices": [{
            "message": {
              "role": "assistant",
              "content": "{\"risk_level\": 4, \"risk_label\": \"C2 Beaconing\", \"user_explanation\": \"Обнаружен подозрительный трафик\", \"technical_summary\": \"Периодический маяк\", \"recommended_action\": \"BlockAlways\", \"action_reason\": \"Угроза эксфильтрации\"}"
            }
          }]
        }
        """;

        var handler = new MockHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        }));

        using var service = new AiRatingService(_cache, NullLogger<AiRatingService>.Instance, "valid-key",
            apiUrl: "https://api.groq.com/openai/v1/chat/completions", httpHandler: handler);

        var incident = CreateIncident();
        var verdict = await service.RateTrafficAnomalyAsync(incident);

        Assert.NotNull(verdict);
        Assert.Equal("LLM", verdict.Source);
        Assert.Equal(4, verdict.RiskLevel);
        Assert.Equal("BlockAlways", verdict.RecommendedAction);
        Assert.Equal("C2 Beaconing", verdict.RiskLabel);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RateTrafficAnomalyAsync_SecondCallSameIncident_ReturnsCachedVerdict()
    {
        var responseContent = """
        {
          "choices": [{
            "message": {
              "role": "assistant",
              "content": "{\"risk_level\": 3, \"risk_label\": \"Подозрительно\", \"user_explanation\": \"Тест\", \"technical_summary\": \"Тест\", \"recommended_action\": \"BlockSession\", \"action_reason\": \"Тест\"}"
            }
          }]
        }
        """;

        var handler = new MockHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        }));

        using var service = new AiRatingService(_cache, NullLogger<AiRatingService>.Instance, "valid-key",
            apiUrl: "https://api.groq.com/openai/v1/chat/completions", httpHandler: handler);

        var incident = CreateIncident();
        var firstVerdict = await service.RateTrafficAnomalyAsync(incident);
        Assert.Equal("LLM", firstVerdict.Source);
        Assert.Equal(1, handler.CallCount);

        var secondVerdict = await service.RateTrafficAnomalyAsync(incident);
        Assert.Equal("Cache", secondVerdict.Source);
        Assert.Equal(3, secondVerdict.RiskLevel);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RateTrafficAnomalyAsync_Http500Error_FallsBackToOfflineHeuristic()
    {
        var handler = new MockHttpHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        using var service = new AiRatingService(_cache, NullLogger<AiRatingService>.Instance, "valid-key",
            apiUrl: "https://api.groq.com/openai/v1/chat/completions", httpHandler: handler);

        var incident = CreateIncident(port: 1337);
        var verdict = await service.RateTrafficAnomalyAsync(incident);

        Assert.NotNull(verdict);
        Assert.Equal("OfflineHeuristic", verdict.Source);
        Assert.Equal(5, verdict.RiskLevel);
        Assert.Equal("BlockAlways", verdict.RecommendedAction);
    }

    private sealed class MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        public int CallCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return handlerFunc(request, cancellationToken);
        }
    }
}
