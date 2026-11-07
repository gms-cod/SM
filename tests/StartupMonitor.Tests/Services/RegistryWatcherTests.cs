using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

public class RegistryWatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonDataStore _store;
    private readonly BlacklistManager _blacklist;
    private readonly InitialScanTracker _tracker;
    private readonly NotificationService _notifications;
    private readonly MonitoringStateCoordinator _coordinator;

    public RegistryWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SM_RegistryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var configPath = Path.Combine(_tempDir, "test-config.json");
        _store = new JsonDataStore(configPath);
        _blacklist = new BlacklistManager(_store);
        _tracker = new InitialScanTracker();
        _notifications = new NotificationService(_tracker);
        _coordinator = new MonitoringStateCoordinator(_store);
    }

    [Fact]
    public void Dispose_ClearsWatchedKeys_WithoutThrowing()
    {
        var watcher = new RegistryWatcher(
            _store, _blacklist, _notifications,
            new SilentLogger<RegistryWatcher>(),
            _tracker,
            _coordinator);

        var exception = Record.Exception(() => watcher.Dispose());
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _notifications?.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

/// <summary>Минимальная заглушка ILogger для тестов.</summary>
internal class SilentLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
