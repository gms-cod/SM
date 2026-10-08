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
    private readonly List<RegistryKey> _watchedKeys = [];

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
                        _blacklist.ApplyBlacklistAction(entry, matched, _store, _notifications);
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

    /// <summary>Запускает цикл уведомлений об изменениях ключа реестра через P/Invoke.</summary>
    private void StartRegNotify(SafeHandle regHandle, string keyPath, RegistryHive hive, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Блокирующий режим: вызов возвращается при изменении ключа или при закрытии handle.
                    // (async-режим с hEvent=IntPtr.Zero возвращал ERROR_INVALID_HANDLE и крутил цикл вхолостую)
                    var result = RegNotifyChangeKeyValue(
                        regHandle,
                        watchSubtree: true,
                        notifyFilter: RegNotifyFilter.NAME | RegNotifyFilter.VALUE | RegNotifyFilter.SET_VALUE,
                        fAsynchronous: false,
                        hEvent: IntPtr.Zero,
                        fImmediate: false);

                    if (ct.IsCancellationRequested) break;

                    if (result == 0)
                    {
                        if (_coordinator.IsPaused) continue;
                        await Task.Delay(500, ct);
                        _logger.LogInformation("Registry change detected: {Key}", keyPath);
                        if (hive == RegistryHive.LocalMachine)
                        {
                            ScanHive(Registry.LocalMachine, "HKLM", allowWow64: true);
                        }
                        else
                        {
                            ScanHive(Registry.CurrentUser, "HKCU", allowWow64: false);
                            foreach (var (sid, name) in WatcherHelper.GetUserProfiles())
                            {
                                using var userKey = Registry.Users.OpenSubKey(sid);
                                if (userKey == null) continue;
                                ScanHive(userKey, $@"HKCU\{name}", allowWow64: false);
                            }
                        }
                    }
                    else
                    {
                        // Ошибка (напр. handle закрыт при остановке) — не жечь CPU в цикле
                        await Task.Delay(1000, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Registry notification error for {Key}", keyPath);
                    await Task.Delay(5000, ct);
                }
            }
        }, ct);
    }

    /// <summary>Остановка: закрываем ключи, чтобы разблокировать RegNotifyChangeKeyValue.</summary>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Закрытие ключей разблокирует RegNotifyChangeKeyValue в потоках наблюдения
        foreach (var key in _watchedKeys)
            try { key.Dispose(); } catch { }
        _watchedKeys.Clear();
        return base.StopAsync(cancellationToken);
    }

    /// <summary>P/Invoke для отслеживания изменений в реестре.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        RegNotifyFilter notifyFilter,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous,
        IntPtr hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fImmediate);

    /// <summary>Фильтры уведомлений реестра.</summary>
    [Flags]
    private enum RegNotifyFilter
    {
        NAME = 1,
        VALUE = 2,
        SET_VALUE = 4,
    }
}
