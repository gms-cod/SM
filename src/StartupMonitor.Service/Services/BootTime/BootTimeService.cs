using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.BootTime;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services.BootTime;

/// <summary>
/// Вспомогательный класс для нормализации путей к файлам (удаление кавычек, переменных среды).
/// </summary>
internal static class BootPathHelper
{
    /// <summary>Нормализует путь: раскрывает переменные среды, убирает лишние кавычки и аргументы.</summary>
    public static string CleanPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;

        // Убираем кавычки
        var p = rawPath.Trim().Trim('"');

        // Берём только первый токен (до пробела, если нет кавычек вокруг пути)
        var spaceIdx = p.IndexOf(' ', StringComparison.Ordinal);
        if (spaceIdx > 0 && !rawPath.StartsWith('"'))
            p = p[..spaceIdx];

        // Раскрываем переменные среды Windows (%SystemRoot%, %ProgramFiles% и т.д.)
        try { p = Environment.ExpandEnvironmentVariables(p); }
        catch { }

        return p.ToLowerInvariant();
    }
}

/// <summary>
/// Сервис анализа времени загрузки Windows и сопоставления виновников задержки старта с базой автозапуска.
/// </summary>
public class BootTimeService
{
    private readonly JsonDataStore _store;
    private readonly ILogger<BootTimeService> _logger;
    private readonly WindowsDiagnosticsLogReader _logReader;

    private readonly object _cacheLock = new();
    private BootTimeReport? _cachedReport;
    private DateTime _cacheTime = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public BootTimeService(JsonDataStore store, ILogger<BootTimeService> logger)
    {
        _store = store;
        _logger = logger;
        _logReader = new WindowsDiagnosticsLogReader(logger);
    }

    /// <summary>
    /// Возвращает актуальный отчет анализа времени загрузки Windows (с кешированием на 60 секунд).
    /// </summary>
    public BootTimeReport GetReport(bool forceRefresh = false)
    {
        using var _prof = ExecutionProfiler.Measure("Service.BootTime.GetReport");
        lock (_cacheLock)
        {
            if (!forceRefresh && _cachedReport != null && (DateTime.UtcNow - _cacheTime) < CacheTtl)
            {
                return _cachedReport;
            }

            try
            {
                var sessions = _logReader.ReadBootSessions(maxSessions: 20);
                var degradationEvents = _logReader.ReadDegradationEvents(maxEvents: 50);

                var latest = sessions.FirstOrDefault();

                // Фильтруем виновников: берем события, относящиеся к последней загрузке (или за последние 7 дней)
                var cutoff = latest != null ? latest.Timestamp.AddMinutes(-10) : DateTime.UtcNow.AddDays(-7);
                var relevantOffenders = degradationEvents
                    .Where(d => d.Timestamp >= cutoff || latest == null)
                    .OrderByDescending(d => d.DegradationTimeMs)
                    .ToList();

                // Если для последней загрузки событий мало, дополняем топовыми историческими
                if (relevantOffenders.Count < 5 && degradationEvents.Count > 0)
                {
                    relevantOffenders = degradationEvents
                        .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(x => x.DegradationTimeMs).First())
                        .OrderByDescending(d => d.DegradationTimeMs)
                        .Take(20)
                        .ToList();
                }

                // Связываем виновников с записями автозапуска
                var cfg = _store.Load();
                LinkOffendersToEntries(relevantOffenders, cfg.Entries);

                var report = new BootTimeReport
                {
                    LatestBoot = latest,
                    RecentSessions = sessions,
                    TopOffenders = relevantOffenders,
                    TotalSessionsRecorded = sessions.Count,
                    GeneratedAt = DateTime.UtcNow
                };

                _cachedReport = report;
                _cacheTime = DateTime.UtcNow;

                _logger.LogInformation("Сформирован отчет анализа загрузки: BootTime={BootTime}, сессий={Sessions}, виновников={Offenders}",
                    latest?.DisplayBootTime ?? "N/A", sessions.Count, relevantOffenders.Count);

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка формирования отчета времени загрузки системы");
                return _cachedReport ?? new BootTimeReport();
            }
        }
    }

    /// <summary>
    /// Обогащает элементы автозапуска метриками замедления старта системы (BootDelayBadge).
    /// </summary>
    public void EnrichEntriesWithBootImpact(IEnumerable<StartupEntry> entries)
    {
        var report = GetReport();
        if (!report.HasData || report.TopOffenders.Count == 0) return;

        // Индексируем виновников по имени и пути
        var offendersByName = new Dictionary<string, BootDegradationEvent>(StringComparer.OrdinalIgnoreCase);
        var offendersByPath = new Dictionary<string, BootDegradationEvent>(StringComparer.OrdinalIgnoreCase);

        foreach (var off in report.TopOffenders)
        {
            if (!string.IsNullOrWhiteSpace(off.Name) && !offendersByName.ContainsKey(off.Name))
            {
                offendersByName[off.Name] = off;
            }
            if (!string.IsNullOrWhiteSpace(off.Path))
            {
                var cleanPath = BootPathHelper.CleanPath(off.Path);
                if (!string.IsNullOrEmpty(cleanPath) && !offendersByPath.ContainsKey(cleanPath))
                {
                    offendersByPath[cleanPath] = off;
                }
            }
        }

        foreach (var entry in entries)
        {
            BootDegradationEvent? matched = null;

            // 1. Поиск по чистому пути файла
            if (!string.IsNullOrWhiteSpace(entry.FilePath))
            {
                var clean = BootPathHelper.CleanPath(entry.FilePath);
                if (!string.IsNullOrEmpty(clean) && offendersByPath.TryGetValue(clean, out var byPath))
                {
                    matched = byPath;
                }
            }

            // 2. Поиск по имени exe-файла
            if (matched == null && !string.IsNullOrWhiteSpace(entry.FilePath))
            {
                var fileName = Path.GetFileName(entry.FilePath);
                if (!string.IsNullOrEmpty(fileName) && offendersByName.TryGetValue(fileName, out var byName))
                {
                    matched = byName;
                }
            }

            // 3. Поиск по имени службы или записи
            if (matched == null && !string.IsNullOrWhiteSpace(entry.Name))
            {
                if (offendersByName.TryGetValue(entry.Name, out var byEntryName))
                {
                    matched = byEntryName;
                }
                else if (offendersByName.TryGetValue(entry.Name + ".exe", out var byExeName))
                {
                    matched = byExeName;
                }
            }

            if (matched != null && matched.DegradationTimeMs > 0)
            {
                entry.BootDelayImpactMs = matched.DegradationTimeMs;
                entry.BootDelayBadge = $"⏳ +{BootSessionInfo.FormatDuration(matched.DegradationTimeMs)}";
                entry.BootDelayTooltip = $"Программа замедлила запуск системы на {BootSessionInfo.FormatDuration(matched.DegradationTimeMs)} (общее время: {BootSessionInfo.FormatDuration(matched.TotalTimeMs)}) по данным журнала диагностики Windows";
                matched.LinkedStartupEntryId = entry.Id;
            }
        }
    }

    private static void LinkOffendersToEntries(List<BootDegradationEvent> offenders, List<StartupEntry> entries)
    {
        var entriesByPath = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
        var entriesByExeName = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
        var entriesByName = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            if (!string.IsNullOrWhiteSpace(e.FilePath))
            {
                var clean = BootPathHelper.CleanPath(e.FilePath);
                if (!string.IsNullOrEmpty(clean))
                {
                    entriesByPath[clean] = e;
                }
                var fileName = Path.GetFileName(e.FilePath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    entriesByExeName[fileName] = e;
                }
            }
            if (!string.IsNullOrWhiteSpace(e.Name))
            {
                entriesByName[e.Name] = e;
            }
        }

        foreach (var off in offenders)
        {
            if (!string.IsNullOrEmpty(off.LinkedStartupEntryId)) continue;

            if (!string.IsNullOrWhiteSpace(off.Path))
            {
                var clean = BootPathHelper.CleanPath(off.Path);
                if (!string.IsNullOrEmpty(clean) && entriesByPath.TryGetValue(clean, out var byPath))
                {
                    off.LinkedStartupEntryId = byPath.Id;
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(off.Name))
            {
                if (entriesByExeName.TryGetValue(off.Name, out var byExe))
                {
                    off.LinkedStartupEntryId = byExe.Id;
                    continue;
                }
                if (entriesByName.TryGetValue(off.Name, out var byName))
                {
                    off.LinkedStartupEntryId = byName.Id;
                    continue;
                }
            }
        }
    }
}

