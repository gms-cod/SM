using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
public class AutostartServiceTests
{
    private readonly MemoryBackend _backend = new();
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    private readonly string _valueName = $"SMonTest_{Guid.NewGuid():N}";

    /// <summary>
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void SetEnabled_True_CreatesRunValue()
    {
        AutostartService.SetEnabled(true, _valueName, _backend);

        Assert.True(AutostartService.IsEnabled(_valueName, _backend));
    }

    /// <summary>
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void SetEnabled_False_RemovesRunValue()
    {
        AutostartService.SetEnabled(true, _valueName, _backend);
        AutostartService.SetEnabled(false, _valueName, _backend);

        Assert.False(AutostartService.IsEnabled(_valueName, _backend));
    }

    /// <summary>
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void EnsureSingleAutostartEntry_CleansDuplicates()
    {
        // Verifies component behavior and assertions.
        _backend.SetRunValue("StartupMonitorUI", "\"C:\\Program Files\\StartupMonitor\\StartupMonitor.UI.exe\" --minimized");

        Assert.True(AutostartService.EnsureSingleAutostartEntry(true, null, _backend));

        // Verifies component behavior and assertions.
        Assert.False(_backend.Run.ContainsKey("StartupMonitorUI"));
        // Verifies component behavior and assertions.
        Assert.True(_backend.Run.ContainsKey(AutostartService.ValueName));
    }

    /// <summary>
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void EnsureSingleAutostartEntry_UpdatesOutdatedPath()
    {
        var oldCmd = "\"C:\\OldPath\\StartupMonitor.UI.exe\" --minimized";
        var newPath = @"C:\NewPath\StartupMonitor.UI.exe";
        var expectedCmd = $"\"{newPath}\" --minimized";

        _backend.SetRunValue(AutostartService.ValueName, oldCmd);

        Assert.True(AutostartService.EnsureSingleAutostartEntry(true, newPath, _backend));

        Assert.Equal(expectedCmd, _backend.Run[AutostartService.ValueName]);
    }

    /// <summary>
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    /// </summary>
    [Fact]
    public void EnsureSingleAutostartEntry_False_RemovesEntry()
    {
        Assert.True(AutostartService.EnsureSingleAutostartEntry(true, @"C:\Test\StartupMonitor.UI.exe", _backend));
        Assert.True(AutostartService.EnsureSingleAutostartEntry(false, null, _backend));

        Assert.False(_backend.Run.ContainsKey(AutostartService.ValueName));
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Fact]
    public void ElevatedStartup_MigratesToOwnedInteractiveTask()
    {
        _backend.IsElevated = true;
        _backend.SetRunValue(AutostartService.ValueName, "\"C:\\Old\\StartupMonitor.UI.exe\" --minimized");
        Assert.True(AutostartService.EnsureSingleAutostartEntry(true, null, _backend));
        Assert.Empty(_backend.Run);
        var xml = Assert.Single(_backend.Tasks).Value;
        var document = System.Xml.Linq.XDocument.Parse(xml);
        Assert.Contains(document.Descendants(), e => e.Name.LocalName == "LogonType" && e.Value == "InteractiveToken");
        Assert.Contains(document.Descendants(), e => e.Name.LocalName == "RunLevel" && e.Value == "HighestAvailable");
        Assert.Contains(document.Descendants(), e => e.Name.LocalName == "Command" && e.Value == _backend.ExecutablePath);
        Assert.Contains(document.Descendants(), e => e.Name.LocalName == "UserId" && e.Value == _backend.UserSid);
        Assert.Contains(document.Descendants(), e => e.Name.LocalName == "Source" && e.Value == "urn:startupmonitor:ui-autostart:" + _backend.UserSid);
        Assert.True(AutostartService.IsEnabled(null, _backend));
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Fact]
    public void FailedTaskRegistration_PreservesBootstrapRun()
    {
        _backend.IsElevated = true;
        _backend.RefuseTaskRegistration = true;
        var original = "\"C:\\Old\\StartupMonitor.UI.exe\" --minimized";
        _backend.SetRunValue(AutostartService.ValueName, original);
        Assert.False(AutostartService.EnsureSingleAutostartEntry(true, null, _backend));
        Assert.Equal(original, _backend.Run[AutostartService.ValueName]);
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-19")]
    [InlineData("S-1-5-20")]
    public void ServiceAccount_DoesNotCreateUiAutostart(string sid)
    {
        _backend.UserSid = sid;
        _backend.IsElevated = true;
        _backend.SetRunValue("Other", "other.exe");
        Assert.False(AutostartService.EnsureSingleAutostartEntry(true, null, _backend));
        Assert.Empty(_backend.Tasks);
        Assert.Equal("Other", Assert.Single(_backend.Run).Key);
        Assert.Equal(0, _backend.LegacyCleanCalls);
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Fact]
    public void ElevatedStartup_RemovesOnlyVerifiedSystemTask()
    {
        _backend.IsElevated = true;
        const string systemName = "StartupMonitor.UI.S-1-5-18";
        _backend.Tasks[systemName] = WindowsAutostartBackend.BuildTaskXml(_backend.ExecutablePath, "S-1-5-18");
        Assert.True(AutostartService.EnsureSingleAutostartEntry(true, null, _backend));
        Assert.False(_backend.Tasks.ContainsKey(systemName));
        Assert.True(_backend.Tasks.ContainsKey(AutostartService.GetTaskName(_backend.UserSid)));
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Theory]
    [InlineData("marker")]
    [InlineData("principal")]
    [InlineData("path")]
    [InlineData("argument")]
    [InlineData("extra-action")]
    [InlineData("malformed")]
    public void ElevatedStartup_PreservesUnverifiedSystemTask(string alteration)
    {
        _backend.IsElevated = true;
        const string systemName = "StartupMonitor.UI.S-1-5-18";
        var doc = System.Xml.Linq.XDocument.Parse(WindowsAutostartBackend.BuildTaskXml(_backend.ExecutablePath, "S-1-5-18"));
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        switch (alteration)
        {
            case "marker": doc.Descendants(ns + "Source").Single().Value = "foreign"; break;
            case "principal": doc.Descendants(ns + "Principal").Single().Element(ns + "UserId")!.Value = "S-1-5-19"; break;
            case "path": doc.Descendants(ns + "Command").Single().Value = @"C:\Other\StartupMonitor.UI.exe"; break;
            case "argument": doc.Descendants(ns + "Arguments").Single().Value = "--minimized --other"; break;
            case "extra-action": doc.Descendants(ns + "Actions").Single().Add(new System.Xml.Linq.XElement(ns + "Exec")); break;
        }
        var xml = alteration == "malformed" ? "<task" : doc.ToString();
        _backend.Tasks[systemName] = xml;
        Assert.True(AutostartService.EnsureSingleAutostartEntry(true, null, _backend));
        Assert.Equal(xml, _backend.Tasks[systemName]);
        Assert.True(_backend.Tasks.ContainsKey(AutostartService.GetTaskName(_backend.UserSid)));
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Fact]
    public void Disable_PreservesForeignStartupEntries()
    {
        _backend.IsElevated = true;
        AutostartService.EnsureSingleAutostartEntry(true, null, _backend);
        _backend.SetRunValue("Other", "\"C:\\Tools\\other.exe\"");
        _backend.Tasks["OtherTask"] = "foreign";
        Assert.True(AutostartService.EnsureSingleAutostartEntry(false, null, _backend));
        Assert.Equal("Other", Assert.Single(_backend.Run).Key);
        Assert.Equal("OtherTask", Assert.Single(_backend.Tasks).Key);
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Fact]
    public void SimilarEntryName_DoesNotDeleteAnotherExecutable()
    {
        _backend.SetRunValue(AutostartService.ValueName, "\"C:\\Tools\\other.exe\"");
        Assert.False(AutostartService.EnsureSingleAutostartEntry(true, null, _backend));
        Assert.Contains("other.exe", _backend.Run[AutostartService.ValueName]);
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Fact]
    public void TaskXml_EscapesPathAndContainsNoPasswordLogon()
    {
        var path = @"C:\Apps & Tools\StartupMonitor.UI.exe";
        var document = System.Xml.Linq.XDocument.Parse(WindowsAutostartBackend.BuildTaskXml(path, _backend.UserSid));
        Assert.Equal(path, document.Descendants().Single(e => e.Name.LocalName == "Command").Value);
        Assert.DoesNotContain(document.Descendants(), e => e.Name.LocalName == "Password");
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    [Fact]
    public void MissingTask_MappedFileNotFoundException_IsRecognized()
    {
        var method = typeof(WindowsAutostartBackend).GetMethod("FindTask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Null(method.Invoke(null, [new MissingTaskFolder(), "StartupMonitor.UI.test"]));
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    public sealed class MissingTaskFolder
    {
        /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
        public object GetTask(string name) => throw new System.IO.FileNotFoundException(name);
    }

    /// <summary>Verifies test scenario for AutostartServiceTests.cs.</summary>
    private sealed class MemoryBackend : IUserAutostartBackend
    {
        public Dictionary<string, string> Run { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public string ExecutablePath => @"C:\Program Files\StartupMonitor\StartupMonitor.UI.exe";
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public string UserSid { get; set; } = "S-1-5-21-1-2-3-1001";
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public bool IsElevated { get; set; }
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public bool RefuseTaskRegistration { get; set; }
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public int LegacyCleanCalls { get; private set; }
        public Dictionary<string, string> GetRunEntries() => new(Run, StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public void SetRunValue(string name, string value) => Run[name] = value;
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public void DeleteRunValue(string name) => Run.Remove(name);
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public void RegisterScheduledTask(string name, string xml)
        {
            if (RefuseTaskRegistration) throw new InvalidOperationException("Controlled scheduler failure");
            Tasks[name] = xml;
        }
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public bool HasScheduledTask(string name) => Tasks.ContainsKey(name);
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public void DeleteScheduledTask(string name) => Tasks.Remove(name);
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public void CleanLegacySystemTask(string executable)
        {
            const string name = "StartupMonitor.UI.S-1-5-18";
            if (Tasks.TryGetValue(name, out var xml) && WindowsAutostartBackend.IsLegacySystemTask(xml, executable))
                Tasks.Remove(name);
        }
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public void CleanLegacyMachineRunEntries() => LegacyCleanCalls++;
    }
}
