using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Audit.Models;

namespace StartupMonitor.Shared.Audit.Exporters;

/// <summary>
/// Генератор структурированного отчета в формате JSON.
/// </summary>
public class JsonAuditReportExporter : IAuditReportExporter
{
    public string FileExtension => "json";
    public string ContentType => "application/json";

    /// <summary>ExportToString: Экспортирует данные отчета ToString.</summary>
    /// <param name="report">Входные данные: значение параметра report (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
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

    /// <summary>ExportToBytes: Экспортирует данные отчета ToBytes.</summary>
    /// <param name="report">Входные данные: значение параметра report (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа byte[].</returns>
    public byte[] ExportToBytes(AuditReport report)
    {
        return Encoding.UTF8.GetBytes(ExportToString(report));
    }
}

