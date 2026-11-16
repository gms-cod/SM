using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using StartupMonitor.Shared.Audit.Models;

namespace StartupMonitor.Shared.Audit.Exporters;

/// <summary>
/// CSV report generator (RFC 4180) with UTF-8 BOM support for full Microsoft Excel compatibility.
/// </summary>
public class CsvAuditReportExporter : IAuditReportExporter
{
    /// <summary>Gets the file extension used for exported report files.</summary>
    /// <summary>Gets the MIME content type for exported CSV report data.</summary>
    /// <summary>Exports the specified audit report data to a CSV string.</summary>
    /// <param name="report">The audit report to export.</param>
    /// <returns>A string containing the report data in CSV format.</returns>
    public string FileExtension => "csv";
    /// <summary>Gets the MIME content type of the exported CSV data.</summary>
    public string ContentType => "text/csv; charset=utf-8";

    /// <summary>ExportToString: Exports report data to string.</summary>
    /// <param name="report">Input data: value of the report parameter (AuditReport).</param>
    /// <returns>Output data: the resulting value of type string.</returns>
    public string ExportToString(AuditReport report)
    {
        var sb = new StringBuilder();

        // 1. Audit metadata
        sb.AppendLine($"# Startup Monitor Audit Report; Version={EscapeCsv(report.Metadata.AppVersion)}; Date={EscapeCsv(report.Metadata.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"))}; Host={EscapeCsv(report.Metadata.MachineName)}; User={EscapeCsv(report.Metadata.UserName)}; OS={EscapeCsv(report.Metadata.OsDescription)}");
        sb.AppendLine($"# Summary: TotalEntries={report.Summary.TotalStartupEntries}; Enabled={report.Summary.EnabledEntries}; Disabled={report.Summary.DisabledEntries}; Blocked={report.Summary.BlockedEntries}; CLI_Threats={report.Summary.CliThreatsCount}; CIRCL_Alerts={report.Summary.CirclFoundCount}; AvgAiRating={report.Summary.AverageAiRating.ToString("F1", CultureInfo.InvariantCulture)}/10; ActiveProcesses={report.Summary.TotalProcessesCount}");
        sb.AppendLine();

        // 2. Auto-start records section
        sb.AppendLine("--- STARTUP ENTRIES ---");
        sb.AppendLine("Name,Publisher,Status,Location,LocationDetail,FilePath,CommandLine,SHA256,DetectedAt,AiRating,AiCategory,AiRisk,KasperskyZone,KasperskyDetection,HasCliThreat,CliThreatLevel,CliThreatBadge,CliThreatIndicators,AiRecommendation");

        foreach (var entry in report.StartupEntries)
        {
            var indicators = entry.ThreatInfo?.Indicators != null && entry.ThreatInfo.Indicators.Count > 0
                ? string.Join("; ", entry.ThreatInfo.Indicators)
                : string.Empty;

            var row = new[]
            {
                EscapeCsv(entry.Name),
                EscapeCsv(entry.Publisher),
                EscapeCsv(entry.Status.ToString()),
                EscapeCsv(entry.Location.ToString()),
                EscapeCsv(entry.LocationDetail),
                EscapeCsv(entry.FilePath),
                EscapeCsv(entry.CommandLine),
                EscapeCsv(entry.SHA256),
                EscapeCsv(entry.DetectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(entry.AiRating > 0 ? entry.AiRating.ToString() : string.Empty),
                EscapeCsv(entry.AiCategory),
                EscapeCsv(entry.AiRisk),
                EscapeCsv(entry.KasperskyZone),
                EscapeCsv(entry.KasperskyDetection),
                EscapeCsv(entry.ThreatInfo?.HasThreat == true ? "True" : "False"),
                EscapeCsv(entry.ThreatInfo?.ThreatLevel.ToString() ?? "None"),
                EscapeCsv(entry.ThreatInfo?.BadgeText),
                EscapeCsv(indicators),
                EscapeCsv(entry.AiRecommendation)
            };
            sb.AppendLine(string.Join(",", row));
        }

        // 3. Active processes section (if any)
        if (report.ActiveProcesses != null && report.ActiveProcesses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- ACTIVE PROCESSES ---");
            sb.AppendLine("PID,ParentPID,ProcessName,Publisher,CPU_Percent,Memory_MB,Disk_KBs,Network_KBs,IsFromStartup,StartupLocation,IsSuspended,IsService,IsSystemProcess,IsBlocked,HasThreat,ThreatLevel,ThreatBadge,CommandLine,FilePath");

            foreach (var proc in report.ActiveProcesses)
            {
                double memMb = Math.Round(proc.WorkingSetBytes / (1024.0 * 1024.0), 1);
                double diskKbs = Math.Round((proc.DiskReadBytesPerSec + proc.DiskWriteBytesPerSec) / 1024.0, 1);
                double netKbs = Math.Round(proc.NetworkBytesPerSec / 1024.0, 1);

                var row = new[]
                {
                    EscapeCsv(proc.ProcessId.ToString()),
                    EscapeCsv(proc.ParentProcessId.ToString()),
                    EscapeCsv(proc.ProcessName),
                    EscapeCsv(proc.Publisher),
                    EscapeCsv(proc.CpuUsagePercent.ToString("F1", CultureInfo.InvariantCulture)),
                    EscapeCsv(memMb.ToString("F1", CultureInfo.InvariantCulture)),
                    EscapeCsv(diskKbs.ToString("F1", CultureInfo.InvariantCulture)),
                    EscapeCsv(netKbs.ToString("F1", CultureInfo.InvariantCulture)),
                    EscapeCsv(proc.IsFromStartup ? "True" : "False"),
                    EscapeCsv(proc.StartupLocation?.ToString() ?? string.Empty),
                    EscapeCsv(proc.IsSuspended ? "True" : "False"),
                    EscapeCsv(proc.IsService ? "True" : "False"),
                    EscapeCsv(proc.IsSystemProcess ? "True" : "False"),
                    EscapeCsv(proc.IsBlocked ? "True" : "False"),
                    EscapeCsv(proc.ThreatInfo?.HasThreat == true ? "True" : "False"),
                    EscapeCsv(proc.ThreatInfo?.ThreatLevel.ToString() ?? "None"),
                    EscapeCsv(proc.ThreatInfo?.BadgeText),
                    EscapeCsv(proc.CommandLine),
                    EscapeCsv(proc.FilePath)
                };
                sb.AppendLine(string.Join(",", row));
            }
        }

        return sb.ToString();
    }

    /// <summary>ExportToBytes: Exports report data as a byte array.</summary>
/// <param name="report">Input data: value of the report parameter (AuditReport).</param>
/// <returns>Output data: the resulting value of type byte[].</returns>
    public byte[] ExportToBytes(AuditReport report)
    {
        // Writes the UTF-8 BOM (\uFEFF: 0xEF, 0xBB, 0xBF) before the content so the file opens correctly in Excel
        var content = ExportToString(report);
        var utf8Bytes = Encoding.UTF8.GetBytes(content);
        var bom = Encoding.UTF8.GetPreamble();

        var result = new byte[bom.Length + utf8Bytes.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(utf8Bytes, 0, result, bom.Length, utf8Bytes.Length);
        return result;
    }

    /// <summary>EscapeCsv: Escapes the specified value according to CSV rules.</summary>
    /// <param name="value">The value to escape (string?).</param>
    /// <returns>The resulting string value.</returns>
    public static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // If the value contains a comma, a quote, a line break, or starts with a formula character (=, +, -, @)
        bool mustQuote = value.Contains(',') || value.Contains(';') || value.Contains('"') ||
                         value.Contains('\r') || value.Contains('\n') ||
                         value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@');

        if (mustQuote)
        {
            // Escapes double quotes by doubling them
            var escaped = value.Replace("\"", "\"\"");
            // If it begins with formula characters, prefixes with an apostrophe for safety
            if (escaped.StartsWith('=') || escaped.StartsWith('+') || escaped.StartsWith('-') || escaped.StartsWith('@'))
            {
                escaped = "'" + escaped;
            }
            return "\"" + escaped + "\"";
        }

        return value;
    }
}

