using System.Text;
using System.Windows.Threading;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Агрегатор всплывающих уведомлений (балунов) в системном трее.
/// При одновременном поступлении множества уведомлений объединяет их по смыслу
/// (батчинг со скользящим окном дебаунса 400 мс) вместо непрерывного спама одиночными окнами.
/// Включает клиентское окно охлаждения (Cooldown 15 минут) для исключения повторного показа
/// одинаковых уведомлений по одной и той же программе.
/// Критичные предупреждения (SystemDanger) выводятся немедленно вне очереди.
/// </summary>
public sealed class TrayNotificationAggregator : IDisposable
{
    private readonly object _lock = new();
    private readonly List<PendingTrayNotification> _buffer = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Action<string, string, IReadOnlyList<string>> _showBalloonAction;

    /// <summary>Окно охлаждения для одинаковых уведомлений (Cooldown 15 минут).</summary>
    private static readonly TimeSpan NotificationCooldown = TimeSpan.FromMinutes(15);

    /// <summary>Кэш времени показа последних уведомлений (ключ -> время UTC).</summary>
    private readonly Dictionary<string, DateTime> _recentShown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Максимальное количество отдельных строк, перечисляемых в одном балуне.</summary>
    public const int MaxItemsPerCategory = 3;

    /// <summary>Безопасный лимит символов для текста подсказки Win32 Shell Balloon Tip.</summary>
    public const int MaxBalloonChars = 255;

    /// <summary>Интервал дебаунса для агрегации пачки уведомлений.</summary>
    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Инициализирует агрегатор уведомлений трея.</summary>
    /// <param name="dispatcher">UI-диспетчер WPF.</param>
    /// <param name="showBalloonAction">Делегат вызова WinForms NotifyIcon.ShowBalloonTip.</param>
    public TrayNotificationAggregator(
        Dispatcher dispatcher,
        Action<string, string, IReadOnlyList<string>> showBalloonAction)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _showBalloonAction = showBalloonAction ?? throw new ArgumentNullException(nameof(showBalloonAction));

        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = DebounceInterval
        };
        _timer.Tick += (_, _) => Flush();
    }

    /// <summary>
    /// Добавляет уведомление в очередь агрегации.
    /// Если уведомление критическое (SystemDanger), оно отображается немедленно вне очереди.
    /// Повторные одинаковые уведомления в пределах NotificationCooldown отсекаются.
    /// </summary>
    public void Enqueue(string title, string text, string? entryId, TrayNotificationKind kind)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text))
            return;

        if (kind == TrayNotificationKind.Auto)
            kind = Classify(title);

        // Критичные предупреждения доставляются мгновенно без фильтрации Cooldown
        if (kind == TrayNotificationKind.SystemDanger)
        {
            _showBalloonAction(title, text, entryId != null ? new[] { entryId } : Array.Empty<string>());
            return;
        }

        var now = DateTime.UtcNow;
        var dedupKey = $"{title}:{entryId ?? text}";

        lock (_lock)
        {
            // Проверка Cooldown на стороне клиента: не показываем одинаковое сообщение чаще 1 раза в 15 минут
            if (_recentShown.TryGetValue(dedupKey, out var lastShown) && now - lastShown < NotificationCooldown)
            {
                return; // Подавление дубликата
            }

            _recentShown[dedupKey] = now;
            _buffer.Add(new PendingTrayNotification(title, text, entryId, now, kind));

            // Периодическая очистка устаревших ключей Cooldown
            if (_recentShown.Count > 256)
            {
                var cutoff = now - NotificationCooldown - NotificationCooldown;
                var staleKeys = _recentShown.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
                foreach (var k in staleKeys)
                    _recentShown.Remove(k);
            }
        }

        // Перезапускаем скользящий таймер дебаунса в UI-потоке
        if (_dispatcher.CheckAccess())
        {
            _timer.Stop();
            _timer.Start();
        }
        else
        {
            _dispatcher.BeginInvoke(() =>
            {
                _timer.Stop();
                _timer.Start();
            });
        }
    }

    /// <summary>
    /// Немедленно сбрасывает накопленный буфер и отображает объединенное уведомление.
    /// </summary>
    public void Flush()
    {
        List<PendingTrayNotification> batch;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;
            batch = _buffer.ToList();
            _buffer.Clear();
        }

        _timer.Stop();

        var (title, text, ids) = Coalesce(batch);
        _showBalloonAction(title, text, ids);
    }

    /// <summary>
    /// Интеллектуальное объединение пачки уведомлений по смысловым категориям.
    /// </summary>
    public static (string Title, string Text, IReadOnlyList<string> EntryIds) Coalesce(
        IReadOnlyList<PendingTrayNotification> batch)
    {
        if (batch == null || batch.Count == 0)
            return (string.Empty, string.Empty, Array.Empty<string>());

        // Если пришло всего 1 уведомление — выводим как есть
        if (batch.Count == 1)
        {
            var single = batch[0];
            return (
                single.Title,
                Truncate(single.Text, MaxBalloonChars),
                single.EntryId != null ? new[] { single.EntryId } : Array.Empty<string>()
            );
        }

        var loc = LocalizationService.Instance;
        var entryIds = batch.Where(b => !string.IsNullOrEmpty(b.EntryId))
                            .Select(b => b.EntryId!)
                            .Distinct()
                            .ToList();

        var groups = batch.GroupBy(b => b.Kind)
                          .OrderBy(g => (int)g.Key)
                          .ToList();

        // Формирование заголовка
        string title;
        if (groups.Count == 1)
        {
            title = FormatCategoryTitle(groups[0].Key, groups[0].Count(), loc);
        }
        else
        {
            var mixedPattern = loc["TrayMixedTitle"] ?? "Уведомления ({0})";
            title = string.Format(mixedPattern, batch.Count);
        }

        // Формирование тела сообщения
        var sb = new StringBuilder();
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (groups.Count > 1)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine($"• {FormatCategoryTitle(group.Key, group.Count(), loc)}:");
            }

            var items = group.Take(MaxItemsPerCategory).ToList();
            foreach (var item in items)
            {
                var line = groups.Count > 1 ? $"  - {item.Text}" : $"• {item.Text}";
                sb.AppendLine(line);
            }

            int remaining = group.Count() - items.Count;
            if (remaining > 0)
            {
                var andMorePattern = loc["TrayAndMore"] ?? "...и ещё {0}";
                var moreLine = groups.Count > 1
                    ? $"  {string.Format(andMorePattern, remaining)}"
                    : string.Format(andMorePattern, remaining);
                sb.AppendLine(moreLine);
            }
        }

        string resultText = Truncate(sb.ToString().TrimEnd(), MaxBalloonChars);
        return (title, resultText, entryIds);
    }

    /// <summary>
    /// Автоматическая классификация категории по заголовку (для обратной совместимости).
    /// </summary>
    public static TrayNotificationKind Classify(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return TrayNotificationKind.Info;

        var loc = LocalizationService.Instance;

        if (title.Equals(loc.SystemDangerTitle, StringComparison.OrdinalIgnoreCase))
            return TrayNotificationKind.SystemDanger;

        if (title.Equals(loc.BlacklistNotifTitle, StringComparison.OrdinalIgnoreCase))
            return TrayNotificationKind.BlacklistMatch;

        if (title.Equals(loc.ActionNotifTitle, StringComparison.OrdinalIgnoreCase))
            return TrayNotificationKind.ActionNotice;

        if (title.Equals(loc.NotifTitle, StringComparison.OrdinalIgnoreCase))
            return TrayNotificationKind.NewEntry;

        if (title.Equals(loc["TmProcessKilled"], StringComparison.OrdinalIgnoreCase) ||
            title.Equals(loc["TmProcessBlocked"], StringComparison.OrdinalIgnoreCase) ||
            title.Equals(loc["TmProcessSuspended"], StringComparison.OrdinalIgnoreCase) ||
            title.Equals(loc["TmProcessResumed"], StringComparison.OrdinalIgnoreCase))
        {
            return TrayNotificationKind.ProcessStatus;
        }

        return TrayNotificationKind.Info;
    }

    /// <summary>Форматирует локализованный заголовок категории уведомлений.</summary>
    /// <param name="kind">Тип уведомления.</param>
    /// <param name="count">Количество сообщений в пачке.</param>
    /// <param name="loc">Сервис локализации интерфейса.</param>
    /// <returns>Строка заголовка для трея.</returns>
    private static string FormatCategoryTitle(TrayNotificationKind kind, int count, LocalizationService loc) =>
        kind switch
        {
            TrayNotificationKind.NewEntry =>
                $"{loc.NotifTitle} ({count})",
            TrayNotificationKind.BlacklistMatch =>
                $"{loc.BlacklistNotifTitle} ({count})",
            TrayNotificationKind.ActionNotice =>
                $"{loc.ActionNotifTitle} ({count})",
            TrayNotificationKind.ProcessStatus =>
                $"{loc["TmProcessTitle"] ?? "Процессы"} ({count})",
            TrayNotificationKind.SystemDanger =>
                $"{loc.SystemDangerTitle} ({count})",
            _ => $"{loc.AppName} ({count})"
        };

    /// <summary>Обрезает строку по максимальной длине с добавлением многоточия.</summary>
    /// <param name="s">Исходный текст.</param>
    /// <param name="max">Максимальное допустимое количество символов.</param>
    /// <returns>Обрезанная безопасная строка.</returns>
    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s.Substring(0, max - 1) + "…";
    }

    /// <summary>Освобождает ресурсы таймера и сбрасывает оставшиеся уведомления.</summary>
    public void Dispose()
    {
        _timer.Stop();
        Flush();
    }
}
