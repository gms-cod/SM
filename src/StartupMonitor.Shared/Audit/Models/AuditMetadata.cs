using System;
using System.Runtime.InteropServices;

namespace StartupMonitor.Shared.Audit.Models;

/// <summary>
/// Метаданные отчета аудита системы (хост, пользователь, версия ОС и сборки программы).
/// </summary>
public class AuditMetadata
{
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsServiceConnected { get; set; }
    public bool StrictMode { get; set; }
    public bool SilentMode { get; set; }

    public static AuditMetadata CreateCurrent(
        string appVersion = "1.0.15",
        bool isServiceConnected = true,
        bool strictMode = false,
        bool silentMode = false)
    {
        return new AuditMetadata
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            OsDescription = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            AppVersion = appVersion,
            GeneratedAt = DateTime.Now,
            GeneratedAtUtc = DateTime.UtcNow,
            IsServiceConnected = isServiceConnected,
            StrictMode = strictMode,
            SilentMode = silentMode
        };
    }
}

