# Субагент 3: Заморозка таймеров при сворачивании в трей и разгрузка UI Dispatcher

- Модель субагента: `opencode/deepseek-v4.1-flash-free`
- Время выполнения: 384.6 сек
- Статус: Успешно

---

Now I'll implement the changes. Starting with `MainViewModel` — adding the suspend/resume state and methods.
Now `MainWindow.xaml.cs` — wiring the tray freeze to the three handlers.
Now `TaskManagerViewModel` — expose active state and offload `EnumWindows` to a background refresh.
Now `WindowHelper` — adding a thread-safe cache with background refresh.
Now `PipeClient` — offloading large JSON parsing to background threads.
Now let's build to verify.
Cleaning up a redundant double-unsubscribe in the suspend method.
Готово. UI-проект собирается чисто (`0 Warning(s), 0 Error(s)`). Ниже — итоговый код по задаче.

### 1. `MainViewModel.cs` — заморозка всей фоновой активности

Поля состояния:

```csharp
/// <summary>Флаг полной заморозки фоновых таймеров UI (окно скрыто в трей). 1 = приостановлено (Interlocked).</summary>
private int _allBackgroundSuspended;

/// <summary>Была ли активна подписка Диспетчера задач на момент заморозки (для симметричной разморозки).</summary>
private bool _taskManagerWasActive;
```

Методы (добавлены рядом с `SuspendCpuTimer`/`ResumeCpuTimer`):

```csharp
/// <summary>Полностью ли приостановлена фоновая активность UI (окно скрыто в трей).</summary>
public bool IsAllBackgroundSuspended => _allBackgroundSuspended == 1;

/// <summary>
/// Полная заморозка фоновой активности UI при уходе окна в трей (0% CPU от таймеров):
/// останавливает polling, опрос системной темы, замер CPU и watch логов,
/// отменяет фоновые циклы рейтингов/хешей и отписывается от потока метрик процессов службы.
/// Идемпотентно (повторный вызов безвреден).
/// </summary>
public void SuspendAllBackgroundActivity()
{
    if (Interlocked.Exchange(ref _allBackgroundSuspended, 1) == 1) return;

    try { _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
    try { _themePollTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
    try { _logWatchTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }

    // Глубокий покой CPU-таймера: флаг + остановка (двойная защита от срабатывания in-flight тика)
    _cpuTimerSuspended = true;
    try { _cpuUsageTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }

    // Останавливаем фоновые циклы рейтингов AI и дозагрузки хешей
    try { _backgroundCts?.Cancel(); } catch { }

    // Отписка от потока метрик процессов службы (UnsubscribeProcessMetricsAsync):
    // служба перестаёт собирать снапшоты и рассылать метрики (0% CPU).
    _taskManagerWasActive = TaskManager.IsActive;
    _ = TaskManager.DeactivateAsync();
}

/// <summary>
/// Симметричная разморозка фоновой активности UI при восстановлении окна из трея:
/// мгновенно перезапускает все таймеры, возобновляет фоновые циклы и обновляет данные.
/// Идемпотентно (повторный вызов безвреден).
/// </summary>
public void ResumeAllBackgroundActivity()
{
    if (Interlocked.Exchange(ref _allBackgroundSuspended, 0) == 0) return;

    // Polling: возобновляем только если мониторинг не на паузе
    if (!IsMonitoringPaused)
        try { _pollingTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5)); } catch { }

    // Опрос системной темы: только если watcher реально создан (тема = system)
    if (_themePollTimer != null)
        try { _themePollTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2)); } catch { }

    // Watch логов: только если страница «Логи» была активна
    if (_logWatchTimer != null)
        try { _logWatchTimer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)); } catch { }

    _cpuTimerSuspended = false;
    try { _cpuUsageTimer?.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)); } catch { }

    // Возобновляем подписку Диспетчера задач, если она была активна до заморозки
    if (_taskManagerWasActive)
    {
        _taskManagerWasActive = false;
        _ = TaskManager.ActivateAsync();
    }

    // Перезапускаем фоновые циклы рейтингов/хешей новым токеном
    _backgroundCts?.Dispose();
    _backgroundCts = new CancellationTokenSource();
    List<StartupEntry> entries;
    lock (_entriesLock) { entries = _allEntries.ToList(); }
    if (entries.Count > 0)
    {
        _ = LoadRatingsAsync(entries, _backgroundCts.Token);
        _ = LoadHashDataAsync(entries, _backgroundCts.Token);
    }

    // Однократный немедленный запрос актуального состояния после разморозки
    _ = LoadDataIfChanged();
}
```

### 2. `MainWindow.xaml.cs` — привязка к `StateChanged` / `IsVisibleChanged` / `RestoreFromTray`

```csharp
IsVisibleChanged += (_, _) =>
{
    if (!IsVisible)
    {
        // Глубокий покой UI в трее (п. 4.1): замораживаем ВСЕ фоновые таймеры и метрики
        _viewModel.SuspendAllBackgroundActivity();
        try { FirewallHubView?.Deactivate(); } catch { }
        try { ToolboxMainView?.Deactivate(); } catch { }
    }
    else
    {
        if (WindowState != WindowState.Minimized)
            _viewModel.ResumeAllBackgroundActivity();
        // ... существующие активации по _selectedNavIndex (6/8/9)
    }
};

StateChanged += (_, _) =>
{
    if (WindowState == WindowState.Minimized)
    {
        // Окно свернуто/ушло в трей — полная заморозка фоновых таймеров (0% CPU)
        _viewModel.SuspendAllBackgroundActivity();
        try { FirewallHubView?.Deactivate(); } catch { }
        try { ToolboxMainView?.Deactivate(); } catch { }
        if (_hasShown && _viewModel.MinimizeToTray)
            Hide();
    }
    else
    {
        _lastNonMinimizedState = WindowState;
        if (IsVisible)
            _viewModel.ResumeAllBackgroundActivity();
        // ... существующие активации по _selectedNavIndex
    }
};

public void RestoreFromTray()
{
    Show();
    WindowState = _lastNonMinimizedState;
    _viewModel.ResumeAllBackgroundActivity();
    Activate();
    Focus();
    // ... существующие активации
}
```

### 3a. `WindowHelper.cs` — фоновое `EnumWindows` + TTL-кэш

```csharp
private static HashSet<int> _cachedPids = [];
private static DateTime _cacheExpiryUtc = DateTime.MinValue;
private static int _refreshInFlight;
private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

/// <summary>Последний кэшированный набор PID с видимыми окнами (без EnumWindows — безопасно с UI-потока).</summary>
public static HashSet<int> CachedProcessIdsWithWindows => _cachedPids;

/// <summary>Обновляет кэш PID с окнами в фоновом потоке (EnumWindows вне UI-потока).</summary>
public static void RefreshProcessIdsWithWindowsInBackground(Action? onRefreshed = null)
{
    if (DateTime.UtcNow < _cacheExpiryUtc) return;
    if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0) return;

    _ = Task.Run(() =>
    {
        try
        {
            var fresh = GetProcessIdsWithWindows();
            _cachedPids = fresh;
            _cacheExpiryUtc = DateTime.UtcNow.Add(CacheTtl);
            onRefreshed?.Invoke();
        }
        catch { }
        finally { Interlocked.Exchange(ref _refreshInFlight, 0); }
    });
}
```

### 3b. `TaskManagerViewModel.cs` — чтение кэша вместо `EnumWindows` на UI-потоке

```csharp
/// <summary>Активна ли в данный момент подписка на поток метрик процессов службы.</summary>
public bool IsActive => _isActive;

// В UpdateSnapshot вместо WindowHelper.GetProcessIdsWithWindows():
HashSet<int>? windowPids = null;
try
{
    // EnumWindows НЕ вызывается здесь (UI-поток): читаем потокобезопасный кэш,
    // а обновление кэша запускается фоном.
    windowPids = WindowHelper.CachedProcessIdsWithWindows;
    WindowHelper.RefreshProcessIdsWithWindowsInBackground(ReapplyWindowFlags);
}
catch { }

/// <summary>Повторно применяет флаги наличия окна к процессам после фонового EnumWindows.</summary>
private void ReapplyWindowFlags()
{
    var windowPids = WindowHelper.CachedProcessIdsWithWindows;
    if (windowPids.Count == 0) return;

    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
    {
        Application.Current.Dispatcher.InvokeAsync(ReapplyWindowFlags, DispatcherPriority.Background);
        return;
    }

    foreach (var p in _processMap.Values)
        if (!p.HasWindow && windowPids.Contains(p.Pid))
            p.HasWindow = true;
}
```

### 4. `PipeClient.cs` — парсинг больших JSON в фоне

```csharp
/// <summary>Десериализует JSON на фоновом потоке (не блокирует UI Dispatcher на крупных payload).</summary>
private static Task<T?> DeserializeInBackgroundAsync<T>(string json, JsonSerializerOptions options)
    => Task.Run(() => JsonSerializer.Deserialize<T>(json, options));
```

Применено к тяжёлым нагрузкам: `List<PipeMessage>` в event-loop, `SystemPerformanceSnapshot` (событие и `GetProcessesAsync`), `List<StartupEntry>` (`GetAllEntriesAsync`) и `EntriesDeltaResponse` (`GetEntriesDeltaAsync`).

Примечание: полная сборка решения сейчас падает не из-за этой задачи — в `StartupMonitor.Service/Services/SystemScannerService.cs:215,257` другой параллельный воркер оставил неоднозначность `Task` (`Microsoft.Win32.TaskScheduler.Task` vs `System.Threading.Tasks.Task`). UI-проект компилируется без ошибок и предупреждений.


### Логи выполнения / Предупреждения:
```
[0m
> build · deepseek-v4.1-flash-free
[0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs
[0m→ [0mRead src/StartupMonitor.UI/Services/PipeClient.cs
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml.cs
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs[90m [offset=1180][0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs[90m [offset=2296][0m
[0m✱ [0mGrep "GetProcessIdsWithWindows"[90m 16 matches[0m
[0m✱ [0mGrep "class TaskManagerViewModel|DeactivateAsync|ActivateAsync|PerformanceSnapshot"[90m 63 matches[0m
[0m✱ [0mGrep "UnsubscribeProcessMetrics"[90m 15 matches[0m
[0m→ [0mRead optimization.md
[0m→ [0mRead src/StartupMonitor.UI/Services/WindowHelper.cs
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs[90m [offset=120, limit=180][0m
[0m✱ [0mGrep "HasWindow"[90m 11 matches[0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs[90m [limit=120][0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs[90m [offset=296, limit=120][0m
[0m→ [0mRead src/StartupMonitor.Shared/Models/ProcessInfo.cs[90m [offset=200, limit=40][0m
[0m✱ [0mGrep "SuspendAllBackgroundActivity|ResumeAllBackgroundActivity|SuspendCpuTimer|ResumeCpuTimer|IsCpuTimerSuspended"[90m 23 matches[0m
[0m✱ [0mGrep "GetCachedProcessIdsWithWindows|CachedProcessIdsWithWindows|RefreshProcessIdsWithWindowsInBackground|_cachedPids"[90m 0 matches[0m
[0m→ [0mRead docs/SWARM_DEEPSEEK_3_UI_TELEMETRY.md[90m [offset=270, limit=130][0m
[0m
[0m$ [0mrg -n "UpdateSnapshot|OnProcessSnapshotReceived|GetProcessIdsWithWindows" tests/ src/ --glob "*.cs"
rg : ��� "rg" �� �ᯮ����� ��� ��� ���������, �㭪樨, 䠩�� �業��� ��� �믮��塞�� �ணࠬ��. �஢���� �ࠢ��쭮�
�� ����ᠭ�� �����, � ⠪�� ����稥 � �ࠢ��쭮��� ���, ��᫥ 祣� ������ ������.
��ப�:1 ����:1
+ rg -n "UpdateSnapshot|OnProcessSnapshotReceived|GetProcessIdsWithWind ...
+ ~~
    + CategoryInfo          : ObjectNotFound: (rg:String) [], CommandNotFoundException
    + FullyQualifiedErrorId : CommandNotFoundException
[0m
[0m✱ [0mGrep "UpdateSnapshot|OnProcessSnapshotReceived"[90m 25 matches[0m
[0m
[0m← [0mEdit src/StartupMonitor.UI/ViewModels/MainViewModel.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
@@ -42,8 +42,14 @@
 
 /// <summary>Флаг глубокого покоя CPU-таймера (окно скрыто в трей). 1 = приостановлен.</summary>
 private volatile bool _cpuTimerSuspended;
 
+/// <summary>Флаг полной заморозки фоновых таймеров UI (окно скрыто в трей). 1 = приостановлено (Interlocked).</summary>
+private int _allBackgroundSuspended;
+
+/// <summary>Была ли активна подписка Диспетчера задач на момент заморозки (для симметричной разморозки).</summary>
+private bool _taskManagerWasActive;
+
 /// <summary>Флаг защиты от реэнтрантного входа в LoadDataIfChanged (Interlocked).</summary>
 private int _isLoading;
 
 /// <summary>Флаг защиты от реэнтрантного переключения паузы мониторинга (Interlocked).</summary>

[0m
[0m← [0mEdit src/StartupMonitor.UI/ViewModels/MainViewModel.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
@@ -460,8 +460,85 @@
 
 /// <summary>Приостановлен ли CPU-таймер (окно скрыто в трей).</summary>
 public bool IsCpuTimerSuspended => _cpuTimerSuspended;
 
+/// <summary>Полностью ли приостановлена фоновая активность UI (окно скрыто в трей).</summary>
+public bool IsAllBackgroundSuspended => _allBackgroundSuspended == 1;
+
+/// <summary>
+/// Полная заморозка фоновой активности UI при уходе окна в трей (0% CPU от таймеров):
+/// останавливает polling, опрос системной темы, замер CPU и watch логов,
+/// отменяет фоновые циклы рейтингов/хешей и отписывается от потока метрик процессов службы.
+/// Идемпотентно (повторный вызов безвреден).
+/// </summary>
+public void SuspendAllBackgroundActivity()
+{
+    if (Interlocked.Exchange(ref _allBackgroundSuspended, 1) == 1) return;
+
+    try { _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
+    try { _themePollTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
+    try { _logWatchTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
+
+    // Глубокий покой CPU-таймера: флаг + остановка (двойная защита от срабатывания in-flight тика)
+    _cpuTimerSuspended = true;
+    try { _cpuUsageTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
+
+    // Останавливаем фоновые циклы рейтингов AI и дозагрузки хешей
+    try { _backgroundCts?.Cancel(); } catch { }
+
+    // Отписка от потока метрик процессов: служба перестаёт собирать снапшоты (0% CPU)
+    if (_pipeClient.IsConnected)
+        _ = _pipeClient.UnsubscribeProcessMetricsAsync();
+    _taskManagerWasActive = TaskManager.IsActive;
+    _ = TaskManager.DeactivateAsync();
+}
+
+/// <summary>
+/// Симметричная разморозка фоновой активности UI при восстановлении окна из трея:
+/// мгновенно перезапускает все таймеры, возобновляет фоновые циклы и обновляет данные.
+/// Идемпотентно (повторный вызов безвреден).
+/// </summary>
+public void ResumeAllBackgroundActivity()
+{
+    if (Interlocked.Exchange(ref _allBackgroundSuspended, 0) == 0) return;
+
+    // Polling: возобновляем только если мониторинг не на паузе
+    if (!IsMonitoringPaused)
+        try { _pollingTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5)); } catch { }
+
+    // Опрос системной темы: только если watcher реально создан (тема = system)
+    if (_themePollTimer != null)
+        try { _themePollTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2)); } catch { }
+
+    // Watch логов: только если страница «Логи» была активна
+    if (_logWatchTimer != null)
+        try { _logWatchTimer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)); } catch { }
+
+    _cpuTimerSuspended = false;
+    try { _cpuUsageTimer?.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)); } catch { }
+
+    // Возобновляем подписку Диспетчера задач, если она была активна до заморозки
+    if (_taskManagerWasActive)
+    {
+        _taskManagerWasActive = false;
+        _ = TaskManager.ActivateAsync();
+    }
+
+    // Перезапускаем фоновые циклы рейтингов/хешей новым токеном
+    _backgroundCts?.Dispose();
+    _backgroundCts = new CancellationTokenSource();
+    List<StartupEntry> entries;
+    lock (_entriesLock) { entries = _allEntries.ToList(); }
+    if (entries.Count > 0)
+    {
+        _ = LoadRatingsAsync(entries, _backgroundCts.Token);
+        _ = LoadHashDataAsync(entries, _backgroundCts.Token);
+    }
+
+    // Однократный немедленный запрос актуального состояния после разморозки
+    _ = LoadDataIfChanged();
+}
+
 /// <summary>Включены ли уведомления о новых записях.</summary>
 [ObservableProperty]
 private bool _notifyOnNewEntry = true;
 

[0m
[0m← [0mEdit src/StartupMonitor.UI/Views/MainWindow.xaml.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
@@ -205,19 +205,17 @@
 IsVisibleChanged += (_, _) =>
 {
     if (!IsVisible)
     {
-        // Глубокий покой UI в трее (п. 2.1): останавливаем замер CPU
-        _viewModel.SuspendCpuTimer();
-        if (_selectedNavIndex == 6)
-            _ = _viewModel.TaskManager.DeactivateAsync();
+        // Глубокий покой UI в трее (п. 4.1): замораживаем ВСЕ фоновые таймеры и метрики
+        _viewModel.SuspendAllBackgroundActivity();
         try { FirewallHubView?.Deactivate(); } catch { }
         try { ToolboxMainView?.Deactivate(); } catch { }
     }
     else
     {
         if (WindowState != WindowState.Minimized)
-            _viewModel.ResumeCpuTimer();
+            _viewModel.ResumeAllBackgroundActivity();
         if (_selectedNavIndex == 6 && _isStartupComplete && WindowState != WindowState.Minimized)
             _ = _viewModel.TaskManager.ActivateAsync();
         if (_selectedNavIndex == 8 && _isStartupComplete && WindowState != WindowState.Minimized)
         {

[0m
[0m← [0mEdit src/StartupMonitor.UI/Views/MainWindow.xaml.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
@@ -231,11 +231,10 @@
 StateChanged += (_, _) =>
 {
     if (WindowState == WindowState.Minimized)
     {
-        // Окно свернуто/ушло в трей — приостанавливаем CPU-таймер (0% CPU)
-        _viewModel.SuspendCpuTimer();
-        _ = _viewModel.TaskManager.DeactivateAsync();
+        // Окно свернуто/ушло в трей — полная заморозка фоновых таймеров (0% CPU)
+        _viewModel.SuspendAllBackgroundActivity();
         try { FirewallHubView?.Deactivate(); } catch { }
         try { ToolboxMainView?.Deactivate(); } catch { }
         if (_hasShown && _viewModel.MinimizeToTray)
             Hide();
@@ -243,9 +242,9 @@
     else
     {
         _lastNonMinimizedState = WindowState;
         if (IsVisible)
-            _viewModel.ResumeCpuTimer();
+            _viewModel.ResumeAllBackgroundActivity();
         if (_selectedNavIndex == 6 && _isStartupComplete && IsVisible)
             _ = _viewModel.TaskManager.ActivateAsync();
         if (_selectedNavIndex == 8 && _isStartupComplete && IsVisible)
         {

[0m
[0m← [0mEdit src/StartupMonitor.UI/Views/MainWindow.xaml.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
@@ -762,9 +762,9 @@
 public void RestoreFromTray()
 {
     Show();
     WindowState = _lastNonMinimizedState;
-    _viewModel.ResumeCpuTimer();
+    _viewModel.ResumeAllBackgroundActivity();
     Activate();
     Focus();
     if (_selectedNavIndex == 6 && _isStartupComplete)
     {

[0m
[0m← [0mEdit src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
@@ -37,8 +37,11 @@
 
 /// <summary>Событие показа всплывающего уведомления.</summary>
 public event Action<string, string>? StatusMessageRequested;
 
+/// <summary>Активна ли в данный момент подписка на поток метрик процессов службы.</summary>
+public bool IsActive => _isActive;
+
 [ObservableProperty]
 private SystemPerformanceSnapshot? _performanceSnapshot;
 
 [ObservableProperty]

[0m
[0m← [0mEdit src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
@@ -221,9 +221,12 @@
 
 HashSet<int>? windowPids = null;
 try
 {
-    windowPids = WindowHelper.GetProcessIdsWithWindows();
+    // EnumWindows НЕ вызывается здесь (UI-поток): читаем потокобезопасный кэш,
+    // а обновление кэша запускается фоном (RefreshProcessIdsWithWindowsInBackground).
+    windowPids = WindowHelper.CachedProcessIdsWithWindows;
+    WindowHelper.RefreshProcessIdsWithWindowsInBackground(ReapplyWindowFlags);
 }
 catch { }
 
 var incomingPids = new HashSet<int>();

[0m
[0m✗ [0mEdit src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs failed
[91m[1mError: [0mFound multiple matches for oldString. Provide more surrounding context to make the match unique.
[0m
[0m← [0mEdit src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs
@@ -310,8 +310,31 @@
 
     ApplyFilterAndSort();
 }
 
+/// <summary>
+/// Повторно применяет флаги наличия видимого окна к уже отображённым процессам
+/// после завершения фонового <see cref="WindowHelper.GetProcessIdsWithWindows"/>.
+/// Вызывается на фоновом потоке — переключается на Dispatcher при необходимости.
+/// </summary>
+private void ReapplyWindowFlags()
+{
+    var windowPids = WindowHelper.CachedProcessIdsWithWindows;
+    if (windowPids.Count == 0) return;
+
+    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
+    {
+        Application.Current.Dispatcher.InvokeAsync(ReapplyWindowFlags, DispatcherPriority.Background);
+        return;
+    }
+
+    foreach (var p in _processMap.Values)
+    {
+        if (!p.HasWindow && windowPids.Contains(p.Pid))
+            p.HasWindow = true;
+    }
+}
+
 /// <summary>Проверяет, относится ли процесс к категории пользовательских приложений (Apps).</summary>
 public static bool IsAppProcess(ProcessInfo p)
 {
     if (IsServiceProcess(p)) return false;

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/WindowHelper.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\WindowHelper.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\WindowHelper.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\WindowHelper.cs
@@ -1,12 +1,15 @@
 using System;
 using System.Collections.Generic;
 using System.Runtime.InteropServices;
+using System.Threading;
+using System.Threading.Tasks;
 
 namespace StartupMonitor.UI.Services;
 
 /// <summary>
 /// Помощник для быстрого определения PID процессов, имеющих видимые окна на рабочем столе пользователя.
+/// Для UI-потока предусмотрен потокобезопасный кэш с фоновым обновлением (без блокировки Dispatcher).
 /// </summary>
 public static class WindowHelper
 {
     /// <summary>EnumWindowsProc: Выполняет операцию EnumWindowsProc.</summary>

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/WindowHelper.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\WindowHelper.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\WindowHelper.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\WindowHelper.cs
@@ -97,6 +97,57 @@
         }
 
         return pids;
     }
+
+    // ─────────────────────────── Фоновый кэш (UI Dispatcher Offload) ───────────────────────────
+
+    /// <summary>Потокобезопасный кэш PID процессов с видимыми окнами (обновляется в фоне).</summary>
+    private static HashSet<int> _cachedPids = [];
+
+    /// <summary>Момент времени (UTC), до которого кэш считается свежим.</summary>
+    private static DateTime _cacheExpiryUtc = DateTime.MinValue;
+
+    /// <summary>Флаг «обновление кэша уже выполняется» (защита от параллельных EnumWindows).</summary>
+    private static int _refreshInFlight;
+
+    /// <summary>Время жизни кэша PID с видимыми окнами (EnumWindows не чаще 1 раза в 2 с).</summary>
+    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);
+
+    /// <summary>
+    /// Последний кэшированный набор PID процессов с видимыми окнами.
+    /// Не выполняет EnumWindows — безопасно вызывать с UI-потока.
+    /// </summary>
+    public static HashSet<int> CachedProcessIdsWithWindows => _cachedPids;
+
+    /// <summary>
+    /// Обновляет кэш PID с видимыми окнами в фоновом потоке (EnumWindows вне UI-потока),
+    /// устраняя задержки отрисовки кадров WPF. При истечении TTL запускается не более одного
+    /// обновления; по завершении вызывается <paramref name="onRefreshed"/> (на фоновом потоке).
+    /// </summary>
+    /// <param name="onRefreshed">Необязательный колбэк, выполняемый после успешного обновления кэша.</param>
+    public static void RefreshProcessIdsWithWindowsInBackground(Action? onRefreshed = null)
+    {
+        if (DateTime.UtcNow < _cacheExpiryUtc) return;
+        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0) return;
+
+        _ = Task.Run(() =>
+        {
+            try
+            {
+                var fresh = GetProcessIdsWithWindows();
+                _cachedPids = fresh;
+                _cacheExpiryUtc = DateTime.UtcNow.Add(CacheTtl);
+                onRefreshed?.Invoke();
+            }
+            catch
+            {
+                // Игнорируем сбои P/Invoke — оставляем прежний кэш
+            }
+            finally
+            {
+                Interlocked.Exchange(ref _refreshInFlight, 0);
+            }
+        });
+    }
 }
 

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/PipeClient.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
@@ -31,8 +31,15 @@
     PropertyNameCaseInsensitive = true,
     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
 };
 
+/// <summary>
+/// Десериализует JSON на фоновом потоке, чтобы парсинг крупных полезных нагрузок
+/// (снимок процессов ~195 КБ, список записей автозапуска) не блокировал UI Dispatcher.
+/// </summary>
+private static Task<T?> DeserializeInBackgroundAsync<T>(string json, JsonSerializerOptions options)
+    => Task.Run(() => JsonSerializer.Deserialize<T>(json, options));
+
 /// <summary>Событие подключения к каналу.</summary>
 public event EventHandler? Connected;
 
 /// <summary>Событие отключения от канала.</summary>

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/PipeClient.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
@@ -126,9 +126,9 @@
 
 List<PipeMessage>? batch = null;
 try
 {
-    batch = JsonSerializer.Deserialize<List<PipeMessage>>(line, JsonOptions);
+    batch = await DeserializeInBackgroundAsync<List<PipeMessage>>(line, JsonOptions);
 }
 catch
 {
     continue;

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/PipeClient.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
@@ -142,9 +142,9 @@
 if (msg.Type == PipeMessageTypes.ProcessesSnapshot && !string.IsNullOrEmpty(msg.Payload))
 {
     try
     {
-        var snapshot = JsonSerializer.Deserialize<SystemPerformanceSnapshot>(msg.Payload, JsonOptions);
+        var snapshot = await DeserializeInBackgroundAsync<SystemPerformanceSnapshot>(msg.Payload, JsonOptions);
         if (snapshot != null)
             ProcessSnapshotReceived?.Invoke(this, snapshot);
     }
     catch { }

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/PipeClient.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
@@ -241,9 +241,9 @@
     });
 
     if (response?.Type == PipeMessageTypes.GetAllEntriesResponse && !string.IsNullOrEmpty(response.Payload))
     {
-        return JsonSerializer.Deserialize<List<StartupEntry>>(response.Payload, JsonOptions) ?? [];
+        return await DeserializeInBackgroundAsync<List<StartupEntry>>(response.Payload, JsonOptions) ?? [];
     }
 
     return [];
 }

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/PipeClient.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
@@ -275,9 +275,9 @@
 if (response.Type == PipeMessageTypes.GetEntriesDeltaResponse && !string.IsNullOrEmpty(response.Payload))
 {
     try
     {
-        var delta = JsonSerializer.Deserialize<EntriesDeltaResponse>(response.Payload, JsonOptions);
+        var delta = await DeserializeInBackgroundAsync<EntriesDeltaResponse>(response.Payload, JsonOptions);
         if (delta != null && delta.IsModified)
             return (true, delta.NewRevision, delta.Entries);
         if (delta != null && !delta.IsModified)
             return (false, delta.NewRevision, null);

[0m
[0m← [0mEdit src/StartupMonitor.UI/Services/PipeClient.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs
@@ -684,9 +684,9 @@
     });
 
     if (response?.Type == PipeMessageTypes.ProcessesSnapshot && !string.IsNullOrEmpty(response.Payload))
     {
-        return JsonSerializer.Deserialize<SystemPerformanceSnapshot>(response.Payload, JsonOptions);
+        return await DeserializeInBackgroundAsync<SystemPerformanceSnapshot>(response.Payload, JsonOptions);
     }
 
     return null;
 }

[0m
[0m$ [0mGet-ChildItem -Recurse -Filter *.sln | Select-Object -ExpandProperty FullName; Get-ChildItem -Recurse -Filter StartupMonitor.UI.csproj | Select-Object -ExpandProperty FullName
D:\Nas\Coding\StartupMonitor\StartupMonitor.sln
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj
[0m
[0m$ [0mdotnet build "src\StartupMonitor.UI\StartupMonitor.UI.csproj" -c Debug -v minimal --nologo
Determining projects to restore...
  All projects are up-to-date for restore.
  StartupMonitor.Shared -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
  StartupMonitor.UI -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\bin\Debug\net8.0-windows10.0.17763\win-x64\StartupMonitor.UI.dll
    Determining projects to restore...
    Restored D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.csproj (in 376 ms).
    Restored D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\StartupMonitor.Service.csproj (in 405 ms).
    StartupMonitor.Shared -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs(215,19): error CS0104: 'Task' is an ambiguous reference between 'Microsoft.Win32.TaskScheduler.Task' and 'System.Threading.Tasks.Task' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\StartupMonitor.Service.csproj] [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj]
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs(257,26): error CS0104: 'Task' is an ambiguous reference between 'Microsoft.Win32.TaskScheduler.Task' and 'System.Threading.Tasks.Task' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\StartupMonitor.Service.csproj] [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj]
    Determining projects to restore...
    Restored D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj (in 365 ms).
    1 of 2 projects are up-to-date for restore.
    StartupMonitor.Shared -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
    StartupMonitor.UI -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\bin\Debug\net8.0-windows10.0.17763\win-x64\StartupMonitor.UI.dll
    StartupMonitor.UI -> D:\Nas\Coding\StartupMonitor\SMon2\
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj(37,5): error MSB3073: The command "powershell -NoProfile -ExecutionPolicy Bypass -Command "Stop-Service -Name 'StartupMonitor' -Force -ErrorAction SilentlyContinue; Stop-Process -Name 'StartupMonitor.Service','StartupMonitor.UI' -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500; dotnet publish 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\StartupMonitor.Service\StartupMonitor.Service.csproj' -o 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2' -c Debug -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:ExcludePublishToSMon=true -p:IntermediateOutputPath='obj\pub_svc\'; dotnet publish 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj' -o 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2' -c Debug -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:ExcludePublishToSMon=true -p:IntermediateOutputPath='obj\pub_ui\'; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\assets\icon.svg' 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\icon.svg' -Force -ErrorAction SilentlyContinue; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\assets\app.ico' 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\app.ico' -Force -ErrorAction SilentlyContinue; if (Test-Path 'C:\Program Files\StartupMonitor') { Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\StartupMonitor*.exe' 'C:\Program Files\StartupMonitor\' -Force -ErrorAction SilentlyContinue; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\icon.svg' 'C:\Program Files\StartupMonitor\' -Force -ErrorAction SilentlyContinue; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\app.ico' 'C:\Program Files\StartupMonitor\' -Force -ErrorAction SilentlyContinue; Get-ChildItem 'C:\Program Files\StartupMonitor' -File | Unblock-File -ErrorAction SilentlyContinue }; Get-ChildItem 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2' -Recurse -File | Unblock-File -ErrorAction SilentlyContinue; exit 0"" exited with code -1.

Build FAILED.

D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs(215,19): error CS0104: 'Task' is an ambiguous reference between 'Microsoft.Win32.TaskScheduler.Task' and 'System.Threading.Tasks.Task' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\StartupMonitor.Service.csproj] [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj]
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs(257,26): error CS0104: 'Task' is an ambiguous reference between 'Microsoft.Win32.TaskScheduler.Task' and 'System.Threading.Tasks.Task' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\StartupMonitor.Service.csproj] [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj]
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj(37,5): error MSB3073: The command "powershell -NoProfile -ExecutionPolicy Bypass -Command "Stop-Service -Name 'StartupMonitor' -Force -ErrorAction SilentlyContinue; Stop-Process -Name 'StartupMonitor.Service','StartupMonitor.UI' -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500; dotnet publish 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\StartupMonitor.Service\StartupMonitor.Service.csproj' -o 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2' -c Debug -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:ExcludePublishToSMon=true -p:IntermediateOutputPath='obj\pub_svc\'; dotnet publish 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj' -o 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2' -c Debug -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:ExcludePublishToSMon=true -p:IntermediateOutputPath='obj\pub_ui\'; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\assets\icon.svg' 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\icon.svg' -Force -ErrorAction SilentlyContinue; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\assets\app.ico' 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\app.ico' -Force -ErrorAction SilentlyContinue; if (Test-Path 'C:\Program Files\StartupMonitor') { Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\StartupMonitor*.exe' 'C:\Program Files\StartupMonitor\' -Force -ErrorAction SilentlyContinue; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\icon.svg' 'C:\Program Files\StartupMonitor\' -Force -ErrorAction SilentlyContinue; Copy-Item 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2\app.ico' 'C:\Program Files\StartupMonitor\' -Force -ErrorAction SilentlyContinue; Get-ChildItem 'C:\Program Files\StartupMonitor' -File | Unblock-File -ErrorAction SilentlyContinue }; Get-ChildItem 'D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\..\..\SMon2' -Recurse -File | Unblock-File -ErrorAction SilentlyContinue; exit 0"" exited with code -1.
    0 Warning(s)
    3 Error(s)

Time Elapsed 00:00:23.71
[0m
[0m$ [0mgit status --short; git stash list
M src/StartupMonitor.Service/Pipe/PipeServer.cs
 M src/StartupMonitor.Service/Services/EntryOperations.cs
 M src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
 M src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
 M src/StartupMonitor.Service/Services/SystemScannerService.cs
 M src/StartupMonitor.Shared/Helpers/AuthenticodeVerifier.cs
 M src/StartupMonitor.UI/Services/HashAndSecurityHelper.cs
 M src/StartupMonitor.UI/Services/PipeClient.cs
 M src/StartupMonitor.UI/Services/WindowHelper.cs
 M src/StartupMonitor.UI/ViewModels/MainViewModel.cs
 M src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs
 M src/StartupMonitor.UI/Views/MainWindow.xaml.cs
?? docs/AUDIT_DEEPSEEK_5_AGENT_4_NETWORK.md
?? docs/AUDIT_SWARM_1_SERVICE_IDLE.md
?? docs/AUDIT_SWARM_2_SCANNER_PARALLEL.md
?? docs/AUDIT_SWARM_3_TELEMETRY_MULTICORE.md
?? docs/AUDIT_SWARM_4_TOOLBOX_PARALLEL.md
?? docs/AUDIT_SWARM_5_IPC_CONCURRENCY.md
?? docs/SWARM_DEEPSEEK_1_SERVICE_IDLE.md
?? docs/SWARM_DEEPSEEK_2_SCANNERS_PARALLEL.md
?? docs/SWARM_DEEPSEEK_3_UI_TELEMETRY.md
?? optimization.md
?? scripts/run_deepseek_5_swarm.py
?? scripts/run_deepseek_optimization_swarm.py
?? src/StartupMonitor.Service/Services/Runtime/
[0m
[0m→ [0mRead src/StartupMonitor.UI/StartupMonitor.UI.csproj
[0m→ [0mRead src/StartupMonitor.Service/Services/SystemScannerService.cs[90m [limit=40, offset=200][0m
[0m
[0m$ [0mdotnet build "src\StartupMonitor.UI\StartupMonitor.UI.csproj" -c Debug -v minimal --nologo -p:ExcludePublishToSMon=true
Determining projects to restore...
  Restored D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.csproj (in 339 ms).
  Restored D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj (in 349 ms).
  StartupMonitor.Shared -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
  StartupMonitor.UI -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\bin\Debug\net8.0-windows10.0.17763\win-x64\StartupMonitor.UI.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:07.20
[0m
[0m← [0mEdit src/StartupMonitor.UI/ViewModels/MainViewModel.cs
Index: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
===================================================================
--- d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
+++ d:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
@@ -484,11 +484,10 @@
 
     // Останавливаем фоновые циклы рейтингов AI и дозагрузки хешей
     try { _backgroundCts?.Cancel(); } catch { }
 
-    // Отписка от потока метрик процессов: служба перестаёт собирать снапшоты (0% CPU)
-    if (_pipeClient.IsConnected)
-        _ = _pipeClient.UnsubscribeProcessMetricsAsync();
+    // Отписка от потока метрик процессов службы (UnsubscribeProcessMetricsAsync):
+    // служба перестаёт собирать снапшоты и рассылать метрики (0% CPU).
     _taskManagerWasActive = TaskManager.IsActive;
     _ = TaskManager.DeactivateAsync();
 }
 

[0m
[0m$ [0mdotnet build "src\StartupMonitor.UI\StartupMonitor.UI.csproj" -c Debug -v minimal --nologo -p:ExcludePublishToSMon=true
Determining projects to restore...
  All projects are up-to-date for restore.
  StartupMonitor.Shared -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
  StartupMonitor.UI -> D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\bin\Debug\net8.0-windows10.0.17763\win-x64\StartupMonitor.UI.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:06.29
[0m

```
