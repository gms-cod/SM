using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StartupMonitor.Shared.Diagnostics;

/// <summary>
/// Static thread-safe profiler of subroutine execution duration (Service and UI).
/// Measured via <see cref="Stopwatch.GetTimestamp"/> (~20 ns per call).
/// Zero GC allocations per call (16-byte struct on the stack; compiler calls Dispose directly).
/// Periodic report flush every 5 minutes via <see cref="StartAutoReportAsync"/> or <see cref="DrainReport"/>.
/// </summary>
public static class ExecutionProfiler
{
    /// <summary>Global flag indicating whether measurements are active.</summary>
    public static volatile bool Enabled = true;

    private static readonly ConcurrentDictionary<string, Aggregate> _stats =
        new(StringComparer.Ordinal);

    private static readonly double _tickToMs = 1000.0 / Stopwatch.Frequency;

    /// <summary>Snapshot of metrics for a single operation.</summary>
    public sealed class ProfileSnapshot
    {
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public string Name { get; init; } = string.Empty;
        public long Count { get; init; }
        public double TotalMs { get; init; }
        public double MaxMs { get; init; }
        public double AvgMs { get; init; }
    }

    private sealed class Aggregate
    {
        public long Count;
        public long TotalTicks;
        public long MaxTicks;
        public long MinTicks = long.MaxValue;
        public readonly object Sync = new();
    }

    /// <summary>\
    /// Starts measuring an operation. Zero GC allocations.\
    /// Usage: <c>using var _ = ExecutionProfiler.Profile("Service.ScanAll");</c>\
    /// </summary>
    public static Scope Profile(string operationName)
    {
        if (!Enabled || string.IsNullOrEmpty(operationName))
            return default;
        return new Scope(operationName, Stopwatch.GetTimestamp());
    }

    /// <summary>\
    /// Alias for <see cref="Profile"/> for convenience and backward compatibility.\
    /// Usage: <c>using var _ = ExecutionProfiler.Measure("Service.ScanAll");</c>\
    /// </summary>
    public static Scope Measure(string operationName) => Profile(operationName);

    /// <summary>Records a completed measurement in Stopwatch ticks.</summary>
    public static void Record(string operationName, long elapsedTicks)
    {
        if (!Enabled || string.IsNullOrEmpty(operationName) || elapsedTicks < 0)
            return;

        var slot = _stats.GetOrAdd(operationName, static _ => new Aggregate());
        lock (slot.Sync)
        {
            slot.Count++;
            slot.TotalTicks += elapsedTicks;
            if (elapsedTicks > slot.MaxTicks)
                slot.MaxTicks = elapsedTicks;
            if (elapsedTicks < slot.MinTicks)
                slot.MinTicks = elapsedTicks;
        }
    }

    /// <summary>Records a finished measurement from a <see cref="TimeSpan"/>.</summary>
    public static void Record(string operationName, TimeSpan elapsed)
    {
        Record(operationName, (long)(elapsed.TotalSeconds * Stopwatch.Frequency));
    }

    /// <summary>
    /// Returns a structured snapshot of all measurements and resets the counters (atomically).
    /// Sorted by TotalMs in descending order.
    /// </summary>
    public static List<ProfileSnapshot> GetSnapshotAndReset()
    {
        if (_stats.IsEmpty)
            return [];

        var list = new List<ProfileSnapshot>(_stats.Count);
        foreach (var kv in _stats)
        {
            long count, total, max;
            lock (kv.Value.Sync)
            {
                count = kv.Value.Count;
                total = kv.Value.TotalTicks;
                max = kv.Value.MaxTicks;
                kv.Value.Count = 0;
                kv.Value.TotalTicks = 0;
                kv.Value.MaxTicks = 0;
                kv.Value.MinTicks = long.MaxValue;
            }

            if (count == 0)
                continue;

            double totalMs = total * _tickToMs;
            double maxMs = max * _tickToMs;
            double avgMs = count > 0 ? totalMs / count : 0;

            list.Add(new ProfileSnapshot
            {
                Name = kv.Key,
                Count = count,
                TotalMs = totalMs,
                MaxMs = maxMs,
                AvgMs = avgMs
            });
        }

        list.Sort(static (a, b) => b.TotalMs.CompareTo(a.TotalMs));
        return list;
    }

    /// <summary>
    /// Builds a structured text report for the elapsed window (5 minutes) and resets the counters.
    /// </summary>
    public static string DrainReport(string componentName = "")
    {
        var snapshots = GetSnapshotAndReset();
        var tag = string.IsNullOrWhiteSpace(componentName) ? "Profiler" : componentName;

        if (snapshots.Count == 0)
            return $"[{tag}] ExecutionProfiler за 5 мин: вызовов не было.";

        var sb = new StringBuilder(1024);
        sb.Append('[').Append(tag).Append("] ExecutionProfiler за 5 мин: ")
          .Append(snapshots.Count).AppendLine(" подпрограмм:");
        sb.AppendLine("  Operation | Count | Avg ms | Max ms | Total ms");
        foreach (var s in snapshots)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F2} | {3:F2} | {4:F1}\n",
                s.Name, s.Count, s.AvgMs, s.MaxMs, s.TotalMs);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Alias for <see cref="DrainReport"/>.
    /// </summary>
    public static string DumpAndReset(string componentName = "") => DrainReport(componentName);

    /// <summary>
    /// Starts a background task that periodically flushes the report to the log.
    /// Does not block the calling thread.
    /// </summary>
    public static async Task StartAutoReportAsync(
        TimeSpan interval,
        Action<string> logAction,
        CancellationToken ct = default)
    {
        if (logAction == null) return;
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                var report = DrainReport();
                if (!string.IsNullOrWhiteSpace(report) && !report.Contains("вызовов не было"))
                {
                    logAction(report);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExecutionProfiler.StartAutoReport error: {ex.Message}");
        }
    }

    /// <summary>Clears all counters without a report (for tests).</summary>
    public static void Clear() => _stats.Clear();

    /// <summary>
    /// Stack measurement timer. 16-byte readonly struct; not boxed when used with `using`
    /// and safe to use in async/await methods.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string? _operationName;
        private readonly long _startTimestamp;

        /// <summary>Scope: Executes a scope operation.</summary>
        /// <param name="operationName">Input data: value of the operationName parameter (string).</param>
        /// <param name="startTimestamp">Input data: value of the startTimestamp parameter (long).</param>
        internal Scope(string operationName, long startTimestamp)
        {
            _operationName = operationName;
            _startTimestamp = startTimestamp;
        }

        /// <summary>Dispose: Releases resources used by the object.</summary>
        public void Dispose()
        {
            var name = _operationName;
            if (name is null)
                return;
            Record(name, Stopwatch.GetTimestamp() - _startTimestamp);
        }
    }
}

