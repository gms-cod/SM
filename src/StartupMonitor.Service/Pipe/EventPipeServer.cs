using System.Collections.Concurrent;
using System.IO.Pipes;
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
    private const string EventPipeName = "StartupMonitorEvents";
    // Сервис уведомлений, отсюда приходят батчи сообщений
    private readonly NotificationService _notificationService;
    // Логгер
    private readonly ILogger<EventPipeServer> _logger;
    /// <summary>
    /// Обертка подключенного клиента с собственным асинхронным семафором для исключения блокировок потоков ОС.
    /// </summary>
    private sealed class ConnectedClient : IDisposable
    {
        public int Id { get; }
        public NamedPipeServerStream Pipe { get; }
        public StreamWriter Writer { get; }
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public ConnectedClient(int id, NamedPipeServerStream pipe, StreamWriter writer)
        {
            Id = id;
            Pipe = pipe;
            Writer = writer;
        }

        public void Dispose()
        {
            try { Writer.Dispose(); } catch { }
            try { Pipe.Dispose(); } catch { }
            try { Lock.Dispose(); } catch { }
        }
    }

    // Словарь подключённых клиентов: ID → ConnectedClient
    private readonly ConcurrentDictionary<int, ConnectedClient> _clients = new();
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
        ILogger<EventPipeServer> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Основной цикл: подписывается на уведомления, слушает входящие соединения
    /// от UI-клиентов на событийном канале. Клиенты не отправляют данные —
    /// только получают уведомления (PipeDirection.Out).
    /// </summary>
    /// <param name="stoppingToken">Токен отмены остановки службы</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Подписываемся на батчи уведомлений
        _notificationService.MessagesBatchReady += OnMessagesBatchReady;
        _logger.LogInformation("EventPipeServer запущен");
        StartHeartbeat(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Создаём pipe только на запись (out) — клиенты только слушают (буфер 64 КБ)
                var pipe = NamedPipeServerStreamAcl.Create(
                    EventPipeName,
                    PipeDirection.Out,
                    maxNumberOfServerInstances: 10,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 65536,
                    PipeSecurityFactory.CreateDefault());

                await pipe.WaitForConnectionAsync(stoppingToken);
                _logger.LogInformation("Event-клиент подключился");

                var writer = new StreamWriter(pipe) { AutoFlush = true };
                var id = pipe.GetHashCode();
                var client = new ConnectedClient(id, pipe, writer);
                _clients[id] = client;

                // Фоновая задача отслеживает отмену службы для освобождения ресурсов
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(Timeout.Infinite, stoppingToken); }
                    catch (OperationCanceledException) { }
                    RemoveClient(id);
                }, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException ex)
            {
                // Все инстансы заняты (клиенты не успели отключиться) — не спамим Error
                _logger.LogDebug(ex, "EventPipeServer: канал временно занят");
                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка EventPipeServer");
                await Task.Delay(1000, stoppingToken);
            }
        }

        // Отписываемся при остановке службы
        _notificationService.MessagesBatchReady -= OnMessagesBatchReady;
    }

    /// <summary>
    /// Периодическая проверка живучести клиентов: неблокирующая отправка пустого батча.
    /// </summary>
    private void StartHeartbeat(CancellationToken stoppingToken)
    {
        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) { break; }
                OnMessagesBatchReady(this, []);
            }
        }, stoppingToken);
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
        _ = BroadcastBatchAsync(batch);
    }

    /// <summary>
    /// Неблокирующая параллельная широковещательная рассылка батча клиентам с жестким таймаутом 2 секунды.
    /// </summary>
    private async Task BroadcastBatchAsync(List<PipeMessage> batch)
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

        var dead = new List<int>();

        // Рассылаем параллельно всем подключенным клиентам с таймаутом на каждого
        var sendTasks = _clients.Select(async kvp =>
        {
            var id = kvp.Key;
            var client = kvp.Value;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                // Защита от одновременной записи в один стрим с таймаутом ожидания
                if (!await client.Lock.WaitAsync(TimeSpan.FromSeconds(2), cts.Token))
                {
                    _logger.LogWarning("Event-клиент {Id} не освободил лок записи за 2 сек — клиент завис", id);
                    return (id, false);
                }

                try
                {
                    await client.Writer.WriteLineAsync(json.AsMemory(), cts.Token);
                    await client.Writer.FlushAsync(cts.Token);
                    return (id, true);
                }
                finally
                {
                    client.Lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка отправки батча клиенту {Id} (клиент отключен или завис)", id);
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
