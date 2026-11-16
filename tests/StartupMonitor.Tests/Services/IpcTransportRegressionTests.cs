using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
public sealed class IpcTransportRegressionTests
{
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task DisableEntry_ServiceRefusal_IsReturnedToCaller()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new PipeClient();
        await using var server = await AttachTransportAsync(client, timeout.Token);
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var pending = client.DisableEntryAsync("entry-id");
        Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
        var response = new PipeMessage { Type = PipeMessageTypes.Error, Payload = "Отказ системы" };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), timeout.Token);
        var result = await pending.WaitAsync(timeout.Token);
        Assert.False(result.Success);
        Assert.Equal("Отказ системы", result.Error);
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task CancellationAfterRequestWasRead_ClosesTransportBeforeNextRequest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new PipeClient();
        await using var server = await AttachTransportAsync(client, timeout.Token);
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        using var cancelRequest = new CancellationTokenSource();
        var pending = client.SendAsync(new PipeMessage { Type = "ProbeA" }, cancelRequest.Token);
        Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
        cancelRequest.Cancel();
        Assert.Null(await pending.WaitAsync(timeout.Token));
        Assert.False(client.IsConnected);
        Assert.Null(await client.SendAsync(new PipeMessage { Type = "ProbeB" }, timeout.Token));

        // Verifies component behavior and assertions.
        await using var replacement = await AttachTransportAsync(client, timeout.Token);
        using var replacementReader = new StreamReader(replacement, leaveOpen: true);
        using var replacementWriter = new StreamWriter(replacement, leaveOpen: true) { AutoFlush = true };
        var next = client.SendAsync(new PipeMessage { Type = "ProbeB" }, timeout.Token);
        Assert.NotNull(await replacementReader.ReadLineAsync(timeout.Token));
        await replacementWriter.WriteLineAsync(JsonSerializer.Serialize(new PipeMessage { Type = "ProbeBResponse" }));
        Assert.Equal("ProbeBResponse", (await next.WaitAsync(timeout.Token))?.Type);
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task MalformedResponse_ReturnsFailureAndDisconnects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new PipeClient();
        await using var server = await AttachTransportAsync(client, timeout.Token);
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var request = client.SendAsync(new PipeMessage { Type = "ProbeMalformed" }, timeout.Token);
        await reader.ReadLineAsync(timeout.Token);
        await writer.WriteLineAsync("{malformed-json");
        Assert.Null(await request.WaitAsync(timeout.Token));
        Assert.False(client.IsConnected);
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task IncompleteResponse_ReturnsFailureAndDisconnects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new PipeClient();
        var disconnected = 0;
        client.Disconnected += (_, _) => disconnected++;
        await using var server = await AttachTransportAsync(client, timeout.Token);
        using var reader = new StreamReader(server, leaveOpen: true);
        var request = client.SendAsync(new PipeMessage { Type = "ProbeIncomplete" }, timeout.Token);
        Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
        using (var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true })
        {
            await writer.WriteAsync("{\"Type\":\"Incomplete\"}".AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);
        }
        await server.DisposeAsync();
        Assert.Null(await request.WaitAsync(timeout.Token));
        Assert.False(client.IsConnected);
        Assert.Equal(1, disconnected);
        Assert.Null(await client.SendAsync(new PipeMessage { Type = "AfterIncomplete" }, timeout.Token));
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task OversizedResponse_ReturnsFailureAndDisconnects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new PipeClient();
        var disconnected = 0;
        client.Disconnected += (_, _) => disconnected++;
        await using var server = await AttachTransportAsync(client, timeout.Token);
        var transportReader = GetField<StreamReader>(client, "_reader");
        SetField(client, "_responseFrames", new BoundedLineReader(transportReader, 32));
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var request = client.SendAsync(new PipeMessage { Type = "ProbeOversized" }, timeout.Token);
        Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
        await writer.WriteLineAsync(new string('x', 64).AsMemory(), timeout.Token);
        Assert.Null(await request.WaitAsync(timeout.Token));
        Assert.False(client.IsConnected);
        Assert.Equal(1, disconnected);
        Assert.Null(await client.SendAsync(new PipeMessage { Type = "AfterOversized" }, timeout.Token));
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task MetricsSubscriptions_AreIdempotentIsolatedAndReleasedOnDisconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var enricher = new ProcessMetadataEnricher();
        using var metrics = new ProcessMetricsTracker(enricher, new ProcessStartupLinker());
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var server = (PipeServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PipeServer));
        SetField(server, "_uiClientsSync", new object());
        SetField(server, "_logger", NullLogger<PipeServer>.Instance);
        SetField(server, "_metricsTracker", metrics);
        SetField(server, "_alertClaims", new NetworkAlertClaimRegistry());
        var first = await AttachCommandSessionAsync(server, timeout.Token);
        var second = await AttachCommandSessionAsync(server, timeout.Token);
        using var firstClient = first.Client;
        using var secondClient = second.Client;
        using var firstReader = new StreamReader(firstClient, leaveOpen: true);
        using var secondReader = new StreamReader(secondClient, leaveOpen: true);
        // Verifies component behavior and assertions.
        using (var firstWriter = new StreamWriter(firstClient, leaveOpen: true) { AutoFlush = true })
        using (var secondWriter = new StreamWriter(secondClient, leaveOpen: true) { AutoFlush = true })
        {
            await SendSubscriptionAsync(secondWriter, secondReader, PipeMessageTypes.UnsubscribeProcessMetrics, timeout.Token);
            Assert.Equal(0, metrics.SubscriberCount);
            await SendSubscriptionAsync(firstWriter, firstReader, PipeMessageTypes.SubscribeProcessMetrics, timeout.Token);
            await SendSubscriptionAsync(firstWriter, firstReader, PipeMessageTypes.SubscribeProcessMetrics, timeout.Token);
            Assert.Equal(1, metrics.SubscriberCount);
            await SendSubscriptionAsync(secondWriter, secondReader, PipeMessageTypes.UnsubscribeProcessMetrics, timeout.Token);
            Assert.Equal(1, metrics.SubscriberCount);
            await SendSubscriptionAsync(secondWriter, secondReader, PipeMessageTypes.SubscribeProcessMetrics, timeout.Token);
            Assert.Equal(2, metrics.SubscriberCount);
            await SendSubscriptionAsync(secondWriter, secondReader, PipeMessageTypes.UnsubscribeProcessMetrics, timeout.Token);
            await SendSubscriptionAsync(secondWriter, secondReader, PipeMessageTypes.UnsubscribeProcessMetrics, timeout.Token);
            Assert.Equal(1, metrics.SubscriberCount);
        }

        firstClient.Dispose();
        await first.Handling.WaitAsync(timeout.Token);
        Assert.Equal(0, metrics.SubscriberCount);
        secondClient.Dispose();
        await second.Handling.WaitAsync(timeout.Token);
        Assert.Equal(0, server.ActiveUiClients);
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task BoundedReader_PreservesConsecutiveFrames()
    {
        var frames = new BoundedLineReader(new StringReader("first\r\nsecond\n"), 16);
        Assert.Equal("first", await frames.ReadLineAsync());
        Assert.Equal("second", await frames.ReadLineAsync());
        Assert.Null(await frames.ReadLineAsync());
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task BoundedReader_RejectsOversizedUnterminatedFrame()
    {
        var frames = new BoundedLineReader(new StringReader(new string('x', 4096)), 32);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await frames.ReadLineAsync());
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task BoundedReader_RejectsIncompleteFrame()
    {
        var frames = new BoundedLineReader(new StringReader("{\"type\":\"unfinished\"}"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await frames.ReadLineAsync());
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Theory]
    [InlineData(PipeMessageTypes.GetStatus, true)]
    [InlineData(PipeMessageTypes.GetNetworkTrafficHistory, true)]
    [InlineData(PipeMessageTypes.ResolveNetworkAlert, false)]
    [InlineData(PipeMessageTypes.EvaluateNetworkAlert, false)]
    [InlineData(PipeMessageTypes.GetPendingNetworkAlerts, false)]
    [InlineData(PipeMessageTypes.GetNetworkAlertStatus, false)]
    [InlineData(PipeMessageTypes.GetAiConfig, false)]
    [InlineData(PipeMessageTypes.KillProcessById, false)]
    [InlineData(PipeMessageTypes.BlockIp, false)]
    [InlineData(PipeMessageTypes.ShutdownService, false)]
    [InlineData("GetFutureSecret", false)]
    public void Authorization_OrdinaryClientUsesExplicitAllowlist(string type, bool allowed)
    {
        Assert.Equal(allowed, PipeServer.IsCommandAllowed(type, false));
        Assert.True(PipeServer.IsCommandAllowed(type, true));
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    [Fact]
    public async Task EventServer_ReconnectionsAndStop_ReleaseClients()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tracker = new InitialScanTracker(0);
        using var notifications = new NotificationService(tracker);
        var name = "StartupMonitorEventsTest_" + Guid.NewGuid().ToString("N");
        using var server = new EventPipeServer(notifications, NullLogger<EventPipeServer>.Instance, name);
        await server.StartAsync(timeout.Token);
        for (var i = 0; i < 12; i++)
        {
            var acceptedBefore = server.TotalAcceptedClients;
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeout.Token);
            while (server.TotalAcceptedClients <= acceptedBefore) await Task.Delay(1, timeout.Token);
            using var reader = new StreamReader(client, leaveOpen: true);
            notifications.Enqueue(new PipeMessage { Type = "LifecycleProbe", Payload = i.ToString() });
            var line = await reader.ReadLineAsync(timeout.Token);
            Assert.Contains("LifecycleProbe", line);
        }
        await server.StopAsync(timeout.Token);
        Assert.Equal(0, server.ConnectedClientCount);
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    private static async Task<NamedPipeServerStream> AttachTransportAsync(PipeClient client, CancellationToken ct)
    {
        var name = "StartupMonitorTransportTest_" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 65536, 65536);
        var waiting = server.WaitForConnectionAsync(ct);
        var transport = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await transport.ConnectAsync(ct);
        await waiting;
        SetField(client, "_pipe", transport);
        SetField(client, "_reader", new StreamReader(transport));
        SetField(client, "_writer", new StreamWriter(transport) { AutoFlush = true });
        return server;
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Не найдено транспортное поле {name}.");
        field.SetValue(target, value);
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    private static T GetField<T>(object target, string name) where T : class =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T
        ?? throw new InvalidOperationException($"Поле {name} отсутствует или имеет неверный тип.");

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    private static async Task<(NamedPipeClientStream Client, Task Handling)> AttachCommandSessionAsync(PipeServer server, CancellationToken ct)
    {
        var name = "StartupMonitorMetricsSession_" + Guid.NewGuid().ToString("N");
        var transport = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            var connected = transport.WaitForConnectionAsync(ct);
            await client.ConnectAsync(ct);
            await connected;
            var method = typeof(PipeServer).GetMethod("HandleClientAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Обработчик командной сессии не найден.");
            var handling = method.Invoke(server, [transport, ct]) as Task
                ?? throw new InvalidOperationException("Обработчик командной сессии не вернул задачу.");
            return (client, handling);
        }
        catch
        {
            client.Dispose();
            transport.Dispose();
            throw;
        }
    }

    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for IpcTransportRegressionTests.cs.</summary>
    private static async Task SendSubscriptionAsync(StreamWriter writer, StreamReader reader, string type, CancellationToken ct)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(new PipeMessage { Type = type }).AsMemory(), ct);
        var line = await reader.ReadLineAsync(ct);
        Assert.NotNull(line);
        Assert.Equal(PipeMessageTypes.Success, JsonSerializer.Deserialize<PipeMessage>(line)?.Type);
    }
}
