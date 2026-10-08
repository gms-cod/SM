# Архитектура Startup Monitor

## Процессы

```
┌─────────────────────────────────────────────────────────────┐
│ StartupMonitor.Service (LocalSystem / консоль --console)    │
│                                                             │
│  Watcher'ы (BackgroundService):                             │
│   RegistryWatcher — Run/RunOnce/RunServices/Policies        │
│   OtherStartupLocationsWatcher — Winlogon, IFEO, GPO,       │
│     AppPaths, BHO, ContextMenuHandlers и др. (RegNotify)    │
│   StartupFolderWatcher — FileSystemWatcher + .disabled      │
│   ServiceWatcher — WMI __InstanceCreationEvent              │
│   TaskSchedulerWatcher — poll 30с, триггеры Logon/Boot      │
│                                                             │
│  Ядро:                                                      │
│   JsonDataStore — секционный конфиг (см. ниже)              │
│   BlacklistManager — правила + ApplyBlacklistAction         │
│   EntryOperations — disable/enable/remove/restore/kill      │
│   SystemScannerService — полный снимок (verify для cleanup) │
│   NotificationService — coalescing 200мс → батчи            │
│   AiRatingService (Groq) / RatingsCache                     │
│   HashLookupService (CIRCL)                                 │
│                                                             │
│  PipeServer (request-response) + EventPipeServer (push)     │
└──────────────┬──────────────────────────────────────────────┘
               │ Named Pipes: "StartupMonitor", "StartupMonitorEvents"
               │ JSON (System.Text.Json), DACL:
               │   SYSTEM/Administrators — FullControl
               │   Interactive — ReadWrite (сеть/аноним закрыты)
┌──────────────▼──────────────────────────────────────────────┐
│ StartupMonitor.UI (WPF, интерактивный пользователь)         │
│  PipeClient: request-response (таймаут 70с) + event pipe    │
│  Polling 5с (данные/рейтинги) + push-события                │
│  MVVM (CommunityToolkit.Mvvm), локализация resx RU/EN       │
│  Трей (Hardcodet.NotifyIcon) + WinRT Toast (fallback баллун)│
└─────────────────────────────────────────────────────────────┘
```

## Конфиг (секционный, рядом с exe)

| Файл | Содержимое | Кто пишет |
|---|---|---|
| `settings.json` | language, theme, AppSettings (вкл. `ai_api_key`) | служба (по командам UI) |
| `blacklist.json` | правила ЧС | служба |
| `startup-entries.json` | записи автозапуска (+ ai_rating, kaspersky_zone) | служба |
| `removed-entries.json` | бэкапы удалённых (restore_payload) | служба |
| `history.jsonl` | история событий, append-only | служба |
| `ui-settings.json` | настройки UI (фильтр, тумблеры, ключ AI) | UI |

Атомарная запись (tmp+move), секция пишется только при изменении (SHA256).
Миграция старого единого `startup-monitor.json` → `.migrated`.

## Жизненный цикл записи

```
обнаружена (watcher) → Enabled
  ├─ Disable  → значение/файл/служба/задача выключены, payload в записи → Disabled
  ├─ Enable   → восстановление из payload → Enabled
  ├─ Block    → правило ЧС → kill + удаление из системы (+бэкап в removed) → Blocked (остаётся в списке)
  │   └─ Unblock → удаление правила + RestoreToSystem из бэкапа → Enabled
  ├─ Remove   → kill + удаление из системы (+бэкап в removed) → уходит из списка
  │   └─ Restore (страница «Удалённые») → RestoreToSystem → снова Enabled
  └─ исчезла из системы → CleanupStaleEntries (60с) → RemovedStale (только Enabled-записи)
```

Защита kill: стоп-лист системных имён + сверка полного пути образа.
Повторная регистрация заблокированной — автоудаление (AddOrUpdate → ApplyBlacklistAction).

## IPC-протокол (команды)

GetAllEntries, DisableEntry, EnableEntry, RemoveFromSystem, RestoreEntry,
GetRemovedEntries, AddToBlacklist, RemoveFromBlacklist, GetBlacklist,
GetRatings, GetStatus, GetHistory, CleanStale, SetAiConfig.
События (push): EntryAdded, EntryModified, EntryDeleted, BlacklistMatch.

## Безопасность

- Pipe ACL (SYSTEM/Admins/Interactive), сетевой доступ закрыт
- API-ключ Groq — в конфиге, задаётся из UI; в коде секретов нет
- Критические процессы (svchost, lsass, …) в стоп-листе завершения
- Имя службы из реестра валидируется перед `sc` (инъекция аргументов)

## Сборка

`dotnet build` → PublishToSMon target → `SMon2\` (single-file self-contained win-x64,
внешних DLL нет). Конфиги бэкапятся/восстанавливаются при публикации.
Тесты: `dotnet test` (xUnit, 355 шт).
