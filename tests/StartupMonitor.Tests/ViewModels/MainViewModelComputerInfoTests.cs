using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

/// <summary>Проверяет завершение длительного сбора сведений о компьютере вместе с моделью.</summary>
[Collection("MainViewModelConfig")]
public class MainViewModelComputerInfoTests
{
    /// <summary>Частичный отказ сообщает об удалении правила и не помечает запись включённой.</summary>
    /// <returns>Задача проверки частичного восстановления.</returns>
    [Fact]
    public async Task UnblockEntry_WhenEnableFailsAfterRuleRemoval_ReportsPartialResult()
    {
        using var model = new MainViewModel(new PipeClient());
        var entry = new StartupEntry { Id = "entry", Name = "Test", FilePath = @"C:\test.exe", Status = EntryStatus.Blocked };
        var rule = new BlacklistRule { Id = "rule", Value = entry.FilePath, MatchType = BlacklistMatchType.Path };
        model.SetInitialData([entry], [rule]);
        model.RemoveBlacklistForUnblockOverride = _ => Task.FromResult<(bool, string?)>((true, null));
        model.EnableForUnblockOverride = _ => Task.FromResult<(bool, string?)>((false, "conflict"));
        string? error = null;
        model.EntryEnableErrorHandler = message => error = message;

        await model.UnblockEntryByIdCommand.ExecuteAsync(entry.Id);

        Assert.Equal(EntryStatus.Blocked, entry.Status);
        Assert.Empty(model.Blacklist);
        Assert.Contains("conflict", error);
        Assert.Contains(LocalizationService.Instance["UnblockRuleRemovedButEnableFailed"], error);
    }

    /// <summary>Потеря подключения при включении не меняет статус записи и показывает ошибку.</summary>
    /// <returns>Задача проверки обработки отказа службы.</returns>
    [Fact]
    public async Task EnableEntry_WhenDisconnected_KeepsDisabledStatus()
    {
        using var model = new MainViewModel(new PipeClient());
        var entry = new StartupEntry { Id = "entry", Name = "Test", Status = EntryStatus.Disabled };
        model.SetInitialData([entry], []);
        string? error = null;
        model.EntryEnableErrorHandler = message => error = message;

        await model.EnableEntryCommand.ExecuteAsync(entry.Id);

        Assert.Equal(EntryStatus.Disabled, entry.Status);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>Ошибка опроса не препятствует открытию окна с последним доступным снимком.</summary>
    /// <returns>Задача проверки отказа источника сведений.</returns>
    [Fact]
    public async Task OpenSystemInfo_OpensWhenCollectionFails()
    {
        using var model = new MainViewModel(new PipeClient());
        var opened = false;
        model.OpenSystemInfoRequested += () => opened = true;
        model.ComputerInfoCollector = () => throw new InvalidOperationException("Ошибка тестового источника");

        await model.OpenSystemInfoAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(opened);
    }

    /// <summary>Закрытая модель не открывает окно после отмены и безопасно освобождается повторно.</summary>
    /// <returns>Задача проверки отмены фонового сбора.</returns>
    [Fact]
    public async Task OpenSystemInfo_CancelsWaitingWhenDisposed()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new MainViewModel(new PipeClient());
        var opened = false;
        model.OpenSystemInfoRequested += () => opened = true;
        model.ComputerInfoCollector = () =>
        {
            started.Set();
            release.Wait();
            finished.TrySetResult();
            return new SystemComputerInfo();
        };
        try
        {
            var opening = model.OpenSystemInfoAsync();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            model.Dispose();
            model.Dispose();
            await opening.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(opened);
        }
        finally
        {
            release.Set();
            model.Dispose();
            if (started.IsSet)
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
