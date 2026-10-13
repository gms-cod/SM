Проектирую сервис управления Windows Firewall — изучаю текущую архитектуру мониторинга.
Обнаружил готовый базовый сервис firewall — проверяю интеграцию с трекером сети.
В проекте уже есть эталонная реализация — `src/StartupMonitor.Service/Services/WindowsFirewallService.cs:29`, сборка `Service` — `Build succeeded, 0 Errors`. Ниже — архитектурный разбор как ТЗ закрыто.

## 1. Архитектура

```
UI (таблица сокетов) ──Pipe IPC (JSON DTO)──> Service (SYSTEM, без UAC)
                                                  │
                    ┌─────────────────────────────┼─────────────────────────────┐
                    │ ProcessNetworkTracker.cs:36 │ WindowsFirewallService.cs:29│
                    │ GetExtendedTcp/UdpTable     │ HNetCfg.FwPolicy2 (late-bound)│
                    │ GetSockets(pid)             │ HNetCfg.FWRule                │
                    │ CaptureNetworkBandwidth()   │ lock(_sync) read-modify-write│
                    └──────────────┬──────────────┴──────────────┬──────────────┘
                                   │ ProcessInfo.cs +            │
                                   │ SystemPerformanceSnapshot.cs│
                                   │ ActiveSocketsCount,         │
                                   │ NetworkBytesPerSec          │
                                   └─────────────────────────────┘
                                                  │
                                          WFP / MpsSvc
                              Profiles=0x7FFFFFFF (Domain|Private|Public)
```

Почему так:

* `late binding` (`Type.GetTypeFromProgID("HNetCfg.FwPolicy2")` + `dynamic`) — без COM-reference, совместимо с .NET 8 single-file/self-contained. См. `WindowsFirewallService.cs:644-668`.
* COM-объект живёт только внутри одной операции → `finally { Release() }`. См. `WindowsFirewallService.cs:814-823` (`Marshal.IsComObject` + `ReleaseComObject`, повторное освобождение игнорируется).
* Служба зарегистрирована как `AddSingleton<WindowsFirewallService>` в `src/StartupMonitor.Service/Program.cs:96`, работает от `SYSTEM` — полные права на WFP без UAC.
* Мутации агрегированных правил под `lock(_sync)` — `BlockIp/UnblockIp/BlockPort` — защита CSV `RemoteAddresses` от гонок.

## 2. IP: агрегированные vs одиночные правила

Идея: `O(1)` правил вместо `O(N)` — минимальный оверхед файрвола:

* `AggregatedRuleNameIn = "StartupMonitor_Blocked_IPs_In"` / `..._Out` (`WindowsFirewallService.cs:40-43`), `Grouping="StartupMonitor"`, `Action=Block`, `Protocol=Any(256)`, `Profiles=ALL`.
* `AddIpToAggregated(normalizedIp, DirIn/Out, ruleName)` (`WindowsFirewallService.cs:450`): `FindRule` → нет → `CreateRuleObject + Rules.Add`; есть → `HashSet<SplitAddresses(RemoteAddresses)> + string.Join(",")`, `Enabled=true`.
* Пустое правило не храним: `RemoveIpFromAggregated` (`WindowsFirewallService.cs:496`) удаляет правило если `set.Count==0`.
* Одиночные: `StartupMonitor_Block_IP_{sanit}_In/Out` (`WindowsFirewallService.cs:862-864`) через `UpsertSingleIpRule` (`WindowsFirewallService.cs:534`) — идемпотентный `Enabled=true` если уже есть.
* `IsIpBlocked(ip)` (`WindowsFirewallService.cs:212`): перебор всех правил (включая чужие), `Enabled && Action==Block && AddressListContains(RemoteAddresses)` → `BlockedBoth/Inbound/Outbound/Allowed`.
* `GetBlockedIps()` (`WindowsFirewallService.cs:248`): `SortedSet` из двух агрегированных правил.

Защита от самоблокировки — `FirewallGuard.IsBlockableAddress` в `src/StartupMonitor.Shared/Models/Firewall/FirewallModels.cs:261`: запрет `loopback`, `0.0.0.0/::`, `multicast >=224.0.0.0`.

## 3. Порты TCP/UDP (+ привязка к EXE)

* `BlockPort(BlockPortRequest{Port, Protocol, Direction, ApplicationPath, Reason})` (`WindowsFirewallService.cs:287`): валидация `1-65535`, только `Tcp/Udp`, `NormalizeAppPath` (`WindowsFirewallService.cs:866` — warning если не абсолютный/файл не найден, правило всё равно создаётся впрок).
* Имя: `StartupMonitor_Block_Port_{Tcp}_{80}_{In}_{System|App_Name}` (`WindowsFirewallService.cs:877-883`).
* `UpsertPortRule` (`WindowsFirewallService.cs:599`): `Protocol=(int)protocol (6/17)`, `LocalPorts=port.ToString()`, `ApplicationName=appPath` если задано (вся система если `null`), `Profiles=ALL`.
* `UnblockPort` (`WindowsFirewallService.cs:320`) удаляет только свои правила по имени, чужие не трогает. `IsPortBlocked(port, proto, dir, app)` (`WindowsFirewallService.cs:359`) — точечная проверка по имени + `Enabled && Action==Block`.

## 4. DTO (`FirewallModels.cs` — чистые POCO, без COM, для Pipe/UI)

```csharp
enum FirewallDirection { Inbound=1, Outbound=2, Both=3 }
enum FirewallProtocol { Any=256, Tcp=6, Udp=17 }
enum FirewallAction { Block=0, Allow=1 }
[Flags] enum FirewallProfile { Domain=1, Private=2, Public=4, All=... }
enum FirewallBlockStatus { Allowed, BlockedInbound, BlockedOutbound, BlockedBoth }

class FirewallOperationResult { bool Success; string Message; string? RuleName; bool Created; }
class FirewallRuleDto { Name, Description, Direction, Action, Protocol,
  LocalPorts, RemoteAddresses, ApplicationPath, Enabled, Profiles, IsManaged }
class BlockIpRequest { Ip, Direction=Both, Aggregated=true, Reason }
class UnblockIpRequest { Ip, Direction=Both }
class BlockPortRequest { Port, Protocol=Tcp, Direction=Inbound, ApplicationPath, Reason }
class UnblockPortRequest { Port, Protocol, Direction, ApplicationPath }
class FirewallStatusDto { Available, Enabled, CurrentProfiles,
  DomainEnabled, PrivateEnabled, PublicEnabled, ManagedRulesCount, BlockedIpsCount }
```

## 5. Надёжность / профили / ошибки

* Профили `Domain=1, Private=2, Public=4` (`WindowsFirewallService.cs:62-64`). Все правила `Profiles=0x7FFFFFFF`. `GetStatus()` (`WindowsFirewallService.cs:81`) читает `CurrentProfileTypes` + `FirewallEnabled[profile]` через `IsEnabledSafe` (`WindowsFirewallService.cs:807`) — если файрвол выключен, правила persist, но трафик не фильтруется, UI это видит (`Available/Enabled`).
* `IsFirewallDown(ex)` (`WindowsFirewallService.cs:827`): `FirewallNotAvailableException` или `COMException HResult 0x80070422/0x8007042C/0x80070005` → `Fail("MpsSvc остановлена?")`, а не crash. `FriendlyError` (`WindowsFirewallService.cs:832`) маппит в RU-сообщения.
* `FindRule/EnumerateRules` (`WindowsFirewallService.cs:672,716`): каждый неподошедший `RCW` сразу `Release`, `enumerator` тоже `Release` — утечек нет. `ReadRule` (`WindowsFirewallService.cs:744`) через `SafeGet/SafeGetInt/SafeGetBool` — битые чужие правила пропускаются.
* Инвентаризация: `ListManagedRules()` (префикс `StartupMonitor_`), `RemoveAllManagedRules()` → `int removed`.

## 6. Связка с целевыми файлами

`ProcessNetworkTracker.cs:290 GetSockets(pid)` даёт `ProcessNetworkSocket{RemoteAddress, RemotePort, ProcessId, ProcessName}`. `ProcessMetricsTracker.cs:94 GetProcessSockets` + `ProcessMetricsTracker.cs:160 CaptureNetworkBandwidth` обогащают `ProcessInfo{ActiveSocketsCount, NetworkBytesPerSec, HasNetworkActivity}`.

Интеграция (пример вызова из Pipe-хендлера, без правок в трекере):

```csharp
// блок IP из строки таблицы сокетов:
var socks = metricsTracker.GetProcessSockets(pid);
var target = socks.First(s => s.State=="ESTABLISHED" && !s.IsRemotePrivate);
var r = fw.BlockIp(new BlockIpRequest {
  Ip = target.RemoteAddress, Direction = FirewallDirection.Both,
  Aggregated = true, Reason = $"pid={target.ProcessId} {target.ProcessName}" });

// бейдж в UI:
var st = fw.IsIpBlocked(sock.RemoteAddress); // Allowed / BlockedIn / BlockedOut / Both
var portBlocked = fw.IsPortBlocked(sock.RemotePort, FirewallProtocol.Tcp,
  FirewallDirection.Outbound);
```

Файла `Shared/Models/ProcessMetrics.cs` в репо нет — его роль выполняют `ProcessInfo.cs` (метрика процесса) + `SystemPerformanceSnapshot.cs` (сводка) + `ProcessNetworkSocket.cs`. Отдельный файл не нужен.

Верификация: `dotnet build src/StartupMonitor.Service -c Release` — `0 Warning(s), 0 Error(s)`.
