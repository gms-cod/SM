using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Ограничивает приём входящих событий жизненного цикла процессов по количеству и оценочному байтовому бюджету.
/// Предотвращает неконтролируемый рост памяти, ведет атомарный учет потерь и запросов сверки (reconciliation).
/// </summary>
public sealed class LifecycleIngress
{
    private readonly Channel<QueuedEvent> _channel;
    private readonly long _byteBudget;
    private long _retainedBytes;
    private long _dropped;
    private long _requested;
    private long _completed;
    private long _parseFailures;
    private ParseFailureDiagnostic? _lastParseFailure;
    private int _closed;

    /// <summary>
    /// Хранит оценочную стоимость памяти вместе с событием для строго симметричного освобождения бюджета.
    /// </summary>
    /// <param name="Event">Событие жизненного цикла процесса.</param>
    /// <param name="Cost">Оценочный размер полезной нагрузки в байтах.</param>
    private readonly record struct QueuedEvent(ProcessLifecycleEvent Event, long Cost);

    /// <summary>
    /// Инициализирует ограниченный канал приёма событий со стандартными лимитами (4096 событий, 8 МБ буфера).
    /// </summary>
    public LifecycleIngress() : this(4096, 8 * 1024 * 1024)
    {
    }

    /// <summary>
    /// Инициализирует ограниченный канал приёма событий с заданными лимитами очереди и памяти.
    /// </summary>
    /// <param name="capacity">Максимальное количество одновременно ожидающих событий в очереди (по умолчанию 4096).</param>
    /// <param name="byteBudget">Байтовый лимит удерживаемых строк и служебных данных полезной нагрузки (по умолчанию 8 МБ).</param>
    public LifecycleIngress(int capacity = 4096, long byteBudget = 8 * 1024 * 1024)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Емкость очереди должна быть строго положительной.");
        }

        if (byteBudget < 256)
        {
            throw new ArgumentOutOfRangeException(nameof(byteBudget), byteBudget, "Байтовый бюджет очереди не может быть менее 256 байт.");
        }

        _byteBudget = byteBudget;
        _channel = Channel.CreateBounded<QueuedEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// Возвращает общее число событий, отклоненных из-за переполнения очереди или исчерпания байтового бюджета.
    /// </summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Возвращает текущий объем зарезервированных и ожидающих в очереди полезных данных в байтах.
    /// </summary>
    public long RetainedBytes => Interlocked.Read(ref _retainedBytes);

    /// <summary>
    /// Возвращает суммарное число ошибок парсинга полезной нагрузки.
    /// </summary>
    public long ParseFailures => Interlocked.Read(ref _parseFailures);

    /// <summary>
    /// Возвращает номер поколения сверки, который должен быть покрыт следующей успешной синхронизацией.
    /// </summary>
    public long RequestedReconciliation => Interlocked.Read(ref _requested);

    /// <summary>
    /// Показывает, требуется ли выполнение сверки (есть ли неподтвержденные запросы поколений).
    /// </summary>
    public bool NeedsReconciliation => RequestedReconciliation > Interlocked.Read(ref _completed);

    /// <summary>
    /// Регистрирует ошибку разбора полезной нагрузки, обновляет счетчик, фиксирует последнюю диагностику и запрашивает сверку.
    /// Не сохраняет текст исключения и конфиденциальные данные.
    /// </summary>
    /// <param name="exception">Возникшее исключение (сам объект и Message не сохраняются).</param>
    /// <param name="kind">Вид события жизненного цикла.</param>
    /// <param name="sourceEpoch">Эпоха сессии источника событий.</param>
    public void ReportParseFailure(Exception exception, LifecycleEventKind kind, Guid sourceEpoch)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (sourceEpoch == Guid.Empty)
        {
            throw new ArgumentException("Эпоха источника ошибки не может быть пустой.", nameof(sourceEpoch));
        }

        Interlocked.Increment(ref _parseFailures);
        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        Interlocked.Exchange(ref _lastParseFailure, new ParseFailureDiagnostic(sourceEpoch, kind, typeName, exception.HResult));
        RequestReconciliation();
    }

    /// <summary>
    /// Атомарно извлекает последнюю коалесцированную диагностику ошибки парсинга для безопасного логирования вне обратного вызова.
    /// </summary>
    /// <returns>Диагностическая запись или null, если новых ошибок не зафиксировано.</returns>
    public ParseFailureDiagnostic? TakeParseFailureDiagnostic()
    {
        return Interlocked.Exchange(ref _lastParseFailure, null);
    }

    /// <summary>
    /// Запрашивает выполнение сверки состояния процессов без блокировки вызывающего потока.
    /// </summary>
    /// <returns>Новый назначенный номер поколения сверки.</returns>
    public long RequestReconciliation()
    {
        return Interlocked.Increment(ref _requested);
    }

    /// <summary>
    /// Подтверждает успешную фиксацию снимка сверки для указанного поколения.
    /// Должен вызываться единственным потоком-писателем после слияния снимка с очередью.
    /// </summary>
    /// <param name="generation">Номер поколения сверки, зафиксированный перед началом соответствующего снимка.</param>
    public void AcknowledgeReconciliation(long generation)
    {
        var requested = RequestedReconciliation;
        if (generation < 0 || generation > requested)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                generation,
                $"Номер поколения сверки {generation} выходит за допустимые границы [0, {requested}].");
        }

        while (true)
        {
            var current = Interlocked.Read(ref _completed);
            if (generation <= current)
            {
                break;
            }

            if (Interlocked.CompareExchange(ref _completed, generation, current) == current)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Пытается асинхронно или мгновенно поместить событие в очередь без блокирующего ожидания.
    /// Проверяет доступность байтового бюджета и ёмкости канала.
    /// </summary>
    /// <param name="value">Проверенное событие жизненного цикла процесса.</param>
    /// <returns>true, если событие успешно добавлено; false, если отклонено из-за лимитов или закрытия.</returns>
    public bool TryPublish(ProcessLifecycleEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.ImageName);
        ArgumentNullException.ThrowIfNull(value.CommandLine);

        if (Volatile.Read(ref _closed) != 0)
        {
            return false;
        }

        var cost = 128L + 2L * (value.ImageName.Length + (long)value.CommandLine.Length);

        while (true)
        {
            var retained = Interlocked.Read(ref _retainedBytes);
            if (cost > _byteBudget - retained)
            {
                return Reject();
            }

            if (Interlocked.CompareExchange(ref _retainedBytes, retained + cost, retained) == retained)
            {
                break;
            }
        }

        if (_channel.Writer.TryWrite(new QueuedEvent(value, cost)))
        {
            return true;
        }

        // Если запись не удалась (очередь переполнена), возвращаем зарезервированные байты
        Interlocked.Add(ref _retainedBytes, -cost);
        return Volatile.Read(ref _closed) == 0 && Reject();
    }

    /// <summary>
    /// Ожидает появления новых данных для чтения без периодического опроса (polling).
    /// </summary>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>true, если доступны элементы для чтения; false, если канал завершен.</returns>
    public ValueTask<bool> WaitToReadAsync(CancellationToken ct)
    {
        return _channel.Reader.WaitToReadAsync(ct);
    }

    /// <summary>
    /// Извлекает следующее событие из очереди и симметрично освобождает зарезервированный байтовый бюджет.
    /// </summary>
    /// <param name="value">Извлеченное событие или null при отсутствии данных.</param>
    /// <returns>true, если событие успешно извлечено; иначе false.</returns>
    public bool TryRead(out ProcessLifecycleEvent? value)
    {
        if (!_channel.Reader.TryRead(out var queued))
        {
            value = null;
            return false;
        }

        Interlocked.Add(ref _retainedBytes, -queued.Cost);
        value = queued.Event;
        return true;
    }

    /// <summary>
    /// Идемпотентно закрывает приём входящих событий. Оставшиеся события могут быть вычитаны до конца.
    /// </summary>
    public void Complete()
    {
        Interlocked.Exchange(ref _closed, 1);
        _channel.Writer.TryComplete();
    }

    /// <summary>
    /// Учитывает отклонение события, увеличивает счётчик потерь и запрашивает сверку.
    /// </summary>
    /// <returns>Всегда false для удобства цепочки возврата.</returns>
    private bool Reject()
    {
        Interlocked.Increment(ref _dropped);
        RequestReconciliation();
        return false;
    }
}
