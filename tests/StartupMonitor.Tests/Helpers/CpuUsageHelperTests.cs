using StartupMonitor.Shared.Helpers;
using Xunit;

namespace StartupMonitor.Tests.Helpers;

public class CpuUsageHelperTests
{
    [Fact]
    public void CalculateCpuPercentage_ZeroTotal_ReturnsZero()
    {
        var pct = CpuUsageHelper.CalculateCpuPercentage(0, 0, 0, true);
        Assert.Equal(0, pct);
    }

    [Fact]
    public void CalculateCpuPercentage_AllIdle_ReturnsZero()
    {
        // total = 1000, idle = 1000 -> 0%
        var pct = CpuUsageHelper.CalculateCpuPercentage(1000, 1000, 0, true);
        Assert.Equal(0, pct);
    }

    [Fact]
    public void CalculateCpuPercentage_HalfBusy_NoSelf_Returns50()
    {
        // total = 1000, idle = 500 -> busy = 500 -> 50%
        var pct = CpuUsageHelper.CalculateCpuPercentage(1000, 500, 0, true);
        Assert.Equal(50, pct);
    }

    [Fact]
    public void CalculateCpuPercentage_ExcludeSelf_DeductsOurTicks()
    {
        // total = 1000, idle = 200 (busy = 800, 80% total)
        // our ticks = 300
        // external busy = 800 - 300 = 500 -> 50% external
        var pctWithSelf = CpuUsageHelper.CalculateCpuPercentage(1000, 200, 300, excludeSelf: false);
        var pctExcluded = CpuUsageHelper.CalculateCpuPercentage(1000, 200, 300, excludeSelf: true);

        Assert.Equal(80, pctWithSelf);
        Assert.Equal(50, pctExcluded);
    }

    [Fact]
    public void CalculateCpuPercentage_OurTicksExceedsBusy_ClampsToZero()
    {
        // total = 1000, idle = 700 (busy = 300)
        // our ticks = 400 (exceeds busy due to slight time delta)
        var pct = CpuUsageHelper.CalculateCpuPercentage(1000, 700, 400, excludeSelf: true);
        Assert.Equal(0, pct);
    }

    [Fact]
    public void CalculateCpuPercentage_MaxClampedTo100()
    {
        // total = 100, idle = -50 (anomalous)
        var pct = CpuUsageHelper.CalculateCpuPercentage(100, 0, 0, false);
        Assert.Equal(100, pct);
    }

    [Fact]
    public void GetCpuUsage_ReturnsValueBetween0And100()
    {
        var usage = CpuUsageHelper.GetCpuUsage(excludeSelf: true);
        Assert.InRange(usage, 0, 100);

        var totalUsage = CpuUsageHelper.GetCpuUsage(excludeSelf: false);
        Assert.InRange(totalUsage, 0, 100);
    }
}

