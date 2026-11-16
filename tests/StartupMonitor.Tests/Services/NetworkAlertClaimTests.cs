using System.Text.Json;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for NetworkAlertClaimTests.cs.</summary>
public sealed class NetworkAlertClaimTests
{
    /// <summary>Verifies test scenario for NetworkAlertClaimTests.cs.</summary>
    [Fact]
    public void SeparateClients_DoNotReceiveTheSameAlert()
    {
        var registry = new NetworkAlertClaimRegistry();
        List<NetworkConnectionAlertDto> alerts = [new() { AlertId = "first" }, new() { AlertId = "second" }];
        Assert.Equal("first", Assert.Single(registry.GetNext("client-a", alerts)).AlertId);
        Assert.Equal("second", Assert.Single(registry.GetNext("client-b", alerts)).AlertId);
        Assert.False(registry.TryAcquire("client-b", "first"));
        Assert.Equal("first", Assert.Single(registry.GetNext("client-a", alerts)).AlertId);
    }

    /// <summary>Verifies test scenario for NetworkAlertClaimTests.cs.</summary>
    [Fact]
    public void ExpiredLease_IsReturnedToQueue()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var registry = new NetworkAlertClaimRegistry(() => now);
        List<NetworkConnectionAlertDto> alerts = [new() { AlertId = "first" }];
        Assert.Single(registry.GetNext("client-a", alerts));
        Assert.Empty(registry.GetNext("client-b", alerts));
        now = now.AddMinutes(2);
        Assert.Single(registry.GetNext("client-b", alerts));
    }

    /// <summary>Verifies test scenario for NetworkAlertClaimTests.cs.</summary>
    [Fact]
    public void Disconnect_ReleasesClaimAndRenewalKeepsOwnership()
    {
        var now = DateTime.UtcNow;
        var registry = new NetworkAlertClaimRegistry(() => now);
        List<NetworkConnectionAlertDto> alerts = [new() { AlertId = "first" }];
        registry.GetNext("client-a", alerts);
        now = now.AddSeconds(40);
        Assert.True(registry.TryAcquire("client-a", "first"));
        now = now.AddSeconds(40);
        Assert.Empty(registry.GetNext("client-b", alerts));
        registry.ReleaseClient("client-a");
        Assert.Single(registry.GetNext("client-b", alerts));
    }

    /// <summary>Verifies test scenario for NetworkAlertClaimTests.cs.</summary>
    [Fact]
    public void Release_IsOwnerScopedAndCompletedGroupsArePruned()
    {
        var registry = new NetworkAlertClaimRegistry();
        registry.GetNext("client-a", [new() { AlertId = "old" }]);
        registry.Release("client-b", "old");
        Assert.False(registry.TryAcquire("client-b", "old"));
        Assert.Equal("new", Assert.Single(registry.GetNext("client-b", [new() { AlertId = "new" }])).AlertId);
        Assert.True(registry.TryAcquire("client-b", "old"));
    }

    /// <summary>Verifies test scenario for NetworkAlertClaimTests.cs.</summary>
    [Fact]
    public void FailedPolicyOperation_IsNeverTransportSuccess()
    {
        var response = PipeServer.FirewallOperationReply(FirewallOperationResult.Fail("Controlled policy rejection"));
        Assert.Equal(PipeMessageTypes.Error, response.Type);
        var result = JsonSerializer.Deserialize<FirewallOperationResult>(response.Payload);
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("Controlled policy rejection", result.Message);
        Assert.Equal(PipeMessageTypes.Success, PipeServer.FirewallOperationReply(FirewallOperationResult.Ok("Saved")).Type);
    }
}
