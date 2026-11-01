using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services;

/// <summary>Накопительные счётчики интерфейса; изменение набора интерфейсов начинает новый интервал наблюдения.</summary>
/// <param name="Id">Стабильный идентификатор интерфейса.</param>
/// <param name="ReceivedBytes">Накопленные принятые байты.</param>
/// <param name="SentBytes">Накопленные отправленные байты.</param>
public sealed record NetworkInterfaceTrafficCounter(string Id, long ReceivedBytes, long SentBytes);

/// <summary>Граница получения системных счётчиков, заменяемая управляемым источником в тестах.</summary>
public interface INetworkTrafficCounterSource
{
    /// <summary>Читает накопительные счётчики активных интерфейсов без сетевых запросов.</summary>
    /// <returns>Снимок системных счётчиков; пустой список означает отсутствие измерений.</returns>
    IReadOnlyList<NetworkInterfaceTrafficCounter> ReadCounters();
}

/// <summary>Читает локальные счётчики IP Helper через .NET; список адаптеров обновляется не чаще раза в минуту.</summary>
public sealed class NetworkTrafficCounterSource : INetworkTrafficCounterSource
{
    private NetworkInterface[] _interfaces = [];
    private DateTime _refreshAfterUtc;

    /// <summary>Получает фактические RX/TX; loopback и tunnel не включаются в суммарный интерфейсный трафик.</summary>
    /// <returns>Текущие счётчики доступных интерфейсов.</returns>
    public IReadOnlyList<NetworkInterfaceTrafficCounter> ReadCounters()
    {
        var now = DateTime.UtcNow;
        if (now >= _refreshAfterUtc)
        {
            _interfaces = NetworkInterface.GetAllNetworkInterfaces();
            _refreshAfterUtc = now.AddMinutes(1);
        }
        var result = new List<NetworkInterfaceTrafficCounter>(_interfaces.Length);
        foreach (var adapter in _interfaces)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var counters = adapter.GetIPStatistics();
                result.Add(new(adapter.Id, counters.BytesReceived, counters.BytesSent));
            }
            catch (NetworkInformationException ex)
            {
                // Исчезнувший адаптер исключается из снимка: изменение набора сбросит базовую линию.
                System.Diagnostics.Trace.TraceWarning("Счётчики интерфейса {0} недоступны: {1}", adapter.Id, ex.Message);
                _refreshAfterUtc = DateTime.MinValue;
            }
        }
        return result;
    }
}

/// <summary>
/// Снимает локальные RX/TX раз в пять секунд, агрегирует минутные бакеты за семь суток.
/// Не перебирает процессы и сокеты, не создаёт сетевых запросов; файл записывается не чаще раза в пять минут.
/// При сбросе счётчиков, смене адаптеров или длинной паузе отсутствующие данные не подменяются нулём.
/// </summary>
public sealed class NetworkTrafficHistoryService : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly object _sync = new();
    private readonly SortedDictionary<DateTime, MinuteTraffic> _minutes = new();
    private readonly INetworkTrafficCounterSource _source;
    private readonly ILogger<NetworkTrafficHistoryService> _logger;
    private readonly TimeProvider _time;
    private readonly string _path;
    private Dictionary<string, NetworkInterfaceTrafficCounter> _previous = new(StringComparer.Ordinal);
    private DateTime _previousUtc;
    private DateTime _lastPersistUtc;

    /// <summary>Создаёт ограниченное хранилище; некорректный файл истории не препятствует работе службы.</summary>
    /// <param name="store">Хранилище, определяющее каталог данных.</param>
    /// <param name="logger">Диагностика локального сбора и записи.</param>
    /// <param name="source">Источник счётчиков; null выбирает системный.</param>
    /// <param name="timeProvider">Часы для retention и управляемых тестов.</param>
    public NetworkTrafficHistoryService(JsonDataStore store, ILogger<NetworkTrafficHistoryService> logger,
        INetworkTrafficCounterSource? source = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _source = source ?? new NetworkTrafficCounterSource();
        _time = timeProvider ?? TimeProvider.System;
        _path = Path.Combine(store.BaseDirectory, "network-traffic-history.json");
        LoadHistory();
    }

    /// <summary>Минутное среднее по действительным наблюдениям с ограниченной точностью хранения.</summary>
    public sealed class MinuteTraffic
    {
        public DateTime MinuteUtc { get; set; }
        public DateTime ObservedFromUtc { get; set; }
        public DateTime ObservedToUtc { get; set; }
        public double ReceivedBytes { get; set; }
        public double SentBytes { get; set; }
        public double MeasuredSeconds { get; set; }
    }

    /// <summary>Единственный последовательный цикл исключает наложение замеров и конкурентную запись файла.</summary>
    /// <param name="stoppingToken">Отмена при остановке службы.</param>
    /// <returns>Задача жизненного цикла сборщика.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), _time);
        try
        {
            do
            {
                try
                {
                    RecordCounters(_time.GetUtcNow().UtcDateTime, _source.ReadCounters());
                    if (_time.GetUtcNow().UtcDateTime - _lastPersistUtc >= TimeSpan.FromMinutes(5))
                    {
                        _lastPersistUtc = _time.GetUtcNow().UtcDateTime;
                        await PersistAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is NetworkInformationException or IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Не удалось получить или сохранить локальные счётчики сетевого трафика");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Сбор сетевых счётчиков остановлен");
        }
        finally
        {
            using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await PersistAsync(saveTimeout.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                _logger.LogWarning(ex, "Не удалось сохранить последние бакеты сетевого трафика при остановке");
            }
        }
    }

    /// <summary>Преобразует накопительные счётчики в действительные интервалы; сброс и разрыв дают пропуск.</summary>
    /// <param name="timestampUtc">Время снимка UTC.</param>
    /// <param name="counters">Текущие системные счётчики.</param>
    internal void RecordCounters(DateTime timestampUtc, IReadOnlyList<NetworkInterfaceTrafficCounter> counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        var current = new Dictionary<string, NetworkInterfaceTrafficCounter>(StringComparer.Ordinal);
        foreach (var counter in counters)
        {
            if (counter is not null && !string.IsNullOrWhiteSpace(counter.Id) && counter.ReceivedBytes >= 0 && counter.SentBytes >= 0)
                current[counter.Id] = counter;
        }
        lock (_sync)
        {
            if (timestampUtc <= _previousUtc) return;
            var elapsed = (timestampUtc - _previousUtc).TotalSeconds;
            var valid = elapsed is > 0 and <= 30 && current.Count > 0 && current.Count == _previous.Count;
            double received = 0, sent = 0;
            foreach (var (id, value) in current)
            {
                if (!_previous.TryGetValue(id, out var previous) || value.ReceivedBytes < previous.ReceivedBytes || value.SentBytes < previous.SentBytes)
                {
                    valid = false;
                    break;
                }
                received += value.ReceivedBytes - previous.ReceivedBytes;
                sent += value.SentBytes - previous.SentBytes;
            }
            if (valid)
            {
                var start = _previousUtc;
                while (start < timestampUtc)
                {
                    var minute = new DateTime(start.Ticks - start.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
                    var end = timestampUtc < minute.AddMinutes(1) ? timestampUtc : minute.AddMinutes(1);
                    var seconds = (end - start).TotalSeconds;
                    if (!_minutes.TryGetValue(minute, out var bucket))
                        _minutes[minute] = bucket = new() { MinuteUtc = minute, ObservedFromUtc = start };
                    bucket.ObservedToUtc = end;
                    bucket.ReceivedBytes += received * seconds / elapsed;
                    bucket.SentBytes += sent * seconds / elapsed;
                    bucket.MeasuredSeconds += seconds;
                    start = end;
                }
            }
            _previous = current;
            _previousUtc = timestampUtc;
            PruneLocked(timestampUtc);
        }
    }

    /// <summary>Возвращает только реальные минутные средние; нет наблюдений — HasMeasurement=false.</summary>
    /// <param name="request">Ограниченный диапазон и число выходных бакетов.</param>
    /// <returns>Независимый снимок равномерных бакетов.</returns>
    public IReadOnlyList<NetworkTrafficHistoryPoint> GetHistory(NetworkTrafficHistoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BucketCount is < 1 or > 240 || request.ToUtc <= request.FromUtc || request.ToUtc - request.FromUtc > Retention)
            throw new ArgumentOutOfRangeException(nameof(request), "Диапазон должен быть положительным, не больше семи суток, число бакетов 1..240.");
        var received = new double[request.BucketCount];
        var sent = new double[request.BucketCount];
        var measured = new double[request.BucketCount];
        var bucketTicks = (request.ToUtc - request.FromUtc).Ticks / (double)request.BucketCount;
        lock (_sync)
        {
            foreach (var minute in _minutes.Values)
            {
                if (minute.ObservedToUtc <= request.FromUtc || minute.ObservedFromUtc >= request.ToUtc || minute.MeasuredSeconds <= 0)
                    continue;
                var first = Math.Clamp((int)((minute.ObservedFromUtc - request.FromUtc).Ticks / bucketTicks), 0, request.BucketCount - 1);
                var last = Math.Clamp((int)((minute.ObservedToUtc - request.FromUtc).Ticks / bucketTicks), 0, request.BucketCount - 1);
                for (var index = first; index <= last; index++)
                {
                    var from = request.FromUtc.AddTicks((long)(index * bucketTicks));
                    var to = index == request.BucketCount - 1 ? request.ToUtc : request.FromUtc.AddTicks((long)((index + 1) * bucketTicks));
                    var overlapStart = minute.ObservedFromUtc > from ? minute.ObservedFromUtc : from;
                    var overlapEnd = minute.ObservedToUtc < to ? minute.ObservedToUtc : to;
                    var seconds = (overlapEnd - overlapStart).TotalSeconds;
                    if (seconds <= 0) continue;
                    var weight = seconds / (minute.ObservedToUtc - minute.ObservedFromUtc).TotalSeconds;
                    received[index] += minute.ReceivedBytes * weight;
                    sent[index] += minute.SentBytes * weight;
                    measured[index] += minute.MeasuredSeconds * weight;
                }
            }
        }
        var result = new NetworkTrafficHistoryPoint[request.BucketCount];
        for (var index = 0; index < result.Length; index++)
        {
            var hasMeasurement = measured[index] > 0;
            result[index] = new(request.FromUtc.AddTicks((long)((index + 0.5) * bucketTicks)),
                hasMeasurement ? received[index] / measured[index] : 0,
                hasMeasurement ? sent[index] / measured[index] : 0, hasMeasurement);
        }
        return result;
    }

    /// <summary>Удаляет просроченные бакеты, ограничивая память примерно 10081 минутной записью.</summary>
    /// <param name="nowUtc">Время отсечения.</param>
    private void PruneLocked(DateTime nowUtc)
    {
        var cutoff = nowUtc - Retention;
        while (_minutes.Count > 0 && _minutes.First().Key < cutoff)
            _minutes.Remove(_minutes.First().Key);
    }

    /// <summary>Читает небольшой ограниченный файл и отбрасывает невалидную или просроченную историю.</summary>
    private void LoadHistory()
    {
        if (!File.Exists(_path)) return;
        try
        {
            if (new FileInfo(_path).Length > 4 * 1024 * 1024)
                throw new InvalidDataException("История трафика превышает допустимые 4 МБ.");
            var buckets = JsonSerializer.Deserialize<List<MinuteTraffic>>(File.ReadAllText(_path));
            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var bucket in buckets ?? [])
            {
                if (bucket is null || bucket.MinuteUtc < now - Retention || bucket.MinuteUtc > now ||
                    bucket.MinuteUtc.Ticks % TimeSpan.TicksPerMinute != 0 ||
                    bucket.ObservedFromUtc < bucket.MinuteUtc || bucket.ObservedToUtc > bucket.MinuteUtc.AddMinutes(1) ||
                    bucket.ObservedToUtc <= bucket.ObservedFromUtc || bucket.MeasuredSeconds is <= 0 or > 60 ||
                    !double.IsFinite(bucket.MeasuredSeconds) || !double.IsFinite(bucket.ReceivedBytes) || !double.IsFinite(bucket.SentBytes) ||
                    bucket.ReceivedBytes < 0 || bucket.SentBytes < 0)
                    continue;
                _minutes[bucket.MinuteUtc] = bucket;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _logger.LogWarning(ex, "История трафика недоступна или повреждена; новые измерения будут сохранены отдельно");
        }
    }

    /// <summary>Атомарно сохраняет ограниченный снимок; периодическая запись не блокирует читателей истории.</summary>
    /// <param name="ct">Отмена дисковой операции.</param>
    /// <returns>Задача завершения атомарной записи.</returns>
    internal async Task PersistAsync(CancellationToken ct)
    {
        byte[] bytes;
        lock (_sync)
        {
            PruneLocked(_time.GetUtcNow().UtcDateTime);
            bytes = JsonSerializer.SerializeToUtf8Bytes(_minutes.Values);
        }
        var temporary = _path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        File.Move(temporary, _path, overwrite: true);
        _lastPersistUtc = _time.GetUtcNow().UtcDateTime;
    }
}
