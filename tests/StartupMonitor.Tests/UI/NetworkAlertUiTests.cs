using System.Text.Json;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Views;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// UI-тесты интерактивных сетевых алертов (Worker 3):
/// маппинг <see cref="NetworkConnectionAlertDto"/> → <see cref="ResolveNetworkAlertRequest"/>,
/// решающая функция тика поллера <c>NetworkSecurityHubView.ShouldShowAlertWindow</c>,
/// сериализация Pipe-контрактов (GetPendingNetworkAlerts / ResolveNetworkAlert / SetAlertsEnabled).
/// WPF-окна не создаются: проверяется чистая логика без STA-диспетчера.
/// </summary>
public sealed class NetworkAlertUiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static NetworkConnectionAlertDto SampleAlert(string alertId = "abc123") => new()
    {
        AlertId = alertId,
        ProcessId = 4242,
        ProcessName = "telegram.exe",
        ProcessPath = @"C:\Users\User\AppData\Roaming\Telegram Desktop\Telegram.exe",
        Protocol = "TCP",
        RemoteAddress = "149.154.167.91",
        RemotePort = 443,
        DetectedAt = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc)
    };

    // ─── 1. Маппинг DTO → ResolveNetworkAlertRequest ───

    [Theory]
    [InlineData(NetworkAlertResolution.AllowAlways)]
    [InlineData(NetworkAlertResolution.AllowSession)]
    [InlineData(NetworkAlertResolution.BlockAlways)]
    public void BuildResolveRequest_MapsAllFields_ForEachResolution(NetworkAlertResolution resolution)
    {
        var alert = SampleAlert();

        var req = NetworkSecurityHubView.BuildResolveRequest(alert, resolution);

        Assert.Equal("abc123", req.AlertId);
        Assert.Equal(@"C:\Users\User\AppData\Roaming\Telegram Desktop\Telegram.exe", req.ProcessPath);
        Assert.Equal("telegram.exe", req.ProcessName);
        Assert.Equal("149.154.167.91", req.RemoteAddress);
        Assert.Equal(resolution, req.Resolution);
    }

    [Fact]
    public void BuildResolveRequest_NullAlert_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NetworkSecurityHubView.BuildResolveRequest(null!, NetworkAlertResolution.AllowSession));
    }

    [Fact]
    public void BuildResolveRequest_NullStrings_MappedToEmpty()
    {
        var alert = new NetworkConnectionAlertDto
        {
            AlertId = null!,
            ProcessName = null!,
            ProcessPath = null!,
            RemoteAddress = null!
        };

        var req = NetworkSecurityHubView.BuildResolveRequest(alert, NetworkAlertResolution.BlockAlways);

        Assert.Equal(string.Empty, req.AlertId);
        Assert.Equal(string.Empty, req.ProcessName);
        Assert.Equal(string.Empty, req.ProcessPath);
        Assert.Equal(string.Empty, req.RemoteAddress);
        Assert.Equal(NetworkAlertResolution.BlockAlways, req.Resolution);
    }

    [Fact]
    public void BuildResolveRequest_DoesNotMutateSourceDto()
    {
        var alert = SampleAlert("id-1");
        var before = JsonSerializer.Serialize(alert, JsonOptions);

        _ = NetworkSecurityHubView.BuildResolveRequest(alert, NetworkAlertResolution.AllowAlways);

        Assert.Equal(before, JsonSerializer.Serialize(alert, JsonOptions));
        Assert.Equal("id-1", alert.AlertId);
    }

    // ─── 2. Логика тика поллера: ShouldShowAlertWindow ───

    [Fact]
    public void ShouldShowAlertWindow_EnabledWithAlertsAndClosed_ReturnsTrue()
    {
        var alerts = new List<NetworkConnectionAlertDto> { SampleAlert() };

        Assert.True(NetworkSecurityHubView.ShouldShowAlertWindow(
            alertsEnabled: true, alerts: alerts, windowAlreadyOpen: false));
    }

    [Fact]
    public void ShouldShowAlertWindow_DisabledWithAlerts_ReturnsFalse()
    {
        var alerts = new List<NetworkConnectionAlertDto> { SampleAlert() };

        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(
            alertsEnabled: false, alerts: alerts, windowAlreadyOpen: false));
    }

    [Fact]
    public void ShouldShowAlertWindow_WindowAlreadyOpen_ReturnsFalse_NoDuplicates()
    {
        var alerts = new List<NetworkConnectionAlertDto> { SampleAlert(), SampleAlert("second") };

        // Второе окно не открывается, пока первое не закрыто — защита от спама тостами.
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(
            alertsEnabled: true, alerts: alerts, windowAlreadyOpen: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldShowAlertWindow_EmptyAlerts_ReturnsFalse(bool enabled)
    {
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(
            alertsEnabled: enabled,
            alerts: new List<NetworkConnectionAlertDto>(),
            windowAlreadyOpen: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldShowAlertWindow_NullAlerts_ReturnsFalse(bool enabled)
    {
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(
            alertsEnabled: enabled, alerts: null, windowAlreadyOpen: false));
    }

    [Fact]
    public void ShouldShowAlertWindow_PollerTickMatrix_AllCombinations()
    {
        var one = new List<NetworkConnectionAlertDto> { SampleAlert() };
        var empty = new List<NetworkConnectionAlertDto>();

        // (enabled, alerts, open) → expected
        Assert.True(NetworkSecurityHubView.ShouldShowAlertWindow(true, one, false));
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(true, one, true));
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(true, empty, false));
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(true, null, false));
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(false, one, false));
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(false, empty, false));
        Assert.False(NetworkSecurityHubView.ShouldShowAlertWindow(false, one, true));
    }

    // ─── 3. Pipe-контракты: сериализация/десериализация ───

    [Fact]
    public void NetworkAlertsPayload_RoundTrip_PreservesAlerts()
    {
        var payload = new NetworkAlertsPayload(new List<NetworkConnectionAlertDto>
        {
            SampleAlert("a1"),
            SampleAlert("a2")
        });

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var back = JsonSerializer.Deserialize<NetworkAlertsPayload>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(2, back!.Alerts.Count);
        Assert.Equal("a1", back.Alerts[0].AlertId);
        Assert.Equal("a2", back.Alerts[1].AlertId);
        Assert.Equal(443, back.Alerts[0].RemotePort);
    }

    [Fact]
    public void NetworkAlertDto_JsonPropertyNames_AreCamelCase()
    {
        var json = JsonSerializer.Serialize(SampleAlert("zz"), JsonOptions);

        Assert.Contains("\"alertId\"", json);
        Assert.Contains("\"processId\"", json);
        Assert.Contains("\"processName\"", json);
        Assert.Contains("\"processPath\"", json);
        Assert.Contains("\"remoteAddress\"", json);
        Assert.Contains("\"remotePort\"", json);
    }

    [Fact]
    public void ResolveRequest_RoundTrip_PreservesResolution()
    {
        var req = NetworkSecurityHubView.BuildResolveRequest(
            SampleAlert("r1"), NetworkAlertResolution.BlockAlways);

        var json = JsonSerializer.Serialize(req, JsonOptions);
        var back = JsonSerializer.Deserialize<ResolveNetworkAlertRequest>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("r1", back!.AlertId);
        Assert.Equal(NetworkAlertResolution.BlockAlways, back.Resolution);
    }

    [Fact]
    public void ResolveResultPayload_RoundTrip()
    {
        var result = new ResolveNetworkAlertResultPayload(true, "Решение применено.");
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var back = JsonSerializer.Deserialize<ResolveNetworkAlertResultPayload>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.True(back!.Success);
        Assert.Equal("Решение применено.", back.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlertsEnabledPayload_RoundTrip(bool enabled)
    {
        var payload = new AlertsEnabledPayload(enabled);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var back = JsonSerializer.Deserialize<AlertsEnabledPayload>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(enabled, back!.Enabled);
    }

    [Fact]
    public void PipeMessageTypes_AlertConstants_AreDistinct()
    {
        var types = new[]
        {
            PipeMessageTypes.GetPendingNetworkAlerts,
            PipeMessageTypes.GetPendingNetworkAlertsResponse,
            PipeMessageTypes.ResolveNetworkAlert,
            PipeMessageTypes.ResolveNetworkAlertResponse,
            PipeMessageTypes.SetAlertsEnabled,
            PipeMessageTypes.GetAlertsEnabled,
            PipeMessageTypes.GetAlertsEnabledResponse
        };

        Assert.Equal(types.Length, types.Distinct(StringComparer.Ordinal).Count());
        Assert.All(types, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    // ─── 4. Резолв: все решения сериализуются и различаются ───

    [Fact]
    public void ResolveRequests_AllResolutions_SerializeDistinctly()
    {
        var alert = SampleAlert("multi");
        var allResolutions = Enum.GetValues<NetworkAlertResolution>();
        var jsons = allResolutions
            .Select(r => JsonSerializer.Serialize(
                NetworkSecurityHubView.BuildResolveRequest(alert, r), JsonOptions))
            .ToList();

        Assert.Equal(allResolutions.Length, jsons.Distinct().Count());
        Assert.All(jsons, j => Assert.Contains("\"alertId\"", j));
    }
}
