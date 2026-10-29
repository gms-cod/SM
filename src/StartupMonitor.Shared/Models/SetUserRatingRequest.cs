namespace StartupMonitor.Shared.Models;

/// <summary>
/// Запрос на ручную установку или сброс пользовательского рейтинга записи автозапуска.
/// </summary>
public sealed class SetUserRatingRequest
{
    /// <summary>Уникальный идентификатор записи автозапуска.</summary>
    public string EntryId { get; set; } = string.Empty;

    /// <summary>Количество выбранных пользователем звёзд (от 1 до 5).</summary>
    public int Stars { get; set; } = 5;

    /// <summary>Флаг сброса к автоматической AI-оценке.</summary>
    public bool ResetToAi { get; set; }
}

