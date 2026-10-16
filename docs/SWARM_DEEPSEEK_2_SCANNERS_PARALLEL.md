# Субагент 2 (DeepSeek V4.1 Flash): Многопоточное распараллеливание сканеров автозапуска по ядрам CPU

- Модель субагента: `opencode/deepseek-v4.1-flash-free`
- Время выполнения: 153.7 сек
- Статус: Успешно

---

I'll start by exploring the target files and their dependencies to conduct the audit.
The target paths differ. Let me locate the actual scanner files.
The actual scanner is `SystemScannerService.cs` plus the Watcher classes. Let me read the core files.
Let me read the supporting files that drive the scan pipeline.
Let me check how ScanAll is invoked and whether any throttling/parallelism already exists.
Let me examine the remaining hotspots: language/PE detection, CPU throttle helper, and any existing profiler logs.
# Технический аудит: параллелизм и узкие места сканеров автозапуска

Версия кодовой базы: **1.7.3** (Directory.Build.props). Целевые файлы задачи переименованы в репозитории — фактическое соответствие:

| Задача (концепт) | Фактический тип/метод | Файл:строка |
|---|---|---|
| `StartupScanner` | `SystemScannerService.ScanAll` | `SystemScannerService.cs:63` |
| `RegistryScanner` | `ScanRegistry` / `ScanHiveRegistry` / `ScanOtherLocations` / `ScanClsidEntries` | `SystemScannerService.cs:160,183,245,438` |
| `TaskSchedulerScanner` | `ScanTaskScheduler` | `SystemScannerService.cs:714` |
| `WmiScanner` | `ScanWmiSubscriptions` + `WmiSubscriptionHelper.ScanSubscriptions` | `SystemScannerService.cs:692`, `WmiSubscriptionHelper.cs:83` |
| `StartupFolderScanner` | `ScanStartupFolders` | `SystemScannerService.cs:577` |
| `AuthenticodeVerifier` | без изменений | `AuthenticodeVerifier.cs` |

---

## 0. Главный вывод (TL;DR)

1. **`ScanAll` полностью синхронный и однопоточный** (`SystemScannerService.cs:63` — нет ни одного `Task`/`Parallel`/`Channel`). Все секции выполняются строго последовательно на вызывающем потоке.
2. **Реальное узкое место — не реестр и не CLSID, а `EntryOperations.ResolveAndEnrichEntry`** (`EntryOperations.cs:858`), который вызывается **для каждой** записи и внутри себя синхронно делает `WinVerifyTrust` + парсинг PE + чтение версии + определение языка. CLSID-скан уже оптимизирован до активных хендлеров — «>7000 записей» там нет.
3. **`ScanAll` вызывается прямо в обработчике pipe-сообщения** (`PipeServer.cs:661, 2068, 2165`) — долгий скан блокирует поток ThreadPool и весь pipe-сервер (UI ждёт до 60 c, `PipeServer.cs:622`).
4. **Прежде чем распараллеливать — обязательно починить три общих кэша**, иначе параллелизм их сломает/уронит производительность:
   - `AuthenticodeVerifier.Cache.Clear()` при >2000 (`AuthenticodeVerifier.cs:141`) — thundering herd + stampede;
   - `EntryOperations.AddToEnrichCache` — `OrderBy().Take(500)` на каждой вставке при >5000 (`EntryOperations.cs:837`);
   - `WatcherHelper.GetUserProfiles()` — `SecurityIdentifier.Translate` (может блокироваться на AD/сети), вызывается в цикле по каждому AppX-пакету (`AppxStartupTaskHelper.cs:25`).

---

## 1. Карта горячих участков и модель стоимости

### 1.1 Замер через существующую инструментацию

Проект уже содержит zero-alloc профайлер `ExecutionProfiler` (`ExecutionProfiler.cs:51`), отчёт сбрасывается каждые 5 мин (`DrainReport`, `ExecutionProfiler.cs:136`). Уже размечены:

```
Service.ScanAll              SystemScannerService.cs:65
Service.ScanRegistry         SystemScannerService.cs:162
Service.ScanClsid            SystemScannerService.cs:440
Service.ScanServices         SystemScannerService.cs:650
Service.ScanWmi              SystemScannerService.cs:694
Service.ScanTaskScheduler    SystemScannerService.cs:716
Service.ScanAppx             SystemScannerService.cs:773
Service.EntryOps.ResolveEnrich  EntryOperations.cs:860
Shared.Authenticode.VerifyFile  AuthenticodeVerifier.cs:121
```

**Пробелы в разметке (нужно добавить):** `Service.ScanOtherLocations`, `Service.ScanStartupFolders`, `Service.EntryOps.ComputeSha256`, и «теневые» секции внутри `ResolveAndEnrichEntry` (`ResolveExecutablePath`, `ProgramLangDetector.Detect`, `FileVersionInfo`). Без них `Service.EntryOps.ResolveEnrich` покажет суммарное время, но не скажет, кто внутри виновен.

### 1.2 Модель стоимости (порядок величин на одну запись)

Значения — типовые для cache-only WinVerifyTrust (`WTD_REVOKE_NONE` + `WTD_CACHE_ONLY_URL_RETRIEVAL`, см. `AuthenticodeVerifier.cs:31-34`) на прогретом кэше ОС. Первый холодный вызов дороже в разы.

| Операция | Где | Тип | Порядок | Масштабируется по ядрам? |
|---|---|---|---|---|
| `WinVerifyTrust` (кэш) | `AuthenticodeVerifier.cs:173` | Win32, блокирующий | 0.5–5 мс | **Плохо** (внутренние локи CryptoAPI) |
| `X509Certificate.CreateFromSignedFile` | `AuthenticodeVerifier.cs:202` | Парсинг PE + ASN.1 | 0.3–3 мс | частично |
| `GetFileVersionInfo*` ×3 | `ProgramLangDetector.cs:44-51` | kernel32/version.dll + `new byte[size]` | 0.2–1 мс | да |
| `FileVersionInfo.GetVersionInfo` | `EntryOperations.cs:996` | чтение PE | 0.3–2 мс | да |
| `ResolveExecutablePath` | `EntryOperations.cs:1032` | несколько `File.Exists` | 0.05–0.5 мс | да |
| `ScanTaskScheduler` (`AllTasks` + `.Definition.Actions`) | `SystemScannerService.cs:719-725` | COM, XML deserialize/задача | 1–20 мс **на задачу** | **Нет** (COM apartment) |
| `ScanServices` (WMI `Win32_Service`) | `SystemScannerService.cs:653` | WMI sync | 100–600 мс разово | нет смысла |
| `ScanWmiSubscriptions` | `WmiSubscriptionHelper.cs:90` | WMI sync | 50–300 мс разово | нет смысла |
| `ScanAppx` (`Directory.GetDirectories(WindowsApps)` + XDocument.Load) | `SystemScannerService.cs:781-839` | FS + XML | 0.5–5 мс/пакет × N | да |
| `XDocument.Load(manifest)` | `SystemScannerService.cs:839` | XML | 1–10 мс/пакет | да |

**Итоговая картина:** при 150–300 записях автозапуска доминирует `N × (Authenticode + Lang + Version)` ≈ 150–300 × 1.5–8 мс ≈ **0.3–2.5 с** чистого последовательного времени только на обогащение, поверх разовых WMI/COM-секций. Это и есть «тормоз», а не реестр.

### 1.3 CLSID / меню проводника — узкое место уже устранено на уровне дизайна

Историческая проблема «>7000 записей» — это полный обход `HKCR\CLSID`. В `ScanClsidEntries` (`SystemScannerService.cs:438`) сделано правильно: сначала собирается множество **активных** хендлеров только из 6 веток `shellex\ContextMenuHandlers` (`:442-450`), и лишь для них открывается `CLSID\{GUID}\InprocServer32|LocalServer32` (`:515-553`). То же в комментариях `:35-39` (GPExtensions 289, AppPaths 53, Drivers32 и т.д. сознательно не сканируются).

Остаточная стоимость = `activeHandlers.Count × ResolveAndEnrichEntry` — снова упирается в обогащение, а не в перечисление CLSID. **Отдельного «медленного CLSID-скана» в текущем коде нет**, и это не точка приложения усилий по параллелизму.

---

## 2. Проблемы, критичные ДО распараллеливания

### 2.1 `AuthenticodeVerifier`: нет `GetOrAdd`, грубый `Clear`

```csharp
// AuthenticodeVerifier.cs:134-145
if (Cache.TryGetValue(cacheKey, out var cached)) return cached;
var result = PerformVerification(cleanPath);   // ← дубли при гонке
Cache[cacheKey] = result;
if (Cache.Count > 2000) { Cache.Clear(); Cache[cacheKey] = result; }  // ← stampede
```

При параллельном обогащении (которое мы хотим ввести) несколько потоков одновременно промахиваются по одному и тому же файлу и **параллельно** вызывают `WinVerifyTrust` — а он сам сериализуется внутри CryptoAPI, т.е. мы получим деградацию, а не ускорение. `Cache.Clear()` сбрасывает весь кэш → после каждого сброса «холодный» прогон. Нужен `GetOrAdd` + Lazy-дедупликация + bounded-эвикция.

### 2.2 `_enrichCache`: O(N log N) на вставку

```csharp
// EntryOperations.cs:840-852
if (_enrichCache.Count > EnrichCacheCapacity) {
    var oldest = _enrichCache.OrderBy(kv => kv.Value.CacheTimestamp)  // сортировка всего словаря
                     .Take(500).Select(kv => kv.Key).ToList();
    ...
}
```

При конкурентных вставках это выполняется на каждой записи сверх лимита и берёт блокировку на CPU. Параллельный обогащение без починки этого уронит throughput.

### 2.3 `WatcherHelper.GetUserProfiles()` — N+1 и потенциальный блокирующий `Translate`

```csharp
// WatcherHelper.cs:40
name = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;  // может уйти в сеть/AD
```

Вызывается **для каждого пакета** в `AppxStartupTaskHelper.ReadState` (`AppxStartupTaskHelper.cs:25`) — `N_packages × M_profiles` трансляций SID. То же в `EntryOperations.OpenRegistry` (`EntryOperations.cs:97`). Нужен единый снимок профилей на один проход скана + `Lazy`/мемоизация трансляции.

### 2.4 Синхронный `ScanAll` на потоке pipe-сервера

`PipeServer.cs:661, 2068, 2165` вызывают `_scanner.ScanAll(...)` из async-метода **без** `Task.Run`/`await`. Это блокирует поток ThreadPool на всё время скана; при конкуренции с `CpuUsageHelper.GetOurProcessesTicks()` (перечисляет все процессы, `CpuUsageHelper.cs:214`) и WMI — риск thread starvation. Плюс периодические `BackgroundCleanupLoop` каждые 5 мин (`PipeServer.cs:2151`) повторяют это же.

---

## 3. Схема конвейера (Producer-Consumer через `Channel<T>`)

Принцип: **«быстрые» производители** (реестр, папки, WMI, планировщик) только *находят* сырые записи и кладут их в канал; **«тяжёлые» потребители** (DOP = пул воркеров) параллельно выполняют `ResolveAndEnrichEntry`. Это развязывает I/O реестра (по природе частично последовательный) и дорогую CPU/kernel-валидацию.

```
               ┌──────────────────────── ПРОИЗВОДИТЕЛИ (I/O, параллельно) ─────────────────────────┐
               │                                                                                    │
 HKLM ─────────┤ ScanHiveRegistry(HKLM)        ┐                                                  │
 HKCU ─────────┤ ScanHiveRegistry(HKCU)        │ Parallel.ForEachAsync, DOP = ProcessorCount      │
 HKEY_USERS ───┤ ScanHiveRegistry(SID_i)       ┘  (реестр — разные кусты, независимы)              │
               │                                                                                    │
 StartupFolders┤ ScanStartupFolders ───────────┐                                                  │
 Other+CLSID ──┤ ScanOtherLocations+CLSID ──────┤  Task.WhenAll (разные подсистемы)                │
 AppX ─────────┤ ScanAppxStartupTasks ──────────┘                                                  │
               │                                                                                    │
 TaskSch/COM ──┤ ScanTaskScheduler ═══════════ dedicated thread (COM, сериализовано) ────────────┤──► raw
 Services/WMI ─┤ ScanServices / ScanWmiSubscriptions (WMI, сериализовано в WMI-группе) ──────────┤   StartupEntry
               └────────────────────────────────────────────────────────────────────────────────────┘
                                          │  Writing (bounded capacity = DOP × 4)
                                          ▼
                              ╔═══════════════════════════════╗
                              ║  Channel<StartupEntry>        ║   ← backpressure: продюсеры ждут,
                              ║  (BoundedChannelOptions)      ║     если потребители не успевают
                              ╚═══════════════════════════════╝
                                          │  Reading (N = DOP)
                                          ▼
               ┌───────────────── ПОТРЕБИТЕЛИ (CPU/kernel, DOP ядер) ──────────────────┐
               │  ResolveExecutablePath → ComputeSha256? → Authenticode (SemaphoreSlim) │
               │    → ProgramLangDetector → FileVersionInfo → _enrichCache              │
               │  SemaphoreSlim(4) вокруг WinVerifyTrust — защита от сериализации CryptoAPI
               └────────────────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
                                  List<StartupEntry> results
                                  (+ _failedLocations)
```

Ключевые правила:
- **Реестровые кусты** (HKLM/HKCU/HKEY_USERS) независимы → `Parallel.ForEachAsync`.
- **COM (Task Scheduler) и WMI — отдельная сериализованная группа**: `ITaskService` не потокобезопасен и привязан к apartment; WMI-сеансы тоже плохо масштабируются. Их запускаем как одну `Task`, но параллельно с реестрово-файловой группой.
- **`SemaphoreSlim` вокруг `WinVerifyTrust`**: ограничивает число одновременных kernel-вызовов, они не масштабируются линейно.

---

## 4. Готовые примеры кода (C# / net8.0)

### 4.1 Параллельный обход кустов реестра — `Task.WhenAll` + `Parallel.ForEachAsync`

```csharp
// SystemScannerService.cs — замена ScanRegistry (синхронный, :160)
private async Task ScanRegistryAsync(ChannelWriter<StartupEntry> sink, CancellationToken ct)
{
    using var _prof = ExecutionProfiler.Measure("Service.ScanRegistry");

    // Снимок профилей — ОДИН раз на весь скан (устраняет N+1 из п.2.3)
    var profiles = WatcherHelper.GetUserProfiles();

    var hives = new List<(RegistryKey Base, string Prefix, bool Wow64)>
    {
        (Registry.LocalMachine, "HKLM", true),
        (Registry.CurrentUser,  "HKCU", false),
    };

    // HKEY_USERS открываем в самом воркере (свой handle на итерацию)
    var sidList = profiles.Select(p => (p.Sid, $@"HKCU\{p.Name}")).ToList();

    var dop = Math.Clamp(Environment.ProcessorCount, 2, 8);
    var opts = new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct };

    // HKLM + HKCU
    await Parallel.ForEachAsync(hives, opts, async (h, token) =>
    {
        await ScanHiveRegistryAsync(h.Base, h.Prefix, h.Wow64, sink, token);
    });

    // Все пользовательские кусты — параллельно
    await Parallel.ForEachAsync(sidList, opts, async (item, token) =>
    {
        try
        {
            using var userKey = Registry.Users.OpenSubKey(item.Sid);
            if (userKey == null) return;
            await ScanHiveRegistryAsync(userKey, item.Item2, allowWow64: false, sink, token);
        }
        catch { /* чужой SID / выгрузка куста TOCTOU — пропускаем профиль */ }
    });
}
```

`ScanHiveRegistryAsync` — это текущий `ScanHiveRegistry` (`SystemScannerService.cs:183`), который вместо `results.Add(entry)` пишет в канал:

```csharp
private static async Task ScanHiveRegistryAsync(
    RegistryKey baseKey, string hivePrefix, bool allowWow64,
    ChannelWriter<StartupEntry> sink, CancellationToken ct)
{
    foreach (var (keyPath, location) in RegistryLocations)
    {
        ct.ThrowIfCancellationRequested();
        if (!allowWow64 && keyPath.Contains("Wow6432Node", StringComparison.OrdinalIgnoreCase))
            continue;

        try
        {
            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) continue;
            var detailPrefix = $@"{hivePrefix}\{keyPath}";

            string[] valueNames;
            try { valueNames = key.GetValueNames(); } catch { continue; }

            foreach (var valueName in valueNames)
            {
                string? value;
                try { value = key.GetValue(valueName) as string; } catch { continue; }
                if (string.IsNullOrWhiteSpace(value) || !WatcherHelper.LooksLikeCommand(value)) continue;

                var entry = MakeRegistryEntry(valueName, value, location, detailPrefix, keyPath);
                await sink.WriteAsync(entry, ct);   // ← вместо results.Add + синхронного Enrich
            }
        }
        catch { continue; }
    }
}
```

> **Важно:** `MakeRegistryEntry` (`SystemScannerService.cs:225`) сейчас сам вызывает `ResolveAndEnrichEntry`. В конвейере обогащение убирается **из производителя** и переносится в потребителя. `MakeRegistryEntry` возвращает «сырую» запись (только `Id` через `ComputeStableId`).

### 4.2 Конвейер Producer-Consumer на `System.Threading.Channels.Channel<StartupEntry>`

```csharp
using System.Threading.Channels;

public async Task<List<StartupEntry>> ScanAllAsync(
    ILogger? logger = null, bool skipRegistry = false, CancellationToken ct = default)
{
    using var _prof = ExecutionProfiler.Measure("Service.ScanAll");
    _failedLocations.Clear();

    var dop = Math.Clamp(Environment.ProcessorCount, 2, 8);

    // Bounded — backpressure, чтобы производители не раздували очередь в RAM
    var channel = Channel.CreateBounded<StartupEntry>(new BoundedChannelOptions(dop * 4)
    {
        SingleReader = false,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });

    // ── Потребители: тяжёлое обогащение ──
    var consumers = Enumerable.Range(0, dop).Select(_ => Task.Run(async () =>
    {
        await foreach (var entry in channel.Reader.ReadAllAsync(ct))
        {
            try { EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false); }
            catch { /* одна запись не должна ронять воркер */ }
        }
    }, ct)).ToArray();

    // ── Производители: I/O-секции ──
    var producers = new List<Task>();
    if (!skipRegistry)
    {
        producers.Add(ScanRegistryAsync(channel.Writer, ct));
        producers.Add(Task.Run(() => ScanOtherLocationsProducer(channel.Writer, ct), ct));
    }
    // COM и WMI — сериализованная группа на одном потоке
    producers.Add(Task.Run(() =>
    {
        ScanTaskSchedulerProducer(channel.Writer, ct);
        ScanServicesProducer(channel.Writer, ct);
        ScanWmiSubscriptionsProducer(channel.Writer, ct);
    }, ct));
    producers.Add(Task.Run(() => ScanStartupFoldersProducer(channel.Writer, ct), ct));
    producers.Add(ScanAppxStartupTasksProducerAsync(channel.Writer, logger, ct));

    try
    {
        await Task.WhenAll(producers);      // все производители завершились…
        channel.Writer.Complete();          // …закрываем канал
    }
    finally
    {
        channel.Writer.TryComplete();
    }

    await Task.WhenAll(consumers);          // дожидаемся дренажа канала

    var results = /* собрать из потокобезопасного накопителя или из отдельных списков */;
    logger?.LogInformation("Scanner: всего просканировано {Total} записей", results.Count);
    return results;
}
```

### 4.3 Потокобезопасный сбор результатов

`List<StartupEntry>` не потокобезопасен. Либо канал читается в один `ConcurrentBag`, либо каждый воркер пишет локально и мы сливаем. Предпочтительно — вернуть из воркеров локальные списки:

```csharp
var bag = new System.Collections.Concurrent.ConcurrentBag<StartupEntry>();
...
await foreach (var entry in channel.Reader.ReadAllAsync(ct))
{
    // ... enrich ...
    bag.Add(entry);
}
...
var results = bag.ToList();
```

### 4.4 Защита `WinVerifyTrust` семафором + кэш через `GetOrAdd`

```csharp
public static class AuthenticodeVerifier
{
    // Ограничение параллелизма: kernel-вызовы CryptoAPI сериализуются внутри Windows,
    // поэтому >4-8 одновременных вызовов бесполезны и лишь плодят потоки.
    private static readonly SemaphoreSlim Throttle = new(Math.Clamp(Environment.ProcessorCount, 2, 8));

    // Значение-Lazy: дедупликация одновременных проверок одного файла
    private static readonly ConcurrentDictionary<string, Lazy<SignatureVerificationResult>> Cache
        = new(StringComparer.OrdinalIgnoreCase);
    private static int _cacheCount;

    public static SignatureVerificationResult VerifyFile(string? filePath)
    {
        using var _prof = ExecutionProfiler.Measure("Shared.Authenticode.VerifyFile");
        if (string.IsNullOrWhiteSpace(filePath))
            return new(DigitalSignatureStatus.Unknown, false, null, null, "Путь не указан");

        var cleanPath = filePath.Trim().Trim('"');
        try
        {
            if (!File.Exists(cleanPath))
                return new(DigitalSignatureStatus.Unknown, false, null, null, "Файл не найден");

            long lastWriteTicks = File.GetLastWriteTimeUtc(cleanPath).Ticks;
            var key = $"{cleanPath}|{lastWriteTicks}";

            // GetOrAdd + Lazy: при гонке тяжёлую работу выполнит только один поток
            var lazy = Cache.GetOrAdd(key, k => new Lazy<SignatureVerificationResult>(
                () => PerformVerificationThrottled(cleanPath),
                LazyThreadSafetyMode.ExecutionAndPublication));

            // Учёт размера и мягкая эвикция (без Cache.Clear!)
            if (lazy.IsValueCreated && Interlocked.Increment(ref _cacheCount) > 4000)
                TrimCache();

            return lazy.Value;
        }
        catch (Exception ex)
        {
            return new(DigitalSignatureStatus.Unknown, false, null, null, ex.Message);
        }
    }

    private static SignatureVerificationResult PerformVerificationThrottled(string path)
    {
        Throttle.Wait();
        try { return PerformVerification(path); }   // существующая логика :158
        finally { Throttle.Release(); }
    }

    private static void TrimCache()
    {
        // Удаляем ~четверть, не сортируя: убираем «старые» по Ticks в ключе
        foreach (var k in Cache.Keys.Take(1000))
            if (Cache.TryRemove(k, out _))
                Interlocked.Decrement(ref _cacheCount);
    }
}
```

### 4.5 `ArrayPool<byte>` для хеширования файлов (путь `computeHash:true`)

`ComputeSha256` (`EntryOperations.cs:1087`) сейчас использует `SHA256.HashData(stream)` — это ОК (внутренние буферы не аллоцируются в LOH). Но `ProgramLangDetector.Detect` (`ProgramLangDetector.cs:47`) на каждый вызов делает `new byte[size]`, где `size` — размер всей version-секции (может быть десятки КБ) → давление на GC при сотнях записей. Пример замены:

```csharp
using System.Buffers;

public static string Detect(string filePath)
{
    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return "en";

    int size = GetFileVersionInfoSizeW(filePath, out _);
    if (size <= 0) return "en";

    byte[]? rented = null;
    try
    {
        rented = ArrayPool<byte>.Shared.Rent(size);
        var span = rented.AsSpan(0, size);
        if (!GetFileVersionInfoW(filePath, 0, size, rented)) return "en";
        if (!VerQueryValueW(rented, @"\VarFileInfo\Translation", out var buf, out var len) || len < 4)
            return "en";

        // читаем язык прямо из неуправляемого буфера, без Marshal.Copy в новый массив
        Span<byte> lang = stackalloc byte[4];
        Marshal.Copy(buf, lang.ToArray(), 0, 4);   // 4 байта — стек не вариант для Marshal.Copy, массив мал
        ushort langId = (ushort)(lang[0] | (lang[1] << 8));
        return CultureInfo.GetCultureInfo(langId).TwoLetterISOLanguageName;
    }
    catch { return "en"; }
    finally
    {
        if (rented != null) ArrayPool<byte>.Shared.Return(rented);
    }
}
```

> Примечание: `GetFileVersionInfoW` принимает `byte[]` — `ArrayPool.Rent` возвращает массив длиной ≥ `size`, что допустимо (API пишет ровно `size` байт).

### 4.6 Task Scheduler на выделенном потоке (COM-безопасность)

```csharp
// COM/ITaskService не потокобезопасен — запускаем строго на одном выделенном потоке,
// но параллельно с реестрово-файловой группой.
private void ScanTaskSchedulerProducer(ChannelWriter<StartupEntry> sink, CancellationToken ct)
{
    using var _prof = ExecutionProfiler.Measure("Service.ScanTaskScheduler");
    try
    {
        using var ts = new TaskService();
        var tasks = ts.AllTasks;
        if (tasks == null) return;

        foreach (var task in tasks)
        {
            ct.ThrowIfCancellationRequested();
            var taskCmd = FormatTaskAction(task);
            var entry = new StartupEntry
            {
                Name = task.Name,
                FilePath = taskCmd,
                CommandLine = taskCmd,
                Location = EntryLocation.TaskScheduler,
                LocationDetail = task.Path ?? string.Empty,
                Status = task.Enabled ? EntryStatus.Enabled : EntryStatus.Disabled,
            };
            entry.Id = WatcherHelper.ComputeStableId(entry);
            sink.TryWrite(entry);   // обогащение — в потребителе
        }
    }
    catch { _failedLocations.Add(EntryLocation.TaskScheduler); }
}
```

### 4.7 Починка эвикции `_enrichCache`

```csharp
// EntryOperations.cs:837 — вместо OrderBy().Take(500) на каждой вставке
private static void AddToEnrichCache(string key, EnrichCacheValue value)
{
    _enrichCache[key] = value;
    if (_enrichCache.Count <= EnrichCacheCapacity) return;

    // Амортизированная эвикция: раз в ~256 вставок убираем пакет по «возрасту».
    if (Interlocked.Increment(ref _insertCounter) % 256 != 0) return;

    var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(10);
    foreach (var kv in _enrichCache)
        if (kv.Value.CacheTimestamp < cutoff)
            _enrichCache.TryRemove(kv.Key, out _);
}
private static int _insertCounter;
```

---

## 5. Балансировка Thread Pool и защита от I/O-насыщения

| Решение | Значение | Обоснование |
|---|---|---|
| DOP для файловых/реестровых производителей | `Math.Clamp(Environment.ProcessorCount, 2, 8)` | реестр/чтение файлов — I/O-bound, но драйвер реестра имеет внутренние локи; >8 не даёт выигрыша |
| Число потребителей обогащения | `= DOP` | CPU/kernel-bound |
| Семафор вокруг `WinVerifyTrust` | `Math.Clamp(ProcessorCount, 2, 8)` | CryptoAPI сериализуется сам; лишние потоки только жгут стек |
| Ёмкость bounded-канала | `DOP * 4` | backpressure без раздувания RAM (записи мелкие, но их сотни) |
| COM/WMI-группа | 1 поток, сериализовано | `ITaskService`/`ManagementScope` не потокобезопасны |
| Синхронный `ScanAll` в pipe | убрать; вызывать `await ScanAllAsync(...)` | устраняет thread starvation (`PipeServer.cs:661,2068,2165`) |
| Буферы version-ресурсов | `ArrayPool<byte>` | снимает GC-давление `new byte[size]` (`ProgramLangDetector.cs:47`) |
| `ScanStartupFolders` | `Directory.EnumerateFiles` вместо `GetFiles` | потокобезопаснее, не материализует массив (`SystemScannerService.cs:617`) |

**Чего делать НЕ надо:**
- `Parallel.ForEach` поверх `ScanTaskScheduler`/WMI — COM-апартамент-ошибки и деградация.
- Увеличивать DOP выше `ProcessorCount` для потребителей обогащения — упрётесь в `WinVerifyTrust`, не в ядра.
- `Task.Run` вокруг каждой отдельной записи — 300 задач на ThreadPool вызовут starvation; нужен именно bounded-канал с фиксированным числом воркеров.

---

## 6. План внедрения (инкрементальный, с регресс-контролем)

1. **Разметка замера** (полчаса): добавить `ExecutionProfiler` в `ScanOtherLocations`, `ScanStartupFolders`, `ResolveExecutablePath`, `ProgramLangDetector.Detect`, `ComputeSha256`. Собрать `DrainReport` с реальной машины — это даст фактические цифры вместо модели §1.2.
2. **Починка кэшей** (без изменения поведения): `AuthenticodeVerifier` `GetOrAdd`+`SemaphoreSlim` (§4.4), `AddToEnrichCache` (§4.7), мемоизация `GetUserProfiles` на проход.
3. **`ScanAllAsync`** с bounded-каналом (§4.2), сохранив синхронную обёртку `ScanAll` для тестов (`SystemScannerVerificationTests.cs:469` вызывает `ScanAll()` синхронно).
4. **Перевод вызовов в `PipeServer`** на `await ScanAllAsync` (3 места).
5. **Прогон:** `dotnet test` (xUnit) — существующие `SystemScannerVerificationTests` и `ExecutionProfilerTests` должны остаться зелёными; сверить `Service.ScanAll` до/после.

**Ожидаемый эффект:** обогащение (доминирующая фаза) масштабируется от ~1× до ~2.5–4× на 4–8 ядрах с учётом семафора CryptoAPI; WMI/COM-секции остаются разовыми, но перестают блокировать файловую группу; `ScanAll` перестаёт занимать поток pipe-сервера.

---

## 7. Риски

- **`ConcurrentDictionary.Clear()` убрать обязательно** — иначе параллельный прогон вызовет полный сброс кэша под нагрузкой.
- **TOCTOU HKEY_USERS** (`SystemScannerService.cs:169-178`) сохраняется; `try/catch` уже есть, при параллелизме важнее не ронять весь `WhenAll` — оборачивать каждую итерацию (в §4.1 так и сделано).
- **`_failedLocations` (`HashSet`) не потокобезопасен** — записывать в него только из сериализованных/единственных секций либо заменить на `ConcurrentDictionary<EntryLocation, byte>`.
- **COM-апартамент:** нельзя `Parallel.ForEach` по задачам планировщика — только выделенный поток (§4.6).


### Логи выполнения / Предупреждения:
```
[0m
> build · deepseek-v4.1-flash-free
[0m
[0m✗ [0mRead src/StartupMonitor.Service/Scanners/RegistryScanner.cs failed
[91m[1mError: [0mFile not found: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Scanners\RegistryScanner.cs
[0m✗ [0mRead src/StartupMonitor.Service/Scanners/TaskSchedulerScanner.cs failed
[91m[1mError: [0mFile not found: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Scanners\TaskSchedulerScanner.cs
[0m✗ [0mRead src/StartupMonitor.Service/Scanners/WmiScanner.cs failed
[91m[1mError: [0mFile not found: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Scanners\WmiScanner.cs
[0m✗ [0mRead src/StartupMonitor.Service/Scanners/StartupFolderScanner.cs failed
[91m[1mError: [0mFile not found: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Scanners\StartupFolderScanner.cs
[0m✗ [0mRead src/StartupMonitor.Service/Services/StartupScanner.cs failed
[91m[1mError: [0mFile not found: d:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\StartupScanner.cs
[0m→ [0mRead src/StartupMonitor.Shared/Helpers/AuthenticodeVerifier.cs
[0m✱ [0mGlob "**/*Scanner*.cs"[90m 4 matches[0m
[0m✱ [0mGlob "**/Scanners/**/*.cs"[90m 0 matches[0m
[0m✱ [0mGlob "src/StartupMonitor.Service/**/*.cs"[90m 51 matches[0m
[0m✱ [0mGlob "src/StartupMonitor.Shared/**/*.cs"[90m 52 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/SystemScannerService.cs
[0m→ [0mRead src/StartupMonitor.Shared/Diagnostics/ExecutionProfiler.cs
[0m→ [0mRead src/StartupMonitor.Shared/Models/StartupEntry.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/WatcherHelper.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/AppxStartupTaskHelper.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/Wmi/WmiSubscriptionHelper.cs
[0m✱ [0mGrep "ScanAll|SystemScannerService|skipRegistry"[90m 42 matches[0m
[0m✱ [0mGrep "Parallel\.|Task\.WhenAll|Channel<|ArrayPool|ForEachAsync"[90m 11 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [offset=620, limit=120][0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [offset=2040, limit=160][0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs[90m [offset=1069, limit=180][0m
[0m✱ [0mGlob "**/logs/*.txt"[90m 0 matches[0m
[0m✱ [0mGrep "ExecutionProfiler\.(Measure|Profile)\("[90m 67 matches[0m
[0m→ [0mRead src/StartupMonitor.Shared/Helpers/CpuUsageHelper.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/Ai/ProgramLangDetector.cs
[0m✱ [0mGlob "Directory.Build.props"[90m 1 match[0m
[0m
[0m$ [0mGet-ChildItem -Path . -Filter *.sln -File | Select-Object Name; Get-Content .\src\StartupMonitor.Service\StartupMonitor.Service.csproj
Name              
----              
StartupMonitor.sln
<Project Sdk="Microsoft.NET.Sdk.Worker">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>StartupMonitor.Service</RootNamespace>
    <NoWarn>CA1416</NoWarn>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <DebugType>embedded</DebugType>
    <ApplicationIcon>..\..\assets\app.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
    <PackageReference Include="System.Management" Version="8.0.0" />
    <PackageReference Include="Serilog" Version="4.0.0" />
    <PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
    <PackageReference Include="TaskScheduler" Version="2.12.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\StartupMonitor.Shared\StartupMonitor.Shared.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="StartupMonitor.Tests" />
  </ItemGroup>

</Project>
[0m
[0m→ [0mRead Directory.Build.props
[0m→ [0mRead src/StartupMonitor.Service/Services/RegistryWatcher.cs[90m [limit=130, offset=80][0m

```
