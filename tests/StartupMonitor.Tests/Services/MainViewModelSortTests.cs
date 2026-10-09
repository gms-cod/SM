using System;
using System.Collections.Generic;
using System.Linq;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.Services;

[Collection("MainViewModelConfig")]
public class MainViewModelSortTests : IDisposable
{
    private readonly string _tempConfigFile;

    public MainViewModelSortTests()
    {
        _tempConfigFile = Path.Combine(Path.GetTempPath(), $"test-svc-sort-{Guid.NewGuid():N}.json");
        MainViewModel.CustomConfigPath = _tempConfigFile;
    }

    public void Dispose()
    {
        MainViewModel.CustomConfigPath = null;
        try { if (File.Exists(_tempConfigFile)) File.Delete(_tempConfigFile); } catch { }
    }

    [Fact]
    public void SortBy_Publisher_SortsCorrectly()
    {
        var vm = new MainViewModel(new PipeClient());
        vm.SortBy("name");
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "App B", Publisher = "Microsoft Corporation" },
            new() { Id = "2", Name = "App A", Publisher = "Adobe Systems" },
            new() { Id = "3", Name = "App C", Publisher = "" }
        };

        vm.SetInitialData(entries, new List<BlacklistRule>());

        vm.SortByPublisherCommand.Execute(null);
        Assert.Equal("App A", vm.FilteredEntries[0].Name);
        Assert.Equal("App B", vm.FilteredEntries[1].Name);
        Assert.Equal("App C", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25B2", vm.SortPublisherIndicator);

        vm.SortByPublisherCommand.Execute(null);
        Assert.Equal("App B", vm.FilteredEntries[0].Name);
        Assert.Equal("App A", vm.FilteredEntries[1].Name);
        Assert.Equal("App C", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25BC", vm.SortPublisherIndicator);
    }

    [Fact]
    public void SortBy_Impact_SortsCorrectly()
    {
        var vm = new MainViewModel(new PipeClient());
        vm.SortBy("name");
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "App Low", Location = EntryLocation.RegistryRun },
            new() { Id = "2", Name = "App High", Location = EntryLocation.BootExecute },
            new() { Id = "3", Name = "App Medium", Location = EntryLocation.Service }
        };

        vm.SetInitialData(entries, new List<BlacklistRule>());

        vm.SortByImpactCommand.Execute(null);
        Assert.Equal("App Low", vm.FilteredEntries[0].Name);
        Assert.Equal("App Medium", vm.FilteredEntries[1].Name);
        Assert.Equal("App High", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25B2", vm.SortImpactIndicator);

        vm.SortByImpactCommand.Execute(null);
        Assert.Equal("App High", vm.FilteredEntries[0].Name);
        Assert.Equal("App Medium", vm.FilteredEntries[1].Name);
        Assert.Equal("App Low", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25BC", vm.SortImpactIndicator);
    }

    [Fact]
    public void SortBy_Status_SortsCorrectly()
    {
        var vm = new MainViewModel(new PipeClient());
        vm.SortBy("name");
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "App Disabled", Status = EntryStatus.Disabled },
            new() { Id = "2", Name = "App Enabled", Status = EntryStatus.Enabled },
            new() { Id = "3", Name = "App Blocked", Status = EntryStatus.Blocked }
        };

        vm.SetInitialData(entries, new List<BlacklistRule>());

        vm.SortByStatusCommand.Execute(null);
        Assert.Equal("App Enabled", vm.FilteredEntries[0].Name);
        Assert.Equal("App Disabled", vm.FilteredEntries[1].Name);
        Assert.Equal("App Blocked", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25B2", vm.SortStatusIndicator);

        vm.SortByStatusCommand.Execute(null);
        Assert.Equal("App Blocked", vm.FilteredEntries[0].Name);
        Assert.Equal("App Disabled", vm.FilteredEntries[1].Name);
        Assert.Equal("App Enabled", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25BC", vm.SortStatusIndicator);
    }
}

