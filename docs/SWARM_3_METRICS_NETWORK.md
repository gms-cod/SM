# OpenCode Swarm Agent 3: ProcessMetricsTracker & Network Sockets Audit (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 87.7 сек
- Статус: Успешно

---

Разбираю трекеры метрик и сети — ищу лишние аллокации и риски хэндлов.
Нашел горячие точки в трекерах — теперь смотрю смежные вызовы снимков и обогащения.
## Аудит: `ProcessMetricsTracker` + `ProcessNetworkTracker` + `ProcessMetadataEnricher`

### 1. Аллокации на тик (1.5 с, 300 процессов)

| Место | Что аллоцируется | Цена |
|---|---|---|
| `MetricsTracker:130` `Process.GetProcesses()` | 300× `Process` + 300× native handle + `ProcessName` string | ~300 `OpenProcess` + 300 строк. Главный источник `Gen0`. `proc.ProcessName` дёргается 4 раза (`142,182,287,303`) — кэшировать в локальную переменную. |
| `MetricsTracker:141-142` `HashSet + Dictionary` | 2 коллекции × 300/тик | Мелочь, но легко переиспользовать через `Clear()`. |
| `MetricsTracker:149,326` `ConcurrentBag<ProcessInfo> + [..processInfos]` | `ConcurrentBag` — самая медленная коллекция (thread-local + глобальный lock), + копия в массив | Заменить на `ProcessInfo[]` + индексный `Parallel.For`. |
| `MetricsTracker:178` `new ProcessInfo` | `ProcessInfo : INotifyPropertyChanged`, `Set()` на каждое поле → 15+ `PropertyChangedEventArgs` на процесс, хотя в службе биндинга нет, только JSON-сериализация в Pipe | В службе это чистый оверхед + давление на GC. Либо DTO без `INPC`, либо `init`-объект. |
| `NetworkTracker:43-44,57` 3× `Dictionary` на тик | `currentSocketBytes<string,ulong>`, `accumulated`, `processSpeeds` | Плюс `string key = $"4:{...}"` на каждый `ESTABLISHED`-сокет — главный шторм `Gen0`. |
| `NetworkTracker:82,83,150,151` `AllocHGlobal(size) + AllocHGlobal(rodSize)` | 6× `AllocHGlobal/FreeHGlobal` на тик (TCPv4+v6 + 2×rod + UDPv4+v6) | `FreeHGlobal` не возвращает в пул, каждый тик — syscall `HeapAlloc/Free`. Переиспользовать буферы. |
| `NetworkTracker:96,164` `Marshal.PtrToStructure<T>` на строку | Для IPv6 — 2× `byte[16]` на строку + бокс структуры | 300 сокетов × 2 массива × каждые 1.5 с. Перейти на `unsafe`-каст / `MemoryMarshal`. |
| `Enricher:81-93` первый проход | `new StringBuilder(1024)` на процесс + `AllocHGlobal(returnLength)` в `TryGetProcessCommandLine` + 2× `NtQueryInformationProcess(60)` | После прогрева кэша `(Pid,StartTime)` — 0. Но первый проход выполняется **синхронно в `Parallel.ForEach`** и блокирует снимок. |
| `MetricsTracker:329,336,342` `ToList() + _history.Keys` | Снапшот `ConcurrentDictionary` + `List` каждые 1.5 с | `GetSuspendedPids().ToList()` + 2 прохода по `_history`/`_inaccessiblePids`. Можно один проход. |

### 2. Хэндлы и задержки

**Утечка есть, одна — критичная:**

```csharp
// ProcessNetworkTracker.cs:292-293 — GetSockets()
var p = Process.GetProcessById(pid); // никогда не Dispose!
var name = p.ProcessName + ".exe";
```

Каждый открытый инспектор сокетов течёт хэндлами `Process`. Фикс — `using`:

```csharp
using var p = Process.GetProcessById(pid);
```

Остальное корректно закрывается (`MetricsTracker:225 CloseHandle`, `Enricher:256 CloseHandle`, `Linker:255 CloseHandle(snapshot)`, `ServiceHelper:119 CloseServiceHandle`), утечек нет.

**Задержки / зависания:**

1. `MetricsTracker:211 OpenProcess(0x1000)` × 300/тик. На PPL/Protected (`csrss, lsass, MsMpEng, System`) и античитах (`Vanguard, EAC`) — `ERROR_ACCESS_DENIED`, `hProcess == Zero`. Дальше `Enricher:95 TryGetProcessPath(pid)` делает **второй** `OpenProcess` на тот же PID — двойной syscall вхолостую. Нужен негативный кэш.
2. `_inaccessiblePids` (`237-250`) уже кэширует `TotalProcessorTime/StartTime`-фейлы — хорошо. Но ключ — только `PID`, очистка — раз в тик по `activePids`. При реюзе PID новым процессом в пределах окна — ложный пропуск. Ключ должен быть `(Pid, StartTime)` или сбрасываться при исчезновении PID (уже делается, `342-346` — ок, но гонка остаётся).
3. `Enricher:288-317 NtQueryInformationProcess(h, 60)` — `returnLength` приходит из ядра/драйвера без капа. Античит-хук может вернуть гигантское значение → `AllocHGlobal(huge)` → `OOM`/ступор. Обязательно кап `64*1024`. Сам вызов на замороженном/защищённом процессе может висеть десятки мс и блокирует поток из `Parallel.ForEach`.
4. `NetworkTracker:113,182 SetPerTcpConnectionEStats` вызывается **на каждый ESTABLISHED-сокет каждый тик**. Это лишний kernel-переход (~5-10 мкс × 200 сокетов = 1-2 мс/тик впустую). Вызывать только для новых ключей.
5. **Баг-коллизия IPv6-ключа (`190`):** `key = $"6:{dwLocalPort}-{dwRemotePort}"` — адреса игнорируются. Два разных соединения с одинаковыми портами схлопываются, дельта байт неверна. Ключ обязан включать адреса.
6. **Гонка данных `Enricher:123 _enrichQueue.Enqueue(proc)`:** в очередь кладётся живой `ProcessInfo`, который уже ушёл в снимок и сериализуется в Pipe. Фоновый воркер (`134-211`) мутирует `proc.FilePath/Publisher` после отправки — UI увидит только на следующем тике + data race на `INPC`. В очередь класть `(pid, startTime)`, не объект.
7. Порядок в `MetricsTracker:221 vs 242`: `EnrichOrQueue` вызывается **до** чтения `StartTime`, поэтому ключ кэша `(Pid, Ticks)` для недоступных процессов всегда `(pid, 0)` — stale-hit при реюзе PID.

### 3. Как оптимизировать под 300+ процессов

**A. Сеть: struct-ключ + переиспользование буферов + EStats только для новых:**

```csharp
private readonly record struct SocketKey(ulong Lo, ulong Hi, ushort LocalPort, ushort RemotePort, byte Family);

private Dictionary<SocketKey, ulong> _prevSocketBytes = new(256);
private IntPtr _tcpBuf = IntPtr.Zero; private int _tcpBufSize;
private IntPtr _rodBuf = IntPtr.Zero; // аллоцируется один раз под _rodSize

private IntPtr Rent(int need, ref IntPtr buf, ref int cap)
{
    if (cap < need)
    {
        if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        cap = (int)(need * 1.5);
        buf = Marshal.AllocHGlobal(cap);
    }
    return buf;
}
// в Collect: сначала GetExtendedTcpTable(IntPtr.Zero, ref size...),
// затем Rent(size,...), повторный вызов при ERROR_INSUFFICIENT_BUFFER(122).
```

```csharp
// только для новых сокетов:
if (!_prevSocketBytes.ContainsKey(key))
    SetPerTcpConnectionEStats(ref tcpRow, ...);
uint getRes = GetPerTcpConnectionEStats(...);
```

Пропуск неактивных: `socketCounts` считать всегда (дешево), а `EStats`-байты — только для `ESTABLISHED`. UDP вообще без `EStats` — уже так.

**B. Метрики: массив + кэш имени + один `OpenProcess`:**

```csharp
var procs = Process.GetProcesses();
var infos = new ProcessInfo[procs.Length];
Parallel.For(0, procs.Length, _parallelOptions, i =>
{
    var proc = procs[i];
    try
    {
        string name = proc.ProcessName; // один раз
        // ... весь body, OpenProcess один раз, EnrichOrQueue только при cache-miss
    }
    finally { proc.Dispose(); }
});
```

Плюс негативный кэш `OpenProcess`-фейлов:

```csharp
if (_openFailPids.Contains(pid)) { _enricher.EnrichFromCacheOnly(info); return; }
IntPtr h = OpenProcess(...);
if (h == IntPtr.Zero) { _openFailPids.TryAdd(pid, 0); ... }
```

**C. Enricher: кап + вынос из горячего цикла:**

```csharp
int status = NtQueryInformationProcess(hProcess, 60, IntPtr.Zero, 0, out int len);
if (len <= 0 || len > 64 * 1024) return string.Empty;
```

А `GetProcessPathFromHandle/StringBuilder(1024)` и `NtQuery(60)` вообще убрать из `Parallel.ForEach` — оставить только `TryGetValue(cache)`, всё остальное в throttled-воркере (уже есть `5 шт/30 мс` — расширить до `10 шт/15 мс` для 300 процессов: догон за ~0.5 с вместо 1.8 с).

**D. `ProcessInfo` в службе:** убрать `INPC`-нотификации на горячем пути — завести `ProcessInfoDto` (plain auto-properties) для Pipe, маппить в UI в `ProcessInfo` с `INPC`. Либо флаг `SuppressNotify` на время заполнения.

### 4. Оценка эффекта

| Оптимизация | CPU | RAM/GC |
|---|---|---|
| Struct-ключ вместо `string $"..."` + `Dictionary<string,ulong>` | −60-70% времени `CaptureNetworkBandwidth` (~8-12 мс → 2-4 мс на 200 сокетах) | −90% `Gen0` в сети (главный аллоген) |
| Reuse `HGlobal`-буферов (4-6 аллокаций/тик → 0) | −1-2 мс/тик, нет `HeapAlloc`-шторма | −~1 МБ/с churn |
| `SetPerTcpConnectionEStats` только для новых | −1-2 мс/тик | 0 syscall в стационаре |
| `ProcessInfo[] + Parallel.For` вместо `ConcurrentBag` | −15-20% времени `CollectSnapshotCore` | нет contention `ConcurrentBag` |
| Один `ProcessName` + один `OpenProcess` + негативный кэш | −200 лишних syscalls/с (двойные `OpenProcess` на защищённых) | меньше `Win32Exception`-объектов |
| Кап `NtQuery(60)` 64К + вынос enrich из горячего цикла | убирает выбросы 50-150 мс на античитах/PPL | нет `AllocHGlobal(huge)` |
| DTO без `INPC` в службе | −5-10% сериализации | −4500 `EventArgs`/тик при 300 проц. |

Итого на 300 процессах: снимок `Service.ProcessMetrics.Collect` ориентировочно **25-40 мс → 8-15 мс**, `Service.Network.CaptureBandwidth` **10-15 мс → 2-5 мс**, суммарный CPU службы в стационаре падает примерно **в 2-3 раза** (с текущих ~27 с стартового окна по профайлеру — до ~10 с), `Gen0`-аллокации сети/метрик — **−80-90%**.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetadataEnricher.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/ProcessInfo.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
[0m✱ [0mGrep "GetProcessSnapshotMap|IsProcessSuspended|GetRunningServicesByPid|GetParentProcessId"[90m 18 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ServiceProcessHelper.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessStartupLinker.cs

```
