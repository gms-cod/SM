using System;
using System.Net;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты для поведенческого анализатора сетевого трафика <see cref="TrafficBehavioralScorer"/>.
/// Проверяет математику выявления маяков (C2 Beaconing), расчёт джиттера (MAD),
/// изоляцию процессов по времени старта (PID reuse) и фильтрацию белых списков.
/// </summary>
public sealed class TrafficBehavioralScorerTests
{
    private static ProcessNetworkSocket CreateSocket(
        int pid = 1234,
        string processName = "malware.exe",
        string localAddr = "192.168.1.50",
        int localPort = 50000,
        string remoteAddr = "185.220.101.5",
        int remotePort = 4444,
        string direction = "Outbound",
        string protocol = "TCP")
    {
        return new ProcessNetworkSocket
        {
            ProcessId = pid,
            ProcessName = processName,
            LocalAddress = localAddr,
            LocalPort = localPort,
            RemoteAddress = remoteAddr,
            RemotePort = remotePort,
            Direction = direction,
            Protocol = protocol
        };
    }

    [Fact]
    public void Observe_Throws_WhenSocketIsNull()
    {
        var scorer = new TrafficBehavioralScorer();
        Assert.Throws<ArgumentNullException>(() => scorer.Observe(null!, 100L, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(0, 100L)]
    [InlineData(-1, 100L)]
    [InlineData(1234, null)]
    [InlineData(1234, 0L)]
    [InlineData(1234, -5L)]
    public void Observe_ReturnsNull_WhenProcessIdOrStartTimeInvalid(int pid, long? startTime)
    {
        var scorer = new TrafficBehavioralScorer();
        var socket = CreateSocket(pid: pid);
        var result = scorer.Observe(socket, startTime, DateTime.UtcNow);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Inbound", "TCP")]
    [InlineData("Unknown", "TCP")]
    [InlineData("Outbound", "UDP")]
    [InlineData("Outbound", "RAW")]
    public void Observe_ReturnsNull_WhenDirectionIsNotOutboundOrProtocolNotTcp(string direction, string protocol)
    {
        var scorer = new TrafficBehavioralScorer();
        var socket = CreateSocket(direction: direction, protocol: protocol);
        var result = scorer.Observe(socket, 100L, DateTime.UtcNow);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("127.0.0.1", 443)]
    [InlineData("192.168.1.1", 443)]
    [InlineData("10.0.0.5", 443)]
    [InlineData("172.16.0.10", 443)]
    [InlineData("invalid_ip", 443)]
    [InlineData("185.220.101.5", 0)]
    [InlineData("185.220.101.5", 70000)]
    public void Observe_ReturnsNull_WhenAddressOrPortIsInvalidOrInternal(string remoteAddr, int remotePort)
    {
        var scorer = new TrafficBehavioralScorer();
        var socket = CreateSocket(remoteAddr: remoteAddr, remotePort: remotePort);
        var result = scorer.Observe(socket, 100L, DateTime.UtcNow);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("chrome.exe", 443)]
    [InlineData("chrome.exe", 80)]
    [InlineData("msedge.exe", 443)]
    [InlineData("firefox.exe", 443)]
    [InlineData("svchost.exe", 443)]
    [InlineData("w32time.exe", 123)]
    [InlineData("anyapp.exe", 123)]
    public void Observe_ReturnsNull_WhenProcessOrPortIsAllowlisted(string processName, int port)
    {
        var scorer = new TrafficBehavioralScorer();
        var socket = CreateSocket(processName: processName, remotePort: port);
        var result = scorer.Observe(socket, 100L, DateTime.UtcNow);
        Assert.Null(result);
    }

    [Fact]
    public void Observe_ReturnsNull_WhenFewerThanRequiredObservations()
    {
        var scorer = new TrafficBehavioralScorer();
        var baseTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        const long startTime = 55555L;

        // Подаём 4 наблюдения (требуется 5) с идеальным интервалом 60 сек
        for (int i = 0; i < 4; i++)
        {
            var socket = CreateSocket(localPort: 50000 + i);
            var result = scorer.Observe(socket, startTime, baseTime.AddSeconds(i * 60));
            Assert.Null(result);
        }
    }

    [Fact]
    public void Observe_DetectsBeaconing_WhenIntervalsAreStrictlyRegular()
    {
        var scorer = new TrafficBehavioralScorer();
        var baseTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        const long startTime = 55555L;
        TrafficAnomalyObservation? observation = null;

        // 5 наблюдений с интервалом ровно 60 секунд (Jitter = 0%)
        for (int i = 0; i < 5; i++)
        {
            var socket = CreateSocket(localPort: 50000 + i, remotePort: 4444);
            observation = scorer.Observe(socket, startTime, baseTime.AddSeconds(i * 60));
        }

        Assert.NotNull(observation);
        Assert.Equal(1234, observation.ProcessId);
        Assert.Equal("malware.exe", observation.ProcessName);
        Assert.Equal(4444, observation.RemotePort);
        Assert.Equal(5, observation.ObservationCount);
        Assert.Equal(60.0, observation.AverageIntervalSeconds);
        Assert.Equal(0.0, observation.JitterPercent);
        Assert.Equal("SuspiciousPort", observation.AnomalyType);
        Assert.Equal(90, observation.RiskScore);
    }

    [Fact]
    public void Observe_DetectsBeaconing_OnStandardNonAllowlistedPort_WithJitterCalculation()
    {
        var scorer = new TrafficBehavioralScorer();
        var baseTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        const long startTime = 88888L;
        TrafficAnomalyObservation? observation = null;

        // Интервалы между 5 наблюдениями: 60с, 62с, 58с, 60с (медиана 60, разброс минимальный)
        int[] secondOffsets = [0, 60, 122, 180, 240];

        for (int i = 0; i < 5; i++)
        {
            var socket = CreateSocket(processName: "backdoor.exe", localPort: 51000 + i, remotePort: 8080);
            observation = scorer.Observe(socket, startTime, baseTime.AddSeconds(secondOffsets[i]));
        }

        Assert.NotNull(observation);
        Assert.Equal("backdoor.exe", observation.ProcessName);
        Assert.Equal("Beaconing", observation.AnomalyType);
        Assert.True(observation.JitterPercent < 10.0);
        Assert.True(observation.RiskScore >= 80 && observation.RiskScore <= 95);
    }

    [Fact]
    public void Observe_ReturnsNull_WhenJitterExceedsThreshold()
    {
        var scorer = new TrafficBehavioralScorer();
        var baseTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        const long startTime = 77777L;

        // Хаотичные интервалы: 10с, 200с, 45с, 300с (высокий разброс, нормальный серфинг)
        int[] secondOffsets = [0, 10, 210, 255, 555];

        TrafficAnomalyObservation? observation = null;
        for (int i = 0; i < 5; i++)
        {
            var socket = CreateSocket(localPort: 52000 + i, remotePort: 8080);
            observation = scorer.Observe(socket, startTime, baseTime.AddSeconds(secondOffsets[i]));
        }

        Assert.Null(observation);
    }

    [Fact]
    public void Observe_IsolatesDifferentStartTimes_WhenPidIsReused()
    {
        var scorer = new TrafficBehavioralScorer();
        var baseTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        // Одинаковый PID 1234, но разные startTime (PID был перевыдан ОС новому процессу)
        const long startTime1 = 11111L;
        const long startTime2 = 22222L;

        // 3 события у процесса 1
        for (int i = 0; i < 3; i++)
        {
            var socket = CreateSocket(localPort: 53000 + i);
            Assert.Null(scorer.Observe(socket, startTime1, baseTime.AddSeconds(i * 60)));
        }

        // 3 события у процесса 2 с тем же PID
        for (int i = 0; i < 3; i++)
        {
            var socket = CreateSocket(localPort: 54000 + i);
            Assert.Null(scorer.Observe(socket, startTime2, baseTime.AddSeconds((i + 3) * 60)));
        }
        // Ни у одного из процессов нет 5 наблюдений, поэтому алерта быть не должно
    }

    [Fact]
    public void Observe_RespectsCooldown_AfterAlertGenerated()
    {
        var scorer = new TrafficBehavioralScorer();
        var baseTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        const long startTime = 99999L;

        // Первые 5 наблюдений -> фиксация алерта
        for (int i = 0; i < 5; i++)
        {
            var socket = CreateSocket(localPort: 55000 + i);
            var res = scorer.Observe(socket, startTime, baseTime.AddSeconds(i * 60));
            if (i == 4) Assert.NotNull(res);
        }

        // 6-е наблюдение через 60 секунд (в пределах 30-минутного Cooldown) -> должно вернуть null
        var nextSocket = CreateSocket(localPort: 55005);
        var cooledResult = scorer.Observe(nextSocket, startTime, baseTime.AddSeconds(300));
        Assert.Null(cooledResult);

        // 7-е наблюдение спустя 31 минуту (после завершения Cooldown) -> снова срабатывает
        var afterCooldownSocket = CreateSocket(localPort: 55006);
        var afterCooldownResult = scorer.Observe(afterCooldownSocket, startTime, baseTime.AddSeconds(300 + 1900));
        Assert.NotNull(afterCooldownResult);
    }

    [Fact]
    public void Clear_ResetsAllGroups_AndPreventsAlertUntilReseeded()
    {
        var scorer = new TrafficBehavioralScorer();
        var baseTime = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        const long startTime = 33333L;

        // 4 наблюдения
        for (int i = 0; i < 4; i++)
        {
            var socket = CreateSocket(localPort: 56000 + i);
            scorer.Observe(socket, startTime, baseTime.AddSeconds(i * 60));
        }

        // Очищаем кэш
        scorer.Clear();

        // 5-е наблюдение после Clear не создаёт алерт, так как счётчик обнулился
        var socket5 = CreateSocket(localPort: 56004);
        var result = scorer.Observe(socket5, startTime, baseTime.AddSeconds(240));
        Assert.Null(result);
    }
}
