using StartupMonitor.Service.Services.AntiRansomware;
using StartupMonitor.Shared.Models.Ransomware;

namespace StartupMonitor.Tests.Security;

/// <summary>
/// Модульные тесты статического детектора деструктивных команд шифровальщиков (RansomwareCliDetector).
/// Проверяют выявление попыток удаления теневых копий томов, подавления восстановления Windows и журналов аудита.
/// </summary>
public class RansomwareCliDetectorTests
{
    /// <summary>
    /// Проверяет, что деструктивные команды вымогателей надежно определяются как угроза.
    /// </summary>
    /// <param name="command">Проверяемая строка команды.</param>
    /// <param name="expectedPattern">Ожидаемая сигнатура вредоносного действия.</param>
    [Theory]
    [InlineData("vssadmin.exe delete shadows /all /quiet", "delete shadows")]
    [InlineData("vssadmin DELETE SHADOWS /all", "delete shadows")]
    [InlineData("wmic shadowcopy delete", "shadowcopy delete")]
    [InlineData("vssadmin delete shadowstorage /for=c: /on=c:", "delete shadowstorage")]
    [InlineData("wbadmin delete catalog -quiet", "wbadmin delete catalog")]
    [InlineData("wbadmin delete systemstatebackup -keepVersions:0", "wbadmin delete systemstatebackup")]
    [InlineData("bcdedit.exe /set {default} recoveryenabled no", "recoveryenabled no")]
    [InlineData("bcdedit /set {current} bootstatuspolicy ignoreallfailures", "ignoreallfailures")]
    [InlineData("wevtutil cl security", "wevtutil cl security")]
    [InlineData("wevtutil.exe cl system", "wevtutil cl system")]
    public void IsRansomwareTamperCommand_MaliciousCommands_DetectedAsThreat(string command, string expectedPattern)
    {
        // Act
        bool isThreat = RansomwareCliDetector.IsRansomwareTamperCommand(
            command,
            out string matchedPattern,
            out string threatDescription);

        // Assert
        Assert.True(isThreat, $"Команда '{command}' должна быть определена как деструктивная атака");
        Assert.Equal(expectedPattern, matchedPattern);
        Assert.False(string.IsNullOrWhiteSpace(threatDescription));
    }

    /// <summary>
    /// Проверяет, что легитимные команды администрирования не вызывают ложных срабатываний.
    /// </summary>
    /// <param name="benignCommand">Безопасная команда операционной системы.</param>
    [Theory]
    [InlineData("vssadmin list shadows")]
    [InlineData("vssadmin list shadowstorage")]
    [InlineData("wmic process get name,processid")]
    [InlineData("bcdedit /enum {current}")]
    [InlineData("dir C:\\Users")]
    [InlineData("ping 8.8.8.8 -t")]
    [InlineData("powershell.exe -ExecutionPolicy Bypass -File myscript.ps1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsRansomwareTamperCommand_BenignCommands_NotDetectedAsThreat(string? benignCommand)
    {
        // Act
        bool isThreat = RansomwareCliDetector.IsRansomwareTamperCommand(
            benignCommand,
            out string matchedPattern,
            out string threatDescription);

        // Assert
        Assert.False(isThreat, $"Безопасная команда '{benignCommand}' не должна детектироваться как угроза");
        Assert.Empty(matchedPattern);
        Assert.Empty(threatDescription);
    }

    /// <summary>
    /// Проверяет оценку процесса с учетом фильтрации исполняемых файлов (например, PowerShell обращение к Win32_ShadowCopy).
    /// </summary>
    [Fact]
    public void Evaluate_ProcessFilter_DifferentiatesContext()
    {
        // Arrange
        string cmd = "Get-WmiObject Win32_ShadowCopy | ForEach-Object { $_.Delete() }";

        // Act & Assert
        // Для powershell.exe команда должна быть заблокирована
        var verdictPwsh = RansomwareCliDetector.Evaluate("powershell.exe", cmd);
        Assert.True(verdictPwsh.IsThreat);
        Assert.Equal(RansomwareTriggerType.VssTamper, verdictPwsh.TriggerType);

        // Для стороннего процесса (например, doc_editor.exe) фильтр не должен сработать
        var verdictDoc = RansomwareCliDetector.Evaluate("notepad.exe", cmd);
        Assert.False(verdictDoc.IsThreat);
    }
}
