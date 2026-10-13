# План интеграции Kaspersky OpenTIP

**Токен:** `Zem+jUJbQEiqHPpwsfjTyQ==`
**Кому:** @antivirus_plan

## Шаг 1. Shared — новая модель и поля

**1.1** Создать `src\StartupMonitor.Shared\Models\KasperskyResult.cs`:
```csharp
public class KasperskyResult
{
    public string Zone { get; set; } = "";       // Red/Yellow/Green/Grey
    public string DetectionName { get; set; } = "";
    public string FileStatus { get; set; } = "";
    public bool IsFound { get; set; }
}
```

**1.2** Дополнить `StartupEntry.cs` — 3 поля:
- `KasperskyZone` (string) — Red/Yellow/Green/Grey
- `KasperskyDetection` (string) — первое имя детекта
- `KasperskyFileStatus` (string) — Malware/Clean/Not categorized

---

## Шаг 2. Service — сервис сканирования

**2.1** Создать папку `src\StartupMonitor.Service\Services\VirusScan\`

**2.2** Создать `KasperskyScanService.cs`:
- `HttpClient` → `GET https://opentip.kaspersky.com/api/v1/search/hash?request={sha256}`
- Header: `x-api-key: Zem+jUJbQEiqHPpwsfjTyQ==`
- Метод `ScanEntryAsync(StartupEntry entry)` — один хэш
- Метод `ScanSuspiciousAsync(List<StartupEntry> entries)` — проход по `AiRating >= 7`
- Парсит JSON-ответ, пишет поля в `entry`
- Сохраняет через `_store.UpdateEntry(entry)`
- 404 → `IsFound = false`, запись не обновляем

---

## Шаг 3. DI — регистрация в Program.cs

Добавить в `ConfigureServices`:
```csharp
services.AddSingleton<KasperskyScanService>();
```

---

## Шаг 4. PipeServer — фоновый запуск

В `GetRatings`, после `PrefetchAndLogAsync`, добавить:
```csharp
_ = ScanSuspiciousAsync(ratingsCfg.Entries);
```

Метод `ScanSuspiciousAsync`:
- фильтр `entry.AiRating >= 7`
- последовательный проход
- запись результатов через `_store.UpdateEntry`

---

## Шаг 5. UI — конвертер для отображения

**5.1** Создать `KasperskyIndicatorConverter.cs`:
- `Zone == "Red"` → `☠` красный
- `Zone == "Yellow"` → `⚠` жёлтый
- `Zone == "Green"` → `✓` зелёный
- пусто → `?` серый

**5.2** Зарегистрировать в `App.xaml`.

---

## Шаг 6. UI — MainWindow.xaml

Добавить новую колонку в DataTemplate:
```xml
<ColumnDefinition Width="25" />
...
<TextBlock Text="{Binding KasperskyZone, Converter={StaticResource KasperskyIndicatorConverter}}"
           ToolTip="{Binding KasperskyDetection, StringFormat='Kaspersky: {0}'}" />
```

В тултип добавить:
```xml
<TextBlock Text="{Binding KasperskyDetection, StringFormat='  Kaspersky: {0}'}" />
```

---

## Шаг 7. UI — MainViewModel.cs

В `LoadRatingsAsync` — при обновлении `ratings` подхватывать `KasperskyZone`, `KasperskyDetection`, `KasperskyFileStatus` из entry (они уже в `StartupEntry`, приходят из `GetAllEntriesAsync`).

---

## Шаг 8. Сборка и тест

- `dotnet build` — 0 ошибок
- Публикация в `SMon2`
- Запуск UI
- Проверка: зелёные записи (AiRating 1-2★) → не сканируются
- Проверка: красные записи (AiRating 9-10, 1★) → сканируются, появляется `☠` / `✓`
