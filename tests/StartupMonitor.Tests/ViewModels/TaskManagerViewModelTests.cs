using System.Collections.Generic;
using System.Linq;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

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
    public void Filter_ByApps_ShowsOnlyAppsWithWindowOrExplorerParent()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SelectedFilter = "apps";

        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("GameApp", vm.FilteredProcesses[0].Name);
    }

    [Fact]
    public void Filter_ByServices_ShowsOnlyServices()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SelectedFilter = "services";

        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("SpoolerService", vm.FilteredProcesses[0].Name);
    }

    [Fact]
    public void Filter_ByAutostart_ShowsOnlyAutostartProcesses()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        vm.UpdateSnapshot(CreateSampleSnapshot());

        vm.SelectedFilter = "autostart";

        Assert.Single(vm.FilteredProcesses);
        Assert.Equal("AutostartApp", vm.FilteredProcesses[0].Name);
    }

    [Fact]
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
    public void CleanExecutablePath_NormalizesVariousFormats(string? input, string expected)
    {
        var result = TaskManagerViewModel.CleanExecutablePath(input);
        Assert.Equal(expected, result);
    }

    [Fact]
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
    public async Task TaskManagerViewModel_ActivateAndDeactivate_Lifecycle_Succeeds()
    {
        var vm = new TaskManagerViewModel(new PipeClient());
        Assert.True(vm.IsLoading);

        // Первый вызов ActivateAsync (труба не подключена) не должен бросать исключений
        await vm.ActivateAsync();
        Assert.True(vm.IsLoading);

        // Повторный вызов ActivateAsync также безопасен
        await vm.ActivateAsync();

        // Обновление снимка переключает IsLoading и обновляет метрики
        vm.UpdateSnapshot(CreateSampleSnapshot());
        Assert.False(vm.IsLoading);
        Assert.Equal(4, vm.FilteredProcesses.Count);

        // Деактивация
        await vm.DeactivateAsync();

        // Повторная деактивация безопасна
        await vm.DeactivateAsync();

        // Dispose безопасен
        vm.Dispose();
    }
}



