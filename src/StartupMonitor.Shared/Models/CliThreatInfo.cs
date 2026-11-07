using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Уровень угрозы/подозрительности командной строки или пути запуска.
/// </summary>
public enum CliThreatLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

/// <summary>
/// Информация о результатах эвристического анализа командной строки и пути.
/// </summary>
public class CliThreatInfo
{
    /// <summary>Флаг наличия обнаруженной угрозы или подозрительного паттерна.</summary>
    [JsonPropertyName("hasThreat")]
    public bool HasThreat { get; set; }

    /// <summary>Уровень критичности найденной аномалии.</summary>
    [JsonPropertyName("threatLevel")]
    public CliThreatLevel ThreatLevel { get; set; } = CliThreatLevel.None;

    /// <summary>Краткий текст для бейджа в интерфейсе (например, "Base64 скрипт", "Скрытый запуск").</summary>
    [JsonPropertyName("badgeText")]
    public string BadgeText { get; set; } = string.Empty;

    /// <summary>Подробное понятное описание причины срабатывания эвристики для тултипа.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Список сработавших эвристических индикаторов.</summary>
    [JsonPropertyName("indicators")]
    public List<string> Indicators { get; set; } = new();
}

