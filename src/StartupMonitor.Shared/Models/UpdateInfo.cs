using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>Информация о доступном обновлении из GitHub Releases.</summary>
public class UpdateInfo
{
    /// <summary>Номер доступной версии (например, "1.2.3").</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Заголовок релиза.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Текст чейнджлога (описание изменений в релизе).</summary>
    [JsonPropertyName("changelog")]
    public string Changelog { get; set; } = string.Empty;

    /// <summary>Дата публикации релиза (UTC).</summary>
    [JsonPropertyName("release_date")]
    public DateTime ReleaseDate { get; set; }

    /// <summary>Прямая ссылка на скачивание установочного пакета (.exe).</summary>
    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Имя файла установочного пакета.</summary>
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>Ожидаемая контрольная сумма SHA-256 (если указана в релизе или .sha256 файле).</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>Размер файла в байтах.</summary>
    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    /// <summary>Признак: доступна ли более свежая версия по сравнению с установленной.</summary>
    [JsonPropertyName("is_update_available")]
    public bool IsUpdateAvailable { get; set; }

    /// <summary>Текущая установленная версия приложения.</summary>
    [JsonPropertyName("current_version")]
    public string CurrentVersion { get; set; } = string.Empty;
}

/// <summary>Результат проверки обновлений.</summary>
public class UpdateCheckResult
{
    /// <summary>Успешно ли завершился запрос к GitHub API.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Сообщение об ошибке (если success == false).</summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    /// <summary>Информация об обновлении (если проверка успешна).</summary>
    [JsonPropertyName("update_info")]
    public UpdateInfo? UpdateInfo { get; set; }
}

/// <summary>Текущее состояние процесса загрузки и валидации обновления.</summary>
public class UpdateProgressInfo
{
    /// <summary>Статус операции: "Idle", "Downloading", "Verifying", "Ready", "Failed".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Idle";

    /// <summary>Количество загруженных байт.</summary>
    [JsonPropertyName("bytes_downloaded")]
    public long BytesDownloaded { get; set; }

    /// <summary>Общий размер файла в байтах.</summary>
    [JsonPropertyName("total_bytes")]
    public long TotalBytes { get; set; }

    /// <summary>Процент загрузки (0..100).</summary>
    [JsonPropertyName("percent")]
    public int Percent { get; set; }

    /// <summary>Скорость загрузки (байт/сек).</summary>
    [JsonPropertyName("speed_bytes_per_sec")]
    public long SpeedBytesPerSec { get; set; }

    /// <summary>Локальный путь к загруженному файлу инсталлятора.</summary>
    [JsonPropertyName("downloaded_file_path")]
    public string? DownloadedFilePath { get; set; }

    /// <summary>Текст ошибки при сбое.</summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }
}

/// <summary>Полезная нагрузка сохранения параметров автообновления.</summary>
public record UpdateSettingsPayload(
    [property: JsonPropertyName("auto_check_updates")] bool AutoCheckUpdates,
    [property: JsonPropertyName("auto_download_updates")] bool AutoDownloadUpdates,
    [property: JsonPropertyName("skipped_update_version")] string? SkippedUpdateVersion = null
);

