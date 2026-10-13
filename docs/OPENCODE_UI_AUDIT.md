# OpenCode UI Performance Auditor (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 109.3 сек
- Статус: Успешно

---

# Аудит производительности StartupMonitor.UI: CPU / RAM / GPU / фризы

## 1. Таймеры `MainViewModel.cs` — фоновая нагрузка и трей

### 1.1 `_cpuUsageTimer` — стр. 953–969, тик 1 с, не останавливается в трее
```csharp
_cpuUsageTimer = new Timer(_ => {
  var total = CpuUsageHelper.GetCpuUsage(false);
  var external = CpuUsageHelper.GetCpuUsage(true);
  Dispatcher.Invoke(() => { CurrentCpuUsageText=...; CpuUsagePercentText=...; });
}, 1s, 1s);
```
Механика:
- `GetCpuUsage()` в `CpuUsageHelper.cs:54-103`: первый вызов в паре делает `GetOurProcessesTicks()` (`CpuUsageHelper.cs:160-196`) — `Process.GetProcesses()` + перебор **всех** процессов + `TotalProcessorTime` + `Dispose()` на каждом. Второй вызов (<200 мс) отдаёт кэш — дёшево, но первый — дорого, **каждую секунду**.
- `Dispatcher.Invoke` (синхронный) — пул-поток блокируется пока UI занят; если UI в layout (см. п.3 `Clear+Add`), тик встаёт в очередь и дёргает 3 `OnPropertyChanged` → перебиндинг карточки CPU.
- `MainWindow.xaml.cs:185-213` (`IsVisibleChanged`, `StateChanged`) останавливает только `TaskManager`, но **не** `_cpuUsageTimer`/`_pollingTimer`/`_themePollTimer`. `Hide()` в трей (стр. 132, 205) нагрузку не снимает.

Эффект: постоянный wake 1 Гц, ~1–2% CPU, запрет CPU-idle, микрофризы при совпадении с重排 списков.

### 1.2 `_pollingTimer` — стр. 1001–1005, 5 с, два IPC + полный ребилд списка
```csharp
_pollingTimer = new Timer(async _ => await LoadDataIfChanged(), 5s, 5s);
```
`LoadDataIfChanged` (1164–1216): `GetAllEntriesAsync() + GetBlacklistAsync()` → `HasChanges()` → `ApplyFilter()` (1368–1411):
```csharp
FilteredEntries = new ObservableCollection<StartupEntry>(filtered);
```
- Новый объект коллекции каждые 5 с (и при каждом `LoadRatings`/`LoadHashData`) → `ListBox` сносит все контейнеры, заново гоняет все конвертеры + `FilePathToIconConverter` + layout. Пауза останавливает (`OnIsMonitoringPausedChanged`, стр. 99/107) — правильно, но сворачивание в трей — нет.

### 1.3 `_logWatchTimer` — стр. 2064–2073, 3 с, дисковое чтение
`Task.Run(() => LogReaderService.ReadAllLogs(baseDir))` каждые 3 с + `Dispatcher.Invoke(ApplyLogFilter)`. Старт/стоп только по навигации на страницу логов. Если уйти в трей, находясь на `LogsPage`, чтение файлов и `new ObservableCollection<LogEntry>` продолжаются. Проверка изменений (2091–2098) сравнивает только первую/последнюю строку, но само чтение — всегда полное, O(n).

### 1.4 `_themePollTimer` — стр. 379–386, 2 с
```csharp
_themePollTimer = new Timer(_ => Dispatcher.Invoke(ApplySystemTheme), 2s, 2s);
```
`ApplySystemTheme` → чтение реестра `AppsUseLightTheme` + `ThemeService.ApplyTheme` (перестройка ресурсов). Активен только при `theme==system`, но тоже не гасится в трее.

### 1.5 Фоновые циклы рейтингов/хешей — стр. 1465–1550
`LoadRatingsAsync` (до 36×5 с = 3 мин) и `LoadHashDataAsync` (до 12×5 с = 60 с) каждый цикл делают IPC + `ApplyFilter()` (полный ребилд). `entries.Find(e => e.Id==...)` в цикле — O(n²). Отмена только через `_backgroundCts`, трея не знают.

### 1.6 `_searchDebounceTimer` — стр. 205–212
Пересоздание `Timer` на каждый кейстрок + `Dispatcher.Invoke(ApplyFilter)` → полный ребилд коллекции на UI-потоке. Быстрый ввод = серия ребилдов по 30–100 мс.

> Общий диагноз: ни один таймер `MainViewModel`, кроме `_pollingTimer` при `Pause`, не реагирует на `Hide()/IsVisible==false`. Приложение в трее продолжает будить CPU на 1/2/3/5 с.

## 2. `MainWindow.xaml` — виртуализация

| Список | Строки | Статус |
|---|---|---|
| `EntryList` | 536–543 `IsVirtualizing=True, Recycling` | Виртуализация **включена**, срыва нет. Но шаблон тяжёлый (см. ниже) |
| `ProcessList` | 1785–1792 `IsVirtualizing=True, Recycling` | Включена. Тяжёлый шаблон + `Clear+Add` (п.3) сводит выигрыш на нет |
| `LogEntries` | 1426–1427 `ScrollViewer > ListBox` | **Сорвана**. Внешний `ScrollViewer` даёт `ListBox` бесконечную высоту → `VirtualizingStackPanel` материализует **все** тысячи строк. RAM-всплеск + секундный фриз на `LoadLogsAsync` |
| `Blacklist` | 842–843 `ScrollViewer > ItemsControl + StackPanel` | **Нет вообще**. `ItemsControl` не виртуализуется по определению. При 100+ правил — все `ui:Card` в визуальном дереве |
| `RemovedEntries` 1326, `HistoryEntries` 1371 | plain `ListBox` | Виртуализация по умолчанию есть, но без `Recycling` — лишние аллокации контейнеров при скролле |
| `BootTimePage` 2141–2291 `ScrollViewer > StackPanel > 2×ListView+GridView` | Внешний `ScrollViewer` отключает внутреннюю виртуализацию обоих `ListView`; `GridView` и так не виртуализирует колонки хорошо |

Дополнительные GPU-расходы (не срыв, но цена кадра):
- `22–32`: 3 `Ellipse` 800–1050 px с `RadialGradientBrush` (Aurora) поверх `WindowBackdropType="Mica"` (стр. 11) — полноэкранный overdraw при каждом ресайзе/скролле. Mica + градиенты = самый дорогой фон в окне.
- `DropShadowEffect` **на строку**: логотип `60 Blur=18`, неоновый кант карточек `284, 1538 Blur=8`, Kaspersky-точка `761–763 Blur=6`. `DropShadowEffect` в WPF — software render, отключает батчинг; на 30 видимых строках — падение FPS при скролле.
- Тултип строки автозапуска `711–752`: `ToolTip > ScrollViewer MaxHeight=780 > Border > StackPanel` с ~15 биндингами + `RatingToStars/Color`, `Kaspersky*`, `ThreatLevel*`. Тултип не виртуализируется.
- Строка процесса `1869–2036`: `ContextMenu` задан через `Setter` в `ItemContainerStyle` (1800–1865) — отдельный инстанс меню на 7 `MenuItem` + иконки на каждый контейнер; `ItemsControl ThreatInfo.Indicators` внутри тултипа — вложенный не виртуализируемый список.

## 3. `TaskManagerViewModel.cs` — `Clear + Add` каждые 1.5 с

`UpdateSnapshot` (191–293) сам по себе грамотный: in-place обновление `_processMap` (мутация `CpuPercent/WorkingSet/...`, без пересоздания `ProcessInfo`).

Но финал `ApplyFilterAndSort` (520–527):
```csharp
FilteredProcesses.Clear();
foreach (var item in sortedList) FilteredProcesses.Add(item);
```
- Каждые 1.5 с (тикает служба): N событий `Add` + `Reset` → `ListBox` в режиме `Recycling` всё равно пересоздаёт привязки, перезапускает все конвертеры строки, прыгает скролл, слетает `SelectedProcess` (костыль-перевыбор 529–535 дёргает селекцию ещё раз).
- Перед этим: `SortProcesses` (331–363) — `OrderBy/ThenBy` с `ToList` на каждый тик; в flat-режиме цикл 385–390 сбрасывает `TreeLevel/HasChildren/ChildrenCount` **всем** процессам (лишние `PropertyChanged` ×3×N); в tree-режиме — `ToList + ToDictionary ×2 + HashSet + DFS-рекурсия `AddNodeAndChildren` (494–512)` — аллокации на каждый тик, GC-прессинг.
- `WindowHelper.GetProcessIdsWithWindows()` (209–214) — `EnumWindows` P/Invoke на каждый тик.
- Мутации метрик (230–233) уже подняли ~1200 `PropertyChanged` (300 проц. × 4), а `Clear+Add` сверху перестраивает контейнеры — двойная работа.
- Сортировка по умолчанию по CPU по убыванию → порядок строк churn'ится каждый тик, по строке невозможно попасть мышью (воспринимается как фриз).

## 4. `DesignHelperConverters.cs` и соседи — `new SolidColorBrush` без `Freeze()`

Прямое нарушение в целевых файлах:
- `DesignHelperConverters.cs:142` `HexToBrushConverter` — `return new SolidColorBrush(color)` на каждый вызов + `ColorConverter.ConvertFromString` (парсинг + исключения) на каждый вызов. Используется для `BadgeColor/SpeedRatingColor` BootTime (стр. 2206, 2279) — на строку на ребилд.
- `KasperskyIndicatorConverter.cs:45–48` `KasperskyColorConverter` — 4× `new SolidColorBrush` на вызов, вызывается дважды на строку (Fill эллипса 758 + Foreground тултипа 742).
- `StatusToColorConverter.cs:24–34` — до 7× `new` на вызов + чтение `ThemeService.CurrentTheme` на каждую строку.
- `RatingToStarsConverter.cs:49–71` `RatingToColorConverter` — до 11× `new` на вызов, вызывается дважды на строку (Text 708–709 + тултип 736).

Незамороженная кисть: UI-thread affinity, нельзя кэшировать render-потоком, Gen0-мусор. При ребилде процессов (300 строк × ~5 кистевых конвертеров = ~1500 кистей / 1.5 с) — заметный GC + re-upload в рендер.

Положительный пример в этом же проекте: `ProcessConverters.cs:11–21, 42–48` и `SignatureConverters.cs:38–48` — `static readonly` + `Freeze()` в статическом конструкторе. Нужно распространить паттерн.

## 5. `AppIconService.cs` — синхронный IO на UI-потоке

Цепочка: `Image Source={Binding FilePath, Converter=FilePathToIconConverter}` (`MainWindow.xaml:589`) → `FilePathToIconConverter:17 GetIcon(path)` → `AppIconService.GetIcon` (44–54):

- `ExtractIconCore` (56–89) выполняется **на вызывающем потоке**, т.е. на UI, т.к. конвертер вызывается в layout: `File.Exists(path)` (60) + в `CleanPath` ещё до 2× `File.Exists` (101, 105) — до 3 синхронных обращений к диску на промах (сеть/отключённый диск = десятки-сотни мс).
- `SHGetFileInfo` (65) — shell, для отсутствующих/сетевых путей тоже бьёт по диску.
- `Cache.GetOrAdd(cleanPath, ExtractIconCore)` — фабрика выполняется синхронно; первое открытие списка на ~200 записей = 200× IO + shell подряд → фриз открытия вкладки.
- Кэш без эвикции — неограниченный рост; нет `SHGFI_USEFILEATTRIBUTES` (флаг объявлен, стр. 32, но не используется в 64) — нельзя отдать иконку по расширению без касания диска.
- `Image ... BitmapScalingMode=HighQuality` (591) — дорогой ресемплинг на каждую строку.

`bitmap.Freeze()` (77) — единственное правильное место, его сохранить.

## Предложения (по приоритету, с оценкой)

1. **Гасить таймеры в трее** (`MainWindow.xaml.cs:185-213`, `MainViewModel`): в `IsVisible==false / Minimized+Hide` ставить `_cpuUsageTimer/_pollingTimer/_logWatchTimer/_themePollTimer` на `Timeout.Infinite`, при `Show` — рестарт; фоновые `LoadRatings/LoadHashData` отменять через `_backgroundCts`. Эффект: 0% CPU в трее вместо ~1–3%, продление батареи/тишина.
2. **Убрать `ScrollViewer` вокруг `LogEntries`** (1426): оставить скролл самого `ListBox` + `Recycling` + cap (напр. последние 2000 строк, виртуализация + инкрементальная подгрузка). Эффект: открытие логов с секунд до ~100 мс, RAM в разы ниже.
3. **Виртуализировать Blacklist и BootTime**: `ItemsControl+StackPanel` → `ListBox + VirtualizingStackPanel.Recycling`; `ListView+GridView` внутри внешнего `ScrollViewer` → `DataGrid` с внутренней виртуализацией или постраничность. Эффект: линейный рост UI заменяется константным.
4. **In-place diff `FilteredProcesses`** вместо `Clear+Add`: сверка по PID (update/move/remove/add через `ObservableCollection.Move`), сортировка только при смене колонки/направления, а не каждый тик; в flat-режиме не трогать `TreeLevel`-свойства; `GetProcessIdsWithWindows` кэшировать на 3–5 с. Эффект: исчезновение прыгания списка и 30–80 мс layout на тик.
5. **Замороженные статические кисти**: `KasperskyColor/StatusToColor/RatingToColor/HexToBrush` перевести на `static readonly + Freeze()` как в `ProcessConverters`; `HexToBrush` + словарь кэша `string→Brush`. Эффект: −тысячи аллокаций в секунду, меньше Gen0 GC и работы render-потока.
6. **Асинхронные иконки**: в `FilePathToIconConverter` возвращать placeholder сразу, догрузку через `Task.Run(ExtractIconCore)` + `Dispatcher` + `Cache`; в `SHGetFileInfo` добавить `SHGFI_USEFILEATTRIBUTES` для fallback по расширению; лимит кэша (LRU ~500). Эффект: открытие вкладки автозапуска без фриза, IO уходит с UI-потока.
7. **Срезать GPU-эффекты в строках**: убрать `DropShadowEffect` из `ItemTemplate` (оставить только на карточках/логотипе), Aurora-градиенты уменьшить/статизировать (`CacheMode`/freeze brush), `Dispatcher.Invoke` в тиках заменить на `InvokeAsync`. Эффект: плавный скролл, меньше просадок на iGPU.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs
[0m→ [0mRead src/StartupMonitor.UI/Converters/DesignHelperConverters.cs
[0m→ [0mRead src/StartupMonitor.UI/Services/AppIconService.cs
[0m✱ [0mGlob "*.cs"[90m in src/StartupMonitor.UI/Converters · 19 matches[0m
[0m✱ [0mGrep "_logWatchTimer|_pollingTimer|_cpuUsageTimer|_themePollTimer|IsVisibleChanged|OnTray|MinimizeToTray|Hide\(\)|Show\(\)"[90m in src/StartupMonitor.UI · 34 matches[0m
[0m✱ [0mGrep "ScrollViewer.*ListBox|ListBox.*ScrollViewer|<ScrollViewer|<ListBox|<ListView|<DataGrid|VirtualizingStackPanel|VirtualizationMode|ScrollViewer\.CanContentScroll"[90m in src/StartupMonitor.UI · 93 matches[0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs[90m [offset=1167][0m
[0m→ [0mRead src/StartupMonitor.UI/Converters/FilePathToIconConverter.cs
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml.cs[90m [limit=120, offset=120][0m
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml[90m [limit=300, offset=1300][0m
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml[90m [limit=250, offset=700][0m
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml[90m [limit=300, offset=1780][0m
[0m✱ [0mGrep "DropShadowEffect|BlurRadius|AuroraGlow|WindowBackdropType|Mica|FluentGlassCard"[90m in src/StartupMonitor.UI · 28 matches[0m
[0m✱ [0mGrep "class CpuUsageHelper|GetCpuUsage|PerformanceCounter|GetProcessTimes|QuerySystem"[90m in src/StartupMonitor.UI/Services · 0 matches[0m
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml[90m [limit=320, offset=2080][0m
[0m✱ [0mGrep "new SolidColorBrush|Freeze\(\)|new Brush|ColorConverter|Brushes\."[90m in src/StartupMonitor.UI/Converters · 100 matches[0m
[0m✱ [0mGrep "class CpuUsageHelper"[90m in src · 1 match[0m
[0m→ [0mRead src/StartupMonitor.UI/Converters/KasperskyIndicatorConverter.cs
[0m→ [0mRead src/StartupMonitor.UI/Converters/StatusToColorConverter.cs
[0m→ [0mRead src/StartupMonitor.UI/Converters/RatingToStarsConverter.cs
[0m→ [0mRead src/StartupMonitor.Shared/Helpers/CpuUsageHelper.cs

```
