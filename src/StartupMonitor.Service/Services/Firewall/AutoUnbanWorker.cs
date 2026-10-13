using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Фоновый воркер автоматического разбана (Auto-Unban Worker) для службы Port Defender.
/// Запускается с интервалом 30 секунд, проверяет активные временные баны,
/// у которых истёк срок действия (<c>ExpiresAtUtc &lt;= DateTime.UtcNow</c>),
/// и снимает их через службу сетевого экрана.
/// </summary>
public sealed class AutoUnbanWorker : BackgroundService
{
    private readonly ProcessPortDefenderService _defender;
    private readonly ILogger<AutoUnbanWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Инициализирует фоновый воркер AutoUnbanWorker.
    /// </summary>
    public AutoUnbanWorker(ProcessPortDefenderService defender, ILogger<AutoUnbanWorker> logger)
    {
        _defender = defender ?? throw new ArgumentNullException(nameof(defender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Основной цикл фонового выполнения воркера.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoUnbanWorker: Служба авторазбана запущена (интервал проверки: {Interval}с).",
            _checkInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                int unbannedCount = _defender.ProcessExpiredBans();
                if (unbannedCount > 0)
                {
                    _logger.LogInformation("AutoUnbanWorker: Снято {Count} истёкших банов брандмауэра.", unbannedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AutoUnbanWorker: Ошибка во время периодической проверки истёкших банов.");
            }
        }

        _logger.LogInformation("AutoUnbanWorker: Служба авторазбана остановлена.");
    }
}
