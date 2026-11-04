# Agent 1: Shared IPC Contracts & Delta Models (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 107.6 сек
- Статус: Успешно

---

Внедрено в `src/StartupMonitor.Shared/Models/PipeMessage.cs`, сборка `StartupMonitor.Shared` — 0 ошибок. Старые `GetAllEntries / GetAllEntriesResponse` не тронуты.

Готовый код:

```csharp
// В PipeMessageTypes, сразу после GetAllEntriesResponse:
/// <summary>Запрос дельты записей автозапуска по ревизии клиента.</summary>
public const string GetEntriesDelta = "GetEntriesDelta";
/// <summary>Ответ с дельтой записей автозапуска (или признаком отсутствия изменений).</summary>
public const string GetEntriesDeltaResponse = "GetEntriesDeltaResponse";
/// <summary>Признак отсутствия изменений (ревизия клиента актуальна).</summary>
public const string NotModified = "NotModified";
```

```csharp
/// <summary>Запрос дельты записей автозапуска. Клиент передаёт последнюю известную ревизию.</summary>
public sealed class EntriesRevisionRequest
{
    /// <summary>Последняя известная клиенту ревизия (0 = запросить полный снимок).</summary>
    [JsonPropertyName("clientRevision")]
    public long ClientRevision { get; set; }

    public EntriesRevisionRequest() { }
    public EntriesRevisionRequest(long clientRevision) { ClientRevision = clientRevision; }
}

/// <summary>Ответ с дельтой записей автозапуска. При IsModified=false список Entries пуст/отсутствует.</summary>
public sealed class EntriesDeltaResponse
{
    /// <summary>Актуальная ревизия на стороне службы.</summary>
    [JsonPropertyName("newRevision")]
    public long NewRevision { get; set; }

    /// <summary>True — данные изменились, Entries содержит актуальный снимок/дельту.</summary>
    [JsonPropertyName("isModified")]
    public bool IsModified { get; set; }

    /// <summary>Актуальный список записей (null/пусто при NotModified).</summary>
    [JsonPropertyName("entries")]
    public List<StartupEntry>? Entries { get; set; }

    public EntriesDeltaResponse() { }
    public EntriesDeltaResponse(long newRevision, bool isModified, List<StartupEntry>? entries = null)
    {
        NewRevision = newRevision;
        IsModified = isModified;
        Entries = entries;
    }
}
```

Заметки:
- `StartupEntry` в том же неймспейсе `StartupMonitor.Shared.Models`, дополнительный `using` не нужен.
- Параметрлесс-конструкторы оставлены для `System.Text.Json`.
- Обратная совместимость полная: только добавление, без переименований.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.Shared/Models/PipeMessage.cs
[0m
[0m← [0mEdit src/StartupMonitor.Shared/Models/PipeMessage.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs
@@ -24,8 +24,14 @@
 /// <summary>Запрос всех записей автозапуска.</summary>
 public const string GetAllEntries = "GetAllEntries";
 /// <summary>Ответ со списком всех записей.</summary>
 public const string GetAllEntriesResponse = "GetAllEntriesResponse";
+/// <summary>Запрос дельты записей автозапуска по ревизии клиента.</summary>
+public const string GetEntriesDelta = "GetEntriesDelta";
+/// <summary>Ответ с дельтой записей автозапуска (или признаком отсутствия изменений).</summary>
+public const string GetEntriesDeltaResponse = "GetEntriesDeltaResponse";
+/// <summary>Признак отсутствия изменений (ревизия клиента актуальна).</summary>
+public const string NotModified = "NotModified";
 /// <summary>Отключить запись (удалить из автозапуска, сохранив конфиг).</summary>
 public const string DisableEntry = "DisableEntry";
 /// <summary>Восстановить запись в автозапуске.</summary>
 public const string EnableEntry = "EnableEntry";

[0m
[0m← [0mEdit src/StartupMonitor.Shared/Models/PipeMessage.cs
Index: d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs
===================================================================
--- d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs
+++ d:\NasDisk\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs
@@ -289,4 +289,39 @@
     bool BlockDangerousCli,
     bool BlockNewServices
 );
 
+/// <summary>Запрос дельты записей автозапуска. Клиент передаёт последнюю известную ревизию.</summary>
+public sealed class EntriesRevisionRequest
+{
+    /// <summary>Последняя известная клиенту ревизия (0 = запросить полный снимок).</summary>
+    [JsonPropertyName("clientRevision")]
+    public long ClientRevision { get; set; }
+
+    public EntriesRevisionRequest() { }
+    public EntriesRevisionRequest(long clientRevision) { ClientRevision = clientRevision; }
+}
+
+/// <summary>Ответ с дельтой записей автозапуска. При IsModified=false список Entries пуст/отсутствует.</summary>
+public sealed class EntriesDeltaResponse
+{
+    /// <summary>Актуальная ревизия на стороне службы.</summary>
+    [JsonPropertyName("newRevision")]
+    public long NewRevision { get; set; }
+
+    /// <summary>True — данные изменились, Entries содержит актуальный снимок/дельту.</summary>
+    [JsonPropertyName("isModified")]
+    public bool IsModified { get; set; }
+
+    /// <summary>Актуальный список записей (null/пусто при NotModified).</summary>
+    [JsonPropertyName("entries")]
+    public List<StartupEntry>? Entries { get; set; }
+
+    public EntriesDeltaResponse() { }
+    public EntriesDeltaResponse(long newRevision, bool isModified, List<StartupEntry>? entries = null)
+    {
+        NewRevision = newRevision;
+        IsModified = isModified;
+        Entries = entries;
+    }
+}
+

[0m
[0m$ [0mdotnet build src/StartupMonitor.Shared/StartupMonitor.Shared.csproj -c Release --nologo -v q
Сборка успешно завершена.
    Предупреждений: 0
    Ошибок: 0

Прошло времени 00:00:20.28
[0m

```
