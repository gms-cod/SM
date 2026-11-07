using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет стоимость сопоставления большого числа сокетов без настоящей сети и COM.</summary>
public sealed class NetworkAlertCollectionEfficiencyTests
{
    /// <summary>Много соединений одного PID требуют одного получения пути на снимок без долгоживущего PID-кэша.</summary>
    [Fact]
    public void RepeatedProcessConnections_ResolvePathOncePerSnapshot()
    {
        using var tracker = new ProcessNetworkTracker();
        using var service = new NetworkConnectionAlertService(tracker, new WindowsFirewallService(), sessionFirewall: new TestSessionFirewallPolicy());
        var calls = 0;
        service.SocketsProvider = () => Enumerable.Range(0, 100).Select(index => new ProcessNetworkSocket
        {
            ProcessId = 12345,
            ProcessName = "audit-app.exe",
            Protocol = "TCP",
            State = "ESTABLISHED",
            LocalAddress = "192.168.1.10",
            LocalPort = 40000 + index,
            RemoteAddress = "8.8.8.8",
            RemotePort = 443
        }).ToList();
        service.ProcessPathResolver = _ => { calls++; return @"C:\Apps\audit-app.exe"; };
        service.HasFirewallRuleOverride = _ => true;
        service.ScanNewConnections();
        Assert.Equal(1, calls);
        service.ScanNewConnections();
        Assert.Equal(2, calls);
    }
}
