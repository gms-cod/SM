using Microsoft.Win32;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
public class SystemScannerVerificationTests
{
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    private static StartupEntry MakeDbEntry(EntryLocation loc, string name, string filePath,
        string? locationDetail = null)
    {
        var entry = new StartupEntry
        {
            Id = WatcherHelper.ComputeStableId(new StartupEntry
            {
                Name = name,
                Location = loc,
                LocationDetail = locationDetail ?? $@"HKLM\SomeKey\{name}"
            }),
            Name = name,
            FilePath = filePath,
            Location = loc,
            LocationDetail = locationDetail ?? $@"HKLM\SomeKey\{name}",
        };
        return entry;
    }

    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    private static StartupEntry MakeScanEntry(EntryLocation loc, string name, string filePath,
        string? locationDetail = null)
    {
        return new StartupEntry
        {
            Name = name,
            FilePath = filePath,
            Location = loc,
            LocationDetail = locationDetail ?? $@"HKLM\SomeKey\{name}",
        };
    }

    // ───────── TryMatch tests ─────────

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_LocationAndDetailMatch_ReturnsTrue()
    {
        var db = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");
        var scan = MakeScanEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");

        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_LocationDetailExact_ReturnsTrue()
    {
        var detail = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\MyApp";
        var db = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe", detail);
        var scan = MakeScanEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe", detail);

        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_WinlogonSubkey_LocationDetailMatches_ReturnsTrue()
    {
        // Verifies component behavior and assertions.
        var detail = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit\SomeValue";
        var db = MakeDbEntry(EntryLocation.Winlogon, @"Userinit\SomeValue", @"C:\windows\some.exe", detail);
        // Verifies component behavior and assertions.
        var scan = MakeScanEntry(EntryLocation.Winlogon, "SomeValue", @"C:\windows\some.exe", detail);

        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_NameAndPathMatch_ReturnsTrue()
    {
        var db = MakeDbEntry(EntryLocation.StartupFolder, "app.lnk", @"C:\Users\Test\Startup\app.lnk");
        var scan = MakeScanEntry(EntryLocation.StartupFolder, "app.lnk", @"C:\Users\Test\Startup\app.lnk");

        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_DifferentNameSameFilePath_ReturnsFalse()
    {
        var db = MakeDbEntry(EntryLocation.StartupFolder, "oldname.lnk", @"C:\Users\Test\Startup\app.lnk");
        var scan = MakeScanEntry(EntryLocation.StartupFolder, "newname.lnk", @"C:\Users\Test\Startup\app.lnk");

        Assert.False(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_WinlogonSubkey_LastSegmentMatch_ReturnsTrue()
    {
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var db = MakeDbEntry(EntryLocation.Winlogon, @"Userinit\SomeValue", @"C:\windows\some.exe");
        var scan = MakeScanEntry(EntryLocation.Winlogon, "SomeValue", @"C:\windows\some.exe");

        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_DifferentLocation_ReturnsFalse()
    {
        var db = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");
        var scan = MakeScanEntry(EntryLocation.StartupFolder, "MyApp", @"C:\app.exe");

        Assert.False(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_CompletelyDifferent_ReturnsFalse()
    {
        var db = MakeDbEntry(EntryLocation.RegistryRun, "App1", @"C:\app1.exe");
        var scan = MakeScanEntry(EntryLocation.RegistryRun, "App2", @"C:\app2.exe");

        Assert.False(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_ServiceByDetail_ReturnsTrue()
    {
        // Verifies component behavior and assertions.
        var db = MakeDbEntry(EntryLocation.Service, "My Service", @"C:\svc.exe", "MySvc");
        var scan = MakeScanEntry(EntryLocation.Service, "My Service", @"C:\svc.exe", "MySvc");

        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    // ───────── VerifyInScan tests ─────────

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyInScan_EntryExistsInScan_ReturnsTrue()
    {
        var dbEntry = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");
        var scan = new List<StartupEntry>
        {
            MakeScanEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe"),
            MakeScanEntry(EntryLocation.RegistryRunOnce, "Other", @"C:\other.exe"),
        };

        Assert.True(SystemScannerService.VerifyInScan(dbEntry, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyInScan_EntryNotInScan_ReturnsFalse()
    {
        var dbEntry = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");
        var scan = new List<StartupEntry>
        {
            MakeScanEntry(EntryLocation.RegistryRun, "OtherApp", @"C:\other.exe"),
        };

        Assert.False(SystemScannerService.VerifyInScan(dbEntry, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyInScan_EmptyScan_ReturnsFalse()
    {
        var dbEntry = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");
        var scan = new List<StartupEntry>();

        Assert.False(SystemScannerService.VerifyInScan(dbEntry, scan));
    }

    // ───────── EntryOperations.VerifyEntry tests ─────────

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void EntryVerify_EntryInScan_ReturnsTrue()
    {
        var dbEntry = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");
        var scan = new List<StartupEntry>
        {
            MakeScanEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe"),
        };

        Assert.True(EntryOperations.VerifyEntry(dbEntry, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void EntryVerify_EntryNotInScan_ReturnsFalse()
    {
        var dbEntry = MakeDbEntry(EntryLocation.RegistryRun, "MyApp", @"C:\app.exe");
        var scan = new List<StartupEntry>
        {
            MakeScanEntry(EntryLocation.RegistryRunOnce, "Other", @"C:\other.exe"),
        };

        Assert.False(EntryOperations.VerifyEntry(dbEntry, scan));
    }

    // ───────── CleanupStaleEntries tests ─────────

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CleanupStaleEntries_RemovesStaleOnly()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"smon-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(tmpFile);
        var entries = new List<StartupEntry>
        {
            MakeDbEntry(EntryLocation.RegistryRun, "Alive", @"C:\alive.exe"),
            MakeDbEntry(EntryLocation.RegistryRun, "Stale", @"C:\stale.exe"),
            MakeDbEntry(EntryLocation.RegistryRun, "AlsoAlive", @"C:\also.exe"),
        };
        var scan = new List<StartupEntry>
        {
            MakeScanEntry(EntryLocation.RegistryRun, "Alive", @"C:\alive.exe"),
            MakeScanEntry(EntryLocation.RegistryRun, "AlsoAlive", @"C:\also.exe"),
        };

        var before = entries.Count;
        EntryOperations.CleanupStaleEntries(entries, store, scan, null);
        var after = entries.Count;

        Assert.Equal(3, before);
        Assert.Equal(2, after);
        Assert.DoesNotContain(entries, e => e.Name == "Stale");
        Assert.Contains(entries, e => e.Name == "Alive");
        Assert.Contains(entries, e => e.Name == "AlsoAlive");
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CleanupStaleEntries_AllAlive_RemovesNothing()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"smon-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(tmpFile);
        var entries = new List<StartupEntry>
        {
            MakeDbEntry(EntryLocation.RegistryRun, "App1", @"C:\app1.exe"),
            MakeDbEntry(EntryLocation.RegistryRun, "App2", @"C:\app2.exe"),
        };
        var scan = new List<StartupEntry>
        {
            MakeScanEntry(EntryLocation.RegistryRun, "App1", @"C:\app1.exe"),
            MakeScanEntry(EntryLocation.RegistryRun, "App2", @"C:\app2.exe"),
        };

        EntryOperations.CleanupStaleEntries(entries, store, scan, null);
        Assert.Equal(2, entries.Count);
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CleanupStaleEntries_AllStale_RemovesAll()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"smon-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(tmpFile);
        var entries = new List<StartupEntry>
        {
            MakeDbEntry(EntryLocation.RegistryRun, "App1", @"C:\app1.exe"),
            MakeDbEntry(EntryLocation.RegistryRun, "App2", @"C:\app2.exe"),
        };
        var scan = new List<StartupEntry>();

        EntryOperations.CleanupStaleEntries(entries, store, scan, null);
        Assert.Empty(entries);
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CleanupStaleEntries_DoesNotWriteHistoryForStale()
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"smon-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(tmpFile);
        var entries = new List<StartupEntry>
        {
            MakeDbEntry(EntryLocation.RegistryRun, "Stale", @"C:\stale.exe"),
        };
        var scan = new List<StartupEntry>();

        EntryOperations.CleanupStaleEntries(entries, store, scan, null);

        var cfg = store.Load();
        Assert.Empty(cfg.History);
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_WinlogonSubkey_DifferentLocationDetail_StillMatchByPath()
    {
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var db = MakeDbEntry(EntryLocation.Winlogon, @"Userinit\SomeValue",
            @"C:\windows\some.exe",
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit\SomeValue");
        var scan = MakeScanEntry(EntryLocation.Winlogon, "SomeValue",
            @"C:\windows\some.exe",
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit");

        // Verifies component behavior and assertions.
        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void TryMatch_Wow6432Node_LocationDetailDiffers_StillMatchByPath()
    {
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var db = MakeDbEntry(EntryLocation.RegistryRun, "Nikon",
            @"C:\Program Files (x86)\Nikon\Nikon.exe",
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run\Nikon");
        var scan = MakeScanEntry(EntryLocation.RegistryRun, "Nikon",
            @"C:\Program Files (x86)\Nikon\Nikon.exe",
            @"HKLM\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run\Nikon");

        Assert.True(SystemScannerService.TryMatch(db, scan));
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CleanupStaleEntries_DisabledAndBlocked_ArePreserved()
    {
        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        var tmpFile = Path.Combine(Path.GetTempPath(), $"smon-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(tmpFile);
        var disabled = MakeDbEntry(EntryLocation.RegistryRun, "Disabled", @"C:\disabled.exe");
        disabled.Status = EntryStatus.Disabled;
        var blocked = MakeDbEntry(EntryLocation.RegistryRun, "Blocked", @"C:\blocked.exe");
        blocked.Status = EntryStatus.Blocked;
        var staleEnabled = MakeDbEntry(EntryLocation.RegistryRun, "StaleEnabled", @"C:\stale.exe");
        var entries = new List<StartupEntry> { disabled, blocked, staleEnabled };
        var scan = new List<StartupEntry>(); // Empty snapshot - all enabled entries are considered stale

        EntryOperations.CleanupStaleEntries(entries, store, scan, null);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "Disabled");
        Assert.Contains(entries, e => e.Name == "Blocked");
        Assert.DoesNotContain(entries, e => e.Name == "StaleEnabled");
    }

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CleanupStaleEntries_FailedScanSection_IsPreserved()
    {
        // Verifies component behavior and assertions.
        var tmpFile = Path.Combine(Path.GetTempPath(), $"smon-{Guid.NewGuid():N}", "startup-monitor.json");
        var store = new JsonDataStore(tmpFile);
        var entries = new List<StartupEntry>
        {
            MakeDbEntry(EntryLocation.Service, "MyService", @"C:\svc.exe", "MyService"),
            MakeDbEntry(EntryLocation.RegistryRun, "StaleApp", @"C:\stale.exe"),
        };
        var scan = new List<StartupEntry>();
        var failed = new[] { EntryLocation.Service, EntryLocation.Driver };

        EntryOperations.CleanupStaleEntries(entries, store, scan, null, failedLocations: failed);

        Assert.Single(entries);
        Assert.Equal("MyService", entries[0].Name);
    }

    // ───────── Active Setup filtering tests ─────────

    /// <summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ScanAll_ActiveSetup_FiltersInactiveComponents()
    {
        const string basePath = @"Software\Microsoft\Active Setup\Installed Components";
        var created = new List<string>();

        try
        {
            using (var root = Registry.CurrentUser.CreateSubKey(basePath))
            {
                // Verifies component behavior and assertions.
                using (var k = root.CreateSubKey("{SMonTestActive-0000-0000-0000-000000000001}"))
                    k.SetValue("StubPath", @"C:\Windows\System32\smon-test.exe -flag");
                created.Add("{SMonTestActive-0000-0000-0000-000000000001}");

                // Verifies component behavior and assertions.
                using (var k = root.CreateSubKey(">{SMonTestDeactivated-0000-0000-0000-000000000002}"))
                    k.SetValue("StubPath", @"C:\Windows\System32\old.exe");
                created.Add(">{SMonTestDeactivated-0000-0000-0000-000000000002}");

                // Verifies component behavior and assertions.
                using (var k = root.CreateSubKey("{SMonTestStubMarked-0000-0000-0000-000000000003}"))
                    k.SetValue("StubPath", ">C:\\Windows\\System32\\old2.exe");
                created.Add("{SMonTestStubMarked-0000-0000-0000-000000000003}");

                // Verifies component behavior and assertions.
                using (var k = root.CreateSubKey("{SMonTestNoStub-0000-0000-0000-000000000004}"))
                    k.SetValue("ComponentID", "TestComp");
                created.Add("{SMonTestNoStub-0000-0000-0000-000000000004}");

                // Verifies component behavior and assertions.
                using (var k = root.CreateSubKey("{SMonTestMeta-0000-0000-0000-000000000005}"))
                {
                    k.SetValue("Locale", "*");
                    k.SetValue("Version", "1.0");
                }
                created.Add("{SMonTestMeta-0000-0000-0000-000000000005}");
            }

            var results = new SystemScannerService().ScanAll();
            var testEntries = results
                .Where(e => e.Location == EntryLocation.ActiveSetup
                            && e.Name.Contains("SMonTest", StringComparison.Ordinal))
                .ToList();

            // Verifies component behavior and assertions.
            Assert.Single(testEntries);
            Assert.Equal("{SMonTestActive-0000-0000-0000-000000000001}", testEntries[0].Name);
            Assert.Equal(@"C:\Windows\System32\smon-test.exe", testEntries[0].FilePath);
        }
        finally
        {
            using var root = Registry.CurrentUser.OpenSubKey(basePath, writable: true);
            foreach (var name in created)
                root?.DeleteSubKey(name, throwOnMissingSubKey: false);
        }
    }

    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    [Fact]
    public void ReadStartupTaskFromManifest_ReturnsTaskInfo()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smon-appx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var manifest = Path.Combine(dir, "AppxManifest.xml");
        File.WriteAllText(manifest, """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
                     xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10">
              <Applications>
                <Application Id="MSTeams">
                  <uap:VisualElements DisplayName="Microsoft Teams" />
                  <desktop:Extensions>
                    <desktop:Extension Category="windows.startupTask">
                      <desktop:StartupTask TaskId="TeamsTfwStartupTask" Enabled="true" />
                    </desktop:Extension>
                  </desktop:Extensions>
                </Application>
              </Applications>
            </Package>
            """);

        var (taskId, exe, displayName) =
            SystemScannerService.ReadStartupTaskFromManifest(manifest);

        Assert.Equal("TeamsTfwStartupTask", taskId);
        Assert.Equal("Microsoft Teams", displayName);
    }

    /// <summary>Verifies test scenario for SystemScannerVerificationTests.cs.</summary>
    [Fact]
    public void ReadStartupTaskFromManifest_NoTask_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smon-appx2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var manifest = Path.Combine(dir, "AppxManifest.xml");
        File.WriteAllText(manifest, """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Applications>
                <Application Id="NoTask">
                  <uap:VisualElementsDisplayName xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" DisplayName="Plain" />
                </Application>
              </Applications>
            </Package>
            """);

        var (taskId, exe, displayName) =
            SystemScannerService.ReadStartupTaskFromManifest(manifest);

        Assert.Equal(string.Empty, taskId);
        Assert.Equal(string.Empty, displayName);
    }
}
