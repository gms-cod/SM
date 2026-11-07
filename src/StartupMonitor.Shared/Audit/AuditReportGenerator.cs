using System;
using System.Collections.Generic;
using System.IO;
using StartupMonitor.Shared.Audit.Exporters;
using StartupMonitor.Shared.Audit.Models;

namespace StartupMonitor.Shared.Audit;

/// <summary>
/// Фасад для генерации и экспорта отчетов аудита в различных форматах (HTML, CSV, JSON).
/// </summary>
public static class AuditReportGenerator
{
    private static readonly Lazy<HtmlAuditReportExporter> HtmlExporterInstance = new(() => new HtmlAuditReportExporter());
    private static readonly Lazy<CsvAuditReportExporter> CsvExporterInstance = new(() => new CsvAuditReportExporter());
    private static readonly Lazy<JsonAuditReportExporter> JsonExporterInstance = new(() => new JsonAuditReportExporter());

    public static HtmlAuditReportExporter HtmlExporter => HtmlExporterInstance.Value;
    public static CsvAuditReportExporter CsvExporter => CsvExporterInstance.Value;
    public static JsonAuditReportExporter JsonExporter => JsonExporterInstance.Value;

    /// <summary>GenerateHtml: Выполняет операцию GenerateHtml.</summary>
    /// <param name="report)">Входные данные: значение параметра report) (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public static string GenerateHtml(AuditReport report) => HtmlExporter.ExportToString(report);
    /// <summary>GenerateCsv: Выполняет операцию GenerateCsv.</summary>
    /// <param name="report)">Входные данные: значение параметра report) (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public static string GenerateCsv(AuditReport report) => CsvExporter.ExportToString(report);
    /// <summary>GenerateJson: Выполняет операцию GenerateJson.</summary>
    /// <param name="report)">Входные данные: значение параметра report) (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public static string GenerateJson(AuditReport report) => JsonExporter.ExportToString(report);

    /// <summary>GenerateHtmlBytes: Выполняет операцию GenerateHtmlBytes.</summary>
    /// <param name="report)">Входные данные: значение параметра report) (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа byte[].</returns>
    public static byte[] GenerateHtmlBytes(AuditReport report) => HtmlExporter.ExportToBytes(report);
    /// <summary>GenerateCsvBytes: Выполняет операцию GenerateCsvBytes.</summary>
    /// <param name="report)">Входные данные: значение параметра report) (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа byte[].</returns>
    public static byte[] GenerateCsvBytes(AuditReport report) => CsvExporter.ExportToBytes(report);
    /// <summary>GenerateJsonBytes: Выполняет операцию GenerateJsonBytes.</summary>
    /// <param name="report)">Входные данные: значение параметра report) (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа byte[].</returns>
    public static byte[] GenerateJsonBytes(AuditReport report) => JsonExporter.ExportToBytes(report);

    /// <summary>
    /// Получить экспортер по расширению или названию формата (.html, .csv, .json).
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
    /// Экспортировать отчет в строку по расширению или формату.
    /// </summary>
    public static string Export(AuditReport report, string formatOrExtension)
    {
        return GetExporter(formatOrExtension).ExportToString(report);
    }

    /// <summary>
    /// Экспортировать отчет в массив байтов (с учетом BOM для CSV и кодировок).
    /// </summary>
    public static byte[] ExportBytes(AuditReport report, string formatOrExtension)
    {
        return GetExporter(formatOrExtension).ExportToBytes(report);
    }
}

