using System.Text.Json;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Юнит-тесты протокола Named Pipe для операций Firewall и Port Defender (Worker 2).
/// </summary>
public class FirewallPipeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void BlockIpPayload_RoundTrip()
    {
        var payload = new BlockIpPayload("198.51.100.1", FirewallDirection.Inbound, true, 15, "Brute force attack");
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BlockIpPayload>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("198.51.100.1", deserialized.Ip);
        Assert.Equal(FirewallDirection.Inbound, deserialized.Direction);
        Assert.True(deserialized.Aggregated);
        Assert.Equal(15, deserialized.BanDurationMinutes);
        Assert.Equal("Brute force attack", deserialized.Reason);
    }

    [Fact]
    public void UnblockIpPayload_RoundTrip()
    {
        var payload = new UnblockIpPayload("198.51.100.1", FirewallDirection.Both);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<UnblockIpPayload>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("198.51.100.1", deserialized.Ip);
        Assert.Equal(FirewallDirection.Both, deserialized.Direction);
    }

    [Fact]
    public void BlockPortPayload_RoundTrip()
    {
        var payload = new BlockPortPayload(3389, FirewallProtocol.Tcp, FirewallDirection.Inbound, @"C:\Windows\System32\mstsc.exe", "RDP Lock");
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BlockPortPayload>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(3389, deserialized.Port);
        Assert.Equal(FirewallProtocol.Tcp, deserialized.Protocol);
        Assert.Equal(FirewallDirection.Inbound, deserialized.Direction);
        Assert.Equal(@"C:\Windows\System32\mstsc.exe", deserialized.ApplicationPath);
        Assert.Equal("RDP Lock", deserialized.Reason);
    }

    [Fact]
    public void UnblockPortPayload_RoundTrip()
    {
        var payload = new UnblockPortPayload(80, FirewallProtocol.Tcp, FirewallDirection.Inbound, null);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<UnblockPortPayload>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(80, deserialized.Port);
        Assert.Equal(FirewallProtocol.Tcp, deserialized.Protocol);
        Assert.Equal(FirewallDirection.Inbound, deserialized.Direction);
        Assert.Null(deserialized.ApplicationPath);
    }

    [Fact]
    public void ActiveBansPayload_RoundTrip()
    {
        var ban = new ActiveBanDto
        {
            Ip = "203.0.113.5",
            TargetPort = 3389,
            TargetProcess = "mstsc.exe",
            BanStartedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            AttackRate = 25,
            Reason = "RDP Flood",
            RepeatCount = 2,
            RuleName = "StartupMonitor_Blocked_IPs_In"
        };

        var payload = new ActiveBansPayload(new List<ActiveBanDto> { ban });
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ActiveBansPayload>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Bans);
        Assert.Equal("203.0.113.5", deserialized.Bans[0].Ip);
        Assert.Equal(3389, deserialized.Bans[0].TargetPort);
        Assert.Equal(3389, deserialized.Bans[0].Port);
        Assert.Equal("mstsc.exe", deserialized.Bans[0].TargetProcess);
        Assert.Equal(25, deserialized.Bans[0].AttackRate);
        Assert.Equal(25, deserialized.Bans[0].AttemptCount);
        Assert.Equal(2, deserialized.Bans[0].RepeatCount);
    }

    [Fact]
    public void PortDefenderConfigDto_RoundTrip()
    {
        var config = new PortDefenderConfigDto
        {
            Enabled = true,
            ProtectedPorts = new List<int> { 22, 80, 443, 3389 },
            MaxAttempts = 15,
            WindowUnit = TimeWindowUnit.Minute,
            WindowSeconds = 60,
            BanDurationMinutes = 30,
            AutoEscalate = true,
            TrustPrivateNetworks = true,
            WhitelistIps = new List<string> { "192.168.1.50", "10.0.0.1" },
            Rules = new List<PortDefenderRuleConfig>
            {
                new(3389, null, true, 5, TimeWindowUnit.Minute, 60, true, new List<string>())
            }
        };

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PortDefenderConfigDto>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Enabled);
        Assert.True(deserialized.IsGloballyEnabled);
        Assert.Equal(4, deserialized.ProtectedPorts.Count);
        Assert.Equal(15, deserialized.MaxAttempts);
        Assert.Equal(15, deserialized.ThresholdCount);
        Assert.Equal(TimeWindowUnit.Minute, deserialized.WindowUnit);
        Assert.Equal(30, deserialized.BanDurationMinutes);
        Assert.Equal(30, deserialized.DefaultBanDurationMinutes);
        Assert.True(deserialized.AutoEscalate);
        Assert.True(deserialized.TrustPrivateNetworks);
        Assert.Equal(2, deserialized.WhitelistIps.Count);
        Assert.Single(deserialized.Rules);
        Assert.Equal(3389, deserialized.Rules[0].LocalPort);
    }

    [Fact]
    public void FirewallOperationResult_Serialization()
    {
        var ok = FirewallOperationResult.Ok("IP заблокирован.", "StartupMonitor_Blocked_IPs_In", created: true);
        var okJson = JsonSerializer.Serialize(ok, JsonOptions);
        var okDes = JsonSerializer.Deserialize<FirewallOperationResult>(okJson, JsonOptions);

        Assert.NotNull(okDes);
        Assert.True(okDes.Success);
        Assert.Equal("IP заблокирован.", okDes.Message);
        Assert.Equal("StartupMonitor_Blocked_IPs_In", okDes.RuleName);
        Assert.True(okDes.Created);

        var fail = FirewallOperationResult.Fail("Ошибка блокировки.");
        var failJson = JsonSerializer.Serialize(fail, JsonOptions);
        var failDes = JsonSerializer.Deserialize<FirewallOperationResult>(failJson, JsonOptions);

        Assert.NotNull(failDes);
        Assert.False(failDes.Success);
        Assert.Equal("Ошибка блокировки.", failDes.Message);
    }
}
