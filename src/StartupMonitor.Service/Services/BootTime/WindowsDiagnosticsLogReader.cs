using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models.BootTime;

namespace StartupMonitor.Service.Services.BootTime;

/// <summary>
/// Считывает и парсит события из системного журнала Windows Event Log:
/// "Microsoft-Windows-Diagnostics-Performance/Operational"
/// </summary>
public class WindowsDiagnosticsLogReader
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private readonly ILogger _logger;

    /// <summary>WindowsDiagnosticsLogReader: Выполняет операцию WindowsDiagnosticsLogReader.</summary>
    /// <param name="logger">Входные данные: значение параметра logger (ILogger).</param>
    public WindowsDiagnosticsLogReader(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Считывает историю сессий запуска Windows (Event ID 100).
    /// </summary>
    /// <param name="maxSessions">Максимальное количество возвращаемых сессий.</param>
    public List<BootSessionInfo> ReadBootSessions(int maxSessions = 20)
    {
        var result = new List<BootSessionInfo>();

        try
        {
            var query = new EventLogQuery(
                LogName,
                PathType.LogName,
                "*[System[(EventID=100)]]")
            {
                ReverseDirection = true // От самых свежих к старым
            };

            using var reader = new EventLogReader(query);
            for (int i = 0; i < maxSessions; i++)
            {
                using var record = reader.ReadEvent();
                if (record == null) break;

                try
                {
                    var session = ParseEvent100(record);
                    if (session != null)
                    {
                        result.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ошибка разбора события 100 запуска системы");
                }
            }
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogWarning("Журнал событий '{LogName}' не найден в системе", LogName);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Нет доступа к чтению журнала событий '{LogName}'", LogName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать события запуска из '{LogName}'", LogName);
        }

        return result;
    }

    /// <summary>
    /// Считывает события замедления загрузки приложениями, службами и драйверами (Event ID 101–110).
    /// </summary>
    /// <param name="maxEvents">Максимальное количество событий.</param>
    public List<BootDegradationEvent> ReadDegradationEvents(int maxEvents = 50)
    {
        var result = new List<BootDegradationEvent>();

        try
        {
            // Запрашиваем события деградации производительности
            var query = new EventLogQuery(
                LogName,
                PathType.LogName,
                "*[System[(EventID >= 101 and EventID <= 110)]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            for (int i = 0; i < maxEvents; i++)
            {
                using var record = reader.ReadEvent();
                if (record == null) break;

                try
                {
                    var degradation = ParseDegradationEvent(record);
                    if (degradation != null)
                    {
                        result.Add(degradation);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ошибка разбора события деградации {Id}", record.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать события деградации старта из '{LogName}'", LogName);
        }

        return result;
    }

    /// <summary>ParseEvent100: Выполняет операцию ParseEvent100.</summary>
    /// <param name="record">Входные данные: значение параметра record (EventRecord).</param>
    /// <returns>Выходные данные: результирующее значение типа BootSessionInfo?.</returns>
    private static BootSessionInfo? ParseEvent100(EventRecord record)
    {
        var xml = record.ToXml();
        var doc = XDocument.Parse(xml);
        var dataElements = doc.Root?.Element("{http://schemas.microsoft.com/win/2004/08/events/event}EventData")?.Elements();
        if (dataElements == null) return null;

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in dataElements)
        {
            var name = el.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                data[name] = el.Value;
            }
        }

        var session = new BootSessionInfo
        {
            Timestamp = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
            BootInstance = ParseInt(data, "SystemBootInstance"),
            BootTimeMs = ParseLong(data, "BootTime"),
            MainPathBootTimeMs = ParseLong(data, "MainPathBootTime"),
            PostBootTimeMs = ParseLong(data, "BootPostBootTime"),
            KernelInitTimeMs = ParseLong(data, "BootKernelInitTime"),
            DriverInitTimeMs = ParseLong(data, "BootDriverInitTime"),
            CriticalServicesInitTimeMs = ParseLong(data, "BootCriticalServicesInitTime"),
            UserProfileInitTimeMs = ParseLong(data, "BootUserProfileProcessingTime"),
            ExplorerInitTimeMs = ParseLong(data, "BootExplorerInitTime"),
            StartupAppsCount = ParseInt(data, "BootNumStartupApps"),
            IsRebootAfterInstall = string.Equals(data.GetValueOrDefault("BootIsRebootAfterInstall"), "true", StringComparison.OrdinalIgnoreCase)
        };

        return session;
    }

    /// <summary>ParseDegradationEvent: Выполняет операцию ParseDegradationEvent.</summary>
    /// <param name="record">Входные данные: значение параметра record (EventRecord).</param>
    /// <returns>Выходные данные: результирующее значение типа BootDegradationEvent?.</returns>
    private static BootDegradationEvent? ParseDegradationEvent(EventRecord record)
    {
        var xml = record.ToXml();
        var doc = XDocument.Parse(xml);
        var dataElements = doc.Root?.Element("{http://schemas.microsoft.com/win/2004/08/events/event}EventData")?.Elements();
        if (dataElements == null) return null;

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in dataElements)
        {
            var name = el.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                data[name] = el.Value;
            }
        }

        var category = record.Id switch
        {
            101 => BootDegradationCategory.Application,
            102 => BootDegradationCategory.Driver,
            103 => BootDegradationCategory.Service,
            107 or 108 => BootDegradationCategory.GroupPolicy,
            109 => BootDegradationCategory.Device,
            _ => BootDegradationCategory.Other
        };

        var nameStr = data.GetValueOrDefault("Name")?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(nameStr))
        {
            nameStr = data.GetValueOrDefault("FriendlyName")?.Trim() ?? $"Event {record.Id}";
        }

        return new BootDegradationEvent
        {
            EventId = record.Id,
            Category = category,
            Name = nameStr,
            FriendlyName = data.GetValueOrDefault("FriendlyName")?.Trim(),
            Path = data.GetValueOrDefault("Path")?.Trim(),
            Publisher = data.GetValueOrDefault("CompanyName")?.Trim(),
            Version = data.GetValueOrDefault("Version")?.Trim(),
            TotalTimeMs = ParseLong(data, "TotalTime"),
            DegradationTimeMs = ParseLong(data, "DegradationTime"),
            Timestamp = record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow
        };
    }

    /// <summary>ParseInt: Выполняет операцию ParseInt.</summary>
    /// <param name="dict">Входные данные: значение параметра dict (Dictionary<string, string>).</param>
    /// <param name="key">Входные данные: значение параметра key (string).</param>
    /// <returns>Выходные данные: результирующее значение типа int.</returns>
    private static int ParseInt(Dictionary<string, string> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return 0;
    }

    /// <summary>ParseLong: Выполняет операцию ParseLong.</summary>
    /// <param name="dict">Входные данные: значение параметра dict (Dictionary<string, string>).</param>
    /// <param name="key">Входные данные: значение параметра key (string).</param>
    /// <returns>Выходные данные: результирующее значение типа long.</returns>
    private static long ParseLong(Dictionary<string, string> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return 0;
    }
}

