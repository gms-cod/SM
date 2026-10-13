# План реализации системы самообновления из GitHub и легитимной цифровой подписи (Code Signing)

## 1. Концепция и цели
1. **Самообновление (Auto-Update)**:
   - Проверка новых релизов на GitHub (`https://api.github.com/repos/gms-cod/Startup-Monitor/releases/latest`).
   - Настраиваемый режим в UI: тумблер включения/отключения автопроверки, ручная проверка по кнопке.
   - Пользовательский запрос на обновление: показ чейнджлога, выбор «Обновить сейчас / Напомнить позже / Пропустить».
   - Фоновая загрузка инсталлятора с проверкой SHA256 и тихая установка без потери данных.
2. **Легитимная цифровая подпись (Code Signing)**:
   - Обеспечение доверия Windows (отсутствие синего экрана SmartScreen «Неизвестный издатель»).
   - Локальная автоматическая подпись сборочных файлов через `signtool.exe` и доверенный сертификат.
   - Интеграция с открытыми центрами сертификации (SignPath Foundation / Certum Open Source).

---

## 2. Пошаговая реализация (План параллелизации по субагентам)

### Этап 1: ✅ Автоматизация локальной цифровой подписи (Code Signing)
- **Субагент A1 (Security & Build Automation)**:
  - Создание скрипта `scripts/sign-binaries.ps1`:
    - Проверка наличия/генерация сертификата разработчика `Startup Monitor Authenticode` с EKU Code Signing (`1.3.6.1.5.5.7.3.3`).
    - Подписание `StartupMonitor.Service.exe`, `StartupMonitor.UI.exe` и `StartupMonitorSetup-*.exe` через `signtool.exe` с SHA-256 и штампом времени RFC 3161 (`http://timestamp.digicert.com`).
  - Интеграция вызова скрипта подписи в `installers/build-setup.ps1` и `scripts/publish.ps1`.
  - Подготовка `.github/workflows/release-sign.yml` для автоматической сборки и подписи в облаке.

### Этап 2: ✅ Бэкенд службы и интеграция с GitHub Releases (Service & Shared)
- **Субагент B1 (Shared Models & IPC Data Contract)**:
  - Расширение `AppSettings`: `auto_check_updates` (bool, default: true), `auto_download_updates` (bool), `last_update_check_utc` (DateTime?), `skipped_update_version` (string?).
  - DTO модели: `UpdateInfo` (Version, Title, Changelog, ReleaseDate, DownloadUrl, Sha256, SizeBytes), `UpdateCheckResult`, `UpdateProgressInfo`, `UpdateSettingsPayload`.
  - Команды Named Pipe в `PipeMessageTypes`: `CheckForUpdates`, `DownloadUpdate`, `DownloadUpdateProgress`, `InstallUpdate`, `GetUpdateStatus`, `SetUpdateSettings`.
- **Субагент B2 (Service Backend & GitHub API)**:
  - Создание `GitHubUpdateService` в `StartupMonitor.Service`:
    - Асинхронный опрос `https://api.github.com/repos/gms-cod/Startup-Monitor/releases/latest` с SemVer-сравнением версий.
    - Фоновый таймер (раз в 12 часов при активной настройке `AutoCheckUpdates`).
    - Загрузка дистрибутива во временную директорию с валидацией контрольной суммы SHA-256 и расчетом прогресса/скорости.
    - Запуск тихой установки: Inno Setup `/SILENT /NORESTART /SUPPRESSMSGBOXES` с правами администратора.
    - Удалённые уведомления в Telegram и Discord через `RemoteNotificationService.NotifyUpdateAvailableAsync`.

### Этап 3: ✅ Пользовательский интерфейс и нотификации (WPF UI)
- **Субагент C1 (UI Settings & Preferences)**:
  - Карточка «Обновление программы» в настройках:
    - Тумблер `Автоматически проверять обновления`.
    - Кнопка `Проверить обновления` с индикатором загрузки и выводом статуса.
    - Строка с текущей версией и временем последней проверки.
    - Двуязычная локализация (`Strings.resx` / `Strings.ru.resx`).
- **Субагент C2 (Update Notification & Modal Window)**:
  - Создание модального окна `UpdateAvailableWindow.xaml`:
    - Отображение новой версии, даты публикации и скроллируемого чейнджлога (Changelog).
    - Кнопки: «Обновить сейчас» (с авто-переключением на «Установить и перезапустить»), «Напомнить позже», «Пропустить эту версию».
    - Индикатор прогресса загрузки дистрибутива (Progress Bar с процентами и скоростью скачивания).
  - Интеграция уведомлений в UI и системный трей.

### Этап 4: ✅ Тестирование и верификация (QA & Verification)
- **Субагент D1 (Unit Tests)**:
  - Тесты нормализации тегов SemVer (`CleanVersionTag_NormalizesProperly`).
  - Тесты парсинга версий (`ParseSemVer_ParsesValidVersionStrings`).
  - Тесты выявления более свежей версии (`SemVerComparison_CorrectlyDetectsNewerVersion`).
  - Тесты поиска инсталлятора среди ассетов релиза (`FindInstallerAsset_PicksStartupMonitorSetupExe`).
  - Тесты парсинга хешей SHA-256 из описания релиза (`ExtractSha256Checksum_FindsChecksumInBody`).
  - Тесты DTO-моделей и сериализации (`GitHubUpdateServiceTests`).
- **Оркестратор**:
  - Полная сборка всех проектов решения (`StartupMonitor.Shared`, `StartupMonitor.Service`, `StartupMonitor.UI`, `StartupMonitor.Tests`).
  - Прогон тестов (`dotnet test`): 743 из 743 пройдены успешно (0 ошибок).

---

## 3. Легитимная цифровая подпись: Что нужно сделать разработчику?

Чтобы Windows и антивирусы безоговорочно доверяли `.exe` файлу (без синего экрана SmartScreen «Система Windows защитила ваш компьютер» и предупреждения «Неизвестный издатель»), подпись должна быть выпущена **доверенным корневым центром сертификации (Public CA)**.

### Вариант 1. Бесплатно: SignPath Foundation (Идеально для Open-Source)
Поскольку ваш репозиторий открыт (`https://github.com/gms-cod/Startup-Monitor`), вы имеете право на бесплатный официальный сертификат Code Signing:
- **Что это дает**: Программа подписывается признанным во всем мире сертификатом, SmartScreen не ругается, Windows видит официального издателя.
- **Что требуется от вас**:
  1. Зарегистрироваться на сайте [signpath.org/apply](https://signpath.org/apply).
  2. Указать ссылку на репозиторий `https://github.com/gms-cod/Startup-Monitor` и открытую лицензию (MIT / GPL).
  3. Пройти базовую верификацию автора проекта (подтверждение владения репозиторием GitHub).
  4. Добавить секретный токен `SIGNPATH_API_TOKEN` в настройки репозитория GitHub (`Settings -> Secrets and variables -> Actions`).
  5. Сборка и подпись происходят автоматически через GitHub Actions при выпуске каждого релиза.

### Вариант 2. Недорого: Certum Open Source Code Signing (~€70/год)
Если вы хотите иметь сертификат, привязанный лично к вам (на физическое лицо или ИП) для локальной подписи:
- Польский удостоверяющий центр **Certum** — единственный крупный CA, предоставляющий льготные тарифы для Open-Source разработчиков.
- **Что требуется от вас**:
  1. Заказ сертификата на сайте Certum (Open Source Code Signing).
  2. Подтверждение личности (паспорт / ID) и владение проектом.
  3. Доступ через облачный сервис Certum SimplySign (без необходимости покупать аппаратный USB-токен). Подпись ставится как на локальном ПК через консольную утилиту, так и в CI/CD.

### Вариант 3. Microsoft Azure Trusted Signing (Новый сервис от Microsoft)
- Официальный облачный сервис Microsoft для разработчиков (Pay-as-you-go, ~$10 в месяц).
- **Плюс**: Мгновенное максимальное доверие Windows Defender SmartScreen прямо из коробки, так как подпись валидируется самой экосистемой Microsoft.
- **Что требуется от вас**: Учетная запись Microsoft Azure с подтвержденным юридическим лицом или верифицированным аккаунтом разработчика.

### Вариант 4. Локальная автоматическая подпись (Делаем сами прямо сейчас)
- Создается локальный сертификат `Startup Monitor Code Signing`, регистрируется в доверенном корневом хранилище вашего ПК.
- Все файлы `StartupMonitor.Service.exe`, `StartupMonitor.UI.exe` и `Setup.exe` подписываются автоматически при каждой компиляции.
- **Эффект**: На вашем ПК и любых машинах, куда импортирован открытый сертификат (или в корпоративной сети), приложение определяется как полностью доверенное и подписанное с валидным штампом времени.

