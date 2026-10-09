using System.Text.Json;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Delayed;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Converters;

namespace StartupMonitor.Tests.Delayed;

/// <summary>
/// Тесты для функциональности отложенного запуска программ (Delayed Startup, v1.0.17).
/// </summary>
public class DelayedStartupTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonDataStore _store;
    private readonly NotificationService _notifications;
    private readonly DelayedStartupManager _manager;

    public DelayedStartupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SMon_Delayed_Tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _store = new JsonDataStore(Path.Combine(_tempDir, "startup-monitor.json"));
        _notifications = new NotificationService(new InitialScanTracker(1));
        _manager = new DelayedStartupManager(_store, NullLogger<DelayedStartupManager>.Instance, _notifications);
    }

    public void Dispose()
    {
        _notifications.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void PrepareExecutionTarget_UnquotedSimplePath_ReturnsExeAndWorkingDir()
    {
        var dummyExe = Path.Combine(_tempDir, "dummy_app.exe");
        File.WriteAllText(dummyExe, "binary content");

        var (exe, cmd, workingDir) = UserSessionLauncher.PrepareExecutionTarget(dummyExe, null);

        Assert.Equal(dummyExe, exe);
        Assert.Equal(dummyExe, cmd);
        Assert.Equal(_tempDir, workingDir);
    }

    [Fact]
    public void PrepareExecutionTarget_QuotedPathWithArgs_ParsesExeAndCommandLine()
    {
        var dummyExe = Path.Combine(_tempDir, "program with spaces.exe");
        File.WriteAllText(dummyExe, "binary");

        var rawCmd = $"\"{dummyExe}\" --silent -autostart";
        var (exe, cmd, workingDir) = UserSessionLauncher.PrepareExecutionTarget(null, rawCmd);

        Assert.Equal(dummyExe, exe);
        Assert.Equal(rawCmd, cmd);
        Assert.Equal(_tempDir, workingDir);
    }

    [Fact]
    public void PrepareExecutionTarget_EmptyOrWhitespace_ReturnsEmpty()
    {
        var (exe, cmd, workingDir) = UserSessionLauncher.PrepareExecutionTarget("   ", "");
        Assert.Empty(exe);
        Assert.Empty(cmd);
        Assert.Null(workingDir);
    }

    [Fact]
    public void Payload_SetDelayedEntry_SerializationRoundTrip()
    {
        var payload = new SetDelayedEntryPayload("entry-123", 45);
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<SetDelayedEntryPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("entry-123", deserialized.EntryId);
        Assert.Equal(45, deserialized.DelaySeconds);
    }

    [Fact]
    public void Payload_CancelDelayedEntry_SerializationRoundTrip()
    {
        var payload = new CancelDelayedEntryPayload("entry-456");
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<CancelDelayedEntryPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("entry-456", deserialized.EntryId);
    }

    [Fact]
    public void Payload_LaunchDelayedEntry_SerializationRoundTrip()
    {
        var payload = new LaunchDelayedEntryPayload("entry-789");
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<LaunchDelayedEntryPayload>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("entry-789", deserialized.EntryId);
    }

    [Fact]
    public void DelayedStartupManager_SetDelayed_UpdatesStatusAndDelaySeconds()
    {
        var appPath = Path.Combine(_tempDir, "testapp.exe");
        File.WriteAllText(appPath, "mz");

        var entry = new StartupEntry
        {
            Id = "delay-entry-1",
            Name = "Test App",
            FilePath = appPath,
            Location = EntryLocation.StartupFolder,
            Status = EntryStatus.Enabled
        };
        _store.AddEntry(entry);

        var (success, error) = _manager.SetDelayed("delay-entry-1", 60);

        Assert.True(success, error);
        Assert.Null(error);

        var updated = _store.Load().Entries.First(e => e.Id == "delay-entry-1");
        Assert.Equal(EntryStatus.Delayed, updated.Status);
        Assert.Equal(60, updated.DelaySeconds);
    }

    [Fact]
    public void DelayedStartupManager_CancelDelayed_RestoresStatusToEnabled()
    {
        var appPath = Path.Combine(_tempDir, "testapp2.exe");
        File.WriteAllText(appPath, "mz");

        var entry = new StartupEntry
        {
            Id = "delay-entry-2",
            Name = "Test App 2",
            FilePath = appPath,
            Location = EntryLocation.StartupFolder,
            Status = EntryStatus.Delayed,
            DelaySeconds = 30
        };
        _store.AddEntry(entry);

        var (success, error) = _manager.CancelDelayed("delay-entry-2");

        Assert.True(success, error);
        Assert.Null(error);

        var updated = _store.Load().Entries.First(e => e.Id == "delay-entry-2");
        Assert.Equal(EntryStatus.Enabled, updated.Status);
    }

    [Fact]
    public void DelayedStartupManager_SetDelayed_EntryNotFound_ReturnsError()
    {
        var (success, error) = _manager.SetDelayed("non-existent-id", 30);
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("не найдена", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DelayedStartupManager_CancelDelayed_EntryNotFound_ReturnsError()
    {
        var (success, error) = _manager.CancelDelayed("non-existent-id");
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("не найдена", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DelayedStartupManager_LaunchNow_EntryNotFound_ReturnsError()
    {
        var (success, error) = _manager.LaunchNow("non-existent-id");
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("не найдена", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StatusToColorConverter_DelayedStatus_ReturnsAmberBrush()
    {
        var converter = new StatusToColorConverter();
        var result = converter.Convert(EntryStatus.Delayed, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        var brush = Assert.IsAssignableFrom<SolidColorBrush>(result);
        // Должен быть янтарным / оранжевым оттенком (R > 200, G > 100, B < 50)
        Assert.True(brush.Color.R >= 200);
        Assert.True(brush.Color.G >= 100);
        Assert.True(brush.Color.B <= 50);
    }

    [Fact]
    public void StatusToDisplayConverter_DelayedStatus_ReturnsNonEmptyLocalizedText()
    {
        var converter = new StatusToDisplayConverter();
        var result = converter.Convert(EntryStatus.Delayed, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        var text = Assert.IsAssignableFrom<string>(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.True(text.Contains("Отложен", StringComparison.OrdinalIgnoreCase) || text.Contains("Delayed", StringComparison.OrdinalIgnoreCase));
    }
}
