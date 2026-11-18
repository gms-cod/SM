using System;
using Xunit;
using StartupMonitor.Shared.Models;
using StartupMonitor.Service.Services.ProcessLifecycle;

namespace StartupMonitor.Tests.Security;

/// <summary>
/// Модульные тесты механизмов безопасности активных процессов (SystemProcessSafeguard, ProcessThreatConfig, Вердикты).
/// </summary>
public class ProcessThreatSecurityTests
{
    private readonly SystemProcessSafeguard _safeguard = new(null);

    /// <summary>Проверка, что системный svchost.exe из System32 защищен от закрытия.</summary>
    [Fact]
    public void System32_Svchost_IsProtected_ReturnsTrue()
    {
        var path = @"C:\Windows\System32\svchost.exe";
        var name = "svchost.exe";
        var isProtected = _safeguard.IsCriticalSystemProcess(1000, name, path);
        Assert.True(isProtected);
    }

    /// <summary>Проверка, что мимик csrss.exe из AppData признается угрозой и не защищается.</summary>
    [Fact]
    public void Mimic_Csrss_FromAppData_IsNotProtected_ReturnsFalse()
    {
        var path = @"C:\Users\User\AppData\Roaming\csrss.exe";
        var name = "csrss.exe";
        var isProtected = _safeguard.IsCriticalSystemProcess(1234, name, path);
        Assert.False(isProtected);
    }

    /// <summary>Проверка самозащиты службы StartupMonitor.</summary>
    [Fact]
    public void StartupMonitor_Service_IsProtected_ReturnsTrue()
    {
        var path = @"C:\Program Files\StartupMonitor\StartupMonitor.Service.exe";
        var name = "StartupMonitor.Service.exe";
        var isProtected = _safeguard.IsCriticalSystemProcess(2000, name, path);
        Assert.True(isProtected);
    }

    /// <summary>Проверка, что обычный пользовательский процесс (notepad.exe) не считается защищенным.</summary>
    [Fact]
    public void NormalUserProcess_Notepad_IsNotProtected_ReturnsFalse()
    {
        var path = @"C:\Windows\System32\notepad.exe";
        var name = "notepad.exe";
        var isProtected = _safeguard.IsCriticalSystemProcess(3000, name, path);
        Assert.False(isProtected);
    }

    /// <summary>Проверка корректности значений по умолчанию конфигурации ProcessThreatConfigDto.</summary>
    [Fact]
    public void ConfigDto_DefaultValues_AreCorrect()
    {
        var config = new ProcessThreatConfigDto();
        Assert.True(config.Enabled);
        Assert.True(config.KasperskyEnabled);
        Assert.True(config.AiEnabled);
        Assert.Equal(ProcessThreatAction.AlertOnly, config.Action);
        Assert.Equal(80, config.RiskThreshold);
        Assert.NotNull(config.Whitelist);
        Assert.Empty(config.Whitelist);
    }

    /// <summary>Проверка глубокого независимого клонирования конфигурации ProcessThreatConfigDto.</summary>
    [Fact]
    public void ConfigDto_Clone_IsIndependentDeepCopy()
    {
        var original = new ProcessThreatConfigDto
        {
            Enabled = false,
            Action = ProcessThreatAction.AutoKill,
            RiskThreshold = 90,
            Whitelist = new System.Collections.Generic.List<string> { "custom.exe" }
        };

        var clone = original.Clone();

        Assert.Equal(original.Enabled, clone.Enabled);
        Assert.Equal(original.Action, clone.Action);
        Assert.Equal(original.RiskThreshold, clone.RiskThreshold);
        Assert.Single(clone.Whitelist);
        Assert.Equal("custom.exe", clone.Whitelist[0]);

        // Проверка независимости списка
        clone.Whitelist.Add("another.exe");
        Assert.Single(original.Whitelist);
        Assert.Equal(2, clone.Whitelist.Count);
    }

    /// <summary>Проверка свойства HasKasperskyThreat при красной зоне Kaspersky OpenTIP.</summary>
    [Fact]
    public void ProcessInfo_KasperskyRed_HasKasperskyThreat_ReturnsTrue()
    {
        var proc = new ProcessInfo
        {
            KasperskyZone = "Red",
            KasperskyDetection = "Trojan.Win32.Generic"
        };

        Assert.True(proc.HasKasperskyThreat);
        Assert.Equal("Вредоносен", proc.KasperskyZoneText);
    }

    /// <summary>Проверка свойства HasAiThreat при высоком риске ИИ (>= 70%).</summary>
    [Fact]
    public void ProcessInfo_AiScoreHigh_HasAiThreat_ReturnsTrue()
    {
        var proc = new ProcessInfo
        {
            AiRiskScore = 85,
            AiVerdict = "Malicious"
        };

        Assert.True(proc.HasAiThreat);
        Assert.Equal("Malicious (85%)", proc.DisplayAiVerdict);
    }
}
