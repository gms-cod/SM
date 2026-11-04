# Agent 8: UI Architecture, MVVM, Performance & UX Glitches Audit

- Время анализа: 127.0 сек
- Статус: Успех

---

Аудит UI к релизу — проверяю поток UI, утечки, первый старт и битые привязки.
Прочитал базу — теперь смотрю остаток и смежные сервисы.
Основное собрал — добью пару фактов про старт и конвертеры.
# UI-аудит перед релизом: StartupMonitor (WPF / .NET 8)

**Вердикт: к публичному выпуску не готов.** Белого экрана при первом старте нет (оверлей есть), архитектура polling/delta опроса грамотная, но есть 3 блокера P0: синхронный дисковый I/O настроек на UI-потоке, синхронные `Dispatcher.Invoke` с таймеров и дисковый I/O иконок в конвертере на UI-потоке. Плюс захардкоженные бейджи («+3»/«+1»/«Secure») — это видно пользователю сразу.

## 1. Зависания (Freezes)

### P0-1. `SaveSetting`/`LoadSetting` — синхронный файловый I/O на UI-потоке
`ViewModels/MainViewModel.cs:2267-2286` — каждый `SaveSetting` делает `File.ReadAllText` + `Deserialize` + `WriteAllText(tmp)` + `Move` **синхронно**. Вызывается из всех `OnXxxChanged` (тема, язык, тоглы, слайдер `CpuThrottleThreshold`, фильтры, сортировка). Слайдер `MainWindow.xaml:992` шлёт сохранение на каждый тик + pipe-вызов.
Конструктор `MainViewModel.cs:1301-1335` ~30 раз перечитывает и перепарсивает весь `ui-settings.json`.
**Исправление:** in-memory кэш настроек + дебаунс flush (~500 мс, `Task.Run`), `LoadSetting` — из кэша. Слайдер — сохранять по `LostFocus`/Compose, pipe-throttle.

### P0-2. Блокирующие `Dispatcher.Invoke` с фоновых таймеров
- Поиск: `MainViewModel.cs:226` — `Dispatcher.Invoke(ApplyFilter)` (sync).
- Тема: `MainViewModel.cs:401` — `Dispatcher.Invoke(ApplySystemTheme)` без проверки `Application.Current == null` (падение при shutdown).
- CPU-таймер: `MainViewModel.cs:1364` — `Dispatcher.Invoke` каждую секунду; при занятом UI пул-нитей копится.
- `LoadDataIfChanged`: `MainViewModel.cs:1694`, `ApplyFilter:1932` — sync Invoke.
**Исправление:** везде `InvokeAsync`/`BeginInvoke` + `CheckAccess`, coalesce (флаг «обновление уже в очереди»), в theme-watcher try/catch + null-check.

### P0-3. Иконки в `FilePathToIconConverter` — диск + `SHGetFileInfo` на UI-потоке
`Converters/FilePathToIconConverter.cs:19-26` → `Services/AppIconService.cs:69-102`: `Convert` вызывается на UI-потоке для каждой виртуализированной строки при скролле; внутри `File.Exists` (дважды: `CleanPath` + `ExtractIconCore`) + `SHGetFileInfo`. Кэш помогает только повторным. Первый скролл по ~200 записям = сотни синхронных обращений к диску.
**Исправление:** предзагрузка иконок в фоне после `SetInitialData`/`LoadData` (`Task.Run` + прогрев `AppIconService.GetIcon`), конвертер — только чтение кэша; либо async-свойство `Icon` на VM записи с плейсхолдером.

### P1-4. `StartupSequence` без таймаута на blacklist + двойное подключение
`Views/MainWindow.xaml.cs:352-364`: `entriesTask` покрыт таймаутом 75 с, а `blacklistTask` (`await blacklistTask`) — нет: зависший blacklist = вечный оверлей. Плюс создаётся отдельный `loader = new PipeClient()`, а затем ещё `ConnectCommand`/`LoadDataCommand` на основном клиенте — двойной коннект-чёрн.
**Исправление:** `Task.WhenAll` под общим `CancellationToken` 75 с; переиспользовать основной `_pipeClient`, убрать временный `loader`.

### P1-5. TaskManager: `EnumWindows` + сортировка на UI-потоке каждый тик
`ViewModels/TaskManagerViewModel.cs:219-224` — `WindowHelper.GetProcessIdsWithWindows()` на UI-потоке каждый снапшот; `ApplyFilterAndSort` + построение дерева + сортировки там же.
**Исправление:** снапшот разбирать в фоне, на UI маршалить только `SyncFilteredProcesses` (он уже in-place — это хорошо). `windowPids` кэшировать с троттлом ~2 с.

### P2-6. `TryGetProcessPath` на UI-потоке
`TaskManagerViewModel.cs:922-951` — `Process.GetProcessById().MainModule` в команде `OpenFileLocation` (UI-поток, может подвисать на защищённых процессах). Обернуть в `Task.Run`.

## 2. Утечки памяти / таймеры / коллекции

- ** пересоздание `FilteredEntries` каждые 5 с** — `MainViewModel.cs:1939`: `new ObservableCollection` при каждом `ApplyFilter` (poll 5 с + `LoadRatingsAsync` до 36 раз + hash-loop). Виртуализация `Recycling` обесценивается, сбрасываются скролл/выделение, мерцание, LOH-churn. TaskManager так не делает (`SyncFilteredProcesses`, `TaskManagerViewModel.cs:549`) — **перенести тот же in-place diff в MainViewModel**. То же для `Blacklist = new ...` в `LoadDataIfChanged:1697`.
- **`_backgroundCts` в `OnAiApiKeyChanged` (`MainViewModel.cs:575`)**: `Cancel()` без `Dispose` старого + гонка со старым `LoadRatingsAsync`. Диспозить под lock.
- **Двойной Dispose PipeClient**: `MainWindow.xaml.cs:159-164` — `_viewModel.Dispose()` уже диспозит `_pipeClient`, затем `_pipeClient.Dispose()` повторно. `PipeClient.Dispose` (`Services/PipeClient.cs:1297`) ловит исключения, но `_sendLock.Dispose()` дважды — скрытый `ObjectDisposedException`. Сделать `Dispose` идемпотентным (флаг `_disposed`) или диспозить только из VM.
- **Fire-and-forget `_ = DeactivateAsync/ActivateAsync`** из `IsVisibleChanged`/`StateChanged`/`NavigateToPage` — перекрывающиеся Activate/Deactivate, лишний `Subscribe/Unsubscribe` трафик. Сериализовать через семафор или флаг-версию.
- Остальное корректно: отписки в `MainViewModel.Dispose:414-427` и `TaskManagerViewModel.Dispose:979-991` есть; `DispatcherTimer` останавливается; `LogReaderService` уже идёт через `Task.Run`.

## 3. Падения: перехват есть, но молчаливый

`App.xaml.cs:84-99` — все три обработчика (`AppDomain`, `DispatcherUnhandledException`, `UnobservedTaskException`) **только пишут в лог**. `args.Handled = true` без диалога = «кнопка ничего не делает» глазами пользователя + продолжение в возможно битом состоянии.
- Theme-watcher (`MainViewModel.cs:399-402`) без try/catch на пул-потоке — исключение там = смерть процесса с единственной строкой в логе.
- Десятки `_ = _pipeClient.SetXxxAsync(...)` без await/catch — рассинхрон настроек UI/служба без единого признака.
**Исправление:** в `DispatcherUnhandledException` — дружелюбный `MessageBox` (не фатальное) + log; фатальное (`OutOfMemory`, `StackOverflow`) — shutdown. Fire-and-forget обернуть в `SafeFireAndForget` с логом. Theme/cpu колбэки — try/catch внутри.

## 4. Первый старт

Хорошо: `StartOverlay` (`MainWindow.xaml:2694`) видим изначально, радар + `StatusText` + панель ошибки с **Повтор/Выход** (`MainWindow.xaml.cs:400-409`). Пустого белого экрана нет. Статус-бар: красная точка + `ServiceStatusDetails` (`...` → `StatusServiceStopped`).

Недоделки:
1. **`--minimized`: `StartupSequence` вообще не запускается** — он висит на `Loaded`, а окно не показывают (`App.xaml.cs:137-140`). Служба не обеспечивается, коннекта нет, пока юзер не откроет окно. **Запускать headless-инициализацию из `App.OnStartup` при `IsMinimized`.**
2. **Нет empty-state**: если записей 0 или фильтр всё отсёк — голая пустая таблица. Добавить плейсхолдер «Нет записей / измените фильтр».
3. `StatusText.Text = message` напрямую (`MainWindow.xaml.cs:323`) вместо биндинга — при смене языка mid-startup текст не переведётся. Мелочь, перевести на свойство VM.

## 5. Недоделанные элементы / биндинги

1. **Захардкоженные заглушки** — `MainViewModel.cs:159,165,168`: `TotalBadgeText => "+3"`, `BlockedBadgeText => "+1"`, `SystemHealthStatus` всегда «Secure». Вычислить дельты (новые за 24 ч) и статус от угроз/заблокированных, либо убрать бейджи.
2. **Тумблер строки OneWay + ручной откат** (`MainWindow.xaml:778-784`, `MainWindow.xaml.cs:292-310`) — рабочий, но при ошибке pipe никакого фидбека: `ToggleEntryAsync` молча глотает. Добавить тост об ошибке.
3. **`MessageBox` без owner и не в стиле** в `TaskManagerViewModel.cs:704,717,731...` (Kill/Block/Suspend/Resume/Locate) — модально блочит, выглядит чужеродно. Задать `owner: Application.Current.MainWindow`, в идеале заменить на Wpf.Ui-диалоги.
4. **Дубли ключей конвертеров** (`App.xaml:42-44`: `BoolToVisibility` + `BooleanToVisibilityConverter` + `BoolToVisibilityConverter`) — resolve работает, но приглашает к ошибкам; оставить один.
5. **Контекстное меню собирается в code-behind при каждом открытии** (`MainWindow.xaml.cs:424-578`) — приемлемо, но проверить Output на binding errors для `StatusToVisibilityConverter(Delayed)`, `HexToBrushConverter(0.15)`, `Hyperlink.Command` внутри `TextBlock` (работает, но лог binding errors при старте прогнать обязательно).
6. `ShutdownMode="OnExplicitShutdown"` (`App.xaml:8`) — корректно для трея, убедиться что пункт трея «Выход» всегда доступен (да, `CreateTrayMenu:282-284`).

## 6. Конкретный план исправлений (порядок)

**P0 (блокеры релиза):**
1. Кэш настроек + асинхронный дебаунс-flush; убрать дисковый I/O с UI-потока.
2. `Dispatcher.Invoke` → `InvokeAsync` во всех таймерах/колбэках + null-check `Application.Current`.
3. Предзагрузка иконок в фоне; конвертер — только кэш.
4. Таймаут на `blacklistTask`; один PipeClient на StartupSequence.

**P1:**
5. In-place diff `FilteredEntries`/`Blacklist` (по образцу `SyncFilteredProcesses`).
6. Headless-старт при `--minimized`; empty-state таблицы.
7. Диалог для `DispatcherUnhandledException` + `SafeFireAndForget` для pipe-сетов; try/catch в theme-watcher.
8. Заменить захардкоженные `+3/+1/Secure` реальными значениями.
9. Идемпотентный `PipeClient.Dispose`; сериализация Activate/Deactivate TaskManager.

**P2:**
10. `MessageBox` с owner → Wpf.Ui-диалоги; тост при ошибке тоггла; чистка дублей конвертеров; прогон Output на binding errors.

Если хочешь, следующим шагом внесу P0-правки (настройки + Dispatcher + иконки + таймаут blacklist) — это ~4 точечных изменения с verificацией `dotnet build`.

