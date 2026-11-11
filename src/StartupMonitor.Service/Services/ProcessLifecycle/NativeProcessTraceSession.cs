using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Данные подтверждения владения сессией ETW (lease-файл).
/// Сохраняются атомарно в %ProgramData%\StartupMonitor\etw_session.lease.
/// </summary>
public sealed class EtwSessionLease
{
    /// <summary>
    /// Статус владения: "Creating", "Active", "Stopped".
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор процесса (PID), создавшего или создающего сессию.
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Время старта процесса владельца в формате UTC (для исключения коллизий переиспользования PID).
    /// </summary>
    public DateTime ProcessCreationTimeUtc { get; set; }

    /// <summary>
    /// Уникальный GUID сессии (nonce), переданный в Wnode.Guid при создании сессии ядра.
    /// </summary>
    public Guid SessionGuid { get; set; }

    /// <summary>
    /// Имя ETW-сессии.
    /// </summary>
    public string SessionName { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор продукта для защиты от конфликтов с чужими компонентами.
    /// </summary>
    public string ProductId { get; set; } = "StartupMonitor";

    /// <summary>
    /// Имя компьютера, на котором запущен владелец.
    /// </summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Момент записи файла lease в UTC.
    /// </summary>
    public DateTime WrittenAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Нативная статистика буферов и потерь событий сессии ядра ETW.
/// </summary>
/// <param name="NumberOfBuffers">Общее число выделенных буферов ядра.</param>
/// <param name="FreeBuffers">Число свободных буферов в пуле.</param>
/// <param name="EventsLost">Число событий, отброшенных ядром из-за нехватки буферов.</param>
/// <param name="BuffersWritten">Число буферов, успешно записанных ядром.</param>
/// <param name="LogBuffersLost">Число буферов файла журнала, потерянных при дисковой записи.</param>
/// <param name="RealTimeBuffersLost">Число буферов реального времени, потерянных потребителями.</param>
public sealed record NativeTraceStatistics(
    uint NumberOfBuffers,
    uint FreeBuffers,
    uint EventsLost,
    uint BuffersWritten,
    uint LogBuffersLost,
    uint RealTimeBuffersLost);

/// <summary>
/// Исключение при сбое управления native-сессией ETW.
/// Сохраняет код ошибки Win32 и имя сессии для контекстной диагностики.
/// </summary>
public sealed class NativeTraceSessionException : Exception
{
    /// <summary>
    /// Код ошибки Win32 (например, 5 — ERROR_ACCESS_DENIED, 183 — ERROR_ALREADY_EXISTS, 1450 — ERROR_NO_SYSTEM_RESOURCES).
    /// </summary>
    public uint ErrorCode { get; }

    /// <summary>
    /// Имя целевой сессии ETW.
    /// </summary>
    public string SessionName { get; }

    /// <summary>
    /// Инициализирует новое исключение с кодом ошибки Win32.
    /// </summary>
    /// <param name="errorCode">Код ошибки Win32.</param>
    /// <param name="sessionName">Имя сессии ETW.</param>
    /// <param name="message">Диагностическое сообщение об ошибке.</param>
    public NativeTraceSessionException(uint errorCode, string sessionName, string message)
        : base($"{message} (Session: '{sessionName}', Win32 Error: {errorCode})")
    {
        ErrorCode = errorCode;
        SessionName = sessionName;
    }

    /// <summary>
    /// Инициализирует новое исключение с кодом ошибки Win32 и вложенным исключением.
    /// </summary>
    /// <param name="errorCode">Код ошибки Win32.</param>
    /// <param name="sessionName">Имя сессии ETW.</param>
    /// <param name="message">Диагностическое сообщение об ошибке.</param>
    /// <param name="innerException">Вложенное исключение.</param>
    public NativeTraceSessionException(uint errorCode, string sessionName, string message, Exception innerException)
        : base($"{message} (Session: '{sessionName}', Win32 Error: {errorCode})", innerException)
    {
        ErrorCode = errorCode;
        SessionName = sessionName;
    }
}

/// <summary>
/// Надежный native-контроллер сессии ETW Windows 10/11 x64 в соответствии с ADR_ETW_SESSION_PROVIDER.
/// Управляет жизненным циклом изолированной сессии SystemTraceProvider, контролирует lease-файл владения,
/// безопасно обрабатывает orphan-сессии предыдущих запусков и гарантирует детерминированное освобождение ресурсов.
/// </summary>
public sealed class NativeProcessTraceSession : IOwnedProcessTraceSession
{
    #region Win32 P/Invoke Structures and Constants

    /// <summary>
    /// Заголовок Wnode для структур трассировки событий ETW (Windows SDK).
    /// Размер на x64: ровно 48 байт (Pack = 8).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct WNODE_HEADER
    {
        /// <summary>Полный размер буфера памяти, выделенного под структуру и строки (в байтах).</summary>
        public uint BufferSize;

        /// <summary>Идентификатор провайдера.</summary>
        public uint ProviderId;

        /// <summary>Исторический контекст (union с Version/Linkage).</summary>
        public ulong HistoricalContext;

        /// <summary>Метка времени (union с KernelHandle).</summary>
        public ulong TimeStamp;

        /// <summary>Уникальный GUID сессии трассировки.</summary>
        public Guid Guid;

        /// <summary>Контекст клиента таймера: 1 = QueryPerformanceCounter (QPC).</summary>
        public uint ClientContext;

        /// <summary>Флаги заголовка: WNODE_FLAG_TRACED_GUID (0x00020000).</summary>
        public uint Flags;
    }

    /// <summary>
    /// Параметры сессии трассировки событий ETW (Windows SDK).
    /// Размер на x64: ровно 120 байт (Pack = 8).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct EVENT_TRACE_PROPERTIES
    {
        /// <summary>Заголовок WNODE_HEADER (смещение 0..48 байт).</summary>
        public WNODE_HEADER Wnode;

        /// <summary>Размер каждого буфера ядра в килобайтах (например, 64 КБ).</summary>
        public uint BufferSize;

        /// <summary>Минимальное количество буферов ядра.</summary>
        public uint MinimumBuffers;

        /// <summary>Максимальное количество буферов ядра.</summary>
        public uint MaximumBuffers;

        /// <summary>Максимальный размер файла журнала в МБ (0 для real-time режима).</summary>
        public uint MaximumFileSize;

        /// <summary>Режим логирования: SYSTEM_LOGGER_MODE | REAL_TIME_MODE.</summary>
        public uint LogFileMode;

        /// <summary>Интервал принудительного сброса буферов в секундах.</summary>
        public uint FlushTimer;

        /// <summary>Флаги трассировки ядра: EVENT_TRACE_FLAG_PROCESS (0x00000001).</summary>
        public uint EnableFlags;

        /// <summary>Предел времени удержания буферов (union FlushThreshold).</summary>
        public int AgeLimit;

        /// <summary>Текущее число выделенных буферов ядра.</summary>
        public uint NumberOfBuffers;

        /// <summary>Число свободных буферов в пуле ядра.</summary>
        public uint FreeBuffers;

        /// <summary>Число отброшенных ядром событий при нехватке буферов.</summary>
        public uint EventsLost;

        /// <summary>Число буферов, записанных ядром.</summary>
        public uint BuffersWritten;

        /// <summary>Число буферов журнала, потерянных при дисковой записи.</summary>
        public uint LogBuffersLost;

        /// <summary>Число буферов реального времени, потерянных потребителем.</summary>
        public uint RealTimeBuffersLost;

        /// <summary>Дескриптор потока логгера ETW (8 байт на x64, смещение 104..112).</summary>
        public IntPtr LoggerThreadId;

        /// <summary>Смещение в байтах от начала структуры к имени файла журнала (0, если файл не используется).</summary>
        public uint LogFileNameOffset;

        /// <summary>Смещение в байтах от начала структуры к Unicode-строке имени сессии.</summary>
        public uint LoggerNameOffset;
    }

    /// <summary>Флаг Wnode: сессия использует пользовательский GUID сессии.</summary>
    internal const uint WNODE_FLAG_TRACED_GUID = 0x00020000;

    /// <summary>Режим real-time доставки событий без обязательного дискового файла.</summary>
    internal const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;

    /// <summary>Режим изолированного системного провайдера (Windows 8+ SystemTraceProvider).</summary>
    internal const uint EVENT_TRACE_SYSTEM_LOGGER_MODE = 0x02000000;

    /// <summary>Флаг трассировки ядра: события жизненного цикла процессов (Process Start / Stop / DCStart).</summary>
    internal const uint EVENT_TRACE_FLAG_PROCESS = 0x00000001;
    /// <summary>Флаг системного провайдера событий TCP/IP.</summary>
    internal const uint EVENT_TRACE_FLAG_NETWORK_TCPIP = 0x00010000;

    /// <summary>Код управления ControlTraceW: опрос текущих параметров и статистики сессии.</summary>
    internal const uint EVENT_TRACE_CONTROL_QUERY = 0;

    /// <summary>Код управления ControlTraceW: остановка сессии трассировки.</summary>
    internal const uint EVENT_TRACE_CONTROL_STOP = 1;

    /// <summary>Код управления ControlTraceW: обновление параметров работающей сессии.</summary>
    internal const uint EVENT_TRACE_CONTROL_UPDATE = 2;

    /// <summary>Код управления ControlTraceW: принудительный сброс активных буферов.</summary>
    internal const uint EVENT_TRACE_CONTROL_FLUSH = 3;

    /// <summary>Win32 статус: операция выполнена успешно.</summary>
    internal const uint ERROR_SUCCESS = 0;

    /// <summary>Win32 статус: отказано в доступе (требуются права локального администратора или SYSTEM).</summary>
    internal const uint ERROR_ACCESS_DENIED = 5;

    /// <summary>Win32 статус: неверная длина или размер буфера.</summary>
    internal const uint ERROR_BAD_LENGTH = 24;

    /// <summary>Win32 статус: передан неверный параметр структуры.</summary>
    internal const uint ERROR_INVALID_PARAMETER = 87;

    /// <summary>Win32 статус: сессия ETW с таким именем уже запущена в системе.</summary>
    internal const uint ERROR_ALREADY_EXISTS = 183;

    /// <summary>Win32 статус: предоставленный буфер меньше требуемого, но сессия остановлена/опрошена.</summary>
    internal const uint ERROR_MORE_DATA = 234;

    /// <summary>Win32 статус: сессия имеет активные соединения или находится в переходном состоянии.</summary>
    internal const uint ERROR_ACTIVE_CONNECTIONS = 240;

    /// <summary>Win32 статус: исчерпаны системные ресурсы или пул сессий трассировки.</summary>
    internal const uint ERROR_NO_SYSTEM_RESOURCES = 1450;

    /// <summary>Win32 статус: указанный экземпляр сессии ETW не найден в ядре.</summary>
    internal const uint ERROR_WMI_INSTANCE_NOT_FOUND = 4201;

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint StartTraceW(
        out ulong sessionHandle,
        string sessionName,
        IntPtr properties);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint ControlTraceW(
        ulong sessionHandle,
        string? sessionName,
        IntPtr properties,
        uint controlCode);

    #endregion

    #region Constants and Fields

    /// <summary>Имя сессии ETW по умолчанию для мониторинга процессов.</summary>
    public const string DefaultSessionName = "StartupMonitor.ProcessLifecycle";

    /// <summary>Имя машинного мьютекса для координации владения сессией между процессами службы.</summary>
    public const string MachineMutexName = @"Global\StartupMonitor.EtwSession.Mutex";
    /// <summary>Имя независимой сессии входящих TCP-соединений.</summary>
    public const string IncomingTcpSessionName = "StartupMonitor.IncomingTcp";
    /// <summary>Мьютекс владения сетевой сессией, отделённый от мониторинга процессов.</summary>
    public const string IncomingTcpMutexName = @"Global\StartupMonitor.IncomingTcp.EtwSession.Mutex";

    /// <summary>Размер общего буфера структуры и строковых параметров свойства ETW (в байтах).</summary>
    private const int PropertiesBufferSize = 4096;

    private readonly object _syncRoot = new();
    private readonly string _sessionName;
    private readonly Guid _sessionGuid;
    private readonly EtwSessionLease _lease;
    private readonly ILogger? _logger;

    private ulong _sessionHandle;
    private IntPtr _propertiesBuffer;
    private int _isStopped;

    #endregion

    #region Properties

    /// <summary>
    /// Имя запущенной native-сессии ETW для подключения consumers.
    /// </summary>
    public string Name => _sessionName;

    /// <summary>
    /// Уникальный GUID запущенной сессии (Wnode.Guid).
    /// </summary>
    public Guid SessionGuid => _sessionGuid;

    /// <summary>
    /// Дескриптор активной native-сессии ETW, возвращенный вызовом StartTraceW.
    /// </summary>
    public ulong SessionHandle => _sessionHandle;

    /// <summary>
    /// Признак активности сессии (не остановлена и дескриптор валиден).
    /// </summary>
    public bool IsActive => Volatile.Read(ref _isStopped) == 0 && _sessionHandle != 0;

    /// <summary>
    /// Актуальный снимок lease-файла владения сессией.
    /// </summary>
    public EtwSessionLease CurrentLease => _lease;

    #endregion

    #region Constructor and Factory Methods

    /// <summary>
    /// Приватный конструктор. Создание экземпляра выполняется через фабричные методы Start или TryStart.
    /// </summary>
    private NativeProcessTraceSession(
        string sessionName,
        ulong sessionHandle,
        Guid sessionGuid,
        EtwSessionLease lease,
        IntPtr propertiesBuffer,
        ILogger? logger)
    {
        _sessionName = sessionName;
        _sessionHandle = sessionHandle;
        _sessionGuid = sessionGuid;
        _lease = lease;
        _propertiesBuffer = propertiesBuffer;
        _logger = logger;
    }

    /// <summary>
    /// Запускает новую native-сессию ETW с проверкой машинного мьютекса и автоматической очисткой подтверждённых orphan-сессий.
    /// </summary>
    /// <param name="sessionName">Имя сессии (по умолчанию "StartupMonitor.ProcessLifecycle").</param>
    /// <param name="logger">Логгер для фиксации системных событий.</param>
    /// <returns>Экземпляр проверенного native-владельца сессии.</returns>
    /// <exception cref="ArgumentException">Если имя сессии пустое или некорректное.</exception>
    /// <exception cref="NativeTraceSessionException">Если StartTraceW или ControlTraceW вернули ошибку ядра.</exception>
    /// <exception cref="TimeoutException">Если машинный мьютекс не удалось захватить за отведенное время.</exception>
    public static NativeProcessTraceSession Start(string sessionName = DefaultSessionName, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new ArgumentException("Имя ETW-сессии не может быть пустым.", nameof(sessionName));
        }

        if (Encoding.Unicode.GetByteCount(sessionName + "\0") > PropertiesBufferSize - Marshal.SizeOf<EVENT_TRACE_PROPERTIES>())
        {
            throw new ArgumentException("Имя ETW-сессии превышает вместимость буфера свойств.", nameof(sessionName));
        }

        logger?.LogInformation("Инициализация запуска native ETW сессии '{SessionName}'...", sessionName);

        // 1. Захватываем машинный мьютекс для безопасного исключительного владения ресурсом
        var mutex = AcquireMachineMutex(TimeSpan.FromSeconds(10), out var ownsMutex, logger, sessionName);
        NativeProcessTraceSession? created = null;
        IntPtr propertiesBuffer = IntPtr.Zero;

        try
        {
            // 2. Проверяем и безопасно останавливаем ТОЛЬКО подтвержденную orphan-сессию от нашего продукта
            ReconcileOrphanSessionIfNeeded(sessionName, logger);

            // 3. Формируем новый непредсказуемый nonce GUID и записываем атомарный Intent-Lease "Creating"
            using var currentProcess = Process.GetCurrentProcess();
            var sessionGuid = Guid.NewGuid();
            var lease = new EtwSessionLease
            {
                Status = "Creating",
                ProcessId = currentProcess.Id,
                ProcessCreationTimeUtc = currentProcess.StartTime.ToUniversalTime(),
                SessionGuid = sessionGuid,
                SessionName = sessionName,
                ProductId = "StartupMonitor",
                MachineName = Environment.MachineName,
                WrittenAtUtc = DateTime.UtcNow
            };
            WriteLeaseAtomic(lease, logger);

            // 4. Подготавливаем зануленный unmanaged-буфер свойств EVENT_TRACE_PROPERTIES с корректными смещениями
            propertiesBuffer = AllocateStartPropertiesBuffer(sessionName, sessionGuid, out _);

            // 5. Вызываем StartTraceW
            var status = StartTraceW(out var sessionHandle, sessionName, propertiesBuffer);

            if (status != ERROR_SUCCESS)
            {
                // Неуспешный StartTrace не доказывает отсутствия частично созданного ресурса.
                // Handle из неуспешного вызова не используется; право очистки подтверждается nonce.
                Marshal.FreeHGlobal(propertiesBuffer);
                propertiesBuffer = IntPtr.Zero;
                CleanupFailedStartLease(lease, logger);

                logger?.LogError("StartTraceW для сессии '{SessionName}' вернул код ошибки Win32 {Status}", sessionName, status);
                throw new NativeTraceSessionException(status, sessionName, $"Вызов StartTraceW завершился с кодом ошибки {status}");
            }

            // Владение созданным handle фиксируется до любых последующих файловых операций.
            // При отказе записи Active очистка выполняется тем же контроллером, intent-lease сохраняется.
            created = new NativeProcessTraceSession(sessionName, sessionHandle, sessionGuid, lease, propertiesBuffer, logger);
            propertiesBuffer = IntPtr.Zero;

            // 6. Успешный запуск ядра! Переводим lease в статус "Active"
            lease.Status = "Active";
            lease.WrittenAtUtc = DateTime.UtcNow;
            WriteLeaseAtomic(lease, logger);

            logger?.LogInformation(
                "Native ETW сессия '{SessionName}' успешно запущена (Handle: 0x{Handle:X16}, Guid: {Guid}, Режим: SystemTraceProvider)",
                sessionName, sessionHandle, sessionGuid);

            return created;
        }
        catch
        {
            try
            {
                created?.Dispose();
            }
            catch (Exception cleanupError)
            {
                logger?.LogError(cleanupError, "Не удалось подтвердить остановку частично созданной ETW-сессии; lease оставлен для восстановления.");
            }
            throw;
        }
        finally
        {
            if (propertiesBuffer != IntPtr.Zero) Marshal.FreeHGlobal(propertiesBuffer);
            // Mutex принадлежит потоку: он сериализует создание и durable lease, а не весь
            // асинхронный lifetime. После публикации lease повторный Start проверяет живого владельца.
            if (ownsMutex) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    /// <summary>
    /// Безопасно пытается запустить native-сессию ETW без генерации необработанных исключений.
    /// При сбое возвращает false и текстовое описание ошибки (для перехода в режим Graceful Degradation).
    /// </summary>
    /// <param name="session">Созданный контроллер сессии или null при сбое.</param>
    /// <param name="errorMessage">Сообщение об ошибке при неудачном запуске.</param>
    /// <param name="sessionName">Имя сессии.</param>
    /// <param name="logger">Логгер.</param>
    /// <returns>True при успешном создании сессии, иначе False.</returns>
    public static bool TryStart(
        out NativeProcessTraceSession? session,
        out string? errorMessage,
        out int errorCode,
        string sessionName = DefaultSessionName,
        ILogger? logger = null)
    {
        try
        {
            session = Start(sessionName, logger);
            errorMessage = null;
            errorCode = 0;
            return true;
        }
        catch (NativeTraceSessionException nativeEx)
        {
            session = null;
            errorMessage = nativeEx.Message;
            errorCode = unchecked((int)nativeEx.ErrorCode);
            logger?.LogWarning(nativeEx, "Ошибка native ETW сессии '{SessionName}', Win32 {Code}", sessionName, errorCode);
            return false;
        }
        catch (Win32Exception win32Ex)
        {
            session = null;
            errorMessage = win32Ex.Message;
            errorCode = win32Ex.NativeErrorCode;
            logger?.LogWarning(win32Ex, "Попытка запуска native ETW сессии '{SessionName}' завершилась ошибкой Win32: {Message} (Код: {Code})", sessionName, win32Ex.Message, errorCode);
            return false;
        }
        catch (Exception ex)
        {
            session = null;
            errorMessage = ex.Message;
            errorCode = ex.HResult;
            logger?.LogWarning(ex, "Попытка запуска native ETW сессии '{SessionName}' завершилась ошибкой: {Message}", sessionName, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Безопасная попытка запуска native ETW сессии без выброса исключений (перегрузка без кода ошибки).
    /// </summary>
    /// <param name="session">Созданный экземпляр сессии при успехе.</param>
    /// <param name="errorMessage">Сообщение об ошибке при неудачном запуске.</param>
    /// <param name="sessionName">Имя сессии.</param>
    /// <param name="logger">Логгер.</param>
    /// <returns>True при успешном создании сессии, иначе False.</returns>
    public static bool TryStart(
        out NativeProcessTraceSession? session,
        out string? errorMessage,
        string sessionName = DefaultSessionName,
        ILogger? logger = null)
    {
        return TryStart(out session, out errorMessage, out _, sessionName, logger);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Идемпотентно останавливает native-сессию ETW с использованием ControlTraceW(handle, null, properties, STOP).
    /// Не использует TraceEventSession.Stop(), исключая побочные эффекты на глобальный интервал семплирования CPU.
    /// </summary>
    public void Stop()
    {
        lock (_syncRoot)
        {
            if (Volatile.Read(ref _isStopped) != 0) return;

            // Не теряем handle и не помечаем lease остановленным до подтверждения результата.
            // Stop вызывается с любого worker-потока; thread-affine mutex здесь не используется.
            if (_sessionHandle != 0)
            {
                var stopBuffer = AllocateControlPropertiesBuffer(null, out _);
                try
                {
                    var queryStatus = ControlTraceW(_sessionHandle, null, stopBuffer, EVENT_TRACE_CONTROL_QUERY);
                    if (queryStatus != ERROR_WMI_INSTANCE_NOT_FOUND && queryStatus != ERROR_SUCCESS && queryStatus != ERROR_MORE_DATA)
                        throw new NativeTraceSessionException(queryStatus, _sessionName, "Не удалось подтвердить владение перед STOP");

                    if (queryStatus != ERROR_WMI_INSTANCE_NOT_FOUND &&
                        Marshal.PtrToStructure<EVENT_TRACE_PROPERTIES>(stopBuffer).Wnode.Guid != _sessionGuid)
                        throw new InvalidOperationException("Handle ETW больше не соответствует GUID собственной сессии; остановка запрещена.");

                    // Остановка сессии через собственный валидный handle без указания имени (согласно SDK)
                    var status = queryStatus == ERROR_WMI_INSTANCE_NOT_FOUND ? queryStatus :
                        ControlTraceW(_sessionHandle, null, stopBuffer, EVENT_TRACE_CONTROL_STOP);
                    if (status != ERROR_SUCCESS && status != ERROR_MORE_DATA && status != ERROR_WMI_INSTANCE_NOT_FOUND)
                        throw new NativeTraceSessionException(status, _sessionName, "Остановка ETW не подтверждена; lease сохранён");

                    // ERROR_MORE_DATA может означать выполненный STOP; повторный QUERY разрешает
                    // неоднозначность, не повторяя операцию с потенциально переиспользованным handle.
                    if (status == ERROR_MORE_DATA)
                    {
                        var confirmation = ControlTraceW(0, _sessionName, stopBuffer, EVENT_TRACE_CONTROL_QUERY);
                        if (confirmation != ERROR_WMI_INSTANCE_NOT_FOUND &&
                            !((confirmation == ERROR_SUCCESS || confirmation == ERROR_MORE_DATA) &&
                              Marshal.PtrToStructure<EVENT_TRACE_PROPERTIES>(stopBuffer).Wnode.Guid != _sessionGuid))
                            throw new NativeTraceSessionException(confirmation, _sessionName, "STOP вернул MORE_DATA, отсутствие собственной сессии не подтверждено");
                    }
                    _sessionHandle = 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(stopBuffer);
                }
            }

            Volatile.Write(ref _isStopped, 1);
            // Фиксируем статус "Stopped" в lease-файле
            MarkLeaseStopped(_lease, _logger);

            // Освобождаем стартовый unmanaged-буфер свойств
            if (_propertiesBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_propertiesBuffer);
                _propertiesBuffer = IntPtr.Zero;
            }

        }
    }

    /// <summary>
    /// Опрашивает ядро Windows о текущей статистике буферов и потерь событий ETW (ControlTraceW с кодом QUERY).
    /// </summary>
    /// <returns>Статистика сессии или null, если сессия остановлена либо запрос не удался.</returns>
    public NativeTraceStatistics? QueryStatistics()
    {
        if (Volatile.Read(ref _isStopped) != 0 || _sessionHandle == 0)
        {
            return null;
        }

        lock (_syncRoot)
        {
            if (Volatile.Read(ref _isStopped) != 0 || _sessionHandle == 0)
            {
                return null;
            }

            var queryBuffer = AllocateControlPropertiesBuffer(null, out _);
            try
            {
                var status = ControlTraceW(_sessionHandle, null, queryBuffer, EVENT_TRACE_CONTROL_QUERY);
                if (status == ERROR_SUCCESS || status == ERROR_MORE_DATA)
                {
                    var props = Marshal.PtrToStructure<EVENT_TRACE_PROPERTIES>(queryBuffer);
                    return new NativeTraceStatistics(
                        props.NumberOfBuffers,
                        props.FreeBuffers,
                        props.EventsLost,
                        props.BuffersWritten,
                        props.LogBuffersLost,
                        props.RealTimeBuffersLost);
                }

                throw new NativeTraceSessionException(status, _sessionName, "Не удалось получить статистику ETW");
            }
            finally
            {
                Marshal.FreeHGlobal(queryBuffer);
            }
        }
    }

    /// <summary>
    /// Детерминированно освобождает ресурсы native-сессии ETW.
    /// </summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Финализатор для аварийного освобождения неуправляемых ресурсов ядра при пропуске Dispose.
    /// </summary>
    ~NativeProcessTraceSession()
    {
        try
        {
            Stop();
        }
        catch (Exception ex)
        {
            // Исключение финализатора не должно завершать SCM-службу; durable lease оставлен.
            try { _logger?.LogError(ex, "Аварийное освобождение ETW не подтверждено; требуется восстановление по lease."); }
            catch (Exception logError) { Trace.TraceError("Ошибка финализатора ETW: {0}; ошибка логирования: {1}", ex.GetType().Name, logError.GetType().Name); }
        }
    }

    #endregion

    #region Orphan Reconciliation and Mutex Coordination

    /// <summary>Разрешает частичный отказ StartTrace без потери intent-lease и без остановки чужой сессии.</summary>
    /// <param name="lease">Intent с nonce текущей попытки запуска.</param>
    /// <param name="logger">Журнал ошибок очистки.</param>
    private static void CleanupFailedStartLease(EtwSessionLease lease, ILogger? logger)
    {
        var buffer = AllocateControlPropertiesBuffer(lease.SessionName, out _);
        try
        {
            var query = ControlTraceW(0, lease.SessionName, buffer, EVENT_TRACE_CONTROL_QUERY);
            if (query == ERROR_WMI_INSTANCE_NOT_FOUND)
            {
                MarkLeaseStopped(lease, logger);
                return;
            }
            if ((query == ERROR_SUCCESS || query == ERROR_MORE_DATA) &&
                Marshal.PtrToStructure<EVENT_TRACE_PROPERTIES>(buffer).Wnode.Guid == lease.SessionGuid)
            {
                var stop = ControlTraceW(0, lease.SessionName, buffer, EVENT_TRACE_CONTROL_STOP);
                if (stop == ERROR_SUCCESS || stop == ERROR_WMI_INSTANCE_NOT_FOUND)
                {
                    MarkLeaseStopped(lease, logger);
                    return;
                }
                logger?.LogWarning("Частичный StartTrace не очищен, STOP {Code}; intent-lease сохранён", stop);
            }
            else
            {
                logger?.LogWarning("Очистка после StartTrace не подтверждена, QUERY {Code}; чужая сессия не изменена, intent сохранён", query);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>
    /// Захватывает машинный мьютекс с обработкой аварийного оставления мьютекса предыдущим процессом.
    /// </summary>
    private static Mutex AcquireMachineMutex(TimeSpan timeout, out bool ownsMutex, ILogger? logger, string sessionName)
    {
        var mutexName = sessionName == IncomingTcpSessionName ? IncomingTcpMutexName : MachineMutexName;
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, mutexName);
        }
        catch (UnauthorizedAccessException)
        {
            mutex = Mutex.OpenExisting(mutexName);
        }

        try
        {
            ownsMutex = mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException ex)
        {
            logger?.LogWarning(ex, "Машинный мьютекс ETW был аварийно оставлен предыдущим владельцем (AbandonedMutexException)");
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            mutex.Dispose();
            throw new TimeoutException($"Не удалось захватить машинный мьютекс '{mutexName}' за {timeout.TotalSeconds:F1} с.");
        }

        return mutex;
    }

    /// <summary>
    /// Выполняет строгую проверку и остановку ТОЛЬКО подтвержденной orphan-сессии от нашего продукта.
    /// Чужие сессии никогда не останавливаются.
    /// </summary>
    private static void ReconcileOrphanSessionIfNeeded(string sessionName, ILogger? logger)
    {
        var existingLease = ReadLease(sessionName, logger);
        if (existingLease == null)
        {
            logger?.LogDebug("Lease-файл ETW отсутствует, проверка orphan-сессий не требуется");
            return;
        }

        // Проверяем принадлежность lease-файла нашему продукту
        if (!string.Equals(existingLease.ProductId, "StartupMonitor", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Lease-файл принадлежит стороннему продукту '{ProductId}', очистка пропущена", existingLease.ProductId);
            throw new InvalidOperationException("Не удалось подтвердить продукт владельца ETW lease.");
        }


        if (!string.Equals(existingLease.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existingLease.SessionName, sessionName, StringComparison.Ordinal) ||
            existingLease.SessionGuid == Guid.Empty)
            throw new InvalidOperationException("Lease не соответствует компьютеру, имени или GUID сессии; изменение запрещено.");

        // Проверяем, жив ли процесс владельца
        if (existingLease.Status != "Stopped" && existingLease.ProcessId > 0 &&
            !(existingLease.Status == "Creating" && existingLease.ProcessId == Environment.ProcessId) &&
            IsOwnerProcessAlive(existingLease.ProcessId, existingLease.ProcessCreationTimeUtc))
        {
                logger?.LogWarning(
                    "Владелец сессии ETW (PID {Pid}, запущен {StartTimeUtc}) всё ещё активен. Сессия не является orphan",
                    existingLease.ProcessId, existingLease.ProcessCreationTimeUtc);

                throw new InvalidOperationException(
                    $"Сессия ETW '{sessionName}' уже принадлежит работающему экземпляру StartupMonitor (PID: {existingLease.ProcessId}).");
        }

        // Владелец процесса завершился или аварийно упал. Опрашиваем состояние нативной сессии ядра через QUERY.
        logger?.LogInformation("Обнаружен потенциальный orphan lease от завершённого PID {Pid}. Проверка нативной сессии ядра...", existingLease.ProcessId);

        var queryBuffer = AllocateControlPropertiesBuffer(sessionName, out _);
        try
        {
            // Опрос нативной сессии по имени (sessionHandle = 0)
            var status = ControlTraceW(0, sessionName, queryBuffer, EVENT_TRACE_CONTROL_QUERY);
            if (status == ERROR_WMI_INSTANCE_NOT_FOUND)
            {
                logger?.LogInformation("Нативная сессия '{SessionName}' уже отсутствует в ядре Windows. Удаление устаревшего lease-файла", sessionName);
                DeleteLeaseFile(sessionName, logger);
                return;
            }

            if (status == ERROR_SUCCESS || status == ERROR_MORE_DATA)
            {
                var props = Marshal.PtrToStructure<EVENT_TRACE_PROPERTIES>(queryBuffer);
                var kernelGuid = props.Wnode.Guid;

                // КРИТИЧЕСКОЕ ТРЕБОВАНИЕ БЕЗОПАСНОСТИ:
                // Останавливать разрешено ТОЛЬКО сессию, чей GUID в ядре строго совпадает с GUID из нашего lease!
                if (kernelGuid == existingLease.SessionGuid && existingLease.SessionGuid != Guid.Empty)
                {
                    logger?.LogWarning(
                        "Подтверждена собственная orphan-сессия ETW (SessionGuid: {Guid}, бывший PID: {Pid}). Остановка сессии...",
                        kernelGuid, existingLease.ProcessId);

                    var stopBuffer = AllocateControlPropertiesBuffer(sessionName, out _);
                    try
                    {
                        var stopStatus = ControlTraceW(0, sessionName, stopBuffer, EVENT_TRACE_CONTROL_STOP);
                        if (stopStatus == ERROR_MORE_DATA)
                        {
                            var confirmation = ControlTraceW(0, sessionName, stopBuffer, EVENT_TRACE_CONTROL_QUERY);
                            if (confirmation != ERROR_WMI_INSTANCE_NOT_FOUND)
                                throw new NativeTraceSessionException(confirmation, sessionName, "Отсутствие orphan-сессии после MORE_DATA не подтверждено; lease сохранён");
                        }
                        if (stopStatus == ERROR_SUCCESS || stopStatus == ERROR_MORE_DATA || stopStatus == ERROR_WMI_INSTANCE_NOT_FOUND)
                        {
                            logger?.LogInformation("Собственная orphan-сессия ETW успешно остановлена (статус {Status})", stopStatus);
                            DeleteLeaseFile(sessionName, logger);
                        }
                        else
                        {
                            throw new NativeTraceSessionException(stopStatus, sessionName, "Не удалось остановить orphan-сессию; lease сохранён");
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(stopBuffer);
                    }
                }
                else
                {
                    logger?.LogError(
                        "Найдена активная сессия ядра '{SessionName}', но её GUID ({KernelGuid}) НЕ совпадает с lease ({LeaseGuid}). ЧУЖАЯ СЕССИЯ! Остановка строго запрещена!",
                        sessionName, kernelGuid, existingLease.SessionGuid);

                    throw new InvalidOperationException(
                        $"Сессия ETW '{sessionName}' занята сторонней подсистемой ядра (GUID ядра: {kernelGuid}). Остановка запрещена ради стабильности Windows.");
                }
            }
            else
            {
                throw new NativeTraceSessionException(status, sessionName, "Не удалось проверить orphan-сессию; lease сохранён");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(queryBuffer);
        }
    }

    /// <summary>
    /// Проверяет, жив ли процесс с указанным идентификатором и совпадает ли время его старта.
    /// Защищает от ложного опознания чужого процесса при повторном использовании PID операционной системой.
    /// </summary>
    private static bool IsOwnerProcessAlive(int processId, DateTime expectedStartTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            var actualStartTimeUtc = process.StartTime.ToUniversalTime();
            return actualStartTimeUtc == expectedStartTimeUtc;
        }
        catch (ArgumentException)
        {
            // Процесс с таким PID отсутствует в системе
            return false;
        }
        catch (InvalidOperationException)
        {
            // Процесс завершился в процессе опроса
            return false;
        }
        catch (Win32Exception)
        {
            // Отказ доступа не доказывает смерть владельца: безопасно считаем его живым.
            return true;
        }
    }

    #endregion

    #region Lease Storage Methods

    /// <summary>
    /// Возвращает путь к lease-файлу в %ProgramData%\StartupMonitor\etw_session.lease.
    /// </summary>
    private static string GetLeaseFilePath(string sessionName)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.Combine(programData, "StartupMonitor");
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return Path.Combine(directory, sessionName == IncomingTcpSessionName ? "etw_incoming_tcp.lease" : "etw_session.lease");
    }

    /// <summary>
    /// Атомарно записывает lease-файл через временный файл в том же каталоге с последующей заменой.
    /// </summary>
    private static void WriteLeaseAtomic(EtwSessionLease lease, ILogger? logger)
    {
        var filePath = GetLeaseFilePath(lease.SessionName);
        var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(lease, new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Ошибка атомарной записи lease-файла ETW: {FilePath}", filePath);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception cleanupError) { logger?.LogWarning(cleanupError, "Не удалось удалить временный lease {FilePath}", tempPath); }
            throw;
        }
    }

    /// <summary>
    /// Читает текущий lease-файл с диска. Возвращает null при отсутствии или повреждении файла.
    /// </summary>
    private static EtwSessionLease? ReadLease(string sessionName, ILogger? logger)
    {
        var filePath = GetLeaseFilePath(sessionName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<EtwSessionLease>(json);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Ошибка чтения или повреждение lease-файла ETW: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Обновляет статус lease-файла на "Stopped" при завершении сессии.
    /// </summary>
    private static void MarkLeaseStopped(EtwSessionLease lease, ILogger? logger)
    {
        try
        {
            lease.Status = "Stopped";
            lease.WrittenAtUtc = DateTime.UtcNow;
            WriteLeaseAtomic(lease, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось обновить статус lease-файла на Stopped");
        }
    }

    /// <summary>
    /// Удаляет файл lease при подтвержденной очистке orphan-сессии.
    /// </summary>
    private static void DeleteLeaseFile(string sessionName, ILogger? logger)
    {
        var filePath = GetLeaseFilePath(sessionName);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось удалить устаревший lease-файл: {FilePath}", filePath);
        }
    }

    #endregion

    #region Unmanaged Memory Allocation Helpers

    /// <summary>
    /// Выделяет и инициализирует буфер EVENT_TRACE_PROPERTIES для вызова StartTraceW.
    /// Настраивает режим SystemTraceProvider, флаги процессов и точные смещения строк.
    /// </summary>
    private static IntPtr AllocateStartPropertiesBuffer(string sessionName, Guid sessionGuid, out int totalSize)
    {
        var headerSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>(); // ровно 120 байт на x64
        totalSize = PropertiesBufferSize;
        var buffer = Marshal.AllocHGlobal(totalSize);

        // Гарантированное зануление памяти структуры
        var zeroes = new byte[totalSize];
        Marshal.Copy(zeroes, 0, buffer, totalSize);

        var props = new EVENT_TRACE_PROPERTIES
        {
            Wnode = new WNODE_HEADER
            {
                BufferSize = (uint)totalSize,
                Flags = WNODE_FLAG_TRACED_GUID,
                ClientContext = 1, // QueryPerformanceCounter (QPC)
                Guid = sessionGuid
            },
            BufferSize = 64, // 64 КБ на один кольцевой буфер ядра
            MinimumBuffers = 4,
            MaximumBuffers = 64,
            MaximumFileSize = 0,
            LogFileMode = EVENT_TRACE_SYSTEM_LOGGER_MODE | EVENT_TRACE_REAL_TIME_MODE,
            FlushTimer = 1, // сброс буферов каждую секунду
            EnableFlags = sessionName == IncomingTcpSessionName ? EVENT_TRACE_FLAG_NETWORK_TCPIP : EVENT_TRACE_FLAG_PROCESS,
            AgeLimit = 0,
            LogFileNameOffset = 0, // Real-time режим без дискового файла
            LoggerNameOffset = (uint)headerSize
        };

        Marshal.StructureToPtr(props, buffer, false);

        // Записываем Unicode-строку имени сессии сразу после заголовка свойств
        var nameBytes = Encoding.Unicode.GetBytes(sessionName + "\0");
        Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, headerSize), nameBytes.Length);

        return buffer;
    }

    /// <summary>
    /// Выделяет и инициализирует буфер EVENT_TRACE_PROPERTIES для вызовов ControlTraceW (QUERY / STOP).
    /// </summary>
    private static IntPtr AllocateControlPropertiesBuffer(string? sessionName, out int totalSize)
    {
        var headerSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
        totalSize = PropertiesBufferSize;
        var buffer = Marshal.AllocHGlobal(totalSize);

        var zeroes = new byte[totalSize];
        Marshal.Copy(zeroes, 0, buffer, totalSize);

        var props = new EVENT_TRACE_PROPERTIES
        {
            Wnode = new WNODE_HEADER
            {
                BufferSize = (uint)totalSize,
                Flags = WNODE_FLAG_TRACED_GUID
            },
            LoggerNameOffset = (uint)headerSize,
            LogFileNameOffset = 0
        };

        Marshal.StructureToPtr(props, buffer, false);

        if (!string.IsNullOrEmpty(sessionName))
        {
            var nameBytes = Encoding.Unicode.GetBytes(sessionName + "\0");
            Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, headerSize), nameBytes.Length);
        }

        return buffer;
    }

    #endregion
}
