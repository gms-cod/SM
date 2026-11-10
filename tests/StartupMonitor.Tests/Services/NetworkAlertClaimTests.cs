using System.Text.Json;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет исключительное право показа, возврат аренды и достоверность IPC-подтверждения без изменения ОС.</summary>
public sealed class NetworkAlertClaimTests
{
    /// <summary>Один AlertId не выдаётся одновременно двум клиентам; второй получает другую доступную группу.</summary>
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

    /// <summary>После истечения аренды живой запрос доступен новому клиенту без потери пользовательского события.</summary>
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

    /// <summary>Отключение клиента сразу освобождает его карточку, а продление защищает от преждевременной выдачи.</summary>
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

    /// <summary>Чужой клиент не освобождает действующую аренду; удалённый запрос не блокирует новую очередь.</summary>
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

    /// <summary>Отказ координатора списков передаётся UI как отказ, сохраняя причину и признак неуспеха.</summary>
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
