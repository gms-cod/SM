# 🏛️ Архитектура платформы Startup Monitor

[English User Guide](USER_GUIDE.md) | [Руководство пользователя](USER_GUIDE.ru.md) | [Главная страница](../README.ru.md)

Документ описывает архитектурные принципы, структуру подсистем, межпроцессное взаимодействие (IPC) и модели безопасности **Startup Monitor v2.1.1**.

---

## 1. Общая топология и разделение ответственности

Архитектура системы построена на строгом разделении привилегий (Separation of Concerns & Least Privilege) между высокопривилегированным системным демоном и интерактивным пользовательским интерфейсом.

```
┌────────────────────────────────────────────────────────────────────────┐
│             StartupMonitor.UI (WPF .NET 8 Fluent 2, win-x64)           │
│                                                                        │
│  MainViewModel ─── TaskManagerVM ─── ThemeService ─── LocalizationSvc │
│         │                                                              │
│         ▼                                                              │
│  PipeClient (Named Pipe IPC) ─── TrayNotificationAggregator ───────────┘
│         ▲                                   ▲
│         │  \\.\pipe\StartupMonitor          │  \\.\pipe\StartupMonitor_Events
│         │  (Request-Response / Delta)       │  (Push Events / Coalesced)
│         ▼                                   │
┌────────────────────────────────────────────────────────────────────────┐
│      StartupMonitor.Service (Windows Service / NT AUTHORITY\SYSTEM)     │
│                                                                        │
│  ├─ ETW Process Engine (Microsoft-Windows-Kernel-Process, 0% CPU Idle) │
│  ├─ Watchers: Registry, StartupFolders, Services, Tasks, WmiEvents     │
│  ├─ Network Security Hub: Dynamic WFP Filters, Port Defender, IP Helper │
│  ├─ ProcessInspectorService & NtSuspendProcess/NtResumeProcess APIs     │
│  ├─ AutoGuardEngine (Превентивные политики безопасности)               │
│  ├─ DelayedStartupManager (Session 0 Bypass via DuplicateTokenEx)      │
│  ├─ KasperskyScanService & AiRatingService (Groq / Ollama / OpenAI)    │
│  ├─ JsonDataStore: Two-Phase Commit, fsync, automated .backup rotation │
│  └─ AuthenticodeVerifier (WinVerifyTrust) & CliHeuristicAnalyzer       │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Ключевые архитектурные модули

### 2.1. ETW Kernel Engine (Zero-Overhead Process Monitor)
- **Технология**: Event Tracing for Windows (`Microsoft-Windows-Kernel-Process`, Provider GUID `{22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716}`).
- **События**: Event ID 1 (ProcessStart) и Event ID 2 (ProcessStop).
- **Особенности**:
  - Суб-миллисекундная реакция на запуск процессов без опроса через WMI или Win32 Toolhelp32.
  - Нулевая нагрузка на процессор (0% CPU) в режиме ожидания.
  - Защита от коллизий повторно используемых идентификаторов процессов (**PID Recycling Guard**).
  - Бесшовный резервный механизм сверки (**Fallback Reconciliation Poller**) при перегрузке или сбое нативной сессии ETW.

### 2.2. Dynamic WFP Firewall & Port Defender
- **Технология**: Windows Filtering Platform (WFP / `FwpmFilterAdd0`) и COM-интерфейс `INetFwPolicy2`.
- **Временные фильтры (Transient Filters)**:
  - Правила блокировки или разрешения, выданные в диалоге оповещения, регистрируются как временные WFP-фильтры с автоматическим освобождением дескрипторов при остановке службы.
- **Port Defender (Fail2ban Engine)**:
  - Мониторинг входящих соединений на критические порты (RDP 3389, SSH 22, SMB 445).
  - Агрегация неудачных попыток подключения в скользящем временном окне.
  - Автоматическая изоляция атакующего IP-адреса с обязательной валидацией и защитой частных подсетей RFC 1918 (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`).

### 2.3. Safe 29-Location Startup Management
- Комплексный мониторинг 29 точек автозапуска:
  - 12 веток системного и пользовательского реестра (Run, RunOnce, Policies, Explorer).
  - 2 физических каталога ярлыков (User Startup, All Users Startup).
  - Диспетчер служб Windows (SCM) с фильтрацией Auto/Delayed-Auto.
  - Планировщик задач Windows Task Scheduler (триггеры входа, загрузки и системных событий).
  - WMI Event Subscriptions (`root\subscription`).
  - Winlogon Userinit / Shell / Notify, Image File Execution Options (IFEO), BHO и AppInit_DLLs.
- **Двухфазная безопасная деактивация (Disable)**:
  - При отключении запись не удаляется: создается сериализованный снимок `DisablePayload`, ярлыки помечаются суффиксом `.disabled`, а ключи реестра переносятся в изолированный ключ хранения.
  - Восстановление точки автозапуска выполняется без потери аргументов командной строки и системных контекстов.

---

## 3. Межпроцессное взаимодействие (IPC)

Взаимодействие между службой и графической оболочкой осуществляется через защищенные локальные именованные каналы Windows (Named Pipes):

1. **Канал команд и запросов**: `\\.\pipe\StartupMonitor` (Request-Response).
   - Обработка команд `GetAllEntries`, `DisableEntry`, `EnableEntry`, `RemoveFromSystem`, `RestoreEntry`, `AddToBlacklist`, `GetBlacklist`, `GetRatings`, `GetStatus`, `GetHistory`, `SetAiConfig`.
   - Валидация прав клиента: аутентификация через дескриптор безопасности канала (DACL). Непривилегированные анонимные вызовы и сетевые подключения отклоняются гардом `TokenImpersonationLevel`.
2. **Канал потоковых событий**: `\\.\pipe\StartupMonitor_Events` (Server-Push).
   - Асинхронная доставка событий добавления, изменения, блокировки и сетевых алертов.
   - Буферизация и подавление дребезга (Coalescing / Single-Flight Debounce 200–500 мс) для исключения перегрузки UI при массовых операциях инсталляторов.

---

## 4. Хранилище данных и транзакционная устойчивость

Все файлы конфигурации хранятся в защищенном рабочем каталоге:
- `settings.json` — конфигурация службы и интерфейса;
- `blacklist.json` — реестр превентивных правил блокировки;
- `startup-entries.json` — снимок обнаруженных элементов автозагрузки;
- `removed-entries.json` — архив удаленных записей с метаданными восстановления;
- `history.jsonl` — журнал аудита в формате JSON Lines (append-only);
- `ui-settings.json` — персональные параметры пользователя.

### Защита от повреждений при сбоях питания (Power-Loss Protection):
- Запись во временный файл `.tmp` с вызовом `FileStream.Flush(flushToDisk: true)` (аппаратный `fsync`).
- Атомарная замена целевого файла через системный вызов `File.Replace`.
- Автоматическая ротация резервных копий `.backup` с мгновенным самовосстановлением при обнаружении поврежденного JSON.

---

## 5. Тестирование и гарантии надежности

Кодовая база Startup Monitor сопровождается полным комплектом модульных, интеграционных и системных тестов:
- **Общее число автоматических тестов**: **1,368 тестов**.
- **Успешность прохождения (Pass Rate)**: **100% (1368 passed, 0 failed)**.
- **Стандарты качества**: Политика Zero-Crash и Zero-Stub на всех системных границах.
