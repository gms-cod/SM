# Аудит StartupMonitor.Service: 0.0% CPU Idle, кеш правил Firewall и Headless-защита

Дата: 2026-10-25  
Область: `src/StartupMonitor.Service`, `src/StartupMonitor.Shared`, `src/StartupMonitor.UI`  
Цель: обеспечить непрерывную сетевую защиту при закрытом UI, строгое энергопотребление 0.0% CPU в простое, корректный кеш `WindowsFirewallService.GetAllRules()` и жёсткую семантику белого/чёрного списков.

---

## 0. Краткий вердикт

Текущая служба **не находится в настоящем Idle**. Причина не в одном таймере, а в трёх независимых контурах, которые будят службу:

1. `NetworkConnectionAlertService` запускает `SleepAwareLoop` с `hasWork: () => AlertsEnabled`. Флаг `AlertsEnabled` по умолчанию `true` и не зависит от наличия UI, поэтому цикл никогда не уходит в `Timeout.InfiniteTimeSpan`. Сканирование сокетов идёт каждые 5 секунд.
2. Каждый UI-запрос `GetPendingNetworkAlerts` синхронно вызывает `_alertService.ScanNewConnections()` (`PipeServer.cs:1837`). UI опрашивает очередь каждые 2 секунды (`MainViewModel.cs:3017`) и каждые 2.5 секунды (`NetworkSecurityHubView.xaml.cs:252`). В результате скан сокетов фактически выполняется каждые ~2 секунды, а не 5.
3. На каждом скане вызывается `_store.Load().Settings.FirewallSilentPid0` (`NetworkConnectionAlertService.cs:247`). `JsonDataStore.Load()` делает глубокий клон всего `AppConfig`, включая `Entries`, `Blacklist`, `RemovedEntries`, `History`. Это не только I/O, но и заметное GC-давление.

Дополнительно:

- `WindowsFirewallService.GetAllRules()` (`WindowsFirewallService.cs:461`) не кешируется. Он перечисляет все правила и читает ~12 COM-свойств на каждое правило через reflection `InvokeMember`.
- `GetApplicationRulePaths()` (`WindowsFirewallService.cs:492`) — более дешёвый, но всё равно полный проход по COM-коллекции. В алерт-сервисе для него есть отдельный кеш `_cachedRulePaths` с TTL 60 секунд, независимый от `WindowsFirewallService`. Это второй уровень кеша и источник рассинхронизации.
- Белый список проверяется **раньше** чёрного (`NetworkConnectionAlertService.cs:272-278`). Если приложение попало в оба списка, побеждает белый. По требованию должно быть наоборот: чёрный список имеет абсолютный приоритет.
- Чёрный список сейчас только подавляет всплывающие окна. Блокирующее правило на старте не создаётся, процесс не завершается, противоположное правило не удаляется.
- UI служба не запускает (`Program.cs:29-30`), а при закрытом UI единственный механизм реакции — фоновый скан, который сам является источником нагрузки. Автоподъём UI и автономная блокировка отсутствуют.

Целевое решение: **событийный ETW-контур** (`Microsoft-Windows-Kernel-Network`) для обнаружения TCP-подключений в реальном времени, **единый кеш правил Firewall** с TTL и инвалидацией по реестру, **headless-responder** с автозапуском UI в пользовательской сессии и **синхронизация списков с правилами Firewall** непосредственно при старте службы и при каждом изменении списка.

---

## 1. Текущее состояние: факты и точные места

| Проблема | Файл и строка | Последствие |
|---|---|---|
| `hasWork` всегда `true` | `NetworkConnectionAlertService.cs:185` | Цикл не спит, скан каждые 5 с |
| `_monitorWindowActive` не участвует в `hasWork` | `NetworkConnectionAlertService.cs:95-96, 171-176, 185` | Закрытый UI не переводит службу в Idle |
| Каждый UI-запрос вызывает скан | `PipeServer.cs:1837` | Скан каждые ~2 с при открытом UI |
| UI-поллер 2 с | `MainViewModel.cs:3013-3018` | Дополнительный постоянный трафик IPC |
| UI-поллер 2.5 с | `NetworkSecurityHubView.xaml.cs:252-253` | Второй независимый поллер |
| `Load()` на каждый скан | `NetworkConnectionAlertService.cs:247` | Глубокий клон `AppConfig`, GC и I/O |
| `Load()` в конструкторе | `NetworkConnectionAlertService.cs:123` | Списки загружаются один раз, изменения не подхватываются |
| Белый список раньше чёрного | `NetworkConnectionAlertService.cs:272-278` | Белый список побеждает чёрный |
| Чёрный список только `continue` | `NetworkConnectionAlertService.cs:276-278` | Нет блокировки и завершения процесса |
| Локальный кеш путей 60 с | `NetworkConnectionAlertService.cs:579-601` | Рассинхронизация с `WindowsFirewallService` |
| `GetAllRules` без кеша | `WindowsFirewallService.cs:461-485` | Дорогой COM-проход на каждый запрос |
| `GetApplicationRulePaths` без общего кеша | `WindowsFirewallService.cs:492-560` | Повторный COM-проход, `Thread.Yield` каждые 100 правил |
| `GetAllRules` вызывается UI-командой | `PipeServer.cs:1572-1584` | Каждое открытие списка правил — полный COM-проход |
| UI не запускается службой | `Program.cs:29-30` | При закрытом UI нет интерактивного реагирования |
| Запуск только по `WTSGetActiveConsoleSessionId` | `UserSessionLauncher.cs:171-181` | Не покрывает RDP/несколько сессий |
| `CREATE_NEW_CONSOLE` для GUI | `UserSessionLauncher.cs:24, 269` | Лишнее консольное окно/нежелательный флаг |
| Второй экземпляр UI только активируется | `App.xaml.cs:62-83, 355-363` | Нет пересылки команды «показать алерт» |

---

## 2. Целевая архитектура

```
                    ┌──────────────────────────────────────────────┐
                    │              StartupMonitor.Service           │
                    │                                              │
  TCP connect ──────► EtwNetworkConnectionWatcher (1 поток ETW)     │
                    │        │ Channel<NetworkConnectEvent>         │
                    │        ▼                                     │
                    │ NetworkEventProcessor (1 consumer)            │
                    │        │                                     │
                    │        ▼                                     │
                    │ HeadlessThreatResponder                      │
                    │   ├─ NetworkPolicyEvaluator                  │
                    │   │    ├─ NetworkListStore (white/black)     │
                    │   │    └─ FirewallRuleCache (O(1) проверка)  │
                    │   ├─ WindowsFirewallService (Block/Allow)    │
                    │   ├─ Process kill (по PID + проверка пути)   │
                    │   └─ UserSessionLauncher (UI в сессии)       │
                    │                                              │
                    │ NetworkConnectionAlertService                │
                    │   ├─ очередь алертов для UI                  │
                    │   ├─ ScanNewConnections (fallback/UI)        │
                    │   └─ hasWork = AlertsEnabled && UI active    │
                    │                                              │
                    │ WindowsFirewallService                       │
                    │   └─ FirewallRuleCache (TTL 60s + registry)  │
                    └──────────────────────────────────────────────┘
                                      │
                                      ▼
                    ┌──────────────────────────────────────────────┐
                    │ UI: poll pending alerts -> alert window      │
                    │ автозапуск: --minimized (существующий poller)│
                    └──────────────────────────────────────────────┘
```

Ключевой принцип: **в простое нет периодических таймеров**. ETW-поток блокируется в `Source.Process()` и не потребляет CPU, пока нет сетевых событий. Фоновый скан сокетов остаётся только как резервный режим и при открытом UI.

---

## 3. Почему сейчас нет 0.0% CPU и как это исправить

### 3.1. Исправить `hasWork`

Было:

```csharp
private Task RunAsync(CancellationToken ct)
    => SleepAwareLoop.RunAsync(
        _wake,
        hasWork: () => AlertsEnabled, // всегда true
        activeInterval: _period,
        idleInterval: Timeout.InfiniteTimeSpan,
        body: _ => { ScanNewConnections(); return Task.CompletedTask; },
        ct);
```

Должно быть:

```csharp
/// <summary>
/// Фоновый цикл сканирования сокетов.
/// Работает только когда включены алерты И открыто окно мониторинга сети.
/// При закрытом UI цикл спит неограниченно долго: обнаружение соединений
/// выполняет событийный ETW-контур, а не периодический опрос.
/// </summary>
private Task RunAsync(CancellationToken ct)
    => SleepAwareLoop.RunAsync(
        _wake,
        hasWork: () => AlertsEnabled && _monitorWindowActive,
        activeInterval: _period,
        idleInterval: Timeout.InfiniteTimeSpan,
        body: _ =>
        {
            try { ScanNewConnections(); }
            catch (Exception ex) { Log.Warning(ex, "NetworkAlerts: ошибка фонового сканирования"); }
            return Task.CompletedTask;
        },
        ct);
```

Дополнительно `SetMonitorWindowActive(true/false)` уже вызывает `_wake.Pulse()`, поэтому цикл мгновенно просыпается при открытии окна и засыпает при закрытии. Менять `SetMonitorWindowActive` не нужно.

### 3.2. Убрать скан из обработчика `GetPendingNetworkAlerts`

Сейчас UI-опрос сам инициирует скан:

```csharp
case PipeMessageTypes.GetPendingNetworkAlerts:
    _alertService.ScanNewConnections(); // <-- убрать
    var alerts = _alertService.GetPendingAlerts();
```

Правильно:

```csharp
case PipeMessageTypes.GetPendingNetworkAlerts:
    try
    {
        if (_alertService == null)
        {
            response.Type = PipeMessageTypes.Error;
            response.Payload = "Служба сетевых алертов недоступна.";
            break;
        }

        // Только читаем очередь. Сканирование выполняется:
        // 1) событийным ETW-контуром;
        // 2) фоновым циклом при открытом окне мониторинга.
        var alerts = _alertService.GetPendingAlerts();
        response.Type = PipeMessageTypes.GetPendingNetworkAlertsResponse;
        response.Payload = JsonSerializer.Serialize(new NetworkAlertsPayload(alerts), JsonOptions);
    }
    catch (Exception ex)
    {
        response.Type = PipeMessageTypes.Error;
        response.Payload = ex.Message;
    }
    break;
```

Это убирает главный источник скачков: сейчас два UI-поллера (2 с и 2.5 с) фактически вызывают полный `GetSockets()` и проверку правил Firewall.

### 3.3. Заменить `Load()` на `GetSettings()`

`JsonDataStore.Load()` возвращает глубокий клон всего конфига (`JsonDataStore.cs:120-128` и `CloneSnapshot` 134-146). Для чтения одного флага это избыточно.

Было:

```csharp
var isSilentPid0 = _store?.Load().Settings.FirewallSilentPid0 ?? true;
```

Должно быть:

```csharp
// GetSettings() клонирует только AppSettings (списки/скаляры), но не Entries/History.
var settings = _store?.GetSettings();
var isSilentPid0 = settings?.FirewallSilentPid0 ?? true;
```

Ещё лучше: один раз прочитать `AppSettings` в начале скана и использовать его для всех проверок, не обращаясь к хранилищу внутри цикла по сокетам.

### 3.4. Что даст ETW

ETW-провайдер `Microsoft-Windows-Kernel-Network` присылает событие `TcpIpConnect` (opcode 12) и `TcpIpConnectIPV6` (opcode 28). Поток `TraceEventSession.Source.Process()` блокируется и не потребляет CPU при отсутствии событий. Это позволяет:

- держать защиту 24/7 при закрытом UI;
- не сканировать все сокеты каждые 2-5 секунд;
- реагировать на новое подключение в момент его появления, а не на следующем тике таймера.

Ограничение: событие `TcpIpConnect` наступает **после** установления соединения. Первые пакеты теоретически могут уйти до блокировки. Для блокировки «до первого пакета» нужен WFP callout driver или режим default-deny outbound. Это отражено в разделе рисков.

---

## 4. ETW-наблюдатель: готовый код

### 4.1. Пакет

В `src/StartupMonitor.Service/StartupMonitor.Service.csproj`:

```xml
<ItemGroup>
  <!-- Реальное время: разбор событий ядра Windows (TCP/IP connect/disconnect). -->
  <PackageReference Include="Microsoft.Diagnostics.Tracing.TraceEvent" Version="3.1.*" />
</ItemGroup>
```

После первого restore версию нужно зафиксировать (например, `3.1.23`) для воспроизводимых сборок.

### 4.2. `NetworkConnectEvent`

```csharp
namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Событие нового сетевого подключения, полученное из ETW.
/// Содержит только неизменяемые данные; разрешение пути процесса
/// выполняется позже, чтобы не тормозить поток ETW.
/// </summary>
public readonly record struct NetworkConnectEvent(
    int ProcessId,
    string RemoteAddress,
    int RemotePort,
    string Protocol,
    DateTime DetectedAtUtc);
```

### 4.3. `EtwNetworkConnectionWatcher`

```csharp
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Событийный наблюдатель TCP-подключений через ETW.
///
/// Почему так:
/// - TraceEventSession.Source.Process() блокируется в отдельном потоке и не опрашивает сокеты;
/// - при отсутствии сетевых событий поток спит во внутреннем ожидании ETW;
/// - колбэки ETW не должны выполнять тяжёлую работу, поэтому они только пишут
///   событие в ограниченный Channel, а обработка идёт в одном потребителе.
///
/// Сессия имеет приватное имя (не "NT Kernel Logger"), поэтому на Windows 8+
/// используется SystemTraceProvider и не конфликтует с системным NT Kernel Logger.
/// </summary>
public sealed class EtwNetworkConnectionWatcher : IDisposable
{
    private const string SessionName = "StartupMonitor-NetworkWatcher";

    private readonly ILogger<EtwNetworkConnectionWatcher> _logger;

    // Ограниченный канал защищает службу от переполнения памяти при шторме подключений.
    // DropOldest — осознанный выбор: лучше потерять часть диагностических событий,
    // чем допустить неконтролируемый рост очереди и OOM.
    private readonly Channel<NetworkConnectEvent> _queue;

    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private int _droppedEvents;
    private bool _started;
    private bool _disposed;

    /// <summary>Потокобезопасный счётчик потерянных событий (диагностика).</summary>
    public int DroppedEvents => Volatile.Read(ref _droppedEvents);

    /// <summary>Читатель очереди для единственного фонового потребителя.</summary>
    public ChannelReader<NetworkConnectEvent> Reader => _queue.Reader;

    /// <summary>
    /// Создаёт наблюдатель. ETW-сессия стартует только после вызова <see cref="Start"/>.
    /// </summary>
    public EtwNetworkConnectionWatcher(ILogger<EtwNetworkConnectionWatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queue = Channel.CreateBounded<NetworkConnectEvent>(new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>
    /// Запускает фоновый ETW-поток. Метод не блокирует вызывающего.
    /// </summary>
    public bool Start()
    {
        if (_disposed)
            return false;
        if (_started)
            return true;

        _started = true;
        _thread = new Thread(() => Run(_cts.Token))
        {
            IsBackground = true,
            Name = "StartupMonitor.EtwNetworkWatcher",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
        return true;
    }

    /// <summary>
    /// Основной цикл ETW. Source.Process() блокируется до отмены или ошибки.
    /// </summary>
    private void Run(CancellationToken ct)
    {
        try
        {
            // Приватное имя сессии: на Windows 8+ SystemTraceProvider допускает
            // несколько таких сессий, поэтому мы не занимаем единственный NT Kernel Logger.
            using var session = new TraceEventSession(SessionName)
            {
                StopOnDispose = true,
            };

            // NetworkTCPIP включает события TCP/IP IPv4 и IPv6.
            // Process/Thread TraceEvent добавляет автоматически, чтобы корректно
            // заполнять ProcessID и имя процесса для каждого события.
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            var kernel = session.Source.Kernel;

            // Колбэки выполняются в потоке ETW. Никаких блокировок, COM и файлового I/O.
            kernel.TcpIpConnect += data =>
                Enqueue(data.ProcessID, data.daddr, data.dport, "TCP");

            kernel.TcpIpConnectIPV6 += data =>
                Enqueue(data.ProcessID, data.daddr, data.dport, "TCPv6");

            // Корректно выходим из блокирующего Source.Process() при остановке службы.
            using var registration = ct.Register(() =>
            {
                try { session.Source.StopProcessing(); }
                catch (Exception ex) { _logger.LogDebug(ex, "ETW: StopProcessing"); }
            });

            _logger.LogInformation("ETW: наблюдатель сетевых подключений запущен (сессия {Session})", SessionName);
            session.Source.Process();
        }
        catch (Exception ex)
        {
            // ETW может быть недоступен в урезанных/контейнерных средах.
            // Служба не падает: HeadlessThreatResponder продолжит работать по firewall-правилам,
            // а NetworkThreatMonitor включит редкий fallback-опрос.
            _logger.LogError(ex, "ETW: не удалось запустить наблюдатель сетевых подключений");
        }
    }

    /// <summary>
    /// Неблокирующая запись события в ограниченный канал.
    /// </summary>
    private void Enqueue(int pid, System.Net.IPAddress? remote, int remotePort, string protocol)
    {
        if (pid <= 0 || remote is null || remotePort <= 0)
            return;

        var evt = new NetworkConnectEvent(
            pid,
            remote.ToString(),
            remotePort,
            protocol,
            DateTime.UtcNow);

        if (!_queue.Writer.TryWrite(evt))
            Interlocked.Increment(ref _droppedEvents);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        try { _thread?.Join(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
```

### 4.4. `NetworkEventProcessor`

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Единственный потребитель ETW-очереди. Последовательная обработка исключает
/// гонки за firewall-правила и не создаёт по задаче на каждое подключение.
/// </summary>
public sealed class NetworkEventProcessor : BackgroundService
{
    private readonly EtwNetworkConnectionWatcher _etw;
    private readonly HeadlessThreatResponder _responder;
    private readonly ILogger<NetworkEventProcessor> _logger;

    public NetworkEventProcessor(
        EtwNetworkConnectionWatcher etw,
        HeadlessThreatResponder responder,
        ILogger<NetworkEventProcessor> logger)
    {
        _etw = etw ?? throw new ArgumentNullException(nameof(etw));
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Запускает ETW и обрабатывает события до остановки службы.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_etw.Start())
        {
            _logger.LogWarning("NetworkEventProcessor: ETW-наблюдатель не запущен");
            return;
        }

        await foreach (var evt in _etw.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                _responder.Handle(evt);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Одно плохое событие не должно останавливать защиту.
                _logger.LogWarning(ex, "NetworkEventProcessor: ошибка обработки события PID={Pid} {Remote}:{Port}",
                    evt.ProcessId, evt.RemoteAddress, evt.RemotePort);
            }
        }
    }
}
```

---

## 5. Кеш правил Windows Firewall

### 5.1. Требования

- TTL по умолчанию 60 секунд.
- Немедленная инвалидация при изменении правил через `WindowsFirewallService`.
- Инвалидация при внешнем изменении через реестр.
- Один общий снимок для `GetAllRules()` и `GetApplicationRulePaths()`.
- Никаких `lock`-порядков, приводящих к дедлоку с `_sync` сервиса.
- При недоступности MpsSvc — короткий TTL ошибки (5 секунд), чтобы не долбить COM каждую секунду.

### 5.2. `FirewallRuleCache`

```csharp
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Потокобезопасный кеш снимка правил Windows Firewall.
///
/// Инварианты:
/// - снимок иммутабелен после публикации;
/// - инвалидация не берёт блокировку загрузки, поэтому не может образовать
///   дедлок с lock (_sync) внутри WindowsFirewallService;
/// - одновременно выполняется только одна загрузка (lock _loadLock);
/// - ошибка кешируется отдельным коротким TTL.
/// </summary>
public sealed class FirewallRuleCache
{
    private sealed class Snapshot
    {
        public Snapshot(
            IReadOnlyList<FirewallRuleDto> rules,
            IReadOnlySet<string> applicationPaths,
            DateTime loadedAtUtc,
            bool isFailure)
        {
            Rules = rules;
            ApplicationPaths = applicationPaths;
            LoadedAtUtc = loadedAtUtc;
            IsFailure = isFailure;
        }

        public IReadOnlyList<FirewallRuleDto> Rules { get; }
        public IReadOnlySet<string> ApplicationPaths { get; }
        public DateTime LoadedAtUtc { get; }
        public bool IsFailure { get; }
    }

    private readonly Func<IReadOnlyList<FirewallRuleDto>> _loader;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _failureTtl;
    private readonly object _loadLock = new();

    private Snapshot? _snapshot;

    /// <summary>
    /// Создаёт кеш.
    /// </summary>
    /// <param name="loader">Функция полной загрузки правил (обычно COM-перечисление).</param>
    /// <param name="ttl">Время жизни успешного снимка.</param>
    /// <param name="failureTtl">Время жизни снимка после ошибки загрузки.</param>
    public FirewallRuleCache(
        Func<IReadOnlyList<FirewallRuleDto>> loader,
        TimeSpan ttl,
        TimeSpan? failureTtl = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL должен быть положительным.");
        _ttl = ttl;
        _failureTtl = failureTtl ?? TimeSpan.FromSeconds(5);
        if (_failureTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(failureTtl), "Failure TTL должен быть положительным.");
    }

    /// <summary>Возвращает снимок всех правил (только для чтения).</summary>
    public IReadOnlyList<FirewallRuleDto> GetRules() => GetSnapshot().Rules;

    /// <summary>Возвращает O(1)-набор путей/имён приложений из того же снимка.</summary>
    public IReadOnlySet<string> GetApplicationPaths() => GetSnapshot().ApplicationPaths;

    /// <summary>
    /// Помечает снимок устаревшим. Следующий читатель выполнит загрузку.
    /// Не блокирует вызывающего и безопасен при конкурентных вызовах.
    /// </summary>
    public void Invalidate()
    {
        Interlocked.Exchange(ref _snapshot, null);
    }

    private Snapshot GetSnapshot()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (IsFresh(snapshot))
            return snapshot!;

        lock (_loadLock)
        {
            // Двойная проверка: пока ждали lock, другой поток мог уже обновить снимок.
            snapshot = Volatile.Read(ref _snapshot);
            if (IsFresh(snapshot))
                return snapshot!;

            IReadOnlyList<FirewallRuleDto> rules;
            var isFailure = false;
            try
            {
                rules = _loader() ?? Array.Empty<FirewallRuleDto>();
            }
            catch
            {
                // Ошибку логирует loader (WindowsFirewallService). Кешируем пустой снимок
                // на короткий срок, чтобы не повторять дорогой COM-вызов на каждый запрос.
                rules = Array.Empty<FirewallRuleDto>();
                isFailure = true;
            }

            snapshot = new Snapshot(
                rules,
                BuildApplicationPathSet(rules),
                DateTime.UtcNow,
                isFailure);

            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

    private bool IsFresh(Snapshot? snapshot)
    {
        if (snapshot is null)
            return false;
        var ttl = snapshot.IsFailure ? _failureTtl : _ttl;
        return DateTime.UtcNow - snapshot.LoadedAtUtc < ttl;
    }

    /// <summary>
    /// Строит набор путей и имён приложений из правил.
    /// COM ApplicationName может содержать переменные окружения, поэтому
    /// добавляем и исходное, и раскрытое значение.
    /// </summary>
    private static IReadOnlySet<string> BuildApplicationPathSet(IReadOnlyList<FirewallRuleDto> rules)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            var raw = rule.ApplicationPath;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            AddCandidate(set, raw);
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw);
                if (!string.Equals(expanded, raw, StringComparison.Ordinal))
                    AddCandidate(set, expanded);
            }
            catch
            {
                // Некорректная переменная окружения не должна ломать весь снимок.
            }
        }

        return set;
    }

    private static void AddCandidate(HashSet<string> set, string value)
    {
        var trimmed = value.Trim().Trim('"');
        if (trimmed.Length == 0 || trimmed == "*")
            return;

        set.Add(trimmed);

        try
        {
            var fileName = Path.GetFileName(trimmed);
            if (!string.IsNullOrWhiteSpace(fileName))
                set.Add(fileName);
        }
        catch
        {
            // Path.GetFileName может бросить на экзотических COM-значениях.
        }
    }
}
```

### 5.3. `FirewallPolicyChangeWatcher`

```csharp
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Следит за веткой реестра, в которой Windows хранит правила Firewall,
/// и инвалидирует кеш при внешнем изменении.
///
/// Почему реестр:
/// - FwpmFilterSubscribeChanges0 не уведомляет подписчика об изменениях,
///   сделанных его же сессией;
/// - запись в реестр является постоянным хранилищем правил и доступна для чтения
///   без повышения прав.
///
/// Поток полностью событийный: RegNotifyChangeKeyValue блокируется до изменения
/// или таймаута, не потребляя CPU.
/// </summary>
public sealed class FirewallPolicyChangeWatcher : IDisposable
{
    private const string PolicyKeyPath =
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

    private const int RegNotifyChangeName = 0x00000001;
    private const int RegNotifyChangeLastSet = 0x00000004;

    private readonly Action _onChanged;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Создаёт наблюдатель.
    /// </summary>
    /// <param name="onChanged">Вызывается после изменения правил (инвалидация кеша).</param>
    /// <param name="log">Необязательный логгер предупреждений.</param>
    public FirewallPolicyChangeWatcher(Action onChanged, Action<string>? log = null)
    {
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _log = log;
    }

    /// <summary>Запускает фоновый поток наблюдения.</summary>
    public void Start()
    {
        if (_disposed || _started)
            return;
        _started = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "StartupMonitor.FirewallPolicyWatcher",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PolicyKeyPath, writable: false);
                if (key?.Handle is null)
                {
                    WaitOrCancel(TimeSpan.FromSeconds(30));
                    continue;
                }

                using var changeEvent = new AutoResetEvent(false);

                // Асинхронное уведомление: поток засыпает на changeEvent.
                var result = RegNotifyChangeKeyValue(
                    key.Handle.DangerousGetHandle(),
                    bWatchSubtree: true,
                    dwNotifyFilter: RegNotifyChangeName | RegNotifyChangeLastSet,
                    hEvent: changeEvent.SafeWaitHandle.DangerousGetHandle(),
                    fAsynchronous: true);

                if (result != 0)
                {
                    _log?.Invoke($"FirewallPolicyWatcher: RegNotifyChangeKeyValue error {result}");
                    WaitOrCancel(TimeSpan.FromSeconds(30));
                    continue;
                }

                var index = WaitHandle.WaitAny(
                    new WaitHandle[] { changeEvent, _cts.Token.WaitHandle },
                    TimeSpan.FromMinutes(10));

                if (index == 1)
                    break;

                if (index == 0)
                {
                    // Небольшая пауза объединяет серию записей в одно изменение.
                    _onChanged();
                    if (_cts.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250)))
                        break;
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"FirewallPolicyWatcher: {ex.Message}");
                WaitOrCancel(TimeSpan.FromSeconds(30));
            }
        }
    }

    private void WaitOrCancel(TimeSpan timeout)
    {
        try { _cts.Token.WaitHandle.WaitOne(timeout); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        IntPtr hKey,
        bool bWatchSubtree,
        int dwNotifyFilter,
        IntPtr hEvent,
        bool fAsynchronous);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _thread?.Join(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
```

### 5.4. Патч `WindowsFirewallService`

Добавить поля и конструктор:

```csharp
private readonly FirewallRuleCache _ruleCache;
private readonly FirewallPolicyChangeWatcher _policyWatcher;

/// <summary>
/// Инициализирует сервис и единый кеш правил.
/// Кеш общий для GetAllRules() и GetApplicationRulePaths(), поэтому
/// алерт-сервис больше не нуждается в собственном TTL-кеше.
/// </summary>
public WindowsFirewallService()
{
    _ruleCache = new FirewallRuleCache(
        LoadAllRulesCore,
        ttl: TimeSpan.FromSeconds(60),
        failureTtl: TimeSpan.FromSeconds(5));

    _policyWatcher = new FirewallPolicyChangeWatcher(
        _ruleCache.Invalidate,
        message => Log.Debug("{Message}", message));
    _policyWatcher.Start();
}
```

Заменить `GetAllRules` на чтение кеша, а старый COM-код вынести в `LoadAllRulesCore`:

```csharp
/// <summary>
/// Возвращает все правила из кеша. Снимок обновляется не чаще раза в 60 секунд
/// или после явной инвалидации (изменение правила, событие реестра).
/// </summary>
public IReadOnlyList<FirewallRuleDto> GetAllRules()
    => _ruleCache.GetRules();

/// <summary>
/// Полная загрузка правил из COM. Вызывается только из FirewallRuleCache
/// под защитой _loadLock, но берёт _sync для сериализации с операциями записи.
/// </summary>
private IReadOnlyList<FirewallRuleDto> LoadAllRulesCore()
{
    lock (_sync)
    {
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;
            return EnumerateRules((object)rules);
        }
        catch (Exception ex) when (IsFirewallDown(ex))
        {
            Log.Warning(ex, "Firewall недоступен при перечислении правил");
            return Array.Empty<FirewallRuleDto>();
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
        }
    }
}
```

Заменить `GetApplicationRulePaths`:

```csharp
/// <summary>
/// O(1)-набор путей/имён приложений из общего снимка правил.
/// Не выполняет COM-перечисление, если снимок свежий.
/// </summary>
public IReadOnlySet<string> GetApplicationRulePathSet()
    => _ruleCache.GetApplicationPaths();

/// <summary>
/// Совместимый метод для старых вызовов. Создаёт копию множества.
/// Новый код должен использовать <see cref="GetApplicationRulePathSet"/>.
/// </summary>
public HashSet<string> GetApplicationRulePaths()
    => new(GetApplicationRulePathSet(), StringComparer.OrdinalIgnoreCase);
```

Добавить публичную инвалидацию:

```csharp
/// <summary>
/// Принудительно устаревает кеш правил (например, после внешнего импорта).
/// </summary>
public void InvalidateRuleCache() => _ruleCache.Invalidate();
```

`GetStatus()` переписать на кеш, чтобы открытие страницы Firewall не вызывало полный COM-проход:

```csharp
public FirewallStatusDto GetStatus()
{
    using var _ = ExecutionProfiler.Measure("Service.Firewall.GetStatus");
    object? policyObj = null;
    try
    {
        dynamic policy = CreatePolicy(out policyObj);

        int current = 0;
        try { current = (int)policy.CurrentProfileTypes; } catch { current = 0; }

        bool d = IsEnabledSafe(policy, ProfileDomain);
        bool p = IsEnabledSafe(policy, ProfilePrivate);
        bool pub = IsEnabledSafe(policy, ProfilePublic);

        // Один снимок из кеша вместо повторного EnumerateRules.
        var all = GetAllRules();
        int managed = 0, blockedIps = 0;
        foreach (var dto in all)
        {
            if (dto.IsManaged) managed++;
            if (dto.Name == AggregatedRuleNameIn || dto.Name == AggregatedRuleNameOut)
                blockedIps += CountAddresses(dto.RemoteAddresses);
        }

        return new FirewallStatusDto
        {
            Available = true,
            Enabled = d || p || pub,
            CurrentProfiles = current,
            DomainEnabled = d,
            PrivateEnabled = p,
            PublicEnabled = pub,
            ManagedRulesCount = managed,
            BlockedIpsCount = blockedIps,
        };
    }
    catch (Exception ex) when (IsFirewallDown(ex))
    {
        Log.Warning(ex, "Проверка статуса Windows Firewall недоступна");
        return new FirewallStatusDto { Available = false };
    }
    finally
    {
        Release(policyObj);
    }
}
```

`ListManagedRules()` также перевести на кеш:

```csharp
public IReadOnlyList<FirewallRuleDto> ListManagedRules()
    => GetAllRules().Where(r => r.IsManaged).ToList();
```

Инвалидация после мутаций. Добавить вызов `_ruleCache.Invalidate()` в `finally` или сразу после успешной операции в:

- `BlockIp` (`WindowsFirewallService.cs:141`)
- `UnblockIp` (`:188`)
- `BlockPort` (`:293`)
- `UnblockPort` (`:326`)
- `RemoveAllManagedRules` (`:423`)
- `CreateOrUpdateRule` (`:566`)
- `DeleteRule` (`:604`)
- `ToggleRule` (`:661`)
- `SetFilteringMode` (`:726`)
- `ImportRulesBackup` (`:848`)
- `ApplyPreset` (`:923`)

Пример для `CreateOrUpdateRule`:

```csharp
lock (_sync)
{
    try
    {
        // ... существующая запись правила ...
        return FirewallOperationResult.Ok(...);
    }
    catch (Exception ex) when (IsFirewallDown(ex))
    {
        return FirewallOperationResult.Fail(...);
    }
    finally
    {
        // Даже при частичном успехе снимок мог измениться.
        _ruleCache.Invalidate();
    }
}
```

Важно: **никогда не вызывайте `GetAllRules()` внутри `lock (_sync)`**. Это может образовать порядок блокировок `_sync -> _loadLock`, тогда как загрузчик кеша держит `_loadLock` и ждёт `_sync`. Внутри `_sync` используйте только `LoadAllRulesCore()` или прямые COM-операции.

---

## 6. Headless-защита без UI

### 6.1. `NetworkListStore`: белый и чёрный списки

```csharp
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Потокобезопасный снимок белого/чёрного списков и разрешённых портов.
///
/// Семантика:
/// - чёрный список имеет абсолютный приоритет над белым;
/// - полный путь сравнивается без учёта регистра;
/// - запись без пути (например, "opera.exe") считается legacy-записью
///   и сравнивается по имени файла;
/// - снимок заменяется целиком, поэтому читатели не видят частично
///   обновлённые коллекции.
/// </summary>
public sealed class NetworkListStore
{
    private readonly object _reloadLock = new();

    private ListEntrySet _white = ListEntrySet.Empty;
    private ListEntrySet _black = ListEntrySet.Empty;
    private IReadOnlySet<int> _allowedPorts = new HashSet<int>();

    /// <summary>Перечитывает списки из настроек. Вызывается при старте и после изменений.</summary>
    public void Reload(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var white = ListEntrySet.Create(settings.FirewallWhitelistedApps);
        var black = ListEntrySet.Create(settings.FirewallBlacklistedApps);
        var ports = new HashSet<int>(settings.FirewallAllowedPorts.Where(p => p > 0));

        lock (_reloadLock)
        {
            Volatile.Write(ref _white, white);
            Volatile.Write(ref _black, black);
            Volatile.Write(ref _allowedPorts, ports);
        }
    }

    /// <summary>Проверяет чёрный список.</summary>
    public bool IsBlacklisted(string? processPath, string? processName)
        => Volatile.Read(ref _black).Matches(processPath, processName);

    /// <summary>Проверяет белый список.</summary>
    public bool IsWhitelisted(string? processPath, string? processName)
        => Volatile.Read(ref _white).Matches(processPath, processName);

    /// <summary>Проверяет разрешённый порт.</summary>
    public bool IsPortAllowed(int port)
        => port > 0 && Volatile.Read(ref _allowedPorts).Contains(port);

    /// <summary>
    /// Иммутабельный набор путей и имён. Отдельные множества нужны, чтобы
    /// не путать полный путь с именем файла при строгой проверке.
    /// </summary>
    private sealed class ListEntrySet
    {
        public static readonly ListEntrySet Empty = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        private readonly HashSet<string> _paths;
        private readonly HashSet<string> _names;

        private ListEntrySet(HashSet<string> paths, HashSet<string> names)
        {
            _paths = paths;
            _names = names;
        }

        public static ListEntrySet Create(IEnumerable<string>? values)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var value = raw.Trim().Trim('"');
                if (value.Length == 0)
                    continue;

                try { value = Environment.ExpandEnvironmentVariables(value); } catch { }

                var looksLikePath = Path.IsPathRooted(value)
                    || value.Contains('\\')
                    || value.Contains('/');

                if (looksLikePath)
                {
                    try { value = Path.GetFullPath(value); } catch { }
                    paths.Add(value);
                }
                else
                {
                    var fileName = Path.GetFileName(value);
                    if (!string.IsNullOrWhiteSpace(fileName))
                        names.Add(fileName);
                }
            }

            return new ListEntrySet(paths, names);
        }

        public bool Matches(string? processPath, string? processName)
        {
            var path = NormalizePath(processPath);
            if (path.Length > 0)
            {
                if (_paths.Contains(path))
                    return true;

                var fileName = GetFileName(path);
                if (fileName.Length > 0 && _names.Contains(fileName))
                    return true;
            }

            var name = GetFileName(processName);
            return name.Length > 0 && _names.Contains(name);
        }

        private static string NormalizePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var result = value.Trim().Trim('"');
            try { result = Environment.ExpandEnvironmentVariables(result); } catch { }
            try
            {
                if (Path.IsPathRooted(result))
                    result = Path.GetFullPath(result);
            }
            catch { }

            return result.Replace('/', '\\');
        }

        private static string GetFileName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try { return Path.GetFileName(value.Trim().Trim('"')); }
            catch { return string.Empty; }
        }
    }
}
```

Замечание по безопасности: запись белого списка только по имени файла (`opera.exe`) позволяет одноимённому файлу из другой папки получить разрешение. UI должен сохранять полный путь. Для старых записей это осознанная совместимость; в отчёте по UI стоит добавить миграцию имён в полные пути.

### 6.2. `NetworkPolicyEvaluator`

```csharp
namespace StartupMonitor.Service.Services.Firewall;

/// <summary>Вердикт политики для одного подключения.</summary>
public enum NetworkVerdict
{
    /// <summary>Игнорировать (системный/доверенный процесс, приватный адрес и т.п.).</summary>
    Ignore = 0,
    /// <summary>Разрешить без алерта.</summary>
    Allow = 1,
    /// <summary>Заблокировать и завершить процесс.</summary>
    Block = 2,
    /// <summary>Показать алерт пользователю.</summary>
    Prompt = 3,
}

/// <summary>Решение политики с причиной для лога.</summary>
public readonly record struct NetworkDecision(NetworkVerdict Verdict, string Reason);

/// <summary>
/// Единая точка принятия решения по сетевому подключению.
/// Порядок проверок критичен: чёрный список проверяется ДО белого.
/// </summary>
public sealed class NetworkPolicyEvaluator
{
    private readonly NetworkListStore _lists;
    private readonly WindowsFirewallService _firewall;

    public NetworkPolicyEvaluator(NetworkListStore lists, WindowsFirewallService firewall)
    {
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
    }

    /// <summary>
    /// Возвращает вердикт для подключения.
    /// </summary>
    public NetworkDecision Evaluate(
        string? processPath,
        string? processName,
        string remoteAddress,
        int remotePort)
    {
        // 1. Чёрный список — абсолютный приоритет. Даже если приложение
        //    одновременно находится в белом списке, оно блокируется.
        if (_lists.IsBlacklisted(processPath, processName))
            return new NetworkDecision(NetworkVerdict.Block, "blacklist");

        // 2. Белый список — разрешено всё, без алертов.
        if (_lists.IsWhitelisted(processPath, processName))
            return new NetworkDecision(NetworkVerdict.Allow, "whitelist");

        // 3. Доверенные системные процессы.
        if (NetworkConnectionAlertService.IsTrustedProcess(processName, processPath))
            return new NetworkDecision(NetworkVerdict.Ignore, "trusted-system-process");

        // 4. Явно разрешённый пользователем порт.
        if (_lists.IsPortAllowed(remotePort))
            return new NetworkDecision(NetworkVerdict.Allow, "allowed-port");

        // 5. Уже существующее правило Firewall для приложения.
        if (HasFirewallRule(processPath, processName))
            return new NetworkDecision(NetworkVerdict.Ignore, "existing-firewall-rule");

        // 6. Неизвестное внешнее подключение — требуется решение пользователя.
        return new NetworkDecision(NetworkVerdict.Prompt, "unknown-application");
    }

    /// <summary>
    /// Проверяет правило по общему кешу. Сначала полный путь, затем имя файла
    /// (совместимость с текущим поведением Firewall-правил).
    /// </summary>
    private bool HasFirewallRule(string? processPath, string? processName)
    {
        var paths = _firewall.GetApplicationRulePathSet();
        if (paths.Count == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var path = processPath.Trim();
            if (paths.Contains(path))
                return true;

            try
            {
                var fileName = Path.GetFileName(path);
                if (!string.IsNullOrWhiteSpace(fileName) && paths.Contains(fileName))
                    return true;
            }
            catch { }
        }

        return !string.IsNullOrWhiteSpace(processName)
            && paths.Contains(processName.Trim());
    }
}
```

### 6.3. `HeadlessThreatResponder`

```csharp
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.Delayed;
using StartupMonitor.Shared.Helpers;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Реагирует на сетевые события без участия UI:
/// - чёрный список блокирует правило и завершает процесс;
/// - неизвестное подключение ставит алерт в очередь и поднимает UI;
/// - белый список и доверенные процессы игнорируются.
///
/// Все решения принимаются в одном потоке-потребителе ETW, поэтому
/// дополнительные блокировки не нужны, кроме single-flight запуска UI.
/// </summary>
public sealed class HeadlessThreatResponder
{
    private readonly NetworkConnectionAlertService _alerts;
    private readonly NetworkPolicyEvaluator _policy;
    private readonly WindowsFirewallService _firewall;
    private readonly ILogger<HeadlessThreatResponder> _logger;

    // Single-flight: одновременно может выполняться только одна попытка запуска UI.
    private readonly SemaphoreSlim _launchGate = new(1, 1);

    // Cooldown защищает от шторма запусков при серии неизвестных подключений.
    private static readonly TimeSpan LaunchCooldown = TimeSpan.FromSeconds(30);
    private DateTime _lastLaunchAttemptUtc = DateTime.MinValue;

    public HeadlessThreatResponder(
        NetworkConnectionAlertService alerts,
        NetworkPolicyEvaluator policy,
        WindowsFirewallService firewall,
        ILogger<HeadlessThreatResponder> logger)
    {
        _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Обрабатывает одно ETW-событие. Метод не блокирует поток ETW:
    /// запуск UI выполняется в отдельной задаче с cooldown.
    /// </summary>
    public void Handle(NetworkConnectEvent evt)
    {
        if (evt.ProcessId <= 0
            || string.IsNullOrWhiteSpace(evt.RemoteAddress)
            || evt.RemotePort <= 0)
            return;

        // Приватные адреса не представляют внешней угрозы.
        if (Shared.Models.ProcessNetworkSocket.CheckIsPrivateAddress(evt.RemoteAddress))
            return;

        var path = ProcessMetadataEnricher.TryGetProcessPath(evt.ProcessId);
        var name = GetProcessName(path, evt.ProcessId);

        var decision = _policy.Evaluate(path, name, evt.RemoteAddress, evt.RemotePort);
        switch (decision.Verdict)
        {
            case NetworkVerdict.Block:
                BlockProgram(path, name, evt.ProcessId, decision.Reason);
                break;

            case NetworkVerdict.Prompt:
                if (!_alerts.AlertsEnabled)
                    return;

                if (_alerts.EnqueueConnectionAlert(
                        evt.ProcessId,
                        path,
                        name,
                        evt.RemoteAddress,
                        evt.RemotePort,
                        evt.Protocol))
                {
                    // UI уже открыт — его поллер сам покажет окно.
                    // UI закрыт — поднимаем его в активной пользовательской сессии.
                    if (!_alerts.IsMonitorWindowActive)
                        _ = EnsureUiRunningAsync();
                }
                break;

            case NetworkVerdict.Allow:
            case NetworkVerdict.Ignore:
            default:
                break;
        }
    }

    /// <summary>
    /// Применяет блокирующее правило и завершает процесс.
    /// </summary>
    private void BlockProgram(string? path, string? name, int pid, string reason)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var rule = NetworkConnectionAlertService.BuildAppRule(
                    path,
                    name ?? string.Empty,
                    Shared.Models.Firewall.FirewallAction.Block);

                _firewall.CreateOrUpdateRule(rule);
                _firewall.InvalidateRuleCache();
            }

            if (TryKillProcess(pid, path))
                _logger.LogWarning("Headless: процесс {Name} (PID {Pid}) заблокирован ({Reason})",
                    name, pid, reason);
            else
                _logger.LogWarning("Headless: правило для {Name} создано, но завершить PID {Pid} не удалось",
                    name, pid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Headless: не удалось заблокировать {Name} (PID {Pid})", name, pid);
        }
    }

    /// <summary>
    /// Завершает процесс только если его путь совпадает с ожидаемым.
    /// Проверка защищает от PID-reuse между ETW-событием и kill.
    /// </summary>
    private static bool TryKillProcess(int pid, string? expectedPath)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return false;

            if (!string.IsNullOrWhiteSpace(expectedPath))
            {
                var actualPath = ProcessMetadataEnricher.TryGetProcessPath(pid);
                if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Поднимает UI в пользовательской сессии, если он ещё не запущен.
    /// </summary>
    private async Task EnsureUiRunningAsync()
    {
        var now = DateTime.UtcNow;
        if (now - _lastLaunchAttemptUtc < LaunchCooldown)
            return;

        if (!await _launchGate.WaitAsync(0).ConfigureAwait(false))
            return; // уже выполняется другая попытка

        try
        {
            _lastLaunchAttemptUtc = DateTime.UtcNow;

            var sessionId = UserSessionLauncher.GetActiveSessionId();
            if (sessionId is 0 or 0xFFFFFFFF)
            {
                _logger.LogWarning("Headless: активная пользовательская сессия не найдена");
                return;
            }

            if (UserSessionLauncher.IsProcessRunningInSession("StartupMonitor.UI", sessionId))
                return;

            var uiPath = Path.Combine(AppContext.BaseDirectory, "StartupMonitor.UI.exe");
            if (!File.Exists(uiPath))
            {
                var parent = Directory.GetParent(AppContext.BaseDirectory)?.FullName;
                if (parent is not null)
                {
                    var alternative = Path.Combine(parent, "StartupMonitor.UI.exe");
                    if (File.Exists(alternative))
                        uiPath = alternative;
                }
            }

            if (!File.Exists(uiPath))
            {
                _logger.LogError("Headless: StartupMonitor.UI.exe не найден рядом со службой");
                return;
            }

            // Защита от подмены: запускаем только валидно подписанный UI.
            var signature = AuthenticodeVerifier.VerifyFile(uiPath);
            if (signature.Status is not (Shared.Models.DigitalSignatureStatus.Valid
                or Shared.Models.DigitalSignatureStatus.Unknown))
            {
                _logger.LogError("Headless: подпись UI недействительна ({Status}), запуск отменён",
                    signature.Status);
                return;
            }

            // --minimized: UI стартует в трее, но его глобальный поллер
            // сам обнаружит очередь алертов и покажет окно.
            var commandLine = $"\"{uiPath}\" --minimized";
            var result = UserSessionLauncher.Launch(uiPath, commandLine, null);
            if (result.Success)
                _logger.LogInformation("Headless: UI запущен в сессии {Session} (PID {Pid})",
                    sessionId, result.ProcessId);
            else
                _logger.LogWarning("Headless: не удалось запустить UI: {Message}", result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Headless: ошибка автозапуска UI");
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private static string? GetProcessName(string? path, int pid)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            try { return Path.GetFileName(path); } catch { }
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }
        catch
        {
            return null;
        }
    }
}
```

### 6.4. Доработка `UserSessionLauncher`

Добавить нативные структуры и методы:

```csharp
private enum WTS_CONNECTSTATE_CLASS
{
    WTSActive = 0,
    WTSConnected = 1,
    WTSConnectQuery = 2,
    WTSShadow = 3,
    WTSDisconnected = 4,
    WTSIdle = 5,
    WTSListen = 6,
    WTSReset = 7,
    WTSDown = 8,
    WTSInit = 9,
}

[StructLayout(LayoutKind.Sequential)]
private struct WTS_SESSION_INFO
{
    public uint SessionId;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string pWinStationName;
    public WTS_CONNECTSTATE_CLASS State;
}

[DllImport("wtsapi32.dll", SetLastError = true)]
private static extern bool WTSEnumerateSessions(
    IntPtr hServer,
    int Reserved,
    int Version,
    out IntPtr ppSessionInfo,
    out int pCount);

[DllImport("wtsapi32.dll")]
private static extern void WTSFreeMemory(IntPtr pMemory);
```

Добавить методы:

```csharp
/// <summary>
/// Возвращает ID активной пользовательской сессии.
/// Сначала используется активная консольная сессия, затем — любая WTSActive
/// (важно для RDP, где WTSGetActiveConsoleSessionId может вернуть 0xFFFFFFFF).
/// </summary>
public static uint GetActiveSessionId()
{
    var console = WTSGetActiveConsoleSessionId();
    if (console != 0xFFFFFFFF && console != 0)
        return console;

    IntPtr buffer = IntPtr.Zero;
    try
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out buffer, out var count))
            return 0xFFFFFFFF;

        var size = Marshal.SizeOf<WTS_SESSION_INFO>();
        for (var i = 0; i < count; i++)
        {
            var info = Marshal.PtrToStructure<WTS_SESSION_INFO>(
                IntPtr.Add(buffer, i * size));

            if (info.State == WTS_CONNECTSTATE_CLASS.WTSActive && info.SessionId != 0)
                return info.SessionId;
        }

        return 0xFFFFFFFF;
    }
    catch
    {
        return 0xFFFFFFFF;
    }
    finally
    {
        if (buffer != IntPtr.Zero)
            WTSFreeMemory(buffer);
    }
}

/// <summary>
/// Проверяет, запущен ли процесс с указанным именем в конкретной сессии.
/// Используется для защиты от повторного запуска UI.
/// </summary>
public static bool IsProcessRunningInSession(string processName, uint sessionId)
{
    if (string.IsNullOrWhiteSpace(processName))
        return false;

    foreach (var process in Process.GetProcessesByName(processName))
    {
        try
        {
            if ((uint)process.SessionId == sessionId)
                return true;
        }
        catch
        {
            // Процесс мог завершиться между GetProcessesByName и SessionId.
        }
        finally
        {
            process.Dispose();
        }
    }

    return false;
}
```

В `Launch` заменить получение сессии:

```csharp
uint activeSessionId = GetActiveSessionId();
if (activeSessionId != 0xFFFFFFFF && activeSessionId != 0)
{
    var sessionResult = LaunchInSession(activeSessionId, exePath, fullCommandLine, workingDir, logger);
    if (sessionResult.Success)
        return sessionResult;

    logger?.LogWarning(
        "Запуск через WTSQueryUserToken сессии {SessionId} завершился: {Msg}.",
        activeSessionId, sessionResult.Message);
}
```

В `LaunchInSession` убрать `CREATE_NEW_CONSOLE`:

```csharp
// Для WPF-приложения консоль не нужна. CREATE_NEW_CONSOLE создавал лишнее
// консольное окно и не должен использоваться для GUI.
uint creationFlags = NORMAL_PRIORITY_CLASS;
if (lpEnv != IntPtr.Zero)
    creationFlags |= CREATE_UNICODE_ENVIRONMENT;
```

Константу `CREATE_NEW_CONSOLE` можно оставить для других сценариев, но в этом методе не использовать.

---

## 7. Жёсткая логика списков и синхронизация с Firewall

### 7.1. Правила

1. Приложение в чёрном списке:
   - блокирующее правило `Direction=Both`, `Protocol=Any`, `Profiles=All`, `Action=Block`;
   - процесс завершается при старте и при каждом новом подключении;
   - никаких алертов;
   - если приложение одновременно в белом списке — побеждает чёрный;
   - allow-правило для него удаляется.

2. Приложение в белом списке:
   - разрешающее правило `Action=Allow`;
   - никаких алертов;
   - block-правило для него удаляется;
   - если оно одновременно в чёрном — не разрешается.

3. Списки применяются:
   - при старте службы;
   - после `AddToFirewallWhitelist` / `RemoveFromFirewallWhitelist`;
   - после `AddToFirewallBlacklist` / `RemoveFromFirewallBlacklist`;
   - после `ResolveNetworkAlert`.

### 7.2. `NetworkListSynchronizer`

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Приводит правила Windows Firewall в соответствие с белым/чёрным списками.
///
/// Порядок:
/// 1. чёрный список — создаём Block, удаляем Allow;
/// 2. белый список — создаём Allow, удаляем Block, но только если путь
///    не находится в чёрном списке.
///
/// Все имена managed-правил детерминированы и содержат хеш пути,
/// поэтому два разных "app.exe" из разных папок не конфликтуют.
/// </summary>
public sealed class NetworkListSynchronizer
{
    private readonly WindowsFirewallService _firewall;
    private readonly NetworkListStore _lists;
    private readonly ILogger<NetworkListSynchronizer> _logger;

    public NetworkListSynchronizer(
        WindowsFirewallService firewall,
        NetworkListStore lists,
        ILogger<NetworkListSynchronizer> logger)
    {
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _lists = lists ?? throw new ArgumentNullException(nameof(lists));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Полная синхронизация. Вызывается при старте и после изменения списков.
    /// </summary>
    public void Synchronize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _lists.Reload(settings);

        // Один снимок правил на всю синхронизацию.
        var existing = _firewall.GetAllRules();

        // 1. Чёрный список: Block имеет приоритет.
        foreach (var raw in settings.FirewallBlacklistedApps)
        {
            if (!TryNormalize(raw, out var path, out var name))
                continue;

            RemoveRules(existing, path, FirewallAction.Allow);
            Upsert(path, name, FirewallAction.Block);
        }

        // 2. Белый список: Allow только если не в чёрном.
        foreach (var raw in settings.FirewallWhitelistedApps)
        {
            if (!TryNormalize(raw, out var path, out var name))
                continue;

            if (_lists.IsBlacklisted(path, name))
            {
                // Чёрный список сильнее. Гарантируем Block и не создаём Allow.
                RemoveRules(existing, path, FirewallAction.Allow);
                Upsert(path, name, FirewallAction.Block);
                continue;
            }

            RemoveRules(existing, path, FirewallAction.Block);
            Upsert(path, name, FirewallAction.Allow);
        }

        _firewall.InvalidateRuleCache();
        _logger.LogInformation(
            "NetworkLists: синхронизация завершена (white={White}, black={Black})",
            settings.FirewallWhitelistedApps.Count,
            settings.FirewallBlacklistedApps.Count);
    }

    private void Upsert(string path, string name, FirewallAction action)
    {
        var rule = new FirewallRuleDto
        {
            Name = BuildRuleName(path, action),
            Description = action == FirewallAction.Block
                ? $"Заблокировано чёрным списком StartupMonitor: {name} ({path})"
                : $"Разрешено белым списком StartupMonitor: {name} ({path})",
            Direction = FirewallDirection.Both,
            Action = action,
            Protocol = FirewallProtocol.Any,
            ApplicationPath = path,
            Enabled = true,
            Profiles = FirewallProfile.All,
            Grouping = WindowsFirewallService.RuleGroup,
            IsManaged = true,
        };

        _firewall.CreateOrUpdateRule(rule);
    }

    /// <summary>
    /// Удаляет managed-правила противоположного действия для того же пути.
    /// </summary>
    private void RemoveRules(
        IReadOnlyList<FirewallRuleDto> existing,
        string path,
        FirewallAction actionToRemove)
    {
        foreach (var rule in existing)
        {
            if (!rule.IsManaged || rule.Action != actionToRemove)
                continue;
            if (!PathEquals(rule.ApplicationPath, path))
                continue;

            _firewall.DeleteRule(rule.Name, rule.Direction);
        }
    }

    /// <summary>
    /// Детерминированное имя правила: действие + имя файла + короткий хеш пути.
    /// Хеш устраняет коллизии одинаковых имён из разных каталогов.
    /// </summary>
    public static string BuildRuleName(string path, FirewallAction action)
    {
        var normalized = path.Trim().ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(hashBytes)[..8];

        var baseName = "app";
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(fileName))
                baseName = Sanitize(fileName);
        }
        catch { }

        var verb = action == FirewallAction.Allow ? "Allow" : "Block";
        return $"StartupMonitor_{verb}_{baseName}_{hash}_Auto";
    }

    private static bool PathEquals(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return false;

        var a = Normalize(left);
        var b = Normalize(right);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        var result = value.Trim().Trim('"');
        try { result = Environment.ExpandEnvironmentVariables(result); } catch { }
        try
        {
            if (Path.IsPathRooted(result))
                result = Path.GetFullPath(result);
        }
        catch { }
        return result.Replace('/', '\\');
    }

    private static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var clean = new string(chars).Trim('_');
        return string.IsNullOrEmpty(clean) ? "app" : clean;
    }

    private static bool TryNormalize(string? raw, out string path, out string name)
    {
        path = string.Empty;
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var value = raw.Trim().Trim('"');
        if (value.Length == 0)
            return false;

        try { value = Environment.ExpandEnvironmentVariables(value); } catch { }

        var looksLikePath = Path.IsPathRooted(value)
            || value.Contains('\\')
            || value.Contains('/');

        if (!looksLikePath)
        {
            // Legacy-запись по имени: без полного пути блокирующее правило
            // создать нельзя. Такие записи должен мигрировать UI.
            name = value;
            return false;
        }

        try { path = Path.GetFullPath(value); }
        catch { path = value; }

        try { name = Path.GetFileName(path); }
        catch { name = path; }

        return !string.IsNullOrWhiteSpace(path);
    }
}
```

### 7.3. Интеграция в `PipeServer`

После каждого изменения списков вызывать синхронизацию. Например:

```csharp
case PipeMessageTypes.AddToFirewallWhitelist:
    try
    {
        var addPayload = JsonSerializer.Deserialize<FirewallAppActionPayload>(message.Payload ?? "{}", JsonOptions);
        if (addPayload is not null && !string.IsNullOrWhiteSpace(addPayload.Path))
        {
            var cfg = _store.Load();
            if (!cfg.Settings.FirewallWhitelistedApps.Contains(addPayload.Path, StringComparer.OrdinalIgnoreCase))
            {
                cfg.Settings.FirewallWhitelistedApps.Add(addPayload.Path);
                _store.Save(cfg);
            }

            // Немедленно применяем список к Firewall и перечитываем policy.
            _listSynchronizer.Synchronize(cfg.Settings);
            _alertService?.ReloadPolicy(cfg.Settings);
        }

        response.Type = PipeMessageTypes.Success;
    }
    catch (Exception ex)
    {
        response.Type = PipeMessageTypes.Error;
        response.Payload = ex.Message;
    }
    break;
```

Аналогично для `RemoveFromFirewallWhitelist`, `AddToFirewallBlacklist`, `RemoveFromFirewallBlacklist`.

В `ResolveAlert` после сохранения `cfg`:

```csharp
_listSynchronizer.Synchronize(cfg.Settings);
_alertService.ReloadPolicy(cfg.Settings);
```

`_listSynchronizer` добавить в конструктор `PipeServer` или получать через `IServiceProvider`.

---

## 8. Патч `NetworkConnectionAlertService`

### 8.1. Поля и конструктор

Добавить снимок политики и зависимости:

```csharp
private readonly NetworkListStore _listStore;
private readonly NetworkListSynchronizer? _listSynchronizer;

/// <summary>
/// Инициализирует детектор.
/// </summary>
public NetworkConnectionAlertService(
    ProcessNetworkTracker tracker,
    WindowsFirewallService firewall,
    JsonDataStore? store = null,
    NetworkListStore? listStore = null,
    NetworkListSynchronizer? listSynchronizer = null)
{
    _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
    _store = store;
    _listStore = listStore ?? new NetworkListStore();
    _listSynchronizer = listSynchronizer;

    if (_store is not null)
    {
        try
        {
            // GetSettings() не клонирует Entries/History.
            ReloadPolicy(_store.GetSettings());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: не удалось инициализировать списки из хранилища");
        }
    }
}
```

Удалить поля `_whitelistedApps`, `_blacklistedApps`, `_allowedPorts` и `_cachedRulePaths`, `_cachedRulePathsTime`, `_ruleCacheTtl`.

### 8.2. `ReloadPolicy`

```csharp
/// <summary>
/// Перечитывает списки и разрешённые порты. Вызывается при старте,
/// после изменения настроек и после резолва алерта.
/// </summary>
public void ReloadPolicy(AppSettings settings)
{
    _listStore.Reload(settings);
    _wake.Pulse();
}
```

### 8.3. Проверки списков

```csharp
public bool IsWhitelistedApp(string? processPath, string? processName)
    => _listStore.IsWhitelisted(processPath, processName);

public bool IsBlacklistedApp(string? processPath, string? processName)
    => _listStore.IsBlacklisted(processPath, processName);

public bool IsPortAllowed(int port)
    => _listStore.IsPortAllowed(port);
```

### 8.4. `HandleConnection`

```csharp
/// <summary>Результат обработки одного подключения.</summary>
public enum ConnectionDisposition
{
    Ignored = 0,
    Allowed = 1,
    Blocked = 2,
    Prompted = 3,
}

/// <summary>
/// Обрабатывает одно подключение. Используется и ETW-контуром, и fallback-сканом.
/// Порядок проверок: чёрный список -> белый -> trusted -> порт -> правило -> алерт.
/// </summary>
public ConnectionDisposition HandleConnection(
    int pid,
    string remoteAddress,
    int remotePort,
    string protocol,
    string? processName = null)
{
    if (pid <= 0 || string.IsNullOrWhiteSpace(remoteAddress) || remotePort <= 0)
        return ConnectionDisposition.Ignored;

    var settings = _store?.GetSettings();
    if (pid == 0 && (settings?.FirewallSilentPid0 ?? true))
        return ConnectionDisposition.Ignored;

    if (IsPortAllowed(remotePort))
        return ConnectionDisposition.Allowed;

    if (ProcessNetworkSocket.CheckIsPrivateAddress(remoteAddress))
        return ConnectionDisposition.Ignored;

    var name = string.IsNullOrWhiteSpace(processName)
        ? ResolveProcessName(pid)
        : processName.Trim();

    var path = ResolveProcessPath(pid, name);

    // 1. Чёрный список — всегда, даже если алерты выключены.
    if (IsBlacklistedApp(path, name))
    {
        BlockProgram(path, name, pid);
        return ConnectionDisposition.Blocked;
    }

    // 2. Белый список — всегда без алертов.
    if (IsWhitelistedApp(path, name))
        return ConnectionDisposition.Allowed;

    // 3. Доверенные системные процессы.
    if (IsTrustedProcess(name, path))
        return ConnectionDisposition.Ignored;

    // 4. Решения текущей сессии.
    var sessionKey = BuildSessionKey(path, name);
    var nameKey = BuildNameSessionKey(name);
    if (_sessionDecisions.ContainsKey(sessionKey)
        || (!string.IsNullOrWhiteSpace(name) && _sessionDecisions.ContainsKey(nameKey)))
        return ConnectionDisposition.Ignored;

    // 5. Существующее правило — из общего кеша Firewall.
    if (HasExistingFirewallRuleCached(path, name))
        return ConnectionDisposition.Ignored;

    // 6. Алерты выключены: не создаём новые всплывающие окна.
    if (!AlertsEnabled)
        return ConnectionDisposition.Ignored;

    return EnqueueConnectionAlert(pid, path, name, remoteAddress, remotePort, protocol)
        ? ConnectionDisposition.Prompted
        : ConnectionDisposition.Ignored;
}
```

### 8.5. Постановка алерта в очередь

```csharp
/// <summary>
/// Добавляет алерт в очередь с дедупликацией.
/// </summary>
public bool EnqueueConnectionAlert(
    int pid,
    string? path,
    string? name,
    string remoteAddress,
    int remotePort,
    string protocol)
{
    if (_pendingById.Count >= MaxPendingAlerts)
        return false;

    if (_seenKeys.Count > 10000)
        _seenKeys.Clear();

    var sessionKey = BuildSessionKey(path, name);
    var seenKey = sessionKey + "|" + remoteAddress.ToLowerInvariant() + ":" + remotePort;
    if (!_seenKeys.TryAdd(seenKey, 0))
        return false;

    var alert = new NetworkConnectionAlertDto
    {
        ProcessId = pid,
        ProcessName = name ?? string.Empty,
        ProcessPath = path ?? string.Empty,
        Protocol = string.IsNullOrWhiteSpace(protocol) ? "TCP" : protocol,
        RemoteAddress = remoteAddress,
        RemotePort = remotePort,
        DetectedAt = DateTime.UtcNow,
    };

    if (!_pendingById.TryAdd(alert.AlertId, alert))
        return false;

    _queue.Enqueue(alert);
    return true;
}
```

### 8.6. `ScanNewConnections` через `HandleConnection`

```csharp
public int ScanNewConnections()
{
    lock (_scanLock)
    {
        if (!AlertsEnabled)
            return 0;

        List<ProcessNetworkSocket> sockets;
        try
        {
            sockets = SocketsProvider?.Invoke() ?? _tracker.GetSockets();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: не удалось получить список сокетов");
            return 0;
        }

        int added = 0;
        foreach (var socket in sockets)
        {
            if (socket is null)
                continue;

            var before = PendingCount;
            var disposition = HandleConnection(
                socket.ProcessId,
                (socket.RemoteAddress ?? string.Empty).Trim(),
                socket.RemotePort,
                socket.Protocol,
                socket.ProcessName);

            if (disposition == ConnectionDisposition.Prompted && PendingCount > before)
                added++;
        }

        PruneQueue();
        return added;
    }
}
```

### 8.7. Замена локального кеша правил

Удалить `BuildFirewallRulePathSet` и `_cachedRulePaths`. `HasExistingFirewallRuleCached` должен читать общий кеш:

```csharp
private bool HasExistingFirewallRuleCached(string? processPath, string? processName)
{
    if (HasFirewallRuleOverride is not null)
    {
        try { return HasFirewallRuleOverride(processPath ?? string.Empty); }
        catch { return false; }
    }

    if (string.IsNullOrWhiteSpace(processPath) && string.IsNullOrWhiteSpace(processName))
        return false;

    var paths = _firewall.GetApplicationRulePathSet();

    if (!string.IsNullOrWhiteSpace(processPath))
    {
        var path = processPath.Trim();
        if (paths.Contains(path))
            return true;

        try
        {
            var fileName = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(fileName) && paths.Contains(fileName))
                return true;
        }
        catch { }
    }

    return !string.IsNullOrWhiteSpace(processName)
        && paths.Contains(processName.Trim());
}
```

`ApplyRule`:

```csharp
private void ApplyRule(FirewallRuleDto rule)
{
    try
    {
        if (ApplyFirewallRuleOverride is not null)
            ApplyFirewallRuleOverride(rule);
        else
            _firewall.CreateOrUpdateRule(rule);

        // Общий кеш должен увидеть новое правило немедленно.
        _firewall.InvalidateRuleCache();
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "NetworkAlerts: не удалось создать правило {Rule}", rule.Name);
    }
}
```

### 8.8. `ResolveAlert`: black wins и удаление противоположных правил

Для `WhitelistProgram`:

```csharp
case NetworkAlertResolution.WhitelistProgram:
    if (!string.IsNullOrWhiteSpace(path))
    {
        // Белый список не должен оставлять block-правило.
        RemoveManagedRules(path, FirewallAction.Block);
        ApplyRule(BuildAppRule(path, name, FirewallAction.Allow));

        if (_store is not null)
        {
            var cfg = _store.Load();
            cfg.Settings.FirewallWhitelistedApps.RemoveAll(p =>
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (!cfg.Settings.FirewallWhitelistedApps.Contains(path, StringComparer.OrdinalIgnoreCase))
                cfg.Settings.FirewallWhitelistedApps.Add(path);

            // Убираем возможное одновременное присутствие в чёрном списке:
            // пользователь явно разрешил программу.
            cfg.Settings.FirewallBlacklistedApps.RemoveAll(p =>
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

            _store.Save(cfg);
            _listSynchronizer?.Synchronize(cfg.Settings);
            ReloadPolicy(cfg.Settings);
        }
    }
    break;
```

Для `BlacklistProgram`:

```csharp
case NetworkAlertResolution.BlacklistProgram:
    if (!string.IsNullOrWhiteSpace(path))
    {
        // Чёрный список сильнее: удаляем allow и создаём block.
        RemoveManagedRules(path, FirewallAction.Allow);
        ApplyRule(BuildAppRule(path, name, FirewallAction.Block));

        // Завершаем процесс, иначе он продолжит работу до следующего события.
        TryKillProcess(alert.ProcessId, path);

        if (_store is not null)
        {
            var cfg = _store.Load();
            cfg.Settings.FirewallBlacklistedApps.RemoveAll(p =>
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (!cfg.Settings.FirewallBlacklistedApps.Contains(path, StringComparer.OrdinalIgnoreCase))
                cfg.Settings.FirewallBlacklistedApps.Add(path);

            // Убираем из белого списка: чёрный имеет приоритет.
            cfg.Settings.FirewallWhitelistedApps.RemoveAll(p =>
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

            _store.Save(cfg);
            _listSynchronizer?.Synchronize(cfg.Settings);
            ReloadPolicy(cfg.Settings);
        }
    }
    break;
```

Для `BlockAlways`:

```csharp
case NetworkAlertResolution.BlockAlways:
    if (!string.IsNullOrWhiteSpace(path))
    {
        RemoveManagedRules(path, FirewallAction.Allow);
        ApplyRule(BuildAppRule(path, name, FirewallAction.Block));
        TryKillProcess(alert.ProcessId, path);
    }
    break;
```

Для `AllowAlways`:

```csharp
case NetworkAlertResolution.AllowAlways:
    if (!string.IsNullOrWhiteSpace(path))
    {
        RemoveManagedRules(path, FirewallAction.Block);
        ApplyRule(BuildAppRule(path, name, FirewallAction.Allow));
    }
    break;
```

Вспомогательные методы:

```csharp
public static FirewallRuleDto BuildAppRule(string processPath, string processName, FirewallAction action)
{
    var ruleName = NetworkListSynchronizer.BuildRuleName(processPath, action);
    return new FirewallRuleDto
    {
        Name = ruleName,
        Description = action == FirewallAction.Allow
            ? $"Разрешено пользователем через алерты сети: {processName} ({processPath})"
            : $"Заблокировано пользователем через алерты сети: {processName} ({processPath})",
        Direction = FirewallDirection.Both,
        Action = action,
        Protocol = FirewallProtocol.Any,
        ApplicationPath = processPath,
        Enabled = true,
        Profiles = FirewallProfile.All,
        Grouping = WindowsFirewallService.RuleGroup,
        IsManaged = true,
    };
}

private void RemoveManagedRules(string processPath, FirewallAction action)
{
    foreach (var rule in _firewall.GetAllRules())
    {
        if (!rule.IsManaged || rule.Action != action)
            continue;
        if (!string.Equals(rule.ApplicationPath, processPath, StringComparison.OrdinalIgnoreCase))
            continue;

        _firewall.DeleteRule(rule.Name, rule.Direction);
    }

    _firewall.InvalidateRuleCache();
}

private void TryKillProcess(int pid, string? expectedPath)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        if (process.HasExited)
            return;

        if (!string.IsNullOrWhiteSpace(expectedPath))
        {
            var actual = ProcessMetadataEnricher.TryGetProcessPath(pid);
            if (!string.Equals(actual, expectedPath, StringComparison.OrdinalIgnoreCase))
                return;
        }

        process.Kill(entireProcessTree: true);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Log.Debug(ex, "NetworkAlerts: не удалось завершить PID {Pid}", pid);
    }
}
```

---

## 9. Интеграция в `Program.cs` и DI

```csharp
services.AddSingleton<WindowsFirewallService>();
services.AddSingleton<NetworkListStore>();
services.AddSingleton<NetworkListSynchronizer>();
services.AddSingleton<NetworkPolicyEvaluator>();
services.AddSingleton<EtwNetworkConnectionWatcher>();
services.AddSingleton<HeadlessThreatResponder>();
services.AddSingleton<NetworkEventProcessor>();
services.AddHostedService(sp => sp.GetRequiredService<NetworkEventProcessor>());
```

`NetworkConnectionAlertService` создаётся после `NetworkListStore`; DI разрешит зависимости автоматически. Если конструктор оставить с необязательными параметрами, существующие тесты продолжат работать.

При старте службы, до запуска `PipeServer`:

```csharp
// Применяем списки к Firewall сразу, не дожидаясь UI.
try
{
    var settings = dataStore.GetSettings();
    var synchronizer = host.Services.GetRequiredService<NetworkListSynchronizer>();
    synchronizer.Synchronize(settings);

    var alerts = host.Services.GetRequiredService<NetworkConnectionAlertService>();
    alerts.ReloadPolicy(settings);
}
catch (Exception ex)
{
    Log.Warning(ex, "Стартовая синхронизация списков Firewall не выполнена");
}
```

Важно: стартовая синхронизация должна идти **после** `host.Build()` и до `host.Run()`, либо в `StartAsync` отдельного hosted service. Иначе `WindowsFirewallService` может быть ещё не готов.

---

## 10. UI: минимальные изменения

UI уже умеет показывать алерты: `MainViewModel.StartNetworkAlertPoller()` (`MainViewModel.cs:3013-3018`) опрашивает очередь и открывает `NetworkConnectionAlertWindow` (`:3054-3064`). Поэтому для автозапуска достаточно `--minimized`, без нового протокола.

Рекомендуется добавить в `App.xaml.cs` обработку `--network-alert` для случая, когда UI запущен службой и нужно сразу показать окно мониторинга:

```csharp
// В OnStartup после разбора аргументов:
internal static bool IsNetworkAlertBootstrap { get; private set; }

IsNetworkAlertBootstrap = e.Args.Contains("--network-alert");
IsMinimized = e.Args.Contains("--minimized") && !IsNetworkAlertBootstrap;
```

Далее после создания главного окна:

```csharp
if (IsNetworkAlertBootstrap && MainWindow is not null)
{
    MainWindow.Show();
    MainWindow.WindowState = WindowState.Normal;
    MainWindow.Activate();
}
```

Если не хотите менять UI, оставьте только `--minimized`: глобальный поллер всё равно покажет окно алерта. Это минимальный и безопасный путь.

---

## 11. Тесты

### 11.1. Обновить существующие тесты

`tests/StartupMonitor.Tests/Services/NetworkConnectionAlertServiceTests.cs` использует конструктор `(ProcessNetworkTracker, WindowsFirewallService)`. Если добавить необязательные параметры, тесты продолжат компилироваться. Но тесты, которые проверяют списки, нужно дополнить:

- `IsWhitelistedApp` / `IsBlacklistedApp` теперь работают через `NetworkListStore`, который инициализируется `ReloadPolicy`.
- Для тестов без `JsonDataStore` списки пусты; это ожидаемо.

### 11.2. Новые тесты

```csharp
[Fact]
public void Blacklist_WinsOverWhitelist_NoAlertAndBlocks()
{
    var applied = new List<FirewallRuleDto>();
    var svc = CreateService(
        new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
        Paths((1234, EvilPath)),
        appliedRules: applied);

    svc.ReloadPolicy(new AppSettings
    {
        FirewallWhitelistedApps = [EvilPath],
        FirewallBlacklistedApps = [EvilPath],
    });

    var added = svc.ScanNewConnections();

    Assert.Equal(0, added);
    Assert.Empty(svc.GetPendingAlerts());
    Assert.Contains(applied, r => r.Action == FirewallAction.Block && r.ApplicationPath == EvilPath);
}

[Fact]
public void Blacklist_BlocksEvenWhenAlertsDisabled()
{
    var applied = new List<FirewallRuleDto>();
    var svc = CreateService(
        new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
        Paths((1234, EvilPath)),
        appliedRules: applied);

    svc.ReloadPolicy(new AppSettings { FirewallBlacklistedApps = [EvilPath] });
    svc.SetAlertsEnabled(false);

    var added = svc.ScanNewConnections();

    Assert.Equal(0, added);
    Assert.Contains(applied, r => r.Action == FirewallAction.Block);
}
```

`FirewallRuleCache`:

```csharp
[Fact]
public void Cache_LoadsOnceWithinTtl_AndReloadsAfterInvalidate()
{
    var loads = 0;
    var cache = new FirewallRuleCache(
        () => { loads++; return new[] { new FirewallRuleDto { Name = "r" + loads } }; },
        TimeSpan.FromMinutes(1));

    Assert.Equal("r1", cache.GetRules()[0].Name);
    Assert.Equal("r1", cache.GetRules()[0].Name);
    Assert.Equal(1, loads);

    cache.Invalidate();

    Assert.Equal("r2", cache.GetRules()[0].Name);
    Assert.Equal(2, loads);
}
```

`NetworkPolicyEvaluator`:

```csharp
[Fact]
public void Evaluator_BlacklistBeatsWhitelist()
{
    var lists = new NetworkListStore();
    lists.Reload(new AppSettings
    {
        FirewallWhitelistedApps = [EvilPath],
        FirewallBlacklistedApps = [EvilPath],
    });

    var evaluator = new NetworkPolicyEvaluator(lists, new WindowsFirewallService());
    var decision = evaluator.Evaluate(EvilPath, "evil.exe", "93.184.216.34", 443);

    Assert.Equal(NetworkVerdict.Block, decision.Verdict);
}
```

`NetworkListSynchronizer`:

```csharp
[Fact]
public void Synchronizer_BlacklistCreatesBlockAndRemovesAllow()
{
    var firewall = new FakeFirewallService();
    var lists = new NetworkListStore();
    var sync = new NetworkListSynchronizer(firewall, lists, NullLogger<NetworkListSynchronizer>.Instance);

    sync.Synchronize(new AppSettings
    {
        FirewallWhitelistedApps = [EvilPath],
        FirewallBlacklistedApps = [EvilPath],
    });

    Assert.Contains(firewall.Rules, r => r.Action == FirewallAction.Block && r.ApplicationPath == EvilPath);
    Assert.DoesNotContain(firewall.Rules, r => r.Action == FirewallAction.Allow && r.ApplicationPath == EvilPath);
}
```

Для `FakeFirewallService` нужно либо сделать `WindowsFirewallService` виртуальным/интерфейсом, либо использовать `ApplyFirewallRuleOverride`. В текущем коде проще добавить интерфейс `IFirewallService` и внедрять его; но это отдельный рефакторинг. Минимальный вариант — тестировать `NetworkListSynchronizer` через реальный `WindowsFirewallService` на тестовой машине с осторожностью. Лучше добавить интерфейс.

### 11.3. Ручные проверки

- ETW: запустить службу, открыть браузер, убедиться, что событие приходит; `logman query -ets` показывает сессию `StartupMonitor-NetworkWatcher`.
- Закрыть UI, открыть новое внешнее соединение неизвестной программой: должен появиться алерт и/или UI.
- Добавить программу в чёрный список: правило Block создано, процесс завершён, алертов нет.
- Добавить ту же программу в белый список: block-правило удалено, allow создано, алертов нет.
- Изменить правило через `wf.msc`: в течение TTL/события реестра кеш обновляется.

---

## 12. Измерение 0.0% CPU

Перед измерениями:

1. Закрыть UI.
2. Убедиться, что `AlertsEnabled = true`, но `hasWork` возвращает `false` (UI не активен).
3. Убедиться, что ETW-сессия запущена.
4. Не открывать страницы Firewall в UI.

Команды:

```powershell
# Средняя загрузка процесса службы за 60 секунд
Get-Counter "\Process(StartupMonitor.Service)\% Processor Time" -SampleInterval 1 -MaxSamples 60 |
  Select-Object -ExpandProperty CounterSamples |
  Measure-Object -Property CookedValue -Average -Minimum -Maximum

# Активные ETW-сессии
logman query -ets | Select-String "StartupMonitor"

# Проверка, что скан не идёт каждые 2 секунды
# В логах не должно быть повторяющихся "NetworkAlerts" записей при закрытом UI.
```

Ожидаемый результат: `Average` около 0, допустимы редкие всплески при сетевых событиях. Если среднее > 0.1% при полном простое — искать оставшиеся таймеры (`ProcessPortDefenderService` должен быть в Idle, `NetworkConnectionAlertService` не должен сканировать, UI не подключён).

Дополнительно:

```powershell
# Проверить, что UI не подключён к pipe
Get-Process StartupMonitor.UI -ErrorAction SilentlyContinue

# Проверить потоки службы
Get-Process StartupMonitor.Service | Select-Object -ExpandProperty Threads |
  Sort-Object TotalProcessorTime -Descending |
  Select-Object -First 10 Id, TotalProcessorTime, ThreadState
```

---

## 13. Риски и ограничения

1. **ETW — пост-фактум.** `TcpIpConnect` фиксирует уже установленное соединение. Для гарантированной блокировки до первого пакета нужен WFP callout driver или outbound default-deny. ETW-контур снижает окно, но не устраняет его полностью. В отчёте это важно зафиксировать, чтобы не создавать ложное ощущение «блокировки до сети».

2. **Конфликт ETW-сессий.** Приватная сессия использует SystemTraceProvider (Windows 8+). На Windows 7 доступен только `NT Kernel Logger`, который может быть занят. Служба должна логировать ошибку и переходить в fallback-режим. Целевая ОС проекта — Windows 10/11, поэтому риск низкий.

3. **Потеря событий при шторме.** Ограниченный канал с `DropOldest` может потерять часть событий. Для критичных сценариев нужно увеличить ёмкость, добавить метрику `DroppedEvents` и, при необходимости, отдельную очередь для чёрного списка.

4. **TTL кеша 60 секунд.** Внешнее изменение, не вызвавшее уведомление реестра, может быть не видно до 60 секунд. Для UI-страницы правил можно добавить команду `RefreshFirewallRules` с принудительной инвалидацией.

5. **Реестровый watcher.** `RegNotifyChangeKeyValue` уведомляет об изменениях ветки, но не гарантирует, что каждое изменение `INetFwPolicy2` немедленно отражено в реестре. TTL остаётся страховкой.

6. **Общие DTO в кеше.** `GetAllRules()` возвращает ссылки на объекты снимка. Вызывающий код не должен их мутировать. Если это невозможно гарантировать, нужно возвращать копии (дороже) или сделать DTO `record`/`init`.

7. **Запуск UI из службы.** `WTSQueryUserToken` требует `SeTcbPrivilege`; LocalSystem обладает этим правом. Если служба запущена под ограниченной учётной записью, запуск не удастся. Логировать ошибку и не пытаться `Process.Start` от SYSTEM.

8. **Подпись UI.** Проверка Authenticode обязательна. В тестовой сборке подпись может быть `Unknown`; в production допускать только `Valid`, иначе есть риск подмены бинарника.

9. **Многосессионность.** `WTSEnumerateSessions` выбирает первую активную сессию. При нескольких одновременных активных сессиях (консоль + RDP) нужна политика выбора. Текущий приоритет — консоль.

10. **Ложные срабатывания по имени.** Legacy-записи белого списка без пути разрешают одноимённые файлы из любых каталогов. UI должен мигрировать их в полные пути.

---

## 14. План внедрения по шагам

1. Добавить `Microsoft.Diagnostics.Tracing.TraceEvent` в `StartupMonitor.Service.csproj`.
2. Добавить `FirewallRuleCache`, `FirewallPolicyChangeWatcher`, `FirewallRuleCache`-поля в `WindowsFirewallService`.
3. Перевести `GetAllRules`, `GetApplicationRulePaths`, `GetStatus`, `ListManagedRules` на кеш; добавить инвалидацию во все мутации.
4. Исправить `hasWork` в `NetworkConnectionAlertService`.
5. Убрать `ScanNewConnections()` из `GetPendingNetworkAlerts` в `PipeServer`.
6. Заменить `_store.Load()` на `GetSettings()` в горячем пути.
7. Добавить `NetworkListStore`, `NetworkPolicyEvaluator`, `NetworkListSynchronizer`.
8. Переписать проверки списков: чёрный раньше белого, блокировка и kill для чёрного.
9. Добавить `EtwNetworkConnectionWatcher`, `NetworkEventProcessor`, `HeadlessThreatResponder`.
10. Доработать `UserSessionLauncher`: `WTSEnumerateSessions`, убрать `CREATE_NEW_CONSOLE`.
11. Зарегистрировать сервисы в DI и запустить стартовую синхронизацию списков.
12. Добавить вызовы `Synchronize`/`ReloadPolicy` после изменения списков в `PipeServer`.
13. Обновить тесты и добавить новые.
14. Провести замеры CPU при закрытом UI и ручные сценарии с чёрным/белым списком.

---

## Приложение A. Карта файлов

| Файл | Изменение |
|---|---|
| `src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs` | `hasWork`, `GetSettings`, порядок списков, `HandleConnection`, `ReloadPolicy`, удаление локального кеша |
| `src/StartupMonitor.Service/Services/Firewall/EtwNetworkConnectionWatcher.cs` | Новый ETW-наблюдатель |
| `src/StartupMonitor.Service/Services/Firewall/NetworkEventProcessor.cs` | Новый потребитель очереди ETW |
| `src/StartupMonitor.Service/Services/Firewall/HeadlessThreatResponder.cs` | Новая автономная реакция и автозапуск UI |
| `src/StartupMonitor.Service/Services/Firewall/NetworkListStore.cs` | Новый снимок списков |
| `src/StartupMonitor.Service/Services/Firewall/NetworkPolicyEvaluator.cs` | Новая политика |
| `src/StartupMonitor.Service/Services/Firewall/NetworkListSynchronizer.cs` | Новая синхронизация списков с Firewall |
| `src/StartupMonitor.Service/Services/Firewall/FirewallRuleCache.cs` | Новый кеш правил |
| `src/StartupMonitor.Service/Services/Firewall/FirewallPolicyChangeWatcher.cs` | Новый watcher реестра |
| `src/StartupMonitor.Service/Services/WindowsFirewallService.cs` | Кеш, инвалидация, `GetApplicationRulePathSet`, `InvalidateRuleCache` |
| `src/StartupMonitor.Service/Services/Delayed/UserSessionLauncher.cs` | `WTSEnumerateSessions`, `GetActiveSessionId`, `IsProcessRunningInSession`, флаги GUI |
| `src/StartupMonitor.Service/Pipe/PipeServer.cs` | Убрать скан из `GetPendingNetworkAlerts`, синхронизация списков |
| `src/StartupMonitor.Service/Program.cs` | DI, стартовая синхронизация |
| `src/StartupMonitor.Shared/Models/Firewall/NetworkAlertModels.cs` | Опционально `SessionId` |
| `tests/StartupMonitor.Tests/Services/NetworkConnectionAlertServiceTests.cs` | Новые тесты black-wins, alerts-disabled, block |
| `tests/StartupMonitor.Tests/Services/FirewallRuleCacheTests.cs` | Новые тесты TTL/инвалидации |

---

## Приложение B. Ключевые текущие строки

- `NetworkConnectionAlertService.cs:123` — `_store.Load()` в конструкторе.
- `NetworkConnectionAlertService.cs:185` — `hasWork: () => AlertsEnabled`.
- `NetworkConnectionAlertService.cs:247` — `_store.Load()` на каждый скан.
- `NetworkConnectionAlertService.cs:272-278` — whitelist раньше blacklist, blacklist только `continue`.
- `NetworkConnectionAlertService.cs:579-601` — локальный кеш путей.
- `WindowsFirewallService.cs:461-485` — `GetAllRules` без кеша.
- `WindowsFirewallService.cs:492-560` — `GetApplicationRulePaths` без общего кеша.
- `PipeServer.cs:1575` — `GetAllRules()` на UI-команду.
- `PipeServer.cs:1837` — скан на каждый `GetPendingNetworkAlerts`.
- `MainViewModel.cs:3017` — поллер 2 секунды.
- `NetworkSecurityHubView.xaml.cs:252` — поллер 2.5 секунды.
- `Program.cs:29-30` — служба не запускает UI.
- `UserSessionLauncher.cs:171-181` — только консольная сессия.
- `UserSessionLauncher.cs:269` — `CREATE_NEW_CONSOLE`.
- `App.xaml.cs:62-83, 355-363` — второй экземпляр UI.
