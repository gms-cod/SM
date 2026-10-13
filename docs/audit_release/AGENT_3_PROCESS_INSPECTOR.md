# Agent 3: Process Inspector, Metrics & Win32 P/Invoke Safety Audit

- Время анализа: 189.2 сек
- Статус: Успех

---

Принято — проведу глубокий аудит безопасности и стабильности диспетчера процессов и сокетов.
Первые четыре сервиса разобраны, дочитываю связанные проверки защиты и сокетов.
# Аудит диспетчера процессов и сетевых сокетов

Прочитаны все 5 целей + `EntryOperations.cs`, `ServiceProcessHelper.cs`, `SystemProtectionHelper.cs`. Уточнение: `ProcessInspectorService.cs` лежит в `Services\ProcessInspector\ProcessInspectorService.cs`, а не в корне `Services`.

## 1. Защищённые процессы, дескрипторы, таблицы TCP/UDP

### 1.1. PPL / Anti-Cheat / Антивирусы — падения нет, но покрытие дырявое

Общая картина: код **не падает** на защищённых процессах, но молча отдаёт неполные данные.

**`ProcessMetricsTracker.cs:218,236-257`:**
- `OpenProcess(0x1000 QUERY_LIMITED, pid)` на PPL (`Lsass`, `MsMpEng`, `csrss`, `services`, `Memory Compression`, `System` PID 4), Vanguard (`vgc/vgk`), EAC (`EasyAntiCheat.exe`), BattlEye вернёт `NULL` (`ERROR_ACCESS_DENIED=5`). Ветка `else → EnrichOrQueue(info, Zero)` корректна.
- `proc.TotalProcessorTime / StartTime` бросают `Win32Exception` — кэшируются в `_inaccessiblePids:244-256`. Это хорошее решение, повторных исключений нет.
- **Баг повторного использования PID:** `_inaccessiblePids` keyed только по `int pid`, без `StartTime`. Если PID переиспользован новым процессом (Windows агрессивно переиспользует PID из пула), новый процесс навсегда останется с `Cpu=0, StartTime=MinValue`, пока старый PID числится в `activePids`. У `ProcessMetadataEnricher.cs:34` кэш правильно keyed `(Pid, StartTimeTicks)` — надо так же. Плюс race: между `Process.GetProcesses()` и чтением свойств процесс может умереть — `catch {}` глотает, ок.
- `proc.WorkingSet64:190` и `proc.ProcessName:189` тоже могут бросить на PPL/умершем процессе — они **вне** `try` для `TotalProcessorTime`, но внутри внешнего `try:169` с пустым `catch:324`. Т.е. весь процесс просто выпадет из снимка на этот тик. Не критично, но даёт мерцание строк в UI.
- Массовый `OpenProcess` по ~250 PID каждые 1.5 сек — это именно тот паттерн, который EDR/Vanguard/Defender считают сканированием. `ObRegisterCallbacks` у античита просто обрежет права, но телеметрия останется. Защита от бана: вы уже минимизировали права до `QUERY_LIMITED`, это правильно, не повышать до `QUERY_INFORMATION|VM_READ`.

**`ProcessMetadataEnricher.cs:301-330` (`NtQueryInformationProcess`, class 60):**
- На PPL вернёт `STATUS_ACCESS_DENIED (0xC0000022)`, `returnLength<=0` → `string.Empty`. Безопасно.
- Тонкость: первый вызов с `IntPtr.Zero, 0` для узнавания длины — недокументированный, но рабочий трюк для `ProcessCommandLineInformation`. На некоторых сборках Win10 возвращает `STATUS_INFO_LENGTH_MISMATCH`, на других `STATUS_BUFFER_TOO_SMALL` — оба дают `returnLength`, код это переживёт. `uniStr.Buffer` указывает внутрь вашего же `buf`, копирование `PtrToStringUni` до `FreeHGlobal` — корректно.

**`ProcessInspector/ProcessInspectorService.cs:88-131,252-256`:**
- `CreateToolhelp32Snapshot(SNAPMODULE|SNAPMODULE32, pid)` на PPL/x64-vs-x86/защищённом AV вернёт `INVALID_HANDLE`. Фоллбэк `proc.Modules` тоже бросит `Win32Exception (0x00000005 / ERROR_PARTIAL_COPY для WoW64)` → глотается → пустой список **без объяснения**. Пользователь видит «0 DLL» и думает, что процесс пуст. Надо прокидывать `ErrorMessage = "PPL/Protected, требуется SYSTEM+DebugPrivilege"`.
- `CollectHandles:252` — `OpenProcess(PROCESS_DUP_HANDLE=0x40)` на PPL закономерно `NULL` → пустой список. Это правильно (иначе `DuplicateHandle` на LSASS был бы дырой). Хорошо, что `NtQuerySystemInformation(64)` вызывается только on-demand, а не в тике 1.5 сек.
- `QueryHandleDetails:345-378` — грамотная защита от зависания: `GetFileType` сначала, `PIPE (3)` и `CHAR (2)` не трогаем `GetFinalPathNameByHandle`/`NtQueryObject(Name)`. Лимит `maxHandlesToReturn=1000` + watchdog `1500ms:291` — правильно.

> Вывод: BSOD/детект античита маловероятен, т.к. нет драйверов, только user-mode API с минимальными правами. Но Vanguard/EAC могут залогировать массовый опрос как suspicious. Хуже другое — см. п. 1.2.

### 1.2. Утечки дескрипторов: в целом чисто, но есть один double-free

Проверил все пары Alloc/Free, Open/Close:

| Место | Вердикт |
|---|---|
| `ProcessMetricsTracker.cs:218-233` `OpenProcess/CloseHandle` в `try/finally` | чисто |
| `ProcessMetricsTracker.cs:327` `proc.Dispose()` в `finally` параллельного цикла | чисто |
| `ProcessStartupLinker.cs:254-281` `CreateToolhelp32Snapshot/CloseHandle` | чисто |
| `ServiceProcessHelper.cs:88-119` `AllocHGlobal/FreeHGlobal`, `OpenSCManager/CloseServiceHandle:127` | чисто |
| `ProcessMetadataEnricher.cs:255-264`, `310-326` | чисто |
| `ProcessNetworkTracker.cs:86-145,158-217,229-284,337-531` все 8 методов таблиц | чисто (нет realloc, ранний `return` до alloc) |
| `ProcessInspectorService.cs:404-451` `GetObjectTypeName/Name` | чисто |

**Критичный баг — `ProcessInspectorService.cs:263-276,319-322`:**
```csharp
IntPtr ptr = AllocHGlobal(length);
try {
  while (NtQuerySystemInformation(...) == MISMATCH) {
    Marshal.FreeHGlobal(ptr);          // 1. освободили
    length = ...;
    ptr = Marshal.AllocHGlobal(length); // 2. если тут OOM -> throw
  }
} finally { Marshal.FreeHGlobal(ptr); } // 3. double-free старого адреса
```
Если повторный `AllocHGlobal` бросит `OutOfMemoryException` (реально при 50-150К хендлов системы и фрагментации), `ptr` всё ещё указывает на уже освобождённую память, `finally` освободит её второй раз → повреждение кучи процесса службы. Фикс:
```csharp
Marshal.FreeHGlobal(ptr); ptr = IntPtr.Zero;
ptr = Marshal.AllocHGlobal(length);
...
finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
```
Рекомендация шире: заменить все `IntPtr + CloseHandle` на `SafeHandle` (`SafeProcessHandle`, `SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid`). Сейчас при `ThreadAbort`/`StackOverflow` между `Open` и `try` хендл потеряется. В .NET 8 это узкое окно, но для службы, опрашивающей 250 процессов/сек, статистически значимо.

### 1.3. `GetExtendedTcpTable / GetExtendedUdpTable` — работает, но с 3 дефектами

Константы верные: `AF_INET=2, AF_INET6=23, TCP_TABLE_OWNER_PID_ALL=5, UDP_TABLE_OWNER_PID=1`. Структуры `MIB_TCPROW_OWNER_PID`, `MIB_TCP6ROW_OWNER_PID`, `MIB_UDPROW_OWNER_PID`, `MIB_UDP6ROW_OWNER_PID` по порядку полей совпадают с `iphlpapi.h`. `ParsePort:536-537` (swap сетевого порядка) верный. `ParseIpv4:542-545` через `new IPAddress((long)dw)` — стандартный рабочий идиом. `ParseIpv6` через `new IPAddress(bytes, scopeId)` — верный.

Дефекты:

**a) Нет retry на `ERROR_INSUFFICIENT_BUFFER` (122). `ProcessNetworkTracker.cs:83-92,155-164,226-232,332-341` и др.**
Двухфазный вызов `NULL → size → Alloc → Get` гоночный: между вызовами таблица выросла (торрент/браузер открывают десятки сокетов/сек), второй вызов вернёт 122, код делает `if (res != 0) return;` → на этот тик `socketCounts` пустые → мерцание `ActiveSocketsCount 0 → N → 0`. Нужен цикл до 3 попыток с реаллоком.

**b) Ключ IPv6-дельты коллизирует. Строка 198:**
```csharp
string key = $"6:{row.dwLocalPort}-{row.dwRemotePort}";
```
Адреса выкинуты. Два разных IPv6-пира с одинаковыми портами перезапишут друг друга в `_prevSocketBytes`, дельта припишется не тому PID. IPv4-ключ (строка 126) лучше (`4:local:port-remote:port`), но тоже без PID: при `SO_REUSEADDR` / быстром переиспользовании 4-tuple новым процессом дельта старого припишется новому. Ключ обязан быть `$"{pid}:{local}-{remote}"` или использовать `GetHashCode` кортежа.

**c) `SetPerTcpConnectionEStats + GetPerTcpConnectionEStats` на каждом ESTABLISHED каждый тик (`117,120,190,192`).**
Это самый дорогой кусок тика: 2 syscall на соединение × 100-200 соединений = 200-400 переходов в ядро каждые 1.5 сек. `Set...` идемпотентен, но дёргать его каждый тик бессмысленно — достаточно один раз (кэшировать множество уже включённых). Хуже: без прав администратора `Get...` вернёт `ERROR_ACCESS_DENIED`, `getRes != 0` молча игнорируется → `NetworkBytesPerSec` всегда 0, а `HasNetworkActivity` для UDP всегда `false` (`ProcessMetricsTracker.cs:212` — UDP только увеличивает `socketCounts`, в `accumulatedBytesByPid` не попадает). Служба у вас `LocalSystem` — ок, но если трекер переиспользуют из UI-процесса без elevation, график сети мёртв. Плюс на части сборок ESTATS вообще отключён политикой → тоже нули. Нужен фоллбэк (PerfCounter `\TCPv4\Segments/sec` / ETW) или хотя бы лог.

Мелочь: `ParseIpv4/ParseIpv6/GetTcpStateString` обросли мусором автогенерации XML-доков (`///<summary>IPAddress...` внутри тела метода, строки 543-544, 552-553) — компилируется, но пахнет сломанным генератором доков, при следующей регенерации затрет.

## 2. `ProcessStartupLinker` — недоделан, в проде даст ложные связи

**Мёртвый код:** `_serviceNameToEntryMap` строится в `UpdateIndex:38,50`, но в `LinkProcess:56-94` **нигде не читается**. Для служб (`svchost.exe`, `services.exe`) остаётся только путь №1 и имя №2.

Что это значит на практике:

1. **Все `svchost` сливаются в одну запись.** `pathMap[norm]=entry` — last-wins. 20 процессов `C:\Windows\System32\svchost.exe -k netsvcs -p -s Dnscache / Dhcp / ...` имеют одинаковый `FilePath`, но разные `-s <ServiceName>`. Линкер всем им выставит один и тот же `StartupEntryId` — последний записанный в словарь. Правильно: матчить `servicePidMap` (уже есть в `ProcessMetricsTracker.cs:142`) + `_serviceNameToEntryMap` по имени службы из `sc qc` / `EnumServicesStatusEx`, а не по пути хоста.
2. **Совпадение по голому имени (`69-74`) без сверки пути — источник false-positive.** `_nameToEntryMap` keyed по `entry.Name` (имя значения Run / имя задачи — часто `"Steam"`, `"Discord"`, `"svchost"`). Сравнение с `proc.ProcessName` без проверки пути: малварь `C:\Users\x\AppData\Local\Temp\svchost.exe` сматчится на легитимную Run-запись `"svchost"` и получит `IsStartupEntry=true` с чужим `StartupEntryId`. А `SetStartupInfo:117-120` ещё и **подменит пустой `CommandLine` процесса командной строкой записи автозапуска** — это уже фальсификация телеметрии, эвристика `CliHeuristicAnalyzer` дальше будет анализировать чужую строку.
3. **`NormalizePath:128-155`** режет по первому `".exe "` + `ExpandEnvironmentVariables` + `GetFullPath`. На `rundll32.exe shell32.dll,OpenAs_RunDLL x` даст голый `rundll32.exe`, а `GetFullPath("rundll32.exe")` резолвит относительно рабочей папки службы (`C:\Windows\System32` для SYSTEM — повезёт, для обычного юзера — `C:\Users\...` → мимо). `svchost -k ... -s ...` вообще не разбирается на `-s`. UWP/Store (`ApplicationFrameHost`, `WWAHost`, `ShellExperienceHost`) с пустым `FilePath` (PPL) никогда не сматчатся по пути.
4. Нет учёта аргументов, издателя, хеша при коллизии имён хостов. `IsHostProcess` (`svchost/rundll32/dllhost/cmd/powershell...`) уже есть в `SystemProtectionHelper.cs:31-34` и правильно запрещает Path-правила ЧС для хостов — линкер должен делать так же: для хостов требовать совпадения `CommandLine`/`-s`/`TaskPath`, а не пути.

Итого: для простых Run-записей с полным путём (`C:\Program Files\...\app.exe`) связь надёжна (O(1) FrozenDictionary — хорошее решение, lock-free подмена в `UpdateIndex:49-51` корректна). Для служб, задач с аргументами, `rundll32/dllhost/svchost`, Store-приложений — **ненадёжна**. В релиз с бейджем «из автозапуска» на основе только имени — нельзя, будет вводить юзера в заблуждение и может отбелить малварь.

## 3. Опрос каждые 1.5 сек — где жгутся такты

Хорошее: On-Demand (`ProcessMetricsTracker.cs:63,68-85` — таймер `Infinite` без подписчиков, `Subscribe/Unsubscribe` со счётчиком, `_isCollecting` от реентрантности). В простое на трее — 0% CPU от этого модуля. `CaptureSnapshot()` для разового `GetProcesses` — тоже правильно.

Плохо — цена одного тика, когда вкладка открыта:

- `Process.GetProcesses()` (~250 объектов) + `Parallel.ForEach 2-6 потоков:167`: на процесс — `ProcessName, WorkingSet64, SessionId` (каждый — отдельный syscall + `OpenProcess` внутри .NET), затем **свой** `OpenProcess + GetProcessIoCounters + QueryFullProcessImageName + NtQuery(PBI parent) + TryGetCommandLine(NtQuery class 60)`, затем снова .NET `TotalProcessorTime + StartTime` (каждый — ещё один `OpenProcess` внутри). Итого 4-5 открытий процесса на PID на тик. Плюс `EntryOperations.IsProcessSuspended:320` — сейчас это просто `ConcurrentDictionary.ContainsKey` (дёшево, ок), но `GetSuspendedPids().ToList()` + `PruneDeadProcesses` + `_history` чистка — три полных прохода по словарям каждый тик.
- `CaptureNetworkBandwidth:160` — 2×TCP-таблица + N×(Set+Get ESTATS) + 2×UDP-таблица. Самый тяжёлый кусок. `Set` каждый тик — лишний.
- `ServiceProcessHelper.GetRunningServicesByPid:142-144` каждые 4 сек — `EnumServicesStatusEx` (~10 мс), терпимо, кэш правильный.
- `GetProcessSnapshotMap:164` (Toolhelp всего дерева) каждый тик — ~1 мс, дёшево и правильно (покрывает PPL без `OpenProcess`).
- `EnrichOrQueue:48` + `CliHeuristicAnalyzer.Analyze` дважды (в Enricher:103 и в Linker:88) на каждый новый процесс — строковые эвристики на каждый тик, пока путь не закэшируется.

Оценка: ~30-80 мс CPU на тик на 6-ядернике (пик 2-5% одного ядра), среднее ~0.3%. Для аналога Диспетчера задач — приемлемо. Но если `GetProcesses` по IPC дёргается чаще 1.5 сек (UI-поллинг), каждый вызов — полный `CollectSnapshotCore` с ESTATS. Проверьте частоту IPC; если надо — добавьте троттлинг/кэш снимка на ~1 сек.

Быстрые победы без потери точности:
1. Кэшировать `SetPerTcpConnectionEStats` (множество включённых 4-tuple), вызывать `Set` только для новых.
2. Поднять интервал до 2000 мс и сделать адаптивным (пауза при свёрнутом окне уже есть — проверить, что `Unsubscribe` реально вызывается).
3. `_history`/`_inaccessiblePids` keyed `(pid, startTime)` + не дёргать `TotalProcessorTime` для PID из `snapshotMap`, но отсутствующих в `Process.GetProcesses` (гонка).
4. Один `OpenProcess(PROCESS_QUERY_LIMITED|PROCESS_VM_READ?)` на PID и переиспользование хендла внутри итерации вместо 3-4 открытий (сейчас .NET `Process` открывает свой, вы — свой).
5. `CliHeuristicAnalyzer` — один раз (в Enricher), из Linker убрать дубль.

## 4. Можно ли в релиз + что чинить

**Вердикт: нет, не в текущем виде.** Стабильность (не падает на PPL) — да. Корректность данных (чьи сокеты, чей автозапуск, сколько CPU) — нет. Три блокера релиза помечены ★.

**Блокеры (до релиза обязательно):**
1. ★ `ProcessNetworkTracker.cs:198` — ключ IPv6 без адресов. Потеря атрибуции трафика, неверный PID в UI. Фикс: ключ `pid+local+remote+ports`.
2. ★ `ProcessStartupLinker.cs:68-74,112-125` — матчинг по голому имени + подмена `CommandLine` из записи. Риск отбеливания малвари и фальсификации телеметрии. Фикс: для хостов (`IsHostProcess`) запретить матч по имени без совпадения пути/аргументов; `SetStartupInfo` никогда не перезаписывать непустой/пустой `CommandLine` процесса данными записи (только отдельное поле `StartupCommandLine`); задействовать `_serviceNameToEntryMap` через `servicePidMap`.
3. ★ `ProcessInspectorService.cs:271-276` — double-free при OOM в росте буфера хендлов. Редко, но это повреждение кучи службы SYSTEM. Фикс выше + `SafeHandle`.
4. `ProcessMetricsTracker.cs:46,244` — `_inaccessiblePids` без `StartTime` → вечные нули CPU после реюза PID. Фикс: ключ `(pid, startTicks)`.

**Настоятельно (следующий спринт):**
5. Retry ×3 на `ERROR_INSUFFICIENT_BUFFER` во всех 8 вызовах `GetExtendedTcp/UdpTable`.
6. Кэш `ESTATS enable`, иначе лишние 200-400 syscall/тик; фоллбэк, когда ESTATS недоступен (иначе нули сети без объяснения).
7. Развести `FilePath` процесса и `StartupEntryId`: все `svchost -k -s X` сейчас сливаются. Матчить службы по имени службы, задачи — по `TaskPath`, `rundll32/dllhost` — по `CommandLine`.
8. В `CollectModules` возвращать `ErrorMessage` для PPL (`AccessDenied: требуется SYSTEM + SeDebugPrivilege`), а не пустой список.
9. Убрать дубли `CliHeuristicAnalyzer` (оставить в Enricher), почистить мусорные XML-доки в `ParseIpv4/ParseIpv6`.
10. Проверить, что UI реально вызывает `Unsubscribe` при уходе с вкладки — иначе «On-Demand 0%» на бумаге, а по факту вечные 1.5-сек тики + ESTATS-шторм.

**Что уже хорошо и трогать не надо:** lock-free `FrozenDictionary` подмена индекса, Toolhelp-снимок ParentPID/ThreadCount без `OpenProcess` (покрывает PPL), watchdog 1500 мс + лимит 1000 хендлов, `GetFileType`-гейт перед `NtQueryObject`, `QUERY_LIMITED` вместо `QUERY_INFORMATION`, `CloseHandle`/`FreeHGlobal` в `finally` почти везде, самозащита `IsProtectedProcessName/IsCoreWindowsService` в kill/disable путях.

Если сроки жмут: минимальный патч до релиза — 4 фиксa выше (ключи IPv6, запрет имя-матчинга для хостов + не подменять CommandLine, Zero-guard для ptr, ключ inaccessible по старту) — это ~40 строк, остальное — следующим релизом с пометкой «сеть/автозапуск для служб — beta».

