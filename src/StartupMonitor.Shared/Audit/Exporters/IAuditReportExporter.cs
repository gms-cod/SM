using StartupMonitor.Shared.Audit.Models;

namespace StartupMonitor.Shared.Audit.Exporters;

/// <summary>
/// Interface for audit report generators supporting multiple formats (HTML, CSV, JSON).
/// </summary>
public interface IAuditReportExporter
{
    string FileExtension { get; }
    string ContentType { get; }
    string ExportToString(AuditReport report);
    byte[] ExportToBytes(AuditReport report);
}

