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
    // Словарь подключённых клиентов: ID → StreamWriter
    private readonly ConcurrentDictionary<int, StreamWriter> _clients = new();
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
                // Создаём pipe только на запись (out) — клиенты только слушают
                var pipe = NamedPipeServerStreamAcl.Create(
                    EventPipeName,
                    PipeDirection.Out,
                    maxNumberOfServerInstances: 10,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    PipeSecurityFactory.CreateDefault());

                await pipe.WaitForConnectionAsync(stoppingToken);
                _logger.LogInformation("Event-клиент подключился");

                var writer = new StreamWriter(pipe) { AutoFlush = true };
                var id = pipe.GetHashCode();
                _clients[id] = writer;

                // Фоновая задача отслеживает отключение клиента (его невозможно
                // детектировать напрямую на out-pipe — только при попытке записи).
                // Heartbeat-записи (см. HeartbeatLoop) переводят мёртвого клиента
                // в dead и RemoveClient освобождает его слот.
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(-1, stoppingToken); }
                    catch (OperationCanceledException) { }
                    RemoveClient(pipe, id);
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
    /// Периодическая проверка живучести клиентов: запись пустого батча мёртвому
    /// клиенту бросает IOException — клиент попадает в dead и освобождает слот
    /// канала. Без этого отключившиеся клиенты занимают инстансы навсегда,
    /// и после 10 подключений сервер перестаёт принимать новых.
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
    /// Удаляет клиента из словаря и освобождает pipe.
    /// </summary>
    /// <param name="pipe">Экземпляр pipe для освобождения</param>
    /// <param name="id">Идентификатор клиента</param>
    private void RemoveClient(NamedPipeServerStream pipe, int id)
    {
        _clients.TryRemove(id, out _);
        try { pipe.Dispose(); } catch { }
    }

    /// <summary>
    /// Обработчик события MessagesBatchReady. Сериализует батч в JSON
    /// и отправляет всем подключённым клиентам. Мёртвые клиенты удаляются.
    /// </summary>
    /// <param name="sender">Источник события (NotificationService)</param>
    /// <param name="batch">Список сообщений для рассылки</param>
    private void OnMessagesBatchReady(object? sender, List<PipeMessage> batch)
    {
        var json = JsonSerializer.Serialize(batch, JsonOptions);
        var dead = new List<int>();

        // Рассылаем всем живым клиентам
        foreach (var (id, writer) in _clients)
        {
            try
            {
                lock (writer)
                {
                    writer.WriteLine(json);
                    writer.Flush();
                }
            }
            catch
            {
                dead.Add(id);
            }
        }

        // Мёртвого клиента удаляем и освобождаем pipe
        foreach (var id in dead)
        {
            if (_clients.TryRemove(id, out var deadWriter))
                try { deadWriter.Dispose(); } catch { }
        }
    }
}
