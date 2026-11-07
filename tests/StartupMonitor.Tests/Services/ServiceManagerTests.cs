using StartupMonitor.Shared;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для ServiceManager: проверка работы с Windows-службами и процессами.
/// </summary>
public class ServiceManagerTests
{
    /// <summary>Права SCM не позволяют обычному пользователю обходить авторизацию IPC остановкой или перенастройкой службы.</summary>
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
    /// Проверяет, что IsServiceRunning возвращает bool (а не null или исключение).
    /// </summary>
    [Fact]
    public void IsServiceRunning_ReturnsBool()
    {
        var result = ServiceManager.IsServiceRunning();
        Assert.IsType<bool>(result);
    }

    /// <summary>
    /// Проверяет, что IsProcessRunning возвращает false для заведомо несуществующего процесса.
    /// </summary>
    [Fact]
    public void IsProcessRunning_DefinitelyNotRunning_ReturnsFalse()
    {
        // Имя, которого гарантированно нет среди процессов — без убийства реальных процессов
        Assert.False(ServiceManager.IsProcessRunning($"smon-nonexistent-{Guid.NewGuid():N}"));
    }

    /// <summary>
    /// Проверяет, что CheckServiceStatus возвращает корректную структуру без исключений.
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
