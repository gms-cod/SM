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
/// Снимает локальные RX/TX раз в пять секунд, хранит минутные средние и точное покрытие до семи суток и 4 МБ.
/// Не перебирает процессы и сокеты, не создаёт сетевых запросов; файл записывается не чаще раза в пять минут.
/// При сбросе счётчиков, смене адаптеров или длинной паузе отсутствующие данные не подменяются нулём.
/// </summary>
public sealed class NetworkTrafficHistoryService : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    /// <summary>Лимит одинаков для загрузки и записи: история приложения всегда остаётся читаемой после перезапуска.</summary>
    internal const int MaximumHistoryBytes = 4 * 1024 * 1024;
    /// <summary>Не более 32 раздельных наблюдений в минуте; штатный пятисекундный опрос требует не более 13.</summary>
    private const int MaximumCoverageIntervals = 32;
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
        /// <summary>Версия 2 сохраняет точные границы покрытия; ноль обозначает старый минутный формат.</summary>
        public int FormatVersion { get; set; }
        /// <summary>Пары смещений начала и конца действительно измеренных интервалов в тиках от начала минуты.</summary>
        public List<long> CoverageTicks { get; set; } = [];
        /// <summary>Начало минутного бакета UTC.</summary>
        public DateTime MinuteUtc { get; set; }
        /// <summary>Первая граница действительного наблюдения UTC.</summary>
        public DateTime ObservedFromUtc { get; set; }
        /// <summary>Последняя граница действительного наблюдения UTC.</summary>
        public DateTime ObservedToUtc { get; set; }
        /// <summary>Байты приёма только за сохранённые интервалы покрытия.</summary>
        public double ReceivedBytes { get; set; }
        /// <summary>Байты отправки только за сохранённые интервалы покрытия.</summary>
        public double SentBytes { get; set; }
        /// <summary>Суммарная длительность действительных наблюдений, исключая разрывы.</summary>
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
                    if (!_minutes.TryGetValue(minute, out var bucket))
                        _minutes[minute] = bucket = new() { FormatVersion = 2, MinuteUtc = minute, ObservedFromUtc = start };
                    // После перезапуска или отката часов уже сохранённое время не учитывается повторно.
                    var observedStart = start < bucket.ObservedToUtc ? bucket.ObservedToUtc : start;
                    if (observedStart < end && AddCoverage(bucket, observedStart, end))
                    {
                        var seconds = (end - observedStart).TotalSeconds;
                        bucket.ObservedToUtc = end;
                        bucket.ReceivedBytes += received * seconds / elapsed;
                        bucket.SentBytes += sent * seconds / elapsed;
                        bucket.MeasuredSeconds += seconds;
                    }
                    start = end;
                }
            }
            _previous = current;
            _previousUtc = timestampUtc;
            PruneLocked(timestampUtc);
        }
    }

    /// <summary>Добавляет фактическое покрытие; соседние интервалы сливаются, разрывы сохраняются явно.</summary>
    /// <param name="bucket">Минутный бакет с упорядоченными непересекающимися интервалами.</param>
    /// <param name="fromUtc">Начало нового наблюдения UTC.</param>
    /// <param name="toUtc">Конец нового наблюдения UTC.</param>
    /// <returns>False при исчерпании лимита раздельных интервалов: такой замер не объявляется сохранённым.</returns>
    private static bool AddCoverage(MinuteTraffic bucket, DateTime fromUtc, DateTime toUtc)
    {
        var from = (fromUtc - bucket.MinuteUtc).Ticks;
        var to = (toUtc - bucket.MinuteUtc).Ticks;
        if (bucket.CoverageTicks.Count > 0 && bucket.CoverageTicks[^1] == from)
            bucket.CoverageTicks[^1] = to;
        else
        {
            if (bucket.CoverageTicks.Count >= MaximumCoverageIntervals * 2) return false;
            bucket.CoverageTicks.Add(from);
            bucket.CoverageTicks.Add(to);
        }
        return true;
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
                // Минутное среднее применяется только к реальным интервалам; паузы внутри минуты остаются пропусками.
                for (var coverageIndex = 0; coverageIndex < minute.CoverageTicks.Count; coverageIndex += 2)
                {
                    var observedFrom = minute.MinuteUtc.AddTicks(minute.CoverageTicks[coverageIndex]);
                    var observedTo = minute.MinuteUtc.AddTicks(minute.CoverageTicks[coverageIndex + 1]);
                    if (observedTo <= request.FromUtc || observedFrom >= request.ToUtc) continue;
                    // Обрезаем до запроса перед переводом в индекс: микроскопический диапазон не переполняет int.
                    var clippedFrom = observedFrom > request.FromUtc ? observedFrom : request.FromUtc;
                    var clippedTo = observedTo < request.ToUtc ? observedTo : request.ToUtc;
                    var first = Math.Clamp((int)((clippedFrom - request.FromUtc).Ticks / bucketTicks), 0, request.BucketCount - 1);
                    var last = Math.Clamp((int)((clippedTo - request.FromUtc).Ticks / bucketTicks), 0, request.BucketCount - 1);
                    for (var index = first; index <= last; index++)
                    {
                        var from = request.FromUtc.AddTicks((long)(index * bucketTicks));
                        var to = index == request.BucketCount - 1 ? request.ToUtc : request.FromUtc.AddTicks((long)((index + 1) * bucketTicks));
                        var overlapStart = observedFrom > from ? observedFrom : from;
                        var overlapEnd = observedTo < to ? observedTo : to;
                        var seconds = (overlapEnd - overlapStart).TotalSeconds;
                        if (seconds <= 0) continue;
                        received[index] += minute.ReceivedBytes * seconds / minute.MeasuredSeconds;
                        sent[index] += minute.SentBytes * seconds / minute.MeasuredSeconds;
                        measured[index] += seconds;
                    }
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
            if (new FileInfo(_path).Length > MaximumHistoryBytes)
                throw new InvalidDataException("История трафика превышает допустимые 4 МБ.");
            var buckets = JsonSerializer.Deserialize<List<MinuteTraffic>>(File.ReadAllText(_path));
            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var bucket in buckets ?? [])
            {
                if (bucket is null || bucket.MinuteUtc < now - Retention || bucket.MinuteUtc > now ||
                    bucket.MinuteUtc.Ticks % TimeSpan.TicksPerMinute != 0 ||
                    bucket.ObservedFromUtc < bucket.MinuteUtc || bucket.ObservedToUtc > bucket.MinuteUtc.AddMinutes(1) ||
                    bucket.ObservedToUtc > now || bucket.ObservedToUtc <= bucket.ObservedFromUtc || bucket.MeasuredSeconds is <= 0 or > 60 ||
                    !double.IsFinite(bucket.MeasuredSeconds) || !double.IsFinite(bucket.ReceivedBytes) || !double.IsFinite(bucket.SentBytes) ||
                    bucket.ReceivedBytes < 0 || bucket.SentBytes < 0)
                    continue;
                if (!ValidateCoverage(bucket)) continue;
                _minutes[bucket.MinuteUtc] = bucket;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _logger.LogWarning(ex, "История трафика недоступна или повреждена; новые измерения будут сохранены отдельно");
        }
    }

    /// <summary>Проверяет покрытие версии 2 либо безопасно мигрирует непрерывный старый бакет.</summary>
    /// <param name="bucket">Загруженные границы, длительность и интервалы наблюдения.</param>
    /// <returns>True только когда положение всех сохранённых наблюдений известно; старые бакеты с разрывами отбрасываются.</returns>
    private static bool ValidateCoverage(MinuteTraffic bucket)
    {
        if (bucket.FormatVersion == 0)
        {
            // Старый формат не хранит положение пауз: восстанавливать их вымышленными замерами нельзя.
            if (Math.Abs((bucket.ObservedToUtc - bucket.ObservedFromUtc).TotalSeconds - bucket.MeasuredSeconds) > 0.000001)
                return false;
            bucket.CoverageTicks = [(bucket.ObservedFromUtc - bucket.MinuteUtc).Ticks, (bucket.ObservedToUtc - bucket.MinuteUtc).Ticks];
            bucket.FormatVersion = 2;
        }
        if (bucket.FormatVersion != 2 || bucket.CoverageTicks is null || bucket.CoverageTicks.Count == 0 ||
            bucket.CoverageTicks.Count % 2 != 0 || bucket.CoverageTicks.Count > MaximumCoverageIntervals * 2)
            return false;
        long measuredTicks = 0, previousEnd = -1;
        for (var index = 0; index < bucket.CoverageTicks.Count; index += 2)
        {
            var from = bucket.CoverageTicks[index];
            var to = bucket.CoverageTicks[index + 1];
            if (from < 0 || from < previousEnd || to <= from || to > TimeSpan.TicksPerMinute) return false;
            measuredTicks += to - from;
            previousEnd = to;
        }
        return bucket.MinuteUtc.AddTicks(bucket.CoverageTicks[0]) == bucket.ObservedFromUtc &&
            bucket.MinuteUtc.AddTicks(bucket.CoverageTicks[^1]) == bucket.ObservedToUtc &&
            Math.Abs(measuredTicks / (double)TimeSpan.TicksPerSecond - bucket.MeasuredSeconds) <= 0.000001;
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
            // При фрагментированном покрытии срок хранения ограничен также размером файла, сохраняя самые свежие минуты.
            while (bytes.Length > MaximumHistoryBytes && _minutes.Count > 0)
            {
                var removeCount = Math.Max(1, _minutes.Count - (int)((long)_minutes.Count * MaximumHistoryBytes / bytes.Length) + 1);
                foreach (var key in _minutes.Keys.Take(removeCount).ToArray()) _minutes.Remove(key);
                bytes = JsonSerializer.SerializeToUtf8Bytes(_minutes.Values);
            }
        }
        var temporary = _path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        File.Move(temporary, _path, overwrite: true);
        _lastPersistUtc = _time.GetUtcNow().UtcDateTime;
    }
}
