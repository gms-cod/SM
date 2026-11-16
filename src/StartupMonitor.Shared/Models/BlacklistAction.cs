namespace StartupMonitor.Shared.Models;

/// <summary>Action performed when a blacklist rule is triggered.</summary>
public enum BlacklistAction
{
    /// <summary>Уведомить пользователя и обратимо отключить запись; имя сохранено для совместимости JSON.</summary>
    NotifyAndRemove,
    /// <summary>Only notify the user, leaving the entry in the system.</summary>
    NotifyOnly
}
