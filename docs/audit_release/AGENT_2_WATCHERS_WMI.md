# Agent 2: Background Watchers, WMI & Scheduler Engines Audit

- Время анализа: 157.6 сек
- Статус: Успех

---

Аудит фоновых вотчеров — читаю все пять целевых файлов и опору для вердикта.
Часть путей не сошлась — уточняю реальную структуру проекта.
Нашёл — вотчеры лежат в Services, а не в Watchers. Читаю их целиком.
Основное прочитал — теперь смотрю вспомогательные компоненты чтобы оценить утечки, дебаунс и остановку.
Аудит провёл по фактическим файлам (`Services\`, не `Watchers\` — путей из задачи нет, файлы лежат в `src\StartupMonitor.Service\Services\`).

# 1. Устойчивость к остановленным `winmgmt` / `Schedule`

**`ServiceWatcher.cs:50-67` — критично.** `InitialScan` ошибки глотает, но создание `ManagementEventWatcher` + `Start()` — без `try/catch`. Если `winmgmt` остановлена/повреждена, конструктор/`Start()` бросит `ManagementException/COMException` → `ExecuteAsync` падает → `BackgroundService` умирает навсегда. Ни ретрая, ни fallback-поллинга нет. Весь детект служб пропадает до перезапуска службы. `TaskSchedulerWatcher` на этом фоне сделан правильно (catch → graceful degradation), а здесь — нет.

**`TaskSchedulerWatcher.cs:70-79` — средне.** Отказ COM при старте обрабатывается, но это one-shot: `return` без ретрая. Если `Schedule` стартовал позже нашей службы (порядок загрузки) или админ его перезапустил — вотчер мёртв до рестарта сервиса. Нужен цикл переподключения, а не `return`. Транзиентные падения в `PollForChanges` уже самовосстанавливаются (`_taskService = null` + пересоздание) — хорошо.

**`WmiSubscriptionWatcher.cs:60-97` — хорошо, но с ловушкой.** `scope.Connect()` + подписки обёрнуты в `try/catch` с fallback на `PeriodicTimer(30с)` — правильно. Ловушка ниже, в п.2.

**Реестр/папки** от WMI/Schedule не зависят — переживут.

# 2. Утечки COM / handles

1. **`ServiceWatcher.cs:88-92` — утечка.** `searcher.Get()` возвращает `ManagementObjectCollection` (COM-перечислитель), который никто не dispose'ит, и каждый `ManagementObject` в `foreach` тоже не dispose'ится. При 30-секундном опросе это был бы рост хендлов; сейчас скан один при старте — утечка разовая, но в `OnServiceCreated` родительский `e.NewEvent` также никогда не освобождается (диспозится только вынутый `TargetInstance`). События редкие — утечка малая, но её надо закрыть.
2. **`Wmi\WmiSubscriptionHelper.cs:97-137, 216-284` — утечка на каждом поллинге.** `using` есть на `ManagementObjectSearcher`, но коллекция от `.Get()` (`foreach ... in searcher.Get()`, `filterCheck.Get().Count`) не диспозится. Поллинг каждые 30с навсегда → медленный рост COM-хендлов. Тот же паттерн в `Remove/Restore/Disable/Enable`. Фикс: `using var col = searcher.Get(); foreach ... in col`.
3. **`TaskSchedulerWatcher` — почти чисто.** Задачи, папки, `root` диспозятся. Нюанс: коллекции `folder.Tasks` / `folder.SubFolders` сами не диспозятся (диспозятся только элементы). Если в используемой версии `TaskScheduler` `TaskCollection` — `IDisposable`, это утечка каждые 30с. Проверить и при необходимости обернуть.
4. **`RegistryWatcher / OtherStartupLocationsWatcher` — хендлы держатся корректно** (`_watchedKeys` + `Dispose` + `StopAsync` разблокирует `RegNotify`). Double-dispose безопасен.
5. **`OtherStartupLocationsWatcher.cs:142-144` — ВОЗМОЖНЫЙ КРИТИЧЕСКИЙ БАГ:**
   ```csharp
   using var baseKey = hive == ... ? Registry.LocalMachine : Registry.CurrentUser;
   ```
   Это dispose process-wide синглтона `Registry.LocalMachine/CurrentUser`. Если в используемой версии `Microsoft.Win32.Registry` это статические синглтоны (а в .NET Core так и есть), то после первого скана `Other`-вотчера **все** последующие обращения к реестру в процессе (`RegistryWatcher`, `GetUserProfiles`) начнут кидать `ObjectDisposedException`. `RegistryWatcher` так не делает — и правильно. Заменить на `RegistryKey.OpenBaseKey(hive, ...)` без `using` статики, либо вообще не диспозить.

# 3. Остановка / Shutdown

- **Реестровые вотчеры — корректно.** Блокирующий `RegNotifyChangeKeyValue(fAsynchronous:false)` разблокируется закрытием ключей в `StopAsync` — приём рабочий. Замечание: `StartRegNotify` — fire-and-forget без ожидания в `StopAsync`; исключения после dispose логгера → `UnobservedTaskException` (низкий риск).
- **`ServiceWatcher.cs:69-77` — риск подвисания стопа.** В `finally` — блокирующие COM-вызовы `createWatcher.Stop()`. Если `winmgmt` висит, стоп службы (и shutdown Windows, где у SCM ~20с) подвиснет. Плюс `InitialScan` через `Task.Run(ct)` не прерывает висящий WMI-`Get()` — отмена придёт только после возврата COM.
- **`TaskSchedulerWatcher` — то же.** COM-вызовы `GetAllTasksWithHidden/IsStartupTask` игнорируют `ct`. Висящий COM во время стопа = превышение таймаута SCM → «служба не ответила», задержка shutdown. Лечится выносом COM в `Task.Run` + `WaitAsync(ct)` с таймаутом.
- **`WmiSubscriptionWatcher.cs:124-153` — гонка debounce vs shutdown.** `ScheduleRescan` заводит `Task.Delay(1000, ct)` по собственному CTS, не связанному со `stoppingToken`. При остановке отложенный рескан всё равно выстрелит и полезет в (возможно уже задиспозенные) `_store/_notifications`. Привязать через `CancellationTokenSource.CreateLinkedTokenSource(debounce, stoppingToken)`.
- **`InitialScanTracker` (5 по умолчанию) vs 6 вотчеров в `Program.cs:106-117`.** По факту баланс сходится (WMI-вотчер трекер вообще не использует и `MarkCompleted` не зовёт; остальные 5 зовут), но хрупко: UI покажет «сканирование завершено» до конца WMI-скана. Либо включить WMI в трекер (6), либо зафиксировать комментарием.

# 4. Дебаунс и пакетные установки

- **Реестр (`RegistryWatcher:253`, `Other:403` — `Delay(500)`) — дебаунс «для вида».** Без коалесцирования: пачка из 20 записей = до 12 параллельных полных ресканов всех кустов (каждый HKCU-ивент тянет `ScanHive(HKCU)+все HKEY_USERS`). Потери событий нет (детект state-based, `AddOrUpdate` дедуплицирует), но CPU-пик при установке софта гарантирован. Правильно: один `CancellationTokenSource`-дебаунс на вотчер + один полный рескан.
- **`StartupFolderWatcher` — дебаунса нет вообще, и это главный источник потерь:**
  - нет `Error`-обработчика `FileSystemWatcher` → при переполнении буфера (8 КБ по умолчанию, пакетная установка) события молча теряются;
  - `Created` срабатывает когда файл ещё пишется/залочен → `ProcessFile` читает partial (хеш/метаданные кривые);
  - нет подписки на `Changed` (обновление `.lnk` на месте — пропуск) и на `Deleted/Renamed-away` (протухшие записи вечно в сторе);
  - 50 файлов = 50 синхронных `store.Load/Save` подряд (проверить потокобезопасность `JsonDataStore` — 6 вотчеров + pipe пишут конкурентно).
  Фикс: `InternalBufferSize=32КБ` + `Error→полный рескан папки`, debounce-словарь `путь→таймер ~1с` с ожиданием разблокировки файла, обработка `Deleted`, `IncludeSubdirectories=false` оставить.
- **`WmiSubscriptionWatcher:124-135` — идея верная (1с на связку Filter→Consumer→Binding), реализация с гонкой.** Три WMI-потока конкурентно делают `_debounceCts?.Cancel(); Dispose(); = new...` — возможен `ObjectDisposedException` и двойной рескан. Лечится `lock` + `Interlocked.Exchange`. Потерь нет (state-based + 30с backstop).
- **`ServiceWatcher` — без дебаунса.** WMI-интринсики с интервалом 5с сами коалесцируют, потерь нет, но `OnServiceCreated` делает store-I/O синхронно на потоке WMI — пачка установок тормозит очередь событий. Разгрузить в `Task.Run`/очередь. Отдельно: два вотчера (`__InstanceCreationEvent` + `__InstanceModificationEvent`, каждый со своим полным опросом `Win32_Service`) опрашивают все службы каждые 5с — дорого; достаточно одного опроса 15–30с + state-diff.

# 5. Избыточное / бессмысленное

- **Тройной copy-paste blacklist-ветвлений** во всех вотчерах (`StartupFolderWatcher:206-226` — эталон: ранний `ApplyBlacklistAction` + `return`, затем тот же вызов после `AddOrUpdate` — второй для `NotifyAndRemove` недостижим). Логика размазана между вотчерами и `WatcherHelper.AddOrUpdate:280-286` (который тоже сам реаплаит). Работе не мешает, но источник багов — централизовать в `AddOrUpdate`.
- **`ServiceWatcher`: `Status` из `State` выставляется только в скане** (`:107-109`), в ивент-пути — дефолт, а update-ветка `AddOrUpdate:257-278` синхронизирует только `CommandLine/FilePath` — флаг `Enabled/Disabled` протухает.
- **Новые SID/профили после старта не подхватываются** (реестр, папки вотчат снимок на старте). Ограничение допустимое, но должно быть задокументировано или перекрыто периодическим переперечислением.
- **Самое опасное «бессмысленное» — дифференциальное удаление в `WmiSubscriptionWatcher:187-215`.** Если `ScanSubscriptions()` вернул `[]` из-за транзиентной недоступности WMI (а все исключения там глотаются и неотличимы от «подписок нет»), вотчер **удалит из стора все WMI-записи** и разошлёт `EntryDeleted`. Нужен guard: при ошибке скана пропускать sync удаления (возвращать `null`/флаг вместо пустого списка).

# Итог

**Вердикт: к выпуску не готов.** Архитектура мониторинга здравая (state-based ресканы + события + поллинг-backstop), остановка реестровых вотчеров аккуратная, `TaskSchedulerWatcher` самый дисциплинированный по COM. Но есть блокеры релиза:

**P0 (блокеры):**
1. `OtherStartupLocationsWatcher:142` — проверить/убрать `using` над `Registry.LocalMachine/CurrentUser` (потенциально ломает весь реестр в процессе).
2. `ServiceWatcher` — обернуть WMI-подписки в try/catch + ретрай-поллинг, иначе смерть вотчера при дохлом `winmgmt`.
3. `WmiSubscriptionWatcher` — guard от «пустого скана при ошибке WMI», иначе снос всех WMI-записей из стора.
4. `StartupFolderWatcher` — `Error`-хендлер + рескан, увеличение буфера, дебаунс с ожиданием анлока, обработка `Deleted`.
5. Проверить потокобезопасность `JsonDataStore` (конкурентные `Add/Update` из 6 вотчеров).

**P1:** утечки `ManagementObjectCollection` в `ServiceWatcher` и `WmiSubscriptionHelper`; атомарный debounce в WMI-вотчере; линковка debounce-CTS к `stoppingToken`; таймауты COM на стопе (`Service/TaskScheduler/WMI`); ретрай подключения `TaskScheduler` вместо one-shot `return`; вынос тяжёлого `OnServiceCreated` с потока WMI.

**P2:** коалесцирующий дебаунс реестра; один опрос служб вместо двух 5-секундных; синхронизация `Status` служб; учёт `InitialScanTracker` для WMI; централизация blacklist-ветвлений; переперечисление новых профилей пользователей.

