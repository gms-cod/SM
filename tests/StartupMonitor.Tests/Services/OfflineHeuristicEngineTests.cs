namespace StartupMonitor.Tests.Services;

using System;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

/// <summary>
/// Модульные тесты для автономного эвристического движка <see cref="OfflineHeuristicEngine"/>.
/// Проверяют корректность оценки уровней рисков, рекомендуемых действий и обработки граничных случаев.
/// </summary>
public sealed class OfflineHeuristicEngineTests
{
    /// <summary>
    /// Создает валидный базовый объект телеметрии инцидента с нейтральными показателями.
    /// </summary>
    private static IncidentTelemetryDto CreateValidIncident() => new()
    {
        IncidentId = "inc-test-001",
        Process = new IncidentTelemetryDto.ProcessTelemetry
        {
            Pid = 1234,
            Name = "chrome.exe",
            Path = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            Signer = "Google LLC",
            IsSystem = false
        },
        Traffic = new IncidentTelemetryDto.TrafficTelemetry
        {
            RemoteAddress = "142.250.185.206",
            RemotePort = 443,
            JitterPercent = 45.0,
            RiskScore = 10
        },
        Heuristics = new IncidentTelemetryDto.HeuristicsTelemetry
        {
            IsBeaconing = false,
            IsNonStandardPort = false,
            IsUserWritablePath = false
        }
    };

    [Fact]
    public void Evaluate_NullIncident_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OfflineHeuristicEngine.Evaluate(null!));
    }

    [Fact]
    public void Evaluate_NullProcess_ThrowsArgumentNullException()
    {
        var incident = CreateValidIncident();
        incident.Process = null!;
        Assert.Throws<ArgumentNullException>(() => OfflineHeuristicEngine.Evaluate(incident));
    }

    [Fact]
    public void Evaluate_NullTraffic_ThrowsArgumentNullException()
    {
        var incident = CreateValidIncident();
        incident.Traffic = null!;
        Assert.Throws<ArgumentNullException>(() => OfflineHeuristicEngine.Evaluate(incident));
    }

    [Fact]
    public void Evaluate_NullHeuristics_ThrowsArgumentNullException()
    {
        var incident = CreateValidIncident();
        incident.Heuristics = null!;
        Assert.Throws<ArgumentNullException>(() => OfflineHeuristicEngine.Evaluate(incident));
    }

    [Theory]
    [InlineData(4444)]
    [InlineData(1337)]
    [InlineData(31337)]
    [InlineData(6667)]
    [InlineData(8088)]
    [InlineData(8888)]
    [InlineData(9999)]
    public void Evaluate_KnownC2Port_ReturnsRiskLevel5BlockAlways(int port)
    {
        var incident = CreateValidIncident();
        incident.Traffic.RemotePort = port;

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(5, verdict.RiskLevel);
        Assert.Contains("Критическая угроза", verdict.RiskLabel);
        Assert.Equal("BlockAlways", verdict.RecommendedAction);
        Assert.Equal("OfflineHeuristic", verdict.Source);
        Assert.False(string.IsNullOrWhiteSpace(verdict.UserExplanation));
    }

    [Fact]
    public void Evaluate_BeaconingUnsignedUserWritable_ReturnsRiskLevel5BlockAlways()
    {
        var incident = CreateValidIncident();
        incident.Process.Signer = "Unsigned";
        incident.Process.Path = @"C:\Users\User\AppData\Local\Temp\evil.exe";
        incident.Heuristics.IsBeaconing = true;
        incident.Heuristics.IsUserWritablePath = true;

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(5, verdict.RiskLevel);
        Assert.Equal("BlockAlways", verdict.RecommendedAction);
        Assert.Contains("UserWritablePath=True", verdict.TechnicalSummary);
    }

    [Fact]
    public void Evaluate_BeaconingLowJitterNonSystem_ReturnsRiskLevel4BlockAlways()
    {
        var incident = CreateValidIncident();
        incident.Process.IsSystem = false;
        incident.Heuristics.IsBeaconing = true;
        incident.Traffic.JitterPercent = 8.5; // <= 15.0%

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(4, verdict.RiskLevel);
        Assert.Contains("Высокий риск", verdict.RiskLabel);
        Assert.Equal("BlockAlways", verdict.RecommendedAction);
    }

    [Fact]
    public void Evaluate_NonStandardPort_ReturnsRiskLevel3BlockSession()
    {
        var incident = CreateValidIncident();
        incident.Heuristics.IsNonStandardPort = true;

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(3, verdict.RiskLevel);
        Assert.Contains("Подозрительная активность", verdict.RiskLabel);
        Assert.Equal("BlockSession", verdict.RecommendedAction);
    }

    [Fact]
    public void Evaluate_HighTrafficRiskScore_ReturnsRiskLevel3BlockSession()
    {
        var incident = CreateValidIncident();
        incident.Traffic.RiskScore = 75; // >= 60

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(3, verdict.RiskLevel);
        Assert.Equal("BlockSession", verdict.RecommendedAction);
    }

    [Fact]
    public void Evaluate_SystemProcess_ReturnsRiskLevel1AllowAlways()
    {
        var incident = CreateValidIncident();
        incident.Process.IsSystem = true;

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(1, verdict.RiskLevel);
        Assert.Contains("Безопасно", verdict.RiskLabel);
        Assert.Equal("AllowAlways", verdict.RecommendedAction);
    }

    [Fact]
    public void Evaluate_TrustedSignedNonBeacon_ReturnsRiskLevel1AllowAlways()
    {
        var incident = CreateValidIncident();
        incident.Process.IsSystem = false;
        incident.Process.Signer = "Microsoft Corporation";
        incident.Heuristics.IsBeaconing = false;
        incident.Heuristics.IsUserWritablePath = false;

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(1, verdict.RiskLevel);
        Assert.Equal("AllowAlways", verdict.RecommendedAction);
    }

    [Fact]
    public void Evaluate_DefaultApplication_ReturnsRiskLevel2AllowSession()
    {
        var incident = CreateValidIncident();
        incident.Process.IsSystem = false;
        incident.Process.Signer = "Unsigned"; // не подписан, но не в Temp и не маячит
        incident.Heuristics.IsBeaconing = false;
        incident.Heuristics.IsUserWritablePath = false;
        incident.Heuristics.IsNonStandardPort = false;
        incident.Traffic.RiskScore = 20;

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.NotNull(verdict);
        Assert.Equal(2, verdict.RiskLevel);
        Assert.Contains("Низкий риск", verdict.RiskLabel);
        Assert.Equal("AllowSession", verdict.RecommendedAction);
    }

    [Fact]
    public void Evaluate_PreservesIncidentIdAndSource()
    {
        var incident = CreateValidIncident();
        incident.IncidentId = "custom-incident-id-999";

        var verdict = OfflineHeuristicEngine.Evaluate(incident);

        Assert.Equal("custom-incident-id-999", verdict.IncidentId);
        Assert.Equal("OfflineHeuristic", verdict.Source);
        Assert.True((DateTime.UtcNow - verdict.EvaluatedAt).TotalSeconds < 5);
    }
}
