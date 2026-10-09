namespace StartupMonitor.Shared.Models.BootTime;

/// <summary>
/// Категория компонента, вызвавшего замедление загрузки системы.
/// </summary>
public enum BootDegradationCategory
{
    /// <summary>Приложение автозапуска или фоновая программа (Event ID 101).</summary>
    Application,
    /// <summary>Системный драйвер устройства (Event ID 102).</summary>
    Driver,
    /// <summary>Служба Windows (Event ID 103).</summary>
    Service,
    /// <summary>Групповая политика компьютера или пользователя (Event ID 107, 108).</summary>
    GroupPolicy,
    /// <summary>Инициализация физического устройства (Event ID 109).</summary>
    Device,
    /// <summary>Другая системная задержка (Event ID 104, 105, 106, 110).</summary>
    Other
}

/// <summary>
/// Событие замедления загрузки Windows конкретным приложением, драйвером или службой.
/// </summary>
public class BootDegradationEvent
{
    /// <summary>Идентификатор события Windows Event Log (101, 102, 103, ...).</summary>
    public int EventId { get; set; }

    /// <summary>Категория компонента-виновника.</summary>
    public BootDegradationCategory Category { get; set; } = BootDegradationCategory.Application;

    /// <summary>Имя файла программы, службы или драйвера.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Понятное отображаемое имя (FriendlyName).</summary>
    public string? FriendlyName { get; set; }

    /// <summary>Полный путь к исполняемому файлу компонента на диске.</summary>
    public string? Path { get; set; }

    /// <summary>Издатель / компания (CompanyName / Publisher).</summary>
    public string? Publisher { get; set; }

    /// <summary>Версия файла программы или драйвера.</summary>
    public string? Version { get; set; }

    /// <summary>Общее время выполнения процесса/компонента при старте в мс (TotalTime).</summary>
    public long TotalTimeMs { get; set; }

    /// <summary>Время замедления сверх нормы в мс (DegradationTime).</summary>
    public long DegradationTimeMs { get; set; }

    /// <summary>Время фиксации события (UTC).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Идентификатор связанной записи в базе автозапуска StartupMonitor (если найдена).</summary>
    public string? LinkedStartupEntryId { get; set; }

    // Свойства для отображения в UI

    /// <summary>Наименование для отображения: FriendlyName если задано, иначе Name.</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName : Name;

    /// <summary>Форматированное время замедления (например, "+25.9 с" или "+850 мс").</summary>
    public string DisplayDegradationTime => $"+{BootSessionInfo.FormatDuration(DegradationTimeMs)}";
    public string DisplayDegradation => DisplayDegradationTime;

    /// <summary>Цвет бейджа степени замедления.</summary>
    public string BadgeColor => DegradationTimeMs > 2000 ? "#EF4444" : "#F59E0B";

    /// <summary>Форматированное общее время работы (например, "30.9 с").</summary>
    public string DisplayTotalTime => BootSessionInfo.FormatDuration(TotalTimeMs);

    /// <summary>Отображаемая категория на русском языке.</summary>
    public string DisplayCategory => Category switch
    {
        BootDegradationCategory.Application => "Приложение",
        BootDegradationCategory.Driver => "Драйвер",
        BootDegradationCategory.Service => "Служба",
        BootDegradationCategory.GroupPolicy => "Групповая политика",
        BootDegradationCategory.Device => "Устройство",
        _ => "Система"
    };

    /// <summary>Цвет бейджа категории.</summary>
    public string CategoryColor => Category switch
    {
        BootDegradationCategory.Application => "#3B82F6",
        BootDegradationCategory.Service => "#F59E0B",
        BootDegradationCategory.Driver => "#8B5CF6",
        _ => "#64748B"
    };
}

