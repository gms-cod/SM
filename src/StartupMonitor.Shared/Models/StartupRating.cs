using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>AI-рейтинг записи: оценка, категория, риск и рекомендация.</summary>
public class StartupRating
{
    /// <summary>Оценка от 1 (безопасно) до 10 (опасно).</summary>
    [JsonPropertyName("score")]
    public int Score { get; set; }

    /// <summary>Категория угрозы (legitimate, adware, trojan, ...).</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "unknown";

    /// <summary>Уровень риска (низкий, средний, высокий).</summary>
    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "";

    /// <summary>Рекомендация по обработке записи.</summary>
    [JsonPropertyName("recommendation")]
    public string Recommendation { get; set; } = "";

    /// <summary>Признак: рейтинг уже вычислен (Score > 0).</summary>
    [JsonIgnore]
    public bool IsRated => Score > 0;
}
