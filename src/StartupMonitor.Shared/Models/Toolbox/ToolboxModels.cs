using System;
using System.Collections.Generic;
using System.Linq;

namespace StartupMonitor.Shared.Models.Toolbox;

/// <summary>
/// Информация о процессе, удерживающем блокировку файла/папки.
/// </summary>
public class FileLockProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string ApplicationType { get; set; } = string.Empty;
}

/// <summary>
/// Результат поиска блокирующих процессов для целевого файла или папки.
/// </summary>
public class FileLockResultDto
{
    public string TargetPath { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public List<FileLockProcessInfo> LockingProcesses { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Статус режима бодрствования (Keep-Awake).
/// </summary>
public class AwakeStatusDto
{
    public bool IsActive { get; set; }
    public int RemainingMinutes { get; set; }
    public bool KeepDisplayOn { get; set; } = true;
    public string ModeText { get; set; } = "Disabled";
}

/// <summary>
/// Запрос на изменение режима бодрствования.
/// </summary>
public class SetAwakeRequest
{
    public bool Enable { get; set; }
    public int DurationMinutes { get; set; }
    public bool KeepDisplayOn { get; set; } = true;
}

/// <summary>
/// Категория временного мусора для очистки диска.
/// </summary>
public class JunkCategoryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsSelected { get; set; } = true;

    public string SizeFormatted
    {
        get
        {
            if (TotalSizeBytes >= 1024 * 1024 * 1024)
                return $"{(double)TotalSizeBytes / (1024 * 1024 * 1024):F2} ГБ";
            if (TotalSizeBytes >= 1024 * 1024)
                return $"{(double)TotalSizeBytes / (1024 * 1024):F1} МБ";
            if (TotalSizeBytes >= 1024)
                return $"{(double)TotalSizeBytes / 1024:F0} КБ";
            return $"{TotalSizeBytes} Б";
        }
    }
}

/// <summary>
/// Результат анализа дискового мусора.
/// </summary>
public class JunkScanResultDto
{
    public List<JunkCategoryInfo> Categories { get; set; } = new();
    public long TotalSizeBytes => Categories.Sum(c => c.TotalSizeBytes);
    public int TotalFilesCount => Categories.Sum(c => c.FileCount);
}

/// <summary>
/// Результат очистки дискового мусора.
/// </summary>
public class JunkCleanResultDto
{
    public long FreedSizeBytes { get; set; }
    public int DeletedFilesCount { get; set; }
    public int ErrorsCount { get; set; }
}

/// <summary>
/// Обнаруженное устройство в локальной сети (Wi-Fi / Ethernet).
/// </summary>
public class LanDeviceDto
{
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public bool IsCurrentDevice { get; set; }
    public bool IsGateway { get; set; }
}

/// <summary>
/// Запрос на очистку выбранных категорий мусора.
/// </summary>
public class CleanJunkRequest
{
    public List<string> CategoryIds { get; set; } = new();
}

/// <summary>
/// Запрос на поиск процессов, блокирующих файл или папку.
/// </summary>
public class FindFileLockRequest
{
    public string TargetPath { get; set; } = string.Empty;
}

