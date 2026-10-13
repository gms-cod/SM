# OpenCode Swarm Agent 6: Deep Glitch & Edge Case Bug Hunter (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 142.2 сек
- Статус: Успешно

---

Глубинный аудит безопасности — читаю целевые модули.
Файл AutoGuardEngine не найден по указанному пути — ищу фактическое расположение.
Нашёл реальные пути — добираю контекст по гонкам, правам и персистентности настроек.
# QA-аудит отказоустойчивости: ТОП-10 критических глюков

Изучены 5 целевых файлов (путь `AutoGuardEngine.cs` в репозитории — `Services\Guards\AutoGuardEngine.cs`, а не `Services\AutoGuards\`), плюс смежные `EntryOperations.cs`, `WatcherHelper.cs`, `UserSessionLauncher.cs`, `MainViewModel.cs` (настройки).

---

## 1. CRITICAL — Пустое правило Publisher/SHA256 = match-all, снос всего автозапуска
`BlacklistManager.cs:123-127`:
```csharp
BlacklistMatchType.Publisher => string.Equals(entry.Publisher, rule.Value, OrdinalIgnoreCase),
BlacklistMatchType.SHA256 => string.Equals(entry.SHA256, rule.Value, OrdinalIgnoreCase),
```
Нет проверки на пустое `rule.Value`. `string.Equals(null, null) == true` — правило с пустым значением совпадёт с **любой необогащённой записью** (Publisher/SHA256 ещё null). В связке с `ApplyBlacklistAction` (`NotifyAndRemove` → `KillProcess` + `RemoveFromSystem`) это массовое удаление легитимного автозапуска, включая апдейтеры Chrome/Edge и антивирусы.
- **Воспроизведение:** создать правило Publisher с пустым `Value` → перезапустить службу → все новые записи без издателя блокируются/удаляются.
- **Патч:**
```csharp
if (string.IsNullOrWhiteSpace(rule.Value)) return false;
// и в ветках Publisher/SHA256:
Publisher => !string.IsNullOrWhiteSpace(entry.Publisher) && string.Equals(...),
SHA256 => !string.IsNullOrWhiteSpace(entry.SHA256) && string.Equals(...),
```

## 2. CRITICAL — `KillProcessesByRules` убивает по голому имени при недоступном пути
`EntryOperations.cs:727-736`:
```csharp
var testPath = procPath ?? (proc.ProcessName + ".exe");
var matched = (procPath != null && MatchesFilePath(rule, procPath))
              || MatchesFilePath(rule, testPath);
```
Если путь процесса недоступен (защищённый/системный, `TryGetProcessPath` вернул null), второй дизъюнкт матчит wildcard-правило `*chrome.exe` против голого `"chrome.exe"`. Легитимный Chrome в `Program Files` убивается правилом, нацеленным на `C:\Bad\chrome.exe`.
- **Воспроизведение:** правило PathWildcard `*updater.exe`, процесс с запретом на чтение пути → kill чужого апдейтера.
- **Патч:** если `procPath == null` — пропускать Path/PathWildcard-правила (кроме bare-name правил без `\` и `*:` явно задокументированных), никогда не матчить wildcard по `ProcessName + ".exe"`.

## 3. CRITICAL — `KillProcessByImage` вернулся к `MainModule` (AccessDenied-шторм + риск чужого тёзки)
`EntryOperations.cs:636`: `procPath = proc.MainModule?.FileName` — бросает `Win32Exception` на защищённых процессах и медленно. В том же файле уже есть быстрый путь через `QueryFullProcessImageName` (`ProcessMetadataEnricher.TryGetProcessPath`), использованный в `KillProcessesByRules:727`. Два расходящихся механизма убийства процессов.
- **Воспроизведение:** правило на `opera.exe`, запущен SYSTEM-процесс с тем же именем → `MainModule` падает → `continue` (повезло), но на доступном тёзке в другой папке без `fullPath` (вызов через `CommandLine`-ветку `KillProcess:679-684`) убьёт чужой процесс.
- **Патч:** унифицировать на `TryGetProcessPath`; при недоступности пути — не убивать; `GetProcessesByName` + сверка полного пути обязательна всегда.

## 4. HIGH — AutoGuard `GuardBlockUnsigned` блокирует до завершения верификации подписи
`AutoGuardEngine.cs:241-263`: `IsUnsignedOrUntrusted` возвращает `true` для **любого** `SignatureStatus != Valid`, включая `Unknown/NotChecked` (запись ещё не обогащена). Плюс `IsWhitelisted:146` пропускает только `Valid + Microsoft + Windows-dir`. Итог: при включённой политике легитимный неподписанный драйвер-апдейтер, portable-утилита или просто ещё непроверенный файл уходит в `EnforceBlock` → `KillProcess` + `RemoveFromSystem` (`WatcherHelper.cs:186-187`).
- **Воспроизведение:** включить `GuardBlockUnsigned + EnforceBlock`, положить в Run новый exe без подписи (или с медленной проверкой `WinVerifyTrust`) → мгновенная нейтрализация до появления статуса.
- **Патч:** при `SignatureStatus == Unknown/NotChecked` возвращать `PolicyWarning` (audit), а не блок; `EnforceBlock` только для `NotSigned/InvalidSignature/UntrustedRoot/Revoked`; добавить allowlist издателей (Google, Mozilla, Kaspersky, производители драйверов).

## 5. HIGH — `IsScriptAutostart`/`IsTempOrPublicLocation`: сабстринг-матчинг даёт ложные срабатывания
`AutoGuardEngine.cs:165-190, 221-233`:
- `cmd.Contains(".js")` матчит `--json`, `my.js.backup\app.exe`; `.Contains(".bat")` — `battery`; `powershell && -file` — легитимные админ-скрипты;
- `lower.Contains(tempDir)` матчит любую строку аргументов, содержащую путь temp (URL, лог-путь).
- **Воспроизведение:** автозапуск `app.exe --json=config` + `GuardBlockScripts` → ложный варнинг/блок. Апдейтер с кэшем в `AppData\Local\Temp` → блок обновления.
- **Патч:** парсить расширение **целевого exe** через `Path.GetExtension` + границы слов (`\b`) для интерпретаторов; temp-проверять только `FilePath` (не весь `CommandLine`), нормализовав через `Path.GetFullPath` + `StartsWith` вместо `Contains`.

## 6. HIGH — Watcher: удаление трекинга даже при провале `RemoveFromSystem` (рассинхрон UI↔ОС)
`WatcherHelper.cs:107-122`: `store.RemoveEntry(entry.Id)` вызывается **безусловно**, а `AddRemovedEntry` с пустым payload — даже если `ok == false`. Запись исчезает из UI, но остаётся в реестре/планировщике → «призрак»: UI показывает чистоту, ОС грузит программу; восстановление из пустого payload — мусор.
- **Воспроизведение:** заблокированная запись, `RemoveFromSystem` вернул `false` (нет прав на ключ HKLM) → запись пропала из списка, ключ на месте.
- **Патч:**
```csharp
if (!ok) { store.UpdateEntry(entry); NotifyExternalEntriesChanged(); return false; }
// RemoveEntry / AddRemovedEntry только при ok == true
```

## 7. HIGH — Session 0/1: молчаливый «успех» запуска в невидимой сессии + неверный токен
`UserSessionLauncher.cs:18-20, 155-164, 223-233`:
- `SecurityIdentification = 2` — неверное имя (2 = `SecurityImpersonation`, работает случайно);
- при `activeSessionId == 0` (выход из системы/RDP-отключение) код падает в `Process.Start` фолбэк **от SYSTEM в Session 0** — процесс «запущен», PID возвращён, но UI невидим;
- `WTSGetActiveConsoleSessionId` не покрывает Fast User Switching / несколько RDP-сессий.
- **Воспроизведение:** Delayed-запуск при заблокированной машине без активной консоли → приложение висит в Session 0, пользователь его не видит.
- **Патч:** переименовать в `SecurityImpersonation`; при `sessionId == 0/0xFFFFFFFF` возвращать `(false, "нет интерактивной сессии")` вместо фолбэка SYSTEM; перебирать сессии через `WTSEnumerateSessions` + `WTSActive`.

## 8. HIGH — `RestoreService` необратимо портит службы (потеря DisplayName, delayed-auto, кавычек)
`EntryOperations.cs:481, 506`: `new ServicePayload(svcName, entry.FilePath, GetServiceStartType(svcName), null)` — `DisplayName` всегда null; `GetServiceStartType` схлопывает `delayed-auto` в `auto`; `sc create binPath="..."` без экранирования внутренних кавычек ломается на службах с аргументами. Зависимости, SID, failure-actions не сохраняются вообще.
- **Воспроизведение:** удалить → восстановить службу с `binPath="C:\App\svc.exe" --service` → `sc create` падает или служба стартует не так.
- **Патч:** сохранять `DisplayName` (через `GetServiceConfig`), `DelayedAutoStart`, зависимости; при restore использовать `sc create` + `sc config`/`sc failure`/`sc sidtype`; кавычки экранировать; при неудаче — честный `(false, err)`, а не молчание.

## 9. MEDIUM — Потеря `ui-settings.json`: неатомарная запись + двойной `StartAutoReport` + `Global\` мьютекс
- `MainViewModel.cs:2030-2047`: `SaveSetting` — `ReadAllText → Deserialize<Dictionary<string,object>> → WriteAllText`. Крэш/убийство процесса между чтением и записью или два параллельных писателя (два экземпляра UI в момент передачи мьютекса) → обрезанный JSON → `LoadSetting` ловит исключение и **молча возвращает дефолты**: слетают тема, язык, AI-ключ, автозапуск (`App.xaml.cs:97-102`, `MainWindow.xaml.cs:63-67`). Плюс запись вызывается на каждую навигацию (`NavigateToPage:593`).
- `App.xaml.cs:112-114` и `151-153`: `StartAutoReportAsync` запущен **дважды** → двойной таймер, двойной спам в `ui-debug.log` каждые 5 мин.
- `App.xaml.cs:55-56`: `Global\StartupMonitorUI_...` из-под обычного пользователя без `SeCreateGlobalPrivilege` может кинуть `UnauthorizedAccessException`; `FindWindow(null, "Startup Monitor")` хрупок при локализации заголовка.
- **Патчи:** писать через `tmp + File.Replace` (+ `.bak`); убрать дублирующий `StartAutoReport`; мьютекс `Local\`; single-instance через именованный pipe/event вместо заголовка окна.

## 10. MEDIUM — Рассинхрон UI↔служба при рестарте + оптимистичный тоггл без проверки результата
- `MainWindow.xaml.cs:321, 336-363`: стартовый снимок грузится через **временный** `PipeClient loader`, затем `ConnectCommand` подключает основной клиент ВМ — два соединения, двойной скан; обработки разрыва/рестарта службы (переподписка, оверлей «служба недоступна») в `MainWindow` нет — UI показывает stale-данные с `Blocked/Disabled`, которых служба уже не знает.
- `MainViewModel.cs:2051-2058` (`DisableEntryAsync`): локальный `entry.Status = Disabled` ставится **без проверки** результата pipe-вызова — при отказе службы (защита §20) UI врёт до следующего поллинга.
- **Воспроизведение:** `sc stop/start StartupMonitor` при открытом UI → счётчики и статусы не обновляются; тоггл защищённой записи визуально «отключается», хотя служба отказала.
- **Патч:** единый `PipeClient`; подписка на disconnect → оверлей + `StartupSequence`-reconnect с бэкоффом; локальный статус менять только при `ok == true`, иначе тост с `error` и `UpdateTarget()` откат (как уже сделано для тумблера в `OnToggleEntryClick:283`).

---
**Приоритет исправлений:** 1 → 2 → 6 → 4 → 7. Первые три — односторонние деструктивные действия без возможности отката одной кнопкой при пустом payload.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m✗ [0mRead src/StartupMonitor.Service/Services/AutoGuards/AutoGuardEngine.cs failed
[91m[1mError: [0mFile not found: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\AutoGuards\AutoGuardEngine.cs
[0m→ [0mRead src/StartupMonitor.UI/App.xaml.cs
[0m→ [0mRead src/StartupMonitor.Shared/Helpers/SystemProtectionHelper.cs
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/BlacklistManager.cs
[0m✱ [0mGlob "**/AutoGuard*.cs"[90m 2 matches[0m
[0m✱ [0mGlob "**/EntryOperations*.cs"[90m 3 matches[0m
[0m✱ [0mGrep "class AutoGuardEngine|class BlacklistManager|CreateProcessAsUser|IsProtectedSystemEntry"[90m in src · 18 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/Delayed/UserSessionLauncher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/Guards/AutoGuardEngine.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs[90m [limit=350, offset=1180][0m
[0m→ [0mRead src/StartupMonitor.Service/Services/WatcherHelper.cs[90m [limit=180, offset=100][0m
[0m✱ [0mGrep "SaveSetting|LoadSetting|ui-settings\.json|KillProcess\(|DisableService|RemoveService|KillProcessesByRules"[90m in src · 100 matches[0m
[0m✱ [0mGrep "static.*KillProcess|static.*DisableService|static.*RemoveService|static.*KillProcessesByRules|QueryFullProcessImageName|MainModule"[90m in src/StartupMonitor.Service/Services/EntryOperations.cs · 8 matches[0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs[90m [limit=160, offset=1970][0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs[90m [limit=330, offset=410][0m

```
