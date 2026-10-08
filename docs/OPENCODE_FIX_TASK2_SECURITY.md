# OpenCode Coder 2: BlacklistManager & AutoGuardEngine Bugfixes (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 94.0 сек
- Статус: Успешно

---

Исправляю уязвимости и утечки — читаю целевые файлы.
Файлы прочитаны, уточняю детали API перед правками.
Уточнил API — применяю четыре исправления.
Все правки применены — проверяю сборку сервиса.
Все 4 исправления применены, сборка сервиса успешна (0 ошибок).

Что сделано:
- `BlacklistManager.cs:IsMatchStatic` — добавлен guard на пустое `rule.Value`; ветки `Publisher`/`SHA256` теперь требуют непустых `entry.Publisher`/`entry.SHA256` перед сравнением.
- `EntryOperations.cs:KillProcessesByRules` — при `procPath == null` сопоставление с `testPath` только для правил без `/`, `\`, `:` (правила уровня имени файла); path-правила против голого имени процесса больше не матчат.
- `EntryOperations.cs:KillProcessByImage` — `proc.MainModule?.FileName` заменён на безопасный `ProcessMetadataEnricher.TryGetProcessPath(proc.Id)` (без исключений, без блокировок на защищённых процессах).
- `AutoGuardEngine.cs:IsUnsignedOrUntrusted` — добавлен ранний `return false` при `SignatureStatus == Unknown`, чтобы не блокировать файл до завершения WinVerifyTrust.
- `ProcessNetworkTracker.cs:GetProcName` — добавлен `using var p = Process.GetProcessById(pid)` для закрытия хэндла.

Отклонение: в `DigitalSignatureStatus.cs` нет значения `NotChecked` (только `Unknown`..`ExplicitDistrust`), поэтому проверка применена только для `Unknown` — иначе код не скомпилируется.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/EntryOperations.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/BlacklistManager.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
[0m→ [0mRead src/StartupMonitor.Service/Services/Guards/AutoGuardEngine.cs
[0m✱ [0mGrep "enum DigitalSignatureStatus|TryGetProcessPath"[90m in src · 7 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessMetadataEnricher.cs[90m [limit=30, offset=240][0m
[0m→ [0mRead src/StartupMonitor.Shared/Models/DigitalSignatureStatus.cs
[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/BlacklistManager.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\BlacklistManager.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\BlacklistManager.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\BlacklistManager.cs
@@ -87,8 +87,9 @@
 /// <summary>Проверяет, подходит ли запись под правило ЧС по типу совпадения (путь/маска/издатель/SHA256).</summary>
 public static bool IsMatchStatic(BlacklistRule rule, StartupEntry entry)
 {
     if (rule == null || entry == null) return false;
+    if (string.IsNullOrWhiteSpace(rule.Value)) return false;
 
     // Защита системных компонентов ОС от ложного сопоставления с правилами ЧС
     if (SystemProtectionHelper.IsProtectedSystemEntry(entry)) return false;
 

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/BlacklistManager.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\BlacklistManager.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\BlacklistManager.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\BlacklistManager.cs
@@ -121,12 +121,12 @@
     {
         BlacklistMatchType.Path => MatchesFilePath(rule, entry.FilePath),
         BlacklistMatchType.PathWildcard => MatchesFilePath(rule, entry.FilePath),
         BlacklistMatchType.Publisher =>
-            string.Equals(entry.Publisher, rule.Value, StringComparison.OrdinalIgnoreCase),
+            !string.IsNullOrWhiteSpace(entry.Publisher) && string.Equals(entry.Publisher, rule.Value, StringComparison.OrdinalIgnoreCase),
 
         BlacklistMatchType.SHA256 =>
-            string.Equals(entry.SHA256, rule.Value, StringComparison.OrdinalIgnoreCase),
+            !string.IsNullOrWhiteSpace(entry.SHA256) && string.Equals(entry.SHA256, rule.Value, StringComparison.OrdinalIgnoreCase),
 
         _ => false
     };
 }

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/EntryOperations.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs
@@ -631,12 +631,12 @@
 try
 {
     if (fullPath != null)
     {
-        string? procPath = null;
-        try { procPath = proc.MainModule?.FileName; }
-        catch { continue; } // нет доступа к модулю — не трогаем процесс
-        if (!string.Equals(procPath, fullPath, StringComparison.OrdinalIgnoreCase))
+        var procPathSafe = ProcessMetadataEnricher.TryGetProcessPath(proc.Id);
+        if (string.IsNullOrEmpty(procPathSafe))
+            continue; // нет доступа к пути образа — не трогаем процесс
+        if (!string.Equals(procPathSafe, fullPath, StringComparison.OrdinalIgnoreCase))
             continue;
     }
     proc.Kill(entireProcessTree: true);
 }

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/EntryOperations.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs
@@ -731,10 +731,24 @@
 if (IsProtectedProcessName(testPath)) continue;
 
 foreach (var rule in activeRules)
 {
-    var matched = (procPath != null && BlacklistManager.MatchesFilePath(rule, procPath))
+    // Если путь образа неизвестен (procPath == null), нельзя сопоставлять
+    // path-правила против testPath ("имя.exe"): иначе маска вроде *chrome.exe
+    // убьёт легитимный Chrome в Program Files. Без пути сопоставляем только
+    // правила уровня имени файла (без слэшей и двоеточий).
+    bool matched;
+    if (procPath != null)
+    {
+        matched = BlacklistManager.MatchesFilePath(rule, procPath)
                   || BlacklistManager.MatchesFilePath(rule, testPath);
+    }
+    else
+    {
+        matched = rule.Value != null
+                  && !rule.Value.Contains('/') && !rule.Value.Contains('\\') && !rule.Value.Contains(':')
+                  && BlacklistManager.MatchesFilePath(rule, testPath);
+    }
 
     if (matched)
     {
         try

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/Guards/AutoGuardEngine.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Guards\AutoGuardEngine.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Guards\AutoGuardEngine.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Guards\AutoGuardEngine.cs
@@ -257,8 +257,14 @@
         {
             return false;
         }
 
+        // Запись ещё не прошла верификацию WinVerifyTrust (Unknown) — не блокируем легитимный файл до окончания проверки
+        if (entry.SignatureStatus == DigitalSignatureStatus.Unknown)
+        {
+            return false;
+        }
+
         // Если файл существует и имеет статус отличный от Valid (NotSigned, InvalidSignature, UntrustedRoot, Expired, Revoked)
         return true;
     }
 }

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
Index: d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs
===================================================================
--- d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs
+++ d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs
@@ -289,9 +289,9 @@
 if (pid <= 0) return string.Empty;
 if (procNames.TryGetValue(pid, out var cached)) return cached;
 try
 {
-    var p = Process.GetProcessById(pid);
+    using var p = Process.GetProcessById(pid);
     var name = p.ProcessName + ".exe";
     procNames[pid] = name;
     return name;
 }

[0m
[0m$ [0mdotnet build src/StartupMonitor.Service/StartupMonitor.Service.csproj -c Release --nologo -v q 2>&1 | Select-Object -Last 30
C:\Program Files\dotnet\sdk\8.0.423\Microsoft.Common.CurrentVersion.targets(4857,5): warning MSB3026: Could not copy "obj\Release\net8.0\StartupMonitor.Shared.dll" to "bin\Release\net8.0\StartupMonitor.Shared.dll". Beginning retry 1 in 1000ms. The process cannot access the file 'D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Release\net8.0\StartupMonitor.Shared.dll' because it is being used by another process.  [D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.csproj]

Build succeeded.

C:\Program Files\dotnet\sdk\8.0.423\Microsoft.Common.CurrentVersion.targets(4857,5): warning MSB3026: Could not copy "obj\Release\net8.0\StartupMonitor.Shared.dll" to "bin\Release\net8.0\StartupMonitor.Shared.dll". Beginning retry 1 in 1000ms. The process cannot access the file 'D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Release\net8.0\StartupMonitor.Shared.dll' because it is being used by another process.  [D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.csproj]
    1 Warning(s)
    0 Error(s)

Time Elapsed 00:00:10.18
[0m

```
