using System.Collections;
using System.Dynamic;
using System.Runtime.InteropServices;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет настоящую логику записи/компенсации на управляемом INetFwRules-адаптере без изменения правил ОС.</summary>
public sealed class FirewallPolicyTransactionTests
{
    private const string AppPath = @"C:\Apps\client.exe";

    /// <summary>Пустые поля обновления действительно снимают прежние ограничения и привязки.</summary>
    [Fact]
    public void Update_ClearsPreviouslyConfiguredApplicationServiceAndRestrictions()
    {
        var fixture = new Fixture();
        var existing = fixture.Add("Custom", FirewallAction.Block, FirewallDirection.Outbound);
        existing.Values["ApplicationName"] = AppPath;
        existing.Values["ServiceName"] = "OldService";
        existing.Values["Description"] = "Old description";
        existing.Values["Protocol"] = 6;
        existing.Values["LocalPorts"] = "80";
        existing.Values["RemotePorts"] = "443";
        existing.Values["RemoteAddresses"] = "8.8.8.8";
        var result = fixture.Service.CreateOrUpdateRule(new FirewallRuleDto
        {
            Name = "Custom", Direction = FirewallDirection.Outbound, Action = FirewallAction.Allow,
            Protocol = FirewallProtocol.Tcp, Profiles = FirewallProfile.All, Enabled = true
        });
        Assert.True(result.Success, result.Message);
        var updated = Assert.Single(fixture.Policy.Rules.Items);
        Assert.NotSame(existing, updated);
        Assert.Equal("", updated.Values["ApplicationName"]);
        Assert.Equal("", updated.Values["ServiceName"]);
        Assert.Equal("", updated.Values["Description"]);
        Assert.Equal("*", updated.Values["LocalPorts"]);
        Assert.Equal("*", updated.Values["RemotePorts"]);
        Assert.Equal("*", updated.Values["RemoteAddresses"]);
    }

    /// <summary>Отказ позднего setter восстанавливает уже изменённые свойства одного существующего правила.</summary>
    [Fact]
    public void Update_LateSetterFailure_RestoresOriginalRule()
    {
        var fixture = new Fixture();
        var existing = fixture.Add("Custom", FirewallAction.Block, FirewallDirection.Outbound);
        existing.Values["Description"] = "Original";
        existing.Values["RemoteAddresses"] = "8.8.8.8";
        var before = new Dictionary<string, object?>(existing.Values);
        existing.FailNextSet = "RemoteAddresses";
        var result = fixture.Service.CreateOrUpdateRule(new FirewallRuleDto
        {
            Name = "Custom", Description = "Changed", Direction = FirewallDirection.Outbound,
            Action = FirewallAction.Allow, Protocol = FirewallProtocol.Tcp, Profiles = FirewallProfile.Private,
            Enabled = true, RemoteAddresses = "1.1.1.1"
        });
        Assert.False(result.Success);
        Assert.DoesNotContain("PartialFailure", result.Message);
        Assert.Equal(before.OrderBy(item => item.Key), existing.Values.OrderBy(item => item.Key));
    }

    /// <summary>Отказ второй записи пары удаляет уже созданное первое направление.</summary>
    [Fact]
    public void CreatePair_SecondAddFails_RemovesFirstDirection()
    {
        var fixture = new Fixture();
        fixture.Policy.Rules.FailNextOutboundAdd = true;
        var result = fixture.Service.ApplyPolicyRule(WindowsFirewallService.BuildApplicationRule(AppPath, "client.exe", FirewallAction.Block));
        Assert.False(result.Success);
        Assert.Empty(fixture.Policy.Rules.Items);
    }

    /// <summary>Ошибка сохранения настроек восстанавливает прежнюю пару и не подтверждает новое действие.</summary>
    [Fact]
    public void ApplyPolicy_PersistenceFails_RestoresBothOriginalDirections()
    {
        var fixture = new Fixture();
        var original = WindowsFirewallService.BuildApplicationRule(AppPath, "Old name", FirewallAction.Block);
        Assert.True(fixture.Service.ApplyPolicyRule(original).Success);
        var result = fixture.Service.ApplyPolicyRule(WindowsFirewallService.BuildApplicationRule(AppPath, "New name", FirewallAction.Allow),
            () => throw new IOException("Controlled settings failure"));
        Assert.False(result.Success);
        Assert.Equal(2, fixture.Policy.Rules.Items.Count);
        Assert.All(fixture.Policy.Rules.Items, item =>
        {
            Assert.Equal(0, item.Values["Action"]);
            Assert.Equal(original.Description, item.Values["Description"]);
            Assert.Equal(true, item.Values["Enabled"]);
        });
    }

    /// <summary>Неудавшаяся компенсация явно отличается от обычного отказа без изменений.</summary>
    [Fact]
    public void Update_RollbackAlsoFails_ReturnsPartialFailure()
    {
        var fixture = new Fixture();
        var existing = fixture.Add("Custom", FirewallAction.Block, FirewallDirection.Outbound);
        existing.FailEverySet = "RemoteAddresses";
        var result = fixture.Service.CreateOrUpdateRule(new FirewallRuleDto
        {
            Name = "Custom", Direction = FirewallDirection.Outbound, Action = FirewallAction.Allow,
            Protocol = FirewallProtocol.Any, Enabled = true, Profiles = FirewallProfile.All
        });
        Assert.False(result.Success);
        Assert.Contains("PartialFailure", result.Message);
    }

    /// <summary>Разные источники выбора сходятся к одной политике; узкие и чужие правила остаются независимыми.</summary>
    [Fact]
    public void ApplyPolicy_MigratesEquivalentGeneratedDuplicates_PreservesNarrowCustomRules()
    {
        var fixture = new Fixture();
        fixture.Add("StartupMonitor_Allow_client_old_Auto_Out", FirewallAction.Allow, FirewallDirection.Outbound, AppPath);
        fixture.Add("StartupMonitor_Access_App_old_Managed_In", FirewallAction.Allow, FirewallDirection.Inbound, AppPath);
        fixture.Add("StartupMonitor_Block_client_old_Auto_Out", FirewallAction.Block, FirewallDirection.Outbound, AppPath);
        var narrow = fixture.Add("StartupMonitor_Block_client_port_Auto_Out", FirewallAction.Block, FirewallDirection.Outbound, AppPath);
        narrow.Values["Protocol"] = 6;
        narrow.Values["RemotePorts"] = "25";
        var custom = fixture.Add("StartupMonitor_CustomAdministrator", FirewallAction.Block, FirewallDirection.Outbound, AppPath);
        var foreign = fixture.Add("Foreign application policy", FirewallAction.Allow, FirewallDirection.Outbound, AppPath);
        var rule = WindowsFirewallService.BuildApplicationRule(AppPath, "Display name one", FirewallAction.Allow);
        Assert.True(fixture.Service.ApplyPolicyRule(rule).Success);
        Assert.True(fixture.Service.ApplyPolicyRule(WindowsFirewallService.BuildApplicationRule(@"C:/Apps/./client.exe", "Different name", FirewallAction.Allow)).Success);
        Assert.Equal(5, fixture.Policy.Rules.Items.Count);
        Assert.Contains(narrow, fixture.Policy.Rules.Items);
        Assert.Contains(custom, fixture.Policy.Rules.Items);
        Assert.Contains(foreign, fixture.Policy.Rules.Items);
        Assert.Equal(2, fixture.Policy.Rules.Items.Count(item => Convert.ToString(item.Values["Name"])!.StartsWith("StartupMonitor_Application_", StringComparison.Ordinal)));
    }

    /// <summary>Ошибка удаления прежней политики не оставляет новую разрешающую пару.</summary>
    [Fact]
    public void ApplyPolicy_OppositeCleanupFails_RestoresOriginalState()
    {
        var fixture = new Fixture();
        var original = fixture.Add("StartupMonitor_Block_old_Auto_Out", FirewallAction.Block, FirewallDirection.Outbound, AppPath);
        fixture.Policy.Rules.FailNextRemoveName = Convert.ToString(original.Values["Name"]);
        var result = fixture.Service.ApplyPolicyRule(WindowsFirewallService.BuildApplicationRule(AppPath, "client", FirewallAction.Allow));
        Assert.False(result.Success);
        Assert.Same(original, Assert.Single(fixture.Policy.Rules.Items));
        Assert.Equal(0, original.Values["Action"]);
    }

    /// <summary>Неполное перечисление отменяет операцию до первой мутации и не возвращает частичный снимок UI.</summary>
    [Fact]
    public void Snapshot_EnumeratorFailure_DoesNotMutateOrReturnPartialRules()
    {
        var fixture = new Fixture();
        fixture.Add("Existing", FirewallAction.Block, FirewallDirection.Outbound);
        fixture.Policy.Rules.FailEnumeration = true;
        var result = fixture.Service.ApplyPolicyRule(WindowsFirewallService.BuildApplicationRule(AppPath, "client", FirewallAction.Allow));
        Assert.False(result.Success);
        Assert.Single(fixture.Policy.Rules.Items);
        Assert.Equal(0, fixture.Policy.Rules.Mutations);
        Assert.Throws<IOException>(() => fixture.Service.GetAllRules());
    }

    /// <summary>Ошибка getter обязательного ограничения не превращается в неограниченное разрешение.</summary>
    [Fact]
    public void Snapshot_RestrictionGetterFails_DoesNotSuppressAlerts()
    {
        var fixture = new Fixture();
        var rule = fixture.Add("Foreign", FirewallAction.Allow, FirewallDirection.Outbound, AppPath);
        rule.FailRead = "RemoteAddresses";
        Assert.Throws<COMException>(() => fixture.Service.GetUnrestrictedOutboundAllowApplicationRulePaths());
        Assert.False(fixture.Service.ApplyPolicyRule(WindowsFirewallService.BuildApplicationRule(AppPath, "client", FirewallAction.Block)).Success);
        Assert.Equal(0, fixture.Policy.Rules.Mutations);
    }

    /// <summary>Узкие правила разных типов не подавляют запросы для всей программы.</summary>
    /// <param name="restriction">Ограничение существующего разрешения.</param>
    [Theory]
    [InlineData("protocol")]
    [InlineData("remote")]
    [InlineData("local")]
    [InlineData("service")]
    [InlineData("profile")]
    [InlineData("interface")]
    [InlineData("disabled")]
    public void AllowedPaths_RejectsRestrictedRules(string restriction)
    {
        var fixture = new Fixture();
        var rule = fixture.Add("Foreign", FirewallAction.Allow, FirewallDirection.Outbound, AppPath);
        switch (restriction)
        {
            case "protocol": rule.Values["Protocol"] = 17; break;
            case "remote": rule.Values["RemoteAddresses"] = "8.8.8.8"; break;
            case "local": rule.Values["LocalAddresses"] = "192.168.1.10"; break;
            case "service": rule.Values["ServiceName"] = "Dnscache"; break;
            case "profile": rule.Values["Profiles"] = 2; break;
            case "interface": rule.Values["InterfaceTypes"] = "Wireless"; break;
            case "disabled": rule.Values["Enabled"] = false; break;
        }
        Assert.Empty(fixture.Service.GetUnrestrictedOutboundAllowApplicationRulePaths());
    }

    /// <summary>Полное разрешение должно покрывать все одновременно активные профили, не только один из них.</summary>
    [Fact]
    public void AllowedPaths_RequiresAllActiveProfiles()
    {
        var fixture = new Fixture();
        var rule = fixture.Add("Foreign", FirewallAction.Allow, FirewallDirection.Outbound, AppPath);
        fixture.Policy.CurrentProfileTypes = 6;
        rule.Values["Profiles"] = 2;
        Assert.Empty(fixture.Service.GetUnrestrictedOutboundAllowApplicationRulePaths());
        rule.Values["Profiles"] = 6;
        Assert.Contains(AppPath.ToLowerInvariant(), fixture.Service.GetUnrestrictedOutboundAllowApplicationRulePaths());
    }

    /// <summary>Удаление/переключение общих правил инвалидирует ревизию; переключение ICMP сохраняет исходный протокол.</summary>
    [Fact]
    public void GenericMutations_AdvanceRevisionAndPreserveNonTcpProtocol()
    {
        var fixture = new Fixture();
        var original = fixture.Add("Icmp", FirewallAction.Block, FirewallDirection.Outbound);
        original.Values["Protocol"] = 1;
        original.Values["IcmpTypesAndCodes"] = "8:*";
        var before = fixture.Service.PolicyRevision;
        Assert.True(fixture.Service.ToggleRule("Icmp", FirewallDirection.Outbound, false).Success);
        Assert.True(fixture.Service.PolicyRevision > before);
        Assert.Equal(1, original.Values["Protocol"]);
        Assert.Equal("8:*", original.Values["IcmpTypesAndCodes"]);
        before = fixture.Service.PolicyRevision;
        Assert.True(fixture.Service.DeleteRule("Icmp", FirewallDirection.Outbound).Success);
        Assert.True(fixture.Service.PolicyRevision > before);
        Assert.Empty(fixture.Policy.Rules.Items);
    }

    /// <summary>Недоступная политика возвращает Unknown через false, а не выдуманный разрешающий Normal.</summary>
    [Fact]
    public void FilteringMode_Unavailable_DoesNotReturnNormalAsKnownState()
    {
        var service = new WindowsFirewallService { PolicyFactoryOverride = () => throw new COMException("Unavailable") };
        Assert.False(service.TryGetFilteringMode(out _));
        Assert.ThrowsAny<Exception>(() => service.GetFilteringMode());
        var fixture = new Fixture();
        Assert.True(fixture.Service.TryGetFilteringMode(out var normal));
        Assert.Equal(StartupMonitor.Shared.Models.Firewall.FirewallFilteringMode.Normal, normal);
    }

    /// <summary>Отказ второго профиля при включении killswitch восстанавливает профили и удаляет уже созданную пару.</summary>
    [Fact]
    public void FilteringMode_ProfileWriteFails_RestoresProfilesAndKillswitchRules()
    {
        var fixture = new Fixture();
        fixture.Policy.FirewallEnabled[1] = false;
        fixture.Policy.FirewallEnabled[2] = false;
        fixture.Policy.FirewallEnabled[4] = false;
        fixture.Policy.FirewallEnabled.FailNextSetProfile = 2;
        var result = fixture.Service.SetFilteringMode(StartupMonitor.Shared.Models.Firewall.FirewallFilteringMode.KillswitchBlockAll);
        Assert.False(result.Success);
        Assert.DoesNotContain("PartialFailure", result.Message);
        Assert.Empty(fixture.Policy.Rules.Items);
        Assert.All(new[] { 1, 2, 4 }, profile => Assert.False(fixture.Policy.FirewallEnabled[profile]));
        Assert.True(fixture.Service.TryGetFilteringMode(out var restored));
        Assert.Equal(StartupMonitor.Shared.Models.Firewall.FirewallFilteringMode.Disabled, restored);
    }

    /// <summary>Подтверждённый режим совпадает с реальными свойствами всех профилей и парой killswitch.</summary>
    /// <param name="modeValue">Значение реального Shared-контракта режима.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FilteringMode_Success_ReadsBackRequestedState(int modeValue)
    {
        var fixture = new Fixture();
        var mode = (StartupMonitor.Shared.Models.Firewall.FirewallFilteringMode)modeValue;
        var result = fixture.Service.SetFilteringMode(mode);
        Assert.True(result.Success, result.Message);
        Assert.True(fixture.Service.TryGetFilteringMode(out var actual));
        Assert.Equal(mode, actual);
        Assert.True(fixture.Service.PolicyRevision > 0);
    }

    /// <summary>Ручное выключение полного собственного правила снимает устаревшее доверие списка.</summary>
    [Fact]
    public void Reconcile_DisabledWholeAppRule_RemovesObsoleteWhitelistMembership()
    {
        var fixture = new Fixture();
        var directory = Path.Combine(Path.GetTempPath(), "StartupMonitor.PolicyTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            var cfg = store.Load();
            cfg.Settings.FirewallWhitelistedApps.Add(AppPath);
            store.Save(cfg);
            var full = fixture.Add("StartupMonitor_Allow_old_Auto_Out", FirewallAction.Allow, FirewallDirection.Outbound, AppPath);
            full.Values["Enabled"] = false;
            using var tracker = new ProcessNetworkTracker();
            using var alerts = new NetworkConnectionAlertService(tracker, fixture.Service, store, sessionFirewall: new TestSessionFirewallPolicy());
            var matrix = new AccessMatrixCoordinatorService(fixture.Service, alerts, store, tracker);
            var result = matrix.ReconcileApplicationLists(AppPath);
            Assert.True(result.Success, result.Message);
            Assert.Empty(store.Load().Settings.FirewallWhitelistedApps);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Фикстура соединяет настоящий сервис только с управляемой коллекцией, исключая обращения к ОС.</summary>
    private sealed class Fixture
    {
        internal NativePolicy Policy { get; } = new();
        internal WindowsFirewallService Service { get; }

        /// <summary>Устанавливает фабрики исключительно локального экземпляра.</summary>
        internal Fixture() => Service = new WindowsFirewallService
        {
            PolicyFactoryOverride = () => Policy, RuleFactoryOverride = () => new NativeRule()
        };

        /// <summary>Добавляет исходное правило непосредственно в изолированную коллекцию фикстуры.</summary>
        internal NativeRule Add(string name, FirewallAction action, FirewallDirection direction, string? path = null)
        {
            var rule = new NativeRule();
            rule.Values["Name"] = name;
            rule.Values["Action"] = (int)action;
            rule.Values["Direction"] = (int)direction;
            rule.Values["ApplicationName"] = path ?? "";
            Policy.Rules.Items.Add(rule);
            return rule;
        }
    }

    /// <summary>Публичный late-bound адаптер INetFwPolicy2 с профилями без системных вызовов.</summary>
    public sealed class NativePolicy
    {
        public NativeRules Rules { get; } = new();
        public int CurrentProfileTypes { get; set; } = 4;
        public ProfileValues<bool> FirewallEnabled { get; } = new(true);
        public ProfileValues<int> DefaultOutboundAction { get; } = new(1);
    }

    /// <summary>Индексированные свойства профилей моделируют контролируемый отказ конкретного setter.</summary>
    public sealed class ProfileValues<T>
    {
        private readonly Dictionary<int, T> _values;
        public int? FailNextSetProfile { get; set; }

        /// <summary>Заполняет три независимых профиля одинаковым исходным значением.</summary>
        public ProfileValues(T initial) => _values = new() { [1] = initial, [2] = initial, [4] = initial };

        /// <summary>Читает профиль либо записывает его с единственным управляемым отказом.</summary>
        public T this[int profile]
        {
            get => _values[profile];
            set
            {
                if (FailNextSetProfile == profile)
                {
                    FailNextSetProfile = null;
                    throw new COMException("Controlled profile setter failure");
                }
                _values[profile] = value;
            }
        }
    }

    /// <summary>Коллекция поддерживает контролируемые отказы второй записи, удаления и полного обхода.</summary>
    public sealed class NativeRules : IEnumerable
    {
        public List<NativeRule> Items { get; } = [];
        public bool FailNextOutboundAdd { get; set; }
        public bool FailEnumeration { get; set; }
        public string? FailNextRemoveName { get; set; }
        public int Mutations { get; private set; }

        /// <summary>Добавляет реальный объект адаптера; отказ моделирует невозможность COM commit второго направления.</summary>
        public void Add(object value)
        {
            var rule = Assert.IsType<NativeRule>(value);
            // Настоящий INetFwRule принимает пустой ServiceName setter, но INetFwRules.Add отклоняет его.
            if (rule.AssignedFields.Contains("ServiceName") && string.IsNullOrWhiteSpace(Convert.ToString(rule.Values["ServiceName"])))
                throw new ArgumentException("ServiceName must remain unset when unrestricted");
            if (FailNextOutboundAdd && Convert.ToInt32(rule.Values["Direction"]) == 2)
            {
                FailNextOutboundAdd = false;
                throw new COMException("Controlled outbound add failure");
            }
            Items.Add(rule);
            Mutations++;
        }

        /// <summary>Удаляет по точному имени или сообщает подтверждённое отсутствие.</summary>
        public void Remove(string name)
        {
            if (string.Equals(FailNextRemoveName, name, StringComparison.OrdinalIgnoreCase))
            {
                FailNextRemoveName = null;
                throw new COMException("Controlled remove failure");
            }
            var rule = Items.SingleOrDefault(item => string.Equals(Convert.ToString(item.Values["Name"]), name, StringComparison.OrdinalIgnoreCase));
            if (rule == null) throw new FileNotFoundException(name);
            Items.Remove(rule);
            Mutations++;
        }

        /// <summary>Реализует прямой COM-индекс, не возвращая ложный null при ошибке.</summary>
        public object Item(string name) => Items.SingleOrDefault(item => string.Equals(Convert.ToString(item.Values["Name"]), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException(name);

        /// <summary>Возвращает полный либо намеренно отказавший снимок.</summary>
        public IEnumerator GetEnumerator()
        {
            foreach (var item in Items.ToArray())
            {
                yield return item;
                if (FailEnumeration) throw new IOException("Controlled enumeration failure");
            }
        }
    }

    /// <summary>Свойства адаптера эквивалентны INetFwRule; отказы задаются независимо для чтения и записи.</summary>
    public sealed class NativeRule : DynamicObject
    {
        public Dictionary<string, object?> Values { get; } = new()
        {
            ["Name"] = "", ["Description"] = "", ["Direction"] = 1, ["Action"] = 0, ["Enabled"] = true,
            ["Profiles"] = 7, ["Protocol"] = 256, ["ApplicationName"] = "", ["ServiceName"] = "",
            ["LocalPorts"] = "*", ["RemotePorts"] = "*", ["RemoteAddresses"] = "*", ["Grouping"] = "",
            ["LocalAddresses"] = "*", ["Interfaces"] = null, ["InterfaceTypes"] = "All", ["EdgeTraversal"] = false,
            ["IcmpTypesAndCodes"] = "*"
        };
        public string? FailNextSet { get; set; }
        public string? FailEverySet { get; set; }
        public string? FailRead { get; set; }
        public HashSet<string> AssignedFields { get; } = new(StringComparer.Ordinal);

        /// <summary>Передаёт обязательное поле либо явный диагностируемый отказ чтения.</summary>
        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            if (binder.Name == FailRead) throw new COMException("Controlled getter failure");
            return Values.TryGetValue(binder.Name, out result);
        }

        /// <summary>Последовательные setter меняют живой объект, как COM; сбой не отменяет предыдущие setter автоматически.</summary>
        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            if (binder.Name == FailEverySet) throw new COMException("Controlled permanent setter failure");
            if (binder.Name == FailNextSet)
            {
                FailNextSet = null;
                throw new COMException("Controlled late setter failure");
            }
            Values[binder.Name] = value;
            AssignedFields.Add(binder.Name);
            return true;
        }
    }
}
