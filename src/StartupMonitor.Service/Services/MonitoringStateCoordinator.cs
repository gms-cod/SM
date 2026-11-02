using System.Text.Json;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>
/// Синглтон-координатор глобального состояния мониторинга (активен / приостановлен).
/// Управляет флагом паузы, уведомляет все фоновые вотчеры и транслирует изменения в UI через EventPipe.
/// </summary>
public class MonitoringStateCoordinator
{
    private volatile bool _isPaused;
    /// <summary>Блокировка для сериализации параллельных вызовов SetPaused и защиты инварианта проверки-и-установки.</summary>
    private readonly object _stateLock = new();
    /// <summary>Упорядоченная очередь переходов; единственный отправитель допускает повторный SetPaused из обработчика.</summary>
    private readonly Queue<bool> _pendingStates = new();
    private bool _isDispatching;
    private readonly JsonDataStore _store;
    private readonly NotificationService? _notifications;
    private readonly ILogger<MonitoringStateCoordinator> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Флаг паузы мониторинга.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>Событие изменения состояния внутри службы.</summary>
    public event EventHandler<bool>? StateChanged;

    public MonitoringStateCoordinator(
        JsonDataStore store,
        NotificationService? notifications = null,
        ILogger<MonitoringStateCoordinator>? logger = null)
    {
        _store = store;
        _notifications = notifications;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MonitoringStateCoordinator>.Instance;
        _isPaused = store.Load()?.Settings?.IsMonitoringPaused ?? false;
    }

    /// <summary>Устанавливает состояние и доставляет переходы в порядке фиксации, включая конкурентные и повторно входящие вызовы.</summary>
    /// <param name="paused">True для паузы мониторинга.</param>
    public void SetPaused(bool paused)
    {
        lock (_stateLock)
        {
            if (_isPaused == paused) return;

            _isPaused = paused;
            _logger.LogInformation("Глобальный мониторинг автозапуска переключен: IsPaused = {IsPaused}", paused);

            try
            {
                var cfg = _store.Load();
                cfg.Settings.IsMonitoringPaused = paused;
                _store.Save(cfg);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось сохранить состояние IsMonitoringPaused в настройках");
            }
            _pendingStates.Enqueue(paused);
            // Другой вызов уже доставляет очередь; возвращаемся без ожидания пользовательского обработчика.
            if (_isDispatching) return;
            _isDispatching = true;
        }

        while (true)
        {
            bool nextState;
            lock (_stateLock)
            {
                if (_pendingStates.Count == 0)
                {
                    _isDispatching = false;
                    return;
                }
                nextState = _pendingStates.Dequeue();
            }
            PublishState(nextState);
        }
    }

    /// <summary>Доставляет один переход всем подписчикам и IPC; отказ отдельного подписчика не прерывает очередь.</summary>
    /// <param name="paused">Состояние зафиксированного перехода.</param>
    private void PublishState(bool paused)
    {
        var handlers = StateChanged?.GetInvocationList();
        if (handlers is not null)
        {
            foreach (var handler in handlers)
            {
                try { ((EventHandler<bool>)handler)(this, paused); }
                catch (Exception ex) { _logger.LogWarning(ex, "Исключение в обработчике StateChanged"); }
            }
        }
        try
        {
            _notifications?.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.MonitoringStateChanged,
                Payload = JsonSerializer.Serialize(new MonitoringStatePayload(paused), JsonOptions)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить событие MonitoringStateChanged через Pipe");
        }
    }
}
