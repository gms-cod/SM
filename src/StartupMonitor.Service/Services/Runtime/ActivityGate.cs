using System.Threading;
using System.Threading.Tasks;

namespace StartupMonitor.Service.Services.Runtime;

/// <summary>
/// Шлюз активности подсистемы. Считает число активных потребителей (UI-клиенты,
/// открытые окна, активные правила) и предоставляет async-«будильник» для глубокого
/// сна без периодического опроса. Полностью потокобезопасен.
/// </summary>
/// <remarks>
/// Используется вместе с <see cref="SleepAwareLoop"/>: пока потребителей нет, цикл
/// спит неограниченно долго (<see cref="Timeout.InfiniteTimeSpan"/>), не расходуя CPU;
/// при появлении потребителя (переход 0→1) или явном <see cref="Pulse"/> цикл мгновенно
/// просыпается. <see cref="SemaphoreSlim"/>, а не <c>ManualResetEventSlim</c>, т.к.
/// блокирующие примитивы недопустимы в async-циклах.
/// </remarks>
public sealed class ActivityGate : IDisposable
{
    // «Будильник»: capacity = 1, начальный count = 0 (спим). Release() будит один цикл.
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _consumers;
    private bool _disposed;

    /// <summary>true — есть хотя бы один активный потребитель.</summary>
    public bool IsActive => Volatile.Read(ref _consumers) > 0;

    /// <summary>Число активных потребителей.</summary>
    public int ConsumerCount => Volatile.Read(ref _consumers);

    /// <summary>
    /// Регистрирует потребителя. При переходе 0→1 немедленно будит спящий цикл.
    /// </summary>
    public void AddConsumer()
    {
        if (Interlocked.Increment(ref _consumers) == 1)
            Pulse();
    }

    /// <summary>
    /// Снимает потребителя с учёта. Значение счётчика не опускается ниже нуля.
    /// </summary>
    public void RemoveConsumer()
    {
        if (Interlocked.Decrement(ref _consumers) < 0)
            Interlocked.Exchange(ref _consumers, 0);
    }

    /// <summary>
    /// Устанавливает активность напрямую: <c>true</c> — один потребитель,
    /// <c>false</c> — ни одного. Идемпотентно (повторные вызовы не копят счётчик).
    /// </summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            if (Interlocked.CompareExchange(ref _consumers, 1, 0) == 0)
                Pulse();
        }
        else if (Interlocked.Exchange(ref _consumers, 0) != 0)
        {
            Pulse();
        }
    }

    /// <summary>
    /// Немедленно будит ожидающий цикл (смена конфигурации, правил ЧС, пауза и т.п.).
    /// Лишние сигналы не накапливаются.
    /// </summary>
    public void Pulse()
    {
        if (_disposed) return;
        try
        {
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Ждёт сигнала не дольше <paramref name="timeout"/>.
    /// <see cref="Timeout.InfiniteTimeSpan"/> — глубокий сон до <see cref="Pulse"/> или отмены.
    /// </summary>
    /// <returns>true — разбудил <see cref="Pulse"/>; false — сработал таймаут.</returns>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        => _wake.WaitAsync(timeout, ct);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Dispose();
    }
}
