# Agent 4: Firewall Engine & Port Defender Core Audit

- Время анализа: 98.8 сек
- Статус: Успех

---

Провожу аудит Firewall и Port Defender — проверяю защиту от самоблокировки и устойчивость к сбоям.
Указанные пути не найдены — ищу фактические файлы модуля.
Нашел реальные файлы под другими именами — читаю их для аудита.
Основное ядро прочитано — теперь проверяю защиту приватных сетей, шлюза и RDP.
Ключевые риски уже видны — проверяю оставшиеся детали перед вердиктом.
# Аудит Firewall + Port Defender: вердикт и риски

> Уточнение по путям: файлов `FirewallGuardService.cs` / `PortDefenderCoreService.cs` / `PortDefenderEscalationService.cs` в репозитории **нет**. Аудит проведён по фактическим файлам:
> - `src/StartupMonitor.Service/Services/WindowsFirewallService.cs` (ядро Firewall, COM `HNetCfg.FwPolicy2`)
> - `src/StartupMonitor.Service/Services/Firewall/ProcessPortDefenderService.cs` (ядро Port Defender)
> - `src/StartupMonitor.Service/Services/Firewall/PortDefenderEscalation.cs` (политика эскалации)
> - `src/StartupMonitor.Service/Services/Firewall/SlidingWindowTracker.cs`
> - `src/StartupMonitor.Service/Services/Firewall/AutoUnbanWorker.cs`
> - `src/StartupMonitor.Shared/Models/Firewall/FirewallModels.cs`

# Вердикт: К ПУБЛИЧНОМУ РЕЛИЗУ НЕ ГОТОВ

Есть минимум 4 блокера уровня P0, при которых пользователь реально может потерять RDP-доступ к своему серверу или получить вечные «зомби»-правила в брандмауэре. Ниже — по пунктам.

---

## 1. Главные риски

### 🔴 P0-1. Защита от самоблокировки — ФИКТИВНА. `TrustPrivateNetworks` нигде не читается

`FirewallGuard.IsBlockableAddress` (`FirewallModels.cs:266`) запрещает только loopback, unspecified (`0.0.0.0`/`::`) и multicast/broadcast. **RFC 1918 (`192.168.x.x`, `10.x.x.x`, `172.16.x.x`) — явно разрешены**, комментарий прямо говорит: *«RFC 1918 разрешены, но вызывающий код должен логировать предупреждение»* — но **никто этого предупреждения не логирует**.

Дальше хуже. В конфиге есть флаг `TrustPrivateNetworks = true` по умолчанию (`FirewallModels.cs:431`) и чекбокс в UI (`PortDefenderConfigWindow.xaml.cs:61,134`). Но поиск по всему `src` показывает: **этот флаг не читается ни в одной строке `ProcessPortDefenderService.cs`**. В `ProcessSocketSnapshot` (стр. 235–294) есть только две проверки: `FirewallGuard.IsBlockableAddress` (режет лишь loopback) и per-rule `IsIpWhitelisted`. Глобальный флаг и глобальный `WhitelistIps` игнорируются.

Вспомогательная инфраструктура при этом существует и даже вводит в заблуждение:
- `ProcessNetworkSocket.CheckIsPrivateAddress` корректно определяет RFC 1918 + APIPA (`ProcessNetworkSocket.cs:59`);
- `ProcessNetworkTracker` заполняет `IsRemotePrivate` для каждого сокета;
- `FirewallUiMapper.GetSocketStatusText` (`FirewallUiMapper.cs:52`) показывает приватные сокеты в UI как **«Protected»** («под защитой, не банится»).

Итого: **UI говорит «защищён», а ядро тем временем банит**. Это прямое противоречие, а не просто缺失.

Отдельно:
- **Нет защиты шлюза, DNS, собственного внешнего IP и активного RDP-клиента** — вообще нет кода, который определяет «мой IP» / «IP текущей RDP-сессии».
- Дефолтное правило на порт 3389 (`ProtectedPorts = {3389}`) в связке с `ThresholdCount = 5/мин` означает: несколько RDP-переподключений администратора = бан собственного IP = **потеря доступа к серверу**. Спасает только ручной whitelist, который по умолчанию пуст.
- Ручной `BlockIp` через Pipe/API: `FirewallGuard` пропустит `192.168.1.1` (шлюз) и IP самого админа без единого warning, причём `Direction` по умолчанию `Both` — убьёт и исходящие.
- Все правила ставятся с `Profiles = 0x7FFFFFFF (ALL)` (`WindowsFirewallService.cs:471`) — бан бьёт и по Domain/Private-профилям, где живёт LAN и RDP. Опции «только Public» нет.

### 🔴 P0-2. Авто-разбан дырявый: рестарт = вечные «зомби»-правила

Механика в `ProcessPortDefenderService.cs:591` (`LoadActiveBans`):

```csharp
if (b.ExpiresAtUtc.HasValue && b.ExpiresAtUtc.Value <= now)
    continue;   // просроченные просто пропускаются
```

Пропущенные записи **не удаляются из firewall-правила**. Сценарий: бан на 15 мин → служба полежала 20 мин (обновление, краш, перезагрузка) → при старте бан «забыт», но IP остался в агрегированном правиле `StartupMonitor_Blocked_IPs_In` **навсегда**. Метод `GetBlockedIps()` (`WindowsFirewallService.cs:248`) существует, но **никто не вызывает его при старте для сверки (reconciliation)** — я проверил `Program.cs` и все вызовы: сверки нет.

Смежные дефекты:
- **Бан пишется в память даже при отказе firewall.** В `TriggerBan` (стр. 354–370): `fwResult = _firewall.BlockIp(...)` → результат только логируется, а `_activeBans[ip] = ban` выполняется безусловно. Рассинхрон «память vs firewall» в обе стороны.
- **Крах между записью firewall и записью JSON** (`SaveActiveBansToFile` идёт после `BlockIp`): правило есть, в файле бана нет → после рестарта это тоже зомби. `WriteAtomic` через `.tmp` + `Move` при краше посередине просто теряет новый файл целиком.
- **`UnbanIp` снимает только `Inbound`** (стр. 148–152), хотя ручной `BlockIp` по умолчанию `Both`. `ClearAllBans` идёт через тот же `UnbanIp` — Outbound-хвосты остаются. Для авто-банов (они всегда `Inbound`, стр. 348) это совпадает, но для ручных — нет.
- **Permanent-баны (`ExpiresAtUtc = null`)** игнорируются `ProcessExpiredBans` по построению (`Where HasValue`, стр. 190) — только ручной разбан, накопления без лимита. Агрегированное правило `RemoteAddresses` — это одна CSV-строка; у `INetFwRule.RemoteAddresses` есть практический лимит длины, при переполнении `rule.RemoteAddresses = ...` бросит COM-исключение → см. предыдущий пункт про рассинхрон.
- `ProcessExpiredBans → UnbanIp → SaveActiveBansToFile` на **каждый** бан — N перезаписей файла за один проход воркера (мелочь, но при сотнях банов заметно).

### 🔴 P0-3. Ложные срабатывания за NAT: один счётчик на IP

`SlidingWindowTracker.RecordConnectionAndCheckThreshold(remoteIp, ...)` — ключ **только IP, без порта** (`ProcessPortDefenderService.cs:279`). Плюс для детекта low-and-slow скана это хорошо, но обратный эффект: **весь офис / весь провайдер за одним NAT-IP — это один счётчик**. Десяток сотрудников, открывающих сайт на защищаемом порту, суммарно набирают `ThresholdCount` за минуту → бан всего офиса одной записью. Какой-либо NAT-эвристики, веса «соединений на порт» или исключения «много разных RemotePort = NAT, не флуд» — нет.

### 🟡 P1. SlidingWindow при 100 000 IP: OOM не будет, ослепление — будет

- **OOM-защита есть** (LRU-cap 10 000, `SlidingWindowTracker.cs:110,160`), за это плюс. Но при атаке со 100k spoofed-IP происходит **cache pollution**: атакующий вытесняет (`EvictLru`, стр. 274) записи легитимных/реально атакующих IP → порог для настоящего атакующего никогда не достигается. Классический evasion через вытеснение, никакой дифференциации «подозрительный vs новый» нет.
- **`Queue<DateTime>` на IP — без верхней границы.** Один IP с флудом при `WindowUnit.Hour` накапливает сотни тысяч `DateTime` в очереди (чистка только по истечению окна, стр. 124–127). Нужен cap вида `threshold × K`.
- **Двойное хранение**: `ConcurrentDictionary _ipTimestamps` (стр. 27) дублирует очередь, уже лежащую в `TrackerEntry.Timestamps`. Мёртвая структура, лишний расход памяти на 10k записей.
- **Смешанная модель синхронизации**: всё под глобальным `lock (_sync)`, при этом словарь — `ConcurrentDictionary` (бессмыслен под lock). При высоком PPS единый lock станет bottleneck; правильнее striped-locks / lock-per-key.
- **Дедупликация `_previousConnectionKeys`** (стр. 233, 246): ключ `"ip:port->port"` пересоздаётся каждый секундный тик; при SYN-flood с рандомными source-портами — десятки тысяч строковых ключей и хешей **каждую секунду** — CPU-налог на каждый опрос.
- **Слепая зона детекта**: учитываются только `ESTABLISHED`/`SYN_RCVD` (`IsIncomingTcpState`, стр. 435). UDP-флуд, SYN-flood без установленных соединений, сканирование `LISTENING`-портов единичными пакетами — мимо. Порт-скан «1 пакет на порт × 1000 портов» триггерит общий счётчик (см. P0-3), но бан атрибутируется первому сработавшему правилу (`TargetPort = rule.LocalPort`) — в `ActiveBanDto` будет неверный порт.

---

## 2. Недоделанное / заглушки в работе с firewall

| # | Место | Проблема |
|---|---|---|
| 1 | `AddressListContains`, `WindowsFirewallService.cs:855` | Только **точное строковое** сравнение. CIDR-диапазоны в чужих правилах (`192.168.0.0/24`) не матчится → `IsIpBlocked("192.168.0.5")` вернёт ложный `Allowed`. А `"*"` в чужом правиле трактуется как «заблокированы все» → ложный `Blocked` для любого IP |
| 2 | `NormalizeAddress`, стр. 840 | Не триммит скобки IPv6 (в отличие от `FirewallGuard`, который триммит). `"[2001:db8::1]"` пройдёт guard и запишется в правило со скобками — кривая запись |
| 3 | `FirewallGuard`, `FirewallModels.cs:266` | Маска CIDR **не валидируется**: `"10.0.0.1/99"` проходит guard и падает уже в COM с cryptic-ошибкой |
| 4 | `PortRuleName`, стр. 877 | Scope = `FileNameWithoutExtension`: `C:\A\app.exe` и `D:\B\app.exe` дают **одно имя правила** → второе правило молча не создаётся (`Upsert` находит первое и ставит `Enabled=true`) |
| 5 | `UpsertSingleIpRule`, стр. 534 | Существующее правило только включается; `Reason`/`Description` не обновляется |
| 6 | `FindRule` / `EnumerateRules`, стр. 672/717 | Полный линейный перебор COM-коллекции **на каждую операцию**, под тем же `_sync`-lock. При сотнях правил — медленно; «битые» правила глотаются пустым `catch {}` (стр. 732) без диагностики |
| 7 | Нормализация подсетей | `"10.0.0.1/24"` и `"10.0.0.0/24"` — разные ключи/записи, дубли в агрегированном правиле |
| 8 | Архитектура | Только late-bound COM `INetFwPolicy2`. **Fallback на `netsh` при недоступном COM — отсутствует** (только `Fail`). Чужие перекрытия портов (`LocalPorts = "RPC"`, диапазоны) `IsPortBlocked` не видит — проверяет только правила по своему имени |

---

## 3. Лишённые смысла / вредные абстракции

1. **`PortDefenderEscalation` (целый файл, 193 строки) — ядром не используется.** `TriggerBan` использует собственный `_repeatHistory` + собственный `CalculateEscalatedDuration` (`ProcessPortDefenderService.cs:412`). И формулы **различаются**: класс даёт 2-й бан `base×2`, 3-й `base×4`, 4-й фикс 24ч; static-метод — `max(base×2,30)`, `max(base×4,60)`, `max(base×8,1440)`. При `base != 15` результаты разные. Два источника истины + `BanDecision`/`BanDurationResult`-рекорды, живые только ради мёртвого класса. Тесты (`PortDefenderEscalationTests`) закрепляют обе версии — расхождение «узаконено».
2. **`WindowSeconds` (`FirewallModels.cs:419`) — мёртвое поле.** `GetTimeSpanForUnit` (стр. 427) его игнорирует, `Second` захардкожен как 1 с. Окно 30 с / 5 мин задать нельзя.
3. **Алиасы-дубли в DTO**: `MaxAttempts`/`ThresholdCount`, `Enabled`/`IsGloballyEnabled`, `BanDurationMinutes`/`DefaultBanDurationMinutes`, `TargetPort`/`Port`, `BanStartedUtc`/`BannedAtUtc`, `AttackRate`/`AttemptCount` — оба свойства сериализуются в JSON (два поля на одно значение), путают контракт Pipe/IPC.
4. **`ProtectedPorts` (default `{3389}`) — мёртвое поле.** Ядро смотрит только на `Rules[]`, а `LoadConfig` по умолчанию создаёт `Rules = []` (стр. 570–575). То есть «из коробки» при `Enabled=true` **защиты фактически нет** — прямое противоречие дефолтам.
5. **`GetConfig` теряет поля** (`ProcessPortDefenderService.cs:88–108`): возвращает только `IsGloballyEnabled`, `DefaultBanDurationMinutes`, `Rules`. `ProtectedPorts`, `MaxAttempts`, `TrustPrivateNetworks`, глобальный `WhitelistIps` — отбрасываются. UI показывает дефолты вместо реальных значений; roundtrip-тест (`PortDefenderCoreServiceTests.Config_GetAndSave`) проходит лишь потому, что проверяет 3 поля.
6. **`TcpSocketInfo : ProcessNetworkSocket`** — тонкая обёртка ради одного `ProcessPath`; а перегрузка `ProcessSocketSnapshot(IReadOnlyList<ProcessNetworkSocket>)` (стр. 300) конвертирует **с потерей `ProcessPath`** → привязка правила к процессу молча не срабатывает.
7. `AutoUnbanWorker` как отдельный `BackgroundService` — **оправдан**, не лишний. Но в модуле уживаются две модели фона (Timer опроса сокетов внутри сервиса + хостед-воркер) — приемлемо, фиксирую как наблюдение, не дефект.

---

## 4. Готовность к релизу

| Требование | Статус |
|---|---|
| Не забанить LAN / шлюз / себя по RDP | ❌ `TrustPrivateNetworks` не enforced, UI врёт («Protected»), RDP-клиент не исключён |
| Бан снимается сам даже при краше/рестарте | ❌ зомби-правила (нет reconciliation), рассинхрон память↔firewall, `UnbanIp` только Inbound |
| Держит 100k IP без деградации защиты | ⚠️ OOM не будет, но evasion через LRU-pollution + безлимитные очереди + единый lock |
| Корректный детект (без ложных банов за NAT, без слепых зон UDP/SYN) | ❌ общий счётчик на IP, только ESTABLISHED/SYN_RCVD |
| Один источник истины эскалации, честный контракт DTO | ❌ две формулы, мёртвые поля, дубли-алиасы в JSON |

**Что исправить до публичного релиза (минимум):**
1. Enforce `TrustPrivateNetworks` + глобальный whitelist в `ProcessSocketSnapshot`; починить `GetConfig`/`SaveConfig` (все поля); добавить защиту шлюза/DNS/IP текущей RDP-сессии; дефолтный whitelist RFC 1918 или явный opt-in бана LAN с предупреждением в UI.
2. Reconciliation при старте: сверить `GetBlockedIps()` с `firewall-bans.json`, снять просроченные/осиротевшие; писать бан в память только при `fwResult.Success`; `UnbanIp` — оба направления (или хотя бы то, что банили).
3. Удалить **или** внедрить `PortDefenderEscalation` (одна формула), удалить мёртвые поля (`WindowSeconds`/`ProtectedPorts`/алиасы или заставить их работать), cap очереди трекера, лимит длины агрегированного правила с фолбэком на одиночные.
4. CIDR-матчинг в `IsIpBlocked`, уникальные имена правил для портов (полный путь/хеш), валидация маски CIDR в `FirewallGuard`.

Без пунктов 1–2 это инструмент, которым администратор с высокой вероятностью отрежет себе доступ к собственному серверу при первом же ложном срабатывании — в публичный доступ такое отдавать нельзя.

