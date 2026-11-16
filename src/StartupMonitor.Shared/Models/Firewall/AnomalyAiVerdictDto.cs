using System;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>
/// DTO структурированного вердикта ИИ (или локального оффлайн-движка) по сетевой аномалии.
/// </summary>
public sealed class AnomalyAiVerdictDto
{
    /// <summary>Уникальный идентификатор анализируемого инцидента.</summary>
    public string IncidentId { get; set; } = string.Empty;

    /// <summary>Уровень риска от 1 (Безопасно) до 5 (Критическая угроза).</summary>
    public int RiskLevel { get; set; } = 1;

    /// <summary>Краткая метка уровня риска (например: «Высокий риск (C2 Beaconing)»).</summary>
    public string RiskLabel { get; set; } = "Неизвестно";

    /// <summary>Понятное человеческое объяснение инцидента на русском языке.</summary>
    public string UserExplanation { get; set; } = string.Empty;

    /// <summary>Техническое резюме сетевой аномалии.</summary>
    public string TechnicalSummary { get; set; } = string.Empty;

    /// <summary>
    /// Рекомендуемое действие пользователя.
    /// Допустимые значения: «AllowAlways», «AllowSession», «BlockSession», «BlockAlways».
    /// </summary>
    public string RecommendedAction { get; set; } = "AllowSession";

    /// <summary>Обоснование рекомендуемого действия.</summary>
    public string ActionReason { get; set; } = string.Empty;

    /// <summary>Источник вердикта («LLM», «Cache», «OfflineHeuristic»).</summary>
    public string Source { get; set; } = "OfflineHeuristic";

    /// <summary>Временная метка вынесения вердикта (UTC).</summary>
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}
