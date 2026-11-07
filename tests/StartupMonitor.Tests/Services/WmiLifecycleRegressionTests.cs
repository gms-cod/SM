using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет завершение debounce при отмене до первого сканирования без подключения к WMI системы.</summary>
public sealed class WmiLifecycleRegressionTests
{
    /// <summary>Даже отмена первого WaitAsync завершает writer и worker до окончания ExecuteAsync.</summary>
    [Fact]
    public async Task CancelledInitialScan_CompletesDebounceChannel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WmiLifecycle_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            using var notifications = new NotificationService(new InitialScanTracker(0));
            using var watcher = new WmiSubscriptionWatcher(store, new BlacklistManager(store), notifications,
                NullLogger<WmiSubscriptionWatcher>.Instance, new MonitoringStateCoordinator(store));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await watcher.StartAsync(cancelled.Token);
            var execution = watcher.ExecuteTask ?? throw new InvalidOperationException("Задача службы не создана.");
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            var field = typeof(WmiSubscriptionWatcher).GetField("_rescanRequests", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Канал debounce отсутствует.");
            var channel = (Channel<string>)(field.GetValue(watcher) ?? throw new InvalidOperationException("Канал не создан."));
            Assert.False(channel.Writer.TryWrite("LateEvent"));
            await channel.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
