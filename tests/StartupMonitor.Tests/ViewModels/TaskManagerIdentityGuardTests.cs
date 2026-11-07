using System.Text.Json;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.Tests.ViewModels;

/// <summary>Проверяет отказ небезопасных команд при неизвестной identity или старой службе без подтверждённой capability.</summary>
public sealed class TaskManagerIdentityGuardTests
{
    /// <summary>Вызывает одно из четырёх настоящих действий модели без реальных команд IPC.</summary>
    /// <param name="viewModel">Проверяемая модель.</param>
    /// <param name="process">Выбранный процесс.</param>
    /// <param name="action">Название команды.</param>
    /// <returns>Задача выполнения действия.</returns>
    private static Task Execute(TaskManagerViewModel viewModel, ProcessInfo process, string action) => action switch
    {
        "Kill" => viewModel.KillProcessAsync(process),
        "Block" => viewModel.BlockProcessAsync(process),
        "Suspend" => viewModel.SuspendProcessAsync(process),
        "Resume" => viewModel.ResumeProcessAsync(process),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>Проверяет запрет отправки деструктивной команды без точного времени создания процесса.</summary>
    /// <param name="action">Проверяемая команда.</param>
    /// <returns>Задача проверки обработчика команды.</returns>
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

    /// <summary>Проверяет запрет команды, если служба явно не подтвердила поддержку токена экземпляра.</summary>
    /// <param name="action">Проверяемая команда.</param>
    /// <returns>Задача проверки обработчика команды.</returns>
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

    /// <summary>Проверяет, что пропущенное старой службой поле capability не интерпретируется как поддержка нового протокола.</summary>
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

    /// <summary>Проверяет отсутствие склейки строки и выделения при неизвестном времени хотя бы одного снимка.</summary>
    /// <param name="oldKnown">Известно ли время создания в прежнем снимке.</param>
    /// <param name="newKnown">Известно ли время создания в новом снимке.</param>
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
