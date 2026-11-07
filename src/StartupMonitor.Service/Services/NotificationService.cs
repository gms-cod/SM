using System.Collections.Concurrent;
using System.Text.Json;
using StartupMonitor.Service.Services.Remote;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Сервис уведомлений с объединением сообщений в батчи (coalescing)
/// и интеллектуальной дедупликацией (Smart Notification Deduplication).
/// Предотвращает циклический спам повторными алертами (например, для блокировок Adobe Updater).
/// Пока идёт начальный скан — уведомления EntryAdded подавляются.
/// Батч отправляется раз в 200 мс через событие MessagesBatchReady.
/// Также отправляет удалённые уведомления в Telegram / Discord после завершения скана.</summary>
public class NotificationService : IDisposable
{
    /// <summary>Потокобезопасная очередь сообщений для отправки в UI.</summary>
    public const int MaxQueueCapacity = 10_000;
    private readonly ConcurrentQueue<PipeMessage> _messageQueue = new();

    /// <summary>Окно подавления повторных однотипных уведомлений (Cooldown 30 минут).</summary>
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(30);

    /// <summary>Потокобезопасный кэш времени отправки последних алертов (ключ -> время UTC).</summary>
    private readonly ConcurrentDictionary<string, DateTime> _recentNotifications = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Счётчик операций для периодической очистки устаревших записей кэша дедупликации.</summary>
    private long _dedupOpCounter;

    /// <summary>Интервал между проходами очистки устаревшего кэша (в операциях).</summary>
    private const int CleanupEveryOperations = 128;

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

    /// <summary>Инициализирует сервис уведомлений с таймером сброса батчей.</summary>
    /// <param name="tracker">Трекер начального сканирования системы.</param>
    /// <param name="remoteNotifications">Опциональный сервис удаленных уведомлений.</param>
    public NotificationService(InitialScanTracker tracker, RemoteNotificationService? remoteNotifications = null)
    {
        _tracker = tracker;
        _remoteNotifications = remoteNotifications;
        _timer = new Timer(FlushBatch, null, _coalesceInterval, _coalesceInterval);
    }

    /// <summary>Добавляет сообщение в очередь с проверкой дедупликации.
    /// Повторные одинаковые уведомления в пределах окна DeduplicationWindow отсекаются.</summary>
    public void Enqueue(PipeMessage message)
    {
        if (!_tracker.IsCompleted && message.Type == PipeMessageTypes.EntryAdded)
            return;

        // Интеллектуальная дедупликация: отсекаем повторные алерты по неизменившимся сущностям
        if (!TryRegisterNotification(message))
            return;

        if (_messageQueue.Count >= MaxQueueCapacity)
        {
            // Drop oldest некритичных сообщений для предотвращения неконтролируемого роста памяти (P1-07)
            _messageQueue.TryDequeue(out _);
        }
        _messageQueue.Enqueue(message);

        // Отправка удалённых оповещений (только после завершения начального сканирования)
        if (_tracker.IsCompleted && _remoteNotifications != null)
        {
            DispatchRemoteNotification(message);
        }
    }

    /// <summary>Проверяет, является ли уведомление дубликатом в пределах окна охлаждения.
    /// Возвращает true, если уведомление новое (нужно отправить); false — если дубликат (подавить).</summary>
    private bool TryRegisterNotification(PipeMessage message)
    {
        var dedupKey = BuildDedupKey(message);
        if (string.IsNullOrEmpty(dedupKey))
            return true; // Неизвестный тип — отправляем без подавления

        var now = DateTime.UtcNow;

        // Периодическая очистка устаревших записей кэша
        if (Interlocked.Increment(ref _dedupOpCounter) % CleanupEveryOperations == 0)
        {
            CleanupStaleDedupEntries(now);
        }

        while (true)
        {
            if (_recentNotifications.TryGetValue(dedupKey, out var lastSentUtc))
            {
                // Если с момента предыдущего уведомления прошло меньше времени, чем DeduplicationWindow — подавляем
                if (now - lastSentUtc < DeduplicationWindow)
                {
                    return false;
                }

                // Окно истекло: атомарно обновляем временную метку
                if (_recentNotifications.TryUpdate(dedupKey, now, lastSentUtc))
                {
                    return true;
                }

                continue;
            }

            // Новая запись: пытаемся добавить в кэш
            if (_recentNotifications.TryAdd(dedupKey, now))
            {
                return true;
            }
        }
    }

    /// <summary>Формирует стабильный дедупликационный ключ на основе типа сообщения и полезной нагрузки.</summary>
    private static string? BuildDedupKey(PipeMessage message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message.Payload))
                return null;

            switch (message.Type)
            {
                case PipeMessageTypes.BlacklistMatch:
                {
                    var entry = JsonSerializer.Deserialize<StartupEntry>(message.Payload, JsonOptions);
                    if (entry == null) return null;
                    // Ключ включает ID и CommandLine (если аргументы изменились — это новый алерт)
                    return $"BlacklistMatch:{entry.Id}:{entry.CommandLine}";
                }

                case PipeMessageTypes.ActionNotice:
                {
                    var notice = JsonSerializer.Deserialize<ActionNoticePayload>(message.Payload, JsonOptions);
                    if (notice == null) return null;
                    return $"ActionNotice:{notice.Name}:{notice.Action}";
                }

                case PipeMessageTypes.EntryAdded:
                {
                    var entry = JsonSerializer.Deserialize<StartupEntry>(message.Payload, JsonOptions);
                    if (entry == null) return null;
                    return $"EntryAdded:{entry.Id}";
                }

                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Удаляет записи кэша дедупликации, время которых превышает удвоенное окно.</summary>
    private void CleanupStaleDedupEntries(DateTime now)
    {
        var cutoff = now - DeduplicationWindow - DeduplicationWindow;
        foreach (var pair in _recentNotifications)
        {
            if (pair.Value < cutoff)
            {
                _recentNotifications.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <summary>Отправляет внешние оповещения в настроенные каналы (Telegram/Discord).</summary>
    /// <param name="message">Сообщение для отправки.</param>
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

    /// <summary>Освобождает занятые ресурсы объекта (таймер батчинга).</summary>
    public void Dispose()
    {
        _timer.Dispose();
    }
}
