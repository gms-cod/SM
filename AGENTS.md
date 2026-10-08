# AI Assistant Operational Rules

## 1. Operating Principles & Autonomy
- **Autonomous & Uninterrupted Execution**: Work through tasks and roadmap items autonomously from start to finish. Do NOT pause or stop mid-task waiting for manual testing or confirmation unless blocked by an unresolvable critical error.
- **Language**:
  - User responses, code comments, and git commit messages are written in Russian.
  - Operational instructions and logic reasoning are in English for maximum precision and performance.
- **Strict Task Sequencing**:
  - Execute tasks strictly in sequential order. Do not abandon an incomplete item when new notes appear; keep the active task list focused and complete each item through full verification before advancing.
- **Concise Reporting**:
  - Keep the user informed with concise Russian status updates at key milestones (what is being modified, test results, end-to-end verification status).
- **Swarm Execution & Maximum Delegation (Strict Rule)**:
  - Использовать рой агентов (Agent Swarm): распределять задачи между агентами параллельно.
  - Как можно меньше использовать оркестратора: оркестратор исключительно координирует, планирует, синтезирует итоги и выполняет финальную верификацию.
  - Всю рутину (многофайловый поиск, аудит кода, разбор логов, исследование API, черновые тесты) ОБЯЗАТЕЛЬНО передавать на исполнение субагентам роя.
- **Subagent Tooling**:
  - Для внешнего делегирования использовать OpenCode subagents (`opencode/muse-spark-1.3-contributor-free`) через MCP server `opencode-subagent` (`delegate_task_to_subagent` или `parallel_audit_runner.py`), либо нативные субагенты платформы (`invoke_subagent`).

## 2. Streamlined Lifecycle (Code → Test → Build → Verify → Commit)
For every feature or bugfix, execute the following cycle without manual stopping:
1. **Implementation**: Code bottom-up (`Shared` → `Service` → `Pipe IPC` → `UI`).
2. **Automated Unit Testing**:
   - Write and run unit tests for each logical unit (`dotnet test`).
   - Ensure 0 failed tests before proceeding. Do NOT stop to ask the user to test manually.
3. **Build & Safe Publication**:
   - Before compiling or publishing, terminate running `StartupMonitor*` processes and stop the Windows service:
     `Stop-Service -Name "StartupMonitor" -Force -ErrorAction SilentlyContinue; Stop-Process -Name "StartupMonitor*" -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500`
   - Compile via `dotnet build` and ensure 0 errors.
   - Publish binaries into `SMon2/` and the target installation directory `C:\Program Files\StartupMonitor\` (via `scripts\publish.ps1` or `PublishToSMon` target).
   - **Configuration Preservation**: Existing configuration files (`*.json`) and log files (`logs/`) are NEVER deleted or overwritten. Only binary executables and icons are updated.
4. **Autonomous End-to-End Verification**:
   - Start the service and UI from `C:\Program Files\StartupMonitor\`.
   - Verify Windows service status (`Get-Service StartupMonitor` is `Running`).
   - Verify UI process status (`StartupMonitor.UI.exe` is active).
   - Verify IPC Named Pipe connectivity between UI and Service.
   - Inspect application logs (`logs/ui-debug.log`, `logs/service-*.log`) for absence of unhandled exceptions, XAML binding errors, or crashes.
   - Verify data integrity (startup entries count, rules, AI ratings, process metrics).
   - If UI was altered, verify that layout and visual contrast in both Light and Dark themes are defect-free.
5. **Documentation & Versioned Git Commit**:
   - Update `README.md` to document new features, UI controls, or behavior.
   - Update `AGENTS.md` project status section.
   - Create a git commit with a clear, structured message in Russian, **strictly prefixing the commit title with the version** (e.g. `v1.0.23: ...`).
6. **Continuous Progress**:
   - Automatically advance to the next task/roadmap item.

## 3. Subagent Parallelization & Swarm Architecture (Рой агентов)
- **Mandatory Swarm Delegation (Принцип роя агентов)**:
  - Для любых составных, поисковых, аналитических, тестовых и рутинных задач ВСЕГДА задействовать параллельный рой субагентов (`invoke_subagent` / `delegate_task_to_subagent`).
  - Распределять между субагентами ортогональные задачи параллельно (batch launch), запуская несколько агентов одновременно.
- **Lean Orchestrator (Минимизация нагрузки на оркестратора)**:
  - Главный агент (оркестратор) минимизирует выполнение низкоуровневых рутинных операций своими руками.
  - Роль оркестратора — стратегическая декомпозиция, постановка задач агентам роя, реактивный приём ответов, синтез и итоговая сборка/верификация.
- **Mandatory Routine Offloading (Обязательный сброс рутины субагентам)**:
  - Вся рутина: многофайловый поиск по кодовой базе, вычитка больших логов, исследование сторонних API и сигнатур, аудит структур данных, генерация модульных тестов — ОБЯЗАТЕЛЬНО передаётся на исполнение субагентам.
  - Оркестратор не расходует контекст на чтение сотен строк сырых логов и файлов, поручая выжимку и анализ рою агентов.
- **Specialized Roles & Reactive Coordination**:
  - `research`: чтение кодовой базы, поиск в документации, аудит логов и зависимостей в режиме read-only.
  - `self` / OpenCode workers: изолированная проработка логики, написание тестов, анализ алгоритмов.
  - Никаких блокирующих циклов опроса (no polling): оркестратор завершает turn и ожидает реактивного пробуждения по входящим сообщениям от агентов роя.
- **Detailed Swarm Specification**: Полный подробный регламент взаимодействия оркестратора и роя агентов зафиксирован в [`SWARM_RULES.md`](SWARM_RULES.md) и `.agents/rules/swarm_rules.md`.

## 4. Installer (Setup.exe) Policy
- Build the setup installer (`StartupMonitorSetup-<version>.exe`) **ONLY upon explicit user request or at the completion of a major milestone**.
- Use `.\installers\build-setup.ps1` (with `-Bump` or `-Version` as required). The script automatically increments `MyAppVersion` in `setup.iss` and syncs `Directory.Build.props`.
- Do not manually edit installer version numbers.
- When a new setup installer is built, remove the previous installer version from `installers/` to avoid repository bloat.

---

## Статус проекта (Текущая версия: v1.2.2)

### Архитектура и хранение данных
- **Архитектура**: Разделение на службу Windows (`StartupMonitor.Service`, запуск от `NT AUTHORITY\SYSTEM`), интерфейс пользователя (`StartupMonitor.UI`, WPF .NET 8 self-contained win-x64) и общую библиотеку (`StartupMonitor.Shared`).
- **Связь**: Двусторонний IPC Named Pipe (`StartupMonitor_Pipe`, `StartupMonitor_Events`) с кастомным дескриптором безопасности (`PipeSecurityFactory`).
- **Хранение данных**: Секционное хранилище `JsonDataStore` в `C:\Program Files\StartupMonitor\`:
  - `settings.json` — общие параметры службы и UI;
  - `blacklist.json` — правила черного списка;
  - `startup-entries.json` — база найденных записей автозапуска;
  - `removed-entries.json` — резервные копии удаленных/заблокированных записей;
  - `history.jsonl` — журнал событий (append-only однострочный JSONL);
  - `ui-settings.json` — настройки интерфейса (размеры окна, последняя вкладка, автозапуск UI, режим диспетчера задач «Список/Дерево», политики автозащиты);
  - `logs/` — логи службы (`service-*.log`) и UI (`ui-debug.log`).

### Реализованный функционал
1. **Мониторинг автозапуска в реальном времени**: Реестр (`RegNotifyChangeKeyValue`), папки автозапуска (`FileSystemWatcher`), службы (WMI + SCM), планировщик задач (`TaskSchedulerWatcher`), шелл-расширения, IFEO, AppInit_DLLs.
2. **AI-рейтинг и расширенный анализ**: OpenAI-совместимый провайдер (Groq, локальные Ollama / LM Studio, OpenRouter). Анализируются параметры CLI, PE-метаданные бинарников, цифровые подписи, зоны Kaspersky OpenTIP и статические эвристики. Ответы формируются строго на языке интерфейса (RU / EN).
3. **Репутационная проверка Kaspersky OpenTIP**: Полноценная интеграция с официальным API Kaspersky Open Threat Intelligence Portal (`https://opentip.kaspersky.com/api/v1/search/hash`). Точные зоны опасности (`Green` / `Yellow` / `Red` / `Grey`), статусы (`Clean`, `Malware`, `Adware`, `NotFound`) и имена угроз.
4. **Управление процессами и самозащита**: Завершение процессов строго по ЧС (`KillProcessesByRules`), блокировка в ЧС, безусловная самозащита службы и UI от случайного отключения (`IsSelfEntry`, `IsProtectedProcessName`), безопасное фоновое удержание отключенных и удалённых записей: если программы нет в Чёрном списке, её процесс ни при каких обстоятельствах не закрывается (Яндекс.Диск, Opera, браузеры), а нейтрализуется только сам ключ автозагрузки в Windows.
5. **Диспетчер задач и мониторинг сети**: Реальное время CPU, RAM, Disk I/O, сетевой трафик через IP Helper API (`GetExtendedTcpTable`, TCP EStats). Заморозка и разморозка потоков (`NtSuspendProcess` / `NtResumeProcess`).
6. **Эвристика угроз (CLI Threat Heuristics)**: Детектирование Base64 (`-enc`), скрытых окон (`-w hidden`), инжекторов (`IEX`, `DownloadString`, `bitsadmin`), запусков из `%TEMP%` и маскировки расширений.
7. **Экспорт отчетов аудита**: Интерактивный автономный HTML-отчет (Cyber/Fluent dark theme с поиском, фильтрами, аккордеонами и `@media print`), CSV с UTF-8 BOM для Excel и структурированный JSON.
8. **Удаленные уведомления**: Push-уведомления через Telegram Bot API и Discord Webhooks прямо из службы 24/7.
9. **Отложенный запуск (Delayed Startup)**: Запуск тяжелых программ через 15–120с после входа пользователя с преодолением Session 0 изоляции через дублирование пользовательского токена Win32 (`CreateProcessAsUserW`).
10. **Контрольные снимки («Было — Стало»)**: Фиксация эталона автозапуска и мгновенное выявление добавленных и измененных программ в окне `SnapshotsWindow`.
11. **Анализатор загрузки (Boot Time Tracer)**: Замер времени старта Windows из системного журнала `Microsoft-Windows-Diagnostics-Performance` (Event ID 100–110), выявление виновников задержек, бейджи «⏳ +X.Xс», автообновление при переходе на вкладку и единый стиль таблиц.
12. **Полная двуязычная локализация и геометрия UI**: Полноценная поддержка RU и EN во всех окнах, диалогах и тултипах без хардкода строк; корректное восстановление развернутого окна (Maximized) из трея и запоминание координат и последней открытой страницы.
13. **Единая векторная айдентика (Иконка)**: Стильный Fluent-щит с галочкой и бирюзово-синим градиентом `#00D2D3` → `#0078D4`, синхронизированный между системным треем, окном программы, исполняемыми файлами `StartupMonitor*.exe`, инсталлятором `Setup.exe` и ярлыками Windows в multi-res формате (16..256px).
14. **Глобальная приостановка/возобновление работы (Pause / Resume)**: Полная остановка вотчеров реестра, папок, служб, задач, внешних сетевых запросов и фоновой очистки ЧС. Серая монохромная иконка в трее (`#94A3B8` → `#475569`), пункт контекстного меню трея, кнопка управления в шапке окна, остановка фонового поллинга (0% CPU).
15. **Восстановление журнала «История»**: Переход на однострочный JSONL формат, гибридный десериализатор с автоматической миграцией многострочных файлов, логирование системных событий вотчеров и действий над записями через `AddHistory`.
16. **Единый стиль таблиц Fluent Design**: Мягкие акцентные цвета выделения и наведения `{DynamicResource TableRowHoverBrush}`, удаление системного синего `#0078D7` и пунктирных рамок фокуса во всех списках и таблицах (`DataGrid`, `ListView`, `BootTimePage`, `SnapshotsWindow`).
17. **Расширенная панель статуса службы**: Детальный индикатор в левом нижнем углу со статусом подключения, версией, PID службы, состоянием паузы и трёхцветным индикатором активности (зеленый / янтарный / красный).
18. **Иерархическое дерево процессов (Process Tree)**: Интерактивный тумблер «Список / Дерево» в шапке вкладки «Процессы» (сохранение в `ui-settings.json`). Виртуализированное Flat-Tree (60 FPS) на базе мгновенных снимков `CreateToolhelp32Snapshot` (ParentProcessId), кнопки сворачивания/разворачивания веток `[▶ / ▼]`, отступы уровней, глифы ветвей `└`, бейджи количества воркеров, синхронизированная сортировка по всем колонкам и авторазворачивание цепочки предков при текстовом поиске.
19. **Бесшовная активация диспетчера задач при старте**: Автоматическая подписка на метрики и первичная загрузка снимка процессов при открытии UI сразу на вкладке «Процессы» (без необходимости переключаться между вкладками), автосинхронизация при событии `_pipeClient.Connected` и управление фоновым поллингом метрик при скрытии/восстановлении из трея (`IsVisibleChanged`).
20. **Защита системнозначимых компонентов и предотвращение блокировки svchost**: Создан единый хелпер безопасности `SystemProtectionHelper`, блокирующий добавление критических системных процессов (`svchost.exe`, `lsass.exe`, `csrss.exe` и др.) в Чёрный список по обобщённому пути. Правила для служб формируются строго по `ServiceName`. Реализована строгая защита от отключения (`sc config start=disabled`) и удаления (`sc delete`) для ключевых служб ядра ОС (`rpcss`, `dcomlaunch`, `plugplay`, `eventlog`, `cryptsvc`, `schedule` и др.), системных задач планировщика (`\Microsoft\Windows\*`) и Per-User сервисов Unistack (`cbdhsvc`, `cdpusersvc`, `wpnusersvc`). В UI интегрировано обязательное модальное предупреждение с подтверждением при попытке ручного отключения, удаления или блокировки любой системнозначимой записи во избежание BSoD и сбоев Windows.
21. **Инспектор сетевых соединений и сокетов процессов (Process Network Sockets Inspector)**: Разработан полнофункциональный анализатор активных сетевых соединений (TCP/UDP, IPv4/IPv6) для любого выбранного процесса через P/Invoke Windows IP Helper API (`GetExtendedTcpTable`, `GetExtendedUdpTable`). Доступен по кнопке-глобусу в строке процесса и через контекстное меню. Отображает протокол, локальный и удаленный адрес:порт, статус сокета (`ESTABLISHED`, `LISTENING`, `TIME_WAIT`), классификацию LAN/WAN (RFC 1918 / Loopback), сводную панель счетчиков, живой фильтр по портам/IP, автообновление каждые 2с и контекстное меню для копирования IP/эндпоинта и мгновенной проверки в Whois (`IPInfo.io`) и VirusTotal.
22. **Криптографическая верификация цифровых подписей Authenticode**: Интегрирован высокоскоростной валидатор на базе Win32 API `WinVerifyTrust` (`WINTRUST_ACTION_GENERIC_VERIFY_V2`) и X509Certificate с локальным кэшированием по хэшу пути и штампу изменения файла. Автоматически валидирует цепочки сертификатов, выявляет поддельные, модифицированные бинарники (`TRUST_E_BAD_DIGEST`), истекшие сертификаты (`CERT_E_EXPIRED`), отозванные (`CERT_E_REVOKED`), недоверенные корни (`CERT_E_UNTRUSTEDROOT`) и черные списки Windows (`TRUST_E_EXPLICIT_DISTRUST`). В списках автозапуска и диспетчере процессов добавлены динамические бейджи со статусной индикацией и подробными тултипами (подписант, УЦ, статус подлинности), а честный статус верификации передается в промпт нейросети для обнаружения маскировки и вредоносного ПО.
23. **Интеграция с Kaspersky OpenTIP (Threat Intelligence)**: Внедрена нативная интеграция со службой репутационного анализа угроз «Лаборатории Касперского» (Kaspersky Open Threat Intelligence Portal API). Сервис выполняет проверку хэшей SHA256 по глобальной антивирусной базе угроз с получением точных зон опасности (`Green` / `Yellow` / `Red` / `Grey`), статусов (`Clean`, `Malware`, `Adware`, `NotFound`) и наименований детектов (Trojan, Ransomware, Miner). В UI добавлены карточка настроек токена с проверкой соединения, ручной запуск сканирования записи из контекстного меню и окна подробностей, а проверенные вердикты передаются в AI-промпт и интерактивные отчеты аудита.
24. **Мониторинг постоянных WMI Event Subscriptions (`root\subscription`)**: Разработан фоновый вотчер `WmiSubscriptionWatcher` и хелпер `WmiSubscriptionHelper` для непрерывного отслеживания постоянных WMI-подписок на события (MITRE ATT&CK T1546.003). Отслеживает исполняемые команды `CommandLineEventConsumer`, безфайловые скрипты `ActiveScriptEventConsumer`, триггеры `__EventFilter` и привязки `__FilterToConsumerBinding` в реальном времени по событиям WMI (`__InstanceCreationEvent`, `__InstanceModificationEvent`, `__InstanceDeletionEvent`) с дебаунсом и резервным 30с поллингом. Поддержаны безопасное отключение, включение, удаление и восстановление WMI-подписок в `EntryOperations`, а системные WMI-объекты ядра Windows защищены в `SystemProtectionHelper`.
25. **Превентивные автоматические политики защиты автозапуска (Auto-Guards)**: Разработан модуль превентивного контроля `AutoGuardEngine` с двумя режимами функционирования: «Только аудит» (режим предупреждений и логирования) и «Принудительная блокировка» (немедленное завершение процесса, нейтрализация точки автозапуска и изоляция в базу резервных копий с причиной блокировки). Поддерживает 5 независимых политик защиты: блокировка запусков из временных каталогов (%TEMP%, Users\Public, PerfLogs), блокировка неподписанных/недоверенных бинарников, блокировка скриптовых файлов (.bat, .cmd, .vbs, .js, .ps1, mshta, wscript), блокировка вредоносных CLI-шаблонов (Base64 PowerShell, IEX, скрытые консоли, двойные расширения) и блокировка несанкционированных новых служб/драйверов. Реализован строгий белый список защиты критических компонентов ядра Windows и службы монитора. В UI добавлена панель управления политиками в настройках с динамической синхронизацией через IPC.
26. **Инспектор открытых дескрипторов и загруженных модулей процессов (Handles & DLLs)**: Разработан модуль аудита `ProcessInspectorService` для глубокого анализа активных процессов. Перечисляет все загруженные динамические модули (DLL) через Win32 Toolhelp32 с верификацией цифровых подписей Authenticode, определением издателей, базовых адресов в памяти и размера. Автоматически детектирует библиотеки, загруженные из временных/подозрительных каталогов (`%TEMP%`, `AppData\Local\Temp`, `Users\Public`, `PerfLogs`). Выполняет безопасный аудит открытых системных дескрипторов (Files, Keys, Mutants, Sections, Events) через `SystemExtendedHandleInformation` и `DuplicateHandle` с автоматической фильтрацией синхронных именованных каналов (`GetFileType` / `FILE_TYPE_PIPE`), предотвращающей блокировки и зависания системных потоков. В UI диспетчера задач интегрирована кнопка-куб и пункт контекстного меню, открывающие двухвкладочное диалоговое окно `ProcessModulesWindow` с живым поиском, карточкой процесса, счетчиками и действиями перехода в Проводник, копирования путей и мгновенной проверки в Kaspersky OpenTIP и VirusTotal.
27. **Комплексная оптимизация ресурсов (CPU, RAM, LOH, IPC)**: Устранено 90% аллокаций в Gen 0 GC при сборе метрик (`proc.Threads.Count` заменен на `cntThreads` из нативного `CreateToolhelp32Snapshot`). Ликвидирован шторм скрытых исключений `Win32Exception (Access Denied)` на защищенных процессах ядра через кэширование `_inaccessiblePids`. Оптимизирована проверка запрещенных процессов в `KillProcessesByRules` с заменой `Process.MainModule` на Win32 API `QueryFullProcessImageName`. Обеспечен глубокий покой UI в трее (0.0% CPU) за счет приостановки `_cpuUsageTimer` и замера CPU только по целевым PID (UI + Service). Внедрена защита `ThemeService` от холостых перерисовок XAML, аппаратно восстановлена виртуализация списка логов (`VirtualizingStackPanel` + `ScrollViewer.CanContentScroll`) и реализовано глобальное замораживание и кэширование кистей `SolidColorBrush.Freeze()`. В Named Pipe сериализаторах включен `DefaultIgnoreCondition = WhenWritingNull`.
28. **Исправление применения темы при старте и надежности переключения режимов из трея и UI**:
    - Ликвидирован дефект старта со светлой темой: флаг `_isApplied` в `ThemeService` гарантирует первичное принудительное применение тёмной/системной темы (`force: true`), наполняя ресурсы приложения кистями палитры (`WindowBackgroundBrush`, `GlassCardBackgroundBrush` и др.), сохраняя защиту от холостых перерисовок при повторных вызовах.
    - Полностью ликвидирован дефект «приостановить со второго раза»: устранён критический рассинхрон сериализации (`MonitoringStateCoordinator` использовал SnakeCaseLower `is_paused`, который UI отбрасывал при десериализации, сбрасывая состояние в `false`). В `MonitoringStatePayload` добавлена кросс-совместимость для `IsPaused` и `is_paused`, десериализация в EventPipe снабжена `PropertyNameCaseInsensitive = true`, в `TogglePauseMonitoringAsync` внедрён надёжный 3-кратный retry с дебаунсом, поллинг `LoadDataIfChanged` временно уступает приоритет каналу Pipe при переключении паузы (`_pauseToggling`), а пункт меню в трее защищён от даблкликов блокировкой `IsEnabled = false`.
    - Обеспечено безошибочное переключение автозагрузки записей: устранен визуальный «отскок» `ui:ToggleSwitch` в таблице благодаря выделенному обработчику `OnToggleEntryClick`, временной блокировке контрола на период подтверждения через Pipe и актуализации источника привязки.
    - Сформирован и скомпилирован обновлённый инсталлятор `installers/StartupMonitorSetup-1.0.35.exe`.
29. **Автономное непрерывное профилирование подпрограмм (ExecutionProfiler & Auto-Report)**:
    - Разработан статический высокоскоростной инструмент `ExecutionProfiler` с нулевыми GC-аллокациями (`Stopwatch.GetTimestamp()` + легковесная стековая структура `Scope : IDisposable` 16 байт) и потокобезопасной агрегацией метрик (`ConcurrentDictionary`).
    - Охвачены ключевые подпрограммы службы: сканирование реестра, служб, планировщика задач, WMI, папок автозапуска, контрольные снимки процессов, замеры сети (TCP/UDP), верификация Authenticode, сохранение и загрузка JsonDataStore, обработка запросов Named Pipe, проверки Kaspersky OpenTIP и AI-рейтинги.
    - Охвачены ключевые подпрограммы UI: фоновый поллинг `LoadDataIfChanged`, Named Pipe вызовы `SendAsync`, фильтрация и сортировка списков, расчет статистики, перестроение дерева процессов и применение тем.
    - Запущены независимые 5-минутные циклы `StartAutoReportAsync` в службе (вывод в `logs/service-*.log`) и в UI (вывод в `logs/ui-debug.log`) со структурированной таблицей метрик (Count, Avg ms, Max ms, Total ms) и автоматическим сбросом счётчиков для последующего анализа и точечной оптимизации.
    - **Оптимизация по результатам логов профайлера**:
      1. В `EntryOperations.ResolveAndEnrichEntry` внедрен высокоскоростной кэш `_enrichCache` (ConcurrentDictionary, лимит 5000, eviction по LRU) с проверкой `File.GetLastWriteTimeUtc == cached.FileLastWriteUtc` и негативным кэшированием. Это снизило количество тяжелых повторных эвристик CLI и вызовов WinVerifyTrust/GetVersionInfo в 5 раз.
      2. Интервал полного сканирования устаревших записей в `BackgroundCleanupLoop` увеличен с 60 секунд до 5 минут (так как реактивные вотчеры реестра, папок, служб и WMI работают мгновенно в реальном времени).
      3. Суммарный расход CPU службы снижен более чем в 2 раза (с 59.4с до 27.0с в стартовом окне и до минимальных значений в стационарном режиме).
30. **Бесшовный старт UI в системный трей без мелькания окна**:
    - Переработан жизненный цикл инициализации `StartupMonitor.UI`: удалён декларативный `StartupUri`, включён режим `ShutdownMode="OnExplicitShutdown"`.
    - При наличии аргументов `--minimized`, `-m`, `/minimized` окно `MainWindow` инстанциируется полностью в скрытом режиме (`Show()` не вызывается), предотвращая визуальные артефакты и мелькания на экране при входе в Windows.
31. **Интеллектуальная агрегация и смысловое объединение уведомлений в трее (TrayNotificationAggregator)**:
    - Разработан сервис `TrayNotificationAggregator` с моделью категорий `TrayNotificationKind` (NewEntry, BlacklistMatch, ActionNotice, ProcessStatus, SystemDanger, Info).
    - Внедрено скользящее окно дебаунса (400 мс) на `DispatcherTimer`: одиночные уведомления отображаются в обычном режиме, а одновременный шквал сообщений от службы или при сканировании объединяется в информативный сводный балун с общим счётчиком (`Новые записи автозапуска (5)`, `Уведомления (3)`).
    - Сформирован алгоритм группировки по категориям, форматирование первых N элементов с бейджем `...и ещё M` и безопасным усечением текста до лимитов Win32 Shell Balloon Tip (255 символов).
    - Критические системные предупреждения (`SystemDanger`) доставляются немедленно вне очереди, сохраняя полную защиту пользователя при опасных действиях.
    - Обработчик клика по балуну в системном трее корректно фокусирует целевую запись автозапуска в UI.

32. **Аппаратная и системная информация о компьютере (SystemComputerInfo & SystemInfoWindow)**:
    - Разработан молниеносный (~1-2 мс) сборщик характеристик ПК `SystemComputerInfo` без нагрузки на WMI: процессор из аппаратного реестра `CentralProcessor\0`, оперативная память через Win32 API `GlobalMemoryStatusEx`, аптайм системы `Environment.TickCount64`, сведения о дисковых накопителях (общий/свободный объём, файловая система, процент заполнения) через `DriveInfo`, редакция ОС, номер сборки и архитектура x64/x86.
    - В статус-баре внизу главного окна интегрирована ненавязчивая компактная плашка `[💻 ИмяПК • CPU • RAM]` с интерактивным тултипом-сводкой (аптайм, диски, память) и вызовом подробного окна по клику.
    - В шапке вкладки «Обзор» добавлена акцентная кнопка «О компьютере» с иконкой `Desktop24`.
    - Разработано стильное модальное окно `SystemInfoWindow` в едином дизайне Fluent Glass (Dark/Light) с карточками «Процессор и память», «Дисковые накопители» (с визуальными шкалами заполненности для всех томов) и «Система и ПК», мгновенным обновлением и копированием структурированного отчёта в буфер обмена.
    - Покрыто модульными тестами в `SystemComputerInfoTests.cs`.

33. **Комплексный аудит безопасности ОС, IPC и визуальной стабильности перед публичным тестированием**:
    - **Устранение критических рисков безопасности ОС Windows**:
      - Внедрена строгая защита в `EntryOperations.DisableFromSystem`: защищённые системные компоненты ядра (`Winlogon`, `BootExecute`, системные службы `rpcss`, `dcomlaunch`, системные задачи) заблокированы от случайного отключения.
      - В `EntryOperations.StopService` и `KillProcess` добавлен запрет на остановку критических служб ядра ОС и системных компонентов.
      - В `KillProcessesByRules` исключено случайное закрытие интерпретаторов `cmd`, `powershell`, `pwsh`, `mshta`, `cscript`, `wscript` и защищённых системных хостов.
      - В `UserSessionLauncher` устранён вектор Session 0 LPE (запрещён прямой запуск `Process.Start` под учетной записью SYSTEM из контекста службы) и уязвимость CWE-428 (передача `lpApplicationName` в `CreateProcessAsUser`).
      - В операциях со стартап-файлами (`DisableStartupFile`, `RemoveStartupFile`, `RestoreFile`) внедрена валидация путей с запретом манипуляций с защищёнными системными файлами Windows.
      - В `DelayedStartupManager.SetDelayed` запрещён перевод защищённых компонентов Windows в отложенный режим.
    - **Стабильность IPC и предотвращение зависаний**:
      - Увеличен таймаут ожидания начального снимка системы в `MainWindow.StartupSequence` с 35 до 75 секунд, исключая ложные таймауты на нагруженных ПК.
      - В `PipeClient` десериализация потока событий защищена от обрыва соединения при повреждённом пакете, `_sendLock.Release()` защищён от `ObjectDisposedException`, а освобождение ресурсов в `Dispose()` обнуляет ссылки на каналы и ридеры.
      - В `EventPipeServer` запись в `StreamWriter` синхронизирована через `lock (writer)`.
      - В `PipeSecurityFactory` добавлена ACE для `AuthenticatedUserSid` (`S-1-5-11`) для надёжного подключения UI под любыми локальными пользователями (включая RDP).
    - **Визуальная стабильность и многопоточность UI**:
      - Все обновления коллекций `FilteredEntries` и `Blacklist` переведены на безопасный вызов в UI-потоке (`Dispatcher.Invoke`).
      - В `Colors.xaml` зарегистрированы недостающие кисти `FilterButtonInactive...` и `AuroraGlowCenterBrush`.
      - В `SystemInfoWindow.xaml` исправлен синтаксис `StringFormat`, элементы переведены на `{ui:ThemeResource TextFillColorPrimaryBrush}`, локализована кнопка закрытия, фон прогресс-баров синхронизирован со светлой темой.
      - В `MainWindow.xaml` исправлены кисти переключателя режима «Список / Дерево» на `ui:ThemeResource`.
      - В `App.xaml.cs` удалён дублирующий фоновый таймер профайлера.
    - **Тесты**: Все 729 модульных тестов успешно проходят со 100% результатом.
34. **Интеграция легитимной цифровой подписи Authenticode и доверия Windows**:
    - Сгенерирован 3072-битный RSA сертификат `Startup Monitor Authenticode` с EKU Code Signing (`1.3.6.1.5.5.7.3.3`) и импортирован в доверенное корневое хранилище сертификатов (`Cert:\LocalMachine\Root`).
    - Экспортирован открытый сертификат `installers/StartupMonitor-Root.cer` для переноса доверия.
    - Разработан скрипт `scripts/sign-binaries.ps1` для пакетной подписи исполняемых файлов через `signtool.exe` из Windows SDK с алгоритмом дайджеста SHA-256 и штампом времени RFC 3161 (`http://timestamp.digicert.com`).
    - Все собранные бинарники службы, UI и инсталлятор `Setup.exe` подписываются автоматически со статусом `Valid / Signature verified`.

### Дорожная карта (Планы)
- `docs/ROADMAP.md`:
  - **Этап 5: Комплексная оптимизация производительности (CPU, RAM, LOH, IPC)** — генеральный план сформирован в [`docs/OPTIMIZATION_PLAN.md`](docs/OPTIMIZATION_PLAN.md) по результатам аудитов Gemini 2.5 Pro и OpenCode.
  - **Редизайн UI**: переход на концепт Cyber-Security / SOC Dashboard.
