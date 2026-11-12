using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Win32;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет обратимую автоматическую блокировку и гонку ручного восстановления без реальных задач Windows.</summary>
public sealed class AutomaticStartupDisableRestoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StartupMonitor_RestoreTests_" + Guid.NewGuid().ToString("N"));
    private readonly JsonDataStore _store;
    private readonly NotificationService _notifications = new(new InitialScanTracker(1));

    /// <summary>Создаёт изолированное хранилище и включает только скриптовую политику для проверки Antigravity.</summary>
    public AutomaticStartupDisableRestoreTests()
    {
        _store = new JsonDataStore(Path.Combine(_directory, "startup-monitor.json"));
        var config = _store.Load();
        config.Settings.AutoGuardMode = "EnforceBlock";
        config.Settings.GuardBlockScripts = true;
        config.Settings.GuardBlockTempPath = false;
        config.Settings.GuardBlockUnsigned = false;
        config.Settings.GuardBlockDangerousCli = false;
        config.Settings.GuardBlockNewServices = false;
        _store.Save(config);
    }

    /// <summary>Создаёт безвредный файл сценария в собственной временной папке.</summary>
    /// <returns>Изолированная запись автозапуска.</returns>
    private StartupEntry Entry()
    {
        var file = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".vbs");
        File.WriteAllText(file, "' harmless isolated test");
        return new StartupEntry { Name = "AntigravityAutoPatch", FilePath = file, CommandLine = file, Location = EntryLocation.StartupFolder };
    }

    /// <summary>AutoGuard и строгий режим сохраняют отключённую запись и возможность включения.</summary>
    /// <param name="strict">Включает строгий режим вместо скриптовой политики.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticPolicy_DisablesWithoutCreatingRemovedBackup(bool strict)
    {
        var config = _store.Load();
        config.Settings.StrictMode = strict;
        _store.Save(config);
        var entry = Entry();

        Assert.False(WatcherHelper.AddOrUpdate(_store, _notifications, entry));

        var stored = Assert.Single(_store.Load().Entries);
        Assert.Equal(EntryStatus.Disabled, stored.Status);
        Assert.NotEmpty(stored.DisablePayload);
        Assert.Empty(_store.GetRemovedEntries());
        Assert.False(File.Exists(entry.FilePath));
        Assert.True(File.Exists(entry.FilePath + ".disabled"));
        Assert.True(EntryOperations.EnableFromSystem(stored).success);
        Assert.True(File.Exists(entry.FilePath));
    }

    /// <summary>Отказ системного отключения показывает ошибку вместо сообщения об успешной блокировке.</summary>
    /// <param name="strict">Выбирает строгий режим или политику AutoGuard.</param>
    /// <returns>Задача получения уведомления и проверки состояния записи.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticPolicy_WhenDisableFails_SendsFailureNotice(bool strict)
    {
        var config = _store.Load();
        config.Settings.StrictMode = strict;
        _store.Save(config);
        var entry = Entry();
        entry.Location = EntryLocation.BrowserPlugin;
        var received = new TaskCompletionSource<List<PipeMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var notifications = new NotificationService(new InitialScanTracker(0));
        notifications.MessagesBatchReady += (_, messages) => received.TrySetResult(messages);

        Assert.False(WatcherHelper.AddOrUpdate(_store, notifications, entry));

        var messages = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var notice = Assert.Single(messages.Where(message => message.Type == PipeMessageTypes.ActionNotice));
        var payload = JsonSerializer.Deserialize<ActionNoticePayload>(notice.Payload);
        Assert.NotNull(payload);
        Assert.Equal("autoDisableFailed", payload.Action);
        Assert.Equal(EntryStatus.Enabled, Assert.Single(_store.Load().Entries).Status);
        Assert.Single(_store.Load().History.Where(history => history.ChangeType == "AutomaticDisableFailed"));
        Assert.True(File.Exists(entry.FilePath));
    }

    /// <summary>
    /// Реальное Enforce-завершение сохраняется для собственного обычного EXE, но общий
    /// интерпретатор остаётся работать. В обоих случаях отключается только значение
    /// в собственном подключе Run, который Windows не использует как точку автозапуска.
    /// </summary>
    /// <param name="sharedHost">Выбирает собственную копию cmd вместо собственного уникально названного ping.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enforce_DisablesOwnRegistryValueAndTerminatesOnlyOrdinaryOwnedImage(bool sharedHost)
    {
        var config = _store.Load();
        config.Settings.GuardBlockTempPath = true;
        _store.Save(config);
        var nonce = "AutoGuardNative_" + Guid.NewGuid().ToString("N");
        var sourceName = sharedHost ? "cmd.exe" : "ping.exe";
        var imagePath = Path.Combine(_directory, sharedHost ? "cmd.exe" : nonce + ".exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), sourceName), imagePath);
        const string runRoot = @"Software\Microsoft\Windows\CurrentVersion\Run";
        var relativeKey = runRoot + @"\" + nonce;
        Process? process = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(relativeKey, writable: true)
                ?? throw new InvalidOperationException("Не удалось создать собственный тестовый подключ реестра.");
            key.SetValue("Probe", '"' + imagePath + '"', RegistryValueKind.String);
            key.Flush();
            process = Process.Start(new ProcessStartInfo
            {
                FileName = imagePath,
                Arguments = sharedHost ? "/d /q" : "-t 127.0.0.1",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = sharedHost,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Не удалось запустить собственный тестовый образ.");
            Assert.False(process.WaitForExit(250), "Собственный образ должен работать до оценки AutoGuard.");
            var entry = new StartupEntry
            {
                Name = nonce + @"\Probe", FilePath = imagePath, CommandLine = '"' + imagePath + '"',
                Location = EntryLocation.RegistryRun, LocationDetail = @"HKCU\" + relativeKey + @"\Probe"
            };

            Assert.False(WatcherHelper.AddOrUpdate(_store, _notifications, entry));

            if (sharedHost)
                Assert.False(process.WaitForExit(250), "Общий хост нельзя завершать по образу без identity сценария.");
            else
                Assert.True(process.WaitForExit(5000), "Enforce должен завершить совпавший собственный обычный EXE.");
            Assert.Null(key.GetValue("Probe"));
            using var disabled = key.OpenSubKey("StartupMonitorDisabled");
            Assert.NotNull(disabled);
            var stored = Assert.Single(_store.Load().Entries);
            Assert.Equal(EntryStatus.Disabled, stored.Status);
            Assert.NotEmpty(stored.DisablePayload);
            Assert.Equal(stored.DisablePayload, disabled.GetValue("Probe"));
            Assert.Empty(_store.GetRemovedEntries());
            Assert.True(File.Exists(imagePath), "Отключение реестра сохраняет исходный исполняемый файл.");
        }
        finally
        {
            try
            {
                if (process != null)
                {
                    // Полный путь клона уникален, а cleanup использует только принадлежащий тесту PID.
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            if (!process.WaitForExit(5000))
                                throw new InvalidOperationException("Собственный тестовый процесс не завершился при очистке.");
                        }
                    }
                    finally { process.Dispose(); }
                }
            }
            finally
            {
                // Удаляем только собственный подключ, не затрагивая действующие значения Run.
                Registry.CurrentUser.DeleteSubKeyTree(relativeKey, throwOnMissingSubKey: false);
            }
        }
    }

    /// <summary>Повторное отключение не уничтожает первый файл или данные включения.</summary>
    [Fact]
    public void RepeatedDisable_PreservesOriginalPayloadAndConflictingFiles()
    {
        var entry = Entry();
        Assert.True(EntryOperations.DisableFromSystem(entry).success);
        var payload = entry.DisablePayload;
        Assert.True(EntryOperations.DisableFromSystem(entry).success);
        Assert.Equal(payload, entry.DisablePayload);
        File.WriteAllText(entry.FilePath, "new conflicting file");
        Assert.False(EntryOperations.DisableFromSystem(entry).success);
        Assert.False(EntryOperations.EnableFromSystem(entry).success);
        Assert.Equal("new conflicting file", File.ReadAllText(entry.FilePath));
        Assert.Equal("' harmless isolated test", File.ReadAllText(entry.FilePath + ".disabled"));
        Assert.Equal(payload, entry.DisablePayload);
    }

    /// <summary>Включение не заменяет новое значение реестра и сохраняет исходный снимок для ручного разрешения конфликта.</summary>
    [Fact]
    public void EnableRegistry_RejectsConflictingValueAndPreservesBackup()
    {
        var nonce = "StartupMonitorConflict_" + Guid.NewGuid().ToString("N");
        var relativeKey = @"Software\Microsoft\Windows\CurrentVersion\Run\" + nonce;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(relativeKey, writable: true)
                ?? throw new InvalidOperationException("Не удалось создать изолированный тестовый ключ реестра.");
            key.SetValue("Probe", "original", RegistryValueKind.String);
            var entry = new StartupEntry { Name = nonce + @"\Probe", Location = EntryLocation.RegistryRun };
            Assert.True(EntryOperations.DisableRegistry(entry).success);
            var originalPayload = entry.DisablePayload;
            key.SetValue("Probe", "new value", RegistryValueKind.String);

            var result = EntryOperations.EnableRegistry(entry);

            Assert.False(result.success);
            Assert.Equal("new value", key.GetValue("Probe"));
            Assert.Equal(originalPayload, entry.DisablePayload);
            using var backup = key.OpenSubKey("StartupMonitorDisabled");
            Assert.NotNull(backup);
            Assert.Equal(originalPayload, backup.GetValue("Probe"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(relativeKey, throwOnMissingSubKey: false);
        }
    }

    /// <summary>Повторное отключение удаляет только совпадающее значение, сохраняя изменённые данные и первый снимок.</summary>
    /// <param name="sameValue">Указывает, совпадает ли новое значение с первоначальным.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepeatedDisableRegistry_PreservesChangedValue(bool sameValue)
    {
        var nonce = "StartupMonitorRepeat_" + Guid.NewGuid().ToString("N");
        var relativeKey = @"Software\Microsoft\Windows\CurrentVersion\Run\" + nonce;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(relativeKey, writable: true)
                ?? throw new InvalidOperationException("Не удалось создать изолированный тестовый ключ реестра.");
            key.SetValue("Probe", "original", RegistryValueKind.String);
            var entry = new StartupEntry { Name = nonce + @"\Probe", Location = EntryLocation.RegistryRun };
            Assert.True(EntryOperations.DisableRegistry(entry).success);
            var payload = entry.DisablePayload;
            key.SetValue("Probe", sameValue ? "original" : "new data", RegistryValueKind.String);

            var result = EntryOperations.DisableRegistry(entry);

            Assert.Equal(sameValue, result.success);
            Assert.Equal(sameValue ? null : "new data", key.GetValue("Probe"));
            Assert.Equal(payload, entry.DisablePayload);
            using var backup = key.OpenSubKey("StartupMonitorDisabled");
            Assert.NotNull(backup);
            Assert.Equal(payload, backup.GetValue("Probe"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(relativeKey, throwOnMissingSubKey: false);
        }
    }

    /// <summary>Watcher сообщает конфликт один раз, показывает активное значение и сохраняет первый снимок при повторном ручном отключении.</summary>
    /// <returns>Задача ожидания уведомления о конфликте.</returns>
    [Fact]
    public async Task Watcher_ChangedRegistryValue_ReportsEnabledAndKeepsBackup()
    {
        var nonce = "UserAutostart_" + Guid.NewGuid().ToString("N");
        var relativeKey = @"Software\Microsoft\Windows\CurrentVersion\Run\" + nonce;
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var notifications = new NotificationService(new InitialScanTracker(0));
        notifications.MessagesBatchReady += (_, messages) =>
        {
            if (messages.Any(message => message.Type == PipeMessageTypes.ActionNotice && message.Payload.Contains("autoDisableConflict")))
                received.TrySetResult(true);
        };
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(relativeKey, writable: true)
                ?? throw new InvalidOperationException("Не удалось создать изолированный тестовый ключ реестра.");
            key.SetValue("Probe", "original", RegistryValueKind.String);
            var entry = new StartupEntry { Name = nonce + @"\Probe", Location = EntryLocation.RegistryRun, FilePath = @"C:\Example\app.exe", CommandLine = "original" };
            Assert.True(EntryOperations.DisableRegistry(entry).success);
            entry.Status = EntryStatus.Disabled;
            var payload = entry.DisablePayload;
            Assert.True(_store.AddEntry(entry));
            key.SetValue("Probe", "new data", RegistryValueKind.String);
            var observed = new StartupEntry { Id = entry.Id, Name = entry.Name, Location = entry.Location, FilePath = entry.FilePath, CommandLine = entry.CommandLine };

            Assert.False(WatcherHelper.AddOrUpdate(_store, notifications, observed));
            Assert.True(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            var current = Assert.Single(_store.Load().Entries);
            Assert.Equal(EntryStatus.Enabled, current.Status);
            Assert.Equal(payload, current.DisablePayload);
            Assert.Equal("new data", key.GetValue("Probe"));
            Assert.False(EntryOperations.DisableRegistry(current).success);
            Assert.Equal(payload, current.DisablePayload);
            Assert.Equal("new data", key.GetValue("Probe"));
            Assert.False(WatcherHelper.AddOrUpdate(_store, notifications, observed));
            Assert.Single(_store.Load().History.Where(h => h.ChangeType == "AutomaticDisableFailed"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(relativeKey, throwOnMissingSubKey: false);
        }
    }

    /// <summary>Ручное восстановление Antigravity не считается новой подозрительной записью watcher.</summary>
    [Fact]
    public void RestoreAntigravity_RegistersKnownIdentityBeforeWatcherAndRemovesBackupAfterSuccess()
    {
        var entry = Entry();
        var removed = Removed(entry);
        _store.AddRemovedEntry(removed);
        var result = EntryOperations.RestoreRemovedEntry(_store, removed, _ =>
        {
            Assert.NotNull(_store.FindEntry(candidate => candidate.Id == entry.Id));
            Assert.Single(_store.GetRemovedEntries());
            Assert.True(EntryOperations.IsRestoreInProgress(_store, entry.Id));
            Assert.False(WatcherHelper.AddOrUpdate(_store, _notifications, entry));
            Assert.True(File.Exists(entry.FilePath));
            return (true, null);
        });
        Assert.True(result.success, result.error);
        Assert.Empty(_store.GetRemovedEntries());
        Assert.Equal(EntryStatus.Enabled, Assert.Single(_store.Load().Entries).Status);
        Assert.False(EntryOperations.IsRestoreInProgress(_store, entry.Id));
        Assert.False(File.Exists(entry.FilePath + ".disabled"));
    }

    /// <summary>Устаревший снимок уже удалённой резервной копии не вызывает повторное восстановление ОС.</summary>
    [Fact]
    public void RestoreStaleBackup_DoesNotInvokeOperatingSystem()
    {
        var removed = Removed(Entry());
        _store.AddRemovedEntry(removed);
        _store.RemoveRemovedEntry(removed.Id);
        var called = false;

        var result = EntryOperations.RestoreRemovedEntry(_store, removed, _ => { called = true; return (true, null); });

        Assert.False(result.success);
        Assert.False(called);
        Assert.Empty(_store.Load().Entries);
        Assert.False(EntryOperations.IsRestoreInProgress(_store, removed.OriginalEntryId));
    }

    /// <summary>Отказ ОС сохраняет резервную копию и удаляет только созданную текущим запросом предварительную запись.</summary>
    [Fact]
    public void RestoreFailure_KeepsBackupAndRollsBackPlaceholder()
    {
        var removed = Removed(Entry());
        _store.AddRemovedEntry(removed);
        var result = EntryOperations.RestoreRemovedEntry(_store, removed, _ => (false, "Controlled OS failure"));
        Assert.False(result.success);
        Assert.Equal("Controlled OS failure", result.error);
        Assert.Single(_store.GetRemovedEntries());
        Assert.Empty(_store.Load().Entries);
        Assert.False(EntryOperations.IsRestoreInProgress(_store, removed.OriginalEntryId));
    }

    /// <summary>Действующее правило ЧС требует явного снятия перед восстановлением; ОС не вызывается.</summary>
    [Fact]
    public void RestoreWithActiveBlacklist_DoesNotInvokeOperatingSystem()
    {
        var entry = Entry();
        var removed = Removed(entry);
        _store.AddRemovedEntry(removed);
        var config = _store.Load();
        config.Blacklist.Add(new BlacklistRule { Value = entry.FilePath, Action = BlacklistAction.NotifyAndRemove });
        _store.Save(config);
        var called = false;
        Assert.False(EntryOperations.RestoreRemovedEntry(_store, removed, _ => { called = true; return (true, null); }).success);
        Assert.False(called);
        Assert.Single(_store.GetRemovedEntries());
        Assert.Empty(_store.Load().Entries);
    }

    /// <summary>Самовосстановившийся файл остаётся видимым отключённым и включается из Removed без перезаписи.</summary>
    [Fact]
    public void ReappearedRemovedEntry_DisablesAndRestoresExistingPoint()
    {
        var entry = Entry();
        var removed = Removed(entry);
        _store.AddRemovedEntry(removed);
        Assert.False(WatcherHelper.AddOrUpdate(_store, _notifications, entry));
        var disabled = Assert.Single(_store.Load().Entries);
        Assert.Equal(EntryStatus.Disabled, disabled.Status);
        Assert.NotEmpty(disabled.DisablePayload);
        Assert.Equal(removed.RestorePayload, Assert.Single(_store.GetRemovedEntries()).RestorePayload);
        Assert.True(EntryOperations.RestoreRemovedEntry(_store, removed).success);
        Assert.Empty(_store.GetRemovedEntries());
        Assert.True(File.Exists(entry.FilePath));
        Assert.Equal(EntryStatus.Enabled, Assert.Single(_store.Load().Entries).Status);
    }

    /// <summary>Повтор завершённого Restore идемпотентен, а иной файл по целевому пути сохраняется.</summary>
    /// <param name="identical">Совпадает ли существующий файл с резервной копией.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestoreFile_ExistingTargetNeverOverwritten(bool identical)
    {
        var original = Path.Combine(_directory, "existing.vbs");
        var backup = Path.Combine(_directory, "backup.vbs");
        File.WriteAllText(original, identical ? "original" : "different");
        File.WriteAllText(backup, "original");
        var payload = JsonSerializer.Serialize(new EntryOperations.FilePayload(backup, original),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Equal(identical, EntryOperations.RestoreFile(payload).success);
        Assert.Equal(identical ? "original" : "different", File.ReadAllText(original));
    }

    /// <summary>Разница Enabled допустима только при неизменённых действиях задачи.</summary>
    [Fact]
    public void TaskDefinitionComparison_RejectsChangedAction()
    {
        const string original = "<Task><Settings><Enabled>true</Enabled></Settings><Actions><Exec><Command>wscript.exe</Command></Exec></Actions></Task>";
        Assert.True(EntryOperations.AreEquivalentTaskDefinitions(original, original.Replace("true", "false")));
        Assert.False(EntryOperations.AreEquivalentTaskDefinitions(original, original.Replace("wscript.exe", "other.exe")));
    }

    /// <summary>Сбой сохранения после успешного восстановления ОС допускает безопасный повтор без потери backup.</summary>
    [Fact]
    public void RestoreStoreSaveFailsAfterOsSuccess_RetryFinalizesWithoutOverwriting()
    {
        var entry = Entry();
        var backup = Path.Combine(_directory, "restore-backup.vbs");
        File.Move(entry.FilePath, backup);
        var removed = Removed(entry);
        removed.RestorePayload = JsonSerializer.Serialize(new EntryOperations.FilePayload(backup, entry.FilePath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        _store.AddRemovedEntry(removed);
        FileStream? locked = null;
        try
        {
            var first = EntryOperations.RestoreRemovedEntry(_store, removed, saved =>
            {
                var restored = EntryOperations.RestoreToSystem(saved);
                Assert.True(restored.success, restored.error);
                locked = new FileStream(Path.Combine(_directory, "removed-entries.json"), FileMode.Open, FileAccess.Read, FileShare.None);
                return restored;
            });
            Assert.False(first.success);
            Assert.Single(_store.GetRemovedEntries());
            Assert.True(File.Exists(entry.FilePath));
        }
        finally { locked?.Dispose(); }

        var retry = EntryOperations.RestoreRemovedEntry(_store, removed);
        Assert.True(retry.success, retry.error);
        Assert.Empty(_store.GetRemovedEntries());
        Assert.Equal("' harmless isolated test", File.ReadAllText(entry.FilePath));
    }

    /// <summary>Создаёт резервную копию, соответствующую прежнему автоматическому удалению.</summary>
    /// <param name="entry">Исходная запись.</param>
    /// <returns>Изолированная резервная копия с исходной identity.</returns>
    private static RemovedEntry Removed(StartupEntry entry) => new()
    {
        OriginalEntryId = entry.Id, Name = entry.Name, FilePath = entry.FilePath, Location = entry.Location,
        LocationDetail = entry.LocationDetail, RemovedAt = DateTime.UtcNow, DetectedAt = entry.DetectedAt,
        RemovalReason = "AutoGuardBlocked:script", RestorePayload = "isolated injected restore"
    };

    /// <summary>Освобождает уведомления и удаляет только собственную проверенную временную директорию.</summary>
    public void Dispose()
    {
        _notifications.Dispose();
        var full = Path.GetFullPath(_directory);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith("StartupMonitor_RestoreTests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Тестовый каталог не прошёл проверку перед удалением.");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }
}
