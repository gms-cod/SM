# Agent 5: IPC Pipe Pipeline & Named Pipe Security Audit

- Время анализа: 135.9 сек
- Статус: Успех

---

Аудит IPC-канала служба–UI: проверяю безопасность пайпов и устойчивость клиента.
Первичные файлы просмотрены — дочитываю остатки сервера и проверяю ACL пайпа.
ACL и событийный канал проверены — теперь смотрю, как UI переживает перезапуск службы.
Картина почти полная — проверяю последний фрагмент логики переподключения.
## Аудит IPC `SYSTEM-служба <-> UI`

### 1. Безопасность `PipeServer`

**`PipeSecurity` настроен — `Pipe\PipeSecurityFactory.cs:22`:**

```csharp
SYSTEM: FullControl, Administrators: FullControl,
InteractiveSid: ReadWrite, AuthenticatedUserSid: ReadWrite
+ CurrentUser: FullControl
```

Используется в обоих каналах: `PipeServer.cs:182`, `EventPipeServer.cs:65`. Сеть/аноним закрыты — это правильно.

**Но: любой процесс залогиненного пользователя = легитимный клиент.** `InteractiveSid`/`AuthenticatedUserSid` не различают UI и малварь того же юзера (даже non-admin). Проверки `GetImpersonationUserName`, токена, подписи, `LowIntegrity`-отсечения — нет. Итог:

| Атака непривилегированной малвари | Возможно? |
|---|---|
| `RemoveFromSystem/DisableEntry/EnableEntry` — удалить/погасить автозапуск антивируса (defense evasion) | **Да**. Защиты `IsProtectedProcessName` в этих ветках **нет** (`PipeServer.cs:347,386,491`). Частично спасает только `ReapplyBlacklist: SystemProtectionHelper.IsProtectedSystemEntry` |
| `AddToBlacklist` произвольного AV | **Да**, кроме `Path/PathWildcard + IsProtectedProcessName` (`PipeServer.cs:444`). `Hash`-правило на системный файл проверкой не покрыто |
| `KillProcessById/SuspendProcess/BlockProcess` | Частично закрыто `IsProtectedProcessName` по имени и по `GetProcessById().ProcessName` (`PipeServer.cs:824,836,939`) |
| `PauseMonitoring`, `SetAutoGuardConfig(AuditOnly)`, `ClearAllBans`, `UnblockIp/Port` | **Да, без ограничений** — полное отключение защиты в один пакет |
| `BlockIp/BlockPort` произвольных хостов | **Да** — DoS руками `SYSTEM` |
| `SetAiConfig/TestAiConnection` с произвольным `ApiUrl`, `SetKasperskyConfig`, `SetRemoteNotificationConfig` (токены Telegram/Discord) | **Да**: SSRF от имени `SYSTEM` + перезапись секретов + чтение `KasperskyApiKey` через fallback в `TestKasperskyConnection` (`PipeServer.cs:1217`) |
| `DownloadUpdate/InstallUpdate` | **Да** — любой юзер триггерит исполнение установщика под `SYSTEM` (`PipeServer.cs:1264,1279`) |
| `maxNumberOfServerInstances=10` | Атакующий занимает 10 инстансов `StartupMonitor` + 10 `StartupMonitorEvents` висящими `GetAllEntries/CleanStale` — легитимный UI получает `PipeBusy` (squatting/DoS, rate-limit отсутствует) |

**Валидация входа — слабая (`PipeServer.cs:218`):**

```csharp
var line = await reader.ReadLineAsync(ct); // без лимита длины
var message = JsonSerializer.Deserialize<PipeMessage>(line, JsonOptions); // вне try
```

* Нет `MaxMessageSize` — гигантская строка без `\n` = рост `LOH`/OOM в `SYSTEM`-процессе. `StreamReader` дефолтного `NamedPipeServerStream` это не режет.
* Битая внешняя JSON-строка бросает `JsonException` мимо внутреннего цикла — ловится только внешним `catch (Exception)` в `HandleClientAsync`, который убивает **все соединение**, а не один пакет. Для чужого инстанса не критично, но легитимный UI при одном битом байте тоже отваливается.
* Внутренние `Deserialize<...>(message.Payload)` в `ProcessMessage` покрыты общим `try/catch -> Error(ex.Message)` — падения нет, но `ex.Message` утекает клиенту (info-leak), а `Deserialize<string>(null)`/`Trim('"')` (`ScanKasperskyEntry:1229`) — `NullReference`-опасны, спасает только общий catch.
* Тяжелые ветки (`GetAllEntries/GetRatings/CleanStale` — `ScanAll()+Save()` + `WaitAsync(60s)`) вызываются без троттлинга/очереди: 10 параллельных клиентов = 10 полных сканов реестра.

`JsonDataStore` в этом плане хорош: `ReadSection:153` переименовывает битый файл в `.corrupt`, `WriteSectionAtomic:248` — `tmp+move` + пропуск неизмененного по `SHA256`, все под `lock`.

### 2. Надежность `PipeClient`

**UI не виснет — но переподключение дырявое:**

* `SendAsync:170`: `_sendLock.WaitAsync(10s)`, `ResponseTimeout=70s` (запас под серверные `60s`), `WriteLine/ReadLine` — все `async`. Стартовый `MainWindow.xaml.cs:337` — `10 x (Connect 3s + Delay 1.5s)` + `scanCts 75s`. Deadlock нет.
* `ConnectAsync:68` — `ConnectAsync(3000)`, при провале `Disconnected`. Event-канал `ConnectAndReadEventsAsync:102` — бесконечный цикл `Connect(3s) -> read -> Delay(2s)` — переживает рестарт службы сам.
* `MainViewModel.OnPipeClientDisconnected:1513` — `IsConnected=false`, стоп `_pollingTimer`, `Delay(3s) -> ConnectAsync()`. `OnPipeClientConnected:1385` — ре-синк настроек в службу (`SetKillOnDisable/StrictMode/CpuThrottle/Language/Remote/AutoGuard`) + рестарт поллинга `5s`, защита от затирания данных пустым ответом (`LoadDataIfChanged:1662`, `LoadDataAsync:1976` — `0 записей -> оставить старые`). Ревизия дельты самолечится: `_entriesRevision` статическая, после рестарта `=1`, клиентский `rev` не совпадет — сервер отдаст полный снимок.
* **Дефекты:**
  1. `SendAsync:185` при `!IsConnected` молча `return null`, а `IOException` от `WriteLine/ReadLine` (обрыв при рестарте службы) **не ловится** — только `OperationCanceledException`. Часть вызовов (`GetAllEntriesAsync` без `try`) бросают в UI-поток; спасает лишь `try` в `LoadDataIfChanged`. `Disconnected` при этом не фаерится — автопереподключение не стартует до следующего таймаута, поллинг крутится вхолостую с `null`.
  2. `ConnectAsync` глотает исключение, не чистит `_reader/_writer` (пересоздает только `_pipe`), без `CancellationToken`/бэкоффа. Параллельные `ConnectAsync` (таймер + `Disconnected`-handler) могут устроить гонку за `_pipe`.
  3. Один `_sendLock` на все запросы: зависший `GetRatings` на 70с сериализует всю UI-очередь; `GetEntriesDeltaAsync` при этом уходит в `FallbackFullEntriesAsync` — вместо легкой дельты тянет полный список.

### 3. Фантомные типы в `PipeMessage.cs`

Односторонние события (`EntryAdded/Modified/Deleted`, `BlacklistMatch`, `ActionNotice`, `MonitoringStateChanged`, `PortDefenderAttackBlocked`, `DownloadUpdateProgress`, `ProcessesSnapshot`) — не фантомы, идут через `EventPipeServer`, в `ProcessMessage` их и не должно быть.

Реальный мусор:

* `BlockIpPayload/UnblockIpPayload/BlockPortPayload/UnblockPortPayload:405-434` — **мертвый код**. Сервер десериализует `BlockIpRequest/UnblockIpRequest/...` (`PipeServer.cs:1326,1354,1384,1413`), клиент шлет DTO из `Firewall`-моделей. `*Payload`-рекорды — легаси-дубли, оставленные как fallback.
* `ActiveBansPayload:437` — нигде не используется, сервер шлет голый `List<ActiveBanDto>` (`PipeServer.cs:1488`).
* `GetPortDefenderConfig:1441` — сервер отвечает **эхом запроса** (`Type = GetPortDefenderConfig`), а не `...Response` (константы ответа нет). Работает только потому, что клиент `GetPortDefenderConfigAsync:1247` проверяет лишь `Payload != empty`, игнорируя `Type`. Та же путаница `GetProcessModules/ProcessModulesResponse` (одно имя на запрос и ответ).

### 4. Итог

| Ось | Оценка |
|---|---|
| ACL от сети/чужих сессий | **Хорошо** (`PipeSecurityFactory`) |
| Изоляция малвари того же юзера | **Отсутствует — критично**: любой процесс юзера = полный пульт `SYSTEM`-службы (удаление автозапуска, `kill`, firewall, пауза мониторинга, `InstallUpdate` под `SYSTEM`) |
| Стойкость к битым/гигантским пакетам, exhaustion 10 инстансов | **Слабо**: нет лимита размера строки, нет rate-limit, тяжелые сканы по запросу |
| Надежность UI при падении/рестарте службы | **Удовлетворительно**: зависаний нет, авто-reconnect обоих пайпов + ре-синк настроек есть; но `IOException` не конвертится в `Disconnected`, семафор один на всех, гонки `ConnectAsync` возможны |

**Минимум для закрытия:** лимит длины `ReadLine` (напр. 1–4 МБ, рвать соединение при превышении); `try` вокруг внешнего `Deserialize` с `continue` вместо разрыва; лимит параллельных тяжелых операций + кэш `GetAllEntries`; конвертить `IOException` в `SendAsync` в `Disconnected+null`; ввести проверку клиента (как минимум `GetImpersonationUserName` + разрешение только владельца сессии/админа) и защиту `IsProtectedSystemEntry` на `Disable/RemoveFromSystem`; убрать `InstallUpdate` из интерактивного API или требовать подтверждения админа; удалить `*Payload`-дубли и унифицировать `GetPortDefenderConfigResponse`.

