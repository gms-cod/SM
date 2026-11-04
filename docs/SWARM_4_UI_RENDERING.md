# OpenCode Swarm Agent 4: WPF Virtualization & UI Thread Bottlenecks (OpenCode muse-spark-1.3-contributor-free)

- Время выполнения: 120.5 сек
- Статус: Успешно

---

Архитектуру разберу — проверю виртуализацию, конвертеры и слияние коллекций.
Первичный разбор готов — теперь копаю глубже в узкие места.
## Аудит производительности UI: виртуализация, конвертеры, `TaskManager`, блокировки UI-потока

### 1. Виртуализация `MainWindow.xaml` — частично сорвана

**Что хорошо:**
- `EntryList` (`MainWindow.xaml:539-546`), `ProcessList` (`1958-1965`), `LogsPage ListBox` (`1596-1603`) имеют `IsVirtualizing=True + Recycling`. `LogsPage` дополнительно `CanContentScroll=True` — эталон.
- Списки лежат напрямую в `Grid.Row=1 Height=*`, а не внутри `ScrollViewer` — контейнер имеет конечную высоту, виртуализация работает.

**Дефекты:**

а) `EntryList` и `ProcessList` **без явного `ScrollViewer.CanContentScroll="True"`**. Для `ListBox` дефолт `True`, но любое переопределение `Template`/`ItemsPanel`/`Style` в `App.xaml` или `WpfUi` молча переключит на `StackPanel` + `SmoothScroll` → виртуализация умирает, создаются все ~300 контейнеров. Явно дописать как в `LogsPage:1601`.

б) Тяжёлый `ItemTemplate` ломает смысл recycling:
- `EntryList:572-792`: `Image (28x28) + 8 колонок + Tooltip` с вложенным `ScrollViewer + Border + ~15 TextBlock + ItemsControl ThreatInfo.Indicators`. Тултип материализуется лениво, но биндинги внутри тултипа (`RatingToStars`, `KasperskyIndicator`, `ThreatLevelToForeground` — `714-751`) подписываются сразу.
- `ProcessList:2072-2259`: `ContextMenu` задан **в `ItemContainerStyle` (`1973-2068`)** — на каждую строку свой `ContextMenu` с 7 `MenuItem` + `Binding PlacementTarget...`. Это ~2000 живых биндингов на 250 процессов. Вынести в один shared `ContextMenu` на уровне `ListBox` (`ListBox.ContextMenu`), `CommandParameter={Binding SelectedProcess}`.
- `DropShadowEffect` на каждой строке: `Ellipse.Effect (764-766)`, неоновый кант карточек `(285-289)`, `Path.Effect` в логотипе `(62-64)`. Эффекты форсируют software-render ветки и инвалидируют кэш при скролле. Заменить на статичную `Border` без эффекта или кэшировать (`CacheMode="BitmapCache"`).

в) Отсутствие `VirtualizingPanel.ScrollUnit="Pixel"`, `IsContainerVirtualizable="True"`, `Delay` на иконках. При быстрой прокрутке каждая новая строка синхронно дёргает `FilePathToIconConverter`.

г) `BlacklistPage:845-884` — `ScrollViewer + ItemsControl + ItemsPanel=StackPanel` — виртуализации нет по дизайну. При >100 правил — всё в визуальном дереве. Заменить на `ListBox` с виртуализацией как в логах.

### 2. Конвертеры: кто аллоцирует в UI-потоке каждый тик

**Чистые (0 аллокаций) — образец:**
- `KasperskyIndicatorConverter.cs:15-26` — возвращает интернированные строки, `KasperskyColorConverter:41-66`, `SignatureConverters.cs:38-136`, `ProcessConverters.cs:11-97` — статические замороженные кисти. Так держать.
- `DesignHelperConverters.cs:135-154` `FrozenBrushCache` + `HexToBrushConverter.ConvertCore:172-207` — правильно, ключ `hex|opacity`, cap 512.

**Грязные (фиксить в первую очередь):**

а) `RatingToStarsConverter + RatingToColorConverter` (`Converters/RatingToStarsConverter.cs:13-72`) — **главный GC-шторм**:
```csharp
// Convert вызывается N_строк × 2 (текст+цвет) × каждый ApplyFilter/Sort/тик
return new SolidColorBrush(...) // ~500 new/тик, незамороженные, с Dispatcher-affinity
```
Плюс чтение `ThemeService.CurrentTheme` внутри `Convert` — триггер перерисовки всех строк при смене темы. Решение: 10 статических замороженных кистей (5 уровней × light/dark) как в `KasperskyColorConverter`, выбирать без `new`.

б) `FilePathToIconConverter.cs:13-20 → AppIconService.GetIcon` (`Services/AppIconService.cs:44-54`):
- `CleanPath:91-110` делает **2× `File.Exists` синхронно в UI-потоке** на каждую новую строку при скролле. `File.Exists` = syscall + antivirus filter, 0.1–2 мс. На 30 новых строк при быстром скролле = 60 мс фриза.
- `ExtractIconCore:56-89` — `SHGetFileInfo + CreateBitmapSourceFromHIcon` тоже в UI-потоке (первый показ каждого exe). `Cache.GetOrAdd(cleanPath, ExtractIconCore)` выполняет фабрику синхронно на вызывающем потоке.
- Решение: двухступенчатое — синхронно вернуть `null`/плейсхолдер из кэша (`TryGetValue`), промах — очередь в `Task.Run` + `Freeze()` (уже есть `77`) + `Dispatcher.BeginInvoke` для вставки в кэш и `OnPropertyChanged(nameof(FilePath))` только для видимых. Либо предзагрузка иконок в фоне в `LoadDataAsync`.

в) `LocationToImpact{Background,Foreground,Border}Converter` (`DesignHelperConverters.cs:52-126`) — `Application.Current?.Resources[...] as Brush` **на каждый вызов**. Это `ResourceDictionary` lookup + `DynamicResource`-совместимость в горячем пути. Закэшировать 3×3 кисти статически при старте / подписаться на смену темы один раз, как сделано для `Signature`.

г) `TreeIndentConverter.cs:250-261` — `new Thickness(...)` на строку. Структура, дёшево, но вызывается на каждый тик сортировки CPU. Можно отдавать `double` в `Margin` через `Thickness` в модели (`TreeIndentLeft` уже `double`?) либо кэшировать 0..10 уровней статическим массивом.

д) `SignatureStatusToTooltipConverter.cs:142-194` — `FormatTooltip` делает **интерполяцию 3–5 строк на каждый `GetToolTip`** + биндинг `ToolTip={Binding Converter=...}` (`MainWindow.xaml:686,2199`) без привязки к конкретному свойству — пересчитывается при любом `PropertyChanged` строки. Перевести тултип на `MultiBinding` (`Status+Signer+Issuer`) или свойство `SignatureTooltip` в модели, посчитанное один раз в службе.

### 3. `TaskManagerViewModel.cs` — слияние правильное, но тик всё равно дёргает UI

**Хорошо (оставить):**
- `UpdateSnapshot:220-298` — обновление метрик in-place на существующих `ProcessInfo` через `INotifyPropertyChanged`, `_processMap` как источник истины, `SyncFilteredProcesses:541-606` — Diff без `Clear()` с `fast-path orderSame (549-560)`, сохранением `SelectedProcess` по PID. Это предотвращает мерцание и сброс скролла. `MainViewModel.ApplyFilter:1703` наоборот делает `FilteredEntries = new ObservableCollection(...)` на каждый поллинг 5с / рейтинг / сортировку — **полное пересоздание контейнеров, потеря фокуса, выделения и позиции скролла**. Унифицировать `MainViewModel` на тот же `Sync`-паттерн.

**Оставшиеся проблемы в `TaskManager`:**

а) `SyncFilteredProcesses:578` — `FilteredProcesses.IndexOf(target)` в цикле = **O(n²) при переупорядочивании**. Сортировка по CPU каждые 1.5с переставляет десятки строк → `Move(oldIndex,i)` по одному с `CollectionChanged` на каждый. При 300 процессах это сотни уведомлений/тик. Решение: если `moves > threshold (напр. 20)`, батчить через `DeferRefresh` / пересобрать через `ObservableCollection` range-замену за одно уведомление, либо throttle сортировки: сортировать только если `CpuDelta > epsilon` или не чаще 1 раза в 3с при отсутствии поиска.

б) `ApplyFilterAndSort:392-397,457-463` — сброс `TreeLevel/HasChildren/ChildrenCount/IsExpanded` **для всех `_processMap.Values` на каждый тик**, даже в flat-режиме. Если сеттеры `ProcessInfo` не проверяют `if (field == value) return`, каждый тик = N×4 `PropertyChanged` → N×4 `Convert` → перераскладка. Добавить guard в сеттеры + не трогать свойства в flat-режиме если уже 0/false/0.

в) `UpdateSnapshot:211-216` — `WindowHelper.GetProcessIdsWithWindows()` (EnumWindows) **синхронно в UI-потоке каждый снимок**. Вынести в службу (уже есть снимок процессов) или кэшировать на 5–10с в фоне.

г) `SortProcesses:337-369` — `OrderBy(...).ToList()` + `ToDictionary` (`417,454`) + `HashSet` аллокации каждый тик. Мелочь, но в дереве `SortProcesses(children)` вызывается рекурсивно на каждую ветвь. Решение: сортировать только видимую (отфильтрованную) проекцию, корни — `List.Sort` in-place по предвычисленным ключам.

д) Поиск в дереве (`419-449`) мутирует `_collapsedPids` и `IsExpanded` прямо во время фильтрации — пользовательский collapse теряется при вводе. Разделить `searchExpanded` overlay от persistent `_collapsedPids`.

е) `DispatcherTimer _searchDebounceTimer (113-121)` — правильно на Dispatcher; а в `MainViewModel:221-228` поиск через `System.Threading.Timer + Dispatcher.Invoke` (синхронный!). Заменить на `DispatcherTimer` как в `TaskManager` или `InvokeAsync`.

### 4. Сценарии зависания UI-потока и архитектурные решения

| # | Место | Блокировка | Решение |
|---|-------|------------|---------|
| 1 | `MainViewModel:401` `StartSystemThemeWatcher` → `Dispatcher.Invoke(ApplySystemTheme)` каждые 2с; `OnSearchTextChanged:226` → `Dispatcher.Invoke(ApplyFilter)` | Синхронный `Invoke` с пул-потока: если UI занят рендером, пул-поток блокируется; при вызове из UI-потока — реентрантность. `ApplyFilter` + `new ObservableCollection` (~мс) выполняется внутри `Invoke`. | Везде `InvokeAsync(DispatcherPriority.Background)` / `BeginInvoke`. Поиск — `DispatcherTimer 250мс` (уже есть в TM). Тему — `ManagementEventWatcher` на реестр вместо поллинга, либо `SystemEvents.UserPreferenceChanged`. |
| 2 | `MainWindow.xaml.cs:305,388` `Dispatcher.Invoke` в `StartupSequence/ShowStartupError` | `StartupSequence` уже на UI-контексте после `await` — `Invoke` избыточен, добавляет deadlock-окно при закрытии окна. | `CheckAccess()` + прямое присваивание, либо `InvokeAsync`. Долгие `EnsureServiceConfiguredAndRunningAsync (45с)` и `loader.ConnectAsync` держать вне UI-контекста (`ConfigureAwait(false)`), в UI возвращаться только для `StatusText`. |
| 3 | `File.Exists` в `AppIconService.CleanPath:101-106,60` + `SHGetFileInfo:65` в UI-потоке | См. п.2б. Плюс `TryGetProcessPath` в `TaskManagerViewModel:911-940` (`Process.GetProcessById + MainModule + OpenProcess`) — вызывается по клику, ок, но `MainModule` на защищённом процессе бросает и тормозит ~100мс. | Иконки — async pipeline с плейсхолдером (выше). `TryGetProcessPath` — только `QueryFullProcessImageName`, без `MainModule`, обернуть в `Task.Run`. |
| 4 | `MainViewModel.LoadDataIfChanged:1454-1494` + `LoadHashDataAsync:1818` `GetAllEntriesAsync` в цикле 60с | Каждый `ApplyFilter()` внутри `LoadRatingsAsync:1794` и `LoadHashDataAsync:1836` пересоздаёт коллекцию (см. п.3) → 12–36 полных перестроений списка за минуту. `HasChanges:1525` строит `ToDictionary` каждый поллинг. | `ApplyFilter` → in-place Diff как в TM; рейтинги/хэши — патчить свойства модели (уже `INotifyPropertyChanged`), без `ApplyFilter`, сортировку по рейтингу откладывать до settle (debounce 1с). `HasChanges` покрыт ревизией сервера — лишний `GetBlacklistAsync` при `!isModified` уже пропущен, ок. |
| 5 | `ProcessInfo` property-churn | Каждое обновление CPU/RAM/Disk/Net (`232-237`) — 6 `PropertyChanged` × 300 процессов × 0.66 Гц = ~1200 уведомлений/с, каждое тянет `CpuUsageToColorConverter` + `DisplayCpu` форматирование. | В модели: guard `if (Math.Abs(field-value) < epsilon) return`; `DisplayCpu/DisplayRam` — вычислять в геттере без отдельных уведомлений, либо throttle текстовых полей (обновлять текст только если изменился на ≥0.1%). |

**Приоритет фиксов:** 1) `RatingToColorConverter` статические кисти; 2) `MainViewModel.ApplyFilter` → Diff-sync; 3) async-иконки; 4) `Invoke→InvokeAsync` + убрать `File.Exists`/`EnumWindows` из UI-потока; 5) `ContextMenu` shared + убрать `DropShadowEffect` из строк.


### Stderr / Warnings:
```
[0m
> build · muse-spark-1.3-contributor-free
[0m
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml
[0m→ [0mRead src/StartupMonitor.UI/Converters/DesignHelperConverters.cs
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs
[0m✱ [0mGlob "src/StartupMonitor.UI/Converters/*.cs"[90m 19 matches[0m
[0m✱ [0mGrep "Dispatcher\.Invoke|FilePathToIcon|KasperskyIndicator|Virtualizing|ScrollViewer|ItemsPanel|CanContentScroll"[90m in src/StartupMonitor.UI · 51 matches[0m
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml[90m [offset=657][0m
[0m→ [0mRead src/StartupMonitor.UI/Converters/FilePathToIconConverter.cs
[0m→ [0mRead src/StartupMonitor.UI/Converters/KasperskyIndicatorConverter.cs
[0m→ [0mRead src/StartupMonitor.UI/Converters/SignatureConverters.cs
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml[90m [offset=1550, limit=500][0m
[0m✱ [0mGrep "class AppIconService|GetIcon|ExtractIcon|File\.Exists|MainModule|Dictionary|ConcurrentDictionary|Freeze"[90m in src/StartupMonitor.UI/Services · 21 matches[0m
[0m→ [0mRead src/StartupMonitor.UI/Converters/ProcessConverters.cs
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml[90m [limit=350, offset=2050][0m
[0m→ [0mRead src/StartupMonitor.UI/Views/MainWindow.xaml.cs[90m [limit=150, offset=290][0m
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs[90m [offset=1220, limit=350][0m
[0m→ [0mRead src/StartupMonitor.UI/Services/AppIconService.cs
[0m→ [0mRead src/StartupMonitor.UI/Converters/RatingToStarsConverter.cs
[0m→ [0mRead src/StartupMonitor.UI/ViewModels/MainViewModel.cs[90m [limit=350, offset=1570][0m

```
