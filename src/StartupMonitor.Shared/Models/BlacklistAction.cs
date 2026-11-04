namespace StartupMonitor.Shared.Models;

/// <summary>Действие, выполняемое при срабатывании правила чёрного списка.</summary>
public enum BlacklistAction
{
    /// <summary>Уведомить пользователя и удалить запись из системы.</summary>
    NotifyAndRemove,
    /// <summary>Только уведомить, запись остаётся в системе.</summary>
    NotifyOnly
}
