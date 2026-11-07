using System.Collections.Generic;
using System.Linq;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Shared.Audit.Models;

/// <summary>
/// Полный снимок отчета аудита безопасности системы.
/// </summary>
public class AuditReport
{
    public AuditMetadata Metadata { get; set; } = new();
    public AuditSummary Summary { get; set; } = new();
    public List<StartupEntry> StartupEntries { get; set; } = new();
    public List<ProcessInfo> ActiveProcesses { get; set; } = new();
    public SystemPerformanceSnapshot? PerformanceSnapshot { get; set; }

    public static AuditReport Create(
        IEnumerable<StartupEntry>? entries,
        IEnumerable<ProcessInfo>? processes = null,
        SystemPerformanceSnapshot? snapshot = null,
        string appVersion = "1.0.15",
        bool isServiceConnected = true,
        bool strictMode = false,
        bool silentMode = false)
    {
        var entryList = entries?.ToList() ?? new List<StartupEntry>();
        var procList = processes?.ToList() ?? new List<ProcessInfo>();

        return new AuditReport
        {
            Metadata = AuditMetadata.CreateCurrent(appVersion, isServiceConnected, strictMode, silentMode),
            Summary = AuditSummary.Calculate(entryList, procList, snapshot),
            StartupEntries = entryList,
            ActiveProcesses = procList,
            PerformanceSnapshot = snapshot
        };
    }
}

