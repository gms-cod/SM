using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Pipe;

/// <summary>
/// Сервер событийного named pipe ("StartupMonitorEvents") для широковещательной
/// рассылки уведомлений UI-клиентам. Подписывается на MessagesBatchReady от
/// NotificationService и отправляет батчи всем подключённым клиентам.
/// </summary>
public class EventPipeServer : BackgroundService
{
    // Имя событийного канала (отдельное от основного командно-ответного)
    private readonly string _eventPipeName;
    // Сервис уведомлений, отсюда приходят батчи сообщений
    private readonly NotificationService _notificationService;
    // Логгер
    private readonly ILogger<EventPipeServer> _logger;
    /// <summary>
    /// Обёртка клиента; единственный worker рассылки обеспечивает последовательный доступ к writer.
    /// </summary>
    private sealed class ConnectedClient : IDisposable
    {
        public int Id { get; }
        public NamedPipeServerStream Pipe { get; }
        public StreamWriter Writer { get; }
        private readonly ILogger _logger;

        public ConnectedClient(int id, NamedPipeServerStream pipe, StreamWriter writer, ILogger logger)
        {
            _logger = logger;
            Id = id;
            Pipe = pipe;
            Writer = writer;
        }

        public void Dispose()
        {
            // Сначала закрывается pipe: синхронный Dispose writer не должен ждать клиента, переставшего читать.
            Pipe.Dispose();
            try { Writer.Dispose(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            { _logger.LogDebug(ex, "EventPipeServer: writer закрыт после разрыва транспорта"); }
        }
    }

    // Словарь подключённых клиентов: ID → ConnectedClient
    private readonly ConcurrentDictionary<int, ConnectedClient> _clients = new();
    private static int _nextClientId;
    /// <summary>Ограниченная очередь исключает рост незавершённых рассылок при медленных клиентах.</summary>
    private readonly Channel<List<PipeMessage>> _batches = Channel.CreateBounded<List<PipeMessage>>(new BoundedChannelOptions(128)
    {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
    });
    // Настройки JSON-сериализации
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Конструктор EventPipeServer.
    /// </summary>
    /// <param name="notificationService">Сервис уведомлений для подписки на батчи</param>
    /// <param name="logger">Логгер</param>
    public EventPipeServer(
        NotificationService notificationService,
        ILogger<EventPipeServer> logger,
        string pipeName = "StartupMonitorEvents")
    {
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _notificationService = notificationService;
        _logger = logger;
        _eventPipeName = pipeName;
    }

    /// <summary>Количество живых подключений для диагностики жизненного цикла сервера.</summary>
    internal int ConnectedClientCount => _clients.Count;

    /// <summary>Общее число принятых подключений за время работы сервера для детерминированной проверки в тестах.</summary>
    internal int TotalAcceptedClients => Volatile.Read(ref _nextClientId);

    /// <summary>
    /// Основной цикл: подписывается на уведомления, слушает входящие соединения
    /// от UI-клиентов на событийном канале. Клиенты не отправляют данные —
    /// только получают уведомления (PipeDirection.Out).
    /// </summary>
    /// <param name="stoppingToken">Токен отмены остановки службы</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _notificationService.MessagesBatchReady += OnMessagesBatchReady;
        var broadcast = RunBroadcastLoopAsync(lifetime.Token);
        var heartbeat = RunHeartbeatAsync(lifetime.Token);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pending = null;
                try
                {
                    pending = NamedPipeServerStreamAcl.Create(_eventPipeName, PipeDirection.Out, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous, 0, 65536, PipeSecurityFactory.CreateDefault());
                    await pending.WaitForConnectionAsync(stoppingToken);
                    var id = Interlocked.Increment(ref _nextClientId);
                    _clients[id] = new ConnectedClient(id, pending, new StreamWriter(pending) { AutoFlush = true }, _logger);
                    pending = null;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "EventPipeServer: соединение временно недоступно");
                    try { await Task.Delay(1000, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                }
                finally { pending?.Dispose(); }
            }
        }
        finally
        {
            _notificationService.MessagesBatchReady -= OnMessagesBatchReady;
            _batches.Writer.TryComplete();
            lifetime.Cancel();
            await Task.WhenAll(broadcast, heartbeat);
            foreach (var id in _clients.Keys) RemoveClient(id);
        }
    }

    /// <summary>Последовательная рассылка сохраняет порядок батчей без semaphore и задач на каждое событие.</summary>
    /// <param name="ct">Токен остановки серверных обработчиков.</param>
    /// <returns>Задача жизненного цикла рассылки.</returns>
    private async Task RunBroadcastLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var batch in _batches.Reader.ReadAllAsync(ct))
                await BroadcastBatchAsync(batch, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { _logger.LogDebug("EventPipeServer: рассылка завершена отменой службы"); }
    }

    /// <summary>Периодически проверяет живучесть клиентов через ту же последовательную очередь.</summary>
    /// <param name="ct">Токен завершения службы.</param>
    /// <returns>Задача жизненного цикла heartbeat.</returns>
    private async Task RunHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(ct)) OnMessagesBatchReady(this, []);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { _logger.LogDebug("EventPipeServer: heartbeat завершён отменой службы"); }
    }

    /// <summary>
    /// Удаляет клиента из словаря и освобождает связанные ресурсы.
    /// </summary>
    private void RemoveClient(int id)
    {
        if (_clients.TryRemove(id, out var client))
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// Обработчик события MessagesBatchReady: асинхронно рассылает батч всем клиентам
    /// без блокировки потока таймера NotificationService (защита от ThreadPool Starvation).
    /// </summary>
    private void OnMessagesBatchReady(object? sender, List<PipeMessage> batch)
    {
        if (!_clients.IsEmpty && !_batches.Writer.TryWrite(batch))
            _logger.LogWarning("EventPipeServer: очередь уведомлений заполнена; медленные клиенты не успевают читать события");
    }

    /// <summary>
    /// Неблокирующая параллельная широковещательная рассылка батча клиентам с жестким таймаутом 2 секунды.
    /// </summary>
    private async Task BroadcastBatchAsync(List<PipeMessage> batch, CancellationToken stoppingToken)
    {
        if (_clients.IsEmpty) return;

        string json;
        try
        {
            json = JsonSerializer.Serialize(batch, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сериализации батча уведомлений");
            return;
        }

        if (json.Length > BoundedLineReader.MaximumFrameCharacters)
        {
            _logger.LogWarning("EventPipeServer: батч превышает допустимый размер кадра");
            return;
        }
        var dead = new List<int>();

        // Рассылаем параллельно всем подключенным клиентам с таймаутом на каждого
        var sendTasks = _clients.Select(async kvp =>
        {
            var id = kvp.Key;
            var client = kvp.Value;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await client.Writer.WriteLineAsync(json.AsMemory(), cts.Token);
                await client.Writer.FlushAsync(cts.Token);
                return (id, true);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                _logger.LogDebug(ex, "EventPipeServer: клиент {Id} отключён или перестал читать", id);
                return (id, false);
            }
        });

        var results = await Task.WhenAll(sendTasks);
        foreach (var (id, success) in results)
        {
            if (!success) dead.Add(id);
        }

        // Мёртвых и зависших клиентов удаляем и освобождаем ресурсы pipe
        foreach (var id in dead)
        {
            RemoveClient(id);
        }
    }
}
