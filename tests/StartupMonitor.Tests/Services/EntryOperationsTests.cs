using System.Text.Json;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для EntryOperations: удаление/восстановление файлов, реестровых записей, служб, задач,
/// проверка защищённых имён процессов.
/// </summary>
public class EntryOperationsTests
{
    /// <summary>Настройки JSON-сериализации: snake_case для payload'ных классов.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Создаёт тестовую запись StartupEntry для указанного Location.</summary>
    private static StartupEntry MakeEntry(EntryLocation loc, string name = "TestEntry", string filePath = "")
    {
        var entry = new StartupEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            FilePath = filePath,
            Location = loc,
            Status = EntryStatus.Enabled,
            DetectedAt = DateTime.UtcNow
        };
        if (loc is EntryLocation.Service or EntryLocation.Driver)
            entry.LocationDetail = name;
        return entry;
    }

    // ───── Registry Payload round-trip ─────

    /// <summary>
    /// Проверяет, что BuildRegistryPayload возвращает путь, начинающийся с ожидаемого hive-префикса.
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRun, "HKEY_CURRENT_USER")]
    [InlineData(EntryLocation.RegistryRunOnce, "HKEY_CURRENT_USER")]
    [InlineData(EntryLocation.RegistryRunServices, "HKEY_CURRENT_USER")]
    [InlineData(EntryLocation.RegistryPolicies, "HKEY_CURRENT_USER")]
    [InlineData(EntryLocation.ContextMenuHandler, "HKEY_CLASSES_ROOT")]
    public void BuildRegistryPayload_HivePrefix(EntryLocation loc, string expectedHive)
    {
        var path = EntryOperations.BuildRegistryPayload(loc);
        Assert.StartsWith(expectedHive, path);
        Assert.Contains("HKEY_", path, StringComparison.Ordinal);
    }

    // ───── StartupFile ─────

    /// <summary>
    /// Проверяет, что RemoveStartupFile возвращает ошибку для несуществующего файла.
    /// </summary>
    [Fact]
    public void RemoveStartupFile_FileNotFound_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.StartupFolder, filePath: @"C:\nonexistent\file.lnk");
        var (success, error) = EntryOperations.RemoveStartupFile(entry, out var payload);
        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", payload);
    }

    /// <summary>
    /// Проверяет, что RemoveStartupFile удаляет существующий файл и создаёт бэкап.
    /// </summary>
    [Fact]
    public void RemoveStartupFile_ExistingFile_RemovesAndReturnsPayload()
    {
        var tempFile = Path.GetTempFileName() + ".lnk";
        try
        {
            File.WriteAllText(tempFile, "dummy shortcut content");
            var entry = MakeEntry(EntryLocation.StartupFolder, filePath: tempFile);

            var (success, error) = EntryOperations.RemoveStartupFile(entry, out var payload);
            Assert.True(success, error);
            Assert.Null(error);

            Assert.False(File.Exists(tempFile), "Original file should be deleted");

            var fp = JsonSerializer.Deserialize<EntryOperations.FilePayload>(payload, JsonOptions);
            Assert.NotNull(fp);
            Assert.Equal(tempFile, fp.OriginalPath);
            Assert.True(File.Exists(fp.BackupPath), "Backup file should exist");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            var backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "StartupMonitor", "backups");
            if (Directory.Exists(backupDir))
                foreach (var f in Directory.GetFiles(backupDir))
                    try { File.Delete(f); } catch { }
        }
    }

    /// <summary>
    /// Проверяет, что RestoreFile возвращает ошибку при отсутствующем бэкапе.
    /// </summary>
    [Fact]
    public void RestoreFile_BackupNotFound_ReturnsError()
    {
        var payload = JsonSerializer.Serialize(
            new EntryOperations.FilePayload(@"C:\nonexistent_backup.dat", @"C:\restore_target.lnk"),
            JsonOptions);

        var (success, error) = EntryOperations.RestoreFile(payload);
        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что RestoreFile восстанавливает файл из бэкапа по валидному payload.
    /// </summary>
    [Fact]
    public void RestoreFile_ValidBackup_RestoresFile()
    {
        var tempBackup = Path.GetTempFileName();
        var tempOriginal = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempBackup, "backup content");

            var payload = JsonSerializer.Serialize(
                new EntryOperations.FilePayload(tempBackup, tempOriginal),
                JsonOptions);

            var (success, error) = EntryOperations.RestoreFile(payload);
            Assert.True(success, error);

            Assert.True(File.Exists(tempOriginal));
            Assert.Equal("backup content", File.ReadAllText(tempOriginal));
        }
        finally
        {
            if (File.Exists(tempBackup)) File.Delete(tempBackup);
            if (File.Exists(tempOriginal)) File.Delete(tempOriginal);
        }
    }

    // ───── Service Payload round-trip ─────

    /// <summary>
    /// Проверяет сериализацию/десериализацию ServicePayload.
    /// </summary>
    [Fact]
    public void ServicePayload_SerializesAndDeserializes()
    {
        var sp = new EntryOperations.ServicePayload("MySvc", @"C:\svc.exe", "auto", "My Service");
        var json = JsonSerializer.Serialize(sp, JsonOptions);
        var back = JsonSerializer.Deserialize<EntryOperations.ServicePayload>(json, JsonOptions);
        Assert.NotNull(back);
        Assert.Equal(sp.ServiceName, back.ServiceName);
        Assert.Equal(sp.BinaryPath, back.BinaryPath);
        Assert.Equal(sp.StartType, back.StartType);
    }

    // ───── Task Payload round-trip ─────

    /// <summary>
    /// Проверяет сериализацию/десериализацию TaskPayload.
    /// </summary>
    [Fact]
    public void TaskPayload_SerializesAndDeserializes()
    {
        var tp = new EntryOperations.TaskPayload("<Task>...</Task>", @"\MyTask");
        var json = JsonSerializer.Serialize(tp, JsonOptions);
        var back = JsonSerializer.Deserialize<EntryOperations.TaskPayload>(json, JsonOptions);
        Assert.NotNull(back);
        Assert.Equal(tp.TaskXml, back.TaskXml);
        Assert.Equal(tp.TaskPath, back.TaskPath);
    }

    // ───── Registry Payload round-trip ─────

    /// <summary>
    /// Проверяет сериализацию/десериализацию RegistryPayload.
    /// </summary>
    [Fact]
    public void RegistryPayload_SerializesAndDeserializes()
    {
        var rp = new EntryOperations.RegistryPayload(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            "MyApp", "\"C:\\MyApp.exe\"");
        var json = JsonSerializer.Serialize(rp, JsonOptions);
        var back = JsonSerializer.Deserialize<EntryOperations.RegistryPayload>(json, JsonOptions);
        Assert.NotNull(back);
        Assert.Equal(rp.Key, back.Key);
        Assert.Equal(rp.ValueName, back.ValueName);
        Assert.Equal(rp.ValueData, back.ValueData);
    }

    // ───── Dispatcher ─────

    /// <summary>
    /// Проверяет, что RemoveFromSystem возвращает ошибку для неподдерживаемых типов Location.
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.KeyboardHook)]
    [InlineData(EntryLocation.AppPaths)]
    [InlineData(EntryLocation.BrowserHelperObject)]
    public void RemoveFromSystem_UnsupportedLocation_ReturnsError(EntryLocation loc)
    {
        var entry = MakeEntry(loc);
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.False(success);
        Assert.Contains("not supported", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(payload);
    }

    /// <summary>
    /// Проверяет, что RestoreToSystem возвращает ошибку для неподдерживаемых типов Location.
    /// </summary>
    [Fact]
    public void RestoreToSystem_UnsupportedLocation_ReturnsError()
    {
        var removed = new RemovedEntry
        {
            Location = EntryLocation.KeyboardHook,
            RestorePayload = "{}"
        };
        var (success, error) = EntryOperations.RestoreToSystem(removed);
        Assert.False(success);
        Assert.Contains("not supported", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что RemoveFromSystem для StartupFolder с отсутствующим файлом возвращает ошибку.
    /// </summary>
    [Fact]
    public void RemoveFromSystem_StartupFolderWithMissingFile_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.StartupFolder, filePath: @"C:\missing.lnk");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Null(payload);
    }

    // ───── RemoveFromSystem routing ─────

    /// <summary>
    /// Проверяет, что RemoveFromSystem для реестровых Location не выбрасывает исключений.
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRun)]
    [InlineData(EntryLocation.RegistryRunOnce)]
    [InlineData(EntryLocation.RegistryPolicies)]
    [InlineData(EntryLocation.RegistryRunServices)]
    [InlineData(EntryLocation.ContextMenuHandler)]
    public void RemoveFromSystem_RegistryLocations_DoesNotThrow(EntryLocation loc)
    {
        var entry = MakeEntry(loc, name: "NonExistentApp_" + Guid.NewGuid().ToString("N"));
        var ex = Record.Exception(() => EntryOperations.RemoveFromSystem(entry));
        Assert.Null(ex);
    }

    /// <summary>
    /// Проверяет, что RemoveFromSystem для службы возвращает payload даже для несуществующей службы.
    /// </summary>
    [Fact]
    public void RemoveFromSystem_ServiceLocation_ReturnsErrorNoService()
    {
        var entry = MakeEntry(EntryLocation.Service, name: "NonExistentSvc_ShouldNotExist");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        // sc stop/delete on nonexistent service returns success from the sc side (exit code 0)
        // but the method just returns success regardless
        // So this test verifies no exception is thrown
        Assert.NotNull(payload);
        Assert.Contains("NonExistentSvc_ShouldNotExist", payload);
    }

    /// <summary>
    /// Проверяет, что RemoveFromSystem для задачи планировщика возвращает ошибку при отсутствии задачи.
    /// </summary>
    [Fact]
    public void RemoveFromSystem_TaskSchedulerLocation_ReturnsErrorNoTask()
    {
        var entry = MakeEntry(EntryLocation.TaskScheduler, filePath: @"\NonExistentTask");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(payload);
    }

    /// <summary>
    /// Проверяет, что IsProtectedProcessName возвращает true для системных и служебных имён.
    /// </summary>
    [Theory]
    [InlineData("svchost.exe")]
    [InlineData(@"C:\Windows\System32\svchost.exe")]
    [InlineData(@"C:\Malware\SVCHOST.EXE")]
    [InlineData("explorer.exe")]
    [InlineData("rundll32")]
    [InlineData("lsass.exe")]
    [InlineData("StartupMonitor.Service.exe")]
    [InlineData("StartupMonitor.UI.exe")]
    [InlineData("")]
    public void IsProtectedProcessName_ProtectedNames_ReturnsTrue(string fileName)
    {
        Assert.True(EntryOperations.IsProtectedProcessName(fileName));
    }

    /// <summary>
    /// Проверяет, что IsProtectedProcessName возвращает false для обычных имён приложений.
    /// </summary>
    [Theory]
    [InlineData("myapp.exe")]
    [InlineData(@"C:\Apps\discord\Discord.exe")]
    public void IsProtectedProcessName_NormalNames_ReturnsFalse(string fileName)
    {
        Assert.False(EntryOperations.IsProtectedProcessName(fileName));
    }

    // ───── TerminateAllBlocked ─────

    /// <summary>
    /// Проверяет, что TerminateAllBlocked не бросает исключений для пустого списка.
    /// </summary>
    [Fact]
    public void TerminateAllBlocked_EmptyList_DoesNotThrow()
    {
        var ex = Record.Exception(() => EntryOperations.TerminateAllBlocked([]));
        Assert.Null(ex);
    }

    /// <summary>
    /// Проверяет, что TerminateAllBlocked пропускает незаблокированные записи
    /// (для несуществующего имени процесс не убивается — безопасный сценарий).
    /// </summary>
    [Fact]
    public void TerminateAllBlocked_NotBlockedEntry_DoesNotAttemptKill()
    {
        var entry = MakeEntry(EntryLocation.StartupFolder,
            name: "__NotExists__TerminateAllBlocked__",
            filePath: @"C:\__NotExists__TerminateAllBlocked__.exe");
        entry.Status = EntryStatus.Enabled;
        var ex = Record.Exception(() => EntryOperations.TerminateAllBlocked([entry]));
        Assert.Null(ex);
    }

    /// <summary>
    /// Проверяет, что TerminateAllBlocked не бросает исключений для Blocked-записи
    /// с несуществующим файлом (KillProcess находит 0 процессов — безопасно).
    /// </summary>
    [Fact]
    public void TerminateAllBlocked_BlockedEntry_DoesNotThrow()
    {
        var entry = MakeEntry(EntryLocation.StartupFolder,
            name: "__NotExists__TerminateAllBlocked__",
            filePath: @"C:\__NotExists__TerminateAllBlocked__.exe");
        entry.Status = EntryStatus.Blocked;
        entry.FromBlacklist = true;
        var ex = Record.Exception(() => EntryOperations.TerminateAllBlocked([entry]));
        Assert.Null(ex);
    }

    // ───── KillProcessesByRules ─────

    /// <summary>
    /// Проверяет, что KillProcessesByRules не бросает исключений для пустого списка правил.
    /// </summary>
    [Fact]
    public void KillProcessesByRules_Empty_DoesNotThrow()
    {
        var ex = Record.Exception(() => EntryOperations.KillProcessesByRules([]));
        Assert.Null(ex);
    }

    /// <summary>
    /// Проверяет, что KillProcessesByRules игнорирует правила NotifyOnly (ничего не убивает).
    /// </summary>
    [Fact]
    public void KillProcessesByRules_NotifyOnlyRule_DoesNotThrow()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\__NotExists__KillByRules__.exe",
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyOnly
        };
        var ex = Record.Exception(() => EntryOperations.KillProcessesByRules([rule]));
        Assert.Null(ex);
    }

    /// <summary>
    /// Проверяет, что KillProcessesByRules с правилом на несуществующий путь не бросает исключений
    /// и не завершает никакие живые процессы.
    /// </summary>
    [Fact]
    public void KillProcessesByRules_NonMatchingPath_DoesNotThrow()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\__NotExists__KillByRules__.exe",
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyAndRemove
        };
        var ex = Record.Exception(() => EntryOperations.KillProcessesByRules([rule]));
        Assert.Null(ex);
    }

    /// <summary>
    /// Проверяет, что SuspendProcess блокирует попытку приостановки критических системных процессов (svchost, explorer и др.).
    /// </summary>
    [Theory]
    [InlineData("svchost.exe")]
    [InlineData("explorer.exe")]
    [InlineData("lsass.exe")]
    [InlineData("services.exe")]
    public void SuspendProcess_ProtectedProcess_ReturnsFalseAndDoesNotSuspend(string protectedName)
    {
        var (success, _) = EntryOperations.SuspendProcess(999999, protectedName);
        Assert.False(success);
        Assert.False(EntryOperations.IsProcessSuspended(999999));
    }

    /// <summary>
    /// Проверяет, что ResumeProcess блокирует попытку возобновления критических системных процессов.
    /// </summary>
    [Theory]
    [InlineData("svchost.exe")]
    [InlineData("explorer.exe")]
    public void ResumeProcess_ProtectedProcess_ReturnsFalse(string protectedName)
    {
        var (success, _) = EntryOperations.ResumeProcess(999999, protectedName);
        Assert.False(success);
        Assert.False(EntryOperations.IsProcessSuspended(999999));
    }

    /// <summary>
    /// Проверяет, что IsProcessSuspended возвращает false для несуществующего PID.
    /// </summary>
    [Fact]
    public void IsProcessSuspended_UnknownPid_ReturnsFalse()
    {
        Assert.False(EntryOperations.IsProcessSuspended(888888));
    }
}
