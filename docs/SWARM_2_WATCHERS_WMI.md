# OpenCode Swarm Agent 2: Watchers, WMI & SCM Audit (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 149.1 сек
- Статус: Успешно

---

Разбираю фоновые вотчеры, дебаунс и риски зависаний.
Нашёл файлы не в том каталоге — читаю реальные вотчеры и проверяю смежные модули.
# Аудит фоновых вотчеров: реестр, службы, задачи, WMI, папки

Пути реальные: `src/StartupMonitor.Service/Services/` (а не `Watchers/` из ТЗ). Проверены `RegistryWatcher.cs`, `OtherStartupLocationsWatcher.cs`, `ServiceWatcher.cs`, `TaskSchedulerWatcher.cs`, `WmiSubscriptionWatcher.cs`, `StartupFolderWatcher.cs`, `Wmi/WmiSubscriptionHelper.cs`, `WatcherHelper.cs`, `JsonDataStore.cs`, `MonitoringStateCoordinator.cs`.

## 1. RegistryWatcher (+ OtherStartupLocationsWatcher — та же схема)

**Критично — блокировка ThreadPool.** Синхронный `RegNotifyChangeKeyValue(fAsynchronous: false)` в `Task.Run` держит по одному потоку ThreadPool на каждый ключ: 7 (HKLM) + ~5 (HKCU) + ~5×N пользователей + ~9×2 (OtherWatcher) ≈ **20–35 вечно заблокированных потоков**. При старте конкурируют с `InitialScan` + `ResolveAndEnrichEntry`. Лечится async-режимом с `hEvent` + `ThreadPool.RegisterWaitForSingleObject`, либо одним мультиплексным потоком, либо наблюдением родительского ключа вместо 7 штук.

**Нет дебаунса, есть «шторм сканов».** После каждого срабатывания — `Delay(500)` + **полный** `ScanHive` (все ключи + все SID + `ResolveAndEnrichEntry` с `WinVerifyTrust` + `SaveEntries` + notify). Пакетная запись 10 значений (инсталлятор) = 10 полных сканов. Два параллельных цикла (HKLM/HKCU) могут сканировать одновременно — `ScanHive` никак не сериализован (в отличие от `WmiWatcher._scanLock`). Двойные `EntryAdded`/`AddHistory("Added")` при гонке гасятся только `AddEntry(checkDuplicate)` под локом — уведомления всё равно могут задвоиться.

**Потеря событий:**
- Ключ, отсутствовавший на старте (`OpenSubKey` → null), **никогда не начнёт наблюдаться** — создание ключа в будущем пропущено. Нужен вотч родителя `...\CurrentVersion` или периодическое перевооружение.
- Профили `HKEY_USERS` снимаются один раз в `StartWatching` — новый логон пользователя не подхватывается.
- Пока `IsPaused` — событие проглатывается (`continue` без перескана) и при resume сверка не делается. Расхождение до следующего события. Тот же дефект в WMI-вотчере. Лечится подпиской на `MonitoringStateCoordinator.StateChanged` → rescan по resume.

**Остановка.** `StopAsync` сначала `Dispose()` ключей, потом `base.StopAsync` (отмена токена). Между ними циклы крутятся на мёртвом handle с `Delay(1000)`. Мелочь, но правильно — сначала отмена, потом dispose. `Dispose()` (синхронный) при живом сервисе тоже ломает вотчи.

## 2. ServiceWatcher (WMI `Win32_Service`)

**Два `ManagementEventWatcher` с `WithinInterval = 5с`.** Это временные WMI-подписки с опросом CIM-репозитория каждые 5 с × 2. Для служб достаточно 10–15 с (события редкие) — снижение нагрузки WMI в ~2–3 раза. Можно слить в один запрос.

**Дебаунса нет, тяжёлая работа в callback.** `OnServiceCreated` синхронно делает `ResolveAndEnrichEntry` (подписи, хэши) в потоке доставки WMI — притормаживает доставку, возможны дропы. Вынести в `Task.Run` + коалесцировать (флаг/семафор). Двойное срабатывание create+modify на `sc create + sc config auto` сейчас идемпотентно за счёт `AddOrUpdate`, повторный `ApplyBlacklistAction` — безвреден, но шумит в логах.

**Утечка COM.** В `InitialScan`: `foreach (var obj in searcher.Get())` — ни `ManagementObjectCollection`, ни сами `ManagementObject` не dispose'ятся (в `OnServiceCreated` `TargetInstance` — dispose есть, хорошо). Фикс: `using var coll = searcher.Get(); foreach (ManagementObject o in coll) using (o) {...}`. Та же картина в `WmiSubscriptionHelper.ScanSubscriptions` (коллекции 4 поисковиков).

**Остановка.** `createWatcher.Stop()` / `modWatcher.Stop()` — синхронный RPC в WMI, при зависшем WMI повесит `StopAsync` сверх SCM-таймаута. Делать через таймаут или только `Dispose()`.

## 3. TaskSchedulerWatcher (COM `TaskService`)

**Самый дорогой вотчер: чистый поллинг каждые 30 с.** Каждый тик — рекурсивный обход всех папок (`CollectTasks`), открытие каждого `task.Definition` (разбор XML через COM), `IsStartupTask`, `FindByTaskPath` (линейный поиск по стору **под локом на каждую задачу**). На машине 150–300 задач это сотни COM-объектов каждые 30 с + строки XML в LOH. Рекомендации:
- интервал 60–120 с (задачи автозапуска меняются редко) → **−50…−75% CPU этого вотчера**;
- инкрементальная проверка (счётчик задач / хэш имён+`DateTime Modified`) — полный разбор только при изменении;
- обрабатывать+dispose по одной задаче, а не держать весь `List<Task>` до `finally` (пиковое число COM-хэндлов).

**Нет детекта удалений.** Poll только добавляет/обновляет, удалённые из планировщика задачи остаются в `startup-entries.json` навсегда (у WMI-вотчера diff-синхронизация есть — сделать так же).

**Прочее.** `_taskService`-синглтон без лока: `PollForChanges` может пользоваться им одновременно с `Dispose`/пересозданием после ошибки → `ObjectDisposedException` (сейчас глушится и пересоздаётся — работает, но недетерминировано). `TaskService` из STA-COM, а сервис — MTA ThreadPool: обычно работает через обёртку, но под нагрузкой возможен `RPC_E_WRONG_THREAD` — зафиксировать как риск.

Пауза обработана правильно (в паузе COM не трогается). Дрейф расписания (`Delay(30000)` без учёта времени скана) — заменить на `PeriodicTimer`.

## 4. WmiSubscriptionWatcher (лучший из всех, но есть баги)

Дебаунс 1 с + `_scanLock` + `PeriodicTimer(30с)` + diff-удаления — эталон, перенести паттерн на реестр/папки.

**Гонка дебаунса.** `_debounceCts?.Cancel(); _debounceCts?.Dispose(); _debounceCts = new...` вызывается из 3 WMI-callback'ов параллельно — классическая гонка (`ObjectDisposedException`, потеря перескана). Фикс — `lock` или `Interlocked.Exchange`, плюс связывать debounce-CTS со `stoppingToken` (сейчас fire-and-forget `Task.Run(..., ct)` переживает остановку сервиса и лезет в стор).

**Фантом-дубликат Unbound→Bound.** `ComputeStableId` включает `LocationDetail`, а туда вшит `(Filter: X)` / `[Unbound]`. Триада Filter→Consumer→Binding, созданная скриптом последовательно, на промежуточном скане видна как `[Unbound]` (один Id), на финальном — как `(Filter: X)` (другой Id). Итог: лишнее `Added` + ложное `Removed` + мусор в истории. Фикс: стабильный Id только от `ConsumerType+ConsumerName` (или нормализовать `LocationDetail` перед хэшем).

**Таймаут `_scanLock.WaitAsync(100)`.** Скан дольше 100 мс под нагрузкой → рескан молча отбрасывается. При 1-с дебаунсе обычно хватает, но 100 мс — слишком жёстко; 1–2 с + логирование пропуска.

**Опрос агрессивный.** 3 вотчера × `WithinInterval 3с` ≈ 1 WQL-запрос/с к `root\subscription`. События — редчайшие (постоянные WMI-подписки). Поднять до 10 с → **−3× нагрузка WMI без потери смысла** (дебаунс+поллинг 30 с остаются страховкой).

**Пауза без catch-up** — см. п.1. Остановка — три последовательных `Stop()` могут упереться в SCM-таймаут; лучше `Dispose()` с таймаутом.

## 5. StartupFolderWatcher (`FileSystemWatcher`)

**Потеря событий при burst.** Буфер по умолчанию 8 КБ, нет подписки на `Error` → переполнение при массовой распаковке = тихая потеря файлов. Нет `Changed`/`Deleted`-обработчиков → смена цели `.lnk` пропускается, удалённый `.lnk` оставляет вечную stale-запись. Фикс: `InternalBufferSize = 32–64КБ` + `Error += full rescan` + обработчики `Changed`/`Deleted` + периодическая полная сверка (как у WMI).

**Файл прочитан раньше, чем дописан.** `Created` стреляет в начале копирования → `ProcessFile` хэширует/обогащает partial-файл (неверные метаданные), `.tmp`/`.crdownload`/`.part`/лок от антивируса не учтены. Фикс: дебаунс 500–1000 мс на путь + retry 3×200 мс + игнор временных расширений (сейчас игнорятся только `.disabled` и `desktop.ini`).

Новые профили после `StartWatching` не подхватываются (аналогично реестру).

## 6. `WatcherHelper.AddOrUpdate` + `JsonDataStore` (сквозное)

**Корень дублей.** `ComputeStableId` хэширует сырой `LocationDetail`, а `NormalizeLocationDetail` (HKCU\<user>\ → HKCU\) применяется только в `DeduplicateEntries`, который вызывается **один раз** после `InitialScan`. Рантайм-ресканы параллельных веток HKCU/HKEY_USERS плодят пары с разными Id до рестарта. Фикс: нормализовать **до** `ComputeStableId` (внутри него или при создании entry) + вызывать `DeduplicateEntries` после каждого полного рескана (дешёвый, под локом, с хэш-скипом записи).

**Шторм записей/уведомлений.** Каждый `AddOrUpdate` = 2–4 `Load()` под `lock` + отдельный `SaveEntries` (сериализация + tmp+move всего `startup-entries.json`) + `NotifyExternalEntriesChanged` на каждую запись. Пакет из N значений = N полных перезаписей файла + N pipe-сообщений. Фикс: батч-режим скана (собирать, один `Save` + один notify + один `EntryAdded`-дайджест).

`store.Load().Settings` дёргается до 4 раз за вызов — прочитать один раз. `check-then-act` между `FindEntry`/`AddEntry`/`UpdateEntry` безопасен только благодаря повторной проверке внутри `AddEntry` под локом — не ломать это при рефакторинге.

## Сводная таблица рекомендаций и эффект

| # | Мера | Эффект |
|---|------|--------|
| 1 | Нормализовать `LocationDetail` до `ComputeStableId` + дедуп после ресканов | Убирает системные дубли HKCU (главная жалоба) |
| 2 | Единый дебаунс-а Грегатор для реестра (общий 750 мс + `SemaphoreSlim`, скан только изменённого `keyPath`) | −5…−10× CPU при пакетных записях |
| 3 | Async `RegNotify` / сокращение вотчей (родительские ключи) | −20…−30 висящих потоков ThreadPool |
| 4 | Перевооружение вотчей (ключи/profiles/папки, созданные после старта) + rescan по resume из паузы | Закрывает все «пропущенные события» |
| 5 | TaskScheduler: интервал 60–120 с + инкрементальный хэш + поштучный dispose + diff-удаления | −50…−75% CPU вотчера, нет stale-записей |
| 6 | ServiceWatcher/WMI: `WithinInterval` 5→10 с / 3→10 с, слияние create+modify, вынос enrich из callback | −2…−3× нагрузка WMI |
| 7 | `using` для `ManagementObjectCollection` + per-object во всех сканах | Убирает рост COM-хэндлов/памяти |
| 8 | Потокобезопасный дебаунс WMI (`Interlocked.Exchange` + linked CTS), стабильный Id без суффикса фильтра, `_scanLock` таймаут 1–2 с | Нет фантомов Unbound/Bound и гонок |
| 9 | FSW: буфер 32–64КБ + `Error`→rescan + `Changed`/`Deleted` + дебаунс пути + retry записи | Нет потерь при bulk-копировании |
| 10 | Батчевые `Save`+notify в сканах, `Stop()` вотчей с таймаутом, `PeriodicTimer` вместо `Delay` | Меньше I/O-шторма, укладка в SCM-таймаут |


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m✗ [0mRead src/StartupMonitor.Service/Watchers/RegistryWatcher.cs failed
[91m[1mError: [0mFile not found: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\RegistryWatcher.cs
[0m✗ [0mRead src/StartupMonitor.Service/Watchers/ServiceWatcher.cs failed
[91m[1mError: [0mFile not found: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\ServiceWatcher.cs
[0m✗ [0mRead src/StartupMonitor.Service/Watchers/TaskSchedulerWatcher.cs failed
[91m[1mError: [0mFile not found: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\TaskSchedulerWatcher.cs
[0m✗ [0mRead src/StartupMonitor.Service/Watchers/WmiSubscriptionWatcher.cs failed
[91m[1mError: [0mFile not found: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\WmiSubscriptionWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/WatcherHelper.cs
[0m✱ [0mGlob "**/*Watcher*.cs"[90m 9 matches[0m
[0m✱ [0mGlob "src/StartupMonitor.Service/**/*.*"[90m 39 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/RegistryWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/WmiSubscriptionWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ServiceWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/TaskSchedulerWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/StartupFolderWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/MonitoringStateCoordinator.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/OtherStartupLocationsWatcher.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/Wmi/WmiSubscriptionHelper.cs
[0m✱ [0mGlob "**/JsonDataStore.cs"[90m 1 match[0m
[0m✱ [0mGrep "lock|Concurrent|Deduplicate|AddEntry|InternalBuffer|debounce|Debounce|Delay\(500|Delay\(1000"[90m in src/StartupMonitor.Service · 100 matches[0m
[0m→ [0mRead src/StartupMonitor.Shared/Models/JsonDataStore.cs

```
