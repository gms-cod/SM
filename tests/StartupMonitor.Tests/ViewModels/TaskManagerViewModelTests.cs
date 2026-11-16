using System.Collections.Generic;
using System.Linq;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

/// <summary>
/// Provides operations for this member.
/// </summary>
public class TaskManagerViewModelTests
{
    private static SystemPerformanceSnapshot CreateSampleSnapshot()
    {
        return new SystemPerformanceSnapshot
        {
            TotalCpuPercent = 25.5,
            TotalMemoryBytes = 16L * 1024 * 1024 * 1024,
            UsedMemoryBytes = 8L * 1024 * 1024 * 1024,
            TotalDiskIoBytesPerSec = 5 * 1024 * 1024,
            TotalProcessesCount = 4,
            Processes = new List<ProcessInfo>
            {
                new()
                {
                    Pid = 100,
                    Name = "GameApp",
                    CpuPercent = 45.0,
                    WorkingSetBytes = 1024L * 1024 * 1024,
                    DiskIoBytesPerSec = 2 * 1024 * 1024,
                    HasWindow = true,
                    Publisher = "GameStudio"
                },
                new()
                {
                    Pid = 200,
                    Name = "BackgroundWorker",
                    CpuPercent = 5.0,
                    WorkingSetBytes = 256L * 1024 * 1024,
                    DiskIoBytesPerSec = 500 * 1024,
                    HasWindow = false,
                    IsService = false,
                    Publisher = "UtilityCo"
                },
                new()
                {
                    Pid = 300,
                    Name = "SpoolerService",
                    CpuPercent = 0.5,
                    WorkingSetBytes = 64L * 1024 * 1024,
                    DiskIoBytesPerSec = 10 * 1024,
                    IsService = true,
                    Publisher = "Microsoft Corporation"
                },
                new()
                {
                    Pid = 400,
                    Name = "AutostartApp",
                    CpuPercent = 12.0,
                    WorkingSetBytes = 512L * 1024 * 1024,
                    DiskIoBytesPerSec = 1024 * 1024,
                    IsStartupEntry = true,
                    StartupEntryId = "auto-1",
                    Publisher = "StartupVendor"
                }
            }
        };
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void UpdateSnapshot_PopulatesMetricsAndProcesses()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        var snapshot = CreateSampleSnapshot();

        vm.UpdateSnapshot(snapshot);

        Assert.Equal(4, vm.FilteredProcesses.Count);
        Assert.Equal("26%", vm.TotalCpuDisplay);
        Assert.Equal(4, int.Parse(vm.ProcessesCountDisplay));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Filter_ByApps_ShowsOnlyAppsWithWindowOrExplorerParent()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SelectedFilter = "apps";

        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("GameApp", vm.FilteredProcesses[0].Name);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Filter_ByServices_ShowsOnlyServices()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SelectedFilter = "services";

        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("SpoolerService", vm.FilteredProcesses[0].Name);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Filter_ByAutostart_ShowsOnlyAutostartProcesses()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SelectedFilter = "autostart";

        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("AutostartApp", vm.FilteredProcesses[0].Name);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Sort_ByCpu_DescendingByDefault()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SortBy("cpu");

        // Highest CPU first: GameApp (45%) -> AutostartApp (12%) -> BackgroundWorker (5%) -> SpoolerService (0.5%)
        Assert.Equal("GameApp", vm.FilteredProcesses[0].Name);
        Assert.Equal("AutostartApp", vm.FilteredProcesses[1].Name);
        Assert.Equal("BackgroundWorker", vm.FilteredProcesses[2].Name);
        Assert.Equal("SpoolerService", vm.FilteredProcesses[3].Name);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Sort_ByRam_OrdersCorrectly()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SortBy("ram"); // desc

        // Highest RAM first: GameApp (1GB) -> AutostartApp (512MB) -> BackgroundWorker (256MB) -> SpoolerService (64MB)
        Assert.Equal("GameApp", vm.FilteredProcesses[0].Name);
        Assert.Equal("AutostartApp", vm.FilteredProcesses[1].Name);
        Assert.Equal("BackgroundWorker", vm.FilteredProcesses[2].Name);
        Assert.Equal("SpoolerService", vm.FilteredProcesses[3].Name);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void SearchText_FiltersByProcessNameOrPublisher()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SearchText = "Microsoft";
        vm.ApplyFilterAndSort();

        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("SpoolerService", vm.FilteredProcesses[0].Name);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe", @"C:\Windows\System32\notepad.exe")]
    [InlineData(@"""C:\Program Files\App\app.exe""", @"C:\Program Files\App\app.exe")]
    [InlineData(@"""C:\Program Files\App\app.exe"" --silent /arg", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe -test", @"C:\Program Files\App\app.exe")]
    [InlineData(@"\??\C:\Windows\System32\drivers\test.sys", @"C:\Windows\System32\drivers\test.sys")]
    [InlineData(@"", @"")]
    [InlineData(null, @"")]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void CleanExecutablePath_NormalizesVariousFormats(string? input, string expected)
    {
        var result = TaskManagerViewModel.CleanExecutablePath(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Filter_ByApps_IncludesExplorerParentWithoutExe()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        var snapshot = new SystemPerformanceSnapshot
        {
            Processes = new List<ProcessInfo>
            {
                new() { Pid = 1, Name = "Notepad", ParentProcessName = "explorer" },
                new() { Pid = 2, Name = "Calculator", ParentProcessName = "explorer.exe" },
                new() { Pid = 3, Name = "SomeDaemon", ParentProcessName = "services.exe", IsService = true }
            }
        };
        vm.UpdateSnapshot(snapshot);

        vm.SelectedFilter = "apps";

        Assert.Equal(2, vm.FilteredProcesses.Count);
        Assert.Contains(vm.FilteredProcesses, p => p.Name == "Notepad");
        Assert.Contains(vm.FilteredProcesses, p => p.Name == "Calculator");
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Filter_ByServices_IncludesSvchostAndServiceLocation()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        var snapshot = new SystemPerformanceSnapshot
        {
            Processes = new List<ProcessInfo>
            {
                new() { Pid = 10, Name = "svchost", ProcessName = "svchost" },
                new() { Pid = 20, Name = "custom_service", StartupLocation = EntryLocation.Service },
                new() { Pid = 30, Name = "regular_app", HasWindow = true }
            }
        };
        vm.UpdateSnapshot(snapshot);

        vm.SelectedFilter = "services";

        Assert.Equal(2, vm.FilteredProcesses.Count);
        Assert.Contains(vm.FilteredProcesses, p => p.Name == "svchost");
        Assert.Contains(vm.FilteredProcesses, p => p.Name == "custom_service");
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ProcessInfo_JsonSerialization_PreservesServiceWindowDescriptionAndSessionId()
    {
        var original = new ProcessInfo
        {
            Pid = 999,
            Name = "test_service.exe",
            IsService = true,
            HasWindow = true,
            Description = "Тестовая служба",
            SessionId = 6
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ProcessInfo>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsService);
        Assert.True(deserialized.HasWindow);
        Assert.Equal("Тестовая служба", deserialized.Description);
        Assert.Equal(6, deserialized.SessionId);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ProcessInfo_IsSuspended_SerializationAndNotificationWorks()
    {
        var proc = new ProcessInfo
        {
            Pid = 1234,
            Name = "worker.exe",
            IsSuspended = true
        };

        var propChanged = false;
        proc.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProcessInfo.IsSuspended))
                propChanged = true;
        };

        proc.IsSuspended = false;
        Assert.True(propChanged);

        var json = System.Text.Json.JsonSerializer.Serialize(proc);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ProcessInfo>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.IsSuspended);

        proc.IsSuspended = true;
        json = System.Text.Json.JsonSerializer.Serialize(proc);
        deserialized = System.Text.Json.JsonSerializer.Deserialize<ProcessInfo>(json);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsSuspended);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void PipeMessage_SuspendAndResumePayloads_SerializeCorrectly()
    {
        var suspendPayload = new SuspendProcessPayload(100, "test.exe");
        var suspendMsg = new PipeMessage
        {
            Type = PipeMessageTypes.SuspendProcess,
            Payload = System.Text.Json.JsonSerializer.Serialize(suspendPayload)
        };

        Assert.Equal(PipeMessageTypes.SuspendProcess, suspendMsg.Type);
        Assert.Contains("100", suspendMsg.Payload);
        Assert.Contains("test.exe", suspendMsg.Payload);

        var resumePayload = new ResumeProcessPayload(100, "test.exe");
        var resumeMsg = new PipeMessage
        {
            Type = PipeMessageTypes.ResumeProcess,
            Payload = System.Text.Json.JsonSerializer.Serialize(resumePayload)
        };

        Assert.Equal(PipeMessageTypes.ResumeProcess, resumeMsg.Type);
        Assert.Contains("100", resumeMsg.Payload);
        Assert.Contains("test.exe", resumeMsg.Payload);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void TaskManagerViewModel_SuspendAndResumeCommands_Instantiated()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        Assert.NotNull(vm.SuspendProcessCommand);
        Assert.NotNull(vm.ResumeProcessCommand);

        var proc = new ProcessInfo { Pid = 555, Name = "app.exe", IsSuspended = false };
        Assert.True(vm.SuspendProcessCommand.CanExecute(proc));
        Assert.True(vm.ResumeProcessCommand.CanExecute(proc));
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public async Task TaskManagerViewModel_ActivateAndDeactivate_Lifecycle_Succeeds()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        Assert.True(vm.IsLoading);

        // Verifies component behavior and assertions.
        await vm.ActivateAsync();
        Assert.True(vm.IsLoading);

        // Verifies component behavior and assertions.
        await vm.ActivateAsync();

        // Verifies component behavior and assertions.
        vm.UpdateSnapshot(CreateSampleSnapshot());
        Assert.False(vm.IsLoading);
        Assert.Equal(4, vm.FilteredProcesses.Count);

        // Verifies component behavior and assertions.
        await vm.DeactivateAsync();

        // Verifies component behavior and assertions.
        await vm.DeactivateAsync();

        // Verifies component behavior and assertions.
        vm.Dispose();
    }

    /// <summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// </summary>
    [Fact]
    public void UpdateSnapshot_WhenStartTimeChangesForSamePid_ReplacesProcessModelAndClearsSelection()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        var startTime1 = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var proc1 = new ProcessInfo
        {
            Pid = 100,
            Name = "worker.exe",
            ProcessName = "worker.exe",
            StartTime = startTime1,
            CpuPercent = 15.0
        };

        vm.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = new List<ProcessInfo> { proc1 } });
        Assert.Single(vm.FilteredProcesses);

        // Verifies component behavior and assertions.
        vm.SelectedProcess = vm.FilteredProcesses[0];
        Assert.NotNull(vm.SelectedProcess);
        Assert.Same(proc1, vm.SelectedProcess);

        // Verifies component behavior and assertions.
        var startTime2 = new DateTime(2026, 11, 4, 10, 0, 8, DateTimeKind.Utc);
        var proc2 = new ProcessInfo
        {
            Pid = 100,
            Name = "worker.exe",
            ProcessName = "worker.exe",
            StartTime = startTime2,
            CpuPercent = 2.0
        };

        vm.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = new List<ProcessInfo> { proc2 } });

        // Verifies component behavior and assertions.
        Assert.Single(vm.FilteredProcesses);
        var currentProc = vm.FilteredProcesses[0];
        Assert.Same(proc2, currentProc);
        Assert.NotSame(proc1, currentProc);
        Assert.Equal(startTime2, currentProc.StartTime);

        // Verifies component behavior and assertions.
        Assert.Null(vm.SelectedProcess);
    }

    /// <summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// </summary>
    [Fact]
    public void UpdateSnapshot_WhenStartTimeChangesForSamePid_ResetsAccumulatedMetricHistory()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        var startTime1 = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var proc1 = new ProcessInfo
        {
            Pid = 250,
            Name = "heavy.exe",
            ProcessName = "heavy.exe",
            StartTime = startTime1,
            CpuPercent = 75.0,
            WorkingSetBytes = 500L * 1024 * 1024
        };

        vm.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = new List<ProcessInfo> { proc1 } });

        var hist1 = vm.GetMetricHistory(250);
        Assert.NotNull(hist1);
        Assert.Equal(75.0, hist1.CpuPercent);
        Assert.Equal(500L * 1024 * 1024, hist1.WorkingSetBytes);

        // Verifies component behavior and assertions.
        var startTime2 = new DateTime(2026, 11, 4, 10, 0, 15, DateTimeKind.Utc);
        var proc2 = new ProcessInfo
        {
            Pid = 250,
            Name = "heavy.exe",
            ProcessName = "heavy.exe",
            StartTime = startTime2,
            CpuPercent = 5.0,
            WorkingSetBytes = 40L * 1024 * 1024
        };

        vm.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = new List<ProcessInfo> { proc2 } });

        var hist2 = vm.GetMetricHistory(250);
        Assert.NotNull(hist2);
        Assert.Equal(5.0, hist2.CpuPercent);
        Assert.Equal(40L * 1024 * 1024, hist2.WorkingSetBytes);
    }

    /// <summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// </summary>
    [Fact]
    public void UpdateSnapshot_WhenStartTimeUnchanged_UpdatesMetricsInPlaceAndPreservesSelection()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        var startTime = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var proc1 = new ProcessInfo
        {
            Pid = 350,
            Name = "stable.exe",
            ProcessName = "stable.exe",
            StartTime = startTime,
            CpuPercent = 10.0,
            WorkingSetBytes = 100L * 1024 * 1024
        };

        vm.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = new List<ProcessInfo> { proc1 } });
        vm.SelectedProcess = vm.FilteredProcesses[0];

        // Verifies component behavior and assertions.
        var procUpdate = new ProcessInfo
        {
            Pid = 350,
            Name = "stable.exe",
            ProcessName = "stable.exe",
            StartTime = startTime,
            CpuPercent = 30.0,
            WorkingSetBytes = 150L * 1024 * 1024
        };

        vm.UpdateSnapshot(new SystemPerformanceSnapshot { Processes = new List<ProcessInfo> { procUpdate } });

        // Verifies component behavior and assertions.
        Assert.Single(vm.FilteredProcesses);
        Assert.Same(proc1, vm.FilteredProcesses[0]);
        Assert.Same(proc1, vm.SelectedProcess);
        Assert.Equal(30.0, vm.SelectedProcess.CpuPercent);
        Assert.Equal(150L * 1024 * 1024, vm.SelectedProcess.WorkingSetBytes);
    }

    /// <summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// </summary>
    [Fact]
    public void GetExpectedCreationFileTimeUtc_HandlesValidAndInvalidTimesSafely()
    {
        // Verifies component behavior and assertions.
        var validTime = new DateTime(2026, 11, 4, 12, 34, 56, DateTimeKind.Utc);
        var procValid = new ProcessInfo { Pid = 1, StartTime = validTime };
        var fileTime = TaskManagerViewModel.GetExpectedCreationFileTimeUtc(procValid);
        Assert.NotNull(fileTime);
        Assert.Equal(validTime.ToFileTimeUtc(), fileTime.Value);

        // Verifies component behavior and assertions.
        Assert.Null(TaskManagerViewModel.GetExpectedCreationFileTimeUtc(null));

        // Verifies component behavior and assertions.
        var procMin = new ProcessInfo { Pid = 2, StartTime = DateTime.MinValue };
        Assert.Null(TaskManagerViewModel.GetExpectedCreationFileTimeUtc(procMin));

        // 4. Default DateTime
        var procDef = new ProcessInfo { Pid = 3, StartTime = default };
        Assert.Null(TaskManagerViewModel.GetExpectedCreationFileTimeUtc(procDef));

        // Verifies component behavior and assertions.
        var procAncient = new ProcessInfo { Pid = 4, StartTime = new DateTime(1500, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Null(TaskManagerViewModel.GetExpectedCreationFileTimeUtc(procAncient));
    }

    /// <summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// </summary>
    [Fact]
    public async Task ProcessCommands_PassExpectedCreationFileTimeUtc_AndHandleProcessInstanceChanged()
    {
        var fakeClient = new FakeProcessPipeClient();
        var vm = new TaskManagerViewModel(fakeClient);

        // Verifies component behavior and assertions.
        vm.ConfirmDialog = (msg, title, btn, img) => System.Windows.MessageBoxResult.Yes;
        string? statusTitle = null;
        string? statusMsg = null;
        vm.StatusMessageRequested += (t, m) => { statusTitle = t; statusMsg = m; };

        string? dialogMsg = null;
        vm.MessageDialog = (msg, title, btn, img) => { dialogMsg = msg; };

        var procTime = new DateTime(2026, 11, 4, 14, 0, 0, DateTimeKind.Utc);
        var proc = new ProcessInfo
        {
            Pid = 777,
            Name = "testapp.exe",
            ProcessName = "testapp.exe",
            StartTime = procTime
        };

        // Verifies component behavior and assertions.
        fakeClient.ResultToReturn = new ProcessActionResultPayload(false, "ProcessInstanceChanged: PID reused by another instance");
        var expectedMsg = LocalizationService.Instance["TmProcessInstanceChanged"];

        // Verifies component behavior and assertions.
        vm.SelectedProcess = proc;
        await vm.KillProcessAsync(proc);

        Assert.Equal(777, fakeClient.LastKillPid);
        Assert.Equal(procTime.ToFileTimeUtc(), fakeClient.LastKillExpectedFileTime);
        Assert.Contains(expectedMsg, statusMsg);
        Assert.Contains(expectedMsg, dialogMsg);
        Assert.Null(vm.SelectedProcess); // Selection reset

        // Verifies component behavior and assertions.
        vm.SelectedProcess = proc;
        statusMsg = null; dialogMsg = null;
        await vm.SuspendProcessAsync(proc);

        Assert.Equal(777, fakeClient.LastSuspendPid);
        Assert.Equal(procTime.ToFileTimeUtc(), fakeClient.LastSuspendExpectedFileTime);
        Assert.Contains(expectedMsg, statusMsg);
        Assert.Null(vm.SelectedProcess);

        // Verifies component behavior and assertions.
        vm.SelectedProcess = proc;
        statusMsg = null; dialogMsg = null;
        await vm.ResumeProcessAsync(proc);

        Assert.Equal(777, fakeClient.LastResumePid);
        Assert.Equal(procTime.ToFileTimeUtc(), fakeClient.LastResumeExpectedFileTime);
        Assert.Contains(expectedMsg, statusMsg);
        Assert.Null(vm.SelectedProcess);

        // Verifies component behavior and assertions.
        vm.SelectedProcess = proc;
        statusMsg = null; dialogMsg = null;
        await vm.BlockProcessAsync(proc);

        Assert.Equal(777, fakeClient.LastBlockPid);
        Assert.Equal(procTime.ToFileTimeUtc(), fakeClient.LastBlockExpectedFileTime);
        Assert.Contains(expectedMsg, statusMsg);
        Assert.Null(vm.SelectedProcess);
    }

}





/// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
public class FakeProcessPipeClient : PipeClient
{
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    public ServiceInfoPayload? ServiceInfoToReturn { get; set; } = new(1, "2.1.0", "Running", DateTime.UtcNow, TimeSpan.Zero, false,
        SupportsProcessInstanceCommands: true);

    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    /// <summary>Verifies test scenario for TaskManagerViewModelTests.cs.</summary>
    public override Task<ServiceInfoPayload?> GetServiceInfoAsync() => Task.FromResult(ServiceInfoToReturn);

    public ProcessActionResultPayload ResultToReturn { get; set; } = new(true, "OK");

    public int? LastKillPid { get; private set; }
    public long? LastKillExpectedFileTime { get; private set; }

    public int? LastBlockPid { get; private set; }
    public long? LastBlockExpectedFileTime { get; private set; }

    public int? LastSuspendPid { get; private set; }
    public long? LastSuspendExpectedFileTime { get; private set; }

    public int? LastResumePid { get; private set; }
    public long? LastResumeExpectedFileTime { get; private set; }

    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public override Task<ProcessActionResultPayload?> KillProcessAsync(int pid, string? processName = null, long? expectedCreationFileTimeUtc = null)
    {
        LastKillPid = pid;
        LastKillExpectedFileTime = expectedCreationFileTimeUtc;
        return Task.FromResult<ProcessActionResultPayload?>(ResultToReturn);
    }

    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public override Task<ProcessActionResultPayload?> BlockProcessAsync(int pid, string filePath, string? processName = null, long? expectedCreationFileTimeUtc = null)
    {
        LastBlockPid = pid;
        LastBlockExpectedFileTime = expectedCreationFileTimeUtc;
        return Task.FromResult<ProcessActionResultPayload?>(ResultToReturn);
    }

    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public override Task<ProcessActionResultPayload?> SuspendProcessAsync(int pid, string? processName = null, long? expectedCreationFileTimeUtc = null)
    {
        LastSuspendPid = pid;
        LastSuspendExpectedFileTime = expectedCreationFileTimeUtc;
        return Task.FromResult<ProcessActionResultPayload?>(ResultToReturn);
    }

    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public override Task<ProcessActionResultPayload?> ResumeProcessAsync(int pid, string? processName = null, long? expectedCreationFileTimeUtc = null)
    {
        LastResumePid = pid;
        LastResumeExpectedFileTime = expectedCreationFileTimeUtc;
        return Task.FromResult<ProcessActionResultPayload?>(ResultToReturn);
    }
}
