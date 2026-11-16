using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Audit.Models;

namespace StartupMonitor.Shared.Audit.Exporters;

/// <summary>
/// Generates a structured report in JSON format.
/// </summary>
public class JsonAuditReportExporter : IAuditReportExporter
{
    /// <summary>Gets the file extension used for exported reports.</summary>
    /// <summary>Gets the content type used for exported reports.</summary>
    public string FileExtension => "json";
    /// <summary>Gets the content type.</summary>
    public string ContentType => "application/json";

    /// <summary>Exports report data as a string.</summary>
    /// <param name="report">The report (AuditReport) to export.</param>
    /// <returns>The resulting string.</returns>
    public string ExportToString(AuditReport report)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return JsonSerializer.Serialize(report, options);
    }

    /// <summary>Exports report data as a byte array.</summary>
    /// <param name="report">The report (AuditReport) to export.</param>
    /// <returns>The resulting byte array.</returns>
    public byte[] ExportToBytes(AuditReport report)
    {
        return Encoding.UTF8.GetBytes(ExportToString(report));
    }
}

