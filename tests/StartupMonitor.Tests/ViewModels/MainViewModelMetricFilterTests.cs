using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

/// <summary>Проверяет соответствие фильтров карточек счётчикам автозапуска.</summary>
[Collection("MainViewModelConfig")]
public class MainViewModelMetricFilterTests
{
    /// <summary>Карточки показывают записи нужного статуса и сбрасывают поиск.</summary>
    [Fact]
    public void MetricCards_FilterEntriesAndResetSearch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-vm-metric-{Guid.NewGuid():N}.json");
        MainViewModel.CustomConfigPath = path;
        try
        {
            using var vm = new MainViewModel(new PipeClient());
            vm.SetInitialData([
                new StartupEntry { Id = "1", Name = "Enabled", Status = EntryStatus.Enabled },
                new StartupEntry { Id = "2", Name = "Blocked", Status = EntryStatus.Blocked },
                new StartupEntry { Id = "3", Name = "Disabled", Status = EntryStatus.Disabled }
            ], []);
            vm.SearchText = "missing";

            vm.FilterMetricCommand.Execute("enabled");
            Assert.Equal(string.Empty, vm.SearchText);
            Assert.Equal(EntryStatus.Enabled, Assert.Single(vm.FilteredEntries).Status);
            vm.FilterMetricCommand.Execute("blocked");
            Assert.Equal(EntryStatus.Blocked, Assert.Single(vm.FilteredEntries).Status);
            vm.FilterMetricCommand.Execute("all");
            Assert.Equal(3, vm.FilteredEntries.Count);
        }
        finally
        {
            MainViewModel.CustomConfigPath = null;
            DeleteTemporaryFile(path);
        }
    }

    /// <summary>Удаляет временный файл с повтором при кратковременной блокировке другим процессом.</summary>
    /// <param name="path">Путь к файлу настроек теста.</param>
    private static void DeleteTemporaryFile(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException ex) when (attempt < 9)
            {
                System.Diagnostics.Debug.WriteLine($"Повтор удаления временного файла {path}: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }
}
