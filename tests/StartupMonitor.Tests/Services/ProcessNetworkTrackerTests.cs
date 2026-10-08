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
        var tracker = new ProcessNetworkTracker();

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
        var tracker = new ProcessNetworkTracker();
        var sockets = tracker.GetSockets(-1);
        Assert.NotNull(sockets);
        // В живой Windows системе всегда есть активные сокеты (TCP/UDP)
        Assert.NotEmpty(sockets);

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
}

