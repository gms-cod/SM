using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using StartupMonitor.Shared.Audit.Models;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Shared.Audit.Exporters;

/// <summary>
/// Генератор автономного интерактивного HTML-отчета аудита системы в корпоративной тёмной теме Fluent/Cyber.
/// Не требует подключения к интернету (zero-dependency single file) и поддерживает живой поиск, фильтры и печать.
/// </summary>
public class HtmlAuditReportExporter : IAuditReportExporter
{
    public string FileExtension => "html";
    public string ContentType => "text/html; charset=utf-8";

    /// <summary>ExportToString: Экспортирует данные отчета ToString.</summary>
    /// <param name="report">Входные данные: значение параметра report (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public string ExportToString(AuditReport report)
    {
        var sb = new StringBuilder();
        var m = report.Metadata;
        var s = report.Summary;

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"ru\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>Startup Monitor — Отчет аудита безопасности ({H(m.MachineName)})</title>");
        sb.AppendLine("  <style>");
        AppendStyles(sb);
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Контейнер отчета
        sb.AppendLine("  <div class=\"report-wrapper\">");

        // 1. Шапка отчета
        AppendHeader(sb, m, s);

        // 2. Сетка ключевых показателей (KPI Cards)
        AppendKpiCards(sb, s);

        // 3. Панель управления (Поиск, Вкладки, Фильтры)
        AppendControls(sb, report);

        // 4. Вкладка: Автозапуск
        AppendStartupEntriesSection(sb, report.StartupEntries);

        // 5. Вкладка: Процессы
        AppendProcessesSection(sb, report.ActiveProcesses);

        // 6. Подвал
        AppendFooter(sb, m);

        sb.AppendLine("  </div>"); // /report-wrapper

        // 7. Встроенный JavaScript
        sb.AppendLine("  <script>");
        AppendScripts(sb);
        sb.AppendLine("  </script>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>ExportToBytes: Экспортирует данные отчета ToBytes.</summary>
    /// <param name="report">Входные данные: значение параметра report (AuditReport).</param>
    /// <returns>Выходные данные: результирующее значение типа byte[].</returns>
    public byte[] ExportToBytes(AuditReport report)
    {
        return Encoding.UTF8.GetBytes(ExportToString(report));
    }

    /// <summary>AppendHeader: Добавляет секцию разметки Header.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    /// <param name="m">Входные данные: значение параметра m (AuditMetadata).</param>
    /// <param name="s">Входные данные: значение параметра s (AuditSummary).</param>
    private static void AppendHeader(StringBuilder sb, AuditMetadata m, AuditSummary s)
    {
        sb.AppendLine("    <header class=\"report-header\">");
        sb.AppendLine("      <div class=\"header-left\">");
        sb.AppendLine("        <div class=\"logo-badge\">");
        sb.AppendLine("          <svg width=\"32\" height=\"32\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z\" fill=\"rgba(0, 210, 211, 0.2)\" stroke=\"#00D2D3\"/></svg>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div>");
        sb.AppendLine("          <h1 class=\"report-title\">Startup Monitor <span class=\"title-accent\">Audit Report</span></h1>");
        sb.AppendLine($"          <div class=\"report-subtitle\">Комплексный аудит безопасности системы, автозапуска и активных процессов</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"header-meta\">");
        sb.AppendLine($"        <div class=\"meta-pill\"><span class=\"meta-label\">Компьютер:</span> <strong class=\"meta-val\">{H(m.MachineName)}</strong></div>");
        sb.AppendLine($"        <div class=\"meta-pill\"><span class=\"meta-label\">Пользователь:</span> <span class=\"meta-val\">{H(m.UserName)}</span></div>");
        sb.AppendLine($"        <div class=\"meta-pill\"><span class=\"meta-label\">ОС:</span> <span class=\"meta-val\">{H(m.OsDescription)} ({H(m.Architecture)})</span></div>");
        sb.AppendLine($"        <div class=\"meta-pill\"><span class=\"meta-label\">Создан:</span> <span class=\"meta-val\">{m.GeneratedAt:yyyy-MM-dd HH:mm:ss}</span></div>");
        sb.AppendLine($"        <div class=\"meta-pill\"><span class=\"meta-label\">Версия:</span> <span class=\"meta-val\">v{H(m.AppVersion)}</span></div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </header>");
    }

    /// <summary>AppendKpiCards: Добавляет секцию разметки KpiCards.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    /// <param name="s">Входные данные: значение параметра s (AuditSummary).</param>
    private static void AppendKpiCards(StringBuilder sb, AuditSummary s)
    {
        sb.AppendLine("    <section class=\"kpi-grid\">");

        // Всего записей
        sb.AppendLine("      <div class=\"kpi-card card-total\">");
        sb.AppendLine("        <div class=\"kpi-title\">Точек автозапуска</div>");
        sb.AppendLine($"        <div class=\"kpi-value\">{s.TotalStartupEntries}</div>");
        sb.AppendLine($"        <div class=\"kpi-detail\">Включено: <strong>{s.EnabledEntries}</strong> • Откл: <strong>{s.DisabledEntries}</strong></div>");
        sb.AppendLine("      </div>");

        // Угрозы CLI
        var threatClass = s.CliThreatsCount > 0 ? "kpi-card card-threats alert" : "kpi-card card-threats";
        sb.AppendLine($"      <div class=\"{threatClass}\">");
        sb.AppendLine("        <div class=\"kpi-title\">Угрозы CLI и скрипты</div>");
        sb.AppendLine($"        <div class=\"kpi-value\">{s.CliThreatsCount}</div>");
        sb.AppendLine($"        <div class=\"kpi-detail\">Подозрительных процессов: <strong>{s.ProcessThreatsCount}</strong></div>");
        sb.AppendLine("      </div>");

        // Черный список / Заблокировано
        sb.AppendLine("      <div class=\"kpi-card card-blocked\">");
        sb.AppendLine("        <div class=\"kpi-title\">Заблокировано в ЧС</div>");
        sb.AppendLine($"        <div class=\"kpi-value\">{s.BlockedEntries}</div>");
        sb.AppendLine($"        <div class=\"kpi-detail\">Kaspersky детектов: <strong>{s.CirclFoundCount}</strong></div>");
        sb.AppendLine("      </div>");

        // Средний AI-рейтинг
        string starsHtml = RenderAiStars(s.AverageAiRating);
        sb.AppendLine("      <div class=\"kpi-card card-ai\">");
        sb.AppendLine("        <div class=\"kpi-title\">Безопасность ИИ (средняя)</div>");
        sb.AppendLine($"        <div class=\"kpi-value ai-stars\">{starsHtml}</div>");
        sb.AppendLine($"        <div class=\"kpi-detail\">Оценка: <strong>{s.AverageAiRating.ToString("F1", CultureInfo.InvariantCulture)} / 10</strong> ({s.RatedEntriesCount} оценено)</div>");
        sb.AppendLine("      </div>");

        // Активные процессы & ЦП
        sb.AppendLine("      <div class=\"kpi-card card-procs\">");
        sb.AppendLine("        <div class=\"kpi-title\">Активные процессы</div>");
        sb.AppendLine($"        <div class=\"kpi-value\">{s.TotalProcessesCount}</div>");
        sb.AppendLine($"        <div class=\"kpi-detail\">ЦП: <strong>{s.TotalCpuPercent:F1}%</strong> • ОЗУ: <strong>{s.TotalRamUsedMb:F0} МБ</strong></div>");
        sb.AppendLine("      </div>");

        sb.AppendLine("    </section>");
    }

    /// <summary>AppendControls: Добавляет секцию разметки Controls.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    /// <param name="report">Входные данные: значение параметра report (AuditReport).</param>
    private static void AppendControls(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("    <div class=\"controls-bar\">");
        sb.AppendLine("      <div class=\"tabs-group\">");
        sb.AppendLine($"        <button class=\"tab-btn active\" onclick=\"switchTab('tab-startup')\">Автозапуск <span class=\"counter-pill\">{report.StartupEntries.Count}</span></button>");
        sb.AppendLine($"        <button class=\"tab-btn\" onclick=\"switchTab('tab-processes')\">Процессы <span class=\"counter-pill\">{report.ActiveProcesses.Count}</span></button>");
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"search-group\">");
        sb.AppendLine("        <input type=\"text\" id=\"liveSearch\" placeholder=\"Поиск по имени, пути, издателю, хешу...\" onkeyup=\"filterTables()\" autocomplete=\"off\" />");
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"filter-chips\">");
        sb.AppendLine("        <button class=\"chip active\" onclick=\"setFilter('all', this)\">Все</button>");
        sb.AppendLine("        <button class=\"chip chip-threat\" onclick=\"setFilter('threats', this)\">⚠ Только угрозы</button>");
        sb.AppendLine("        <button class=\"chip\" onclick=\"setFilter('enabled', this)\">Включенные</button>");
        sb.AppendLine("        <button class=\"chip\" onclick=\"setFilter('blocked', this)\">Заблокированные</button>");
        sb.AppendLine("        <button class=\"chip\" onclick=\"setFilter('services', this)\">Службы</button>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
    }

    /// <summary>AppendStartupEntriesSection: Добавляет секцию разметки StartupEntriesSection.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    /// <param name="entries">Входные данные: значение параметра entries (List<StartupEntry>).</param>
    private static void AppendStartupEntriesSection(StringBuilder sb, List<StartupEntry> entries)
    {
        sb.AppendLine("    <div id=\"tab-startup\" class=\"tab-content active\">");
        sb.AppendLine("      <div class=\"table-card\">");
        sb.AppendLine("        <div class=\"table-header-row\">");
        sb.AppendLine("          <h2>Записи автозагрузки Windows</h2>");
        sb.AppendLine($"          <div class=\"table-stats\">Найдено: <span id=\"startupCount\">{entries.Count}</span></div>");
        sb.AppendLine("        </div>");

        sb.AppendLine("        <div class=\"table-responsive\">");
        sb.AppendLine("          <table class=\"audit-table\" id=\"startupTable\">");
        sb.AppendLine("            <thead>");
        sb.AppendLine("              <tr>");
        sb.AppendLine("                <th style=\"width: 30px;\"></th>");
        sb.AppendLine("                <th>Имя программы</th>");
        sb.AppendLine("                <th>Расположение</th>");
        sb.AppendLine("                <th>Издатель</th>");
        sb.AppendLine("                <th>Статус</th>");
        sb.AppendLine("                <th>ИИ-Рейтинг</th>");
        sb.AppendLine("                <th>Kaspersky OpenTIP</th>");
        sb.AppendLine("                <th>Эвристика CLI</th>");
        sb.AppendLine("              </tr>");
        sb.AppendLine("            </thead>");
        sb.AppendLine("            <tbody>");

        int index = 0;
        foreach (var entry in entries)
        {
            index++;
            var detailId = $"entry-detail-{index}";
            var hasThreat = entry.ThreatInfo != null && entry.ThreatInfo.HasThreat;
            var threatClass = hasThreat ? "row-threat" : string.Empty;
            var statusClass = GetStatusCssClass(entry.Status);
            var categoryData = entry.Location.ToString().ToLowerInvariant();
            var searchData = $"{entry.Name} {entry.Publisher} {entry.FilePath} {entry.CommandLine} {entry.SHA256}".ToLowerInvariant();

            sb.AppendLine($"              <tr class=\"data-row {threatClass}\" data-status=\"{entry.Status.ToString().ToLowerInvariant()}\" data-threat=\"{hasThreat.ToString().ToLowerInvariant()}\" data-location=\"{categoryData}\" data-search=\"{H(searchData)}\" onclick=\"toggleDetail('{detailId}')\">");
            sb.AppendLine($"                <td class=\"expand-cell\"><span class=\"arrow-icon\" id=\"arrow-{detailId}\">▶</span></td>");
            sb.AppendLine($"                <td class=\"name-cell\"><strong>{H(entry.Name)}</strong></td>");
            sb.AppendLine($"                <td><span class=\"badge-loc\">{H(entry.LocationDetail ?? entry.Location.ToString())}</span></td>");
            sb.AppendLine($"                <td class=\"publisher-cell\">{H(entry.Publisher)}</td>");
            sb.AppendLine($"                <td><span class=\"badge-status {statusClass}\">{GetStatusDisplayName(entry.Status)}</span></td>");
            sb.AppendLine($"                <td>{RenderEntryAiRating(entry)}</td>");
            sb.AppendLine($"                <td>{RenderCirclBadge(entry.KasperskyZone, entry.KasperskyDetection)}</td>");
            sb.AppendLine($"                <td>{RenderThreatBadge(entry.ThreatInfo)}</td>");
            sb.AppendLine("              </tr>");

            // Раскрывающаяся строка с детальной информацией
            sb.AppendLine($"              <tr id=\"{detailId}\" class=\"detail-row\" style=\"display: none;\">");
            sb.AppendLine("                <td colspan=\"8\">");
            sb.AppendLine("                  <div class=\"detail-card\">");
            sb.AppendLine("                    <div class=\"detail-grid\">");

            sb.AppendLine("                      <div class=\"detail-item full-width\">");
            sb.AppendLine("                        <div class=\"detail-label\">Командная строка запуска:</div>");
            sb.AppendLine($"                        <pre class=\"detail-code\">{H(entry.CommandLine ?? entry.FilePath ?? "—")}</pre>");
            sb.AppendLine("                      </div>");

            sb.AppendLine("                      <div class=\"detail-item full-width\">");
            sb.AppendLine("                        <div class=\"detail-label\">Путь к файлу:</div>");
            sb.AppendLine($"                        <div class=\"detail-path\">{H(entry.FilePath ?? "—")}</div>");
            sb.AppendLine("                      </div>");

            if (!string.IsNullOrEmpty(entry.SHA256))
            {
                sb.AppendLine("                      <div class=\"detail-item\">");
                sb.AppendLine("                        <div class=\"detail-label\">Хеш SHA-256:</div>");
                sb.AppendLine($"                        <div class=\"detail-hash\">{H(entry.SHA256)}</div>");
                sb.AppendLine("                      </div>");
            }

            sb.AppendLine("                      <div class=\"detail-item\">");
            sb.AppendLine("                        <div class=\"detail-label\">Обнаружено:</div>");
            sb.AppendLine($"                        <div>{entry.DetectedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}</div>");
            sb.AppendLine("                      </div>");

            if (hasThreat && entry.ThreatInfo != null)
            {
                sb.AppendLine("                      <div class=\"detail-item full-width threat-box\">");
                sb.AppendLine($"                        <div class=\"detail-label threat-title\">⚠ Эвристический анализ командной строки ({entry.ThreatInfo.ThreatLevel}):</div>");
                sb.AppendLine($"                        <div class=\"threat-desc\">{H(entry.ThreatInfo.Description)}</div>");
                if (entry.ThreatInfo.Indicators != null && entry.ThreatInfo.Indicators.Count > 0)
                {
                    sb.AppendLine("                        <ul class=\"threat-indicators\">");
                    foreach (var ind in entry.ThreatInfo.Indicators)
                    {
                        sb.AppendLine($"                          <li>{H(ind)}</li>");
                    }
                    sb.AppendLine("                        </ul>");
                }
                sb.AppendLine("                      </div>");
            }

            if (!string.IsNullOrEmpty(entry.AiRecommendation))
            {
                sb.AppendLine("                      <div class=\"detail-item full-width ai-box\">");
                sb.AppendLine($"                        <div class=\"detail-label ai-title\">🤖 Оценка искусственного интеллекта ({entry.AiCategory ?? "Общее"} / {entry.AiRisk ?? "Не указан"}):</div>");
                sb.AppendLine($"                        <div class=\"ai-desc\">{H(entry.AiRecommendation)}</div>");
                sb.AppendLine("                      </div>");
            }

            sb.AppendLine("                    </div>"); // /detail-grid
            sb.AppendLine("                  </div>"); // /detail-card
            sb.AppendLine("                </td>");
            sb.AppendLine("              </tr>");
        }

        sb.AppendLine("            </tbody>");
        sb.AppendLine("          </table>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
    }

    /// <summary>AppendProcessesSection: Добавляет секцию разметки ProcessesSection.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    /// <param name="processes">Входные данные: значение параметра processes (List<ProcessInfo>).</param>
    private static void AppendProcessesSection(StringBuilder sb, List<ProcessInfo> processes)
    {
        sb.AppendLine("    <div id=\"tab-processes\" class=\"tab-content\">");
        sb.AppendLine("      <div class=\"table-card\">");
        sb.AppendLine("        <div class=\"table-header-row\">");
        sb.AppendLine("          <h2>Активные процессы системы (Snapshot)</h2>");
        sb.AppendLine($"          <div class=\"table-stats\">Всего: <span id=\"processCount\">{processes.Count}</span></div>");
        sb.AppendLine("        </div>");

        sb.AppendLine("        <div class=\"table-responsive\">");
        sb.AppendLine("          <table class=\"audit-table\" id=\"processTable\">");
        sb.AppendLine("            <thead>");
        sb.AppendLine("              <tr>");
        sb.AppendLine("                <th>PID</th>");
        sb.AppendLine("                <th>Имя процесса</th>");
        sb.AppendLine("                <th>Издатель</th>");
        sb.AppendLine("                <th>ЦП %</th>");
        sb.AppendLine("                <th>Память (ОЗУ)</th>");
        sb.AppendLine("                <th>Диск</th>");
        sb.AppendLine("                <th>Сеть</th>");
        sb.AppendLine("                <th>Автозапуск</th>");
        sb.AppendLine("                <th>Статус</th>");
        sb.AppendLine("                <th>Эвристика CLI</th>");
        sb.AppendLine("              </tr>");
        sb.AppendLine("            </thead>");
        sb.AppendLine("            <tbody>");

        foreach (var proc in processes)
        {
            var hasThreat = proc.ThreatInfo != null && proc.ThreatInfo.HasThreat;
            var threatClass = hasThreat ? "row-threat" : string.Empty;
            double memMb = proc.WorkingSetBytes / (1024.0 * 1024.0);
            double diskKbs = (proc.DiskReadBytesPerSec + proc.DiskWriteBytesPerSec) / 1024.0;
            double netKbs = proc.NetworkBytesPerSec / 1024.0;
            var searchData = $"{proc.ProcessId} {proc.ProcessName} {proc.Publisher} {proc.FilePath} {proc.CommandLine}".ToLowerInvariant();

            sb.AppendLine($"              <tr class=\"data-row {threatClass}\" data-threat=\"{hasThreat.ToString().ToLowerInvariant()}\" data-search=\"{H(searchData)}\">");
            sb.AppendLine($"                <td class=\"mono\">{proc.ProcessId}</td>");
            sb.AppendLine($"                <td class=\"name-cell\"><strong>{H(proc.ProcessName)}</strong></td>");
            sb.AppendLine($"                <td class=\"publisher-cell\">{H(proc.Publisher)}</td>");
            sb.AppendLine($"                <td class=\"mono\">{proc.CpuUsagePercent:F1}%</td>");
            sb.AppendLine($"                <td class=\"mono\">{memMb:F1} МБ</td>");
            sb.AppendLine($"                <td class=\"mono\">{FormatRate(diskKbs)}</td>");
            sb.AppendLine($"                <td class=\"mono\">{FormatRate(netKbs)}</td>");
            sb.AppendLine($"                <td>{RenderStartupLink(proc)}</td>");
            sb.AppendLine($"                <td>{RenderProcessState(proc)}</td>");
            sb.AppendLine($"                <td>{RenderThreatBadge(proc.ThreatInfo)}</td>");
            sb.AppendLine("              </tr>");
        }

        sb.AppendLine("            </tbody>");
        sb.AppendLine("          </table>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
    }

    /// <summary>AppendFooter: Добавляет секцию разметки Footer.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    /// <param name="m">Входные данные: значение параметра m (AuditMetadata).</param>
    private static void AppendFooter(StringBuilder sb, AuditMetadata m)
    {
        sb.AppendLine("    <footer class=\"report-footer\">");
        sb.AppendLine("      <div>Сгенерировано утилитой <strong>Startup Monitor</strong> v" + H(m.AppVersion) + " • EDR-lite &amp; Windows Startup Guard</div>");
        sb.AppendLine("      <div class=\"footer-sub\">Все права защищены. Отчет предназначен для системного аудита и анализа безопасности.</div>");
        sb.AppendLine("    </footer>");
    }

    /// <summary>RenderAiStars: Выполняет операцию RenderAiStars.</summary>
    /// <param name="avgRating">Входные данные: значение параметра avgRating (double).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string RenderAiStars(double avgRating)
    {
        if (avgRating <= 0) return "<span class=\"stars-none\">Не оценено</span>";

        int starCount = avgRating switch
        {
            <= 2.0 => 5,
            <= 4.0 => 4,
            <= 6.0 => 3,
            <= 8.0 => 2,
            _ => 1
        };

        string colorClass = starCount switch
        {
            5 => "star-green",
            4 => "star-lime",
            3 => "star-amber",
            2 => "star-orange",
            _ => "star-red"
        };

        var filled = new string('★', starCount);
        var empty = new string('☆', 5 - starCount);
        return $"<span class=\"{colorClass}\">{filled}{empty}</span>";
    }

    /// <summary>RenderEntryAiRating: Выполняет операцию RenderEntryAiRating.</summary>
    /// <param name="entry">Входные данные: значение параметра entry (StartupEntry).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string RenderEntryAiRating(StartupEntry entry)
    {
        if (entry.AiRating <= 0)
            return "<span class=\"stars-none\">—</span>";

        int rating = entry.AiRating;
        int stars = rating switch
        {
            <= 2 => 5,
            <= 4 => 4,
            <= 6 => 3,
            <= 8 => 2,
            _ => 1
        };

        string colorClass = stars switch
        {
            5 => "star-green",
            4 => "star-lime",
            3 => "star-amber",
            2 => "star-orange",
            _ => "star-red"
        };

        var filled = new string('★', stars);
        var empty = new string('☆', 5 - stars);
        return $"<span class=\"{colorClass}\" title=\"Оценка: {rating}/10 ({entry.AiRisk})\">{filled}{empty}</span>";
    }

    /// <summary>RenderCirclBadge: Выполняет операцию RenderCirclBadge.</summary>
    /// <param name="zone">Входные данные: значение параметра zone (string?).</param>
    /// <param name="detection">Входные данные: значение параметра detection (string?).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string RenderCirclBadge(string? zone, string? detection)
    {
        var z = zone?.ToLowerInvariant() ?? "";
        if (z == "found" || z == "red")
            return $"<span class=\"badge-circl red\" title=\"{H(detection)}\">Угроза: {H(detection ?? "Malware")}</span>";
        if (z == "yellow")
            return $"<span class=\"badge-circl yellow\" title=\"{H(detection)}\">Подозрительно: {H(detection ?? "Adware/Riskware")}</span>";
        if (z == "notfound" || z == "green")
            return "<span class=\"badge-circl green\">Чист (Kaspersky)</span>";
        if (z == "trusted")
            return "<span class=\"badge-circl green\">Доверенный</span>";

        return "<span class=\"badge-circl grey\">Не проверялся</span>";
    }

    /// <summary>RenderThreatBadge: Выполняет операцию RenderThreatBadge.</summary>
    /// <param name="info">Входные данные: значение параметра info (CliThreatInfo?).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string RenderThreatBadge(CliThreatInfo? info)
    {
        if (info == null || !info.HasThreat)
            return "<span class=\"threat-none\">—</span>";

        string badgeClass = info.ThreatLevel switch
        {
            CliThreatLevel.High => "badge-threat high",
            CliThreatLevel.Medium => "badge-threat medium",
            _ => "badge-threat low"
        };

        return $"<span class=\"{badgeClass}\" title=\"{H(info.Description)}\">⚠ {H(info.BadgeText ?? "Угроза CLI")}</span>";
    }

    /// <summary>RenderStartupLink: Выполняет операцию RenderStartupLink.</summary>
    /// <param name="proc">Входные данные: значение параметра proc (ProcessInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string RenderStartupLink(ProcessInfo proc)
    {
        if (!proc.IsFromStartup)
            return "<span class=\"proc-normal\">—</span>";

        var locText = proc.StartupLocation.HasValue ? proc.StartupLocation.Value.ToString() : "Автозапуск";
        return $"<span class=\"badge-startup\" title=\"{H(locText)}\">🛡 Автозапуск</span>";
    }

    /// <summary>RenderProcessState: Выполняет операцию RenderProcessState.</summary>
    /// <param name="proc">Входные данные: значение параметра proc (ProcessInfo).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string RenderProcessState(ProcessInfo proc)
    {
        if (proc.IsBlocked) return "<span class=\"badge-status blocked\">Чёрный список</span>";
        if (proc.IsSuspended) return "<span class=\"badge-status suspended\">⏸ Приостановлен</span>";
        if (proc.IsService) return "<span class=\"badge-status service\">Служба</span>";
        if (proc.IsSystemProcess) return "<span class=\"badge-status system\">Системный</span>";
        return "<span class=\"badge-status active\">Активен</span>";
    }

    /// <summary>GetStatusCssClass: Возвращает StatusCssClass.</summary>
    /// <param name="status">Входные данные: значение параметра status (EntryStatus).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string GetStatusCssClass(EntryStatus status) => status switch
    {
        EntryStatus.Enabled => "enabled",
        EntryStatus.Disabled => "disabled",
        EntryStatus.Blocked => "blocked",
        _ => "unknown"
    };

    /// <summary>GetStatusDisplayName: Возвращает StatusDisplayName.</summary>
    /// <param name="status">Входные данные: значение параметра status (EntryStatus).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string GetStatusDisplayName(EntryStatus status) => status switch
    {
        EntryStatus.Enabled => "Включена",
        EntryStatus.Disabled => "Отключена",
        EntryStatus.Blocked => "Заблокирована",
        _ => status.ToString()
    };

    /// <summary>FormatRate: Выполняет операцию FormatRate.</summary>
    /// <param name="kbs">Входные данные: значение параметра kbs (double).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string FormatRate(double kbs)
    {
        if (kbs >= 1024.0)
            return $"{kbs / 1024.0:F1} МБ/с";
        if (kbs > 0.0)
            return $"{kbs:F1} КБ/с";
        return "0 КБ/с";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>AppendStyles: Добавляет секцию разметки Styles.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    private static void AppendStyles(StringBuilder sb)
    {
        sb.AppendLine(@"
    :root {
      --bg-main: #0B0F19;
      --bg-card: rgba(17, 24, 39, 0.7);
      --bg-card-hover: rgba(26, 36, 56, 0.85);
      --border-card: rgba(255, 255, 255, 0.08);
      --text-main: #F1F5F9;
      --text-muted: #94A3B8;
      --accent-cyan: #00D2D3;
      --accent-blue: #3B82F6;
      --success: #10B981;
      --warning: #F59E0B;
      --danger: #EF4444;
      --radius: 12px;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      background-color: var(--bg-main);
      color: var(--text-main);
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI Variable Text', 'Segoe UI', Roboto, sans-serif;
      line-height: 1.5;
      padding: 24px;
      font-size: 14px;
    }
    .report-wrapper { max-width: 1400px; margin: 0 auto; }
    .report-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      padding: 24px;
      background: linear-gradient(135deg, rgba(0, 210, 211, 0.08) 0%, rgba(59, 130, 246, 0.05) 100%), var(--bg-card);
      border: 1px solid var(--border-card);
      border-radius: var(--radius);
      margin-bottom: 20px;
      backdrop-filter: blur(16px);
      flex-wrap: wrap;
      gap: 16px;
    }
    .header-left { display: flex; align-items: center; gap: 16px; }
    .logo-badge {
      width: 48px; height: 48px;
      border-radius: 12px;
      background: rgba(0, 210, 211, 0.1);
      display: flex; align-items: center; justify-content: center;
      border: 1px solid rgba(0, 210, 211, 0.3);
    }
    .report-title { font-size: 24px; font-weight: 700; }
    .title-accent { color: var(--accent-cyan); font-weight: 400; }
    .report-subtitle { color: var(--text-muted); font-size: 13px; }
    .header-meta { display: flex; flex-wrap: wrap; gap: 8px; justify-content: flex-end; }
    .meta-pill {
      background: rgba(255, 255, 255, 0.04);
      padding: 4px 10px;
      border-radius: 6px;
      border: 1px solid rgba(255, 255, 255, 0.06);
      font-size: 12px;
    }
    .meta-label { color: var(--text-muted); margin-right: 4px; }
    .meta-val { color: var(--text-main); }
    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
      gap: 16px;
      margin-bottom: 24px;
    }
    .kpi-card {
      background: var(--bg-card);
      border: 1px solid var(--border-card);
      border-radius: var(--radius);
      padding: 18px;
      position: relative;
      overflow: hidden;
      transition: transform 0.2s, border-color 0.2s;
    }
    .kpi-card:hover { transform: translateY(-2px); border-color: rgba(255, 255, 255, 0.15); }
    .kpi-card::before {
      content: ''; position: absolute; top: 0; left: 0; width: 4px; height: 100%;
    }
    .card-total::before { background: var(--accent-blue); }
    .card-threats::before { background: var(--warning); }
    .card-threats.alert::before { background: var(--danger); }
    .card-blocked::before { background: #E11D48; }
    .card-ai::before { background: var(--success); }
    .card-procs::before { background: var(--accent-cyan); }
    .kpi-title { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; color: var(--text-muted); margin-bottom: 8px; }
    .kpi-value { font-size: 28px; font-weight: 700; line-height: 1.1; margin-bottom: 6px; }
    .kpi-detail { font-size: 12px; color: var(--text-muted); }
    .ai-stars { font-size: 20px; }
    .controls-bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 16px;
    }
    .tabs-group { display: flex; gap: 4px; background: rgba(255, 255, 255, 0.03); padding: 4px; border-radius: 8px; }
    .tab-btn {
      background: transparent; border: none; color: var(--text-muted);
      padding: 8px 16px; border-radius: 6px; cursor: pointer; font-size: 13px; font-weight: 600;
      transition: all 0.2s; display: flex; align-items: center; gap: 6px;
    }
    .tab-btn.active { background: rgba(255, 255, 255, 0.08); color: var(--text-main); }
    .counter-pill { background: rgba(255, 255, 255, 0.1); padding: 1px 6px; border-radius: 10px; font-size: 11px; }
    .search-group { flex: 1; min-width: 260px; max-width: 450px; }
    .search-group input {
      width: 100%; background: var(--bg-card); border: 1px solid var(--border-card);
      border-radius: 8px; padding: 8px 14px; color: var(--text-main); font-size: 13px; outline: none;
    }
    .search-group input:focus { border-color: var(--accent-cyan); }
    .filter-chips { display: flex; gap: 6px; flex-wrap: wrap; }
    .chip {
      background: rgba(255, 255, 255, 0.04); border: 1px solid rgba(255, 255, 255, 0.06);
      color: var(--text-muted); padding: 5px 12px; border-radius: 16px; font-size: 12px; cursor: pointer; transition: all 0.2s;
    }
    .chip:hover { border-color: rgba(255, 255, 255, 0.2); color: var(--text-main); }
    .chip.active { background: rgba(0, 210, 211, 0.15); border-color: var(--accent-cyan); color: var(--accent-cyan); font-weight: 600; }
    .chip-threat.active { background: rgba(239, 68, 68, 0.2); border-color: var(--danger); color: var(--danger); }
    .table-card {
      background: var(--bg-card); border: 1px solid var(--border-card);
      border-radius: var(--radius); overflow: hidden; backdrop-filter: blur(16px);
    }
    .table-header-row {
      display: flex; justify-content: space-between; align-items: center; padding: 16px 20px;
      border-bottom: 1px solid var(--border-card);
    }
    .table-header-row h2 { font-size: 16px; font-weight: 600; }
    .table-stats { color: var(--text-muted); font-size: 13px; }
    .table-responsive { overflow-x: auto; }
    .audit-table { width: 100%; border-collapse: collapse; text-align: left; }
    .audit-table th {
      padding: 12px 16px; font-size: 12px; font-weight: 600; text-transform: uppercase;
      letter-spacing: 0.5px; color: var(--text-muted); border-bottom: 1px solid var(--border-card);
      background: rgba(0, 0, 0, 0.2);
    }
    .audit-table td { padding: 12px 16px; border-bottom: 1px solid rgba(255, 255, 255, 0.04); font-size: 13px; }
    .data-row { cursor: pointer; transition: background 0.15s; }
    .data-row:hover { background: var(--bg-card-hover); }
    .row-threat { background: rgba(239, 68, 68, 0.06); }
    .expand-cell { text-align: center; color: var(--text-muted); width: 24px; }
    .name-cell strong { color: #FFF; }
    .publisher-cell { color: var(--text-muted); font-size: 12px; max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .mono { font-family: 'Cascadia Code', Consolas, monospace; font-size: 12px; }
    .badge-status {
      display: inline-block; padding: 3px 8px; border-radius: 4px; font-size: 11px; font-weight: 600;
    }
    .badge-status.enabled { background: rgba(16, 185, 129, 0.15); color: #34D399; border: 1px solid rgba(16, 185, 129, 0.3); }
    .badge-status.disabled { background: rgba(148, 163, 184, 0.15); color: #94A3B8; border: 1px solid rgba(148, 163, 184, 0.3); }
    .badge-status.blocked { background: rgba(239, 68, 68, 0.2); color: #F87171; border: 1px solid rgba(239, 68, 68, 0.3); }
    .badge-status.suspended { background: rgba(245, 158, 11, 0.2); color: #FBBF24; border: 1px solid rgba(245, 158, 11, 0.3); }
    .badge-status.service { background: rgba(59, 130, 246, 0.15); color: #60A5FA; border: 1px solid rgba(59, 130, 246, 0.3); }
    .badge-status.system { background: rgba(168, 85, 247, 0.15); color: #C084FC; border: 1px solid rgba(168, 85, 247, 0.3); }
    .badge-status.active { background: rgba(16, 185, 129, 0.1); color: #34D399; }
    .badge-loc { font-size: 11px; color: var(--text-muted); background: rgba(255, 255, 255, 0.04); padding: 2px 6px; border-radius: 4px; }
    .badge-circl { display: inline-block; padding: 2px 7px; border-radius: 4px; font-size: 11px; font-weight: 500; }
    .badge-circl.green { background: rgba(16, 185, 129, 0.15); color: #34D399; }
    .badge-circl.red { background: rgba(239, 68, 68, 0.2); color: #F87171; font-weight: 600; }
    .badge-circl.grey { color: #64748B; }
    .badge-threat {
      display: inline-block; padding: 3px 8px; border-radius: 4px; font-size: 11px; font-weight: 600;
    }
    .badge-threat.high { background: rgba(239, 68, 68, 0.25); color: #F87171; border: 1px solid rgba(239, 68, 68, 0.4); }
    .badge-threat.medium { background: rgba(245, 158, 11, 0.25); color: #FBBF24; border: 1px solid rgba(245, 158, 11, 0.4); }
    .badge-threat.low { background: rgba(59, 130, 246, 0.2); color: #60A5FA; border: 1px solid rgba(59, 130, 246, 0.3); }
    .badge-startup { display: inline-block; padding: 2px 6px; border-radius: 4px; font-size: 11px; background: rgba(0, 210, 211, 0.15); color: var(--accent-cyan); }
    .star-green { color: #10B981; }
    .star-lime { color: #84CC16; }
    .star-amber { color: #F59E0B; }
    .star-orange { color: #F97316; }
    .star-red { color: #EF4444; }
    .stars-none { color: #64748B; }
    .threat-none, .proc-normal { color: #475569; }
    .detail-row td { background: rgba(0, 0, 0, 0.35); padding: 16px 20px; }
    .detail-card { background: rgba(255, 255, 255, 0.02); border: 1px solid rgba(255, 255, 255, 0.05); border-radius: 8px; padding: 14px; }
    .detail-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .detail-item.full-width { grid-column: span 2; }
    .detail-label { font-size: 11px; text-transform: uppercase; font-weight: 600; color: var(--text-muted); margin-bottom: 4px; }
    .detail-code { background: #070A10; padding: 8px 12px; border-radius: 6px; font-family: monospace; font-size: 12px; color: #E2E8F0; word-break: break-all; white-space: pre-wrap; }
    .detail-path, .detail-hash { font-family: monospace; font-size: 12px; color: #CBD5E1; word-break: break-all; }
    .threat-box { background: rgba(239, 68, 68, 0.08); border: 1px solid rgba(239, 68, 68, 0.2); border-radius: 6px; padding: 10px 14px; }
    .threat-title { color: #F87171; font-weight: 700; }
    .threat-desc { color: #FCA5A5; font-size: 13px; margin-bottom: 6px; }
    .threat-indicators { margin-left: 20px; color: #FCA5A5; font-size: 12px; }
    .ai-box { background: rgba(16, 185, 129, 0.08); border: 1px solid rgba(16, 185, 129, 0.2); border-radius: 6px; padding: 10px 14px; }
    .ai-title { color: #34D399; font-weight: 700; }
    .ai-desc { color: #D1FAE5; font-size: 13px; }
    .tab-content { display: none; }
    .tab-content.active { display: block; }
    .report-footer { margin-top: 32px; text-align: center; color: var(--text-muted); font-size: 12px; padding: 16px; border-top: 1px solid var(--border-card); }
    .footer-sub { font-size: 11px; margin-top: 4px; color: #64748B; }

    @media print {
      body { background: #FFF !important; color: #000 !important; padding: 0 !important; }
      .report-wrapper { max-width: 100% !important; }
      .controls-bar, .expand-cell, .arrow-icon { display: none !important; }
      .kpi-card, .table-card, .report-header { background: #FFF !important; border: 1px solid #CCC !important; color: #000 !important; }
      .kpi-title, .kpi-detail, .meta-label, .table-stats, th { color: #333 !important; }
      .kpi-value, .name-cell strong { color: #000 !important; }
      .tab-content { display: block !important; }
      .detail-row { display: table-row !important; }
      .badge-status, .badge-threat, .badge-circl { border: 1px solid #999 !important; color: #000 !important; background: transparent !important; }
      .detail-code { background: #F3F4F6 !important; color: #000 !important; border: 1px solid #CCC !important; }
    }
");
    }

    /// <summary>AppendScripts: Добавляет секцию разметки Scripts.</summary>
    /// <param name="sb">Входные данные: значение параметра sb (StringBuilder).</param>
    private static void AppendScripts(StringBuilder sb)
    {
        sb.AppendLine(@"
    let currentFilter = 'all';

    function switchTab(tabId) {
      document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
      document.querySelectorAll('.tab-btn').forEach(el => el.classList.remove('active'));
      const target = document.getElementById(tabId);
      if (target) target.classList.add('active');
      event.currentTarget.classList.add('active');
      filterTables();
    }

    function toggleDetail(detailId) {
      const row = document.getElementById(detailId);
      const arrow = document.getElementById('arrow-' + detailId);
      if (!row) return;
      if (row.style.display === 'none' || !row.style.display) {
        row.style.display = 'table-row';
        if (arrow) arrow.textContent = '▼';
      } else {
        row.style.display = 'none';
        if (arrow) arrow.textContent = '▶';
      }
    }

    function setFilter(filter, btn) {
      currentFilter = filter;
      document.querySelectorAll('.chip').forEach(c => c.classList.remove('active'));
      if (btn) btn.classList.add('active');
      filterTables();
    }

    function filterTables() {
      const query = (document.getElementById('liveSearch').value || '').trim().toLowerCase();
      
      // Фильтр автозапуска
      let visibleStartup = 0;
      document.querySelectorAll('#startupTable tbody tr.data-row').forEach(row => {
        const search = row.getAttribute('data-search') || '';
        const status = row.getAttribute('data-status') || '';
        const isThreat = row.getAttribute('data-threat') === 'true';
        const location = row.getAttribute('data-location') || '';
        
        let matchesQuery = !query || search.includes(query);
        let matchesFilter = true;

        if (currentFilter === 'threats') matchesFilter = isThreat;
        else if (currentFilter === 'enabled') matchesFilter = (status === 'enabled');
        else if (currentFilter === 'blocked') matchesFilter = (status === 'blocked');
        else if (currentFilter === 'services') matchesFilter = location.includes('service');

        const isVisible = matchesQuery && matchesFilter;
        row.style.display = isVisible ? '' : 'none';
        
        // Скрываем раскрытые детали, если строка скрыта
        const next = row.nextElementSibling;
        if (next && next.classList.contains('detail-row') && !isVisible) {
          next.style.display = 'none';
        }

        if (isVisible) visibleStartup++;
      });
      const startupCounter = document.getElementById('startupCount');
      if (startupCounter) startupCounter.textContent = visibleStartup;

      // Фильтр процессов
      let visibleProcs = 0;
      document.querySelectorAll('#processTable tbody tr.data-row').forEach(row => {
        const search = row.getAttribute('data-search') || '';
        const isThreat = row.getAttribute('data-threat') === 'true';

        let matchesQuery = !query || search.includes(query);
        let matchesFilter = true;
        if (currentFilter === 'threats') matchesFilter = isThreat;

        const isVisible = matchesQuery && matchesFilter;
        row.style.display = isVisible ? '' : 'none';
        if (isVisible) visibleProcs++;
      });
      const procCounter = document.getElementById('processCount');
      if (procCounter) procCounter.textContent = visibleProcs;
    }
");
    }
}

