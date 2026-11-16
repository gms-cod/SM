using System;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Статический построитель DTO телеметрии инцидентов, используемый для преобразования
/// <see cref="TrafficAnomalyObservation"/> в структурированный объект <see cref="IncidentTelemetryDto"/>.
/// </summary>
public static class IncidentTelemetryBuilder
{
    /// <summary>
    /// Создает DTO телеметрии инцидента на основе наблюдения о сетевой аномалии и контекста процесса.
    /// </summary>
    /// <param name="observation">Наблюдение о сетевой аномалии.</param>
    /// <param name="processPath">Полный путь к процессу (может быть null).</param>
    /// <param name="commandLine">Командная строка запуска процесса (может быть null).</param>
    /// <param name="signer">Подписант исполняемого файла (может быть null).</param>
    /// <param name="sha256">SHA-256 хэш исполняемого файла (может быть null).</param>
    /// <param name="isSystem">Флаг, указывающий, является ли процесс системным.</param>
    /// <param name="parentProcessName">Имя родительского процесса (может быть null).</param>
    /// <returns>Заполненный экземпляр <see cref="IncidentTelemetryDto"/>.</returns>
    public static IncidentTelemetryDto Build(
        TrafficAnomalyObservation observation,
        string? processPath = null,
        string? commandLine = null,
        string? signer = null,
        string? sha256 = null,
        bool isSystem = false,
        string? parentProcessName = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        bool isUserWritable = IsUserWritablePath(processPath);
        bool isBeaconing = string.Equals(observation.AnomalyType, "Beaconing", StringComparison.OrdinalIgnoreCase) || observation.JitterPercent <= 25.0;
        bool isNonStandardPort = string.Equals(observation.AnomalyType, "NonStandardPort", StringComparison.OrdinalIgnoreCase) ||
                                 (observation.RemotePort != 80 && observation.RemotePort != 443 && observation.RemotePort != 53);

        return new IncidentTelemetryDto
        {
            IncidentId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            Process = new IncidentTelemetryDto.ProcessTelemetry
            {
                Pid = observation.ProcessId,
                StartTimeUtc = observation.DetectedAt,
                Name = observation.ProcessName,
                Path = processPath ?? string.Empty,
                Sha256 = sha256 ?? string.Empty,
                CommandLine = commandLine ?? string.Empty,
                Signer = signer ?? string.Empty,
                IsSystem = isSystem,
                ParentProcessName = parentProcessName ?? string.Empty
            },
            Traffic = new IncidentTelemetryDto.TrafficTelemetry
            {
                Direction = "Outbound",
                Protocol = observation.Protocol,
                RemoteAddress = observation.RemoteAddress,
                RemotePort = observation.RemotePort,
                ObservationCount = observation.ObservationCount,
                AverageIntervalSeconds = observation.AverageIntervalSeconds,
                JitterPercent = observation.JitterPercent,
                AnomalyType = observation.AnomalyType,
                RiskScore = observation.RiskScore
            },
            Heuristics = new IncidentTelemetryDto.HeuristicsTelemetry
            {
                IsBeaconing = isBeaconing,
                IsNonStandardPort = isNonStandardPort,
                IsUserWritablePath = isUserWritable,
                PreliminaryRiskScore = observation.RiskScore
            }
        };
    }

    /// <summary>
    /// Определяет, находится ли путь к файлу в общедоступных пользовательских директориях записи (Temp, AppData, Downloads, Desktop).
    /// </summary>
    /// <param name="path">Путь к исполняемому файлу.</param>
    /// <returns>True, если путь расположен в пользовательском каталоге записи.</returns>
    public static bool IsUserWritablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var lower = path.ToLowerInvariant();
        return lower.Contains("\\appdata\\") ||
               lower.Contains("\\temp\\") ||
               lower.Contains("\\downloads\\") ||
               lower.Contains("\\desktop\\") ||
               lower.Contains("\\users\\public\\");
    }
}
