namespace StartupMonitor.Shared.Models.BootTime;

/// <summary>
/// Информация о сессии запуска Windows (на основе Event ID 100 журнала Diagnostics-Performance).
/// </summary>
public class BootSessionInfo
{
    /// <summary>Порядковый номер загрузки системы (SystemBootInstance).</summary>
    public int BootInstance { get; set; }

    /// <summary>Время старта загрузки (UTC).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Общее время загрузки системы в миллисекундах (BootTime).</summary>
    public long BootTimeMs { get; set; }

    /// <summary>Время до отображения рабочего стола в мс (MainPathBootTime).</summary>
    public long MainPathBootTimeMs { get; set; }

    /// <summary>Время стабилизации после входа в мс (BootPostBootTime: запуск приложений и служб).</summary>
    public long PostBootTimeMs { get; set; }

    /// <summary>Время инициализации ядра ОС в мс (BootKernelInitTime).</summary>
    public long KernelInitTimeMs { get; set; }

    /// <summary>Время инициализации драйверов в мс (BootDriverInitTime).</summary>
    public long DriverInitTimeMs { get; set; }

    /// <summary>Время старта критических служб в мс (BootCriticalServicesInitTime).</summary>
    public long CriticalServicesInitTimeMs { get; set; }

    /// <summary>Время обработки профиля пользователя в мс (BootUserProfileProcessingTime).</summary>
    public long UserProfileInitTimeMs { get; set; }

    /// <summary>Время инициализации оболочки Проводника в мс (BootExplorerInitTime).</summary>
    public long ExplorerInitTimeMs { get; set; }

    /// <summary>Количество запущенных приложений автозапуска (BootNumStartupApps).</summary>
    public int StartupAppsCount { get; set; }

    /// <summary>Является ли загрузка результатом перезагрузки после установки обновлений.</summary>
    public bool IsRebootAfterInstall { get; set; }

    // Вспомогательные свойства форматирования для UI

    /// <summary>Форматированное общее время загрузки (например, "28.4 с" или "1м 14с").</summary>
    public string DisplayBootTime => FormatDuration(BootTimeMs);

    /// <summary>Форматированное время до рабочего стола (например, "12.3 с").</summary>
    public string DisplayMainPath => FormatDuration(MainPathBootTimeMs);
    public string DisplayMainPathBootTime => DisplayMainPath;

    /// <summary>Форматированное время фоновой инициализации (например, "45.1 с").</summary>
    public string DisplayPostBoot => FormatDuration(PostBootTimeMs);
    public string DisplayPostBootTime => DisplayPostBoot;

    /// <summary>Дата и время загрузки в локальном часовом поясе.</summary>
    public string DisplayDateTime => Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    /// <summary>Рейтинг скорости загрузки: Fast, Normal, Slow.</summary>
    public string SpeedRating => BootTimeMs switch
    {
        <= 30_000 => "Fast",
        <= 65_000 => "Normal",
        _ => "Slow"
    };

    /// <summary>Цвет рейтинга скорости (#10B981 зеленый, #F59E0B янтарный, #EF4444 красный).</summary>
    public string RatingColor => SpeedRating switch
    {
        "Fast" => "#10B981",
        "Normal" => "#F59E0B",
        _ => "#EF4444"
    };
    public string SpeedRatingColor => RatingColor;

    /// <summary>Преобразует миллисекунды в человекочитаемую строку длительности.</summary>
    public static string FormatDuration(long ms)
    {
        if (ms <= 0) return "0 мс";
        if (ms < 1000) return $"{ms} мс";
        var totalSec = ms / 1000.0;
        if (totalSec < 60) return $"{totalSec.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} с";
        var min = (int)(totalSec / 60);
        var sec = (int)(totalSec % 60);
        return $"{min} мин {sec} с";
    }
}

