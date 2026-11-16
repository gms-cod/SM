using System.Dynamic;
using System.Reflection;
using System.Runtime.InteropServices;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
public sealed class FirewallProjectionRegressionTests
{
    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    [Fact]
    public void RuleProjection_DynamicMembersPreserveValueConversions()
    {
        var rule = new DynamicRule(new Dictionary<string, object?>
        {
            ["Name"] = "Application rule", ["Direction"] = "2", ["Action"] = 1L, ["Protocol"] = "6",
            ["Profiles"] = "3", ["Enabled"] = "false", ["ApplicationName"] = @"C:\Apps\Application.exe",
            ["RemoteAddresses"] = "198.51.100.1", ["Grouping"] = "Group"
        });
        var result = ReadCompleteRule(rule);
        Assert.Equal("Application rule", result.Name);
        Assert.Equal(FirewallDirection.Outbound, result.Direction);
        Assert.Equal(FirewallAction.Allow, result.Action);
        Assert.Equal(FirewallProtocol.Tcp, result.Protocol);
        Assert.Equal(FirewallProfile.Domain | FirewallProfile.Private, result.Profiles);
        Assert.False(result.Enabled);
        Assert.Equal(@"C:\Apps\Application.exe", result.ApplicationPath);
        Assert.Equal("198.51.100.1", result.RemoteAddresses);
        Assert.Equal("Group", result.Grouping);
    }

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    [Fact]
    public void RuleProjection_UnavailableFieldsKeepPriorFallbacks()
    {
        var rule = new DynamicRule(new Dictionary<string, object?>
        {
            ["Name"] = "StartupMonitor_Test", ["Action"] = "not-an-integer", ["Protocol"] = null,
            ["Profiles"] = null, ["Enabled"] = null
        }, ["Direction", "ApplicationName"]);
        var result = ReadCompleteRule(rule);
        Assert.Equal(FirewallDirection.Inbound, result.Direction);
        Assert.Equal(FirewallAction.Block, result.Action);
        Assert.Equal(FirewallProtocol.Any, result.Protocol);
        Assert.Equal(FirewallProfile.All, result.Profiles);
        Assert.True(result.Enabled);
        Assert.Null(result.ApplicationPath);
        Assert.True(result.IsManaged);
    }

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(0)]
    [InlineData("false")]
    public void RuleProjection_ExplicitFalseIsNotReplaced(object enabled)
    {
        var rule = new DynamicRule(new Dictionary<string, object?> { ["Name"] = "Rule", ["Enabled"] = enabled });
        Assert.False(ReadCompleteRule(rule).Enabled);
    }

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    [Fact]
    public void UnknownProperty_PreservesReflectionFallback()
        => Assert.Equal("Custom value", WindowsFirewallService.ReadRuleProperty(new AdditionalRuleProperty(), "CustomLabel"));

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    [Fact]
    public void StatusProjection_ReadsOnlyRequiredFieldsAndPreservesCounts()
    {
        var rules = new[]
        {
            new DynamicRule(new Dictionary<string, object?> { ["Name"] = "Foreign rule" }, ["RemoteAddresses"]),
            new DynamicRule(new Dictionary<string, object?> { ["Name"] = "StartupMonitor_Custom" }, ["RemoteAddresses"]),
            new DynamicRule(new Dictionary<string, object?> { ["Name"] = "startupmonitor_custom" }, ["RemoteAddresses"]),
            new DynamicRule(new Dictionary<string, object?> { ["Name"] = WindowsFirewallService.AggregatedRuleNameIn, ["RemoteAddresses"] = "198.51.100.1,198.51.100.1,*, " }),
            new DynamicRule(new Dictionary<string, object?> { ["Name"] = WindowsFirewallService.AggregatedRuleNameOut, ["RemoteAddresses"] = "198.51.100.1,198.51.100.2" }),
            new DynamicRule(new Dictionary<string, object?> { ["Name"] = WindowsFirewallService.AggregatedRuleNameIn.ToLowerInvariant() }, ["RemoteAddresses"]),
            new DynamicRule(new Dictionary<string, object?> { ["Name"] = null }, ["RemoteAddresses"])
        };
        var result = WindowsFirewallService.ReadManagedRuleCounts(rules);
        Assert.Equal(5, result.ManagedRulesCount);
        Assert.Equal(4, result.BlockedIpsCount);
        Assert.All(rules, rule => Assert.Equal("Name", rule.Reads[0]));
        Assert.All(rules.SelectMany(rule => rule.Reads), name => Assert.True(name is "Name" or "RemoteAddresses"));
        Assert.Equal(2, rules.Sum(rule => rule.Reads.Count(name => name == "RemoteAddresses")));
    }

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    [Fact]
    public void Projection_RejectsInvalidArguments()
    {
        // Verifies component behavior and assertions.
        Assert.Throws<ArgumentNullException>(() => WindowsFirewallService.ReadManagedRuleCounts(null!));
        Assert.Throws<ArgumentNullException>(() => WindowsFirewallService.ReadRuleProperty(null!, "Name"));
        Assert.Throws<ArgumentException>(() => WindowsFirewallService.ReadRuleProperty(new AdditionalRuleProperty(), string.Empty));
    }

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    private static FirewallRuleDto ReadCompleteRule(object rule)
    {
        var method = typeof(WindowsFirewallService).GetMethod("ReadRule", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("Проекция ReadRule отсутствует.");
        return Assert.IsType<FirewallRuleDto>(method.Invoke(null, [rule]));
    }

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    public sealed class DynamicRule : DynamicObject
    {
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        private readonly IReadOnlyDictionary<string, object?> _values;
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        private readonly HashSet<string> _unavailable;
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        public List<string> Reads { get; } = new();

        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        public DynamicRule(IReadOnlyDictionary<string, object?> values, IEnumerable<string>? unavailable = null)
        {
            _values = values;
            _unavailable = new HashSet<string>(unavailable ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            Reads.Add(binder.Name);
            if (_unavailable.Contains(binder.Name)) throw new COMException("Injected property failure: " + binder.Name);
            _values.TryGetValue(binder.Name, out result);
            return true;
        }
    }

    /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
    public sealed class AdditionalRuleProperty
    {
        /// <summary>Verifies test scenario for FirewallProjectionRegressionTests.cs.</summary>
        public string CustomLabel => "Custom value";
    }
}
