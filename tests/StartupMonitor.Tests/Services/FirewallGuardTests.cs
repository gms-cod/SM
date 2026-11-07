using StartupMonitor.Shared.Models.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

public sealed class FirewallGuardTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("185.220.101.5")]
    [InlineData("1.1.1.1")]
    public void IsBlockableAddress_ValidPublicIpv4_ReturnsTrueAndNoError(string ip)
    {
        var result = FirewallGuard.IsBlockableAddress(ip, out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    public void IsBlockableAddress_ValidIpv6_ReturnsTrueAndNoError(string ip)
    {
        var result = FirewallGuard.IsBlockableAddress(ip, out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("::1")]
    public void IsBlockableAddress_Loopback_ReturnsFalseWithSelfLockoutError(string ip)
    {
        var result = FirewallGuard.IsBlockableAddress(ip, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("loopback", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("самоблокировка", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void IsBlockableAddress_Unspecified_ReturnsFalseWithError(string ip)
    {
        var result = FirewallGuard.IsBlockableAddress(ip, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("unspecified", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("ff02::1")]
    public void IsBlockableAddress_BroadcastAndMulticast_ReturnsFalseWithError(string ip)
    {
        var result = FirewallGuard.IsBlockableAddress(ip, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.True(
            error.Contains("multicast", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("broadcast", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBlockableAddress_NullOrWhiteSpace_ReturnsFalseWithEmptyIpError(string? ip)
    {
        var result = FirewallGuard.IsBlockableAddress(ip!, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("Пустой", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid-ip")]
    [InlineData("999.999.999.999")]
    [InlineData("192.168.1.300")]
    [InlineData("not:an:ip:address:xyz")]
    public void IsBlockableAddress_MalformedStrings_ReturnsFalseWithFormatError(string ip)
    {
        var result = FirewallGuard.IsBlockableAddress(ip, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("Некорректный", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("192.168.1.0/24")]
    [InlineData("10.0.0.0/8")]
    [InlineData("172.16.0.0/12")]
    public void IsBlockableAddress_SubnetCidr_ExtractsAddressCorrectlyAndReturnsTrue(string subnet)
    {
        var result = FirewallGuard.IsBlockableAddress(subnet, out var error);

        Assert.True(result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(443)]
    [InlineData(3389)]
    [InlineData(65535)]
    public void IsValidPort_BoundaryAndStandardPorts_ReturnsTrue(int port)
    {
        Assert.True(FirewallGuard.IsValidPort(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(100000)]
    [InlineData(-65535)]
    public void IsValidPort_InvalidValues_ReturnsFalse(int port)
    {
        Assert.False(FirewallGuard.IsValidPort(port));
    }
}
