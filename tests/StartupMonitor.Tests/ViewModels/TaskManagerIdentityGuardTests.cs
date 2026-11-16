using System.Text.Json;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.Tests.ViewModels;

/// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
public sealed class TaskManagerIdentityGuardTests
{
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    private static Task Execute(TaskManagerViewModel viewModel, ProcessInfo process, string action) => action switch
    {
        "Kill" => viewModel.KillProcessAsync(process),
        "Block" => viewModel.BlockProcessAsync(process),
        "Suspend" => viewModel.SuspendProcessAsync(process),
        "Resume" => viewModel.ResumeProcessAsync(process),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    [Theory]
    [InlineData("Kill")]
    [InlineData("Block")]
    [InlineData("Suspend")]
    [InlineData("Resume")]
    public async Task Command_UnknownCreationTime_DoesNotSendLegacyPidOnlyAction(string action)
    {
        using var client = new FakeProcessPipeClient();
        using var viewModel = new TaskManagerViewModel(client);
        string? message = null;
        viewModel.ConfirmDialog = (_, _, _, _) => System.Windows.MessageBoxResult.Yes;
        viewModel.MessageDialog = (text, _, _, _) => message = text;
        var process = new ProcessInfo { ProcessId = 32123, ProcessName = "client.exe", StartTime = DateTime.MinValue };

        await Execute(viewModel, process, action);

        Assert.Null(client.LastKillPid);
        Assert.Null(client.LastBlockPid);
        Assert.Null(client.LastSuspendPid);
        Assert.Null(client.LastResumePid);
        Assert.Equal(LocalizationService.Instance["TmProcessIdentityUnavailable"], message);
    }

    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    [Theory]
    [InlineData("Kill")]
    [InlineData("Block")]
    [InlineData("Suspend")]
    [InlineData("Resume")]
    public async Task Command_ServiceDoesNotSupportInstanceGuard_DoesNotSendAction(string action)
    {
        using var client = new FakeProcessPipeClient
        {
            ServiceInfoToReturn = new ServiceInfoPayload(1, "2.0.14", "Running", DateTime.UtcNow, TimeSpan.Zero, false,
                SupportsProcessInstanceCommands: false)
        };
        using var viewModel = new TaskManagerViewModel(client);
        string? message = null;
        viewModel.ConfirmDialog = (_, _, _, _) => System.Windows.MessageBoxResult.Yes;
        viewModel.MessageDialog = (text, _, _, _) => message = text;
        var process = new ProcessInfo { ProcessId = 32123, ProcessName = "client.exe", StartTime = DateTime.UtcNow.AddMinutes(-1) };

        await Execute(viewModel, process, action);

        Assert.Null(client.LastKillPid);
        Assert.Null(client.LastBlockPid);
        Assert.Null(client.LastSuspendPid);
        Assert.Null(client.LastResumePid);
        Assert.Equal(LocalizationService.Instance["TmProcessInstanceCommandsUnavailable"], message);
    }

    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    [Fact]
    public void ServiceInfo_OldJsonWithoutCapability_DefaultsToUnsupported()
    {
        const string json = """
            {"Pid":1,"Version":"2.0.14","ScmStatus":"Running","StartTime":"2026-11-05T10:00:00Z","Uptime":"00:00:01","IsPaused":false}
            """;

        var info = JsonSerializer.Deserialize<ServiceInfoPayload>(json);

        Assert.NotNull(info);
        Assert.False(info.SupportsProcessInstanceCommands);
    }

    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerIdentityGuardTests.cs.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void UpdateSnapshot_IdentityUnknown_ReplacesRowAndClearsSelection(bool oldKnown, bool newKnown)
    {
        using var client = new PipeClient();
        using var viewModel = new TaskManagerViewModel(client);
        var time = DateTime.UtcNow.AddMinutes(-1);
        var previous = new ProcessInfo { ProcessId = 32123, ProcessName = "client.exe", StartTime = oldKnown ? time : DateTime.MinValue };
        var next = new ProcessInfo { ProcessId = 32123, ProcessName = "client.exe", StartTime = newKnown ? time : DateTime.MinValue };
        viewModel.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = [previous] });
        viewModel.SelectedProcess = previous;

        viewModel.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = [next] });

        Assert.Same(next, Assert.Single(viewModel.FilteredProcesses));
        Assert.Null(viewModel.SelectedProcess);
    }
}
