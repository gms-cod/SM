// <copyright file="ProcessRuleEvaluatorTests.cs" company="StartupMonitor">
// Copyright (c) StartupMonitor. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using StartupMonitor.Service.Services.ProcessLifecycle;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Набор модульных тестов для компонента ProcessRuleEvaluator.
/// Проверяет детерминированное сопоставление правил, защиту системных процессов и policy parity.
/// </summary>
public sealed class ProcessRuleEvaluatorTests
{
    [Fact]
    public void Evaluate_EmptyProcessName_ReturnsRejected()
    {
        var rules = new[]
        {
            new BlacklistRule { MatchType = BlacklistMatchType.Path, Value = "test.exe", Action = BlacklistAction.NotifyAndRemove }
        };

        var result = ProcessRuleEvaluator.Evaluate("", null, rules);
        Assert.False(result.IsMatched);
        Assert.Equal("EmptyProcessName", result.RejectionReason);

        var whitespaceResult = ProcessRuleEvaluator.Evaluate("   ", null, rules);
        Assert.False(whitespaceResult.IsMatched);
        Assert.Equal("EmptyProcessName", whitespaceResult.RejectionReason);
    }

    [Theory]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("powershell")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("explorer.exe")]
    [InlineData("svchost.exe")]
    [InlineData("mshta.exe")]
    [InlineData("cscript.exe")]
    [InlineData("wscript.exe")]
    public void Evaluate_ProtectedProcessName_NeverMatched(string protectedName)
    {
        var rules = new[]
        {
            new BlacklistRule { MatchType = BlacklistMatchType.Path, Value = protectedName, Action = BlacklistAction.NotifyAndRemove }
        };

        var result = ProcessRuleEvaluator.Evaluate(protectedName, null, rules);
        Assert.False(result.IsMatched);
        Assert.Equal("ProtectedProcess", result.RejectionReason);
        Assert.Null(result.MatchedRule);
    }

    [Fact]
    public void Evaluate_NonKillRules_AreIgnored()
    {
        var rules = new[]
        {
            // NotifyOnly не должен приводить к матчу завершения процесса
            new BlacklistRule { MatchType = BlacklistMatchType.Path, Value = @"C:\App\bad.exe", Action = BlacklistAction.NotifyOnly },
            // Publisher не должен приводить к завершению
            new BlacklistRule { MatchType = BlacklistMatchType.Publisher, Value = "Bad Publisher", Action = BlacklistAction.NotifyAndRemove }
        };

        var result = ProcessRuleEvaluator.Evaluate("bad.exe", @"C:\App\bad.exe", rules);
        Assert.False(result.IsMatched);
        Assert.Equal("NoRuleMatched", result.RejectionReason);
    }

    [Fact]
    public void Evaluate_ExactPathMatch_MatchesSuccessfully()
    {
        var targetRule = new BlacklistRule
        {
            MatchType = BlacklistMatchType.Path,
            Value = @"C:\Tools\Miner.exe",
            Action = BlacklistAction.NotifyAndRemove
        };

        var rules = new[] { targetRule };

        var result = ProcessRuleEvaluator.Evaluate("Miner.exe", @"C:\Tools\Miner.exe", rules);
        Assert.True(result.IsMatched);
        Assert.Same(targetRule, result.MatchedRule);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void Evaluate_WildcardPathMatch_MatchesSuccessfully()
    {
        var targetRule = new BlacklistRule
        {
            MatchType = BlacklistMatchType.PathWildcard,
            Value = @"*test_trojan*.exe",
            Action = BlacklistAction.NotifyAndRemove
        };

        var rules = new[] { targetRule };

        var result = ProcessRuleEvaluator.Evaluate("test_trojan_v2.exe", @"D:\Downloads\test_trojan_v2.exe", rules);
        Assert.True(result.IsMatched);
        Assert.Same(targetRule, result.MatchedRule);
    }

    [Fact]
    public void Evaluate_UnknownPath_AllowsOnlyFilenameMatchWithoutSlashes()
    {
        var pathRuleWithDirectory = new BlacklistRule
        {
            MatchType = BlacklistMatchType.Path,
            Value = @"C:\Program Files\App\app.exe",
            Action = BlacklistAction.NotifyAndRemove
        };

        var filenameOnlyRule = new BlacklistRule
        {
            MatchType = BlacklistMatchType.Path,
            Value = "evil_app.exe",
            Action = BlacklistAction.NotifyAndRemove
        };

        var rules = new[] { pathRuleWithDirectory, filenameOnlyRule };

        // 1. Процесс с неизвестным путем НЕ должен матчиться с правилом, содержащим путь (защита Chrome/legit app)
        var result1 = ProcessRuleEvaluator.Evaluate("app.exe", null, new[] { pathRuleWithDirectory });
        Assert.False(result1.IsMatched);

        // 2. Процесс с неизвестным путем матчится с правилом без слешей
        var result2 = ProcessRuleEvaluator.Evaluate("evil_app.exe", null, new[] { filenameOnlyRule });
        Assert.True(result2.IsMatched);
        Assert.Same(filenameOnlyRule, result2.MatchedRule);
    }

    [Fact]
    public void BlacklistRuleSnapshot_FiltersNonKillRulesAndTracksGeneration()
    {
        var rules = new[]
        {
            new BlacklistRule { MatchType = BlacklistMatchType.Path, Value = "a.exe", Action = BlacklistAction.NotifyAndRemove },
            new BlacklistRule { MatchType = BlacklistMatchType.PathWildcard, Value = "*b.exe", Action = BlacklistAction.NotifyAndRemove },
            new BlacklistRule { MatchType = BlacklistMatchType.Path, Value = "c.exe", Action = BlacklistAction.NotifyOnly },
            new BlacklistRule { MatchType = BlacklistMatchType.Publisher, Value = "Microsoft", Action = BlacklistAction.NotifyAndRemove }
        };

        var snapshot = new BlacklistRuleSnapshot(42, rules);
        Assert.Equal(42L, snapshot.Generation);
        Assert.Equal(2, snapshot.ActiveRules.Count);
        Assert.Contains(snapshot.ActiveRules, r => r.Value == "a.exe");
        Assert.Contains(snapshot.ActiveRules, r => r.Value == "*b.exe");
    }
}

