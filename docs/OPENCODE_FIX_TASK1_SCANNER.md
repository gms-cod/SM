# OpenCode Coder 1: SystemScanner CLSID Optimization & Fixes (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 98.6 сек
- Статус: Успешно

---

Чиню тормоз ScanAll — убираю полный перебор HKCR\CLSID.
Нашёл оба файла — сейчас внесу три оптимизации.
Все три правки внесены — проверяю сборку.
Все три изменения применены, сборка `StartupMonitor.Service` — 0 ошибок, 0 предупреждений.

Что сделано:

1. **`SystemScannerService.ScanClsidEntries`** — убран полный перебор всех ~7485 ключей `HKCR\CLSID` с `ResolveAndEnrichEntry` на каждый. Теперь:
   - собираются только активные GUID из 6 веток (`*\shellex\ContextMenuHandlers`, `Directory`, `Folder`, `Drive`, `AllFileSystemObjects`, `Directory\Background\shellex\ContextMenuHandlers`);
   - GUID берётся из значения по умолчанию обработчика (с извлечением `{...}` из строки) либо из имени подраздела, если оно само `{GUID}`;
   - `HKCR\CLSID\{GUID}` открывается только для этого множества, путь читается из `InprocServer32` (приоритет) или `LocalServer32`, запись добавляется с `Location = ContextMenuHandler`, `LocationDetail = GUID`.

2. **`SystemScannerService.ScanAll`** — запись `logs/scanner-dump.txt` теперь только при `logger.IsEnabled(LogLevel.Debug)` (покрывает Debug/Trace), разбивка по Location в лог осталась безусловной.

3. **`VerifyInScan`** — уточнение: метод фактически находится в `SystemScannerService.cs` (строка ~782), а не в `EntryOperations.cs`; `EntryOperations.VerifyEntry` делегирует ему. Оптимизация внесена в этот реальный метод:
   - при `scanResults.Count > 100` строятся `HashSet` по `(Location, LocationDetail)` и `(Location, Name, FilePath)` вместо линейного `Any(TryMatch)`;
   - наборы кэшируются по ссылке списка (с проверкой `Count`), поэтому цикл `CleanupStaleEntries` работает за O(N+M), а не O(N*M);
   - редкий tertiary-кейс (`Name` вида `subkey\value`) при промахе HashSet добивается точечным линейным `TryMatch`, семантика сопоставления сохранена.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/SystemScannerService.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs[90m [offset=1089][0m
[0m✱ [0mGrep "VerifyInScan"[90m in src · 2 matches[0m
[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/SystemScannerService.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
@@ -403,45 +403,132 @@
     }
 }
 
 
-/// <summary>Сканирует CLSID-записи (Shell Extensions / Context Menu).</summary>
+/// <summary>Сканирует ТОЛЬКО реально зарегистрированные в системе Shell Context Menu Handlers:
+/// собирает активные GUID из *\shellex\ContextMenuHandlers и родственных веток HKCR,
+/// затем читает путь к DLL/EXE только для этого множества из HKCR\CLSID\{GUID}\InprocServer32|LocalServer32.</summary>
 private static void ScanClsidEntries(List<StartupEntry> results)
 {
-    try
+    using var _prof = ExecutionProfiler.Measure("Service.ScanClsid");
+    // Ветки HKCR, где регистрируются активные обработчики контекстного меню
+    string[] handlerRoots =
+    [
+        @"*\shellex\ContextMenuHandlers",
+        @"Directory\shellex\ContextMenuHandlers",
+        @"Folder\shellex\ContextMenuHandlers",
+        @"Drive\shellex\ContextMenuHandlers",
+        @"AllFileSystemObjects\shellex\ContextMenuHandlers",
+        @"Directory\Background\shellex\ContextMenuHandlers",
+    ];
+
+    // GUID -> имя обработчика (первое встретившееся, для поля Name)
+    var activeHandlers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
+    foreach (var root in handlerRoots)
     {
-        using var clsidRoot = Registry.ClassesRoot.OpenSubKey("CLSID");
-        if (clsidRoot == null) return;
+        try
+        {
+            using var rootKey = Registry.ClassesRoot.OpenSubKey(root);
+            if (rootKey == null) continue;
 
-        foreach (var clsid in clsidRoot.GetSubKeyNames())
+            string[] handlerNames;
+            try { handlerNames = rootKey.GetSubKeyNames(); }
+            catch { continue; }
+
+            foreach (var handlerName in handlerNames)
+            {
+                try
+                {
+                    string? guid = null;
+                    var trimmedName = handlerName.Trim();
+                    // Само имя подраздела может быть GUID
+                    if (trimmedName.StartsWith('{') && trimmedName.EndsWith('}'))
+                    {
+                        guid = trimmedName;
+                    }
+                    else
+                    {
+                        using var handlerKey = rootKey.OpenSubKey(handlerName);
+                        var defVal = handlerKey?.GetValue(null)?.ToString()?.Trim();
+                        if (!string.IsNullOrEmpty(defVal))
+                        {
+                            // Значение по умолчанию — обычно "{GUID}", извлекаем GUID из строки
+                            var open = defVal.IndexOf('{');
+                            var close = open >= 0 ? defVal.IndexOf('}', open + 1) : -1;
+                            if (open >= 0 && close > open)
+                                guid = defVal.Substring(open, close - open + 1);
+                            else if (defVal.StartsWith('{'))
+                                guid = defVal;
+                        }
+                    }
+
+                    if (string.IsNullOrEmpty(guid)) continue;
+                    if (!activeHandlers.ContainsKey(guid))
+                        activeHandlers[guid] = handlerName;
+                }
+                catch
+                {
+                    // Пропускаем отдельный повреждённый обработчик
+                }
+            }
+        }
+        catch
         {
-            if (!clsid.StartsWith('{')) continue;
+            // Пропускаем недоступную ветку реестра
+        }
+    }
 
-            using var clsidKey = clsidRoot.OpenSubKey(clsid);
-            if (clsidKey == null) continue;
+    if (activeHandlers.Count == 0) return;
 
-            var name = clsidKey.GetValue(null)?.ToString() ?? clsid;
+    try
+    {
+        using var clsidRoot = Registry.ClassesRoot.OpenSubKey("CLSID");
+        if (clsidRoot == null) return;
 
-            foreach (var subKeyName in new[] { "InprocServer32", "LocalServer32" })
+        foreach (var (guid, handlerName) in activeHandlers)
+        {
+            try
             {
-                using var subKey = clsidKey.OpenSubKey(subKeyName);
-                if (subKey == null) continue;
+                using var clsidKey = clsidRoot.OpenSubKey(guid);
+                if (clsidKey == null) continue;
 
-                var dllPath = subKey.GetValue(null)?.ToString();
-                if (string.IsNullOrWhiteSpace(dllPath)) continue;
+                var displayName = clsidKey.GetValue(null)?.ToString();
+                if (string.IsNullOrWhiteSpace(displayName))
+                    displayName = handlerName;
 
-                var entry = new StartupEntry
+                foreach (var subKeyName in new[] { "InprocServer32", "LocalServer32" })
                 {
-                    Name = name,
-                    FilePath = dllPath,
-                    CommandLine = dllPath,
-                    Location = EntryLocation.ContextMenuHandler,
-                    LocationDetail = clsid,
-                };
-                entry.Id = WatcherHelper.ComputeStableId(entry);
-                EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
-                results.Add(entry);
+                    try
+                    {
+                        using var subKey = clsidKey.OpenSubKey(subKeyName);
+                        if (subKey == null) continue;
+
+                        var dllPath = subKey.GetValue(null)?.ToString();
+                        if (string.IsNullOrWhiteSpace(dllPath)) continue;
+
+                        var entry = new StartupEntry
+                        {
+                            Name = displayName,
+                            FilePath = dllPath,
+                            CommandLine = dllPath,
+                            Location = EntryLocation.ContextMenuHandler,
+                            LocationDetail = guid,
+                        };
+                        entry.Id = WatcherHelper.ComputeStableId(entry);
+                        EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
+                        results.Add(entry);
+                        break; // достаточно одного сервера (InprocServer32 приоритетнее)
+                    }
+                    catch
+                    {
+                        // Пропускаем недоступный подраздел сервера
+                    }
+                }
             }
+            catch
+            {
+                // Пропускаем отдельный повреждённый CLSID
+            }
         }
     }
     catch { }
 }

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/SystemScannerService.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
@@ -123,25 +123,29 @@
             .Select(g => $"{g.Key}={g.Count()}");
         logger.LogInformation("Scanner: разбивка по Location — {Breakdown}",
             string.Join(", ", breakdown));
 
-        // Дамп в отдельный файл для отладки пропаж
-        try
+        // Дамп в отдельный файл — только при уровне Debug/Trace,
+        // чтобы не дёргать диск на каждом фоновом скане каждые 5 минут
+        if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
         {
-            var logsDir = Path.Combine(
-                AppContext.BaseDirectory,
-                "logs");
-            Directory.CreateDirectory(logsDir);
-            var dumpPath = Path.Combine(logsDir, "scanner-dump.txt");
-            var lines = results.Select(e =>
-                $"{e.Location,-8} | {e.Name,-50} | {e.FilePath,-80} | {e.LocationDetail}");
-            File.WriteAllLines(dumpPath, lines);
-            logger.LogInformation("Scanner: дамп записан в {Path} ({Count} строк)", dumpPath, results.Count);
+            try
+            {
+                var logsDir = Path.Combine(
+                    AppContext.BaseDirectory,
+                    "logs");
+                Directory.CreateDirectory(logsDir);
+                var dumpPath = Path.Combine(logsDir, "scanner-dump.txt");
+                var lines = results.Select(e =>
+                    $"{e.Location,-8} | {e.Name,-50} | {e.FilePath,-80} | {e.LocationDetail}");
+                File.WriteAllLines(dumpPath, lines);
+                logger.LogInformation("Scanner: дамп записан в {Path} ({Count} строк)", dumpPath, results.Count);
+            }
+            catch (Exception ex)
+            {
+                logger.LogWarning(ex, "Scanner: не удалось записать дамп");
+            }
         }
-        catch (Exception ex)
-        {
-            logger.LogWarning(ex, "Scanner: не удалось записать дамп");
-        }
     }
 
     return results;
 }

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/SystemScannerService.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs
@@ -868,12 +868,68 @@
 
     return false;
 }
 
-/// <summary>Проверяет, находится ли запись в результатах скана.</summary>
+/// <summary>Кэш HashSet для быстрой проверки <see cref="VerifyInScan"/>:
+/// переиспользуется между вызовами по одной и той же ссылке списка скана,
+/// чтобы цикл CleanupStaleEntries работал за O(N+M), а не O(N*M).</summary>
+private static readonly object _verifyCacheLock = new();
+private static List<StartupEntry>? _verifyCacheSource;
+private static int _verifyCacheCount = -1;
+private static HashSet<string>? _verifyDetailSet;
+private static HashSet<string>? _verifyNamePathSet;
+
+/// <summary>Проверяет, находится ли запись в результатах скана.
+/// При scanResults &gt; 100 использует HashSet по (Location, LocationDetail) и
+/// (Location, Name, FilePath) вместо линейного Any(TryMatch).</summary>
 public static bool VerifyInScan(StartupEntry entry, List<StartupEntry> scanResults)
 {
-    return scanResults.Any(s => TryMatch(entry, s));
+    if (scanResults.Count <= 100)
+        return scanResults.Any(s => TryMatch(entry, s));
+
+    HashSet<string> detailSet;
+    HashSet<string> namePathSet;
+    lock (_verifyCacheLock)
+    {
+        if (!ReferenceEquals(_verifyCacheSource, scanResults)
+            || _verifyCacheCount != scanResults.Count
+            || _verifyDetailSet == null
+            || _verifyNamePathSet == null)
+        {
+            var details = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
+            var namePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
+            foreach (var s in scanResults)
+            {
+                if (!string.IsNullOrEmpty(s.LocationDetail))
+                    details.Add($"{(int)s.Location}\0{s.LocationDetail}");
+                if (!string.IsNullOrEmpty(s.Name) && !string.IsNullOrEmpty(s.FilePath))
+                    namePaths.Add($"{(int)s.Location}\0{s.Name}\0{s.FilePath}");
+            }
+            _verifyCacheSource = scanResults;
+            _verifyCacheCount = scanResults.Count;
+            _verifyDetailSet = details;
+            _verifyNamePathSet = namePaths;
+        }
+        detailSet = _verifyDetailSet;
+        namePathSet = _verifyNamePathSet;
+    }
+
+    // Primary: (Location, LocationDetail)
+    if (!string.IsNullOrEmpty(entry.LocationDetail) &&
+        detailSet.Contains($"{(int)entry.Location}\0{entry.LocationDetail}"))
+        return true;
+
+    // Secondary: (Location, Name, FilePath)
+    if (!string.IsNullOrEmpty(entry.Name) && !string.IsNullOrEmpty(entry.FilePath) &&
+        namePathSet.Contains($"{(int)entry.Location}\0{entry.Name}\0{entry.FilePath}"))
+        return true;
+
+    // Tertiary (редкий случай: Name вида "subkey\value"): только такие записи
+    // проверяем линейно через полный TryMatch
+    if (!string.IsNullOrEmpty(entry.Name) && entry.Name.Contains('\\'))
+        return scanResults.Any(s => TryMatch(entry, s));
+
+    return false;
 }
 
 /// <summary>Диагностика: логирует почему entry не найден в scanResults.</summary>
 public static void DebugMatch(StartupEntry dbEntry, List<StartupEntry> scanResults,

[0m
[0m$ [0mdotnet build src/StartupMonitor.Service/StartupMonitor.Service.csproj --nologo -v q 2>&1 | Select-Object -Last 20
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:11.51
[0m

```
