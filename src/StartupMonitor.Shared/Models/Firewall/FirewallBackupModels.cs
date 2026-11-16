// Models for export/import of firewall rules and preconfigured security presets.
// Used by service (SYSTEM), Pipe IPC, and UI. Pure POCOs without COM dependency.
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Preconfigured firewall security preset.</summary>
public enum SecurityPresetType
{
    /// <summary>Максимальная приватность: блокировка телеметрии MS, Cortana, диагностических серверов.</summary>
    MaxPrivacy = 0,
    /// <summary>Game mode: opens ports for Steam, Battle.net, and Epic Games.</summary>
    Gaming = 1,
    /// <summary>Public Wi-Fi: blocks incoming NetBIOS, SMB, RDP, and RPC traffic.</summary>
    PublicWifi = 2,
    /// <summary>Resets to default settings (removes managed rules, Normal mode).</summary>
    ResetToDefaults = 3
}

/// <summary>Firewall rules backup DTO (export/import via Pipe IPC and files).</summary>
public sealed class FirewallBackupDto
{
    /// <summary>Backup format version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.6.0";

    /// <summary>Export timestamp (UTC).</summary>
    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Human-readable backup description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Firewall rules collection.</summary>
    [JsonPropertyName("rules")]
    public List<FirewallRuleDto> Rules { get; set; } = new();
}
