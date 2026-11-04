using System;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Различает реальное рождение, реальное завершение и перечисление существующего процесса в системе.
/// </summary>
public enum LifecycleEventKind
{
    /// <summary>
    /// Реальный запуск нового процесса операционной системой.
    /// </summary>
    Started,

    /// <summary>
    /// Реальное завершение процесса операционной системой.
    /// </summary>
    Stopped,

    /// <summary>
    /// Перечисление уже существующего процесса при запуске или синхронизации сессии (rundown).
    /// </summary>
    Rundown
}

/// <summary>
/// Идентифицирует уникальный экземпляр процесса внутри одного запуска реестра жизненного цикла.
/// Исключает ошибочное сопоставление при повторном использовании PID операционной системой.
/// </summary>
public readonly record struct ProcessInstanceId
{
    private readonly Guid _monitorEpoch;
    private readonly int _processId;
    private readonly long _generation;

    /// <summary>
    /// Эпоха реестра мониторинга, неизменная для всех его рабочих компонентов в рамках одного цикла службы.
    /// </summary>
    public Guid MonitorEpoch
    {
        get => _monitorEpoch;
        init
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("Эпоха мониторинга не может быть пустой.", nameof(value));
            }

            _monitorEpoch = value;
        }
    }

    /// <summary>
    /// Идентификатор процесса (PID); сам по себе не гарантирует уникальности во времени.
    /// </summary>
    public int ProcessId
    {
        get => _processId;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Идентификатор процесса (PID) не может быть отрицательным.");
            }

            _processId = value;
        }
    }

    /// <summary>
    /// Строго положительный номер поколения, назначенный единственным потоком-писателем реестра.
    /// </summary>
    public long Generation
    {
        get => _generation;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Поколение экземпляра процесса должно быть строго положительным.");
            }

            _generation = value;
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр уникального идентификатора процесса с проверкой инвариантов.
    /// </summary>
    /// <param name="monitorEpoch">Эпоха реестра мониторинга.</param>
    /// <param name="processId">Идентификатор процесса (PID >= 0).</param>
    /// <param name="generation">Номер поколения процесса (generation > 0).</param>
    public ProcessInstanceId(Guid monitorEpoch, int processId, long generation)
    {
        MonitorEpoch = monitorEpoch;
        ProcessId = processId;
        Generation = generation;
    }

    /// <summary>
    /// Деконструирует идентификатор процесса на составляющие поля.
    /// </summary>
    /// <param name="monitorEpoch">Эпоха мониторинга.</param>
    /// <param name="processId">Идентификатор процесса.</param>
    /// <param name="generation">Поколение экземпляра.</param>
    public void Deconstruct(out Guid monitorEpoch, out int processId, out long generation)
    {
        monitorEpoch = MonitorEpoch;
        processId = ProcessId;
        generation = Generation;
    }
}

/// <summary>
/// Передаёт собственную неизменяемую копию полей события жизненного цикла процесса без ссылки на переиспользуемый буфер.
/// </summary>
public sealed record ProcessLifecycleEvent
{
    private readonly Guid _sourceEpoch;
    private readonly long _sequence;
    private readonly int _processId;
    private readonly int _parentProcessId;
    private readonly long _occurredQpc;
    private readonly DateTime _occurredUtc;
    private readonly string _imageName = string.Empty;
    private readonly string _commandLine = string.Empty;

    /// <summary>
    /// Эпоха конкретного подключения к источнику событий (ETW-сессии).
    /// </summary>
    public Guid SourceEpoch
    {
        get => _sourceEpoch;
        init
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("Эпоха источника событий не может быть пустой.", nameof(value));
            }

            _sourceEpoch = value;
        }
    }

    /// <summary>
    /// Порядковый номер приёма обратного вызова источника; строго положителен.
    /// </summary>
    public long Sequence
    {
        get => _sequence;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Порядковый номер события должен быть строго положительным.");
            }

            _sequence = value;
        }
    }

    /// <summary>
    /// Вид изменения жизненного цикла процесса.
    /// </summary>
    public LifecycleEventKind Kind { get; init; }

    /// <summary>
    /// Идентификатор процесса (PID) из декодированного содержимого события.
    /// </summary>
    public int ProcessId
    {
        get => _processId;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Идентификатор процесса (PID) не может быть отрицательным.");
            }

            _processId = value;
        }
    }

    /// <summary>
    /// Идентификатор родительского процесса (PPID).
    /// </summary>
    public int ParentProcessId
    {
        get => _parentProcessId;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Идентификатор родительского процесса не может быть отрицательным.");
            }

            _parentProcessId = value;
        }
    }

    /// <summary>
    /// Непрозрачный ключ ядра Windows (UniqueProcessKey); нулевое значение означает отсутствие.
    /// </summary>
    public ulong KernelKey { get; init; }

    /// <summary>
    /// Исходная монотонная метка времени события из счетчика высокой точности QueryPerformanceCounter (QPC).
    /// </summary>
    public long OccurredQpc
    {
        get => _occurredQpc;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Метка QPC не может быть отрицательной.");
            }

            _occurredQpc = value;
        }
    }

    /// <summary>
    /// Время возникновения события в формате UTC для отображения и ведения истории.
    /// </summary>
    public DateTime OccurredUtc
    {
        get => _occurredUtc;
        init => _occurredUtc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    /// <summary>
    /// Имя исполняемого файла образа (может не содержать полного пути).
    /// </summary>
    public string ImageName
    {
        get => _imageName;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _imageName = value;
        }
    }

    /// <summary>
    /// Скопированная строка аргументов командной строки процесса.
    /// </summary>
    public string CommandLine
    {
        get => _commandLine;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _commandLine = value;
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр события жизненного цикла с валидацией границ и инвариантов.
    /// </summary>
    /// <param name="sourceEpoch">Эпоха сессии источника событий.</param>
    /// <param name="sequence">Порядковый номер приёма (> 0).</param>
    /// <param name="kind">Тип события жизненного цикла.</param>
    /// <param name="processId">PID процесса (>= 0).</param>
    /// <param name="parentProcessId">PID родителя (>= 0).</param>
    /// <param name="kernelKey">Непрозрачный ключ ядра Windows.</param>
    /// <param name="occurredQpc">Метка времени QPC (>= 0).</param>
    /// <param name="occurredUtc">Метка времени в формате UTC.</param>
    /// <param name="imageName">Имя исполняемого файла образа.</param>
    /// <param name="commandLine">Строка командной строки запуска.</param>
    public ProcessLifecycleEvent(
        Guid sourceEpoch,
        long sequence,
        LifecycleEventKind kind,
        int processId,
        int parentProcessId,
        ulong kernelKey,
        long occurredQpc,
        DateTime occurredUtc,
        string imageName,
        string commandLine)
    {
        SourceEpoch = sourceEpoch;
        Sequence = sequence;
        Kind = kind;
        ProcessId = processId;
        ParentProcessId = parentProcessId;
        KernelKey = kernelKey;
        OccurredQpc = occurredQpc;
        OccurredUtc = occurredUtc;
        ImageName = imageName;
        CommandLine = commandLine;
    }

    /// <summary>
    /// Деконструирует событие жизненного цикла процесса на ключевые поля.
    /// </summary>
    /// <param name="sourceEpoch">Эпоха источника.</param>
    /// <param name="sequence">Порядковый номер.</param>
    /// <param name="kind">Вид события.</param>
    /// <param name="processId">PID процесса.</param>
    /// <param name="parentProcessId">PID родителя.</param>
    /// <param name="kernelKey">Ключ ядра.</param>
    /// <param name="occurredQpc">Метка QPC.</param>
    /// <param name="occurredUtc">Метка UTC.</param>
    /// <param name="imageName">Имя образа.</param>
    /// <param name="commandLine">Командная строка.</param>
    public void Deconstruct(
        out Guid sourceEpoch,
        out long sequence,
        out LifecycleEventKind kind,
        out int processId,
        out int parentProcessId,
        out ulong kernelKey,
        out long occurredQpc,
        out DateTime occurredUtc,
        out string imageName,
        out string commandLine)
    {
        sourceEpoch = SourceEpoch;
        sequence = Sequence;
        kind = Kind;
        processId = ProcessId;
        parentProcessId = ParentProcessId;
        kernelKey = KernelKey;
        occurredQpc = OccurredQpc;
        occurredUtc = OccurredUtc;
        imageName = ImageName;
        commandLine = CommandLine;
    }
}

/// <summary>
/// Содержит ограниченную диагностику ошибки разбора без сохранения необработанного исключения,
/// конфиденциальных командных строк или секретных данных.
/// </summary>
public sealed record ParseFailureDiagnostic
{
    private readonly Guid _sourceEpoch;
    private readonly string _exceptionType = string.Empty;

    /// <summary>
    /// Эпоха источника, при обработке данных которого произошел сбой.
    /// </summary>
    public Guid SourceEpoch
    {
        get => _sourceEpoch;
        init
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("Эпоха источника ошибки не может быть пустой.", nameof(value));
            }

            _sourceEpoch = value;
        }
    }

    /// <summary>
    /// Вид декодируемого события жизненного цикла.
    /// </summary>
    public LifecycleEventKind Kind { get; init; }

    /// <summary>
    /// Имя типа исключения без конфиденциального текста сообщения или внутренних данных (Message/Data).
    /// </summary>
    public string ExceptionType
    {
        get => _exceptionType;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _exceptionType = value;
        }
    }

    /// <summary>
    /// Числовой код ошибки HResult для диагностики класса сбоя.
    /// </summary>
    public int ErrorCode { get; init; }

    /// <summary>
    /// Инициализирует диагностическую запись об ошибке разбора события.
    /// </summary>
    /// <param name="sourceEpoch">Эпоха источника.</param>
    /// <param name="kind">Вид события жизненного цикла.</param>
    /// <param name="exceptionType">Имя типа возникшего исключения.</param>
    /// <param name="errorCode">Числовой код ошибки HResult.</param>
    public ParseFailureDiagnostic(Guid sourceEpoch, LifecycleEventKind kind, string exceptionType, int errorCode)
    {
        SourceEpoch = sourceEpoch;
        Kind = kind;
        ExceptionType = exceptionType;
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Обозначает проверенное владение запущенной трассировочной сессией операционной системы.
/// Реализация хранит действительный дескриптор StartTrace; чужая сессия никогда не выдаётся через данный контракт.
/// </summary>
public interface IOwnedProcessTraceSession : IDisposable
{
    /// <summary>
    /// Описательное имя собственной сессии для подключения consumer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Идемпотентно останавливает собственный native handle, не меняя глобальные sampling-настройки.
    /// </summary>
    void Stop();
}
