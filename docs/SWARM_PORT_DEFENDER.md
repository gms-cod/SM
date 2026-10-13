Проектирую защиту от флуда и перебора — изучаю трекеры сети и архитектуру сервиса.
# ProcessPortDefenderService — RDP-Guard для любого порта/процесса

## 1. Анализ концепции: RdpGuard / Fail2ban / EvlWatcher

| Система | Источник сигнала | Механизм бана | Плюсы | Минусы для нашей задачи |
|---|---|---|---|---|
| **RdpGuard** | `Security EventLog 4625` (failed logon RDP/SMB/FTP) + счётчик попыток | `WFAS API (INetFwPolicy2)` / `netsh advfirewall`, временный бан + эскалация | Точный (видит даже неудачные auth), малая нагрузка | Привязан к Windows Auth. **Не видит** флуд на кастомный порт `myapp.exe:5000` без логов |
| **Fail2ban (Linux)** | Regex по логам (`/var/log/auth.log`, nginx) → `jail`: `maxretry / findtime / bantime` | `iptables/nftables`, `banaction`, `unban` по таймеру, `recidive` jail для эскалации | Универсальная схема `окно→порог→бан→эскалация` — берём её за основу | На Windows нет логов для произвольных TCP-серверов |
| **EvlWatcher** | Подписка `EventLogWatcher` на `4625/5152` (WFP drop) | Только алертинг | Реактивный, без поллинга | Не покрывает `ESTABLISHED`-флуд, требует включённого аудита |

**Вывод для `StartupMonitor`:**

Единственный универсальный источник для *любого* `PID + LocalPort` без модификации защищаемой программы — периодические снимки `GetExtendedTcpTable` из существующего `ProcessNetworkTracker.GetSockets()` (`ProcessNetworkTracker.cs:290`).

Его ограничения и как их компенсируем:

1. **Снимок видит только живущие сокеты.** Короткие `SYN → RST` (сканер `nmap -sS`) между опросами теряются. Решение: интервал опроса защитника `1 с` (отдельный таймер, не `ProcessMetricsTracker` 1.5с UI on-demand), учёт состояний `SYN_RCVD/SYN_SENT + ESTABLISHED`, дедупликация по 4-кортежу.
2. **Нет признака «неудачный логин».** Детектим только поведенчески: *скорость новых входящих соединений* + *веер по портам* (скан). Это классический `fail2ban jail`, но `findtime/maxretry` считаем по сокетам, а не по логам.
3. **Нагрузка.** `GetSockets(-1)` уже оптимизирован (один `AllocHGlobal` на таблицу). Вызов раз в 1с ≈ 2-5 мс — допустимо в службе `SYSTEM`.

Идеальная схема v2 (после MVP): дополнить опрос `ETW Microsoft-Windows-Kernel-Network` / `WFP 5156` для поимки короткоживущих SYN. MVP — только `GetExtendedTcpTable`.

## 2. Архитектура сервиса

```
┌─ ProcessNetworkTracker.GetSockets(-1) ─── 1с снимки ──┐
│                                                       ▼
│  ProtectedPorts: [{LocalPort:3389,...},{LocalPort:5000,Pid:1234}]
│                                                       │
│                                          ┌────────────┴────────────┐
│                                          │ ProcessPortDefenderService │
│                                          │  (Timer 1000ms)           │
│                                          │  1. Filter inbound        │
│                                          │  2. WhitelistProvider     │
│                                          │  3. ConnectionTracker     │
│                                          │     (Sliding Window+LRU)  │
│                                          │  4. Detector              │
│                                          │     flood / scan          │
│                                          └────────────┬────────────┘
│                                                       │ Ban(IP, reason)
│                                                       ▼
│                                          ┌────────────────────────┐
│                                          │ WindowsFirewallService │── Aggregated_In
│                                          │ BlockIp/UnblockIp      │   StartupMonitor_Blocked_IPs_In
│                                          └────────────────────────┘
│                                                       │
│                                          ┌────────────┴────────────┐
│                                          │ AutoUnbanWorker         │
│                                          │ (Timer 30с, ExpiresAt)  │
│                                          │ + Escalation x2/x4/perm │
│                                          └─────────────────────────┘
```

**Место в DI:** `Program.cs:96` рядом с `AddSingleton<WindowsFirewallService>()` → `AddSingleton<ProcessPortDefenderService>()` + `AddHostedService(sp => sp.GetRequiredService<ProcessPortDefenderService>())` (если делать `BackgroundService`) либо `Timer`-сервис как `ProcessMetricsTracker`.

**Персистентность:** `JsonDataStore` (как `BlacklistManager`) — `bans.json`: `{Ip, FirstSeen, ExpiresAt, RepeatCount, Permanent}` — чтобы бан пережил рестарт службы.

## 3. Алгоритм детекции

### 3.1 Что считаем «входящим»

Из `List<ProcessNetworkSocket>` оставляем только:

```csharp
bool IsInboundCandidate(s) =
  (s.Protocol.StartsWith("TCP"))
  && s.RemoteAddress != "*" && s.RemoteAddress != "0.0.0.0" && s.RemoteAddress != "::"
  && !IPAddress.IsLoopback(...)
  && IsProtected(s.LocalPort, s.ProcessId)   // белый список портов/PID
  && s.State is "SYN_RCVD" or "SYN_SENT" or "ESTABLISHED" or "TIME_WAIT";
```

`LISTENING` — не считаем (это слушатель, а не атака). `UDP` — только опционально (нет состояний, флуд UDP детектится по `socketCounts/PID` из `CaptureNetworkBandwidth()`).

Дедупликация: ключ `remoteIp|localPort|remotePort`. Храним `HashSet<string> _seenTuples` с LRU-очисткой. Только **впервые увиденный** кортеж даёт `+1` в скользящее окно. Иначе один долгоживущий `ESTABLISHED` давал бы +1 каждую секунду — ложный бан.

### 3.2 Скользящее окно — формулы

Для каждого `IP` храним очередь меток `T = {t1..tk}` (только новые соединения).

Пусть `now` — текущий снимок, `W` — окно (с), `N` — порог.

```
Count(W, now) = |{ ti ∈ T : now - ti ≤ W }|
Ban ⟺ Count(W, now) ≥ N
Rate = Count / W   [conn/s]
```

Чистка при каждом снимке (амортизированно O(1)):

```
while T.Count>0 && (now - T.Peek()) > Wmax: T.Dequeue()
```

**Мульти-окно (как fail2ban, рекомендуется):**

```csharp
// бан если СРАБОТАЛО ЛЮБОЕ окно:
rules = [
  { N=10,  W=10с },    // бурст: 1 conn/s
  { N=60,  W=60с },    // флуд:  1 conn/s sustained
  { N=300, W=3600с },  // медленный перебор
]
```

Это покрывает и `Connection Flood` (окно секунды), и `Brute-Force` (окно часы).

**Port Scan:** отдельная проекция — для `IP` считаем множество `DistinctLocalPorts` за `Wscan=60с`:

```
Scan ⟺ |{ localPort : ti ∈ Wscan }| ≥ ScanThreshold (напр. 10 портов/60с)
```

Один IP трогает 10+ защищённых/любых портов — это сканер, баним даже если `N` не превышен.

### 3.3 Пороги по умолчанию

| Профиль | Окно | Порог | Бан |
|---|---|---|---|
| RDP `3389` | 60с | 10 новых conn | 15 мин |
| Общий TCP | 60с | 30 новых conn | 15 мин |
| Бурст | 10с | 15 новых conn | 1 час |
| Скан | 60с | 10 портов | 1 час |

Настраивается на порт: `PortDefenderRule { LocalPort, WindowSec, MaxAttempts, BanSec }`.

## 4. Временный бан, разбан, эскалация

```csharp
// Эскалация:
banDuration(n) = BaseBan * 2^(n-1),  n = RepeatCount (1-based)
// пример Base=15мин: 15м → 30м → 60м → 120м → … cap MaxBan (24ч)
// далее Permanent = true (отдельное правило, без ExpiresAt)
```

* `Ban()`: `Whitelist? → skip` → `firewall.BlockIp(new BlockIpRequest{Ip, Direction=Inbound, Aggregated=true, Reason="PortDefender auto-ban ..."})` → запись в `_activeBans` + persist + событие в лог/UI.
* `AutoUnbanWorker`: `Timer(30с)`. Для каждого `ban where !Permanent && now ≥ ExpiresAtUtc`: `firewall.UnblockIp(...)` → убрать из словаря. **Не** удаляем `RepeatCount` — храним в `_history` (LRU, TTL 7 суток) для эскалации.
* Повторная атака после разбана: `RepeatCount++` → новый `BanDuration` по формуле.
* Ручной `UnbanIp` из UI: снимает бан, но `RepeatCount` сохраняет (иначе атакующий обходит эскалацию ручным разбаном).

Используем **агрегированные** правила `StartupMonitor_Blocked_IPs_In` (`WindowsFirewallService.cs:40`) — `O(1)` правил вместо `O(N)`, критично при `N=1000` забаненных.

## 5. Whitelist

Порядок проверки (самое дешёвое первым):

1. `loopback`: `127.0.0.0/8`, `::1` — через `IPAddress.IsLoopback` + `FirewallGuard.IsBlockableAddress` (уже запрещает loopback, `FirewallModels.cs:278`).
2. `unspecified/multicast`: `0.0.0.0`, `224.0.0.0/4` — тоже запрещены гардом.
3. `RFC 1918 + APIPA + LinkLocal`: `10/8`, `172.16/12`, `192.168/16`, `169.254/16`, `fe80::/10` — **опционально**: флаг `TrustPrivateNetworks=true` (по умолчанию `true` для дома, `false` для VPS). Используем готовый `ProcessNetworkSocket.CheckIsPrivateAddress()` (`ProcessNetworkSocket.cs:59`).
4. `Шлюз по умолчанию + DNS/DHCP`: резолвим раз в 5 мин через `NetworkInterface.GetAllNetworkInterfaces()` → `GatewayAddresses` — иначе при флуде забаним собственный роутер.
5. `Пользовательский список`: CIDR (`192.168.1.100`, `10.0.0.0/24`) из `AppConfig` + UI.

Ни один whitelist-IP **не попадает** в `ConnectionTracker` — экономия памяти.

## 6. Защита от переполнения памяти (LRU)

* Лимит: `MaxTrackedIps = 10_000`, `MaxEventsPerIp = 1_000` (очередь обрезается — старые `Dequeue`).
* `ConnectionTracker: ConcurrentDictionary<string, IpState>` + `LinkedList` LRU или счётчик `LastSeen`. При превышении — вытесняем самый старый `LastSeen`.
* Фоновая чистка каждый тик: удалить `IpState` где `now - LastSeen > 2 * Wmax` (напр. `2ч` при `Wmax=1ч`) и `T` пуста.
* `_seenTuples: HashSet<string>` с cap `50_000`, при переполнении — `Clear()` (дедуплика временно ослабнет, но OOM не будет). Лучше: `MemoryCache` с `SlidingExpiration = Wmax`.
* `_history` (для эскалации): cap `20_000`, TTL 7 дней.

Оценка: `10k IP × (очередь ~100 × 8 байт + overhead ~200Б) ≈ 10 МБ` — безопасно для службы.

## 7. C# прототип (встраивается в `StartupMonitor.Service/Services/`)

```csharp
// PortDefenderOptions.cs — конфиг (сериализуется в AppConfig)
namespace StartupMonitor.Service.Services.PortDefender;

public sealed class PortDefenderRule
{
    public int LocalPort { get; set; }            // 0 = любой порт
    public string? ProcessName { get; set; }      // null = любой процесс
    public int WindowSec { get; set; } = 60;      // W
    public int MaxAttempts { get; set; } = 30;    // N
    public int BanSec { get; set; } = 900;        // 15 мин
}

public sealed class PortDefenderOptions
{
    public bool Enabled { get; set; } = true;
    public int SampleIntervalMs { get; set; } = 1000;
    public int MaxTrackedIps { get; set; } = 10_000;
    public int ScanPortThreshold { get; set; } = 10;  // портов
    public int ScanWindowSec { get; set; } = 60;
    public int MaxBanSec { get; set; } = 86_400;      // 24ч cap
    public bool TrustPrivateNetworks { get; set; } = true;
    public List<string> WhitelistCidrs { get; set; } = new();
    public List<PortDefenderRule> Rules { get; set; } = new()
    {
        new() { LocalPort = 3389, WindowSec = 60, MaxAttempts = 10, BanSec = 900 },
        new() { LocalPort = 0,    WindowSec = 60, MaxAttempts = 30, BanSec = 900 },
        new() { LocalPort = 0,    WindowSec = 10, MaxAttempts = 15, BanSec = 3600 },
    };
}

// ActiveBan.cs
public sealed class ActiveBan
{
    public string Ip { get; set; } = "";
    public DateTime BannedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool Permanent { get; set; }
    public int RepeatCount { get; set; }
    public string Reason { get; set; } = "";
}

// Внутреннее состояние sliding window на IP
internal sealed class IpState
{
    public readonly Queue<DateTime> Hits = new();       // T
    public readonly HashSet<int> Ports = new();         // для scan-детекции
    public DateTime PortsWindowStart = DateTime.UtcNow;
    public DateTime LastSeen = DateTime.UtcNow;
    public int RepeatCount;                              // для эскалации
}
```

```csharp
// ProcessPortDefenderService.cs — ядро (Timer + AutoUnban в одном классе)
namespace StartupMonitor.Service.Services.PortDefender;

public sealed class ProcessPortDefenderService : IDisposable
{
    private readonly ProcessNetworkTracker _tracker;      // существующий
    private readonly WindowsFirewallService _fw;          // существующий
    private readonly PortDefenderOptions _opt;
    private readonly Timer _sampleTimer, _unbanTimer;
    private readonly ConcurrentDictionary<string, IpState> _states = new();
    private readonly ConcurrentDictionary<string, ActiveBan> _bans = new();
    private readonly HashSet<string> _seenTuples = new(); // 4-кортежи, под lock
    private readonly object _tuplesLock = new();
    private readonly WhitelistProvider _whitelist;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ProcessPortDefenderService>();

    public event EventHandler<ActiveBan>? IpBanned;
    public event EventHandler<string>? IpUnbanned;

    public ProcessPortDefenderService(ProcessNetworkTracker tracker,
        WindowsFirewallService fw, PortDefenderOptions opt, WhitelistProvider whitelist)
    {
        _tracker = tracker; _fw = fw; _opt = opt; _whitelist = whitelist;
        _sampleTimer = new Timer(SampleTick, null, Timeout.Infinite, Timeout.Infinite);
        _unbanTimer = new Timer(UnbanTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (!_opt.Enabled) return;
        _sampleTimer.Change(0, _opt.SampleIntervalMs);
        _unbanTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    // --- 1. Снимок и детекция (каждую секунду) ---
    private void SampleTick(object? _)
    {
        List<ProcessNetworkSocket> sockets;
        try { sockets = _tracker.GetSockets(-1); }
        catch { return; } // Zero-Crash

        var now = DateTime.UtcNow;
        foreach (var s in sockets)
        {
            if (!IsInboundCandidate(s)) continue;
            if (_bans.ContainsKey(s.RemoteAddress)) continue;   // уже забанен
            if (_whitelist.IsWhitelisted(s.RemoteAddress)) continue;

            // Дедупликация по 4-кортежу: только НОВОЕ соединение
            var tuple = $"{s.RemoteAddress}|{s.LocalPort}|{s.RemotePort}";
            lock (_tuplesLock)
            {
                if (!_seenTuples.Add(tuple)) continue;
                if (_seenTuples.Count > 50_000) _seenTuples.Clear();
            }

            var st = _states.GetOrAdd(s.RemoteAddress, _ => new IpState());
            lock (st)
            {
                st.Hits.Enqueue(now);
                st.LastSeen = now;
                // scan-проекция
                if ((now - st.PortsWindowStart).TotalSeconds > _opt.ScanWindowSec)
                { st.Ports.Clear(); st.PortsWindowStart = now; }
                st.Ports.Add(s.LocalPort);

                PruneQueue(st, now, MaxWindow());
            }

            Evaluate(s.RemoteAddress, s, now);
        }
        EvictIdle(now);
    }

    private void Evaluate(string ip, ProcessNetworkSocket s, DateTime now)
    {
        if (!_states.TryGetValue(ip, out var st)) return;
        int count;
        lock (st) count = st.Hits.Count;

        // Проверка каждого правила, покрывающего (порт, процесс)
        foreach (var r in _opt.Rules)
        {
            if (r.LocalPort != 0 && r.LocalPort != s.LocalPort) continue;
            if (r.ProcessName != null &&
                !s.ProcessName.Equals(r.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;

            // Count(Wr): считаем только хиты внутри окна правила
            int c = CountInWindow(st, now, r.WindowSec);
            if (c >= r.MaxAttempts)
            {
                Ban(ip, $"Flood: {c} conn / {r.WindowSec}с на порт {s.LocalPort} ({s.ProcessName})", r.BanSec, st);
                return;
            }
        }
        // Port-scan
        lock (st)
        {
            if (st.Ports.Count >= _opt.ScanPortThreshold)
                Ban(ip, $"PortScan: {st.Ports.Count} портов / {_opt.ScanWindowSec}с", 3600, st);
        }
    }

    private static int CountInWindow(IpState st, DateTime now, int windowSec)
    {
        int c = 0;
        lock (st) foreach (var t in st.Hits)
            if ((now - t).TotalSeconds <= windowSec) c++;
        return c; // очередь уже подрезана; полный проход редок (≤1000)
    }

    // --- 2. Бан / эскалация ---
    private void Ban(string ip, string reason, int baseSec, IpState st)
    {
        int repeat;
        lock (st) repeat = ++st.RepeatCount; // 1-based
        long duration = (long)(baseSec * Math.Pow(2, repeat - 1)); // x2 эскалация
        duration = Math.Min(duration, _opt.MaxBanSec);
        bool permanent = repeat >= 6; // после ~5 эскалаций → permanent

        var res = _fw.BlockIp(new StartupMonitor.Shared.Models.Firewall.BlockIpRequest
        {
            Ip = ip,
            Direction = StartupMonitor.Shared.Models.Firewall.FirewallDirection.Inbound,
            Aggregated = true,
            Reason = $"PortDefender auto-ban #{repeat}: {reason}"
        });
        if (!res.Success) { Log.Warning("PortDefender: BlockIp {Ip} failed: {Msg}", ip, res.Message); return; }

        var ban = new ActiveBan
        {
            Ip = ip,
            BannedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = permanent ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(duration),
            Permanent = permanent,
            RepeatCount = repeat,
            Reason = reason
        };
        _bans[ip] = ban;
        _states.TryRemove(ip, out _); // окно сбрасываем, историю repeat храним в IpState? -> переносим
        Log.Warning("PortDefender: {Ip} забанен на {Dur} ({Reason})", ip, permanent ? "perm" : duration + "с", reason);
        IpBanned?.Invoke(this, ban);
        // TODO: persist _bans в bans.json через JsonDataStore
    }

    // --- 3. AutoUnbanWorker (каждые 30с) ---
    private void UnbanTick(object? _)
    {
        var now = DateTime.UtcNow;
        foreach (var (ip, ban) in _bans)
        {
            if (ban.Permanent || now < ban.ExpiresAtUtc) continue;
            var res = _fw.UnblockIp(new StartupMonitor.Shared.Models.Firewall.UnblockIpRequest
            {
                Ip = ip,
                Direction = StartupMonitor.Shared.Models.Firewall.FirewallDirection.Inbound
            });
            if (res.Success) { _bans.TryRemove(ip, out _); IpUnbanned?.Invoke(this, ip); }
            Log.Information("PortDefender: {Ip} разбанен (истёк срок)", ip);
        }
    }

    private static bool IsInboundCandidate(ProcessNetworkSocket s) =>
        s.Protocol.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)
        && s.RemoteAddress is not ("*" or "0.0.0.0" or "::" or "")
        && s.State is "SYN_RCVD" or "SYN_SENT" or "ESTABLISHED" or "TIME_WAIT";

    private int MaxWindow() => _opt.Rules.Count == 0 ? 60 : _opt.Rules.Max(r => r.WindowSec);

    private static void PruneQueue(IpState st, DateTime now, int wmax)
    {
        lock (st)
            while (st.Hits.Count > 0 && (now - st.Hits.Peek()).TotalSeconds > wmax)
                st.Hits.Dequeue();
    }

    private void EvictIdle(DateTime now)
    {
        if (_states.Count < _opt.MaxTrackedIps) // + чистка по TTL
        {
            foreach (var (ip, st in _states) ...) // псевдо: удалить где now-LastSeen > 2*Wmax
                ;
            return;
        }
        // LRU-вытеснение при переполнении
        foreach (var kv in _states.OrderBy(kv => kv.Value.LastSeen).Take(_states.Count - _opt.MaxTrackedIps))
            _states.TryRemove(kv.Key, out _);
    }

    public IReadOnlyCollection<ActiveBan> GetActiveBans() => _bans.Values.ToList();
    public void Dispose() { _sampleTimer.Dispose(); _unbanTimer.Dispose(); }
}
```

```csharp
// WhitelistProvider.cs
public sealed class WhitelistProvider
{
    private readonly PortDefenderOptions _opt;
    private HashSet<string> _gateways = new();
    private DateTime _gwRefresh;
    public WhitelistProvider(PortDefenderOptions opt) => _opt = opt;

    public bool IsWhitelisted(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var addr)) return true; // мусор — не баним
        if (System.Net.IPAddress.IsLoopback(addr)) return true;
        if (_opt.TrustPrivateNetworks && ProcessNetworkSocket.CheckIsPrivateAddress(ip)) return true;
        RefreshGateways();
        if (_gateways.Contains(ip)) return true;
        foreach (var cidr in _opt.WhitelistCidrs)
            if (CidrContains(cidr, addr)) return true;
        return false;
    }
    // + CidrContains(ip/mask), RefreshGateways() через NetworkInterface
}
```

## 8. Интеграция и Pipe API (следующий шаг)

1. `Program.cs`: `services.AddSingleton<PortDefenderOptions>(config)` + `WhitelistProvider` + `ProcessPortDefenderService`.
2. `AppConfig`: секция `PortDefender` (правила, whitelist).
3. Pipe-команды (по аналогии с `GetProcessNetworkConnectionsAsync`): `ConfigurePortDefender`, `GetActiveBans`, `UnbanIp`, `ResetDefenderStats` + DTO `ActiveBanDto`, `DefenderAttackEventDto`.
4. UI: бейджи `[Под защитой]` в таблице сокетов + вкладка «Защита портов».
5. Тесты: unit на `CountInWindow` / эскалацию `banDuration(n)` / `WhitelistProvider` (RFC 1918, loopback, CIDR), интеграционный на `BlockIp/UnblockIp` round-trip.

## 9. Риски и ограничения MVP

* Поллинг 1с пропускает сверхкороткие SYN-сканы — лечится ETW-дополнением в v2, для brute-force/flood (долгоживущие `ESTABLISHED`) достаточно.
* CGNAT / корпоративный NAT: один внешний IP = сотни легитимных пользователей — пороги для публичных сервисов ставить выше + whitelist.
* Самоблокировка исключена гардом (`FirewallGuard.IsBlockableAddress`) + whitelist шлюза.
