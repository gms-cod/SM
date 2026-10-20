using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Watcher для стандартных точек автозапуска в реестре (Run, RunOnce, RunServices, Policies).
/// Выполняет начальное сканирование и отслеживает изменения через RegNotifyChangeKeyValue.</summary>
public class RegistryWatcher : BackgroundService
{
    /// <summary>Хранилище записей автозапуска.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Менеджер чёрного списка.</summary>
    private readonly BlacklistManager _blacklist;
    /// <summary>Сервис уведомлений для отправки в UI.</summary>
    private readonly NotificationService _notifications;
    /// <summary>Логгер.</summary>
    private readonly ILogger<RegistryWatcher> _logger;
    /// <summary>Трекер начального сканирования.</summary>
    private readonly InitialScanTracker _tracker;
    /// <summary>Список открытых ключей реестра (удерживаем, чтобы GC не закрыл handle).</summary>
    /// <summary>Список открытых ключей реестра (удерживаем, чтобы GC не закрыл handle).</summary>
    private readonly List<RegistryKey> _watchedKeys = [];

    /// <summary>Таймер отложенного агрегированного сканирования (Debounce 500ms) для защиты от шторма событий.</summary>
    private readonly Timer _debounceTimer;
    /// <summary>Атомарный индикатор выполнения активного сканирования (0 - свободно, 1 - занято).</summary>
    private int _isScanning = 0;
    /// <summary>Флаг необходимости повторного прохода, если во время сканирования поступили новые события.</summary>
    private int _rescanRequired = 0;

    /// <summary>Пути ключей реестра для стандартных точек автозапуска.</summary>
    private static readonly string[] WatchKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
        @"Software\Microsoft\Windows\CurrentVersion\RunServices",
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\RunServices",
        @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
    ];

    /// <summary>Dispose: Освобождает занятые ресурсы объекта.</summary>
    public override void Dispose()
    {
        try { _debounceTimer?.Dispose(); } catch { }
        foreach (var key in _watchedKeys)
            key?.Dispose();
        _watchedKeys.Clear();
        base.Dispose();
    }

    private readonly MonitoringStateCoordinator _coordinator;

    public RegistryWatcher(
        JsonDataStore store,
        BlacklistManager blacklist,
        NotificationService notifications,
        ILogger<RegistryWatcher> logger,
        InitialScanTracker tracker,
        MonitoringStateCoordinator coordinator)
    {
        _store = store;
        _blacklist = blacklist;
        _notifications = notifications;
        _logger = logger;
        _tracker = tracker;
        _coordinator = coordinator;
        _debounceTimer = new Timer(OnDebounceTimerFired, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Основной цикл: начальное сканирование → наблюдение.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RegistryWatcher started");
        _tracker.CurrentStage = "Сканирование реестра...";
        await InitialScan(stoppingToken);
        _tracker.MarkCompleted();
        StartWatching(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>Начальное сканирование: HKLM + HKCU + все пользовательские кусты.</summary>
    private Task InitialScan(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            try
            {
                ScanHive(Registry.LocalMachine, "HKLM", allowWow64: true, isInitial: true);
                ScanHive(Registry.CurrentUser, "HKCU", allowWow64: false, isInitial: true);
                foreach (var (sid, name) in WatcherHelper.GetUserProfiles())
                {
                    using var userKey = Registry.Users.OpenSubKey(sid);
                    if (userKey == null) continue;
                    ScanHive(userKey, $@"HKCU\{name}", allowWow64: false, isInitial: true);
                }
                _store.DeduplicateEntries();
                _logger.LogInformation("Registry initial scan complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registry initial scan failed");
            }
        }, ct);
    }

    /// <summary>Сканирует базовый ключ реестра: читает значения, обогащает записи, проверяет ЧС.</summary>
    private void ScanHive(RegistryKey baseKey, string hivePrefix, bool allowWow64, bool isInitial = false)
    {
        using var _profRegistry = ExecutionProfiler.Profile("Service.RegistryScan");
        foreach (var keyPath in WatchKeys)
        {
            // Wow6432Node есть только в HKLM
            if (!allowWow64 &&
                keyPath.Contains("Wow6432Node", StringComparison.OrdinalIgnoreCase))
                continue;

            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) continue;

            foreach (var valueName in key.GetValueNames())
            {
                var raw = key.GetValue(valueName);
                if (raw is not string value || string.IsNullOrWhiteSpace(value)) continue;
                // Мусорные значения (числа, "yes", CLSID) не попадают в автозапуск
                if (!WatcherHelper.LooksLikeCommand(value)) continue;
                var filePath = WatcherHelper.ParsePath(value);

                var loc = WatcherHelper.GetLocationFromKeyPath(keyPath);

                var entry = new StartupEntry
                {
                    Name = valueName,
                    FilePath = filePath,
                    CommandLine = value,
                    Location = loc,
                    LocationDetail = $@"{hivePrefix}\{keyPath}\{valueName}",
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
                        var existingBlocked = _store.FindEntry(e => e.Id == entry.Id);
                        // Защита от спама: если запись уже заблокирована в хранилище и параметры командной строки
                        // не изменились, повторное воздействие и спам алертами пропускаются
                        if (existingBlocked == null || existingBlocked.CommandLine != entry.CommandLine || !existingBlocked.FromBlacklist)
                        {
                            _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                        }
                        continue;
                    }
                    entry.Status = EntryStatus.Blocked;
                    entry.FromBlacklist = true;
                }

                // Добавляем/обновляем в хранилище
                if (!WatcherHelper.AddOrUpdate(_store, _notifications, entry, matched, _blacklist)) continue;

                // Запись из ЧС снова появилась — автоматически останавливаем процесс и удаляем (с предупреждением)
                if (matched != null && !isInitial)
                {
                    _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
                    continue;
                }

                // Уведомляем UI о новой записи
                _notifications.Enqueue(new PipeMessage
                {
                    Type = PipeMessageTypes.EntryAdded,
                    Payload = System.Text.Json.JsonSerializer.Serialize(entry)
                });
            }
        }
    }

    /// <summary>Запускает фоновое наблюдение за ключами реестра (HKLM + HKCU).</summary>
    private void StartWatching(CancellationToken ct)
    {
        // HKLM
        foreach (var keyPath in WatchKeys)
        {
            try
            {
                var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
                if (key == null) continue;
                _watchedKeys.Add(key);
                StartRegNotify(key.Handle, keyPath, RegistryHive.LocalMachine, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to watch registry key: {Key}", keyPath);
            }
        }

        // HKCU (без Wow6432Node — их нет в HKCU)
        foreach (var keyPath in WatchKeys)
        {
            if (keyPath.Contains("Wow6432Node", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
                if (key == null) continue;
                _watchedKeys.Add(key);
                StartRegNotify(key.Handle, keyPath, RegistryHive.CurrentUser, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to watch registry key: {Key}", keyPath);
            }
        }

        // HKEY_USERS: профили реальных пользователей (служба работает как LocalSystem)
        foreach (var (sid, name) in WatcherHelper.GetUserProfiles())
        {
            foreach (var keyPath in WatchKeys)
            {
                if (keyPath.Contains("Wow6432Node", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var key = Registry.Users.OpenSubKey($@"{sid}\{keyPath}", writable: false);
                    if (key == null) continue;
                    _watchedKeys.Add(key);
                    StartRegNotify(key.Handle, keyPath, RegistryHive.CurrentUser, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to watch user registry key: {Sid} / {Key}", sid, keyPath);
                }
            }
        }
    }

    /// <summary>
    /// Контекст асинхронной подписки на ключ реестра без блокировки системных потоков ThreadPool.
    /// </summary>
    private sealed class WatchedKeySubscription : IDisposable
    {
        public SafeHandle RegHandle { get; }
        public string KeyPath { get; }
        public RegistryHive Hive { get; }
        public AutoResetEvent NotifyEvent { get; } = new(false);
        public RegisteredWaitHandle? WaitHandle { get; set; }

        public WatchedKeySubscription(SafeHandle regHandle, string keyPath, RegistryHive hive)
        {
            RegHandle = regHandle;
            KeyPath = keyPath;
            Hive = hive;
        }

        public void Dispose()
        {
            try { WaitHandle?.Unregister(null); } catch { }
            try { NotifyEvent.Dispose(); } catch { }
        }
    }

    /// <summary>Список активных асинхронных подписок на ключи реестра.</summary>
    private readonly List<WatchedKeySubscription> _subscriptions = [];

    /// <summary>Запускает асинхронную регистрацию уведомлений через пул потоков Windows без постоянного удержания потока.</summary>
    private void StartRegNotify(SafeHandle regHandle, string keyPath, RegistryHive hive, CancellationToken ct)
    {
        var sub = new WatchedKeySubscription(regHandle, keyPath, hive);
        lock (_subscriptions)
        {
            _subscriptions.Add(sub);
        }

        // Первичное взведение асинхронного уведомления в ядре Windows
        ArmWatcher(sub, ct);
    }

    /// <summary>Взводит асинхронное уведомление ядра Windows и регистрирует дескриптор события в ThreadPool.</summary>
    private void ArmWatcher(WatchedKeySubscription sub, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || sub.RegHandle.IsClosed || sub.RegHandle.IsInvalid)
            return;

        // Каноничный Win32 P/Invoke: hEvent передается 4-м параметром, fAsynchronous: true — 5-м
        var status = RegNotifyChangeKeyValue(
            sub.RegHandle,
            watchSubtree: true,
            notifyFilter: RegNotifyFilter.NAME | RegNotifyFilter.VALUE | RegNotifyFilter.SET_VALUE,
            hEvent: sub.NotifyEvent.SafeWaitHandle,
            fAsynchronous: true);

        if (status != 0)
        {
            _logger.LogDebug("RegNotifyChangeKeyValue вернул статус {Status} для {Key}", status, sub.KeyPath);
            return;
        }

        // Регистрируем ожидание события в системном пуле потоков (0 заблокированных потоков)
        sub.WaitHandle = ThreadPool.RegisterWaitForSingleObject(
            sub.NotifyEvent,
            OnRegistryKeyChanged,
            sub,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    /// <summary>Коллбек системного пула потоков при срабатывании изменения в ключе реестра (Zero-Crash изоляция P0-04).</summary>
    private void OnRegistryKeyChanged(object? state, bool timedOut)
    {
        try
        {
            if (state is not WatchedKeySubscription sub) return;

            // Немедленно перевзводим наблюдение для непрерывного перехвата следующих изменений
            try
            {
                ArmWatcher(sub, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка перевзвода подписки реестра для {Key}", sub.KeyPath);
            }

            if (_coordinator.IsPaused) return;

            // Агрегация через Debounce таймер для подавления шторма событий (P1-06)
            TriggerDebouncedScan();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Необработанная ошибка в пуле потоков RegistryWatcher (Zero-Crash защита)");
        }
    }

    /// <summary>Сбрасывает таймер агрегации (500 мс) для объединения пачки изменений в единый проход.</summary>
    private void TriggerDebouncedScan()
    {
        try
        {
            _debounceTimer?.Change(500, Timeout.Infinite);
        }
        catch { }
    }

    /// <summary>Коллбек таймера агрегации изменений реестра.</summary>
    private void OnDebounceTimerFired(object? state)
    {
        _ = RunSingleFlightScanAsync();
    }

    /// <summary>
    /// Выполняет единственный проход сканирования (Single-Flight). Если в процессе сканирования поступили
    /// новые события, выполняет строго один дополнительный финальный проход.
    /// </summary>
    private async Task RunSingleFlightScanAsync()
    {
        if (Interlocked.CompareExchange(ref _isScanning, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _rescanRequired, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _rescanRequired, 0);
                await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogInformation("Агрегированное сканирование реестра начато");
                        ScanHive(Registry.LocalMachine, "HKLM", allowWow64: true);
                        ScanHive(Registry.CurrentUser, "HKCU", allowWow64: false);
                        try
                        {
                            foreach (var (sid, name) in WatcherHelper.GetUserProfiles())
                            {
                                using var userKey = Registry.Users.OpenSubKey(sid);
                                if (userKey == null) continue;
                                ScanHive(userKey, $@"HKCU\{name}", allowWow64: false);
                            }
                        }
                        catch (Exception exProf)
                        {
                            _logger.LogWarning(exProf, "Не удалось опросить профили пользователей");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка агрегированного сканирования реестра");
                    }
                });
            } while (Interlocked.CompareExchange(ref _rescanRequired, 0, 0) != 0);
        }
        finally
        {
            Interlocked.Exchange(ref _isScanning, 0);
            // Ликвидация lost-wakeup: если запрос на рескан поступил в момент завершения цикла, перезапускаем скан
            if (Interlocked.Exchange(ref _rescanRequired, 0) != 0)
            {
                _ = RunSingleFlightScanAsync();
            }
        }
    }

    /// <summary>Остановка: закрываем ключи и отменяем подписки в пуле потоков.</summary>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_subscriptions)
        {
            foreach (var sub in _subscriptions)
                sub.Dispose();
            _subscriptions.Clear();
        }

        foreach (var key in _watchedKeys)
            try { key.Dispose(); } catch { }
        _watchedKeys.Clear();
        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// P/Invoke для регистрации асинхронных уведомлений об изменениях в реестре Windows.
    /// Каноническая сигнатура Win32 API:
    /// LSTATUS RegNotifyChangeKeyValue(
    ///   HKEY hKey,
    ///   BOOL bWatchSubtree,
    ///   DWORD dwNotifyFilter,
    ///   HANDLE hEvent,
    ///   BOOL fAsynchronous
    /// );
    /// </summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        RegNotifyFilter notifyFilter,
        SafeHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    /// <summary>Фильтры уведомлений реестра.</summary>
    [Flags]
    private enum RegNotifyFilter
    {
        NAME = 1,
        VALUE = 2,
        SET_VALUE = 4,
    }
}
