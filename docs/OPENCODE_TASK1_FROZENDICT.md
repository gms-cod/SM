# Agent 1: Lock-Free FrozenDictionary & ScanAll Optimization (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 289.6 сек
- Статус: Успешно

---

Готово. Сборка — 0 ошибок, тесты — 709/709 пройдено.

**1. `ProcessStartupLinker.cs` — FrozenDictionary (Lock-Free)**

```csharp
using System.Collections.Frozen;

// ...
// Lock-free хеш-индексы. Чтение полностью потокобезопасно без блокировок.
// Обновление — атомарной подменой ссылки в UpdateIndex().
private FrozenDictionary<string, StartupEntry> _pathToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;
private FrozenDictionary<string, StartupEntry> _serviceNameToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;
private FrozenDictionary<string, StartupEntry> _nameToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;

public void UpdateIndex(IEnumerable<StartupEntry> entries)
{
    var pathMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
    var serviceMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
    var nameMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);

    foreach (var entry in entries)
    {
        if (!string.IsNullOrWhiteSpace(entry.FilePath))
        {
            var normPath = NormalizePath(entry.FilePath);
            if (!string.IsNullOrEmpty(normPath))
                pathMap[normPath] = entry;
        }
        if (entry.Location == EntryLocation.Service && !string.IsNullOrWhiteSpace(entry.LocationDetail))
            serviceMap[entry.LocationDetail.Trim()] = entry;
        if (!string.IsNullOrWhiteSpace(entry.Name))
            nameMap[entry.Name.Trim()] = entry;
    }

    // Атомарная подмена: читатели видят либо старый, либо новый снимок целиком
    _pathToEntryMap = pathMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    _serviceNameToEntryMap = serviceMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    _nameToEntryMap = nameMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}

public void LinkProcess(ProcessInfo proc, IReadOnlyDictionary<int, string>? processNamesByPid = null)
{
    // Без lock — FrozenDictionary потокобезопасен для чтения
    if (!string.IsNullOrWhiteSpace(proc.FilePath))
    {
        var norm = NormalizePath(proc.FilePath);
        if (!string.IsNullOrEmpty(norm) && _pathToEntryMap.TryGetValue(norm, out var entry))
        {
            SetStartupInfo(proc, entry);
            return;
        }
    }

    if (!string.IsNullOrWhiteSpace(proc.ProcessName)
        && _nameToEntryMap.TryGetValue(proc.ProcessName, out var entryByName))
    {
        SetStartupInfo(proc, entryByName);
        return;
    }
    // ... дальше без изменений (parent PID, эвристика CLI)
}
```

**2. `PipeServer.cs` — интервал 5 → 15 минут**

```csharp
// 2. Очистка устаревших записей (полный скан) — раз в 15 минут
// (изменения отслеживаются вотчерами реального времени)
if (DateTime.UtcNow - lastFullScan >= TimeSpan.FromMinutes(15))
```

**Результат:**
- `LinkProcess()` вызывается на каждый снимок процессов (горячий путь): убран `lock` на чтение — нет contention между потоком метрик и потоком pipe (`UpdateIndex`), нет риска блокировки при частых `GetAllEntries` / `GetProcesses`.
- `FrozenDictionary` оптимизирован под чтение: хеш-таблица с идеальной раскладкой в памяти, `OrdinalIgnoreCase` сохраняется через `ToFrozenDictionary(comparer)`.
- Атомарная подмена ссылок гарантирует консистентность: читатель никогда не видит полуочищенный словарь (старая проблема `Clear()` + `lock` на общем объекте `_pathToEntryMap` при двух картах под одним замком).
- Полный скан `ScanAll()` (реестр + задачи + службы — самый дорогой) теперь 4 раза в час вместо 12 — меньше CPU/диска в простое, актуальность не страдает, т.к. удаление/создание отлавливают вотчеры реального времени, а проверка ЧС по-прежнему каждые 10 с.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessStartupLinker.cs
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs
[0m✱ [0mGrep "BackgroundCleanupLoop|FromMinutes|FromSeconds|TimeSpan"[90m in src/StartupMonitor.Service · 18 matches[0m
[0m→ [0mRead src/StartupMonitor.Service/Pipe/PipeServer.cs[90m [limit=110, offset=1250][0m
[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessStartupLinker.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
@@ -1,5 +1,6 @@
 using System;
+using System.Collections.Frozen;
 using System.Collections.Generic;
 using System.IO;
 using System.Runtime.InteropServices;
 using StartupMonitor.Shared.Models;

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessStartupLinker.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
@@ -9,42 +9,47 @@
 
 /// <summary>Сервис сопоставления запущенных процессов с базой автозапуска и определения их источников.</summary>
 public class ProcessStartupLinker
 {
-    // Хеш-индексы для сопоставления за O(1)
-    private readonly Dictionary<string, StartupEntry> _pathToEntryMap = new(StringComparer.OrdinalIgnoreCase);
-    private readonly Dictionary<string, StartupEntry> _serviceNameToEntryMap = new(StringComparer.OrdinalIgnoreCase);
-    private readonly Dictionary<string, StartupEntry> _nameToEntryMap = new(StringComparer.OrdinalIgnoreCase);
+    // Lock-free хеш-индексы для сопоставления за O(1).
+    // FrozenDictionary: чтение полностью потокобезопасно без блокировок.
+    // Обновление — атомарной подменой ссылки в UpdateIndex().
+    private FrozenDictionary<string, StartupEntry> _pathToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;
+    private FrozenDictionary<string, StartupEntry> _serviceNameToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;
+    private FrozenDictionary<string, StartupEntry> _nameToEntryMap = FrozenDictionary<string, StartupEntry>.Empty;
 
     /// <summary>Обновляет индекс записей автозапуска из текущего снимка базы.</summary>
     public void UpdateIndex(IEnumerable<StartupEntry> entries)
     {
-        lock (_pathToEntryMap)
+        var pathMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
+        var serviceMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
+        var nameMap = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
+
+        foreach (var entry in entries)
         {
-            _pathToEntryMap.Clear();
-            _serviceNameToEntryMap.Clear();
-            _nameToEntryMap.Clear();
+            if (!string.IsNullOrWhiteSpace(entry.FilePath))
+            {
+                var normPath = NormalizePath(entry.FilePath);
+                if (!string.IsNullOrEmpty(normPath))
+                    pathMap[normPath] = entry;
+            }
 
-            foreach (var entry in entries)
+            if (entry.Location == EntryLocation.Service && !string.IsNullOrWhiteSpace(entry.LocationDetail))
             {
-                if (!string.IsNullOrWhiteSpace(entry.FilePath))
-                {
-                    var normPath = NormalizePath(entry.FilePath);
-                    if (!string.IsNullOrEmpty(normPath))
-                        _pathToEntryMap[normPath] = entry;
-                }
+                serviceMap[entry.LocationDetail.Trim()] = entry;
+            }
 
-                if (entry.Location == EntryLocation.Service && !string.IsNullOrWhiteSpace(entry.LocationDetail))
-                {
-                    _serviceNameToEntryMap[entry.LocationDetail.Trim()] = entry;
-                }
-
-                if (!string.IsNullOrWhiteSpace(entry.Name))
-                {
-                    _nameToEntryMap[entry.Name.Trim()] = entry;
-                }
+            if (!string.IsNullOrWhiteSpace(entry.Name))
+            {
+                nameMap[entry.Name.Trim()] = entry;
             }
         }
+
+        // Атомарная подмена ссылок: читатели в LinkProcess() видят либо старый,
+        // либо новый снимок целиком, блокировки не требуются.
+        _pathToEntryMap = pathMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
+        _serviceNameToEntryMap = serviceMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
+        _nameToEntryMap = nameMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
     }
 
     /// <summary>Связывает процесс с записью автозапуска или определяет имя родительского процесса.</summary>
     public void LinkProcess(ProcessInfo proc, IReadOnlyDictionary<int, string>? processNamesByPid = null)

[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessStartupLinker.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
@@ -53,26 +53,22 @@
 
 /// <summary>Связывает процесс с записью автозапуска или определяет имя родительского процесса.</summary>
 public void LinkProcess(ProcessInfo proc, IReadOnlyDictionary<int, string>? processNamesByPid = null)
 {
-    // 1. Поиск по нормализованному пути к файлу
+    // 1. Поиск по нормализованному пути к файлу (Lock-Free, без lock)
     if (!string.IsNullOrWhiteSpace(proc.FilePath))
     {
         var norm = NormalizePath(proc.FilePath);
-        lock (_pathToEntryMap)
+        if (!string.IsNullOrEmpty(norm) && _pathToEntryMap.TryGetValue(norm, out var entry))
         {
-            if (_pathToEntryMap.TryGetValue(norm, out var entry))
-            {
-                SetStartupInfo(proc, entry);
-                return;
-            }
+            SetStartupInfo(proc, entry);
+            return;
         }
     }
 
-    // 2. Для служб (svchost, services) или по совпадению имени процесса
-    lock (_pathToEntryMap)
-    {
-        if (_nameToEntryMap.TryGetValue(proc.ProcessName, out var entryByName))
+    // 2. Для служб (svchost, services) или по совпадению имени процесса (Lock-Free, без lock)
+    if (!string.IsNullOrWhiteSpace(proc.ProcessName)
+        && _nameToEntryMap.TryGetValue(proc.ProcessName, out var entryByName))
         {
             SetStartupInfo(proc, entryByName);
             return;
         }

[0m
[0m→ [0mRead src/StartupMonitor.Service/Services/ProcessStartupLinker.cs[90m [limit=50, offset=48][0m
[0m
[0m← [0mEdit src/StartupMonitor.Service/Services/ProcessStartupLinker.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs
@@ -67,12 +67,11 @@
 
 // 2. Для служб (svchost, services) или по совпадению имени процесса (Lock-Free, без lock)
 if (!string.IsNullOrWhiteSpace(proc.ProcessName)
     && _nameToEntryMap.TryGetValue(proc.ProcessName, out var entryByName))
-    {
-        SetStartupInfo(proc, entryByName);
-        return;
-    }
+{
+    SetStartupInfo(proc, entryByName);
+    return;
 }
 
 // 3. Если совпадений с автозапуском нет — определяем родительский процесс
 if (proc.ParentProcessId > 0 && processNamesByPid != null)

[0m
[0m← [0mEdit src/StartupMonitor.Service/Pipe/PipeServer.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs
@@ -1279,10 +1279,11 @@
 {
     _logger.LogWarning(ex, "BackgroundCleanupLoop: ошибка при проверке процессов по ЧС");
 }
 
-// 2. Очистка устаревших записей (полный скан) — раз в 60 секунд
-if (DateTime.UtcNow - lastFullScan >= TimeSpan.FromMinutes(5))
+// 2. Очистка устаревших записей (полный скан) — раз в 15 минут
+// (изменения отслеживаются вотчерами реального времени)
+if (DateTime.UtcNow - lastFullScan >= TimeSpan.FromMinutes(15))
 {
     lastFullScan = DateTime.UtcNow;
     try
     {

[0m
[0m← [0mEdit src/StartupMonitor.Service/Pipe/PipeServer.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs
@@ -1247,11 +1247,11 @@
     catch (OperationCanceledException) { }
 }
 
 /// <summary>
-/// Фоновый цикл очистки устаревших записей. Запускается через 30с после старта,
-/// затем повторяется каждые 60с. Удаляет записи, чьи файлы/ключи реестра/службы
-/// больше не существуют в системе.
+/// Фоновый цикл очистки устаревших записей. Запускается через 5с после старта,
+/// проверка ЧС — каждые 10с, полный скан устаревших записей — раз в 15 минут
+/// (изменения отслеживаются вотчерами реального времени).
 /// </summary>
 /// <param name="ct">Токен отмены</param>
 private async Task BackgroundCleanupLoop(CancellationToken ct)
 {

[0m
[0m$ [0mdotnet build src\StartupMonitor.Service\StartupMonitor.Service.csproj -c Release --nologo -v q 2>&1 | Select-Object -Last 20
Сборка успешно завершена.
    Предупреждений: 0
    Ошибок: 0

Прошло времени 00:00:19.73
[0m
[0m$ [0mdotnet test --nologo -v q 2>&1 | Select-Object -Last 15
D:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj : warning NU1701: Package 'SharpVectors.Reloaded 1.1.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0-windows10.0.17763'. This package may not be fully compatible with your project. [D:\NasDisk\Coding\StartupMonitor\StartupMonitor.sln]
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj : warning NU1701: Package 'SharpVectors.Reloaded 1.1.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0-windows10.0.17763'. This package may not be fully compatible with your project. [D:\NasDisk\Coding\StartupMonitor\StartupMonitor.sln]
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj : warning NU1701: Package 'SharpVectors.Reloaded 1.1.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0-windows10.0.17763'. This package may not be fully compatible with your project.
D:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj : warning NU1701: Package 'SharpVectors.Reloaded 1.1.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0-windows10.0.17763'. This package may not be fully compatible with your project.
D:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI_hdmcj0mb_wpftmp.csproj : warning NU1701: Package 'SharpVectors.Reloaded 1.1.0' was restored using '.NETFramework,Version=v4.6.1, .NETFramework,Version=v4.6.2, .NETFramework,Version=v4.7, .NETFramework,Version=v4.7.1, .NETFramework,Version=v4.7.2, .NETFramework,Version=v4.8, .NETFramework,Version=v4.8.1' instead of the project target framework 'net8.0-windows10.0.17763'. This package may not be fully compatible with your project.
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\Services\PipeServerTests.cs(22,23): warning CS1998: В данном асинхронном методе отсутствуют операторы await, поэтому метод будет выполняться синхронно. Воспользуйтесь оператором await для ожидания неблокирующих вызовов API или оператором await Task.Run(...) для выполнения связанных с ЦП заданий в фоновом потоке. [D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj]
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\Services\PipeServerTests.cs(43,23): warning CS1998: В данном асинхронном методе отсутствуют операторы await, поэтому метод будет выполняться синхронно. Воспользуйтесь оператором await для ожидания неблокирующих вызовов API или оператором await Task.Run(...) для выполнения связанных с ЦП заданий в фоновом потоке. [D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj]
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\Services\PipeServerTests.cs(57,23): warning CS1998: В данном асинхронном методе отсутствуют операторы await, поэтому метод будет выполняться синхронно. Воспользуйтесь оператором await для ожидания неблокирующих вызовов API или оператором await Task.Run(...) для выполнения связанных с ЦП заданий в фоновом потоке. [D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj]
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\Services\PipeServerTests.cs(77,23): warning CS1998: В данном асинхронном методе отсутствуют операторы await, поэтому метод будет выполняться синхронно. Воспользуйтесь оператором await для ожидания неблокирующих вызовов API или оператором await Task.Run(...) для выполнения связанных с ЦП заданий в фоновом потоке. [D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj]
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\Services\PipeServerTests.cs(100,23): warning CS1998: В данном асинхронном методе отсутствуют операторы await, поэтому метод будет выполняться синхронно. Воспользуйтесь оператором await для ожидания неблокирующих вызовов API или оператором await Task.Run(...) для выполнения связанных с ЦП заданий в фоновом потоке. [D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj]
D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\Services\WatcherHelperTests.cs(88,17): warning xUnit1012: Null should not be used for type parameter 'input' of type 'string'. Use a non-null value, or convert the parameter to a nullable type. (https://xunit.net/xunit.analyzers/rules/xUnit1012) [D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\StartupMonitor.Tests.csproj]
Тестовый запуск для D:\NasDisk\Coding\StartupMonitor\tests\StartupMonitor.Tests\bin\Debug\net8.0-windows10.0.17763\StartupMonitor.Tests.dll (.NETCoreApp,Version=v8.0)
Общее количество тестовых файлов (1), соответствующих указанному шаблону.

Пройден!   : не пройдено     0, пройдено   709, пропущено     0, всего   709, длительность 1 m 3 s. - StartupMonitor.Tests.dll (net8.0)
[0m

```
