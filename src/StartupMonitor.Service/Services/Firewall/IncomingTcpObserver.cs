using System.Net;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.ProcessLifecycle;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>Наблюдает входящие TCP Accept в отдельной ETW-сессии без управления сетевой политикой.</summary>
public sealed class IncomingTcpObserver : BackgroundService
{
    private readonly JsonDataStore _store;
    private readonly NotificationService _notifications;
    private readonly ILogger<IncomingTcpObserver> _logger;
    private readonly object _rateLock = new();
    private DateTime _lastNotificationUtc;
    private long _suppressed;
    private volatile bool _enabled;

    /// <summary>Создаёт наблюдателя с отдельным сохранённым переключателем.</summary>
    /// <param name="store">Хранилище настроек службы.</param>
    /// <param name="notifications">Ограниченная очередь уведомлений UI.</param>
    /// <param name="logger">Журнал отказов и пропущенных событий.</param>
    public IncomingTcpObserver(JsonDataStore store, NotificationService notifications, ILogger<IncomingTcpObserver> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Запускает наблюдение только при включённой настройке и повторяет запуск после ошибки.</summary>
    /// <param name="stoppingToken">Токен завершения службы.</param>
    /// <returns>Задача жизненного цикла наблюдателя.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { _enabled = _store.GetSettings().IncomingTcpObservationsEnabled; }
                catch (Exception ex)
                {
                    _enabled = false;
                    _logger.LogWarning(ex, "IncomingTCP: настройка недоступна; наблюдение приостановлено");
                }
                if (_enabled)
                {
                    try { await RunSessionAsync(stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { _logger.LogWarning(ex, "IncomingTCP: наблюдение недоступно, события могут быть пропущены"); }
                }
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("IncomingTCP: наблюдение остановлено");
        }
    }

    /// <summary>Управляет исключительно собственной сессией и её читателем ETW.</summary>
    /// <param name="ct">Токен остановки службы.</param>
    /// <returns>Задача завершения чтения.</returns>
    private async Task RunSessionAsync(CancellationToken ct)
    {
        using var session = NativeProcessTraceSession.Start(NativeProcessTraceSession.IncomingTcpSessionName, _logger);
        using var source = new ETWTraceEventSource(session.Name, TraceEventSourceType.Session);
        // Разбор payload находится внутри защиты: повреждённое событие не завершает reader.
        Action<TcpIpConnectTraceData> onV4 = e =>
        {
            try { Publish(e.ProcessID, e.saddr, e.sport, e.daddr, e.dport, e.TimeStamp); }
            catch (Exception ex) { _logger.LogWarning(ex, "IncomingTCP: повреждённое событие IPv4"); }
        };
        Action<TcpIpV6ConnectTraceData> onV6 = e =>
        {
            try { Publish(e.ProcessID, e.saddr, e.sport, e.daddr, e.dport, e.TimeStamp); }
            catch (Exception ex) { _logger.LogWarning(ex, "IncomingTCP: повреждённое событие IPv6"); }
        };
        source.Kernel.TcpIpAccept += onV4;
        source.Kernel.TcpIpAcceptIPV6 += onV6;
        var reader = Task.Run(() => source.Process(), CancellationToken.None);
        try
        {
            while (!ct.IsCancellationRequested && _enabled && !reader.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                _enabled = _store.GetSettings().IncomingTcpObservationsEnabled;
            }
            if (reader.IsCompleted && !ct.IsCancellationRequested && _enabled)
            {
                await reader.ConfigureAwait(false);
                throw new IOException("Чтение событий входящего TCP неожиданно завершилось.");
            }
        }
        finally
        {
            try
            {
                var stats = session.QueryStatistics();
                if (stats is not null && (stats.EventsLost > 0 || stats.RealTimeBuffersLost > 0))
                    _logger.LogWarning("IncomingTCP: ETW потерял {Events} событий и {Buffers} буферов", stats.EventsLost, stats.RealTimeBuffersLost);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "IncomingTCP: статистика потерь ETW недоступна"); }
            source.Kernel.TcpIpAccept -= onV4;
            source.Kernel.TcpIpAcceptIPV6 -= onV6;
            try { source.StopProcessing(); }
            catch (Exception ex) { _logger.LogWarning(ex, "IncomingTCP: ошибка остановки чтения ETW"); }
            try { session.Stop(); }
            catch (Exception ex) { _logger.LogWarning(ex, "IncomingTCP: остановка ETW не подтверждена; lease сохранён"); }
            try { await reader.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                _logger.LogWarning("IncomingTCP: читатель ETW не завершился за 5 секунд");
                _ = reader.ContinueWith(t => _logger.LogWarning(t.Exception, "IncomingTCP: поздняя ошибка читателя ETW"),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            var suppressed = Interlocked.Exchange(ref _suppressed, 0);
            if (suppressed > 0) _logger.LogInformation("IncomingTCP: ограничение частоты скрыло {Count} событий", suppressed);
        }
    }

    /// <summary>Передаёт наружное соединение в очередь с ограничением частоты; частные сети исключены.</summary>
    /// <param name="pid">PID, указанный источником события.</param>
    /// <param name="local">Локальный адрес.</param>
    /// <param name="localPort">Локальный порт.</param>
    /// <param name="remote">Удалённый адрес.</param>
    /// <param name="remotePort">Удалённый порт.</param>
    /// <param name="timestamp">Время события.</param>
    private void Publish(int pid, IPAddress local, int localPort, IPAddress remote, int remotePort, DateTime timestamp)
    {
        try
        {
            if (!_enabled || local is null || remote is null || localPort is < 1 or > 65535 || remotePort is < 1 or > 65535 ||
                IsInternalAddress(remote)) return;
            lock (_rateLock)
            {
                var now = DateTime.UtcNow;
                if (now - _lastNotificationUtc < TimeSpan.FromSeconds(3)) { Interlocked.Increment(ref _suppressed); return; }
                _lastNotificationUtc = now;
            }
            var observation = new IncomingTcpObservation(pid, local.ToString(), localPort, remote.ToString(), remotePort, timestamp.ToUniversalTime());
            _notifications.Enqueue(new PipeMessage { Type = PipeMessageTypes.IncomingTcpObserved, Payload = JsonSerializer.Serialize(observation) });
        }
        catch (Exception ex) { _logger.LogWarning(ex, "IncomingTCP: ошибка обработки события Accept"); }
    }

    /// <summary>Исключает локальные адреса, включая IPv6 ULA и IPv4-отображения.</summary>
    /// <param name="address">Адрес удалённой стороны.</param>
    /// <returns>Признак внутреннего или неопределённого адреса.</returns>
    internal static bool IsInternalAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
            (bytes[0] & 0xfe) == 0xfc) return true;
        return ProcessNetworkSocket.CheckIsPrivateAddress(address.ToString());
    }
}
