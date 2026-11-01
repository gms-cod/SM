using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет реальные дельты, пропуски, retention и запись истории без обращения к сети.</summary>
public sealed class NetworkTrafficHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StartupMonitorTrafficTests_" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));

    /// <summary>Создаёт сервис с управляемыми часами в изолированном каталоге.</summary>
    /// <returns>Не запущенный сборщик для детерминированной подачи счётчиков.</returns>
    private NetworkTrafficHistoryService CreateService() => new(new JsonDataStore(_directory),
        NullLogger<NetworkTrafficHistoryService>.Instance, timeProvider: _clock);

    /// <summary>Пустая история не подменяется фиктивным измеренным нулём.</summary>
    [Fact]
    public void MissingSamples_ReturnUnmeasuredBuckets()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        var points = service.GetHistory(new(now.AddHours(-1), now, 60));
        Assert.Equal(60, points.Count);
        Assert.All(points, point => Assert.False(point.HasMeasurement));
    }

    /// <summary>Средние RX/TX вычисляются из настоящих раздельных накопительных счётчиков.</summary>
    [Fact]
    public void CounterDeltas_ProduceIndependentReceiveAndSendRates()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 1000, 2000)]);
        service.RecordCounters(now.AddSeconds(5), [new("adapter", 1500, 2100)]);
        var point = Assert.Single(service.GetHistory(new(now, now.AddMinutes(1), 1)));
        Assert.True(point.HasMeasurement);
        Assert.Equal(100, point.InboundBps, 8);
        Assert.Equal(20, point.OutboundBps, 8);
    }

    /// <summary>Неизменившиеся счётчики дают измеренный ноль, отличный от отсутствия данных.</summary>
    [Fact]
    public void UnchangedCounters_AreMeasuredZero()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 1000, 2000)]);
        service.RecordCounters(now.AddSeconds(5), [new("adapter", 1000, 2000)]);
        var point = Assert.Single(service.GetHistory(new(now, now.AddMinutes(1), 1)));
        Assert.True(point.HasMeasurement);
        Assert.Equal(0, point.InboundBps);
        Assert.Equal(0, point.OutboundBps);
    }

    /// <summary>Сброс счётчика не порождает отрицательную скорость и начинает новый интервал.</summary>
    [Fact]
    public void CounterReset_StartsNewBaseline()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 1000, 2000)]);
        service.RecordCounters(now.AddSeconds(5), [new("adapter", 10, 20)]);
        Assert.False(Assert.Single(service.GetHistory(new(now, now.AddMinutes(1), 1))).HasMeasurement);
        service.RecordCounters(now.AddSeconds(10), [new("adapter", 60, 120)]);
        var point = Assert.Single(service.GetHistory(new(now, now.AddMinutes(1), 1)));
        Assert.Equal(10, point.InboundBps, 8);
        Assert.Equal(20, point.OutboundBps, 8);
    }

    /// <summary>Смена интерфейса и длинная пауза не заполняют пропуск вымышленной скоростью.</summary>
    [Fact]
    public void InterfaceChangeAndLongGap_RemainUnmeasured()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("old", 1000, 2000)]);
        service.RecordCounters(now.AddSeconds(5), [new("new", 2000, 4000)]);
        service.RecordCounters(now.AddMinutes(2), [new("new", 3000, 5000)]);
        Assert.All(service.GetHistory(new(now, now.AddMinutes(3), 3)), point => Assert.False(point.HasMeasurement));
    }

    /// <summary>Переход через минутную границу сохраняет оба интервала и общую скорость.</summary>
    [Fact]
    public void MinuteBoundary_SplitsMeasuredIntervalWithoutDuplicatingBytes()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime.AddSeconds(58);
        service.RecordCounters(now, [new("adapter", 1000, 2000)]);
        service.RecordCounters(now.AddSeconds(5), [new("adapter", 1500, 2100)]);
        var points = service.GetHistory(new(now.AddSeconds(-58), now.AddSeconds(62), 2));
        Assert.All(points, point => { Assert.True(point.HasMeasurement); Assert.Equal(100, point.InboundBps, 8); });
    }

    /// <summary>Откат часов не создаёт повторного учёта уже измеренного времени.</summary>
    [Fact]
    public void ClockMovesBackwards_DoesNotDuplicateIntervals()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 1000, 1000)]);
        service.RecordCounters(now.AddSeconds(5), [new("adapter", 1500, 1500)]);
        service.RecordCounters(now.AddSeconds(2), [new("adapter", 1200, 1200)]);
        service.RecordCounters(now.AddSeconds(10), [new("adapter", 2000, 2000)]);
        Assert.Equal(100, Assert.Single(service.GetHistory(new(now, now.AddMinutes(1), 1))).InboundBps, 8);
    }

    /// <summary>Минутная история переживает перезапуск, сохраняя признак измерения.</summary>
    [Fact]
    public async Task PersistAndReload_RoundTripMeasuredBuckets()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        using (var service = CreateService())
        {
            service.RecordCounters(now, [new("adapter", 1000, 2000)]);
            service.RecordCounters(now.AddSeconds(5), [new("adapter", 1500, 2100)]);
            _clock.Advance(TimeSpan.FromSeconds(5));
            await service.PersistAsync(CancellationToken.None);
        }
        using var reloaded = CreateService();
        var point = Assert.Single(reloaded.GetHistory(new(now, now.AddMinutes(1), 1)));
        Assert.True(point.HasMeasurement);
        Assert.Equal(100, point.InboundBps, 8);
    }

    /// <summary>Retention удаляет просроченные измерения и с диска, и из памяти.</summary>
    [Fact]
    public async Task ExpiredBuckets_AreRemovedBeforePersistence()
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        service.RecordCounters(now, [new("adapter", 1000, 2000)]);
        service.RecordCounters(now.AddSeconds(5), [new("adapter", 1500, 2100)]);
        _clock.Advance(TimeSpan.FromDays(8));
        await service.PersistAsync(CancellationToken.None);
        Assert.Equal("[]", await File.ReadAllTextAsync(Path.Combine(_directory, "network-traffic-history.json")));
    }

    /// <summary>Отсутствие и повреждение файла не препятствуют запуску сборщика.</summary>
    [Fact]
    public void MalformedHistory_IsIgnoredWithoutBreakingNewMeasurements()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "network-traffic-history.json"), "[invalid");
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        Assert.False(Assert.Single(service.GetHistory(new(now.AddMinutes(-1), now, 1))).HasMeasurement);
    }

    /// <summary>Ограничение запроса предотвращает чрезмерное выделение памяти на IPC-границе.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    [InlineData(int.MaxValue)]
    public void InvalidBucketCount_IsRejected(int count)
    {
        using var service = CreateService();
        var now = _clock.GetUtcNow().UtcDateTime;
        Assert.Throws<ArgumentOutOfRangeException>(() => service.GetHistory(new(now.AddMinutes(-1), now, count)));
    }

    /// <summary>Повторное освобождение worker метаданных не отменяет уже уничтоженный CTS.</summary>
    [Fact]
    public async Task MetadataWorker_DisposeIsIdempotent()
    {
        var worker = new ProcessMetadataEnricher();
        await worker.DisposeAsync();
        worker.Dispose();
        await worker.DisposeAsync();
    }

    /// <summary>Удаляет только собственный каталог после завершения использующих его сервисов.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Управляемые часы исключают ожидания реального времени из тестов retention.</summary>
    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        /// <summary>Возвращает установленное тестом время.</summary>
        /// <returns>Текущий тестовый момент UTC.</returns>
        public override DateTimeOffset GetUtcNow() => now;

        /// <summary>Сдвигает время без блокировки тестового потока.</summary>
        /// <param name="delta">Величина сдвига.</param>
        public void Advance(TimeSpan delta) => now += delta;
    }
}
