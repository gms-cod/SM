using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>Снимок общей производительности системы и список активных процессов.</summary>
public class SystemPerformanceSnapshot
{
    /// <summary>Общая суммарная загрузка процессора ОС (0.0% - 100.0%).</summary>
    public double TotalCpuPercent { get; set; }

    /// <summary>Общий объем физической памяти системы в байтах.</summary>
    public long TotalMemoryBytes { get; set; }

    /// <summary>Используемый объем физической памяти системы в байтах.</summary>
    public long UsedMemoryBytes { get; set; }

    /// <summary>Процент используемой памяти (0.0% - 100.0%).</summary>
    public double MemoryPercent => TotalMemoryBytes > 0 ? (UsedMemoryBytes * 100.0 / TotalMemoryBytes) : 0.0;

    /// <summary>Алиас процента используемой памяти.</summary>
    public double TotalMemoryPercent => MemoryPercent;

    /// <summary>Суммарная дисковая активность всей системы (Байт/с).</summary>
    public long TotalDiskIoBytesPerSec { get; set; }

    /// <summary>Суммарный сетевой обмен всей системы (Байт/с).</summary>
    public long TotalNetworkBytesPerSec { get; set; }

    /// <summary>Общее количество запущенных процессов.</summary>
    public int TotalProcessesCount { get; set; }

    /// <summary>Общее количество потоков в ОС.</summary>
    public int TotalThreadsCount { get; set; }

    /// <summary>Список активных процессов со снимком их метрик.</summary>
    public List<ProcessInfo> Processes { get; set; } = [];

    // Вспомогательные свойства форматирования для UI
    [JsonIgnore]
    public string DisplayTotalCpu => $"{TotalCpuPercent:F0} %";

    [JsonIgnore]
    public string DisplayTotalRam => $"{UsedMemoryBytes / (1024.0 * 1024 * 1024):F1} / {TotalMemoryBytes / (1024.0 * 1024 * 1024):F1} ГБ ({MemoryPercent:F0}%)";

    [JsonIgnore]
    public string DisplayTotalDisk => TotalDiskIoBytesPerSec switch
    {
        < 1024 => "0 КБ/с",
        < 1024 * 1024 => $"{TotalDiskIoBytesPerSec / 1024.0:F0} КБ/с",
        _ => $"{TotalDiskIoBytesPerSec / (1024.0 * 1024):F1} МБ/с"
    };

    [JsonIgnore]
    public string DisplayTotalNetwork => TotalNetworkBytesPerSec switch
    {
        <= 0 => "0 КБ/с",
        < 1024 => $"{TotalNetworkBytesPerSec / 1024.0:F1} КБ/с",
        < 1024 * 1024 => $"{TotalNetworkBytesPerSec / 1024.0:F0} КБ/с",
        _ => $"{TotalNetworkBytesPerSec / (1024.0 * 1024):F1} МБ/с"
    };
}

