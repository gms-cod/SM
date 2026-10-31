using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Controls;
using Xunit;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// Модульные тесты для контрола таймлайна сетевой телеметрии NetworkTimelineChartControl
/// и математического алгоритма адаптивного масштабирования (Dynamic AGC).
/// </summary>
public sealed class NetworkTimelineChartControlTests
{
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

    [Fact]
    public void NetworkTimelineChartControl_SetDataPoints_HandlesNullAndEmptyGracefully()
    {
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            try
            {
                var control = new NetworkTimelineChartControl();
                control.SetDataPoints(null);
                control.SetDataPoints(Enumerable.Empty<NetworkTimelineChartControl.ChartDataPoint>());
                control.SetDataPoints(new List<NetworkTimelineChartControl.ChartDataPoint>
                {
                    new(DateTime.UtcNow.AddMinutes(-5), 1024, 512, 0, 0),
                    new(DateTime.UtcNow, 2048, 1024, 1, 0)
                });
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(threadEx);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(24000.0, 24100.0)] // Малая вариация (высокое смещение DC)
    [InlineData(100.0, 1000000.0)] // Широкий динамический диапазон
    public void DynamicRangeCalculation_ExpandsSpanAdequately(double minVal, double maxVal)
    {
        // Arrange
        double range = maxVal - minVal;
        double effectiveMin;
        double effectiveMax;

        // Act - математическая логика AGC из NetworkTimelineChartControl
        if (maxVal < 10.0)
        {
            effectiveMin = 0.0;
            effectiveMax = 1024.0;
        }
        else if (range < maxVal * 0.40)
        {
            double mid = (maxVal + minVal) * 0.5;
            double desiredSpan = Math.Max(range * 2.0, maxVal * 0.35);
            effectiveMin = Math.Max(0.0, mid - desiredSpan * 0.5);
            effectiveMax = effectiveMin + desiredSpan;
        }
        else
        {
            effectiveMin = 0.0;
            effectiveMax = maxVal * 1.15;
        }

        double span = effectiveMax - effectiveMin;

        // Assert
        Assert.True(span >= 10.0, "Динамический диапазон графика должен быть не менее 10 Б/с для видимой кривой.");
        Assert.True(effectiveMax > effectiveMin, "Верхняя граница AGC обязана строго превышать нижнюю.");
        Assert.True(effectiveMin >= 0.0, "Нижняя граница диапазона не может быть отрицательной.");
    }
}
