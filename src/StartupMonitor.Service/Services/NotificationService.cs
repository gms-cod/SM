using System.Collections.Concurrent;
using System.Text.Json;
using StartupMonitor.Service.Services.Remote;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Сервис уведомлений с объединением сообщений в батчи (coalescing).
/// Пока идёт начальный скан — уведомления EntryAdded подавляются.
/// Батч отправляется раз в 200 мс через событие MessagesBatchReady.
/// Также отправляет удалённые уведомления в Telegram / Discord после завершения скана.</summary>
public class NotificationService : IDisposable
{
    /// <summary>Потокобезопасная очередь сообщений для отправки в UI.</summary>
    private readonly ConcurrentQueue<PipeMessage> _messageQueue = new();
    /// <summary>Таймер для периодического сброса батча.</summary>
    private readonly Timer _timer;
    /// <summary>Интервал объединения сообщений в батч.</summary>
    private readonly TimeSpan _coalesceInterval = TimeSpan.FromMilliseconds(200);
    /// <summary>Трекер начального скана (подавление EntryAdded до завершения).</summary>
    private readonly InitialScanTracker _tracker;
    /// <summary>Сервис удалённых push-оповещений в Telegram/Discord.</summary>
    private readonly RemoteNotificationService? _remoteNotifications;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Событие: готов батч сообщений для отправки через pipe.</summary>
    public event EventHandler<List<PipeMessage>>? MessagesBatchReady;

    /// <summary>NotificationService: Выполняет операцию NotificationService.</summary>
    /// <param name="tracker">Входные данные: значение параметра tracker (InitialScanTracker).</param>
    /// <param name="remoteNotifications">Входные данные: значение параметра remoteNotifications (RemoteNotificationService?).</param>
    public NotificationService(InitialScanTracker tracker, RemoteNotificationService? remoteNotifications = null)
    {
        _tracker = tracker;
        _remoteNotifications = remoteNotifications;
        _timer = new Timer(FlushBatch, null, _coalesceInterval, _coalesceInterval);
    }

    /// <summary>Добавляет сообщение в очередь. EntryAdded подавляются до завершения скана.</summary>
    public void Enqueue(PipeMessage message)
    {
        if (!_tracker.IsCompleted && message.Type == PipeMessageTypes.EntryAdded)
            return;

        _messageQueue.Enqueue(message);

        // Отправка удалённых оповещений (только после завершения начального сканирования)
        if (_tracker.IsCompleted && _remoteNotifications != null)
        {
            DispatchRemoteNotification(message);
        }
    }

    /// <summary>DispatchRemoteNotification: Выполняет операцию DispatchRemoteNotification.</summary>
    /// <param name="message">Входные данные: значение параметра message (PipeMessage).</param>
    private void DispatchRemoteNotification(PipeMessage message)
    {
        try
        {
            if (message.Type == PipeMessageTypes.EntryAdded && !string.IsNullOrEmpty(message.Payload))
            {
                var entry = JsonSerializer.Deserialize<StartupEntry>(message.Payload, JsonOptions);
                if (entry != null)
                {
                    if (entry.ThreatInfo?.HasThreat == true)
                        _ = _remoteNotifications!.NotifyThreatAsync(entry, entry.ThreatInfo);
                    else
                        _ = _remoteNotifications!.NotifyNewEntryAsync(entry);
                }
            }
            else if (message.Type == PipeMessageTypes.BlacklistMatch && !string.IsNullOrEmpty(message.Payload))
            {
                var entry = JsonSerializer.Deserialize<StartupEntry>(message.Payload, JsonOptions);
                if (entry != null)
                {
                    _ = _remoteNotifications!.NotifyBlockedAsync(entry, "Сработало правило Чёрного списка");
                }
            }
            else if (message.Type == PipeMessageTypes.ActionNotice && !string.IsNullOrEmpty(message.Payload))
            {
                var notice = JsonSerializer.Deserialize<ActionNoticePayload>(message.Payload, JsonOptions);
                if (notice != null && (notice.Action == "lockdownBlocked" || notice.Action == "removedAgain"))
                {
                    var reason = notice.Action == "lockdownBlocked"
                        ? "Заблокировано режимом Lockdown (StrictMode)"
                        : "Повторно заблокировано (Чёрный список/удалено)";
                    _ = _remoteNotifications!.NotifyBlockedAsync(new StartupEntry { Name = notice.Name }, reason);
                }
            }
        }
        catch { }
    }

    /// <summary>Атомарный флаг предотвращения параллельного сброса батчей таймером при медленной отправке.</summary>
    private int _isFlushing;

    /// <summary>Сбрасывает накопленные сообщения одним батчем с защитой от реентрантности и переполнения пула потоков.</summary>
    private void FlushBatch(object? state)
    {
        if (!_tracker.IsCompleted) return;
        if (_messageQueue.IsEmpty) return;

        // Предотвращаем накопление тиков таймера 200 мс, если предыдущий батч ещё передаётся в канал
        if (Interlocked.CompareExchange(ref _isFlushing, 1, 0) != 0) return;

        try
        {
            var batch = new List<PipeMessage>();
            while (_messageQueue.TryDequeue(out var message))
            {
                batch.Add(message);
            }

            if (batch.Count > 0)
            {
                // Исключение подписчика не должно убивать поток таймера (и весь процесс службы)
                try { MessagesBatchReady?.Invoke(this, batch); }
                catch { }
            }
        }
        finally
        {
            Volatile.Write(ref _isFlushing, 0);
        }
    }

    /// <summary>Dispose: Освобождает занятые ресурсы объекта.</summary>
    public void Dispose()
    {
        _timer.Dispose();
    }
}
