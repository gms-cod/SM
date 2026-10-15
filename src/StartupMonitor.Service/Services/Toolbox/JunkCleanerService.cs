using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using StartupMonitor.Shared.Models.Toolbox;

namespace StartupMonitor.Service.Services.Toolbox;

/// <summary>
/// Сервис безопасной экспресс-очистки дискового мусора и временных файлов.
/// </summary>
public class JunkCleanerService
{
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public const string CategoryUserTemp = "user_temp";
    public const string CategorySystemTemp = "sys_temp";
    public const string CategoryCrashDumps = "crash_dumps";
    public const string CategoryExplorerThumbs = "explorer_thumbs";
    public const string CategoryWindowsUpdate = "windows_update";
    public const string CategoryRecycleBin = "recycle_bin";

    /// <summary>
    /// Анализирует доступные категории мусора и подсчитывает размер.
    /// </summary>
    public JunkScanResultDto ScanJunk()
    {
        var result = new JunkScanResultDto();

        // 1. User Temp
        try
        {
            var userTemp = Path.GetTempPath();
            var info = ScanDirectory(userTemp, CategoryUserTemp, "Временные файлы пользователя (%TEMP%)", "Кэш и временные файлы установщиков и приложений");
            result.Categories.Add(info);
        }
        catch { }

        // 2. System Temp
        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var sysTemp = Path.Combine(winDir, "Temp");
            var info = ScanDirectory(sysTemp, CategorySystemTemp, "Системные временные файлы (Windows Temp)", "Логи и кэш системных служб и драйверов");
            result.Categories.Add(info);
        }
        catch { }

        // 3. Crash Dumps
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dumpsPath = Path.Combine(localAppData, "CrashDumps");
            var info = ScanDirectory(dumpsPath, CategoryCrashDumps, "Дампы аварийного завершения программ", "Отчеты об ошибках и дампы памяти сбойных процессов");
            result.Categories.Add(info);
        }
        catch { }

        // 4. Explorer Thumbnails
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var thumbsPath = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
            var info = ScanDirectory(thumbsPath, CategoryExplorerThumbs, "Кэш миниатюр Проводника (Thumbnails)", "Временные эскизы изображений и видео (*.db)");
            result.Categories.Add(info);
        }
        catch { }

        // 5. Windows Update Cache
        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var wuPath = Path.Combine(winDir, "SoftwareDistribution", "Download");
            var info = ScanDirectory(wuPath, CategoryWindowsUpdate, "Кэш загрузок Windows Update", "Уже установленные загрузочные пакеты обновлений ОС");
            result.Categories.Add(info);
        }
        catch { }

        // 6. Recycle Bin
        try
        {
            var info = ScanRecycleBin();
            result.Categories.Add(info);
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Очищает выбранные категории мусора.
    /// </summary>
    public JunkCleanResultDto Clean(List<string>? selectedCategoryIds = null) => CleanJunk(selectedCategoryIds ?? new List<string>());

    /// <summary>
    /// Очищает выбранные категории мусора.
    /// </summary>
    public JunkCleanResultDto CleanJunk(List<string> selectedCategoryIds)
    {
        var result = new JunkCleanResultDto();
        var selectedSet = new HashSet<string>(selectedCategoryIds ?? [], StringComparer.OrdinalIgnoreCase);

        // 1. User Temp
        if (selectedSet.Contains(CategoryUserTemp))
        {
            CleanDirectory(Path.GetTempPath(), result);
        }

        // 2. System Temp
        if (selectedSet.Contains(CategorySystemTemp))
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            CleanDirectory(Path.Combine(winDir, "Temp"), result);
        }

        // 3. Crash Dumps
        if (selectedSet.Contains(CategoryCrashDumps))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CleanDirectory(Path.Combine(localAppData, "CrashDumps"), result);
        }

        // 4. Explorer Thumbnails
        if (selectedSet.Contains(CategoryExplorerThumbs))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CleanDirectory(Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"), result, "*.db");
        }

        // 5. Windows Update
        if (selectedSet.Contains(CategoryWindowsUpdate))
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            CleanDirectory(Path.Combine(winDir, "SoftwareDistribution", "Download"), result);
        }

        // 6. Recycle Bin
        if (selectedSet.Contains(CategoryRecycleBin))
        {
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            }
            catch { }
        }

        return result;
    }

    private JunkCategoryInfo ScanDirectory(string path, string id, string name, string desc, string searchPattern = "*")
    {
        var info = new JunkCategoryInfo
        {
            Id = id,
            Name = name,
            Description = desc
        };

        if (!Directory.Exists(path))
            return info;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            foreach (var file in dirInfo.EnumerateFiles(searchPattern, SearchOption.AllDirectories))
            {
                try
                {
                    info.TotalSizeBytes += file.Length;
                    info.FileCount++;
                }
                catch { }
            }
        }
        catch { }

        return info;
    }

    private JunkCategoryInfo ScanRecycleBin()
    {
        // Простая оценка корзины по папкам $Recycle.Bin на всех логических дисках
        var info = new JunkCategoryInfo
        {
            Id = CategoryRecycleBin,
            Name = "Корзина (Recycle Bin)",
            Description = "Файлы, удаленные пользователем в корзину"
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            try
            {
                var rbPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(rbPath))
                {
                    var dirInfo = new DirectoryInfo(rbPath);
                    foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            info.TotalSizeBytes += file.Length;
                            info.FileCount++;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        return info;
    }

    private void CleanDirectory(string path, JunkCleanResultDto result, string searchPattern = "*")
    {
        if (!Directory.Exists(path)) return;

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    long len = file.Length;
                    file.Delete();
                    result.FreedSizeBytes += len;
                    result.DeletedFilesCount++;
                }
                catch
                {
                    result.ErrorsCount++;
                }
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                try
                {
                    sub.Delete(true);
                }
                catch { }
            }
        }
        catch { }
    }
}
