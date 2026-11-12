using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// Проверяет настоящий WPF-диалог на общем STA-диспетчере: ожидание ACK,
/// повторный клик, отказ и тайм-аут. IPC и реальный брандмауэр не используются.
/// </summary>
[Collection("WpfNetworkRendering")]
public sealed class NetworkAlertAcknowledgementTests
{
    private readonly NetworkRenderingTestHost _host;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Принимает единственный WPF-host тестовой коллекции.</summary>
    /// <param name="host">Изолированный диспетчер с ресурсами приложения.</param>
    public NetworkAlertAcknowledgementTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Создаёт диалог с готовой AI-подсказкой, исключающей фоновое обращение к IPC.</summary>
    /// <param name="client">Клиент, принадлежащий текущему тесту.</param>
    /// <param name="sender">Управляемое подтверждение решения.</param>
    /// <param name="canAllowSession">Доступно ли временное разрешение для текущей политики.</param>
    /// <param name="canBlockSession">Доступна ли адресная временная блокировка приложения.</param>
    /// <returns>Созданный, но ещё не показанный диалог.</returns>
    private static NetworkConnectionAlertWindow CreateWindow(PipeClient client, Func<ResolveNetworkAlertRequest, CancellationToken, Task<bool>> sender,
        bool canAllowSession = true, bool canBlockSession = true,
        Func<ResolveNetworkAlertRequest, CancellationToken, Task<ResolveNetworkAlertResultPayload>>? detailedSender = null) => new(
        new NetworkConnectionAlertDto
        {
            AlertId = "ack-regression",
            ProcessName = "client.exe",
            ProcessPath = @"C:\Apps\client.exe",
            RemoteAddress = "8.8.8.8",
            RemotePort = 443,
            CanAllowSession = canAllowSession,
            CanBlockSession = canBlockSession,
            AiPurpose = "Изолированная проверка подтверждения решения"
        }, client, countdownSeconds: 3600, resolutionSender: sender, detailedResolutionSender: detailedSender);

    /// <summary>Возвращает настоящую кнопку действия из производственной разметки.</summary>
    /// <param name="window">Проверяемое окно.</param>
    /// <param name="name">Имя кнопки в XAML.</param>
    /// <returns>Настоящий элемент кнопки.</returns>
    private static ButtonBase Button(NetworkConnectionAlertWindow window, string name) =>
        Assert.IsAssignableFrom<ButtonBase>(window.FindName(name));

    /// <summary>Прокачивает STA-диспетчер до условия с независимым ограничением времени.</summary>
    /// <param name="condition">Наблюдаемое условие завершения.</param>
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (condition() || DateTime.UtcNow >= deadline)
                frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(condition(), "Диалог не достиг ожидаемого состояния за три секунды.");
    }

    /// <summary>Освобождает окно даже после отрицательного ACK без отправки нового пользовательского решения.</summary>
    /// <param name="window">Окно, принадлежащее тесту.</param>
    private static void Cleanup(NetworkConnectionAlertWindow window)
    {
        var confirmed = typeof(NetworkConnectionAlertWindow).GetField("_resolutionConfirmed", PrivateInstance)
            ?? throw new MissingFieldException("Не найдено состояние подтверждённого закрытия диалога.");
        confirmed.SetValue(window, true);
        window.Close();
    }

    /// <summary>Проверяет все кнопки: окно остаётся открытым до ACK, а повторный клик не создаёт второй запрос.</summary>
    /// <param name="buttonName">Имя производственной кнопки.</param>
    /// <param name="expected">Ожидаемое решение пользователя.</param>
    [Theory]
    [InlineData("BtnWhitelist", NetworkAlertResolution.WhitelistProgram)]
    [InlineData("BtnBlockAlways", NetworkAlertResolution.BlacklistProgram)]
    [InlineData("BtnAllowSession", NetworkAlertResolution.AllowSession)]
    [InlineData("BtnBlockSession", NetworkAlertResolution.BlockSession)]
    [InlineData("BtnAllowPort", NetworkAlertResolution.AllowPort)]
    [InlineData("BtnClose", NetworkAlertResolution.Defer)]
    [InlineData("BtnDefer", NetworkAlertResolution.Defer)]
    public void Action_WaitsForAcknowledgementAndClosesOnlyAfterSuccess(string buttonName, NetworkAlertResolution expected)
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sent = new List<ResolveNetworkAlertRequest>();
            var window = CreateWindow(client, (request, _) => { sent.Add(request); return acknowledgement.Task; });
            try
            {
                window.Show();
                var button = Button(window, buttonName);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.True(window.IsVisible);
                Assert.Null(window.Resolution);
                Assert.False(button.IsEnabled);
                Assert.Equal(expected, Assert.Single(sent).Resolution);
                acknowledgement.SetResult(true);

                PumpUntil(() => !window.IsVisible);
                Assert.Equal(expected, window.Resolution);
                Assert.Single(sent);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Проверяет отказ службы: сохраняется то же окно, таймер останавливается и доступен явный повтор.</summary>
    [Fact]
    public void Action_Rejected_KeepsSameWindowAndAllowsExplicitRetry()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var window = CreateWindow(client, (_, _) => { calls++; return acknowledgement.Task; });
            try
            {
                window.Show();
                var button = Button(window, "BtnWhitelist");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                acknowledgement.SetResult(false);
                PumpUntil(() => button.IsEnabled);

                Assert.True(window.IsVisible);
                Assert.Null(window.Resolution);
                var timer = Assert.IsType<DispatcherTimer>(typeof(NetworkConnectionAlertWindow)
                    .GetField("_countdownTimer", PrivateInstance)?.GetValue(window));
                Assert.False(timer.IsEnabled);
                Assert.Equal(LocalizationService.Instance["NetAlertApplyFailed"],
                    Assert.IsType<TextBlock>(window.FindName("TxtCountdown")).Text);
                Assert.Equal(1, calls);

                acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(2, calls);
                acknowledgement.SetResult(true);
                PumpUntil(() => !window.IsVisible);
            }
            finally { Cleanup(window); }
        });

    /// <summary>После отказа крестик отправляет Defer и не подменяет скрытие изменением сетевой политики.</summary>
    [Fact]
    public void Action_Rejected_AllowsForceCloseViaCloseButton()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var sent = new List<ResolveNetworkAlertRequest>();
            var window = CreateWindow(client, (request, _) =>
            {
                sent.Add(request);
                return Task.FromResult(request.Resolution == NetworkAlertResolution.Defer);
            });
            try
            {
                window.Show();
                var button = Button(window, "BtnWhitelist");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => button.IsEnabled);
                Assert.True(window.IsVisible);

                // Откладывание подтверждается отдельно и не повторяет отклонённое разрешение.
                var closeButton = Button(window, "BtnClose");
                closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => !window.IsVisible);
                Assert.False(window.IsVisible);
                Assert.Equal(NetworkAlertResolution.Defer, sent[1].Resolution);
                Assert.Equal(NetworkAlertResolution.Defer, window.Resolution);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Проверяет, что тайм-аут доставки не превращается в успешное решение и не закрывает карточку.</summary>
    [Fact]
    public void Action_TransportTimesOut_KeepsWindowAndShowsFailure()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var window = CreateWindow(client, (_, _) => Task.FromException<bool>(new OperationCanceledException("Controlled transport timeout")));
            try
            {
                window.Show();
                Button(window, "BtnBlockSession").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.True(window.IsVisible);
                Assert.Null(window.Resolution);
                Assert.False(Button(window, "BtnBlockSession").IsEnabled);
                Assert.True(Button(window, "BtnRetry").IsEnabled);
                Assert.Equal(LocalizationService.Instance["NetAlertUnknownOutcome"],
                    Assert.IsType<TextBlock>(window.FindName("TxtCountdown")).Text);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Проверяет, что истечение отсчёта отправляет BlockSession и также ожидает подтверждения.</summary>
    [Fact]
    public void CountdownExpires_SendsSessionBlockAndWaitsForAcknowledgement()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ResolveNetworkAlertRequest? sent = null;
            var window = CreateWindow(client, (request, _) => { sent = request; return acknowledgement.Task; });
            try
            {
                window.Show();
                var windowType = typeof(NetworkConnectionAlertWindow);
                windowType.GetField("_remainingSeconds", PrivateInstance)?.SetValue(window, 1);
                windowType.GetField("_isPausedByHover", PrivateInstance)?.SetValue(window, false);
                var tick = windowType.GetMethod("OnCountdownTick", PrivateInstance)
                    ?? throw new MissingMethodException("Не найден обработчик производственного таймера.");
                tick.Invoke(window, [null, EventArgs.Empty]);

                Assert.NotNull(sent);
                Assert.Equal(NetworkAlertResolution.BlockSession, sent.Resolution);
                Assert.True(window.IsVisible);
                Assert.Null(window.Resolution);
                acknowledgement.SetResult(true);
                PumpUntil(() => !window.IsVisible);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Проверяет отключение недоступных сессионных действий согласно capability конкретного алерта.</summary>
    /// <param name="canAllow">Доступно ли временное разрешение.</param>
    /// <param name="canBlock">Доступна ли временная блокировка.</param>
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void UnsupportedSessionCapability_DisablesCorrespondingButtons(bool canAllow, bool canBlock)
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var calls = 0;
            var window = CreateWindow(client, (_, _) => { calls++; return Task.FromResult(false); }, canAllow, canBlock);
            try
            {
                window.Show();

                Assert.Equal(canAllow, Button(window, "BtnAllowSession").IsEnabled);
                Assert.Equal(canBlock, Button(window, "BtnBlockSession").IsEnabled);
                Assert.True(Button(window, "BtnClose").IsEnabled);
                Assert.True(Button(window, "BtnDefer").IsEnabled);
                Assert.Equal(0, calls);
                Assert.True(window.IsVisible);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Потеря ACK разрешает только повтор той же операции, сохраняя её действие и идентификатор.</summary>
    [Fact]
    public void UnknownOutcome_RetryUsesSameOperationAndPreventsOppositeAction()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var requests = new List<ResolveNetworkAlertRequest>();
            var window = CreateWindow(client, (request, _) =>
            {
                requests.Add(request);
                return requests.Count == 1
                    ? Task.FromException<bool>(new OperationCanceledException("Controlled lost ACK"))
                    : Task.FromResult(true);
            });
            try
            {
                window.Show();
                Button(window, "BtnWhitelist").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(Button(window, "BtnRetry").IsEnabled);
                Assert.False(Button(window, "BtnBlockAlways").IsEnabled);
                Button(window, "BtnRetry").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => !window.IsVisible);
                Assert.Equal(2, requests.Count);
                Assert.False(string.IsNullOrWhiteSpace(requests[0].OperationId));
                Assert.Equal(requests[0].OperationId, requests[1].OperationId);
                Assert.All(requests, request => Assert.Equal(NetworkAlertResolution.WhitelistProgram, request.Resolution));
            }
            finally { Cleanup(window); }
        });

    /// <summary>Во время ожидающего ACK повтора принудительные нажатия не запускают третью или противоположную операцию.</summary>
    [Fact]
    public void UnknownOutcome_PendingRetryRejectsAdditionalClicks()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var requests = new List<ResolveNetworkAlertRequest>();
            var retry = new TaskCompletionSource<ResolveNetworkAlertResultPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = CreateWindow(client, (_, _) => Task.FromResult(false), detailedSender: (request, _) =>
            {
                requests.Add(request);
                return requests.Count == 1
                    ? Task.FromResult(new ResolveNetworkAlertResultPayload(false, "", NetworkAlertResolutionStatus.UnknownOutcome, request.OperationId))
                    : retry.Task;
            });
            try
            {
                window.Show();
                Button(window, "BtnWhitelist").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => Button(window, "BtnRetry").IsEnabled);
                Button(window, "BtnRetry").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Button(window, "BtnRetry").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Button(window, "BtnBlockAlways").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(2, requests.Count);
                Assert.Equal(requests[0].OperationId, requests[1].OperationId);
                Assert.All(requests, request => Assert.Equal(NetworkAlertResolution.WhitelistProgram, request.Resolution));
                retry.SetResult(new(true, "", NetworkAlertResolutionStatus.Applied, requests[1].OperationId, requests[1].Resolution));
                PumpUntil(() => !window.IsVisible);
                Assert.Equal(NetworkAlertResolution.WhitelistProgram, window.Resolution);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Состояние Closing резервируется до анимации и блокирует второй клик даже при принудительном RaiseEvent.</summary>
    [Fact]
    public void ClosingAnimation_RejectsAdditionalPolicyAction()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var requests = new List<ResolveNetworkAlertRequest>();
            var window = CreateWindow(client, (request, _) => { requests.Add(request); return Task.FromResult(true); });
            try
            {
                window.Show();
                Button(window, "BtnClose").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Button(window, "BtnWhitelist").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => !window.IsVisible);
                Assert.Equal(NetworkAlertResolution.Defer, Assert.Single(requests).Resolution);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Отключение и внешнее завершение закрывают карточку без отправки BlockSession.</summary>
    /// <param name="enabled">Продолжает ли работать детектор.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServiceSupersedesAlert_ClosesWithoutSendingPolicy(bool enabled)
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var sends = 0;
            var window = CreateWindow(client, (_, _) => { sends++; return Task.FromResult(true); });
            try
            {
                window.Show();
                window.UpdateFromService(new(enabled, null, NetworkAlertResolutionStatus.Superseded));
                PumpUntil(() => !window.IsVisible);
                Assert.Equal(0, sends);
                Assert.Null(window.Resolution);
            }
            finally { Cleanup(window); }
        });

    /// <summary>После восстановления метаданных та же карточка получает число соединений и доступные действия.</summary>
    [Fact]
    public void ServiceUpdatesGroup_RefreshesSummaryAndCapabilities()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var window = CreateWindow(client, (_, _) => Task.FromResult(true), false, false);
            try
            {
                window.Show();
                window.UpdateFromService(new(true, new NetworkConnectionAlertDto
                {
                    AlertId = "ack-regression", ProcessName = "client.exe", ProcessPath = @"C:\Apps\client.exe",
                    RemoteAddress = "8.8.8.8", RemotePort = 443, ConnectionCount = 17,
                    CanAllowSession = true, CanBlockSession = true, LastSeenAt = DateTime.UtcNow, IsActive = false
                }, NetworkAlertResolutionStatus.Conflict));
                Assert.True(Button(window, "BtnAllowSession").IsEnabled);
                Assert.Contains("17", Assert.IsType<TextBlock>(window.FindName("TxtConnectionSummary")).Text);
                Assert.Contains(LocalizationService.Instance["NetAlertConnectionInactive"],
                    Assert.IsType<TextBlock>(window.FindName("TxtConnectionSummary")).Text);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Пустой путь по окончании отсчёта освобождает очередь через Defer, не создавая глобального разрешения порта.</summary>
    [Fact]
    public void UnknownPath_CountdownDefersWithoutPolicyChange()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var requests = new List<ResolveNetworkAlertRequest>();
            var window = CreateWindow(client, (request, _) => { requests.Add(request); return Task.FromResult(true); }, false, false);
            try
            {
                window.Show();
                typeof(NetworkConnectionAlertWindow).GetField("_remainingSeconds", PrivateInstance)!.SetValue(window, 1);
                typeof(NetworkConnectionAlertWindow).GetField("_isPausedByHover", PrivateInstance)!.SetValue(window, false);
                typeof(NetworkConnectionAlertWindow).GetMethod("OnCountdownTick", PrivateInstance)!.Invoke(window, [null, EventArgs.Empty]);
                PumpUntil(() => !window.IsVisible);
                Assert.Equal(NetworkAlertResolution.Defer, Assert.Single(requests).Resolution);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Точный отказ службы сохраняет объяснение вместо общего сообщения и позволяет безопасный новый выбор.</summary>
    [Fact]
    public void DetailedUnsupported_ShowsServiceReasonAndKeepsWindow()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            const string reason = "Controlled current-mode restriction";
            var window = CreateWindow(client, (_, _) => Task.FromResult(true), detailedSender: (request, _) =>
                Task.FromResult(new ResolveNetworkAlertResultPayload(false, reason, NetworkAlertResolutionStatus.Unsupported, request.OperationId)));
            try
            {
                window.Show();
                Button(window, "BtnAllowSession").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(window.IsVisible);
                Assert.Equal(reason, Assert.IsType<TextBlock>(window.FindName("TxtCountdown")).Text);
                Assert.True(Button(window, "BtnDefer").IsEnabled);
                Assert.False(Button(window, "BtnRetry").IsVisible);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Завершённый другим клиентом запрос не превращается в ошибку нового правила и закрывается без повторной операции.</summary>
    [Fact]
    public void DetailedSuperseded_ClosesWithoutClaimingChosenActionApplied()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var window = CreateWindow(client, (_, _) => Task.FromResult(true), detailedSender: (request, _) =>
                Task.FromResult(new ResolveNetworkAlertResultPayload(false, "Already resolved elsewhere", NetworkAlertResolutionStatus.Superseded, request.OperationId)));
            try
            {
                window.Show();
                Button(window, "BtnWhitelist").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => !window.IsVisible);
                Assert.Null(window.Resolution);
            }
            finally { Cleanup(window); }
        });
}
