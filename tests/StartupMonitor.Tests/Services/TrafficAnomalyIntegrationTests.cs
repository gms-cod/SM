using System;
using System.Collections.Generic;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Интеграционные тесты для детекции сетевых аномалий (C2-маяков) и формирования телеметрии инцидентов.
/// </summary>
public sealed class TrafficAnomalyIntegrationTests
{
    private static ProcessNetworkSocket Sock(
        int pid,
        string processName,
        string remoteAddress,
        int remotePort,
        int localPort = 50000,
        string protocol = "TCP")
    {
        return new ProcessNetworkSocket
        {
            ProcessId = pid,
            ProcessName = processName,
            Protocol = protocol,
            LocalAddress = "192.168.1.10",
            LocalPort = localPort,
            RemoteAddress = remoteAddress,
            RemotePort = remotePort,
            State = "ESTABLISHED",
            Direction = "Outbound"
        };
    }

    /// <summary>
    /// Проверяет корректное создание IncidentTelemetryDto из наблюдения аномалии через фабричный метод.
    /// </summary>
    [Fact]
    public void IncidentTelemetryDto_FromObservation_BuildsAccurateTelemetry()
    {
        var now = new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
        var observation = new TrafficAnomalyObservation(
            ProcessId: 5432,
            ProcessName: "beacon.exe",
            RemoteAddress: "198.51.100.44",
            RemotePort: 4444,
            ObservationCount: 3,
            AverageIntervalSeconds: 15.0,
            JitterPercent: 4.5,
            AnomalyType: "Beaconing",
            RiskScore: 90,
            Protocol: "TCP",
            DetectedAt: now);

        var tempPath = @"C:\Users\Admin\AppData\Local\Temp\beacon.exe";
        var telemetry = IncidentTelemetryDto.FromObservation(observation, tempPath);

        Assert.NotNull(telemetry);
        Assert.False(string.IsNullOrWhiteSpace(telemetry.IncidentId));
        Assert.Equal(5432, telemetry.Process.Pid);
        Assert.Equal("beacon.exe", telemetry.Process.Name);
        Assert.Equal(tempPath, telemetry.Process.Path);
        Assert.Equal("198.51.100.44", telemetry.Traffic.RemoteAddress);
        Assert.Equal(4444, telemetry.Traffic.RemotePort);
        Assert.True(telemetry.Heuristics.IsBeaconing);
        Assert.True(telemetry.Heuristics.IsUserWritablePath);
        Assert.True(telemetry.Heuristics.IsNonStandardPort);
        Assert.Equal(90, telemetry.Heuristics.PreliminaryRiskScore);
    }

    /// <summary>
    /// Проверяет, что при регулярных исходящих подключениях процесс помечается как аномалия C2-маяка.
    /// </summary>
    [Fact]
    public void NetworkConnectionAlertService_DetectsBeaconingAnomaly_OnPeriodicConnections()
    {
        var currentTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var activeSockets = new List<ProcessNetworkSocket>();
        var paths = new Dictionary<int, string> { [2048] = @"C:\Users\User\Downloads\agent.exe" };

        var svc = new NetworkConnectionAlertService(
            new ProcessNetworkTracker(),
            new WindowsFirewallService(),
            store: null,
            sessionFirewall: new TestSessionFirewallPolicy())
        {
            SocketsProvider = () => new List<ProcessNetworkSocket>(activeSockets),
            ProcessPathResolver = pid => paths.TryGetValue(pid, out var p) ? p : null,
            ProcessInstanceResolver = pid => 133000000000000000L,
            UtcNowProvider = () => currentTime,
            HasFirewallRuleOverride = _ => false,
            HasApplicationPolicyOverride = (_, _) => false
        };

        // Требуется RequiredObservations = 5 наблюдений с интервалом >= 10 секунд
        for (int i = 0; i < 5; i++)
        {
            activeSockets.Clear();
            activeSockets.Add(Sock(2048, "agent.exe", "198.51.100.99", 4444, localPort: 51000 + i));
            svc.ScanNewConnections();
            currentTime = currentTime.AddSeconds(15);
        }

        var alerts = svc.GetPendingAlerts();
        Assert.Single(alerts);

        var alert = alerts[0];
        Assert.True(alert.IsAnomaly, "Алерт должен быть помечен как IsAnomaly = true");
        Assert.NotNull(alert.AnomalyDetails);
        Assert.Contains(alert.AnomalyDetails.AnomalyType, new[] { "Beaconing", "SuspiciousPort" });
        Assert.Equal(4444, alert.AnomalyDetails.RemotePort);
        Assert.Equal("198.51.100.99", alert.AnomalyDetails.RemoteAddress);
    }
}
