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

    /// <summary>Устанавливает глобальное состояние паузы или возобновления мониторинга.</summary>
    public void SetPaused(bool paused)
    {
        // Сериализуем параллельные вызовы: предотвращаем гонку check-then-set,
        // при которой два потока одновременно проходят проверку и дублируют
        // сохранение/событие StateChanged.
        lock (_stateLock)
        {
            if (_isPaused == paused) return;

            // Сначала фиксируем флаг (volatile обеспечивает видимость между ядрами CPU),
            // затем сохраняем в хранилище. Чтение _isPaused вне блокировки остаётся безопасным.
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
        }

        // Событие и IPC-уведомление вызываются вне блокировки: подписчики могут
        // синхронно вызывать SetPaused (reentrancy) без риска дедлока. Копия делегата
        // обеспечивает потокобезопасный обход списка подписчиков.
        var stateChangedHandler = StateChanged;
        try
        {
            stateChangedHandler?.Invoke(this, paused);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Исключение в обработчике StateChanged");
        }

        // Трансляция всем подключенным UI клиентам через событийный Named Pipe
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



