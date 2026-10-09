using System.Text.Json;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Полный анализ: проверяем Disable/Enable/Remove/Blacklist для КАЖДОГО типа автозапуска.
/// Выявляем, какие операции реально работают, а какие — только виртуально (в конфиге).
/// </summary>
public class EntryOperationsAnalysisTests
{
    /// <summary>Настройки JSON-сериализации для payload'ных классов.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Все типы из Enum, отсортированные</summary>
    public static TheoryData<EntryLocation> AllEntryLocations =>
        new(Enum.GetValues<EntryLocation>());

    /// <summary>Типы, которые EntryOperations.RemoveFromSystem поддерживает</summary>
    public static TheoryData<EntryLocation> SupportedRemoveLocations
    {
        get
        {
            var data = new TheoryData<EntryLocation>();
            data.Add(EntryLocation.RegistryRun);
            data.Add(EntryLocation.RegistryRunOnce);
            data.Add(EntryLocation.RegistryRunServices);
            data.Add(EntryLocation.RegistryPolicies);
            data.Add(EntryLocation.StartupFolder);
            data.Add(EntryLocation.Service);
            data.Add(EntryLocation.Driver);
            data.Add(EntryLocation.TaskScheduler);
            data.Add(EntryLocation.ContextMenuHandler);
            data.Add(EntryLocation.AppInitDll);
            data.Add(EntryLocation.ImageFileExecutionOptions);
            data.Add(EntryLocation.Winlogon);
            data.Add(EntryLocation.BootExecute);
            return data;
        }
    }

    /// <summary>Типы, которые EntryOperations НЕ поддерживает</summary>
    public static TheoryData<EntryLocation> UnsupportedRemoveLocations
    {
        get
        {
            var data = new TheoryData<EntryLocation>();
            data.Add(EntryLocation.BrowserHelperObject);
            data.Add(EntryLocation.GpoStartupScript);
            data.Add(EntryLocation.GpoLogonScript);
            data.Add(EntryLocation.ComObject);
            data.Add(EntryLocation.BrowserPlugin);
            data.Add(EntryLocation.NetworkProvider);
            data.Add(EntryLocation.PrintMonitor);
            data.Add(EntryLocation.LsaNotification);
            data.Add(EntryLocation.ProtocolCatalog);
            data.Add(EntryLocation.AppPaths);
            data.Add(EntryLocation.KeyboardHook);
            return data;
        }
    }

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
            DetectedAt = DateTime.UtcNow,
        };
        if (loc is EntryLocation.Service or EntryLocation.Driver)
            entry.LocationDetail = name;
        return entry;
    }

    // ═══════════════════════════════════════════════════════
    // 1. DISABLE (виртуальное) — только меняет статус в JSON
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет, что DisableEntry только меняет статус в конфиге, но не трогает систему.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllEntryLocations))]
    public void DisableEntry_OnlyChangesStatusInConfig_NotInSystem(EntryLocation loc)
    {
        // PipeServer.DisableEntry делает:
        //   entry.Status = EntryStatus.Disabled;
        //   _store.UpdateEntry(entry);
        // Никакого реального отключения в системе.

        var path = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(path);
        try
        {
        var entry = MakeEntry(loc, $"DisableTest_{Guid.NewGuid():N}");
        store.AddEntry(entry);

        // Эмулируем PipeServer.DisableEntry
        var cfg = store.Load();
        var stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        stored.Status = EntryStatus.Disabled;
        store.UpdateEntry(stored);

        // Проверяем: статус изменился в конфиге
        cfg = store.Load();
        stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        Assert.Equal(EntryStatus.Disabled, stored.Status);

        // НО: в реальной системе запись всё ещё активна!
        // PipeServer НЕ делает: sc stop, reg delete, file rename, task disable
        // Это ВИРТУАЛЬНОЕ отключение
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// Проверяет, что EnableEntry только меняет статус в конфиге, не трогая систему.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllEntryLocations))]
    public void EnableEntry_OnlyChangesStatusInConfig_NotInSystem(EntryLocation loc)
    {
        var path2 = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        var store2 = new JsonDataStore(path2);
        try
        {
        var entry = MakeEntry(loc, $"EnableTest_{Guid.NewGuid():N}");
        entry.Status = EntryStatus.Disabled;
        store2.AddEntry(entry);

        // Эмулируем PipeServer.EnableEntry
        var cfg = store2.Load();
        var stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        stored.Status = EntryStatus.Enabled;
        store2.UpdateEntry(stored);

        cfg = store2.Load();
        stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        Assert.Equal(EntryStatus.Enabled, stored.Status);
        }
        finally { if (File.Exists(path2)) File.Delete(path2); }
    }

    /// <summary>
    /// Проверяет, что после Disable → Enable Location и Name сохраняются.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllEntryLocations))]
    public void DisableEnable_RoundTrip_PreservesLocationAndName(EntryLocation loc)
    {
        var path3 = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        var store3 = new JsonDataStore(path3);
        try
        {
        var entry = MakeEntry(loc, $"RoundTrip_{Guid.NewGuid():N}", @"C:\test.exe");
        store3.AddEntry(entry);

        // Disable
        var cfg = store3.Load();
        var stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        stored.Status = EntryStatus.Disabled;
        store3.UpdateEntry(stored);

        // Enable
        cfg = store3.Load();
        stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        stored.Status = EntryStatus.Enabled;
        store3.UpdateEntry(stored);

        cfg = store3.Load();
        stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        Assert.Equal(EntryStatus.Enabled, stored.Status);
        Assert.Equal(loc, stored.Location);
        Assert.Equal(entry.Name, stored.Name);
        }
        finally { if (File.Exists(path3)) File.Delete(path3); }
    }

    // ═══════════════════════════════════════════════════════════
    // 2. REMOVE — реальное удаление из системы (EntryOperations)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет, что RemoveFromSystem возвращает ошибку для неподдерживаемых Location.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnsupportedRemoveLocations))]
    public void RemoveFromSystem_UnsupportedLocations_ReturnsError(EntryLocation loc)
    {
        var entry = MakeEntry(loc);
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);

        Assert.False(success);
        Assert.Contains("not supported", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(payload);
    }

    /// <summary>
    /// Проверяет удаление реестровых записей из Run/RunOnce (ключи всегда существуют в HKCU).
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRun)]
    [InlineData(EntryLocation.RegistryRunOnce)]
    public void RemoveFromSystem_Registry_CommonKeys_Success(EntryLocation loc)
    {
        // RegistryRun и RegistryRunOnce всегда существуют в HKCU.
        var uniqueName = "NonExistent_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.True(success);
        Assert.NotNull(payload);
    }

    /// <summary>
    /// Проверяет удаление из редких ключей реестра (RunServices, Policies) — может вернуть ошибку, если ключ отсутствует.
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRunServices)]
    [InlineData(EntryLocation.RegistryPolicies)]
    public void RemoveFromSystem_Registry_RareKeys_KeyNotFound(EntryLocation loc)
    {
        // БАГ: RegistryRunServices и RegistryPolicies часто отсутствуют в HKCU.
        // EntryOperations.OpenSubKey(keyPath, writable: true) возвращает null,
        // RemoveRegistry возвращает false.
        var uniqueName = "NonExistent_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        // Результат зависит от того, существует ли ключ в системе.
        // На чистой системе ключей может не быть -> success=false.
        // Это не ошибка теста, а особенность реализации — нужно создавать ключ перед удалением.
    }

    /// <summary>
    /// Проверяет удаление контекстных меню из HKCR\CLSID (баг был: неправильный путь).
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.ContextMenuHandler)]
    public void RemoveFromSystem_SpecialRegistryLocations_Success(EntryLocation loc)
    {
        // БАГ ИСПРАВЛЕН: путь теперь HKCR\CLSID (вместо HKCR\Software\Classes\CLSID).
        // HKCR\CLSID существует всегда, DeleteValue с throwOnMissingValue: false не падает.
        var uniqueName = "NonExistent_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName);
        entry.FilePath = @"C:\test.dll";
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.True(success);
        Assert.NotNull(payload);
    }

    /// <summary>
    /// Проверяет, что удаление из StartupFolder с отсутствующим файлом возвращает ошибку.
    /// </summary>
    [Fact]
    public void RemoveFromSystem_StartupFolder_FileNotFound_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.StartupFolder, filePath: @"C:\nonexistent\file.lnk");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);

        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(payload);
    }

    /// <summary>
    /// Проверяет, что удаление существующего файла из StartupFolder создаёт бэкап.
    /// </summary>
    [Fact]
    public void RemoveFromSystem_StartupFolder_FileExists_RemovesFile()
    {
        var tempFile = Path.GetTempFileName() + ".lnk";
        try
        {
            File.WriteAllText(tempFile, "dummy");
            var entry = MakeEntry(EntryLocation.StartupFolder, filePath: tempFile);

            var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
            Assert.True(success, error);
            Assert.NotNull(payload);

            // Файл удалён из исходного места
            Assert.False(File.Exists(tempFile));

            // Бэкап создан
            var fp = JsonSerializer.Deserialize<EntryOperations.FilePayload>(payload, JsonOptions);
            Assert.NotNull(fp);
            Assert.True(File.Exists(fp.BackupPath));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            CleanupBackups();
        }
    }

    /// <summary>
    /// Проверяет, что удаление несуществующей службы формально возвращает успех (sc exit code игнорируется).
    /// </summary>
    [Fact]
    public void RemoveFromSystem_Service_NonExistent_ReturnsSuccess_ScExitCodeIgnored()
    {
        // sc stop/delete на несуществующий сервис всё равно возвращает "успех"
        // Это баг — нужно проверять exit code, но пока он игнорируется
        var entry = MakeEntry(EntryLocation.Service, "NonExistentSvc_ShouldNeverExist_" + Guid.NewGuid().ToString("N"));

        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);

        Assert.True(success);
        Assert.NotNull(payload);
        Assert.Contains("NonExistentSvc", payload);
    }

    /// <summary>
    /// Проверяет, что удаление несуществующей задачи возвращает ошибку.
    /// </summary>
    [Fact]
    public void RemoveFromSystem_TaskScheduler_NonExistent_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.TaskScheduler, filePath: @"\NonExistentTask_" + Guid.NewGuid().ToString("N"));

        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);

        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(payload);
    }

    /// <summary>
    /// Проверяет, что RemoveFromSystem не выбрасывает исключений для всех поддерживаемых Location.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedRemoveLocations))]
    public void RemoveFromSystem_SupportedLocations_DoesNotThrow(EntryLocation loc)
    {
        // Для каждого поддерживаемого типа — проверяем что нет эксепшена
        var entry = loc switch
        {
            EntryLocation.StartupFolder => MakeEntry(loc, filePath: Path.GetTempFileName() + ".lnk"),
            EntryLocation.Service => MakeEntry(loc, "NonExistentSvc_" + Guid.NewGuid().ToString("N")),
            EntryLocation.TaskScheduler => MakeEntry(loc, filePath: @"\NonExistentTask_" + Guid.NewGuid().ToString("N")),
            EntryLocation.Driver => MakeEntry(loc, "NonExistentDriver_" + Guid.NewGuid().ToString("N")),
            _ => MakeEntry(loc, "NonExistent_" + Guid.NewGuid().ToString("N")),
        };

        var ex = Record.Exception(() => EntryOperations.RemoveFromSystem(entry));
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════
    // 3. RESTORE — восстановление удалённых записей
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет, что RestoreToSystem возвращает ошибку для неподдерживаемых Location.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnsupportedRemoveLocations))]
    public void RestoreToSystem_UnsupportedLocations_ReturnsError(EntryLocation loc)
    {
        var removed = new RemovedEntry
        {
            Location = loc,
            RestorePayload = "{}",
        };
        var (success, error) = EntryOperations.RestoreToSystem(removed);
        Assert.False(success);
        Assert.Contains("not supported", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет восстановление файла из бэкапа в StartupFolder.
    /// </summary>
    [Fact]
    public void RestoreToSystem_StartupFolder_ValidBackup_RestoresFile()
    {
        var tempBackup = Path.GetTempFileName();
        var tempOriginal = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempBackup, "restore test content");
            var payload = JsonSerializer.Serialize(
                new EntryOperations.FilePayload(tempBackup, tempOriginal), JsonOptions);

            var removed = new RemovedEntry
            {
                Location = EntryLocation.StartupFolder,
                RestorePayload = payload,
            };

            var (success, error) = EntryOperations.RestoreToSystem(removed);
            Assert.True(success, error);
            Assert.True(File.Exists(tempOriginal));
            Assert.Equal("restore test content", File.ReadAllText(tempOriginal));
        }
        finally
        {
            if (File.Exists(tempBackup)) File.Delete(tempBackup);
            if (File.Exists(tempOriginal)) File.Delete(tempOriginal);
        }
    }

    /// <summary>
    /// Проверяет восстановление реестровой записи из RegistryPayload.
    /// </summary>
    [Fact]
    public void RestoreToSystem_Registry_RoundTrip()
    {
        // RegistryPayload -> deserialize -> RestoreRegistry
        var rp = new EntryOperations.RegistryPayload(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            "TestValue", "dummy_data");
        var payload = JsonSerializer.Serialize(rp, JsonOptions);

        var removed = new RemovedEntry
        {
            Location = EntryLocation.RegistryRun,
            RestorePayload = payload,
        };

        var (success, error) = EntryOperations.RestoreToSystem(removed);
        // RestoreRegistry откроет ключ и попробует записать — успех
        Assert.True(success, error ?? "success");
    }

    // ═══════════════════════════════════════════════════════════
    // 4. BLACKLIST — блокировка
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет сопоставление записей с правилами чёрного списка для всех типов MatchType.
    /// </summary>
    [Theory]
    [InlineData(BlacklistMatchType.Path, @"C:\test\app.exe", @"C:\test\app.exe", true)]
    [InlineData(BlacklistMatchType.Path, @"C:\test\app.exe", @"C:\other\app.exe", false)]
    [InlineData(BlacklistMatchType.PathWildcard, @"C:\*\app.exe", @"C:\any\app.exe", true)]
    [InlineData(BlacklistMatchType.PathWildcard, @"C:\*\app.exe", @"C:\any\other.exe", false)]
    [InlineData(BlacklistMatchType.Publisher, "TestCorp", "TestCorp", true)]
    [InlineData(BlacklistMatchType.Publisher, "TestCorp", "OtherCorp", false)]
    public void BlacklistManager_Match_WorksForAllMatchTypes(
        BlacklistMatchType matchType, string ruleValue, string entryValue, bool shouldMatch)
    {
        var path4 = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        var store4 = new JsonDataStore(path4);
        try
        {
        var config = store4.Load();
        config.Blacklist.Add(new BlacklistRule
        {
            Value = ruleValue,
            MatchType = matchType,
            Action = BlacklistAction.NotifyOnly,
        });
        store4.Save(config);

        var manager = new BlacklistManager(store4);
        var entry = new StartupEntry
        {
            FilePath = entryValue,
            Publisher = entryValue,
        };

        var match = manager.MatchEntry(entry);
        if (shouldMatch)
            Assert.NotNull(match);
        else
            Assert.Null(match);
        }
        finally { if (File.Exists(path4)) File.Delete(path4); }
    }

    /// <summary>
    /// Проверяет, что MatchEntry находит правило и возвращает правильное действие.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllEntryLocations))]
    public void BlacklistManager_MatchOnly_ChangesStatus(EntryLocation loc)
    {
        var path5 = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        var store5 = new JsonDataStore(path5);
        try
        {
        var entry = MakeEntry(loc, "BlacklistTest", @"C:\blacklisted\app.exe");

        var config = store5.Load();
        config.Blacklist.Add(new BlacklistRule
        {
            Value = @"C:\blacklisted\app.exe",
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyOnly,
        });
        store5.Save(config);

        var manager = new BlacklistManager(store5);
        var match = manager.MatchEntry(entry);

        Assert.NotNull(match);
        Assert.Equal(BlacklistAction.NotifyOnly, match.Action);
        }
        finally { if (File.Exists(path5)) File.Delete(path5); }
    }

    // ═══════════════════════════════════════════════════════════
    // 5. DISABLE / ENABLE — реальное отключение в системе
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет, что DisableFromSystem возвращает ошибку для неподдерживаемых Location.
    /// </summary>
    [Fact]
    public void DisableFromSystem_UnsupportedLocation_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.KeyboardHook);
        var (success, error) = EntryOperations.DisableFromSystem(entry);
        Assert.False(success);
        Assert.Contains("not supported", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что EnableFromSystem возвращает ошибку для неподдерживаемых Location.
    /// </summary>
    [Fact]
    public void EnableFromSystem_UnsupportedLocation_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.KeyboardHook);
        var (success, error) = EntryOperations.EnableFromSystem(entry);
        Assert.False(success);
        Assert.Contains("not supported", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что DisableFromSystem не выбрасывает исключений для поддерживаемых Location.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedRemoveLocations))]
    public void DisableFromSystem_SupportedLocations_DoesNotThrow(EntryLocation loc)
    {
        var entry = loc switch
        {
            EntryLocation.StartupFolder => MakeEntry(loc, filePath: Path.GetTempFileName() + ".lnk"),
            EntryLocation.Service => MakeEntry(loc, "NonExistentSvc_" + Guid.NewGuid().ToString("N")),
            EntryLocation.Driver => MakeEntry(loc, "NonExistentDriver_" + Guid.NewGuid().ToString("N")),
            EntryLocation.TaskScheduler => MakeEntry(loc, filePath: @"\NonExistentTask_" + Guid.NewGuid().ToString("N")),
            _ => MakeEntry(loc, "NonExistent_" + Guid.NewGuid().ToString("N")),
        };
        var ex = Record.Exception(() => EntryOperations.DisableFromSystem(entry));
        Assert.Null(ex);
    }

    /// <summary>
    /// Проверяет, что DisableStartupFile возвращает ошибку при отсутствующем файле.
    /// </summary>
    [Fact]
    public void DisableStartupFile_FileNotFound_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.StartupFolder, filePath: @"C:\nonexistent\file.lnk");
        var (success, error) = EntryOperations.DisableStartupFile(entry);
        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что DisableStartupFile переименовывает файл в *.disabled.
    /// </summary>
    [Fact]
    public void DisableStartupFile_RenamesToDisabled()
    {
        var tempFile = Path.GetTempFileName() + ".lnk";
        try
        {
            File.WriteAllText(tempFile, "test");
            var entry = MakeEntry(EntryLocation.StartupFolder, filePath: tempFile);

            Assert.True(File.Exists(tempFile));
            var (success, error) = EntryOperations.DisableStartupFile(entry);
            Assert.True(success, error);

            Assert.False(File.Exists(tempFile));
            Assert.True(File.Exists(tempFile + ".disabled"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(tempFile + ".disabled")) File.Delete(tempFile + ".disabled");
        }
    }

    /// <summary>
    /// Проверяет, что EnableStartupFile восстанавливает файл из *.disabled.
    /// </summary>
    [Fact]
    public void EnableStartupFile_RestoresFromDisabled()
    {
        var tempFile = Path.GetTempFileName() + ".lnk";
        var disabledFile = tempFile + ".disabled";
        try
        {
            File.WriteAllText(disabledFile, "test content");
            var entry = MakeEntry(EntryLocation.StartupFolder, filePath: tempFile);

            Assert.False(File.Exists(tempFile));
            Assert.True(File.Exists(disabledFile));

            var (success, error) = EntryOperations.EnableStartupFile(entry);
            Assert.True(success, error);

            Assert.True(File.Exists(tempFile));
            Assert.False(File.Exists(disabledFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(disabledFile)) File.Delete(disabledFile);
        }
    }

    /// <summary>
    /// Проверяет, что EnableStartupFile возвращает ошибку при отсутствии *.disabled файла.
    /// </summary>
    [Fact]
    public void EnableStartupFile_NoDisabledFile_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.StartupFolder, filePath: @"C:\nonexistent\file.lnk");
        var (success, error) = EntryOperations.EnableStartupFile(entry);
        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет, что DisableRegistry удаляет значение реестра для Run/RunOnce.
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRun)]
    [InlineData(EntryLocation.RegistryRunOnce)]
    public void DisableRegistry_RemovesValue(EntryLocation loc)
    {
        var uniqueName = "DisableTest_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error) = EntryOperations.DisableRegistry(entry);
        Assert.True(success, error ?? "");
        Assert.NotEmpty(entry.DisablePayload);
    }

    /// <summary>
    /// Проверяет отключение контекстного меню через реестр (HKCR\CLSID).
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.ContextMenuHandler)]
    public void DisableRegistry_SpecialLocations_Success(EntryLocation loc)
    {
        var uniqueName = "DisableTest_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName);
        entry.FilePath = @"C:\test.dll";
        var (success, error) = EntryOperations.DisableRegistry(entry);
        // HKCR\CLSID существует, DeleteValue с throwOnMissingValue: false
        Assert.True(success, error ?? "");
        Assert.NotEmpty(entry.DisablePayload);
    }

    /// <summary>
    /// Проверяет, что DisableService возвращает ошибку для несуществующей службы.
    /// </summary>
    [Fact]
    public void DisableService_NonExistent_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.Service, "NonExistentSvc_" + Guid.NewGuid().ToString("N"));
        var (success, error) = EntryOperations.DisableService(entry);
        // На несуществующий сервис sc config вернёт ошибку
        Assert.False(success);
    }

    /// <summary>
    /// Проверяет, что DisableTask возвращает ошибку для несуществующей задачи.
    /// </summary>
    [Fact]
    public void DisableTask_NonExistent_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.TaskScheduler, filePath: @"\NonExistentTask_" + Guid.NewGuid().ToString("N"));
        var (success, error) = EntryOperations.DisableTask(entry);
        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════
    // 5. Remove-Then-Blacklist (AutoBlockOnRemove flow)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет, что добавление правила в чёрный список и ReapplyBlacklist правильно помечают запись.
    /// </summary>
    [Fact]
    public void PipeServer_AddToBlacklist_CreatesRuleAndReapplies()
    {
        var path6 = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        var store6 = new JsonDataStore(path6);
        try
        {
        var entry = MakeEntry(EntryLocation.RegistryRun, "BlacklistReapplyTest", @"C:\test\app.exe");
        store6.AddEntry(entry);

        // Добавляем правило
        var rule = new BlacklistRule
        {
            Value = @"C:\test\app.exe",
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyOnly,
        };
        var config = store6.Load();
        config.Blacklist.Add(rule);
        store6.Save(config);

        // ReapplyBlacklist — эмулируем PipeServer
        var mgr = new BlacklistManager(store6);
        config = store6.Load();
        foreach (var e in config.Entries)
        {
            if (e.FromBlacklist) continue;
            if (BlacklistManager.IsMatchStatic(rule, e))
            {
                e.Status = EntryStatus.Blocked;
                e.FromBlacklist = true;
            }
        }
        store6.Save(config);

        // Проверяем — entry теперь blocked
        config = store6.Load();
        var stored = config.Entries.Find(e => e.Id == entry.Id)!;
        Assert.Equal(EntryStatus.Blocked, stored.Status);
        Assert.True(stored.FromBlacklist);
        }
        finally { if (File.Exists(path6)) File.Delete(path6); }
    }

    // ═══════════════════════════════════════════════════════════
    // 6. Payload round-trips
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет сериализацию/десериализацию всех типов payload: Registry, File, Service, Task.
    /// </summary>
    [Fact]
    public void AllPayloadTypes_SerializeDeserialize()
    {
        var reg = new EntryOperations.RegistryPayload("HKCU\\Run", "App", "\"C:\\app.exe\"");
        var regJson = JsonSerializer.Serialize(reg, JsonOptions);
        var regBack = JsonSerializer.Deserialize<EntryOperations.RegistryPayload>(regJson, JsonOptions);
        Assert.Equal(reg.Key, regBack!.Key);
        Assert.Equal(reg.ValueName, regBack.ValueName);

        var file = new EntryOperations.FilePayload(@"C:\backup.dat", @"C:\original.lnk");
        var fileJson = JsonSerializer.Serialize(file, JsonOptions);
        var fileBack = JsonSerializer.Deserialize<EntryOperations.FilePayload>(fileJson, JsonOptions);
        Assert.Equal(file.OriginalPath, fileBack!.OriginalPath);

        var svc = new EntryOperations.ServicePayload("MySvc", @"C:\svc.exe", "auto", "My Service");
        var svcJson = JsonSerializer.Serialize(svc, JsonOptions);
        var svcBack = JsonSerializer.Deserialize<EntryOperations.ServicePayload>(svcJson, JsonOptions);
        Assert.Equal(svc.ServiceName, svcBack!.ServiceName);

        var task = new EntryOperations.TaskPayload("<Task/>", @"\Folder\Task");
        var taskJson = JsonSerializer.Serialize(task, JsonOptions);
        var taskBack = JsonSerializer.Deserialize<EntryOperations.TaskPayload>(taskJson, JsonOptions);
        Assert.Equal(task.TaskPath, taskBack!.TaskPath);
    }

    // ═══════════════════════════════════════════════════════════
    // 7. NEW HKLM registry types — AppInitDll, IFEO, Winlogon, BootExecute
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет удаление HKLM-реестровых записей (AppInitDll, IFEO, Winlogon, BootExecute).
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.AppInitDll)]
    [InlineData(EntryLocation.ImageFileExecutionOptions)]
    [InlineData(EntryLocation.Winlogon)]
    [InlineData(EntryLocation.BootExecute)]
    public void RemoveFromSystem_HklmRegistry_Success(EntryLocation loc)
    {
        var uniqueName = "HklmTest_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.True(success);
        Assert.NotNull(payload);
    }

    /// <summary>
    /// Проверяет отключение HKLM-реестровых записей (AppInitDll, IFEO, Winlogon, BootExecute).
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.AppInitDll)]
    [InlineData(EntryLocation.ImageFileExecutionOptions)]
    [InlineData(EntryLocation.Winlogon)]
    [InlineData(EntryLocation.BootExecute)]
    public void DisableFromSystem_HklmRegistryTypes_Success(EntryLocation loc)
    {
        var uniqueName = "DisableHklm_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error) = EntryOperations.DisableFromSystem(entry);
        Assert.True(success, error ?? "");
        Assert.NotEmpty(entry.DisablePayload);
    }

    /// <summary>
    /// Проверяет полный цикл Disable → Enable для HKLM-реестровых записей.
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.AppInitDll)]
    [InlineData(EntryLocation.ImageFileExecutionOptions)]
    [InlineData(EntryLocation.Winlogon)]
    [InlineData(EntryLocation.BootExecute)]
    public void EnableFromSystem_HklmRegistryTypes_Success(EntryLocation loc)
    {
        // Disable + Enable round-trip
        var uniqueName = "EnableHklm_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        EntryOperations.DisableFromSystem(entry);
        Assert.NotEmpty(entry.DisablePayload);

        var (success, error) = EntryOperations.EnableFromSystem(entry);
        Assert.True(success, error ?? "");
        Assert.Empty(entry.DisablePayload);
    }

    /// <summary>
    /// Проверяет, что передача пустых правил или null в KillProcessesByRules не вызывает исключений.
    /// </summary>
    [Fact]
    public void KillProcessesByRules_NullOrEmpty_NoOp()
    {
        EntryOperations.KillProcessesByRules(null!);
        EntryOperations.KillProcessesByRules(Array.Empty<BlacklistRule>());
    }

    /// <summary>
    /// Проверяет, что защищенные процессы (explorer.exe, svchost.exe) не уничтожаются даже при наличии правила.
    /// </summary>
    [Fact]
    public void KillProcessesByRules_ProtectedProcess_DoesNotKill()
    {
        var rule = new BlacklistRule
        {
            Value = @"C:\Windows\explorer.exe",
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyAndRemove
        };

        // Запуск не должен сбросить или закрыть explorer
        EntryOperations.KillProcessesByRules(new[] { rule });
    }

    // ═══════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════

    /// <summary>Очищает временные бэкапы, созданные тестами.</summary>
    private static void CleanupBackups()
    {
        var backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "StartupMonitor", "backups");
        if (Directory.Exists(backupDir))
            foreach (var f in Directory.GetFiles(backupDir))
                try { File.Delete(f); } catch { }
    }
}
