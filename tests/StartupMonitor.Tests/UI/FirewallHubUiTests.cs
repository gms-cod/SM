using System.Globalization;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// UI-тесты Центра сетевой безопасности (NetworkSecurityHubView, страница 8 MainWindow):
/// фильтрация правил (поиск по имени, порту, типу) и локализация всех ключей брандмауэра.
/// <para/>
/// Фильтрация UserControl требует STA-диспетчера WPF, поэтому здесь проверяется
/// тот же предикат, что и в <c>NetworkSecurityHubView.ApplyRuleFilter</c>:
/// поиск по имени/портам/адресам/пути/описанию + фильтр направления/действия/принадлежности.
/// </summary>
public sealed class FirewallHubUiTests
{
    /// <summary>Фильтр таблицы правил хаба (зеркало приватного enum NetworkSecurityHubView.RuleFilter).</summary>
    private enum HubRuleFilter
    {
        All,
        Inbound,
        Outbound,
        Blocking,
        Managed
    }

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

    /// <summary>
    /// Предикат фильтрации — прямая копия логики NetworkSecurityHubView.ApplyRuleFilter.
    /// При изменении логики хаба этот метод и тесты обязаны обновляться синхронно.
    /// </summary>
    private static List<FirewallRuleDto> ApplyHubFilter(
        IEnumerable<FirewallRuleDto> rules, string query, HubRuleFilter filter)
    {
        var q = (query ?? string.Empty).Trim().ToLowerInvariant();
        return rules.Where(r =>
        {
            switch (filter)
            {
                case HubRuleFilter.Inbound:
                    if (r.Direction != FirewallDirection.Inbound && r.Direction != FirewallDirection.Both)
                        return false;
                    break;
                case HubRuleFilter.Outbound:
                    if (r.Direction != FirewallDirection.Outbound && r.Direction != FirewallDirection.Both)
                        return false;
                    break;
                case HubRuleFilter.Blocking:
                    if (r.Action != FirewallAction.Block)
                        return false;
                    break;
                case HubRuleFilter.Managed:
                    if (!r.IsManaged)
                        return false;
                    break;
            }

            if (string.IsNullOrEmpty(q))
                return true;

            return (r.Name ?? string.Empty).ToLowerInvariant().Contains(q)
                || (r.LocalPorts ?? string.Empty).ToLowerInvariant().Contains(q)
                || (r.RemotePorts ?? string.Empty).ToLowerInvariant().Contains(q)
                || (r.RemoteAddresses ?? string.Empty).ToLowerInvariant().Contains(q)
                || (r.ApplicationPath ?? string.Empty).ToLowerInvariant().Contains(q)
                || (r.Description ?? string.Empty).ToLowerInvariant().Contains(q);
        }).ToList();
    }

    private static List<FirewallRuleDto> SampleRules() => new()
    {
        new FirewallRuleDto
        {
            Name = "SSH Server",
            Description = "Входящий доступ к SSH",
            Direction = FirewallDirection.Inbound,
            Action = FirewallAction.Allow,
            Protocol = FirewallProtocol.Tcp,
            LocalPorts = "22",
            RemoteAddresses = "Any",
            ApplicationPath = @"C:\Windows\System32\sshd.exe",
            Enabled = true,
            Profiles = FirewallProfile.All,
            IsManaged = false,
        },
        new FirewallRuleDto
        {
            Name = "StartupMonitor Anti-Telemetry (1-клик)",
            Description = "Блокировка исходящей телеметрии Microsoft",
            Direction = FirewallDirection.Outbound,
            Action = FirewallAction.Block,
            Protocol = FirewallProtocol.Any,
            RemoteAddresses = "134.170.0.0/16, 157.56.0.0/14",
            Enabled = true,
            Profiles = FirewallProfile.All,
            Grouping = "StartupMonitor",
            IsManaged = true,
        },
        new FirewallRuleDto
        {
            Name = "Блокировка RDP",
            Description = "Запрет входящих подключений к удалённому рабочему столу",
            Direction = FirewallDirection.Inbound,
            Action = FirewallAction.Block,
            Protocol = FirewallProtocol.Tcp,
            LocalPorts = "3389",
            RemoteAddresses = "185.220.101.0/24",
            Enabled = true,
            Profiles = FirewallProfile.Public,
            Grouping = "StartupMonitor",
            IsManaged = true,
        },
        new FirewallRuleDto
        {
            Name = "Allow Browser HTTPS",
            Description = "Исходящий веб-трафик браузера",
            Direction = FirewallDirection.Outbound,
            Action = FirewallAction.Allow,
            Protocol = FirewallProtocol.Tcp,
            RemotePorts = "443",
            ApplicationPath = @"C:\Program Files\Browser\browser.exe",
            Enabled = true,
            Profiles = FirewallProfile.All,
            IsManaged = false,
        },
    };

    // ─── 1. Поиск по имени ───

    [Theory]
    [InlineData("ssh")]
    [InlineData("SSH")]
    [InlineData("  Ssh  ")]
    public void Search_ByName_MatchesCaseInsensitive(string query)
    {
        var result = ApplyHubFilter(SampleRules(), query, HubRuleFilter.All);
        var single = Assert.Single(result);
        Assert.Equal("SSH Server", single.Name);
    }

    [Fact]
    public void Search_ByRussianWord_MatchesNameAndDescription()
    {
        // «БЛОКИРОВКА» (верхний регистр): совпадение по имени «Блокировка RDP»
        // и по описанию анти-телеметрии («Блокировка исходящей телеметрии...»).
        var result = ApplyHubFilter(SampleRules(), "БЛОКИРОВКА", HubRuleFilter.All);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Name == "Блокировка RDP");
    }

    [Fact]
    public void Search_ByDescription_Matches()
    {
        var result = ApplyHubFilter(SampleRules(), "телеметрии", HubRuleFilter.All);
        var single = Assert.Single(result);
        Assert.Contains("Anti-Telemetry", single.Name);
    }

    [Fact]
    public void Search_ByApplicationPath_Matches()
    {
        var result = ApplyHubFilter(SampleRules(), "browser.exe", HubRuleFilter.All);
        var single = Assert.Single(result);
        Assert.Equal("Allow Browser HTTPS", single.Name);
    }

    // ─── 2. Поиск по порту / адресу ───

    [Theory]
    [InlineData("22", "SSH Server")]
    [InlineData("3389", "Блокировка RDP")]
    [InlineData("443", "Allow Browser HTTPS")]
    public void Search_ByPort_MatchesLocalOrRemotePorts(string query, string expectedName)
    {
        var result = ApplyHubFilter(SampleRules(), query, HubRuleFilter.All);
        Assert.Contains(result, r => r.Name == expectedName);
    }

    [Fact]
    public void Search_ByRemoteAddress_Matches()
    {
        var result = ApplyHubFilter(SampleRules(), "185.220", HubRuleFilter.All);
        var single = Assert.Single(result);
        Assert.Equal("Блокировка RDP", single.Name);
    }

    // ─── 3. Фильтр по типу (направление / действие / принадлежность) ───

    [Fact]
    public void Filter_Inbound_ReturnsInboundAndBothOnly()
    {
        var result = ApplyHubFilter(SampleRules(), string.Empty, HubRuleFilter.Inbound);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(
            r.Direction is FirewallDirection.Inbound or FirewallDirection.Both));
    }

    [Fact]
    public void Filter_Outbound_ReturnsOutboundAndBothOnly()
    {
        var result = ApplyHubFilter(SampleRules(), string.Empty, HubRuleFilter.Outbound);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(
            r.Direction is FirewallDirection.Outbound or FirewallDirection.Both));
    }

    [Fact]
    public void Filter_Blocking_ReturnsBlockActionsOnly()
    {
        var result = ApplyHubFilter(SampleRules(), string.Empty, HubRuleFilter.Blocking);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(FirewallAction.Block, r.Action));
    }

    [Fact]
    public void Filter_Managed_ReturnsStartupMonitorRulesOnly()
    {
        var result = ApplyHubFilter(SampleRules(), string.Empty, HubRuleFilter.Managed);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.True(r.IsManaged));
    }

    [Fact]
    public void Filter_AllWithEmptyQuery_ReturnsEverything()
    {
        var result = ApplyHubFilter(SampleRules(), "   ", HubRuleFilter.All);
        Assert.Equal(4, result.Count);
    }

    // ─── 4. Комбинированные условия ───

    [Fact]
    public void Search_CombinedQueryAndBlockingFilter_NarrowsResults()
    {
        // «блок» встречается в имени «Блокировка RDP» и в описании анти-телеметрии.
        var queryOnly = ApplyHubFilter(SampleRules(), "блок", HubRuleFilter.All);
        Assert.Equal(2, queryOnly.Count);

        // + фильтр входящих оставляет только RDP.
        var combined = ApplyHubFilter(SampleRules(), "блок", HubRuleFilter.Inbound);
        var single = Assert.Single(combined);
        Assert.Equal("Блокировка RDP", single.Name);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var result = ApplyHubFilter(SampleRules(), "такого-правила-не-существует-12345", HubRuleFilter.All);
        Assert.Empty(result);
    }

    [Fact]
    public void Search_NullQuery_TreatedAsEmpty()
    {
        var result = ApplyHubFilter(SampleRules(), null!, HubRuleFilter.Managed);
        Assert.Equal(2, result.Count);
    }

    // ─── 5. Локализация: наличие всех ключей хаба в обеих культурах ───

    [Theory]
    [InlineData("NavFirewall")]
    [InlineData("FirewallHubTitle")]
    [InlineData("RuleActionAllow")]
    [InlineData("RuleActionBlock")]
    [InlineData("RuleDirIn")]
    [InlineData("RuleDirOut")]
    [InlineData("FilteringModeNormal")]
    [InlineData("FilteringModeWhitelist")]
    [InlineData("FilteringModeKillswitch")]
    [InlineData("BtnCreateRule")]
    [InlineData("BtnEditRule")]
    [InlineData("BtnDeleteRule")]
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

    // ─── 6. Локализация: точные значения RU ───

    [Theory]
    [InlineData("NavFirewall", "Сетевой экран")]
    [InlineData("FirewallHubTitle", "Центр сетевой безопасности")]
    [InlineData("RuleActionAllow", "Разрешить")]
    [InlineData("RuleActionBlock", "Блокировать")]
    [InlineData("RuleDirIn", "Входящее")]
    [InlineData("RuleDirOut", "Исходящее")]
    [InlineData("FilteringModeNormal", "Обычный")]
    [InlineData("FilteringModeWhitelist", "Белый список")]
    [InlineData("FilteringModeKillswitch", "Killswitch")]
    [InlineData("BtnCreateRule", "Создать правило")]
    [InlineData("BtnEditRule", "Редактировать")]
    [InlineData("BtnDeleteRule", "Удалить")]
    public void LocalizationResources_RussianValues_AreExact(string key, string expected)
    {
        WithCulture("ru", () =>
        {
            Assert.Equal(expected, LocalizationService.Instance[key]);
        });
    }

    // ─── 7. Локализация: точные значения EN ───

    [Theory]
    [InlineData("NavFirewall", "Firewall & Security")]
    [InlineData("FirewallHubTitle", "Network Security Hub")]
    [InlineData("RuleActionAllow", "Allow")]
    [InlineData("RuleActionBlock", "Block")]
    [InlineData("RuleDirIn", "Inbound")]
    [InlineData("RuleDirOut", "Outbound")]
    [InlineData("FilteringModeNormal", "Normal")]
    [InlineData("FilteringModeWhitelist", "Whitelist")]
    [InlineData("FilteringModeKillswitch", "Killswitch")]
    [InlineData("BtnCreateRule", "Create Rule")]
    [InlineData("BtnEditRule", "Edit")]
    [InlineData("BtnDeleteRule", "Delete")]
    public void LocalizationResources_EnglishValues_AreExact(string key, string expected)
    {
        WithCulture("en", () =>
        {
            Assert.Equal(expected, LocalizationService.Instance[key]);
        });
    }

    // ─── 8. Локализация через типизированные свойства ───

    [Fact]
    public void LocalizationService_TypedProperties_ReturnHubStrings()
    {
        WithCulture("ru", () =>
        {
            var loc = LocalizationService.Instance;
            Assert.Equal("Сетевой экран", loc.NavFirewall);
            Assert.Equal("Центр сетевой безопасности", loc.FirewallHubTitle);
            Assert.Equal("Разрешить", loc.RuleActionAllow);
            Assert.Equal("Блокировать", loc.RuleActionBlock);
            Assert.Equal("Входящее", loc.RuleDirIn);
            Assert.Equal("Исходящее", loc.RuleDirOut);
            Assert.Equal("Обычный", loc.FilteringModeNormal);
            Assert.Equal("Белый список", loc.FilteringModeWhitelist);
            Assert.Equal("Killswitch", loc.FilteringModeKillswitch);
            Assert.Equal("Создать правило", loc.BtnCreateRule);
            Assert.Equal("Редактировать", loc.BtnEditRule);
            Assert.Equal("Удалить", loc.BtnDeleteRule);
        });
        WithCulture("en", () =>
        {
            var loc = LocalizationService.Instance;
            Assert.Equal("Firewall & Security", loc.NavFirewall);
            Assert.Equal("Network Security Hub", loc.FirewallHubTitle);
            Assert.Equal("Allow", loc.RuleActionAllow);
            Assert.Equal("Block", loc.RuleActionBlock);
        });
    }
}
