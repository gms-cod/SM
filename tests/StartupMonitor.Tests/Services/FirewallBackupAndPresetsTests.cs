using System.Text.Json;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>Модульные тесты экспорта/импорта правил брандмауэра и пресетов безопасности.</summary>
public sealed class FirewallBackupAndPresetsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // --- FirewallBackupDto: сериализация ------------------------------------

    [Fact]
    public void BackupDto_HasExpectedDefaults()
    {
        var dto = new FirewallBackupDto();

        Assert.Equal("1.6.0", dto.Version);
        Assert.Equal(string.Empty, dto.Description);
        Assert.NotNull(dto.Rules);
        Assert.Empty(dto.Rules);
        Assert.True(DateTime.UtcNow - dto.ExportedAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void BackupDto_SerializationRoundtrip_PreservesAllFields()
    {
        var dto = new FirewallBackupDto
        {
            Version = "1.6.0",
            ExportedAt = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc),
            Description = "Тестовый бэкап",
            Rules = new List<FirewallRuleDto>
            {
                new()
                {
                    Name = "StartupMonitor_Test_Rule",
                    Description = "Тестовое правило",
                    Direction = FirewallDirection.Inbound,
                    Action = FirewallAction.Block,
                    Protocol = FirewallProtocol.Tcp,
                    LocalPorts = "445",
                    RemoteAddresses = "10.0.0.1",
                    Enabled = true,
                    Profiles = FirewallProfile.All,
                    Grouping = "StartupMonitor",
                    IsManaged = true,
                },
            },
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var restored = JsonSerializer.Deserialize<FirewallBackupDto>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal("1.6.0", restored!.Version);
        Assert.Equal(dto.ExportedAt, restored.ExportedAt);
        Assert.Equal("Тестовый бэкап", restored.Description);
        Assert.Single(restored.Rules);
        Assert.Equal("StartupMonitor_Test_Rule", restored.Rules[0].Name);
        Assert.Equal(FirewallDirection.Inbound, restored.Rules[0].Direction);
        Assert.Equal(FirewallAction.Block, restored.Rules[0].Action);
        Assert.Equal("445", restored.Rules[0].LocalPorts);
    }

    [Fact]
    public void BackupDto_EmptyRules_SerializesToEmptyArray()
    {
        var dto = new FirewallBackupDto { Description = "пусто" };
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"rules\":[]", json);
        var restored = JsonSerializer.Deserialize<FirewallBackupDto>(json, JsonOptions);
        Assert.NotNull(restored);
        Assert.Empty(restored!.Rules);
    }

    // --- Пресеты: валидация правил ------------------------------------------

    [Fact]
    public void BuildPresetRules_MaxPrivacy_BlocksTelemetryCortanaDiagnostics()
    {
        var rules = WindowsFirewallService.BuildPresetRules(SecurityPresetType.MaxPrivacy);

        Assert.NotEmpty(rules);
        Assert.All(rules, r =>
        {
            Assert.Equal(FirewallAction.Block, r.Action);
            Assert.Equal(FirewallDirection.Outbound, r.Direction);
            Assert.True(r.Enabled);
            Assert.StartsWith(WindowsFirewallService.RulePrefix, r.Name, StringComparison.OrdinalIgnoreCase);
        });

        var blob = string.Join(" | ", rules.Select(r => $"{r.Name} {r.Description} {r.RemoteAddresses} {r.ApplicationPath}"));
        Assert.Contains("телеметр", blob, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cortana", blob, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("диагностическ", blob, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPresetRules_Gaming_OpensSteamBattleNetEpicPorts()
    {
        var rules = WindowsFirewallService.BuildPresetRules(SecurityPresetType.Gaming);

        Assert.NotEmpty(rules);
        Assert.All(rules, r =>
        {
            Assert.Equal(FirewallAction.Allow, r.Action);
            Assert.Equal(FirewallDirection.Inbound, r.Direction);
            Assert.True(r.Enabled);
        });

        var ports = string.Join(",", rules.Select(r => r.LocalPorts ?? string.Empty));
        Assert.Contains("27015-27036", ports, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3724", ports, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1119", ports, StringComparison.OrdinalIgnoreCase);

        var blob = string.Join(" | ", rules.Select(r => $"{r.Name} {r.Description}"));
        Assert.Contains("Steam", blob, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Battle", blob, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Epic", blob, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPresetRules_PublicWifi_BlocksNetbiosSmbRdpRpc()
    {
        var rules = WindowsFirewallService.BuildPresetRules(SecurityPresetType.PublicWifi);

        Assert.NotEmpty(rules);
        Assert.All(rules, r =>
        {
            Assert.Equal(FirewallAction.Block, r.Action);
            Assert.Equal(FirewallDirection.Inbound, r.Direction);
            Assert.True(r.Enabled);
        });

        var ports = string.Join(",", rules.Select(r => r.LocalPorts ?? string.Empty));
        Assert.Contains("137-139", ports, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("445", ports, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3389", ports, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("135", ports, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPresetRules_ResetToDefaults_ReturnsEmpty()
    {
        var rules = WindowsFirewallService.BuildPresetRules(SecurityPresetType.ResetToDefaults);
        Assert.NotNull(rules);
        Assert.Empty(rules);
    }

    [Fact]
    public void BuildPresetRules_UnknownPreset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowsFirewallService.BuildPresetRules((SecurityPresetType)999));
    }

    [Fact]
    public void BuildPresetRules_AllRules_ManagedAndGrouped()
    {
        foreach (var preset in new[] { SecurityPresetType.MaxPrivacy, SecurityPresetType.Gaming, SecurityPresetType.PublicWifi })
        {
            var rules = WindowsFirewallService.BuildPresetRules(preset);
            Assert.All(rules, r =>
            {
                Assert.True(r.IsManaged);
                Assert.False(string.IsNullOrWhiteSpace(r.Name));
                Assert.False(string.IsNullOrWhiteSpace(r.Grouping));
            });
        }
    }

    // --- PlanImport: подсчёт импортированных правил --------------------------

    private static FirewallBackupDto MakeBackup(params string[] names) => new()
    {
        Version = "1.6.0",
        Description = "test",
        Rules = names.Select(n => new FirewallRuleDto
        {
            Name = n,
            Direction = FirewallDirection.Inbound,
            Action = FirewallAction.Block,
            Protocol = FirewallProtocol.Tcp,
            LocalPorts = "445",
            Enabled = true,
        }).ToList(),
    };

    [Fact]
    public void PlanImport_NoExisting_AllImported()
    {
        var backup = MakeBackup("A", "B", "C");
        var (imported, skipped) = WindowsFirewallService.PlanImport(
            backup, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(3, imported);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void PlanImport_ExistingSkipped_WithoutOverwrite()
    {
        var backup = MakeBackup("A", "B", "C");
        var existing = new HashSet<string>(new[] { "A", "B" }, StringComparer.OrdinalIgnoreCase);

        var (imported, skipped) = WindowsFirewallService.PlanImport(backup, existing, overwriteExisting: false);

        Assert.Equal(1, imported);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void PlanImport_ExistingReimported_WithOverwrite()
    {
        var backup = MakeBackup("A", "B");
        var existing = new HashSet<string>(new[] { "A", "B" }, StringComparer.OrdinalIgnoreCase);

        var (imported, skipped) = WindowsFirewallService.PlanImport(backup, existing, overwriteExisting: true);

        Assert.Equal(2, imported);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void PlanImport_InvalidNames_AreSkipped()
    {
        var backup = MakeBackup("Valid", "", "   ");
        var (imported, skipped) = WindowsFirewallService.PlanImport(
            backup, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(1, imported);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void PlanImport_EmptyBackup_ReturnsZeroZero()
    {
        var (imported, skipped) = WindowsFirewallService.PlanImport(
            new FirewallBackupDto(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(0, imported);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void PlanImport_NullBackup_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => WindowsFirewallService.PlanImport(null!, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ImportRulesBackup_NullBackup_Throws()
    {
        var svc = new WindowsFirewallService();
        Assert.Throws<ArgumentNullException>(() => svc.ImportRulesBackup(null!));
    }

    // --- Pipe-контракт -------------------------------------------------------

    [Fact]
    public void PipeMessageTypes_ContainBackupAndPresetMessages()
    {
        Assert.Equal("ExportFirewallRules", PipeMessageTypes.ExportFirewallRules);
        Assert.Equal("ExportFirewallRulesResponse", PipeMessageTypes.ExportFirewallRulesResponse);
        Assert.Equal("ImportFirewallRules", PipeMessageTypes.ImportFirewallRules);
        Assert.Equal("ImportFirewallRulesResponse", PipeMessageTypes.ImportFirewallRulesResponse);
        Assert.Equal("ApplySecurityPreset", PipeMessageTypes.ApplySecurityPreset);
        Assert.Equal("ApplySecurityPresetResponse", PipeMessageTypes.ApplySecurityPresetResponse);
    }

    [Fact]
    public void ImportRequest_SerializationRoundtrip()
    {
        var req = new ImportFirewallRulesRequest(MakeBackup("A"), overwriteExisting: true);
        var json = JsonSerializer.Serialize(req, JsonOptions);
        var restored = JsonSerializer.Deserialize<ImportFirewallRulesRequest>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.OverwriteExisting);
        Assert.Single(restored.Backup.Rules);
    }

    [Fact]
    public void ApplyPresetRequest_SerializationRoundtrip()
    {
        var req = new ApplySecurityPresetRequest(SecurityPresetType.Gaming);
        var json = JsonSerializer.Serialize(req, JsonOptions);
        var restored = JsonSerializer.Deserialize<ApplySecurityPresetRequest>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(SecurityPresetType.Gaming, restored!.Preset);
        Assert.Equal(SecurityPresetType.Gaming, restored.PresetType);
    }
}
