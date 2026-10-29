using System.Threading;
using System.Threading.Tasks;

namespace StartupMonitor.Service.Services.Runtime;

/// <summary>
/// Прогоняет тело цикла «работа → сон»: активный интервал при наличии работы,
/// Idle-интервал (вплоть до <see cref="Timeout.InfiniteTimeSpan"/> — глубокий сон, 0% CPU)
/// при простое. Пробуждение мгновенное по <see cref="ActivityGate.Pulse"/> или при отмене.
/// </summary>
public static class SleepAwareLoop
{
    /// <summary>Логгер для фиксации сбоев итерации (тихие падения запрещены).</summary>
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext(typeof(SleepAwareLoop));

    /// <summary>
    /// Запускает адаптивный цикл. Тело <paramref name="body"/> вызывается только когда
    /// <paramref name="hasWork"/> возвращает <c>true</c>; иначе цикл спит
    /// <paramref name="idleInterval"/> (обычно <see cref="Timeout.InfiniteTimeSpan"/>).
    /// </summary>
    /// <param name="gate">Шлюз-будильник, пробуждающий цикл без периодического опроса.</param>
    /// <param name="hasWork">Предикат наличия работы (активные окна, правила и т.п.).</param>
    /// <param name="activeInterval">Интервал между итерациями при наличии работы.</param>
    /// <param name="idleInterval">Интервал сна при простое; <c>Infinite</c> — до события.</param>
    /// <param name="body">Тело итерации; исключения логирует само тело.</param>
    /// <param name="ct">Токен отмены всего цикла.</param>
    public static async Task RunAsync(
        ActivityGate gate,
        Func<bool> hasWork,
        TimeSpan activeInterval,
        TimeSpan idleInterval,
        Func<CancellationToken, Task> body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(hasWork);
        ArgumentNullException.ThrowIfNull(body);

        while (!ct.IsCancellationRequested)
        {
            var worked = false;
            var iterationFailed = false;
            try
            {
                worked = hasWork();
                if (worked)
                    await body(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // КРИТИЧНО: при сбое нельзя уходить в idle-сон (он может быть Infinite),
                // иначе подсистема навсегда перестанет просыпаться и перестанет выполнять работу.
                Log.Warning(ex, "SleepAwareLoop: сбой итерации, повтор через активный интервал");
                iterationFailed = true;
            }

            if (iterationFailed)
            {
                // Гарантированный повтор через активный интервал (а не бесконечный сон).
                var retryInterval = activeInterval;
                if (retryInterval <= TimeSpan.Zero || retryInterval == Timeout.InfiniteTimeSpan)
                    retryInterval = TimeSpan.FromSeconds(5);

                try
                {
                    await gate.WaitAsync(retryInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var timeout = worked ? activeInterval : idleInterval;
            try
            {
                await gate.WaitAsync(timeout, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
