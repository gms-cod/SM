using System;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Содержит нормированные показатели нагрузки процесса за интервал измерения.
/// </summary>
/// <param name="CpuPercent">Доля общей мощности логических процессоров системы (от 0.0 до 100.0).</param>
/// <param name="IoBytesPerSecond">Суммарная скорость ввода-вывода (I/O) в байтах за секунду.</param>
public readonly record struct ProcessMetricDelta(double CpuPercent, long IoBytesPerSecond);

/// <summary>
/// Выполняет математический расчёт дельт метрик процессора и дискового ввода-вывода (I/O)
/// с нормализацией на число логических ядер и защитой от сброса счётчиков ядра.
/// </summary>
public static class ProcessMetricMath
{
    /// <summary>
    /// Вычисляет нормированные показатели нагрузки между двумя последовательными отсчётами для одного экземпляра процесса.
    /// Отклоняет первый отсчёт, интервалы с нулевой/отрицательной длительностью и сброшенные системные счётчики.
    /// </summary>
    /// <param name="previousCpuTicks">Предыдущее накопленное время процессора в тиках (TimeSpan ticks).</param>
    /// <param name="currentCpuTicks">Текущее накопленное время процессора в тиках (TimeSpan ticks).</param>
    /// <param name="previousIoBytes">Предыдущий объем переданных данных ввода-вывода в байтах.</param>
    /// <param name="currentIoBytes">Текущий объем переданных данных ввода-вывода в байтах.</param>
    /// <param name="elapsed">Интервал времени между двумя снимками счетчиков.</param>
    /// <param name="processorCount">Количество логических процессоров в системе (строго больше 0).</param>
    /// <returns>Экземпляр <see cref="ProcessMetricDelta"/> или <c>null</c> при невалидном интервале или сбросе счетчиков.</returns>
    public static ProcessMetricDelta? Calculate(
        long previousCpuTicks,
        long currentCpuTicks,
        long previousIoBytes,
        long currentIoBytes,
        TimeSpan elapsed,
        int processorCount)
    {
        if (processorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processorCount),
                processorCount,
                "Число логических процессоров должно быть строго положительным.");
        }

        if (elapsed <= TimeSpan.Zero ||
            previousCpuTicks < 0 ||
            currentCpuTicks < previousCpuTicks ||
            previousIoBytes < 0 ||
            currentIoBytes < previousIoBytes)
        {
            return null;
        }

        var cpuRatio = (currentCpuTicks - previousCpuTicks) / (double)elapsed.Ticks / processorCount * 100.0;
        var cpu = double.IsNaN(cpuRatio) || double.IsInfinity(cpuRatio)
            ? 0.0
            : Math.Clamp(cpuRatio, 0.0, 100.0);

        var rate = (currentIoBytes - previousIoBytes) / elapsed.TotalSeconds;
        long io;
        if (double.IsNaN(rate) || rate <= 0.0)
        {
            io = 0L;
        }
        else if (rate >= long.MaxValue || double.IsPositiveInfinity(rate))
        {
            io = long.MaxValue;
        }
        else
        {
            io = (long)rate;
        }

        return new ProcessMetricDelta(cpu, io);
    }
}
