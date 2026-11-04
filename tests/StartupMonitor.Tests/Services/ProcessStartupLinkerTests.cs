using System.Collections.Generic;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class ProcessStartupLinkerTests
{
    [Fact]
    public void Match_ByExecutablePath_ReturnsLinkedEntry()
    {
        var linker = new ProcessStartupLinker();
        var entries = new List<StartupEntry>
        {
            new()
            {
                Id = "entry-1",
                Name = "Test App",
                FilePath = "C:\\Program Files\\Test\\test.exe",
                Location = EntryLocation.RegistryRun
            }
        };

        linker.UpdateIndex(entries);

        var (isStartup, entryId, location) = linker.MatchProcess("C:\\Program Files\\Test\\test.exe", "test", false);

        Assert.True(isStartup);
        Assert.Equal("entry-1", entryId);
        Assert.Equal(EntryLocation.RegistryRun, location);
    }

    [Fact]
    public void Match_CaseInsensitiveAndPathVariants_Matches()
    {
        var linker = new ProcessStartupLinker();
        var entries = new List<StartupEntry>
        {
            new()
            {
                Id = "entry-discord",
                Name = "Discord",
                FilePath = "\"C:\\Users\\User\\AppData\\Local\\Discord\\app.exe\" --autostart",
                Location = EntryLocation.RegistryRun
            }
        };

        linker.UpdateIndex(entries);

        var (isStartup, entryId, _) = linker.MatchProcess("c:\\users\\user\\appdata\\local\\discord\\app.exe", "Discord", false);

        Assert.True(isStartup);
        Assert.Equal("entry-discord", entryId);
    }

    [Fact]
    public void Match_ByServiceName_ReturnsLinkedEntry()
    {
        var linker = new ProcessStartupLinker();
        var entries = new List<StartupEntry>
        {
            new()
            {
                Id = "entry-svc",
                Name = "Spooler",
                FilePath = "C:\\Windows\\System32\\spoolsv.exe",
                Location = EntryLocation.Service
            }
        };

        linker.UpdateIndex(entries);

        var (isStartup, entryId, location) = linker.MatchProcess("C:\\Windows\\System32\\spoolsv.exe", "Spooler", isService: true);

        Assert.True(isStartup);
        Assert.Equal("entry-svc", entryId);
        Assert.Equal(EntryLocation.Service, location);
    }

    [Fact]
    public void Match_NonStartupProcess_ReturnsFalse()
    {
        var linker = new ProcessStartupLinker();
        var entries = new List<StartupEntry>
        {
            new()
            {
                Id = "entry-1",
                Name = "App",
                FilePath = "C:\\app.exe",
                Location = EntryLocation.RegistryRun
            }
        };

        linker.UpdateIndex(entries);

        var (isStartup, entryId, _) = linker.MatchProcess("C:\\other\\notepad.exe", "notepad", false);

        Assert.False(isStartup);
        Assert.Null(entryId);
    }
}

