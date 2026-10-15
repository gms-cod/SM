using System.Collections.Concurrent;
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
        "StartupMonitor.Service.exe",
        "StartupMonitor.UI.exe",
    };

    /// <summary>Верхняя граница очереди ожидающих алертов (защита от переполнения памяти).</summary>
    public const int MaxPendingAlerts = 1000;

    private readonly ProcessNetworkTracker _tracker;
    private readonly WindowsFirewallService _firewall;

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

    /// <summary>Уже виденные связки "программа → remote:port" (защита от спама).</summary>
    private readonly ConcurrentDictionary<string, byte> _seenKeys =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _scanLock = new();
    private Timer? _pollTimer;
    private bool _disposed;

    /// <summary>Включён ли детектор (переключается через Pipe: SetAlertsEnabled).</summary>
    public bool AlertsEnabled { get; private set; } = true;

    /// <summary>Число ожидающих алертов.</summary>
    public int PendingCount => _pendingById.Count;

    /// <summary>Решения текущей сессии (только чтение).</summary>
    public IReadOnlyDictionary<string, NetworkAlertResolution> SessionDecisions => _sessionDecisions;

    /// <summary>
    /// Инициализирует детектор новых сетевых подключений.
    /// </summary>
    public NetworkConnectionAlertService(
        ProcessNetworkTracker tracker,
        WindowsFirewallService firewall)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
    }

    /// <summary>Запускает фоновый опрос новых подключений (вызывается из PipeServer).</summary>
    public void StartBackgroundScan(TimeSpan? dueTime = null, TimeSpan? period = null)
    {
        StopBackgroundScan();
        _pollTimer = new Timer(
            _ => { try { ScanNewConnections(); } catch { } },
            null,
            dueTime ?? TimeSpan.FromSeconds(5),
            period ?? TimeSpan.FromSeconds(5));
    }

    /// <summary>Останавливает фоновый опрос.</summary>
    public void StopBackgroundScan()
    {
        var timer = Interlocked.Exchange(ref _pollTimer, null);
        try { timer?.Dispose(); }
        catch { }
    }

    /// <summary>Освобождает таймер фонового опроса.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopBackgroundScan();
    }

    /// <summary>Включает/выключает детектор. Выключение сбрасывает очередь ожидающих алертов.</summary>
    public void SetAlertsEnabled(bool enabled)
    {
        AlertsEnabled = enabled;
        if (!enabled)
            ClearPending();
    }

    /// <summary>Очищает очередь ожидающих алертов (сессионные решения сохраняются).</summary>
    public void ClearPending()
    {
        _pendingById.Clear();
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
            // P0: GetAllRules() вызывается ровно один раз за скан (дорогой COM-перебор
            // всех правил), а в цикле проверка идёт за O(1) через HashSet путей.
            var rulePaths = BuildFirewallRulePathSet();
            foreach (var socket in sockets)
            {
                if (socket is null)
                    continue;

                var remote = (socket.RemoteAddress ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(remote) || socket.RemotePort <= 0)
                    continue;

                // 1. Игнорируем LAN / Loopback / unspecified.
                if (socket.IsRemotePrivate || ProcessNetworkSocket.CheckIsPrivateAddress(remote))
                    continue;

                var processName = (socket.ProcessName ?? string.Empty).Trim();
                var processPath = ResolveProcessPath(socket.ProcessId, processName);

                // 2. Игнорируем доверенные системные процессы.
                if (IsTrustedProcess(processName, processPath))
                    continue;

                var sessionKey = BuildSessionKey(processPath, processName);

                // 3. Пропускаем уже решённые в текущей сессии.
                if (_sessionDecisions.ContainsKey(sessionKey))
                    continue;

                // 4. Если для программы уже есть правило — алерт не создаём.
                if (HasExistingFirewallRuleCached(processPath, processName, rulePaths))
                    continue;

                // 5. Дедупликация повторных обращений к тому же remote:port.
                if (_pendingById.Count >= MaxPendingAlerts)
                    continue;
                // Защита от утечки памяти: сбрасываем дедупликационный кеш при переполнении.
                if (_seenKeys.Count > 10000)
                    _seenKeys.Clear();
                var seenKey = sessionKey + "|" + remote.ToLowerInvariant() + ":" + socket.RemotePort;
                if (!_seenKeys.TryAdd(seenKey, 0))
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
                }
            }

            PruneQueue();
            return added;
        }
    }

    /// <summary>Возвращает снимок ожидающих алертов (по времени обнаружения).</summary>
    public List<NetworkConnectionAlertDto> GetPendingAlerts()
    {
        return _pendingById.Values.OrderBy(a => a.DetectedAt).ToList();
    }

    /// <summary>
    /// Применяет решение пользователя по алерту.
    /// <see cref="NetworkAlertResolution.AllowAlways"/> — разрешающее правило в Firewall,
    /// <see cref="NetworkAlertResolution.BlockAlways"/> — блокирующее правило,
    /// <see cref="NetworkAlertResolution.AllowSession"/> — белый список текущей сессии.
    /// </summary>
    /// <returns>True — алерт найден и решение применено.</returns>
    public bool ResolveAlert(ResolveNetworkAlertRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AlertId))
            return false;

        if (!_pendingById.TryRemove(request.AlertId, out var alert))
            return false;

        var path = !string.IsNullOrWhiteSpace(request.ProcessPath)
            ? request.ProcessPath.Trim()
            : (alert.ProcessPath ?? string.Empty).Trim();
        var name = !string.IsNullOrWhiteSpace(request.ProcessName)
            ? request.ProcessName.Trim()
            : (alert.ProcessName ?? string.Empty).Trim();

        var sessionKey = BuildSessionKey(path, name);

        switch (request.Resolution)
        {
            case NetworkAlertResolution.WhitelistProgram:
            case NetworkAlertResolution.AllowAlways:
                _sessionDecisions[sessionKey] = request.Resolution;
                if (!string.IsNullOrWhiteSpace(path))
                    ApplyRule(BuildAppRule(path, name, FirewallAction.Allow));
                break;

            case NetworkAlertResolution.BlockAlways:
                _sessionDecisions[sessionKey] = NetworkAlertResolution.BlockAlways;
                if (!string.IsNullOrWhiteSpace(path))
                    ApplyRule(BuildAppRule(path, name, FirewallAction.Block));
                break;

            case NetworkAlertResolution.AllowSession:
            default:
                _sessionDecisions[sessionKey] = NetworkAlertResolution.AllowSession;
                break;
        }

        return true;
    }

    // ── Вспомогательная логика ──

    /// <summary>Проверяет, является ли процесс доверенным системным (алерты не создаются).</summary>
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
    /// Строит кеш путей программ с правилами Firewall (в нижнем регистре) за один
    /// проход. Возвращает null, когда проверка переопределена тестом или недоступна.
    /// </summary>
    private HashSet<string>? BuildFirewallRulePathSet()
    {
        if (HasFirewallRuleOverride != null)
            return null;

        try
        {
            var rules = _firewall.GetAllRules();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                var app = rule.ApplicationPath ?? rule.ApplicationName;
                if (string.IsNullOrWhiteSpace(app))
                    continue;
                var appTrimmed = app.Trim();
                if (appTrimmed.Length == 0)
                    continue;
                set.Add(appTrimmed.ToLowerInvariant());
                try
                {
                    var fileName = Path.GetFileName(appTrimmed);
                    if (!string.IsNullOrWhiteSpace(fileName))
                        set.Add(fileName.ToLowerInvariant());
                }
                catch { }
            }

            return set;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: не удалось проверить правила Firewall");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// O(1)-проверка наличия правила Firewall по пред построенному кешу путей.
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

        if (string.IsNullOrWhiteSpace(processPath) && string.IsNullOrWhiteSpace(processName))
            return false;

        if (!string.IsNullOrWhiteSpace(processPath) &&
            rulePaths.Contains(processPath.Trim().ToLowerInvariant()))
            return true;

        string? wantedFile = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(processPath))
                wantedFile = Path.GetFileName(processPath.Trim());
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(wantedFile) &&
            rulePaths.Contains(wantedFile.ToLowerInvariant()))
            return true;

        if (!string.IsNullOrWhiteSpace(processName) &&
            rulePaths.Contains(processName.Trim().ToLowerInvariant()))
            return true;

        return false;
    }

    private bool HasExistingFirewallRule(string? processPath, string? processName)
    {
        if (HasFirewallRuleOverride != null)
        {
            try { return HasFirewallRuleOverride(processPath ?? string.Empty); }
            catch { return false; }
        }

        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        try
        {
            var rules = _firewall.GetAllRules();
            var wantedPath = processPath.Trim();
            string? wantedFile = null;
            try { wantedFile = Path.GetFileName(wantedPath); }
            catch { }

            foreach (var rule in rules)
            {
                var app = rule.ApplicationPath ?? rule.ApplicationName;
                if (string.IsNullOrWhiteSpace(app))
                    continue;
                var appTrimmed = app.Trim();
                if (string.Equals(appTrimmed, wantedPath, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!string.IsNullOrWhiteSpace(wantedFile) &&
                    !string.IsNullOrWhiteSpace(processName) &&
                    string.Equals(appTrimmed, processName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
                try
                {
                    if (!string.IsNullOrWhiteSpace(wantedFile) &&
                        string.Equals(Path.GetFileName(appTrimmed), wantedFile, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NetworkAlerts: не удалось проверить правила Firewall");
        }

        return false;
    }

    private void ApplyRule(FirewallRuleDto rule)
    {
        try
        {
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
