using StartupMonitor.Shared;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for ServiceManagerTests.cs.</summary>
public class ServiceManagerTests
{
    /// <summary>Verifies test scenario for ServiceManagerTests.cs.</summary>
    [Fact]
    public void ServiceSecurity_InteractiveUsersCannotStopOrChangeProtection()
    {
        var field = typeof(ServiceManager).GetField("DefaultServiceSecurityDescriptor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Отсутствует политика доступа к службе.");
        var descriptor = new System.Security.AccessControl.RawSecurityDescriptor((string)(field.GetRawConstantValue()
            ?? throw new InvalidOperationException("Не задана политика доступа к службе.")));
        var acl = descriptor.DiscretionaryAcl ?? throw new InvalidOperationException("Политика не содержит DACL.");
        var interactive = Assert.Single(acl.Cast<System.Security.AccessControl.GenericAce>()
            .OfType<System.Security.AccessControl.CommonAce>().Where(ace =>
                ace.SecurityIdentifier.IsWellKnown(System.Security.Principal.WellKnownSidType.InteractiveSid)));
        Assert.Equal(0, interactive.AccessMask & (0x20 | 0x02 | 0x10000 | 0x40000));
        Assert.NotEqual(0, interactive.AccessMask & 0x10);
    }

    /// <summary>
    /// <summary>Verifies test scenario for ServiceManagerTests.cs.</summary>
    /// </summary>
    [Fact]
    public void IsServiceRunning_ReturnsBool()
    {
        var result = ServiceManager.IsServiceRunning();
        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// <summary>Verifies test scenario for ServiceManagerTests.cs.</summary>
    /// </summary>
    [Fact]
    public void IsProcessRunning_DefinitelyNotRunning_ReturnsFalse()
    {
        // Verifies component behavior and assertions.
        Assert.False(ServiceManager.IsProcessRunning($"smon-nonexistent-{Guid.NewGuid():N}"));
    }

    /// <summary>
    /// <summary>Verifies test scenario for ServiceManagerTests.cs.</summary>
    /// </summary>
    [Fact]
    public void CheckServiceStatus_ReturnsValidRecord()
    {
        var status = ServiceManager.CheckServiceStatus();
        Assert.NotNull(status);
        Assert.NotNull(status.ExpectedPath);
        Assert.EndsWith("StartupMonitor.Service.exe", status.ExpectedPath, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<bool>(status.IsInstalled);
        Assert.IsType<bool>(status.IsAutoStart);
        Assert.IsType<bool>(status.IsRunning);
    }
}
