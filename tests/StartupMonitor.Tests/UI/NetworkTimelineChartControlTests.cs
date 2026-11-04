using System;
using System.Collections.Generic;
using System.Linq;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Controls;
using Xunit;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// Модульные тесты для контрола таймлайна сетевой телеметрии NetworkTimelineChartControl
/// и математического алгоритма адаптивного масштабирования (Dynamic AGC).
/// </summary>
[Collection("WpfNetworkRendering")]
public sealed class NetworkTimelineChartControlTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Использует общий STA-диспетчер и ресурсы WPF, исключая отдельные конкурирующие потоки холодной инициализации.</summary>
    /// <param name="host">Единственный WPF-хост тестовой коллекции.</param>
    public NetworkTimelineChartControlTests(NetworkRenderingTestHost host) => _host = host;

    [Fact]
    public void ChartDataPoint_Constructor_InitializesAllPropertiesCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        double inBps = 15420.5;
        double outBps = 8320.2;
        int blocked = 5;
        int alerts = 2;

        // Act
        var point = new NetworkTimelineChartControl.ChartDataPoint(now, inBps, outBps, blocked, alerts);

        // Assert
        Assert.Equal(now, point.Timestamp);
        Assert.Equal(inBps, point.InboundBps);
        Assert.Equal(outBps, point.OutboundBps);
        Assert.Equal(blocked, point.BlockedCount);
        Assert.Equal(alerts, point.AlertCount);
    }

    /// <summary>Проверяет пустые и настоящие данные графика на том же STA, которому принадлежат ресурсы приложения.</summary>
    [Fact]
    public void NetworkTimelineChartControl_SetDataPoints_HandlesNullAndEmptyGracefully()
        => _host.Run(() =>
        {
            var control = new NetworkTimelineChartControl();
            control.SetDataPoints(null);
            control.SetDataPoints(Enumerable.Empty<NetworkTimelineChartControl.ChartDataPoint>());
            control.SetDataPoints(new List<NetworkTimelineChartControl.ChartDataPoint>
            {
                new(DateTime.UtcNow.AddMinutes(-5), 1024, 512, 0, 0),
                new(DateTime.UtcNow, 2048, 1024, 1, 0)
            });
        });

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(24000.0, 24100.0)] // Малая вариация (высокое смещение DC)
    [InlineData(100.0, 1000000.0)] // Широкий динамический диапазон
    public void DynamicRangeCalculation_ExpandsSpanAdequately(double minVal, double maxVal)
    {
        // Проверяем рабочий алгоритм, а не его независимую копию в тесте.
        var (effectiveMin, effectiveMax) = NetworkTimelineChartControl.CalculateTrafficRange(minVal, maxVal);

        double span = effectiveMax - effectiveMin;

        // Assert
        Assert.True(span >= 10.0, "Динамический диапазон графика должен быть не менее 10 Б/с для видимой кривой.");
        Assert.True(effectiveMax > effectiveMin, "Верхняя граница AGC обязана строго превышать нижнюю.");
        Assert.True(effectiveMin >= 0.0, "Нижняя граница диапазона не может быть отрицательной.");
    }
}
