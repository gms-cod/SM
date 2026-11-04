# Agent 9: UI Dialogs, Remote Alerts (Telegram/Discord) & Logging Audit

- Время анализа: 123.4 сек
- Статус: Успех

---

Аудит уведомлений, диалогов и логов — изучаю целевые файлы.
Первичный разбор готов — теперь копаю хранение токенов, ротацию логов и валидацию в диалогах.
Осталось проверить агрегацию уведомлений в трее и детали хранения конфига — затем подведу итоги.
# Аудит: диалоги, уведомления, логирование

## 1. `RemoteNotificationService` — Telegram / Discord

### 1.1. Падение при неверном токене — НЕТ, не падает
Хорошая новость: краша нет.

- `SendTelegramAsync` / `SendDiscordAsync` (`RemoteNotificationService.cs:363-408`) — весь HTTP-вызов в `try/catch`, ошибка только в `_logger.LogWarning`. `Timeout 10с` + `CTS 15с` — зависания исключены.
- `NotificationService.DispatchRemoteNotification` (`NotificationService.cs:57-93`) — обёрнут в `try { } catch { }`, вызовы fire-and-forget `_ = Notify...`.
- `TestTelegramAsync` / `TestDiscordAsync` (`:235-326`) корректно возвращают `(bool, message)`, пустой токен/chat_id/URL проверяются до запроса.

**Но валидации формата нет:** проверяется только `IsNullOrWhiteSpace`. Неверный токен даст generic `401 {errContent}` вместо ранней подсказки. Нет проверки:
- Telegram-токен на паттерн `^\d+:[\w-]{30,}$`, `chat_id` на `^-?\d+$` / `@channel`;
- Discord-URL на принадлежность `discord.com/api/webhooks/` — сейчас служба (SYSTEM!) делает `POST` на **произвольный URL из конфига**. Это SSRF-примитив: любой, кто может править JSON, заставляет SYSTEM стучаться во интранет.

Также `EscapeHtml` (`:413-420`) экранирует только `&<>`, но не кавычки. В `NotifyUpdateAvailableAsync:443` `downloadUrl` вставляется в `href="..."` — при вредоносном `title/version` с GitHub возможна инъекция атрибута (низкий риск, но чинится одной строкой).

### 1.2. Rate Limiting — ОТСУТСТВУЕТ. Спам 20 сообщений — ДА
Это главная находка.

- Локальные уведомления трея агрегируются образцово: `TrayNotificationAggregator` — дебаунс 400 мс, `Coalesce` по категориям, `MaxItemsPerCategory=3`, `MaxBalloonChars=255`, критичные `SystemDanger` — вне очереди.
- Pipe-батчинг `NotificationService` (200 мс, `FlushBatch`) агрегирует только **доставку в UI**, но `DispatchRemoteNotification` вызывается **в `Enqueue` на каждое сообщение** (`NotificationService.cs:49-52`), fire-and-forget без очереди.
- Итог: установка софта → 20× `EntryAdded` → 20× `NotifyNewEntryAsync` → до 40 параллельных HTTP-запросов. Никакой агрегации, дедупликации, минимального интервала.
- Лимиты API: Telegram ~30 msg/s в один чат + 1 msg/s в среднем — 20 подряд уже на грани `429`; Discord webhook ~30 запросов/мин — 20 событий = почти гарантированный `429`. Код `429`/`Retry-After` **не обрабатывает** (`IsSuccessStatusCode` → только `LogWarning`), повтора нет, очередь нет.
- Усугубление: каждый `Notify*` делает `_store.Load()` — полный ре-парс `startup-monitor.json` на каждое событие (I/O-амплификация при шквале).

**Рекомендация (P0):** remote-очередь с коалесцированием по образцу трея: окно 5–10 с, сводное сообщение «Обнаружено N новых записей», `SemaphoreSlim(1)` + `Retry-After` + джиттер, отдельные флаги лимитов Telegram/Discord, кэшировать `Settings` вместо `Load()` на событие.

Дополнительно: `NotifyUpdateAvailableAsync` игнорирует флаги `RemoteNotifyOn*` (шлёт всегда, если канал включён) — непоследовательно.

### 1.3. Хранение токенов — ОТКРЫТЫМ ТЕКСТОМ (P0 для публичного релиза)
`AppSettings` (`AppConfig.cs:97-115`): `TelegramBotToken`, `TelegramChatId`, `DiscordWebhookUrl`, плюс `AiApiKey`, `KasperskyApiKey` — plain strings в `startup-monitor.json` рядом с бинарником службы. Нет `ProtectedData`/DPAPI, нет ACL. Файл читаем любым локальным пользователем → утечка даёт:
- полный контроль над ботом (чтение чата, спам от имени бота);
- право писать в Discord-канал через webhook;
- AI-ключ пользователя.

Отдельно: `KasperskyApiKey = "Zem+jUJbQEiqHPpwsfjTyQ=="` захардкожен дефолтом в `AppConfig.cs:135` — секрет в исходниках, уйдёт в публичный репозиторий. Плюс токены, вероятно, отдаются по именованному каналу в `GetSettings`-ответах — любой локальный процесс может их вычитать (проверить ACL пайпа до релиза).

**Рекомендация:** DPAPI (`ProtectedData.Protect` с `CurrentUser`/`LocalMachine` по месту хранения) + маска в логах/pipe (`***`), `chmod`/ACL только SYSTEM+Admin, убрать секрет Kaspersky из кода.

## 2. Логирование — диск гигабайтами НЕ забьётся, но есть нюансы

| Компонент | Конфигурация | Оценка |
|---|---|---|
| Служба (`Program.cs:35-52`) | Serilog `logs/service-.log`, `RollingInterval.Day`, `retainedFileCountLimit: 14`, `MinimumLevel.Information` | Ротация по суткам + 14 файлов — ок. **Но нет `fileSizeLimitBytes`/`rollOnFileSizeLimit`** — один дневной файл при шторме событий неограничен. На практике — десятки МБ/день, не ГБ, но лимит 50–100 МБ стоит добавить |
| UI (`App.xaml.cs:36-50`) | Ручной `App.Log`: при `>1_000_000` байт → `Move(..., ".1", overwrite:true)` | Диск защищён (макс. ~2 МБ), но диагностика страдает: всего **один** бэкап, перезаписывается молча |
| `LogReaderService` | `ReadAllLogs`: 3 последних `service-*.log` + `ui-debug.log`, `MaxLines=2000`, `FileShare.ReadWrite`, сортировка свежие-сверху | Корректно, блокировок нет. **Но `.1` ротации UI не читается** — после ротации свежая история «пропадает» из просмотрщика. `ReadTail` читает весь файл построчно со скользящим окном — при 1 МБ ок, при росте — O(n) |
| История (`JsonDataStore.TrimHistoryFile`, `HistoryDays=30`) | Чистка старше 30 дней при старте | Ок, но нет капа по количеству |

PERF-отчёты каждые 5 мин (служба + UI) при `Information` — приемлемый шум.

**Рекомендация (P1):** Serilog — добавить `fileSizeLimitBytes: 50MB, rollOnFileSizeLimit: true`; UI — держать 3–5 ротаций с датой (`ui-*.log`) и читать их в `LogReaderService` по маске, как сделано для службы.

## 3. Диалоговые окна

### 3.1. `PortDefenderConfigWindow` — валидации фактически нет (P0)
- **Порты:** `ParsePorts` (`:81-91`) молча отбрасывает мусор (`"99999, abc"` → сохраняется урезанный список, статус «сохранено»). Пользователь не узнает, что часть портов проигнорирована. Позитив: `FirewallGuard.IsValidPort` используется, дубликаты режутся.
- **Whitelist:** `ParseWhitelist` (`:95-106`) — **вообще без проверки**. Любая строка («foobar», «999.999.999.999») уходит в конфиг. При этом готовый валидатор `FirewallGuard.IsBlockableAddress` (loopback/unspecified/multicast-запрет) и CIDR-парсинг уже есть в кодовой базе — просто не вызван. Whitelist `127.0.0.1`/`0.0.0.0` даст ложное чувство защиты.
- **Баг «0 = навсегда»:** XAML-подсказка (`PortDefenderConfigWindow.xaml:62`) обещает «0 = навсегда», а код (`:128-132`) делает `ParsePositiveInt(..., def)` + `if (==0) =дефолт` — **постоянный бан через UI установить невозможно**, `0` тихо превращается в `15`.
- Нет сообщений об отвёргнутых значениях, нет клампов диапазонов (`MaxAttempts=99999999` проходит), сохранение закрывает окно даже при мусоре во всех полях.

**Что сделать:** вернуть список отвёргнутых токенов в `TxtStatus` + не закрывать окно при ошибках; whitelist — через `IsBlockableAddress` + CIDR; `BanDuration`: разрешить `0`, клампы (напр. `1..1000` попыток, `5..3600` окно, `0..43200` бан); числовые поля — `PreviewTextInput` только цифры.

### 3.2. `EntryDetailWindow` — мелочи (P2)
Тонкая обёртка над VM-командами — валидировать нечего. Замечания: `async void` без `try/catch` (исключение → `DispatcherUnhandled`, кнопка «виснет» без фидбэка); `OnRemoveClick` закрывает окно без проверки успеха `RemoveFromSystem`; нет дизейбла кнопок на время операции (двойной клик = двойная команда); `DataContext = entry`-снимок протухает после обновления VM.

### 3.3. `SystemInfoWindow` — мелочи (P2)
`SystemComputerInfo.Collect()` в конструкторе и в `OnRefreshClick` — синхронно в UI-потоке (WMI/реестр могут подвесить окно → вынести в `await Task.Run`). `Clipboard.SetText` обёрнут — ок. `ToReportString()` копируется в буфер без предупреждения — там могут быть имя пользователя/пути, для публичного релиза добавить дисклеймер.

## 4. Готовность к публичному релизу — НЕ ГОТОВ (2×P0)

| Приоритет | Задача |
|---|---|
| **P0** | Remote rate-limit/агрегация + обработка `429`/`Retry-After` |
| **P0** | Шифрование секретов (DPAPI), ACL конфига, убрать Kaspersky-ключ из исходников, аудит отдачи токенов по pipe |
| **P1** | Валидация `PortDefenderConfigWindow` (whitelist через `IsBlockableAddress`, фидбэк по отвёргнутым, починить `0=навсегда`, клампы) |
| **P1** | Serilog `fileSizeLimitBytes` + UI-ротация на N файлов + чтение `.1`/маски в `LogReaderService` |
| **P2** | `EntryDetailWindow`: try/catch + busy-disable + закрытие по результату; `SystemInfoWindow`: асинхронный `Collect`, дисклеймер перед копированием; валидация форматов токена/chat_id/webhook до отправки; `EscapeHtml` + кавычки |

Сильные стороны (сохранить): неотказоустойчивость отправки (ничего не роняет службу), `TestChannelAsync` с раздельной диагностикой каналов, эталонная агрегация трея, суточная ротация Serilog 14 дней, `FileShare.ReadWrite` при чтении логов.

