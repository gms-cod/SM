using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет настоящее окно свойств и live-модель при замене снимка и асинхронной переоценке без AI/IPC.</summary>
[Collection("WpfNetworkRendering")]
public sealed class EntryDetailRatingSynchronizationTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Использует общий изолированный STA-диспетчер ресурсов приложения.</summary>
    public EntryDetailRatingSynchronizationTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Возвращает два независимых объекта одной записи для моделирования очередного ответа службы.</summary>
    private static StartupEntry Entry(int rating = 5, string risk = "Original risk", bool manual = false) => new()
    {
        Id = "detail-rating-entry", Name = "client", FilePath = @"C:\Apps\client.exe", AiRating = rating,
        AiCategory = "Category", AiRisk = risk, AiRecommendation = "Recommendation", IsUserRating = manual
    };

    /// <summary>Создаёт ViewModel без службы, реестра и фоновых таймеров.</summary>
    private static MainViewModel Model(PipeClient client) => new(client,
        _ => Task.FromResult(new List<NetworkConnectionAlertDto>()),
        (_, _) => Task.FromResult<NetworkAlertStatusPayload?>(null),
        _ => throw new InvalidOperationException("Сетевые карточки не относятся к этой проверке"));

    /// <summary>Дожидается WPF-привязок и продолжений с ограничением времени, не блокируя диспетчер.</summary>
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(condition(), "Окно свойств не синхронизировалось за три секунды.");
    }

    /// <summary>Дожидается первого ContentRendered: TitleBar WPF-UI требует живой HWND до завершения этого отложенного callback.</summary>
    private static void ShowRendered(EntryDetailWindow window)
    {
        var rendered = false;
        window.ContentRendered += (_, _) => rendered = true;
        window.Show();
        PumpUntil(() => rendered);
    }

    /// <summary>Смена серверного экземпляра сразу перепривязывает открытое окно, включая происхождение оценки.</summary>
    [Fact]
    public void SnapshotReplacement_UpdatesOpenWindowAndRatingOrigin()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            using var model = Model(client);
            var original = Entry();
            model.SetInitialData([original], []);
            var window = new EntryDetailWindow(original, model, _ => Task.FromResult<StartupEntry?>(null));
            try
            {
                ShowRendered(window);
                var refreshed = Entry(1, "Updated risk", manual: true);
                model.SetInitialData([refreshed], []);
                var stars = Assert.IsType<TextBlock>(window.FindName("TxtStarsDisplay"));
                PumpUntil(() => ReferenceEquals(window.DataContext, refreshed) && stars.Text == "★★★★★");
                Assert.Equal("Установлено вручную", Assert.IsType<TextBlock>(window.FindName("TxtRatingOrigin")).Text);
                Assert.Equal(Visibility.Visible, Assert.IsType<Button>(window.FindName("BtnResetUserRating")).Visibility);
                refreshed.IsUserRating = false;
                Assert.Equal("Определено AI", Assert.IsType<TextBlock>(window.FindName("TxtRatingOrigin")).Text);
            }
            finally { window.Close(); }
        });

    /// <summary>Новая оценка обновляет актуальный список и открытое окно даже после замены снимка во время запроса.</summary>
    [Fact]
    public void RefreshCompletesAfterSnapshotReplacement_UpdatesLiveListWindowAndManualFlag()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            using var model = Model(client);
            var original = Entry(manual: true);
            model.SetInitialData([original], []);
            var answer = new TaskCompletionSource<StartupEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new EntryDetailWindow(original, model, id => model.ReRateEntryCoreAsync(id, _ => answer.Task));
            try
            {
                ShowRendered(window);
                var refresh = window.RefreshRatingAsync();
                Assert.False(Assert.IsType<Button>(window.FindName("BtnRefreshAi")).IsEnabled);
                Assert.Equal(5, original.AiRating);
                Assert.Equal("Original risk", original.AiRisk);
                var live = Entry(5, "Latest snapshot", manual: true);
                model.SetInitialData([live], []);
                var result = Entry(9, "New AI risk");
                result.AiCategory = "New category";
                result.AiRecommendation = "New recommendation";
                answer.SetResult(result);
                PumpUntil(() => refresh.IsCompleted);
                refresh.GetAwaiter().GetResult();
                var stars = Assert.IsType<TextBlock>(window.FindName("TxtStarsDisplay"));
                PumpUntil(() => stars.Text == "★☆☆☆☆");
                Assert.Same(live, window.DataContext);
                Assert.Same(live, model.GetLiveEntryById(original.Id));
                Assert.Equal(9, live.AiRating);
                Assert.Equal("New AI risk", live.AiRisk);
                Assert.Equal("New category", live.AiCategory);
                Assert.Equal("New recommendation", live.AiRecommendation);
                Assert.False(live.IsUserRating);
                Assert.Equal("Определено AI", Assert.IsType<TextBlock>(window.FindName("TxtRatingOrigin")).Text);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(window.FindName("BtnResetUserRating")).Visibility);
                Assert.True(Assert.IsType<Button>(window.FindName("BtnRefreshAi")).IsEnabled);
            }
            finally { window.Close(); }
        });

    /// <summary>Отсутствие нового ответа сохраняет подтверждённый рейтинг, объяснение и ручной статус.</summary>
    [Fact]
    public void RefreshReturnsNoResult_PreservesPreviousDataAndAllowsRetry()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            using var model = Model(client);
            var entry = Entry(2, "Preserved risk", manual: true);
            model.SetInitialData([entry], []);
            var window = new EntryDetailWindow(entry, model, _ => Task.FromResult<StartupEntry?>(null));
            try
            {
                ShowRendered(window);
                var refresh = window.RefreshRatingAsync();
                PumpUntil(() => refresh.IsCompleted);
                refresh.GetAwaiter().GetResult();
                Assert.Equal(2, entry.AiRating);
                Assert.Equal("Preserved risk", entry.AiRisk);
                Assert.True(entry.IsUserRating);
                Assert.True(Assert.IsType<Button>(window.FindName("BtnRefreshAi")).IsEnabled);
                Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(window.FindName("TxtRatingRefreshStatus")).Visibility);
            }
            finally { window.Close(); }
        });

    /// <summary>Дельта с прежним числом звёзд, но новым объяснением/источником не теряется сравнением снимков.</summary>
    [Fact]
    public void SameScoreWithChangedExplanation_IsRecognizedAsUpdatedSnapshot()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            using var model = Model(client);
            model.SetInitialData([Entry()], []);
            var updated = Entry(risk: "Updated risk", manual: true);
            var method = typeof(MainViewModel).GetMethod("HasChanges", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException("Не найдена производственная проверка снимков.");
            Assert.True(Assert.IsType<bool>(method.Invoke(model, [new List<StartupEntry> { updated }, new List<BlacklistRule>()])));
        });

    /// <summary>После закрытия внешняя модель больше не перепривязывает окно и не удерживает его через подписки.</summary>
    [Fact]
    public void ClosedWindow_DetachesFromEntryAndViewModelNotifications()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            using var model = Model(client);
            var entry = Entry();
            model.SetInitialData([entry], []);
            var window = new EntryDetailWindow(entry, model, _ => Task.FromResult<StartupEntry?>(null));
            ShowRendered(window);
            window.Close();
            model.SetInitialData([Entry(1, "Replacement")], []);
            entry.IsUserRating = true;
            Assert.Same(entry, window.DataContext);
            Assert.Equal("Определено AI", Assert.IsType<TextBlock>(window.FindName("TxtRatingOrigin")).Text);
        });
}
