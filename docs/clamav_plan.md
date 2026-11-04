# План интеграции ClamAV (локальный)

> Статус: **план, не реализовано**. Не начинать реализацию без подтверждения пользователя.

## Обзор архитектуры

ClamAV работает как локальный демон (`clamd`) на порту TCP 3310. Служба (LocalSystem) ходит к нему по протоколу INSTREAM (файл передаётся потоком, clamd не требует доступа к пути файла). Кнопка «Проверить ClamAV» в модальном окне записи → pipe-команда → служба → clamd → результат кэшируется в записи и показывается цветом (зелёный/красный/серый).

---

## Фаза 0. Установка и настройка ClamAV на ПК (вручную, один раз)

1. **Скачать** MSI-инсталлятор Windows x64 с https://www.clamav.net/downloads (версия 1.x).
2. **Установить** в `C:\Program Files\ClamAV\`. В папке появятся `clamd.exe`, `clamscan.exe`, `clamdscan.exe`, `freshclam.exe`, `conf_examples\`.
3. **Создать конфиги** — скопировать примеры:
   - `conf_examples\clamd.conf` → `..\clamd.conf`
   - `conf_examples\freshclam.conf` → `..\freshclam.conf`
4. **Отредактировать `clamd.conf`** — критично (иначе clamd откажется стартовать):

   | Опция | Значение | Зачем |
   |---|---|---|
   | `Example` | **удалить/закомментировать** | без этого clamd не запускается (киллер-фича) |
   | `DatabaseDirectory` | `"C:\Program Files\ClamAV\database"` | где лежат базы |
   | `TCPSocket 3310` + `TCPAddr 127.0.0.1` | включить | слушаем только localhost (безопасно, наружу не высовываем) |
   | `LogFile` | `"C:\ProgramData\ClamAV\clamd.log"` | логи; папку `C:\ProgramData\ClamAV` создать заранее |
   | `LogTime yes`, `LogClean yes` | включить | читаемые логи |
   | `StreamMaxLength 100M` | поднять с 25M | файлы автозапуска вроде uTorrent.exe ~40 МБ; дефолт 25M их режет |
   | `DetectPUA false` | оставить выкл | не флагать безобидные утилиты |
   | `SelfCheck 600` | дефолт | проверка своих баз |

   Всё остальное в примере (MaxScanSize, MaxFileSize, MaxRecursion, MaxFiles) — оставить дефолт (100M/25M/16/10000).

5. **Отредактировать `freshclam.conf`**:

   | Опция | Значение |
   |---|---|---|
   | `Example` | удалить |
   | `DatabaseDirectory` | `"C:\Program Files\ClamAV\database"` |
   | `LogFile` | `"C:\ProgramData\ClamAV\freshclam.log"` |
   | `NotifyClamd` | `"C:\Program Files\ClamAV\clamd.exe"` — после обновления баз clamd сам перечитает их (RELOAD) |
   | `CheckInterval 60` | проверять обновления раз в час |

6. **Скачать базы впервые**: `freshclam.exe` вручную (от администратора) — вытянет `main.cvd`, `daily.cvd`, `bytecode.cvd` ≈ **300 МБ** в `database\`.
7. **Запустить демон**: служба «ClamAV daemon» (MSI регистрирует её) или вручную `clamd.exe`. Запускать от админа, чтобы базы читались.
8. **Проверить работоспособность**:
   - Создать `C:\Temp\eicar.txt` с содержимым `X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*`
   - `clamdscan C:\Temp\eicar.txt` → должен ответить `FOUND` (Eicar-Test-Signature) — это проверка через TCP, именно наш путь.
   - `clamdscan C:\Windows\System32\calc.exe` → `OK` (без ложных срабатываний).

---

## Фаза 1. Shared (модели)

- **`StartupEntry`**: новые поля — `ClamavStatus` (string: `NotScanned`/`Clean`/`Infected`/`Error`/`TooLarge`), `ClamavDetection` (имя вируса), `ClamavCheckedAt` (DateTime?). JSON — с теми же дефолтами, чтобы старые конфиги не ломались.
- **`AppConfig`**: секция `clamav: { enabled: false, host: "127.0.0.1", port: 3310 }`.
- **`PipeMessage`**: тип `ScanForVirus` (payload — id записи) + тип `ScanResult` для ответа (или переиспользовать существующий ответный канал).

## Фаза 2. Служба

- NuGet: **nClam** (готовый клиент clamd, протокол INSTREAM; ~200 строк самим писать не нужно).
- **`Services\VirusScan\ClamavService.cs`** (рядом с `HashLookupService`):
  - `PingAsync()` при старте службы — если clamd не отвечает, пишем в лог «ClamAV недоступен», кнопка будет показывать серый статус.
  - `ScanAsync(entry)`:
    1. Guard: `enabled` выключен / нет `FilePath` / уже сканирован (кэш по SHA256, как у HashLookup) → skip.
    2. PING → нет ответа → `Error`.
    3. `SendAndScanFileAsync(path)` с таймаутом 60с (файл уходит потоком, clamd видит содержимое без прав на путь).
    4. Маппинг nClam `ClamScanResult` → `Clean` / `Infected` (+ имя из `InfectedFiles`) / `Error` (в т.ч. `INSTREAM size limit exceeded` → `TooLarge`).
    5. Сохранить поля в запись через store (как AI-рейтинг).
- **`PipeServer`**: case `ScanForVirus` → `ClamavService.ScanAsync(id)` → ответ UI с итоговым статусом.

## Фаза 3. UI

- **`EntryDetailWindow`**: кнопка «Проверить ClamAV» (рядом с AI-рейтингом) + статус-индикатор: зелёный (Clean), красный (Infected + имя детекта), серый (не проверено / ошибка / выключено).
- **`PipeClient`**: `ScanForVirusAsync(entryId)`.
- **Настройки**: в MainWindow секция ClamAV: тумблер enabled + поля host/port (дефолты уже внутри, можно даже не выводить — но для ленивости просто тумблер).
- Локализация RU/EN.

## Фаза 4. Тесты и публикация

1. Unit-тесты: маппинг `ClamScanResult` → статус, skip-логика, парсинг `TooLarge`.
2. `dotnet build` (0 ошибок) → `dotnet test` (358+ зелёных).
3. `PublishToSMon` (процессы убить перед сборкой).
4. Ручная проверка: кнопка на записи uTorrent → Clean; подложить EICAR-файл в автозапуск → Infected с красным статусом.

## Что НЕ делаем (осознанно)

- On-access сканирование, конфликт с Defender (он и так выключен для служб), загрузка файлов > StreamMaxLength, кластеры/реплики clamd, фоновое сканирование всех записей (кнопка — по запросу; добавим, если попросят).
- Обновление баз — забота freshclam (служба «ClamAV Update»), у нас только чтение.
