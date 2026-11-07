using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Watcher для папок автозапуска (Startup Menu).
/// Сканирует папки всех пользователей и общую папку автозапуска,
/// отслеживает новые/перемещённые файлы через FileSystemWatcher.</summary>
public class StartupFolderWatcher : BackgroundService
{
    /// <summary>Хранилище записей автозапуска.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Менеджер чёрного списка.</summary>
    private readonly BlacklistManager _blacklist;
    /// <summary>Сервис уведомлений для отправки в UI.</summary>
    private readonly NotificationService _notifications;
    /// <summary>Логгер.</summary>
    private readonly ILogger<StartupFolderWatcher> _logger;
    /// <summary>Трекер начального сканирования.</summary>
    private readonly InitialScanTracker _tracker;
    private readonly MonitoringStateCoordinator _coordinator;
    /// <summary>Список активных FileSystemWatcher для наблюдения за папками.</summary>
    private readonly List<FileSystemWatcher> _watchers = [];

    public StartupFolderWatcher(
        JsonDataStore store,
        BlacklistManager blacklist,
        NotificationService notifications,
        ILogger<StartupFolderWatcher> logger,
        InitialScanTracker tracker,
        MonitoringStateCoordinator coordinator)
    {
        _store = store;
        _blacklist = blacklist;
        _notifications = notifications;
        _logger = logger;
        _tracker = tracker;
        _coordinator = coordinator;
    }

    /// <summary>Возвращает пути ко всем папкам автозапуска всех пользователей.</summary>
    private static string[] GetAllUserStartupFolders()
    {
        var folders = new List<string>();

        // Ищем корневую папку профилей пользователей через системный реестр или профиль текущего пользователя
        string? usersDir = null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            var raw = key?.GetValue("ProfilesDirectory")?.ToString();
            if (!string.IsNullOrEmpty(raw))
                usersDir = Environment.ExpandEnvironmentVariables(raw);
        }
        catch { }

        if (string.IsNullOrEmpty(usersDir) || !Directory.Exists(usersDir))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            usersDir = !string.IsNullOrEmpty(userProfile) ? Path.GetDirectoryName(userProfile) : null;
        }

        if (string.IsNullOrEmpty(usersDir) || !Directory.Exists(usersDir))
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            usersDir = Path.Combine(systemDrive, "Users");
        }

        if (Directory.Exists(usersDir))
        {
            foreach (var userDir in Directory.GetDirectories(usersDir))
            {
                var startup = Path.Combine(userDir, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs", "Startup");
                if (Directory.Exists(startup))
                    folders.Add(startup);
            }
        }

        // Общая папка автозапуска (All Users)
        var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        if (!string.IsNullOrEmpty(commonStartup) && Directory.Exists(commonStartup))
            folders.Add(commonStartup);

        return folders.ToArray();
    }

    /// <summary>Основной цикл: начальное сканирование → наблюдение за файловой системой.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StartupFolderWatcher started");
        _tracker.CurrentStage = "Сканирование папок автозапуска...";
        try
        {
            await InitialScan(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup folders initial scan failed");
        }
        finally
        {
            // Без MarkCompleted pipe-запросы будут висеть до таймаута всю жизнь сервиса
            _tracker.MarkCompleted();
        }
        StartWatching(stoppingToken);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        finally { DisposeWatchers(); }
    }

    /// <summary>Начальное сканирование: читаем все файлы из папок автозапуска.</summary>
    private Task InitialScan(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var _profFolder = ExecutionProfiler.Profile("Service.FolderScan");
            foreach (var folder in GetAllUserStartupFolders())
            {
                if (!Directory.Exists(folder)) continue;

                foreach (var file in Directory.GetFiles(folder))
                {
                    ProcessFile(file, isInitial: true);
                }
            }
            _logger.LogInformation("Startup folders initial scan complete");
        }, ct);
    }

    /// <summary>Запускает FileSystemWatcher для каждой папки автозапуска.</summary>
    private void StartWatching(CancellationToken ct)
    {
        foreach (var folder in GetAllUserStartupFolders())
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
                };

                // Новый файл создан в папке
                watcher.Created += (s, e) =>
                {
                    _logger.LogInformation("New file in startup folder: {File}", e.Name);
                    ProcessFile(e.FullPath);
                };

                // Перемещение файла в папку — это Renamed, а не Created
                watcher.Renamed += (s, e) =>
                {
                    _logger.LogInformation("File moved into startup folder: {File}", e.Name);
                    ProcessFile(e.FullPath);
                };

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to watch folder: {Folder}", folder);
            }
        }
    }

    /// <summary>Освобождает все FileSystemWatcher.</summary>
    private void DisposeWatchers()
    {
        foreach (var w in _watchers)
            try { w.Dispose(); } catch { }
        _watchers.Clear();
    }

    /// <summary>Обрабатывает новый файл в папке автозапуска: проверка ЧС, добавление, уведомление.</summary>
    private void ProcessFile(string filePath, bool isInitial = false)
    {
        try
        {
            if (!isInitial && _coordinator.IsPaused) return;

            // Отключённые нами файлы (*.disabled) — не новые записи
            if (filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) return;
            // desktop.ini — системный файл папки, не автозапуск
            if (Path.GetFileName(filePath).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return;

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return;

            var entry = new StartupEntry
            {
                Name = fileInfo.Name,
                FilePath = fileInfo.FullName,
                Location = EntryLocation.StartupFolder,
                LocationDetail = fileInfo.DirectoryName ?? string.Empty,
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            entry.DetectedAt = DateTime.UtcNow;
            EntryOperations.ResolveAndEnrichEntry(entry);

            // Проверка чёрного списка: правило создал пользователь — применяем авто-воздействие,
            // но не во время initial scan (нет предупреждения, только пометка)
            var matched = _blacklist.MatchEntry(entry);
            if (matched != null)
            {
                if (!isInitial && matched.Action == BlacklistAction.NotifyAndRemove)
                {
                    _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                    return;
                }
                entry.Status = EntryStatus.Blocked;
                entry.FromBlacklist = true;
            }

            // Добавление/обновление в хранилище
            if (!WatcherHelper.AddOrUpdate(_store, _notifications, entry, matched, _blacklist)) return;

            // Запись из ЧС снова появилась — автоматически останавливаем процесс и удаляем (с предупреждением)
            if (matched != null && !isInitial)
            {
                _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                return;
            }

            _notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.EntryAdded,
                Payload = System.Text.Json.JsonSerializer.Serialize(entry)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process startup file: {File}", filePath);
        }
    }
}
