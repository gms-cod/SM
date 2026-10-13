using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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

    // Фоновый периодический опрос сетевых сокетов
    private readonly Timer? _pollTimer;
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

        // Запуск периодического секундного снимка сетевых соединений
        _pollTimer = new Timer(OnPollTimerTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
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
            _config = config;
            SaveConfigToFile();
        }
        _logger.LogInformation("PortDefender: Конфигурация обновлена. Правил: {Count}, Активен: {Enabled}",
            config.Rules?.Count ?? 0, config.IsGloballyEnabled);
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

        if (!cfg.IsGloballyEnabled || cfg.Rules.Count == 0)
            return;

        // Индексируем активные правила по LocalPort
        var rulesByPort = cfg.Rules
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

    private void OnPollTimerTick(object? state)
    {
        if (_disposed) return;
        try
        {
            var sockets = _networkTracker.GetSockets();
            if (sockets != null && sockets.Count > 0)
            {
                ProcessSocketSnapshot(sockets);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PortDefender: Ошибка при периодическом сканировании сокетов");
        }
    }

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
        _config = new PortDefenderConfigDto
        {
            IsGloballyEnabled = true,
            DefaultBanDurationMinutes = 15,
            Rules = []
        };
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
                    foreach (var b in loaded)
                    {
                        // Не восстанавливаем просроченные баны
                        if (b.ExpiresAtUtc.HasValue && b.ExpiresAtUtc.Value <= now)
                            continue;

                        _activeBans[b.Ip] = b;
                        _repeatHistory[b.Ip] = (b.RepeatCount, b.BanStartedUtc);
                    }
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
        _pollTimer?.Dispose();
    }
}
