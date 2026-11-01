using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет настоящие каналы IPC, отмену обмена, границы кадров и завершение event-сервера.</summary>
public sealed class IpcTransportRegressionTests
{
    /// <summary>Отмена принятого сервером запроса закрывает канал и исключает чтение старого ответа следующим запросом.</summary>
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

        // Поздний ответ остаётся на закрытом старом транспорте; новый транспорт принимает только собственный ответ.
        await using var replacement = await AttachTransportAsync(client, timeout.Token);
        using var replacementReader = new StreamReader(replacement, leaveOpen: true);
        using var replacementWriter = new StreamWriter(replacement, leaveOpen: true) { AutoFlush = true };
        var next = client.SendAsync(new PipeMessage { Type = "ProbeB" }, timeout.Token);
        Assert.NotNull(await replacementReader.ReadLineAsync(timeout.Token));
        await replacementWriter.WriteLineAsync(JsonSerializer.Serialize(new PipeMessage { Type = "ProbeBResponse" }));
        Assert.Equal("ProbeBResponse", (await next.WaitAsync(timeout.Token))?.Type);
    }

    /// <summary>Повреждённый JSON настоящего ответа не выходит исключением и разрывает недостоверный транспорт.</summary>
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

    /// <summary>Несколько кадров в одном чтении не теряют остатки и правильно обрабатывают CRLF.</summary>
    [Fact]
    public async Task BoundedReader_PreservesConsecutiveFrames()
    {
        var frames = new BoundedLineReader(new StringReader("first\r\nsecond\n"), 16);
        Assert.Equal("first", await frames.ReadLineAsync());
        Assert.Equal("second", await frames.ReadLineAsync());
        Assert.Null(await frames.ReadLineAsync());
    }

    /// <summary>Ограничение проверяется до получения перевода строки, включая незавершённый огромный вход.</summary>
    [Fact]
    public async Task BoundedReader_RejectsOversizedUnterminatedFrame()
    {
        var frames = new BoundedLineReader(new StringReader(new string('x', 4096)), 32);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await frames.ReadLineAsync());
    }

    /// <summary>Обрыв внутри кадра отклоняется, а не выдаётся за достоверное сообщение.</summary>
    [Fact]
    public async Task BoundedReader_RejectsIncompleteFrame()
    {
        var frames = new BoundedLineReader(new StringReader("{\"type\":\"unfinished\"}"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await frames.ReadLineAsync());
    }

    /// <summary>Непривилегированный доступ явно ограничен чтением, включая отказ неизвестным новым командам.</summary>
    [Theory]
    [InlineData(PipeMessageTypes.GetStatus, true)]
    [InlineData(PipeMessageTypes.GetNetworkTrafficHistory, true)]
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

    /// <summary>Реальный event-сервер переживает переподключения и освобождает всех клиентов при остановке.</summary>
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

    /// <summary>Подключает настоящий уникальный транспорт к клиенту без обращения к установленной службе.</summary>
    /// <param name="client">Клиент под проверкой.</param>
    /// <param name="ct">Предельное время подключения.</param>
    /// <returns>Серверная сторона изолированного соединения.</returns>
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

    /// <summary>Инъецирует транспортные обёртки, сохраняя настоящий производственный алгоритм SendAsync.</summary>
    /// <param name="client">Проверяемый клиент.</param>
    /// <param name="name">Имя поля транспорта.</param>
    /// <param name="value">Транспортная обёртка.</param>
    private static void SetField(PipeClient client, string name, object value)
    {
        var field = typeof(PipeClient).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Не найдено транспортное поле {name}.");
        field.SetValue(client, value);
    }
}
