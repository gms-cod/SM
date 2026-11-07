using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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
        bool canAllowSession = true, bool canBlockSession = true) => new(
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
        }, client, countdownSeconds: 3600, resolutionSender: sender);

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
    [InlineData("BtnClose", NetworkAlertResolution.BlockSession)]
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

    /// <summary>Проверяет, что при отказе службы закрытие окна крестиком не зависает в дедлоке, а гарантированно закрывает окно.</summary>
    [Fact]
    public void Action_Rejected_AllowsForceCloseViaCloseButton()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var window = CreateWindow(client, (_, _) => Task.FromResult(false));
            try
            {
                window.Show();
                var button = Button(window, "BtnWhitelist");
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => button.IsEnabled);
                Assert.True(window.IsVisible);

                // После отказа службы клик по крестику должен гарантированно закрыть окно
                var closeButton = Button(window, "BtnClose");
                closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => !window.IsVisible);
                Assert.False(window.IsVisible);
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
                Assert.True(Button(window, "BtnBlockSession").IsEnabled);
                Assert.Equal(LocalizationService.Instance["NetAlertApplyFailed"],
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
                Assert.Equal(canBlock, Button(window, "BtnClose").IsEnabled);
                Assert.Equal(0, calls);
                Assert.True(window.IsVisible);
            }
            finally { Cleanup(window); }
        });
}
