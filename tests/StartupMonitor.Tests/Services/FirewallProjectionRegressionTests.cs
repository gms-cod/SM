using System.Dynamic;
using System.Reflection;
using System.Runtime.InteropServices;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет узкую COM-проекцию и прежние fallback без чтения или изменения правил рабочей машины.</summary>
public sealed class FirewallProjectionRegressionTests
{
    /// <summary>Кэшируемые динамические getter поддерживают исходные преобразования чисел, строк и логических значений.</summary>
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

    /// <summary>Сбой getter, null и неверный тип сохраняют прежние значения fallback полного DTO.</summary>
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

    /// <summary>Явные false, ноль и строковое false не заменяются значением fallback=true.</summary>
    /// <param name="enabled">Совместимое с Convert.ToBoolean исходное значение.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(0)]
    [InlineData("false")]
    public void RuleProjection_ExplicitFalseIsNotReplaced(object enabled)
    {
        var rule = new DynamicRule(new Dictionary<string, object?> { ["Name"] = "Rule", ["Enabled"] = enabled });
        Assert.False(ReadCompleteRule(rule).Enabled);
    }

    /// <summary>Неизвестное имя остаётся совместимым с прежним reflection-чтением публичного свойства.</summary>
    [Fact]
    public void UnknownProperty_PreservesReflectionFallback()
        => Assert.Equal("Custom value", WindowsFirewallService.ReadRuleProperty(new AdditionalRuleProperty(), "CustomLabel"));

    /// <summary>Статус читает только Name и адреса двух точных агрегатов, сохраняя регистр префикса и дубликаты адресов.</summary>
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

    /// <summary>Новая граница чтения отвергает отсутствующее правило, коллекцию и пустое имя свойства.</summary>
    [Fact]
    public void Projection_RejectsInvalidArguments()
    {
        // Намеренные null! проверяют валидацию внутреннего контракта и никогда не передаются рабочим кодом.
        Assert.Throws<ArgumentNullException>(() => WindowsFirewallService.ReadManagedRuleCounts(null!));
        Assert.Throws<ArgumentNullException>(() => WindowsFirewallService.ReadRuleProperty(null!, "Name"));
        Assert.Throws<ArgumentException>(() => WindowsFirewallService.ReadRuleProperty(new AdditionalRuleProperty(), string.Empty));
    }

    /// <summary>Вызывает существующую полную проекцию для проверки преобразований её безопасных getter.</summary>
    /// <param name="rule">Публичный динамический адаптер, эквивалентный свойствам IDispatch.</param>
    /// <returns>Настоящий DTO метода ReadRule.</returns>
    private static FirewallRuleDto ReadCompleteRule(object rule)
    {
        var method = typeof(WindowsFirewallService).GetMethod("ReadRule", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("Проекция ReadRule отсутствует.");
        return Assert.IsType<FirewallRuleDto>(method.Invoke(null, [rule]));
    }

    /// <summary>Публичный динамический адаптер фиксирует реальные обращения getter и может имитировать отказ отдельного COM-поля.</summary>
    public sealed class DynamicRule : DynamicObject
    {
        /// <summary>Исходные значения динамических свойств.</summary>
        private readonly IReadOnlyDictionary<string, object?> _values;
        /// <summary>Поля, чтение которых диагностически завершается отказом.</summary>
        private readonly HashSet<string> _unavailable;
        /// <summary>Последовательность обращений к свойствам для проверки узкой проекции.</summary>
        public List<string> Reads { get; } = new();

        /// <summary>Создаёт адаптер без системного COM и без возможности изменения firewall.</summary>
        /// <param name="values">Явные исходные значения свойств.</param>
        /// <param name="unavailable">Поля с принудительным отказом чтения.</param>
        public DynamicRule(IReadOnlyDictionary<string, object?> values, IEnumerable<string>? unavailable = null)
        {
            _values = values;
            _unavailable = new HashSet<string>(unavailable ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        /// <summary>Возвращает значение поля или контролируемый COM-отказ и записывает факт чтения.</summary>
        /// <param name="binder">Запрашиваемое динамическое имя.</param>
        /// <param name="result">Исходное значение либо null для отсутствующего поля.</param>
        /// <returns>True, поскольку адаптер поддерживает запрос любого поля с null-fallback.</returns>
        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            Reads.Add(binder.Name);
            if (_unavailable.Contains(binder.Name)) throw new COMException("Injected property failure: " + binder.Name);
            _values.TryGetValue(binder.Name, out result);
            return true;
        }
    }

    /// <summary>CLR-свойство вне известной COM-проекции проверяет совместимый reflection-fallback.</summary>
    public sealed class AdditionalRuleProperty
    {
        /// <summary>Дополнительное публичное поле, отсутствующее в фиксированном наборе getter.</summary>
        public string CustomLabel => "Custom value";
    }
}
