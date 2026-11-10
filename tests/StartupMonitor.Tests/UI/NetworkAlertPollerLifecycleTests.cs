using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет настоящий поллер MainViewModel без подключения службы, реестра и изменения правил ОС.</summary>
[Collection("WpfNetworkRendering")]
public sealed class NetworkAlertPollerLifecycleTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Использует общий STA-диспетчер WPF.</summary>
    /// <param name="host">Изолированное окружение настоящей разметки приложения.</param>
    public NetworkAlertPollerLifecycleTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Возвращает группу с готовой ИИ-подсказкой, не требующей IPC.</summary>
    private static NetworkConnectionAlertDto Alert(string id = "poller-group") => new()
    {
        AlertId = id, ProcessName = "client.exe", ProcessPath = @"C:\Apps\client.exe",
        RemoteAddress = "8.8.8.8", RemotePort = 443, AiPurpose = "Изолированная проверка"
    };

    /// <summary>Прокачивает очередь WPF до наблюдаемого результата с независимым ограничением времени.</summary>
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
        Assert.True(condition(), "Поллер не завершил ожидаемый переход за три секунды.");
    }

    /// <summary>Ожидает настоящий async-тик и передаёт ошибку проверки вызывающей стороне.</summary>
    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    /// <summary>Поздний снимок отменённого поколения не создаёт карточку даже при некооперативном источнике.</summary>
    [Fact]
    public void Stop_CancelsPendingReadAndDiscardsLateSnapshot()
        => _host.Run(() =>
        {
            var source = new TaskCompletionSource<List<NetworkConnectionAlertDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = CancellationToken.None;
            var windows = 0;
            using var model = new MainViewModel(new PipeClient(), ct => { token = ct; return source.Task; },
                (_, _) => Task.FromResult<NetworkAlertStatusPayload?>(null),
                alert => { windows++; return new NetworkConnectionAlertWindow(alert, new PipeClient()); });
            model.IsConnected = true;
            model.StartNetworkAlertPoller();
            var tick = model.RefreshNetworkAlertsAsync();
            model.StopNetworkAlertPoller();
            Assert.True(token.IsCancellationRequested);
            Complete(tick);
            source.SetResult([Alert()]);
            Complete(model.RefreshNetworkAlertsAsync());
            Assert.Equal(0, windows);
        });

    /// <summary>Остановка после готового снимка, но до callback диспетчера отменяет уже зарезервированный показ.</summary>
    [Fact]
    public void Stop_BeforeQueuedShow_DoesNotOpenObsoleteWindow()
        => _host.Run(() =>
        {
            var windows = 0;
            using var client = new PipeClient();
            using var model = new MainViewModel(client,
                _ => Task.FromResult(new List<NetworkConnectionAlertDto> { Alert() }),
                (_, _) => Task.FromResult<NetworkAlertStatusPayload?>(null),
                alert => { windows++; return new NetworkConnectionAlertWindow(alert, client); });
            model.IsConnected = true;
            model.StartNetworkAlertPoller();
            var tick = model.RefreshNetworkAlertsAsync();
            // Show ещё стоит в очереди STA и не может выполниться до возврата текущего обработчика.
            model.StopNetworkAlertPoller();
            Complete(tick);
            Assert.Equal(0, windows);
        });

    /// <summary>После reconnect используется новая группа; завершившийся позже старый запрос её не заменяет.</summary>
    [Fact]
    public void Restart_NewGenerationIgnoresPreviousResponse()
        => _host.Run(() =>
        {
            var previous = new TaskCompletionSource<List<NetworkConnectionAlertDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            var windows = new List<NetworkConnectionAlertWindow>();
            using var client = new PipeClient();
            using var model = new MainViewModel(client, _ => ++reads == 1 ? previous.Task : Task.FromResult(new List<NetworkConnectionAlertDto> { Alert("new-generation") }),
                (_, _) => Task.FromResult<NetworkAlertStatusPayload?>(null),
                alert => { var window = new NetworkConnectionAlertWindow(alert, client); windows.Add(window); return window; });
            try
            {
                model.IsConnected = true;
                model.StartNetworkAlertPoller();
                var oldTick = model.RefreshNetworkAlertsAsync();
                model.StopNetworkAlertPoller();
                Complete(oldTick);
                model.StartNetworkAlertPoller();
                Complete(model.RefreshNetworkAlertsAsync());
                Assert.Equal("new-generation", Assert.Single(windows).AlertId);
                previous.SetResult([Alert("old-generation")]);
                Complete(model.RefreshNetworkAlertsAsync());
                Assert.Single(windows);
            }
            finally
            {
                model.StopNetworkAlertPoller();
                PumpUntil(() => windows.All(window => !window.IsVisible));
            }
        });

    /// <summary>Открытая карточка продолжает получать статус; внешнее завершение закрывает её без нового решения.</summary>
    [Fact]
    public void OpenWindow_RefreshesStatusAndClosesSupersededWithoutBlock()
        => _host.Run(() =>
        {
            var reads = 0;
            var statusReads = 0;
            var sends = 0;
            NetworkConnectionAlertWindow? window = null;
            using var client = new PipeClient();
            using var model = new MainViewModel(client,
                _ => { reads++; return Task.FromResult(new List<NetworkConnectionAlertDto> { Alert() }); },
                (_, _) => { statusReads++; return Task.FromResult<NetworkAlertStatusPayload?>(new(true, null, NetworkAlertResolutionStatus.Superseded)); },
                alert => window = new NetworkConnectionAlertWindow(alert, client, resolutionSender: (_, _) => { sends++; return Task.FromResult(true); }));
            try
            {
                model.IsConnected = true;
                model.StartNetworkAlertPoller();
                Complete(model.RefreshNetworkAlertsAsync());
                Assert.NotNull(window);
                Assert.True(window.IsVisible);
                Complete(model.RefreshNetworkAlertsAsync());
                PumpUntil(() => !window.IsVisible);
                Assert.Equal(1, reads);
                Assert.Equal(1, statusReads);
                Assert.Equal(0, sends);
            }
            finally
            {
                model.StopNetworkAlertPoller();
                if (window != null) PumpUntil(() => !window.IsVisible);
            }
        });

    /// <summary>Недоставленный Defer закрывает UI безопасно, а локальный cooldown не возвращает тот же запрос на следующем тике.</summary>
    [Fact]
    public void UndeliveredDefer_DoesNotImmediatelyReopenSameGroup()
        => _host.Run(() =>
        {
            var windows = new List<NetworkConnectionAlertWindow>();
            using var client = new PipeClient();
            using var model = new MainViewModel(client,
                _ => Task.FromResult(new List<NetworkConnectionAlertDto> { Alert() }),
                (_, _) => Task.FromResult<NetworkAlertStatusPayload?>(new(true, Alert(), NetworkAlertResolutionStatus.UnknownOutcome)),
                alert => { var window = new NetworkConnectionAlertWindow(alert, client, resolutionSender: (_, _) => Task.FromResult(false)); windows.Add(window); return window; });
            try
            {
                model.IsConnected = true;
                model.StartNetworkAlertPoller();
                Complete(model.RefreshNetworkAlertsAsync());
                var first = Assert.Single(windows);
                ((ButtonBase)first.FindName("BtnClose")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => !first.IsVisible);
                Complete(model.RefreshNetworkAlertsAsync());
                Assert.Single(windows);
            }
            finally
            {
                model.StopNetworkAlertPoller();
                PumpUntil(() => windows.All(window => !window.IsVisible));
            }
        });
}
