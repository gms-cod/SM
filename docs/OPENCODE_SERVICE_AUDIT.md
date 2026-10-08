# OpenCode Service Performance Auditor (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 125.6 сек
- Статус: Успешно

---

# Аудит нагрузки `StartupMonitor.Service`: CPU / RAM / GC / системные ресурсы

> Уточнение путей: вотчеры лежат не в `Watchers/`, а в `Services/`: `Services/RegistryWatcher.cs`, `Services/ServiceWatcher.cs`, `Services/TaskSchedulerWatcher.cs`. Аудит выполнен по фактическим файлам + смежным `ProcessNetworkTracker.cs`, `ProcessMetadataEnricher.cs`, `EntryOperations.cs:700`, `SystemScannerService.cs:61`, `PipeServer.cs:1252+`.

---

## 1. `ProcessMetricsTracker.cs` — главный потребитель CPU в активном режиме (вкладка «Процессы», тик 1.5 с)

### 1.1 `proc.Threads.Count` — самый дорогой вызов тика (стр. 188)
```csharp
try { Interlocked.Add(ref totalThreads, proc.Threads.Count); } catch { }
```
**Механика:** `Process.Threads` каждый раз делает `NtQueryInformationProcess` + обход всех потоков процесса + аллокацию `ProcessThreadCollection` и массива `ProcessThread[]`. При ~1500 потоках в системе это **O(threads) kernel-вызовов каждые 1.5 с**. Плюс каждый `ProcessThread` — финализируемый объект → давление на финализатор/GC.
**Эффект:** на машине с 250 процессами — единицы % CPU только на подсчёт потоков.
**Фикс (P0):**
- считать потоки раз в N тиков (например каждый 4-й, т.е. раз в 6 с) или вообще убрать из горячего пути;
- либо один вызов `NtQuerySystemInformation(SystemProcessInformation)` на всех, вместо N× `Process.Threads`.

### 1.2 `TotalProcessorTime` + `StartTime` — 2 syscall на процесс (стр. 218–219)
```csharp
totalProcTime = proc.TotalProcessorTime;
startTime = proc.StartTime;
```
Каждый геттер — отдельный `NtQueryInformationProcess` + конвертации `FILETIME`. Итого ~500 syscall/тик при 250 процессах + `OpenProcess` (стр. 191) + `GetProcessIoCounters` (стр. 197) + `CloseHandle` (стр. 205). Суммарно **~1000 syscall каждые 1.5 с**.
**Дополнительно:** для защищённых/PPL-процессов эти геттеры **бросают исключение каждый тик** — аллокация exception + stack trace (~мкс–мс каждая). Исключения глотаются `catch {}`, но цена остаётся.
**Фикс (P0):**
- кэшировать множество «недоступных PID» (не опрашивать `TotalProcessorTime/StartTime/IoCounters` каждый тик, а раз в 10–30 с);
- `StartTime` запрашивать только для новых PID (нет в `_history`), а не для всех каждый тик;
- `OpenProcess` один на процесс уже есть — использовать его хендл и для CPU-времени через `GetProcessTimes` вместо `Process.TotalProcessorTime`.

### 1.3 `CaptureNetworkBandwidth()` — второй по тяжести (стр. 146 → `ProcessNetworkTracker.cs`)
- `CollectIpv4Sockets:72-135`, `CollectIpv6Sockets:137-202`: два вызова `GetExtendedTcpTable` (probe + real) + **на каждое ESTABLISHED-соединение** пара `SetPerTcpConnectionEStats` (стр. 106,174) + `GetPerTcpConnectionEStats` (стр. 109,176). При 100 соединениях (браузер) — **~200 syscall/тик**.
- Ключ `string key = $"4:{...}-{...}"` (стр. 115, 182) — интерполированная строка + `Dictionary<string,ulong>` lookup на каждое соединение каждый тик → Gen0-мусор.
- `Marshal.AllocHGlobal/FreeHGlobal` ×2 на каждый `Collect*` (стр. 76-77, 143-144) — 4 аллокации неуправляемой памяти на тик.
- `GetSockets()` (стр. 218): `Process.GetProcessById(pid)` на каждый сокет + `p.ProcessName` без `Dispose` (`var p = Process.GetProcessById(pid)` — стр. 218, `Dispose` не вызывается) — **утечка хендлов** при открытом окне инспектора сокетов.

**Фикс (P0):**
- ключ сокета — `ValueTuple<uint,uint,uint,uint>` / `HashCode.Combine`, без строк;
- `SetPerTcpConnectionEStats` вызывать один раз при первом появлении сокета (флаг в словаре), а не каждый тик;
- переиспользовать `HGlobal`-буферы (`ArrayPool` / один статический буфер с ростом);
- семплировать сеть раз в 3–5 с, а не каждый 1.5-с тик (или только когда вкладка видима);
- в `GetSockets` обернуть `Process` в `using`.

### 1.4 Аллокации снимка (стр. 123, 134–140, 142, 297)
`Process.GetProcesses()` (стр. 123) — 250+ финализируемых `Process` каждые 1.5 с (Dispose в `finally` стр. 291 — правильно, но финализатор всё равно нагружен). Плюс `HashSet<int>`, `Dictionary<int,string>`, `ConcurrentBag<ProcessInfo>`, `[.. processInfos]` — несколько коллекций на тик. `ConcurrentBag` — неудачный выбор: ThreadLocal-хранилище, медленная энумерация; лучше `List<ProcessInfo>` с `lock` или `Partitioner + локальные списки`.
`foreach (_history.Keys)` (стр. 307) — энумерация `ConcurrentDictionary.Keys` делает снимок-копию массива каждый тик.

**Фикс (P1):** пул `List<ProcessInfo>`, `struct`-истории, чистка `_history` по `activePids` без копирования ключей (собирать мёртвые PID в переиспользуемый `List<int>`).

### 1.5 Отравление кэша `Enricher` + гонка данных (стр. 48, 244; `ProcessMetadataEnricher.cs:48,121`)
Ключ кэша — `(Pid, StartTime.Ticks)`. Для недоступных процессов `StartTime = DateTime.MinValue` (стр. 215) → ключ `(pid, 0)` **коллизирует между перезапусками разных процессов** с тем же PID. Плюс `EnrichOrQueue` ставит в очередь **ссылку на тот же `ProcessInfo`**, который уже ушёл в снапшот; фоновый `ProcessEnrichmentLoopAsync` мутирует `proc.FilePath/Publisher/...` **после** того, как снапшот сериализуется в `PipeServer.cs:123-132` → torn read / несогласованный UI.
**Фикс (P1):** не кэшировать записи с `StartTime == MinValue`; очередь — по значению (копия) или обогащение только до отправки снапшота.

### 1.6 `Parallel.ForEach` с `MaxDegree 2–6` (стр. 52, 153) + `GetParentPidMap` (стр. 150)
Сам `CreateToolhelp32Snapshot` (~1 мс) и `ServiceProcessHelper.GetRunningServicesByPid` раз в 4 с (стр. 127–131, `EnumServicesStatusEx` ~10 мс) — приемлемо. Проблема в том, что каждые 1.5 с дёргается пул потоков + внутри `EnrichOrQueue` → `NtQueryInformationProcess(60)` для command line (два вызова + `AllocHGlobal` на процесс, `ProcessMetadataEnricher.cs:291-310`) и `QueryFullProcessImageName`. Это правильно вынесено в throttled-воркер (5 шт/30 мс, стр. 132, 214), но **первый проход** всё равно делает это синхронно в параллельном цикле (стр. 78–96 `Enricher`).
**Фикс (P1):** в горячем тике — только PID/имя/CPU/IO; путь и command line — строго из кэша, иначе `""` до фонового обогащения.

---

## 2. `PipeServer.cs` — фоновые циклы 10 с / 60 с / 5 мин

### 2.1 `KillProcessesByRules` каждые 10 с (стр. 1272 → `EntryOperations.cs:700-762`)
```csharp
procs = Process.GetProcesses();              // 714
procPath = proc.MainModule?.FileName;        // 725
```
`Process.MainModule` — **самый тяжёлый вызов кодовой базы**: `OpenProcess(PROCESS_QUERY_INFORMATION|VM_READ)` + `EnumProcessModules` + `GetModuleFileNameEx`. Для ~30–50 системных/PPL-процессов каждый раз бросает `Win32Exception` (Access Denied) → **десятки исключений каждые 10 с в простое**, даже когда ЧС пуст (ранний выход только если `activeRules.Count == 0`, стр. 709 — если правило есть, платим всегда). Плюс все `Process` висят до `finally` (стр. 757).
**Эффект:** пила 50–150 мс CPU каждые 10 с + рост Gen0.
**Фикс (P0):**
- ранний выход, если нет правил `NotifyAndRemove` (уже есть — держать);
- заменить `MainModule.FileName` на `QueryFullProcessImageName(OpenProcess(QUERY_LIMITED))` — в разы дешевле и реже бросает;
- кэшировать `PID → path` (инвалидация по `StartTime`), опрашивать только новые PID;
- при пустом ЧС пропускать `GetProcesses` вообще.

### 2.2 `ScanAll` каждые 60 с (стр. 1294, `SystemScannerService.cs:61`)
Полный проход: реестр (все `Run` + `OtherLocations`) + папки + **WMI `Win32_Service`** + COM планировщика + WMI-подписки + AppX. Оценка 0.5–2 с CPU/IO на HDD + `ResolveAndEnrichEntry` с `AuthenticodeVerifier.VerifyFile` (`WinVerifyTrust`, 20–100 мс/файл) и `File.Exists` на каждую запись. Выполняется на потоке `BackgroundCleanupLoop`, блокируя и kill-цикл.
**Фикс (P0):** полный `ScanAll` — раз в 10–15 мин; каждые 60 с — только инкрементальная сверка (вотчеры уже push-уведомляют); `VerifyFile` — только для новых/изменённых путей (кэш по SHA/path+mtime).

### 2.3 `_store.Load()` / `_store.Save()` в горячих путях
- `BackgroundCleanupLoop:1271` — `Load` каждые 10 с;
- `BackgroundHashScanLoop:1333-1335` — `Load` + `Save` каждые 5 мин даже без изменений (перезапись JSON, износ SSD, гонка last-write-wins с вотчерами);
- `BackgroundRatingLoop:1228` + `PrefetchAndLogAsync:1173` — двойной `Load`;
- `WatcherHelper.AddOrUpdate:65,81,124` — `store.Load()` **на каждую запись** при скане (N записей = N полных парсов JSON);
- `GetAllEntries:263-269` — `Deduplicate + Load + ResolveAndEnrichEntry` на каждый опрос UI (~5 с) + `Save` — т.е. подпись/метаданные пересчитываются постоянно.

**Фикс (P0):** in-memory кэш конфига + `ReaderWriterLockSlim`, `Load` только при изменении файла (FS-нотификация / mtime); `Save` — дебаунс 2–5 с и только при dirty; `ResolveAndEnrichEntry` в `GetAllEntries` — только для записей без метаданных.

### 2.4 Прочее в `PipeServer`
- Стр. 147–149: `_ = Background*Loop` без супервизора — падение цикла = тихая смерть функции навсегда. Обернуть в restart-обёртку с логом.
- Стр. 171: `_ = HandleClientAsync` неограниченно (10 pipe-инстансов, но тасков больше) — нужен семафор.
- Стр. 999: `Process.GetCurrentProcess()` в `GetServiceInfo` без `Dispose` — утечка хендла на каждый опрос статуса. Фикс: `using` или кэшировать `StartTime`.
- Стр. 123–132: сериализация полного снапшота процессов (~100–300 КБ JSON) каждые 1.5 с → LOH/Gen0. Если UI отключён, очередь `NotificationService` растёт — нужен bounded channel с drop-oldest.
- `ReapplyBlacklist:1378` + `KillProcessesByRules` — двойной `GetProcesses` подряд при добавлении правила.

---

## 3. Вотчеры

### 3.1 `RegistryWatcher.cs` — блокировка ThreadPool (стр. 229–280)
`StartRegNotify` делает `Task.Run(async () => { RegNotifyChangeKeyValue(fAsynchronous:false) })` **на каждый ключ**: HKLM 7 + HKCU ~4 + HKEY_USERS N×4. Итого **15–30 Task, навсегда блокирующих потоки ThreadPool** в синхронном win32-вызове. Последствие — голодание пула: `Task.Delay`-продолжения и pipe-обработчики ждут инжекции потока (1 поток/500 мс).
**Фикс (P0):** один долгоживущий `Thread(IsBackground, LongRunning)` на ключ или один мультиплексор: `RegNotifyChangeKeyValue(async, hEvent)` + `WaitForMultipleObjects` / `ThreadPool.RegisterWaitForSingleObject`. Комментарий в стр. 236 про `ERROR_INVALID_HANDLE` в async-режиме — лечить правильным мануальным `CreateEvent`, а не блокировкой пула.

Дополнительно:
- Стр. 250–265: одно изменение значения → `ScanHive` всего улья (все ключи + `ResolveAndEnrichEntry` + `AddOrUpdate` с `Load` внутри). Шумный ключ = лавина дисковых операций. Фикс: перечитывать только изменившийся `keyPath`, дебаунс 500 мс уже есть (стр. 250) — оставить, но сузить скан.
- Стр. 213: `Registry.Users.OpenSubKey(sid\path)` складывается в `_watchedKeys` без `using` родителя — десятки вечно открытых хендлов; при выгрузке профиля — вечный цикл `ошибка → Delay(1000)` (стр. 270), жгущий цикл. Фикс: обрабатывать `ERROR_KEY_DELETED`, отписываться.
- Стр. 24, 287: `_watchedKeys: List<>` без синхронизации; `StopAsync` диспоузит ключи, пока потоки в `RegNotify` держат `key.Handle` → race. Фикс: `lock` + сначала отмена токена, потом Dispose.

### 3.2 `ServiceWatcher.cs` — WMI-поллинг + утечки RCW
- Стр. 49–66: **два `ManagementEventWatcher` с интервалом 5 с** (`__InstanceCreationEvent` + `__InstanceModificationEvent` по `Win32_Service`). Каждый — полный опрос таблицы служб в `WmiPrvSE` каждые 5 с → постоянная нагрузка **в другом процессе (svchost WMI)** + DCOM-маршалинг. Фикс (P1): один watcher, интервал 15–30 с; либо `NotifyServiceStatusChange` (SCM API) вместо WMI; фильтр `StartMode='Auto'` оставить серверным.
- Стр. 89–94: `foreach (var obj in searcher.Get())` — ни `ManagementObjectCollection`, ни каждый `ManagementObject` не диспоузятся → **утечка COM RCW** при каждом `InitialScan`. Фикс: `using var col = searcher.Get(); foreach... using (obj)`.
- Стр. 140: `using var targetInstance`, но сам `e.NewEvent` не диспоузится → RCW-утечка на каждое событие. Фикс: `using (e.NewEvent)` / `using (targetInstance)`.
- Стр. 135–163: `OnServiceCreated` синхронно делает `ResolveAndEnrichEntry` (диск + подпись + SHA) на WMI-callback потоке → блокировка доставки событий, потери при всплеске. Фикс: только построить `entry` + enqueue в очередь, обогащение — в воркере.
- `obj["Name"]` (стр. 91–94) — каждый доступ к свойству = COM roundtrip; для InitialScan приемлемо (разово), но в поллинге недопустимо.

### 3.3 `TaskSchedulerWatcher.cs` — COM RCW-вал каждые 30 с
- Стр. 143–196 `PollForChanges`: `GetAllTasksWithHidden()` рекурсивно обходит **все папки `\Microsoft\Windows\*` (~150–300 задач)** каждые 30 с. Каждый `Task`, `TaskFolder`, `TaskDefinition`, `TaskCollection` — COM RCW.
  - `CollectTasks:305-325`: `folder.Tasks` (`TaskCollection`) **никогда не диспоузится**; `subFolder` диспоузится (стр. 320) — хорошо, но коллекции нет.
  - `IsStartupTask:261-263`: `task.Definition.Actions/Triggers` — COM-объекты, не освобождаются; каждый геттер — межквартирный COM-вызов 0.1–0.5 мс → **30–150 мс на опрос**.
  - `finally:186-193` диспоузит `Task`, но не `Definition`-RCW → **тысячи неосвобождённых RCW в час**, рост Gen2/рабочего набора, давление на финализатор COM.
- Стр. 158, 205: `FindByTaskPath` → `_store.FindEntry` → (если внутри `Load`) файловый I/O **на каждую из 300 задач каждые 30 с**. Даже при in-memory — линейный поиск O(N×M).
- Стр. 104: `ts.AllTasks` в `InitialScan` — та же утечка Definition-RCW.

**Фикс (P0/P1):**
- `using` на `TaskCollection`, `TaskDefinition`, `ActionCollection`, `TriggerCollection` (или явный `Marshal.ReleaseComObject` в `finally`);
- кэшировать `path → entryId` (`HashSet`), а не линейный поиск;
- поллинг 30 с → 60–120 с (задачи — редко меняющаяся сущность; push не нужен);
- `FormatTaskAction`/`IsStartupTask` — один проход по `Definition`, без повторных обращений к COM-свойствам;
- долгоживущий `TaskService` (стр. 26) держать, но добавить health-check + пересоздание при `COMException` (частично есть, стр. 181, 292).

---

## 4. Приоритизированный план оптимизаций

| Приоритет | Изменение | Где | Ожидаемый эффект |
|---|---|---|---|
| P0 | Убрать `proc.Threads.Count` из тика 1.5 с (семпл раз в 6–10 с) | `ProcessMetricsTracker.cs:188` | −2–5% CPU при открытой вкладке |
| P0 | Сетевой трекер: ключи без строк, `SetEStats` один раз, буферы переиспользовать, семпл 3–5 с | `ProcessNetworkTracker.cs:106-126,182` | −3–8% CPU, −Gen0 |
| P0 | `KillProcessesByRules`: `QueryFullProcessImageName` вместо `MainModule`, кэш PID→path, пропуск при пустом ЧС | `EntryOperations.cs:711-761`, `PipeServer.cs:1272` | убрать пилу 50–150 мс/10 с в простое |
| P0 | `RegistryWatcher`: не блокировать ThreadPool (LongRunning-threads / event-based wait) | `RegistryWatcher.cs:229-280` | −15–30 висящих потоков пула, отзывчивость pipe/таймеров |
| P0 | `TaskSchedulerWatcher`: Dispose COM-коллекций/Definition, поллинг 60–120 с, индекс по path | `TaskSchedulerWatcher.cs:143-325` | −30–150 мс/30 с, стоп утечки RCW |
| P0 | Кэш `JsonDataStore` в памяти + дебаунс `Save`, убрать `Load` из циклов | `PipeServer.cs:1271,1333,1228`, `WatcherHelper.cs:65` | −дисковый I/O, −гонки last-write-wins |
| P1 | CPU/StartTime только для новых PID + кэш недоступных | `ProcessMetricsTracker.cs:218-219` | −~500 syscall/тик, −исключения |
| P1 | Не кэшировать `(pid, MinValue)`, копировать `ProcessInfo` в очередь enricher | `ProcessMetricsTracker.cs:244`, `Enricher.cs:48-121` | корректность данных |
| P1 | `ScanAll` 60 с → 10–15 мин + инкрементальная сверка; `VerifyFile` только для новых | `PipeServer.cs:1280-1302` | −0.5–2 с Stall/мин |
| P1 | `ServiceWatcher`: один WMI-watcher 15–30 с, Dispose RCW, обогащение в очереди | `ServiceWatcher.cs:49-140` | −нагрузка WmiPrvSE, −утечки |
| P1 | `GetAllEntries`: не обогащать/сохранять всё каждые 5 с | `PipeServer.cs:267-269` | −диск + CPU на UI-пути |
| P2 | `ConcurrentBag` → локальные списки; не копировать `_history.Keys`; пул коллекций тика | `ProcessMetricsTracker.cs:142,297,307` | −Gen0/LOH |
| P2 | Супервизор фоновых циклов, семафор `HandleClientAsync`, `using GetCurrentProcess`, bounded очередь снапшотов | `PipeServer.cs:147,171,999,123` | надёжность, −утечки |

Самый дешёвый и самый весомый первый шаг: **п. P0 по `Threads.Count` + сетевому трекеру + `MainModule` в `KillProcessesByRules`** — это три места, дающие большую часть фоновой и активной нагрузки при минимальном диффе.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m✗ [0mRead src/StartupMonitor.Service/Watchers/RegistryWatcher.cs failed
[91m[1mError: [0mFile not found: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\RegistryWatcher.cs
[0m✗ [0mRead src/StartupMonitor.Service/Watchers/ServiceWatcher.cs failed
[91m[1mError: [0mFile not found: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\ServiceWatcher.cs
[0m✗ [0mRead src/StartupMonitor.Service/Watchers/TaskSchedulerWatcher.cs failed
[91m[1mError: [0mFile not found: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\TaskSchedulerWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
[0m✱ [0mGlob "**/*.cs"[90m in src · 100 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ServiceWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/RegistryWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/TaskSchedulerWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetadataEnricher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ServiceProcessHelper.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessStartupLinker.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/WatcherHelper.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [offset=879][0m
[0m✱ [0mGrep "KillProcessesByRules|IsProcessSuspended|public.*ScanAll|ConcurrentBag|Threads\.Count|GetProcessById|TotalProcessorTime|StartTime"[90m in src/StartupMonitor.Service · 29 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/SystemScannerService.cs[90m [limit=120, offset=1][0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs[90m [limit=130, offset=690][0m

```
