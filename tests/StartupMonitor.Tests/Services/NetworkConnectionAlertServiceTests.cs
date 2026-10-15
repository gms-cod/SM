using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты детектора новых сетевых подключений
/// (<see cref="NetworkConnectionAlertService"/>): фильтрация частных адресов
/// и системных процессов, обработка решений AllowAlways / BlockAlways / AllowSession.
/// </summary>
public sealed class NetworkConnectionAlertServiceTests
{
    private const string EvilPath = @"C:\Apps\evil.exe";

    private static ProcessNetworkSocket Sock(
        int pid,
        string processName,
        string remoteAddress,
        int remotePort,
        string protocol = "TCP")
    {
        return new ProcessNetworkSocket
        {
            ProcessId = pid,
            ProcessName = processName,
            Protocol = protocol,
            LocalAddress = "192.168.1.10",
            LocalPort = 50000,
            RemoteAddress = remoteAddress,
            RemotePort = remotePort,
            State = "ESTABLISHED",
        };
    }

    private static NetworkConnectionAlertService CreateService(
        List<ProcessNetworkSocket>? sockets = null,
        Dictionary<int, string>? pathsByPid = null,
        Func<string, bool>? hasRule = null,
        List<FirewallRuleDto>? appliedRules = null)
    {
        var svc = new NetworkConnectionAlertService(
            new ProcessNetworkTracker(),
            new WindowsFirewallService())
        {
            SocketsProvider = () => sockets ?? new List<ProcessNetworkSocket>(),
            ProcessPathResolver = pid =>
                pathsByPid != null && pathsByPid.TryGetValue(pid, out var p) ? p : null,
            HasFirewallRuleOverride = hasRule ?? (_ => false),
            ApplyFirewallRuleOverride = rule => appliedRules?.Add(rule),
        };
        return svc;
    }

    private static Dictionary<int, string> Paths(params (int Pid, string Path)[] pairs) =>
        pairs.ToDictionary(p => p.Pid, p => p.Path);

    // ── Фильтрация частных адресов ──

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.5")]
    [InlineData("172.16.5.5")]
    [InlineData("169.254.10.20")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::1")]
    public void ScanNewConnections_PrivateAddresses_AreFiltered(string remote)
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", remote, 443) },
            Paths((1234, EvilPath)));

        var added = svc.ScanNewConnections();

        Assert.Equal(0, added);
        Assert.Equal(0, svc.PendingCount);
    }

    [Fact]
    public void ScanNewConnections_UdpListeningSocketWithoutRemote_IsFiltered()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "*", 0, "UDP") },
            Paths((1234, EvilPath)));

        Assert.Equal(0, svc.ScanNewConnections());
    }

    // ── Фильтрация системных процессов ──

    [Theory]
    [InlineData("System")]
    [InlineData("svchost.exe")]
    [InlineData("SVCHOST.EXE")]
    [InlineData("lsass.exe")]
    [InlineData("services.exe")]
    [InlineData("StartupMonitor.Service.exe")]
    [InlineData("StartupMonitor.UI.exe")]
    public void ScanNewConnections_SystemProcesses_AreIgnored(string processName)
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(4, processName, "8.8.8.8", 443) },
            Paths((4, @"C:\Windows\System32\" + processName.TrimEnd('m', 'M'))));

        var added = svc.ScanNewConnections();

        Assert.Equal(0, added);
        Assert.Empty(svc.GetPendingAlerts());
    }

    [Theory]
    [InlineData("svchost.exe", true)]
    [InlineData("SVCHOST.EXE", true)]
    [InlineData("System", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("evil.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrustedProcess_MatchesExpected(string? processName, bool expected)
    {
        Assert.Equal(expected, NetworkConnectionAlertService.IsTrustedProcess(processName, null));
    }

    [Fact]
    public void IsTrustedProcess_MatchesByProcessPathFileName()
    {
        Assert.True(NetworkConnectionAlertService.IsTrustedProcess(
            "unknown", @"C:\Windows\System32\svchost.exe"));
        Assert.False(NetworkConnectionAlertService.IsTrustedProcess(
            "unknown", @"C:\Apps\evil.exe"));
    }

    // ── Создание алертов ──

    [Fact]
    public void ScanNewConnections_PublicAddress_CreatesAlertWithFields()
    {
        var before = DateTime.UtcNow.AddMinutes(-1);
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)));

        var added = svc.ScanNewConnections();
        var pending = svc.GetPendingAlerts();

        Assert.Equal(1, added);
        var alert = Assert.Single(pending);
        Assert.False(string.IsNullOrWhiteSpace(alert.AlertId));
        Assert.Equal(1234, alert.ProcessId);
        Assert.Equal("evil.exe", alert.ProcessName);
        Assert.Equal(EvilPath, alert.ProcessPath);
        Assert.Equal("TCP", alert.Protocol);
        Assert.Equal("93.184.216.34", alert.RemoteAddress);
        Assert.Equal(443, alert.RemotePort);
        Assert.True(alert.DetectedAt >= before);
    }

    [Fact]
    public void ScanNewConnections_SameConnectionTwice_ProducesSingleAlert()
    {
        var sockets = new List<ProcessNetworkSocket>
        {
            Sock(1234, "evil.exe", "93.184.216.34", 443),
        };
        var svc = CreateService(sockets, Paths((1234, EvilPath)));

        Assert.Equal(1, svc.ScanNewConnections());
        Assert.Equal(0, svc.ScanNewConnections());
        Assert.Equal(1, svc.PendingCount);
    }

    [Fact]
    public void ScanNewConnections_DifferentRemotes_ProduceSeparateAlerts()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket>
            {
                Sock(1234, "evil.exe", "93.184.216.34", 443),
                Sock(1234, "evil.exe", "1.1.1.1", 80),
            },
            Paths((1234, EvilPath)));

        Assert.Equal(2, svc.ScanNewConnections());
        Assert.Equal(2, svc.GetPendingAlerts().Count);
    }

    [Fact]
    public void ScanNewConnections_ExistingFirewallRule_SkipsAlert()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            hasRule: _ => true);

        Assert.Equal(0, svc.ScanNewConnections());
        Assert.Empty(svc.GetPendingAlerts());
    }

    // ── Резолв алертов ──

    private static NetworkConnectionAlertDto ScanSingle(NetworkConnectionAlertService svc)
    {
        Assert.Equal(1, svc.ScanNewConnections());
        return Assert.Single(svc.GetPendingAlerts());
    }

    [Fact]
    public void ResolveAlert_AllowAlways_CreatesAllowRuleAndRemovesAlert()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        var alert = ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            RemoteAddress = "93.184.216.34",
            Resolution = NetworkAlertResolution.AllowAlways,
        });

        Assert.True(ok);
        Assert.Empty(svc.GetPendingAlerts());
        var rule = Assert.Single(applied);
        Assert.Equal(FirewallAction.Allow, rule.Action);
        Assert.Equal(EvilPath, rule.ApplicationPath);
        Assert.True(rule.Enabled);
        var key = NetworkConnectionAlertService.BuildSessionKey(EvilPath, "evil.exe");
        Assert.Equal(NetworkAlertResolution.AllowAlways, svc.SessionDecisions[key]);
    }

    [Fact]
    public void ResolveAlert_BlockAlways_CreatesBlockRuleAndRemovesAlert()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        var alert = ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            RemoteAddress = "93.184.216.34",
            Resolution = NetworkAlertResolution.BlockAlways,
        });

        Assert.True(ok);
        Assert.Empty(svc.GetPendingAlerts());
        var rule = Assert.Single(applied);
        Assert.Equal(FirewallAction.Block, rule.Action);
        Assert.Equal(EvilPath, rule.ApplicationPath);
        var key = NetworkConnectionAlertService.BuildSessionKey(EvilPath, "evil.exe");
        Assert.Equal(NetworkAlertResolution.BlockAlways, svc.SessionDecisions[key]);
    }

    [Fact]
    public void ResolveAlert_AllowSession_NoFirewallRule_SessionWhitelisted()
    {
        var applied = new List<FirewallRuleDto>();
        var sockets = new List<ProcessNetworkSocket>
        {
            Sock(1234, "evil.exe", "93.184.216.34", 443),
        };
        var svc = CreateService(sockets, Paths((1234, EvilPath)), appliedRules: applied);
        var alert = ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            RemoteAddress = "93.184.216.34",
            Resolution = NetworkAlertResolution.AllowSession,
        });

        Assert.True(ok);
        Assert.Empty(applied);
        Assert.Empty(svc.GetPendingAlerts());
        var key = NetworkConnectionAlertService.BuildSessionKey(EvilPath, "evil.exe");
        Assert.Equal(NetworkAlertResolution.AllowSession, svc.SessionDecisions[key]);

        // Повторный скан (включая новый remote того же процесса) алертов не даёт.
        sockets.Add(Sock(1234, "evil.exe", "9.9.9.9", 53));
        Assert.Equal(0, svc.ScanNewConnections());
        Assert.Empty(svc.GetPendingAlerts());
    }

    [Fact]
    public void ResolveAlert_AllowAlways_PreventsFutureAlerts()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        var alert = ScanSingle(svc);

        svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            Resolution = NetworkAlertResolution.AllowAlways,
        });

        Assert.Equal(0, svc.ScanNewConnections());
    }

    [Fact]
    public void ResolveAlert_UnknownId_ReturnsFalse()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = "nonexistent",
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            Resolution = NetworkAlertResolution.AllowAlways,
        });

        Assert.False(ok);
        Assert.Empty(applied);
        Assert.Equal(1, svc.PendingCount);
    }

    [Fact]
    public void ResolveAlert_NullOrEmptyRequest_ReturnsFalse()
    {
        var svc = CreateService();

        Assert.False(svc.ResolveAlert(null!));
        Assert.False(svc.ResolveAlert(new ResolveNetworkAlertRequest()));
    }

    // ── Включение/выключение ──

    [Fact]
    public void SetAlertsEnabled_Disabled_SkipsScanAndClearsPending()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)));

        Assert.True(svc.AlertsEnabled);
        Assert.Equal(1, svc.ScanNewConnections());

        svc.SetAlertsEnabled(false);

        Assert.False(svc.AlertsEnabled);
        Assert.Empty(svc.GetPendingAlerts());
        Assert.Equal(0, svc.ScanNewConnections());

        svc.SetAlertsEnabled(true);
        Assert.True(svc.AlertsEnabled);
    }

    [Fact]
    public void GetPendingAlerts_ReturnsSnapshotOrderedByDetectionTime()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket>
            {
                Sock(1111, "a.exe", "93.184.216.34", 443),
                Sock(2222, "b.exe", "1.1.1.1", 80),
            },
            Paths((1111, @"C:\Apps\a.exe"), (2222, @"C:\Apps\b.exe")));

        Assert.Equal(2, svc.ScanNewConnections());
        var pending = svc.GetPendingAlerts();

        Assert.Equal(2, pending.Count);
        Assert.True(pending[0].DetectedAt <= pending[1].DetectedAt);
    }
}
