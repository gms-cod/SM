# OpenCode Swarm Agent 5: IPC Pipes, Large Object Heap (LOH) & Storage (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 84.8 сек
- Статус: Успешно

---

Разбираю протокол Pipe и хранилище — меряю аллокации и LOH.
Файл хранилища не найден по указанному пути — ищу фактическое расположение.
Основные файлы разобраны — теперь оцениваю размер снимков и частоту отправки.
## Аудит GC / LOH / IPC: Named Pipe + `JsonDataStore`

### 1. Протокол Pipe: двойная упаковка + `StreamWriter/Reader`

**Конверт `Shared/Models/PipeMessage.cs:6-19`:**

```csharp
class PipeMessage { string Type; string Payload; DateTime Timestamp; }
// Payload — это *уже сериализованный JSON*, вложенный строкой
```

Цепочка на горячем пути (`Service/Pipe/PipeServer.cs:137-141`, `UI/Services/PipeClient.cs:121-131`):

```
snapshot → Serialize(snapshot) : string (N байт)
  → new PipeMessage{ Payload = thatString }
    → Serialize(PipeMessage) : string (~1.3×N из-за экранирования `\"`)
      → WriteLine(string) → Flush()
```

Что это стоит:

| Дефект | Где | Эффект |
|---|---|---|
| Двойная сериализация + экранирование | `PipeServer.cs:140`, `HandleClientAsync:220`, `PipeClient.cs:163,182`, `EventPipeServer.cs:149` | ~2× CPU сериализации, +25–35% к размеру (каждая `"` в inner JSON → `\"`). Для снимка 300 КБ → ~400 КБ по проводу |
| `string` как единица фрейминга | везде `ReadLineAsync` / `WriteLineAsync` | Нет zero-copy: нельзя арендовать `byte[]` из пула, нельзя стримить. Каждая сторона держит минимум 2 копии (inner + outer) + 1 при десериализации |
| `new StreamReader(pipe)` / `new StreamWriter(pipe){AutoFlush=true}` без буфера и кодировки | `PipeServer.cs:205-206`, `PipeClient.cs:72-73,108`, `EventPipeServer.cs:78` | Дефолтный буфер 1024 chars, `AutoFlush=true` = syscall `Flush` на каждую строку. `WriteLine+json` + явный `Flush()` в `PipeClient.cs:164-165` — двойной flush |
| `PipeTransmissionMode.Message` + `inBufferSize:0/outBufferSize:0` | `PipeServer.cs:170-173`, `EventPipeServer.cs:69-72` | Message-режим игнорируется при чтении через `StreamReader.ReadLine` (stream-режим). Нулевые буферы = системный дефолт 4 КБ, фрагментация больших кадров на десятки `Read` |
| Синхронный broadcast на таймере | `EventPipeServer.cs:148-164` `writer.WriteLine+json; writer.Flush()` в цикле по `_clients` | Голова-очередь блокирует всех: один медленный UI stalls `FlushBatch`-поток `NotificationService`. `OnMessagesBatchReady` — `void`, исключение одного клиента ловится, но время уже потеряно |
| `JsonSerializerOptions` на инстанс без SourceGen | 4 копии опций (`PipeServer:75`, `EventPipeServer:28`, `PipeClient:27`, `JsonDataStore:69`) | Рефлексия + `PropertyNameCaseInsensitive=true` на каждом тике. Нет `JsonSerializerContext` → кэш метаданных медленнее, больше Gen0 |
| `Timestamp = DateTime.UtcNow` по умолчанию + ISO-8601 с дробью | `PipeMessage.cs:18` | ~30 байт на каждое сообщение впустую; для батча из 1 снапшота — мелочь, но для частых `ActionNotice` — мусор |

Отдельно — гонка, найденная рядом: `ProcessMetadataEnricher:121` кладёт **ту же ссылку `ProcessInfo`** в очередь обогащения, которая уже лежит в `processInfos` и сериализуется в `SnapshotReady` (`PipeServer.cs:133-144`). Фон обогащения мутирует `FilePath/Publisher/IsSigned` конкурентно с `Serialize` → torn-read (в один тик подпись полупустая, в следующий — полная).

### 2. LOH (>85 000 байт): снимок `SystemPerformanceSnapshot`

**Состав:** `SystemPerformanceSnapshot.cs:37` → `List<ProcessInfo>`, `ProcessInfo.cs` — ~30 свойств, из них тяжёлые строки: `FilePath`, `CommandLine` (у браузеров/electron — килобайты), `Description`, `Publisher`, `SignerName`, `SignatureIssuer`, `ParentProcessName`, `StartupLocationDetail` + `ThreatInfo` + enum-строки.

Оценка (250 процессов × ~0.8–1.5 КБ JSON):

* inner `Serialize(snapshot)` ≈ **200–450 КБ string → LOH** (порог 85 КБ превышен в 3–5×);
* outer `Serialize(PipeMessage)` ≈ **260–600 КБ string → второй LOH-объект**;
* на приёме (`PipeClient.cs:112,125`): `line: string` (LOH) + `List<PipeMessage>` + `msg.Payload: string` (LOH) + `SystemPerformanceSnapshot` + 250× `ProcessInfo` + 250×N `string` полей.

Итого **4–6 LOH-объектов на тик**, тик каждые **1.5 с** (`ProcessMetricsTracker.cs:69` `_pollTimer.Change(0,1500)`):

```
~0.5 МБ × 2 стороны × 40 тиков/мин ≈ 40 МБ/мин LOH-трафика только на диспетчер задач
```

LOH не компактируется по умолчанию → фрагментация, затем частые **Gen2 Full GC** (пауза, CPU-пила в службе под SYSTEM). Усугубляется тем, что `ProcessInfo : INotifyPropertyChanged` (`ProcessInfo.cs:11-23`) едет по проводу как DTO: делегат `PropertyChanged`, `Set<T>` с `EqualityComparer<T>.Default` и `OnPropertyChanged` на каждое свойство при десериализации — лишний Gen0-мусор и ложные пробуждения биндингов в UI.

Тот же класс проблем у `GetAllEntries/GetEntriesDelta` (`PipeServer.cs:287,327`): полный `List<StartupEntry>` (у `StartupEntry.cs` ~20 полей + `DisablePayload`, `CommandLine`, `SHA256`) сериализуется целиком при каждом опросе, хотя `NotModified`-путь (`PipeClient.cs:231`) уже частично смягчает.

### 3. `JsonDataStore` (`Shared/Models/JsonDataStore.cs`)

Хранилище секционное — это хорошо. Проблемы в мутациях:

| Строка | Дефект |
|---|---|
| `Save():293-305` → `SaveEntries+SaveBlacklist+SaveRemoved+SaveSettings` | Любой чих (`UpdateEntry:458`, `SetKillOnDisable`, `SetLanguage`) сериализует и хеширует **все 4 секции**. `UpdateEntry` одной записи на 200 записей = 4× сериализация всего файла |
| `WriteSectionAtomic:246-254` + `ContentHash:240-242` | `Serialize → string → UTF8.GetBytes → SHA256.HashData → HexString` — **3 полных прохода** по содержимому ради проверки «изменилось ли». Сам `GetBytes` уже LOH для файлов >85 КБ. Хеш считается даже когда `Save` вызван из read-пути без изменений |
| `GetAllEntries:273-280` и `GetEntriesDelta:317-321` | **Save-on-read**: `DeduplicateEntries() → Load → ResolveAndEnrich → Save` на каждый опрос UI. Чтение превращается в 4 сериализации + 4 SHA256 + дисковые `Move`. При поллинге 5 с — постоянный дисковый и CPU-шум |
| `DeduplicateEntries:391-444` | Полный ребилд `Dictionary<string,StartupEntry>` с интерполированным ключом `$"{Name}\|{Location}\|{detail}"` на каждый вызов + `NormalizeLocationDetail` (`Replace`, `Split`, `Join`). Это Gen0-шторм строк ради проверки, которая нужна только при вставке |
| `Load:85-112` возвращает `_cache` напрямую | Общий мутабельный синглтон без копии: `ProcessMessage`-обработчики мутируют `cfg.Entries` (`entry.Status=...`) внутри `lock`, но `_linker.UpdateIndex`, вотчеры и `Save` видят тот же список — гонки итерирования (`ToList()` в `PipeServer.cs:278` — костыль-следствие) |
| `ReadSection:156` `File.ReadAllText` + `ReadHistory:174` весь файл в `string` | Для `startup-entries.json` >85 КБ — LOH на каждое чтение. `ReadHistory` дополнительно `StringReader` + **попытка `Deserialize` каждой строки с `try/catch`** (исключения как control flow для legacy-многострочки) + `StringBuilder` на строку |
| `TrimHistoryFile:264-275` вызывается из `Load` | Перезапись `history.jsonl` целиком при каждом старте/загрузке, если есть протухшие записи. Должно быть фоновым daily, а не на горячем пути |
| `_options WriteIndented=true:71` | Индентация раздувает файлы ~30% → больше байт хешировать, писать, читать. Для machine-store indent не нужен |

### 4. Решения (по приоритету, с оценкой эффекта)

**P0 — убрать двойную упаковку без смены протокола (1–2 ч, −50% CPU IPC, −30% трафика):**
* Ввести `PipeMessage<T>` / `JsonElement Payload` вместо `string Payload`: сериализовать конверт и нагрузку **одним проходом** через `Utf8JsonWriter`. Обратная совместимость: старый клиент шлёт `string`, новый — объект; на приёме пробовать `Deserialize<T>` напрямую, при неудаче — fallback на `string`-развёртку.
* Перейти на `JsonSerializerContext` (SourceGen) для `PipeMessage`, `SystemPerformanceSnapshot`, `ProcessInfo`, `StartupEntry`, `EntriesDeltaResponse`. Убрать `PropertyNameCaseInsensitive` с горячего пути (договориться о `snake_case` или `camelCase` один раз).

**P0 — байтовый фрейминг + `ArrayPool` (4–6 ч, LOH→0 на транспорте):**
* `PipeTransmissionMode.Byte`, кадр = `Int32 LE length + UTF8 body`. Отправка: `SerializeToUtf8Bytes(obj, ctx)` в арендованный `ArrayPool<byte>` буфер → один `WriteAsync`. Приём: арендовать по длине, `ReadExactlyAsync`, `Deserialize(ReadOnlySpan<byte>)`, вернуть в пул. `StreamReader/Writer` на hot path удалить полностью (оставить только для редких команд).
* В `EventPipeServer.OnMessagesBatchReady`: сериализовать батч **один раз в `byte[]`**, рассылать `WriteAsync` параллельно (`Task.WhenAll`), мёртвых чистить без блокировки живых. Heartbeat `[]` каждые 30 с уже есть — оставить.
* Backpressure для снапшотов: семантика latest-wins — если предыдущий `SnapshotReady` ещё сериализуется/отправляется, дропать промежуточный тик (флаг `Interlocked`, уже есть `_isCollecting` в трекере — довести до pipe).

**P1 — дельта-снапшоты процессов (1 день, −80–90% LOH-трафика диспетчера):**
* Разделить `ProcessInfo` на **static** (PID→path, publisher, signature, commandline — шлётся один раз при `Subscribe`/`GetProcesses` + по изменению) и **dynamic** (cpu, ram, io, net, sockets — каждый тик). Тик = `{ seq, upsert: [{pid,cpu,ram,io,net}], removed: [pid] }`.
* Альтернатива-минимум без протокола v2: не слать `CommandLine` длиннее 512 символов (триммировать на сервере), исключить `Description` для `svchost`-клонов (восстановимо в UI по `IsService`), `DefaultIgnoreCondition=WhenWritingNull` уже включён — добавить `WhenWritingDefault` для `CpuUsagePercent==0`, `NetworkBytesPerSec==0`.
* Разорвать `INotifyPropertyChanged` на проводе: wire-DTO — plain `sealed class`/`record` без событий, маппинг в VM только в UI-потоке. Убирает `PropertyChanged`-шторм при десериализации 250 объектов × 30 свойств.

**P1 — `JsonDataStore`: писать только грязные секции, не хешировать строками (0.5 дня):**
* `Save(config)` → `SaveChanges()` с dirty-флагами: `UpdateEntry` трогает только `startup-entries.json`. Убрать `SHA256` поверх всего файла — достаточно сравнения ревизии/счётчика (`Interlocked` revision уже есть `_entriesRevision` в `PipeServer`) или `string.Equals` по кэшированному JSON при малых файлах.
* Писать `FileStream + SerializeAsync(stream, ctx)` напрямую, без промежуточного `string`. `WriteIndented=false` для store-файлов.
* Убрать `Save` из `GetAllEntries`/`GetEntriesDelta` read-пути: `DeduplicateEntries` — только при вставке (инкрементальный `HashSet` индекс), `ResolveAndEnrich(computeHash:false)` — с `_enrichCache` уже есть, но не должен триггерить `Save` при отсутствии изменений (сейчас `Save` безусловный → перезапись + смена mtime → ложные `FileSystemWatcher`-циклы).
* `Load()` — возвращать копию или read-only view; `ReadHistory` — потоковое `File.ReadLines` + одна попытка парса (без exception-flow), миграцию legacy — одноразовым скриптом, не в каждом `Load`. `TrimHistoryFile` — в фоновый daily-timer.

**P2 — опционально (когда P0/P1 исчерпаны):**
* Brotli/Gzip для кадров >64 КБ (снимки жмутся в 4–6× из-за повторяющихся путей), флаг в заголовке кадра.
* Раздельные `JsonSerializerContext` для IPC (compact, `camelCase`, `WhenWritingDefault`) и для store (стабильный `snake_case` для совместимости файлов).
* Метрика для проверки: счётчики `GC.GetGenerationCollections` / `GC.GetTotalAllocatedBytes` в `ExecutionProfiler`-отчёте (уже есть 5-мин авторепорт) + размер кадра в байтах в debug-лог `PipeServer`.

Ожидаемый итог P0+P1: транспорт снимков с ~500 КБ строк/тик → ~30–60 КБ дельты, **0 LOH-объектов** на транспорте (пулы), `Gen2`-сборы по причине IPC исчезают; запись `UpdateEntry` с 4 файлов + 4 SHA256 → 1 файл append/stream, дисковый шум на read-пути — ноль.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m✗ [0mRead src/StartupMonitor.Shared/Storage/JsonDataStore.cs failed
[91m[1mError: [0mFile not found: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Storage\JsonDataStore.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/PipeMessage.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/EventPipeServer.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs
[0m→ [0mRead src/StartupMonitor.UI/Services/PipeClient.cs
[0m✱ [0mGlob "**/JsonDataStore*"[90m 2 matches[0m
[0m✱ [0mGrep "class JsonDataStore|class SystemPerformanceSnapshot|class ProcessInfo"[90m 5 matches[0m
[0m→ [0mRead src/StartupMonitor.Shared/Models/ProcessInfo.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/SystemPerformanceSnapshot.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/JsonDataStore.cs
[0m✱ [0mGrep "class StartupEntry|class StartupRating|class NotificationService|Enqueue|SnapshotReady|Subscribe\(\)|Interval|Timer.*metric|_batch"[90m 58 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/NotificationService.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/StartupEntry.cs[90m [limit=120][0m

```
