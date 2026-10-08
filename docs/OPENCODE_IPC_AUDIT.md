# OpenCode IPC & Data Flow Auditor (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 125.6 сек
- Статус: Успешно

---

# Аудит IPC Named Pipes + `StartupMonitor.Shared` / `JsonDataStore`

Аудит по факту кода, без предположений. Пути ниже — относительно `src/`.

---

## 1. `PipeMessage`: двойная JSON-сериализация + `WriteLine/ReadLine` вместо стриминга

### 1.1. `Payload: string` = сериализация в квадрате

`Shared/Models/PipeMessage.cs:12-14`:

```csharp
public string Payload { get; set; } // "JSON-строка с данными"
```

Механика на каждом сообщении:

1. `JsonSerializer.Serialize(innerObject)` → строка `innerJson`.
2. `JsonSerializer.Serialize(new PipeMessage { Payload = innerJson })` → внешний JSON. При этом весь `innerJson` проходит повторное экранирование: каждая `"` → `\"` (+1 байт), каждый `\` → `\\`, кириллица/переносы — `\uXXXX`/`\n`.
3. На приёме — зеркально две десериализации.

Точки двойной работы:

| Место | Строки | Что происходит |
|---|---|---|
| `Service/Pipe/PipeServer.cs:130` | `Serialize(snapshot)` → `Enqueue(PipeMessage)` | inner JSON снимка процессов |
| `Service/Pipe/EventPipeServer.cs:147` | `Serialize(List<PipeMessage>)` | outer JSON, повторное экранирование всего снимка |
| `Service/Pipe/PipeServer.cs:210,276,481,526,541,560,739` | `Serialize(response)` | то же на request/response канале |
| `UI/Services/PipeClient.cs:159,178` | `Serialize(request)` / `Deserialize<PipeMessage>` | outer |
| `UI/Services/PipeClient.cs:112,122,200,581` | `Deserialize<List<PipeMessage>>` + `Deserialize<SystemPerformanceSnapshot>(msg.Payload)` | inner, строка 122 — вторая десериализация внутри цикла по батчу |

Эффект:

* +10–25% к размеру пакета только за счёт экранирования (зависит от плотности кавычек/путей/CLI; у `CommandLine` и `FilePath` кавычек много).
* 2× CPU сериализации и 2× больших `string`-аллокаций на каждый срез. Сериализатор нельзя заинлайнить через source-gen для `object`, вся работа — через рефлексию при каждом тике.
* `JsonOptions` везде только `PropertyNameCaseInsensitive = true` (`PipeMessage.cs` — нет вообще, `EventPipeServer.cs:27-30`, `PipeServer.cs:66-69`, `PipeClient.cs:25-28`). Нет `DefaultIgnoreCondition.WhenWritingNull`, поэтому `null`-поля (`SignerName`, `SignatureIssuer`, `StartupEntryId` и т.д.) гоняются в каждом пакете впустую.

### 1.2. Фрейминг строкой: нет стриминга, нет length-prefix

Отправка везде — `StreamWriter.WriteLine(json)`:

* `EventPipeServer.cs:155-156` (`WriteLine` + лишний `Flush` при `AutoFlush = true`, стр. 76),
* `PipeServer.cs:211` (`WriteLineAsync`),
* `PipeClient.cs:160,168` (`WriteLineAsync` / `ReadLineAsync`).

Приём — `ReadLineAsync` (`PipeServer.cs:203`, `PipeClient.cs:109,168`).

Проблемы:

* Весь снимок обязан материализоваться как одна contiguous `string` + одна строка UTF-8. Частичное чтение/парсинг (`JsonDocument`, `Utf8JsonReader`, `PipeReader`) невозможны.
* `PipeTransmissionMode.Message` (`EventPipeServer.cs:67`, `PipeServer.cs:160`) при использовании `StreamReader/StreamWriter` игнорируется — границы сообщений теряются, всё держится на `\n`. Любой `\n` внутри payload (теоретически в `CommandLine`) ломает фрейминг.
* `inBufferSize: 0, outBufferSize: 0` — дефолтные 4 КБ буферы ОС. Пакет 500 КБ режется на ~125 системных чанков, но собирается обратно в один LOH-`string` на приёмнике.
* `EventPipeServer.OnMessagesBatchReady:145` — синхронный `void`-обработчик на потоке `Timer` из `NotificationService.FlushBatch` (`NotificationService.cs:92,104`). `Serialize` батча блокирует таймер; медленный клиент в цикле `151-162` тормозит рассылку всем остальным (head-of-line blocking, нет per-client очередей и backpressure).
* `PipeClient.SendAsync:46,143-188` — глобальный `SemaphoreSlim(1,1)` на все команды. `ResponseTimeout = 70с` (стр. 49) держит семафор, блокируя все остальные команды (`GetAllEntries`, `KillProcess` и т.д.). При зависшем `GetProcesses` UI на 70 с теряет управление пайпом.

Попутные дефекты в том же файле:

* `EventPipeServer.cs:77`: `pipe.GetHashCode()` как ID клиента — возможны коллизии, нужен `Interlocked.Increment`.
* `EventPipeServer.cs:76`: `new StreamWriter(pipe)` без явного `Encoding.UTF8` и размера буфера + `AutoFlush=true` = syscall на каждую запись.
* `PipeClient.cs:102-105`: при реконнекте `_eventReader` не dispose — утечка; `_eventPipe?.Dispose()` без dispose старого `_eventReader`.

---

## 2. LOH / GC Gen2: каждый тик 1.5 с — гарантированный LOH-churn

Порог LOH: ~85 000 байт (~83 КБ для `string` — 42.5K chars × 2 байта).

Оценка размера `SystemPerformanceSnapshot` (`Shared/Models/SystemPerformanceSnapshot.cs:37` + `ProcessInfo`, см. §3):

* 200–300 процессов × ~1–2 КБ JSON на процесс (`FilePath` + `CommandLine` + `Publisher` + `Description` + метрики) = **300–600 КБ inner JSON**.
* Outer JSON после экранирования — ещё +10–20% → **350–720 КБ**.

Источники тиков:

* `Service/Services/ProcessMetricsTracker.cs:58-66` — `Subscribe()` включает `_pollTimer.Change(0, 1500)`.
* `PipeServer.cs:123-134` — `SnapshotReady → Serialize(snapshot) → Enqueue`.
* `NotificationService.cs:19,34,92` — flush батча каждые 200 мс, т.е. снимок уходит почти сразу.
* `EventPipeServer.cs:147` — повторный `Serialize(batch)`.

Итого на **один тик**:

1. `Serialize(snapshot)` → `string` ~500 КБ (LOH, Gen2).
2. `Serialize(List<PipeMessage>)` → `string` ~600 КБ (LOH).
3. `StreamWriter` UTF-8 `byte[]` ~600 КБ (LOH).
4. На клиенте: `ReadLineAsync` → `string` ~600 КБ (LOH) + inner `Payload string` ~500 КБ (LOH) + объекты `List<ProcessInfo>` (~300 объектов + строки).

Частота: 40 тиков/мин → **~2400 снапшотов/час**, только строк LOH — порядка **2 × 500 КБ × 2400 ≈ 2.4 ГБ/час на сторону** (сервер + клиент). LOH по умолчанию не компактируется → фрагментация, дорогие Gen2-паузы. На машине с 8 ГБ это видимые фризы UI при сортировке/поиске по таблице процессов.

Усугубление:

* Heartbeat `EventPipeServer.cs:115-126` каждые 30 с шлёт `Serialize([])` даже без подписчиков — мелочь, но будит таймер и пайп.
* `GetProcesses` (`PipeServer.cs:735-740`, `PipeClient.cs:572-585`) дублирует тот же путь вне подписки: `CaptureSnapshot()` — синхронный полный сбор + двойная сериализация в request/response канале, конкурирует с event-каналом за тот же `_sendLock` на клиенте.

---

## 3. `ProcessInfo`: статические строки гоняются каждые 1.5 с

`Shared/Models/ProcessInfo.cs`:

Статические (не меняются за жизнь PID+StartTime), строк 25–27 в объекте:

* `33-35 ProcessName`, `37-39 FilePath`, `41-43 CommandLine`, `47-48 Publisher`, `58-60 SignerName`, `62-64 SignatureIssuer`, `130-132 StartTime`, `190-192 Description`, `162-172 ParentProcessName/StartupLocationDetail`, `202-212 ThreatInfo`, флаги `IsSigned/SignatureStatus/IsSystemProcess/IsService`.

Динамические (единственное, что реально меняется за 1.5 с):

* `66-76 CpuUsagePercent`, `78-88 WorkingSetBytes`, `90-92 PrivateBytes`, `94-128 DiskRead/Write, NetworkBytesPerSec`, `198-200 IsSuspended`.

Сейчас `CollectSnapshotCore` (`ProcessMetricsTracker.cs:153-293`) создаёт **новый полный `ProcessInfo` на каждый процесс каждый тик** и сериализует всё целиком. Кэш `ProcessMetadataEnricher` (`ProcessMetadataEnricher.cs:16-28, 49-68`) экономит Win32-запросы, но **не экономит передачу** — закэшированные `FilePath/CommandLine/Publisher/SignerName/Issuer` всё равно копируются в каждый `info` (стр. 51–59) и уходят по пайпу.

Дополнительные издержки DTO:

* `ProcessInfo:10` — `INotifyPropertyChanged` на IPC-DTO. Каждое свойство идёт через `Set<T>` с `EqualityComparer<T>.Default` + `PropertyChanged?.Invoke` (`17-23`). Для десериализованных 300 объектов × 25 свойств = 7500 сравнений + аллокаций `PropertyChangedEventArgs` на тик в UI-потоке. DTO для провода не должен реализовывать `INPC` — это задача ViewModel.
* Дубли полей-алиасов (`294-318`: `Pid/Name/ExecutablePath/CpuPercent/DiskIoBytesPerSec/IsStartupEntry`) — не сериализуются (`[JsonIgnore]`), но засоряют модель и копируются.
* `TreeLevel/HasChildren/IsExpanded/ChildrenCount` (`320-357`) — уже `[JsonIgnore]`, правильно, но вычисляются на клиенте заново при каждом снапшоте — лишняя работа при flat-tree на 300 узлов.

Отдельно — **гонка**: `EnrichOrQueue(info, ...)` (`ProcessMetadataEnricher.cs:121`) кладёт в `_enrichQueue` **ту же ссылку `ProcessInfo`**, которая уже добавлена в `processInfos` (`ProcessMetricsTracker.cs:286`) и позже сериализуется в `SnapshotReady → Serialize` (`PipeServer.cs:130`). Фоновый `ProcessEnrichmentLoopAsync:125-219` мутирует `proc.FilePath/Publisher/IsSigned/...` (стр. 152–193) **конкурентно с сериализацией**. Это torn-read: в один тик может уехать полузаполненная подпись, в следующий — полная, без изменения версии.

Также `EnrichOrQueue:107-118` кэширует метаданные **до** проверки подписи (пустой `Publisher`, `IsSigned=false`), а полный вариант перезаписывает кэш позже (стр. 196–209). Первые 1–3 тика после старта процесса рассылают `IsSigned=false/Unknown`, затем внезапное «появление» подписи — UI мигает бейджами.

---

## 4. `JsonDataStore`: синхронный I/O + SHA256 на чтении

Файл: `Shared/Models/JsonDataStore.cs` (в задаче указан неверный путь `Shared/Storage/` — фактически `Shared/Models/`).

### 4.1. Синхронный дисковый I/O под глобальным `lock`

* `lock (_lock)` — **один мьютекс на всё**: `Load:85`, `Save:292`, `AddEntry:320`, `UpdateEntry:454`, `AddHistory:471`, все `Removed/Snapshot` методы.
* Внутри лока — блокирующий I/O: `File.ReadAllText:125,153,171`, `File.WriteAllText:227,248`, `File.AppendAllText:257`, `File.Move:118,142,159,249`.
* Горячий путь пайпа вызывает это напрямую: `PipeServer.GetAllEntries:262-269` (`DeduplicateEntries + Load + Save`), `GetBlacklist:539`, `GetRatings:549`, `DisableEntry:296,298`, `AddToBlacklist:384-385`. Поток `HandleClientAsync` (`PipeServer.cs:191-234`) блокируется на диске (а диск — `C:\Program Files\...` под антивирусом). Один медленный `Save` стопарит все параллельные `HandleClientAsync`.
* `Save(config):290-300` всегда пишет **все 4 секции** (`SaveEntries/SaveBlacklist/SaveRemoved/SaveSettings`), даже если изменилась одна. Каждые 5-сек опросы UI (`GetAllEntries`), делающие `Save` (стр. 269), переписывают 4 файла.
* `_options.WriteIndented = true` (стр. 68–72) — файлы на 20–30% больше и медленнее парсятся, без нужды для машинных секций.

### 4.2. SHA256 на каждое чтение и каждую запись

* `ContentHash:237-239` — `UTF8.GetBytes(content)` (аллокация всего файла) + `SHA256.HashData`.
* `WriteSectionAtomic:242-251` — хэш перед записью (оправданно, пропуск неизменённого).
* `ReadSection:148-162` — **тот же SHA256 на чтении** (стр. 154: `_writtenHashes[path] = ContentHash(json)`). Чтение, которое по определению не меняет файл, платит полный хэш + аллокацию байт-массива. При `Load()` — 4–5 хэшей (entries + blacklist + removed + settings + snapshots отдельно).
* SHA256 — криптографический, ~100+ МБ/с с overhead; для change-detection достаточно xxHash32/CRC32/FNV-1a в ~10–20× быстрее. На файле `startup-entries.json` в сотни КБ это миллисекунды на каждый `Load`, умноженные на частоту опросов.

### 4.3. История и снапшоты — самые дорогие места

* `ReadHistory:165-234` — `File.ReadAllText` всего `history.jsonl`, затем построчный `Deserialize` в `try/catch` (стр. 184–197): **исключения как управление потоком** для каждой legacy-многострочной записи. Плюс `multiLineBuffer.ToString().Trim()` (стр. 201) на каждую строку незавершённого объекта — O(n²) на битых файлах. Миграция (стр. 222–230) — полная перезапись файла прямо внутри чтения под локом.
* `AddHistory:469-481` — делает `Load()` (все секции + их SHA256!) ради одного `AppendHistoryLine` (одна строка в конец). Горячее событие вотчеров → тяжёлый `Load` + `AppendAllText` под глобальным локом.
* `TrimHistoryFile:261-272` — при старте переписывает весь history-файл, если есть хоть одно старое событие.
* `GetSnapshots/GetSnapshot/SaveSnapshot/DeleteSnapshot:571-617` — **каждый вызов читает `snapshots.json` с диска** (`ReadSection`), игнорируя `_cache`. `GetSnapshots` ещё и сортирует (`OrderByDescending`, стр. 576) при каждом опросе UI.
* `DeduplicateEntries:385-438` — O(n) со строковыми ключами `$"{Name}|{Location}|{normDetail}"` + `Split('\\')` в `NormalizeLocationDetail:372` на каждую запись, вызывается на каждый `GetAllEntries` (PipeServer:262) и `GetRatings` (стр. 548).

---

## 5. Предлагаемые оптимизации (с оценкой)

Приоритет по соотношению «эффект / сложность».

### P0 — Дельта-снапшоты процессов (убирает LOH из горячего пути)

Разделить контракт:

```csharp
record ProcessStaticInfo(int Pid, long StartTicks, string Name, string Path,
    string Cmd, string Publisher, string Signer, ...); // 1 раз на жизнь процесса
record ProcessDelta(int Pid, float Cpu, long Ram, long Disk, long Net, bool Suspended);
message SnapshotTick { long TickId; Delta[] Deltas; int[] Removed; StaticInfo[] Added; }
```

Сервер хранит `Dictionary<(Pid,StartTicks), StaticInfo>` и шлёт статику только для новых PID; клиент мержит по PID. Пути/CLI dictionary-encode (таблица строк, индекс вместо повтора).

*Эффект:* 350–600 КБ → 15–40 КБ на тик (−90–95%), пакеты уходят из LOH в Gen0, Gen2-паузы исчезают. Сетевой/pipe трафик −~1 ГБ/час. Сложность средняя (версионирование `TickId`, full-sync по запросу при рассинхроне).

Дополнительно: убрать `INotifyPropertyChanged` из `ProcessInfo` (оставить чистый DTO + source-gen `JsonSerializerContext`), маппить в ViewModel на клиенте. −7500 сравнений/событий на тик.

### P1 — Убрать двойную сериализацию, ввести length-prefix + прямую запись в поток

* Заменить `Payload: string` на типизированный envelope (`JsonPolymorphism` / `JsonDerivedType`) или хотя бы `Payload: JsonElement` с `JsonSerializer.SerializeToUtf8Bytes` напрямую в `pipe` через `BinaryWriter.Write(length)+bytes`.
* `SerializeAsync(pipeStream)` вместо `Serialize → string → WriteLine`; приём — `PipeReader + Utf8JsonReader`, без промежуточных `string`.
* Включить `DefaultIgnoreCondition.WhenWritingNull`, `NumberHandling`, source-gen контекст.

*Эффект:* −30–40% CPU сериализации, −50% больших аллокаций (исчезает outer-string), −10–25% размера, конец проблеме `\n` в payload. Сложность средняя, обратно совместимо через версионирование `Type`.

Параллельно починить рассылку: per-client очереди с дропом устаревших снапшотов (оставлять только последний тик), `AutoFlush=false` + явный flush раз в батч, ID клиента через `Interlocked`.

### P2 — `JsonDataStore`: убрать I/O из-под запроса

1. Убрать `ContentHash` из `ReadSection` (стр. 154) — хэш только на записи. Заменить SHA256 на xxHash/FNV для change-detection (−10–20× на хэше).
2. `WriteIndented = false` для data-секций (оставить pretty только для `settings.json`, если нужно человеку).
3. Разделить локи по секциям (`ReaderWriterLockSlim` на entries/blacklist/removed/settings/snapshots/history) вместо глобального `lock`.
4. `AddHistory` — без `Load()`: append в файл + `lock`-добавление в in-memory список; `Trim` — фоновым таймером, не в `Load`.
5. Дебаунс `Save`: dirty-флаг + coalesce 500 мс, `WriteAllTextAsync` + `FileStream(..., useAsync:true)`. `Save(config)` пишет только грязные секции.
6. Кэшировать `snapshots.json` в памяти с инвалидацией по записи; убрать дисковое чтение из `GetSnapshots/GetSnapshot`.
7. `ReadHistory` — без exception-driven парсинга: сначала пробовать `JsonDocument.Parse`, различать JSONL/legacy по первому символу, `ArrayPool<char>`/потоковое чтение вместо `ReadAllText + ToString()` в цикле.

*Эффект:* `GetAllEntries`/`GetBlacklist` перестают упираться в диск и SHA256; латентность команд −50–80-й перцентиль в разы (особенно под AV); дисковые перезаписи −70% (только грязные секции, без indent). Сложность низкая–средняя.

### P3 — Снять гонку обогащения и мигание подписей

* Не класть в `_enrichQueue` живую ссылку из снапшота: очередь хранит `(Pid, StartTicks)`, worker пишет только в `_cache`; `CollectSnapshotCore` читает из кэша, отсутствующее — помечает `SignatureStatus=Pending`, не шлёт `false`.
* Не кэшировать неполные метаданные (стр. 107–118) как финальные — разделить `FastCache` (путь/CLI) и `FullCache` (подпись/издатель).
* `PruneDeadProcesses:222-231` итерирует `_cache.Keys` (снапшот словаря) каждый тик — заменить на очередь завершившихся PID из `activePids`-diff, который уже посчитан (стр. 300–311).

*Эффект:* детерминированные подписи с первого полного тика, нет torn-read при сериализации, −1 полный обход кэша каждые 1.5 с.

---

### Сводка узких мест (для поиска по коду)

* Двойной JSON: `PipeMessage.cs:12-14`; `PipeServer.cs:130,210,276`; `EventPipeServer.cs:147`; `PipeClient.cs:112,122,159,178,200,581`.
* Строковый фрейминг: `PipeServer.cs:203,211`; `EventPipeServer.cs:155`; `PipeClient.cs:109,160,168`.
* Тик 1.5 с: `ProcessMetricsTracker.cs:58-66,89-104,123-134,153-297`.
* Полный `ProcessInfo` каждый тик: `ProcessInfo.cs:33-64,130-212`; кэш не спасает передачу: `ProcessMetadataEnricher.cs:49-68,107-121`.
* Гонка enrich/serialize: `ProcessMetadataEnricher.cs:121-122 vs 152-193` + `PipeServer.cs:130`.
* Синхронный I/O + глобальный lock: `JsonDataStore.cs:85,292,320,454,471` + `153,171,248,257`.
* SHA256 на чтении: `JsonDataStore.cs:154,237-239`; на записи: `242-251`.
* История/снапшоты: `JsonDataStore.cs:165-234,261-272,469-481,571-617`.

Если делать только две вещи — делать **P0 (дельты)** и **P2.1–P2.4 (хэш/лок/append истории)**: они дают ~90% выигрыша по GC/пайпам и убирают блокировки командного канала при минимальном риске регрессий.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m✗ [0mRead src/StartupMonitor.Shared/Storage/JsonDataStore.cs failed
[91m[1mError: [0mFile not found: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Storage\JsonDataStore.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/PipeMessage.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/EventPipeServer.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/ProcessInfo.cs
[0m→ [0mRead src/StartupMonitor.UI/Services/PipeClient.cs
[0m✱ [0mGlob "**/JsonDataStore*"[90m in . · 2 matches[0m
[0m→ [0mRead src/StartupMonitor.Shared/Models/JsonDataStore.cs
[0m✱ [0mGlob "**/*.cs"[90m in src · 100 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/SystemPerformanceSnapshot.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/NotificationService.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetadataEnricher.cs

```
