using StartupMonitor.Shared;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для ServiceManager: проверка работы с Windows-службами и процессами.
/// </summary>
public class ServiceManagerTests
{
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
