Пользователь просит написать комплексные юнит-тесты на C# для .NET 9 и xUnit. Нужно покрыть два сервиса:

1. `NetworkConnectionAlertService`:
   - Приоритет Blacklist над Whitelist (если в обоих -> заблокировано).
   - Потокобезопасность при чтении/записи в списки (CollectionModified exception).

2. `ProcessMetricsTracker`:
   - Структура метрик (ProcessId, CPU, IO, Memory).
   - Корректность расчета дельты процессора.

Требования:
- Комментарии на русском.
- Готовый компилируемый код.
- Проект: `tests/StartupMonitor.Tests`.

Поскольку я не вижу исходный код сервисов, мне нужно предположить их API и написать тесты соответствующим образом, либо написать моки/заглушки внутри тестового проекта, чтобы код был компилируемым. Лучший подход — определить интерфейсы/классы в тестовом проекте (или предположить их存在ание в основном проекте), но так как просят "готовый компилируемый C# код", я должен либо включить минимальные реализации сервисов для компиляции, либо написать тесты с предположениями о API и добавить注释, что нужно подстроить под реальный API.

Однако, обычно в таких задачах ожидается, что я напишу тесты, предполагая определенную структуру классов, и возможно включу fake-реализации или assuming classes. Чтобы код был компилируемым, я могу создать тестовый класс с вложенными тестовымиdouble или просто описать ожидаемый API. Но лучше всего: написать тесты, используя предполагаемые интерфейсы/классы, и добавить в начале файла минимальные stub-классы, если они не существуют. Но пользователь просит "юнит-тесты для проекта tests/StartupMonitor.Tests", значит основной проект уже есть.

Давайте предположим следующее API на основе имен:

**NetworkConnectionAlertService**
- Метод `bool IsBlocked(string processName)` или `IsBlocked(int processId)`? Название NetworkConnectionAlertService suggère network connections. Возможно, метод `CheckConnection(string appName)` или `IsAlertRequired(string processName)`. Списки Blacklist/Whitelist, вероятно, содержат имена процессов или PID.
- Методы для управления списками: `AddToBlacklist`, `AddToWhitelist`, `RemoveFromBlacklist`, `RemoveFromWhitelist`, `ClearBlacklist`, `ClearWhitelist`.
- Свойства: `IReadOnlyCollection<string> Blacklist`, `Whitelist`.

Поскольку я не знаю точного API, я должен написать тесты гибко, но для компиляции нужен конкретный API. Я могу создать внутри тестового проекта stub-классы, имитирующие сервисы, но тогда тесты будут тестировать мои заглушки, а не реальный код. Это не то.

Лучший вариант: написать тесты, предполагая реальный API, и добавить комментарии "// Предполагаемый API сервиса — при необходимости подгоните под реальную сигнатуру". Но пользователь просит "готовый компилируемый код". Чтобы он был компилируемым без основного проекта, я могу включить в файл тестов минимальные реализации сервисов в región "Тестовые двойники / заглушки" или просто в отдельных файлах. Но обычно в ответе дают один файл с тестами.

Давайте я создам один файл `NetworkConnectionAlertServiceTests.cs` и один файл `ProcessMetricsTrackerTests.cs`, в которых предположу API, но также включу `#if false` блоки с заглушками? Нет, это усложнит.

Альтернатива: я напишу тесты, используя интерфейсы, которые я определю в самом файле тестов (или в отдельных файлах), и реализую их просто для демонстрации. Но тогда это будут тесты моих интерфейсов.

Подумаем: пользователь, скорее всего, хочет увидеть *подход* к тестированию и паттерны (xUnit, .NET 9, потокобезопасность, Assert). Поэтому я могу:

1. Определить предполагаемые классы/интерфейсы в комментариях.
2. Написать тесты, которые компилируются, если в проекте есть эти классы.
3. Добавить прим.: "Замените пространства имён и сигнатуры на реальные из вашего решения".

Но чтобы код был *компилируемым* изолированно, я могу включить в конец файла или в отдельные файлы минимальные реализации этих сервисов (заглушки), помеченные как `internal` или `public`, в тестовом проекте. Но тогда это не тесты реального сервиса.

Лучше: я напишу тесты, предполагая, что сервисы находятся в `StartupMonitor.Core` или `StartupMonitor.Services`, и дам код, который компилируется при наличии этих сервисов с ожидаемым API. Также я предоставлю "참考ную реализацию" сервисов в комментариях или как отдельные файлы, чтобы пользователь мог сравнить API.

Давайте остановимся на таком API:

**NetworkConnectionAlertService** (пространство имён `StartupMonitor.Services`):
```csharp
public class NetworkConnectionAlertService
{
    public void AddToBlacklist(string processName);
    public void AddToWhitelist(string processName);
    public void RemoveFromBlacklist(string processName);
    public void RemoveFromWhitelist(string processName);
    public bool IsBlocked(string processName); // возвращает true, если в черном списке (приоритет черного)
    public IReadOnlyCollection<string> Blacklist { get; }
    public IReadOnlyCollection<string> Whitelist { get; }
}
```

**ProcessMetricsTracker** (пространство имён `StartupMonitor.Diagnostics`):
```csharp
public class ProcessMetrics
{
    public int ProcessId { get; init; }
    public double CpuPercent { get; init; }
    public long ReadBytes { get; init; }
    public long WriteBytes { get; init; }
    public long WorkingSetBytes { get; init; } // Memory
    public DateTimeOffset Timestamp { get; init; }
}

public class ProcessMetricsTracker
{
    public void RecordSnapshot(int processId, double cpu, long readBytes, long writeBytes, long memoryBytes);
    public ProcessMetrics? GetLastSnapshot(int processId);
    public double GetCpuDelta(int processId); // дельта между последними двумя замерами
    public IEnumerable<ProcessMetrics> GetHistory(int processId);
}
```

Для потокобезопасности NetworkConnectionAlertService: предположим, что внутри используются `HashSet<string>` или `List<string>`, и методы `AddToBlacklist` / `AddToWhitelist` могут вызываться параллельно с `IsBlocked` или чтением свойств. Тест должен запустить множество параллельных задач: одни пишут (add/remove), другие читают (IsBlocked, свойства), и проверять absence of `InvalidOperationException` (Collection was modified).

Для ProcessMetricsTracker:
- Создать tracker, записать два снапшота для одного PID с разным CPU.
- Проверить, что `GetLastSnapshot` возвращает структуру со всеми полями.
- Проверить `GetCpuDelta` (разница между вторым и первым).

Используем xUnit. .NET 9. Нужны `using Xunit;` и possibly `FluentAssertions`? Пользователь не просил, лучше стандартный xUnit Assert.

Напишем два файла или один большой? Лучше два файла для читаемости, но в ответе можно объединить в один кодовый блок с разделителями или предоставить два блока. Пользователь сказал "Выдай готовый компилируемый C# код юнит-тестов". Можно выдать в одном сообщении несколько файлов.

Давайте я 작성ю:

1. `NetworkConnectionAlertServiceTests.cs`
2. `ProcessMetricsTrackerTests.cs`

В каждом файле в начале akan быть комментарий с предполагаемым пространством имён и API, чтобы пользователь мог подстроить.

Также добавим `using System.Collections.Concurrent;` для параллельных тестов.

Для теста приоритета Blacklist:
- Добавляем "evil.exe" в Whitelist и Blacklist.
- `IsBlocked("evil.exe")` should return `true`.

Для потокобезопасности:
- Используем `Parallel.For` или `Task.WhenAll`.
- Несколько задач добавляют/удаляют элементы.
- Несколько задач читают `IsBlocked` и перечисляют коллекции.
- Оборачиваем в `try/catch` или просто ждем `Task.WhenAll`, и если будет exception, тест упадет. Но лучше явно перехватить `InvalidOperationException` и.fail. Однако xUnit тест упадет на любом необработанном исключении в задаче? Если задача бросает исключение, `Task.WhenAll` сбросит AggregateException. Так что можно просто запустить и дождаться.
- Чтобы избежать реальных модификаций коллекции во время перечисления, нужно, чтобы сервис был потокобезопасен (например, использует `ConcurrentBag` или блокировки и возвращает снимки). Тест проверяет, что сервис это обеспечивает.

Для ProcessMetricsTracker:
- Структура метрик: создаем объект `ProcessMetrics` и проверяем свойства.
- Дельта CPU: записываем 10%, затем 25%, ожидаем дельта 15% (или абсолютную разницу).

Давайте уточним API ProcessMetricsTracker. Возможно, метод `Track(int processId, TimeSpan interval)` или что-то с `Process`. Но я suppose простой трекер с ручной записью снимков.

Напишем код:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StartupMonitor.Tests;

public class NetworkConnectionAlertServiceTests
{
    // ... тесты
}
```

Но если пространства имён отличаются, код не скомпилируется. Я добавлю комментарий: "Замените StartupMonitor.Services на ваше действительное пространство имён".

Теперь, чтобы код был максимально близко к реальности и компилируемым, я могу добавить в конец файла `internal class` stub-реализации, закомментированные или под `#if DEBUG`, но лучше не надо.

Давайте напишем полные тесты с допущениями.

**NetworkConnectionAlertServiceTests.cs**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

// Предполагаемое пространство имён сервиса. Подгоните под решение.
// using StartupMonitor.Services;

namespace StartupMonitor.Tests;

public class NetworkConnectionAlertServiceTests
{
    [Fact]
    public void Blacklist_Takes_Priority_Over_Whitelist()
    {
        // Arrange
        var service = new NetworkConnectionAlertService();
        const string processName = "suspicious-app.exe";

        service.AddToWhitelist(processName);
        service.AddToBlacklist(processName);

        // Act
        bool isBlocked = service.IsBlocked(processName);

        // Assert
        Assert.True(isBlocked, $"Процесс '{processName}' должен считаться заблокированным, даже если он есть в белом списке.");
    }

    [Fact]
    public void Concurrent_Reads_And_Writes_Do_Not_Throw_CollectionModifiedException()
    {
        // Arrange
        var service = new NetworkConnectionAlertService();
        const int iterations = 1000;

        var writeTasks = Enumerable.Range(0, iterations).Select(i => Task.Run(() =>
        {
            var name = $"process-{i % 50}.exe";
            service.AddToBlacklist(name);
            service.AddToWhitelist(name);
            service.RemoveFromBlacklist(name);
            service.RemoveFromWhitelist(name);
        }));

        var readTasks = Enumerable.Range(0, iterations).Select(i => Task.Run(() =>
        {
            var name = $"process-{i % 50}.exe";
            _ = service.IsBlocked(name);
            _ = service.Blacklist;
            _ = service.Whitelist;
        }));

        // Act & Assert
        // Ожидаем, что параллельные чтение и запись не приводят к InvalidOperationException.
        var allTasks = writeTasks.Concat(readTasks).ToArray();
        Task.WaitAll(allTasks);

        // Если дошли до этой строки — исключений CollectionModified не было.
        Assert.True(true);
    }

    [Fact]
    public void IsBlocked_Returns_False_For_Process_Only_In_Whitelist()
    {
        // Arrange
        var service = new NetworkConnectionAlertService();
        service.AddToWhitelist("safe-app.exe");

        // Act
        bool result = service.IsBlocked("safe-app.exe");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsBlocked_Returns_True_For_Process_In_Blacklist_Only()
    {
        // Arrange
        var service = new NetworkConnectionAlertService();
        service.AddToBlacklist("bad-app.exe");

        // Act
        bool result = service.IsBlocked("bad-app.exe");

        // Assert
        Assert.True(result);
    }
}
```

**ProcessMetricsTrackerTests.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// using StartupMonitor.Diagnostics;

namespace StartupMonitor.Tests;

public class ProcessMetricsTrackerTests
{
    [Fact]
    public void Recorded_Snapshot_Has_Correct_Structure_And_Values()
    {
        // Arrange
        var tracker = new ProcessMetricsTracker();
        int pid = 1234;
        double cpu = 12.5;
        long readBytes = 1024;
        long writeBytes = 2048;
        long memoryBytes = 50_000_000;

        // Act
        tracker.RecordSnapshot(pid, cpu, readBytes, writeBytes, memoryBytes);
        var snapshot = tracker.GetLastSnapshot(pid);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(pid, snapshot!.ProcessId);
        Assert.Equal(cpu, snapshot.CpuPercent, precision: 2);
        Assert.Equal(readBytes, snapshot.ReadBytes);
        Assert.Equal(writeBytes, snapshot.WriteBytes);
        Assert.Equal(memoryBytes, snapshot.WorkingSetBytes);
        Assert.InRange(snapshot.Timestamp, DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void Cpu_Delta_Is_Calculated_Correctly_Between_Two_Snapshots()
    {
        // Arrange
        var tracker = new ProcessMetricsTracker();
        int pid = 5678;

        tracker.RecordSnapshot(pid, 10.0, 0, 0, 0);
        tracker.RecordSnapshot(pid, 25.0, 0, 0, 0);

        // Act
        double delta = tracker.GetCpuDelta(pid);

        // Assert
        Assert.Equal(15.0, delta, precision: 2);
    }

    [Fact]
    public void Cpu_Delta_Returns_Zero_When_Only_One_Snapshot_Exists()
    {
        // Arrange
        var tracker = new ProcessMetricsTracker();
        tracker.RecordSnapshot(999, 50.0, 0, 0, 0);

        // Act
        double delta = tracker.GetCpuDelta(999);

        // Assert
        Assert.Equal(0.0, delta);
    }
}
```

Это хорошо. Но я хотел бы сделать код более надежным, добавив `using System.Diagnostics;`? Не обязательно.

Также, для .NET 9, xUnit v3? xUnit 3 поддерживает `[Theory]`, `[InlineData]`. Можно использовать классические `[Fact]`.

Теперь, есть нюанс: пользователь просит "готовый компилируемый C# код". Если я просто приведу эти тесты, они не скомпилируются без реальных классов. Я должен предупредить и, возможно, добавить минимальные реализации сервисов внутри тестового проекта в отдельных файлах, чтобы код был действительно компилируемым. Но тогда я буду тестировать свои реализации.

Лучший подход: дать тесты + привести "модельные классы-стабы" в отдельных файлах, которые можно временно использовать для компиляции, но с пометкой, что в реальном проекте они уже существуют. Или добавить их в те же файлы под `#if STUBS`.

Давайте я добавлю в каждый файл тестов región с "Вспомогательные заглушки для автономной компиляции" под компиляторной константой, например, `#if INCLUDE_STUBS`, но по умолчанию отключено. Тогда пользователь может раскомментировать. Но это может быть избыточно.

Мне кажется, достаточно написать тесты с явным комментарием о предполагаемом API и пространствах имён. Пользователь, ведущий инженер, поймет.

Однако, чтобы максимально соответствовать "готовый компилируемый код