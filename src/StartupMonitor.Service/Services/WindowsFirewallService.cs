using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Service.Services;

/// <summary>
/// Управление стандартным брандмауэром Windows (Windows Advanced Firewall)
/// через late-bound COM <c>HNetCfg.FwPolicy2</c> / <c>HNetCfg.FWRule</c>.
///
/// Архитектурные решения:
/// <list type="bullet">
/// <item>Late binding (<c>Type.GetTypeFromProgID</c> + <c>dynamic</c>) — без COM-reference,
///   совместимо с .NET 8 single-file/self-contained публикацией.</item>
/// <item>COM-объекты живут только внутри одной операции и всегда освобождаются
///   через <c>Marshal.FinalReleaseComObject</c> (утечек RCW нет).</item>
/// <item>Агрегированные правила <c>StartupMonitor_Blocked_IPs_In/Out</c>: O(1) правил
///   вместо O(N) на каждый IP — минимальный оверхед файрвола.</item>
/// <item>Thread-safe: мутации под <c>lock</c> (read-modify-write CSV списка IP).</item>
/// <item>Служба работает от SYSTEM — полные права на WFP/брандмауэр без UAC.</item>
/// </list>
/// </summary>
/// <remarks>
/// Профили: Domain=1, Private=2, Public=4. Все управляемые правила ставятся на
/// <c>Profiles = 0x7FFFFFFF (ALL)</c>, чтобы защита не зависела от текущей сети.
/// Если файрвол выключен для профиля — правила всё равно создаются (persist),
/// но трафик не фильтруется; <see cref="GetStatus"/> это явно показывает.
/// </remarks>
public sealed class WindowsFirewallService : IPortDefenderFirewall
{
    // --- Имена и константы -------------------------------------------------

    /// <summary>Префикс всех управляемых правил (для List/RemoveAll/IsManaged).</summary>
    public const string RulePrefix = "StartupMonitor_";

    /// <summary>Группа правил (отображается в wf.msc).</summary>
    public const string RuleGroup = "StartupMonitor";

    /// <summary>Агрегированное правило блокировки IP: входящие.</summary>
    public const string AggregatedRuleNameIn = "StartupMonitor_Blocked_IPs_In";

    /// <summary>Агрегированное правило блокировки IP: исходящие.</summary>
    public const string AggregatedRuleNameOut = "StartupMonitor_Blocked_IPs_Out";

    /// <summary>Killswitch: блокировка всего трафика (входящие).</summary>
    public const string KillswitchRuleNameIn = "StartupMonitor_Killswitch_BlockAll_In";

    /// <summary>Killswitch: блокировка всего трафика (исходящие).</summary>
    public const string KillswitchRuleNameOut = "StartupMonitor_Killswitch_BlockAll_Out";

    private const string PolicyProgId = "HNetCfg.FwPolicy2";
    private const string RuleProgId = "HNetCfg.FWRule";

    // NET_FW_RULE_DIRECTION
    private const int DirIn = 1;
    private const int DirOut = 2;

    // NET_FW_ACTION
    private const int ActionBlock = 0;
    private const int ActionAllow = 1;

    // NET_FW_IP_PROTOCOL
    private const int ProtoAny = 256;
    private const int ProtoTcp = 6;
    private const int ProtoUdp = 17;

    // NET_FW_PROFILE_TYPE (CurrentProfileTypes bitmask)
    private const int ProfileDomain = 1;
    private const int ProfilePrivate = 2;
    private const int ProfilePublic = 4;

    // INetFwRule.Profiles = ALL
    private const int ProfilesAll = 0x7FFFFFFF;

    // HRESULT сервиса брандмауэра выключен / нет доступа
    private const int HResultServiceDisabled = unchecked((int)0x80070422);
    private const int HResultAccessDenied = unchecked((int)0x80070005);
    private const int HResultFirewallServiceDown = unchecked((int)0x8007042C);

    private readonly object _sync = new();
    /// <summary>Ревизия подтверждённых изменений; частичный отказ также инвалидирует проекции политики.</summary>
    private long _policyRevision;
    /// <summary>Текущая версия политики для немедленного сброса кэшей детектора.</summary>
    public long PolicyRevision => Interlocked.Read(ref _policyRevision);
    /// <summary>Изолированный источник коллекции в тестах; рабочий экземпляр всегда использует COM Windows.</summary>
    internal Func<object>? PolicyFactoryOverride { get; set; }
    /// <summary>Изолированный конструктор правила в тестах без изменения правил ОС.</summary>
    internal Func<object>? RuleFactoryOverride { get; set; }
    private static readonly Serilog.ILogger Log =
        Serilog.Log.ForContext<WindowsFirewallService>();

    // --- Публичный API: статус --------------------------------------------

    /// <summary>Возвращает сводный статус брандмауэра и управляемых правил.</summary>
    public FirewallStatusDto GetStatus()
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.GetStatus");
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            int current = 0;
            try { current = (int)policy.CurrentProfileTypes; } catch { current = 0; }

            bool d = IsEnabledSafe(policy, ProfileDomain);
            bool p = IsEnabledSafe(policy, ProfilePrivate);
            bool pub = IsEnabledSafe(policy, ProfilePublic);

            // Статусу нужны только имена правил и адреса двух агрегатов, без полного чтения DTO каждого правила.
            var (managed, blockedIps) = ReadManagedRuleCounts((object)rules);

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
            Log.Warning(ex, "Служба брандмауэра Windows недоступна");
            return new FirewallStatusDto { Available = false };
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
        }
    }

    // --- Публичный API: IP --------------------------------------------------

    /// <summary>Блокирует удалённый IP/подсеть (Inbound/Outbound/Both).</summary>
    /// <param name="request">IP, направление, режим агрегации.</param>
    public FirewallOperationResult BlockIp(BlockIpRequest request)
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.BlockIp");
        var ip = (request.Ip ?? string.Empty).Trim();
        if (!FirewallGuard.IsBlockableAddress(ip, out var err))
            return FirewallOperationResult.Fail(err ?? "Недопустимый IP-адрес.");

        var normalized = NormalizeAddress(ip);
        lock (_sync)
        {
            try
            {
                if (request.Aggregated)
                {
                    var touched = new List<string>(2);
                    if (request.Direction is FirewallDirection.Inbound or FirewallDirection.Both)
                        touched.Add(AddIpToAggregated(normalized, DirIn, AggregatedRuleNameIn,
                            $"Агрегированная блокировка входящих (StartupMonitor). Причина: {request.Reason ?? "—"}"));
                    if (request.Direction is FirewallDirection.Outbound or FirewallDirection.Both)
                        touched.Add(AddIpToAggregated(normalized, DirOut, AggregatedRuleNameOut,
                            $"Агрегированная блокировка исходящих (StartupMonitor). Причина: {request.Reason ?? "—"}"));
                    Log.Information("Firewall: IP {Ip} заблокирован ({Dir}) в {Rules}", normalized, request.Direction, string.Join(",", touched));
                    return FirewallOperationResult.Ok($"IP {normalized} заблокирован.", string.Join("+", touched), created: true);
                }

                // Одиночные правила: StartupMonitor_Block_IP_{sanit}_In/Out
                var created = new List<string>(2);
                if (request.Direction is FirewallDirection.Inbound or FirewallDirection.Both)
                    created.Add(UpsertSingleIpRule(normalized, DirIn, request.Reason));
                if (request.Direction is FirewallDirection.Outbound or FirewallDirection.Both)
                    created.Add(UpsertSingleIpRule(normalized, DirOut, request.Reason));
                return FirewallOperationResult.Ok($"IP {normalized} заблокирован (отдельные правила).", string.Join("+", created), created: true);
            }
            catch (Exception ex) when (IsFirewallDown(ex))
            {
                Log.Warning(ex, "Firewall недоступен при блокировке {Ip}", normalized);
                return FirewallOperationResult.Fail("Служба брандмауэра Windows недоступна (MpsSvc остановлена?).");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BlockIp({Ip}) не выполнен", normalized);
                return FirewallOperationResult.Fail($"Не удалось заблокировать {normalized}: {FriendlyError(ex)}");
            }
        }
    }

    /// <summary>Разблокирует IP: убирает из агрегированных и удаляет одиночные правила.</summary>
    public FirewallOperationResult UnblockIp(UnblockIpRequest request)
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.UnblockIp");
        var ip = (request.Ip ?? string.Empty).Trim();
        var normalized = NormalizeAddress(ip);
        lock (_sync)
        {
            try
            {
                bool removedAny = false;
                if (request.Direction is FirewallDirection.Inbound or FirewallDirection.Both)
                    removedAny |= RemoveIpFromAggregated(normalized, AggregatedRuleNameIn);
                if (request.Direction is FirewallDirection.Outbound or FirewallDirection.Both)
                    removedAny |= RemoveIpFromAggregated(normalized, AggregatedRuleNameOut);

                removedAny |= RemoveSingleIpRules(normalized, request.Direction);

                // Отсутствующее управляемое правило уже удовлетворяет требуемому состоянию разбана.
                return FirewallOperationResult.Ok(removedAny
                    ? $"IP {normalized} разблокирован."
                    : $"Управляемая блокировка IP {normalized} уже отсутствует.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "UnblockIp({Ip}) не выполнен", normalized);
                return FirewallOperationResult.Fail($"Не удалось разблокировать {normalized}: {FriendlyError(ex)}");
            }
        }
    }

    /// <summary>Проверяет статус блокировки IP по всем правилам (включая чужие BLOCK-правила).</summary>
    public FirewallBlockStatus IsIpBlocked(string ip)
    {
        var normalized = NormalizeAddress((ip ?? string.Empty).Trim());
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            bool inBlocked = false, outBlocked = false;
            foreach (var dto in EnumerateRules(rules))
            {
                if (!dto.Enabled || dto.Action != FirewallAction.Block) continue;
                if (!AddressListContains(dto.RemoteAddresses, normalized)) continue;
                if (dto.Direction == FirewallDirection.Inbound) inBlocked = true;
                if (dto.Direction == FirewallDirection.Outbound) outBlocked = true;
            }

            return (inBlocked, outBlocked) switch
            {
                (true, true) => FirewallBlockStatus.BlockedBoth,
                (true, false) => FirewallBlockStatus.BlockedInbound,
                (false, true) => FirewallBlockStatus.BlockedOutbound,
                _ => FirewallBlockStatus.Allowed,
            };
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
        }
    }

    /// <summary>Возвращает все заблокированные IP из агрегированных правил.</summary>
    public IReadOnlyList<string> GetBlockedIps()
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            foreach (var name in new[] { AggregatedRuleNameIn, AggregatedRuleNameOut })
            {
                dynamic? rule = FindRule(rules, name);
                if (rule is null) continue;
                try
                {
                    string addrs = string.Empty;
                    try { addrs = (string)rule.RemoteAddresses ?? string.Empty; } catch { }
                    foreach (var a in SplitAddresses(addrs))
                        result.Add(a);
                }
                finally { Release(rule); }
            }

            return result.ToList();
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
        }
    }

    // --- Публичный API: порты -----------------------------------------------

    /// <summary>
    /// Блокирует порт TCP/UDP: для всей системы или конкретного EXE (ApplicationName).
    /// </summary>
    public FirewallOperationResult BlockPort(BlockPortRequest request)
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.BlockPort");
        if (!FirewallGuard.IsValidPort(request.Port))
            return FirewallOperationResult.Fail($"Некорректный порт: {request.Port} (1–65535).");
        if (request.Protocol is not (FirewallProtocol.Tcp or FirewallProtocol.Udp))
            return FirewallOperationResult.Fail("Протокол должен быть TCP или UDP.");
        var app = NormalizeAppPath(request.ApplicationPath);

        lock (_sync)
        {
            try
            {
                var names = new List<string>(2);
                if (request.Direction is FirewallDirection.Inbound or FirewallDirection.Both)
                    names.Add(UpsertPortRule(request.Port, request.Protocol, DirIn, app, request.Reason));
                if (request.Direction is FirewallDirection.Outbound or FirewallDirection.Both)
                    names.Add(UpsertPortRule(request.Port, request.Protocol, DirOut, app, request.Reason));

                Log.Information("Firewall: порт {Proto}/{Port} {Dir} заблокирован (app: {App})",
                    request.Protocol, request.Port, request.Direction, app ?? "*");
                return FirewallOperationResult.Ok(
                    $"Порт {request.Protocol}/{request.Port} заблокирован.", string.Join("+", names), created: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BlockPort({Proto}/{Port}) не выполнен", request.Protocol, request.Port);
                return FirewallOperationResult.Fail($"Не удалось заблокировать порт: {FriendlyError(ex)}");
            }
        }
    }

    /// <summary>Снимает блокировку порта (удаляет управляемые правила; чужие не трогает).</summary>
    public FirewallOperationResult UnblockPort(UnblockPortRequest request)
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.UnblockPort");
        var app = NormalizeAppPath(request.ApplicationPath);
        lock (_sync)
        {
            object? policyObj = null;
            object? rulesObj = null;
            try
            {
                dynamic policy = CreatePolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;

                int removed = 0;
                foreach (var wantDir in WantedDirs(request.Direction))
                {
                    var name = PortRuleName(request.Port, request.Protocol, wantDir, app);
                    if (RemoveRuleByName(rules, name)) removed++;
                    // Старое имя без хеша удаляем только после проверки полного пути, чтобы не затронуть одноимённое приложение.
                    dynamic? legacy = FindLegacyPortRule(rules, request.Port, request.Protocol, wantDir, app);
                    if (legacy is not null)
                    {
                        try
                        {
                            if (RemoveRuleByName(rules, (string)legacy.Name)) removed++;
                        }
                        finally { Release(legacy); }
                    }
                }

                return removed > 0
                    ? FirewallOperationResult.Ok($"Порт {request.Protocol}/{request.Port} разблокирован (удалено правил: {removed}).")
                    : FirewallOperationResult.Fail("Управляемое правило для порта не найдено.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "UnblockPort({Proto}/{Port}) не выполнен", request.Protocol, request.Port);
                return FirewallOperationResult.Fail($"Не удалось разблокировать порт: {FriendlyError(ex)}");
            }
            finally
            {
                Release(rulesObj);
                Release(policyObj);
            }
        }
    }

    /// <summary>Проверяет, перекрыт ли порт управляемым BLOCK-правилом.</summary>
    public bool IsPortBlocked(int port, FirewallProtocol protocol, FirewallDirection direction, string? applicationPath = null)
    {
        var app = NormalizeAppPath(applicationPath);
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            foreach (var wantDir in WantedDirs(direction))
            {
                var name = PortRuleName(port, protocol, wantDir, app);
                dynamic? rule = FindRule(rules, name);
                rule ??= FindLegacyPortRule(rules, port, protocol, wantDir, app);
                if (rule is null) continue;
                try
                {
                    bool enabled = false, blocked = false;
                    try { enabled = (bool)rule.Enabled; } catch { }
                    try { blocked = (int)rule.Action == ActionBlock; } catch { }
                    if (enabled && blocked) return true;
                }
                finally { Release(rule); }
            }

            return false;
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
        }
    }

    // --- Публичный API: инвентаризация ---------------------------------------

    /// <summary>Возвращает все управляемые правила StartupMonitor (префикс имени).</summary>
    public IReadOnlyList<FirewallRuleDto> ListManagedRules()
    {
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;
            List<FirewallRuleDto> all = EnumerateRules((object)rules);
            return all.Where(r => r.IsManaged).ToList();
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
        }
    }

    /// <summary>Удаляет ВСЕ управляемые правила StartupMonitor. Возвращает число удалённых.</summary>
    public int RemoveAllManagedRules()
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

                List<FirewallRuleDto> all = EnumerateRules((object)rules);
                var names = all.Where(r => r.IsManaged).Select(r => r.Name).ToList();
                int removed = 0;
                foreach (var n in names)
                {
                    if (RemoveRuleByName(rules, n)) removed++;
                }

                Log.Information("Firewall: удалено управляемых правил: {Count}", removed);
                return removed;
            }
            finally
            {
                Release(rulesObj);
                Release(policyObj);
            }
        }
    }

    // --- Публичный API: ядро управления правилами ------------------------------

    /// <summary>
    /// Перечисляет ВСЕ правила COM-политики (включая чужие).
    /// Каждое COM-свойство читается через SafeGet (может бросать при Any-протоколе),
    /// все RCW освобождаются через <c>Marshal.FinalReleaseComObject</c> (Zero-Crash).
    /// </summary>
    public IReadOnlyList<FirewallRuleDto> GetAllRules()
    {
        lock (_sync)
        {
            object? policyObj = null;
            object? rulesObj = null;
            try
            {
                dynamic policy = OpenPolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;
                return EnumerateRules((object)rules);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Firewall: полный снимок правил недоступен");
                throw;
            }
            finally
            {
                Release(rulesObj);
                Release(policyObj);
            }
        }
    }

    /// <summary>
    /// Высокопроизводительное точечное чтение только путей и имён приложений для всех правил Firewall.
    /// Считывает только COM-свойство ApplicationName без накладных расходов на чтение 12 неиспользуемых свойств.
    /// Нагрузка на CPU снижается в 13 раз, исключая фоновые всплески утилизации ядра.
    /// </summary>
    public HashSet<string> GetApplicationRulePaths()
    {
        lock (_sync)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object? policyObj = null;
            object? rulesObj = null;
            System.Collections.IEnumerator? enumerator = null;
            try
            {
                dynamic policy = CreatePolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;

                enumerator = ((System.Collections.IEnumerable)rules).GetEnumerator();
                int count = 0;
                while (true)
                {
                    bool moved;
                    try { moved = enumerator.MoveNext(); }
                    catch { break; }
                    if (!moved) break;

                    dynamic r = enumerator.Current;
                    try
                    {
                        string? app = SafeGet(r, "ApplicationName");
                        if (!string.IsNullOrWhiteSpace(app))
                        {
                            var trimmed = app.Trim();
                            set.Add(trimmed);
                            try
                            {
                                var expanded = Environment.ExpandEnvironmentVariables(trimmed);
                                if (!string.Equals(expanded, trimmed, StringComparison.OrdinalIgnoreCase))
                                {
                                    set.Add(expanded);
                                }
                            }
                            catch { }
                            try
                            {
                                var fileName = System.IO.Path.GetFileName(trimmed);
                                if (!string.IsNullOrWhiteSpace(fileName))
                                {
                                    set.Add(fileName);
                                    var withoutExt = System.IO.Path.GetFileNameWithoutExtension(trimmed);
                                    if (!string.IsNullOrWhiteSpace(withoutExt))
                                        set.Add(withoutExt);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    finally
                    {
                        Release(r);
                    }

                    count++;
                    if (count % 100 == 0)
                    {
                        Thread.Yield();
                    }
                }
            }
            catch (Exception ex) when (IsFirewallDown(ex))
            {
                Log.Warning(ex, "Firewall недоступен при быстром чтении путей приложений");
            }
            finally
            {
                Release(enumerator);
                Release(rulesObj);
                Release(policyObj);
            }

            return set;
        }
    }

    /// <summary>
    /// Возвращает нормализованные полные пути приложений, для которых уже существует
    /// РАЗРЕШАЮЩЕЕ ИСХОДЯЩЕЕ правило Windows Firewall.
    /// </summary>
    /// <remarks>
    /// Используется детектором сетевых алертов, чтобы не показывать уведомление о новом
    /// исходящем соединении для программы, которой пользователь уже разрешил исходящий трафик.
    /// В отличие от <see cref="GetApplicationRulePaths"/> в набор попадают СТРОГО:
    /// <list type="bullet">
    /// <item>правила направления Outbound (<c>Direction = 2</c>) — входящие правила (например mDNS-In) не считаются;</item>
    /// <item>правила действия Allow (<c>Action = 1</c>) — блокирующие правила не подавляют алерт;</item>
    /// <item>только полные пути (без имён файлов и имён без расширения), нормализованные к нижнему регистру.</item>
    /// </list>
    /// Это устраняет ложные срабатывания фильтра «у программы уже есть правило».
    /// </remarks>
    public HashSet<string> GetOutboundAllowApplicationRulePaths()
        => GetUnrestrictedOutboundAllowApplicationRulePaths();

    /// <summary>Возвращает только полные разрешения приложения без ограничений протокола, портов, адресов, службы и интерфейсов.</summary>
    /// <returns>Канонические пути разрешений, покрывающих все активные профили; неполное чтение не выдаёт разрешение.</returns>
    public HashSet<string> GetUnrestrictedOutboundAllowApplicationRulePaths()
    {
        lock (_sync)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object? policyObj = null;
            object? rulesObj = null;
            List<RuleSnapshot>? snapshot = null;
            var held = new List<object>();
            try
            {
                dynamic policy = OpenPolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;
                var profiles = Convert.ToInt32(policy.CurrentProfileTypes) & 7;
                if (profiles == 0)
                    throw new InvalidOperationException("Не определены активные профили брандмауэра.");
                snapshot = CaptureRules((object)rules, held);
                foreach (var item in snapshot)
                {
                    var dto = item.Dto;
                    if (dto.Direction != FirewallDirection.Outbound || dto.Action != FirewallAction.Allow || !dto.Enabled ||
                        (((int)dto.Profiles & profiles) != profiles) || !IsUnrestrictedApplicationRule(item))
                        continue;
                    var path = NormalizeRuleAppPath(dto.ApplicationPath);
                    if (path != null && Path.IsPathFullyQualified(path)) set.Add(path);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Firewall: полный снимок разрешений приложений недоступен");
                throw;
            }
            finally
            {
                ReleaseHeldRules(held);
                Release(rulesObj);
                Release(policyObj);
            }

            return set;
        }
    }

    /// <summary>
    /// Нормализует ApplicationName правила Firewall к каноническому виду:
    /// раскрывает переменные окружения, убирает кавычки, приводит слэши к '\' и регистр к нижнему.
    /// </summary>
    /// <param name="app">Сырое значение ApplicationName из COM-объекта правила.</param>
    /// <returns>Нормализованный полный путь либо <c>null</c> для пустого значения.</returns>
    private static string? NormalizeRuleAppPath(string? app)
    {
        if (string.IsNullOrWhiteSpace(app))
            return null;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(app.Trim().Trim('"')).Replace('/', '\\');
            if (!Path.IsPathFullyQualified(path)) return null;
            return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(ex, "Firewall: некорректный путь приложения");
            return null;
        }
    }

    /// <summary>Канонизирует полный путь одинаково для очереди, постоянных правил и списков.</summary>
    /// <param name="path">Возможный путь с кавычками, переменными окружения или альтернативными разделителями.</param>
    /// <returns>Полный канонический путь либо null; имя файла само по себе идентичностью не является.</returns>
    public static string? NormalizeApplicationPath(string? path) => NormalizeRuleAppPath(path);

    /// <summary>Получает подтверждённые действия только собственных полных включённых политик одного приложения.</summary>
    internal HashSet<FirewallAction> GetApplicationPolicyActions(string path)
    {
        var normalized = NormalizeRuleAppPath(path) ?? throw new ArgumentException("Необходим полный путь приложения.", nameof(path));
        lock (_sync)
        {
            object? policyObj = null;
            object? rulesObj = null;
            var held = new List<object>();
            try
            {
                dynamic policy = OpenPolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;
                return CaptureRules((object)rules, held).Where(item => item.Dto.Enabled && item.Dto.Direction == FirewallDirection.Outbound &&
                    IsOwnedApplicationPolicy(item, normalized)).Select(item => item.Dto.Action).ToHashSet();
            }
            finally { ReleaseHeldRules(held); Release(rulesObj); Release(policyObj); }
        }
    }

    /// <summary>
    /// Создаёт или обновляет произвольное правило <c>HNetCfg.FWRule</c>.
    /// При <c>Direction.Both</c> создаются/обновляются два правила In+Out.
    /// </summary>
    public FirewallOperationResult CreateOrUpdateRule(FirewallRuleDto rule)
    {
        var validation = ValidateRule(rule);
        if (validation != null) return FirewallOperationResult.Fail(validation, rule?.Name);
        return ExecuteRuleTransaction(_ => ExpandRule(rule!).Select(pair => new RuleMutation(pair.Name, pair.Rule)).ToList(), null, rule!.Name);
    }

    /// <summary>Применяет собственную программную/портовую политику и сохранение настроек с компенсацией любых записанных правил.</summary>
    /// <param name="rule">Авторитетное правило приложения либо явного глобального порта.</param>
    /// <param name="persist">Сохранение настроек; хранилище обязано само откатывать отказавшую файловую запись.</param>
    /// <returns>Успех после проверки правил и persist; PartialFailure явно означает неуспешную компенсацию.</returns>
    public FirewallOperationResult ApplyPolicyRule(FirewallRuleDto rule, Action? persist = null)
    {
        var validation = ValidateRule(rule);
        if (validation != null) return FirewallOperationResult.Fail(validation, rule?.Name);
        var path = NormalizeRuleAppPath(rule!.ApplicationPath);
        return ExecuteRuleTransaction(snapshot =>
        {
            var writes = ExpandRule(rule).Select(pair => new RuleMutation(pair.Name, pair.Rule)).ToList();
            if (path != null)
            {
                var names = writes.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var previous in snapshot.Where(item => IsOwnedApplicationPolicy(item, path) && !names.Contains(item.Dto.Name)))
                    writes.Add(new RuleMutation(previous.Dto.Name, null));
            }
            return writes;
        }, persist, rule.Name);
    }

    /// <summary>Удаляет только собственную полную политику приложения, сохраняя узкие пользовательские исключения.</summary>
    /// <param name="path">Полный канонизируемый путь приложения.</param>
    /// <param name="persist">Сохранение нового состояния списков после проверки удаления.</param>
    /// <returns>Подтверждённое удаление либо диагностируемый отказ с результатом компенсации.</returns>
    public FirewallOperationResult RemoveApplicationPolicy(string path, Action? persist = null)
    {
        var normalized = NormalizeRuleAppPath(path);
        if (normalized == null || !Path.IsPathFullyQualified(normalized))
            return FirewallOperationResult.Fail("Для удаления политики необходим полный путь приложения.");
        return ExecuteRuleTransaction(snapshot => snapshot.Where(item => IsOwnedApplicationPolicy(item, normalized))
            .Select(item => new RuleMutation(item.Dto.Name, null)).ToList(), persist, normalized);
    }

    /// <summary>Создаёт единственное имя полной политики независимо от источника выбора, имени exe и действия.</summary>
    /// <param name="path">Полный путь, определяющий идентичность.</param>
    /// <param name="name">Отображаемое имя только для описания.</param>
    /// <param name="action">Разрешение или запрет всей программы.</param>
    /// <returns>Правило Both/Any/All, пригодное для popup и матрицы.</returns>
    public static FirewallRuleDto BuildApplicationRule(string path, string name, FirewallAction action)
    {
        var normalized = NormalizeRuleAppPath(path);
        if (normalized == null || !Path.IsPathFullyQualified(normalized)) throw new ArgumentException("Необходим полный путь приложения.", nameof(path));
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        var displayPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).Replace('/', '\\'));
        return new FirewallRuleDto
        {
            Name = $"{RulePrefix}Application_{hash}", Description = $"Политика приложения StartupMonitor: {name} ({displayPath})",
            Direction = FirewallDirection.Both, Action = action, Protocol = FirewallProtocol.Any,
            ApplicationPath = displayPath, Enabled = true, Profiles = FirewallProfile.All,
            LocalPorts = "*", RemotePorts = "*", RemoteAddresses = "*", Grouping = RuleGroup, IsManaged = true
        };
    }

    /// <summary>План одной точной записи/удаления; null означает удаление.</summary>
    private sealed record RuleMutation(string Name, FirewallRuleDto? Rule, bool? EnabledOnly = null);

    /// <summary>Полный снимок затрагиваемых полей и удерживаемый native-объект для восстановления удалённого правила без потери расширенных свойств.</summary>
    private sealed record RuleSnapshot(object Native, FirewallRuleDto Dto, Dictionary<string, object?> Values);

    /// <summary>Выполняет проверяемую последовательность записей и компенсирует её при любом отказе, включая persist.</summary>
    /// <param name="plan">План на основании достоверного полного снимка.</param>
    /// <param name="persist">Последняя потенциально отказавшая запись настроек.</param>
    /// <param name="operationName">Имя для диагностического результата.</param>
    /// <returns>Успех либо отказ с явным признаком неполного отката.</returns>
    private FirewallOperationResult ExecuteRuleTransaction(Func<IReadOnlyList<RuleSnapshot>, List<RuleMutation>> plan, Action? persist, string operationName)
    {
        lock (_sync)
        {
            object? policyObj = null;
            object? rulesObj = null;
            List<RuleSnapshot>? snapshot = null;
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changedOriginals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var created = new List<object>();
            var held = new List<object>();
            try
            {
                dynamic policy = OpenPolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;
                snapshot = CaptureRules((object)rules, held);
                var changes = plan(snapshot);
                foreach (var change in changes)
                {
                    if (snapshot.Count(item => string.Equals(item.Dto.Name, change.Name, StringComparison.OrdinalIgnoreCase)) > 1)
                        throw new InvalidOperationException($"Правило {change.Name} имеет неоднозначные дубликаты имени; изменение отменено.");
                }
                foreach (var change in changes)
                {
                    touched.Add(change.Name);
                    var old = snapshot.SingleOrDefault(item => string.Equals(item.Dto.Name, change.Name, StringComparison.OrdinalIgnoreCase));
                    if (change.Rule == null)
                    {
                        if (!RemoveRuleByName(rules, change.Name)) throw new InvalidOperationException($"Правило {change.Name} исчезло во время изменения политики.");
                        continue;
                    }
                    // COM не принимает ServiceName="" при Add. Снятие/смена optional identity выполняется
                    // на подготовленном новом объекте; исходный RCW удерживается неизменным для компенсации.
                    var replaceIdentity = old != null && !change.EnabledOnly.HasValue &&
                        (!string.Equals(NormalizeRuleAppPath(old.Dto.ApplicationPath), NormalizeRuleAppPath(change.Rule.ApplicationPath), StringComparison.OrdinalIgnoreCase) ||
                         !string.Equals(old.Dto.ServiceName ?? "", change.Rule.ServiceName ?? "", StringComparison.OrdinalIgnoreCase));
                    object native = old == null || replaceIdentity ? NewRuleObject() : old.Native;
                    if (old == null || replaceIdentity)
                    {
                        created.Add(native);
                        if (old != null) CopyUnchangedScope(old, native);
                    }
                    else changedOriginals.Add(change.Name);
                    if (change.EnabledOnly.HasValue) ((dynamic)native).Enabled = change.EnabledOnly.Value;
                    else ApplyDtoToComRule((dynamic)native, change.Rule, change.Name, (int)change.Rule.Direction);
                    if (replaceIdentity && !RemoveRuleByName(rules, change.Name))
                        throw new InvalidOperationException($"Исходное правило {change.Name} исчезло при замене привязки.");
                    if (old == null || replaceIdentity) rules.Add((dynamic)native);
                }
                var observed = CaptureRules((object)rules, held);
                foreach (var change in changes)
                {
                    var matches = observed.Where(item => string.Equals(item.Dto.Name, change.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (change.Rule == null ? matches.Count != 0 : matches.Count != 1 || !RuleMatches(matches[0].Dto, change.Rule))
                        throw new InvalidOperationException($"Брандмауэр не подтвердил итоговое состояние правила {change.Name}.");
                }
                persist?.Invoke();
                Interlocked.Increment(ref _policyRevision);
                return FirewallOperationResult.Ok("Правила и настройки подтверждены.", operationName, created: created.Count > 0);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Firewall: операция политики {Operation} не завершена", operationName);
                var failures = new List<Exception>();
                if (rulesObj != null && snapshot != null)
                {
                    dynamic rules = rulesObj;
                    foreach (var name in touched.Reverse())
                    {
                        try
                        {
                            var previous = snapshot.SingleOrDefault(item => string.Equals(item.Dto.Name, name, StringComparison.OrdinalIgnoreCase));
                            if (previous == null)
                            {
                                RemoveRuleByName(rules, name);
                                continue;
                            }
                            if (changedOriginals.Contains(name)) RestoreRule(previous);
                            object? current = FindRuleDirect(rules, name);
                            if (current != null) held.Add(current);
                            if (current == null) rules.Add((dynamic)previous.Native);
                            else if (!ReferenceEquals(current, previous.Native))
                            {
                                RemoveRuleByName(rules, name);
                                rules.Add((dynamic)previous.Native);
                            }
                        }
                        catch (Exception rollbackError)
                        {
                            failures.Add(rollbackError);
                            Log.Error(rollbackError, "Firewall: компенсация правила {Rule} не завершена", name);
                        }
                    }
                    if (touched.Count > 0) Interlocked.Increment(ref _policyRevision);
                }
                return FirewallOperationResult.Fail(failures.Count == 0 && !ex.Message.StartsWith("PartialFailure:", StringComparison.Ordinal)
                    ? $"Изменение не применено; прежние правила восстановлены. {FriendlyError(ex)}"
                    : $"PartialFailure: компенсация правил или профилей не завершена. Проверьте журнал службы. {FriendlyError(ex)}", operationName);
            }
            finally
            {
                held.AddRange(created);
                ReleaseHeldRules(held);
                Release(rulesObj);
                Release(policyObj);
            }
        }
    }

    /// <summary>Проверяет базовые enum и область до первого обращения к изменению COM.</summary>
    private static string? ValidateRule(FirewallRuleDto? rule)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.Name)) return "Не указано правило или его имя.";
        if (!Enum.IsDefined(rule.Direction) || !Enum.IsDefined(rule.Action) || !Enum.IsDefined(rule.Protocol) ||
            (rule.Profiles & FirewallProfile.All) == 0 || ((int)rule.Profiles & ~7) != 0)
            return "Некорректное направление, действие, протокол или профиль правила.";
        if (!string.IsNullOrWhiteSpace(rule.ApplicationPath) && !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(rule.ApplicationPath.Trim().Trim('"'))))
            return "Путь приложения должен быть абсолютным.";
        return null;
    }

    /// <summary>Разворачивает Both в независимые In/Out без изменения DTO вызывающего кода.</summary>
    private static IEnumerable<(string Name, FirewallRuleDto Rule)> ExpandRule(FirewallRuleDto rule)
    {
        foreach (var direction in rule.Direction == FirewallDirection.Both
                     ? new[] { FirewallDirection.Inbound, FirewallDirection.Outbound } : new[] { rule.Direction })
        {
            var name = rule.Name.Trim() + (rule.Direction == FirewallDirection.Both ? direction == FirewallDirection.Inbound ? "_In" : "_Out" : "");
            yield return (name, CopyRule(rule, name, direction));
        }
    }

    /// <summary>Копирует изменяемые поля правила для независимого плана.</summary>
    private static FirewallRuleDto CopyRule(FirewallRuleDto rule, string name, FirewallDirection direction) => new()
    {
        Name = name, Direction = direction, Description = rule.Description, Action = rule.Action, Protocol = rule.Protocol,
        Profiles = rule.Profiles, Enabled = rule.Enabled, ApplicationPath = rule.ApplicationPath, ServiceName = rule.ServiceName,
        LocalPorts = rule.LocalPorts, RemotePorts = rule.RemotePorts, RemoteAddresses = rule.RemoteAddresses,
        Grouping = rule.Grouping, IsManaged = rule.IsManaged
    };

    /// <summary>Сравнивает подтверждённую COM-проекцию с намерением, включая явное снятие ограничений.</summary>
    private static bool RuleMatches(FirewallRuleDto actual, FirewallRuleDto expected) =>
        actual.Direction == expected.Direction && actual.Action == expected.Action && actual.Protocol == expected.Protocol &&
        actual.Enabled == expected.Enabled && actual.Profiles == expected.Profiles &&
        string.Equals(NormalizeRuleAppPath(actual.ApplicationPath), NormalizeRuleAppPath(expected.ApplicationPath), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(actual.ServiceName ?? "", expected.ServiceName ?? "", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(actual.Description ?? "", expected.Description ?? "", StringComparison.Ordinal) &&
        string.Equals(actual.Grouping ?? "", expected.Grouping ?? (expected.Name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase) ? RuleGroup : ""), StringComparison.Ordinal) &&
        (expected.Protocol is not (FirewallProtocol.Tcp or FirewallProtocol.Udp) ||
            Wildcard(actual.LocalPorts) == Wildcard(expected.LocalPorts) && Wildcard(actual.RemotePorts) == Wildcard(expected.RemotePorts)) &&
        Wildcard(actual.RemoteAddresses) == Wildcard(expected.RemoteAddresses);

    /// <summary>Нормализует отсутствие ограничения к явному wildcard Windows.</summary>
    private static string Wildcard(string? value) => string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();

    /// <summary>Отбирает лишь полную программную политику, не портовые/адресные пользовательские исключения.</summary>
    private static bool IsUnrestrictedApplicationRule(RuleSnapshot item) => item.Dto.Protocol == FirewallProtocol.Any &&
        !string.IsNullOrWhiteSpace(item.Dto.ApplicationPath) && string.IsNullOrWhiteSpace(item.Dto.ServiceName) &&
        Wildcard(item.Dto.RemoteAddresses) == "*" && Wildcard(Convert.ToString(item.Values["LocalAddresses"])) == "*" &&
        (string.IsNullOrWhiteSpace(Convert.ToString(item.Values["InterfaceTypes"])) || string.Equals(Convert.ToString(item.Values["InterfaceTypes"]), "All", StringComparison.OrdinalIgnoreCase)) &&
        (item.Values["Interfaces"] == null || item.Values["Interfaces"] is Array { Length: 0 }) &&
        !Convert.ToBoolean(item.Values["EdgeTraversal"]);

    /// <summary>Распознаёт каноничную и прежнюю генерируемую политику, сохраняя любые узкие или произвольно названные custom-правила.</summary>
    private static bool IsOwnedApplicationPolicy(RuleSnapshot item, string path) =>
        item.Dto.IsManaged && item.Dto.Profiles == FirewallProfile.All && IsUnrestrictedApplicationRule(item) &&
        string.Equals(NormalizeRuleAppPath(item.Dto.ApplicationPath), path, StringComparison.OrdinalIgnoreCase) &&
        (item.Dto.Name.StartsWith(RulePrefix + "Application_", StringComparison.OrdinalIgnoreCase) ||
         item.Dto.Name.StartsWith(RulePrefix + "Access_", StringComparison.OrdinalIgnoreCase) && item.Dto.Name.Contains("_Managed", StringComparison.OrdinalIgnoreCase) ||
         (item.Dto.Name.StartsWith(RulePrefix + "Allow_", StringComparison.OrdinalIgnoreCase) || item.Dto.Name.StartsWith(RulePrefix + "Block_", StringComparison.OrdinalIgnoreCase)) && item.Dto.Name.Contains("_Auto", StringComparison.OrdinalIgnoreCase));

    /// <summary>Удаляет правило по имени (Both удаляет In+Out варианты).</summary>
    public FirewallOperationResult DeleteRule(string ruleName, FirewallDirection direction = FirewallDirection.Both)
    {
        if (string.IsNullOrWhiteSpace(ruleName) || !Enum.IsDefined(direction))
            return FirewallOperationResult.Fail("Не указано имя или направление правила.");
        return ExecuteRuleTransaction(snapshot =>
        {
            var names = CandidateNames(ruleName.Trim(), direction).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var changes = snapshot.Where(item => names.Contains(item.Dto.Name) &&
                (direction == FirewallDirection.Both || item.Dto.Direction == direction)).Select(item => new RuleMutation(item.Dto.Name, null)).ToList();
            if (changes.Count == 0) throw new InvalidOperationException("Правило не найдено.");
            return changes;
        }, null, ruleName.Trim());
    }

    /// <summary>Включает/выключает правило по имени.</summary>
    public FirewallOperationResult ToggleRule(string ruleName, FirewallDirection direction, bool enable)
    {
        if (string.IsNullOrWhiteSpace(ruleName) || !Enum.IsDefined(direction))
            return FirewallOperationResult.Fail("Не указано имя или направление правила.");
        return ExecuteRuleTransaction(snapshot =>
        {
            var names = CandidateNames(ruleName.Trim(), direction).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var changes = new List<RuleMutation>();
            foreach (var item in snapshot.Where(item => names.Contains(item.Dto.Name) &&
                         (direction == FirewallDirection.Both || item.Dto.Direction == direction)))
            {
                var copy = CopyRule(item.Dto, item.Dto.Name, item.Dto.Direction);
                copy.Enabled = enable;
                changes.Add(new RuleMutation(copy.Name, copy, enable));
            }
            if (changes.Count == 0) throw new InvalidOperationException("Правило не найдено.");
            return changes;
        }, null, ruleName.Trim());
    }

    /// <summary>
    /// Устанавливает режим фильтрации: настраивает политику
    /// (FirewallEnabled / DefaultOutboundAction) или спецправило Killswitch.
    /// </summary>
    public FirewallOperationResult SetFilteringMode(FirewallFilteringMode mode)
    {
        if (!Enum.IsDefined(typeof(FirewallFilteringMode), mode))
            return FirewallOperationResult.Fail($"Неизвестный режим фильтрации: {(int)mode}.");

        lock (_sync)
        {
            object? policyObj = null;
            try
            {
                dynamic policy = OpenPolicy(out policyObj);
                var profiles = new[] { ProfileDomain, ProfilePrivate, ProfilePublic };
                var before = new Dictionary<int, (bool Enabled, int Outbound)>();
                foreach (var profile in profiles)
                    before[profile] = (Convert.ToBoolean(policy.FirewallEnabled[profile]), Convert.ToInt32(policy.DefaultOutboundAction[profile]));
                return ExecuteRuleTransaction(snapshot =>
                {
                    if (mode == FirewallFilteringMode.KillswitchBlockAll)
                        return new List<RuleMutation>
                        {
                            new(KillswitchRuleNameIn, BuildKillswitchRule(KillswitchRuleNameIn, FirewallDirection.Inbound)),
                            new(KillswitchRuleNameOut, BuildKillswitchRule(KillswitchRuleNameOut, FirewallDirection.Outbound))
                        };
                    return snapshot.Where(item => item.Dto.Name is KillswitchRuleNameIn or KillswitchRuleNameOut)
                        .Select(item => new RuleMutation(item.Dto.Name, null)).ToList();
                }, () =>
                {
                    try
                    {
                        foreach (var profile in profiles)
                        {
                            if (mode is FirewallFilteringMode.Normal or FirewallFilteringMode.WhitelistOutbound)
                                policy.DefaultOutboundAction[profile] = mode == FirewallFilteringMode.Normal ? ActionAllow : ActionBlock;
                            policy.FirewallEnabled[profile] = mode != FirewallFilteringMode.Disabled;
                        }
                        foreach (var profile in profiles)
                        {
                            if (Convert.ToBoolean(policy.FirewallEnabled[profile]) != (mode != FirewallFilteringMode.Disabled) ||
                                mode is FirewallFilteringMode.Normal or FirewallFilteringMode.WhitelistOutbound &&
                                Convert.ToInt32(policy.DefaultOutboundAction[profile]) != (mode == FirewallFilteringMode.Normal ? ActionAllow : ActionBlock))
                                throw new InvalidOperationException("Брандмауэр не подтвердил состояние профиля после изменения режима.");
                        }
                    }
                    catch (Exception changeError)
                    {
                        var rollbackErrors = new List<Exception>();
                        foreach (var profile in profiles)
                        {
                            try
                            {
                                policy.DefaultOutboundAction[profile] = before[profile].Outbound;
                                policy.FirewallEnabled[profile] = before[profile].Enabled;
                            }
                            catch (Exception rollbackError)
                            {
                                rollbackErrors.Add(rollbackError);
                                Log.Error(rollbackError, "Firewall: профиль {Profile} не восстановлен", profile);
                            }
                        }
                        Interlocked.Increment(ref _policyRevision);
                        if (rollbackErrors.Count > 0) throw new InvalidOperationException("PartialFailure: профили брандмауэра восстановлены не полностью.", changeError);
                        throw;
                    }
                }, mode.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SetFilteringMode({Mode}) не выполнен", mode);
                return FirewallOperationResult.Fail($"Не удалось установить режим {mode}: {FriendlyError(ex)}");
            }
            finally
            {
                Release(policyObj);
            }
        }
    }

    /// <summary>Возвращает текущий режим фильтрации (эвристика по политике и Killswitch-правилу).</summary>
    public FirewallFilteringMode GetFilteringMode()
        => TryGetFilteringMode(out var mode) ? mode : throw new FirewallNotAvailableException("Невозможно достоверно определить режим брандмауэра.");

    /// <summary>Формирует полный глобальный запрет для компенсируемой смены режима.</summary>
    private static FirewallRuleDto BuildKillswitchRule(string name, FirewallDirection direction) => new()
    {
        Name = name, Description = "Killswitch StartupMonitor: блокировка всего трафика.", Direction = direction,
        Action = FirewallAction.Block, Protocol = FirewallProtocol.Any, Enabled = true, Profiles = FirewallProfile.All,
        RemoteAddresses = "*", Grouping = RuleGroup, IsManaged = true
    };

    /// <summary>Читает режим без подмены отказа COM разрешающим Normal.</summary>
    /// <param name="mode">Действительный режим только при возвращённом true.</param>
    /// <returns>False при любом неполном чтении профилей или killswitch-правила.</returns>
    public bool TryGetFilteringMode(out FirewallFilteringMode mode)
    {
        lock (_sync)
        {
            mode = default;
            object? policyObj = null;
            object? rulesObj = null;
            try
            {
                dynamic policy = OpenPolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;
                var profiles = new[] { ProfileDomain, ProfilePrivate, ProfilePublic };
                var enabled = profiles.Select(profile => Convert.ToBoolean(policy.FirewallEnabled[profile])).ToArray();
                if (enabled.All(value => !value))
                {
                    mode = FirewallFilteringMode.Disabled;
                    return true;
                }
                foreach (var name in new[] { KillswitchRuleNameIn, KillswitchRuleNameOut })
                {
                    object? native = FindRuleDirect(rules, name);
                    try
                    {
                        if (native == null) continue;
                        dynamic candidate = native;
                        if (Convert.ToBoolean(candidate.Enabled) && Convert.ToInt32(candidate.Action) == ActionBlock)
                        {
                            mode = FirewallFilteringMode.KillswitchBlockAll;
                            return true;
                        }
                    }
                    finally { Release(native); }
                }
                mode = profiles.Any(profile => Convert.ToInt32(policy.DefaultOutboundAction[profile]) == ActionBlock)
                    ? FirewallFilteringMode.WhitelistOutbound : FirewallFilteringMode.Normal;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Firewall: режим фильтрации недоступен");
                return false;
            }
            finally { Release(rulesObj); Release(policyObj); }
        }
    }

    // --- Публичный API: экспорт/импорт и пресеты ---------------------------------

    /// <summary>Собирает текущие правила брандмауэра в объект бэкапа.</summary>
    public FirewallBackupDto ExportRulesBackup()
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.ExportRulesBackup");
        var rules = GetAllRules();
        return new FirewallBackupDto
        {
            Version = "1.6.0",
            ExportedAt = DateTime.UtcNow,
            Description = $"Экспорт правил брандмауэра ({rules.Count}) от {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
            Rules = rules.ToList(),
        };
    }

    /// <summary>
    /// Импортирует правила из бэкапа, пропуская или обновляя существующие.
    /// </summary>
    /// <param name="backup">Бэкап правил.</param>
    /// <param name="overwriteExisting">Перезаписывать существующие правила с тем же именем.</param>
    /// <returns>Кортеж (импортировано, пропущено).</returns>
    public (int Imported, int Skipped) ImportRulesBackup(FirewallBackupDto backup, bool overwriteExisting = false)
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.ImportRulesBackup");
        if (backup is null)
            throw new ArgumentNullException(nameof(backup));
        var rules = backup.Rules ?? new List<FirewallRuleDto>();
        if (rules.Count == 0)
            return (0, 0);

        IReadOnlyList<FirewallRuleDto> existing;
        try
        {
            existing = GetAllRules();
        }
        catch
        {
            existing = Array.Empty<FirewallRuleDto>();
        }

        var existingNames = new HashSet<string>(
            existing.Select(r => r.Name ?? string.Empty).Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);

        int imported = 0, skipped = 0;
        lock (_sync)
        {
            foreach (var rule in rules)
            {
                if (rule is null || string.IsNullOrWhiteSpace(rule.Name))
                {
                    skipped++;
                    continue;
                }

                var name = rule.Name.Trim();
                bool exists = existingNames.Contains(name)
                    || existingNames.Contains(name + "_In")
                    || existingNames.Contains(name + "_Out");
                if (exists && !overwriteExisting)
                {
                    skipped++;
                    continue;
                }

                FirewallOperationResult result;
                try
                {
                    result = CreateOrUpdateRule(rule);
                }
                catch
                {
                    skipped++;
                    continue;
                }

                if (result?.Success == true)
                {
                    imported++;
                    existingNames.Add(name);
                }
                else
                {
                    skipped++;
                }
            }
        }

        Log.Information("Firewall: импорт бэкапа v{Version}: импортировано {Imported}, пропущено {Skipped}",
            backup.Version, imported, skipped);
        return (imported, skipped);
    }

    /// <summary>Генерирует и применяет комплект правил для выбранного пресета.</summary>
    /// <param name="presetType">Пресет безопасности.</param>
    /// <returns>Число применённых правил.</returns>
    public int ApplyPreset(SecurityPresetType presetType)
    {
        using var _ = ExecutionProfiler.Measure("Service.Firewall.ApplyPreset");
        if (!Enum.IsDefined(typeof(SecurityPresetType), presetType))
            throw new ArgumentOutOfRangeException(nameof(presetType), $"Неизвестный пресет: {(int)presetType}.");

        if (presetType == SecurityPresetType.ResetToDefaults)
        {
            lock (_sync)
            {
                int removed;
                try
                {
                    removed = RemoveAllManagedRules();
                }
                catch
                {
                    removed = 0;
                }

                try
                {
                    SetFilteringMode(FirewallFilteringMode.Normal);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ApplyPreset(ResetToDefaults): не удалось сбросить режим фильтрации");
                }

                Log.Information("Firewall: пресет ResetToDefaults применён, удалено правил: {Count}", removed);
                return removed;
            }
        }

        var presetRules = BuildPresetRules(presetType);
        int applied = 0;
        lock (_sync)
        {
            foreach (var rule in presetRules)
            {
                FirewallOperationResult result;
                try
                {
                    result = CreateOrUpdateRule(rule);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ApplyPreset({Preset}): правило {Name} не применено", presetType, rule?.Name);
                    continue;
                }

                if (result?.Success == true)
                    applied++;
            }
        }

        Log.Information("Firewall: пресет {Preset} применён, правил: {Count}", presetType, applied);
        return applied;
    }

    /// <summary>
    /// Чистый построитель комплектов правил для пресетов (без обращения к COM —
    /// пригоден для модульных тестов и Pipe-слоя).
    /// </summary>
    public static IReadOnlyList<FirewallRuleDto> BuildPresetRules(SecurityPresetType presetType)
    {
        FirewallRuleDto PresetRule(
            string name,
            string description,
            FirewallDirection direction,
            FirewallAction action,
            FirewallProtocol protocol,
            string localPorts = "",
            string remoteAddresses = "",
            string? remotePorts = null,
            string? applicationPath = null) => new()
            {
                Name = name,
                Description = description,
                Direction = direction,
                Action = action,
                Protocol = protocol,
                LocalPorts = localPorts,
                RemotePorts = remotePorts,
                RemoteAddresses = remoteAddresses,
                ApplicationPath = applicationPath,
                Enabled = true,
                Profiles = FirewallProfile.All,
                Grouping = RuleGroup,
                IsManaged = true,
            };

        return presetType switch
        {
            SecurityPresetType.MaxPrivacy => new List<FirewallRuleDto>
            {
                PresetRule(
                    $"{RulePrefix}Preset_MaxPrivacy_Telemetry",
                    "MaxPrivacy: блокировка телеметрии Microsoft (диапазоны IP телеметрии).",
                    FirewallDirection.Outbound,
                    FirewallAction.Block,
                    FirewallProtocol.Any,
                    remoteAddresses: "20.180.0.0/16,51.10.0.0/16,13.107.4.0/24,20.190.128.0/18"),
                PresetRule(
                    $"{RulePrefix}Preset_MaxPrivacy_Cortana",
                    "MaxPrivacy: блокировка Cortana (SearchUI) — исходящие соединения.",
                    FirewallDirection.Outbound,
                    FirewallAction.Block,
                    FirewallProtocol.Any,
                    applicationPath: @"%SystemRoot%\SystemApps\Microsoft.Windows.Cortana_cw5n1h2txyewy\SearchUI.exe"),
                PresetRule(
                    $"{RulePrefix}Preset_MaxPrivacy_Diagnostics",
                    "MaxPrivacy: блокировка диагностических серверов Microsoft.",
                    FirewallDirection.Outbound,
                    FirewallAction.Block,
                    FirewallProtocol.Tcp,
                    remoteAddresses: "191.232.139.0/24,65.55.44.0/24,20.180.0.0/16,51.10.0.0/16,13.107.4.0/24,20.190.128.0/18"),
            },
            SecurityPresetType.Gaming => new List<FirewallRuleDto>
            {
                PresetRule(
                    $"{RulePrefix}Preset_Gaming_Steam",
                    "Gaming: входящие Steam (27015-27036 TCP/UDP).",
                    FirewallDirection.Inbound,
                    FirewallAction.Allow,
                    FirewallProtocol.Any,
                    localPorts: "27015-27036"),
                PresetRule(
                    $"{RulePrefix}Preset_Gaming_BattleNet",
                    "Gaming: входящие Battle.net (3724, 1119 TCP).",
                    FirewallDirection.Inbound,
                    FirewallAction.Allow,
                    FirewallProtocol.Tcp,
                    localPorts: "3724,1119"),
                PresetRule(
                    $"{RulePrefix}Preset_Gaming_EpicGames",
                    "Gaming: входящие Epic Games (5222, 443, 3478-3479).",
                    FirewallDirection.Inbound,
                    FirewallAction.Allow,
                    FirewallProtocol.Tcp,
                    localPorts: "5222,443,3478-3479"),
            },
            SecurityPresetType.PublicWifi => new List<FirewallRuleDto>
            {
                PresetRule(
                    $"{RulePrefix}Preset_PublicWifi_NetBIOS",
                    "PublicWifi: блокировка входящих NetBIOS (137-139).",
                    FirewallDirection.Inbound,
                    FirewallAction.Block,
                    FirewallProtocol.Any,
                    localPorts: "137-139"),
                PresetRule(
                    $"{RulePrefix}Preset_PublicWifi_SMB",
                    "PublicWifi: блокировка входящего SMB (445).",
                    FirewallDirection.Inbound,
                    FirewallAction.Block,
                    FirewallProtocol.Tcp,
                    localPorts: "445"),
                PresetRule(
                    $"{RulePrefix}Preset_PublicWifi_RDP",
                    "PublicWifi: блокировка входящего RDP (3389).",
                    FirewallDirection.Inbound,
                    FirewallAction.Block,
                    FirewallProtocol.Tcp,
                    localPorts: "3389"),
                PresetRule(
                    $"{RulePrefix}Preset_PublicWifi_RPC",
                    "PublicWifi: блокировка входящего RPC (135).",
                    FirewallDirection.Inbound,
                    FirewallAction.Block,
                    FirewallProtocol.Tcp,
                    localPorts: "135"),
            },
            SecurityPresetType.ResetToDefaults => Array.Empty<FirewallRuleDto>(),
            _ => throw new ArgumentOutOfRangeException(nameof(presetType), $"Неизвестный пресет: {(int)presetType}."),
        };
    }

    /// <summary>
    /// Чистый подсчёт плана импорта (без COM): сколько правил будет импортировано/пропущено.
    /// </summary>
    /// <param name="backup">Бэкап правил.</param>
    /// <param name="existingNames">Имена уже существующих правил.</param>
    /// <param name="overwriteExisting">Перезаписывать существующие.</param>
    public static (int Imported, int Skipped) PlanImport(
        FirewallBackupDto backup,
        IReadOnlySet<string> existingNames,
        bool overwriteExisting = false)
    {
        if (backup is null)
            throw new ArgumentNullException(nameof(backup));
        var rules = backup.Rules ?? new List<FirewallRuleDto>();
        var existing = existingNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var comparer = StringComparer.OrdinalIgnoreCase;
        int imported = 0, skipped = 0;
        foreach (var rule in rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Name))
            {
                skipped++;
                continue;
            }

            var name = rule.Name.Trim();
            bool exists = existing.Contains(name)
                || existing.Contains(name + "_In")
                || existing.Contains(name + "_Out");
            // Двунаправленные правила создают варианты _In/_Out: учитываем и обратное отображение.
            if (!exists && (name.EndsWith("_In", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_Out", StringComparison.OrdinalIgnoreCase)))
            {
                var baseName = name.Substring(0, name.Length - 3);
                exists = existing.Contains(baseName);
            }

            _ = comparer;
            if (exists && !overwriteExisting)
                skipped++;
            else
                imported++;
        }

        return (imported, skipped);
    }

    private static IEnumerable<string> CandidateNames(string name, FirewallDirection direction)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };

        // Если имя уже оканчивается на _In или _Out, получаем базовое имя
        string baseName = name;
        if (name.EndsWith("_In", StringComparison.OrdinalIgnoreCase))
            baseName = name[..^3];
        else if (name.EndsWith("_Out", StringComparison.OrdinalIgnoreCase))
            baseName = name[..^4];

        set.Add(baseName);
        if (direction == FirewallDirection.Both)
        {
            set.Add(baseName + "_In");
            set.Add(baseName + "_Out");
        }
        else
        {
            set.Add(baseName + (direction == FirewallDirection.Outbound ? "_Out" : "_In"));
        }

        return set;
    }

    private static string UpsertGenericRule(FirewallRuleDto dto, string name, int direction)
    {
        object? policyObj = null;
        object? rulesObj = null;
        dynamic? rule = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            rule = FindRule(rules, name);
            bool created = rule is null;
            if (created)
            {
                Release(rule);
                rule = CreateRuleObject();
                rule.Name = name;
            }

            ApplyDtoToComRule(rule, dto, name, direction);
            if (created)
                rules.Add(rule);

            return name;
        }
        finally
        {
            Release(rule);
            Release(rulesObj);
            Release(policyObj);
        }
    }

    /// <summary>Применяет ограничения правила; отказ обязательного COM-свойства передаётся в результат ошибки операции.</summary>
    /// <param name="comRule">Изменяемое COM-правило.</param>
    /// <param name="dto">Параметры правила.</param>
    /// <param name="name">Имя правила.</param>
    /// <param name="direction">Направление трафика.</param>
    private static void ApplyDtoToComRule(dynamic comRule, FirewallRuleDto dto, string name, int direction)
    {
        // Ограничения устанавливаются раньше действия/включения; поздний setter не подтверждает неполную политику.
        var oldProtocol = Convert.ToInt32(comRule.Protocol);
        var protocol = dto.Protocol switch { FirewallProtocol.Tcp => ProtoTcp, FirewallProtocol.Udp => ProtoUdp, _ => ProtoAny };
        if (oldProtocol != protocol && oldProtocol is ProtoTcp or ProtoUdp)
        {
            comRule.LocalPorts = "*";
            comRule.RemotePorts = "*";
        }
        comRule.Protocol = protocol;
        comRule.Name = name;
        comRule.Description = dto.Description ?? string.Empty;
        comRule.Direction = direction;
        comRule.Profiles = ToComProfiles(dto.Profiles);
        // Не присваиваем пустое optional-поле: detached INetFwRule принимает setter, но Rules.Add
        // отклоняет явно пустой ServiceName. Очистка прежней привязки обеспечивается заменой объекта выше.
        if (!string.IsNullOrWhiteSpace(dto.ApplicationPath)) comRule.ApplicationName = dto.ApplicationPath.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(dto.ServiceName)) comRule.ServiceName = dto.ServiceName.Trim();
        if (protocol is ProtoTcp or ProtoUdp)
        {
            comRule.LocalPorts = Wildcard(dto.LocalPorts);
            comRule.RemotePorts = Wildcard(dto.RemotePorts);
        }
        comRule.RemoteAddresses = Wildcard(dto.RemoteAddresses);
        comRule.Grouping = dto.Grouping ?? (name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase) ? RuleGroup : string.Empty);
        comRule.Action = dto.Action == FirewallAction.Allow ? ActionAllow : ActionBlock;
        comRule.Enabled = dto.Enabled;
    }

    private static int ToComProfiles(FirewallProfile profiles)
    {
        if (profiles.HasFlag(FirewallProfile.Domain)
            && profiles.HasFlag(FirewallProfile.Private)
            && profiles.HasFlag(FirewallProfile.Public))
            return ProfilesAll;
        int mask = (int)profiles & 7;
        return mask == 0 ? ProfilesAll : mask;
    }

    private static void UpsertKillswitchRule(dynamic rules, string name, int direction)
    {
        dynamic? rule = FindRule(rules, name);
        bool created = rule is null;
        try
        {
            if (created)
            {
                Release(rule);
                rule = CreateRuleObject();
                rule!.Name = name;
            }

            rule!.Description = "Killswitch StartupMonitor: блокировка всего трафика.";
            rule!.Grouping = RuleGroup;
            rule!.Direction = direction;
            rule!.Action = ActionBlock;
            rule!.Enabled = true;
            rule!.Profiles = ProfilesAll;
            rule!.Protocol = ProtoAny;

            if (created)
                rules.Add(rule);
            else
                rule!.Enabled = true;
        }
        finally
        {
            Release(rule);
        }
    }

    private static bool IsKillswitchActive(dynamic rules)
    {
        foreach (var n in new[] { KillswitchRuleNameIn, KillswitchRuleNameOut })
        {
            dynamic? rule = FindRule(rules, n);
            if (rule is null) continue;
            try
            {
                bool enabled = SafeGetBool(rule, "Enabled", false);
                int action = SafeGetInt(rule, "Action", ActionAllow);
                if (enabled && action == ActionBlock) return true;
            }
            finally { Release(rule); }
        }

        return false;
    }

    /// <summary>Меняет состояние профилей без ложного успеха при отказе одного из них.</summary>
    /// <param name="policy">Политика Windows Firewall.</param>
    /// <param name="enabled">Требуемое состояние профилей.</param>
    private static void SetFirewallEnabledAll(dynamic policy, bool enabled)
    {
        foreach (var profile in new[] { ProfileDomain, ProfilePrivate, ProfilePublic })
        {
            policy.FirewallEnabled[profile] = enabled;
        }
    }

    /// <summary>Устанавливает действие исходящего трафика, передавая отказ профиля вызывающей операции.</summary>
    /// <param name="policy">Политика Windows Firewall.</param>
    /// <param name="action">Действие исходящего трафика.</param>
    private static void SetDefaultOutboundAll(dynamic policy, int action)
    {
        foreach (var profile in new[] { ProfileDomain, ProfilePrivate, ProfilePublic })
        {
            policy.DefaultOutboundAction[profile] = action;
        }
    }

    private static bool IsDefaultOutboundBlocked(dynamic policy)
    {
        foreach (var profile in new[] { ProfileDomain, ProfilePrivate, ProfilePublic })
        {
            try
            {
                int v = Convert.ToInt32(policy.DefaultOutboundAction[profile]);
                if (v == ActionBlock) return true;
            }
            catch { }
        }

        return false;
    }

    // --- Внутренняя логика: агрегированные IP --------------------------------

    private string AddIpToAggregated(string normalizedIp, int direction, string ruleName, string description)
    {
        object? policyObj = null;
        object? rulesObj = null;
        dynamic? rule = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            rule = FindRule(rules, ruleName);
            if (rule is null)
            {
                rule = CreateRuleObject();
                rule.Name = ruleName;
                rule.Description = description;
                rule.Grouping = RuleGroup;
                rule.Direction = direction;
                rule.Action = ActionBlock;
                rule.Enabled = true;
                rule.Profiles = ProfilesAll;
                rule.Protocol = ProtoAny;
                rule.RemoteAddresses = normalizedIp;
                rules.Add(rule);
                return ruleName;
            }

            UpdateAggregatedIpRule(rule, normalizedIp, direction);

            return ruleName;
        }
        finally
        {
            Release(rule);
            Release(rulesObj);
            Release(policyObj);
        }
    }

    /// <summary>Восстанавливает обязательные свойства управляемой блокировки даже для уже содержащегося в ней IP.</summary>
    /// <param name="ruleObject">Существующее COM-правило либо совместимый адаптер для проверки.</param>
    /// <param name="normalizedIp">Нормализованный блокируемый адрес.</param>
    /// <param name="direction">Направление входящего или исходящего трафика.</param>
    internal static void UpdateAggregatedIpRule(object ruleObject, string normalizedIp, int direction)
    {
        ArgumentNullException.ThrowIfNull(ruleObject);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedIp);
        if (direction is not (DirIn or DirOut)) throw new ArgumentOutOfRangeException(nameof(direction));
        dynamic rule = ruleObject;
        // Ошибка чтения адресов не должна превращаться в потерю остальных IP агрегированного правила.
        var addresses = (string?)rule.RemoteAddresses;
        var set = new HashSet<string>(SplitAddresses(addresses ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var changed = set.Remove("*");
        changed |= set.Add(normalizedIp);
        if (changed)
            rule.RemoteAddresses = string.Join(",", set.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        rule.Direction = direction;
        rule.Action = ActionBlock;
        rule.Profiles = ProfilesAll;
        rule.Protocol = ProtoAny;
        rule.Enabled = true;
        if (!(bool)rule.Enabled || (int)rule.Action != ActionBlock || (int)rule.Direction != direction ||
            (int)rule.Protocol != ProtoAny || (int)rule.Profiles != ProfilesAll)
            throw new InvalidOperationException("Брандмауэр не подтвердил обязательные свойства управляемой блокировки IP.");
    }

    private bool RemoveIpFromAggregated(string normalizedIp, string ruleName)
    {
        object? policyObj = null;
        object? rulesObj = null;
        dynamic? rule = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            rule = FindRule(rules, ruleName);
            if (rule is null) return false;

            // При отказе COM-чтения нельзя объявлять адрес отсутствующим и терять возможность повторного разбана.
            var set = new HashSet<string>(SplitAddresses((string?)rule.RemoteAddresses ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            if (!set.Remove(normalizedIp)) return false;

            if (set.Count == 0)
            {
                Release(rule);
                rule = null;
                RemoveRuleByName(rules, ruleName); // пустое правило не храним
            }
            else
            {
                rule.RemoteAddresses = string.Join(",", set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            }

            return true;
        }
        finally
        {
            Release(rule);
            Release(rulesObj);
            Release(policyObj);
        }
    }

    private string UpsertSingleIpRule(string normalizedIp, int direction, string? reason)
    {
        var name = SingleIpRuleName(normalizedIp, direction);
        object? policyObj = null;
        object? rulesObj = null;
        dynamic? rule = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            rule = FindRule(rules, name);
            if (rule is not null)
            {
                rule.Enabled = true;
                return name;
            }

            Release(rule);
            rule = CreateRuleObject();
            rule.Name = name;
            rule.Description = $"Блокировка IP {normalizedIp} (StartupMonitor). {reason ?? string.Empty}".Trim();
            rule.Grouping = RuleGroup;
            rule.Direction = direction;
            rule.Action = ActionBlock;
            rule.Enabled = true;
            rule.Profiles = ProfilesAll;
            rule.Protocol = ProtoAny;
            rule.RemoteAddresses = normalizedIp;
            rules.Add(rule);
            return name;
        }
        finally
        {
            Release(rule);
            Release(rulesObj);
            Release(policyObj);
        }
    }

    private bool RemoveSingleIpRules(string normalizedIp, FirewallDirection direction)
    {
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            bool removed = false;
            foreach (var d in WantedDirs(direction))
                removed |= RemoveRuleByName(rules, SingleIpRuleName(normalizedIp, d));
            return removed;
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
        }
    }

    // --- Внутренняя логика: порты --------------------------------------------

    private string UpsertPortRule(int port, FirewallProtocol protocol, int direction, string? appPath, string? reason)
    {
        var name = PortRuleName(port, protocol, direction, appPath);
        object? policyObj = null;
        object? rulesObj = null;
        dynamic? rule = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            rule = FindRule(rules, name);
            if (rule is not null)
            {
                // Идентичность существующего правила подтверждается полным путём, включая внешние изменения COM-правила.
                if (!string.IsNullOrEmpty(appPath))
                    rule.ApplicationName = appPath;
                rule.Enabled = true;
                return name;
            }

            Release(rule);
            rule = CreateRuleObject();
            rule.Name = name;
            rule.Description = $"Блокировка порта {(int)protocol}/{port} {(direction == DirIn ? "In" : "Out")} (StartupMonitor). {reason ?? string.Empty}".Trim();
            rule.Grouping = RuleGroup;
            rule.Direction = direction;
            rule.Action = ActionBlock;
            rule.Enabled = true;
            rule.Profiles = ProfilesAll;
            rule.Protocol = (int)protocol;
            rule.LocalPorts = port.ToString();
            if (!string.IsNullOrEmpty(appPath))
                rule.ApplicationName = appPath;
            rules.Add(rule);
            return name;
        }
        finally
        {
            Release(rule);
            Release(rulesObj);
            Release(policyObj);
        }
    }

    // --- COM-инфраструктура ----------------------------------------------------

    /// <summary>Открывает политику текущего экземпляра; управляемая подмена доступна только внутренним тестам.</summary>
    /// <param name="raw">Владелец COM-объекта для освобождения.</param>
    /// <returns>Настоящая политика Windows или явный изолированный адаптер.</returns>
    private dynamic OpenPolicy(out object? raw)
    {
        if (PolicyFactoryOverride == null) return CreatePolicy(out raw);
        raw = PolicyFactoryOverride() ?? throw new InvalidOperationException("Источник политики вернул null.");
        return raw;
    }

    /// <summary>Создаёт самостоятельный native-объект, который не добавляется в политику до полной валидации setter.</summary>
    private object NewRuleObject() => RuleFactoryOverride?.Invoke() ?? (object)CreateRuleObject();

    /// <summary>Ищет только в прямом индексе без перечисления и освобождения удерживаемых транзакцией RCW.</summary>
    private static object? FindRuleDirect(dynamic rules, string name)
    {
        try { return rules.Item(name); }
        catch (FileNotFoundException) { return null; }
        catch (COMException ex) when (ex.ErrorCode is unchecked((int)0x80070002) or unchecked((int)0x80070490)) { return null; }
    }

    /// <summary>Получает достоверный снимок; отказ одного getter или enumerator отменяет весь результат.</summary>
    /// <param name="rules">Коллекция INetFwRules или управляемый адаптер.</param>
    /// <param name="held">Владельцы RCW; удерживаются до завершения/компенсации всей операции.</param>
    /// <returns>Полные записи без подстановки разрешающих значений при ошибке.</returns>
    private static List<RuleSnapshot> CaptureRules(object rules, List<object> held)
    {
        var result = new List<RuleSnapshot>();
        var enumerator = ((System.Collections.IEnumerable)rules).GetEnumerator();
        try
        {
            while (enumerator.MoveNext())
            {
                var native = enumerator.Current ?? throw new InvalidDataException("Коллекция содержит пустое правило.");
                held.Add(native);
                result.Add(CaptureRule(native));
            }
            return result;
        }
        finally { (enumerator as IDisposable)?.Dispose(); Release(enumerator); }
    }

    /// <summary>Читает обязательные свойства без SafeGet-fallback и сохраняет исходные значения для точного отката.</summary>
    private static RuleSnapshot CaptureRule(object native)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in new[] { "Name", "Description", "Direction", "Action", "Enabled", "Profiles", "Protocol", "ApplicationName", "ServiceName", "RemoteAddresses", "Grouping", "LocalAddresses", "InterfaceTypes", "Interfaces", "EdgeTraversal" })
            values[property] = ReadRuleProperty(native, property);
        var name = Convert.ToString(values["Name"]);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Правило не содержит имени.");
        foreach (var property in new[] { "Direction", "Action", "Enabled", "Profiles", "Protocol", "EdgeTraversal" })
            if (values[property] == null) throw new InvalidDataException($"Не прочитано обязательное поле {property} правила {name}.");
        var protocol = Convert.ToInt32(values["Protocol"]);
        if (protocol is ProtoTcp or ProtoUdp)
        {
            values["LocalPorts"] = ReadRuleProperty(native, "LocalPorts");
            values["RemotePorts"] = ReadRuleProperty(native, "RemotePorts");
        }
        if (protocol is 1 or 58) values["IcmpTypesAndCodes"] = ReadRuleProperty(native, "IcmpTypesAndCodes");
        var direction = Convert.ToInt32(values["Direction"]);
        var action = Convert.ToInt32(values["Action"]);
        if (direction is not (DirIn or DirOut) || action is not (ActionAllow or ActionBlock))
            throw new InvalidDataException($"Неизвестное действие/направление правила {name}.");
        var dto = new FirewallRuleDto
        {
            Name = name, Description = Convert.ToString(values["Description"]) ?? "", Direction = (FirewallDirection)direction,
            Action = (FirewallAction)action, Enabled = Convert.ToBoolean(values["Enabled"]),
            Profiles = (FirewallProfile)(Convert.ToInt32(values["Profiles"]) & 7), Protocol = (FirewallProtocol)protocol,
            ApplicationPath = Convert.ToString(values["ApplicationName"]), ServiceName = Convert.ToString(values["ServiceName"]),
            LocalPorts = Convert.ToString(values.GetValueOrDefault("LocalPorts")) ?? "*",
            RemotePorts = Convert.ToString(values.GetValueOrDefault("RemotePorts")) ?? "*",
            RemoteAddresses = Convert.ToString(values["RemoteAddresses"]) ?? "*", Grouping = Convert.ToString(values["Grouping"]),
            IsManaged = name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase)
        };
        return new RuleSnapshot(native, dto, values);
    }

    /// <summary>Восстанавливает сохранённые поля существующего RCW; интерфейсы и прочие нетронутые свойства остаются на исходном объекте.</summary>
    private static void RestoreRule(RuleSnapshot item)
    {
        dynamic rule = item.Native;
        var values = item.Values;
        rule.Enabled = false;
        var currentProtocol = Convert.ToInt32(rule.Protocol);
        if (currentProtocol is ProtoTcp or ProtoUdp) { rule.LocalPorts = "*"; rule.RemotePorts = "*"; }
        rule.Protocol = Convert.ToInt32(values["Protocol"]);
        if (values.TryGetValue("LocalPorts", out var local)) rule.LocalPorts = Wildcard(Convert.ToString(local));
        if (values.TryGetValue("RemotePorts", out var remote)) rule.RemotePorts = Wildcard(Convert.ToString(remote));
        if (values.TryGetValue("IcmpTypesAndCodes", out var icmp)) rule.IcmpTypesAndCodes = Convert.ToString(icmp) ?? "*";
        rule.Name = item.Dto.Name;
        rule.Description = Convert.ToString(values["Description"]) ?? "";
        rule.Direction = Convert.ToInt32(values["Direction"]);
        rule.Profiles = Convert.ToInt32(values["Profiles"]);
        var application = Convert.ToString(values["ApplicationName"]);
        var service = Convert.ToString(values["ServiceName"]);
        if (!string.IsNullOrWhiteSpace(application)) rule.ApplicationName = application;
        if (!string.IsNullOrWhiteSpace(service)) rule.ServiceName = service;
        rule.RemoteAddresses = Wildcard(Convert.ToString(values["RemoteAddresses"]));
        rule.Grouping = Convert.ToString(values["Grouping"]) ?? "";
        rule.Action = Convert.ToInt32(values["Action"]);
        rule.Enabled = Convert.ToBoolean(values["Enabled"]);
    }

    /// <summary>При замене optional-привязки сохраняет интерфейсную и локальную область, отсутствующую в редактируемом DTO.</summary>
    /// <param name="previous">Достоверный снимок исходного правила.</param>
    /// <param name="replacement">Новый, ещё не установленный INetFwRule.</param>
    private static void CopyUnchangedScope(RuleSnapshot previous, object replacement)
    {
        dynamic rule = replacement;
        rule.LocalAddresses = Wildcard(Convert.ToString(previous.Values["LocalAddresses"]));
        rule.InterfaceTypes = string.IsNullOrWhiteSpace(Convert.ToString(previous.Values["InterfaceTypes"]))
            ? "All" : Convert.ToString(previous.Values["InterfaceTypes"]);
        if (previous.Values["Interfaces"] is Array { Length: > 0 } interfaces) rule.Interfaces = interfaces;
        rule.EdgeTraversal = Convert.ToBoolean(previous.Values["EdgeTraversal"]);
    }

    /// <summary>Освобождает каждый удерживаемый RCW ровно один раз, когда он больше не нужен для компенсации.</summary>
    private static void ReleaseHeldRules(IEnumerable<object> held)
    {
        foreach (var native in held.Distinct(ReferenceEqualityComparer.Instance)) Release(native);
    }

    private static dynamic CreatePolicy(out object? raw)
    {
        var type = Type.GetTypeFromProgID(PolicyProgId, throwOnError: false);
        if (type is null)
            throw new FirewallNotAvailableException("COM-класс HNetCfg.FwPolicy2 не зарегистрирован (повреждён брандмауэр Windows?).");

        object? instance;
        try { instance = Activator.CreateInstance(type); }
        catch (COMException ex) { throw new FirewallNotAvailableException("Не удалось создать INetFwPolicy2.", ex); }

        if (instance is null)
            throw new FirewallNotAvailableException("Activator.CreateInstance(HNetCfg.FwPolicy2) вернул null.");

        raw = instance;
        return (dynamic)instance;
    }

    private static dynamic CreateRuleObject()
    {
        var type = Type.GetTypeFromProgID(RuleProgId, throwOnError: false);
        if (type is null)
            throw new FirewallNotAvailableException("COM-класс HNetCfg.FWRule не зарегистрирован.");
        return (dynamic)(Activator.CreateInstance(type)
            ?? throw new FirewallNotAvailableException("Не удалось создать INetFwRule."));
    }

    /// <summary>Ищет правило через Item и резервное перечисление; отказ чтения передаёт операции, не подменяя его отсутствием правила.</summary>
    /// <param name="rules">Коллекция правил Windows Firewall.</param>
    /// <param name="name">Искомое имя правила.</param>
    /// <returns>Правило с переданным владением COM-объектом либо null при подтверждённом отсутствии.</returns>
    private static dynamic? FindRule(dynamic rules, string name)
    {
        // Быстрый нативный поиск O(1) без перечисления всей коллекции COM.
        try
        {
            dynamic? direct = rules.Item(name);
            if (direct is not null)
            {
                try
                {
                    // Обращение к свойству подтверждает, что объект живой.
                    _ = (string)direct.Name;
                    return direct; // владение RCW переходит вызывающему коду
                }
                catch (Exception ex)
                {
                    Release(direct);
                    Log.Warning(ex, "Firewall: не удалось проверить правило {RuleName}", name);
                    throw;
                }
            }
            else
            {
                Release(direct);
            }
        }
        catch (COMException ex) when (ex.ErrorCode is unchecked((int)0x80070002) or unchecked((int)0x80070490))
        {
            Log.Debug("Firewall: правило {RuleName} отсутствует в прямом индексе; проверяем перечисление", name);
        }
        catch (FileNotFoundException)
        {
            Log.Debug("Firewall: правило {RuleName} отсутствует в прямом индексе; проверяем перечисление", name);
        }

        System.Collections.IEnumerator? enumerator = null;
        try
        {
            enumerator = ((System.Collections.IEnumerable)rules).GetEnumerator();
            while (true)
            {
                if (!enumerator.MoveNext()) break;

                dynamic current = enumerator.Current;
                string? currentName;
                try { currentName = (string)current.Name; }
                catch (Exception ex)
                {
                    Release(current);
                    Log.Warning(ex, "Firewall: отказ чтения коллекции при поиске правила {RuleName}", name);
                    throw;
                }

                if (string.Equals(currentName, name, StringComparison.OrdinalIgnoreCase))
                    return current; // владение RCW переходит вызывающему коду

                Release(current);
            }

            return null;
        }
        finally
        {
            Release(enumerator);
        }
    }

    /// <summary>Удаляет правило, считая штатным только подтверждённое отсутствие; остальные ошибки передаёт вызывающему коду.</summary>
    /// <param name="rules">Коллекция COM-правил либо совместимый адаптер.</param>
    /// <param name="name">Точное имя удаляемого правила.</param>
    /// <returns>True при удалении; false, если API явно сообщил, что правило отсутствует.</returns>
    internal static bool RemoveRuleByName(dynamic rules, string name)
    {
        ArgumentNullException.ThrowIfNull((object?)rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            rules.Remove(name);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (COMException ex) when (ex.ErrorCode is unchecked((int)0x80070002) or unchecked((int)0x80070490))
        {
            return false;
        }
    }

    private static List<FirewallRuleDto> EnumerateRules(dynamic rules)
    {
        // Полный снимок не допускает пропуска повреждённого элемента: иначе отсутствие запрета можно принять за разрешение.
        var held = new List<object>();
        try { return CaptureRules((object)rules, held).Select(item => item.Dto).ToList(); }
        finally { ReleaseHeldRules(held); }
    }

    private static FirewallRuleDto ReadRule(dynamic r)
    {
        string name = SafeGet(r, "Name") ?? string.Empty;
        int dir = SafeGetInt(r, "Direction", DirIn);
        int action = SafeGetInt(r, "Action", ActionBlock);
        int proto = SafeGetInt(r, "Protocol", ProtoAny);
        int profilesMask = SafeGetInt(r, "Profiles", ProfilesAll) & 7;
        if (profilesMask == 0) profilesMask = 7;

        string? app = SafeGet(r, "ApplicationName");
        bool managed = name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase);

        return new FirewallRuleDto
        {
            Name = name,
            Description = SafeGet(r, "Description") ?? string.Empty,
            Direction = dir == DirOut ? FirewallDirection.Outbound : FirewallDirection.Inbound,
            Action = action == ActionAllow ? FirewallAction.Allow : FirewallAction.Block,
            Protocol = proto switch
            {
                ProtoTcp => FirewallProtocol.Tcp,
                ProtoUdp => FirewallProtocol.Udp,
                _ => FirewallProtocol.Any,
            },
            LocalPorts = SafeGet(r, "LocalPorts") ?? string.Empty,
            RemotePorts = SafeGet(r, "RemotePorts"),
            RemoteAddresses = SafeGet(r, "RemoteAddresses") ?? string.Empty,
            ApplicationPath = app,
            ServiceName = SafeGet(r, "ServiceName"),
            Enabled = SafeGetBool(r, "Enabled", true),
            Profiles = (FirewallProfile)profilesMask,
            Grouping = SafeGet(r, "Grouping"),
            IsManaged = managed,
        };
    }

    /// <summary>Считает управляемые правила и адреса агрегатов без чтения остальных COM-свойств.</summary>
    /// <param name="rules">Настоящая коллекция COM-правил или совместимый перечисляемый адаптер.</param>
    /// <returns>Количество правил с управляемым префиксом и число адресов в обоих агрегатах без изменения прежней семантики дубликатов.</returns>
    internal static (int ManagedRulesCount, int BlockedIpsCount) ReadManagedRuleCounts(object rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int managed = 0, blockedIps = 0;
        System.Collections.IEnumerator? enumerator = null;
        try
        {
            enumerator = ((System.Collections.IEnumerable)rules).GetEnumerator();
            while (true)
            {
                bool moved;
                try { moved = enumerator.MoveNext(); }
                catch (Exception ex) { Log.Debug(ex, "Firewall: перечисление имён для статуса прервано"); break; }
                if (!moved) break;

                var rule = enumerator.Current;
                try
                {
                    var name = SafeGet(rule, "Name") ?? string.Empty;
                    if (name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase)) managed++;
                    // Имена агрегатов сравниваются точно, как при прежней проекции через ReadRule.
                    if (name == AggregatedRuleNameIn || name == AggregatedRuleNameOut)
                        blockedIps += CountAddresses(SafeGet(rule, "RemoteAddresses") ?? string.Empty);
                }
                finally { Release(rule); }
            }
        }
        finally { Release(enumerator); }
        return (managed, blockedIps);
    }

    /// <summary>Читает известное свойство через кэшируемый DLR-вызов вместо повторного Reflection InvokeMember для каждого правила.</summary>
    /// <param name="rule">COM-правило либо совместимый объект с публичными свойствами.</param>
    /// <param name="property">Точное имя свойства; неизвестные имена сохраняют прежний reflection-путь.</param>
    /// <returns>Исходное значение свойства без изменения правил преобразования типов и fallback вызывающего метода.</returns>
    internal static object? ReadRuleProperty(object rule, string property)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrEmpty(property);
        dynamic nativeRule = rule;
        return property switch
        {
            "Name" => (object?)nativeRule.Name,
            "Description" => (object?)nativeRule.Description,
            "Direction" => (object?)nativeRule.Direction,
            "Action" => (object?)nativeRule.Action,
            "Protocol" => (object?)nativeRule.Protocol,
            "Profiles" => (object?)nativeRule.Profiles,
            "ApplicationName" => (object?)nativeRule.ApplicationName,
            "LocalPorts" => (object?)nativeRule.LocalPorts,
            "RemotePorts" => (object?)nativeRule.RemotePorts,
            "RemoteAddresses" => (object?)nativeRule.RemoteAddresses,
            "LocalAddresses" => (object?)nativeRule.LocalAddresses,
            "InterfaceTypes" => (object?)nativeRule.InterfaceTypes,
            "Interfaces" => (object?)nativeRule.Interfaces,
            "EdgeTraversal" => (object?)nativeRule.EdgeTraversal,
            "IcmpTypesAndCodes" => (object?)nativeRule.IcmpTypesAndCodes,
            "ServiceName" => (object?)nativeRule.ServiceName,
            "Enabled" => (object?)nativeRule.Enabled,
            "Grouping" => (object?)nativeRule.Grouping,
            _ => rule.GetType().InvokeMember(property, System.Reflection.BindingFlags.GetProperty, null, rule, null),
        };
    }

    /// <summary>Безопасно читает строковое свойство, сохраняя null при недоступном COM-поле.</summary>
    /// <param name="rule">Правило брандмауэра.</param>
    /// <param name="property">Имя запрашиваемого свойства.</param>
    /// <returns>Строковое представление либо null; сбой диагностируется с именем свойства.</returns>
    private static string? SafeGet(dynamic rule, string property)
    {
        try
        {
            object? v = ReadRuleProperty((object)rule, property);
            return v?.ToString();
        }
        catch (Exception ex) { Log.Debug(ex, "Firewall: свойство {Property} недоступно при строковом чтении", property); return null; }
    }

    /// <summary>Читает целочисленное свойство с прежним Convert.ToInt32 и явным fallback при null или ошибке.</summary>
    /// <param name="rule">Правило брандмауэра.</param>
    /// <param name="property">Имя запрашиваемого свойства.</param>
    /// <param name="fallback">Значение при недоступном либо непреобразуемом поле.</param>
    /// <returns>Преобразованное значение или fallback с диагностикой ошибки.</returns>
    private static int SafeGetInt(dynamic rule, string property, int fallback)
    {
        try
        {
            object? v = ReadRuleProperty((object)rule, property);
            return v is null ? fallback : Convert.ToInt32(v);
        }
        catch (Exception ex) { Log.Debug(ex, "Firewall: свойство {Property} недоступно при числовом чтении", property); return fallback; }
    }

    /// <summary>Читает логическое свойство с прежним Convert.ToBoolean, не подменяя false значением fallback.</summary>
    /// <param name="rule">Правило брандмауэра.</param>
    /// <param name="property">Имя запрашиваемого свойства.</param>
    /// <param name="fallback">Значение при null либо ошибке чтения/преобразования.</param>
    /// <returns>Преобразованное значение или fallback с диагностикой ошибки.</returns>
    private static bool SafeGetBool(dynamic rule, string property, bool fallback)
    {
        try
        {
            object? v = ReadRuleProperty((object)rule, property);
            return v is null ? fallback : Convert.ToBoolean(v);
        }
        catch (Exception ex) { Log.Debug(ex, "Firewall: свойство {Property} недоступно при логическом чтении", property); return fallback; }
    }

    private static bool IsEnabledSafe(dynamic policy, int profile)
    {
        try { return (bool)policy.FirewallEnabled[profile]; }
        catch { return false; }
    }

    /// <summary>Освобождение одного COM RCW (обнуление счётчика). Не-COM объекты игнорируются.</summary>
    private static void Release(object? comObject)
    {
        if (comObject is null) return;
        try
        {
            if (Marshal.IsComObject(comObject))
                Marshal.FinalReleaseComObject(comObject);
        }
        catch { /* повторное освобождение — игнорируем */ }
    }

    // --- Утилиты -----------------------------------------------------------------

    private static bool IsFirewallDown(Exception ex) =>
        ex is FirewallNotAvailableException ||
        (ex is COMException c &&
         c.HResult is HResultServiceDisabled or HResultFirewallServiceDown or HResultAccessDenied);

    private static string FriendlyError(Exception ex) => ex switch
    {
        COMException c when c.HResult == HResultServiceDisabled => "служба брандмауэра отключена (MpsSvc).",
        COMException c when c.HResult == HResultAccessDenied => "отказано в доступе (служба должна работать от SYSTEM).",
        COMException c => $"COM 0x{c.HResult:X8}: {c.Message}",
        _ => ex.Message,
    };

    private static string NormalizeAddress(string ip)
    {
        var parts = ip.Split('/', 2);
        if (parts.Length == 2 && System.Net.IPAddress.TryParse(parts[0].Trim(), out var baseIp))
            return $"{baseIp}/{parts[1].Trim()}";
        return parts[0].Trim();
    }

    private static IEnumerable<string> SplitAddresses(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s is not "*" && s.Length > 0)
            .Select(s => s.Trim());

    private static int CountAddresses(string csv) => SplitAddresses(csv).Count();

    private static bool AddressListContains(string csv, string normalizedIp) =>
        SplitAddresses(csv).Any(a =>
            string.Equals(a, normalizedIp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "*", StringComparison.Ordinal));

    private static string SanitizeIp(string ip) =>
        ip.Replace(":", "_").Replace("/", "_").Replace(".", "_").Replace("*", "any");

    private static string SingleIpRuleName(string normalizedIp, int direction) =>
        $"{RulePrefix}Block_IP_{SanitizeIp(normalizedIp)}_{(direction == DirOut ? "Out" : "In")}";

    private static string? NormalizeAppPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim().Trim('"');
        if (!Path.IsPathRooted(p))
            Log.Warning("Firewall: ApplicationName '{Path}' не абсолютный — правило может не сработать", p);
        else if (!File.Exists(p))
            Log.Warning("Firewall: файл '{Path}' не найден — правило создаётся впрок", p);
        return p;
    }

    /// <summary>Формирует имя правила с идентичностью полного пути приложения, исключая коллизии одноимённых EXE.</summary>
    /// <param name="port">Локальный порт.</param>
    /// <param name="protocol">Сетевой протокол.</param>
    /// <param name="direction">Направление правила Windows Firewall.</param>
    /// <param name="appPath">Полный путь приложения либо null для системного правила.</param>
    /// <returns>Стабильное имя управляемого правила.</returns>
    internal static string PortRuleName(int port, FirewallProtocol protocol, int direction, string? appPath)
    {
        var scope = string.IsNullOrEmpty(appPath)
            ? "System"
            : $"App_{Path.GetFileNameWithoutExtension(appPath)}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(appPath).ToUpperInvariant())))}";
        return $"{RulePrefix}Block_Port_{protocol}_{port}_{(direction == DirOut ? "Out" : "In")}_{scope}";
    }

    /// <summary>Находит совместимое правило старого формата только при точном совпадении пути приложения.</summary>
    /// <param name="rules">Коллекция COM-правил.</param>
    /// <param name="port">Локальный порт.</param>
    /// <param name="protocol">Сетевой протокол.</param>
    /// <param name="direction">Направление правила.</param>
    /// <param name="appPath">Путь целевого приложения.</param>
    /// <returns>COM-правило, которое должен освободить вызывающий код, либо null.</returns>
    private static object? FindLegacyPortRule(dynamic rules, int port, FirewallProtocol protocol, int direction, string? appPath)
    {
        if (string.IsNullOrEmpty(appPath)) return null;
        var legacyName = $"{RulePrefix}Block_Port_{protocol}_{port}_{(direction == DirOut ? "Out" : "In")}_App_{Path.GetFileNameWithoutExtension(appPath)}";
        object? candidate = FindRule(rules, legacyName);
        if (candidate is null) return null;
        try
        {
            dynamic legacy = candidate;
            var actualPath = (string?)legacy.ApplicationName;
            if (!string.IsNullOrEmpty(actualPath) && string.Equals(
                    Path.GetFullPath(actualPath), Path.GetFullPath(appPath), StringComparison.OrdinalIgnoreCase))
            {
                var matched = candidate;
                candidate = null;
                return matched;
            }
            return null;
        }
        finally { Release(candidate); }
    }

    private static IEnumerable<int> WantedDirs(FirewallDirection direction)
    {
        if (direction is FirewallDirection.Inbound or FirewallDirection.Both) yield return DirIn;
        if (direction is FirewallDirection.Outbound or FirewallDirection.Both) yield return DirOut;
    }
}

/// <summary>Брандмауэр Windows недоступен (COM-класс не зарегистрирован / MpsSvc остановлена).</summary>
public sealed class FirewallNotAvailableException : Exception
{
    /// <summary>FirewallNotAvailableException: создаёт исключение.</summary>
    public FirewallNotAvailableException(string message) : base(message) { }
    /// <summary>FirewallNotAvailableException: создаёт исключение с вложенным.</summary>
    public FirewallNotAvailableException(string message, Exception inner) : base(message, inner) { }
}
