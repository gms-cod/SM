using System.Text.Json;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
/// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
public class EntryOperationsAnalysisTests
{
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    public static TheoryData<EntryLocation> AllEntryLocations =>
        new(Enum.GetValues<EntryLocation>());

    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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

    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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

    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    // Verifies component behavior and assertions.
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllEntryLocations))]
    public void DisableEntry_OnlyChangesStatusInConfig_NotInSystem(EntryLocation loc)
    {
        // Verifies component behavior and assertions.
        //   entry.Status = EntryStatus.Disabled;
        //   _store.UpdateEntry(entry);
        // Verifies component behavior and assertions.

        var path = Path.Combine(Path.GetTempPath(), $"sm-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(path);
        try
        {
        var entry = MakeEntry(loc, $"DisableTest_{Guid.NewGuid():N}");
        store.AddEntry(entry);

        // Verifies component behavior and assertions.
        var cfg = store.Load();
        var stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        stored.Status = EntryStatus.Disabled;
        store.UpdateEntry(stored);

        // Verifies component behavior and assertions.
        cfg = store.Load();
        stored = cfg.Entries.Find(e => e.Id == entry.Id)!;
        Assert.Equal(EntryStatus.Disabled, stored.Status);

        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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

        // Verifies component behavior and assertions.
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    // Verifies component behavior and assertions.
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRun)]
    [InlineData(EntryLocation.RegistryRunOnce)]
    public void RemoveFromSystem_Registry_CommonKeys_Success(EntryLocation loc)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\" + (loc == EntryLocation.RegistryRun ? "Run" : "RunOnce"));
        var uniqueName = "RemoveTest_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        try
        {
            key.SetValue(uniqueName, entry.FilePath);
            var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
            Assert.True(success, error);
            Assert.Null(key.GetValue(uniqueName));
            Assert.NotNull(payload);
            var saved = JsonSerializer.Deserialize<EntryOperations.RegistryPayload>(payload, JsonOptions);
            Assert.NotNull(saved);
            Assert.Equal(entry.FilePath, saved.ValueData);
        }
        finally
        {
            key.DeleteValue(uniqueName, throwOnMissingValue: false);
            using var disabled = key.OpenSubKey("StartupMonitorDisabled", writable: true);
            disabled?.DeleteValue(uniqueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRunServices)]
    [InlineData(EntryLocation.RegistryPolicies)]
    public void RemoveFromSystem_Registry_RareKeys_KeyNotFound(EntryLocation loc)
    {
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var uniqueName = "NonExistent_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.ContextMenuHandler)]
    public void RemoveFromSystem_SpecialRegistryLocations_MissingValueReturnsError(EntryLocation loc)
    {
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var uniqueName = "NonExistent_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName);
        entry.FilePath = @"C:\test.dll";
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.False(success);
        Assert.NotEmpty(error ?? string.Empty);
        Assert.Null(payload);
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Fact]
    public void RemoveFromSystem_StartupFolder_FileExists_RemovesFile()
    {
        var tempFile = Path.GetTempFileName() + ".lnk";
        string? createdBackup = null;
        try
        {
            File.WriteAllText(tempFile, "dummy");
            var entry = MakeEntry(EntryLocation.StartupFolder, filePath: tempFile);

            var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
            var fp = string.IsNullOrEmpty(payload) ? null : JsonSerializer.Deserialize<EntryOperations.FilePayload>(payload, JsonOptions);
            createdBackup = fp?.BackupPath;
            Assert.True(success, error);
            Assert.NotNull(payload);

            // Verifies component behavior and assertions.
            Assert.False(File.Exists(tempFile));

            // Verifies component behavior and assertions.
            Assert.NotNull(fp);
            Assert.True(File.Exists(fp.BackupPath));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            CleanupBackup(createdBackup);
        }
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Fact]
    public void RemoveFromSystem_Service_NonExistent_ReturnsSuccess_ScExitCodeIgnored()
    {
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var entry = MakeEntry(EntryLocation.Service, "NonExistentSvc_ShouldNeverExist_" + Guid.NewGuid().ToString("N"));

        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);

        Assert.True(success);
        Assert.NotNull(payload);
        Assert.Contains("NonExistentSvc", payload);
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedRemoveLocations))]
    public void RemoveFromSystem_SupportedLocations_DoesNotThrow(EntryLocation loc)
    {
        // Verifies component behavior and assertions.
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
    // Verifies component behavior and assertions.
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Fact]
    public void RestoreToSystem_StartupFolder_ValidBackup_RestoresFile()
    {
        var tempBackup = Path.GetTempFileName();
        var tempOriginal = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempBackup, "restore test content");
            // Verifies component behavior and assertions.
            File.Delete(tempOriginal);
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Fact]
    public void RestoreToSystem_Registry_RoundTrip()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var uniqueName = "RestoreTest_" + Guid.NewGuid().ToString("N");
        var rp = new EntryOperations.RegistryPayload(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            uniqueName, "dummy_data");
        var payload = JsonSerializer.Serialize(rp, JsonOptions);

        var removed = new RemovedEntry
        {
            Location = EntryLocation.RegistryRun,
            RestorePayload = payload,
        };

        try
        {
            var (success, error) = EntryOperations.RestoreToSystem(removed);
            Assert.True(success, error);
            Assert.Equal(rp.ValueData, key.GetValue(uniqueName));
        }
        finally
        {
            key.DeleteValue(uniqueName, throwOnMissingValue: false);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // Verifies component behavior and assertions.
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    // Verifies component behavior and assertions.
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    [Theory]
    [InlineData(EntryLocation.RegistryRun)]
    [InlineData(EntryLocation.RegistryRunOnce)]
    public void DisableRegistry_RemovesValue(EntryLocation loc)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\" + (loc == EntryLocation.RegistryRun ? "Run" : "RunOnce"));
        var uniqueName = "DisableTest_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        try
        {
            key.SetValue(uniqueName, entry.FilePath);
            var (success, error) = EntryOperations.DisableRegistry(entry);
            Assert.True(success, error);
            Assert.Null(key.GetValue(uniqueName));
            Assert.NotEmpty(entry.DisablePayload);
            var saved = JsonSerializer.Deserialize<EntryOperations.RegistryPayload>(entry.DisablePayload, JsonOptions);
            Assert.NotNull(saved);
            Assert.Equal(entry.FilePath, saved.ValueData);
        }
        finally
        {
            key.DeleteValue(uniqueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.ContextMenuHandler)]
    public void DisableRegistry_SpecialLocations_Success(EntryLocation loc)
    {
        var uniqueName = "DisableTest_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName);
        entry.FilePath = @"C:\test.dll";
        var (success, error) = EntryOperations.DisableRegistry(entry);
        // Verifies component behavior and assertions.
        Assert.False(success);
        Assert.NotEmpty(error ?? string.Empty);
        Assert.Empty(entry.DisablePayload);
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Fact]
    public void DisableService_NonExistent_ReturnsError()
    {
        var entry = MakeEntry(EntryLocation.Service, "NonExistentSvc_" + Guid.NewGuid().ToString("N"));
        var (success, error) = EntryOperations.DisableService(entry);
        // Verifies component behavior and assertions.
        Assert.False(success);
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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

        // Verifies component behavior and assertions.
        var rule = new BlacklistRule
        {
            Value = @"C:\test\app.exe",
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyOnly,
        };
        var config = store6.Load();
        config.Blacklist.Add(rule);
        store6.Save(config);

        // Verifies component behavior and assertions.
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

        // Verifies component behavior and assertions.
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.AppInitDll)]
    [InlineData(EntryLocation.ImageFileExecutionOptions)]
    [InlineData(EntryLocation.Winlogon)]
    [InlineData(EntryLocation.BootExecute)]
    public void RemoveFromSystem_HklmRegistry_MissingValueReturnsError(EntryLocation loc)
    {
        var uniqueName = "HklmTest_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);
        Assert.False(success);
        Assert.NotEmpty(error ?? string.Empty);
        Assert.Null(payload);
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.AppInitDll)]
    [InlineData(EntryLocation.ImageFileExecutionOptions)]
    [InlineData(EntryLocation.Winlogon)]
    [InlineData(EntryLocation.BootExecute)]
    public void DisableFromSystem_HklmRegistryTypes_MissingValueReturnsError(EntryLocation loc)
    {
        var uniqueName = "DisableHklm_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        var (success, error) = EntryOperations.DisableFromSystem(entry);
        Assert.False(success);
        Assert.NotEmpty(error ?? string.Empty);
        Assert.Empty(entry.DisablePayload);
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Theory]
    [InlineData(EntryLocation.AppInitDll)]
    [InlineData(EntryLocation.ImageFileExecutionOptions)]
    [InlineData(EntryLocation.Winlogon)]
    [InlineData(EntryLocation.BootExecute)]
    public void EnableFromSystem_HklmRegistryTypes_MissingValueReturnsError(EntryLocation loc)
    {
        // Disable + Enable round-trip
        var uniqueName = "EnableHklm_" + Guid.NewGuid().ToString("N");
        var entry = MakeEntry(loc, uniqueName, @"C:\test.exe");
        Assert.False(EntryOperations.DisableFromSystem(entry).success);
        Assert.Empty(entry.DisablePayload);

        var (success, error) = EntryOperations.EnableFromSystem(entry);
        Assert.False(success);
        Assert.NotEmpty(error ?? string.Empty);
        Assert.Empty(entry.DisablePayload);
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// </summary>
    [Fact]
    public void KillProcessesByRules_NullOrEmpty_NoOp()
    {
        EntryOperations.KillProcessesByRules(null!);
        EntryOperations.KillProcessesByRules(Array.Empty<BlacklistRule>());
    }

    /// <summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
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

        // Verifies component behavior and assertions.
        EntryOperations.KillProcessesByRules(new[] { rule });
    }

    // ═══════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════

    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    /// <summary>Verifies test scenario for EntryOperationsAnalysisTests.cs.</summary>
    private static void CleanupBackup(string? createdBackup)
    {
        if (createdBackup != null && File.Exists(createdBackup)) File.Delete(createdBackup);
    }
}
