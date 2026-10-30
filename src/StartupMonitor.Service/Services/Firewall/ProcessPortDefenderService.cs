using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.Runtime;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services.Firewall;

/// <summary>
/// Ядро службы защиты портов (Port Defender) для Startup Monitor.
/// Анализирует частоту входящих TCP-соединений через алгоритм скользящего окна (Sliding Window),
/// обнаруживает сканирование портов, перебор паролей (brute force) и флуд,
/// автоматически блокирует атакующие IP в Windows Firewall и производит адаптивную эскалацию длительности бана.
/// </summary>
public sealed class ProcessPortDefenderService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly WindowsFirewallService _firewall;
    private readonly ProcessNetworkTracker _networkTracker;
    private readonly NotificationService _notifications;
    private readonly JsonDataStore _dataStore;
    private readonly ILogger<ProcessPortDefenderService> _logger;

    private readonly SlidingWindowTracker _windowTracker = new();
    private readonly object _sync = new();

    private readonly string _bansFilePath;
    private readonly string _configFilePath;

    // Активные баны: IP -> ActiveBanDto
    private readonly ConcurrentDictionary<string, ActiveBanDto> _activeBans =
        new(StringComparer.OrdinalIgnoreCase);

    // История повторных нарушений: IP -> (RepeatCount, LastBanUtc)
    private readonly ConcurrentDictionary<string, (int RepeatCount, DateTime LastBanUtc)> _repeatHistory =
        new(StringComparer.OrdinalIgnoreCase);

    // Ранее зафиксированные активные соединения (дедупликация между секундными снимками)
    private HashSet<string> _previousConnectionKeys = [];

    // Конфигурация службы
    private PortDefenderConfigDto _config = new();

    // ── Фоновый цикл в режиме 0% CPU Idle ──
    // Опрос сокетов выполняется только при наличии активного UI-клиента И включённых
    // правил. Во всех остальных случаях цикл спит неограниченно долго (0% CPU).
    private readonly ActivityGate _wake = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _uiActive;
    private bool _disposed;

    /// <summary>
    /// Инициализирует службу ProcessPortDefenderService.
    /// </summary>
    public ProcessPortDefenderService(
        WindowsFirewallService firewall,
        ProcessNetworkTracker networkTracker,
        NotificationService notifications,
        JsonDataStore dataStore,
        ILogger<ProcessPortDefenderService> logger)
    {
        _firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        _networkTracker = networkTracker ?? throw new ArgumentNullException(nameof(networkTracker));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var baseDir = _dataStore.BaseDirectory;
        _bansFilePath = Path.Combine(baseDir, "firewall-bans.json");
        _configFilePath = Path.Combine(baseDir, "firewall-defender.json");

        LoadConfig();
        LoadActiveBans();

        // Запуск адаптивного цикла опроса: активен только при UI-клиенте и правилах,
        // иначе — глубокий сон (0% CPU) до Pulse/смены конфигурации.
        StartLoop();
    }

    /// <summary>Регистрирует наличие/отсутствие активного UI-клиента (вызывается PipeServer).</summary>
    public void SetUiActive(bool active)
    {
        if (_uiActive == active) return;
        _uiActive = active;
        _wake.Pulse();
    }

    /// <summary>Есть ли работа: активен UI-клиент И включён хотя бы один порт-правило.</summary>
    private bool HasWork()
    {
        if (!_uiActive) return false;
        lock (_sync)
        {
            return _config.IsGloballyEnabled &&
                   (_config.Rules.Any(r => r.IsEnabled) || (_config.ProtectedPorts != null && _config.ProtectedPorts.Count > 0));
        }
    }

    private void StartLoop()
    {
        if (_loop is { IsCompleted: false }) return;
        try { _cts?.Dispose(); } catch { }
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    private void StopLoop()
    {
        var cts = _cts;
        _cts = null;
        _loop = null;
        if (cts == null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
    }

    /// <summary>
    /// Адаптивный цикл: при наличии работы опрос каждые 3 секунды, иначе — глубокий
    /// сон до смены конфигурации (<see cref="SaveConfig"/>) или подключения UI-клиента.
    /// </summary>
    private async Task RunAsync(CancellationToken ct)
    {
        // Первоначальная задержка — не мешаем старту службы.
        try { await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        await SleepAwareLoop.RunAsync(
            _wake,
            HasWork,
            activeInterval: TimeSpan.FromSeconds(3),
            idleInterval: Timeout.InfiniteTimeSpan, // 0% CPU: сон до события/отмены
            body: _ =>
            {
                try
                {
                    var sockets = _networkTracker.GetSockets();
                    if (sockets != null && sockets.Count > 0)
                        ProcessSocketSnapshot(sockets);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "PortDefender: Ошибка при периодическом сканировании сокетов");
                }
                return Task.CompletedTask;
            },
            ct).ConfigureAwait(false);
    }

    // ───────────────────────────── Конфигурация ─────────────────────────────

    /// <summary>
    /// Возвращает текущую конфигурацию правил защиты Port Defender.
    /// </summary>
    public PortDefenderConfigDto GetConfig()
    {
        lock (_sync)
        {
            return new PortDefenderConfigDto
            {
                IsGloballyEnabled = _config.IsGloballyEnabled,
                DefaultBanDurationMinutes = _config.DefaultBanDurationMinutes,
                TrustPrivateNetworks = _config.TrustPrivateNetworks,
                WhitelistIps = [.. _config.WhitelistIps],
                ProtectedPorts = [.. _config.ProtectedPorts],
                MaxAttempts = _config.MaxAttempts,
                WindowUnit = _config.WindowUnit,
                WindowSeconds = _config.WindowSeconds,
                BanDurationMinutes = _config.BanDurationMinutes,
                AutoEscalate = _config.AutoEscalate,
                Rules = _config.Rules.Select(r => new PortDefenderRuleConfig
                {
                    LocalPort = r.LocalPort,
                    ProcessPath = r.ProcessPath,
                    IsEnabled = r.IsEnabled,
                    ThresholdCount = r.ThresholdCount,
                    WindowUnit = r.WindowUnit,
                    BanDurationMinutes = r.BanDurationMinutes,
                    AutoEscalate = r.AutoEscalate,
                    WhitelistIps = [.. r.WhitelistIps]
                }).ToList()
            };
        }
    }

    /// <summary>
    /// Сохраняет новую конфигурацию правил защиты Port Defender.
    /// </summary>
    public void SaveConfig(PortDefenderConfigDto config)
    {
        if (config == null) return;
        lock (_sync)
        {
            var oldPorts = new HashSet<int>(_config.ProtectedPorts ?? Enumerable.Empty<int>());
            NormalizeAndSynchronizeRules(config);
            var newPorts = new HashSet<int>(config.ProtectedPorts ?? Enumerable.Empty<int>());

            // Очищаем ключи удаленных портов из дедупликатора
            var removedPorts = oldPorts.Except(newPorts).ToHashSet();
            if (removedPorts.Count > 0 && _previousConnectionKeys.Count > 0)
            {
                _previousConnectionKeys.RemoveWhere(k =>
                {
                    int arrowIdx = k.LastIndexOf("->", StringComparison.Ordinal);
                    if (arrowIdx >= 0 && int.TryParse(k.Substring(arrowIdx + 2), out var p))
                    {
                        return removedPorts.Contains(p);
                    }
                    return false;
                });
            }

            _config = config;
            SaveConfigToFile();
        }
        _logger.LogInformation("PortDefender: Конфигурация обновлена. Портов: {PortCount}, Правил: {RuleCount}, Активен: {Enabled}",
            config.ProtectedPorts?.Count ?? 0, config.Rules?.Count ?? 0, config.IsGloballyEnabled);

        // Смена правил → немедленный пересчёт режима сна (активный опрос либо 0% CPU Idle).
        _wake.Pulse();
    }

    // ───────────────────────────── Активные баны ─────────────────────────────

    /// <summary>
    /// Возвращает список всех активных банов.
    /// </summary>
    public IReadOnlyList<ActiveBanDto> GetActiveBans()
    {
        return _activeBans.Values.ToList();
    }

    /// <summary>
    /// Досрочно разблокирует указанный IP-адрес.
    /// </summary>
    public bool UnbanIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        var normalized = ip.Trim();

        _activeBans.TryRemove(normalized, out var ban);
        _windowTracker.Reset(normalized);

        try
        {
            var res = _firewall.UnblockIp(new UnblockIpRequest
            {
                Ip = normalized,
                Direction = FirewallDirection.Inbound
            });

            _logger.LogInformation("PortDefender: IP {Ip} разблокирован. Результат: {Result}",
                normalized, res.Success ? "Успешно" : res.Message);

            SaveActiveBansToFile();
            return res.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PortDefender: Ошибка при разблокировке IP {Ip}", normalized);
            return false;
        }
    }

    /// <summary>
    /// Снимает все активные блокировки Port Defender.
    /// </summary>
    public int ClearAllBans()
    {
        var bans = _activeBans.Values.ToList();
        int count = 0;
        foreach (var b in bans)
        {
            if (UnbanIp(b.Ip)) count++;
        }
        _activeBans.Clear();
        _windowTracker.Clear();
        SaveActiveBansToFile();
        return count;
    }

    /// <summary>
    /// Проверяет активные блокировки с истёкшим временем и снимает их (вызывается AutoUnbanWorker).
    /// </summary>
    public int ProcessExpiredBans()
    {
        var now = DateTime.UtcNow;
        var expiredBans = _activeBans.Values
            .Where(b => b.ExpiresAtUtc.HasValue && b.ExpiresAtUtc.Value <= now)
            .ToList();

        int unbanned = 0;
        foreach (var b in expiredBans)
        {
            _logger.LogInformation("PortDefender: Срок бана IP {Ip} истёк ({ExpiresUtc:u}). Авторазбан.",
                b.Ip, b.ExpiresAtUtc);
            if (UnbanIp(b.Ip))
            {
                unbanned++;
            }
        }

        return unbanned;
    }

    // ───────────────────────────── Обработка сетевых снимков ─────────────────────────────

    /// <summary>
    /// Обрабатывает снимок сокетов системы и выявляет аномальную частоту входящих подключений.
    /// </summary>
    public void ProcessSocketSnapshot(IReadOnlyList<TcpSocketInfo> sockets)
    {
        if (sockets == null || sockets.Count == 0) return;

        PortDefenderConfigDto cfg;
        lock (_sync)
        {
            cfg = _config;
        }

        if (!cfg.IsGloballyEnabled)
            return;

        var activeRules = GetEffectiveRules(cfg);
        if (activeRules.Count == 0)
            return;

        // Индексируем активные правила по LocalPort
        var rulesByPort = activeRules
            .Where(r => r.IsEnabled)
            .ToLookup(r => r.LocalPort);

        if (!rulesByPort.Any()) return;

        var currentConnectionKeys = new HashSet<string>(Math.Max(16, sockets.Count), StringComparer.OrdinalIgnoreCase);

        foreach (var sock in sockets)
        {
            // Фильтруем входящие TCP-соединения (ESTABLISHED, SYN_RCVD)
            if (!IsIncomingTcpState(sock.State))
                continue;

            // Ключ уникального соединения: RemoteIP:RemotePort -> LocalPort
            var connKey = $"{sock.RemoteAddress}:{sock.RemotePort}->{sock.LocalPort}";
            currentConnectionKeys.Add(connKey);

            // Если соединение уже наблюдалось на предыдущей секунде — пропускаем дедупликацию
            if (_previousConnectionKeys.Contains(connKey))
                continue;

            // Проверяем, отслеживается ли целевой локальный порт
            var matchingRules = rulesByPort[sock.LocalPort];
            if (!matchingRules.Any())
                continue;

            var remoteIp = sock.RemoteAddress;

            // Защита от блокировки локальных/специальных адресов
            if (!FirewallGuard.IsBlockableAddress(remoteIp, out _))
                continue;

            // P0: Честная проверка TrustPrivateNetworks из конфигурации.
            // Если TrustPrivateNetworks == true, ЛЮБЫЕ адреса RFC 1918
            // (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, APIPA 169.254.0.0/16,
            // link-local IPv6) НИ ПРИ КАКИХ ОБСТОЯТЕЛЬСТВАХ НЕ БАНЯТСЯ.
            if (cfg.TrustPrivateNetworks && ProcessNetworkSocket.CheckIsPrivateAddress(remoteIp))
                continue;

            // P0: Глобальный белый список (_config.WhitelistIps) имеет приоритет
            // над любыми правилами детекта.
            if (IsIpWhitelisted(remoteIp, cfg.WhitelistIps))
                continue;

            // Уже забаненный IP не обрабатываем повторно
            if (_activeBans.ContainsKey(remoteIp))
                continue;

            foreach (var rule in matchingRules)
            {
                // Если в правиле задан конкретный процесс — сопоставляем путь/имя
                if (!string.IsNullOrWhiteSpace(rule.ProcessPath))
                {
                    if (!MatchesProcess(rule.ProcessPath, sock.ProcessName, sock.ProcessPath))
                        continue;
                }

                // Проверка белого списка правила
                if (IsIpWhitelisted(remoteIp, rule.WhitelistIps))
                    continue;

                // Передаём в трекер скользящего окна
                var windowTimeSpan = GetTimeSpanForUnit(rule.WindowUnit);
                bool exceeded = _windowTracker.RecordConnectionAndCheckThreshold(
                    remoteIp,
                    rule.ThresholdCount,
                    windowTimeSpan,
                    out int currentRate);

                if (exceeded)
                {
                    TriggerBan(remoteIp, rule, currentRate, sock.ProcessName ?? sock.ProcessPath ?? "System");
                    break;
                }
            }
        }

        _previousConnectionKeys = currentConnectionKeys;
    }

    /// <summary>
    /// Перегрузка для совместимости с базовыми сокетами ProcessNetworkSocket.
    /// </summary>
    public void ProcessSocketSnapshot(IReadOnlyList<ProcessNetworkSocket> sockets)
    {
        if (sockets == null || sockets.Count == 0) return;
        var tcpList = new List<TcpSocketInfo>(sockets.Count);
        for (int i = 0; i < sockets.Count; i++)
        {
            tcpList.Add(new TcpSocketInfo(sockets[i]));
        }
        ProcessSocketSnapshot(tcpList);
    }

    // ───────────────────────────── Логика бана и эскалации ─────────────────────────────

    private void TriggerBan(string ip, PortDefenderRuleConfig rule, int attackRate, string processName)
    {
        try
        {
            // P0 defense-in-depth: повторная проверка перед фактической блокировкой,
            // т.к. конфигурация могла измениться между детектом и баном.
            PortDefenderConfigDto cfgSnapshot;
            lock (_sync)
            {
                cfgSnapshot = _config;
            }

            if (cfgSnapshot.TrustPrivateNetworks && ProcessNetworkSocket.CheckIsPrivateAddress(ip))
            {
                _logger.LogDebug("PortDefender: Бан IP {Ip} пропущен (частная сеть, TrustPrivateNetworks=true).", ip);
                return;
            }

            if (IsIpWhitelisted(ip, cfgSnapshot.WhitelistIps))
            {
                _logger.LogDebug("PortDefender: Бан IP {Ip} пропущен (глобальный WhitelistIps).", ip);
                return;
            }

            if (IsIpWhitelisted(ip, rule.WhitelistIps))
            {
                _logger.LogDebug("PortDefender: Бан IP {Ip} пропущен (whitelist правила, порт {Port}).", ip, rule.LocalPort);
                return;
            }

            var now = DateTime.UtcNow;

            // Вычисляем RepeatCount (эскалация)
            int repeatCount = 1;
            if (_repeatHistory.TryGetValue(ip, out var hist))
            {
                // Если предыдущая атака была менее 24 часов назад — инкрементируем счётчик повторов
                if ((now - hist.LastBanUtc).TotalHours <= 24)
                {
                    repeatCount = hist.RepeatCount + 1;
                }
            }
            _repeatHistory[ip] = (repeatCount, now);

            // Длительность бана
            int banMinutes = CalculateEscalatedDuration(rule.BanDurationMinutes, repeatCount, rule.AutoEscalate);
            DateTime? expiresUtc = banMinutes > 0 ? now.AddMinutes(banMinutes) : null;

            var windowUnitStr = rule.WindowUnit switch
            {
                TimeWindowUnit.Second => "сек",
                TimeWindowUnit.Minute => "мин",
                TimeWindowUnit.Hour => "час",
                _ => "окно"
            };

            var reason = $"PortDefender: флуд на порт {rule.LocalPort} ({attackRate} подкл./{windowUnitStr}, порог {rule.ThresholdCount}), рецидив: x{repeatCount}";

            // Вызов блокировки в Windows Firewall
            var blockReq = new BlockIpRequest
            {
                Ip = ip,
                Direction = FirewallDirection.Inbound,
                Aggregated = true,
                BanDurationMinutes = banMinutes,
                Reason = reason
            };

            var fwResult = _firewall.BlockIp(blockReq);
            _logger.LogWarning("PortDefender: Атака с IP {Ip} заблокирована на {Minutes}м (рецидив: {Repeat}). Результат FW: {Result}",
                ip, banMinutes, repeatCount, fwResult.Message);

            var ban = new ActiveBanDto
            {
                Ip = ip,
                TargetPort = rule.LocalPort,
                TargetProcess = processName,
                BanStartedUtc = now,
                ExpiresAtUtc = expiresUtc,
                AttackRate = attackRate,
                Reason = reason,
                RepeatCount = repeatCount
            };

            _activeBans[ip] = ban;
            SaveActiveBansToFile();

            // Сброс истории в трекере для этого IP
            _windowTracker.Reset(ip);

            // Уведомление через Named Pipe
            try
            {
                _notifications.Enqueue(new PipeMessage
                {
                    Type = PipeMessageTypes.PortDefenderAttackBlocked,
                    Payload = JsonSerializer.Serialize(ban, JsonOptions),
                    Timestamp = now
                });

                var banDurationText = banMinutes > 0 ? $"{banMinutes} мин" : "навсегда";
                _notifications.Enqueue(new PipeMessage
                {
                    Type = PipeMessageTypes.ActionNotice,
                    Payload = JsonSerializer.Serialize(new ActionNoticePayload(
                        ip,
                        $"Port Defender: заблокирован {ip} (порт {rule.LocalPort}, {attackRate} подкл./{windowUnitStr}, бан на {banDurationText})"),
                        JsonOptions),
                    Timestamp = now
                });
            }
            catch (Exception exNotification)
            {
                _logger.LogWarning(exNotification, "PortDefender: Не удалось отправить уведомление о бане {Ip}", ip);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PortDefender: Ошибка при обработке бана для IP {Ip}", ip);
        }
    }

    /// <summary>
    /// Вычисляет длительность бана с учётом эскалации при повторных атаках:
    /// 1-й бан: base (15м) -> 2-й: 30м -> 3-й: 60м (1ч) -> 4-й: 1440м (24ч) -> 5+: 0 (навсегда).
    /// </summary>
    public static int CalculateEscalatedDuration(int baseDurationMinutes, int repeatCount, bool autoEscalate)
    {
        if (!autoEscalate || baseDurationMinutes <= 0)
            return baseDurationMinutes;

        return repeatCount switch
        {
            <= 1 => baseDurationMinutes,
            2 => Math.Max(baseDurationMinutes * 2, 30),
            3 => Math.Max(baseDurationMinutes * 4, 60),
            4 => Math.Max(baseDurationMinutes * 8, 1440),
            _ => 0 // 0 = навсегда (permanent)
        };
    }

    private static TimeSpan GetTimeSpanForUnit(TimeWindowUnit unit) => unit switch
    {
        TimeWindowUnit.Second => TimeSpan.FromSeconds(1),
        TimeWindowUnit.Minute => TimeSpan.FromMinutes(1),
        TimeWindowUnit.Hour => TimeSpan.FromHours(1),
        _ => TimeSpan.FromMinutes(1)
    };

    private static bool IsIncomingTcpState(string? state)
    {
        if (string.IsNullOrEmpty(state)) return false;
        return state.Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase) ||
               state.Equals("SYN_RCVD", StringComparison.OrdinalIgnoreCase) ||
               state.Equals("SYN_RECEIVED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesProcess(string targetRulePath, string? procName, string? procPath)
    {
        if (string.IsNullOrWhiteSpace(targetRulePath)) return true;
        var ruleTrim = targetRulePath.Trim();

        if (!string.IsNullOrEmpty(procPath) &&
            string.Equals(procPath, ruleTrim, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(procName))
        {
            var ruleFileName = Path.GetFileName(ruleTrim);
            if (string.Equals(procName, ruleFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, ruleTrim, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Проверяет, входит ли адрес в белый список (точный IP, маска CIDR, wildcard).
    /// </summary>
    public static bool IsIpWhitelisted(string ip, IEnumerable<string>? whitelist)
    {
        if (whitelist == null) return false;
        foreach (var item in whitelist)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            var trimmed = item.Trim();

            // Точное совпадение
            if (string.Equals(trimmed, ip, StringComparison.OrdinalIgnoreCase))
                return true;

            // CIDR подсеть, например 192.168.1.0/24
            if (trimmed.Contains('/') && IPAddress.TryParse(ip, out var clientIp))
            {
                if (IsInSubnet(clientIp, trimmed))
                    return true;
            }

            // Префикс со звёздочкой, например 192.168.1.*
            if (trimmed.EndsWith('*'))
            {
                var prefix = trimmed[..^1];
                if (ip.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool IsInSubnet(IPAddress address, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out var baseIp)) return false;
        if (!int.TryParse(parts[1], out var prefixLength)) return false;

        var addressBytes = address.GetAddressBytes();
        var baseBytes = baseIp.GetAddressBytes();
        if (addressBytes.Length != baseBytes.Length) return false;

        int totalBits = addressBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > totalBits) return false;

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != baseBytes[i]) return false;
        }

        if (remainingBits > 0 && fullBytes < addressBytes.Length)
        {
            byte mask = (byte)(0xFF << (8 - remainingBits));
            if ((addressBytes[fullBytes] & mask) != (baseBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }

    // ───────────────────────────── Опрос и таймеры ─────────────────────────────

    // (цикл опроса — см. RunAsync/HasWork/SetUiActive выше)

    // ───────────────────────────── Персистентность файлов ─────────────────────────────

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var loaded = JsonSerializer.Deserialize<PortDefenderConfigDto>(json, JsonOptions);
                if (loaded != null)
                {
                    NormalizeAndSynchronizeRules(loaded);
                    _config = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PortDefender: Ошибка при чтении {Path}", _configFilePath);
        }

        // Значения по умолчанию
        var def = new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            DefaultBanDurationMinutes = 15,
            ProtectedPorts = new List<int> { 3389 },
            Rules = []
        };
        NormalizeAndSynchronizeRules(def);
        _config = def;
    }

    /// <summary>
    /// Нормализует список защищаемых портов и синхронизирует его со списком правил.
    /// Гарантирует, что каждый защищаемый порт имеет активное правило мониторинга сокетов,
    /// а удаленные порты исключаются из активного сканирования.
    /// </summary>
    private static void NormalizeAndSynchronizeRules(PortDefenderConfigDto config)
    {
        if (config == null) return;

        config.ProtectedPorts ??= new List<int>();
        config.Rules ??= new List<PortDefenderRuleConfig>();

        // Очищаем и нормализуем защищаемые порты (только валидные 1..65535, без дубликатов)
        var validProtectedPorts = config.ProtectedPorts
            .Where(p => p is >= 1 and <= 65535)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        // Порты из явных правил
        var rulePorts = config.Rules
            .Where(r => r.LocalPort is >= 1 and <= 65535)
            .Select(r => r.LocalPort)
            .ToHashSet();

        // Все порты из правил добавляем в ProtectedPorts
        foreach (var rp in rulePorts)
        {
            if (!validProtectedPorts.Contains(rp))
            {
                validProtectedPorts.Add(rp);
            }
        }
        validProtectedPorts.Sort();
        config.ProtectedPorts = validProtectedPorts;

        // Если для защищаемого порта нет правила — создаем дефолтное
        var existingRulePorts = config.Rules
            .Where(r => r.LocalPort is >= 1 and <= 65535)
            .Select(r => r.LocalPort)
            .ToHashSet();

        foreach (var port in config.ProtectedPorts)
        {
            if (!existingRulePorts.Contains(port))
            {
                config.Rules.Add(new PortDefenderRuleConfig
                {
                    LocalPort = port,
                    ProcessPath = null,
                    IsEnabled = true,
                    ThresholdCount = config.MaxAttempts > 0 ? config.MaxAttempts : 10,
                    WindowUnit = config.WindowUnit,
                    BanDurationMinutes = config.BanDurationMinutes > 0 ? config.BanDurationMinutes : 15,
                    AutoEscalate = config.AutoEscalate,
                    WhitelistIps = config.WhitelistIps != null ? new List<string>(config.WhitelistIps) : new()
                });
            }
        }

        // Удаляем общие правила портов (без привязки к конкретному processPath), которых больше нет в ProtectedPorts
        var protectedSet = new HashSet<int>(config.ProtectedPorts);
        config.Rules.RemoveAll(r => r.LocalPort > 0 && string.IsNullOrWhiteSpace(r.ProcessPath) && !protectedSet.Contains(r.LocalPort));
    }

    /// <summary>
    /// Возвращает эффективный список активных правил с учётом индивидуальных правил и защищаемых портов.
    /// </summary>
    private static List<PortDefenderRuleConfig> GetEffectiveRules(PortDefenderConfigDto cfg)
    {
        var result = new List<PortDefenderRuleConfig>();
        if (cfg.Rules != null)
        {
            result.AddRange(cfg.Rules.Where(r => r.IsEnabled));
        }

        if (cfg.ProtectedPorts != null && cfg.ProtectedPorts.Count > 0)
        {
            var coveredPorts = result.Select(r => r.LocalPort).ToHashSet();
            foreach (var port in cfg.ProtectedPorts)
            {
                if (port is >= 1 and <= 65535 && !coveredPorts.Contains(port))
                {
                    result.Add(new PortDefenderRuleConfig
                    {
                        LocalPort = port,
                        IsEnabled = true,
                        ThresholdCount = cfg.MaxAttempts > 0 ? cfg.MaxAttempts : 10,
                        WindowUnit = cfg.WindowUnit,
                        BanDurationMinutes = cfg.BanDurationMinutes > 0 ? cfg.BanDurationMinutes : 15,
                        AutoEscalate = cfg.AutoEscalate,
                        WhitelistIps = cfg.WhitelistIps != null ? new List<string>(cfg.WhitelistIps) : new()
                    });
                }
            }
        }

        return result;
    }

    private void SaveConfigToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, JsonOptions);
            WriteAtomic(_configFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PortDefender: Ошибка при сохранении конфигурации в {Path}", _configFilePath);
        }
    }

    private void LoadActiveBans()
    {
        try
        {
            if (File.Exists(_bansFilePath))
            {
                var json = File.ReadAllText(_bansFilePath);
                var loaded = JsonSerializer.Deserialize<List<ActiveBanDto>>(json, JsonOptions);
                if (loaded != null)
                {
                    var now = DateTime.UtcNow;
                    bool fileChanged = false;
                    foreach (var b in loaded)
                    {
                        // P0: Просроченные баны НЕ просто пропускаем — их необходимо
                        // УДАЛИТЬ из правила брандмауэра, иначе после перезагрузки
                        // остаются вечные 'зомби'-правила в Windows Firewall.
                        if (b.ExpiresAtUtc.HasValue && b.ExpiresAtUtc.Value <= now)
                        {
                            fileChanged = true;
                            try
                            {
                                var res = _firewall.UnblockIp(new UnblockIpRequest
                                {
                                    Ip = b.Ip,
                                    Direction = FirewallDirection.Both
                                });
                                _logger.LogInformation(
                                    "PortDefender: Просроченный бан IP {Ip} (истёк {ExpiresUtc:u}) удалён из firewall при старте. Результат: {Result}",
                                    b.Ip, b.ExpiresAtUtc, res.Success ? "Успешно" : res.Message);
                            }
                            catch (Exception exUnblock)
                            {
                                _logger.LogWarning(exUnblock,
                                    "PortDefender: Не удалось удалить просроченный бан IP {Ip} из firewall при старте", b.Ip);
                            }
                            continue;
                        }

                        _activeBans[b.Ip] = b;
                        _repeatHistory[b.Ip] = (b.RepeatCount, b.BanStartedUtc);
                    }

                    // Перезаписываем файл без просроченных записей, чтобы зомби-баны
                    // не воскресали при следующем старте.
                    if (fileChanged)
                        SaveActiveBansToFile();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PortDefender: Ошибка при загрузке активных банов из {Path}", _bansFilePath);
        }
    }

    private void SaveActiveBansToFile()
    {
        try
        {
            var list = _activeBans.Values.ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);
            WriteAtomic(_bansFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PortDefender: Ошибка при сохранении банов в {Path}", _bansFilePath);
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopLoop();
        _wake.Dispose();
    }
}
