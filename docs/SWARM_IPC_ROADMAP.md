Проектирую протокол Firewall/Defender для UI и службы — изучаю текущие модели и каналы.
Нашел базовые типы сообщений и структуру каналов — проверяю модели настроек и существующие заделы firewall.
Подтверждено — базовый firewall есть, а PortDefender и ActiveBan отсутствуют. Смотрю персистентность и шаблоны обработки.
# Firewall / Defender — протокол UI ↔ Служба (.NET 8, Named Pipe)

Анализ существующего кода:

* `src/StartupMonitor.Shared/Models/Firewall/FirewallModels.cs:95-216` — уже есть `FirewallRuleDto`, `BlockIpRequest/UnblockIpRequest`, `BlockPortRequest/UnblockPortRequest`, `FirewallStatusDto`, `FirewallGuard`.
* `src/StartupMonitor.Shared/Models/PipeMessage.cs:22-190` — `PipeMessage {Type, Payload, Timestamp}`, `PipeMessageTypes` без firewall-типов.
* `src/StartupMonitor.Service/Services/WindowsFirewallService.cs:29-75` — late-bound COM `HNetCfg.FwPolicy2`, агрегированные правила `StartupMonitor_Blocked_IPs_In/Out`, thread-safe `lock`, Zero-Crash через `FirewallOperationResult`.
* `src/StartupMonitor.Service/Pipe/PipeServer.cs:257-1311` — `switch(message.Type)`, `JsonOptions {CaseInsensitive}`, `Success/Error` ответы.
* `src/StartupMonitor.UI/Services/PipeClient.cs:165-218` — `SendAsync` + `SemaphoreSlim _sendLock`, таймаут 70с.
* `src/StartupMonitor.Shared/Models/AppConfig.cs:38-179`, `JsonDataStore.cs:30-42` — секционные файлы `settings.json`, `startup-entries.json`, `blacklist.json`. `PortDefender`/`ActiveBan` отсутствуют — проектируем с нуля.

---

## 1. Shared DTO — новый файл `Shared/Models/Firewall/DefenderModels.cs`

Не трогать существующий `FirewallModels.cs`. Все новое — отдельно, чистые POCO, `System.Text.Json`:

```csharp
// src/StartupMonitor.Shared/Models/Firewall/DefenderModels.cs
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Конфигурация PortDefender (fail2ban для RDP/SSH/кастомных портов).</summary>
public sealed class PortDefenderConfigDto
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("protectedPorts")] public List<int> ProtectedPorts { get; set; } = new() { 3389 };
    [JsonPropertyName("maxAttempts")] public int MaxAttempts { get; set; } = 5;
    [JsonPropertyName("windowSeconds")] public int WindowSeconds { get; set; } = 300;
    [JsonPropertyName("banMinutes")] public int BanMinutes { get; set; } = 60;
    [JsonPropertyName("whitelistIps")] public List<string> WhitelistIps { get; set; } = new();
}

/// <summary>Активный бан (временная блокировка IP PortDefender'ом).</summary>
public sealed class ActiveBanDto
{
    [JsonPropertyName("ip")] public string Ip { get; set; } = string.Empty;
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("protocol")] public FirewallProtocol Protocol { get; set; } = FirewallProtocol.Tcp;
    [JsonPropertyName("bannedAtUtc")] public DateTime BannedAtUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("expiresAtUtc")] public DateTime ExpiresAtUtc { get; set; }
    [JsonPropertyName("attemptCount")] public int AttemptCount { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
    [JsonPropertyName("ruleName")] public string? RuleName { get; set; }
}

/// <summary>Запрос ручного разбана.</summary>
public sealed class UnbanIpRequest
{
    [JsonPropertyName("ip")] public string Ip { get; set; } = string.Empty;
}

/// <summary>Событие атаки (push из службы в UI через Events-pipe).</summary>
public sealed class DefenderAttackEventDto
{
    [JsonPropertyName("ip")] public string Ip { get; set; } = string.Empty;
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("attemptCount")] public int AttemptCount { get; set; }
    [JsonPropertyName("isBanned")] public bool IsBanned { get; set; }
    [JsonPropertyName("detectedAtUtc")] public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Запрос вкл/выкл правила по имени (для ToggleFirewallRule).</summary>
public sealed class ToggleFirewallRuleRequest
{
    [JsonPropertyName("ruleName")] public string RuleName { get; set; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

/// <summary>Расширенный статус для UI: Firewall + Defender в одном ответе.</summary>
public sealed class FirewallFullStatusDto
{
    [JsonPropertyName("firewall")] public FirewallStatusDto Firewall { get; set; } = new();
    [JsonPropertyName("defenderConfig")] public PortDefenderConfigDto DefenderConfig { get; set; } = new();
    [JsonPropertyName("activeBansCount")] public int ActiveBansCount { get; set; }
    [JsonPropertyName("totalAttacksBlocked")] public long TotalAttacksBlocked { get; set; }
}
```

> `FirewallRuleDto`, `BlockIpRequest`, `BlockPortRequest` — переиспользовать из `FirewallModels.cs:95-196`, не дублировать.

### Расширение `FirewallGuard` (самоблокировка)

Дописать в `FirewallModels.cs`:

```csharp
public static bool IsBlockableAddressEx(string ipOrSubnet, out string? error,
    IEnumerable<string>? extraProtected = null)
{
    if (!IsBlockableAddress(ipOrSubnet, out error)) return false;
    var addr = ipOrSubnet.Split('/')[0].Trim().Trim('[', ']');
    // 1. Шлюз по умолчанию + DNS
    foreach (var p in GetLocalGatewayAndDns())
        if (string.Equals(p, addr, StringComparison.OrdinalIgnoreCase))
        { error = $"Блокировка {addr} запрещена: шлюз/DNS."; return false; }
    // 2. Локальные IP хоста
    foreach (var local in GetLocalUnicastAddresses())
        if (string.Equals(local, addr, StringComparison.OrdinalIgnoreCase))
        { error = $"Блокировка {addr} запрещена: собственный IP хоста."; return false; }
    // 3. Whitelist PortDefender
    if (extraProtected != null && extraProtected.Contains(addr, StringComparer.OrdinalIgnoreCase))
    { error = $"IP {addr} в whitelist."; return false; }
    return true;
}
```

Реализация `GetLocalGatewayAndDns()` — `NetworkInterface.GetAllNetworkInterfaces()` + `IPInterfaceProperties.GatewayAddresses/DnsAddresses`; `GetLocalUnicastAddresses()` — `Dns.GetHostAddresses + UnicastAddresses`. Только чтение, без исключений наружу (Zero-Crash: `try/catch → false`).

## 2. `PipeMessageTypes` — дописать в `PipeMessage.cs:188`

```csharp
/// <summary>Запрос полного статуса Firewall+Defender.</summary>
public const string GetFirewallStatus = "GetFirewallStatus";
public const string GetFirewallStatusResponse = "GetFirewallStatusResponse";
/// <summary>Вкл/выкл правило по имени. Payload: ToggleFirewallRuleRequest.</summary>
public const string ToggleFirewallRule = "ToggleFirewallRule";
/// <summary>Сохранить конфиг PortDefender. Payload: PortDefenderConfigDto.</summary>
public const string ConfigurePortDefender = "ConfigurePortDefender";
/// <summary>Список активных банов.</summary>
public const string GetActiveBans = "GetActiveBans";
public const string GetActiveBansResponse = "GetActiveBansResponse";
/// <summary>Снять бан. Payload: UnbanIpRequest.</summary>
public const string UnbanIp = "UnbanIp";
/// <summary>Сбросить счётчики атак/банов.</summary>
public const string ResetDefenderStats = "ResetDefenderStats";
/// <summary>Push-событие атаки (Events-pipe). Payload: DefenderAttackEventDto.</summary>
public const string DefenderAttackEvent = "DefenderAttackEvent";
```

Соглашение: запрос → `Success` с `FirewallOperationResult` в Payload, либо типизированный Response; ошибка → `Type=Error`, `Payload=строка`.

## 3. Персистентность `settings.json` + восстановление

Расширить `AppSettings` (`AppConfig.cs:38`):

```csharp
[JsonPropertyName("port_defender")] public PortDefenderConfigDto PortDefender { get; set; } = new();
[JsonPropertyName("active_bans")] public List<ActiveBanDto> ActiveBans { get; set; } = new();
[JsonPropertyName("total_attacks_blocked")] public long TotalAttacksBlocked { get; set; }
```

Почему в `settings.json`, а не отдельный файл: `JsonDataStore` уже атомарно пишет секцию (`WriteSectionAtomic`, tmp+move), переживает `.corrupt`-файлы, требование ТЗ — именно `settings.json`. Отдельный `firewall.json` — только если банов >10k (не наш случай).

Восстановление при старте службы (`Program.cs` + новый `PortDefenderService`):

```csharp
// Program.cs
services.AddSingleton<WindowsFirewallService>();
services.AddSingleton<PortDefenderService>(); // новый: таймер polling Security EventLog 4625 + BanStore

// PortDefenderService.StartAsync:
var cfg = _store.Load();
foreach (var ban in cfg.Settings.ActiveBans.Where(b => b.ExpiresAtUtc > DateTime.UtcNow))
    _firewall.BlockIp(new BlockIpRequest { Ip = ban.Ip, Direction = FirewallDirection.Inbound,
        Aggregated = true, Reason = $"restore:{ban.Reason}" });
// просроченные — удалить из конфига и сохранить
```

Истечение бана — фоновый таймер 60с: `UnblockIp + убрать из ActiveBans + Save`.

## 4. `PipeServer.cs` — обработчики (вставить в `switch` перед `default:`)

Требуется инжект в конструктор: `WindowsFirewallService firewall, PortDefenderService defender`.

```csharp
case PipeMessageTypes.GetFirewallStatus:
{
    var fw = _firewall.GetStatus(); // Zero-Crash внутри, Available=false при MpsSvc down
    var cfg = _store.Load();
    var full = new FirewallFullStatusDto
    {
        Firewall = fw,
        DefenderConfig = cfg.Settings.PortDefender,
        ActiveBansCount = cfg.Settings.ActiveBans.Count(b => b.ExpiresAtUtc > DateTime.UtcNow),
        TotalAttacksBlocked = cfg.Settings.TotalAttacksBlocked
    };
    response.Type = PipeMessageTypes.GetFirewallStatusResponse;
    response.Payload = JsonSerializer.Serialize(full, JsonOptions);
    break;
}
case PipeMessageTypes.ToggleFirewallRule:
{
    var req = JsonSerializer.Deserialize<ToggleFirewallRuleRequest>(message.Payload, JsonOptions);
    if (req == null || string.IsNullOrWhiteSpace(req.RuleName))
    { response.Type = PipeMessageTypes.Error; response.Payload = "Пустое имя правила."; break; }
    var r = _firewall.SetRuleEnabled(req.RuleName, req.Enabled); // обернуть try/catch, вернуть FirewallOperationResult
    response.Type = r.Success ? PipeMessageTypes.Success : PipeMessageTypes.Error;
    response.Payload = JsonSerializer.Serialize(r, JsonOptions);
    break;
}
case PipeMessageTypes.ConfigurePortDefender:
{
    var dto = JsonSerializer.Deserialize<PortDefenderConfigDto>(message.Payload, JsonOptions);
    if (dto == null) { response.Type = PipeMessageTypes.Error; response.Payload = "Пустой конфиг."; break; }
    if (dto.MaxAttempts < 2 || dto.MaxAttempts > 100 || dto.WindowSeconds < 30 || dto.BanMinutes < 1)
    { response.Type = PipeMessageTypes.Error; response.Payload = "Недопустимые пороги."; break; }
    foreach (var p in dto.ProtectedPorts)
        if (!FirewallGuard.IsValidPort(p)) { response.Type = PipeMessageTypes.Error; response.Payload = $"Порт {p} вне 1-65535."; break; }
    if (response.Type == PipeMessageTypes.Error) break;
    var cfg = _store.Load();
    cfg.Settings.PortDefender = dto;
    _store.Save(cfg);
    _defender.ApplyConfig(dto); // переподписка на EventLog без перезапуска службы
    response.Type = PipeMessageTypes.Success;
    break;
}
case PipeMessageTypes.GetActiveBans:
{
    var cfg = _store.Load();
    var live = cfg.Settings.ActiveBans.Where(b => b.ExpiresAtUtc > DateTime.UtcNow).ToList();
    response.Type = PipeMessageTypes.GetActiveBansResponse;
    response.Payload = JsonSerializer.Serialize(live, JsonOptions);
    break;
}
case PipeMessageTypes.UnbanIp:
{
    var req = JsonSerializer.Deserialize<UnbanIpRequest>(message.Payload, JsonOptions);
    if (req == null || !FirewallGuard.IsBlockableAddress(req.Ip, out _))
    { response.Type = PipeMessageTypes.Error; response.Payload = "Некорректный IP."; break; }
    var r = _defender.Unban(req.Ip); // UnblockIp + удаление из ActiveBans + Save + History
    response.Type = r.Success ? PipeMessageTypes.Success : PipeMessageTypes.Error;
    response.Payload = JsonSerializer.Serialize(r, JsonOptions);
    break;
}
case PipeMessageTypes.ResetDefenderStats:
{
    var cfg = _store.Load();
    cfg.Settings.TotalAttacksBlocked = 0;
    _store.Save(cfg);
    _defender.ResetCounters();
    response.Type = PipeMessageTypes.Success;
    break;
}
```

Каждый `BlockIp` в `_defender` предварительно вызывает `IsBlockableAddressEx(ip, whitelist)`. Исключения COM (`0x80070422`, `0x80070005`, `0x8007042C`) маппятся в `Fail("Служба брандмауэра недоступна")`, не роняют `ProcessMessage` (внешний `try/catch → Error` уже есть в `PipeServer.cs:1304`).

Push атак: `_defender.OnAttack += dto => _notifications.Enqueue(new PipeMessage { Type = DefenderAttackEvent, Payload = serialize(dto) })` — UI уже слушает `EventsReceived` в `PipeClient.cs:128`.

## 5. `PipeClient.cs` — методы UI

```csharp
public async Task<FirewallFullStatusDto?> GetFirewallStatusAsync()
{
    var rsp = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetFirewallStatus });
    if (rsp?.Type == PipeMessageTypes.GetFirewallStatusResponse && !string.IsNullOrEmpty(rsp.Payload))
        try { return JsonSerializer.Deserialize<FirewallFullStatusDto>(rsp.Payload, JsonOptions); } catch { }
    return null;
}
public async Task<FirewallOperationResult?> ToggleFirewallRuleAsync(string ruleName, bool enabled)
{
    var rsp = await SendAsync(new PipeMessage { Type = PipeMessageTypes.ToggleFirewallRule,
        Payload = JsonSerializer.Serialize(new ToggleFirewallRuleRequest { RuleName = ruleName, Enabled = enabled }, JsonOptions) });
    if (!string.IsNullOrEmpty(rsp?.Payload))
        try { return JsonSerializer.Deserialize<FirewallOperationResult>(rsp.Payload, JsonOptions); } catch { }
    return null;
}
public async Task<bool> ConfigurePortDefenderAsync(PortDefenderConfigDto cfg)
{
    var rsp = await SendAsync(new PipeMessage { Type = PipeMessageTypes.ConfigurePortDefender,
        Payload = JsonSerializer.Serialize(cfg, JsonOptions) });
    return rsp?.Type == PipeMessageTypes.Success;
}
public async Task<List<ActiveBanDto>> GetActiveBansAsync()
{
    var rsp = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetActiveBans });
    if (rsp?.Type == PipeMessageTypes.GetActiveBansResponse && !string.IsNullOrEmpty(rsp.Payload))
        try { return JsonSerializer.Deserialize<List<ActiveBanDto>>(rsp.Payload, JsonOptions) ?? new(); } catch { }
    return new();
}
public async Task<FirewallOperationResult?> UnbanIpAsync(string ip)
{
    var rsp = await SendAsync(new PipeMessage { Type = PipeMessageTypes.UnbanIp,
        Payload = JsonSerializer.Serialize(new UnbanIpRequest { Ip = ip }, JsonOptions) });
    if (!string.IsNullOrEmpty(rsp?.Payload))
        try { return JsonSerializer.Deserialize<FirewallOperationResult>(rsp.Payload, JsonOptions); } catch { }
    return null;
}
public async Task<bool> ResetDefenderStatsAsync()
{
    var rsp = await SendAsync(new PipeMessage { Type = PipeMessageTypes.ResetDefenderStats });
    return rsp?.Type == PipeMessageTypes.Success;
}
```

Подписка на атаки в UI: в существующем `EventsReceived` добавить ветку `DefenderAttackEvent → Deserialize<DefenderAttackEventDto> → toast + обновление бейджа`.

## 6. Матрица рисков

| # | Риск | Вероятность / Ущерб | Митигация |
|---|------|---------------------|-----------|
| R1 | Самоблокировка: бан шлюза/DNS/своего IP → потеря сети | Низкая / Критический | `IsBlockableAddressEx`: loopback, unspecified, multicast, шлюз, DNS, локальные unicast, whitelist; UI-предупреждение при RFC1918 |
| R2 | MpsSvc остановлена / COM `HNetCfg` повреждён | Средняя / Средний | `GetStatus.Available=false`, бейдж в UI, все мутации → `Fail`, без исключений; повторная попытка при рестарте службы |
| R3 | Рассинхрон `settings.json` ↔ реальные правила WFP (правила удалены вручную через `wf.msc`) | Средняя / Средний | Reconcile при старте: сверка `ActiveBans` с `ListManagedRules()`, пересоздание недостающих; просроченные баны чистятся |
| R4 | Флуд EventLog 4625 (брутфорс) → рост CPU/памяти | Средняя / Средний | Агрегированные правила O(1), скользящее окно в памяти (`ConcurrentDictionary<IP,Queue<DateTime>>`), троттлинг опроса EventLog 5с, лимит `ActiveBans` 5000 с eviction самых старых |
| R5 | Pipe-flood / вредоносный локальный клиент | Низкая / Средний | ACL на pipe (`PipeSecurityFactory`), валидация всех DTO на сервере, `SemaphoreSlim` + таймаут 70с уже есть |
| R6 | Повреждённый `settings.json` после краша | Низкая / Низкий | Атомарная запись tmp+move, `.corrupt`-бэкап, дефолтный `PortDefenderConfigDto` при null |

## 7. Пошаговый план для `release_firewall.md` (Этапы 1–6)

**Этап 1 — Shared-контракт.** Создать `DefenderModels.cs` (4 DTO выше), дописать 7 констант в `PipeMessageTypes`, расширить `FirewallGuard` + `AppSettings`. Проверка: `dotnet build StartupMonitor.Shared`, JSON round-trip тест (сериализация snake_case).

**Этап 2 — Служба: `PortDefenderService` + `BanStore`.** Новый `Service/Services/PortDefender/PortDefenderService.cs`: опрос Security EventLog (4625) каждые 5с, скользящее окно, `BlockIp(Aggregated)`, запись в `ActiveBans`, `Enqueue(DefenderAttackEvent)`, таймер разбана 60с. Регистрация в `Program.cs`. Проверка: unit-тест логики окна (5 попыток/300с → бан), интеграционный тест `BlockIp/UnblockIp` на VM.

**Этап 3 — PipeServer.** Инжект `WindowsFirewallService + PortDefenderService`, 6 веток `switch` из §4, reconcile `ActiveBans` при старте. Проверка: ручной прогон всех команд через тестовый pipe-клиент, `MpsSvc` stop → `Available=false`, `Error` без краша службы.

**Этап 4 — PipeClient + UI.** 6 методов из §5, вкладка Firewall: таблица `FirewallRuleDto`, бейдж статуса, таблица `ActiveBanDto` с кнопкой Unban, форма `PortDefenderConfigDto`, toast на `DefenderAttackEvent`. Проверка: UI против живой службы, отключение службы → graceful `null`.

**Этап 5 — Безопасность и Zero-Crash.** Тест-кейсы самоблокировки (`127.0.0.1`, `::1`, шлюз, DNS, свой IP → `Fail`), fuzz невалидных Payload (`""`, `{bad json}`, порт 0/99999), удаление правил вручную → восстановление при рестарте. Прогон `dotnet test`.

**Этап 6 — Релиз.** Обновить `settings.json`-миграцию (дефолты при отсутствии секции), написать `docs/release_firewall.md` (этот план + матрица), bump версии Service+UI синхронно (старый UI игнорирует неизвестные типы через `default → Error`), smoke-тест установщика: установка → бан тестового IP → перезапуск ОС → бан на месте → unban → правило удалено.
