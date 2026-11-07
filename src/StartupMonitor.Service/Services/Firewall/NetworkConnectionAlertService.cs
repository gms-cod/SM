using System.Collections.Concurrent;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Service.Services.Runtime;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Детектор новых сетевых подключений программ во внешнюю сеть.
/// Периодически опрашивает <see cref="ProcessNetworkTracker"/> и ставит в очередь
/// <see cref="NetworkConnectionAlertDto"/> для программ без правил в Windows Firewall.
/// </summary>
/// <remarks>
/// Правила фильтрации (алерт НЕ создаётся):
/// <list type="bullet">
/// <item>LAN / Loopback / unspecified адреса (<see cref="ProcessNetworkSocket.CheckIsPrivateAddress"/>).</item>
/// <item>Доверенные системные процессы (<see cref="TrustedSystemProcesses"/>).</item>
/// <item>Программы, для которых уже есть правило в <see cref="WindowsFirewallService"/>.</item>
/// <item>Программы, разрешённые/заблокированные решением текущей сессии.</item>
/// <item>Программы из белого или чёрного списков.</item>
/// </list>
/// Thread-safe: очередь и кеши решений на <c>Concurrent*</c>-коллекциях, скан под <c>lock</c>.
/// </remarks>
public sealed class NetworkConnectionAlertService : IDisposable
{
    private static readonly Serilog.ILogger Log =
        Serilog.Log.ForContext<NetworkConnectionAlertService>();

    /// <summary>Доверенные системные процессы: алерты по ним не создаются никогда.</summary>
    public static readonly HashSet<string> TrustedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "svchost.exe",
        "lsass.exe",
        "services.exe",
        "explorer.exe",
        "Widgets.exe",
        "SearchHost.exe",
        "StartMenuExperienceHost.exe",
        "ShellExperienceHost.exe",
        "sihost.exe",
        "ctfmon.exe",
        "RuntimeBroker.exe",
        "dwm.exe",
        "spoolsv.exe",
        "StartupMonitor.Service.exe",
        "StartupMonitor.UI.exe",
    };

    /// <summary>Верхняя граница очереди ожидающих алертов (защита от переполнения памяти).</summary>
    public const int MaxPendingAlerts = 1000;

    /// <summary>Максимальное количество записей в журнале истории подключений.</summary>
    public const int MaxHistoryItems = 2500;

    /// <summary>Кольцевая очередь истории сетевых подключений.</summary>
    private readonly ConcurrentQueue<NetworkConnectionHistoryItem> _historyQueue = new();
    private readonly ConcurrentDictionary<string, NetworkConnectionHistoryItem> _historyById = new();
    private readonly ConcurrentDictionary<string, DateTime> _historyDedup = new(StringComparer.OrdinalIgnoreCase);

    private readonly ProcessNetworkTracker _tracker;
    private readonly WindowsFirewallService _firewall;
    private readonly JsonDataStore? _store;
    /// <summary>Единый владелец временных исходящих правил; подменяется в изолированных тестах.</summary>
    private readonly ISessionFirewallPolicy _sessionFirewall;
    // Потокобезопасные коллекции (ConcurrentDictionary) для исключения гонок потоков и сбоев итерации
    private readonly ConcurrentDictionary<string, byte> _whitelistedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _blacklistedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, byte> _allowedPorts = new();

    // ── Тестовые швы (test seams): в проде null → реальное поведение ──

    /// <summary>Переопределение источника сокетов (по умолчанию — <c>_tracker.GetSockets()</c>).</summary>
    public Func<List<ProcessNetworkSocket>>? SocketsProvider { get; set; }

    /// <summary>Переопределение резолва пути процесса по PID.</summary>
    public Func<int, string?>? ProcessPathResolver { get; set; }

    /// <summary>Переопределение проверки наличия правила Firewall для пути программы.</summary>
    public Func<string, bool>? HasFirewallRuleOverride { get; set; }

    /// <summary>Переопределение применения правила Firewall (по умолчанию — CreateOrUpdateRule).</summary>
    public Action<FirewallRuleDto>? ApplyFirewallRuleOverride { get; set; }

    /// <summary>Переопределение оценки сетевой активности нейросетью (тестовый шов).</summary>
    public Func<string, string, string, int, Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?>>? AiEvaluatorOverride { get; set; }

    private AiRatingService? _aiRating;
    private volatile bool _aiAutonomousNetworkDecisionsEnabled;
    private volatile string _aiAutonomousDecisionMode = "AllowSession";

    // ── Состояние ──

    /// <summary>Известные решения текущей сессии: ключ сессии → решение.</summary>
    private readonly ConcurrentDictionary<string, NetworkAlertResolution> _sessionDecisions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Программы с незавершённой автономной оценкой; не являются подтверждёнными разрешениями.</summary>
    private readonly ConcurrentDictionary<string, long> _autonomousPending = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Поколение оценки защищает от старого ответа после отмены и повторного запуска для того же пути.</summary>
    private long _autonomousGeneration;

    /// <summary>Потокобезопасная очередь алертов (порядок обнаружения).</summary>
    private readonly ConcurrentQueue<NetworkConnectionAlertDto> _queue = new();

    /// <summary>Ожидающие алерты по AlertId (для резолва и дедупликации выдачи).</summary>
    private readonly ConcurrentDictionary<string, NetworkConnectionAlertDto> _pendingById = new();

    /// <summary>Уже виденные связки "программа → локальный сокет → remote:port" с TTL (защита от спама).</summary>
    private readonly ConcurrentDictionary<string, DateTime> _seenKeys =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Время жизни записи дедупликации: соединение с тем же remote:port снова даст алерт после TTL.</summary>
    private static readonly TimeSpan SeenKeyTtl = TimeSpan.FromMinutes(10);

    /// <summary>Последняя очистка устаревших записей дедупликации (не чаще раза в минуту).</summary>
    private DateTime _lastSeenPrune = DateTime.MinValue;

    /// <summary>
    /// Срок жизни сессионных решений (<see cref="NetworkAlertResolution.AllowSession"/> /
    /// <see cref="NetworkAlertResolution.BlockSession"/> / <see cref="NetworkAlertResolution.CreateRule"/>).
    /// Ограниченный TTL не позволяет одному проигнорированному окну навсегда заглушить программу.
    /// </summary>
    private static readonly TimeSpan SessionDecisionTtl = TimeSpan.FromHours(8);

    /// <summary>Срок действия каждого сессионного решения (DateTime.MaxValue — бессрочно).</summary>
    private readonly ConcurrentDictionary<string, DateTime> _sessionDecisionExpiry =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _scanLock = new();
    /// <summary>Ограниченный журнал подтверждений; изменяется только под блокировкой сканера.</summary>
    private readonly Dictionary<string, NetworkAlertResolution> _completedResolutions = new(StringComparer.Ordinal);
    /// <summary>Порядок вытеснения подтверждений без неограниченного накопления идентификаторов.</summary>
    private readonly Queue<string> _completedResolutionOrder = new();
    private readonly object _settingsLock = new();
    private bool _disposed;

    // ── Кэш правил брандмауэра для обеспечения 0% CPU ──
    private HashSet<string>? _cachedRulePaths;
    private DateTime _cachedRulePathsTime = DateTime.MinValue;
    private readonly TimeSpan _ruleCacheTtl = TimeSpan.FromSeconds(60);

    // ── Кэш настроек из хранилища (обновляется только при изменении ревизии) ──
    private long _settingsRevision = -1;
    private volatile bool _silentPid0 = true;

    // ── Фоновый цикл в автономном режиме ──
    private readonly ActivityGate _wake = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private TimeSpan _due = TimeSpan.FromSeconds(5);
    private TimeSpan _period = TimeSpan.FromSeconds(5);

    /// <summary>Открыто ли окно мониторинга сети у активного UI-клиента.</summary>
    private volatile bool _monitorWindowActive;

    /// <summary>Включён ли детектор (переключается через Pipe: SetAlertsEnabled).</summary>
    private volatile bool _alertsEnabled = true;

    /// <summary>Включён ли детектор сетевых алертов (потокобезопасное чтение флага).</summary>
    public bool AlertsEnabled => _alertsEnabled;

    /// <summary>Число ожидающих алертов.</summary>
    public int PendingCount => _pendingById.Count;

    /// <summary>Решения текущей сессии (только чтение).</summary>
    public IReadOnlyDictionary<string, NetworkAlertResolution> SessionDecisions
    {
        get
        {
            lock (_scanLock)
            {
                foreach (var key in _sessionDecisions.Keys)
                    HasActiveSessionDecision(key);
                return new Dictionary<string, NetworkAlertResolution>(_sessionDecisions, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Инициализирует детектор новых сетевых подключений.
    /// </summary>
    /// <param name="tracker">Источник снимков TCP-соединений.</param>
    /// <param name="firewall">Адаптер постоянной политики Windows Firewall.</param>
    /// <param name="store">Необязательное хранилище постоянных списков и настроек.</param>
    /// <param name="aiRating">Необязательная оценка сетевого поведения ИИ.</param>
    /// <param name="sessionFirewall">Владелец временной политики либо управляемый адаптер изолированных тестов.</param>
    public NetworkConnectionAlertService(
        ProcessNetworkTracker tracker,
        WindowsFirewallService firewall,
        JsonDataStore? store = null,
        AiRatingService? aiRating = null,
        ISessionFirewallPolicy? sessionFirewall = null)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _store = store;
        _sessionFirewall = sessionFirewall ?? new SessionFirewallPolicy(_firewall);
        _aiRating = aiRating;

        if (_store != null)
        {
            try
            {
                var cfg = _store.Load();
                _silentPid0 = cfg.Settings.FirewallSilentPid0;
                _aiAutonomousNetworkDecisionsEnabled = cfg.Settings.AiAutonomousNetworkDecisionsEnabled;
                _aiAutonomousDecisionMode = string.IsNullOrWhiteSpace(cfg.Settings.AiAutonomousDecisionMode) ? "AllowSession" : cfg.Settings.AiAutonomousDecisionMode.Trim();
                _settingsRevision = _store.Revision;
                foreach (var app in cfg.Settings.FirewallWhitelistedApps)
                {
                    if (!string.IsNullOrWhiteSpace(app))
                        _whitelistedApps.TryAdd(app.Trim(), 0);
                }
                foreach (var app in cfg.Settings.FirewallBlacklistedApps)
                {
                    if (!string.IsNullOrWhiteSpace(app))
                    {
                        // Приоритет Blacklist > Whitelist: удаляем из белого списка при загрузке чёрного
                        _whitelistedApps.TryRemove(app.Trim(), out _);
                        _blacklistedApps.TryAdd(app.Trim(), 0);
                    }
                }
                foreach (var p in cfg.Settings.FirewallAllowedPorts)
                {
                    if (p > 0)
                        _allowedPorts.TryAdd(p, 0);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NetworkAlerts: не удалось инициализировать списки из хранилища");
            }
        }
    }

    /// <summary>Устанавливает или актуализирует сервис оценки нейросетью.</summary>
    public void SetAiRatingService(AiRatingService aiRating)
    {
        _aiRating = aiRating;
    }

    /// <summary>Запускает фоновый опрос новых подключений (вызывается из PipeServer).</summary>
    /// <param name="dueTime">Задержка перед первым сканом (не блокирует старт службы).</param>
    /// <param name="period">Интервал между сканами при включённом детекторе.</param>
    public void StartBackgroundScan(TimeSpan? dueTime = null, TimeSpan? period = null)
    {
        StopBackgroundScan();
        _due = dueTime ?? TimeSpan.FromSeconds(5);
        _period = period ?? TimeSpan.FromSeconds(5);
        var cts = new CancellationTokenSource();
        _cts = cts;
        // Первый скан откладывается на _due: конструктор PipeServer больше не блокируется
        // на синхронном COM-перечислении правил Firewall при старте службы.
        _loop = StartLoopAsync(cts.Token);
    }

    /// <summary>Выдерживает начальную задержку и запускает адаптивный цикл сканирования.</summary>
    private async Task StartLoopAsync(CancellationToken ct)
    {
        try
        {
            if (_due > TimeSpan.Zero)
                await Task.Delay(_due, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Останавливает фоновый опрос (полная остановка цикла).</summary>
    public void StopBackgroundScan()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts == null) return;

        // Перед возвратом подтверждаем завершение: последующий Dispose шлюза не гоняется с WaitAsync.
        cts.Cancel();
        try { loop?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { Log.Debug("NetworkAlerts: фоновый сборщик остановлен"); }
        finally { cts.Dispose(); }
    }

    /// <summary>
    /// Открытие/закрытие окна мониторинга сети активным UI-клиентом.
    /// </summary>
    public void SetMonitorWindowActive(bool active)
    {
        if (_monitorWindowActive == active) return;
        _monitorWindowActive = active;
        _wake.Pulse();
    }

    /// <summary>
    /// Фоновый цикл: служба сканирует автономно, пока включен флаг AlertsEnabled.
    /// Благодаря кэшированию правил Windows Firewall с TTL 60с нагрузка на CPU составляет 0%.
    /// </summary>
    private Task RunAsync(CancellationToken ct)
        => SleepAwareLoop.RunAsync(
            _wake,
            hasWork: () => AlertsEnabled,
            activeInterval: _period,
            idleInterval: Timeout.InfiniteTimeSpan,
            body: _ =>
            {
                try
                {
                    ScanNewConnections();
                }
                catch (Exception ex)
                {
                    // Прозрачность ошибок: сбой скана логируется, цикл не падает.
                    Log.Warning(ex, "NetworkAlerts: сбой фонового сканирования");
                }
                return Task.CompletedTask;
            },
            ct);

    /// <summary>Отменяет и дожидается сборщика до освобождения используемого им шлюза активности.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autonomousPending.Clear();
        StopBackgroundScan();
        _sessionFirewall.Dispose();
        _wake.Dispose();
    }

    /// <summary>Включает/выключает детектор. Выключение сбрасывает очередь ожидающих алертов.</summary>
    public void SetAlertsEnabled(bool enabled)
    {
        _alertsEnabled = enabled;
        if (!enabled)
            ClearPending();
        _wake.Pulse();
    }

    /// <summary>
    /// Очищает очередь ожидающих алертов и кэш дедупликации.
    /// Сброс дедупликации обязателен: иначе после повторного включения детектора
    /// уже «виденные» соединения больше никогда не дадут алерт.
    /// </summary>
    public void ClearPending()
    {
        _pendingById.Clear();
        _seenKeys.Clear();
        PruneQueue(force: true);
    }

    /// <summary>
    /// Сканирует активные сокеты и ставит в очередь алерты о новых внешних подключениях.
    /// </summary>
    /// <returns>Число новых алертов, добавленных за этот проход.</returns>
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
            // Один PID может иметь сотни соединений: путь запрашивается один раз в пределах снимка.
            // Кэш не переживает снимок и потому не подменяет путь при переиспользовании PID другой программой.
            var pathsByPid = new Dictionary<int, string?>();
            // Режим читается не чаще одного раза на снимок, когда действительно нужен новый алерт.
            bool? sessionAllowSupported = null;
            // Кэш настроек обновляется только при изменении ревизии хранилища:
            // больше нет глубокого клонирования всего конфига (включая историю) каждые 5 секунд.
            RefreshSettingsCache();
            var isSilentPid0 = _silentPid0;
            // Кэшированный набор путей разрешающих ИСХОДЯЩИХ правил Firewall с TTL 60с.
            var rulePaths = BuildFirewallRulePathSet();
            PruneSeenKeys();
            foreach (var socket in sockets)
            {
                if (socket is null)
                    continue;

                var remote = (socket.RemoteAddress ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(remote) || socket.RemotePort <= 0)
                    continue;

                // 0. Учитываем только реальные исходящие TCP-соединения (ESTABLISHED/SYN_SENT).
                // TIME_WAIT/CLOSE_WAIT/LISTENING — мусорные состояния, они не должны порождать алерты.
                if (!IsAlertableSocket(socket))
                    continue;

                // 0.1 Тихий режим: сокеты без определенного процесса (PID = 0) не создают всплывающих окон при включенной опции
                if (socket.ProcessId == 0 && isSilentPid0)
                    continue;

                // 0.2 Пропускаем порты, разрешенные пользователем (подавление всплывающих окон по этому порту)
                if (IsPortAllowed(socket.RemotePort))
                    continue;

                // 1. Игнорируем LAN / Loopback / unspecified.
                if (socket.IsRemotePrivate || ProcessNetworkSocket.CheckIsPrivateAddress(remote))
                    continue;

                var processName = (socket.ProcessName ?? string.Empty).Trim();
                if (!pathsByPid.TryGetValue(socket.ProcessId, out var processPath))
                {
                    processPath = ResolveProcessPath(socket.ProcessId, processName);
                    pathsByPid[socket.ProcessId] = processPath;
                }

                var sessionKey = BuildSessionKey(processPath, processName);

                // 1.1 Абсолютный приоритет: Пропускаем программы из чёрного списка (соединения заблокированы, алерты подавлены)
                if (IsBlacklistedApp(processPath, processName))
                {
                    TryLogHistory(socket, processPath, processName, remote, NetworkConnectionVerdict.Blacklisted, "Заблокировано правилом чёрного списка");
                    continue;
                }

                // Временный запрет перекрывает постоянное разрешение, как и реальный WFP block.
                if (_sessionDecisions.TryGetValue(sessionKey, out var temporaryDecision) &&
                    temporaryDecision == NetworkAlertResolution.BlockSession && HasActiveSessionDecision(sessionKey))
                {
                    TryLogHistory(socket, processPath, processName, remote, NetworkConnectionVerdict.BlockSession, "Решение текущей сессии");
                    continue;
                }

                // 1.2 Пропускаем программы из белого списка (только если приложение отсутствует в чёрном списке)
                if (IsWhitelistedApp(processPath, processName))
                {
                    TryLogHistory(socket, processPath, processName, remote, NetworkConnectionVerdict.Whitelisted, "Разрешено белым списком");
                    continue;
                }

                // 2. Игнорируем доверенные системные процессы.
                if (IsTrustedProcess(processName, processPath))
                    continue;

                // 3. Пропускаем уже решённые в текущей сессии (по пути или по имени исполняемого файла)
                if (HasActiveSessionDecision(sessionKey))
                {
                    var isAllow = _sessionDecisions.TryGetValue(sessionKey, out var decision)
                        && decision is NetworkAlertResolution.AllowSession or NetworkAlertResolution.AllowAlways
                            or NetworkAlertResolution.WhitelistProgram;
                    TryLogHistory(socket, processPath, processName, remote, isAllow ? NetworkConnectionVerdict.AllowSession : NetworkConnectionVerdict.BlockSession, "Решение текущей сессии");
                    continue;
                }

                // 4. Если для программы уже есть РАЗРЕШАЮЩЕЕ ИСХОДЯЩЕЕ правило — алерт не создаём.
                if (HasExistingFirewallRuleCached(processPath, processName, rulePaths))
                {
                    TryLogHistory(socket, processPath, processName, remote, NetworkConnectionVerdict.Allowed, "Разрешено правилом Windows Firewall");
                    continue;
                }

                // 5. Дедупликация: ключ включает локальный endpoint, поэтому новое TCP-соединение
                // (новый эфемерный порт) снова даст алерт даже после истечения TTL.
                if (_pendingById.Count >= MaxPendingAlerts)
                    continue;
                var seenKey = sessionKey + "|" +
                              (string.IsNullOrWhiteSpace(socket.Protocol) ? "TCP" : socket.Protocol) + "|" +
                              (socket.LocalAddress ?? string.Empty) + ":" + socket.LocalPort + "|" +
                              remote.ToLowerInvariant() + ":" + socket.RemotePort;
                if (!_seenKeys.TryAdd(seenKey, DateTime.UtcNow))
                    continue;

                // 4.5. Автономные решения нейросети: применяются без всплывающего окна
                if (_aiAutonomousNetworkDecisionsEnabled && !string.IsNullOrWhiteSpace(processPath))
                {
                    if (_autonomousPending.Count >= MaxPendingAlerts)
                        continue;
                    var generation = Interlocked.Increment(ref _autonomousGeneration);
                    if (!_autonomousPending.TryAdd(sessionKey, generation))
                        continue;

                    _ = ExecuteAutonomousDecisionAsync(
                        processName,
                        processPath,
                        remote,
                        socket.RemotePort,
                        socket.LocalAddress ?? string.Empty,
                        socket.LocalPort,
                        socket.Protocol ?? "TCP",
                        socket.ProcessId,
                        sessionKey,
                        generation);

                    continue;
                }

                var alert = new NetworkConnectionAlertDto
                {
                    ProcessId = socket.ProcessId,
                    ProcessName = processName,
                    ProcessPath = processPath ?? string.Empty,
                    CanAllowSession = !string.IsNullOrWhiteSpace(processPath) && (sessionAllowSupported ??= _sessionFirewall.CanAllowSession),
                    CanBlockSession = !string.IsNullOrWhiteSpace(processPath),
                    Protocol = string.IsNullOrWhiteSpace(socket.Protocol) ? "TCP" : socket.Protocol,
                    RemoteAddress = remote,
                    RemotePort = socket.RemotePort,
                    DetectedAt = DateTime.UtcNow,
                };

                if (_pendingById.TryAdd(alert.AlertId, alert))
                {
                    _queue.Enqueue(alert);
                    added++;

                    _ = EnrichAlertWithAiAsync(alert);

                    RecordHistory(new NetworkConnectionHistoryItem
                    {
                        Id = alert.AlertId,
                        Timestamp = alert.DetectedAt,
                        ProcessId = alert.ProcessId,
                        ProcessName = alert.ProcessName,
                        ProcessPath = alert.ProcessPath,
                        Protocol = alert.Protocol,
                        LocalAddress = socket.LocalAddress ?? string.Empty,
                        LocalPort = socket.LocalPort,
                        RemoteAddress = alert.RemoteAddress,
                        RemotePort = alert.RemotePort,
                        Verdict = NetworkConnectionVerdict.PendingAlert,
                        Direction = "Outbound",
                        Details = "Ожидает решения пользователя"
                    });
                }
            }

            PruneQueue();
            return added;
        }
    }

    /// <summary>Добавляет запись в кольцевой журнал истории сетевых подключений.</summary>
    public void RecordHistory(NetworkConnectionHistoryItem item)
    {
        if (item is null) return;
        _historyQueue.Enqueue(item);
        if (!string.IsNullOrEmpty(item.Id))
            _historyById[item.Id] = item;

        while (_historyQueue.Count > MaxHistoryItems && _historyQueue.TryDequeue(out var old))
        {
            if (!string.IsNullOrEmpty(old.Id))
                _historyById.TryRemove(old.Id, out _);
        }
    }

    /// <summary>Возвращает снимок журнала истории сетевых подключений (по убыванию времени).</summary>
    public List<NetworkConnectionHistoryItem> GetConnectionHistory()
    {
        return _historyQueue.OrderByDescending(h => h.Timestamp).ToList();
    }

    /// <summary>Очищает журнал истории сетевых подключений.</summary>
    public void ClearConnectionHistory()
    {
        _historyById.Clear();
        while (_historyQueue.TryDequeue(out _)) { }
    }

    /// <summary>Возвращает снимок ожидающих алертов (по времени обнаружения).</summary>
    public List<NetworkConnectionAlertDto> GetPendingAlerts()
    {
        return _pendingById.Values.OrderBy(a => a.DetectedAt).ToList();
    }

    private void TryLogHistory(ProcessNetworkSocket socket, string? processPath, string processName, string remote, NetworkConnectionVerdict verdict, string details)
    {
        try
        {
            var dedupKey = $"{processName}|{remote}:{socket.RemotePort}|{verdict}";
            var now = DateTime.UtcNow;
            if (_historyDedup.TryGetValue(dedupKey, out var lastTime) && (now - lastTime).TotalSeconds < 15)
                return;

            _historyDedup[dedupKey] = now;
            if (_historyDedup.Count > 1000)
                _historyDedup.Clear();

            RecordHistory(new NetworkConnectionHistoryItem
            {
                Timestamp = now,
                ProcessId = socket.ProcessId,
                ProcessName = processName,
                ProcessPath = processPath ?? string.Empty,
                Protocol = string.IsNullOrWhiteSpace(socket.Protocol) ? "TCP" : socket.Protocol,
                LocalAddress = socket.LocalAddress ?? string.Empty,
                LocalPort = socket.LocalPort,
                RemoteAddress = remote,
                RemotePort = socket.RemotePort,
                Verdict = verdict,
                Direction = "Outbound",
                Details = details
            });
        }
        catch { }
    }

    /// <summary>
    /// Сериализует решение со сканированием и подтверждает его только после успешного
    /// применения политики. Неудача оставляет исходный алерт и историю доступными для повтора.
    /// </summary>
    /// <param name="request">Идентификатор алерта и выбранное действие; идентичность берётся из алерта службы.</param>
    /// <returns>True только для применённого решения либо его идемпотентного повтора.</returns>
    public bool ResolveAlert(ResolveNetworkAlertRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AlertId) || !Enum.IsDefined(request.Resolution))
            return false;

        lock (_scanLock)
        {
            if (_disposed)
                return false;
            if (_completedResolutions.TryGetValue(request.AlertId, out var previous))
                return previous == request.Resolution;
            if (!_pendingById.TryGetValue(request.AlertId, out var alert))
                return false;

            // UI не может подменить приложение или порт, к которым относится выданный службой AlertId.
            var path = (alert.ProcessPath ?? string.Empty).Trim();
            var name = (alert.ProcessName ?? string.Empty).Trim();
            var key = BuildSessionKey(path, name);
            _autonomousPending.TryRemove(key, out _);
            if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name))
                return false;
            if (request.Resolution is NetworkAlertResolution.AllowAlways or NetworkAlertResolution.BlockAlways
                or NetworkAlertResolution.WhitelistProgram or NetworkAlertResolution.BlacklistProgram
                or NetworkAlertResolution.AllowSession or NetworkAlertResolution.BlockSession
                && string.IsNullOrWhiteSpace(path))
                return false;
            if (request.Resolution == NetworkAlertResolution.CreateRule)
                return false; // Запрос без описания детального правила не подтверждает его создание.
            if (request.Resolution == NetworkAlertResolution.AllowSession && !_sessionFirewall.CanAllowSession)
            {
                alert.CanAllowSession = false;
                return false;
            }
            if (request.Resolution == NetworkAlertResolution.AllowPort && alert.RemotePort is < 1 or > 65535)
                return false;

            try
            {
                switch (request.Resolution)
                {
                    case NetworkAlertResolution.AllowPort:
                        if (!ApplyRule(new FirewallRuleDto
                        {
                            Name = $"StartupMonitor_Allow_RemotePort_{alert.RemotePort}_Auto",
                            Description = $"Удалённый TCP-порт разрешён пользователем через сетевое уведомление: {alert.RemotePort}",
                            Direction = FirewallDirection.Outbound,
                            Action = FirewallAction.Allow,
                            Protocol = FirewallProtocol.Tcp,
                            RemotePorts = alert.RemotePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Enabled = true,
                            Profiles = FirewallProfile.All,
                            Grouping = WindowsFirewallService.RuleGroup,
                            IsManaged = true
                        }))
                            return false;
                        break;
                    case NetworkAlertResolution.AllowSession:
                    case NetworkAlertResolution.BlockSession:
                        var temporary = _sessionFirewall.Apply(path,
                            request.Resolution == NetworkAlertResolution.AllowSession ? FirewallAction.Allow : FirewallAction.Block,
                            SessionDecisionTtl);
                        if (!temporary.Success)
                        {
                            Log.Warning("NetworkAlerts: временное решение {Resolution} отклонено: {Message}",
                                request.Resolution, temporary.Message);
                            return false;
                        }
                        break;
                    case NetworkAlertResolution.WhitelistProgram:
                    case NetworkAlertResolution.AllowAlways:
                        if (!ApplyRule(BuildAppRule(path, name, FirewallAction.Allow)))
                            return false;
                        break;
                    case NetworkAlertResolution.BlacklistProgram:
                    case NetworkAlertResolution.BlockAlways:
                        if (!ApplyRule(BuildAppRule(path, name, FirewallAction.Block)))
                            return false;
                        break;
                }

                if (_store != null && request.Resolution is NetworkAlertResolution.WhitelistProgram
                    or NetworkAlertResolution.BlacklistProgram or NetworkAlertResolution.AllowPort)
                {
                    var cfg = _store.Load();
                    if (request.Resolution == NetworkAlertResolution.AllowPort)
                    {
                        if (!cfg.Settings.FirewallAllowedPorts.Contains(alert.RemotePort))
                            cfg.Settings.FirewallAllowedPorts.Add(alert.RemotePort);
                    }
                    else
                    {
                        var allowed = request.Resolution == NetworkAlertResolution.WhitelistProgram;
                        var target = allowed ? cfg.Settings.FirewallWhitelistedApps : cfg.Settings.FirewallBlacklistedApps;
                        var opposite = allowed ? cfg.Settings.FirewallBlacklistedApps : cfg.Settings.FirewallWhitelistedApps;
                        opposite.RemoveAll(app => string.Equals(app, path, StringComparison.OrdinalIgnoreCase));
                        if (!target.Contains(path, StringComparer.OrdinalIgnoreCase))
                            target.Add(path);
                    }
                    _store.Save(cfg);
                }
                if (request.Resolution is NetworkAlertResolution.AllowAlways or NetworkAlertResolution.BlockAlways
                    or NetworkAlertResolution.WhitelistProgram or NetworkAlertResolution.BlacklistProgram)
                    _sessionFirewall.Remove(path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NetworkAlerts: решение {Resolution} для алерта {AlertId} не применено",
                    request.Resolution, request.AlertId);
                return false;
            }

            // Область подавления совпадает с областью действия решения: точный путь либо один порт.
            if (request.Resolution == NetworkAlertResolution.AllowPort)
                _allowedPorts.TryAdd(alert.RemotePort, 0);
            else
                SetSessionDecision(key, request.Resolution);

            if (request.Resolution == NetworkAlertResolution.WhitelistProgram)
            {
                _blacklistedApps.TryRemove(path, out _);
                _whitelistedApps.TryAdd(path, 0);
            }
            else if (request.Resolution == NetworkAlertResolution.BlacklistProgram)
            {
                _whitelistedApps.TryRemove(path, out _);
                _blacklistedApps.TryAdd(path, 0);
            }

            var affected = _pendingById.Values.Where(candidate =>
                request.Resolution == NetworkAlertResolution.AllowPort
                    ? candidate.RemotePort == alert.RemotePort
                    : string.Equals(BuildSessionKey(candidate.ProcessPath, candidate.ProcessName), key,
                        StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var candidate in affected)
            {
                if (_historyById.TryGetValue(candidate.AlertId, out var item))
                    item.Verdict = ResolutionVerdict(request.Resolution);
                _pendingById.TryRemove(candidate.AlertId, out _);
                RememberResolution(candidate.AlertId, request.Resolution);
            }
            PruneQueue(force: true);
            return true;
        }
    }

    /// <summary>Переводит подтверждённое действие в вердикт журнала без изменения исходных событий.</summary>
    /// <param name="resolution">Успешно применённое действие.</param>
    /// <returns>Вердикт, соответствующий области действия политики.</returns>
    private static NetworkConnectionVerdict ResolutionVerdict(NetworkAlertResolution resolution) => resolution switch
    {
        NetworkAlertResolution.AllowSession or NetworkAlertResolution.AllowPort => NetworkConnectionVerdict.AllowSession,
        NetworkAlertResolution.BlockSession => NetworkConnectionVerdict.BlockSession,
        NetworkAlertResolution.WhitelistProgram => NetworkConnectionVerdict.Whitelisted,
        NetworkAlertResolution.BlacklistProgram => NetworkConnectionVerdict.Blacklisted,
        _ => NetworkConnectionVerdict.RuleCreated
    };

    /// <summary>Хранит ограниченное число ACK для безопасного повтора после потери IPC-ответа.</summary>
    /// <param name="alertId">Идентификатор подтверждённого алерта.</param>
    /// <param name="resolution">Действие, которое действительно было применено.</param>
    private void RememberResolution(string alertId, NetworkAlertResolution resolution)
    {
        _completedResolutions[alertId] = resolution;
        _completedResolutionOrder.Enqueue(alertId);
        while (_completedResolutionOrder.Count > MaxPendingAlerts && _completedResolutionOrder.TryDequeue(out var expired))
            _completedResolutions.Remove(expired);
    }

    /// <summary>Применяет сессионный доступ для матрицы и автономного ИИ через того же владельца WFP.</summary>
    /// <param name="path">Полный путь приложения; имя без пути не является безопасной областью правила.</param>
    /// <param name="name">Имя для диагностики и формирования совместимого ключа.</param>
    /// <param name="resolution">Только AllowSession или BlockSession.</param>
    /// <param name="lifetime">Явный TTL либо прежний предел сеанса в восемь часов.</param>
    /// <returns>Подтверждение установки временной политики без постоянных COM-правил.</returns>
    public FirewallOperationResult ApplySessionAccess(string path, string name, NetworkAlertResolution resolution, TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            resolution is not (NetworkAlertResolution.AllowSession or NetworkAlertResolution.BlockSession))
            return FirewallOperationResult.Fail("Для временного правила нужен полный путь и сессионное действие.");

        lock (_scanLock)
        {
            if (_disposed)
                return FirewallOperationResult.Fail("Детектор сетевых подключений остановлен.");
            RefreshSettingsCache();
            if (resolution == NetworkAlertResolution.AllowSession && !_sessionFirewall.CanAllowSession)
                return FirewallOperationResult.Fail("Временное разрешение недоступно в текущем режиме исходящей фильтрации.");
            if (resolution == NetworkAlertResolution.AllowSession && IsBlacklistedApp(path, name))
                return FirewallOperationResult.Fail("Постоянный чёрный список запрещает временное разрешение. Сначала измените постоянную политику.");

            var duration = lifetime ?? SessionDecisionTtl;
            if (duration <= TimeSpan.Zero || duration > SessionDecisionTtl)
                return FirewallOperationResult.Fail("Срок временного правила должен быть больше нуля и не превышать восемь часов.");
            var result = _sessionFirewall.Apply(path.Trim(), resolution == NetworkAlertResolution.AllowSession ? FirewallAction.Allow : FirewallAction.Block, duration);
            if (!result.Success)
                return result;

            var key = BuildSessionKey(path, name);
            _autonomousPending.TryRemove(key, out _);
            SetSessionDecision(key, resolution);
            _sessionDecisionExpiry[key] = DateTime.UtcNow + duration;
            foreach (var pending in _pendingById.Values.Where(pending =>
                string.Equals(BuildSessionKey(pending.ProcessPath, pending.ProcessName), key, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _pendingById.TryRemove(pending.AlertId, out _);
                if (_historyById.TryGetValue(pending.AlertId, out var history))
                    history.Verdict = ResolutionVerdict(resolution);
                RememberResolution(pending.AlertId, resolution);
            }
            PruneQueue(force: true);
            return result;
        }
    }

    /// <summary>Возвращает приложение к запросам, снимая только принадлежащую службе временную политику.</summary>
    /// <param name="path">Полный путь приложения.</param>
    /// <param name="name">Имя приложения для совместимого ключа при отсутствии пути.</param>
    public void ClearSessionAccess(string path, string name)
    {
        lock (_scanLock)
        {
            _sessionFirewall.Remove(path);
            _cachedRulePaths = null;
            var key = BuildSessionKey(path, name);
            _autonomousPending.TryRemove(key, out _);
            _sessionDecisions.TryRemove(key, out _);
            _sessionDecisionExpiry.TryRemove(key, out _);
            foreach (var seen in _seenKeys.Keys.Where(seen => seen.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase)))
                _seenKeys.TryRemove(seen, out _);
        }
    }

    // ── Вспомогательная логика ──

    /// <summary>Проверяет, находится ли программа в постоянном белом списке (любые соединения разрешены).</summary>
    public bool IsWhitelistedApp(string? processPath, string? processName)
    {
        // Абсолютный приоритет: если приложение есть в чёрном списке — белый список игнорируется
        if (IsBlacklistedApp(processPath, processName))
            return false;

        if (!string.IsNullOrWhiteSpace(processPath) && _whitelistedApps.ContainsKey(processPath.Trim()))
            return true;
        if (!string.IsNullOrWhiteSpace(processName) && _whitelistedApps.ContainsKey(processName.Trim()))
            return true;
        return false;
    }

    /// <summary>Проверяет, находится ли программа в постоянном чёрном списке (любые соединения запрещены).</summary>
    public bool IsBlacklistedApp(string? processPath, string? processName)
    {
        if (!string.IsNullOrWhiteSpace(processPath) && _blacklistedApps.ContainsKey(processPath.Trim()))
            return true;
        if (!string.IsNullOrWhiteSpace(processName) && _blacklistedApps.ContainsKey(processName.Trim()))
            return true;
        return false;
    }

    /// <summary>Проверяет, разрешен ли порт пользователем для подавления повторных всплывающих алертов.</summary>
    public bool IsPortAllowed(int port) => port > 0 && _allowedPorts.ContainsKey(port);

    /// <summary>
    /// Возвращает true для сокета, который действительно отражает исходящее соединение:
    /// TCP в состоянии ESTABLISHED или SYN_SENT. UDP в этом трекере не несёт удалённую
    /// конечную точку (RemotePort = 0), LISTENING/TIME_WAIT/CLOSE_WAIT алертов не создают.
    /// </summary>
    private static bool IsAlertableSocket(ProcessNetworkSocket socket)
    {
        var protocol = socket.Protocol ?? string.Empty;
        if (!protocol.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            return false;

        var state = socket.State ?? string.Empty;
        // Пустое состояние трактуем как установленное соединение (совместимость с тестовыми сокетами).
        if (string.IsNullOrWhiteSpace(state))
            return true;

        return state.Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase)
            || state.Equals("SYN_SENT", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Удаляет устаревшие записи дедупликации (не чаще раза в минуту).</summary>
    private void PruneSeenKeys()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSeenPrune < TimeSpan.FromMinutes(1))
            return;

        _lastSeenPrune = now;
        foreach (var kv in _seenKeys)
        {
            if (now - kv.Value > SeenKeyTtl)
                _seenKeys.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>
    /// Фиксирует решение по программе. Бессрочные решения (правило/списки) не истекают,
    /// сессионные — действуют ограниченное время <see cref="SessionDecisionTtl"/>.
    /// </summary>
    private void SetSessionDecision(string key, NetworkAlertResolution resolution)
    {
        _sessionDecisions[key] = resolution;

        var ttl = resolution is NetworkAlertResolution.AllowSession
            or NetworkAlertResolution.BlockSession
            or NetworkAlertResolution.CreateRule
            ? SessionDecisionTtl
            : TimeSpan.MaxValue;

        _sessionDecisionExpiry[key] = ttl == TimeSpan.MaxValue
            ? DateTime.MaxValue
            : DateTime.UtcNow + ttl;
    }

    /// <summary>
    /// Проверяет активное (не истёкшее) решение по ключу. Истёкшее решение удаляется,
    /// чтобы программа снова могла порождать алерты.
    /// </summary>
    private bool HasActiveSessionDecision(string key)
    {
        if (!_sessionDecisions.ContainsKey(key))
            return false;

        if (_sessionDecisions.TryGetValue(key, out var resolution) &&
            resolution is NetworkAlertResolution.AllowSession or NetworkAlertResolution.BlockSession &&
            key.StartsWith("path:", StringComparison.OrdinalIgnoreCase) && !_sessionFirewall.IsActive(key[5..]))
        {
            _sessionDecisions.TryRemove(key, out _);
            _sessionDecisionExpiry.TryRemove(key, out _);
            return false;
        }

        if (_sessionDecisionExpiry.TryGetValue(key, out var expiresAt) && expiresAt <= DateTime.UtcNow)
        {
            _sessionDecisions.TryRemove(key, out _);
            _sessionDecisionExpiry.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Синхронизирует белый/чёрный списки, разрешённые порты и флаг PID=0 с хранилищем.
    /// Вызывается только при изменении ревизии конфига (дешёвый <see cref="JsonDataStore.GetSettings"/>),
    /// поэтому правки, сделанные через вкладку Firewall, сразу видны детектору.
    /// </summary>
    private void RefreshSettingsCache()
    {
        if (_store == null)
            return;

        var revision = _store.Revision;
        if (revision == _settingsRevision)
            return;

        lock (_settingsLock)
        {
            if (revision == _settingsRevision)
                return;

            try
            {
                var settings = _store.GetSettings();
                _silentPid0 = settings.FirewallSilentPid0;
                _aiAutonomousNetworkDecisionsEnabled = settings.AiAutonomousNetworkDecisionsEnabled;
                _aiAutonomousDecisionMode = string.IsNullOrWhiteSpace(settings.AiAutonomousDecisionMode) ? "AllowSession" : settings.AiAutonomousDecisionMode.Trim();

                // Чёрный список имеет абсолютный приоритет над белым.
                var black = new HashSet<string>(
                    settings.FirewallBlacklistedApps.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                var white = new HashSet<string>(
                    settings.FirewallWhitelistedApps.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                white.ExceptWith(black);

                _blacklistedApps.Clear();
                foreach (var a in black) _blacklistedApps.TryAdd(a, 0);
                _whitelistedApps.Clear();
                foreach (var a in white) _whitelistedApps.TryAdd(a, 0);

                _allowedPorts.Clear();
                foreach (var p in settings.FirewallAllowedPorts)
                {
                    if (p > 0) _allowedPorts.TryAdd(p, 0);
                }

                _settingsRevision = revision;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NetworkAlerts: не удалось обновить кэш настроек из хранилища");
            }
        }
    }

    /// <summary>Проверяет, является ли процесс доверенным системным (алерты не создаются никогда).</summary>
    public static bool IsTrustedProcess(string? processName, string? processPath)
    {
        string? fileName = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(processPath))
                fileName = Path.GetFileName(processPath);
        }
        catch { }

        foreach (var candidate in new[] { processName, fileName })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            var n = candidate.Trim();
            if (TrustedSystemProcesses.Contains(n))
                return true;
            var withoutExt = n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? n[..^4]
                : n;
            if (TrustedSystemProcesses.Contains(withoutExt) ||
                TrustedSystemProcesses.Contains(withoutExt + ".exe"))
                return true;
        }

        return false;
    }

    /// <summary>Строит ключ сессии/дедупликации: путь программы (или имя, если пути нет).</summary>
    public static string BuildSessionKey(string? processPath, string? processName)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
            return "path:" + processPath.Trim().ToLowerInvariant();
        return "name:" + (processName ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>Строит сессионный ключ исключительно по имени исполняемого файла для надежного матчинга браузеров.</summary>
    public static string BuildNameSessionKey(string? processName)
    {
        return "name:" + (processName ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>Получает актуальный путь одного PID; завершение процесса оставляет явно отсутствующий путь.</summary>
    /// <param name="pid">Идентификатор процесса из текущего снимка сокетов.</param>
    /// <param name="processName">Имя процесса для диагностики без раскрытия командной строки.</param>
    /// <returns>Полный путь либо null при недоступном процессе.</returns>
    private string? ResolveProcessPath(int pid, string processName)
    {
        if (ProcessPathResolver != null)
        {
            try { return ProcessPathResolver(pid); }
            catch (Exception ex) { Log.Debug(ex, "NetworkAlerts: источник пути PID {Pid} недоступен", pid); return null; }
        }

        if (pid <= 0)
            return null;
        try
        {
            // Быстрый путь через QueryFullProcessImageName (без тяжёлого MainModule).
            var fastPath = ProcessMetadataEnricher.TryGetProcessPath(pid);
            return string.IsNullOrWhiteSpace(fastPath) ? null : fastPath;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "NetworkAlerts: путь процесса {Process} PID {Pid} недоступен", processName, pid);
            return null;
        }
    }

    /// <summary>
    /// Строит кеш нормализованных полных путей программ, у которых есть разрешающее
    /// ИСХОДЯЩЕЕ правило Firewall (TTL 60 секунд). Обеспечивает 0% загрузку CPU в фоне.
    /// </summary>
    private HashSet<string>? BuildFirewallRulePathSet()
    {
        if (HasFirewallRuleOverride != null)
            return null;

        var now = DateTime.UtcNow;
        if (_cachedRulePaths != null && (now - _cachedRulePathsTime) < _ruleCacheTtl)
            return _cachedRulePaths;

        try
        {
            // Читаем только разрешающие исходящие правила: входящие/блокирующие не подавляют алерт.
            var set = _firewall.GetOutboundAllowApplicationRulePaths();
            _cachedRulePaths = set;
            _cachedRulePathsTime = now;
            return set;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: не удалось проверить правила Firewall");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// O(1)-проверка наличия разрешающего исходящего правила Firewall по пред построенному кешу.
    /// Сопоставление выполняется ТОЛЬКО по полному нормализованному пути.
    /// </summary>
    private bool HasExistingFirewallRuleCached(string? processPath, string? processName, HashSet<string>? rulePaths)
    {
        if (HasFirewallRuleOverride != null)
        {
            try { return HasFirewallRuleOverride(processPath ?? string.Empty); }
            catch { return false; }
        }

        if (rulePaths is null)
            return HasExistingFirewallRule(processPath, processName);

        // Матчинг по имени файла/имени процесса сознательно убран: правило другого файла
        // с тем же именем (например browser.exe из другого каталога) или только входящее
        // правило не должны подавлять алерт об исходящем соединении.
        var normalizedPath = NormalizeProcessPath(processPath);
        return normalizedPath != null && rulePaths.Contains(normalizedPath);
    }

    /// <summary>
    /// Нормализует путь процесса к виду, в котором хранятся пути правил Firewall:
    /// снимает кавычки, приводит слэши к '\' и регистр к нижнему.
    /// </summary>
    private static string? NormalizeProcessPath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return null;

        var path = processPath.Trim().Trim('"');
        try { path = Environment.ExpandEnvironmentVariables(path); } catch { }
        path = path.Replace('/', '\\').TrimEnd('\\');
        return path.Length == 0 ? null : path.ToLowerInvariant();
    }

    private bool HasExistingFirewallRule(string? processPath, string? processName)
    {
        if (HasFirewallRuleOverride != null)
        {
            try { return HasFirewallRuleOverride(processPath ?? string.Empty); }
            catch { return false; }
        }

        if (string.IsNullOrWhiteSpace(processPath) && string.IsNullOrWhiteSpace(processName))
            return false;

        var set = BuildFirewallRulePathSet();
        return HasExistingFirewallRuleCached(processPath, processName, set);
    }

    /// <summary>Проверяет результат записи политики; отказ COM или тестового адаптера не превращается в ACK.</summary>
    /// <param name="rule">Полное правило приложения.</param>
    /// <returns>True после подтверждённой записи правила.</returns>
    private bool ApplyRule(FirewallRuleDto rule)
    {
        try
        {
            // Сбрасываем кеш путей правил брандмауэра для немедленной актуализации
            _cachedRulePaths = null;

            if (ApplyFirewallRuleOverride != null)
            {
                ApplyFirewallRuleOverride(rule);
                return true;
            }
            else
            {
                var result = _firewall.CreateOrUpdateRule(rule);
                if (!result.Success)
                    Log.Warning("NetworkAlerts: правило {Rule} не применено: {Message}", rule.Name, result.Message);
                if (!result.Success || string.IsNullOrWhiteSpace(rule.ApplicationPath))
                    return result.Success;

                // Новое разрешение не оставляет старый собственный запрет для того же файла.
                // Чужие правила и программы с таким же именем в другом каталоге не затрагиваются.
                if (!_firewall.GetStatus().Available)
                    return false;
                foreach (var opposite in _firewall.GetAllRules().Where(candidate =>
                    IsManagedApplicationRule(candidate, rule.ApplicationPath) && candidate.Action != rule.Action))
                {
                    var removed = _firewall.DeleteRule(opposite.Name, opposite.Direction);
                    if (!removed.Success)
                    {
                        Log.Warning("NetworkAlerts: конфликтующее правило {Rule} не удалено: {Message}", opposite.Name, removed.Message);
                        return false;
                    }
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: не удалось создать правило {Rule}", rule.Name);
            return false;
        }
    }

    /// <summary>Ограничивает очистку правил собственной политикой одного полного пути.</summary>
    /// <param name="rule">Правило, которое может отсутствовать в повреждённой коллекции.</param>
    /// <param name="path">Авторитетный путь приложения.</param>
    /// <returns>True только для собственного правила того же исполняемого файла.</returns>
    internal static bool IsManagedApplicationRule(FirewallRuleDto? rule, string path)
    {
        var target = NormalizeProcessPath(path);
        return rule is { IsManaged: true } && target != null &&
            string.Equals(NormalizeProcessPath(rule.ApplicationPath), target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Создаёт постоянное правило точного приложения с устойчивым хешем пути в имени.</summary>
    /// <param name="processPath">Полный путь, определяющий идентичность правила.</param>
    /// <param name="processName">Имя для описания пользовательского выбора.</param>
    /// <param name="action">Подтверждаемое разрешение либо запрет.</param>
    /// <returns>Правило приложения для обоих направлений и всех профилей.</returns>
    private static FirewallRuleDto BuildAppRule(string processPath, string processName, FirewallAction action)
    {
        var baseName = "app";
        try
        {
            var fn = Path.GetFileNameWithoutExtension(processPath);
            if (!string.IsNullOrWhiteSpace(fn))
                baseName = fn;
        }
        catch { }

        var verb = action == FirewallAction.Allow ? "Allow" : "Block";
        return new FirewallRuleDto
        {
            Name = $"StartupMonitor_{verb}_{SanitizeRuleName(baseName)}_{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(NormalizeProcessPath(processPath) ?? processPath)))[..16]}_Auto",
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

    private static string SanitizeRuleName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var clean = new string(chars).Trim('_');
        return string.IsNullOrEmpty(clean) ? "app" : clean;
    }

    private void PruneQueue(bool force = false)
    {
        if (!force && _queue.Count <= _pendingById.Count + 128)
            return;
        while (_queue.TryDequeue(out _)) { }
        if (force || _pendingById.Count > 0)
        {
            foreach (var alert in _pendingById.Values.OrderBy(a => a.DetectedAt))
                _queue.Enqueue(alert);
        }
    }

    /// <summary>
    /// Выполняет автономное решение ИИ в фоновом потоке, запрашивая вердикт у модели
    /// и фиксируя результат в журнале истории и логе службы без всплывающего окна.
    /// </summary>
    private async Task ExecuteAutonomousDecisionAsync(
        string processName,
        string? processPath,
        string remoteAddress,
        int remotePort,
        string localAddress,
        int localPort,
        string protocol,
        int processId,
        string sessionKey,
        long generation)
    {
        try
        {
            (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)? verdict = null;

            if (AiEvaluatorOverride != null)
            {
                try
                {
                    verdict = await AiEvaluatorOverride(processName, processPath ?? string.Empty, remoteAddress, remotePort).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NetworkAlerts: сбой в AiEvaluatorOverride для {Process}", processName);
                }
            }
            else if (_aiRating != null)
            {
                try
                {
                    verdict = await _aiRating.EvaluateNetworkProcessAsync(processName, processPath ?? string.Empty, remoteAddress, remotePort).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NetworkAlerts: сбой EvaluateNetworkProcessAsync для {Process}", processName);
                }
            }

            // Выбираем финальное решение с учётом настроенного режима и рекомендации ИИ
            var resolution = string.Equals(_aiAutonomousDecisionMode, "BlockSession", StringComparison.OrdinalIgnoreCase)
                ? NetworkAlertResolution.BlockSession
                : NetworkAlertResolution.AllowSession;

            if (verdict.HasValue && !string.IsNullOrWhiteSpace(verdict.Value.RecommendedAction))
            {
                if (string.Equals(verdict.Value.RecommendedAction, "BlockSession", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(verdict.Value.RecommendedAction, "BlockAlways", StringComparison.OrdinalIgnoreCase))
                {
                    resolution = NetworkAlertResolution.BlockSession;
                }
                else if (string.Equals(verdict.Value.RecommendedAction, "AllowSession", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(verdict.Value.RecommendedAction, "AllowAlways", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(_aiAutonomousDecisionMode, "BlockSession", StringComparison.OrdinalIgnoreCase))
                    {
                        resolution = NetworkAlertResolution.AllowSession;
                    }
                }
            }

            lock (_scanLock)
            {
                // Ручное решение или остановка службы отменяют право запоздалой AI-оценки менять политику.
                if (_disposed || !_autonomousPending.TryGetValue(sessionKey, out var activeGeneration) || activeGeneration != generation)
                    return;
                var result = ApplySessionAccess(processPath ?? string.Empty, processName, resolution);
                if (!result.Success)
                {
                    Log.Warning("NetworkAlerts: автономное решение {Resolution} не применено: {Message}", resolution, result.Message);
                    QueueAutonomousFailure(processId, processPath, processName, protocol, remoteAddress, remotePort, localAddress, localPort, result.Message);
                    return;
                }
            }

            Log.Information(
                "NetworkAlerts (Автономно ИИ): Процесс {Process} ({Path}) -> {Remote}:{Port}. Решение: {Resolution}. Назначение: {Purpose}. Риски: {Risk}. Рекомендация: {Rec}",
                processName, processPath, remoteAddress, remotePort, resolution,
                verdict?.Purpose ?? "Назначение не определено",
                verdict?.BlockingRisk ?? "Риски не определены",
                verdict?.Recommendation ?? "Автономное действие");

            var purposeDesc = verdict?.Purpose ?? "Назначение не определено";
            var recDesc = verdict?.Recommendation ?? "Решение применено автоматически";

            RecordHistory(new NetworkConnectionHistoryItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                ProcessId = processId,
                ProcessName = processName,
                ProcessPath = processPath ?? string.Empty,
                Protocol = protocol,
                LocalAddress = localAddress,
                LocalPort = localPort,
                RemoteAddress = remoteAddress,
                RemotePort = remotePort,
                Verdict = resolution == NetworkAlertResolution.AllowSession ? NetworkConnectionVerdict.AllowSession : NetworkConnectionVerdict.BlockSession,
                Direction = "Outbound",
                Details = $"Автономное решение ИИ ({resolution}): {purposeDesc}. {recDesc}"
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: сбой выполнения автономного решения для {Process}", processName);
            lock (_scanLock)
            {
                if (!_disposed && _autonomousPending.TryGetValue(sessionKey, out var activeGeneration) && activeGeneration == generation)
                    QueueAutonomousFailure(processId, processPath, processName, protocol, remoteAddress, remotePort, localAddress, localPort, ex.Message);
            }
        }
        finally
        {
            ((ICollection<KeyValuePair<string, long>>)_autonomousPending).Remove(new(sessionKey, generation));
        }
    }

    /// <summary>Возвращает отказ автономной политики в обычную очередь вместо ложной записи об успехе.</summary>
    /// <param name="processId">PID исходного наблюдения.</param>
    /// <param name="path">Полный путь приложения.</param>
    /// <param name="name">Имя процесса.</param>
    /// <param name="protocol">Протокол наблюдения.</param>
    /// <param name="remote">Удалённый адрес.</param>
    /// <param name="remotePort">Удалённый TCP-порт.</param>
    /// <param name="local">Локальный адрес.</param>
    /// <param name="localPort">Локальный порт.</param>
    /// <param name="failure">Причина отказа для журнала службы.</param>
    private void QueueAutonomousFailure(int processId, string? path, string name, string protocol, string remote, int remotePort, string local, int localPort, string failure)
    {
        if (_pendingById.Count >= MaxPendingAlerts)
            return;
        var alert = new NetworkConnectionAlertDto
        {
            ProcessId = processId, ProcessPath = path ?? string.Empty, ProcessName = name,
            Protocol = protocol, RemoteAddress = remote, RemotePort = remotePort,
            CanAllowSession = !string.IsNullOrWhiteSpace(path) && _sessionFirewall.CanAllowSession,
            CanBlockSession = !string.IsNullOrWhiteSpace(path)
        };
        if (_pendingById.TryAdd(alert.AlertId, alert))
        {
            _queue.Enqueue(alert);
            RecordHistory(new NetworkConnectionHistoryItem
            {
                Id = alert.AlertId, Timestamp = alert.DetectedAt, ProcessId = processId,
                ProcessPath = path ?? string.Empty, ProcessName = name, Protocol = protocol,
                RemoteAddress = remote, RemotePort = remotePort, LocalAddress = local, LocalPort = localPort,
                Verdict = NetworkConnectionVerdict.PendingAlert, Direction = "Outbound",
                Details = $"Автономное решение не применено: {failure}. Ожидается выбор пользователя."
            });
        }
    }

    /// <summary>
    /// Фоновое обогащение ожидающего алерта оценкой нейросети.
    /// </summary>
    private async Task EnrichAlertWithAiAsync(NetworkConnectionAlertDto alert)
    {
        try
        {
            (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)? verdict = null;

            if (AiEvaluatorOverride != null)
            {
                verdict = await AiEvaluatorOverride(alert.ProcessName, alert.ProcessPath, alert.RemoteAddress, alert.RemotePort).ConfigureAwait(false);
            }
            else if (_aiRating != null)
            {
                verdict = await _aiRating.EvaluateNetworkProcessAsync(alert.ProcessName, alert.ProcessPath, alert.RemoteAddress, alert.RemotePort).ConfigureAwait(false);
            }

            if (verdict != null)
            {
                alert.AiPurpose = verdict.Value.Purpose;
                alert.AiBlockingRisk = verdict.Value.BlockingRisk;
                alert.AiRecommendation = verdict.Value.Recommendation;
                alert.AiRecommendedAction = verdict.Value.RecommendedAction;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: сбой фонового обогащения алерта {AlertId}", alert.AlertId);
        }
    }

    /// <summary>
    /// Запрашивает оценку нейросети для сетевого алерта (вызывается через IPC PipeServer при открытии диалогового окна).
    /// </summary>
    public async Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?> EvaluateAlertAsync(
        string alertId, string processName, string processPath, string remoteAddress, int remotePort)
    {
        (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)? verdict = null;

        if (AiEvaluatorOverride != null)
        {
            verdict = await AiEvaluatorOverride(processName, processPath, remoteAddress, remotePort).ConfigureAwait(false);
        }
        else if (_aiRating != null)
        {
            verdict = await _aiRating.EvaluateNetworkProcessAsync(processName, processPath, remoteAddress, remotePort).ConfigureAwait(false);
        }

        if (verdict != null && !string.IsNullOrEmpty(alertId) && _pendingById.TryGetValue(alertId, out var alert))
        {
            alert.AiPurpose = verdict.Value.Purpose;
            alert.AiBlockingRisk = verdict.Value.BlockingRisk;
            alert.AiRecommendation = verdict.Value.Recommendation;
            alert.AiRecommendedAction = verdict.Value.RecommendedAction;
        }

        return verdict;
    }
}
