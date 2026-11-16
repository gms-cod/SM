using System;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>
/// DTO телеметрии инцидента, используемый для передачи данных о сетевых аномалиях в модуль анализа и ИИ.
/// </summary>
public sealed class IncidentTelemetryDto
{
    /// <summary>Уникальный идентификатор инцидента (GUID).</summary>
    public string IncidentId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Временная метка инцидента в формате UTC.</summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Информация о процессе, инициировавшем сетевую активность.</summary>
    public ProcessTelemetry Process { get; set; } = new();

    /// <summary>Метаданные о сетевом трафике, связанном с инцидентом.</summary>
    public TrafficTelemetry Traffic { get; set; } = new();

    /// <summary>Эвристические показатели, вычисленные детектором аномалий.</summary>
    public HeuristicsTelemetry Heuristics { get; set; } = new();

    /// <summary>Телеметрические данные о процессе.</summary>
    public sealed class ProcessTelemetry
    {
        /// <summary>Идентификатор процесса (PID).</summary>
        public int Pid { get; set; }

        /// <summary>Временная метка запуска процесса в формате UTC.</summary>
        public DateTime StartTimeUtc { get; set; }

        /// <summary>Имя исполняемого файла процесса.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Полный путь к исполняемому файлу процесса.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>SHA-256 хэш исполняемого файла процесса.</summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>Командная строка запуска процесса.</summary>
        public string CommandLine { get; set; } = string.Empty;

        /// <summary>Информация о цифровой подписи (издатель / Unsigned).</summary>
        public string Signer { get; set; } = string.Empty;

        /// <summary>Является ли процесс системным (SYSTEM/LocalService).</summary>
        public bool IsSystem { get; set; }

        /// <summary>Имя родительского процесса.</summary>
        public string ParentProcessName { get; set; } = string.Empty;
    }

    /// <summary>Телеметрические данные о сетевом трафике.</summary>
    public sealed class TrafficTelemetry
    {
        /// <summary>Направление трафика (Outbound, Inbound).</summary>
        public string Direction { get; set; } = "Outbound";

        /// <summary>Сетевой протокол (TCP, UDP).</summary>
        public string Protocol { get; set; } = "TCP";

        /// <summary>Удалённый IP-адрес назначения.</summary>
        public string RemoteAddress { get; set; } = string.Empty;

        /// <summary>Удалённый порт назначения.</summary>
        public int RemotePort { get; set; }

        /// <summary>Количество зафиксированных наблюдений.</summary>
        public int ObservationCount { get; set; }

        /// <summary>Средний интервал между соединениями в секундах.</summary>
        public double AverageIntervalSeconds { get; set; }

        /// <summary>Процент относительного отклонения интервалов (Jitter).</summary>
        public double JitterPercent { get; set; }

        /// <summary>Тип аномалии (Beaconing, SuspiciousPort, NonStandardPort).</summary>
        public string AnomalyType { get; set; } = "Beaconing";

        /// <summary>Оценка риска инцидента от 0 до 100.</summary>
        public int RiskScore { get; set; }
    }

    /// <summary>Эвристические признаки инцидента.</summary>
    public sealed class HeuristicsTelemetry
    {
        /// <summary>Обнаружен ли периодический маяк C2 (Beaconing).</summary>
        public bool IsBeaconing { get; set; }

        /// <summary>Используется ли нестандартный порт для данного протокола.</summary>
        public bool IsNonStandardPort { get; set; }

        /// <summary>Расположен ли бинарный файл в общедоступной папке (Temp/AppData/Downloads).</summary>
        public bool IsUserWritablePath { get; set; }

        /// <summary>Предварительный скор риска (0-100).</summary>
        public int PreliminaryRiskScore { get; set; }
    }

    /// <summary>
    /// Создает экземпляр <see cref="IncidentTelemetryDto"/> на основе наблюдения об аномалии.
    /// </summary>
    /// <param name="observation">Наблюдение об аномалии трафика.</param>
    /// <param name="processPath">Полный путь к процессу.</param>
    /// <returns>Заполненный объект телеметрии инцидента.</returns>
    public static IncidentTelemetryDto FromObservation(TrafficAnomalyObservation observation, string? processPath = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return new IncidentTelemetryDto
        {
            IncidentId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            Process = new ProcessTelemetry
            {
                Pid = observation.ProcessId,
                StartTimeUtc = observation.DetectedAt,
                Name = observation.ProcessName,
                Path = processPath ?? string.Empty
            },
            Traffic = new TrafficTelemetry
            {
                RemoteAddress = observation.RemoteAddress,
                RemotePort = observation.RemotePort,
                Protocol = observation.Protocol,
                Direction = "Outbound",
                ObservationCount = observation.ObservationCount,
                AverageIntervalSeconds = observation.AverageIntervalSeconds,
                JitterPercent = observation.JitterPercent,
                AnomalyType = observation.AnomalyType,
                RiskScore = observation.RiskScore
            },
            Heuristics = new HeuristicsTelemetry
            {
                IsUserWritablePath = !string.IsNullOrWhiteSpace(processPath) &&
                    (processPath.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) ||
                     processPath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) ||
                     processPath.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase) ||
                     processPath.Contains(@"\Desktop\", StringComparison.OrdinalIgnoreCase)),
                IsBeaconing = string.Equals(observation.AnomalyType, "Beaconing", StringComparison.OrdinalIgnoreCase) || observation.JitterPercent <= 25.0,
                IsNonStandardPort = observation.RemotePort != 80 && observation.RemotePort != 443 && observation.RemotePort != 53,
                PreliminaryRiskScore = observation.RiskScore
            }
        };
    }
}
