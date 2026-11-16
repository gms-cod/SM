using System;
using System.Collections.Generic;
using System.Linq;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Shared.Audit.Models;

/// <summary>
/// Агрегированные показатели аудита безопасности (KPI: счетчики записей, угрозы, AI-рейтинг, ресурсы).
/// </summary>
public class AuditSummary
{
    /// <summary>Gets or sets the total number of startup entries.</summary>
    /// <summary>Gets or sets the number of enabled entries.</summary>
    /// <summary>Gets or sets the number of disabled entries.</summary>
    /// <summary>Gets or sets the number of blocked entries.</summary>
    /// <summary>Gets or sets the count of CLI threats.</summary>
    /// <summary>Gets or sets the count of entries found by CIRCL.</summary>
    /// <summary>Gets or sets the average AI rating.</summary>
    /// <summary>Gets or sets the count of rated entries.</summary>
    public int TotalStartupEntries { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int EnabledEntries { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int DisabledEntries { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int BlockedEntries { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int CliThreatsCount { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int CirclFoundCount { get; set; }
    public double AverageAiRating { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int RatedEntriesCount { get; set; }

    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int TotalProcessesCount { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int SuspendedProcessesCount { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public int ProcessThreatsCount { get; set; }
    public double TotalCpuPercent { get; set; }
    public double TotalRamUsedMb { get; set; }

    public static AuditSummary Calculate(
        IEnumerable<StartupEntry>? entries,
        IEnumerable<ProcessInfo>? processes = null,
        SystemPerformanceSnapshot? snapshot = null)
    {
        var entryList = entries?.ToList() ?? new List<StartupEntry>();
        var procList = processes?.ToList() ?? new List<ProcessInfo>();

        var rated = entryList.Where(e => e.AiRating > 0).ToList();
        double avgAi = rated.Count > 0 ? rated.Average(e => e.AiRating) : 0;

        double cpuPercent = snapshot != null
            ? Math.Round(snapshot.TotalCpuPercent, 1)
            : Math.Round(procList.Sum(p => p.CpuUsagePercent), 1);

        double ramUsedMb = snapshot != null
            ? Math.Round(snapshot.UsedMemoryBytes / (1024.0 * 1024.0), 1)
            : Math.Round(procList.Sum(p => p.WorkingSetBytes) / (1024.0 * 1024.0), 1);

        return new AuditSummary
        {
            TotalStartupEntries = entryList.Count,
            EnabledEntries = entryList.Count(e => e.Status == EntryStatus.Enabled),
            DisabledEntries = entryList.Count(e => e.Status == EntryStatus.Disabled),
            BlockedEntries = entryList.Count(e => e.Status == EntryStatus.Blocked),
            CliThreatsCount = entryList.Count(e => e.ThreatInfo != null && e.ThreatInfo.HasThreat),
            CirclFoundCount = entryList.Count(e => string.Equals(e.KasperskyZone, "Red", StringComparison.OrdinalIgnoreCase) || string.Equals(e.KasperskyZone, "Found", StringComparison.OrdinalIgnoreCase)),
            AverageAiRating = Math.Round(avgAi, 1),
            RatedEntriesCount = rated.Count,
            TotalProcessesCount = procList.Count,
            SuspendedProcessesCount = procList.Count(p => p.IsSuspended),
            ProcessThreatsCount = procList.Count(p => p.ThreatInfo != null && p.ThreatInfo.HasThreat),
            TotalCpuPercent = cpuPercent,
            TotalRamUsedMb = ramUsedMb
        };
    }
}

