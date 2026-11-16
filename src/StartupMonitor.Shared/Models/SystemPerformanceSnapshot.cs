using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>System performance snapshot and list of active processes.</summary>
public class SystemPerformanceSnapshot
{
    /// <summary>Overall OS CPU utilization (0.0% - 100.0%).</summary>
    public double TotalCpuPercent { get; set; }

    /// <summary>Total system physical memory in bytes.</summary>
    public long TotalMemoryBytes { get; set; }

    /// <summary>Used system physical memory in bytes.</summary>
    public long UsedMemoryBytes { get; set; }

    /// <summary>Percentage of memory utilized (0.0% - 100.0%).</summary>
    public double MemoryPercent => TotalMemoryBytes > 0 ? (UsedMemoryBytes * 100.0 / TotalMemoryBytes) : 0.0;

    /// <summary>Alias for percentage of memory utilized.</summary>
    public double TotalMemoryPercent => MemoryPercent;

    /// <summary>Aggregate system disk activity (bytes/sec).</summary>
    public long TotalDiskIoBytesPerSec { get; set; }

    /// <summary>Aggregate system network throughput (bytes/sec).</summary>
    public long TotalNetworkBytesPerSec { get; set; }

    /// <summary>Total count of running processes.</summary>
    public int TotalProcessesCount { get; set; }

    /// <summary>Total number of threads in the OS.</summary>
    public int TotalThreadsCount { get; set; }

    /// <summary>List of active processes with a snapshot of their metrics.</summary>
    public List<ProcessInfo> Processes { get; set; } = [];

    // Auxiliary formatting properties for UI
    [JsonIgnore]
    public string DisplayTotalCpu => $"{TotalCpuPercent:F0} %";

    [JsonIgnore]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string DisplayTotalRam => $"{UsedMemoryBytes / (1024.0 * 1024 * 1024):F1} / {TotalMemoryBytes / (1024.0 * 1024 * 1024):F1} ГБ ({MemoryPercent:F0}%)";

    [JsonIgnore]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string DisplayTotalDisk => TotalDiskIoBytesPerSec switch
    {
        < 1024 => "0 КБ/с",
        < 1024 * 1024 => $"{TotalDiskIoBytesPerSec / 1024.0:F0} КБ/с",
        _ => $"{TotalDiskIoBytesPerSec / (1024.0 * 1024):F1} МБ/с"
    };

    [JsonIgnore]
    /// <summary>Gets the formatted total network bandwidth usage as a human-readable string.</summary>
    public string DisplayTotalNetwork => TotalNetworkBytesPerSec switch
    {
        <= 0 => "0 КБ/с",
        < 1024 => $"{TotalNetworkBytesPerSec / 1024.0:F1} КБ/с",
        < 1024 * 1024 => $"{TotalNetworkBytesPerSec / 1024.0:F0} КБ/с",
        _ => $"{TotalNetworkBytesPerSec / (1024.0 * 1024):F1} МБ/с"
    };
}

