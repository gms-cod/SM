using System.Text.Json;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Views;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// UI-тесты бэкапов и пресетов брандмауэра (Worker 3):
/// структура JSON экспорта/импорта, десериализация <see cref="FirewallBackupDto"/>,
/// Pipe-контракты Import/Export/Preset и маппинг <see cref="SecurityPresetType"/>.
/// Без WPF-диспетчера: проверяются статические хелперы
/// <c>NetworkSecurityHubView.SerializeBackupToJson / DeserializeBackupFromJson / GetSecurityPresetDisplayName</c>.
/// </summary>
public sealed class FirewallBackupAndPresetsUiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static FirewallBackupDto SampleBackup() => new()
    {
        Version = "1.6.0",
        ExportedAt = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc),
        Description = "Test backup",
        Rules = new List<FirewallRuleDto>
        {
            new()
            {
                Name = "Allow SSH",
                Description = "Inbound SSH",
                Direction = FirewallDirection.Inbound,
                Action = FirewallAction.Allow,
                Protocol = FirewallProtocol.Tcp,
                LocalPorts = "22",
                RemoteAddresses = "Any",
                Enabled = true,
                Profiles = FirewallProfile.All,
                IsManaged = true
            },
            new()
            {
                Name = "Block Telemetry",
                Description = "Outbound telemetry block",
                Direction = FirewallDirection.Outbound,
                Action = FirewallAction.Block,
                Protocol = FirewallProtocol.Any,
                RemoteAddresses = "134.170.0.0/16",
                Enabled = true,
                Profiles = FirewallProfile.All,
                Grouping = "StartupMonitor",
                IsManaged = true
            }
        }
    };

    // ─── 1. Экспорт: структура JSON ───

    [Fact]
    public void SerializeBackup_ProducesIndentedJson_WithAllSections()
    {
        var json = NetworkSecurityHubView.SerializeBackupToJson(SampleBackup());

        Assert.Contains("\"version\"", json);
        Assert.Contains("\"exportedAt\"", json);
        Assert.Contains("\"description\"", json);
        Assert.Contains("\"rules\"", json);
        Assert.Contains("Allow SSH", json);
        Assert.Contains("Block Telemetry", json);
        // WriteIndented = true: есть переносы строк и отступы.
        Assert.Contains("\n", json);
    }

    [Fact]
    public void SerializeBackup_Roundtrip_PreservesRules()
    {
        var backup = SampleBackup();

        var json = NetworkSecurityHubView.SerializeBackupToJson(backup);
        var restored = NetworkSecurityHubView.DeserializeBackupFromJson(json);

        Assert.NotNull(restored);
        Assert.Equal("1.6.0", restored!.Version);
        Assert.Equal("Test backup", restored.Description);
        Assert.Equal(2, restored.Rules.Count);
        Assert.Contains(restored.Rules, r => r.Name == "Allow SSH");
        Assert.Contains(restored.Rules, r => r.Name == "Block Telemetry");
    }

    [Fact]
    public void SerializeBackup_NullBackup_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NetworkSecurityHubView.SerializeBackupToJson(null!));
    }

    [Fact]
    public void DeserializeBackup_NullJson_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NetworkSecurityHubView.DeserializeBackupFromJson(null!));
    }

    // ─── 2. Импорт: валидация и десериализация ───

    [Fact]
    public void DeserializeBackup_ValidJson_RestoresVersionAndRules()
    {
        const string json = """
            {
              "version": "1.6.0",
              "exportedAt": "2026-10-15T12:00:00Z",
              "description": "File import",
              "rules": [
                { "name": "Rule1", "direction": 0, "action": 1, "protocol": 0 }
              ]
            }
            """;

        var backup = NetworkSecurityHubView.DeserializeBackupFromJson(json);

        Assert.NotNull(backup);
        Assert.Equal("1.6.0", backup!.Version);
        Assert.Single(backup.Rules);
        Assert.Equal("Rule1", backup.Rules[0].Name);
    }

    [Fact]
    public void DeserializeBackup_InvalidJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            NetworkSecurityHubView.DeserializeBackupFromJson("{ not valid json"));
    }

    [Fact]
    public void DeserializeBackup_EmptyRules_AllowsZeroCount()
    {
        const string json = """{ "version": "1.6.0", "rules": [] } """;

        var backup = NetworkSecurityHubView.DeserializeBackupFromJson(json);

        Assert.NotNull(backup);
        Assert.Empty(backup!.Rules);
    }

    [Fact]
    public void BuildImportRequest_PreservesBackupAndOverwriteFlag()
    {
        var backup = SampleBackup();

        var withOverwrite = NetworkSecurityHubView.BuildImportRequest(backup, true);
        var withoutOverwrite = NetworkSecurityHubView.BuildImportRequest(backup, false);

        Assert.Same(backup, withOverwrite.Backup);
        Assert.True(withOverwrite.OverwriteExisting);
        Assert.False(withoutOverwrite.OverwriteExisting);
    }

    [Fact]
    public void ImportRequest_Serializes_WithOverwriteExistingContract()
    {
        var req = new ImportFirewallRulesRequest(SampleBackup(), overwriteExisting: true);

        var json = JsonSerializer.Serialize(req, JsonOptions);

        Assert.Contains("overwriteExisting", json);
        Assert.Contains("backup", json);
    }

    [Fact]
    public void FirewallImportResultPayload_Deserializes_ImportedAndSkipped()
    {
        const string json = """{ "imported": 5, "skipped": 2 }""";

        var result = JsonSerializer.Deserialize<FirewallImportResultPayload>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(5, result!.Imported);
        Assert.Equal(2, result.Skipped);
    }

    // ─── 3. Пресеты: маппинг ───

    [Theory]
    [InlineData(SecurityPresetType.MaxPrivacy, "Максимальная приватность")]
    [InlineData(SecurityPresetType.Gaming, "Игровой режим")]
    [InlineData(SecurityPresetType.PublicWifi, "Публичный Wi-Fi")]
    [InlineData(SecurityPresetType.ResetToDefaults, "Сброс к стандартным параметрам")]
    public void GetSecurityPresetDisplayName_MapsEachPreset(SecurityPresetType preset, string expected)
    {
        Assert.Equal(expected, NetworkSecurityHubView.GetSecurityPresetDisplayName(preset));
    }

    [Fact]
    public void SecurityPresetType_AllValuesDefined()
    {
        Assert.True(Enum.IsDefined(typeof(SecurityPresetType), SecurityPresetType.MaxPrivacy));
        Assert.True(Enum.IsDefined(typeof(SecurityPresetType), SecurityPresetType.Gaming));
        Assert.True(Enum.IsDefined(typeof(SecurityPresetType), SecurityPresetType.PublicWifi));
        Assert.True(Enum.IsDefined(typeof(SecurityPresetType), SecurityPresetType.ResetToDefaults));
        Assert.Equal(4, Enum.GetValues<SecurityPresetType>().Length);
    }

    [Theory]
    [InlineData(SecurityPresetType.MaxPrivacy)]
    [InlineData(SecurityPresetType.Gaming)]
    [InlineData(SecurityPresetType.PublicWifi)]
    [InlineData(SecurityPresetType.ResetToDefaults)]
    public void BuildPresetRequest_PreservesPreset_InBothContractFields(SecurityPresetType preset)
    {
        var req = NetworkSecurityHubView.BuildPresetRequest(preset);

        Assert.Equal(preset, req.Preset);
        Assert.Equal(preset, req.PresetType);
    }

    [Fact]
    public void ApplyPresetRequest_Serializes_BothPresetAliases()
    {
        var req = new ApplySecurityPresetRequest(SecurityPresetType.Gaming);

        var json = JsonSerializer.Serialize(req, JsonOptions);

        Assert.Contains("preset", json);
        Assert.Contains("presetType", json);
    }

    [Fact]
    public void SecurityPresetResultPayload_Deserializes_AppliedCount()
    {
        const string json = """{ "preset": 1, "appliedCount": 7, "success": true, "message": "ok" }""";

        var result = JsonSerializer.Deserialize<SecurityPresetResultPayload>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(SecurityPresetType.Gaming, result!.Preset);
        Assert.Equal(7, result.AppliedCount);
        Assert.True(result.Success);
    }

    // ─── 4. Имя файла бэкапа ───

    [Fact]
    public void BuildDefaultBackupFileName_UsesDateStamp()
    {
        var name = NetworkSecurityHubView.BuildDefaultBackupFileName(new DateTime(2026, 10, 15));

        Assert.Equal("FirewallRules_Backup_20261015.json", name);
    }

    // ─── 5. Pipe-контракты экспорта ───

    [Fact]
    public void PipeMessageTypes_ExportImportPresetDefined()
    {
        Assert.Equal("ExportFirewallRules", PipeMessageTypes.ExportFirewallRules);
        Assert.Equal("ExportFirewallRulesResponse", PipeMessageTypes.ExportFirewallRulesResponse);
        Assert.Equal("ImportFirewallRules", PipeMessageTypes.ImportFirewallRules);
        Assert.Equal("ImportFirewallRulesResponse", PipeMessageTypes.ImportFirewallRulesResponse);
        Assert.Equal("ApplySecurityPreset", PipeMessageTypes.ApplySecurityPreset);
        Assert.Equal("ApplySecurityPresetResponse", PipeMessageTypes.ApplySecurityPresetResponse);
    }
}
