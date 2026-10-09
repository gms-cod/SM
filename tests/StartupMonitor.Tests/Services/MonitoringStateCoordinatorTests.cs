using System;
using System.IO;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class MonitoringStateCoordinatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonDataStore _store;

    public MonitoringStateCoordinatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SM_StateCoordTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var configPath = Path.Combine(_tempDir, "settings.json");
        _store = new JsonDataStore(configPath);
    }

    [Fact]
    public void InitialState_MatchesSettings()
    {
        var coord = new MonitoringStateCoordinator(_store);
        Assert.False(coord.IsPaused);
    }

    [Fact]
    public void SetPaused_TogglesState_AndFiresEvent()
    {
        var coord = new MonitoringStateCoordinator(_store);
        bool eventFired = false;
        bool newState = false;

        coord.StateChanged += (_, paused) =>
        {
            eventFired = true;
            newState = paused;
        };

        coord.SetPaused(true);
        Assert.True(coord.IsPaused);
        Assert.True(eventFired);
        Assert.True(newState);

        // Проверяем сохранение в настройках JsonDataStore
        var cfg = _store.Load();
        Assert.True(cfg.Settings.IsMonitoringPaused);

        // Повторный вызов с тем же значением не должен повторно вызывать событие
        eventFired = false;
        coord.SetPaused(true);
        Assert.False(eventFired);

        // Возобновление
        coord.SetPaused(false);
        Assert.False(coord.IsPaused);
        Assert.True(eventFired);
        Assert.False(newState);

        cfg = _store.Load();
        Assert.False(cfg.Settings.IsMonitoringPaused);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
