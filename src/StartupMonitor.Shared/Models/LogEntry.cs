using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>Строка лога, прочитанная из лог-файла службы или UI.</summary>
public class LogEntry
{
    /// <summary>Время записи лога.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>Источник лога (service, ui, scanner).</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Уровень (INFO, WARN, ERROR, DEBUG).</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    /// <summary>Текст сообщения.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Полная строка из лог-файла (включая префикс с датой и уровнем).</summary>
    [JsonPropertyName("full_line")]
    public string FullLine { get; set; } = string.Empty;
}
