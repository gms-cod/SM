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
/// Статический потокобезопасный профайлер длительности выполнения подпрограмм (Service и UI).
/// Замер через <see cref="Stopwatch.GetTimestamp"/> (~20 нс на вызов).
/// Нулевые GC-аллокации при вызове (структура 16 байт на стеке, компилятор вызывает Dispose напрямую).
/// Периодический сброс отчёта каждые 5 минут через <see cref="StartAutoReportAsync"/> или <see cref="DrainReport"/>.
/// </summary>
public static class ExecutionProfiler
{
    /// <summary>Глобальный флаг активности замеров.</summary>
    public static volatile bool Enabled = true;

    private static readonly ConcurrentDictionary<string, Aggregate> _stats =
        new(StringComparer.Ordinal);

    private static readonly double _tickToMs = 1000.0 / Stopwatch.Frequency;

    /// <summary>Снимок метрик одной операции.</summary>
    public sealed class ProfileSnapshot
    {
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

    /// <summary>
    /// Начинает замер операции. Нулевые GC-аллокации.
    /// Использование: <c>using var _ = ExecutionProfiler.Profile("Service.ScanAll");</c>
    /// </summary>
    public static Scope Profile(string operationName)
    {
        if (!Enabled || string.IsNullOrEmpty(operationName))
            return default;
        return new Scope(operationName, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Алиас <see cref="Profile"/> для удобства и обратной совместимости.
    /// Использование: <c>using var _ = ExecutionProfiler.Measure("Service.ScanAll");</c>
    /// </summary>
    public static Scope Measure(string operationName) => Profile(operationName);

    /// <summary>Записывает готовый замер в тиках Stopwatch.</summary>
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

    /// <summary>Записывает готовый замер из <see cref="TimeSpan"/>.</summary>
    public static void Record(string operationName, TimeSpan elapsed)
    {
        Record(operationName, (long)(elapsed.TotalSeconds * Stopwatch.Frequency));
    }

    /// <summary>
    /// Возвращает структурированный снимок всех замеров и сбрасывает счётчики (атомарно).
    /// Сортировка — по убыванию TotalMs.
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
    /// Формирует структурированный текстовый отчёт за прошедшее окно (5 минут) и сбрасывает счётчики.
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
    /// Алиас для <see cref="DrainReport"/>.
    /// </summary>
    public static string DumpAndReset(string componentName = "") => DrainReport(componentName);

    /// <summary>
    /// Запускает фоновую задачу периодического сброса отчёта в лог.
    /// Не блокирует вызывающий поток.
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

    /// <summary>Очищает все счётчики без отчёта (для тестов).</summary>
    public static void Clear() => _stats.Clear();

    /// <summary>
    /// Стековый таймер замера. Readonly-структура 16 байт,
    /// не боксируется при using и безопасно работает в async/await методах.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string? _operationName;
        private readonly long _startTimestamp;

        /// <summary>Scope: Выполняет операцию Scope.</summary>
        /// <param name="operationName">Входные данные: значение параметра operationName (string).</param>
        /// <param name="startTimestamp">Входные данные: значение параметра startTimestamp (long).</param>
        internal Scope(string operationName, long startTimestamp)
        {
            _operationName = operationName;
            _startTimestamp = startTimestamp;
        }

        /// <summary>Dispose: Освобождает занятые ресурсы объекта.</summary>
        public void Dispose()
        {
            var name = _operationName;
            if (name is null)
                return;
            Record(name, Stopwatch.GetTimestamp() - _startTimestamp);
        }
    }
}

