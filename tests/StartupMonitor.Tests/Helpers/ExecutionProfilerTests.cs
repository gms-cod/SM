using StartupMonitor.Shared.Diagnostics;
using Xunit;

namespace StartupMonitor.Tests.Helpers;

public class ExecutionProfilerTests
{
    [Fact]
    public void Measure_RecordsCountAvgMaxTotal()
    {
        ExecutionProfiler.Enabled = true;
        ExecutionProfiler.Clear();

        using (ExecutionProfiler.Measure("Test.Op"))
            Thread.Sleep(5);

        ExecutionProfiler.Record("Test.Op", TimeSpan.FromMilliseconds(10));

        var snap = ExecutionProfiler.GetSnapshotAndReset();
        var op = Assert.Single(snap, s => s.Name == "Test.Op");
        Assert.Equal(2, op.Count);
        Assert.True(op.TotalMs >= 10, $"TotalMs={op.TotalMs}");
        Assert.True(op.MaxMs >= 5, $"MaxMs={op.MaxMs}");
        Assert.Equal(op.TotalMs / op.Count, op.AvgMs, precision: 5);
    }

    [Fact]
    public void Disabled_NoRecordingZeroOverhead()
    {
        ExecutionProfiler.Clear();
        ExecutionProfiler.Enabled = false;
        try
        {
            using (ExecutionProfiler.Measure("Test.Disabled"))
                Thread.Sleep(1);
            var snap = ExecutionProfiler.GetSnapshotAndReset();
            Assert.Empty(snap);
        }
        finally
        {
            ExecutionProfiler.Enabled = true;
        }
    }

    [Fact]
    public void DrainReport_ContainsHeaderAndResets()
    {
        ExecutionProfiler.Enabled = true;
        ExecutionProfiler.Clear();
        ExecutionProfiler.Record("Test.A", TimeSpan.FromMilliseconds(3));

        var report = ExecutionProfiler.DrainReport();
        Assert.Contains("Test.A", report);
        Assert.Contains("Count", report);
        Assert.Contains("Avg ms", report);

        // После Drain счётчики сброшены
        var snap = ExecutionProfiler.GetSnapshotAndReset();
        Assert.DoesNotContain(snap, s => s.Name == "Test.A");
    }

    [Fact]
    public void Concurrent_RecordsAreThreadSafe()
    {
        ExecutionProfiler.Enabled = true;
        ExecutionProfiler.Clear();

        Parallel.For(0, 200, i =>
        {
            using var _ = ExecutionProfiler.Measure("Test.Concurrent");
        });

        var snap = ExecutionProfiler.GetSnapshotAndReset();
        var op = Assert.Single(snap, s => s.Name == "Test.Concurrent");
        Assert.Equal(200, op.Count);
    }
}
