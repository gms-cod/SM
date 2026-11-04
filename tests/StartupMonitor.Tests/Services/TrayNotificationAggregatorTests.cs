using StartupMonitor.UI.Services;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты для TrayNotificationAggregator: проверка объединения и группировки всплывающих сообщений.
/// </summary>
public class TrayNotificationAggregatorTests
{
    [Fact]
    public void Coalesce_SingleNotification_ReturnsOriginalTextAndId()
    {
        var item = new PendingTrayNotification(
            "Новая запись автозапуска",
            "Discord (HKCU\\Run)",
            "entry-1",
            DateTime.UtcNow,
            TrayNotificationKind.NewEntry);

        var (title, text, ids) = TrayNotificationAggregator.Coalesce(new[] { item });

        Assert.Equal("Новая запись автозапуска", title);
        Assert.Equal("Discord (HKCU\\Run)", text);
        Assert.Single(ids);
        Assert.Equal("entry-1", ids[0]);
    }

    [Fact]
    public void Coalesce_MultipleNotificationsSameCategory_GroupsWithCountAndSummary()
    {
        var items = new List<PendingTrayNotification>
        {
            new("Новая запись автозапуска", "App1 (HKCU\\Run)", "id-1", DateTime.UtcNow, TrayNotificationKind.NewEntry),
            new("Новая запись автозапуска", "App2 (HKLM\\Run)", "id-2", DateTime.UtcNow, TrayNotificationKind.NewEntry),
            new("Новая запись автозапуска", "App3 (Startup Folder)", "id-3", DateTime.UtcNow, TrayNotificationKind.NewEntry),
            new("Новая запись автозапуска", "App4 (Task Scheduler)", "id-4", DateTime.UtcNow, TrayNotificationKind.NewEntry),
            new("Новая запись автозапуска", "App5 (Services)", "id-5", DateTime.UtcNow, TrayNotificationKind.NewEntry),
        };

        var (title, text, ids) = TrayNotificationAggregator.Coalesce(items);

        // Заголовок должен содержать общее число элементов
        Assert.Contains("(5)", title);
        // В теле должны быть первые элементы
        Assert.Contains("App1", text);
        Assert.Contains("App2", text);
        Assert.Contains("App3", text);
        // Должна присутствовать строчка "...и ещё 2" (или "…и ещё 2")
        Assert.Contains("2", text);
        // Все ID должны быть собраны
        Assert.Equal(5, ids.Count);
    }

    [Fact]
    public void Coalesce_MixedCategories_CreatesSectionsForEveryCategory()
    {
        var items = new List<PendingTrayNotification>
        {
            new("Новая запись автозапуска", "Telegram (HKCU\\Run)", "id-1", DateTime.UtcNow, TrayNotificationKind.NewEntry),
            new("Программа заблокирована", "Malware.exe (Blacklist)", "id-2", DateTime.UtcNow, TrayNotificationKind.BlacklistMatch),
            new("Процесс завершён", "Killed rogue process", "id-3", DateTime.UtcNow, TrayNotificationKind.ProcessStatus)
        };

        var (title, text, ids) = TrayNotificationAggregator.Coalesce(items);

        // Общий заголовок со счётчиком всех событий
        Assert.Contains("(3)", title);
        Assert.Contains("Telegram", text);
        Assert.Contains("Malware.exe", text);
        Assert.Contains("Killed rogue process", text);
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public void Classify_IdentifiesKindsCorrectly()
    {
        Assert.Equal(TrayNotificationKind.SystemDanger, TrayNotificationAggregator.Classify(LocalizationService.Instance.SystemDangerTitle));
        Assert.Equal(TrayNotificationKind.BlacklistMatch, TrayNotificationAggregator.Classify(LocalizationService.Instance.BlacklistNotifTitle));
        Assert.Equal(TrayNotificationKind.ActionNotice, TrayNotificationAggregator.Classify(LocalizationService.Instance.ActionNotifTitle));
        Assert.Equal(TrayNotificationKind.NewEntry, TrayNotificationAggregator.Classify(LocalizationService.Instance.NotifTitle));
    }
}

