using System.IO.Pipes;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.Tests.UI;

/// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
[Collection("WpfNetworkRendering")]
public sealed class IncomingTcpDeliveryTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    public IncomingTcpDeliveryTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    [Theory]
    [InlineData("10.0.0.3", "8.8.8.8", "10.0.0.3:443", "8.8.8.8:51000")]
    [InlineData("fd00::3", "2001:4860:4860::8888", "[fd00::3]:443", "[2001:4860:4860::8888]:51000")]
    public void Observation_ReachesViewModelAcrossEventPipe(string localAddress, string remoteAddress, string expectedLocal, string expectedRemote)
    {
        _host.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "incoming-delivery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var previous = MainViewModel.CustomConfigPath;
            MainViewModel.CustomConfigPath = Path.Combine(directory, "ui.json");
            try
            {
                using var notifications = new NotificationService(new InitialScanTracker(0));
                using var server = new EventPipeServer(notifications, NullLogger<EventPipeServer>.Instance, "incoming-test-" + Guid.NewGuid().ToString("N"));
                using var observer = new IncomingTcpObserver(new JsonDataStore(directory), notifications, NullLogger<IncomingTcpObserver>.Instance);
                using var uiClient = new PipeClient();
                using var model = new MainViewModel(uiClient);
                model.SilentMode = false;
                string? text = null;
                model.NotificationRequested += (_, message, _) => text = message;

                var name = (string)typeof(EventPipeServer).GetField("_eventPipeName", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(server)!;
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
                Complete(server.StartAsync(CancellationToken.None));
                Complete(pipe.ConnectAsync(5000));
                PumpUntil(() => server.ConnectedClientCount == 1);
                using var reader = new StreamReader(pipe);
                var read = reader.ReadLineAsync();
                typeof(IncomingTcpObserver).GetField("_enabled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(observer, true);
                typeof(IncomingTcpObserver).GetMethod("Publish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(observer,
                    [1234, IPAddress.Parse(localAddress), 443, IPAddress.Parse(remoteAddress), 51000, DateTime.UtcNow]);
                Complete(read);
                var batch = JsonSerializer.Deserialize<List<PipeMessage>>(read.Result!)!;
                Assert.Contains(batch, item => item.Type == PipeMessageTypes.IncomingTcpObserved);
                typeof(MainViewModel).GetMethod("OnPipeClientEventsReceived", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(model, [uiClient, batch]);
                PumpUntil(() => text is not null);
                Assert.Contains(expectedRemote, text);
                Assert.Contains(expectedLocal, text);
                Assert.Contains("PID 1234", text);
                Complete(server.StopAsync(CancellationToken.None));
            }
            finally
            {
                MainViewModel.CustomConfigPath = previous;
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });
    }

    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    /// <summary>Verifies test scenario for IncomingTcpDeliveryTests.cs.</summary>
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(condition(), "Событие TCP не доставлено за отведённое время.");
    }
}
