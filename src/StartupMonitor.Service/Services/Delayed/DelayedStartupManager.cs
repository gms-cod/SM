using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.Delayed;

/// <summary>
/// Управляет отложенным запуском программ (Delayed Startup):
/// - вынесение программ из раннего автозапуска Windows для ускорения старта системы;
/// - запуск программ в пользовательской сессии через заданный интервал времени;
/// - ручной тестовый запуск и отмена задержки с возвратом в автозапуск.
/// </summary>
public class DelayedStartupManager : IHostedService
{
    private readonly JsonDataStore _store;
    private readonly ILogger<DelayedStartupManager> _logger;
    private readonly NotificationService _notifications;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _scheduledTasks = new();
    private readonly ConcurrentDictionary<string, DateTime> _launchedThisSession = new();

    public DelayedStartupManager(
        JsonDataStore store,
        ILogger<DelayedStartupManager> logger,
        NotificationService notifications)
    {
        _store = store;
        _logger = logger;
        _notifications = notifications;
    }

    /// <summary>StartAsync: Выполняет операцию StartAsync.</summary>
    /// <param name="cancellationToken">Входные данные: значение параметра cancellationToken (CancellationToken).</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Инициализация менеджера отложенного запуска (Delayed Startup)");
        ScheduleAllDelayedEntries();
        return Task.CompletedTask;
    }

    /// <summary>StopAsync: Выполняет операцию StopAsync.</summary>
    /// <param name="cancellationToken">Входные данные: значение параметра cancellationToken (CancellationToken).</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Остановка менеджера отложенного запуска");
        CancelAllScheduled();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Переводит запись в режим отложенного запуска с указанным интервалом в секундах.
    /// Физически отключает стандартный автозапуск в системе, чтобы Windows не запускала программу при входе.
    /// </summary>
    public (bool Success, string? Error) SetDelayed(string entryId, int delaySeconds)
    {
        var cfg = _store.Load();
        var entry = cfg.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null)
        {
            return (false, "Запись не найдена в базе данных.");
        }

        if (entry.Status == EntryStatus.Blocked)
        {
            return (false, "Запись заблокирована Чёрным списком. Сначала снимите блокировку.");
        }

        if (EntryOperations.IsSelfEntry(entry) || SystemProtectionHelper.IsProtectedSystemEntry(entry))
        {
            return (false, "Защищённые системные и служебные компоненты Windows нельзя переводить в отложенный запуск.");
        }

        int clampedDelay = Math.Clamp(delaySeconds, 5, 600);

        // Если запись активна в системе — временно отключаем её физический автозапуск
        if (entry.Status == EntryStatus.Enabled)
        {
            var (disOk, disErr) = EntryOperations.DisableFromSystem(entry);
            if (!disOk && !string.IsNullOrEmpty(disErr))
            {
                _logger.LogWarning("Предупреждение при отключении автозапуска для отложенного режима '{Name}': {Error}",
                    entry.Name, disErr);
            }
        }

        entry.Status = EntryStatus.Delayed;
        entry.DelaySeconds = clampedDelay;
        _store.UpdateEntry(entry);

        _store.AddHistory(new HistoryEntry
        {
            EntryName = entry.Name,
            EntryPath = entry.FilePath,
            Location = entry.Location.ToString(),
            ChangeType = "DelayedSet",
            Action = $"Установлен отложенный запуск: задержка {clampedDelay} сек.",
            Timestamp = DateTime.UtcNow
        });

        // Планируем таймер запуска
        ScheduleEntry(entry);

        _notifications.Enqueue(new PipeMessage
        {
            Type = PipeMessageTypes.ActionNotice,
            Payload = JsonSerializer.Serialize(new ActionNoticePayload(entry.Name, "delayedSet"))
        });

        return (true, null);
    }

    /// <summary>
    /// Отменяет режим отложенного запуска и восстанавливает стандартный автозапуск в системе.
    /// </summary>
    public (bool Success, string? Error) CancelDelayed(string entryId)
    {
        var cfg = _store.Load();
        var entry = cfg.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null)
        {
            return (false, "Запись не найдена в базе данных.");
        }

        if (entry.Status != EntryStatus.Delayed)
        {
            return (false, "Запись не находится в режиме отложенного запуска.");
        }

        // Отменяем таймер
        CancelScheduled(entry.Id);

        // Восстанавливаем в системе
        var (enOk, enErr) = EntryOperations.EnableFromSystem(entry);
        if (!enOk && !string.IsNullOrEmpty(enErr))
        {
            _logger.LogWarning("Предупреждение при восстановлении автозапуска для '{Name}': {Error}",
                entry.Name, enErr);
        }

        entry.Status = EntryStatus.Enabled;
        _store.UpdateEntry(entry);

        _store.AddHistory(new HistoryEntry
        {
            EntryName = entry.Name,
            EntryPath = entry.FilePath,
            Location = entry.Location.ToString(),
            ChangeType = "DelayedCanceled",
            Action = "Отложенный запуск отменён, программа возвращена в обычный автозапуск.",
            Timestamp = DateTime.UtcNow
        });

        _notifications.Enqueue(new PipeMessage
        {
            Type = PipeMessageTypes.ActionNotice,
            Payload = JsonSerializer.Serialize(new ActionNoticePayload(entry.Name, "delayedCanceled"))
        });

        return (true, null);
    }

    /// <summary>
    /// Немедленно выполняет тестовый запуск отложенной программы.
    /// </summary>
    public (bool Success, string Message) LaunchNow(string entryId)
    {
        var cfg = _store.Load();
        var entry = cfg.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null)
        {
            return (false, "Запись не найдена в базе данных.");
        }

        var (ok, msg, pid) = UserSessionLauncher.Launch(entry.FilePath, entry.CommandLine, _logger);
        if (ok)
        {
            _launchedThisSession[entry.Id] = DateTime.UtcNow;
            _store.AddHistory(new HistoryEntry
            {
                EntryName = entry.Name,
                EntryPath = entry.FilePath,
                Location = entry.Location.ToString(),
                ChangeType = "DelayedManualLaunch",
                Action = $"Выполнен ручной запуск программы (PID {pid})",
                Timestamp = DateTime.UtcNow
            });

            _notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.ActionNotice,
                Payload = JsonSerializer.Serialize(new ActionNoticePayload(entry.Name, "delayedManualLaunch"))
            });

            return (true, $"Программа '{entry.Name}' успешно запущена (PID {pid})");
        }

        return (false, $"Не удалось запустить '{entry.Name}': {msg}");
    }

    /// <summary>
    /// Инициализирует и запускает таймеры для всех отложенных записей при старте службы.
    /// </summary>
    public void ScheduleAllDelayedEntries()
    {
        var cfg = _store.Load();
        var delayedEntries = cfg.Entries.Where(e => e.Status == EntryStatus.Delayed).ToList();

        if (delayedEntries.Count == 0)
        {
            _logger.LogInformation("Отложенных записей автозапуска не обнаружено");
            return;
        }

        _logger.LogInformation("Найдено {Count} отложенных записей. Запуск планировщика...", delayedEntries.Count);
        foreach (var entry in delayedEntries)
        {
            ScheduleEntry(entry);
        }
    }

    /// <summary>ScheduleEntry: Выполняет операцию ScheduleEntry.</summary>
    /// <param name="entry">Входные данные: значение параметра entry (StartupEntry).</param>
    private void ScheduleEntry(StartupEntry entry)
    {
        CancelScheduled(entry.Id);

        var cts = new CancellationTokenSource();
        _scheduledTasks[entry.Id] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var delay = TimeSpan.FromSeconds(Math.Max(5, entry.DelaySeconds));
                _logger.LogInformation("Отложенный запуск для '{Name}' запланирован через {Delay} сек",
                    entry.Name, entry.DelaySeconds);

                await Task.Delay(delay, cts.Token);

                // Повторно проверяем статус записи перед фактическим запуском
                var current = _store.Load().Entries.FirstOrDefault(e => e.Id == entry.Id);
                if (current == null || current.Status != EntryStatus.Delayed)
                {
                    _logger.LogInformation("Отложенный запуск для '{Name}' отменён (статус изменился)", entry.Name);
                    return;
                }

                var (ok, msg, pid) = UserSessionLauncher.Launch(current.FilePath, current.CommandLine, _logger);
                if (ok)
                {
                    _launchedThisSession[current.Id] = DateTime.UtcNow;
                    _logger.LogInformation("Отложенный запуск '{Name}' успешно произведён (PID {Pid})", current.Name, pid);

                    _store.AddHistory(new HistoryEntry
                    {
                        EntryName = current.Name,
                        EntryPath = current.FilePath,
                        Location = current.Location.ToString(),
                        ChangeType = "DelayedLaunch",
                        Action = $"Программа запущена по расписанию через {current.DelaySeconds}с (PID {pid})",
                        Timestamp = DateTime.UtcNow
                    });

                    _notifications.Enqueue(new PipeMessage
                    {
                        Type = PipeMessageTypes.ActionNotice,
                        Payload = JsonSerializer.Serialize(new ActionNoticePayload(current.Name, "delayedLaunched"))
                    });
                }
                else
                {
                    _logger.LogWarning("Не удалось запустить отложенную программу '{Name}': {Message}", current.Name, msg);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Таймер отложенного запуска для '{Name}' отменён", entry.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Исключение при выполнении отложенного запуска '{Name}'", entry.Name);
            }
            finally
            {
                _scheduledTasks.TryRemove(entry.Id, out _);
                cts.Dispose();
            }
        });
    }

    /// <summary>CancelScheduled: Выполняет операцию CancelScheduled.</summary>
    /// <param name="entryId">Входные данные: значение параметра entryId (string).</param>
    private void CancelScheduled(string entryId)
    {
        if (_scheduledTasks.TryRemove(entryId, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
    }

    /// <summary>CancelAllScheduled: Выполняет операцию CancelAllScheduled.</summary>
    private void CancelAllScheduled()
    {
        foreach (var key in _scheduledTasks.Keys.ToList())
        {
            CancelScheduled(key);
        }
    }
}

