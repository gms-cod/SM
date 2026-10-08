using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>
/// Тесты для модели BlacklistRule: проверка значений по умолчанию, свойств при создании, количества типов MatchType.
/// </summary>
public class BlacklistRuleTests
{
    /// <summary>
    /// Проверяет, что при создании пустого правила проставляются ID, MatchType.Path, Action.NotifyAndRemove и CreatedAt.
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
    /// Проверяет, что при создании правила с заполненными полями все значения сохраняются корректно.
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
    /// Проверяет количество значений в BlacklistMatchType (4: Path, PathWildcard, Publisher, SHA256).
    /// </summary>
    [Fact]
    public void BlacklistMatchType_AllValues()
    {
        var values = Enum.GetValues<BlacklistMatchType>();
        Assert.Equal(4, values.Length);
    }
}
