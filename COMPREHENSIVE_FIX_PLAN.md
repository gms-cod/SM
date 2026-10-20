# Сводный план комплексного устранения дефектов Startup Monitor (v1.9.0)

Документ сформирован на основе независимого параллельного аудита триадами разнородных моделей OpenCode (`DeepSeek V4.1 Flash`, `Space Bunny`, `LongCat 2.5 Preview`) с полным циклом рассуждений до 1 часа.

---

## Матрица реализации и готовности (100% Выполнено)

| Приоритет | Категория | Статус | Проверка |
| :---: | :--- | :---: | :---: |
| **P0** | **Блокеры безопасности и целостности** | **ВЫПОЛНЕНО (4/4)** | Тесты 1232/1232 пройдены |
| **P1** | **Высокий риск / Обязательно к релизу** | **ВЫПОЛНЕНО (8/8)** | Тесты 1232/1232 пройдены |
| **P2** | **Производительность, локализация и темы** | **ВЫПОЛНЕНО (6/6)** | 716/716 ключей ru/en, Dark/Light темы |

---

## 1. Блокеры (Приоритет P0) — Завершено

### [P0-01] Устранение уязвимости Local Privilege Escalation (LPE) в Named Pipe
- **Файл:** [`src/StartupMonitor.Service/Pipe/PipeSecurityFactory.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Pipe/PipeSecurityFactory.cs)
- **Результат:** Удалено небезопасное широкое правило для `AuthenticatedUserSid`. Для интерактивных пользователей (`InteractiveSid`) доступ ограничен на чтение и запись с явным снятием флага создания серверных экземпляров канала `~PipeAccessRights.CreateNewInstance`.

---

### [P0-02] Ликвидация раздачи живой ссылки `_cache` и изоляция транзакций в `JsonDataStore`
- **Файлы:** [`src/StartupMonitor.Shared/Models/JsonDataStore.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Shared/Models/JsonDataStore.cs), модели данных.
- **Результат:** Метод `Load()` теперь всегда возвращает глубокую изолированную копию данных (`CloneSnapshot`). В модели `StartupEntry`, `BlacklistRule`, `RemovedEntry`, `HistoryEntry`, `AppSettings` внедрены методы глубокого клонирования `Clone()`. Внутренние мутации оперируют оригинальным кэшем строго под `lock (_lock)`.

---

### [P0-03] Защита от повреждения хранилища при BSOD / сбое питания (`fsync` + ротируемый бэкап)
- **Файл:** [`src/StartupMonitor.Shared/Models/JsonDataStore.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Shared/Models/JsonDataStore.cs)
- **Результат:** Внедрен принудительный сброс дисковых кэшей Windows `FileStream.Flush(flushToDisk: true)` перед подменой файлов. Реализована автоматическая ротация резервных копий `.backup` и автовосстановление из них при обнаружении битого или неполного основного файла.

---

### [P0-04] Изоляция исключений `RegistryWatcher` от падения процесса службы (Zero-Crash)
- **Файл:** [`src/StartupMonitor.Service/Services/RegistryWatcher.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/RegistryWatcher.cs)
- **Результат:** Коллбэк системного пула потоков `OnRegistryKeyChanged` полностью изолирован блоком `try/catch`. Ошибки дескрипторов и повторного взвода уведомлений логируются без падения процесса службы Windows.

---

## 2. Сеть, Firewall и высокий приоритет (Приоритет P1) — Завершено

### [P1-01] Ликвидация искусственной 5-секундной задержки в `AiRatingService`
- **Файл:** [`src/StartupMonitor.Service/Services/Ai/AiRatingService.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/Ai/AiRatingService.cs)
- **Результат:** Безусловная 5.5-секундная задержка из блока `finally` удалена. Успешные запросы выполняются на максимальной скорости сети без искусственного замедления.

---

### [P1-02] Исправление бага приведения типов в Circuit Breaker `AiRatingService`
- **Файл:** [`src/StartupMonitor.Service/Services/Ai/AiRatingService.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/Ai/AiRatingService.cs)
- **Результат:** Расчет задержки 429 защищен через `Math.Clamp((long)retryAfter.Value, 1000L, 120_000L)`.

---

### [P1-03, P1-05] Потокобезопасный `EventPipeServer` и предотвращение утечек дескрипторов
- **Файл:** [`src/StartupMonitor.Service/Pipe/EventPipeServer.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Pipe/EventPipeServer.cs)
- **Результат:** Идентификаторы клиентов формируются атомарным генератором `Interlocked.Increment(ref _nextClientId)`. При сбоях подключения или отмене дескриптор канала гарантированно освобождается через `pipe?.Dispose()`.

---

### [P1-04] Централизация инкремента ревизий данных (`_revision`)
- **Файл:** [`src/StartupMonitor.Shared/Models/JsonDataStore.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Shared/Models/JsonDataStore.cs)
- **Результат:** Добавлена монотонно возрастающая ревизия `Revision` (через `Interlocked.Increment`), обновляемая при любой модификации хранилища.

---

### [P1-06] Защита от шторма событий реестра (Debounce & Single-Flight)
- **Файл:** [`src/StartupMonitor.Service/Services/RegistryWatcher.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/RegistryWatcher.cs)
- **Результат:** Внедрен таймер агрегации (500 мс) с атомарным флагом `_isScanning` и флагом необходимости повторного прохода `_rescanRequired`. Пачка из сотен одновременных изменений реестра объединяется в единое сканирование.

---

### [P1-07] Ограничение емкости очереди уведомлений (Bounded Queue)
- **Файл:** [`src/StartupMonitor.Service/Services/NotificationService.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/NotificationService.cs)
- **Результат:** Буфер ограничен емкостью `MaxQueueCapacity = 10_000` с политикой вытеснения устаревших сообщений (`TryDequeue`).

---

### [P1-08] Защита от зависаний на сетевых (UNC) путях в `AiRatingService`
- **Файл:** [`src/StartupMonitor.Service/Services/Ai/AiRatingService.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/Ai/AiRatingService.cs)
- **Результат:** Добавлена проверка на UNC-пути (`!path.StartsWith(@"\\")`) и защитный блок `try/catch` при извлечении метаданных PE/версий файлов.

---

### [P1-09] Полноценный белый список программы и порта в `NetworkConnectionAlertService`
- **Файлы:** [`NetworkConnectionAlertService.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs), [`NetworkConnectionAlertWindow.xaml(.cs)`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.UI/Views/Dialogs/NetworkConnectionAlertWindow.xaml.cs)
- **Результат:**
  - При выборе «Добавить в белый список»: создается разрешающее правило для программы в Windows Firewall, программа заносится в постоянный белый список `FirewallWhitelistedApps` с мгновенным исключением повторных алертов.
  - При разрешении по порту: порт фиксируется в `FirewallAllowedPorts`, всплывающие окна по данному порту подавляются навсегда.

---

### [P1-10] Тихий режим (Silent Mode) для сокетов без процесса (`PID = 0`)
- **Файлы:** [`ProcessNetworkTracker.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs), [`NetworkSecurityHubView.xaml(.cs)`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml.cs)
- **Результат:**
  - Резолв `PID = 0` возвращает `"System (Kernel/Idle)"` вместо пустой строки.
  - На странице «Сетевая безопасность» добавлен чекбокс «Тихий режим для системных сокетов (PID = 0)» с сохранением настройки в конфигурацию и немедленной синхронизацией со службой через IPC.

---

### [P1-11] Потокобезопасность `GetAllRules()` в `WindowsFirewallService`
- **Файл:** [`src/StartupMonitor.Service/Services/WindowsFirewallService.cs`](file:///d:/Nas/Coding/StartupMonitor/src/StartupMonitor.Service/Services/WindowsFirewallService.cs)
- **Результат:** Метод `GetAllRules()` взят под блокировку `lock (_sync)` для исключения повреждения состояния COM-объекта `FwPolicy2.Rules`.

---

## 3. Удаление устаревших модулей (Стадия 0) — Завершено

- Модуль Toolbox (Tools) полностью исключен из всего решения (удалены папки, XAML-страницы, контроллеры, тесты, IPC-сообщения и навигация).

---

## 4. Проверка тем оформления и локализации — Завершено

- **Словари локализации:** 716 ключей в `Strings.resx` (en) и 716 ключей в `Strings.ru.resx` (ru). Полная синхронизация 100%, 0 пропусков, 0 пустых значений.
- **Темы оформления (Dark / Light):** Проверены обе палитры, ресурсы зафиксированы (`Freeze()`), динамическая адаптация контрастности по стандарту WCAG.
- **Тестовый набор:** 1232 теста успешно пройдены (0 сбоев).
