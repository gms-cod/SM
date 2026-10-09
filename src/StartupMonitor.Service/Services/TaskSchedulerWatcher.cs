using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;
using Task = System.Threading.Tasks.Task;

namespace StartupMonitor.Service.Services;

/// <summary>Watcher для планировщика задач Windows. Выполняет начальное сканирование
/// всех задач с триггерами Logon/Boot, затем опрашивает планировщик каждые 30 секунд
/// на предмет новых задач автозапуска.</summary>
public class TaskSchedulerWatcher : BackgroundService
{
    /// <summary>Хранилище записей автозапуска.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Менеджер чёрного списка.</summary>
    private readonly BlacklistManager _blacklist;
    /// <summary>Сервис уведомлений для отправки в UI.</summary>
    private readonly NotificationService _notifications;
    /// <summary>Логгер.</summary>
    private readonly ILogger<TaskSchedulerWatcher> _logger;
    /// <summary>Трекер начального сканирования.</summary>
    private readonly InitialScanTracker _tracker;
    private readonly MonitoringStateCoordinator _coordinator;
    /// <summary>Подключение к планировщику задач (COM-объект).</summary>
    private TaskService? _taskService;

    public TaskSchedulerWatcher(
        JsonDataStore store,
        BlacklistManager blacklist,
        NotificationService notifications,
        ILogger<TaskSchedulerWatcher> logger,
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

    /// <summary>Dispose: Освобождает занятые ресурсы объекта.</summary>
    public override void Dispose()
    {
        try { _taskService?.Dispose(); } catch { }
        _taskService = null;
        base.Dispose();
    }

    /// <summary>Получает или пересоздаёт подключение к планировщику задач.</summary>
    private TaskService GetOrCreateTaskService()
    {
        if (_taskService == null)
            _taskService = new TaskService();
        return _taskService;
    }

    /// <summary>Основной цикл: подключение к планировщику → начальное сканирование → опрос изменений.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("TaskSchedulerWatcher started");
            _tracker.CurrentStage = "Сканирование планировщика задач...";

            // Попытка подключения к COM-планировщику задач
            try
            {
                GetOrCreateTaskService();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось подключиться к планировщику задач, TaskSchedulerWatcher остановлен");
                _tracker.MarkCompleted();
                return;
            }

            await InitialScan(stoppingToken);
            _tracker.MarkCompleted();

            await PollForChanges(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("TaskSchedulerWatcher штатно остановлен");
            _tracker.MarkCompleted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TaskSchedulerWatcher аварийно завершился");
            _tracker.MarkCompleted();
        }
    }

    /// <summary>Начальное сканирование: итерация по всем задачам, поиск задач автозапуска.</summary>
    private Task InitialScan(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var _profTasks = ExecutionProfiler.Profile("Service.TaskSchedulerScan");
            try
            {
                var ts = GetOrCreateTaskService();
                var tasks = ts.AllTasks;
                if (tasks == null) return;

                foreach (var task in tasks)
                {
                    try
                    {
                        if (ct.IsCancellationRequested) break;
                        if (!IsStartupTask(task)) continue;
                        ProcessTask(task);
                    }
                    finally
                    {
                        try { task.Dispose(); } catch { }
                    }
                }

                _logger.LogInformation("Task Scheduler initial scan complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan Task Scheduler");
                try { _taskService?.Dispose(); } catch { }
                _taskService = null;
            }
        }, ct);
    }

    /// <summary>Периодический опрос (каждые 30 с) для обнаружения новых задач.</summary>
    private async Task PollForChanges(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_coordinator.IsPaused)
            {
                await Task.Delay(5000, ct);
                continue;
            }

            List<Microsoft.Win32.TaskScheduler.Task>? tasks = null;
            try
            {
                // Получаем все задачи (включая скрытые)
                tasks = GetAllTasksWithHidden();
                if (tasks == null)
                {
                    await Task.Delay(30000, ct);
                    continue;
                }

                var startupTasks = tasks.Where(IsStartupTask);

                foreach (var task in startupTasks)
                {
                    var existing = FindByTaskPath(task.Path);
                    if (existing == null)
                        ProcessTask(task);
                    else if (string.IsNullOrEmpty(existing.CommandLine))
                    {
                        // Обновляем CommandLine, если он пустой (дозаполняем)
                        var taskCmd = FormatTaskAction(task);
                        existing.CommandLine = taskCmd;
                        existing.FilePath = taskCmd;
                        _store.UpdateEntry(existing);
                    }
                    else if (string.IsNullOrEmpty(existing.FilePath) || existing.FilePath == existing.Name)
                    {
                        // Обновляем FilePath, если это просто имя задачи
                        var taskCmd = FormatTaskAction(task);
                        existing.FilePath = taskCmd;
                        _store.UpdateEntry(existing);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Task Scheduler poll failed");
                try { _taskService?.Dispose(); } catch { }
                _taskService = null;
            }
            finally
            {
                if (tasks != null)
                {
                    foreach (var task in tasks)
                    {
                        try { task.Dispose(); } catch { }
                    }
                }
            }

            await Task.Delay(30000, ct);
        }
    }

    /// <summary>Обрабатывает задачу: создаёт запись, проверяет ЧС, добавляет в хранилище, уведомляет UI.</summary>
    private void ProcessTask(Microsoft.Win32.TaskScheduler.Task task)
    {
        try
        {
            // Пропускаем если уже есть (проверка по полному пути)
            var existing = FindByTaskPath(task.Path);
            if (existing != null) return;

            var taskCmd = FormatTaskAction(task);
            var entry = new StartupEntry
            {
                Name = task.Name,
                FilePath = taskCmd,
                Location = EntryLocation.TaskScheduler,
                LocationDetail = task.Path ?? string.Empty,
                CommandLine = taskCmd,
                Status = task.Enabled ? EntryStatus.Enabled : EntryStatus.Disabled,
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            entry.DetectedAt = DateTime.UtcNow;
            EntryOperations.ResolveAndEnrichEntry(entry);

            var matched = _blacklist.MatchEntry(entry);
            if (matched != null)
            {
                if (matched.Action == BlacklistAction.NotifyAndRemove)
                {
                    _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                    _logger.LogInformation("Blacklist: задача {Name} заблокирована и отключена", task.Name);
                    return;
                }
                entry.Status = EntryStatus.Blocked;
                entry.FromBlacklist = true;
            }

            if (!WatcherHelper.AddOrUpdate(_store, _notifications, entry, matched, _blacklist)) return;

            // Запись из ЧС снова появилась — автоматически останавливаем процесс и удаляем (с предупреждением)
            if (matched != null)
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
            _logger.LogError(ex, "Failed to process task: {Task}", task.Name);
        }
    }

    /// <summary>Задача относится к автозапуску: есть ExecAction и триггер Logon/Boot.</summary>
    private static bool IsStartupTask(Microsoft.Win32.TaskScheduler.Task task)
    {
        try
        {
            if (!task.Definition.Actions.OfType<ExecAction>().Any()) return false;
            return task.Definition.Triggers.Any(tr =>
                tr is LogonTrigger or BootTrigger);
        }
        catch { return false; }
    }

    /// <summary>Имена задач не уникальны между папками — сравниваем по полному пути задачи.</summary>
    private StartupEntry? FindByTaskPath(string? taskPath)
    {
        if (string.IsNullOrEmpty(taskPath)) return null;
        return _store.FindEntry(e =>
            e.Location == EntryLocation.TaskScheduler &&
            string.Equals(e.LocationDetail, taskPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Получает все задачи рекурсивно, включая скрытые (Hidden = true).</summary>
    private List<Microsoft.Win32.TaskScheduler.Task>? GetAllTasksWithHidden()
    {
        try
        {
            var ts = GetOrCreateTaskService();
            using var root = ts.GetFolder("\\");
            if (root == null) return null;

            var result = new List<Microsoft.Win32.TaskScheduler.Task>();
            CollectTasks(root, result);
            return result;
        }
        catch
        {
            try { _taskService?.Dispose(); } catch { }
            _taskService = null;
            // Fallback: AllTasks (возвращает только видимые)
            try
            {
                var ts = GetOrCreateTaskService();
                return ts.AllTasks?.ToList();
            }
            catch { return null; }
        }
    }

    /// <summary>Рекурсивно собирает все задачи из папки и подпапок.</summary>
    private static void CollectTasks(Microsoft.Win32.TaskScheduler.TaskFolder folder, List<Microsoft.Win32.TaskScheduler.Task> result)
    {
        try
        {
            foreach (var task in folder.Tasks)
                result.Add(task);

            foreach (var subFolder in folder.SubFolders)
            {
                try
                {
                    CollectTasks(subFolder, result);
                }
                finally
                {
                    try { subFolder.Dispose(); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>Извлекает команду из действия задачи планировщика (ExecAction).</summary>
    private static string FormatTaskAction(Microsoft.Win32.TaskScheduler.Task task)
    {
        try
        {
            var action = task.Definition.Actions.FirstOrDefault();
            if (action is Microsoft.Win32.TaskScheduler.ExecAction exec)
            {
                var cmd = exec.Path?.Trim('"') ?? "";
                var args = exec.Arguments?.Trim() ?? "";
                return string.IsNullOrEmpty(args) ? cmd : $"{cmd} {args}";
            }
        }
        catch { }
        return task.Name;
    }
}
