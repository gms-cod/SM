using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Provides operations for this member.
/// </summary>
public class ProcessTreeTests
{
    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ProcessStartupLinker_GetParentPidMap_ReturnsCurrentProcessParent()
    {
        var map = ProcessStartupLinker.GetParentPidMap();

        Assert.NotNull(map);
        Assert.NotEmpty(map);

        var currentPid = Process.GetCurrentProcess().Id;
        Assert.True(map.ContainsKey(currentPid), "Текущий тестовый процесс должен присутствовать в карте процессов.");
        Assert.True(map[currentPid] > 0, "PID родительского процесса должен быть больше нуля.");
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void TaskManagerViewModel_FlatTreeHierarchy_BuildsCorrectLevelsAndExpands()
    {
        var vm = new TaskManagerViewModel(new PipeClient());

        var snapshot = new SystemPerformanceSnapshot
        {
            Processes = new List<ProcessInfo>
            {
                new() { ProcessId = 1000, ProcessName = "explorer", ParentProcessId = 0, CpuPercent = 5 },
                new() { ProcessId = 1010, ProcessName = "cmd", ParentProcessId = 1000, CpuPercent = 10 },
                new() { ProcessId = 1020, ProcessName = "powershell", ParentProcessId = 1000, CpuPercent = 2 },
                new() { ProcessId = 1030, ProcessName = "git", ParentProcessId = 1010, CpuPercent = 1 },
                new() { ProcessId = 2000, ProcessName = "svchost", ParentProcessId = 0, CpuPercent = 15 }
            }
        };

        // Verifies component behavior and assertions.
        vm.UpdateSnapshot(snapshot);

        // Verifies component behavior and assertions.
        vm.SetViewMode("flat");
        Assert.False(vm.IsTreeView);
        Assert.Equal(5, vm.FilteredProcesses.Count);
        Assert.All(vm.FilteredProcesses, p =>
        {
            Assert.Equal(0, p.TreeLevel);
            Assert.False(p.HasChildren);
        });

        // Verifies component behavior and assertions.
        vm.SetViewMode("tree");
        Assert.True(vm.IsTreeView);
        Assert.Equal(5, vm.FilteredProcesses.Count);

        var explorer = vm.FilteredProcesses.First(p => p.Pid == 1000);
        Assert.Equal(0, explorer.TreeLevel);
        Assert.True(explorer.HasChildren);
        Assert.Equal(2, explorer.ChildrenCount);

        var cmd = vm.FilteredProcesses.First(p => p.Pid == 1010);
        Assert.Equal(1, cmd.TreeLevel);
        Assert.True(cmd.HasChildren);
        Assert.Equal(1, cmd.ChildrenCount);

        var git = vm.FilteredProcesses.First(p => p.Pid == 1030);
        Assert.Equal(2, git.TreeLevel);
        Assert.False(git.HasChildren);

        var powershell = vm.FilteredProcesses.First(p => p.Pid == 1020);
        Assert.Equal(1, powershell.TreeLevel);
        Assert.False(powershell.HasChildren);

        var svchost = vm.FilteredProcesses.First(p => p.Pid == 2000);
        Assert.Equal(0, svchost.TreeLevel);
        Assert.False(svchost.HasChildren);

        // Verifies component behavior and assertions.
        vm.ToggleExpand(explorer);
        // Verifies component behavior and assertions.
        Assert.Equal(2, vm.FilteredProcesses.Count);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1000);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 2000);
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 1010);
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 1020);
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 1030);

        // Verifies component behavior and assertions.
        vm.ToggleExpand(explorer);
        Assert.Equal(5, vm.FilteredProcesses.Count);

        // Verifies component behavior and assertions.
        vm.ToggleExpand(explorer); // Collapse explorer again
        Assert.Equal(2, vm.FilteredProcesses.Count);

        vm.SearchText = "git";
        vm.ApplyFilterAndSort();
        // Verifies component behavior and assertions.
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1030);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1010);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1000);
    }
}

