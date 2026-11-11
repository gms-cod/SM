namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Наблюдаемое после принятия входящее TCP-соединение; не является решением брандмауэра.</summary>
/// <param name="ProcessId">Идентификатор процесса из события ETW.</param>
/// <param name="LocalAddress">Локальный адрес.</param>
/// <param name="LocalPort">Локальный порт.</param>
/// <param name="RemoteAddress">Удалённый адрес.</param>
/// <param name="RemotePort">Удалённый порт.</param>
/// <param name="ObservedAtUtc">Время наблюдения в UTC.</param>
public sealed record IncomingTcpObservation(int ProcessId, string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, DateTime ObservedAtUtc);
