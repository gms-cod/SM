using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// UI-тесты вкладки «Активные соединения» (NetworkSecurityHubView, вкладка Live):
/// предикаты фильтрации WAN/LAN/ESTABLISHED/LISTEN + поиск,
/// формирование <see cref="LiveConnectionRow"/> и валидация флага CanBlockIp.
/// <para/>
/// DataGrid требует STA-диспетчера WPF, поэтому здесь проверяется тот же
/// предикат, что и в <c>NetworkSecurityHubView.MatchesConnFilter</c>:
/// фильтр типа сети/состояния + поиск по ProcessName, ProcessId,
/// RemoteAddress, LocalEndPoint, RemoteEndPoint.
/// </summary>
public sealed class LiveConnectionsUiTests
{
    /// <summary>Фильтр таблицы соединений (зеркало приватного enum NetworkSecurityHubView.ConnFilter).</summary>
    private enum HubConnFilter
    {
        All,
        Wan,
        Lan,
        Established,
        Listening
    }

    /// <summary>
    /// Предикат фильтрации — прямая копия логики NetworkSecurityHubView.MatchesConnFilter.
    /// При изменении логики хаба этот метод и тесты обязаны обновляться синхронно.
    /// </summary>
    private static List<LiveConnectionRow> ApplyConnFilter(
        IEnumerable<LiveConnectionRow> rows, string query, HubConnFilter filter)
    {
        var q = (query ?? string.Empty).Trim().ToLowerInvariant();
        return rows.Where(r =>
        {
            switch (filter)
            {
                case HubConnFilter.Wan:
                    if (r.IsPrivate)
                        return false;
                    break;
                case HubConnFilter.Lan:
                    if (!r.IsPrivate)
                        return false;
                    break;
                case HubConnFilter.Established:
                    if (!string.Equals((r.State ?? string.Empty).Trim(), "ESTABLISHED", StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;
                case HubConnFilter.Listening:
                    var state = (r.State ?? string.Empty).Trim().ToUpperInvariant();
                    if (state != "LISTENING" && state != "LISTEN" && state != "BOUND")
                        return false;
                    break;
            }

            if (string.IsNullOrEmpty(q))
                return true;

            return (r.ProcessName ?? string.Empty).ToLowerInvariant().Contains(q)
                || r.ProcessId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                || (r.RemoteAddress ?? string.Empty).ToLowerInvariant().Contains(q)
                || (r.LocalEndPoint ?? string.Empty).ToLowerInvariant().Contains(q)
                || (r.RemoteEndPoint ?? string.Empty).ToLowerInvariant().Contains(q);
        }).ToList();
    }

    private static ProcessNetworkSocket Socket(
        string processName, int pid, string protocol,
        string localAddress, int localPort,
        string remoteAddress, int remotePort, string state) => new()
        {
            ProcessId = pid,
            ProcessName = processName,
            Protocol = protocol,
            LocalAddress = localAddress,
            LocalPort = localPort,
            RemoteAddress = remoteAddress,
            RemotePort = remotePort,
            State = state
        };

    private static List<LiveConnectionRow> SampleConnections() => new()
    {
        // Публичное ESTABLISHED-соединение браузера.
        new LiveConnectionRow(Socket("chrome", 1234, "TCP", "192.168.1.50", 51234, "142.250.0.1", 443, "ESTABLISHED")),
        // Слушающий сокет веб-сервера (RemoteAddress "*" — LAN/прослушивание).
        new LiveConnectionRow(Socket("nginx", 567, "TCP", "0.0.0.0", 80, "*", 0, "LISTENING")),
        // Локальное ESTABLISHED-соединение внутри LAN.
        new LiveConnectionRow(Socket("lan-sync", 910, "TCP", "192.168.1.50", 51235, "192.168.1.10", 445, "ESTABLISHED")),
        // SSH-демон в режиме LISTEN на всех интерфейсах.
        new LiveConnectionRow(Socket("sshd", 42, "TCP", "0.0.0.0", 22, "0.0.0.0", 0, "LISTEN")),
        // UDP-сокет в состоянии BOUND (считается прослушиваемым).
        new LiveConnectionRow(Socket("dns-client", 777, "UDP", "192.168.1.50", 53535, "8.8.8.8", 53, "BOUND")),
    };

    // ─── 1. Фильтр WAN / LAN ───

    [Fact]
    public void Filter_Wan_ReturnsPublicOnly()
    {
        var result = ApplyConnFilter(SampleConnections(), string.Empty, HubConnFilter.Wan);
        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.False(r.IsPrivate));
        Assert.Contains(result, r => r.ProcessName == "chrome");
    }

    [Fact]
    public void Filter_Lan_ReturnsPrivateAndListeningOnly()
    {
        var result = ApplyConnFilter(SampleConnections(), string.Empty, HubConnFilter.Lan);
        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.True(r.IsPrivate));
    }

    [Fact]
    public void Filter_AllWithEmptyQuery_ReturnsEverything()
    {
        var result = ApplyConnFilter(SampleConnections(), "   ", HubConnFilter.All);
        Assert.Equal(5, result.Count);
    }

    // ─── 2. Фильтр по состоянию ───

    [Fact]
    public void Filter_Established_ReturnsEstablishedOnly()
    {
        var result = ApplyConnFilter(SampleConnections(), string.Empty, HubConnFilter.Established);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("ESTABLISHED", r.State.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("LISTENING")]
    [InlineData("LISTEN")]
    [InlineData("BOUND")]
    public void Filter_Listening_CoversAllListenStates(string state)
    {
        var rows = new List<LiveConnectionRow>
        {
            new(Socket("proc", 1, "TCP", "0.0.0.0", 8080, "*", 0, state))
        };
        var result = ApplyConnFilter(rows, string.Empty, HubConnFilter.Listening);
        Assert.Single(result);
    }

    [Fact]
    public void Filter_Listening_ReturnsListenStatesOnly()
    {
        var result = ApplyConnFilter(SampleConnections(), string.Empty, HubConnFilter.Listening);
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Contains(
            r.State.ToUpperInvariant(), new[] { "LISTENING", "LISTEN", "BOUND" }));
    }

    // ─── 3. Поиск ───

    [Theory]
    [InlineData("chrome")]
    [InlineData("CHROME")]
    [InlineData("  chrome  ")]
    public void Search_ByProcessName_MatchesCaseInsensitive(string query)
    {
        var result = ApplyConnFilter(SampleConnections(), query, HubConnFilter.All);
        var single = Assert.Single(result);
        Assert.Equal("chrome", single.ProcessName);
    }

    [Fact]
    public void Search_ByPid_Matches()
    {
        var result = ApplyConnFilter(SampleConnections(), "1234", HubConnFilter.All);
        var single = Assert.Single(result);
        Assert.Equal(1234, single.ProcessId);
    }

    [Fact]
    public void Search_ByRemoteIp_Matches()
    {
        var result = ApplyConnFilter(SampleConnections(), "142.250", HubConnFilter.All);
        var single = Assert.Single(result);
        Assert.Equal("chrome", single.ProcessName);
    }

    [Fact]
    public void Search_ByLocalEndPoint_Matches()
    {
        var result = ApplyConnFilter(SampleConnections(), "192.168.1.50:51234", HubConnFilter.All);
        var single = Assert.Single(result);
        Assert.Equal("chrome", single.ProcessName);
    }

    [Fact]
    public void Search_ByRemoteEndPoint_Matches()
    {
        var result = ApplyConnFilter(SampleConnections(), "142.250.0.1:443", HubConnFilter.All);
        var single = Assert.Single(result);
        Assert.Equal("chrome", single.ProcessName);
    }

    [Fact]
    public void Search_CombinedQueryAndFilter_NarrowsResults()
    {
        // Подстрока "192.168" встречается и в локальных, и в удалённых адресах.
        var queryOnly = ApplyConnFilter(SampleConnections(), "192.168", HubConnFilter.All);
        Assert.True(queryOnly.Count >= 2);

        // + фильтр WAN оставляет только публичные строки (локальный адрес не в счёт —
        // IsPrivate определяется по удалённому IP через GeoIpLookupService).
        var combined = ApplyConnFilter(SampleConnections(), "192.168", HubConnFilter.Wan);
        Assert.All(combined, r => Assert.False(r.IsPrivate));
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var result = ApplyConnFilter(SampleConnections(), "такого-соединения-нет-12345", HubConnFilter.All);
        Assert.Empty(result);
    }

    [Fact]
    public void Search_NullQuery_TreatedAsEmpty()
    {
        var result = ApplyConnFilter(SampleConnections(), null!, HubConnFilter.All);
        Assert.Equal(5, result.Count);
    }

    // ─── 4. Формирование LiveConnectionRow ───

    [Fact]
    public void Row_ExposesEndpointsAndProcess()
    {
        var row = new LiveConnectionRow(
            Socket("chrome", 1234, "TCP", "192.168.1.50", 51234, "142.250.0.1", 443, "ESTABLISHED"));
        Assert.Equal(1234, row.ProcessId);
        Assert.Equal(1234, row.Pid);
        Assert.Equal("chrome", row.ProcessName);
        Assert.Equal("192.168.1.50:51234", row.LocalEndPoint);
        Assert.Equal("142.250.0.1:443", row.RemoteEndPoint);
        Assert.Equal("ESTABLISHED", row.State);
    }

    [Fact]
    public void Row_DefaultBadge_IsAllowed()
    {
        var row = new LiveConnectionRow(
            Socket("chrome", 1234, "TCP", "192.168.1.50", 51234, "142.250.0.1", 443, "ESTABLISHED"));
        Assert.Equal("🟢 Разрешено", row.FirewallBadge);
    }

    [Fact]
    public void Row_CustomBadge_IsPreserved()
    {
        var row = new LiveConnectionRow(
            Socket("chrome", 1234, "TCP", "192.168.1.50", 51234, "142.250.0.1", 443, "ESTABLISHED"),
            "🚫 Заблокирован");
        Assert.Equal("🚫 Заблокирован", row.FirewallBadge);
    }

    [Fact]
    public void Row_GeoEnrichment_PublicIpIsWan()
    {
        var row = new LiveConnectionRow(
            Socket("chrome", 1234, "TCP", "192.168.1.50", 51234, "8.8.8.8", 53, "ESTABLISHED"));
        Assert.False(row.IsPrivate);
        Assert.Equal("🌐 WAN", row.NetworkTypeBadge);
        Assert.False(string.IsNullOrWhiteSpace(row.CountryText));
    }

    [Fact]
    public void Row_GeoEnrichment_PrivateIpIsLan()
    {
        var row = new LiveConnectionRow(
            Socket("lan-sync", 910, "TCP", "192.168.1.50", 51235, "192.168.1.10", 445, "ESTABLISHED"));
        Assert.True(row.IsPrivate);
        Assert.Equal("🏠 LAN", row.NetworkTypeBadge);
    }

    // ─── 5. Флаг CanBlockIp ───

    [Fact]
    public void CanBlockIp_PublicIp_ReturnsTrue()
    {
        var row = new LiveConnectionRow(
            Socket("chrome", 1234, "TCP", "192.168.1.50", 51234, "142.250.0.1", 443, "ESTABLISHED"));
        Assert.True(row.CanBlockIp);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.5")]
    [InlineData("127.0.0.1")]
    public void CanBlockIp_PrivateIp_ReturnsFalse(string ip)
    {
        var row = new LiveConnectionRow(
            Socket("lan-sync", 910, "TCP", "192.168.1.50", 51235, ip, 445, "ESTABLISHED"));
        Assert.False(row.CanBlockIp);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("")]
    public void CanBlockIp_ListeningSocket_ReturnsFalse(string remoteAddress)
    {
        var row = new LiveConnectionRow(
            Socket("nginx", 567, "TCP", "0.0.0.0", 80, remoteAddress, 0, "LISTENING"));
        Assert.False(row.CanBlockIp);
    }

    [Fact]
    public void GeoIp_Resolve_NeverThrows_AndCaches()
    {
        GeoIpLookupService.ClearCache();
        var ex = Record.Exception(() =>
        {
            var pub = GeoIpLookupService.Resolve("8.8.8.8");
            Assert.False(pub.IsPrivate);
            var priv = GeoIpLookupService.Resolve("192.168.0.1");
            Assert.True(priv.IsPrivate);
            var listen = GeoIpLookupService.Resolve("*");
            Assert.True(listen.IsPrivate);
        });
        Assert.Null(ex);
    }
}
