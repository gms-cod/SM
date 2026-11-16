namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Признак регулярных наблюдаемых исходящих соединений; не является подтверждением вредоносности.</summary>
/// <param name="ProcessId">Идентификатор процесса.</param>
/// <param name="ProcessName">Имя процесса, если оно известно.</param>
/// <param name="RemoteAddress">Удалённый IP-адрес.</param>
/// <param name="RemotePort">Удалённый порт.</param>
/// <param name="ObservationCount">Число различных наблюдённых сокетов.</param>
/// <param name="AverageIntervalSeconds">Средний интервал между наблюдениями в секундах.</param>
/// <param name="JitterPercent">Процент относительного отклонения интервалов (MAD / Median * 100).</param>
/// <param name="AnomalyType">Категория обнаруженной аномалии (Beaconing, SuspiciousPort, NonStandardPort).</param>
/// <param name="RiskScore">Оценка риска инцидента от 0 до 100.</param>
/// <param name="Protocol">Сетевой протокол соединения (TCP, UDP).</param>
/// <param name="DetectedAt">Временная метка обнаружения аномалии (UTC).</param>
public sealed record TrafficAnomalyObservation(
    int ProcessId,
    string ProcessName,
    string RemoteAddress,
    int RemotePort,
    int ObservationCount,
    double AverageIntervalSeconds,
    double JitterPercent = 0.0,
    string AnomalyType = "Beaconing",
    int RiskScore = 80,
    string Protocol = "TCP",
    DateTime DetectedAt = default);
