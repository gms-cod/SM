using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
public sealed class NetworkReliabilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NetworkReliability_" + Guid.NewGuid().ToString("N"));
    private readonly TestPortDefenderFirewall _firewall = new();
    private readonly ProcessNetworkTracker _network = new();
    private readonly NotificationService _notifications;
    private readonly JsonDataStore _store;
    private readonly ProcessPortDefenderService _defender;

    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    public NetworkReliabilityTests()
    {
        _store = new JsonDataStore(_directory);
        var scan = new InitialScanTracker();
        scan.MarkCompleted();
        _notifications = new NotificationService(scan);
        _defender = CreateDefender();
    }

    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    private ProcessPortDefenderService CreateDefender() => new(
        _firewall, _network, _notifications, _store,
        NullLogger<ProcessPortDefenderService>.Instance, startMonitoring: false);

    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    private void Configure(params PortDefenderRuleConfig[] rules) => _defender.SaveConfig(new PortDefenderConfigDto
    {
        IsGloballyEnabled = true,
        ProtectedPorts = [],
        Rules = [.. rules]
    });

    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    private static TcpSocketInfo Socket(int port = 8080, int remotePort = 40000) => new()
    {
        ProcessId = 123,
        LocalAddress = "192.0.2.1",
        LocalPort = port,
        RemoteAddress = "198.51.100.25",
        RemotePort = remotePort,
        State = "ESTABLISHED"
    };

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void HasWork_EnabledProtectionRemainsActiveWithoutUi()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080 });
        _defender.SetUiActive(false);
        Assert.True(_defender.HasWork());
        _defender.SaveConfig(new PortDefenderConfigDto { IsGloballyEnabled = false });
        Assert.False(_defender.HasWork());
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void DisabledRule_DoesNotReappearThroughProtectedPorts()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, IsEnabled = false, ThresholdCount = 1 });
        Assert.False(_defender.HasWork());
        _defender.ProcessSocketSnapshot(new[] { Socket(), Socket(remotePort: 40001) });
        Assert.Empty(_defender.GetActiveBans());
        Assert.Equal(0, _firewall.BlockCalls);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void DifferentPorts_DoNotShareRateCounter()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 2 },
            new PortDefenderRuleConfig { LocalPort = 9090, ThresholdCount = 2 });
        _defender.ProcessSocketSnapshot(new[] { Socket(), Socket(9090, 40001) });
        Assert.Empty(_defender.GetActiveBans());
        Assert.Equal(0, _firewall.BlockCalls);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void RulesOnSamePort_DoNotCountOneConnectionTwice()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 3, WindowUnit = TimeWindowUnit.Minute },
            new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 3, WindowUnit = TimeWindowUnit.Hour });
        _defender.ProcessSocketSnapshot(new[] { Socket(), Socket(remotePort: 40001) });
        Assert.Empty(_defender.GetActiveBans());
        _defender.ProcessSocketSnapshot(new[] { Socket(), Socket(remotePort: 40001), Socket(remotePort: 40002) });
        Assert.Single(_defender.GetActiveBans());
        Assert.Equal(1, _firewall.BlockCalls);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void EmptySnapshot_ResetsConnectionDeduplication()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 2 });
        _defender.ProcessSocketSnapshot(new[] { Socket() });
        _defender.ProcessSocketSnapshot(Array.Empty<TcpSocketInfo>());
        _defender.ProcessSocketSnapshot(new[] { Socket() });
        Assert.Single(_defender.GetActiveBans());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void FailedBlock_DoesNotRegisterBanAndRetriesUnchangedSnapshot(bool throwException)
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 1 });
        _firewall.BlockSucceeds = false;
        _firewall.ThrowOnBlock = throwException;
        _defender.ProcessSocketSnapshot(new[] { Socket() });
        Assert.Empty(_defender.GetActiveBans());
        _firewall.BlockSucceeds = true;
        _firewall.ThrowOnBlock = false;
        _defender.ProcessSocketSnapshot(new[] { Socket() });
        var ban = Assert.Single(_defender.GetActiveBans());
        Assert.Equal(1, ban.RepeatCount);
        Assert.Equal(2, _firewall.BlockCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void FailedUnblock_PreservesBanForRetry(bool throwException)
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 1 });
        _defender.ProcessSocketSnapshot(new[] { Socket() });
        _firewall.UnblockSucceeds = false;
        _firewall.ThrowOnUnblock = throwException;
        Assert.False(_defender.UnbanIp("198.51.100.25"));
        Assert.Single(_defender.GetActiveBans());
        Assert.Equal(0, _defender.ClearAllBans());
        Assert.Single(_defender.GetActiveBans());
        _firewall.UnblockSucceeds = true;
        _firewall.ThrowOnUnblock = false;
        Assert.True(_defender.UnbanIp("198.51.100.25"));
        Assert.Empty(_defender.GetActiveBans());
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ExpiredBan_FailedStartupUnblockSurvivesAndAutoUnbanRetries()
    {
        _defender.Dispose();
        File.WriteAllText(Path.Combine(_directory, "firewall-bans.json"), JsonSerializer.Serialize(new[]
        {
            new ActiveBanDto { Ip = "198.51.100.25", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1), RepeatCount = 1 }
        }));
        _firewall.UnblockSucceeds = false;
        using var restarted = CreateDefender();
        Assert.Single(restarted.GetActiveBans());
        Assert.Equal(0, restarted.ProcessExpiredBans());
        _firewall.UnblockSucceeds = true;
        Assert.Equal(1, restarted.ProcessExpiredBans());
        Assert.Empty(restarted.GetActiveBans());
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void SavedConfiguration_IsIndependentFromCallerMutations()
    {
        var rule = new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 1 };
        Configure(rule);
        rule.IsEnabled = false;
        rule.WhitelistIps.Add("198.51.100.25");
        _defender.ProcessSocketSnapshot(new[] { Socket() });
        Assert.Single(_defender.GetActiveBans());
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ReturnedBan_IsIndependentFromInternalState()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 1 });
        _defender.ProcessSocketSnapshot(new[] { Socket() });
        var returned = Assert.Single(_defender.GetActiveBans());
        returned.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        Assert.Equal(0, _defender.ProcessExpiredBans());
        Assert.Single(_defender.GetActiveBans());
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void FullPathRule_DoesNotMatchDifferentApplicationWithSameFileName()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 1, ProcessPath = @"C:\A\app.exe" });
        var wrongApplication = Socket();
        wrongApplication.ProcessName = "app.exe";
        wrongApplication.ProcessPath = @"C:\B\app.exe";
        _defender.ProcessSocketSnapshot(new[] { wrongApplication });
        Assert.Empty(_defender.GetActiveBans());
        var correctApplication = Socket(remotePort: 40001);
        correctApplication.ProcessPath = @"C:\A\app.exe";
        _defender.ProcessSocketSnapshot(new[] { correctApplication });
        Assert.Single(_defender.GetActiveBans());
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void UdpSocket_IsNotCountedAsIncomingTcpConnection()
    {
        Configure(new PortDefenderRuleConfig { LocalPort = 8080, ThresholdCount = 1 });
        var udp = Socket();
        udp.Protocol = "UDP";
        _defender.ProcessSocketSnapshot(new[] { udp });
        Assert.Empty(_defender.GetActiveBans());
        Assert.Equal(0, _firewall.BlockCalls);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void PortRuleIdentity_UsesFullPathAndIsCaseInsensitive()
    {
        var first = WindowsFirewallService.PortRuleName(8080, FirewallProtocol.Tcp, 1, @"C:\A\app.exe");
        var second = WindowsFirewallService.PortRuleName(8080, FirewallProtocol.Tcp, 1, @"C:\B\app.exe");
        var same = WindowsFirewallService.PortRuleName(8080, FirewallProtocol.Tcp, 1, @"c:\a\APP.EXE");
        Assert.NotEqual(first, second);
        Assert.Equal(first, same, ignoreCase: true);
        Assert.Equal("StartupMonitor_Block_Port_Tcp_8080_In_System", WindowsFirewallService.PortRuleName(8080, FirewallProtocol.Tcp, 1, null));
    }

    /// <summary>Verifies test scenario for NetworkReliabilityTests.cs.</summary>
    public void Dispose()
    {
        _defender.Dispose();
        _network.Dispose();
        _notifications.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
