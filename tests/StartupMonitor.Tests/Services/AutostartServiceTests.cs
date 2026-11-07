using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для AutostartService: проверка включения/выключения автозапуска UI через реестр.
/// </summary>
public class AutostartServiceTests : IDisposable
{
    /// <summary>Уникальное имя значения в реестре для изоляции тестов.</summary>
    private readonly string _valueName = $"SMonTest_{Guid.NewGuid():N}";

    /// <summary>
    /// Проверяет, что SetEnabled(true) создаёт значение в автозапуске.
    /// </summary>
    [Fact]
    public void SetEnabled_True_CreatesRunValue()
    {
        AutostartService.SetEnabled(true, _valueName);

        Assert.True(AutostartService.IsEnabled(_valueName));
    }

    /// <summary>
    /// Проверяет, что SetEnabled(false) удаляет значение из автозапуска.
    /// </summary>
    [Fact]
    public void SetEnabled_False_RemovesRunValue()
    {
        AutostartService.SetEnabled(true, _valueName);
        AutostartService.SetEnabled(false, _valueName);

        Assert.False(AutostartService.IsEnabled(_valueName));
    }

    /// <summary>
    /// Проверяет, что EnsureSingleAutostartEntry(true) удаляет устаревшие дубликаты (StartupMonitorUI) и оставляет только каноническую запись.
    /// </summary>
    [Fact]
    public void EnsureSingleAutostartEntry_CleansDuplicates()
    {
        // Создаем фиктивный дубликат с устаревшим именем
        Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)?
            .SetValue("StartupMonitorUI", "\"C:\\Program Files\\StartupMonitor\\StartupMonitor.UI.exe\" --minimized");

        AutostartService.EnsureSingleAutostartEntry(true);

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        // Дубликат удалён
        Assert.Null(key?.GetValue("StartupMonitorUI"));
        // Каноническая запись присутствует
        Assert.NotNull(key?.GetValue(AutostartService.ValueName));
    }

    /// <summary>
    /// Проверяет, что EnsureSingleAutostartEntry обновляет путь в реестре, если путь к exe изменился.
    /// </summary>
    [Fact]
    public void EnsureSingleAutostartEntry_UpdatesOutdatedPath()
    {
        var oldCmd = "\"C:\\OldPath\\StartupMonitor.UI.exe\" --minimized";
        var newPath = @"C:\NewPath\StartupMonitor.UI.exe";
        var expectedCmd = $"\"{newPath}\" --minimized";

        using (var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
        {
            runKey?.SetValue(AutostartService.ValueName, oldCmd);
        }

        AutostartService.EnsureSingleAutostartEntry(true, newPath);

        using (var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            var actual = runKey?.GetValue(AutostartService.ValueName)?.ToString();
            Assert.Equal(expectedCmd, actual);
        }
    }

    /// <summary>
    /// Проверяет, что EnsureSingleAutostartEntry(false) полностью удаляет запись из реестра.
    /// </summary>
    [Fact]
    public void EnsureSingleAutostartEntry_False_RemovesEntry()
    {
        AutostartService.EnsureSingleAutostartEntry(true, @"C:\Test\StartupMonitor.UI.exe");
        AutostartService.EnsureSingleAutostartEntry(false);

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        Assert.Null(key?.GetValue(AutostartService.ValueName));
    }

    /// <summary>Чистит реестр от тестовых значений.</summary>
    public void Dispose()
    {
        // Чистим за собой на случай падения теста
        AutostartService.SetEnabled(false, _valueName);
        AutostartService.EnsureSingleAutostartEntry(false);
    }
}
