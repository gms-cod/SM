# План интеграции Gemini API (встроенный ключ)

## Цель
Добавить ИИ-оценку каждой записи автозапуска: описание, легальность, опасность (1-5), рекомендация. Без каких-либо действий со стороны пользователя — ключ API зашит в приложение.

---

## 1. Вариант с встроенным ключом

- Создаётся один бесплатный Gemini API-ключ в Google AI Studio
- Ключ хранится в коде (константа или ресурс)
- Все пользователи используют один и тот же ключ
- **Лимиты**: Gemini 2.5 Flash Free Tier — ~1500 запросов/день, токены бесплатно
- Нагрузка: один batch-запрос при старте (10-50 entry) = 1 запрос/запуск, даже при 10 запусках в день — 300/мес, запас 5x
- Если ключ утечёт — злоумышленник упрётся в тот же бесплатный лимит, финансовых потерь ноль

---

## 2. NuGet-пакет

```bash
dotnet add src\StartupMonitor.Shared package Google.GenAI
```

---

## 3. Новая модель в Shared — `AiRating`

`src\StartupMonitor.Shared\Models\AiRating.cs`:

```csharp
namespace StartupMonitor.Shared.Models;

public class AiRating
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SafeToDisable { get; set; }
    public string DisableImpact { get; set; } = string.Empty;
    public int Recommendation { get; set; }
    public string Category { get; set; } = "unknown";
    public List<string> Tags { get; set; } = [];
    public DateTime RatedAt { get; set; } = DateTime.UtcNow;
}
```

Контейнер — `src\StartupMonitor.Shared\Models\AiRatingsCache.cs`:

```csharp
namespace StartupMonitor.Shared.Models;

public class AiRatingsCache
{
    public int Version { get; set; } = 1;
    public List<AiRating> Entries { get; set; } = [];
}
```

---

## 4. Новый сервис — `AiRatingService`

`src\StartupMonitor.Service\Services\AiRatingService.cs`

### 4.1 Конструктор

```
- Принимает ILogger<AiRatingService>
- Создаёт Google.GenAI Client с apiKey = "встроенный_ключ"
- Загружает startup-ratings.json (если есть) в AiRatingsCache
```

### 4.2 Ключ

Хранить в `src\StartupMonitor.Service\Services\GeminiKey.cs`:

```csharp
namespace StartupMonitor.Service.Services;

internal static class GeminiKey
{
    internal const string ApiKey = "AIzaSy...";
}
```

.gitignore не трогает — ключ в репозитории. Если ключ когда-нибудь умрёт (Google отзовёт) — заменить и выпустить новый релиз.

### 4.3 Основной метод

```csharp
public async Task<AiRating?> GetRatingAsync(StartupEntry entry)
```

1. Ищет entry.Name (и entry.FilePath) в кеше
2. Если найдено — возвращает готовый рейтинг
3. Если нет — вызывает `RateBatchAsync` (собирает все entry без рейтинга и шлёт одним запросом)

### 4.4 Gemini Prompt (один запрос на batch)

```
Ты — эксперт по безопасности Windows. Оцени каждую запись автозапуска.
Формат ответа — строгий JSON-массив без пояснений:
[
  {
    "name": "...",
    "description": "что это, на русском, 1-2 предложения",
    "safe_to_disable": true/false,
    "disable_impact": "что будет если отключить",
    "recommendation": 1-5,
    "category": "system|driver|update|antivirus|cloud|browser|office|utility|adware|pua|game|vpn|unknown",
    "tags": ["tag1", "tag2"]
  }
]

Записи для оценки:
1. Realtek Audio Service (C:\Windows\System32\RtkAudUService64.exe)
2. ...
```

### 4.5 Сохранение

- Ответ парсится, каждый `AiRating` дописывается в кеш
- Кеш сохраняется в `startup-ratings.json` рядом с конфигом
- Начальный `startup-ratings.json` уже создан (41 запись) — при первом запуске с новыми entry дополняются только они

---

## 5. Где вызывать

**PipeServer.ProcessMessage**, case `GetAllEntries`:

```
1. Получить entries из JsonDataStore
2. Вызвать AiRatingService.GetRatingAsync() для каждой entry (кеш внутри)
3. Прикрепить AiRating к StartupEntry
4. Вернуть клиенту
```

В `StartupEntry` добавить:

```csharp
public AiRating? AiRating { get; set; }

// Не сериализовать в основной конфиг (кеш отдельно)
public bool ShouldSerializeAiRating() => false;
```

---

## 6. Регистрация в DI

`Program.cs` сервиса:

```csharp
services.AddSingleton<AiRatingService>();
```

---

## 7. Отображение в UI

### 7.1 Конвертер

`src\StartupMonitor.UI\Converters\RatingToColorConverter.cs`:

```csharp
public class RatingToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int rating)
            return rating switch
            {
                1 => new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)),  // красный
                2 => new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)),
                3 => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),  // оранжевый
                4 => new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)),  // зелёный
                5 => new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)),
                _ => new SolidColorBrush(Colors.Gray)
            };
        return new SolidColorBrush(Colors.Gray);
    }
}
```

### 7.2 XAML

В карточку entry добавить индикаторы:

```xml
<StackPanel Orientation="Horizontal" ToolTip="{Binding AiRating.Description}">
  <Rectangle Fill="{Binding AiRating.Recommendation, Converter={StaticResource RatingToColorConverter}}"
             Width="12" Height="12" RadiusX="6" RadiusY="6" Margin="0,0,4,0" />
  <TextBlock Text="{Binding AiRating.Recommendation, StringFormat='{}{0}/5'}"
             FontSize="11" Foreground="{Binding AiRating.Recommendation, Converter={StaticResource RatingToColorConverter}}" />
  <TextBlock Text="{Binding AiRating.Category}" FontSize="11" Margin="8,0,0,0" Opacity="0.6" />
</StackPanel>
```

### 7.3 Тултип

```xml
<TextBlock Text="?" FontSize="11" Cursor="Help">
  <TextBlock.ToolTip>
    <StackPanel>
      <TextBlock Text="{Binding AiRating.Description}" TextWrapping="Wrap" />
      <TextBlock Text="{Binding AiRating.DisableImpact}" TextWrapping="Wrap" />
    </StackPanel>
  </TextBlock.ToolTip>
</TextBlock>
```

---

## 8. Первый запуск

1. Сервис стартует, видит пустой `startup-ratings.json` (или отсутствующий)
2. UI шлёт `GetAllEntries`
3. PipeServer видит, что у всех entry нет рейтинга
4. `AiRatingService` шлёт batch-запрос в Gemini со всеми entry
5. Gemini возвращает JSON-массив
6. `AiRatingService` парсит, сохраняет в кеш и на диск
7. Ответ уходит в UI
8. Пользователь видит цветные индикаторы + описание в тултипе

---

## 9. Последующие запуски

1. Кеш уже есть на диске
2. Новые entry (которых нет в кеше) отправляются в Gemini
3. Старые не перезапрашиваются
4. Можно добавить кнопку "Обновить рейтинг" — очищает кеш и перезапрашивает всё

---

## 10. Обработка ошибок

- Нет интернета: `AiRatingService` возвращает `null`, UI показывает серый индикатор
- Gemini вернул кривой JSON: логируем, возвращаем `null`
- Лимит превышен (429): логируем, следующий batch — через час
- Все ошибки тихие — программа работает как раньше, но без рейтинга

---

## 11. Защита ключа от поверхностного извлечения

- Не хранить ключ строкой в открытом виде в коде GitHub
- Разбить на части и собирать через `string.Concat`
- Или хранить в embedded resource и читать при старте
- Или обфусцировать через `Convert.FromBase64String` + XOR

Пример:

```csharp
internal static class GeminiKey
{
    private static readonly byte[] _obfuscated = [0x12, 0x34, 0x56, ...];
    private static readonly byte[] _xor = [0xAB, 0xCD, 0xEF, ...];

    internal static string ApiKey
    {
        get
        {
            var result = new byte[_obfuscated.Length];
            for (int i = 0; i < _obfuscated.Length; i++)
                result[i] = (byte)(_obfuscated[i] ^ _xor[i]);
            return Encoding.UTF8.GetString(result);
        }
    }
}
```

Это не остановит целевого реверс-инженера, но защитит от авто-поиска ключей в репозиториях.

---

## 12. Порядок реализации

1. Установить `Google.GenAI` в Shared
2. Создать `AiRating`, `AiRatingsCache` модели
3. Создать `GeminiKey.cs` с обфусцированным ключом
4. Создать `AiRatingService` (загрузка кеша, вызов Gemini, сохранение)
5. Модифицировать `StartupEntry` — добавить `AiRating?` с `ShouldSerializeAiRating`
6. Подключить `AiRatingService` в DI сервиса
7. Модифицировать `PipeServer.ProcessMessage` (case `GetAllEntries`)
8. Создать `RatingToColorConverter`
9. Добавить отображение рейтинга в `MainWindow.xaml`
10. Написать тесты для `AiRatingService` (с mock HTTP)
