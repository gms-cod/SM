# OpenCode Swarm Agent 1: SystemScanner & CLSID Explorer Audit (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 93.4 сек
- Статус: Успешно

---

Готовлю отчёт по тормозам ScanAll — смотрю сканер, обогащение и пайпы.
Нашёл узкое место в CLSID-скане — теперь разбираю очистку устаревших и фоновые циклы.
Собрал картину по трём файлам — уточняю пару деталей по кэшу и хранилищу для точных цифр.
# Отчёт: `Service.ScanAll` 12.3с / 7817× `ResolveAndEnrichEntry`

## 1. Механика главной проблемы

### Виновник: `ScanClsidEntries` — `SystemScannerService.cs:408-447`

```csharp
using var clsidRoot = Registry.ClassesRoot.OpenSubKey("CLSID"); // :412
foreach (var clsid in clsidRoot.GetSubKeyNames())               // :415 ~7485 шт
  ...
  foreach (var subKeyName in new[] { "InprocServer32", "LocalServer32" }) // :424
    ...
    EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false); // :441
    results.Add(entry);
```

Что происходит:

* Нет фильтра «реально зарегистрирован в Explorer». Сканируется **весь `HKCR\CLSID`** (~7485 ключей), хотя реально подключённых через `shellex` / `ShellContextMenuHandlers` / `Browser Helper Objects` — 50–200 шт.
* Нет `WatcherHelper.LooksLikeCommand`-фильтра (как в `ScanHiveRegistry:188`, `ScanKeyValues:305`, `ScanSubKeyValues:342`). Любой непустой `InprocServer32/@default` → запись. Отсюда 7817 вызовов enrich ≈ `7485 × ~1.04` (часть CLSID имеет оба `InprocServer32` + `LocalServer32`).
* Каждый вызов `ResolveAndEnrichEntry` (`EntryOperations.cs:825`) при холодном кэше делает: `ResolveExecutablePath` → `File.Exists` (`:1026` + `:904`) → `CliHeuristicAnalyzer.Analyze` дважды (`:896`, `:926`) → `AuthenticodeVerifier.VerifyFile` = `WinVerifyTrust` (`:951`) → `FileVersionInfo.GetVersionInfo` (`:963`) → `ProgramLangDetector.Detect` = `VerQueryValueW` (`:972`) → `File.GetLastWriteTimeUtc` (`:979`) + `AddToEnrichCache` (`:982`).

Арифметика:

| Метрика | Значение |
|---|---|
| `12.3с / 7817` | **~1.57 мс / enrich** |
| Доля CLSID в `ScanAll` | `7485/7817 ≈ 96%` записей, **~11.8с из 12.3с** |
| CPU-эффект | 1 ядро 100% на 12.3с каждые 5 мин (`PipeServer.cs:1346`) → **~4% среднего load 1 ядра + просадка UI** (Pipe ждёт `scanTracker` до 60с, `PipeServer.cs:266,311`) |
| Диск | ~7817× `File.Exists` + ~7817× `WinVerifyTrust` (открытие+чтение DLL) + дамп `logs/scanner-dump.txt` 7800 строк при каждом скане (`SystemScannerService.cs:130-138`) |

Кэш `_enrichCache` (`EntryOperations.cs:786`, cap 5000, `:783`) и кэш `AuthenticodeVerifier.Cache` (`AuthenticodeVerifier.cs:46`) помогают только на **повторных** сканах. Первый/холодный скан после рестарта службы всегда платит полные 12с. А при `7817 > 5000` cap — см. баг №7.

## 2. Как оптимизировать (в порядке эффекта)

### Вариант A (рекомендую): только реально подключённые shellex — `−97…98%`, 12.3с → ~0.2–0.4с

Собрать множество задействованных CLSID из точек подключения, затем обогащать только их:

* `HKCR\*\shellex`, `HKCR\Directory\shellex`, `HKCR\Folder\shellex`, `HKCR\Drive\shellex`, `HKCR\AllFileSystemObjects\shellex`
* `...\Explorer\ShellContextMenuHandlers` (уже сканируется в `OtherLocations:49`), `Browser Helper Objects`
* `HKCR\CLSID\{GUID}\InprocServer32` открывать только если GUID ∈ множеству.

Ожидаемо: `N: 7485 → 50–200`, enrich-вызовы `7817 → ~100–250`, время CLSID-секции `~11.8с → ~0.2с`.

### Вариант B: инкрементальный скан CLSID

`ScanClsidEntries` вызывается из `ScanOtherLocations:282` при **каждом** `ScanAll` (а тот — каждые 5 мин из `BackgroundCleanupLoop:1346-1361` + по требованию `CleanStale:630`). CLSID почти статичен. Достаточно:

* `static HashSet<string> _knownClsid` + полный скан 1 раз при старте;
* далее — только по событию вотчера реестра на `HKCR\CLSID` (добавлен/удалён ключ).

### Вариант C: ленивое обогащение (дёшево, совместимо с A)

В `ScanClsidEntries` не вызывать `ResolveAndEnrichEntry:441` вообще — класть сырую запись (`Name`, `dllPath`, `CLSID`). Обогащать:

* по требованию UI (открытие деталей / AI-рейтинг / Kaspersky-check);
* либо в `GetAllEntries`/`GetEntriesDelta` только для видимой дельты, а не для всех 7800.

Дополнительно, независимо от A/B/C:

* `VerifyInScan` (`EntryOperations.cs:1070`, `SystemScannerService.cs:781-785`): `scanResults.Any(s => TryMatch(...))` = **O(N×M)**. Построить `HashSet` по `LocationDetail` + `Id` один раз перед циклом `CleanupStaleEntries:1106`. При `N≈500, M≈7800` это ~3.9M строковых сравнений на каждый cleanup.
* Не писать `scanner-dump.txt` (`:130-138`) на каждый фоновый скан — только под флагом debug.

## 3. Баги и гонки

### Б1. `LocationDetail = clsid` ломает сопоставление — `SystemScannerService.cs:438`

```csharp
LocationDetail = clsid,  // просто "{GUID}", без hive-пути
```

Везде остальные ветки кладут полный путь (`HKLM\...\value`: `314`, `351`, `394`). А `TryMatch:756-759` требует точного равенства `LocationDetail`. Любое расхождение формата между сканом и записью от вотчера (`OtherStartupLocationsWatcher`) → `VerifyEntry=false` → **cleanup удаляет живую запись**. Плюс `Id = ComputeStableId(entry)` считается от сырого `dllPath` (`:440`, часто `%SystemRoot%\...` в кавычках), а после `ExpandPath:1034` путь другой — Id нестабилен между сканами.

### Б2. `CleanupStaleEntries` может удалить системные записи при частичном скане

`_failedLocations` (`SystemScannerService.cs:17`) заполняется только для Service/Driver (`:563-564`), WMI (`:587`), TaskScheduler (`:624`), AppX (`:708`) и `skipRegistry:72-84`. Но:

* `ScanRegistry:156`, `ScanStartupFolders:462`, `ScanKeyValues/ScanSubKeyValues` — **не добавляют** в `_failedLocations` при `UnauthorizedAccessException`/падении WMI. Исключение выше — и `ScanAll` прерывается, а `_failedLocations` пуст.
* Защита в cleanup (`EntryOperations.cs:1128`) тогда не срабатывает → все записи упавшей секции признаются stale и удаляются (`:1157-1163`).

Отдельно: `BootExecute:301` и `Winlogon:299` фильтры по именам значений — если в БД лежит запись со старым форматом имени, она никогда не сматчится (см. `TryMatch` tertiary только для `Name.Contains('\\'):769`).

### Б3. Гонка `config.Entries` / потеря обновлений вотчеров

* `JsonDataStore.Load()` возвращает **живую ссылку на `_cache`** (`JsonDataStore.cs:90: if (_cache != null) return _cache;`), а не копию. `cfg.Entries` в `BackgroundCleanupLoop:1351`, `CleanStale:626`, `GetAllEntries:274` — один и тот же `List<StartupEntry>`.
* `CleanupStaleEntries:1106` делает `entries.ToList()` для итерации (хорошо), но затем `entries.RemoveAll` (`:1161`) + `store.RemoveEntry` (`:1112,:1160`) мутируют общий список, пока вотчеры (`ServiceWatcher`, `OtherStartupLocationsWatcher`) делают `Add` в тот же список без синхронизации с этим циклом. `List<T>` не потокобезопасен → возможны `InvalidOperationException`, пропуски, а последующий `_store.Save(cfg)` (`PipeServer.cs:1362,632`) перезаписывает файл снимком, **теряя записи, добавленные вотчером между `Load` и `Save`** (last-writer-wins).
* Тот же конфликт: `GetAllEntries:278-280` и `GetEntriesDelta:319-321` делают `ResolveAndEnrichEntry(e, computeHash:false)` + `_store.Save` **на каждый опрос UI** — мутация + запись всего `startup-entries.json` каждые ~5с конкурирует с cleanup-сохранением.

### Б4. Двойное удаление + шум в логах

* `CleanupStaleEntries:1110-1116` (`IsRemoved` → `RemoveEntry`) и `:1157-1163` (stale → `RemoveEntry` + `RemoveAll`) — два пути удаления в одном проходе; между ними `_store.Save` не вызывается, состояние на диске и в памяти расходится до `Save` caller'ом.
* На каждую stale-запись — `LogInformation` + полный `DebugMatch` с дампом 5 кандидатов (`:1149-1152` → `SystemScannerService.cs:788-855`). При массовой чистке это сотни строк в `service-*.log` на каждый цикл.

### Б5. Eviction кэша обогащения — O(N log N) на каждую вставку при переполнении — `EntryOperations.cs:804-821`

```csharp
if (_enrichCache.Count > EnrichCacheCapacity) // 5000
  var oldest = _enrichCache.OrderBy(kv => kv.Value.CacheTimestamp).Take(500)...
```

При `7817 > 5000` условие истинно на каждой из ~2800 вставок хвоста, каждая — полная сортировка 5000 элементов. Это само по себе добавляет **сотни мс–секунды** к тому же `ScanAll`. Лечится очередью/FIFO или `Interlocked`-счётчиком поколений вместо `OrderBy`.

### Б6. Тройные дисковые syscall на запись — `EntryOperations.cs:849,979,1026`

Cache-hit путь делает `File.Exists` (`:849`) + `GetLastWriteTimeUtc` (`:850`); miss-путь — `File.Exists` в `ResolveExecutablePath:1026` + снова `File.Exists:904,947` + `GetLastWriteTimeUtc:979`. Итого 3–4 syscall × 7817 ≈ **25–30k обращений к FS** за скан. Свернуть в один `FileInfo.Exists + LastWriteTimeUtc`.

### Б7. Противоречие `FilePath` для `ContextMenuHandler`

Скан кладёт `FilePath = dllPath` (`SystemScannerService.cs:435`), а enrich специально **не заменяет** `FilePath` для `ShellExtension/ContextMenuHandler` (`EntryOperations.cs:857-861, 936-939`) с комментарием «FilePath хранит CLSID». По факту хранится DLL-путь, а не CLSID — комментарий врёт, а downstream-код (Kill/blacklist-match по `FilePath`) трактует поле двояко.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/SystemScannerService.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs
[0m✱ [0mGrep "CleanupStaleEntries|BackgroundCleanupLoop"[90m in src/StartupMonitor.Service · 12 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs[90m [offset=1089][0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [offset=1310, limit=120][0m
[0m✱ [0mGrep "class AuthenticodeVerifier|static.*VerifyFile|_cache|ConcurrentDictionary"[90m in src · 79 matches[0m
[0m✱ [0mGrep "class JsonDataStore|public.*Load\(\)|public.*Save\(|lock|RemoveEntry|UpdateEntry"[90m in src/StartupMonitor.Service · 100 matches[0m
[0m→ [0mRead src/StartupMonitor.Shared/Models/JsonDataStore.cs[90m [limit=120, offset=1][0m
[0m→ [0mRead src/StartupMonitor.Shared/Helpers/AuthenticodeVerifier.cs[90m [limit=80, offset=40][0m

```
