namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Request for bounded traffic measurement buckets across network interfaces.</summary>
/// <param name="FromUtc">Start of UTC range.</param>
/// <param name="ToUtc">End of UTC range, exclusive.</param>
/// <param name="BucketCount">Uniform bucket count from 1 to 240.</param>
public sealed record NetworkTrafficHistoryRequest(DateTime FromUtc, DateTime ToUtc, int BucketCount = 60);

/// <summary>Средняя скорость реально наблюдавшихся сетевых интерфейсов; пропуск отличается от измеренного нуля.</summary>
/// <param name="Timestamp">Midpoint timestamp in UTC.</param>
/// <param name="InboundBps">Measured inbound transfer rate in bytes per second.</param>
/// <param name="OutboundBps">Measured outbound transfer rate in bytes per second.</param>
/// <param name="HasMeasurement">Indicates if bucket contains at least one valid measurement.</param>
public sealed record NetworkTrafficHistoryPoint(DateTime Timestamp, double InboundBps, double OutboundBps, bool HasMeasurement);
