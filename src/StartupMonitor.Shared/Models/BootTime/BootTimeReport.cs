namespace StartupMonitor.Shared.Models.BootTime;

/// <summary>
/// Комплексный отчет анализа времени загрузки Windows (Boot Time Report).
/// </summary>
public class BootTimeReport
{
    /// <summary>Сведения о последней завершённой загрузке системы.</summary>
    public BootSessionInfo? LatestBoot { get; set; }

    /// <summary>История недавних сессий загрузки системы (хронологический список от новых к старым).</summary>
    public List<BootSessionInfo> RecentSessions { get; set; } = [];

    /// <summary>Ключевые виновники замедления старта (приложения, службы, драйверы с наибольшим временем задержки).</summary>
    public List<BootDegradationEvent> TopOffenders { get; set; } = [];

    /// <summary>Общее количество зафиксированных в журнале сессий запуска.</summary>
    public int TotalSessionsRecorded { get; set; }

    /// <summary>Время генерации отчета (UTC).</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Имеются ли данные анализа загрузки системы.</summary>
    public bool HasData => LatestBoot != null;

    /// <summary>Разница времени загрузки по сравнению с предыдущим стартом в мс (отрицательное — ускорение, положительное — замедление).</summary>
    public long? DeltaPreviousBootMs
    {
        get
        {
            if (RecentSessions.Count >= 2 && LatestBoot != null)
            {
                var previous = RecentSessions[1];
                return LatestBoot.BootTimeMs - previous.BootTimeMs;
            }
            return null;
        }
    }

    /// <summary>Человекочитаемое описание динамики скорости загрузки относительно предыдущего старта.</summary>
    public string DisplayDelta
    {
        get
        {
            var delta = DeltaPreviousBootMs;
            if (!delta.HasValue) return "Первая запись";
            if (Math.Abs(delta.Value) < 1000) return "Без изменений";
            if (delta.Value < 0) return $"⚡ На {BootSessionInfo.FormatDuration(Math.Abs(delta.Value))} быстрее";
            return $"⚠️ На {BootSessionInfo.FormatDuration(delta.Value)} медленнее";
        }
    }

    /// <summary>Цвет индикатора динамики скорости загрузки.</summary>
    public string DeltaColor
    {
        get
        {
            var delta = DeltaPreviousBootMs;
            if (!delta.HasValue) return "#94A3B8";
            if (delta.Value <= -1000) return "#10B981"; // Зеленый (быстрее)
            if (delta.Value >= 3000) return "#EF4444";  // Красный (заметно медленнее)
            return "#F59E0B";                           // Янтарный (небольшая разница)
        }
    }
}

