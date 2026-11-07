namespace StartupMonitor.UI.Services;

/// <summary>
/// Категория всплывающего уведомления в системном трее.
/// Позволяет объединять и форматировать однородные уведомления в смысловые группы.
/// </summary>
public enum TrayNotificationKind
{
    /// <summary>Определить категорию автоматически по заголовку или источнику.</summary>
    Auto,

    /// <summary>Обнаружение новых программ в автозагрузке.</summary>
    NewEntry,

    /// <summary>Срабатывание правил черного списка или превентивных Auto-Guards.</summary>
    BlacklistMatch,

    /// <summary>Уведомления о выполненных действиях над записями (отключено, удалено, восстановлено).</summary>
    ActionNotice,

    /// <summary>Действия над процессами в Диспетчере задач (завершение, блокировка, заморозка, разморозка).</summary>
    ProcessStatus,

    /// <summary>Критичные системные предупреждения (попытка блокировки ядра Windows и др.) — доставляются немедленно.</summary>
    SystemDanger,

    /// <summary>Общие информационные уведомления и результаты проверок.</summary>
    Info
}

/// <summary>
/// Элемент очереди отложенных уведомлений трея.
/// </summary>
public sealed record PendingTrayNotification(
    string Title,
    string Text,
    string? EntryId,
    DateTime TimestampUtc,
    TrayNotificationKind Kind);

