using System.Collections.Concurrent;
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

    // ── Состояние ──

    /// <summary>Известные решения текущей сессии: ключ сессии → решение.</summary>
    private readonly ConcurrentDictionary<string, NetworkAlertResolution> _sessionDecisions =
        new(StringComparer.OrdinalIgnoreCase);

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
    public IReadOnlyDictionary<string, NetworkAlertResolution> SessionDecisions => _sessionDecisions;

    /// <summary>
    /// Инициализирует детектор новых сетевых подключений.
    /// </summary>
    public NetworkConnectionAlertService(
        ProcessNetworkTracker tracker,
        WindowsFirewallService firewall,
        JsonDataStore? store = null)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _store = store;

        if (_store != null)
        {
            try
            {
                var cfg = _store.Load();
                _silentPid0 = cfg.Settings.FirewallSilentPid0;
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

        try { cts.Cancel(); } catch (ObjectDisposedException) { }

        // CTS нельзя освобождать сразу: цикл может ещё наблюдать токен.
        // Освобождаем его только после фактического завершения задачи.
        if (loop != null)
        {
            _ = loop.ContinueWith(_ =>
            {
                try { cts.Dispose(); } catch (ObjectDisposedException) { }
            }, TaskScheduler.Default);
        }
        else
        {
            try { cts.Dispose(); } catch (ObjectDisposedException) { }
        }
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

    /// <summary>Освобождает таймер фонового опроса.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopBackgroundScan();
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
                if (IsPortAllowed(socket.RemotePort) || IsPortAllowed(socket.LocalPort))
                    continue;

                // 1. Игнорируем LAN / Loopback / unspecified.
                if (socket.IsRemotePrivate || ProcessNetworkSocket.CheckIsPrivateAddress(remote))
                    continue;

                var processName = (socket.ProcessName ?? string.Empty).Trim();
                var processPath = ResolveProcessPath(socket.ProcessId, processName);

                var sessionKey = BuildSessionKey(processPath, processName);
                var nameKey = BuildNameSessionKey(processName);

                // 1.1 Абсолютный приоритет: Пропускаем программы из чёрного списка (соединения заблокированы, алерты подавлены)
                if (IsBlacklistedApp(processPath, processName))
                {
                    TryLogHistory(socket, processPath, processName, remote, NetworkConnectionVerdict.Blacklisted, "Заблокировано правилом чёрного списка");
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
                if (HasActiveSessionDecision(sessionKey) ||
                    (!string.IsNullOrWhiteSpace(processName) && HasActiveSessionDecision(nameKey)))
                {
                    var isAllow = (_sessionDecisions.TryGetValue(sessionKey, out var d1) && d1 == NetworkAlertResolution.AllowSession)
                               || (_sessionDecisions.TryGetValue(nameKey, out var d2) && d2 == NetworkAlertResolution.AllowSession);
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

                var alert = new NetworkConnectionAlertDto
                {
                    ProcessId = socket.ProcessId,
                    ProcessName = processName,
                    ProcessPath = processPath ?? string.Empty,
                    Protocol = string.IsNullOrWhiteSpace(socket.Protocol) ? "TCP" : socket.Protocol,
                    RemoteAddress = remote,
                    RemotePort = socket.RemotePort,
                    DetectedAt = DateTime.UtcNow,
                };

                if (_pendingById.TryAdd(alert.AlertId, alert))
                {
                    _queue.Enqueue(alert);
                    added++;

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
    /// Применяет решение пользователя по алерту с поддержкой 5 основных действий,
    /// двойного ключа сопоставления и мгновенной очистки накопившихся очередей.
    /// </summary>
    /// <returns>True — алерт найден и решение применено.</returns>
    public bool ResolveAlert(ResolveNetworkAlertRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AlertId))
            return false;

        if (!_pendingById.TryRemove(request.AlertId, out var alert))
            return false;

        if (_historyById.TryGetValue(request.AlertId, out var hItem))
        {
            hItem.Verdict = request.Resolution switch
            {
                NetworkAlertResolution.AllowSession => NetworkConnectionVerdict.AllowSession,
                NetworkAlertResolution.BlockSession => NetworkConnectionVerdict.BlockSession,
                NetworkAlertResolution.AllowAlways => NetworkConnectionVerdict.RuleCreated,
                NetworkAlertResolution.BlockAlways => NetworkConnectionVerdict.RuleCreated,
                NetworkAlertResolution.CreateRule => NetworkConnectionVerdict.RuleCreated,
                NetworkAlertResolution.WhitelistProgram => NetworkConnectionVerdict.Whitelisted,
                NetworkAlertResolution.BlacklistProgram => NetworkConnectionVerdict.Blacklisted,
                NetworkAlertResolution.AllowPort => NetworkConnectionVerdict.AllowSession,
                _ => NetworkConnectionVerdict.PendingAlert
            };
            hItem.Details = $"Решение пользователя: {request.Resolution}";
        }

        var path = !string.IsNullOrWhiteSpace(request.ProcessPath)
            ? request.ProcessPath.Trim()
            : (alert.ProcessPath ?? string.Empty).Trim();
        var name = !string.IsNullOrWhiteSpace(request.ProcessName)
            ? request.ProcessName.Trim()
            : (alert.ProcessName ?? string.Empty).Trim();

        var sessionKey = BuildSessionKey(path, name);
        var nameKey = BuildNameSessionKey(name);

        // Фиксируем решение в сессии по обоим ключам (путь и имя процесса).
        // Для сессионных решений задаётся ограниченный срок действия, чтобы одно
        // проигнорированное окно не заглушало программу навсегда.
        SetSessionDecision(sessionKey, request.Resolution);
        if (!string.IsNullOrWhiteSpace(name))
            SetSessionDecision(nameKey, request.Resolution);

        // МГНОВЕННО удаляем из ожидающих все алерты, относящиеся к этой же программе
        var keysToRemove = _pendingById.Values
            .Where(a => (!string.IsNullOrWhiteSpace(path) && string.Equals(a.ProcessPath, path, StringComparison.OrdinalIgnoreCase))
                     || (!string.IsNullOrWhiteSpace(name) && string.Equals(a.ProcessName, name, StringComparison.OrdinalIgnoreCase)))
            .Select(a => a.AlertId)
            .ToList();

        foreach (var k in keysToRemove)
        {
            _pendingById.TryRemove(k, out _);
        }
        PruneQueue(force: true);

        switch (request.Resolution)
        {
            case NetworkAlertResolution.WhitelistProgram:
                var whiteAppKey = !string.IsNullOrWhiteSpace(path) ? path : name;
                if (!string.IsNullOrWhiteSpace(whiteAppKey))
                {
                    // Взаимное исключение: при добавлении в белый список очищаем из чёрного
                    _blacklistedApps.TryRemove(whiteAppKey, out _);
                    _whitelistedApps.TryAdd(whiteAppKey, 0);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _blacklistedApps.TryRemove(name, out _);
                        _whitelistedApps.TryAdd(name, 0);
                    }

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        ApplyRule(BuildAppRule(path, name, FirewallAction.Allow));
                    }
                    if (_store != null)
                    {
                        try
                        {
                            var cfg = _store.Load();
                            if (!cfg.Settings.FirewallWhitelistedApps.Contains(whiteAppKey, StringComparer.OrdinalIgnoreCase))
                            {
                                cfg.Settings.FirewallWhitelistedApps.Add(whiteAppKey);
                                _store.Save(cfg);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "NetworkAlerts: ошибка сохранения белого списка программы");
                        }
                    }
                }
                break;

            case NetworkAlertResolution.BlacklistProgram:
                var blackAppKey = !string.IsNullOrWhiteSpace(path) ? path : name;
                if (!string.IsNullOrWhiteSpace(blackAppKey))
                {
                    // Абсолютный приоритет: при добавлении в чёрный список гарантированно вычищаем из белого
                    _whitelistedApps.TryRemove(blackAppKey, out _);
                    _blacklistedApps.TryAdd(blackAppKey, 0);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _whitelistedApps.TryRemove(name, out _);
                        _blacklistedApps.TryAdd(name, 0);
                    }

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        ApplyRule(BuildAppRule(path, name, FirewallAction.Block));
                    }
                    if (_store != null)
                    {
                        try
                        {
                            var cfg = _store.Load();
                            if (!cfg.Settings.FirewallBlacklistedApps.Contains(blackAppKey, StringComparer.OrdinalIgnoreCase))
                            {
                                cfg.Settings.FirewallBlacklistedApps.Add(blackAppKey);
                                _store.Save(cfg);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "NetworkAlerts: ошибка сохранения чёрного списка программы");
                        }
                    }
                }
                break;

            case NetworkAlertResolution.AllowPort:
                var targetPort = request.RemotePort > 0 ? request.RemotePort : alert.RemotePort;
                if (targetPort > 0)
                {
                    _allowedPorts.TryAdd(targetPort, 0);
                    if (_store != null)
                    {
                        try
                        {
                            var cfg = _store.Load();
                            if (!cfg.Settings.FirewallAllowedPorts.Contains(targetPort))
                            {
                                cfg.Settings.FirewallAllowedPorts.Add(targetPort);
                                _store.Save(cfg);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "NetworkAlerts: ошибка сохранения разрешенного порта");
                        }
                    }
                }
                break;

            case NetworkAlertResolution.AllowAlways:
                if (!string.IsNullOrWhiteSpace(path))
                    ApplyRule(BuildAppRule(path, name, FirewallAction.Allow));
                break;

            case NetworkAlertResolution.BlockAlways:
                if (!string.IsNullOrWhiteSpace(path))
                    ApplyRule(BuildAppRule(path, name, FirewallAction.Block));
                break;

            case NetworkAlertResolution.BlockSession:
            case NetworkAlertResolution.CreateRule:
            case NetworkAlertResolution.AllowSession:
            default:
                // Сессионные решения уже зафиксированы в _sessionDecisions
                break;
        }

        return true;
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

    private string? ResolveProcessPath(int pid, string processName)
    {
        if (ProcessPathResolver != null)
        {
            try { return ProcessPathResolver(pid); }
            catch { return null; }
        }

        if (pid <= 0)
            return null;
        try
        {
            // Быстрый путь через QueryFullProcessImageName (без тяжёлого MainModule).
            var fastPath = ProcessMetadataEnricher.TryGetProcessPath(pid);
            return string.IsNullOrWhiteSpace(fastPath) ? null : fastPath;
        }
        catch
        {
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

    private void ApplyRule(FirewallRuleDto rule)
    {
        try
        {
            // Сбрасываем кеш путей правил брандмауэра для немедленной актуализации
            _cachedRulePaths = null;

            if (ApplyFirewallRuleOverride != null)
                ApplyFirewallRuleOverride(rule);
            else
                _firewall.CreateOrUpdateRule(rule);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: не удалось создать правило {Rule}", rule.Name);
        }
    }

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
            Name = $"StartupMonitor_{verb}_{SanitizeRuleName(baseName)}_Auto",
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
}
