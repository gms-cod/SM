using Microsoft.Win32;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для вспомогательных методов WatcherHelper: парсинг путей, определение местоположения записей.
/// </summary>
public class WatcherHelperTests
{
    // ───── ParsePath ─────

    /// <summary>
    /// Проверяет, что ParsePath возвращает пустую строку для null, пустой строки и пробелов.
    /// </summary>
    [Fact]
    public void ParsePath_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", WatcherHelper.ParsePath(null!));
        Assert.Equal("", WatcherHelper.ParsePath(""));
        Assert.Equal("", WatcherHelper.ParsePath("   "));
    }

    /// <summary>
    /// Проверяет извлечение пути из кавычек: путь в кавычках с аргументами и без.
    /// </summary>
    [Theory]
    [InlineData(@"""C:\Program Files\MyApp\app.exe"" --arg", @"C:\Program Files\MyApp\app.exe")]
    [InlineData(@"""C:\path\app.exe""", @"C:\path\app.exe")]
    [InlineData(@"""""", @"""""")] // empty quotes
    [InlineData(@"""noCloseQuote", @"""noCloseQuote")] // malformed
    public void ParsePath_QuotedPath_ExtractsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, WatcherHelper.ParsePath(input));
    }

    /// <summary>
    /// Проверяет извлечение пути без кавычек: путь, путь с аргументами, путь без расширения.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Simple\app.exe", @"C:\Simple\app.exe")]
    [InlineData(@"C:\Simple\app.exe --arg", @"C:\Simple\app.exe")]
    [InlineData(@"app.exe --arg", @"app.exe")]
    [InlineData(@"C:\path\without\ext", @"C:\path\without\ext")] // no args
    public void ParsePath_UnquotedPath_ExtractsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, WatcherHelper.ParsePath(input));
    }

    /// <summary>
    /// Проверяет извлечение для .com и .bat файлов — они также должны корректно парситься.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Tools\helper.com /quiet", @"C:\Tools\helper.com")]
    [InlineData(@"C:\Scripts\deploy.bat nightly", @"C:\Scripts\deploy.bat")]
    public void ParsePath_ComAndBat_ExtractsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, WatcherHelper.ParsePath(input));
    }

    /// <summary>
    /// Проверяет извлечение DLL-пути из rundll32.exe — особая команда с запятой.
    /// </summary>
    [Fact]
    public void ParsePath_DllRegSvr32_Roundtrip()
    {
        var input = @"rundll32.exe C:\Windows\System32\my.dll,EntryPoint";
        var result = WatcherHelper.ParsePath(input);
        Assert.Equal(@"C:\Windows\System32\my.dll", result);
    }

    /// <summary>
    /// Проверяет, что путь без пробелов возвращается целиком.
    /// </summary>
    [Fact]
    public void ParsePath_NoSpace_ReturnsFullString()
    {
        Assert.Equal(@"C:\NoArgs.exe", WatcherHelper.ParsePath(@"C:\NoArgs.exe"));
    }

    // ───── ParseServicePath ─────

    /// <summary>
    /// Проверяет, что ParseServicePath возвращает пустую строку для null, пустого и пробелов.
    /// </summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ParseServicePath_NullOrEmpty_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, WatcherHelper.ParseServicePath(input));
    }

    /// <summary>
    /// Проверяет извлечение пути службы из кавычек.
    /// </summary>
    [Theory]
    [InlineData(@"""C:\Program Files\Service\svc.exe"" --arg", @"C:\Program Files\Service\svc.exe")]
    [InlineData(@"""C:\path\svc.exe""", @"C:\path\svc.exe")]
    [InlineData(@"""""", @"""""")]
    public void ParseServicePath_Quoted_ExtractsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, WatcherHelper.ParseServicePath(input));
    }

    /// <summary>
    /// Проверяет извлечение пути службы без кавычек.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Service\svc.exe", @"C:\Service\svc.exe")]
    [InlineData(@"C:\Service\svc.exe --arg1 arg2", @"C:\Service\svc.exe")]
    [InlineData(@"C:\NoArgs\binary.exe", @"C:\NoArgs\binary.exe")]
    public void ParseServicePath_Unquoted_ExtractsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, WatcherHelper.ParseServicePath(input));
    }

    /// <summary>
    /// Проверяет, что для пути с пробелами без кавычек берётся только первый токен (особенность sc).
    /// </summary>
    [Fact]
    public void ParseServicePath_SpaceInUnquotedPath_FirstTokenOnly()
    {
        var result = WatcherHelper.ParseServicePath(@"C:\Program Files\MySvc\svc.exe");
        Assert.Equal(@"C:\Program", result);
    }

    // ───── GetLocationFromKeyPath ─────

    /// <summary>
    /// Проверяет, что запись из списка удалённых (списка восстановления) не возвращается
    /// в общий список при повторном обнаружении в системе.
    /// </summary>
    [Fact]
    public void AddOrUpdate_RemovedEntry_DoesNotReaddToGeneralList()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"smon-{Guid.NewGuid():N}");
        var store = new JsonDataStore(tmpDir);

        var entry = new StartupEntry
        {
            Name = "App",
            FilePath = @"C:\app.exe",
            CommandLine = @"C:\app.exe",
            Location = EntryLocation.StartupFolder,
            LocationDetail = @"C:\Users\Test\Startup",
        };
        entry.Id = WatcherHelper.ComputeStableId(entry);

        // Запись добавлена, затем удалена пользователем из системы (→ список восстановления)
        store.AddEntry(entry);
        store.AddRemovedEntry(new RemovedEntry { OriginalEntryId = entry.Id });
        store.RemoveEntry(entry.Id);

        // Запись снова обнаружена watcher'ом — не должна вернуться в общий список
        var notif = new NotificationService(new InitialScanTracker());
        var added = WatcherHelper.AddOrUpdate(store, notif, entry);

        Assert.False(added);
        Assert.Null(store.FindEntry(e => e.Id == entry.Id));
    }

    /// <summary>
    /// Проверяет, что при включенном StrictMode новая запись не добавляется в активный список,
    /// а стирается и отправляется уведомление lockdownBlocked.
    /// </summary>
    [Fact]
    public void AddOrUpdate_StrictModeEnabled_BlocksAndRemovesNewEntry()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"smon-strict-{Guid.NewGuid():N}");
        var store = new JsonDataStore(tmpDir);
        var cfg = store.Load();
        cfg.Settings.StrictMode = true;
        store.Save(cfg);

        var notif = new NotificationService(new InitialScanTracker());

        var entry = new StartupEntry
        {
            Name = "NewUntrustedApp",
            FilePath = @"C:\Test\untrusted.exe",
            CommandLine = @"C:\Test\untrusted.exe",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
        };
        entry.Id = WatcherHelper.ComputeStableId(entry);

        var added = WatcherHelper.AddOrUpdate(store, notif, entry);

        Assert.False(added);
        Assert.Null(store.FindEntry(e => e.Id == entry.Id));
        // Должна появиться в RemovedEntries
        Assert.Contains(store.GetRemovedEntries(), r => r.OriginalEntryId == entry.Id);
    }

    /// <summary>
    /// Проверяет, что при выключенном StrictMode новая запись успешно регистрируется.
    /// </summary>
    [Fact]
    public void AddOrUpdate_StrictModeDisabled_AddsNormally()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"smon-normal-{Guid.NewGuid():N}");
        var store = new JsonDataStore(tmpDir);
        var notif = new NotificationService(new InitialScanTracker());

        var entry = new StartupEntry
        {
            Name = "TrustedApp",
            FilePath = @"C:\Test\trusted.exe",
            CommandLine = @"C:\Test\trusted.exe",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
        };
        entry.Id = WatcherHelper.ComputeStableId(entry);

        var added = WatcherHelper.AddOrUpdate(store, notif, entry);

        Assert.True(added);
        Assert.NotNull(store.FindEntry(e => e.Id == entry.Id));
    }

    // ───── LooksLikeCommand ─────

    /// <summary>
    /// Проверяет, что команды автозапуска распознаются: пути, кавычки, %env%, bare-exe.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows\System32\app.exe")]
    [InlineData(@"C:\Windows\System32\app.exe -flag")]
    [InlineData(@"""C:\Program Files\MyApp\app.exe"" --arg")]
    [InlineData(@"%windir%\system32\appidcertstorecheck.exe")]
    [InlineData(@"C:\Windows\System32\rundll32.exe C:\Windows\System32\x.dll,Entry")]
    [InlineData(@"explorer.exe")]
    [InlineData(@"\\server\share\app.exe")]
    [InlineData(@"C:\Windows\System32\userinit.exe,")]
    [InlineData(@"RUNDLL32.EXE C:\WINDOWS\system32\winsta.dll,...")]
    [InlineData(@"C:\Windows\System32\ie4uinit.exe -UserConfig")]
    public void LooksLikeCommand_RealCommands_ReturnsTrue(string value)
    {
        Assert.True(WatcherHelper.LooksLikeCommand(value));
    }

    /// <summary>
    /// Проверяет, что мусорные значения реестра не распознаются как команды:
    /// числа, "yes", CLSID, имена DLL/драйверов без пути, списки, деактивированные StubPath.
    /// </summary>
    [Theory]
    [InlineData("15")]
    [InlineData("yes")]
    [InlineData("U")]
    [InlineData("0")]
    [InlineData("{22d6f312-b0f6-11d0-94ab-0080c74c7e95}")]
    [InlineData(@"/UserInstall")]
    [InlineData("wdmaud.drv")]
    [InlineData("iyuv_32.dll")]
    [InlineData("ntmarta.dll")]
    [InlineData("IISSUBA")]
    [InlineData("IEToEdge")]
    [InlineData("P9NP,RDPNP,LanmanWorkstation,webclient")]
    [InlineData(">C:\\Windows\\System32\\old.exe")]
    [InlineData("HP LaserJet M1120 MFP,winspool,Ne01:")]
    [InlineData("C:\\Windows\\System32\\l3codeca.acm")]
    [InlineData("")]
    [InlineData("   ")]
    public void LooksLikeCommand_JunkValues_ReturnsFalse(string value)
    {
        Assert.False(WatcherHelper.LooksLikeCommand(value));
    }

    // ───── GetActiveSetupStubPath ─────

    /// <summary>
    /// Проверяет, что активный компонент Active Setup возвращает StubPath.
    /// </summary>
    [Fact]
    public void GetActiveSetupStubPath_ActiveComponent_ReturnsStub()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\SMonTests\ActiveSetup\Active");
        key.SetValue("StubPath", @"C:\Windows\System32\test.exe -flag");
        key.SetValue("ComponentID", "TestComp");
        try
        {
            Assert.Equal(@"C:\Windows\System32\test.exe -flag",
                WatcherHelper.GetActiveSetupStubPath(key));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\SMonTests", false);
        }
    }

    /// <summary>
    /// Проверяет, что компонент без StubPath не является автозапуском.
    /// </summary>
    [Fact]
    public void GetActiveSetupStubPath_NoStubPath_ReturnsNull()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\SMonTests\ActiveSetup\NoStub");
        key.SetValue("ComponentID", "TestComp");
        try
        {
            Assert.Null(WatcherHelper.GetActiveSetupStubPath(key));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\SMonTests", false);
        }
    }

    /// <summary>
    /// Проверяет, что деактивированный компонент (StubPath с '>') не является автозапуском.
    /// </summary>
    [Fact]
    public void GetActiveSetupStubPath_DeactivatedStub_ReturnsNull()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\SMonTests\ActiveSetup\Old");
        key.SetValue("StubPath", ">C:\\Windows\\System32\\old.exe");
        try
        {
            Assert.Null(WatcherHelper.GetActiveSetupStubPath(key));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\SMonTests", false);
        }
    }

    // ───── GetLocationFromKeyPath ─────

    /// <summary>
    /// Проверяет определение типа автозагрузки по пути реестрового ключа.
    /// </summary>
    [Theory]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Run", EntryLocation.RegistryRun)]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", EntryLocation.RegistryRun)]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", EntryLocation.RegistryRunOnce)]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\RunServices", EntryLocation.RegistryRunServices)]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", EntryLocation.RegistryPolicies)]
    [InlineData(@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", EntryLocation.RegistryRun)]
    [InlineData(@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", EntryLocation.RegistryRunOnce)]
    public void GetLocationFromKeyPath_ReturnsCorrectLocation(string keyPath, EntryLocation expected)
    {
        Assert.Equal(expected, WatcherHelper.GetLocationFromKeyPath(keyPath));
    }
}
