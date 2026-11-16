using System.Text.Json;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Views;

namespace StartupMonitor.Tests.UI;

/// <summary>Verifies test scenario for FirewallBackupAndPresetsUiTests.cs.</summary>
/// <summary>Verifies test scenario for FirewallBackupAndPresetsUiTests.cs.</summary>
/// <summary>Verifies test scenario for FirewallBackupAndPresetsUiTests.cs.</summary>
/// <summary>Verifies test scenario for FirewallBackupAndPresetsUiTests.cs.</summary>
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

    // Verifies component behavior and assertions.

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
        // Verifies component behavior and assertions.
        Assert.Contains("\n", json);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void SerializeBackup_NullBackup_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NetworkSecurityHubView.SerializeBackupToJson(null!));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void DeserializeBackup_NullJson_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NetworkSecurityHubView.DeserializeBackupFromJson(null!));
    }

    // Verifies component behavior and assertions.

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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void DeserializeBackup_InvalidJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            NetworkSecurityHubView.DeserializeBackupFromJson("{ not valid json"));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void DeserializeBackup_EmptyRules_AllowsZeroCount()
    {
        const string json = """{ "version": "1.6.0", "rules": [] } """;

        var backup = NetworkSecurityHubView.DeserializeBackupFromJson(json);

        Assert.NotNull(backup);
        Assert.Empty(backup!.Rules);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ImportRequest_Serializes_WithOverwriteExistingContract()
    {
        var req = new ImportFirewallRulesRequest(SampleBackup(), overwriteExisting: true);

        var json = JsonSerializer.Serialize(req, JsonOptions);

        Assert.Contains("overwriteExisting", json);
        Assert.Contains("backup", json);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void FirewallImportResultPayload_Deserializes_ImportedAndSkipped()
    {
        const string json = """{ "imported": 5, "skipped": 2 }""";

        var result = JsonSerializer.Deserialize<FirewallImportResultPayload>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(5, result!.Imported);
        Assert.Equal(2, result.Skipped);
    }

    // Verifies component behavior and assertions.

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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void BuildPresetRequest_PreservesPreset_InBothContractFields(SecurityPresetType preset)
    {
        var req = NetworkSecurityHubView.BuildPresetRequest(preset);

        Assert.Equal(preset, req.Preset);
        Assert.Equal(preset, req.PresetType);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ApplyPresetRequest_Serializes_BothPresetAliases()
    {
        var req = new ApplySecurityPresetRequest(SecurityPresetType.Gaming);

        var json = JsonSerializer.Serialize(req, JsonOptions);

        Assert.Contains("preset", json);
        Assert.Contains("presetType", json);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void SecurityPresetResultPayload_Deserializes_AppliedCount()
    {
        const string json = """{ "preset": 1, "appliedCount": 7, "success": true, "message": "ok" }""";

        var result = JsonSerializer.Deserialize<SecurityPresetResultPayload>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(SecurityPresetType.Gaming, result!.Preset);
        Assert.Equal(7, result.AppliedCount);
        Assert.True(result.Success);
    }

    // Verifies component behavior and assertions.

    [Fact]
    public void BuildDefaultBackupFileName_UsesDateStamp()
    {
        var name = NetworkSecurityHubView.BuildDefaultBackupFileName(new DateTime(2026, 10, 15));

        Assert.Equal("FirewallRules_Backup_20261015.json", name);
    }

    // Verifies component behavior and assertions.

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
