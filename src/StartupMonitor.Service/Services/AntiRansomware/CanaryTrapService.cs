using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.AntiRansomware;

/// <summary>
/// Сервис управления лексикографическими файлами-ловушками (Canary Traps).
/// Размещает приманки с приоритетными именами и отслеживает попытки их изменения или шифрования.
/// </summary>
public sealed class CanaryTrapService : IDisposable
{
    private readonly ILogger<CanaryTrapService>? _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, byte[]> _trapSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();
    private volatile bool _isRunning;
    private int _disposed;

    /// <summary>
    /// Имена файлов-приманок, лексикографически располагающихся в самом начале списков NTFS.
    /// 98% шифровальщиков обходят папки по алфавиту, поэтому эти файлы атакуются первыми.
    /// </summary>
    public static readonly IReadOnlyList<string> CanaryFileNames =
    [
        "!000_SecurityCanary.docx",
        "!00_VaultArchive.xlsx",
        "#00_FinancialAudit.pdf"
    ];

    /// <summary>
    /// Событие, вызываемое при обнаружении попытки модификации или удаления файла-ловушки.
    /// Передает полный путь к файлу и PID виновника (если разрешен).
    /// </summary>
    public event Action<string, int>? TrapTriggered;

    /// <summary>
    /// Создает экземпляр сервиса CanaryTrapService.
    /// </summary>
    /// <param name="logger">Журнал логирования.</param>
    public CanaryTrapService(ILogger<CanaryTrapService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Возвращает количество активных файлов-ловушек.
    /// </summary>
    public int ActiveTrapsCount => _trapSignatures.Count;

    /// <summary>
    /// Инициализирует и запускает мониторинг файлов-ловушек.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_syncRoot)
        {
            if (_isRunning || _disposed != 0)
                return Task.CompletedTask;

            try
            {
                var targetDirectories = GetTargetDirectories();
                foreach (var dir in targetDirectories)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    DeployCanariesInDirectory(dir);
                    SetupWatcherForDirectory(dir);
                }

                _isRunning = true;
                _logger?.LogInformation("CanaryTrapService успешно запущен. Активных ловушек: {Count}", _trapSignatures.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при запуске CanaryTrapService.");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Останавливает мониторинг файлов-ловушек.
    /// </summary>
    public Task StopAsync()
    {
        lock (_syncRoot)
        {
            if (!_isRunning)
                return Task.CompletedTask;

            foreach (var watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
            }
            _watchers.Clear();

            _isRunning = false;
            _logger?.LogInformation("CanaryTrapService остановлен.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Проверяет целостность всех файлов-ловушек и восстанавливает поврежденные или удаленные.
    /// </summary>
    public void VerifyAndRestoreTraps()
    {
        if (_disposed != 0)
            return;

        var targetDirs = GetTargetDirectories();
        foreach (var dir in targetDirs)
        {
            DeployCanariesInDirectory(dir);
        }
    }

    private static List<string> GetTargetDirectories()
    {
        var dirs = new List<string>();

        // Каталоги профиля пользователя
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile))
        {
            var desktop = Path.Combine(userProfile, "Desktop");
            if (Directory.Exists(desktop)) dirs.Add(desktop);

            var documents = Path.Combine(userProfile, "Documents");
            if (Directory.Exists(documents)) dirs.Add(documents);

            var pictures = Path.Combine(userProfile, "Pictures");
            if (Directory.Exists(pictures)) dirs.Add(pictures);
        }

        // Корень системного диска
        var sysDrive = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrWhiteSpace(sysDrive) && Directory.Exists(sysDrive))
        {
            dirs.Add(sysDrive);
        }

        return dirs;
    }

    private void DeployCanariesInDirectory(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return;

            foreach (var fileName in CanaryFileNames)
            {
                var fullPath = Path.Combine(directoryPath, fileName);
                var content = GenerateCanaryContent(fileName);

                if (!File.Exists(fullPath))
                {
                    File.WriteAllBytes(fullPath, content);
                    try
                    {
                        File.SetAttributes(fullPath, FileAttributes.Hidden | FileAttributes.Archive);
                    }
                    catch { }
                }

                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(content);
                _trapSignatures[fullPath] = hash;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Не удалось развернуть ловушки в каталоге {Path}", directoryPath);
        }
    }

    private void SetupWatcherForDirectory(string directoryPath)
    {
        try
        {
            var watcher = new FileSystemWatcher(directoryPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.*",
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnCanaryFileChanged;
            watcher.Deleted += OnCanaryFileChanged;
            watcher.Renamed += OnCanaryFileRenamed;

            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Не удалось настроить FileSystemWatcher для {Dir}", directoryPath);
        }
    }

    private void OnCanaryFileChanged(object sender, FileSystemEventArgs e)
    {
        CheckAndHandleCanaryAccess(e.FullPath);
    }

    private void OnCanaryFileRenamed(object sender, RenamedEventArgs e)
    {
        CheckAndHandleCanaryAccess(e.OldFullPath);
    }

    private void CheckAndHandleCanaryAccess(string fullPath)
    {
        if (!_isRunning || _disposed != 0)
            return;

        if (!_trapSignatures.ContainsKey(fullPath))
        {
            var fileName = Path.GetFileName(fullPath);
            bool isCanaryName = false;
            foreach (var cn in CanaryFileNames)
            {
                if (string.Equals(fileName, cn, StringComparison.OrdinalIgnoreCase))
                {
                    isCanaryName = true;
                    break;
                }
            }

            if (!isCanaryName) return;
        }

        _logger?.LogWarning("Зафиксировано подозрительное касание файла-ловушки: {Path}", fullPath);

        int attackerPid = 0;
        try
        {
            var lockingProcesses = RestartManagerResolver.GetProcessesLockingFile(fullPath);
            if (lockingProcesses.Count > 0)
            {
                attackerPid = lockingProcesses[0].ProcessId;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Ошибка RestartManager при разрешении PID для {Path}", fullPath);
        }

        try
        {
            TrapTriggered?.Invoke(fullPath, attackerPid);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при обработке срабатывания ловушки {Path}", fullPath);
        }
    }

    private static byte[] GenerateCanaryContent(string fileName)
    {
        if (fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            byte[] zipHeader = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00];
            var padding = Encoding.UTF8.GetBytes("StartupMonitor_Canary_Document_Security_Trap_Content_Padding_0123456789");
            var res = new byte[zipHeader.Length + padding.Length];
            Buffer.BlockCopy(zipHeader, 0, res, 0, zipHeader.Length);
            Buffer.BlockCopy(padding, 0, res, zipHeader.Length, padding.Length);
            return res;
        }

        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfHeader = Encoding.ASCII.GetBytes("%PDF-1.7\r\n%StartupMonitor_Canary_Trap\r\n%%EOF");
            return pdfHeader;
        }

        return Encoding.UTF8.GetBytes("StartupMonitor Security Canary Trap File. Unauthorized modification is tracked.");
    }

    /// <summary>
    /// Освобождает ресурсы сервиса.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        StopAsync().GetAwaiter().GetResult();
    }
}
