using System.Globalization;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Converters;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// Интеграционные тесты UI-слоя Firewall и Port Defender:
/// маппинг статусов сокетов/банов, строковые форматы/конвертеры,
/// валидация правил Port Defender и наличие ресурсов EN/RU.
/// </summary>
public sealed class FirewallUiIntegrationTests
{
    private static void WithCulture(string cultureCode, Action action)
    {
        var prevCulture = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            var culture = new CultureInfo(cultureCode);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    // ─── 1. Маппинг статусов брандмауэра ───

    [Theory]
    [InlineData(FirewallBlockStatus.Allowed, "FwStatusAllowed")]
    [InlineData(FirewallBlockStatus.BlockedInbound, "FwStatusBlockedInbound")]
    [InlineData(FirewallBlockStatus.BlockedOutbound, "FwStatusBlockedOutbound")]
    [InlineData(FirewallBlockStatus.BlockedBoth, "FwStatusBlockedBoth")]
    public void GetBlockStatusKey_MapsEnumToResourceKey(FirewallBlockStatus status, string expectedKey)
    {
        Assert.Equal(expectedKey, FirewallUiMapper.GetBlockStatusKey(status));
    }

    [Fact]
    public void GetBlockStatusText_EnAndRu_ReturnsLocalizedStrings()
    {
        WithCulture("en", () =>
        {
            Assert.Equal("Allowed", FirewallUiMapper.GetBlockStatusText(FirewallBlockStatus.Allowed));
            Assert.Contains("Blocked", FirewallUiMapper.GetBlockStatusText(FirewallBlockStatus.BlockedBoth));
        });
        WithCulture("ru", () =>
        {
            Assert.Equal("Разрешено", FirewallUiMapper.GetBlockStatusText(FirewallBlockStatus.Allowed));
            Assert.Contains("Заблокирован", FirewallUiMapper.GetBlockStatusText(FirewallBlockStatus.BlockedBoth));
        });
    }

    [Fact]
    public void GetSocketStatusText_PrioritizesBanOverBlockOverProtected()
    {
        var publicSocket = new ProcessNetworkSocket
        {
            ProcessId = 1234,
            ProcessName = "test.exe",
            Protocol = "TCP",
            LocalAddress = "0.0.0.0",
            LocalPort = 3389,
            RemoteAddress = "185.220.101.5",
            RemotePort = 51234,
            State = "ESTABLISHED",
            IsRemotePrivate = false,
        };
        var privateSocket = new ProcessNetworkSocket
        {
            ProcessId = 1234,
            ProcessName = "test.exe",
            Protocol = "TCP",
            LocalAddress = "127.0.0.1",
            LocalPort = 8080,
            RemoteAddress = "192.168.1.10",
            RemotePort = 5000,
            State = "ESTABLISHED",
            IsRemotePrivate = true,
        };

        WithCulture("ru", () =>
        {
            // Бан имеет наивысший приоритет.
            Assert.Equal("В бане",
                FirewallUiMapper.GetSocketStatusText(publicSocket, FirewallBlockStatus.Allowed, isBanned: true));
            // Затем — блокировка в брандмауэре.
            Assert.Equal("Заблокирован",
                FirewallUiMapper.GetSocketStatusText(publicSocket, FirewallBlockStatus.BlockedBoth, isBanned: false));
            // Приватный адрес под защитой Defender.
            Assert.Equal("Защищён Defender",
                FirewallUiMapper.GetSocketStatusText(privateSocket, FirewallBlockStatus.Allowed, isBanned: false));
            // Публичный разрешённый.
            Assert.Equal("Разрешено",
                FirewallUiMapper.GetSocketStatusText(publicSocket, FirewallBlockStatus.Allowed, isBanned: false));
        });

        WithCulture("en", () =>
        {
            Assert.Equal("Banned",
                FirewallUiMapper.GetSocketStatusText(publicSocket, FirewallBlockStatus.Allowed, isBanned: true));
            Assert.Equal("Blocked",
                FirewallUiMapper.GetSocketStatusText(publicSocket, FirewallBlockStatus.BlockedInbound, isBanned: false));
            Assert.Equal("Protected by Defender",
                FirewallUiMapper.GetSocketStatusText(privateSocket, FirewallBlockStatus.Allowed, isBanned: false));
            Assert.Equal("Allowed",
                FirewallUiMapper.GetSocketStatusText(publicSocket, FirewallBlockStatus.Allowed, isBanned: false));
        });
    }

    [Fact]
    public void GetBanStatusText_PermanentAndTemporary_FormatsCorrectly()
    {
        var permanent = new ActiveBanDto
        {
            Ip = "45.142.214.9",
            TargetPort = 3389,
            TargetProcess = "mstsc",
            BanStartedUtc = DateTime.UtcNow,
            ExpiresAtUtc = null,
            AttackRate = 20,
            Reason = "flood",
            RepeatCount = 5,
        };
        Assert.True(permanent.IsPermanent);

        var temporary = new ActiveBanDto
        {
            Ip = "91.240.118.4",
            TargetPort = 22,
            TargetProcess = "sshd",
            BanStartedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            AttackRate = 12,
            Reason = "flood",
            RepeatCount = 2,
        };
        Assert.False(temporary.IsPermanent);

        WithCulture("ru", () =>
        {
            var permText = FirewallUiMapper.GetBanStatusText(permanent);
            Assert.Contains("навсегда", permText);
            Assert.Contains("x5", permText);

            var tempText = FirewallUiMapper.GetBanStatusText(temporary);
            Assert.Contains("В бане до", tempText);
            Assert.Contains("x2", tempText);
        });

        WithCulture("en", () =>
        {
            Assert.Contains("permanently", FirewallUiMapper.GetBanStatusText(permanent));
            Assert.Contains("Banned until", FirewallUiMapper.GetBanStatusText(temporary));
        });
    }

    // ─── 2. Строковые форматы и конвертеры ───

    [Fact]
    public void FormatSocketCount_UsesLocalizedFormat()
    {
        WithCulture("en", () =>
        {
            var text = FirewallUiMapper.FormatSocketCount(10, 6, 3);
            Assert.Equal("Connections: 10 (established: 6, listening: 3)", text);
        });
        WithCulture("ru", () =>
        {
            var text = FirewallUiMapper.FormatSocketCount(10, 6, 3);
            Assert.Equal("Соединений: 10 (установлено: 6, прослушивается: 3)", text);
        });
    }

    [Fact]
    public void StatusToDisplayConverter_AllEntryStatuses_ReturnsNonEmptyExceptEnabled()
    {
        // StatusEnabledShort пуст по дизайну (нейтральный статус без бейджа),
        // остальные обязаны давать непустую локализованную строку.
        var converter = new StatusToDisplayConverter();
        WithCulture("ru", () =>
        {
            var blocked = converter.Convert(EntryStatus.Blocked, typeof(string), null!, CultureInfo.CurrentUICulture) as string;
            var disabled = converter.Convert(EntryStatus.Disabled, typeof(string), null!, CultureInfo.CurrentUICulture) as string;
            var delayed = converter.Convert(EntryStatus.Delayed, typeof(string), null!, CultureInfo.CurrentUICulture) as string;
            Assert.False(string.IsNullOrEmpty(blocked));
            Assert.False(string.IsNullOrEmpty(disabled));
            Assert.False(string.IsNullOrEmpty(delayed));
        });
    }

    [Fact]
    public void RemoteEndPoint_WildcardAndNormal_FormatsCorrectly()
    {
        var wildcard = new ProcessNetworkSocket { RemoteAddress = "*", RemotePort = 0 };
        Assert.Equal("*:*", wildcard.RemoteEndPoint);

        var zeroAddr = new ProcessNetworkSocket { RemoteAddress = "0.0.0.0", RemotePort = 0 };
        Assert.Equal("*:*", zeroAddr.RemoteEndPoint);

        var normal = new ProcessNetworkSocket { RemoteAddress = "8.8.8.8", RemotePort = 443 };
        Assert.Equal("8.8.8.8:443", normal.RemoteEndPoint);
    }

    [Theory]
    [InlineData(15, "en", "Ban for 15 minutes")]
    [InlineData(60, "en", "Ban for 1 hour")]
    [InlineData(1440, "en", "Ban for 24 hours")]
    [InlineData(0, "en", "Ban permanently")]
    [InlineData(null, "en", "Ban permanently")]
    [InlineData(15, "ru", "Бан на 15 минут")]
    [InlineData(60, "ru", "Бан на 1 час")]
    [InlineData(1440, "ru", "Бан на 24 часа")]
    [InlineData(0, "ru", "Бан навсегда")]
    [InlineData(null, "ru", "Бан навсегда")]
    public void GetBanDurationText_MapsMinutesToMenuString(int? minutes, string culture, string expected)
    {
        WithCulture(culture, () =>
        {
            Assert.Equal(expected, FirewallUiMapper.GetBanDurationText(minutes));
        });
    }

    // ─── 3. Валидация правил Port Defender ───

    [Fact]
    public void ValidateRule_ValidRule_ReturnsTrue()
    {
        var rule = new PortDefenderRuleConfig
        {
            LocalPort = 3389,
            ProcessPath = null,
            IsEnabled = true,
            ThresholdCount = 5,
            WindowUnit = TimeWindowUnit.Minute,
            BanDurationMinutes = 15,
            AutoEscalate = true,
            WhitelistIps = new List<string> { "192.168.1.100", "10.0.0.0/8" },
        };
        var (valid, error) = FirewallUiMapper.ValidateRule(rule);
        Assert.True(valid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void ValidateRule_InvalidPort_ReturnsFalse(int port)
    {
        var rule = new PortDefenderRuleConfig { LocalPort = port, ThresholdCount = 5, BanDurationMinutes = 15 };
        var (valid, error) = FirewallUiMapper.ValidateRule(rule);
        Assert.False(valid);
        Assert.NotNull(error);
        Assert.Contains("port", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateRule_InvalidThreshold_ReturnsFalse(int threshold)
    {
        var rule = new PortDefenderRuleConfig { LocalPort = 3389, ThresholdCount = threshold, BanDurationMinutes = 15 };
        var (valid, _) = FirewallUiMapper.ValidateRule(rule);
        Assert.False(valid);
    }

    [Fact]
    public void ValidateRule_NegativeBanDuration_ReturnsFalse()
    {
        var rule = new PortDefenderRuleConfig { LocalPort = 3389, ThresholdCount = 5, BanDurationMinutes = -1 };
        var (valid, _) = FirewallUiMapper.ValidateRule(rule);
        Assert.False(valid);
    }

    [Fact]
    public void ValidateRule_MalformedWhitelist_ReturnsFalse()
    {
        var rule = new PortDefenderRuleConfig
        {
            LocalPort = 3389,
            ThresholdCount = 5,
            BanDurationMinutes = 15,
            WhitelistIps = new List<string> { "not-an-ip" },
        };
        var (valid, error) = FirewallUiMapper.ValidateRule(rule);
        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateRule_ZeroPortAnyAndZeroBanPermanent_AreValid()
    {
        // LocalPort = 0 — любой порт; BanDurationMinutes = 0 — перманентный бан.
        var rule = new PortDefenderRuleConfig { LocalPort = 0, ThresholdCount = 10, BanDurationMinutes = 0 };
        var (valid, error) = FirewallUiMapper.ValidateRule(rule);
        Assert.True(valid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(1, 15, false)]
    [InlineData(2, 30, false)]
    [InlineData(3, 60, false)]
    [InlineData(4, 1440, false)]
    [InlineData(5, 0, true)]
    public void EscalationLevels_MatchBanMenuDurations(int repeat, int minutes, bool permanent)
    {
        var result = PortDefenderEscalation.CalculateBanDuration(repeat);
        Assert.Equal(permanent, result.IsPermanent);
        if (!permanent)
            Assert.Equal(TimeSpan.FromMinutes(minutes), result.Duration);
    }

    [Fact]
    public void FirewallGuard_SelfLockoutAddresses_AreRejected()
    {
        Assert.False(FirewallGuard.IsBlockableAddress("127.0.0.1", out _));
        Assert.False(FirewallGuard.IsBlockableAddress("0.0.0.0", out _));
        Assert.True(FirewallGuard.IsBlockableAddress("8.8.8.8", out _));
    }

    // ─── 4. Наличие ресурсов EN/RU ───

    [Theory]
    [InlineData("FwStatusAllowed")]
    [InlineData("FwStatusBlocked")]
    [InlineData("FwStatusBanned")]
    [InlineData("FwStatusProtected")]
    [InlineData("BlockIp")]
    [InlineData("UnblockIp")]
    [InlineData("BanDuration15m")]
    [InlineData("BanDuration1h")]
    [InlineData("BanDuration24h")]
    [InlineData("BanPermanent")]
    [InlineData("PortDefenderTitle")]
    [InlineData("PortDefenderSubtitle")]
    [InlineData("PortDefenderEnabledToggle")]
    [InlineData("PortDefenderProtectedPortsLabel")]
    [InlineData("PortDefenderProtectedPortsTooltip")]
    [InlineData("PortDefenderThresholdLabel")]
    [InlineData("PortDefenderThresholdTooltip")]
    [InlineData("PortDefenderBanDurationLabel")]
    [InlineData("PortDefenderBanDurationTooltip")]
    [InlineData("PortDefenderAutoEscalateToggle")]
    [InlineData("PortDefenderTrustPrivateToggle")]
    [InlineData("PortDefenderWhitelistLabel")]
    [InlineData("PortDefenderWhitelistTooltip")]
    [InlineData("PortDefenderRulesTitle")]
    [InlineData("PortDefenderActiveBansTitle")]
    [InlineData("PortDefenderUnbanAllButton")]
    [InlineData("PortDefenderSaveButton")]
    [InlineData("PortDefenderCloseButton")]
    [InlineData("FwSocketCountFormat")]
    [InlineData("PortDefenderBanStatusFormat")]
    [InlineData("PortDefenderBanPermanentFormat")]
    public void LocalizationResources_ExistInBothCultures(string key)
    {
        WithCulture("en", () =>
        {
            var en = LocalizationService.Instance[key];
            Assert.NotEqual(key, en);
            Assert.False(string.IsNullOrWhiteSpace(en));
        });
        WithCulture("ru", () =>
        {
            var ru = LocalizationService.Instance[key];
            Assert.NotEqual(key, ru);
            Assert.False(string.IsNullOrWhiteSpace(ru));
        });
    }

    [Fact]
    public void LocalizationResources_RuDiffersFromEn_ForKeyStatuses()
    {
        string? en, ru;
        WithCulture("en", () => { en = LocalizationService.Instance.FwStatusAllowed; Assert.Equal("Allowed", en); });
        WithCulture("ru", () => { ru = LocalizationService.Instance.FwStatusAllowed; Assert.Equal("Разрешено", ru); });
    }
}
