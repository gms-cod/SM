using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Shared.Models.Toolbox;

namespace StartupMonitor.Service.Services.Toolbox;

/// <summary>
/// Сервис безопасной экспресс-очистки дискового мусора и временных файлов.
/// Категории сканируются и очищаются параллельно на всех ядрах процессора
/// (<see cref="Parallel.ForEachAsync"/>), все операции поддерживают отмену.
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

    /// <summary>Описание цели сканирования/очистки категории мусора.</summary>
    private readonly record struct JunkScanTarget(string Id, string Name, string Description, string Path, string SearchPattern);

    /// <summary>Потокобезопасный агрегатор статистики очистки.</summary>
    private sealed class CleanStats
    {
        public long Freed;
        public int Deleted;
        public int Errors;
    }

    /// <summary>
    /// Анализирует доступные категории мусора и подсчитывает размер.
    /// Категории сканируются параллельно на всех ядрах процессора.
    /// </summary>
    public async Task<JunkScanResultDto> ScanJunkAsync(CancellationToken ct = default)
    {
        var result = new JunkScanResultDto();
        var targets = GetScanTargets();

        // Массив фиксированной длины: каждый индекс пишет ровно одна задача,
        // порядок категорий остаётся детерминированным (как в исходной версии).
        var categoryResults = new JunkCategoryInfo?[targets.Count + 1];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, targets.Count),
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            (i, token) =>
            {
                categoryResults[i] = ScanDirectory(targets[i], token);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        categoryResults[targets.Count] = await ScanRecycleBinAsync(ct).ConfigureAwait(false);

        foreach (var info in categoryResults)
        {
            if (info is not null)
                result.Categories.Add(info);
        }

        return result;
    }

    /// <summary>Синхронная версия <see cref="ScanJunkAsync"/> (обратная совместимость).</summary>
    public JunkScanResultDto ScanJunk() => Task.Run(() => ScanJunkAsync(CancellationToken.None)).GetAwaiter().GetResult();

    /// <summary>
    /// Очищает выбранные категории мусора.
    /// </summary>
    public JunkCleanResultDto Clean(List<string>? selectedCategoryIds = null) => CleanJunk(selectedCategoryIds ?? new List<string>());

    /// <summary>Синхронная версия <see cref="CleanJunkAsync"/> (обратная совместимость).</summary>
    public JunkCleanResultDto CleanJunk(List<string> selectedCategoryIds) => Task.Run(() => CleanJunkAsync(selectedCategoryIds, CancellationToken.None)).GetAwaiter().GetResult();

    /// <summary>
    /// Очищает выбранные категории мусора. Категории обрабатываются параллельно,
    /// отмена проверяется перед удалением каждого файла/каталога.
    /// </summary>
    public async Task<JunkCleanResultDto> CleanJunkAsync(List<string> selectedCategoryIds, CancellationToken ct = default)
    {
        var result = new JunkCleanResultDto();
        var selectedSet = new HashSet<string>(selectedCategoryIds ?? [], StringComparer.OrdinalIgnoreCase);
        var stats = new CleanStats();

        var dirTargets = GetScanTargets().Where(t => selectedSet.Contains(t.Id)).ToList();

        await Parallel.ForEachAsync(
            dirTargets,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            (target, token) =>
            {
                CleanDirectory(target.Path, target.SearchPattern, stats, token);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        if (selectedSet.Contains(CategoryRecycleBin))
        {
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            }
            catch { }
        }

        result.FreedSizeBytes = Interlocked.Read(ref stats.Freed);
        result.DeletedFilesCount = Volatile.Read(ref stats.Deleted);
        result.ErrorsCount = Volatile.Read(ref stats.Errors);
        return result;
    }

    /// <summary>Формирует список целей сканирования. Пути, недоступные в текущей среде, игнорируются.</summary>
    private static List<JunkScanTarget> GetScanTargets()
    {
        return new List<JunkScanTarget>(5)
        {
            new JunkScanTarget(
                CategoryUserTemp,
                "Временные файлы пользователя (%TEMP%)",
                "Кэш и временные файлы установщиков и приложений",
                SafePath(() => Path.GetTempPath()),
                "*"),

            new JunkScanTarget(
                CategorySystemTemp,
                "Системные временные файлы (Windows Temp)",
                "Логи и кэш системных служб и драйверов",
                SafePath(() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")),
                "*"),

            new JunkScanTarget(
                CategoryCrashDumps,
                "Дампы аварийного завершения программ",
                "Отчеты об ошибках и дампы памяти сбойных процессов",
                SafePath(() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps")),
                "*"),

            new JunkScanTarget(
                CategoryExplorerThumbs,
                "Кэш миниатюр Проводника (Thumbnails)",
                "Временные эскизы изображений и видео (*.db)",
                SafePath(() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer")),
                "*.db"),

            new JunkScanTarget(
                CategoryWindowsUpdate,
                "Кэш загрузок Windows Update",
                "Уже установленные загрузочные пакеты обновлений ОС",
                SafePath(() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")),
                "*"),
        };
    }

    private static string SafePath(Func<string> factory)
    {
        try { return factory() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static JunkCategoryInfo ScanDirectory(JunkScanTarget target, CancellationToken ct)
    {
        var info = new JunkCategoryInfo
        {
            Id = target.Id,
            Name = target.Name,
            Description = target.Description
        };

        if (string.IsNullOrEmpty(target.Path) || !Directory.Exists(target.Path))
            return info;

        long size = 0;
        int count = 0;

        try
        {
            var dirInfo = new DirectoryInfo(target.Path);
            foreach (var file in dirInfo.EnumerateFiles(target.SearchPattern, SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    size += file.Length;
                    count++;
                }
                catch { }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch { }

        info.TotalSizeBytes = size;
        info.FileCount = count;
        return info;
    }

    private async Task<JunkCategoryInfo> ScanRecycleBinAsync(CancellationToken ct)
    {
        // Простая оценка корзины по папкам $Recycle.Bin на всех логических дисках.
        var info = new JunkCategoryInfo
        {
            Id = CategoryRecycleBin,
            Name = "Корзина (Recycle Bin)",
            Description = "Файлы, удаленные пользователем в корзину"
        };

        long total = 0;
        int count = 0;

        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();

        await Parallel.ForEachAsync(
            drives,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            (drive, token) =>
            {
                try
                {
                    var rbPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                    if (Directory.Exists(rbPath))
                    {
                        var dirInfo = new DirectoryInfo(rbPath);
                        foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                Interlocked.Add(ref total, file.Length);
                                Interlocked.Increment(ref count);
                            }
                            catch { }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        info.TotalSizeBytes = total;
        info.FileCount = count;
        return info;
    }

    private static void CleanDirectory(string path, string searchPattern, CleanStats stats, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long len = file.Length;
                    file.Delete();
                    Interlocked.Add(ref stats.Freed, len);
                    Interlocked.Increment(ref stats.Deleted);
                }
                catch
                {
                    Interlocked.Increment(ref stats.Errors);
                }
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    sub.Delete(true);
                }
                catch { }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch { }
    }
}
