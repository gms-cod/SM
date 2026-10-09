using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using StartupMonitor.Shared.Audit;
using StartupMonitor.Shared.Audit.Exporters;
using StartupMonitor.Shared.Audit.Models;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Audit;

public class AuditReportExporterTests
{
    private static AuditReport CreateSampleReport()
    {
        var entry1 = new StartupEntry
        {
            Id = "abc123456789",
            Name = "NormalApp",
            Location = EntryLocation.RegistryRun,
            LocationDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            Publisher = "Microsoft Corporation",
            FilePath = @"C:\Program Files\Normal\app.exe",
            CommandLine = @"""C:\Program Files\Normal\app.exe"" --start",
            Status = EntryStatus.Enabled,
            AiRating = 2,
            AiRisk = "Низкий",
            AiCategory = "Системное",
            AiRecommendation = "Штатное приложение, безопасно.",
            KasperskyZone = "NotFound",
            ThreatInfo = new CliThreatInfo { HasThreat = false }
        };

        var entry2 = new StartupEntry
        {
            Id = "threat987654",
            Name = "SuspiciousScript",
            Location = EntryLocation.StartupFolder,
            LocationDetail = @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup",
            Publisher = "Неизвестный издатель",
            FilePath = @"C:\Users\User\AppData\Local\Temp\evil.cmd",
            CommandLine = @"powershell.exe -w hidden -enc JABhAD0AMQA=",
            Status = EntryStatus.Enabled,
            AiRating = 9,
            AiRisk = "Критический",
            AiCategory = "Вредоносное ПО",
            AiRecommendation = "Заблокировать и удалить. Запуск скрытого PowerShell скрипта.",
            KasperskyZone = "Found",
            KasperskyDetection = "Trojan.PowerShell.Downloader",
            ThreatInfo = CliHeuristicAnalyzer.Analyze(@"powershell.exe -w hidden -enc JABhAD0AMQA=", @"C:\Users\User\AppData\Local\Temp\evil.cmd")
        };

        var entry3 = new StartupEntry
        {
            Id = "blocked333221",
            Name = "BlockedMiner",
            Location = EntryLocation.RegistryRun,
            Publisher = "Miner Inc",
            FilePath = @"C:\Games\miner.exe",
            Status = EntryStatus.Blocked,
            AiRating = 10,
            KasperskyZone = "Found",
            KasperskyDetection = "RiskTool.BitCoinMiner"
        };

        var proc1 = new ProcessInfo
        {
            ProcessId = 1234,
            ParentProcessId = 100,
            ProcessName = "app.exe",
            Publisher = "Microsoft Corporation",
            FilePath = @"C:\Program Files\Normal\app.exe",
            CommandLine = @"""C:\Program Files\Normal\app.exe""",
            CpuUsagePercent = 1.5,
            WorkingSetBytes = 120 * 1024 * 1024,
            DiskReadBytesPerSec = 10240,
            NetworkBytesPerSec = 5120,
            StartupEntryId = "normal123456",
            StartupLocation = EntryLocation.RegistryRun
        };

        var proc2 = new ProcessInfo
        {
            ProcessId = 5678,
            ParentProcessId = 1234,
            ProcessName = "powershell.exe",
            CommandLine = @"powershell.exe -w hidden -enc JABhAD0AMQA=",
            FilePath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            CpuUsagePercent = 12.0,
            WorkingSetBytes = 85 * 1024 * 1024,
            IsSuspended = true,
            ThreatInfo = CliHeuristicAnalyzer.Analyze(@"powershell.exe -w hidden -enc JABhAD0AMQA=", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")
        };

        var snapshot = new SystemPerformanceSnapshot
        {
            TotalCpuPercent = 13.5,
            UsedMemoryBytes = 205L * 1024 * 1024,
            TotalMemoryBytes = 16384L * 1024 * 1024,
            TotalProcessesCount = 2,
            TotalThreadsCount = 40
        };

        return AuditReport.Create(
            new[] { entry1, entry2, entry3 },
            new[] { proc1, proc2 },
            snapshot,
            appVersion: "1.0.15",
            isServiceConnected: true,
            strictMode: false,
            silentMode: false);
    }

    [Fact]
    public void AuditSummary_Calculate_CorrectCounters()
    {
        var report = CreateSampleReport();
        var s = report.Summary;

        Assert.Equal(3, s.TotalStartupEntries);
        Assert.Equal(2, s.EnabledEntries);
        Assert.Equal(0, s.DisabledEntries);
        Assert.Equal(1, s.BlockedEntries);
        Assert.Equal(1, s.CliThreatsCount);
        Assert.Equal(2, s.CirclFoundCount);
        Assert.Equal(3, s.RatedEntriesCount);
        Assert.True(s.AverageAiRating > 0);

        Assert.Equal(2, s.TotalProcessesCount);
        Assert.Equal(1, s.SuspendedProcessesCount);
        Assert.Equal(1, s.ProcessThreatsCount);
        Assert.Equal(13.5, s.TotalCpuPercent);
    }

    [Fact]
    public void HtmlReport_GeneratesOfflineHtml_WithExpectedStructure()
    {
        var report = CreateSampleReport();
        var html = AuditReportGenerator.GenerateHtml(report);

        Assert.NotNull(html);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<title>Startup Monitor — Отчет аудита безопасности", html);
        Assert.Contains("NormalApp", html);
        Assert.Contains("SuspiciousScript", html);
        Assert.Contains("BlockedMiner", html);
        Assert.Contains("powershell.exe", html);
        Assert.Contains("tab-startup", html);
        Assert.Contains("tab-processes", html);
        Assert.Contains("liveSearch", html);
        Assert.Contains("filterTables", html);
    }

    [Fact]
    public void HtmlReport_EscapesSpecialCharacters_ToPreventXss()
    {
        var xssEntry = new StartupEntry
        {
            Name = "<script>alert('xss')</script>",
            Publisher = "Evil & Co. \"Ltd\"",
            CommandLine = "cmd.exe /c \"test & calc.exe\" <input>"
        };

        var report = AuditReport.Create(new[] { xssEntry });
        var html = AuditReportGenerator.GenerateHtml(report);

        Assert.DoesNotContain("<script>alert('xss')</script>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", html);
        Assert.Contains("Evil &amp; Co. &quot;Ltd&quot;", html);
    }

    [Fact]
    public void HtmlReport_RendersThreatBadgesAndAiStars()
    {
        var report = CreateSampleReport();
        var html = AuditReportGenerator.GenerateHtml(report);

        Assert.Contains("badge-threat high", html);
        Assert.Contains("Trojan.PowerShell.Downloader", html);
        Assert.Contains("badge-circl red", html);
        Assert.Contains("star-green", html);
        Assert.Contains("star-red", html);
    }

    [Fact]
    public void CsvReport_IncludesUtf8Bom_ForExcel()
    {
        var report = CreateSampleReport();
        var bytes = AuditReportGenerator.GenerateCsvBytes(report);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void CsvReport_EscapesQuotesAndCommas_AccordingToRfc4180()
    {
        var entry = new StartupEntry
        {
            Name = "App, with comma",
            Publisher = "Company \"Quote\"",
            CommandLine = @"C:\Path with, comma\app.exe"
        };

        var report = AuditReport.Create(new[] { entry });
        var csv = AuditReportGenerator.GenerateCsv(report);

        Assert.Contains("\"App, with comma\"", csv);
        Assert.Contains("\"Company \"\"Quote\"\"\"", csv);
        Assert.Contains("\"C:\\Path with, comma\\app.exe\"", csv);
    }

    [Fact]
    public void CsvReport_ContainsHeadersAndDataRows()
    {
        var report = CreateSampleReport();
        var csv = AuditReportGenerator.GenerateCsv(report);

        Assert.Contains("--- STARTUP ENTRIES ---", csv);
        Assert.Contains("Name,Publisher,Status,Location", csv);
        Assert.Contains("NormalApp", csv);
        Assert.Contains("SuspiciousScript", csv);
        Assert.Contains("--- ACTIVE PROCESSES ---", csv);
        Assert.Contains("PID,ParentPID,ProcessName", csv);
        Assert.Contains("powershell.exe", csv);
    }

    [Fact]
    public void JsonReport_GeneratesValidJson_AndRoundtrips()
    {
        var report = CreateSampleReport();
        var json = AuditReportGenerator.GenerateJson(report);

        Assert.NotNull(json);
        Assert.Contains("\"machineName\":", json);
        Assert.Contains("\"summary\":", json);
        Assert.Contains("\"startupEntries\":", json);
        Assert.Contains("\"activeProcesses\":", json);

        // Проверяем валидность JSON через JsonDocument
        using var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
        Assert.Equal(3, doc.RootElement.GetProperty("startupEntries").GetArrayLength());
        Assert.Equal(2, doc.RootElement.GetProperty("activeProcesses").GetArrayLength());
    }

    [Theory]
    [InlineData(".html", typeof(HtmlAuditReportExporter))]
    [InlineData("html", typeof(HtmlAuditReportExporter))]
    [InlineData(".csv", typeof(CsvAuditReportExporter))]
    [InlineData("csv", typeof(CsvAuditReportExporter))]
    [InlineData(".json", typeof(JsonAuditReportExporter))]
    [InlineData("json", typeof(JsonAuditReportExporter))]
    public void AuditReportGenerator_GetExporter_SupportsExtensions(string ext, Type expectedType)
    {
        var exporter = AuditReportGenerator.GetExporter(ext);
        Assert.IsType(expectedType, exporter);
    }

    [Fact]
    public void AuditReportGenerator_GetExporter_ThrowsOnUnsupported()
    {
        Assert.Throws<NotSupportedException>(() => AuditReportGenerator.GetExporter(".pdf"));
    }
}

