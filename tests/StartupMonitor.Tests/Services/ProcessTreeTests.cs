using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class ProcessTreeTests
{
    [Fact]
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

        // Загружаем снимок
        vm.UpdateSnapshot(snapshot);

        // 1. Проверяем плоский режим
        vm.SetViewMode("flat");
        Assert.False(vm.IsTreeView);
        Assert.Equal(5, vm.FilteredProcesses.Count);
        Assert.All(vm.FilteredProcesses, p =>
        {
            Assert.Equal(0, p.TreeLevel);
            Assert.False(p.HasChildren);
        });

        // 2. Переключаем в древовидный режим
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

        // 3. Тестируем сворачивание (Collapse) узла explorer
        vm.ToggleExpand(explorer);
        // explorer свернут: его дети (cmd, git, powershell) должны скрыться
        Assert.Equal(2, vm.FilteredProcesses.Count);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1000);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 2000);
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 1010);
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 1020);
        Assert.DoesNotContain(vm.FilteredProcesses, p => p.Pid == 1030);

        // 4. Тестируем разворачивание (Expand) узла explorer обратно
        vm.ToggleExpand(explorer);
        Assert.Equal(5, vm.FilteredProcesses.Count);

        // 5. Тестируем поиск по имени в дереве (поиск "git" должен развернуть explorer и cmd)
        vm.ToggleExpand(explorer); // Снова свернем explorer
        Assert.Equal(2, vm.FilteredProcesses.Count);

        vm.SearchText = "git";
        vm.ApplyFilterAndSort();
        // Поиск должен найти git и автоматически развернуть всех предков (explorer, cmd)
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1030);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1010);
        Assert.Contains(vm.FilteredProcesses, p => p.Pid == 1000);
    }
}

