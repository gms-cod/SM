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

/// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
/// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
[Collection("WpfNetworkRendering")]
public sealed class NetworkAlertAcknowledgementTests
{
    private readonly NetworkRenderingTestHost _host;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    public NetworkAlertAcknowledgementTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    private static ButtonBase Button(NetworkConnectionAlertWindow window, string name) =>
        Assert.IsAssignableFrom<ButtonBase>(window.FindName(name));

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    private static void Cleanup(NetworkConnectionAlertWindow window)
    {
        var confirmed = typeof(NetworkConnectionAlertWindow).GetField("_resolutionConfirmed", PrivateInstance)
            ?? throw new MissingFieldException("Не найдено состояние подтверждённого закрытия диалога.");
        confirmed.SetValue(window, true);
        window.Close();
    }

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

                // Verifies component behavior and assertions.
                var closeButton = Button(window, "BtnClose");
                closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpUntil(() => !window.IsVisible);
                Assert.False(window.IsVisible);
                Assert.Equal(NetworkAlertResolution.Defer, sent[1].Resolution);
                Assert.Equal(NetworkAlertResolution.Defer, window.Resolution);
            }
            finally { Cleanup(window); }
        });

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkAlertAcknowledgementTests.cs.</summary>
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
