namespace StartupMonitor.Shared.Models;

public class KasperskyResult
{
    public string Zone { get; set; } = "";
    public string DetectionName { get; set; } = "";
    public string FileStatus { get; set; } = "";
    public int? HitsCount { get; set; }
    public string? Signer { get; set; }
    public string? DescriptionUrl { get; set; }
    public bool IsFound { get; set; }
}

/// <summary>Результат тестовой проверки подключения к API Kaspersky OpenTIP.</summary>
public class KasperskyTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Zone { get; set; }
    public string? Status { get; set; }
}

/// <summary>DTO для передачи настроек Kaspersky OpenTIP через IPC.</summary>
public class KasperskyConfigDto
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
}
