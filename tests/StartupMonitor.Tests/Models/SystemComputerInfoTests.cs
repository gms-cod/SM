using System;
using System.Linq;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Models;

/// <summary>
/// Модульные тесты для сборщика системной информации SystemComputerInfo.
/// </summary>
public class SystemComputerInfoTests
{
    [Fact]
    public void Collect_ReturnsValidSystemInfo()
    {
        // Act
        var info = SystemComputerInfo.Collect();

        // Assert
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.MachineName));
        Assert.Equal(Environment.MachineName, info.MachineName);
        Assert.False(string.IsNullOrWhiteSpace(info.UserName));
        Assert.Equal(Environment.UserName, info.UserName);
        Assert.False(string.IsNullOrWhiteSpace(info.OsName));
        Assert.False(string.IsNullOrWhiteSpace(info.CpuName));
        Assert.True(info.LogicalCores > 0);
        Assert.Equal(Environment.ProcessorCount, info.LogicalCores);
        Assert.True(info.RamTotalGb > 0);
        Assert.False(string.IsNullOrWhiteSpace(info.Architecture));
        Assert.False(string.IsNullOrWhiteSpace(info.UptimeDisplay));
    }

    [Fact]
    public void Collect_PopulatesDrivesInformation()
    {
        // Act
        var info = SystemComputerInfo.Collect();

        // Assert
        Assert.NotNull(info.Disks);
        Assert.NotEmpty(info.Disks);

        var firstDisk = info.Disks.First();
        Assert.False(string.IsNullOrWhiteSpace(firstDisk.Name));
        Assert.True(firstDisk.TotalGb > 0);
        Assert.True(firstDisk.FreeGb >= 0);
        Assert.True(firstDisk.UsedPercent >= 0 && firstDisk.UsedPercent <= 100);
        Assert.False(string.IsNullOrWhiteSpace(firstDisk.DisplaySummary));
    }

    [Fact]
    public void CompactSummary_ContainsCoreMetrics()
    {
        // Act
        var info = SystemComputerInfo.Collect();
        var summary = info.CompactSummary;

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(summary));
        Assert.Contains(info.CpuName, summary);
        Assert.Contains("RAM", summary);
        Assert.Contains("Диски", summary);
    }

    [Fact]
    public void ToReportString_GeneratesStructuredReport()
    {
        // Act
        var info = SystemComputerInfo.Collect();
        var report = info.ToReportString();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(report));
        Assert.Contains("=== СИСТЕМНАЯ ИНФОРМАЦИЯ О КОМПЬЮТЕРЕ ===", report);
        Assert.Contains(info.CpuName, report);
        Assert.Contains(info.OsName, report);
        Assert.Contains(info.MachineName, report);
        Assert.Contains("--- НАКОПИТЕЛИ И ДИСКИ ---", report);
    }

    [Theory]
    [InlineData(500, "500 Б")]
    [InlineData(1024, "1.0 КБ")]
    [InlineData(1024 * 1024, "1.0 МБ")]
    [InlineData(1024L * 1024 * 1024 * 5, "5.0 ГБ")]
    [InlineData(1024L * 1024 * 1024 * 1024 * 2, "2.0 ТБ")]
    public void FormatBytes_FormatsSizesCorrectly(long bytes, string expected)
    {
        // Act
        var formatted = SystemComputerInfo.FormatBytes(bytes);

        // Assert
        Assert.Equal(expected, formatted);
    }
}

