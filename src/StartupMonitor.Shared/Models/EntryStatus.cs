namespace StartupMonitor.Shared.Models;

/// <summary>Статус записи автозапуска.</summary>
public enum EntryStatus
{
    /// <summary>Активна — запись присутствует в системе и выполняется.</summary>
    Enabled,
    /// <summary>Отключена — удалена из системы, но сохранена в конфиге.</summary>
    Disabled,
    /// <summary>Заблокирована чёрным списком.</summary>
    Blocked,
    /// <summary>Отложенный запуск — отключена в системе, запускается службой с задержкой.</summary>
    Delayed,
    /// <summary>Статус не определён.</summary>
    Unknown
}
