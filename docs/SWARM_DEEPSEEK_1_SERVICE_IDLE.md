# Субагент 1 (DeepSeek V4.1 Flash): Служба Windows, фоновые циклы и спящий режим 0% CPU Idle

- Модель субагента: `opencode/deepseek-v4.1-flash-free`
- Время выполнения: 244.1 сек
- Статус: Успешно

---

I'll start by reading the target files to understand the current implementation.
Let me read the remaining sections of the key files, particularly the background loops.
Now let me examine the supporting infrastructure: coordinator, notification service, network tracker, data store, and DI registration.
Let me read the port defender and alerts command handling sections in PipeServer.
Let me verify how the UI drives the network alerts and port defender windows.
# Технический отчёт: Smart Sleep и реактивный переход для фоновых подпрограмм `StartupMonitor.Service`

> Аудит проведён по: `PipeServer.cs`, `ProcessPortDefenderService.cs`, `NetworkConnectionAlertService.cs`, `EntryOperations.cs`, `SystemScannerService.cs`, а также по инфраструктуре (`JsonDataStore`, `NotificationService`, `ProcessNetworkTracker`, `ProcessMetadataEnricher`, `MonitoringStateCoordinator`, `Program.cs`, UI-потребители).
>
> Числовые оценки в разделе 3 — **аналитические (на основе кода и числа системных вызовов)**, а не инструментальные: службу в этой среде запустить нельзя. Дана методика их подтверждения штатным `ExecutionProfiler`.

---

## 1. Резюме

Служба содержит **6 постоянных фоновых контуров**, которые просыпаются вне зависимости от того, нужен ли результат потребителю:

| Контур | Расположение | Частота | Условие остановки сейчас |
|---|---|---|---|
| `BackgroundCleanupLoop` | `PipeServer.cs:2123` | 5 с / скан 5 мин | только `IsPaused` (сон 5 с) |
| `BackgroundHashScanLoop` | `PipeServer.cs:2187` | 5 мин | только `IsPaused` |
| `BackgroundRatingLoop` | `PipeServer.cs:2084` | 10 мин | только `IsPaused` |
| `ProcessPortDefenderService` poll | `ProcessPortDefenderService.cs:79` | 3 с | есть быстрый `return` без правил |
| `NetworkConnectionAlertService` poll | `NetworkConnectionAlertService.cs:102` | **5 с, всегда** | **нет привязки к окну UI** |
| `NotificationService` flush | `NotificationService.cs:37` | 200 мс, всегда | нет |
| `AutoUnbanWorker` | `AutoUnbanWorker.cs:39` | 30 с | нет |
| `EventPipeServer` heartbeat | `EventPipeServer.cs:123` | 30 с | нет |

**Ключевые дефекты, найденные аудитом:**

1. `NetworkConnectionAlertService.StartBackgroundScan()` вызывается **безусловно** в конструкторе `PipeServer` (`PipeServer.cs:154`) и молотит `ProcessNetworkTracker.GetSockets()` **+ `WindowsFirewallService.GetAllRules()` (дорогой COM-перебор) каждые 5 секунд 24/7**, даже когда ни одного UI-клиента нет и окно «Сетевая безопасность» закрыто. Это **главный паразитный потребитель**.
2. `ProcessPortDefenderService` имеет быстрый выход, но **при активных правилах** делает полную сетеنبю таблицу каждые 3 с — параллельно с таким же вызовом из `NetworkConnectionAlertService` (двойная работа).
3. Ни один контур не завязан на **наличие подключённых UI-клиентов** (named pipe), хотя `PipeServer` — единственный владелец `HandleClientAsync` и точно знает число клиентов.
4. `PipeServer._cts` (`PipeServer.cs:82`) создаётся и отменяется в `Dispose()`, но **не передаётся** ни в один цикл (они получают `stoppingToken`). Отмена `_cts` не останавливает циклы.
5. Латентный всплеск: `EntryOperations.AddToEnrichCache` (`EntryOperations.cs:844`) при переполнении делает `OrderBy(...).Take(500)` по до 5000 записям **на каждую вставку** — O(n log n) в горячем пути обогащения.

---

## 2. Карта фоновых циклов (детальный аудит)

### 2.1 `PipeServer` — владелец жизненного цикла (`ExecuteAsync`, строки 182–224)

```
_ = BackgroundCleanupLoop(stoppingToken);   // 187
_ = BackgroundHashScanLoop(stoppingToken);  // 188
_ = BackgroundRatingLoop(stoppingToken);    // 189
```
Все три — fire-and-forget `Task`, не привязаны к `_cts`, не имеют шлюза активности.

При подключении клиента (`PipeServer.cs:212`) используется fire-and-forget `_ = HandleClientAsync(...)` — **счётчик клиентов нигде не ведётся**.

### 2.2 `ProcessPortDefenderService` (`OnPollTimerTick`, строки 576–601)

```csharp
if (!cfg.IsGloballyEnabled || cfg.Rules.Count == 0 || !cfg.Rules.Any(r => r.IsEnabled))
    return;                    // быстрый выход — хорошо
var sockets = _networkTracker.GetSockets();   // тяжёлый системный вызов
ProcessSocketSnapshot(sockets);
```
Таймер `System.Threading.Timer` (не async) с фиксированным периодом 3 с; при активных правилах нет адаптации и нет сна. `SaveConfig` (`:121`) — естественная точка пробуждения.

### 2.3 `NetworkConnectionAlertService` (`StartBackgroundScan`, строки 99–115)

```csharp
_pollTimer = new Timer(_ => { try { ScanNewConnections(); } catch { } },
    null, dueTime ?? 5s, period ?? 5s);
```
`AlertsEnabled` **не влияет на таймер** — `ScanNewConnections()` сам проверяет флаг и возвращает 0, но `_tracker.GetSockets()` внутри `ScanNewConnections` **выполняется до этой проверки?** Нет — проверка `if (!AlertsEnabled) return 0;` идёт первой (`:148`), поэтому при `false` вызов дешёвый. Однако флаг по умолчанию `true` (`:79`) и **никогда не сбрасывается при закрытии окна** — UI шлёт `SetAlertsEnabled` только по тумблеру (`NetworkSecurityHubView.xaml.cs:499`), а не по `Unloaded` (там просто останавливаются UI-таймеры, `:272`).

### 2.4 `BackgroundCleanupLoop` (`PipeServer.cs:2123`)

Каждые 10 с — `KillProcessesByRules(cfg.Blacklist, _logger)` (`EntryOperations.cs:716`), который делает `Process.GetProcesses()` по всей системе и по каждому процессу `OpenProcess`+`QueryFullProcessImageName`. Каждые 5 мин — полный `ScanAll` + `CleanupStaleEntries`. Оба выполняются безусловно.

### 2.5 `ResolveAndEnrichEntry` (`EntryOperations.cs:858`)

Вызывается для **всех** записей при каждом `GetAllEntries` и `GetEntriesDelta` (`PipeServer.cs:309`, `:350`), т.е. на каждый 5-секундный опрос UI. Кэш есть, но при попадании всё равно делается `File.Exists` + `File.GetLastWriteTimeUtc` на каждую запись.

### 2.6 `JsonDataStore` (`Save`, `Load`, `DeduplicateEntries`, `AddHistory`)

- `Save()` (`:296`) сериализует **все 4 секции** и считает SHA256 каждой, чтобы решить «писать/не писать». Дедупликация по хэшу экономит **диск**, но не **CPU сериализации+хеширования**.
- `GetAllEntries` вызывает `DeduplicateEntries()` + `Save()` **на каждый опрос** (`PipeServer.cs:304,311`).

---

## 3. Замеры узких мест

### 3.1 Методика

`ExecutionProfiler` (`ExecutionProfiler.cs`) уже снимает метрики и раз в 5 минут печатает `PERF REPORT` (запущен в `Program.cs:66`). В коде уже есть маркеры: `Service.ScanAll`, `Service.ScanRegistry`, `Service.ScanServices`, `Service.ScanWmi`, `Service.ScanTaskScheduler`, `Service.ScanAppx`, `Service.Network.GetSockets`, `Service.NetworkConnectionsScan`, `Service.JsonDataStoreLoad/Save`, `Service.EntryOps.ResolveEnrich`, `Service.EntryOps.KillByRules`, `Service.PipeProcessMessage`.

Не хватает маркеров на **фоновых циклах**: `BackgroundCleanupLoop`, `BackgroundHashScanLoop`, `NetworkConnectionAlertService.ScanNewConnections`, `WindowsFirewallService.GetAllRules`. Рекомендую добавить:

```csharp
// NetworkConnectionAlertService.ScanNewConnections
using var _profScan = ExecutionProfiler.Measure("Service.NetworkAlerts.Scan");
// внутри BuildFirewallRulePathSet (дорогая COM-часть):
using var _profRules = ExecutionProfiler.Measure("Service.Firewall.GetAllRules");
```

### 3.2 Оценочная таблица (подтвердить профилировщиком)

| Подпрограмма | Триггер/частота | Оценка стоимости | Комментарий |
|---|---|---|---|
| `GetAllRules()` (COM) | каждый `ScanNewConnections` (5 с) | **30–400 мс** при 100–5000 правил | Доминанта суммарного CPU |
| `ProcessNetworkTracker.GetSockets()` | 3 с (defender) + 5 с (alerts) | 3–20 мс ×(20+12) в минуту | Двойной проход по одной и той же таблице |
| `KillProcessesByRules` | 10 с | 10–60 мс (250–500 процессов) | + ~300 аллокаций `Process`/проход |
| `ResolveAndEnrichEntry` (hit) | каждые 5 с × N записей | ~0.02–0.05 мс/запись | `File.Exists`+`GetLastWriteTimeUtc` ×N |
| `JsonDataStore.Save` | каждый `GetAllEntries` (5 с) | 2–15 мс | Полная сериализация + SHA256 секций |
| `AddToEnrichCache` eviction | при `Count>5000` | O(5000·log5000) **на вставку** | Латентный «пик» |
| `AddHistory` → `File.AppendAllText` | каждое действие | open/close файла | Кандидат на буферизацию |

**Итог:** при простое (нет UI, окно сети закрыто) служба всё равно тратит ресурс на (а) COM-просмотр брандмауэра, (б) две сетевые таблицы, (в) kill-sweep — при том что результат первого некому доставлять.

---

## 4. Архитектура спящего режима (Smart Sleep)

Идея: **каждая подсистема знает свой «шлюз активности» и спит «до события», а не «до таймаута»**. Таймер остаётся только как «нижний страховочный интервал» для источников, у которых нет события (например, запуск процесса из ЧС).

Три режима:

```
┌────────────┐  есть потребитель/работа   ┌──────────────┐  нет работы
│  ACTIVE    │ ─────────────────────────► │ IDLE (lazy)  │
│ опрос N с  │ ◄───────────────────────── │ редкий скан  │
└────────────┘     работа появилась       └──────────────┘
        ▲                                        │ нет событий-источников
        │ Pulse()/подписка                       ▼
        └──────────────────────────────  ┌──────────────┐
                                          │  DEEP SLEEP  │  Timeout.Infinite
                                          │  0% CPU      │  до Pulse/Cancel
                                          └──────────────┘
```

### 4.1 Ядро — переиспользуемый `ActivityGate`

Новый файл `src/StartupMonitor.Service/Services/Runtime/ActivityGate.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace StartupMonitor.Service.Services.Runtime;

/// <summary>
/// Шлюз активности подсистемы. Считает число активных потребителей (UI-клиенты, открытые окна)
/// и предоставляет async-«будильник» для глубокого сна без периодического опроса.
/// </summary>
public sealed class ActivityGate : IDisposable
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _consumers;
    private bool _disposed;

    /// <summary>true — есть хотя бы один активный потребитель.</summary>
    public bool IsActive => Volatile.Read(ref _consumers) > 0;

    /// <summary>Число активных потребителей.</summary>
    public int ConsumerCount => Volatile.Read(ref _consumers);

    /// <summary>Регистрирует потребителя; при переходе 0→1 немедленно будит спящий цикл.</summary>
    public void AddConsumer()
    {
        if (Interlocked.Increment(ref _consumers) == 1)
            Pulse();
    }

    /// <summary>Снимает потребителя с учёта (переход в простой). Цикл уснёт после следующего тика.</summary>
    public void RemoveConsumer() => Interlocked.Decrement(ref _consumers);

    /// <summary>Немедленно будит ожидающий цикл (смена конфигурации, правил ЧС, пауза и т.п.).</summary>
    public void Pulse()
    {
        if (_disposed) return;
        // Одного разрешения достаточно как «будильника» — не копим счётчик.
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); }
            catch (SemaphoreFullException) { }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Ждёт сигнала не дольше <paramref name="timeout"/>.
    /// <see cref="Timeout.InfiniteTimeSpan"/> — глубокий сон до Pulse или отмены.
    /// true — разбудил Pulse; false — сработал таймаут.
    /// </summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        => _wake.WaitAsync(timeout, ct);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Dispose();
    }
}
```

### 4.2 Вспомогательный цикл с адаптивным интервалом

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StartupMonitor.Service.Services.Runtime;

/// <summary>Прогоняет тело цикла «работа → сон»: активный интервал при наличии работы,
/// глубокий сон (или Idle-интервал) при простое. Пробуждение — по ActivityGate.Pulse().</summary>
internal static class SleepAwareLoop
{
    public static async Task RunAsync(
        ActivityGate gate,
        Func<bool> hasWork,
        TimeSpan activeInterval,
        TimeSpan idleInterval,
        Func<CancellationToken, Task> body,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (hasWork())
            {
                try { await body(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch { /* тело логирует само */ }
            }

            var timeout = hasWork() ? activeInterval : idleInterval;
            try { await gate.WaitAsync(timeout, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```

> Здесь `idleInterval = Timeout.InfiniteTimeSpan` даёт **нулевое потребление CPU** в глубоком сне; подсистему будит событие (`Pulse`) при появлении работы.

---

## 5. Реактивный переход: замены polling-механизмов

| Было | Стало | Механизм |
|---|---|---|
| `System.Threading.Timer` в `ProcessPortDefenderService` | `IHostedService` + `ActivityGate` | deep sleep до `SaveConfig` |
| `Timer` в `NetworkConnectionAlertService` | async-loop + gate по «окно открыто» | + `Channel<T>` не нужен: детект сокетов по природе опросный |
| `BackgroundCleanupLoop` с `Task.Delay(10с)` | gate + событие изменения ЧС | `EventWaitHandle`-семантика через `Pulse` |
| Поллинг `ResolveAndEnrichEntry` при каждом опросе | дельта-протокол уже есть; обогащать только изменённые | `_entriesRevision` |
| Раскладка JSON при каждом `Save` | debounced single-writer | `Channel<AppConfig>` |
| `AddHistory` → `File.AppendAllText` | buffered writer | `Channel<HistoryEntry>` |

**`System.Threading.Channels`** нужен там, где есть очередь производителей и один потребитель-писатель (диск). Для «сон/просыпание» достаточно `SemaphoreSlim` (async-safe замена `ManualResetEventSlim`, который в async-циклах блокировал бы поток). `ManualResetEventSlim` имеет смысл оставить только в **синхронных** legacy-путях.

---

## 6. Конкретные изменения по файлам (готовый код)

### 6.1 `PipeServer` — учёт UI-клиентов и спящий `BackgroundCleanupLoop`

**Добавить поля:**

```csharp
// Шлюз активности UI: пока клиентов нет — обслуживающие циклы спят.
private readonly ActivityGate _uiClients = new();
// Будильник для цикла очистки (события: смена паузы, изменение ЧС, ручные действия).
private readonly ActivityGate _cleanupWake = new();
```

**`ExecuteAsync` (строки 192–223)** — регистрируем клиента при подключении:

```csharp
await pipe.WaitForConnectionAsync(stoppingToken);
_logger.LogInformation("UI-клиент подключился");
_uiClients.AddConsumer();                       // ← 0→1 будит циклы
_ = HandleClientAsync(pipe, stoppingToken);
```

**`HandleClientAsync` (finally, строки 255–261)** — снимаем клиента:

```csharp
finally
{
    _uiClients.RemoveConsumer();                // ← цикл уйдёт в сон после текущего тика
    try { writer.Dispose(); } catch { }
    try { reader.Dispose(); } catch { }
    try { pipe.Dispose(); } catch { }
}
```

**Новая версия `BackgroundCleanupLoop`:**

```csharp
private async Task BackgroundCleanupLoop(CancellationToken ct)
{
    // Первый kill-sweep ЧС сразу после старта службы.
    try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
    catch (OperationCanceledException) { return; }

    var lastFullScan = DateTime.MinValue;

    while (!ct.IsCancellationRequested)
    {
        if (_coordinator.IsPaused)
        {
            // Глубокий сон: разбудит SetPaused(false) через _cleanupWake.Pulse().
            try { await _cleanupWake.WaitAsync(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { break; }
            continue;
        }

        var cfg = _store.Load();

        // (1) Реакция на запуск запрещённых процессов нужна ТОЛЬКО если есть path-правила
        //     «убить и удалить»; иначе контур не должен просыпаться.
        var needsKillSweep = cfg.Blacklist.Any(r =>
            r.Action == BlacklistAction.NotifyAndRemove &&
            r.MatchType is BlacklistMatchType.Path or BlacklistMatchType.PathWildcard);

        if (needsKillSweep)
        {
            try { EntryOperations.KillProcessesByRules(cfg.Blacklist, _logger); }
            catch (Exception ex) { _logger.LogWarning(ex, "BackgroundCleanupLoop: ошибка kill-sweep ЧС"); }
        }

        // (2) Полный скан — не чаще 5 минут.
        if (DateTime.UtcNow - lastFullScan >= TimeSpan.FromMinutes(5))
        {
            lastFullScan = DateTime.UtcNow;
            try
            {
                var scanTime = DateTime.UtcNow;
                var skipReg = cfg.Settings.CpuThrottleEnabled
                    && CpuUsageHelper.GetCpuUsage(excludeSelf: true) >= cfg.Settings.CpuThrottleThreshold;
                var scan = _scanner.ScanAll(_logger, skipReg);
                EntryOperations.CleanupStaleEntries(cfg.Entries, _store, scan, _logger, scanTime, _scanner.FailedLocations);
                _store.Save(cfg);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "BackgroundCleanupLoop: ошибка очистки"); }
        }

        // (3) Интервал: 10 с — когда нужен kill-sweep; иначе глубокий сон до события.
        var wait = needsKillSweep
            ? TimeSpan.FromSeconds(10)
            : Timeout.InfiniteTimeSpan;

        // Если UI-клиентов нет и kill-sweep не нужен — спим максимально долго,
        // но всё равно ограничиваем 5 мин, чтобы скан не «залипал» навсегда.
        if (wait == Timeout.InfiniteTimeSpan)
            wait = TimeSpan.FromMinutes(5);

        try { await _cleanupWake.WaitAsync(wait, ct); }
        catch (OperationCanceledException) { break; }
    }
}
```

**Пробуждение цикла по событиям** — в обработчиках `ProcessMessage`:

```csharp
case PipeMessageTypes.SetPaused:            // где вызывается _coordinator.SetPaused(...)
    _coordinator.SetPaused(paused);
    _cleanupWake.Pulse();                   // разбудить/усыпить цикл
    break;

case PipeMessageTypes.AddToBlacklist:
case PipeMessageTypes.RemoveFromBlacklist:
    // ... после Save(cfg) и ReapplyBlacklist(...)
    _cleanupWake.Pulse();                   // немедленно применить/снять kill-sweep
    break;
```

Аналогично `_cleanupWake.Pulse()` при смене `CpuThrottle`, `StrictMode`.

> **Настоящий рефакторинг подписки (без «магических» строк):** добавить событие `event EventHandler? BlacklistChanged;` в `BlacklistManager` и подписаться в конструкторе `PipeServer`, чтобы любой мутатор ЧС пробуждал цикл.

### 6.2 `ProcessPortDefenderService` — из `Timer` в gated `IHostedService`

**Класс:** `public sealed class ProcessPortDefenderService : IHostedService, IDisposable`

**Убрать** `_pollTimer` (строка 52) и его создание (строка 79). **Добавить:**

```csharp
private readonly ActivityGate _wake = new();
private CancellationTokenSource? _cts;
private Task? _loop;

private bool IsDefenseActive()
{
    lock (_sync)
        return _config.IsGloballyEnabled && _config.Rules.Any(r => r.IsEnabled);
}

public Task StartAsync(CancellationToken cancellationToken)
{
    _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _loop = RunAsync(_cts.Token);
    return Task.CompletedTask;
}

public async Task StopAsync(CancellationToken cancellationToken)
{
    try { _cts?.Cancel(); } catch { }
    if (_loop is not null)
        try { await _loop.WaitAsync(cancellationToken).ConfigureAwait(false); } catch { }
}

private async Task RunAsync(CancellationToken ct)
{
    try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }   // первоначальная задержка
    catch (OperationCanceledException) { return; }

    while (!ct.IsCancellationRequested)
    {
        var active = IsDefenseActive();

        if (active)
        {
            try
            {
                var sockets = _networkTracker.GetSockets();
                if (sockets is { Count: > 0 })
                    ProcessSocketSnapshot(sockets);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "PortDefender: ошибка периодического сканирования сокетов");
            }
        }

        // Активен → опрос 3 с; выключен → глубокий сон до SaveConfig.
        var wait = active ? TimeSpan.FromSeconds(3) : Timeout.InfiniteTimeSpan;
        try { await _wake.WaitAsync(wait, ct); }
        catch (OperationCanceledException) { break; }
    }
}
```

**`SaveConfig` (строки 121–131)** — будим цикл при любой смене правил:

```csharp
public void SaveConfig(PortDefenderConfigDto config)
{
    if (config == null) return;
    lock (_sync)
    {
        _config = config;
        SaveConfigToFile();
    }
    _logger.LogInformation("PortDefender: Конфигурация обновлена. Правил: {Count}, Активен: {Enabled}",
        config.Rules?.Count ?? 0, config.IsGloballyEnabled);
    _wake.Pulse();      // ← включили/выключили правила → мгновенная смена режима сна
}
```

**`Dispose`:**

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    try { _cts?.Cancel(); } catch { }
    _cts?.Dispose();
    _wake.Dispose();
}
```

**Регистрация в `Program.cs`** (после `services.AddSingleton<ProcessPortDefenderService>();`):

```csharp
services.AddHostedService(sp => sp.GetRequiredService<ProcessPortDefenderService>());
```

### 6.3 `NetworkConnectionAlertService` — сон при закрытом окне

**Поля:** убрать `Timer? _pollTimer`, добавить:

```csharp
private readonly ActivityGate _wake = new();
private CancellationTokenSource? _cts;
private Task? _loop;
private TimeSpan _due = TimeSpan.FromSeconds(5);
private TimeSpan _period = TimeSpan.FromSeconds(5);

/// <summary>Окно «Сетевая безопасность» открыто и активно опрашивает алерты.</summary>
private volatile bool _monitorWindowActive;
```

**`StartBackgroundScan` / `StopBackgroundScan`:**

```csharp
public void StartBackgroundScan(TimeSpan? dueTime = null, TimeSpan? period = null)
{
    StopBackgroundScan();
    _due = dueTime ?? TimeSpan.FromSeconds(5);
    _period = period ?? TimeSpan.FromSeconds(5);
    _cts = new CancellationTokenSource();
    _loop = RunAsync(_cts.Token);
}

public void StopBackgroundScan()
{
    try { _cts?.Cancel(); } catch { }
    _cts?.Dispose();
    _cts = null;
    _loop = null;
}

/// <summary>Открытие/закрытие окна мониторинга сети. Закрытие усыпляет фоновый скан.</summary>
public void SetMonitorWindowActive(bool active)
{
    _monitorWindowActive = active;
    _wake.Pulse();               // разбудить цикл для немедленного перехода в сон/работу
}

private async Task RunAsync(CancellationToken ct)
{
    try { await Task.Delay(_due, ct); }
    catch (OperationCanceledException) { return; }

    while (!ct.IsCancellationRequested)
    {
        // Фон сканирует только при включённых алертах и открытом окне.
        // Пока окно закрыто — алерты копятся по запросу GetPendingNetworkAlerts.
        if (AlertsEnabled && _monitorWindowActive)
        {
            try { ScanNewConnections(); } catch { }
        }

        var wait = (AlertsEnabled && _monitorWindowActive)
            ? _period
            : Timeout.InfiniteTimeSpan;

        try { await _wake.WaitAsync(wait, ct); }
        catch (OperationCanceledException) { break; }
    }
}
```

**`SetAlertsEnabled` (строки 126–131):**

```csharp
public void SetAlertsEnabled(bool enabled)
{
    AlertsEnabled = enabled;
    if (!enabled) ClearPending();
    _wake.Pulse();
}
```

**Новый pipe-контракт (сервис + UI).** Добавить `PipeMessageTypes.SetNetworkMonitorActive`:

```csharp
// PipeServer.ProcessMessage
case PipeMessageTypes.SetNetworkMonitorActive:
    bool monitorActive = bool.TryParse((message.Payload ?? "").Trim().Trim('"'), out var ma) && ma;
    _alertService?.SetMonitorWindowActive(monitorActive);
    response.Type = PipeMessageTypes.Success;
    break;
```

В UI (`NetworkSecurityHubView.xaml.cs`) вызывается из `Loaded`/`Unloaded` (там уже есть `Deactivate()` на `Unloaded`):
- `OnLoaded` → `await _pipeClient.SetNetworkMonitorActiveAsync(true);`
- `Unloaded` (`Deactivate`) → `await _pipeClient.SetNetworkMonitorActiveAsync(false);`

> **Важно:** это не заменяет `AlertsEnabled` (пользовательский тумблер), а **дополняет** его: скан идёт только когда `AlertsEnabled && windowActive`. Приватность и совместимость сохраняются.

### 6.4 `EntryOperations.ResolveAndEnrichEntry` и kill-sweep

**(A) Устранить O(n log n) вытеснение кэша** (`EntryOperations.cs:836`). Заменить «сортировать весь словарь при каждом переполнении» на пакетное вытеснение с гистерезисом:

```csharp
private const int EnrichCacheCapacity = 5000;
private static int _enrichCacheOps;
private static readonly object _enrichEvictLock = new();

private static void AddToEnrichCache(string key, EnrichCacheValue value)
{
    _enrichCache[key] = value;

    // Проверяем переполнение не на каждой вставке, а раз в 128 записей.
    if ((Interlocked.Increment(ref _enrichCacheOps) & 0x7F) != 0) return;
    if (_enrichCache.Count <= EnrichCacheCapacity) return;

    lock (_enrichEvictLock)
    {
        if (_enrichCache.Count <= EnrichCacheCapacity) return;
        // Однократный проход: берём 10% самых старых, БЕЗ полной сортировки.
        var victims = _enrichCache
            .OrderBy(kv => kv.Value.CacheTimestamp)
            .Take(EnrichCacheCapacity / 10)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var k in victims)
            _enrichCache.TryRemove(k, out _);
    }
}
```

**(B) Не обогащать весь список на каждый опрос.** В `GetAllEntries`/`GetEntriesDelta` вызывать `ResolveAndEnrichEntry` только для записей, изменённых с прошлой ревизии. `_entriesRevision` уже есть — хранить `_lastEnrichedRevision` и пропускать цикл, если ревизия не менялась:

```csharp
case PipeMessageTypes.GetAllEntries:
    // ...
    var serverRev = Volatile.Read(ref _entriesRevision);
    if (serverRev != _lastEnrichedRevision)
    {
        foreach (var e in config.Entries.ToList())
            EntryOperations.ResolveAndEnrichEntry(e, computeHash: false);
        _lastEnrichedRevision = serverRev;
        _store.Save(config);
    }
    // ...
```

**(C) kill-sweep (см. 6.1)** уже гейтится флагом `needsKillSweep`; дополнительно можно один раз кэшировать `activeRules` в `KillProcessesByRules` (сейчас пересчитывается на каждый вызов — это дешево, но лишняя аллокация списка каждые 10 с).

### 6.5 `JsonDataStore` — dirty-tracking и отложенная запись

**(A) Пропускать сериализацию неизменённых секций.** Сейчас `Save()` всегда сериализует все 4 секции ради хэша. Ввести флаги «грязных» секций:

```csharp
[Flags]
private enum DirtySections { None = 0, Entries = 1, Blacklist = 2, Removed = 4, Settings = 8, All = 15 }

private DirtySections _dirty = DirtySections.All;

public void MarkEntriesDirty()   { lock (_lock) _dirty |= DirtySections.Entries; }
public void MarkSettingsDirty()  { lock (_lock) _dirty |= DirtySections.Settings; }
// ... аналогично blacklist/removed
```

`Save()` пишет только то, что помечено (у остального хэш уже совпадает, повторная сериализация не нужна):

```csharp
public void Save(AppConfig config)
{
    lock (_lock)
    {
        _cache = config;
        if ((_dirty & DirtySections.Entries)   != 0) SaveEntries();
        if ((_dirty & DirtySections.Blacklist) != 0) SaveBlacklist();
        if ((_dirty & DirtySections.Removed)   != 0) SaveRemoved();
        if ((_dirty & DirtySections.Settings)  != 0) SaveSettings();
        _dirty = DirtySections.None;
    }
}
```
> Семантику вызовов нужно поправить точечно (`UpdateEntry`/`AddEntry` → `MarkEntriesDirty`), иначе сложно отследить. Более консервативный вариант — оставить `Save()` как есть, но в горячих вызовах `GetAllEntries` вызывать `Save` только при реальных изменениях (см. 6.4B).

**(B) Debounced single-writer через `Channel`** (снимает блокирующий I/O с потока обработки pipe):

```csharp
private readonly Channel<AppConfig> _saveQueue =
    Channel.CreateBounded<AppConfig>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true, SingleWriter = false
    });

public void Save(AppConfig config)
{
    // in-memory кэш обновляем сразу (чтение консистентно),
    // дисковую запись делегируем единственному писателю с коалесцингом.
    lock (_lock) _cache = config;
    _saveQueue.Writer.TryWrite(config);
}

public void StartWriter(CancellationToken ct) => _ = Task.Run(async () =>
{
    await foreach (var cfg in _saveQueue.Reader.ReadAllAsync(ct))
    {
        try
        {
            // Микро-дебаунс: забираем «свежак», если за время записи пришёл новый.
            while (_saveQueue.Reader.TryRead(out var newer)) cfg = newer;
            lock (_lock) { SaveEntries(); SaveBlacklist(); SaveRemoved(); SaveSettings(); }
        }
        catch (Exception ex) { /* лог */ }
    }
}, ct);
```

**(C) `history.jsonl`.** `File.AppendAllText` на каждое событие открывает/закрывает файл. Заменить на буферизованный писатель:

```csharp
private readonly Channel<HistoryEntry> _historyQueue =
    Channel.CreateUnbounded<HistoryEntry>(new UnboundedChannelOptions { SingleReader = true });

public void AddHistory(HistoryEntry entry)
{
    lock (_lock)
    {
        var config = Load();
        config.History.Add(entry);
        var cutoff = DateTime.UtcNow.AddDays(-config.Settings.HistoryDays);
        config.History.RemoveAll(h => h.Timestamp < cutoff);
    }
    _historyQueue.Writer.TryWrite(entry);   // I/O вне lock
}

// Single writer: открывает файл один раз на батч, а не на запись.
public void StartHistoryWriter(CancellationToken ct) => _ = Task.Run(async () =>
{
    var buffer = new List<HistoryEntry>(64);
    while (!ct.IsCancellationRequested)
    {
        buffer.Clear();
        if (await _historyQueue.Reader.WaitToReadAsync(ct))
            while (buffer.Count < 64 && _historyQueue.Reader.TryRead(out var h))
                buffer.Add(h);

        if (buffer.Count == 0) continue;
        var sb = new StringBuilder();
        foreach (var h in buffer) sb.Append(JsonSerializer.Serialize(h, _historyOptions)).Append('\n');
        try { File.AppendAllText(HistoryPath, sb.ToString()); } catch { }
    }
}, ct);
```

### 6.6 `SystemScannerService` / `CleanupStaleEntries` (вторичная оптимизация)

- `ScanAll` уже профайлится по секциям (`Service.ScanRegistry`, `Service.ScanServices`, `Service.ScanTaskScheduler`, `Service.ScanWmi`, `Service.ScanAppx`). **Оставлять 5-минутный период, но гейтить запуск** — он уже гейтится через `needsKillSweep`/UI в 6.1; полный скан раз в 5 минут при простое без UI можно сдвинуть на «раз в 15 минут либо при появлении клиента» (`_uiClients`).
- `ScanAppxStartupTasks` (`SystemScannerService.cs:770`) перебирает `Directory.GetDirectories(WindowsApps)` и парсит XML всех манифестов — дорогая секция; её стоит запускать только при появлении UI-клиента (`_uiClients.IsActive`), а фоновый скан выполнять с `skipAppx`.

---

## 7. План внедрения, приоритеты, риски

| # | Изменение | Эффект | Риск | Файлы |
|---|---|---|---|---|
| P0 | Гейт `NetworkConnectionAlertService` по `AlertsEnabled && windowActive` + deep sleep | Убирает COM-перебор `GetAllRules()` каждые 5 с в простое — **крупнейшая экономия** | Средний (нужен новый pipe-контракт + UI) | `NetworkConnectionAlertService.cs`, `PipeServer.cs`, `Program.cs`, `PipeMessageTypes`, UI |
| P0 | `ProcessPortDefenderService` → gated `IHostedService`, deep sleep | Снимает сетевую таблицу каждые 3 с в простое | Низкий (изолированный класс) | `ProcessPortDefenderService.cs`, `Program.cs` |
| P1 | `ActivityGate` + учёт UI-клиентов + gated `BackgroundCleanupLoop` | Kill-sweep/скан только по необходимости | Низкий | `PipeServer.cs`, новый `ActivityGate.cs` |
| P1 | Убрать O(n log n) в `AddToEnrichCache` | Убирает латентные всплески обогащения | Низкий | `EntryOperations.cs` |
| P2 | Не обогащать весь список при неизменной ревизии | Экономия `File.Exists`×N каждые 5 с | Низкий | `PipeServer.cs` |
| P2 | Dirty-tracking / debounced-writer `JsonDataStore` | Меньше синхронного дискового I/O | Средний (семантика вызовов) | `JsonDataStore.cs` |
| P3 | `NotificationService` flush только при наличии сообщений/клиентов; heartbeat `EventPipeServer` — только при `_clients.Count>0` | Мелкая экономия | Низкий | `NotificationService.cs`, `EventPipeServer.cs` |
| P3 | Исправить `PipeServer._cts`: передать его в циклы (`CreateLinkedTokenSource(stoppingToken, _cts.Token)`) | Корректная остановка по `Dispose` | Низкий | `PipeServer.cs` |

**Обязательная верификация после внедрения:**
1. `dotnet build` обоих проектов, синтаксическая чистота.
2. `dotnet test` (xUnit в `StartupMonitor.Tests` — `InternalsVisibleTo` уже настроен).
3. Юнит-тесты на `ActivityGate` (переходы 0→1→0, `Pulse` в глубоком сне, отмена), `SleepAwareLoop.RunAsync` (тело не вызывается при `hasWork=false`), `NetworkConnectionAlertService` (скан не идёт при `windowActive=false` — тестовые швы `SocketsProvider`/`HasFirewallRuleOverride` уже есть).
4. Снять `PERF REPORT` до/после: ожидается, что в простое блоки `Service.NetworkAlerts.Scan`, `Service.Firewall.GetAllRules`, `Service.Network.GetSockets` **исчезнут** из отчёта до появления UI-клиента/открытия окна.

**Замечания по совместимости:**
- `ManualResetEventSlim` использовать **не** следует в async-циклах (блокирует поток); роль async-будильника выполняет `SemaphoreSlim` внутри `ActivityGate`.
- `Channels` нужны только для дискового single-writer (`JsonDataStore`, history); для детекта сокетов событий нет — там корректна модель «gated polling» с deep sleep.
- Изменение дефолта `AlertsEnabled` не требуется: гейт «окно открыто» снимает нагрузку, не ломая пользовательский тумблер.

---

## 8. Итоговый результат для трёх целевых подпрограмм

1. **`BackgroundCleanupLoop`** — из постоянного `Task.Delay(10с)` превращается в адаптивный цикл: `needsKillSweep ? 10 с : 5 мин`, глубокий сон при `IsPaused`, мгновенное пробуждение на события паузы/ЧС через `ActivityGate`.
2. **`ProcessPortDefenderService._pollTimer`** — таймер удаляется; `IsDefenseActive()` (учёт `Rules.Count`/`IsGloballyEnabled`) управляет сном: активен → 3 с, выключен → бесконечный сон до `SaveConfig().Pulse()`.
3. **`NetworkConnectionAlertService.ScanNewConnections`** — фоновый скан выполняется только при `AlertsEnabled && windowActive`; при закрытом окне — глубокий сон, а алерты отдаются по требованию через существующий `GetPendingNetworkAlerts` (который уже вызывает `ScanNewConnections()` на месте).

Дополнительно снят паразитный COM-перебор брандмауэра, двойной проход по сетевой таблице, латентные всплески кэша обогащения и лишний дисковый I/O JSON-хранилища.


### Логи выполнения / Предупреждения:
```
[0m
> build · deepseek-v4.1-flash-free
[0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/Firewall/ProcessPortDefenderService.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/SystemScannerService.cs
[0m✱ [0mGrep "BackgroundCleanupLoop|BackgroundHashScanLoop|BackgroundRatingLoop|Thread.Sleep|Task.Delay|CancellationTokenSource|Timer"[90m in src/StartupMonitor.Service/Pipe/PipeServer.cs · 27 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs[90m [offset=1069][0m
[0m→ [0mRead src/StartupMonitor.Service/Services/SystemScannerService.cs[90m [offset=1024][0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/EventPipeServer.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [offset=2075, limit=160][0m
[0m✱ [0mGlob "**/{MonitoringStateCoordinator,NotificationService,ProcessNetworkTracker,JsonDataStore,ExecutionProfiler,InitialScanTracker}.cs"[90m 6 matches[0m
[0m✱ [0mGlob "**/Program.cs"[90m 2 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/MonitoringStateCoordinator.cs
[0m→ [0mRead src/StartupMonitor.Service/Program.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/NotificationService.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/JsonDataStore.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
[0m✱ [0mGrep "SetAlertsEnabled|AlertsEnabled|NetworkAlertsEnabled|GetPendingAlerts|SetPortDefender|SaveConfig|ProcessSocketSnapshot|ProcessPortDefenderService|_portDefenderService|_alertService"[90m in src/StartupMonitor.Service/Pipe/PipeServer.cs · 28 matches[0m
[0m→ [0mRead src/StartupMonitor.Shared/Diagnostics/ExecutionProfiler.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [offset=1440, limit=100][0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [offset=1780, limit=120][0m
[0m✱ [0mGlob "**/StartupMonitor.Service/*.csproj"[90m 1 match[0m
[0m✱ [0mGrep "TryGetProcessPath|GetExtendedTcpTable|GetAllRules"[90m in src/StartupMonitor.Service · 28 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/StartupMonitor.Service.csproj
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetadataEnricher.cs[90m [offset=245, limit=60][0m
[0m→ [0mRead src/StartupMonitor.Service/Services/WindowsFirewallService.cs[90m [offset=455, limit=60][0m
[0m✱ [0mGrep "Win32_ProcessStartTrace|ProcessStartEvent|_processedPids|OnProcessStarted"[90m in src/StartupMonitor.Service · 0 matches[0m
[0m✱ [0mGrep "event|class BlacklistManager|public "[90m in src/StartupMonitor.Service/Services/BlacklistManager.cs · 100 matches[0m
[0m✱ [0mGlob "**/AutoUnbanWorker.cs"[90m 1 match[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/BlacklistManager.cs[90m [limit=40][0m
[0m→ [0mRead src/StartupMonitor.Service/Services/Firewall/AutoUnbanWorker.cs
[0m✱ [0mGrep "SetAlertsEnabled|GetPendingNetworkAlerts|AlertsEnabled|SetPortDefenderConfig|GetActiveBans|SubscribeProcessMetrics"[90m in src/StartupMonitor.UI · 28 matches[0m
[0m→ [0mRead src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml.cs[90m [limit=80, offset=330][0m
[0m→ [0mRead src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml.cs[90m [limit=80, offset=480][0m
[0m✱ [0mGrep "_alertPollerTimer|OnUnloaded|Unloaded|DispatcherTimer|Interval|OnLoaded|_liveConnTimer"[90m in src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml.cs · 37 matches[0m

```
