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

        StateChanged?.Invoke(this, paused);

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
