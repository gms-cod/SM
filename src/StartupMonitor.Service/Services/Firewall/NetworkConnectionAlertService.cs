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
        "smss.exe",
        "csrss.exe",
        "wininit.exe",
        "winlogon.exe",
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
    /// <summary>Связь отдельных записей журнала с группой, решение которой завершает все её наблюдения.</summary>
    private readonly ConcurrentDictionary<string, string> _historyGroupById = new(StringComparer.Ordinal);

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

    /// <summary>Источник времени для детерминированной проверки сроков без изменения часов ОС.</summary>
    public Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

    /// <summary>Источник времени создания процесса; PID без этого значения не используется как долговечная идентичность.</summary>
    public Func<int, long?>? ProcessInstanceResolver { get; set; }

    /// <summary>Переопределение проверки наличия правила Firewall для пути программы.</summary>
    public Func<string, bool>? HasFirewallRuleOverride { get; set; }

    /// <summary>Изолированная проверка подтверждённой постоянной политики нужного действия.</summary>
    public Func<string, FirewallAction, bool>? HasApplicationPolicyOverride { get; set; }

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

    /// <summary>Единственный активный запрос полного приложения либо достоверного экземпляра неизвестного процесса.</summary>
    private readonly Dictionary<string, string> _pendingByGroup = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Отложенные карточки остаются событиями, но не предлагаются повторно до явного срока.</summary>
    private readonly Dictionary<string, DateTime> _deferredUntil = new(StringComparer.Ordinal);
    /// <summary>Набор наблюдений группы исключает увеличение счётчика на каждом повторном снимке.</summary>
    private readonly Dictionary<string, HashSet<string>> _observationsByAlert = new(StringComparer.Ordinal);
    /// <summary>Устаревшие идентификаторы завершают уже показанное окно без выдуманного применения политики.</summary>
    private readonly HashSet<string> _supersededAlerts = new(StringComparer.Ordinal);
    /// <summary>Ограниченные результаты операций защищают повтор после потери подтверждения.</summary>
    private readonly Dictionary<string, (string AlertId, NetworkAlertResolution Resolution, ResolveNetworkAlertResultPayload Result)> _operationResults = new(StringComparer.Ordinal);
    private readonly Queue<string> _operationOrder = new();
    /// <summary>Отмена фонового ИИ привязана к включённому поколению детектора.</summary>
    private CancellationTokenSource _aiLifetime = new();
    private long _policyRevision = -1;

    /// <summary>Подтверждения полных постоянных правил с TTL/ревизией, чтобы каждый сокет не перечислял COM заново.</summary>
    private readonly Dictionary<string, (long Revision, DateTime CheckedAt, HashSet<FirewallAction> Actions)> _persistentPolicyCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Изолируемый источник полных подтверждённых действий для проверки стоимости сканирования.</summary>
    internal Func<string, HashSet<FirewallAction>>? PersistentPolicyActionsProvider { get; set; }

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
        lock (_scanLock)
        {
            if (_disposed) return;
            _disposed = true;
            _aiLifetime.Cancel();
            _autonomousPending.Clear();
        }
        StopBackgroundScan();
        _sessionFirewall.Dispose();
        _aiLifetime.Dispose();
        _wake.Dispose();
    }

    /// <summary>Включает/выключает детектор. Выключение сбрасывает очередь ожидающих алертов.</summary>
    public void SetAlertsEnabled(bool enabled)
    {
        lock (_scanLock)
        {
            if (_disposed) return;
            _alertsEnabled = enabled;
            if (!enabled)
                ClearPending();
        }
        _wake.Pulse();
    }

    /// <summary>
    /// Очищает очередь ожидающих алертов и кэш дедупликации.
    /// Сброс дедупликации обязателен: иначе после повторного включения детектора
    /// уже «виденные» соединения больше никогда не дадут алерт.
    /// </summary>
    public void ClearPending()
    {
        lock (_scanLock)
        {
            foreach (var alert in _pendingById.Values)
                SupersedeAlert(alert);
            _pendingById.Clear();
            _pendingByGroup.Clear();
            _deferredUntil.Clear();
            _observationsByAlert.Clear();
            _seenKeys.Clear();
            _autonomousPending.Clear();
            _aiLifetime.Cancel();
            _aiLifetime.Dispose();
            _aiLifetime = new CancellationTokenSource();
            PruneQueue(force: true);
        }
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
            if (_policyRevision != _firewall.PolicyRevision)
            {
                _policyRevision = _firewall.PolicyRevision;
                _cachedRulePaths = null;
                foreach (var decision in _sessionDecisions.Where(pair => pair.Value is not (NetworkAlertResolution.AllowSession or NetworkAlertResolution.BlockSession)).ToArray())
                {
                    _sessionDecisions.TryRemove(decision.Key, out _);
                    _sessionDecisionExpiry.TryRemove(decision.Key, out _);
                }
            }
            foreach (var pending in _pendingById.Values)
                pending.IsActive = false;
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
                var groupKey = BuildPendingGroupKey(processPath, processName, socket.ProcessId);
                foreach (var previous in _pendingById.Values.Where(candidate => candidate.ProcessId == socket.ProcessId && string.IsNullOrWhiteSpace(candidate.ProcessPath)).ToArray())
                    if (!_pendingByGroup.TryGetValue(groupKey, out var currentUnknown) || currentUnknown != previous.AlertId)
                        SupersedeAlert(previous);
                // Полный путь не наследует решение по имени. Старый неизвестный PID лишь устаревает:
                // это безопасно и при переиспользовании PID новым процессом.
                if (!string.IsNullOrWhiteSpace(processPath))
                    foreach (var unknown in _pendingById.Values.Where(candidate => candidate.ProcessId == socket.ProcessId && string.IsNullOrWhiteSpace(candidate.ProcessPath)).ToArray())
                        SupersedeAlert(unknown);

                // 1.1 Абсолютный приоритет: Пропускаем программы из чёрного списка (соединения заблокированы, алерты подавлены)
                if (IsBlacklistedApp(processPath, processName) && HasConfirmedListPolicy(processPath, FirewallAction.Block))
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
                if (IsWhitelistedApp(processPath, processName) && HasConfirmedListPolicy(processPath, FirewallAction.Allow))
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

                var seenKey = groupKey + "|" +
                              (string.IsNullOrWhiteSpace(socket.Protocol) ? "TCP" : socket.Protocol) + "|" +
                              (socket.LocalAddress ?? string.Empty) + ":" + socket.LocalPort + "|" +
                              remote.ToLowerInvariant() + ":" + socket.RemotePort;
                if (_pendingByGroup.TryGetValue(groupKey, out var existingId) && _pendingById.TryGetValue(existingId, out var existing))
                {
                    existing.LastSeenAt = UtcNowProvider();
                    existing.IsActive = true;
                    if (_observationsByAlert.TryGetValue(existingId, out var observations) && observations.Count < MaxHistoryItems && observations.Add(seenKey))
                    {
                        existing.ConnectionCount++;
                        TryLogHistory(socket, processPath, processName, remote, NetworkConnectionVerdict.PendingAlert, "Дополнительное соединение ожидающей группы", existing.AlertId);
                    }
                    continue;
                }

                // 5. Дедупликация: ключ включает локальный endpoint, поэтому новое TCP-соединение
                // (новый эфемерный порт) снова даст алерт даже после истечения TTL.
                if (_pendingById.Count >= MaxPendingAlerts)
                    continue;
                if (!_seenKeys.TryAdd(seenKey, UtcNowProvider()))
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
                        generation,
                        socket.Direction);

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
                    Direction = socket.Direction,
                    RemoteAddress = remote,
                    RemotePort = socket.RemotePort,
                    DetectedAt = UtcNowProvider(),
                    LastSeenAt = UtcNowProvider(),
                    ConnectionCount = 1,
                    IsActive = true,
                };

                if (_pendingById.TryAdd(alert.AlertId, alert))
                {
                    _pendingByGroup[groupKey] = alert.AlertId;
                    _observationsByAlert[alert.AlertId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { seenKey };
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
                        Direction = socket.Direction,
                        Details = "Ожидает решения пользователя"
                    }, alert.AlertId);
                }
            }

            PruneQueue();
            return added;
        }
    }

    /// <summary>Добавляет запись в кольцевой журнал истории сетевых подключений.</summary>
    public void RecordHistory(NetworkConnectionHistoryItem item, string? groupId = null)
    {
        if (item is null) return;
        _historyQueue.Enqueue(item);
        if (!string.IsNullOrEmpty(item.Id))
        {
            _historyById[item.Id] = item;
            if (groupId != null) _historyGroupById[item.Id] = groupId;
        }

        while (_historyQueue.Count > MaxHistoryItems && _historyQueue.TryDequeue(out var old))
        {
            if (!string.IsNullOrEmpty(old.Id))
            {
                _historyById.TryRemove(old.Id, out _);
                _historyGroupById.TryRemove(old.Id, out _);
            }
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
        _historyGroupById.Clear();
        while (_historyQueue.TryDequeue(out _)) { }
    }

    /// <summary>Возвращает снимок ожидающих алертов (по времени обнаружения).</summary>
    public List<NetworkConnectionAlertDto> GetPendingAlerts()
    {
        lock (_scanLock)
        {
            if (_disposed || !AlertsEnabled) return [];
            RefreshSettingsCache();
            bool? canAllow = null;
            foreach (var alert in _pendingById.Values.ToArray())
            {
                if (!RetainUnallowedObservations(alert)) continue;
                if (!alert.IsActive && UtcNowProvider() - alert.LastSeenAt > TimeSpan.FromMinutes(15))
                {
                    SupersedeAlert(alert);
                    continue;
                }
                var key = BuildSessionKey(alert.ProcessPath, alert.ProcessName);
                if (IsBlacklistedApp(alert.ProcessPath, alert.ProcessName) && HasConfirmedListPolicy(alert.ProcessPath, FirewallAction.Block) ||
                    IsWhitelistedApp(alert.ProcessPath, alert.ProcessName) && HasConfirmedListPolicy(alert.ProcessPath, FirewallAction.Allow) ||
                    HasActiveSessionDecision(key) ||
                    HasExistingFirewallRuleCached(alert.ProcessPath, alert.ProcessName, BuildFirewallRulePathSet()))
                {
                    SupersedeAlert(alert);
                    continue;
                }
                alert.CanBlockSession = !string.IsNullOrWhiteSpace(alert.ProcessPath);
                alert.CanAllowSession = alert.CanBlockSession && (canAllow ??= _sessionFirewall.CanAllowSession);
            }
            var now = UtcNowProvider();
            foreach (var expired in _deferredUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            {
                _deferredUntil.Remove(expired);
                UpdateGroupHistory(expired, NetworkConnectionVerdict.PendingAlert, "Срок откладывания истёк; ожидается решение пользователя");
            }
            return _pendingById.Values.Where(alert => !_deferredUntil.TryGetValue(alert.AlertId, out var until) || until <= now)
                .OrderBy(a => a.DetectedAt).ToList();
        }
    }

    /// <summary>Строит ключ неизвестного процесса с временем старта, не создавая псевдоним по имени exe.</summary>
    private string BuildPendingGroupKey(string? path, string name, int pid)
    {
        if (!string.IsNullOrWhiteSpace(path)) return BuildSessionKey(path, name);
        long? started = null;
        try
        {
            if (ProcessInstanceResolver != null) started = ProcessInstanceResolver(pid);
            else if (pid > 0)
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                started = process.StartTime.ToUniversalTime().ToFileTimeUtc();
            }
        }
        catch (Exception ex) { Log.Debug(ex, "NetworkAlerts: идентичность PID {Pid} недоступна", pid); }
        return $"unknown:{pid}:{started?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unverified"}:{name.ToLowerInvariant()}";
    }

    /// <summary>Удаляет из смешанной группы только глобально разрешённые порты; оставшаяся область получает актуальную карточку.</summary>
    private bool RetainUnallowedObservations(NetworkConnectionAlertDto alert)
    {
        if (!_observationsByAlert.TryGetValue(alert.AlertId, out var observations) || observations.Count == 0)
        {
            if (!IsPortAllowed(alert.RemotePort)) return true;
            SupersedeAlert(alert);
            return false;
        }
        var remaining = observations.Where(observation =>
        {
            var separator = observation.LastIndexOf(':');
            return separator < 0 || !int.TryParse(observation[(separator + 1)..], out var port) || !IsPortAllowed(port);
        }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (remaining.Count == observations.Count) return true;
        if (remaining.Count == 0)
        {
            SupersedeAlert(alert);
            return false;
        }
        if (!IsPortAllowed(alert.RemotePort))
        {
            _observationsByAlert[alert.AlertId] = remaining;
            alert.ConnectionCount = remaining.Count;
            return true;
        }
        var endpoint = remaining.First().Split('|')[^1];
        var split = endpoint.LastIndexOf(':');
        if (split < 1 || !int.TryParse(endpoint[(split + 1)..], out var remotePort)) return true;
        var replacement = new NetworkConnectionAlertDto
        {
            ProcessId = alert.ProcessId, ProcessName = alert.ProcessName, ProcessPath = alert.ProcessPath, Protocol = alert.Protocol,
            Direction = alert.Direction, RemoteAddress = endpoint[..split], RemotePort = remotePort, DetectedAt = alert.DetectedAt,
            LastSeenAt = alert.LastSeenAt, IsActive = alert.IsActive, ConnectionCount = remaining.Count,
            CanAllowSession = alert.CanAllowSession, CanBlockSession = alert.CanBlockSession
        };
        foreach (var history in _historyQueue.Where(history => !IsPortAllowed(history.RemotePort) &&
            _historyGroupById.TryGetValue(history.Id, out var group) && group == alert.AlertId).ToArray())
            _historyGroupById[history.Id] = replacement.AlertId;
        SupersedeAlert(alert);
        _pendingById[replacement.AlertId] = replacement;
        _pendingByGroup[BuildPendingGroupKey(replacement.ProcessPath, replacement.ProcessName, replacement.ProcessId)] = replacement.AlertId;
        _observationsByAlert[replacement.AlertId] = remaining;
        _queue.Enqueue(replacement);
        _ = EnrichAlertWithAiAsync(replacement);
        return false;
    }

    /// <summary>Согласует все исторические наблюдения одной группы с подтверждённым состоянием её запроса.</summary>
    private void UpdateGroupHistory(string alertId, NetworkConnectionVerdict verdict, string details)
    {
        foreach (var item in _historyQueue.Where(item => item.Id == alertId ||
            _historyGroupById.TryGetValue(item.Id, out var group) && group == alertId))
        {
            item.Verdict = verdict;
            item.Details = details;
        }
    }

    /// <summary>Завершает устаревшую карточку без утверждения о применении пользовательской политики.</summary>
    private void SupersedeAlert(NetworkConnectionAlertDto alert)
    {
        UpdateGroupHistory(alert.AlertId, NetworkConnectionVerdict.RequestClosed, "Запрос завершён или заменён актуальной политикой; новое сетевое разрешение этим действием не создавалось");
        _pendingById.TryRemove(alert.AlertId, out _);
        foreach (var key in _pendingByGroup.Where(pair => pair.Value == alert.AlertId).Select(pair => pair.Key).ToArray())
            _pendingByGroup.Remove(key);
        _observationsByAlert.Remove(alert.AlertId);
        _deferredUntil.Remove(alert.AlertId);
        _supersededAlerts.Add(alert.AlertId);
        if (_supersededAlerts.Count > MaxPendingAlerts * 2)
            _supersededAlerts.Remove(_supersededAlerts.First());
    }

    /// <summary>Согласует запросы и проекции после внешнего изменения постоянной политики.</summary>
    /// <param name="path">Полный путь изменённого приложения либо null для общей инвалидации кэша.</param>
    public void NotifyPolicyChanged(string? path = null)
    {
        lock (_scanLock)
        {
            _persistentPolicyCache.Clear();
            _cachedRulePaths = null;
            _policyRevision = _firewall.PolicyRevision;
            _settingsRevision = -1;
            RefreshSettingsCache();
            foreach (var pair in _sessionDecisions.Where(pair => pair.Value is not (NetworkAlertResolution.AllowSession or NetworkAlertResolution.BlockSession) &&
                (path == null || string.Equals(pair.Key, BuildSessionKey(path, null), StringComparison.OrdinalIgnoreCase))).ToArray())
            {
                _sessionDecisions.TryRemove(pair.Key, out _);
                _sessionDecisionExpiry.TryRemove(pair.Key, out _);
            }
            if (path != null)
            {
                var key = BuildSessionKey(path, null);
                _autonomousPending.TryRemove(key, out _);
                foreach (var alert in _pendingById.Values.Where(alert => string.Equals(BuildSessionKey(alert.ProcessPath, alert.ProcessName), key, StringComparison.OrdinalIgnoreCase)).ToArray())
                    SupersedeAlert(alert);
                foreach (var seen in _seenKeys.Keys.Where(seen => seen.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase)))
                    _seenKeys.TryRemove(seen, out _);
            }
            PruneQueue(force: true);
        }
    }

    private void TryLogHistory(ProcessNetworkSocket socket, string? processPath, string processName, string remote, NetworkConnectionVerdict verdict, string details, string? groupId = null)
    {
        try
        {
            var dedupKey = $"{BuildSessionKey(processPath, processName)}|{socket.Protocol}|{socket.LocalAddress}:{socket.LocalPort}|{remote}:{socket.RemotePort}|{verdict}";
            var now = UtcNowProvider();
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
                Direction = socket.Direction,
                Details = details
            }, groupId);
        }
        catch (Exception ex) { Log.Debug(ex, "NetworkAlerts: не удалось добавить событие в журнал соединений"); }
    }

    /// <summary>
    /// Сериализует решение со сканированием и подтверждает его только после успешного
    /// применения политики. Неудача оставляет исходный алерт и историю доступными для повтора.
    /// </summary>
    /// <param name="request">Идентификатор алерта и выбранное действие; идентичность берётся из алерта службы.</param>
    /// <returns>True только для применённого решения либо его идемпотентного повтора.</returns>
    public bool ResolveAlert(ResolveNetworkAlertRequest request) => ResolveAlertDetailed(request).Success;

    /// <summary>Применяет идемпотентный выбор и возвращает точную причину завершения карточки.</summary>
    /// <param name="request">Идентификатор операции и авторитетной карточки.</param>
    /// <returns>Подтверждённое состояние либо диагностируемый отказ без ложного ACK.</returns>
    public ResolveNetworkAlertResultPayload ResolveAlertDetailed(ResolveNetworkAlertRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.AlertId) || !Enum.IsDefined(request.Resolution))
            return new(false, "Некорректное действие или идентификатор запроса.", NetworkAlertResolutionStatus.Unsupported);
        lock (_scanLock)
        {
            var operation = string.IsNullOrWhiteSpace(request.OperationId) ? request.AlertId + ":" + request.Resolution : request.OperationId;
            if (_operationResults.TryGetValue(operation, out var recorded))
                return recorded.AlertId == request.AlertId && recorded.Resolution == request.Resolution
                    ? recorded.Result with { Status = recorded.Result.Success ? NetworkAlertResolutionStatus.AlreadyApplied : recorded.Result.Status }
                    : new(false, "Идентификатор операции уже использован для другого выбора.", NetworkAlertResolutionStatus.Conflict, operation);
            ResolveNetworkAlertResultPayload Result(bool success, string message, NetworkAlertResolutionStatus status, NetworkAlertResolution? applied = null)
            {
                var result = new ResolveNetworkAlertResultPayload(success, message, status, operation, applied);
                // Отказы можно повторить с той же операцией после устранения причины; сохраняем подтверждения и неопределённые частичные исходы.
                if (success || status == NetworkAlertResolutionStatus.PartialFailure)
                {
                    _operationResults[operation] = (request.AlertId, request.Resolution, result);
                    _operationOrder.Enqueue(operation);
                    while (_operationOrder.Count > MaxPendingAlerts * 2)
                        _operationResults.Remove(_operationOrder.Dequeue());
                }
                return result;
            }
            if (_disposed || !AlertsEnabled)
                return Result(false, "Запрос завершён: интерактивный детектор остановлен.", NetworkAlertResolutionStatus.Superseded);
            if (_completedResolutions.TryGetValue(request.AlertId, out var previous))
                return Result(previous == request.Resolution, "Запрос уже завершён другим подтверждённым решением.",
                    previous == request.Resolution ? NetworkAlertResolutionStatus.AlreadyApplied : NetworkAlertResolutionStatus.Superseded, previous);
            if (!_pendingById.TryGetValue(request.AlertId, out var alert))
                return Result(false, "Запрос больше не актуален.", NetworkAlertResolutionStatus.Superseded);
            RefreshSettingsCache();
            if (request.Resolution == NetworkAlertResolution.Defer)
            {
                _deferredUntil[alert.AlertId] = UtcNowProvider().AddMinutes(5);
                UpdateGroupHistory(alert.AlertId, NetworkConnectionVerdict.Deferred, "Показ отложен на пять минут без изменения сетевой политики");
                _autonomousPending.TryRemove(BuildSessionKey(alert.ProcessPath, alert.ProcessName), out _);
                return Result(true, "Показ отложен на пять минут; сетевые правила не изменены.", NetworkAlertResolutionStatus.Deferred);
            }
            if (request.Resolution == NetworkAlertResolution.CreateRule ||
                (request.Resolution != NetworkAlertResolution.AllowPort && string.IsNullOrWhiteSpace(alert.ProcessPath)) ||
                request.Resolution == NetworkAlertResolution.AllowSession && !_sessionFirewall.CanAllowSession)
                return Result(false, "Действие недоступно для текущего пути или режима фильтрации.", NetworkAlertResolutionStatus.Unsupported);
            if (request.Resolution == NetworkAlertResolution.AllowSession && IsBlacklistedApp(alert.ProcessPath, alert.ProcessName))
                return Result(false, "Постоянный чёрный список запрещает временное разрешение.", NetworkAlertResolutionStatus.Conflict);
            _lastApplyResult = null;
            var applied = ResolveAlertCore(request);
            if (applied)
                return Result(true, "Выбор применён; область действия соответствует указанному правилу.", NetworkAlertResolutionStatus.Applied, request.Resolution);
            var partial = _lastApplyResult?.Message?.StartsWith("PartialFailure", StringComparison.OrdinalIgnoreCase) == true;
            return Result(false, _lastApplyResult?.Message ?? "Не удалось применить политику. Исходный запрос сохранён для повтора.",
                partial ? NetworkAlertResolutionStatus.PartialFailure : NetworkAlertResolutionStatus.Failed);
        }
    }

    /// <summary>Диагностика последнего применения; используется только внутри сериализованного резолва.</summary>
    private FirewallOperationResult? _lastApplyResult;

    /// <summary>Выполняет проверенный выбор под общей блокировкой сканирования.</summary>
    private bool ResolveAlertCore(ResolveNetworkAlertRequest request)
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
                        }, BuildPersistenceAction(alert, request.Resolution)))
                            return false;
                        break;
                    case NetworkAlertResolution.AllowSession:
                    case NetworkAlertResolution.BlockSession:
                        var temporary = _sessionFirewall.Apply(path,
                            request.Resolution == NetworkAlertResolution.AllowSession ? FirewallAction.Allow : FirewallAction.Block,
                            SessionDecisionTtl);
                        if (!temporary.Success)
                        {
                            _lastApplyResult = temporary;
                            Log.Warning("NetworkAlerts: временное решение {Resolution} отклонено: {Message}",
                                request.Resolution, temporary.Message);
                            return false;
                        }
                        break;
                    case NetworkAlertResolution.WhitelistProgram:
                    case NetworkAlertResolution.AllowAlways:
                        if (!ApplyRule(BuildAppRule(path, name, FirewallAction.Allow), BuildPersistenceAction(alert, request.Resolution)))
                            return false;
                        break;
                    case NetworkAlertResolution.BlacklistProgram:
                    case NetworkAlertResolution.BlockAlways:
                        if (!ApplyRule(BuildAppRule(path, name, FirewallAction.Block), BuildPersistenceAction(alert, request.Resolution)))
                            return false;
                        break;
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
                    ? candidate.RemotePort == alert.RemotePort || _observationsByAlert.TryGetValue(candidate.AlertId, out var keys) && keys.Any(seen => seen.EndsWith(":" + alert.RemotePort, StringComparison.Ordinal))
                    : string.Equals(BuildSessionKey(candidate.ProcessPath, candidate.ProcessName), key,
                        StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var candidate in affected)
            {
                if (request.Resolution == NetworkAlertResolution.AllowPort && _observationsByAlert.TryGetValue(candidate.AlertId, out var observations))
                {
                    var remaining = observations.Where(seen => !seen.EndsWith(":" + alert.RemotePort, StringComparison.Ordinal)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (remaining.Count > 0)
                    {
                        if (candidate.RemotePort != alert.RemotePort)
                        {
                            _observationsByAlert[candidate.AlertId] = remaining;
                            candidate.ConnectionCount = remaining.Count;
                            continue;
                        }
                        var endpoint = remaining.First().Split('|')[^1];
                        var separator = endpoint.LastIndexOf(':');
                        if (separator > 0 && int.TryParse(endpoint[(separator + 1)..], out var port))
                        {
                            var replacement = new NetworkConnectionAlertDto
                            {
                                ProcessId = candidate.ProcessId, ProcessName = candidate.ProcessName, ProcessPath = candidate.ProcessPath,
                                Protocol = candidate.Protocol, Direction = candidate.Direction, RemoteAddress = endpoint[..separator], RemotePort = port,
                                DetectedAt = candidate.DetectedAt, LastSeenAt = candidate.LastSeenAt, IsActive = candidate.IsActive,
                                ConnectionCount = remaining.Count, CanAllowSession = candidate.CanAllowSession, CanBlockSession = candidate.CanBlockSession
                            };
                            foreach (var observed in _historyQueue.Where(observed => observed.RemotePort != alert.RemotePort &&
                                (_historyGroupById.TryGetValue(observed.Id, out var group) && group == candidate.AlertId)).ToArray())
                                _historyGroupById[observed.Id] = replacement.AlertId;
                            SupersedeAlert(candidate);
                            _pendingById[replacement.AlertId] = replacement;
                            _pendingByGroup[BuildPendingGroupKey(replacement.ProcessPath, replacement.ProcessName, replacement.ProcessId)] = replacement.AlertId;
                            _observationsByAlert[replacement.AlertId] = remaining;
                            _queue.Enqueue(replacement);
                            _ = EnrichAlertWithAiAsync(replacement);
                        }
                    }
                }
                if (_historyById.TryGetValue(candidate.AlertId, out var item))
                    item.Verdict = ResolutionVerdict(request.Resolution);
                UpdateGroupHistory(candidate.AlertId, ResolutionVerdict(request.Resolution), "Пользовательская политика применена к области группы");
                _pendingById.TryRemove(candidate.AlertId, out _);
                foreach (var group in _pendingByGroup.Where(group => group.Value == candidate.AlertId).Select(group => group.Key).ToArray())
                    _pendingByGroup.Remove(group);
                _observationsByAlert.Remove(candidate.AlertId);
                _deferredUntil.Remove(candidate.AlertId);
                RememberResolution(candidate.AlertId, request.Resolution);
            }
            PruneQueue(force: true);
            return true;
        }
    }

    /// <summary>Создаёт сохраняемое намерение, выполняемое внутри компенсируемой записи постоянной политики.</summary>
    private Action? BuildPersistenceAction(NetworkConnectionAlertDto alert, NetworkAlertResolution resolution)
    {
        if (_store == null || resolution is not (NetworkAlertResolution.WhitelistProgram or NetworkAlertResolution.BlacklistProgram or NetworkAlertResolution.AllowPort))
            return null;
        return () =>
        {
            var cfg = _store.Load();
            if (resolution == NetworkAlertResolution.AllowPort)
            {
                if (!cfg.Settings.FirewallAllowedPorts.Contains(alert.RemotePort)) cfg.Settings.FirewallAllowedPorts.Add(alert.RemotePort);
            }
            else
            {
                var allowed = resolution == NetworkAlertResolution.WhitelistProgram;
                var target = allowed ? cfg.Settings.FirewallWhitelistedApps : cfg.Settings.FirewallBlacklistedApps;
                var opposite = allowed ? cfg.Settings.FirewallBlacklistedApps : cfg.Settings.FirewallWhitelistedApps;
                opposite.RemoveAll(app => string.Equals(app, alert.ProcessPath, StringComparison.OrdinalIgnoreCase));
                if (!target.Contains(alert.ProcessPath, StringComparer.OrdinalIgnoreCase)) target.Add(alert.ProcessPath);
            }
            _store.Save(cfg);
        };
    }

    /// <summary>Переводит подтверждённое действие в вердикт журнала без изменения исходных событий.</summary>
    /// <param name="resolution">Успешно применённое действие.</param>
    /// <returns>Вердикт, соответствующий области действия политики.</returns>
    private static NetworkConnectionVerdict ResolutionVerdict(NetworkAlertResolution resolution) => resolution switch
    {
        NetworkAlertResolution.AllowSession => NetworkConnectionVerdict.AllowSession,
        NetworkAlertResolution.AllowPort => NetworkConnectionVerdict.RuleCreated,
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
            _sessionDecisionExpiry[key] = UtcNowProvider() + duration;
            foreach (var pending in _pendingById.Values.Where(pending =>
                string.Equals(BuildSessionKey(pending.ProcessPath, pending.ProcessName), key, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                SupersedeAlert(pending);
                if (_historyById.TryGetValue(pending.AlertId, out var history))
                    history.Verdict = ResolutionVerdict(resolution);
                UpdateGroupHistory(pending.AlertId, ResolutionVerdict(resolution), "Временная исходящая политика применена к приложению");
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
        var now = UtcNowProvider();
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
            : UtcNowProvider() + ttl;
    }

    /// <summary>
    /// Проверяет активное (не истёкшее) решение по ключу. Истёкшее решение удаляется,
    /// чтобы программа снова могла порождать алерты.
    /// </summary>
    private bool HasActiveSessionDecision(string key)
    {
        if (!_sessionDecisions.ContainsKey(key))
            return false;

        // Постоянный выбор является проекцией действующей политики, а не вечным разрешением памяти.
        if (_sessionDecisions.TryGetValue(key, out var persistent) && persistent is not (NetworkAlertResolution.AllowSession or NetworkAlertResolution.BlockSession) && key.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
        {
            var path = key[5..];
            bool exists;
            if (persistent is NetworkAlertResolution.WhitelistProgram or NetworkAlertResolution.BlacklistProgram)
                exists = persistent == NetworkAlertResolution.WhitelistProgram
                    ? IsWhitelistedApp(path, null) && HasConfirmedListPolicy(path, FirewallAction.Allow)
                    : IsBlacklistedApp(path, null) && HasConfirmedListPolicy(path, FirewallAction.Block);
            else if (PersistentPolicyActionsProvider != null)
                exists = HasConfirmedPersistentPolicy(path, persistent == NetworkAlertResolution.AllowAlways ? FirewallAction.Allow : FirewallAction.Block);
            else if (HasApplicationPolicyOverride != null)
                exists = HasApplicationPolicyOverride(path, persistent == NetworkAlertResolution.AllowAlways ? FirewallAction.Allow : FirewallAction.Block);
            else if (HasFirewallRuleOverride != null)
                exists = HasFirewallRuleOverride(path);
            else
                exists = HasConfirmedPersistentPolicy(path, persistent == NetworkAlertResolution.AllowAlways ? FirewallAction.Allow : FirewallAction.Block);
            if (!exists)
            {
                _sessionDecisions.TryRemove(key, out _);
                _sessionDecisionExpiry.TryRemove(key, out _);
                return false;
            }
        }

        if (_sessionDecisions.TryGetValue(key, out var resolution) &&
            resolution is NetworkAlertResolution.AllowSession or NetworkAlertResolution.BlockSession &&
            key.StartsWith("path:", StringComparison.OrdinalIgnoreCase) && !_sessionFirewall.IsActive(key[5..]))
        {
            _sessionDecisions.TryRemove(key, out _);
            _sessionDecisionExpiry.TryRemove(key, out _);
            return false;
        }

        if (_sessionDecisionExpiry.TryGetValue(key, out var expiresAt) && expiresAt <= UtcNowProvider())
        {
            _sessionDecisions.TryRemove(key, out _);
            _sessionDecisionExpiry.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    /// <summary>Проверяет только полную собственную политику; TTL ограничивает устаревание после внешней правки Windows.</summary>
    /// <param name="path">Канонический полный путь приложения.</param>
    /// <param name="action">Подтверждаемое постоянное действие.</param>
    /// <returns>Наличие действующего полного правила, а не узкого пользовательского исключения.</returns>
    private bool HasConfirmedPersistentPolicy(string path, FirewallAction action)
    {
        var revision = _firewall.PolicyRevision;
        var now = UtcNowProvider();
        if (!_persistentPolicyCache.TryGetValue(path, out var cached) || cached.Revision != revision || now - cached.CheckedAt >= _ruleCacheTtl)
        {
            var actions = PersistentPolicyActionsProvider?.Invoke(path) ?? _firewall.GetApplicationPolicyActions(path);
            if (_persistentPolicyCache.Count >= MaxPendingAlerts * 2) _persistentPolicyCache.Clear();
            cached = (revision, now, actions);
            _persistentPolicyCache[path] = cached;
        }
        return cached.Actions.Contains(action);
    }

    /// <summary>Отделяет сохранённое намерение списка от реально подтверждённого полного правила.</summary>
    /// <param name="path">Достоверный путь программы; имя без пути не подтверждает системную политику.</param>
    /// <param name="action">Действие сохранённого списка.</param>
    /// <returns>False при внешнем удалении правила или недоступной проверке, чтобы запрос не скрывался навсегда.</returns>
    private bool HasConfirmedListPolicy(string? path, FirewallAction action)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            if (PersistentPolicyActionsProvider != null) return HasConfirmedPersistentPolicy(path, action);
            if (HasApplicationPolicyOverride != null) return HasApplicationPolicyOverride(path, action);
            // Управляемый адаптер применения в изолированных тестах является владельцем его записей.
            if (ApplyFirewallRuleOverride != null) return true;
            return HasConfirmedPersistentPolicy(path, action);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: действующая политика сохранённого списка не подтверждена");
            return false;
        }
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
                if (_aiAutonomousNetworkDecisionsEnabled != settings.AiAutonomousNetworkDecisionsEnabled ||
                    !string.Equals(_aiAutonomousDecisionMode, settings.AiAutonomousDecisionMode, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var key in _autonomousPending.Keys)
                        foreach (var seen in _seenKeys.Keys.Where(seen => seen.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase)))
                            _seenKeys.TryRemove(seen, out _);
                    _autonomousPending.Clear();
                    _aiLifetime.Cancel();
                    _aiLifetime.Dispose();
                    _aiLifetime = new CancellationTokenSource();
                }
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
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath)) return false;
        try
        {
            var path = Path.GetFullPath(processPath.Trim().Trim('"'));
            var fileName = Path.GetFileName(path);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windows)) return false;
            if (string.Equals(fileName, "StartupMonitor.Service.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "StartupMonitor.UI.exe", StringComparison.OrdinalIgnoreCase))
                return string.Equals(path, Path.Combine(AppContext.BaseDirectory, fileName), StringComparison.OrdinalIgnoreCase);
            if (string.Equals(fileName, "ntoskrnl.exe", StringComparison.OrdinalIgnoreCase))
                return string.Equals(path, Path.Combine(windows, "System32", fileName), StringComparison.OrdinalIgnoreCase);
            if (!TrustedSystemProcesses.Contains(fileName)) return false;
            if (string.Equals(fileName, "explorer.exe", StringComparison.OrdinalIgnoreCase))
                return string.Equals(path, Path.Combine(windows, fileName), StringComparison.OrdinalIgnoreCase);
            return string.Equals(path, Path.Combine(windows, "System32", fileName), StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(Path.Combine(windows, "SystemApps") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) { Log.Debug(ex, "NetworkAlerts: не удалось проверить расположение доверенного компонента"); }
        return false;
    }

    /// <summary>Строит ключ сессии/дедупликации: путь программы (или имя, если пути нет).</summary>
    public static string BuildSessionKey(string? processPath, string? processName)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
            return "path:" + NormalizeProcessPath(processPath);
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
        if (_policyRevision != _firewall.PolicyRevision)
        {
            _policyRevision = _firewall.PolicyRevision;
            _cachedRulePaths = null;
        }
        if (_cachedRulePaths != null && (now - _cachedRulePathsTime) < _ruleCacheTtl)
            return _cachedRulePaths;

        try
        {
            // Читаем только разрешающие исходящие правила: входящие/блокирующие не подавляют алерт.
            var set = _firewall.GetUnrestrictedOutboundAllowApplicationRulePaths();
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
        return WindowsFirewallService.NormalizeApplicationPath(processPath);
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
    private bool ApplyRule(FirewallRuleDto rule, Action? persist = null)
    {
        try
        {
            // Сбрасываем кеш путей правил брандмауэра для немедленной актуализации
            _cachedRulePaths = null;

            if (ApplyFirewallRuleOverride != null)
            {
                ApplyFirewallRuleOverride(rule);
                try { persist?.Invoke(); }
                catch (Exception ex)
                {
                    _lastApplyResult = FirewallOperationResult.Fail("PartialFailure: тестовый адаптер уже принял правило, сохранение не выполнено; требуется сверка. " + ex.Message);
                    return false;
                }
                return true;
            }
            else
            {
                var result = _firewall.ApplyPolicyRule(rule, persist);
                _lastApplyResult = result;
                if (!result.Success)
                    Log.Warning("NetworkAlerts: правило {Rule} не применено: {Message}", rule.Name, result.Message);
                _policyRevision = _firewall.PolicyRevision;
                return result.Success;
            }
        }
        catch (Exception ex)
        {
            _lastApplyResult = FirewallOperationResult.Fail("Правило не применено: " + ex.Message);
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
        return WindowsFirewallService.BuildApplicationRule(processPath, processName, action);
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
        long generation,
        string direction = "Unknown")
    {
        var cancellation = _aiLifetime.Token;
        try
        {
            (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)? verdict = null;

            if (AiEvaluatorOverride != null)
            {
                try
                {
                    verdict = await AiEvaluatorOverride(processName, processPath ?? string.Empty, remoteAddress, remotePort).WaitAsync(cancellation).ConfigureAwait(false);
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
                    verdict = await _aiRating.EvaluateNetworkProcessAsync(processName, processPath ?? string.Empty, remoteAddress, remotePort, cancellation).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NetworkAlerts: сбой EvaluateNetworkProcessAsync для {Process}", processName);
                }
            }

            NetworkAlertResolution resolution;
            lock (_scanLock)
            {
                RefreshSettingsCache();
                // Ручное решение или остановка службы отменяют право запоздалой AI-оценки менять политику.
                if (_disposed || cancellation.IsCancellationRequested || !AlertsEnabled || !_aiAutonomousNetworkDecisionsEnabled ||
                    !_autonomousPending.TryGetValue(sessionKey, out var activeGeneration) || activeGeneration != generation || _pendingByGroup.ContainsKey(sessionKey))
                    return;
                var action = verdict?.RecommendedAction;
                if (action is not ("AllowSession" or "AllowAlways" or "BlockSession" or "BlockAlways"))
                {
                    QueueAutonomousFailure(processId, processPath, processName, protocol, remoteAddress, remotePort, localAddress, localPort,
                        "Нет подтверждённого допустимого заключения ИИ", direction);
                    return;
                }
                resolution = string.Equals(_aiAutonomousDecisionMode, "BlockSession", StringComparison.OrdinalIgnoreCase) || action is "BlockSession" or "BlockAlways"
                    ? NetworkAlertResolution.BlockSession : NetworkAlertResolution.AllowSession;
                var result = ApplySessionAccess(processPath ?? string.Empty, processName, resolution);
                if (!result.Success)
                {
                    Log.Warning("NetworkAlerts: автономное решение {Resolution} не применено: {Message}", resolution, result.Message);
                    QueueAutonomousFailure(processId, processPath, processName, protocol, remoteAddress, remotePort, localAddress, localPort, result.Message, direction);
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
                Direction = direction,
                Details = $"Автономное решение ИИ ({resolution}): {purposeDesc}. {recDesc}"
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Log.Debug("NetworkAlerts: устаревшая автономная оценка отменена");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: сбой выполнения автономного решения для {Process}", processName);
            lock (_scanLock)
            {
                if (!_disposed && _autonomousPending.TryGetValue(sessionKey, out var activeGeneration) && activeGeneration == generation)
                    QueueAutonomousFailure(processId, processPath, processName, protocol, remoteAddress, remotePort, localAddress, localPort, ex.Message, direction);
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
    private void QueueAutonomousFailure(int processId, string? path, string name, string protocol, string remote, int remotePort, string local, int localPort, string failure, string direction = "Unknown")
    {
        var key = BuildPendingGroupKey(path, name, processId);
        if (_disposed || !AlertsEnabled || _pendingById.Count >= MaxPendingAlerts || _pendingByGroup.ContainsKey(key))
            return;
        var alert = new NetworkConnectionAlertDto
        {
            ProcessId = processId, ProcessPath = path ?? string.Empty, ProcessName = name,
            Protocol = protocol, RemoteAddress = remote, RemotePort = remotePort,
            Direction = direction, LastSeenAt = UtcNowProvider(), IsActive = true,
            DetectedAt = UtcNowProvider(),
            CanAllowSession = !string.IsNullOrWhiteSpace(path) && _sessionFirewall.CanAllowSession,
            CanBlockSession = !string.IsNullOrWhiteSpace(path)
        };
        if (_pendingById.TryAdd(alert.AlertId, alert))
        {
            _pendingByGroup[key] = alert.AlertId;
            _observationsByAlert[alert.AlertId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                key + "|" + protocol + "|" + local + ":" + localPort + "|" + remote.ToLowerInvariant() + ":" + remotePort
            };
            _queue.Enqueue(alert);
            RecordHistory(new NetworkConnectionHistoryItem
            {
                Id = alert.AlertId, Timestamp = alert.DetectedAt, ProcessId = processId,
                ProcessPath = path ?? string.Empty, ProcessName = name, Protocol = protocol,
                RemoteAddress = remote, RemotePort = remotePort, LocalAddress = local, LocalPort = localPort,
                Verdict = NetworkConnectionVerdict.PendingAlert, Direction = direction,
                Details = $"Автономное решение не применено: {failure}. Ожидается выбор пользователя."
            }, alert.AlertId);
        }
    }

    /// <summary>
    /// Фоновое обогащение ожидающего алерта оценкой нейросети.
    /// </summary>
    private async Task EnrichAlertWithAiAsync(NetworkConnectionAlertDto alert)
    {
        var cancellation = _aiLifetime.Token;
        try
        {
            (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)? verdict = null;

            if (AiEvaluatorOverride != null)
            {
                verdict = await AiEvaluatorOverride(alert.ProcessName, alert.ProcessPath, alert.RemoteAddress, alert.RemotePort).WaitAsync(cancellation).ConfigureAwait(false);
            }
            else if (_aiRating != null)
            {
                verdict = await _aiRating.EvaluateNetworkProcessAsync(alert.ProcessName, alert.ProcessPath, alert.RemoteAddress, alert.RemotePort, cancellation).ConfigureAwait(false);
            }

            if (verdict != null && !cancellation.IsCancellationRequested && _pendingById.ContainsKey(alert.AlertId))
            {
                alert.AiPurpose = verdict.Value.Purpose;
                alert.AiBlockingRisk = verdict.Value.BlockingRisk;
                alert.AiRecommendation = verdict.Value.Recommendation;
                alert.AiRecommendedAction = verdict.Value.RecommendedAction;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Log.Debug("NetworkAlerts: обогащение устаревшей карточки отменено"); }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: сбой фонового обогащения алерта {AlertId}", alert.AlertId);
        }
    }

    /// <summary>
    /// Запрашивает оценку нейросети для сетевого алерта (вызывается через IPC PipeServer при открытии диалогового окна).
    /// </summary>
    public async Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?> EvaluateAlertAsync(
        string alertId, string processName, string processPath, string remoteAddress, int remotePort, CancellationToken cancellationToken = default)
    {
        NetworkConnectionAlertDto authoritative;
        lock (_scanLock)
        {
            if (_disposed || !AlertsEnabled || !_pendingById.TryGetValue(alertId, out authoritative!)) return null;
            processName = authoritative.ProcessName;
            processPath = authoritative.ProcessPath;
            remoteAddress = authoritative.RemoteAddress;
            remotePort = authoritative.RemotePort;
        }
        (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)? verdict = null;

        if (AiEvaluatorOverride != null)
        {
            verdict = await AiEvaluatorOverride(processName, processPath, remoteAddress, remotePort).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_aiRating != null)
        {
            verdict = await _aiRating.EvaluateNetworkProcessAsync(processName, processPath, remoteAddress, remotePort, cancellationToken).ConfigureAwait(false);
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
