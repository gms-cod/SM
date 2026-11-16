namespace StartupMonitor.Shared.Models;

/// <summary>
/// Provides operations for this member.
/// </summary>
public class KasperskyResult
{
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string Zone { get; set; } = "";
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string DetectionName { get; set; } = "";
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string FileStatus { get; set; } = "";
    public int? HitsCount { get; set; }
    public string? Signer { get; set; }
    public string? DescriptionUrl { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public bool IsFound { get; set; }
}

/// <summary>Результат тестовой проверки подключения к API Kaspersky OpenTIP.</summary>
public class KasperskyTestResult
{
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public bool Success { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string Message { get; set; } = string.Empty;
    public string? Zone { get; set; }
    public string? Status { get; set; }
}

/// <summary>DTO для передачи настроек Kaspersky OpenTIP через IPC.</summary>
public class KasperskyConfigDto
{
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public string ApiUrl { get; set; } = string.Empty;
}
