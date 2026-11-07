using System;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class ProcessNetworkTrackerTests
{
    [Fact]
    public void CaptureNetworkBandwidth_ReturnsValidResultWithoutExceptions()
    {
        using var tracker = new ProcessNetworkTracker();

        // Первый вызов инициализирует начальный срез сокетов
        var (map1, total1, sockets1) = tracker.CaptureNetworkBandwidth();
        Assert.NotNull(map1);
        Assert.True(total1 >= 0);
        Assert.NotNull(sockets1);

        // Второй вызов замеряет дельту
        var (map2, total2, sockets2) = tracker.CaptureNetworkBandwidth();
        Assert.NotNull(map2);
        Assert.True(total2 >= 0);
        Assert.NotNull(sockets2);
    }

    [Fact]
    public void ProcessInfo_HasNetworkActivity_EvaluatesCorrectly()
    {
        var info = new ProcessInfo();
        Assert.False(info.HasNetworkActivity);

        info.NetworkBytesPerSec = 1024;
        Assert.True(info.HasNetworkActivity);

        info.NetworkBytesPerSec = 0;
        Assert.False(info.HasNetworkActivity);

        info.ActiveSocketsCount = 2;
        Assert.True(info.HasNetworkActivity);

        info.ActiveSocketsCount = 0;
        Assert.False(info.HasNetworkActivity);

        info.HasNetworkActivity = true;
        Assert.True(info.HasNetworkActivity);
    }

    [Theory]
    [InlineData(0, "0 КБ/с")]
    [InlineData(-10, "0 КБ/с")]
    [InlineData(512, "0.5 КБ/с")]
    [InlineData(100, "0.1 КБ/с")]
    [InlineData(2048, "2 КБ/с")]
    [InlineData(1024 * 1024 * 5, "5.0 МБ/с")]
    public void ProcessInfo_DisplayNetwork_FormatsExpectedly(long bytesPerSec, string expected)
    {
        var info = new ProcessInfo
        {
            NetworkBytesPerSec = bytesPerSec
        };

        // Запятая или точка в зависимости от локали
        var display = info.DisplayNetwork.Replace(',', '.');
        Assert.Equal(expected, display);
    }

    [Theory]
    [InlineData(0, "0 КБ/с")]
    [InlineData(512, "0.5 КБ/с")]
    [InlineData(1024 * 512, "512 КБ/с")]
    [InlineData(1024 * 1024 * 10, "10.0 МБ/с")]
    public void SystemPerformanceSnapshot_DisplayTotalNetwork_FormatsExpectedly(long totalBytes, string expected)
    {
        var snapshot = new SystemPerformanceSnapshot
        {
            TotalNetworkBytesPerSec = totalBytes
        };

        var display = snapshot.DisplayTotalNetwork.Replace(',', '.');
        Assert.Equal(expected, display);
    }

    [Fact]
    public void GetSockets_ReturnsValidList()
    {
        using var tracker = new ProcessNetworkTracker();
        var sockets = tracker.GetSockets(-1);
        Assert.NotNull(sockets);
        // Отсутствие сокетов допустимо в изолированной системе; проверяем контракт фактически полученных строк.

        foreach (var s in sockets)
        {
            Assert.True(s.ProcessId >= 0);
            Assert.False(string.IsNullOrWhiteSpace(s.Protocol));
            Assert.False(string.IsNullOrWhiteSpace(s.LocalAddress));
            Assert.True(s.LocalPort >= 0);
            Assert.NotNull(s.RemoteAddress);
            Assert.False(string.IsNullOrWhiteSpace(s.State));
        }
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("169.254.10.20", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("204.79.197.200", false)]
    [InlineData("*", true)]
    [InlineData("", true)]
    public void ProcessNetworkSocket_CheckIsPrivateAddress_EvaluatesCorrectly(string ip, bool expectedPrivate)
    {
        var isPrivate = ProcessNetworkSocket.CheckIsPrivateAddress(ip);
        Assert.Equal(expectedPrivate, isPrivate);
    }

    [Fact]
    public void ProcessNetworkSocket_DisplayProperties_FormatCorrectly()
    {
        var s = new ProcessNetworkSocket
        {
            Protocol = "TCP",
            LocalAddress = "192.168.1.50",
            LocalPort = 55432,
            RemoteAddress = "142.250.180.206",
            RemotePort = 443,
            State = "ESTABLISHED",
            IsRemotePrivate = ProcessNetworkSocket.CheckIsPrivateAddress("142.250.180.206")
        };

        Assert.Equal("192.168.1.50:55432", s.LocalEndPoint);
        Assert.Equal("142.250.180.206:443", s.RemoteEndPoint);
        Assert.False(s.IsRemotePrivate);
    }

    /// <summary>Проверяет общий кэш для разных PID, изоляцию изменяемых DTO и обновление точно по монотонному времени.</summary>
    [Fact]
    public void GetSockets_ReusesSnapshotAcrossConsumersAndRefreshesAfterExpiry()
    {
        var clock = new ManualNetworkTimeProvider();
        var captures = 0;
        using var tracker = new ProcessNetworkTracker(() =>
        {
            captures++;
            return [new ProcessNetworkSocket { ProcessId = 123, LocalPort = 8080 },
                new ProcessNetworkSocket { ProcessId = 456, LocalPort = 9090 }];
        }, clock);
        var first = tracker.GetSockets(123);
        Assert.Equal(8080, Assert.Single(first).LocalPort);
        first[0].LocalPort = 1;
        first.Clear();
        Assert.Equal(9090, Assert.Single(tracker.GetSockets(456)).LocalPort);
        Assert.Equal(8080, Assert.Single(tracker.GetSockets(123)).LocalPort);
        Assert.Equal(1, captures);
        clock.Advance(ProcessNetworkTracker.SocketSnapshotLifetime - TimeSpan.FromTicks(1));
        Assert.Equal(2, tracker.GetSockets().Count);
        Assert.Equal(1, captures);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(2, tracker.GetSockets().Count);
        Assert.Equal(2, captures);
    }

    /// <summary>Проверяет отсутствие параллельного повторного сбора при одновременном запросе нескольких потребителей.</summary>
    [Fact]
    public void GetSockets_ConcurrentReadersCaptureSourceOnce()
    {
        var clock = new ManualNetworkTimeProvider();
        var captures = 0;
        using var tracker = new ProcessNetworkTracker(() =>
        {
            Interlocked.Increment(ref captures);
            return [new ProcessNetworkSocket { ProcessId = 123 }];
        }, clock);
        Parallel.For(0, 100, _ => Assert.Single(tracker.GetSockets()));
        Assert.Equal(1, captures);
    }

    /// <summary>Проверяет, что внешнее изменение исходного DTO не повреждает опубликованный кэш.</summary>
    [Fact]
    public void GetSockets_SourceMutationDoesNotChangeCachedSnapshot()
    {
        var source = new ProcessNetworkSocket { LocalPort = 8080 };
        using var tracker = new ProcessNetworkTracker(() => [source], new ManualNetworkTimeProvider());
        Assert.Equal(8080, Assert.Single(tracker.GetSockets()).LocalPort);
        source.LocalPort = 9090;
        Assert.Equal(8080, Assert.Single(tracker.GetSockets()).LocalPort);
    }

    /// <summary>Проверяет входные границы IPv6 до фиксированного чтения 128-битного адреса.</summary>
    /// <param name="length">Недопустимая длина адреса.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void SocketKey6_InvalidAddressLengthIsRejected(int length)
    {
        Assert.Throws<ArgumentException>(() => new ProcessNetworkTracker.SocketKey6(new byte[length], 0, 0, new byte[16], 0, 0));
        Assert.Throws<ArgumentException>(() => new ProcessNetworkTracker.SocketKey6(new byte[16], 0, 0, new byte[length], 0, 0));
    }

    /// <summary>Предоставляет управляемые монотонные ticks без ожиданий реального времени.</summary>
    private sealed class ManualNetworkTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;

        /// <summary>Продвигает тестовое время на заданный интервал.</summary>
        /// <param name="elapsed">Интервал до следующего запроса.</param>
        internal void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }
}

