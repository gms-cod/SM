using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Информация об отдельном физическом или логическом дисковом накопителе.
/// </summary>
public sealed class SystemDiskInfo
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public double TotalGb { get; set; }
    public double FreeGb { get; set; }
    public double UsedGb { get; set; }
    public int UsedPercent { get; set; }

    [JsonIgnore]
    public string DisplaySummary => $"{Name} ({Label}) — {FreeGb:F1} ГБ своб. из {TotalGb:F1} ГБ ({UsedPercent}% занято)";
}

/// <summary>
/// Полные аппаратные и системные характеристики компьютера, собираемые за миллисекунды.
/// </summary>
public sealed class SystemComputerInfo
{
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string OsName { get; set; } = string.Empty;
    public string OsDisplayVersion { get; set; } = string.Empty;
    public string OsBuild { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string CpuName { get; set; } = string.Empty;
    public int LogicalCores { get; set; }
    public double RamTotalGb { get; set; }
    public double RamAvailableGb { get; set; }
    public double RamUsedGb { get; set; }
    public int RamUsedPercent { get; set; }
    public TimeSpan SystemUptime { get; set; }
    public List<SystemDiskInfo> Disks { get; set; } = new();

    /// <summary>
    /// Компактная сводка одной строкой для статус-бара / тултипа.
    /// </summary>
    [JsonIgnore]
    public string CompactSummary =>
        $"{CpuName} ({LogicalCores} потоков) • RAM: {RamUsedGb:F1}/{RamTotalGb:F1} ГБ ({RamUsedPercent}%) • Диски: {string.Join(", ", Disks.Select(d => $"{d.Name} {d.FreeGb:F0}/{d.TotalGb:F0} ГБ"))}";

    /// <summary>
    /// Человекочитаемая строка аптайма системы.
    /// </summary>
    [JsonIgnore]
    public string UptimeDisplay =>
        $"{(int)SystemUptime.TotalDays} дн {SystemUptime.Hours} ч {SystemUptime.Minutes} мин";

    /// <summary>
    /// Формирует структурированный текстовый отчёт о конфигурации компьютера.
    /// </summary>
    public string ToReportString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== СИСТЕМНАЯ ИНФОРМАЦИЯ О КОМПЬЮТЕРЕ ===");
        sb.AppendLine($"Процессор: {CpuName} ({LogicalCores} потоков)");
        sb.AppendLine($"Оперативная память: {RamUsedGb:F1} / {RamTotalGb:F1} ГБ ({RamUsedPercent}% занято, свободно {RamAvailableGb:F1} ГБ)");
        sb.AppendLine($"ОС: {OsName} {OsDisplayVersion} (Сборка {OsBuild}, {Architecture})");
        sb.AppendLine($"Компьютер: {MachineName} (пользователь: {UserName})");
        sb.AppendLine($"Время работы: {UptimeDisplay}");
        sb.AppendLine("--- НАКОПИТЕЛИ И ДИСКИ ---");
        foreach (var d in Disks)
        {
            sb.AppendLine($"  - Диск [{d.Name}] {d.Label} ({d.Format}): свободно {d.FreeGb:F1} ГБ из {d.TotalGb:F1} ГБ ({d.UsedPercent}% занято)");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Преобразует байты в понятный строковый размер.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} Б";
        double kb = bytes / 1024.0;
        if (kb < 1024) return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{kb:F1} КБ");
        double mb = kb / 1024.0;
        if (mb < 1024) return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mb:F1} МБ");
        double gb = mb / 1024.0;
        if (gb < 1024) return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{gb:F1} ГБ");
        double tb = gb / 1024.0;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tb:F1} ТБ");
    }

    #region Win32 Native Memory Helper

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>GlobalMemoryStatusEx: Выполняет операцию GlobalMemoryStatusEx.</summary>
    /// <param name="lpBuffer">Входные данные: значение параметра lpBuffer (ref MEMORYSTATUSEX).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    #endregion

    /// <summary>
    /// Мгновенно собирает актуальные аппаратные характеристики компьютера через прямой Win32 P/Invoke и реестр.
    /// Время выполнения: ~1-3 мс (нулевая нагрузка на процессор).
    /// </summary>
    public static SystemComputerInfo Collect()
    {
        var info = new SystemComputerInfo();

        try
        {
            info.MachineName = Environment.MachineName;
            info.UserName = Environment.UserName;
            info.LogicalCores = Environment.ProcessorCount;
            info.Architecture = Environment.Is64BitOperatingSystem ? "x64 (64-бит)" : "x86 (32-бит)";
            info.SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            // 1. Процессор из аппаратного реестра
            try
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var procName = cpuKey?.GetValue("ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(procName))
                {
                    info.CpuName = procName.Trim();
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(info.CpuName))
            {
                info.CpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown Processor";
            }

            // 2. Операционная система и сборка
            try
            {
                using var osKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (osKey != null)
                {
                    var prodName = osKey.GetValue("ProductName") as string;
                    var displayVer = osKey.GetValue("DisplayVersion") as string ?? osKey.GetValue("ReleaseId") as string;
                    var currentBuild = osKey.GetValue("CurrentBuild") as string ?? osKey.GetValue("CurrentBuildNumber") as string;
                    var ubr = osKey.GetValue("UBR")?.ToString();

                    info.OsName = prodName ?? Environment.OSVersion.VersionString;
                    info.OsDisplayVersion = displayVer ?? string.Empty;
                    info.OsBuild = !string.IsNullOrEmpty(ubr) ? $"{currentBuild}.{ubr}" : (currentBuild ?? string.Empty);
                }
            }
            catch
            {
                info.OsName = Environment.OSVersion.VersionString;
            }

            // 3. Оперативная память через Win32 API
            try
            {
                var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
                {
                    info.RamTotalGb = Math.Round((double)mem.ullTotalPhys / (1024 * 1024 * 1024), 1);
                    info.RamAvailableGb = Math.Round((double)mem.ullAvailPhys / (1024 * 1024 * 1024), 1);
                    info.RamUsedGb = Math.Round((double)(mem.ullTotalPhys - mem.ullAvailPhys) / (1024 * 1024 * 1024), 1);
                    info.RamUsedPercent = (int)Math.Round(((double)(mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys) * 100);
                }
            }
            catch { }

            // 4. Накопители (DriveInfo — только готовые фиксированные диски)
            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var d in drives)
                {
                    if (d.IsReady && d.DriveType == DriveType.Fixed)
                    {
                        var totalGb = Math.Round((double)d.TotalSize / (1024 * 1024 * 1024), 1);
                        var freeGb = Math.Round((double)d.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
                        var usedGb = Math.Round((double)(d.TotalSize - d.AvailableFreeSpace) / (1024 * 1024 * 1024), 1);
                        var usedPct = d.TotalSize > 0 ? (int)Math.Round(((double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize) * 100) : 0;

                        info.Disks.Add(new SystemDiskInfo
                        {
                            Name = d.Name.TrimEnd('\\'),
                            Label = !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : "Локальный диск",
                            Format = d.DriveFormat,
                            TotalGb = totalGb,
                            FreeGb = freeGb,
                            UsedGb = usedGb,
                            UsedPercent = usedPct
                        });
                    }
                }
            }
            catch { }
        }
        catch { }

        return info;
    }
}

