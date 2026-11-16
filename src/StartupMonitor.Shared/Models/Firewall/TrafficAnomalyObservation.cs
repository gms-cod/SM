namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Признак регулярных наблюдаемых исходящих соединений; не является подтверждением вредоносности.</summary>
/// <param name="ProcessId">Идентификатор процесса.</param>
/// <param name="ProcessName">Имя процесса, если оно известно.</param>
/// <param name="RemoteAddress">Удалённый IP-адрес.</param>
/// <param name="RemotePort">Удалённый порт.</param>
/// <param name="ObservationCount">Число различных наблюдённых сокетов.</param>
/// <param name="AverageIntervalSeconds">Средний интервал между наблюдениями в секундах.</param>
public sealed record TrafficAnomalyObservation(int ProcessId, string ProcessName, string RemoteAddress, int RemotePort, int ObservationCount, double AverageIntervalSeconds);
