using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Services;

public sealed class GeoIpLookupServiceTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("10.0.0.5")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.100")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:192.168.0.1")]
    public void Resolve_PrivateAndLoopback_ReturnsLocalNetwork(string ip)
    {
        var (countryName, countryFlag, isPrivate) = GeoIpLookupService.Resolve(ip);

        Assert.True(isPrivate);
        Assert.Equal("Локальная сеть / ПК", countryName);
        Assert.Equal("🏠", countryFlag);
    }

    [Theory]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    public void Resolve_PublicRangeNearPrivateBoundaries_IsNotPrivate(string ip)
    {
        var (_, _, isPrivate) = GeoIpLookupService.Resolve(ip);

        Assert.False(isPrivate);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("*")]
    [InlineData("::")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resolve_UnspecifiedOrEmpty_ReturnsListening(string? ip)
    {
        var (countryName, countryFlag, isPrivate) = GeoIpLookupService.Resolve(ip);

        Assert.True(isPrivate);
        Assert.Equal("Прослушивание", countryName);
        Assert.Equal("👂", countryFlag);
    }

    [Theory]
    [InlineData("8.8.8.8", "США (Google DNS)")]
    [InlineData("8.8.4.4", "США (Google DNS)")]
    [InlineData("1.1.1.1", "США (Cloudflare DNS)")]
    [InlineData("1.0.0.1", "США (Cloudflare DNS)")]
    [InlineData("77.88.8.8", "Россия (Yandex DNS)")]
    [InlineData("77.88.8.1", "Россия (Yandex DNS)")]
    [InlineData("9.9.9.9", "США (Quad9)")]
    public void Resolve_WellKnownDns_ReturnsExpectedCountry(string ip, string expectedCountry)
    {
        var (countryName, countryFlag, isPrivate) = GeoIpLookupService.Resolve(ip);

        Assert.False(isPrivate);
        Assert.Equal(expectedCountry, countryName);
        Assert.False(string.IsNullOrEmpty(countryFlag));
    }

    [Fact]
    public void Resolve_GoogleDns_HasUsaFlag()
    {
        var (_, flag, _) = GeoIpLookupService.Resolve("8.8.8.8");

        Assert.Equal("🇺🇸", flag);
    }

    [Fact]
    public void Resolve_YandexDns_HasRussiaFlag()
    {
        var (_, flag, _) = GeoIpLookupService.Resolve("77.88.8.8");

        Assert.Equal("🇷🇺", flag);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("999.999.999.999")]
    [InlineData("8.8.8.8:53")]
    public void Resolve_InvalidInput_ReturnsInternetFallbackWithoutThrow(string ip)
    {
        var ex = Record.Exception(() => GeoIpLookupService.Resolve(ip));

        Assert.Null(ex);
        var (countryName, countryFlag, isPrivate) = GeoIpLookupService.Resolve(ip);
        Assert.Equal("Интернет", countryName);
        Assert.Equal("🌐", countryFlag);
        Assert.False(isPrivate);
    }

    [Theory]
    [InlineData("213.180.204.3", "Россия")]
    [InlineData("142.250.1.1", "США")]
    [InlineData("135.181.10.20", "Германия")]
    [InlineData("51.38.1.1", "Франция")]
    [InlineData("46.101.10.20", "Нидерланды")]
    [InlineData("86.54.10.20", "Великобритания")]
    [InlineData("65.21.10.20", "Финляндия")]
    [InlineData("120.25.10.20", "Китай")]
    public void Resolve_PopularPrefixes_ReturnsExpectedRegion(string ip, string expectedPart)
    {
        var (countryName, countryFlag, isPrivate) = GeoIpLookupService.Resolve(ip);

        Assert.False(isPrivate);
        Assert.Contains(expectedPart, countryName);
        Assert.False(string.IsNullOrEmpty(countryFlag));
    }

    [Fact]
    public void Resolve_SameIpTwice_ReturnsCachedResult()
    {
        GeoIpLookupService.ClearCache();
        const string ip = "8.8.8.8";

        var first = GeoIpLookupService.Resolve(ip);
        var second = GeoIpLookupService.Resolve(ip);

        Assert.Equal(first, second);
        Assert.Equal(1, GeoIpLookupService.CacheCount);
    }

    [Fact]
    public void Resolve_DifferentIps_CachesEachSeparately()
    {
        GeoIpLookupService.ClearCache();

        GeoIpLookupService.Resolve("8.8.8.8");
        GeoIpLookupService.Resolve("1.1.1.1");

        Assert.Equal(2, GeoIpLookupService.CacheCount);
    }
}
