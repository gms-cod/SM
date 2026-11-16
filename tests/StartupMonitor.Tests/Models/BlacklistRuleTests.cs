using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>Verifies test scenario for BlacklistRuleTests.cs.</summary>
public class BlacklistRuleTests
{
    /// <summary>
    /// <summary>Verifies test scenario for BlacklistRuleTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CreateRule_DefaultValues()
    {
        var rule = new BlacklistRule();

        Assert.NotEmpty(rule.Id);
        Assert.Equal(BlacklistMatchType.Path, rule.MatchType);
        Assert.Equal(BlacklistAction.NotifyAndRemove, rule.Action);
        Assert.NotEqual(default, rule.CreatedAt);
    }

    /// <summary>
    /// <summary>Verifies test scenario for BlacklistRuleTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CreateRule_WithValues()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\Program Files\*\update.exe",
            MatchType = BlacklistMatchType.PathWildcard,
            Action = BlacklistAction.NotifyOnly,
            Comment = "Нежелательное обновление"
        };

        Assert.Equal(BlacklistMatchType.PathWildcard, rule.MatchType);
        Assert.Equal(BlacklistAction.NotifyOnly, rule.Action);
        Assert.Equal("Нежелательное обновление", rule.Comment);
    }

    /// <summary>
    /// <summary>Verifies test scenario for BlacklistRuleTests.cs.</summary>
    /// </summary>
    [Fact]
    public void BlacklistMatchType_AllValues()
    {
        var values = Enum.GetValues<BlacklistMatchType>();
        Assert.Equal(4, values.Length);
    }
}
