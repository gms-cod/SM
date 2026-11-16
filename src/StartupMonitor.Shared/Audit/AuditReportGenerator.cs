using System;
using System.Collections.Generic;
using System.IO;
using StartupMonitor.Shared.Audit.Exporters;
using StartupMonitor.Shared.Audit.Models;

namespace StartupMonitor.Shared.Audit;

/// <summary>
/// Facade for generating and exporting audit reports in various formats (HTML, CSV, JSON).
/// </summary>
public static class AuditReportGenerator
{
    private static readonly Lazy<HtmlAuditReportExporter> HtmlExporterInstance = new(() => new HtmlAuditReportExporter());
    private static readonly Lazy<CsvAuditReportExporter> CsvExporterInstance = new(() => new CsvAuditReportExporter());
    private static readonly Lazy<JsonAuditReportExporter> JsonExporterInstance = new(() => new JsonAuditReportExporter());

    public static HtmlAuditReportExporter HtmlExporter => HtmlExporterInstance.Value;
    public static CsvAuditReportExporter CsvExporter => CsvExporterInstance.Value;
    public static JsonAuditReportExporter JsonExporter => JsonExporterInstance.Value;

    /// <summary>GenerateHtml: Performs the GenerateHtml operation.</summary>
    /// <param name="report)">Input: the value of the report) parameter (AuditReport).</param>
    /// <returns>Output: the resulting value of type string.</returns>
    public static string GenerateHtml(AuditReport report) => HtmlExporter.ExportToString(report);
    /// <summary>GenerateCsv: Performs the GenerateCsv operation.</summary>
    /// <param name="report)">Input: the value of the report) parameter (AuditReport).</param>
    /// <returns>Output: the resulting value of type string.</returns>
    public static string GenerateCsv(AuditReport report) => CsvExporter.ExportToString(report);
    /// <summary>Performs the GenerateJson operation.</summary>
    /// <param name="report)">The value of the report parameter (AuditReport).</param>
    /// <returns>The resulting string value.</returns>
    public static string GenerateJson(AuditReport report) => JsonExporter.ExportToString(report);

    /// <summary>Performs the GenerateHtmlBytes operation.</summary>
    /// <param name="report)">The value of the report parameter (AuditReport).</param>
    /// <returns>The resulting byte array.</returns>
    public static byte[] GenerateHtmlBytes(AuditReport report) => HtmlExporter.ExportToBytes(report);
    /// <summary>Performs the GenerateCsvBytes operation.</summary>
    /// <param name="report)">The value of the report parameter (AuditReport).</param>
    /// <returns>The resulting byte array.</returns>
    public static byte[] GenerateCsvBytes(AuditReport report) => CsvExporter.ExportToBytes(report);
    /// <summary>Generates JSON bytes for the specified report.</summary>
    /// <param name="report)">The audit report to convert to JSON.</param>
    /// <returns>A byte array containing the generated JSON data.</returns>
    public static byte[] GenerateJsonBytes(AuditReport report) => JsonExporter.ExportToBytes(report);

    /// <summary>
    /// Gets the exporter for the specified file extension or format name (.html, .csv, .json).
    /// </summary>
    public static IAuditReportExporter GetExporter(string formatOrExtension)
    {
        var ext = formatOrExtension.Trim().TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "html" or "htm" => HtmlExporter,
            "csv" => CsvExporter,
            "json" => JsonExporter,
            _ => throw new NotSupportedException($"Формат экспорта '{formatOrExtension}' не поддерживается.")
        };
    }

    /// <summary>
    /// Exports the report to a string using the specified file extension or format.
    /// </summary>
    public static string Export(AuditReport report, string formatOrExtension)
    {
        return GetExporter(formatOrExtension).ExportToString(report);
    }

    /// <summary>
    /// Exports the report to a byte array, accounting for BOM for CSV and encodings.
    /// </summary>
    public static byte[] ExportBytes(AuditReport report, string formatOrExtension)
    {
        return GetExporter(formatOrExtension).ExportToBytes(report);
    }
}

