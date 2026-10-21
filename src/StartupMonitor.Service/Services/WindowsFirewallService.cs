using System.Runtime.InteropServices;
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
public sealed class WindowsFirewallService
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

            int managed = 0, blockedIps = 0;
            foreach (var dto in EnumerateRules(rules))
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

                return removedAny
                    ? FirewallOperationResult.Ok($"IP {normalized} разблокирован.")
                    : FirewallOperationResult.Fail($"IP {normalized} не найден в правилах StartupMonitor.");
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
                                var fileName = System.IO.Path.GetFileName(trimmed);
                                if (!string.IsNullOrWhiteSpace(fileName))
                                {
                                    set.Add(fileName);
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
    /// Создаёт или обновляет произвольное правило <c>HNetCfg.FWRule</c>.
    /// При <c>Direction.Both</c> создаются/обновляются два правила In+Out.
    /// </summary>
    public FirewallOperationResult CreateOrUpdateRule(FirewallRuleDto rule)
    {
        if (rule is null)
            return FirewallOperationResult.Fail("Пустое правило.");
        if (string.IsNullOrWhiteSpace(rule.Name))
            return FirewallOperationResult.Fail("Пустое имя правила.");

        lock (_sync)
        {
            try
            {
                var targets = rule.Direction == FirewallDirection.Both
                    ? new[] { FirewallDirection.Inbound, FirewallDirection.Outbound }
                    : new[] { rule.Direction is FirewallDirection.Outbound ? FirewallDirection.Outbound : FirewallDirection.Inbound };

                var written = new List<string>(targets.Length);
                foreach (var dir in targets)
                {
                    var name = targets.Length == 1 ? rule.Name.Trim() : rule.Name.Trim() + (dir == FirewallDirection.Outbound ? "_Out" : "_In");
                    written.Add(UpsertGenericRule(rule, name, dir == FirewallDirection.Outbound ? DirOut : DirIn));
                }

                Log.Information("Firewall: правило {Name} создано/обновлено ({Dir})", rule.Name, rule.Direction);
                return FirewallOperationResult.Ok($"Правило {rule.Name} сохранено.", string.Join("+", written), created: true);
            }
            catch (Exception ex) when (IsFirewallDown(ex))
            {
                return FirewallOperationResult.Fail("Служба брандмауэра Windows недоступна (MpsSvc остановлена?).", rule.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CreateOrUpdateRule({Name}) не выполнен", rule.Name);
                return FirewallOperationResult.Fail($"Не удалось сохранить правило {rule.Name}: {FriendlyError(ex)}", rule.Name);
            }
        }
    }

    /// <summary>Удаляет правило по имени (Both удаляет In+Out варианты).</summary>
    public FirewallOperationResult DeleteRule(string ruleName, FirewallDirection direction = FirewallDirection.Both)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            return FirewallOperationResult.Fail("Пустое имя правила.");

        var name = ruleName.Trim();
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
                foreach (var candidate in CandidateNames(name, direction))
                {
                    if (direction is FirewallDirection.Inbound or FirewallDirection.Outbound)
                    {
                        dynamic? found = FindRule(rules, candidate);
                        if (found is null) continue;
                        try
                        {
                            int dir = SafeGetInt(found, "Direction", DirIn);
                            bool match = direction == FirewallDirection.Inbound ? dir == DirIn : dir == DirOut;
                            if (!match) continue;
                        }
                        finally { Release(found); }
                    }

                    if (RemoveRuleByName(rules, candidate)) removed++;
                }

                return removed > 0
                    ? FirewallOperationResult.Ok($"Правило {name} удалено (удалено: {removed}).", name)
                    : FirewallOperationResult.Fail($"Правило {name} не найдено.", name);
            }
            catch (Exception ex) when (IsFirewallDown(ex))
            {
                return FirewallOperationResult.Fail("Служба брандмауэра Windows недоступна (MpsSvc остановлена?).", name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DeleteRule({Name}) не выполнен", name);
                return FirewallOperationResult.Fail($"Не удалось удалить правило {name}: {FriendlyError(ex)}", name);
            }
            finally
            {
                Release(rulesObj);
                Release(policyObj);
            }
        }
    }

    /// <summary>Включает/выключает правило по имени.</summary>
    public FirewallOperationResult ToggleRule(string ruleName, FirewallDirection direction, bool enable)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            return FirewallOperationResult.Fail("Пустое имя правила.");

        var name = ruleName.Trim();
        lock (_sync)
        {
            object? policyObj = null;
            object? rulesObj = null;
            try
            {
                dynamic policy = CreatePolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;

                int toggled = 0;
                foreach (var candidate in CandidateNames(name, direction))
                {
                    dynamic? found = FindRule(rules, candidate);
                    if (found is null) continue;
                    try
                    {
                        if (direction is FirewallDirection.Inbound or FirewallDirection.Outbound)
                        {
                            int dir = SafeGetInt(found, "Direction", DirIn);
                            bool match = direction == FirewallDirection.Inbound ? dir == DirIn : dir == DirOut;
                            if (!match) continue;
                        }

                        found.Enabled = enable;
                        toggled++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "ToggleRule({Name}): не удалось переключить Enabled", candidate);
                    }
                    finally { Release(found); }
                }

                return toggled > 0
                    ? FirewallOperationResult.Ok($"Правило {name} {(enable ? "включено" : "выключено")}.", name)
                    : FirewallOperationResult.Fail($"Правило {name} не найдено.", name);
            }
            catch (Exception ex) when (IsFirewallDown(ex))
            {
                return FirewallOperationResult.Fail("Служба брандмауэра Windows недоступна (MpsSvc остановлена?).", name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ToggleRule({Name}) не выполнен", name);
                return FirewallOperationResult.Fail($"Не удалось переключить правило {name}: {FriendlyError(ex)}", name);
            }
            finally
            {
                Release(rulesObj);
                Release(policyObj);
            }
        }
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
            object? rulesObj = null;
            try
            {
                dynamic policy = CreatePolicy(out policyObj);
                dynamic rules = policy.Rules;
                rulesObj = rules;

                switch (mode)
                {
                    case FirewallFilteringMode.Normal:
                        SetFirewallEnabledAll(policy, true);
                        SetDefaultOutboundAll(policy, ActionAllow);
                        RemoveRuleByName(rules, KillswitchRuleNameIn);
                        RemoveRuleByName(rules, KillswitchRuleNameOut);
                        break;

                    case FirewallFilteringMode.WhitelistOutbound:
                        SetFirewallEnabledAll(policy, true);
                        RemoveRuleByName(rules, KillswitchRuleNameIn);
                        RemoveRuleByName(rules, KillswitchRuleNameOut);
                        SetDefaultOutboundAll(policy, ActionBlock);
                        break;

                    case FirewallFilteringMode.KillswitchBlockAll:
                        SetFirewallEnabledAll(policy, true);
                        UpsertKillswitchRule(rules, KillswitchRuleNameIn, DirIn);
                        UpsertKillswitchRule(rules, KillswitchRuleNameOut, DirOut);
                        break;

                    case FirewallFilteringMode.Disabled:
                        RemoveRuleByName(rules, KillswitchRuleNameIn);
                        RemoveRuleByName(rules, KillswitchRuleNameOut);
                        SetFirewallEnabledAll(policy, false);
                        break;
                }

                Log.Information("Firewall: режим фильтрации -> {Mode}", mode);
                return FirewallOperationResult.Ok($"Режим фильтрации: {mode}.");
            }
            catch (Exception ex) when (IsFirewallDown(ex))
            {
                return FirewallOperationResult.Fail("Служба брандмауэра Windows недоступна (MpsSvc остановлена?).");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SetFilteringMode({Mode}) не выполнен", mode);
                return FirewallOperationResult.Fail($"Не удалось установить режим {mode}: {FriendlyError(ex)}");
            }
            finally
            {
                Release(rulesObj);
                Release(policyObj);
            }
        }
    }

    /// <summary>Возвращает текущий режим фильтрации (эвристика по политике и Killswitch-правилу).</summary>
    public FirewallFilteringMode GetFilteringMode()
    {
        object? policyObj = null;
        object? rulesObj = null;
        try
        {
            dynamic policy = CreatePolicy(out policyObj);
            dynamic rules = policy.Rules;
            rulesObj = rules;

            bool d = IsEnabledSafe(policy, ProfileDomain);
            bool p = IsEnabledSafe(policy, ProfilePrivate);
            bool pub = IsEnabledSafe(policy, ProfilePublic);
            if (!d && !p && !pub)
                return FirewallFilteringMode.Disabled;

            if (IsKillswitchActive(rules))
                return FirewallFilteringMode.KillswitchBlockAll;

            if (IsDefaultOutboundBlocked(policy))
                return FirewallFilteringMode.WhitelistOutbound;

            return FirewallFilteringMode.Normal;
        }
        catch
        {
            return FirewallFilteringMode.Normal;
        }
        finally
        {
            Release(rulesObj);
            Release(policyObj);
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

    private static void ApplyDtoToComRule(dynamic comRule, FirewallRuleDto dto, string name, int direction)
    {
        comRule.Name = name;
        if (!string.IsNullOrWhiteSpace(dto.Description))
            comRule.Description = dto.Description;
        comRule.Direction = direction;
        comRule.Action = dto.Action == FirewallAction.Allow ? ActionAllow : ActionBlock;
        comRule.Enabled = dto.Enabled;
        comRule.Profiles = ToComProfiles(dto.Profiles);
        comRule.Protocol = dto.Protocol switch
        {
            FirewallProtocol.Tcp => ProtoTcp,
            FirewallProtocol.Udp => ProtoUdp,
            _ => ProtoAny,
        };

        var app = dto.ApplicationPath ?? dto.ApplicationName;
        if (!string.IsNullOrWhiteSpace(app))
            comRule.ApplicationName = app;
        if (!string.IsNullOrWhiteSpace(dto.ServiceName))
        {
            try { comRule.ServiceName = dto.ServiceName; } catch { }
        }

        if (comRule.Protocol is ProtoTcp or ProtoUdp)
        {
            if (!string.IsNullOrWhiteSpace(dto.LocalPorts))
            {
                try { comRule.LocalPorts = dto.LocalPorts.Trim(); } catch { }
            }
            if (!string.IsNullOrWhiteSpace(dto.RemotePorts))
            {
                try { comRule.RemotePorts = dto.RemotePorts.Trim(); } catch { }
            }
        }
        if (!string.IsNullOrWhiteSpace(dto.RemoteAddresses))
        {
            try { comRule.RemoteAddresses = dto.RemoteAddresses.Trim(); } catch { }
        }
        if (!string.IsNullOrWhiteSpace(dto.Grouping))
            comRule.Grouping = dto.Grouping;
        else if (name.StartsWith(RulePrefix, StringComparison.OrdinalIgnoreCase))
            comRule.Grouping = RuleGroup;
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

    private static void SetFirewallEnabledAll(dynamic policy, bool enabled)
    {
        foreach (var profile in new[] { ProfileDomain, ProfilePrivate, ProfilePublic })
        {
            try { policy.FirewallEnabled[profile] = enabled; } catch { }
        }
    }

    private static void SetDefaultOutboundAll(dynamic policy, int action)
    {
        foreach (var profile in new[] { ProfileDomain, ProfilePrivate, ProfilePublic })
        {
            try { policy.DefaultOutboundAction[profile] = action; } catch { }
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

            var set = new HashSet<string>(SplitAddresses(SafeGet(rule, "RemoteAddresses") ?? "*"), StringComparer.OrdinalIgnoreCase);
            set.Remove("*");
            if (set.Add(normalizedIp))
            {
                rule.RemoteAddresses = string.Join(",", set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
                rule.Enabled = true;
            }

            return ruleName;
        }
        finally
        {
            Release(rule);
            Release(rulesObj);
            Release(policyObj);
        }
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

            var set = new HashSet<string>(SplitAddresses(SafeGet(rule, "RemoteAddresses") ?? string.Empty), StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Поиск правила по имени: сначала быстрый нативный Item(), затем линейный обход.</summary>
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
                catch
                {
                    Release(direct);
                }
            }
            else
            {
                Release(direct);
            }
        }
        catch (COMException) { }
        catch (FileNotFoundException) { }

        System.Collections.IEnumerator? enumerator = null;
        try
        {
            enumerator = ((System.Collections.IEnumerable)rules).GetEnumerator();
            while (true)
            {
                bool moved;
                try { moved = enumerator.MoveNext(); }
                catch { break; }
                if (!moved) break;

                dynamic current = enumerator.Current;
                string? currentName = null;
                try { currentName = (string)current.Name; }
                catch { Release(current); continue; }

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

    private static bool RemoveRuleByName(dynamic rules, string name)
    {
        try
        {
            rules.Remove(name);
            return true;
        }
        catch
        {
            return false; // правило отсутствует — не ошибка
        }
    }

    private static List<FirewallRuleDto> EnumerateRules(dynamic rules)
    {
        var list = new List<FirewallRuleDto>();
        System.Collections.IEnumerator? enumerator = null;
        try
        {
            enumerator = ((System.Collections.IEnumerable)rules).GetEnumerator();
            while (true)
            {
                bool moved;
                try { moved = enumerator.MoveNext(); }
                catch { break; }
                if (!moved) break;

                dynamic r = enumerator.Current;
                try { list.Add(ReadRule(r)); }
                catch { /* битое правило — пропускаем */ }
                finally { Release(r); }

                // Кооперативный Time-Slicing: отдаем квант времени каждые 50 правил
                if (list.Count % 50 == 0)
                {
                    Thread.Yield();
                }
            }
        }
        finally
        {
            Release(enumerator);
        }

        return list;
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

    private static string? SafeGet(dynamic rule, string property)
    {
        try
        {
            object? v = rule.GetType().InvokeMember(property,
                System.Reflection.BindingFlags.GetProperty, null, rule, null);
            return v?.ToString();
        }
        catch { return null; }
    }

    private static int SafeGetInt(dynamic rule, string property, int fallback)
    {
        try
        {
            object? v = rule.GetType().InvokeMember(property,
                System.Reflection.BindingFlags.GetProperty, null, rule, null);
            return v is null ? fallback : Convert.ToInt32(v);
        }
        catch { return fallback; }
    }

    private static bool SafeGetBool(dynamic rule, string property, bool fallback)
    {
        try
        {
            object? v = rule.GetType().InvokeMember(property,
                System.Reflection.BindingFlags.GetProperty, null, rule, null);
            return v is null ? fallback : Convert.ToBoolean(v);
        }
        catch { return fallback; }
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

    private static string PortRuleName(int port, FirewallProtocol protocol, int direction, string? appPath)
    {
        var scope = string.IsNullOrEmpty(appPath)
            ? "System"
            : $"App_{Path.GetFileNameWithoutExtension(appPath)}";
        return $"{RulePrefix}Block_Port_{protocol}_{port}_{(direction == DirOut ? "Out" : "In")}_{scope}";
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
