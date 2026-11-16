using System;
using System.Runtime.InteropServices;

namespace StartupMonitor.Shared.Audit.Models;

/// <summary>
/// Represents metadata for a system audit report, including the host, user, OS version, and application build.
/// </summary>
public class AuditMetadata
{
    /// <summary>
    /// Gets or sets the name of the machine where the information was captured.
    /// </summary>
    /// <summary>
    /// Gets or sets the name of the user under whose account the information was collected.
    /// </summary>
    /// <summary>
    /// Gets or sets the description of the host operating system.
    /// </summary>
    /// <summary>
    /// Gets or sets the processor architecture of the host system.
    /// </summary>
    /// <summary>
    /// Gets or sets the version string of the application.
    /// </summary>
    /// <summary>
    /// Gets or sets the local date and time when the information was generated.
    /// </summary>
    /// <summary>
    /// Gets or sets the UTC date and time when the information was generated.
    /// </summary>
    /// <summary>
    /// Gets or sets a value indicating whether the service is currently connected.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the user name associated with this report.
    /// </summary>
    /// <summary>
    /// Gets or sets the description of the operating system.
    /// </summary>
    /// <summary>
    /// Gets or sets the processor architecture of the host.
    /// </summary>
    /// <summary>
    /// Gets or sets the application version string.
    /// </summary>
    /// <summary>
    /// Gets or sets the local time when the report was generated.
    /// </summary>
    /// <summary>
    /// Gets or sets the UTC time when the report was generated.
    /// </summary>
    /// <summary>
    /// Gets or sets a value indicating whether the service is connected.
    /// </summary>
    /// <summary>
    /// Gets or sets a value indicating whether strict mode is enforced.
    /// </summary>
    public string UserName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the human-readable description of the operating system.
    /// </summary>
    /// <summary>
    /// Gets or sets the processor architecture name of the current platform.
    /// </summary>
    /// <summary>
    /// Gets or sets the version string of the application.
    /// </summary>
    /// <summary>
    /// Gets or sets the local timestamp when the report was generated.
    /// </summary>
    /// <summary>
    /// Gets or sets the UTC timestamp when the report was generated.
    /// </summary>
    /// <summary>
    /// Gets or sets a value indicating whether the service connection is established.
    /// </summary>
    /// <summary>
    /// Gets or sets a value indicating whether strict validation mode is enabled.
    /// </summary>
    /// <summary>
    /// Gets or sets a value indicating whether silent mode is enabled.
    /// </summary>
    public string OsDescription { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets environment and build metadata: architecture, application version, generation timestamps (local and UTC), and service connection, strict mode, and silent mode flags.
    /// </summary>
    public string Architecture { get; set; } = string.Empty;
    /// <summary>
    /// Creates a new <see cref="AuditMetadata"/> instance initialized with the current application version and timestamps.
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// Creates a new <see cref="AuditMetadata"/> instance populated with the current application version and runtime settings.
    /// </summary>
    public bool IsServiceConnected { get; set; }
    /// <summary>
    /// Creates new audit metadata populated with the current application version and the specified connection, strict, and silent mode flags.
    /// </summary>
    public bool StrictMode { get; set; }
    /// <summary>
    /// Creates an <see cref="AuditMetadata"/> instance populated with the current application version and connection settings.
    /// </summary>
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

