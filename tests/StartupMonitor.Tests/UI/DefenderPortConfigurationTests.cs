using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Views;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет настоящий алгоритм CRUD портов без создания окна или обращения к установленной службе.</summary>
public sealed class DefenderPortConfigurationTests
{
    /// <summary>Удаление убирает общие и процессные правила порта, не меняя соседние порты.</summary>
    [Fact]
    public void DeletePort_RemovesAllMatchingRules()
    {
        var config = new PortDefenderConfigDto
        {
            ProtectedPorts = [8080, 9090],
            Rules = [new() { LocalPort = 8080, IsEnabled = false }, new() { LocalPort = 8080, ProcessPath = @"C:\app.exe" }, new() { LocalPort = 9090 }]
        };
        NetworkSecurityHubView.ApplyDefenderPortChange(config, 8080, null);
        Assert.Equal(new[] { 9090 }, config.ProtectedPorts);
        Assert.Equal(9090, Assert.Single(config.Rules).LocalPort);
    }

    /// <summary>Перенос сохраняет выключение, индивидуальные пороги, процесс и белый список каждого правила.</summary>
    [Fact]
    public void EditPort_PreservesEveryRuleSetting()
    {
        var first = new PortDefenderRuleConfig { LocalPort = 8080, IsEnabled = false, ThresholdCount = 27, WindowUnit = TimeWindowUnit.Hour,
            BanDurationMinutes = 123, AutoEscalate = false, WhitelistIps = ["198.51.100.25"] };
        var second = new PortDefenderRuleConfig { LocalPort = 8080, ProcessPath = @"C:\app.exe", ThresholdCount = 31 };
        var config = new PortDefenderConfigDto { ProtectedPorts = [8080], Rules = [first, second] };
        NetworkSecurityHubView.ApplyDefenderPortChange(config, 8080, 8443);
        Assert.Equal(new[] { 8443 }, config.ProtectedPorts);
        Assert.All(config.Rules, rule => Assert.Equal(8443, rule.LocalPort));
        Assert.False(first.IsEnabled);
        Assert.Equal(27, first.ThresholdCount);
        Assert.Equal(TimeWindowUnit.Hour, first.WindowUnit);
        Assert.Equal(123, first.BanDurationMinutes);
        Assert.False(first.AutoEscalate);
        Assert.Equal(new[] { "198.51.100.25" }, first.WhitelistIps);
        Assert.Equal(@"C:\app.exe", second.ProcessPath);
        Assert.Equal(31, second.ThresholdCount);
    }

    /// <summary>Существующий целевой порт нельзя молча объединить с другими правилами.</summary>
    [Fact]
    public void EditPort_ExistingTargetIsRejectedWithoutMutatingRules()
    {
        var config = new PortDefenderConfigDto { ProtectedPorts = [8080, 9090], Rules = [new() { LocalPort = 8080 }] };
        Assert.Throws<InvalidOperationException>(() => NetworkSecurityHubView.ApplyDefenderPortChange(config, 8080, 9090));
        Assert.Equal(new[] { 8080, 9090 }, config.ProtectedPorts);
        Assert.Equal(8080, Assert.Single(config.Rules).LocalPort);
    }
}
