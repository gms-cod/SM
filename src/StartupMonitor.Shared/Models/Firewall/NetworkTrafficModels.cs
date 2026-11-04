namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Запрос ограниченного числа бакетов измеренного трафика сетевых интерфейсов.</summary>
/// <param name="FromUtc">Начало диапазона UTC.</param>
/// <param name="ToUtc">Конец диапазона UTC, исключая правую границу.</param>
/// <param name="BucketCount">Число равномерных бакетов от 1 до 240.</param>
public sealed record NetworkTrafficHistoryRequest(DateTime FromUtc, DateTime ToUtc, int BucketCount = 60);

/// <summary>Средняя скорость реально наблюдавшихся сетевых интерфейсов; пропуск отличается от измеренного нуля.</summary>
/// <param name="Timestamp">Середина бакета UTC.</param>
/// <param name="InboundBps">Измеренная скорость приёма в байтах в секунду.</param>
/// <param name="OutboundBps">Измеренная скорость отправки в байтах в секунду.</param>
/// <param name="HasMeasurement">В бакет попало хотя бы одно действительное измерение.</param>
public sealed record NetworkTrafficHistoryPoint(DateTime Timestamp, double InboundBps, double OutboundBps, bool HasMeasurement);
