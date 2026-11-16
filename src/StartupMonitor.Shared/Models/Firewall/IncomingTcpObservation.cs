namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Observed accepted inbound TCP connection; not an active firewall decision.</summary>
/// <param name="ProcessId">Process identifier from ETW event.</param>
/// <param name="LocalAddress">Local IP address.</param>
/// <param name="LocalPort">Local port number.</param>
/// <param name="RemoteAddress">Remote IP address.</param>
/// <param name="RemotePort">Remote port number.</param>
/// <param name="ObservedAtUtc">Observation timestamp in UTC.</param>
public sealed record IncomingTcpObservation(int ProcessId, string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, DateTime ObservedAtUtc);
