using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты для защиты системнозначимых процессов и служб Windows от удаления и блокировки.
/// Все тесты используют фиктивные и изолированные структуры данных без воздействия на реальную ОС.
/// </summary>
public class SystemProtectionTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonDataStore _store;
    private readonly NotificationService _notifications;

    public SystemProtectionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"sm-protection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new JsonDataStore(Path.Combine(_testDir, "startup-entries.json"));
        var tracker = new InitialScanTracker();
        tracker.MarkCompleted();
        _notifications = new NotificationService(tracker);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData("svchost.exe", true)]
    [InlineData("svchost", true)]
    [InlineData("csrss.exe", true)]
    [InlineData("lsass.exe", true)]
    [InlineData("services.exe", true)]
    [InlineData("smss.exe", true)]
    [InlineData("wininit.exe", true)]
    [InlineData("explorer.exe", true)]
    [InlineData("C:\\Windows\\System32\\svchost.exe", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("mycustomapp.exe", false)]
    [InlineData("", true)]
    public void IsProtectedProcessName_IdentifiesCoreProcesses(string processOrPath, bool expected)
    {
        Assert.Equal(expected, SystemProtectionHelper.IsProtectedProcessName(processOrPath));
    }

    [Theory]
    [InlineData("svchost.exe", true)]
    [InlineData("rundll32.exe", true)]
    [InlineData("dllhost.exe", true)]
    [InlineData("cmd.exe", true)]
    [InlineData("powershell.exe", true)]
    [InlineData("pwsh.exe", true)]
    [InlineData("cscript.exe", true)]
    [InlineData("wscript.exe", true)]
    [InlineData("C:\\Windows\\System32\\svchost.exe", true)]
    [InlineData("telegram.exe", false)]
    [InlineData("chrome.exe", false)]
    public void IsHostProcess_IdentifiesHostBinaries(string processOrPath, bool expected)
    {
        Assert.Equal(expected, SystemProtectionHelper.IsHostProcess(processOrPath));
    }

    [Theory]
    [InlineData("rpcss", true)]
    [InlineData("RPCSS", true)]
    [InlineData("dcomlaunch", true)]
    [InlineData("plugplay", true)]
    [InlineData("eventlog", true)]
    [InlineData("cryptsvc", true)]
    [InlineData("schedule", true)]
    [InlineData("unistoresvc", true)]
    [InlineData("cdpusersvc_45678", true)]
    [InlineData("userdataaccess_abcdef", true)]
    [InlineData("cbdhsvc_12345", true)]
    [InlineData("MyCustomService", false)]
    [InlineData("MockCustomService", false)]
    [InlineData("", false)]
    public void IsCoreWindowsService_IdentifiesCriticalServices(string serviceName, bool expected)
    {
        Assert.Equal(expected, SystemProtectionHelper.IsCoreWindowsService(serviceName));
    }

    [Fact]
    public void IsProtectedSystemEntry_CoreWindowsService_ReturnsTrue()
    {
        var entry = new StartupEntry
        {
            Name = "DcomLaunch",
            Location = EntryLocation.Service,
            LocationDetail = "DcomLaunch",
            FilePath = @"C:\Windows\system32\svchost.exe -k DcomLaunch"
        };

        Assert.True(SystemProtectionHelper.IsProtectedSystemEntry(entry));
    }

    [Fact]
    public void IsProtectedSystemEntry_SystemTaskScheduler_ReturnsTrue()
    {
        var entry = new StartupEntry
        {
            Name = "ScheduledDefrag",
            Location = EntryLocation.TaskScheduler,
            LocationDetail = @"\Microsoft\Windows\Defrag\ScheduledDefrag",
            FilePath = @"C:\Windows\system32\defrag.exe -c"
        };

        Assert.True(SystemProtectionHelper.IsProtectedSystemEntry(entry));
    }

    [Fact]
    public void IsProtectedSystemEntry_ThirdPartyService_ReturnsFalse()
    {
        var entry = new StartupEntry
        {
            Name = "MyTestAppService",
            Location = EntryLocation.Service,
            LocationDetail = "MyTestAppService",
            FilePath = @"C:\Program Files\TestApp\testsvc.exe"
        };

        Assert.False(SystemProtectionHelper.IsProtectedSystemEntry(entry));
    }

    [Fact]
    public void IsProtectedSystemEntry_ThirdPartyRegistryEntry_ReturnsFalse()
    {
        var entry = new StartupEntry
        {
            Name = "MyHelper",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            FilePath = @"C:\Program Files\Helper\helper.exe"
        };

        Assert.False(SystemProtectionHelper.IsProtectedSystemEntry(entry));
    }

    [Fact]
    public void BlacklistManager_IsMatchStatic_DoesNotMatchProtectedCoreServicesByGenericPath()
    {
        var protectedService = new StartupEntry
        {
            Name = "DcomLaunch",
            Location = EntryLocation.Service,
            LocationDetail = "DcomLaunch",
            FilePath = @"C:\Windows\system32\svchost.exe -k DcomLaunch"
        };

        // Правило по пути к svchost.exe
        var rule = new BlacklistRule
        {
            MatchType = BlacklistMatchType.Path,
            Value = @"C:\Windows\system32\svchost.exe"
        };

        // Защищённая служба не должна сопоставляться
        Assert.False(BlacklistManager.IsMatchStatic(rule, protectedService));
    }

    [Fact]
    public void BlacklistManager_IsMatchStatic_DoesNotMatchHostProcessesByGenericExeName()
    {
        var hostService = new StartupEntry
        {
            Name = "SomeHostService",
            Location = EntryLocation.Service,
            LocationDetail = "SomeHostService",
            FilePath = @"C:\Windows\system32\svchost.exe -k CustomGroup"
        };

        var rule = new BlacklistRule
        {
            MatchType = BlacklistMatchType.Path,
            Value = "svchost.exe"
        };

        // Служба-хост не должна сопоставляться по общему ExeName без точного имени службы
        Assert.False(BlacklistManager.IsMatchStatic(rule, hostService));
    }

    [Fact]
    public void BlacklistManager_IsMatchStatic_MatchesCustomServiceByName()
    {
        var customService = new StartupEntry
        {
            Name = "CustomBadService",
            Location = EntryLocation.Service,
            LocationDetail = "CustomBadService",
            FilePath = @"C:\Tools\badsvc.exe"
        };

        var rule = new BlacklistRule
        {
            MatchType = BlacklistMatchType.Path,
            Value = "CustomBadService"
        };

        Assert.True(BlacklistManager.IsMatchStatic(rule, customService));
    }

    [Fact]
    public void EntryOperations_DisableService_RejectsCoreWindowsService()
    {
        // Попытка отключить ключевую службу Windows (например, rpcss) должна быть немедленно отклонена
        var entry = new StartupEntry
        {
            Name = "rpcss",
            Location = EntryLocation.Service,
            LocationDetail = "rpcss",
            FilePath = @"C:\Windows\system32\svchost.exe -k rpcss"
        };
        var (success, error) = EntryOperations.DisableService(entry);
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("критически важных системных служб", error);
    }

    [Fact]
    public void EntryOperations_RemoveService_RejectsCoreWindowsService()
    {
        // Попытка удалить ключевую службу Windows (например, dcomlaunch) должна быть немедленно отклонена
        var entry = new StartupEntry
        {
            Name = "dcomlaunch",
            Location = EntryLocation.Service,
            LocationDetail = "dcomlaunch",
            FilePath = @"C:\Windows\system32\svchost.exe -k DcomLaunch"
        };
        var (success, error) = EntryOperations.RemoveService(entry, out _);
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("критически важных системных служб", error);
    }

    [Fact]
    public void EntryOperations_RemoveFromSystem_RejectsProtectedSystemEntry()
    {
        var protectedEntry = new StartupEntry
        {
            Name = "DcomLaunch",
            Location = EntryLocation.Service,
            LocationDetail = "DcomLaunch",
            FilePath = @"C:\Windows\system32\svchost.exe -k DcomLaunch"
        };

        var (success, error, _) = EntryOperations.RemoveFromSystem(protectedEntry);
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("защищённых системных компонентов", error);
    }

    [Fact]
    public void EntryOperations_DisableFromSystem_RejectsProtectedSystemEntry()
    {
        var protectedEntry = new StartupEntry
        {
            Name = "Shell",
            Location = EntryLocation.Winlogon,
            CommandLine = "explorer.exe"
        };

        var (success, error) = EntryOperations.DisableFromSystem(protectedEntry);
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("защищённых системных компонентов", error);
    }

    [Fact]
    public void EntryOperations_StartupFile_RejectsProtectedPaths()
    {
        var winEntry = new StartupEntry
        {
            Name = "SystemExe",
            Location = EntryLocation.StartupFolder,
            FilePath = @"C:\Windows\System32\svchost.exe"
        };

        var (disOk, disErr) = EntryOperations.DisableStartupFile(winEntry);
        Assert.False(disOk);
        Assert.Contains("защищённый", disErr);

        var (remOk, remErr) = EntryOperations.RemoveStartupFile(winEntry, out _);
        Assert.False(remOk);
        Assert.Contains("защищённый", remErr);

        var (resOk, resErr) = EntryOperations.RestoreFile("{\"BackupPath\":\"C:\\\\temp\\\\x.bak\",\"OriginalPath\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\"}");
        Assert.False(resOk);
        Assert.Contains("защищённый", resErr);
    }

    [Fact]
    public void SystemProtectionHelper_ProtectsWinlogonAndCommandLine()
    {
        var userinitEntry = new StartupEntry
        {
            Name = "Userinit",
            Location = EntryLocation.Winlogon,
            CommandLine = @"C:\Windows\System32\userinit.exe,"
        };
        Assert.True(SystemProtectionHelper.IsProtectedSystemEntry(userinitEntry));

        var bootExecEntry = new StartupEntry
        {
            Name = "BootExecute",
            Location = EntryLocation.BootExecute,
            CommandLine = "autocheck autochk *"
        };
        Assert.True(SystemProtectionHelper.IsProtectedSystemEntry(bootExecEntry));

        Assert.True(SystemProtectionHelper.IsHostProcess("cmd.exe"));
        Assert.True(SystemProtectionHelper.IsHostProcess("powershell.exe"));
        Assert.True(SystemProtectionHelper.IsHostProcess("pwsh.exe"));
    }
}

