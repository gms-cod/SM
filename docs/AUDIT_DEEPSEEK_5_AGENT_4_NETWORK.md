# Субагент 4: ProcessNetworkTracker и устранение GC-мусора при сборе сокетов

- Модель субагента: `opencode/deepseek-v4.1-flash-free`
- Время выполнения: 272.3 сек
- Статус: Успешно

---

Now I have full context. Let me implement the optimizations directly (the changes are tightly coupled across both files, so a single coherent edit is safer than parallel agents).
Public API is stable. Now rewriting `ProcessNetworkTracker.cs` with struct keys, reusable buffers, and disposal.
Now updating `ProcessMetricsTracker.cs` to add the 5-second Toolhelp32 cache and dispose the network tracker.
Now let me verify the build and run the relevant tests.
The Service project compiles cleanly. The test run is blocked by a **pre-existing, unrelated** compile error in the UI project (`MainViewModel.cs:491` references `TaskManager.IsActive`, which is part of someone else's uncommitted WIP — confirmed in `git diff`, not my changes).

Let me confirm the UI breakage is independent of my edits:
The workspace is being **concurrently modified by other agents** — `AuthenticodeVerifier.cs` changed between two consecutive tool reads (old code at read time, rewritten code in a later grep). So the UI/Shared errors are transient in-flight edits, unrelated to my task.

Let me re-confirm my Service changes still build:
Готово. Изменения внесены и компилируются (Service: 0 warnings / 0 errors).

## Что сделано

**`ProcessNetworkTracker.cs`**
1. **`SocketKey4` / `SocketKey6`** — компактные `readonly struct : IEquatable<T>` (стековые ключи, без боксинга и строк):
   - `SocketKey4`: `LocalAddr/LocalPort/RemoteAddr/RemotePort`, порты нормализуются в конструкторе.
   - `SocketKey6`: 16-байтовые адреса упаковываются в два `ulong` через `BinaryPrimitives.ReadUInt64LittleEndian` (без аллокаций).
   - Заменённые словари: `Dictionary<SocketKey4, ulong>` + `Dictionary<SocketKey6, ulong>` вместо `Dictionary<string, ulong>`. Заодно исправлены ключи v6 — теперь учитываются адреса и scope (раньше только порты → коллизии).
2. **Переиспользование буферов** — `EnsureBuffer(ref IntPtr, ref int, int)` с `AllocHGlobal`/`ReAllocHGlobal`: поля `_tcp4Buffer/_tcp6Buffer/_udp4Buffer/_udp6Buffer/_rodBuffer`. Убран цикл `AllocHGlobal/FreeHGlobal` на каждый тик. Добавлен retry на `ERROR_INSUFFICIENT_BUFFER`. Класс стал `IDisposable`, буферы освобождаются в `Dispose()`; доступ к общим буферам и кэшу защищён `lock (_sync)`.
3. `ProcessMetricsTracker.Dispose()` теперь вызывает `_networkTracker.Dispose()`.

**`ProcessMetricsTracker.cs`** — кэш снимка Toolhelp32 на 5 секунд (`_processSnapshotMap` + `_lastProcessSnapshot`, атомарная подмена ссылки), вместо вызова `GetProcessSnapshotMap()` каждый тик (снимает ~41% времени сбора).

## Блокер верификации
`dotnet test` не запускается из-за ошибок в **чужих** незакоммиченных правках, не связанных с задачей:
- `MainViewModel.cs:491` — `TaskManager.IsActive` (нет такого свойства), есть в `git diff` — чужой WIP.
- Проекты `UI`/`Shared` прямо сейчас перезаписываются другими агентами (файл `AuthenticodeVerifier.cs` изменился между двумя последовательными чтениями).

Публичные API `CaptureNetworkBandwidth()` / `GetSockets(int)` не менялись, тесты совместимы. Если хотите — могу временно отключить/поправить UI-зависимость тестового проекта, чтобы прогнать `ProcessNetworkTrackerTests` и `ProcessTreeTests`.


### Логи выполнения / Предупреждения:
```
[0m
> build · deepseek-v4.1-flash-free
[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
[0m✱ [0mGlob "**/ProcessStartupLinker.cs"[90m 1 match[0m
[0m✱ [0mGlob "**/*.csproj"[90m 5 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/StartupMonitor.Service.csproj
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessStartupLinker.cs
[0m✱ [0mGrep "new ProcessNetworkTracker|ProcessNetworkTracker\("[90m 4 matches[0m
[0m✱ [0mGrep "CaptureNetworkBandwidth|GetProcessSnapshotMap|GetParentPidMap"[90m 46 matches[0m
[0m→ [0mRead tests/StartupMonitor.Tests/Services/ProcessNetworkTrackerTests.cs
[0m→ [0mRead tests/StartupMonitor.Tests/Services/ProcessTreeTests.cs
[0m
[0m← [0mWrite src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
Wrote file successfully.
[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
Index: D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
===================================================================
--- D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
+++ D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
@@ -39,8 +39,12 @@
 private bool _hasSystemTimesPrev;
 // Кэш сопоставления PID со службами Windows (обновляется раз в 4 секунды)
 private Dictionary<int, List<string>> _servicePidMap = new();
 private DateTime _lastServicePoll = DateTime.MinValue;
+// Кэш снимка Toolhelp32 (Parent PID + ThreadCount): снимок тяжёлый (~4 мс),
+// поэтому обновляем его раз в 5 секунд атомарной подменой ссылки.
+private Dictionary<int, ProcessStartupLinker.ProcessSnapshotEntry> _processSnapshotMap = new();
+private DateTime _lastProcessSnapshot = DateTime.MinValue;
 // PID процессов, для которых TotalProcessorTime/StartTime недоступны
 // (системные/PPL процессы, бросающие Win32Exception Access Denied) —
 // не вызываем эти свойства повторно до завершения процесса.
 private readonly ConcurrentDictionary<int, byte> _inaccessiblePids = new();

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
Index: D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
===================================================================
--- D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
+++ D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
@@ -163,10 +163,17 @@
 // 4. Замер сетевой активности процессов через IP Helper API
 var (netMap, totalNet, socketCounts) = _networkTracker.CaptureNetworkBandwidth();
 snapshot.TotalNetworkBytesPerSec = totalNet;
 
-// Карта снимка процессов (Parent PID + ThreadCount) через CreateToolhelp32Snapshot
-var snapshotMap = ProcessStartupLinker.GetProcessSnapshotMap();
+// Карта снимка процессов (Parent PID + ThreadCount) через CreateToolhelp32Snapshot.
+// Тяжёлый вызов (~4 мс, до 41% времени сбора) кэшируется на 5 секунд:
+// Parent PID и число потоков меняются редко, поэтому свежести раз в 5 с достаточно.
+if ((now - _lastProcessSnapshot).TotalSeconds > 5.0 || _processSnapshotMap.Count == 0)
+{
+    _processSnapshotMap = ProcessStartupLinker.GetProcessSnapshotMap();
+    _lastProcessSnapshot = now;
+}
+var snapshotMap = _processSnapshotMap;
 
 // 5. Параллельный сбор метрик по ядрам процессора
 Parallel.ForEach(rawProcesses, _parallelOptions, proc =>
 {

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
Index: D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
===================================================================
--- D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
+++ D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs
@@ -474,7 +474,8 @@
     /// <summary>Dispose: Освобождает занятые ресурсы объекта.</summary>
     public void Dispose()
     {
         _pollTimer.Dispose();
+        _networkTracker.Dispose();
     }
 }
 

[0m
[0m$ [0mdotnet build src\StartupMonitor.Service\StartupMonitor.Service.csproj -c Debug --nologo -v q
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:12.12
[0m
[0m$ [0mdotnet test tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~ProcessNetworkTrackerTests|FullyQualifiedName~ProcessTreeTests" -v q
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs(491,45): error CS1061: 'TaskManagerViewModel' does not contain a definition for 'IsActive' and no accessible extension method 'IsActive' accepting a first argument of type 'TaskManagerViewModel' could be found (are you missing a using directive or an assembly reference?) [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI_izc1wibd_wpftmp.csproj]
[0m
[0m✱ [0mGrep "IsActive"[90m in src/StartupMonitor.UI · 4 matches[0m
[0m
[0m$ [0mgit -C "D:\Nas\Coding\StartupMonitor" status --short; git -C "D:\Nas\Coding\StartupMonitor" stash list
M src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
 M src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
 M src/StartupMonitor.UI/ViewModels/MainViewModel.cs
 M src/StartupMonitor.UI/Views/MainWindow.xaml.cs
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
[0m
[0m$ [0mgit -C "D:\Nas\Coding\StartupMonitor" diff -- src/StartupMonitor.UI/ViewModels/MainViewModel.cs | Select-String -Pattern "IsActive" -Context 3,3
+        // Отписка от потока метрик процессов: служба перестаёт соб�
�рать снапшоты (0% CPU)
  +        if (_pipeClient.IsConnected)
  +            _ = _pipeClient.UnsubscribeProcessMetricsAsync();
> +        _taskManagerWasActive = TaskManager.IsActive;
  +        _ = TaskManager.DeactivateAsync();
  +    }
  +
[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs[90m [offset=160, limit=18][0m
[0m
[0m$ [0mdotnet build src\StartupMonitor.UI\StartupMonitor.UI.csproj -c Debug --nologo -v q 2>&1 | Select-String -Pattern "error" | Select-Object -First 5
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs(152,24): error CS0029: Cannot im
plicitly convert type 'System.Lazy<StartupMonitor.Shared.Helpers.SignatureVerificationResult>' to 'StartupMonitor.Share
d.Helpers.SignatureVerificationResult' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.cs
proj]
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs(155,31): error CS0029: Cannot im
plicitly convert type 'StartupMonitor.Shared.Helpers.SignatureVerificationResult' to 'System.Lazy<StartupMonitor.Shared
.Helpers.SignatureVerificationResult>' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.cs
proj]
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs(161,35): error CS0029: Cannot im
plicitly convert type 'StartupMonitor.Shared.Helpers.SignatureVerificationResult' to 'System.Lazy<StartupMonitor.Shared
.Helpers.SignatureVerificationResult>' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.cs
proj]
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs(152,24): error CS0029: Cannot im
plicitly convert type 'System.Lazy<StartupMonitor.Shared.Helpers.SignatureVerificationResult>' to 'StartupMonitor.Share
d.Helpers.SignatureVerificationResult' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.cs
proj]
D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs(155,31): error CS0029: Cannot im
plicitly convert type 'StartupMonitor.Shared.Helpers.SignatureVerificationResult' to 'System.Lazy<StartupMonitor.Shared
.Helpers.SignatureVerificationResult>' [D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.cs
proj]
[0m
[0m→ [0mRead tests/StartupMonitor.Tests/StartupMonitor.Tests.csproj
[0m→ [0mRead src/StartupMonitor.Shared/Helpers/AuthenticodeVerifier.cs[90m [offset=130, limit=45][0m
[0m✱ [0mGrep "Cache"[90m in src/StartupMonitor.Shared/Helpers/AuthenticodeVerifier.cs · 13 matches[0m
[0m
[0m$ [0mdotnet build src\StartupMonitor.Service\StartupMonitor.Service.csproj -c Debug --nologo -v q 2>&1 | Select-String -Pattern "error|Build succeeded|Warning"
Build succeeded.
    0 Warning(s)
    0 Error(s)
[0m

```
