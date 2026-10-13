# Agent 7: Update System, Installer & Lifecycle Production Readiness Audit

- Время анализа: 108.1 сек
- Статус: Успех

---

Аудит самообновления и инсталлятора — читаю целевые файлы.
Нашёл базовые проблемы — проверяю смежные файлы релизного цикла.
Проверяю, можно ли подменить репозиторий через настройки.
# Аудит самообновления и инсталлятора StartupMonitor

## 1. Безопасность `GitHubUpdateService.cs`

### 1.1. Проверка SHA-256 — КРИТИЧНО, обход возможен

**Механика сейчас (`GitHubUpdateService.cs:386-410`):**

```csharp
if (!string.IsNullOrWhiteSpace(update.Sha256)) { /* verify */ }
// иначе — молча пропуск, сразу File.Move + Status=Ready
```

Источники хэша (`ExtractSha256Checksum`, `GitHubUpdateService.cs:586-600`):

* только `body` релиза, два regex: `SHA-256: <hex>` и любой изолированный `\b[0-9a-f]{64}\b`;
* `.sha256`-ассет релиза **никогда не скачивается**, хотя комментарий в `586` обещает «или .sha256 ассета», а `publish_github_release.py:253-268,334,453` его реально загружает (`Setup-*.exe.sha256` + встраивает хэш в `body`).

**Выводы:**

1. **Подмена без хэша = выполнение произвольного бинарника.** Если релиз создан вручную / кастомным `--body` без хэша, `Sha256 == null` → проверка пропускается без warning в лог, файл сразу идёт в `InstallUpdate()` → `Process.Start(..., Verb="runas")`. Компрометация GitHub-аккаунта или залитый вручную релиз = RCE под UAC.
2. **Ложные срабатывания regex.** Второй regex подберёт любой 64-hex из чейнджлога (commit SHA, чужой хэш) и сравнит с ним инсталлятор → легитимное обновление будет отвергнуто как «mismatch», либо наоборот подтвердит чужой хэш.
3. **Нет проверки Authenticode.** `sign-binaries.ps1` подписывает, но `InstallUpdate()` (`452-482`) не делает `signtool verify` / `X509Certificate` проверку. SHA сверяет целостность, но не авторство.
4. **Нет allowlist хоста загрузки.** `DownloadUrl` берётся из `browser_download_url` без валидации (`FindInstallerAsset:550-584`). Сегодня это GitHub, но любой JSON с `browser_download_url=https://evil/...exe` будет скачан и запущен (актуально при компрометации аккаунта / кэширующего прокси).
5. **TOCTOU на установке.** `InstallUpdate()` не перепроверяет хэш/размер перед `Process.Start`. Файл лежит в `ProgramData\StartupMonitor\Updates` между `Ready` и кликом «Установить»; подмена в этом окне не детектируется.
6. **Размер не ограничен.** Нет max-size check — злонамеренный ассет в гигабайты = DoS диска.

**Что исправить (P0):**

* сделать хэш обязательным: `Sha256 == null → Status=Failed`, явная ошибка «релиз без SHA-256 отклонён»;
* докачать `.sha256`-ассет из `releaseRoot.assets` и сверять с ним, fallback — `body`;
* строгий regex только с префиксом (`SHA-256\s*[:=]`), убрать «голый hex»;
* перед `Process.Start` — повторный `ComputeSha256` + проверка подписи `X509Certificate.CreateFromSignedFile` / `WinVerifyTrust`, отказ при отсутствии подписи `CN=Startup Monitor`;
* allowlist: `DownloadUrl.Host ∈ {github.com, objects.githubusercontent.com, release-assets.githubusercontent.com, *.githubusercontent.com}` + `https only` + `EndsWith(.exe)`;
* лимит размера (напр. 500 МБ) и проверка свободного места.

### 1.2. Валидация URL репозитория — ХОРОШО

URL захардкожены (`22-23`):

```csharp
https://api.github.com/repos/gms-cod/SM/releases/latest
https://api.github.com/repos/gms-cod/Startup-Monitor/releases/latest
```

Настройки (`AppConfig.cs:167-179`, `PipeServer.cs:1296-1304`) содержат только `AutoCheckUpdates / AutoDownloadUpdates / SkippedUpdateVersion`. Подсунуть фишинговый репозиторий через UI/pipe/config **невозможно** — вектора нет. Проверено grep по `repos/` — конфигурируемого поля нет.

Замечания P2:

* два репозитория-зеркала удваивают поверхность (достаточно взломать один). Fallback срабатывает только на 404 (`155`), а не на 403/500 — при rate-limit второй репозиторий не пробуется;
* расхождение каноники: `.iss:11` и `sync-to-github.cmd` ссылаются на `gms-cod/SM`, доки/README — на `gms-cod/Startup-Monitor`. Унифицировать, иначе путаница «какой релиз легитимен».

### 1.3. Обработка сбоев — СРЕДНЕ

`DownloadUpdateAsync:294-446`:

* обрыв посреди загрузки → `catch (Exception)` → `Status=Failed`, но **`.download`-файл не удаляется** (удаляется только при mismatch, `402`). Мусор копится; следующая попытка удаляет в начале (`327-330`) — терпимо, но при repeated-обрывах папка растёт;
* нет докачки (resume), нет retry — каждый обрыв = заново;
* `totalBytes` берётся из `Content-Length ?? SizeBytes`, если оба 0 → `percent=0` вечно, но не падает — ок;
* старая версия затирается (`413-417: Delete + Move` — неатомарно; сбой питания между операциями = нет ни старого, ни нового);
* старые версии никогда не чистятся — `Updates/` растёт на ~30-80 МБ за релиз;
* фоновая закачка (`OnTimerTickAsync:119`) — fire-and-forget, результат выбрасывается, повторный запуск блокируется только проверкой `Status != Downloading/Ready` без лока — гонка двух `DownloadUpdateAsync` возможна (один отменяет другой через `_downloadCts.Cancel()` под локом, но `_currentProgress` перезаписывается без синхронизации с `progressCallback`);
* `Timer(async _ => ...)` — `async void`, при долгой проверке тики перекрываются; `HttpClient` без общего `Timeout` (только `ConnectTimeout 15s`), зависший стрим держит таймер;
* `UpdateAvailableWindow.xaml.cs:99` — `DownloadUpdateAsync()` возвращает `bool started`, но не подписывается на итог: если закачка упадёт, окно покажет ошибку только через `UpdateProgressReceived/Failed` — это есть (`66-71`), ок. Но кнопки Cancel нет, повторный клик после `Failed` снова вызывает download — ок. `_isReadyToInstall` никогда не сбрасывается при новой проверке.

**Исправить (P1):** `finally { try Delete(.download) on fail/cancel }`, атомарная замена (`Move with overwrite` / временное имя + `File.Replace`), чистка `Updates/` (оставлять последние N), `Timeout=10 мин`, `Interlocked`/лок на статусе, кнопка «Отмена» → `CancelDownload`.

---

## 2. Инсталлятор `StartupMonitor.iss`

### 2.1. Служба — РАБОТАЕТ, НО ХРУПКО

* `PrepareForInstall()` в `InitializeSetup` + `InitializeUninstall` (`80-107`) останавливает службу и `taskkill /F` оба exe, `Sleep(500)` — **гонка**: 500 мс часто мало для graceful-стопа службы; `[Files] flags: restartreplace` отложит замену до ребута, а `[Run] sc start` запустит **старую** версию. Нужен цикл ожидания `sc query` до `STOPPED` с таймаутом 10-15 с.
* `[Run] sc create` упадёт при обновлении (служба уже есть), `sc config` следом чинит `binPath` — де-факто работает, но в лог установщика пишется ошибка; чище: `sc stop + sc delete` в `PrepareForInstall` или `if not exist → create`.
* Нет `[InstallDelete]`/`AppMutex` — параллельный запуск двух установщиков возможен.
* `taskkill /F` по UI без предупреждения — потеря несохранённого состояния; приемлемо для апдейтера, но в интерактивном режиме лучше `CloseApplications=yes` уже есть — дублирование.
* `sc failure ... restart/5000/...` (`66`) — хорошо.
* Нет `MinVersion` (Win10+), нет явного `ArchitecturesAllowed=x64compatible`.

### 2.2. Права и антивирус — НОРМА

* `PrivilegesRequired=admin` (`36`) — корректно для `Program Files` + службы. Отдельных `Permissions` в `[Files]` нет → наследуются от `{app}` (только админ на запись) — правильно.
* `publish.ps1:54` делает `Add-MpPreference -ExclusionPath SMon2` — **только dev-машина**, в `.iss` исключений нет — правильно, ослаблять Defender на пользовательских машинах нельзя. Обратная сторона: без EV-сертификата (сейчас самоподписанный `CN=Startup Monitor` из `sign-binaries.ps1:29`) SmartScreen будет ругаться — это ожидать, в релиз-нотах предупредить.
* `publish.ps1:39-50` копирует бинарники прямо в `C:\Program Files\StartupMonitor` мимо инсталлятора — dev-хак, в CI его быть не должно (расхождение с установленным MSI/Inno-состоянием, служба может подхватить неподписанный билд).

### 2.3. Чистота деинсталляции — НЕПОЛНАЯ (P1)

`[UninstallRun] (72-76)` останавливает/удаляет службу и убивает UI. **Не удаляются:**

* `%ProgramData%\StartupMonitor\Updates\*.exe` (скачанные инсталляторы);
* логи/БД в `%ProgramData%\StartupMonitor` (если хранить — уточнить, но сейчас каталог остаётся);
* exclusion из `publish.ps1` на dev-машине остаётся навсегда;
* firewall-правила: в текущем `.iss` их нет — если служба создаёт их в рантайме, деинсталлятор их не чистит (проверить рантайм-код; в `.iss` добавить `[UninstallRun] netsh advfirewall ... delete` при необходимости);
* ярлык из `{autostartup}` Inno удаляет сам — ок.

Добавить в `[Code] procedure CurUninstallStepChanged(usDone)` удаление `Updates/`, логов, проверку `sc query` → ожидание `DELETE_PENDING`.

Прочее по `.iss`:

* `[Files]` перечисляет 4 файла из `..\SMon2` (`50-53`). Хрупко: любой новый артефакт publish (`.json`, `.pdb`, рантайм) молча не попадёт в установщик. Лучше `Source: "..\SMon2\*"; Excludes: "*.pdb,*.log"`.
* `OutputDir=..\installers` — инсталлятор собирается внутрь исходников; ок для ручного флоу, но артефакт коммитится по ошибке.
* `UninstallDisplayIcon={app}\app.ico` зависит от копирования `app.ico` — при пропуске иконки запись в «Установке и удалении» без иконки.

---

## 3. Недоделки и шероховатости (кратко)

| # | Место | Проблема |
|---|-------|----------|
| 1 | `GitHubUpdateService:499-523` | `CleanVersionTag` отрезает `-beta/-rc` → `1.4.0-beta` считается `1.4.0` и предлагается как стабильное. `ParseSemVer` на мусоре возвращает `0.0.0` → любое обновление «новее». Нужны `prerelease`-флаг из API и отказ от beta без opt-in |
| 2 | `CheckForUpdatesAsync:142` | `isManual` нигде не используется (только док). Нет rate-limit обработки 403 `X-RateLimit-Remaining` |
| 3 | `UpdateAvailableWindow:28-38` | Нет размера файла, хэша, прогресса-скорости до старта; чейнджлог — plain text (markdown не рендерится); fallback `"1.2.2"` захардкожен, уже протух |
| 4 | Версии | `MyAppVersion 1.3.1` (.iss) vs fallback `1.2.2` (`build-installer.ps1:16`) vs `new Version(1,2,4)` (`GitHubUpdateService:41`) — дрейф; единый источник — `Directory.Build.props` |
| 5 | `build-installer.ps1:80-88`, `sign-binaries.ps1:23-26,34-37` | Подпись **молча пропускается** при отсутствии `signtool`/сертификата, сборка считается успешной. Для релиза — `throw`, для dev — `-SkipSign` явно |
| 6 | `publish.ps1` | `Stop-Service/Stop-Process -SilentlyContinue`, `Unblock-File`, `Add-MpPreference` без проверки админа; скрипт меняет живую `Program Files` — разделить `publish-local` и `publish-artifacts` |
| 7 | `publish_github_release.py` + клиент | Сервер кладёт хэш и в `body`, и в `.sha256`-ассет, клиент читает только `body`. Добавить чтение ассета (см. п.1) |

---

## 4. Готовность к релизу

**Не готов без P0.** Матрица:

* **P0 (блокеры):** обязательный SHA-256 (fail-closed) + чтение `.sha256`-ассета; перепроверка хэша + Authenticode перед `Process.Start`; allowlist хоста загрузки; ожидание остановки службы в `.iss` (не `Sleep(500)`); чистка `Updates/` при деинсталляции; `throw` при отсутствии подписи в релизной сборке.
* **P1:** удаление `.download` при ошибке, атомарная замена файла, лимит размера, `AppMutex`, `Source: ..\SMon2\*`, разделение `publish.ps1` (без правки `Program Files` и без `Add-MpPreference` в релизном пайплайне), унификация канонического репозитория.
* **P2:** beta-канал, rate-limit, рендер markdown в `UpdateAvailableWindow`, единая версия из `Directory.Build.props`.

После P0 — можно в закрытую бету; после P0+P1 — в публичный релиз (с оговоркой про SmartScreen до получения OV/EV-сертификата).

