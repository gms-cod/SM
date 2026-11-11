using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет границы наблюдения входящих соединений без изменения реальных сетевых правил.</summary>
public sealed class IncomingTcpObserverTests
{
    /// <summary>Сетевой фильтр исключает локальные IPv4 и IPv6, включая отображённые IPv4-адреса.</summary>
    /// <param name="value">Адрес удалённого узла.</param>
    /// <param name="expected">Ожидаемый признак внутреннего адреса.</param>
    [Theory]
    [InlineData("fc00::1234", true)]
    [InlineData("fd12::1234", true)]
    [InlineData("::ffff:192.168.1.7", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    public void AddressFilter_HandlesIpv6AndMappedIpv4(string value, bool expected)
    {
        Assert.Equal(expected, IncomingTcpObserver.IsInternalAddress(IPAddress.Parse(value)));
    }

    /// <summary>Настройка выключена по умолчанию и сохраняется при изменении через настоящий IPC-обработчик.</summary>
    /// <returns>Задача проверки записи настройки.</returns>
    [Fact]
    public async Task Setting_IsOptIn_AndRoundTripsThroughServer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "incoming-tcp-setting-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            Assert.False(store.GetSettings().IncomingTcpObservationsEnabled);
            var server = (PipeServer)RuntimeHelpers.GetUninitializedObject(typeof(PipeServer));
            var field = typeof(PipeServer).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic);
            var method = typeof(PipeServer).GetMethod("ProcessMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            Assert.NotNull(method);
            field.SetValue(server, store);
            async Task<PipeMessage> Send(string type, string payload = "") => await Assert.IsType<Task<PipeMessage>>(method.Invoke(server,
                new object?[] { new PipeMessage { Type = type, Payload = payload }, CancellationToken.None, null }));
            Assert.Equal(PipeMessageTypes.Success, (await Send(PipeMessageTypes.SetIncomingTcpObservations, "true")).Type);
            Assert.Equal("true", (await Send(PipeMessageTypes.GetIncomingTcpObservations)).Payload);
            Assert.True(new JsonDataStore(directory).GetSettings().IncomingTcpObservationsEnabled);
            Assert.Equal(PipeMessageTypes.Success, (await Send(PipeMessageTypes.SetIncomingTcpObservations, "false")).Type);
            Assert.Equal("false", (await Send(PipeMessageTypes.GetIncomingTcpObservations)).Payload);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Проверяет фильтр локального трафика, ограничение частоты и данные события, доходящего до очереди UI.</summary>
    /// <returns>Задача получения уведомления.</returns>
    [Fact]
    public async Task Observation_FiltersPrivateAndLimitsNotifications()
    {
        var directory = Path.Combine(Path.GetTempPath(), "incoming-tcp-events-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            using var notifications = new NotificationService(new InitialScanTracker(0));
            using var observer = new IncomingTcpObserver(store, notifications, NullLogger<IncomingTcpObserver>.Instance);
            var received = new TaskCompletionSource<List<PipeMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
            notifications.MessagesBatchReady += (_, batch) => received.TrySetResult(batch);
            var enabledField = typeof(IncomingTcpObserver).GetField("_enabled", BindingFlags.Instance | BindingFlags.NonPublic);
            var publish = typeof(IncomingTcpObserver).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(enabledField);
            Assert.NotNull(publish);
            enabledField.SetValue(observer, true);
            void Emit(string address) => publish.Invoke(observer, new object[] { 123, IPAddress.Parse("10.0.0.2"), 443,
                IPAddress.Parse(address), 51000, DateTime.UtcNow });
            Emit("192.168.0.1");
            Emit("8.8.8.8");
            Emit("1.1.1.1");
            var batch = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var message = Assert.Single(batch.Where(m => m.Type == PipeMessageTypes.IncomingTcpObserved));
            var observed = JsonSerializer.Deserialize<IncomingTcpObservation>(message.Payload);
            Assert.NotNull(observed);
            Assert.Equal("8.8.8.8", observed.RemoteAddress);
            Assert.Equal(443, observed.LocalPort);
            Assert.Equal(123, observed.ProcessId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
