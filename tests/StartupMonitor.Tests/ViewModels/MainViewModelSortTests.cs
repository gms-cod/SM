using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

[Collection("MainViewModelConfig")]
public class MainViewModelSortTests : IDisposable
{
    private readonly string _tempConfigFile;

    public MainViewModelSortTests()
    {
        _tempConfigFile = Path.Combine(Path.GetTempPath(), $"test-vm-sort-{Guid.NewGuid():N}.json");
        MainViewModel.CustomConfigPath = _tempConfigFile;
    }

    public void Dispose()
    {
        MainViewModel.CustomConfigPath = null;
        try { if (File.Exists(_tempConfigFile)) File.Delete(_tempConfigFile); } catch { }
    }

    [Fact]
    public void SortByPublisher_Ascending_NamedPublishersFirstAlphabetically()
    {
        var vm = new MainViewModel(new PipeClient());
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "ZApp", Publisher = "" },
            new() { Id = "2", Name = "BApp", Publisher = "Microsoft Corporation" },
            new() { Id = "3", Name = "AApp", Publisher = "Google LLC" },
            new() { Id = "4", Name = "XApp", Publisher = "   " },
        };

        vm.SetInitialData(entries, []);
        vm.SortBy("publisher");

        Assert.Equal("Google LLC", vm.FilteredEntries[0].Publisher);
        Assert.Equal("Microsoft Corporation", vm.FilteredEntries[1].Publisher);
        // Записи без издателя в конце
        Assert.True(string.IsNullOrWhiteSpace(vm.FilteredEntries[2].Publisher));
        Assert.True(string.IsNullOrWhiteSpace(vm.FilteredEntries[3].Publisher));
        Assert.Equal("\u25B2", vm.SortPublisherIndicator);
    }

    [Fact]
    public void SortByPublisher_Descending_ReversesOrder()
    {
        var vm = new MainViewModel(new PipeClient());
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "ZApp", Publisher = "" },
            new() { Id = "2", Name = "BApp", Publisher = "Microsoft Corporation" },
            new() { Id = "3", Name = "AApp", Publisher = "Google LLC" },
        };

        vm.SetInitialData(entries, []);
        vm.SortBy("publisher"); // asc
        vm.SortBy("publisher"); // desc

        Assert.Equal("Microsoft Corporation", vm.FilteredEntries[0].Publisher);
        Assert.Equal("Google LLC", vm.FilteredEntries[1].Publisher);
        Assert.True(string.IsNullOrWhiteSpace(vm.FilteredEntries[2].Publisher));
        Assert.Equal("\u25BC", vm.SortPublisherIndicator);
    }

    [Fact]
    public void SortByImpact_AscendingAndDescending_OrdersCorrectly()
    {
        var vm = new MainViewModel(new PipeClient());
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "HighApp", Location = EntryLocation.BootExecute }, // High (3)
            new() { Id = "2", Name = "LowApp", Location = EntryLocation.RegistryRun },    // Low (1)
            new() { Id = "3", Name = "MedApp", Location = EntryLocation.Service },        // Med (2)
        };

        vm.SetInitialData(entries, []);
        vm.SortBy("impact");

        // Asc: Low (1) -> Med (2) -> High (3)
        Assert.Equal("LowApp", vm.FilteredEntries[0].Name);
        Assert.Equal("MedApp", vm.FilteredEntries[1].Name);
        Assert.Equal("HighApp", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25B2", vm.SortImpactIndicator);

        vm.SortBy("impact"); // desc
        Assert.Equal("HighApp", vm.FilteredEntries[0].Name);
        Assert.Equal("MedApp", vm.FilteredEntries[1].Name);
        Assert.Equal("LowApp", vm.FilteredEntries[2].Name);
        Assert.Equal("\u25BC", vm.SortImpactIndicator);
    }

    [Fact]
    public void SortByStatus_AscendingAndDescending_OrdersCorrectly()
    {
        var vm = new MainViewModel(new PipeClient());
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "BlockedApp", Status = EntryStatus.Blocked },
            new() { Id = "2", Name = "EnabledApp", Status = EntryStatus.Enabled },
            new() { Id = "3", Name = "DisabledApp", Status = EntryStatus.Disabled },
        };

        vm.SetInitialData(entries, []);
        vm.SortBy("status");

        // Asc: Enabled -> Disabled -> Blocked
        Assert.Equal(EntryStatus.Enabled, vm.FilteredEntries[0].Status);
        Assert.Equal(EntryStatus.Disabled, vm.FilteredEntries[1].Status);
        Assert.Equal(EntryStatus.Blocked, vm.FilteredEntries[2].Status);
        Assert.Equal("\u25B2", vm.SortStatusIndicator);

        vm.SortBy("status"); // desc
        Assert.Equal(EntryStatus.Blocked, vm.FilteredEntries[0].Status);
        Assert.Equal(EntryStatus.Disabled, vm.FilteredEntries[1].Status);
        Assert.Equal(EntryStatus.Enabled, vm.FilteredEntries[2].Status);
        Assert.Equal("\u25BC", vm.SortStatusIndicator);
    }
}

