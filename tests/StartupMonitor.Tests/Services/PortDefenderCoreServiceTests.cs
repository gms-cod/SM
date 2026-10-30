using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

public sealed class PortDefenderCoreServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonDataStore _store;
    private readonly InitialScanTracker _scanTracker;
    private readonly NotificationService _notificationService;
    private readonly WindowsFirewallService _firewallService;
    private readonly ProcessNetworkTracker _networkTracker;
    private readonly ProcessPortDefenderService _defender;

    public PortDefenderCoreServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PortDefenderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _store = new JsonDataStore(_tempDir);
        _scanTracker = new InitialScanTracker();
        _scanTracker.MarkCompleted();
        _notificationService = new NotificationService(_scanTracker);
        _firewallService = new WindowsFirewallService();
        _networkTracker = new ProcessNetworkTracker();

        _defender = new ProcessPortDefenderService(
            _firewallService,
            _networkTracker,
            _notificationService,
            _store,
            NullLogger<ProcessPortDefenderService>.Instance);
    }

    [Fact]
    public void Config_GetAndSave_RoundtripsSuccessfully()
    {
        var config = new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            DefaultBanDurationMinutes = 30,
            Rules = new List<PortDefenderRuleConfig>
            {
                new()
                {
                    LocalPort = 3389,
                    ProcessPath = "mstsc.exe",
                    IsEnabled = true,
                    ThresholdCount = 5,
                    WindowUnit = TimeWindowUnit.Minute,
                    BanDurationMinutes = 60,
                    AutoEscalate = true,
                    WhitelistIps = new List<string> { "192.168.1.100", "10.0.0.0/8" }
                }
            }
        };

        _defender.SaveConfig(config);
        var loaded = _defender.GetConfig();

        Assert.True(loaded.IsGloballyEnabled);
        Assert.Equal(30, loaded.DefaultBanDurationMinutes);
        Assert.Single(loaded.Rules);
        Assert.Equal(3389, loaded.Rules[0].LocalPort);
        Assert.Equal("mstsc.exe", loaded.Rules[0].ProcessPath);
        Assert.Equal(5, loaded.Rules[0].ThresholdCount);
        Assert.Equal(TimeWindowUnit.Minute, loaded.Rules[0].WindowUnit);
        Assert.Equal(60, loaded.Rules[0].BanDurationMinutes);
        Assert.Contains("192.168.1.100", loaded.Rules[0].WhitelistIps);
    }

    [Fact]
    public void ProcessSocketSnapshot_FloodOnProtectedPort_TriggersBan()
    {
        var rule = new PortDefenderRuleConfig
        {
            LocalPort = 8080,
            IsEnabled = true,
            ThresholdCount = 3,
            WindowUnit = TimeWindowUnit.Minute,
            BanDurationMinutes = 15,
            AutoEscalate = false
        };

        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            Rules = new List<PortDefenderRuleConfig> { rule }
        });

        var attackingIp = "198.51.100.25"; // Допустимый публичный IP (RFC 5737 TEST-NET-2)

        // Имитируем 3 входящих соединения с разных удалённых портов
        var sockets = new List<TcpSocketInfo>
        {
            new() { LocalPort = 8080, RemoteAddress = attackingIp, RemotePort = 40001, State = "ESTABLISHED" },
            new() { LocalPort = 8080, RemoteAddress = attackingIp, RemotePort = 40002, State = "ESTABLISHED" },
            new() { LocalPort = 8080, RemoteAddress = attackingIp, RemotePort = 40003, State = "ESTABLISHED" }
        };

        _defender.ProcessSocketSnapshot(sockets);

        var bans = _defender.GetActiveBans();
        Assert.Contains(bans, b => b.Ip == attackingIp && b.TargetPort == 8080);
    }

    [Fact]
    public void ProcessSocketSnapshot_WhitelistedIp_IsNeverBanned()
    {
        var trustedIp = "192.168.1.50";
        var rule = new PortDefenderRuleConfig
        {
            LocalPort = 443,
            IsEnabled = true,
            ThresholdCount = 2,
            WindowUnit = TimeWindowUnit.Minute,
            WhitelistIps = new List<string> { trustedIp, "10.0.0.0/8" }
        };

        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            Rules = new List<PortDefenderRuleConfig> { rule }
        });

        var sockets = new List<TcpSocketInfo>
        {
            new() { LocalPort = 443, RemoteAddress = trustedIp, RemotePort = 50001, State = "ESTABLISHED" },
            new() { LocalPort = 443, RemoteAddress = trustedIp, RemotePort = 50002, State = "ESTABLISHED" },
            new() { LocalPort = 443, RemoteAddress = trustedIp, RemotePort = 50003, State = "ESTABLISHED" }
        };

        _defender.ProcessSocketSnapshot(sockets);

        var bans = _defender.GetActiveBans();
        Assert.DoesNotContain(bans, b => b.Ip == trustedIp);
    }

    [Fact]
    public void ProcessSocketSnapshot_LoopbackIp_IsRejectedByGuard()
    {
        var rule = new PortDefenderRuleConfig
        {
            LocalPort = 22,
            IsEnabled = true,
            ThresholdCount = 1
        };

        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            Rules = new List<PortDefenderRuleConfig> { rule }
        });

        var sockets = new List<TcpSocketInfo>
        {
            new() { LocalPort = 22, RemoteAddress = "127.0.0.1", RemotePort = 22001, State = "ESTABLISHED" }
        };

        _defender.ProcessSocketSnapshot(sockets);

        var bans = _defender.GetActiveBans();
        Assert.Empty(bans);
    }

    [Fact]
    public void UnbanAndClearAllBans_RemovesBansCorrectly()
    {
        var rule = new PortDefenderRuleConfig
        {
            LocalPort = 3389,
            IsEnabled = true,
            ThresholdCount = 1
        };

        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            Rules = new List<PortDefenderRuleConfig> { rule }
        });

        var ip = "203.0.113.10";
        _defender.ProcessSocketSnapshot(new List<TcpSocketInfo>
        {
            new() { LocalPort = 3389, RemoteAddress = ip, RemotePort = 55555, State = "ESTABLISHED" }
        });

        Assert.NotEmpty(_defender.GetActiveBans());

        // Снятие конкретного бана
        _defender.UnbanIp(ip);
        Assert.Empty(_defender.GetActiveBans());
    }

    [Fact]
    public void EscalationFormula_ComputesExpectedDurations()
    {
        // 1-й: base (15м)
        Assert.Equal(15, ProcessPortDefenderService.CalculateEscalatedDuration(15, 1, true));
        // 2-й: 30м
        Assert.Equal(30, ProcessPortDefenderService.CalculateEscalatedDuration(15, 2, true));
        // 3-й: 60м (1ч)
        Assert.Equal(60, ProcessPortDefenderService.CalculateEscalatedDuration(15, 3, true));
        // 4-й: 1440м (24ч)
        Assert.Equal(1440, ProcessPortDefenderService.CalculateEscalatedDuration(15, 4, true));
        // 5-й+: 0 (Permanent)
        Assert.Equal(0, ProcessPortDefenderService.CalculateEscalatedDuration(15, 5, true));
        Assert.Equal(0, ProcessPortDefenderService.CalculateEscalatedDuration(15, 10, true));

        // Без эскалации: всегда base
        Assert.Equal(15, ProcessPortDefenderService.CalculateEscalatedDuration(15, 5, false));
    }

    [Fact]
    public void WhitelistMatching_SupportsExactCidrAndWildcard()
    {
        var list = new List<string> { "192.168.1.100", "10.0.0.0/8", "172.16.5.*" };

        Assert.True(ProcessPortDefenderService.IsIpWhitelisted("192.168.1.100", list));
        Assert.False(ProcessPortDefenderService.IsIpWhitelisted("192.168.1.101", list));

        Assert.True(ProcessPortDefenderService.IsIpWhitelisted("10.5.20.1", list));
        Assert.True(ProcessPortDefenderService.IsIpWhitelisted("10.254.254.254", list));
        Assert.False(ProcessPortDefenderService.IsIpWhitelisted("11.0.0.1", list));

        Assert.True(ProcessPortDefenderService.IsIpWhitelisted("172.16.5.99", list));
        Assert.False(ProcessPortDefenderService.IsIpWhitelisted("172.16.6.99", list));
    }

    /// <summary>
    /// Проверяет, что порты из ProtectedPorts без явного списка Rules успешно отслеживаются и блокируют атаки.
    /// </summary>
    [Fact]
    public void ProcessSocketSnapshot_ProtectedPortsWithoutExplicitRule_TriggersBan()
    {
        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            ProtectedPorts = new List<int> { 8088 },
            MaxAttempts = 3,
            WindowSeconds = 60,
            Rules = new List<PortDefenderRuleConfig>()
        });

        var attackingIp = "198.51.100.99";
        var sockets = new List<TcpSocketInfo>
        {
            new() { LocalPort = 8088, RemoteAddress = attackingIp, RemotePort = 41001, State = "ESTABLISHED" },
            new() { LocalPort = 8088, RemoteAddress = attackingIp, RemotePort = 41002, State = "ESTABLISHED" },
            new() { LocalPort = 8088, RemoteAddress = attackingIp, RemotePort = 41003, State = "ESTABLISHED" }
        };

        _defender.ProcessSocketSnapshot(sockets);

        var bans = _defender.GetActiveBans();
        Assert.Contains(bans, b => b.Ip == attackingIp && b.TargetPort == 8088);
    }

    /// <summary>
    /// Проверяет, что при удалении порта из ProtectedPorts он немедленно перестает отслеживаться.
    /// </summary>
    [Fact]
    public void SaveConfig_PortRemovedFromProtectedPorts_ImmediatelyStopsMonitoring()
    {
        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            ProtectedPorts = new List<int> { 9099 },
            MaxAttempts = 2,
            WindowSeconds = 60
        });

        // Удаляем порт 9099 и оставляем только 3389
        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            ProtectedPorts = new List<int> { 3389 },
            MaxAttempts = 2,
            WindowSeconds = 60
        });

        var attackingIp = "198.51.100.120";
        var sockets = new List<TcpSocketInfo>
        {
            new() { LocalPort = 9099, RemoteAddress = attackingIp, RemotePort = 42001, State = "ESTABLISHED" },
            new() { LocalPort = 9099, RemoteAddress = attackingIp, RemotePort = 42002, State = "ESTABLISHED" },
            new() { LocalPort = 9099, RemoteAddress = attackingIp, RemotePort = 42003, State = "ESTABLISHED" }
        };

        _defender.ProcessSocketSnapshot(sockets);

        var bans = _defender.GetActiveBans();
        Assert.DoesNotContain(bans, b => b.Ip == attackingIp && b.TargetPort == 9099);
    }

    /// <summary>
    /// Проверяет, что при изменении порта в ProtectedPorts новый порт немедленно защищается.
    /// </summary>
    [Fact]
    public void SaveConfig_PortChangedInProtectedPorts_ImmediatelyProtectsNewPort()
    {
        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            ProtectedPorts = new List<int> { 7070 },
            MaxAttempts = 2,
            WindowSeconds = 60
        });

        // Заменяем 7070 на 7071
        _defender.SaveConfig(new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            ProtectedPorts = new List<int> { 7071 },
            MaxAttempts = 2,
            WindowSeconds = 60
        });

        var attackingIp = "198.51.100.130";
        var sockets = new List<TcpSocketInfo>
        {
            new() { LocalPort = 7071, RemoteAddress = attackingIp, RemotePort = 43001, State = "ESTABLISHED" },
            new() { LocalPort = 7071, RemoteAddress = attackingIp, RemotePort = 43002, State = "ESTABLISHED" }
        };

        _defender.ProcessSocketSnapshot(sockets);

        var bans = _defender.GetActiveBans();
        Assert.Contains(bans, b => b.Ip == attackingIp && b.TargetPort == 7071);
    }

    public void Dispose()
    {
        _defender.Dispose();
        _notificationService.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }
}
