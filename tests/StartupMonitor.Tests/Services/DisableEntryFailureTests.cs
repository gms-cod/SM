using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет отказ системного отключения через настоящий обработчик IPC.</summary>
public sealed class DisableEntryFailureTests
{
    /// <summary>Отказ записи хранилища возвращает файл и снимок к прежнему состоянию при отключении и включении.</summary>
    /// <param name="enable">Проверяет включение ранее отключённого файла.</param>
    /// <returns>Задача проверки ответа IPC и состояния файла.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistenceFailure_CompensatesStartupFileChange(bool enable)
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-save-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "probe.cmd");
            File.WriteAllText(path, "test");
            var entry = new StartupEntry { Id = "probe", Name = "probe", FilePath = path, Location = EntryLocation.StartupFolder, Status = EntryStatus.Enabled };
            if (enable)
            {
                Assert.True(EntryOperations.DisableFromSystem(entry).success);
                entry.Status = EntryStatus.Disabled;
            }
            var store = new JsonDataStore(directory);
            var config = store.Load();
            config.Entries.Add(entry);
            store.Save(config);
            var server = (PipeServer)RuntimeHelpers.GetUninitializedObject(typeof(PipeServer));
            typeof(PipeServer).GetField("_store", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, store);
            typeof(PipeServer).GetField("_logger", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, NullLogger<PipeServer>.Instance);
            var method = typeof(PipeServer).GetMethod("ProcessMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var locked = new FileStream(Path.Combine(directory, "startup-entries.json.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var task = Assert.IsType<Task<PipeMessage>>(method.Invoke(server, new object?[]
            {
                new PipeMessage { Type = enable ? PipeMessageTypes.EnableEntry : PipeMessageTypes.DisableEntry, Payload = JsonSerializer.Serialize(entry.Id) },
                CancellationToken.None, null
            }));
            var response = await task;
            Assert.Equal(PipeMessageTypes.Error, response.Type);
            Assert.Equal(enable ? EntryStatus.Disabled : EntryStatus.Enabled, Assert.Single(store.Load().Entries).Status);
            Assert.Equal(!enable, File.Exists(path));
            Assert.Equal(enable, File.Exists(path + ".disabled"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Удалённая или уже восстановленная запись не должна приводить к ложному успешному ответу.</summary>
    /// <returns>Задача проверки ответа обработчика восстановления.</returns>
    [Fact]
    public async Task RestoreMissingEntry_ReturnsError()
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-restore-missing-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            store.Load();
            var server = (PipeServer)RuntimeHelpers.GetUninitializedObject(typeof(PipeServer));
            var storeField = typeof(PipeServer).GetField("_store", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(storeField);
            storeField.SetValue(server, store);
            var method = typeof(PipeServer).GetMethod("ProcessMessage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = Assert.IsType<Task<PipeMessage>>(method.Invoke(server, new object?[]
            {
                new PipeMessage { Type = PipeMessageTypes.RestoreEntry, Payload = JsonSerializer.Serialize("missing-entry") },
                CancellationToken.None, null
            }));

            var response = await task;

            Assert.Equal(PipeMessageTypes.Error, response.Type);
            Assert.Contains("не найдена", response.Payload, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(store.Load().RemovedEntries);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Отказ операции не меняет запись и историю, даже если включено завершение процесса.</summary>
    [Fact]
    public async Task RefusedDisable_PreservesStatusAndHistoryAndReturnsReason()
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-disable-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            var config = store.Load();
            config.Settings.KillOnDisable = true;
            config.Entries.Add(new StartupEntry
            {
                Id = "unsupported-location", Name = "Unsupported Test Entry",
                Location = EntryLocation.BrowserPlugin, Status = EntryStatus.Enabled
            });
            store.Save(config);

            // Не создаём полноценный сервер с системными зависимостями: проверяем его настоящий диспетчер.
            var server = (PipeServer)RuntimeHelpers.GetUninitializedObject(typeof(PipeServer));
            var storeField = typeof(PipeServer).GetField("_store", BindingFlags.NonPublic | BindingFlags.Instance);
            var loggerField = typeof(PipeServer).GetField("_logger", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(storeField);
            Assert.NotNull(loggerField);
            storeField.SetValue(server, store);
            loggerField.SetValue(server, NullLogger<PipeServer>.Instance);
            var method = typeof(PipeServer).GetMethod("ProcessMessage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            var task = Assert.IsType<Task<PipeMessage>>(method.Invoke(server, new object?[]
            {
                new PipeMessage { Type = PipeMessageTypes.DisableEntry, Payload = JsonSerializer.Serialize("unsupported-location") },
                CancellationToken.None, null
            }));
            var response = await task;

            Assert.Equal(PipeMessageTypes.Error, response.Type);
            Assert.Contains("not supported", response.Payload, StringComparison.OrdinalIgnoreCase);
            var after = store.Load();
            Assert.Equal(EntryStatus.Enabled, Assert.Single(after.Entries).Status);
            Assert.Empty(after.History);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
