# Agent 1: SystemScanner, CLSID & Registry Discovery Audit

- Время анализа: 151.1 сек
- Статус: Успех

---

Провожу предрелизный аудит сканера — проверяю устойчивость, очистку и производительность.
Три файла прочитал — теперь копаю вглубь: права доступа, очистка и CLSID.
Основные риски наметил — запускаю углубленную проверку каждого направления.
## Аудит сканера автозапуска: вердикт — **к публичному релизу не готов**

Физического сноса Windows нет (`CleanupStaleEntries:1188-1195` удаляет только из БД, `RemoveFromSystem:1277-1284` прикрыт `IsSelfEntry + IsProtectedSystemEntry`). Но есть гарантированная потеря мониторинга и ложные зачистки БД при частичном скане. На миллионах машин это выльется в churn, ложные «новые угрозы» и слепоту.

### 1. AccessDenied — упадет

Критично: `SystemScannerService.cs:160-198` — единственный незащищенный участок. `ScanOtherLocations:232-261,268-281`, `ScanSubKeyValues:331-365`, `ScanClsidEntries:432-537` все в `try/catch`, а здесь нет:

* `167`: `Registry.Users.OpenSubKey(sid)` вне `try` — чужой SID с приватным ACL, выгрузка куста при logoff между `GetUserProfiles()` и открытием (TOCTOU) роняет весь `ScanAll` (`ScanAll:89,93` тоже без `try`).
* `182`: `baseKey.OpenSubKey(keyPath)` — один запрещенный `Policies\Explorer\Run` убивает все 8 `RegistryLocations`, уже собранный `HKLM\Run` теряется.
* `187,189`: `GetValueNames()/GetValue()` — отзыв ACL / выгрузка hive между открытием и чтением.
* `ScanFolder:592` `Directory.GetFiles(folder)` без `try` — `AccessDenied` на чужом `Startup` роняет `ScanStartupFolders:553-587`.

Каскад: `ScanRegistry` ничего не пишет в `_failedLocations` (пишет только `skipRegistry:69-84`), `CleanupStaleEntries:1160` не знает о падении и сносит все `RegistryRun` как stale.

Безопасны, не трогать: `WatcherHelper.cs:17-49` (два `try`), `EntryOperations.cs:85-119 OpenRegistry`, `1102-1119 VerifyEntry` (fail-open `return true`).

**Патч P0:**
```csharp
// SystemScannerService.cs:174-198 — обернуть per-key как в ScanOtherLocations
foreach (var (keyPath, location) in RegistryLocations) {
  if (!allowWow64 && keyPath.Contains("Wow6432Node", StringComparison.OrdinalIgnoreCase)) continue;
  try {
    using var key = baseKey.OpenSubKey(keyPath);
    if (key == null) continue;
    string[] valueNames; try { valueNames = key.GetValueNames(); } catch { continue; }
    foreach (var vn in valueNames) {
      string? v; try { v = key.GetValue(vn) as string; } catch { continue; }
      // ... LooksLikeCommand + MakeRegistryEntry
    }
  } catch { continue; }
}
// SystemScannerService.cs:165-170
foreach (var (sid,name) in WatcherHelper.GetUserProfiles()) {
  try { using var uk = Registry.Users.OpenSubKey(sid); if (uk==null) continue;
    ScanHiveRegistry(uk, $"HKCU\\{name}", false, results); } catch { continue; }
}
// SystemScannerService.cs:63-95 + 590-592
try { ScanRegistry(results); } catch { _failedLocations.UnionWith([RegistryRun,RegistryRunOnce,RegistryRunServices,RegistryPolicies]); }
try { ScanFolder... } catch { _failedLocations.Add(StartupFolder); return; }
```

### 2. `CleanupStaleEntries` (`EntryOperations.cs:1124-1200`) — корректна лишь частично

Дыры `FailedLocations`:
* `ScanRegistry`, `ScanOtherLocations:258,278`, `ScanClsidEntries:477,525,531,537` глотают `catch{}`, в `_failedLocations` не пишут. `if(activeHandlers.Count==0) return:484` — пустой результат неотличим от «все удалено».
* `skipRegistry:69-84` добавляет 13 локаций, но нет `StartupFolder/ShellExtension/ComObject/Drivers32` и др. никогда-не-сканируемых локаций (`ShellExtension, ComObject, Drivers32` сканер не производит вообще, но `RemoveFromSystem:1286-1293` их удалять умеет — каждый цикл 5 мин их вытирает из БД).
* `ScanAll:89,98,102,106,110,114` без `Guard`.

`TryMatch:841-870` / `VerifyInScan:884-932`: сравнение сырых строк. `LocationDetail` то `HKCU\...`, то `HKCU\Имя\...` (`163-169` vs `185,318,355`), `JsonDataStore.NormalizeLocationDetail` не используется. AppX `LocationDetail="taskId (pkg_версия)":784` — после обновления Store ни Primary, ни Secondary не матчится. `FilePath` без нормализации (`%env%`, `/` vs `\`, кавычки). Tertiary только в одну сторону (`db.Name` с `\`).

Гонки: `entries.RemoveAll:1193` без лока на живом `_cache` (`JsonDataStore` возвращает shared `List`), конкурентный `Add` вотчера → `ArgumentException`/lost update при `_store.Save` (`PipeServer.cs:647,1664`). Статический `_verifyCacheSource:875-879` — утечка ссылки + ping-pong перестроений при двух конкурентных cleanup; `FailedLocations:19` отдает живой `HashSet`.

**Патч P0 (закрывает все одним махом):**
```csharp
// EntryOperations.cs:1131 — в начало CleanupStaleEntries
if (scanResults.Count == 0) return; // fail-closed
var scannedLocations = new HashSet<EntryLocation>(scanResults.Select(s => s.Location));
// в цикле перед VerifyEntry:
if (!scannedLocations.Contains(entry.Location)) { skipped++; results.Add((entry,true)); continue; }
// + батч-удаление под локом, убрать RemoveEntry внутри цикла;
// FailedLocations => lock(... ) return _failedLocations.ToArray();
// кэш Verify сделать instance, а не static
```

### 3. CLSID — хорошо, AppX — плохо

CLSID `410-538`: сканируются **только активные** — 6 `handlerRoots:418-426` → `activeHandlers` (обычно 20-60 GUID) → 1x `OpenSubKey(CLSID\{GUID}):495` + `InprocServer32|LocalServer32:502` с `break:523`. Тысяч COM-классов нет. Цена первого скана: ~50 × `WinVerifyTrust (5-30мс) + FileVersionInfo:995 + LangDetector:1004` = 0.25-1.5с. Кэш-хит `866-922`: `File.Exists+GetLastWriteTime:881-882`. Дефект: `FilePath=dllPath:515-516` сырое (`%SystemRoot%`, `"path",-123`), чинится только в `ResolveExecutablePath`.

AppX `741-802`: перф-дыра — двойное `GetDirectories(WindowsApps):750+752`, `XDocument.Load` **всех сотен** манифестов `806-813` (StartupTask есть у ~5), `LogInformation` на каждый таск `775` каждые 5 мин. Баг `768`: `GetFiles` возвращает полные пути, `Path.Combine(pkgDir, fullPath)` — случайное везение. Нет `ResolveAndEnrichEntry` (в отличие от всех секций) — нет `Authenticode/Publisher/ThreatInfo`.

### 4. Недоделки и мертвый код

`TODO/FIXME` — 0. Пустые `catch{}` — осознанные, но без лога: `SystemScannerService.cs:258,278,332,362,404,472,478,525,531,537,651,675,712,732,833`; `EntryOperations.cs:661,665,677,786,922,999,1027,1079,1095`.

Двойной парсинг: `MakeRegistryEntry:204` + `ResolveExecutablePath:1049` (+`1039` для Shell) — 2-3× `ParsePath` на значение; `File.Exists` 3-5× на файл (`1058,936,979,1088,1010`). `ParseServicePath:456-467` ломается на `C:\Program Files\...\svc.exe -k` (нужен `ParsePath`), `FormatTaskAction:727` `Trim('"')`, `ScanFolder:600-601` лишний `FileInfo.Exists`, `.lnk` не резолвится.

Мертвый: `Drivers32` в `GetRegistryKeyPath:48` + switch `1293,1346,1384,1424` — не сканируется никогда; `ShellExtension` создается нигде (сканер дает только `ContextMenuHandler:517`); `GetLocationFromKeyPath:470-479` знает только Run-семейство, для `OtherLocationsWatcher` даст неверный Location.

Фантомные XML-doc внутри вызовов (артефакт генератора, компилируется но мусор): `EntryOperations.cs:193-199` (`/// <summary>RegistryPayload...` + 3× `/// <param` внутри `Serialize`), `WatcherHelper.cs:300-303` (`ActionNoticePayload`). Удалить руками, затем чинить кодоген (по репо ~100 хитов `/// <param name="`).

### Что чинить перед релизом

* **P0-блокер:** патч `ScanHiveRegistry` + per-SID `try` + `Guard` в `ScanAll` + `scannedLocations`/abort-на-пустом-скане в `Cleanup`.
* **P1:** нормализация `Detail/Path` в `TryMatch` + особый матч AppX по `taskId`, `instance`-кэш + копия `FailedLocations` + лок на `entries`.
* **P2:** AppX префильтр (`Contains("startupTask")` до `XDocument.Load`), убрать двойное перечисление `750`, добавить `ResolveAndEnrichEntry` + `File.Exists` на `768`, `ParseServicePath→ParsePath`, удалить фантомные доки, понизить AppX-лог до Debug.

