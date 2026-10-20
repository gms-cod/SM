namespace StartupMonitor.Shared.Models;

/// <summary>Правило чёрного списка: значение, тип совпадения и действие при срабатывании.</summary>
public class BlacklistRule
{
    /// <summary>Уникальный идентификатор правила.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Значение для сопоставления (путь, хеш, издатель).</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Способ сопоставления значения с записью.</summary>
    public BlacklistMatchType MatchType { get; set; } = BlacklistMatchType.Path;
    /// <summary>Действие при срабатывании правила.</summary>
    public BlacklistAction Action { get; set; } = BlacklistAction.NotifyAndRemove;
    /// <summary>Дата и время создания правила.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Комментарий (опционально, для чего добавлено правило).</summary>
    public string Comment { get; set; } = string.Empty;
    /// <summary>Создает независимую копию правила.</summary>
    public BlacklistRule Clone() => (BlacklistRule)MemberwiseClone();
}