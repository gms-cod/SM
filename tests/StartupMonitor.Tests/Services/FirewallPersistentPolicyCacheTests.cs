using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет ограниченную стоимость подтверждения постоянной политики и её истечение при внешнем отзыве.</summary>
public sealed class FirewallPersistentPolicyCacheTests
{
    /// <summary>Тысяча сокетов не вызывает тысячу чтений правил; после TTL внешнее удаление возвращает запрос.</summary>
    [Fact]
    public void PersistentDecision_UsesBoundedRuleReadsAndRechecksExternalDeletion()
    {
        using var tracker = new ProcessNetworkTracker();
        using var service = new NetworkConnectionAlertService(tracker, new WindowsFirewallService(), sessionFirewall: new TestSessionFirewallPolicy());
        var now = DateTime.UtcNow;
        var sockets = new List<ProcessNetworkSocket> { Socket(40000) };
        var reads = 0;
        var present = true;
        service.UtcNowProvider = () => now;
        service.SocketsProvider = () => sockets;
        service.ProcessPathResolver = _ => @"C:\Apps\cache-check.exe";
        service.HasFirewallRuleOverride = _ => false;
        service.ApplyFirewallRuleOverride = _ => present = true;
        service.PersistentPolicyActionsProvider = _ => { reads++; return present ? [FirewallAction.Allow] : []; };
        service.ScanNewConnections();
        var alert = Assert.Single(service.GetPendingAlerts());
        Assert.True(service.ResolveAlert(new() { AlertId = alert.AlertId, Resolution = NetworkAlertResolution.AllowAlways }));
        sockets = Enumerable.Range(0, 1000).Select(i => Socket(41000 + i)).ToList();
        service.ScanNewConnections();
        service.ScanNewConnections();
        Assert.Equal(1, reads);
        Assert.Empty(service.GetPendingAlerts());
        present = false;
        now = now.AddSeconds(61);
        service.ScanNewConnections();
        Assert.Equal(2, reads);
        Assert.Single(service.GetPendingAlerts());
    }

    /// <summary>Создаёт независимую строку снимка с одним путём приложения и различимым локальным портом.</summary>
    private static ProcessNetworkSocket Socket(int port) => new()
    {
        ProcessId = 12345, ProcessName = "cache-check.exe", Protocol = "TCP", State = "ESTABLISHED",
        LocalAddress = "192.168.1.2", LocalPort = port, RemoteAddress = "8.8.8.8", RemotePort = 443
    };
}
