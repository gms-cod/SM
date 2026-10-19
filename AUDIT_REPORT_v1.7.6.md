# Комплексный глобальный аудит архитектуры, стабильности и эффективности Startup Monitor v1.7.6

Дата проведения: 19 октября 2026 г.  
Статус аудита: Успешно завершен армией субагентов.  
Кодовая база: 442 C# файла (.NET 8, WPF, Windows Service, Win32 P/Invoke, COM, Named Pipe IPC).

---

## 1. Сводная таблица найденных дефектов и оптимизаций

| № | Уровень важности | Подсистема / Файл | Выявленный дефект или узкое место | Риск / Влияние на систему | Предлагаемое архитектурное решение |
|---|---|---|---|---|---|
| **1** | 🔴 **Критическая (P0)** | `Pipe/EventPipeServer.cs`<br>`Services/NotificationService.cs` | Синхронная блокировка `lock (writer)` и `writer.Flush()` в тике таймера 200 мс | При подлагивании UI-клиента или переполнении буфера Windows NamedPipe служба мгновенно исчерпывает пул потоков .NET (ThreadPool Starvation) и зависает намертво. | Перевести отправку на асинхронный канал с таймаутом `CancellationTokenSource(2s)` и изолированной очередью сброса пакетов на каждого клиента. |
| **2** | 🔴 **Критическая (P0)** | `Services/Ai/AiRatingService.cs` | Англоязычный string matching `ex.Message.Contains("No connection could be made")` и отсутствие Circuit Breaker | На русскоязычных Windows ошибка сокета пишется по-русски, из-за чего проверка не срабатывает. При отключенном интернете служба уходит в 5 повторов по 7 секунд на каждую запись (зависание до 30 минут). | Проверять тип сетевой ошибки (`SocketException` / `HttpRequestError`), внедрить Circuit Breaker с переходом в режим ожидания сети на 5 минут после 2 сбоев подряд. |
| **3** | 🔴 **Критическая (P0)** | `Shared/Models/JsonDataStore.cs`<br>`Pipe/PipeServer.cs` | Прямой возврат мутабельной коллекции `_cache.Entries` без синхронизации чтения-записи | Фоновые сканеры и команды IPC мутируют список одновременно с чтением UI-запросов, вызывая `InvalidOperationException: Collection was modified`. | Возвращать защищенный снимок `IReadOnlyList<StartupEntry>` (`ToArray()`) либо защитить обращение через `ReaderWriterLockSlim`. |
| **4** | 🔴 **Критическая (P0)** | `Services/RegistryWatcher.cs` | Синхронный блокирующий P/Invoke `RegNotifyChangeKeyValue(fAsynchronous: false)` на 25 потоках | 20–30 системных потоков ThreadPool перманентно заблокированы ожиданием реестра, деградируя реакцию службы на таймеры и IPC. | Перевести на асинхронный режим (`fAsynchronous: true`) с `AutoResetEvent` и `ThreadPool.RegisterWaitForSingleObject` (0 заблокированных потоков). |
| **5** | 🔴 **Критическая (P0)** | `Pipe/PipeServer.cs:311, 352` | Вызов `_store.Save(config)` при операциях обычного чтения `GetAllEntries` и `GetEntriesDelta` | Каждые несколько секунд при штатном опросе из UI происходит полная перезапись 4 конфигурационных файлов на SSD с расчетом SHA-256 хэшей. | Устранить вызов `_store.Save` из обработчиков чтения. Сохранять состояние строго при добавлении, редактировании или удалении записей. |
| **6** | 🔴 **Критическая (P0)** | `UI/Services/PipeClient.cs`<br>`UI/ViewModels/MainViewModel.cs` | Перехват только `OperationCanceledException` в `SendAsync`, пропуск `IOException` | При перезапуске или падении службы вызов канала выбрасывает `IOException`, пробивая стек Dispatcher. Событие `Disconnected` не вызывается, авто-реконнект не запускается. | Обернуть сетевой стек в `catch (Exception ex) when (ex is IOException or ObjectDisposedException)` с гарантированным вызовом `Disconnected?.Invoke(...)`. |
| **7** | 🔴 **Критическая (P0)** | `UI/Converters/FilePathToIconConverter.cs`<br>`UI/Services/AppIconService.cs` | Синхронный `SHGetFileInfo` и `File.Exists` в UI-потоке диспетчера WPF при скролле списков | Просадка FPS ниже 15 при прокрутке списка автозагрузки, микрофризы при наличии сетевых или недоступных путей. | Асинхронная загрузка иконок в фоновом потоке с `BitmapSource.Freeze()` и отображением легковесной заглушки до готовности иконки. |
| **8** | 🔴 **Критическая (P0)** | `Services/ProcessNetworkTracker.cs:198` | Отсутствие IP-адресов в ключе IPv6 сокетов (`6:{port}-{port}`) | Все соединения IPv6 разных хостов с одинаковыми портами затирают статистику друг друга, приводя к коллизиям и искажению метрик. | Включить `LocalAddress` и `RemoteAddress` в структуру ключа `SocketKey6`. |
| **9** | 🟠 **Высокая (P1)** | `Shared/Models/PipeMessage.cs`<br>`Pipe/PipeServer.cs` | Double-JSON экранирование в `string Payload` и попадание пакетов > 85 КБ в LOH кучу | При передаче сотен правил брандмауэра или процессов строки попадают в LOH, провоцируя частые сборки мусора Gen 2 и фрагментацию ОЗУ. | Использовать типизированную потоковую сериализацию `Utf8JsonWriter` без создания промежуточных строковых JSON буферов. |
| **10** | 🟠 **Высокая (P1)** | `Services/Firewall/NetworkConnectionAlertService.cs` | Перебор через late-bound COM 800 правил Firewall каждые 5 секунд | Паразитная загрузка CPU на уровне 5–15% в режиме простоя только на COM-опрос правил безопасности. | Кэшировать список путей брандмауэра на 60 секунд со сбросом кэша только при операциях добавления/удаления правил. |
| **11** | 🟠 **Высокая (P1)** | `Services/ProcessNetworkTracker.cs:288` | Бесконечный рост словаря `_procNameCache` без удаления устаревших PID | Постепенная утечка оперативной памяти (Memory Leak) при длительной непрерывной работе Windows. | Добавить таймер очистки устаревших записей по истечении TTL (15 секунд). |
| **12** | 🟠 **Высокая (P1)** | `UI/ViewModels/MainViewModel.cs:1958` | Пересоздание экземпляра `ObservableCollection` при каждом символе поиска | Сброс виртуализации, сброс позиции скролла и лаги при вводе в строку поиска. | Внедрить дифференциальную синхронизацию (in-place) аналогично уже проверенному методу `SyncFilteredProcesses` из диспетчера задач. |
| **13** | 🟠 **Высокая (P1)** | `UI/ViewModels/TaskManagerViewModel.cs`<br>`UI/Services/WindowHelper.cs` | Синхронный `EnumWindows` и `GetWindowTextLength` на UI-потоке каждую секунду | Если в Windows есть хотя бы одно зависшее окно, UI диспетчера задач намертво блокируется на системный таймаут. | Вынести `EnumWindows` на фоновый поток и использовать `SendMessageTimeout` с флагом `SMTO_ABORTIFHUNG`. |
| **14** | 🟠 **Высокая (P1)** | `Services/SystemScannerService.cs`<br>`Services/ServiceWatcher.cs` | Неутилизированные COM-коллекции WMI `ManagementObjectCollection` и TaskScheduler | Накопление COM RCW объектов в очереди финализации .NET, утечка системных дескрипторов WMI. | Обернуть вызовы `searcher.Get()` и `task` в конструкции `using`. |
| **15** | 🟠 **Высокая (P1)** | `Services/SystemScannerService.cs:63` | Отсутствие передачи `CancellationToken` в `ScanAll` | При завершении работы Windows служба не может прервать глубокое сканирование и принудительно убивается SCM по таймауту. | Пробросить токен отмены во все приватные методы сканирования реестра, WMI, папок и служб. |
| **16** | 🟠 **Высокая (P1)** | `Services/Firewall/ProcessPortDefenderService.cs` | Гонка на `_previousConnectionKeys` и одновременная запись в файл `.tmp` | Ошибки `IOException: The process cannot access the file` при одновременном срабатывании бана и авторазбана. | Синхронизировать дисковую запись и операции над коллекцией единым объектом блокировки `_sync`. |
| **17** | 🟠 **Высокая (P1)** | `Services/WindowsFirewallService.cs:1675` | Неполный перечень HRESULT в `IsFirewallDown` (нет 0x80070424, 0x8007041D, 0x800706BA) | Сбои логики при поврежденной службе брандмауэра или перезапуске RPC-сервера Windows. | Расширить перечень перехватываемых HRESULT кодов RPC и Service Control Manager. |
| **18** | 🟠 **Высокая (P1)** | `UI/Views/MainWindow.xaml.cs`<br>`UI/Views/NetworkSecurityHubView.xaml.cs` | Утечка дизайнерских экземпляров UserControl и PipeClient при инициализации в XAML | Зомби-таймеры сетевого опроса продолжают тикать в памяти после подмены UserControl в коде окна. | Освобождать ресурсы старого экземпляра через `(control as IDisposable)?.Dispose()` перед добавлением нового контрола. |
| **19** | 🟠 **Высокая (P1)** | `UI/ViewModels/MainViewModel.cs:441` | Не останавливаются `_pollingTimer` и `_themePollTimer` при сворачивании в трей | Лишние опросы Named Pipe и системного реестра каждые 2–5 секунд при свернутом окне. | Останавливать таймеры в `SuspendBackgroundActivity()` и перейти на событие `SystemEvents.UserPreferenceChanged`. |
| **20** | 🟠 **Высокая (P1)** | `UI/Views/MainWindow.xaml:876, 2449` | Потеря виртуализации списков из-за `ItemsControl` и родительских `ScrollViewer/StackPanel` | Повышенное потребление ОЗУ и медленный рендеринг черного списка и истории загрузки. | Заменить на `ListBox` с фиксированной строкой Grid и включенным режимом виртуализации. |
| **21** | 🟡 **Средняя (P2)** | `Shared/Helpers/AuthenticodeVerifier.cs` | Полный сброс кэша `Cache.Clear()` при достижении 2000 записей | Спайк нагрузки на диск и процессор из-за лавинообразной повторной валидации цифровых подписей (Cache Stampede). | Заменить на LRU-вытеснение 20% старых записей либо `MemoryCache`. |
| **22** | 🟡 **Средняя (P2)** | `UI/Views/MainWindow.xaml` | Отсутствие `VirtualizingPanel.ScrollUnit="Pixel"` в таблицах | Ступенчатый скроллинг элементами с рывками вместо плавного 60 FPS скролла. | Добавить `ScrollUnit="Pixel"` и `CacheLength="2,2"`. |
| **23** | 🟡 **Средняя (P2)** | `UI/Views/NetworkSecurityHubView.xaml.cs` | Перепривязка `ItemsSource` каждые 2 секунды в DataGrid соединений | Сброс пользовательского выделения строки и лишняя нагрузка на GC. | Применить дифференциальное in-place обновление строк без смены коллекции. |
| **24** | 🟡 **Средняя (P2)** | `Shared/Audit/Exporters/JsonAuditReportExporter.cs` | Инстанцирование `new JsonSerializerOptions` при каждом экспорте | Лишние аллокации памяти и сброс внутренних кэшей метаданных сериализатора. | Сделать `JsonSerializerOptions` статическим синглтоном. |
| **25** | 🟡 **Средняя (P2)** | `Services/Toolbox/LanScannerService.cs:73` | Вызов `limiter.Release()` на утилизированном `SemaphoreSlim` при отмене | Ошибка `ObjectDisposedException` в фоновом пуле потоков при отмене сканирования сети. | Дождаться завершения дочерних задач перед уничтожением семафора. |
| **26** | 🟡 **Средняя (P2)** | `UI/Views/ToolboxView.xaml.cs:406` | Тройное последовательное чтение файла для расчета SHA256, SHA1 и MD5 | Замедление хеширования больших файлов в 3 раза из-за трехкратного чтения с диска. | Вычислять все три хеша за один проход `FileStream` через `IncrementalHash`. |
| **27** | 🟡 **Средняя (P2)** | `Services/Wmi/WmiSubscriptionHelper.cs:97` | WMI поиск `ManagementObjectSearcher` без таймаута | Риск зависания службы при повреждении базы данных репозитория WMI Windows. | Установить `EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) }`. |
| **28** | 🟢 **Низкая (P3)** | `Services/Firewall/SlidingWindowTracker.cs:110` | Отсутствие вытеснения устаревших IP до достижения лимита в 10 000 | Удержание в памяти устаревших записей сканеров портов. | Добавить фоновую периодическую очистку узлов с пустыми метками времени. |
| **29** | 🟢 **Низкая (P3)** | `Pipe/PipeServer.cs:197` | Нулевой размер буфера `inBufferSize: 0, outBufferSize: 0` в NamedPipe | Увеличение количества переключений контекста ядра Windows при передаче данных. | Задать явный размер буфера канала `65536` байт. |
| **30** | 🟢 **Низкая (P3)** | `Services/Runtime/ActivityGate.cs` | Механизм `ActivityGate` не интегрирован во все вторичные циклы службы | Служба пробуждает процессор чаще необходимого при отсутствии активных окон UI. | Подключить `ActivityGate` к циклам проверки правил и анализа процессов. |

---

## 2. Пошаговый план доработок по степени важности

### Этап 1: Устранение критических уязвимостей ядра и службы (P0 — Немедленно)
1. **EventPipeServer & NotificationService**:
   - Перевести рассылку системных уведомлений на асинхронный канал с таймаутом `CancellationTokenSource(TimeSpan.FromSeconds(2))`.
   - Изолировать зависших UI-клиентов от основного конвейера службы.
2. **AiRatingService (Защита от отсутствия интернета)**:
   - Заменить строковый поиск англоязычного сообщения на строгую проверку типа исключения `SocketException` / `HttpRequestError`.
   - Внедрить Circuit Breaker: при 2 ошибках подряд замораживать внешние сетевые запросы на 5 минут.
3. **JsonDataStore & PipeServer**:
   - Возвращать защищенный снимок `Entries.ToArray()` в методе `Load()`, исключив гонки `InvalidOperationException`.
   - Удалить строку `_store.Save(config)` из обработчиков чтения `GetAllEntries` и `GetEntriesDelta`, прекратив постоянную перезапись SSD.
4. **RegistryWatcher (Zero-Crash ThreadPool)**:
   - Заменить синхронный вызов `RegNotifyChangeKeyValue(fAsynchronous: false)` на асинхронную регистрацию в ThreadPool через `RegisterWaitForSingleObject`. Освободить 20–30 потоков.
5. **ProcessNetworkTracker**:
   - Включить IP-адреса в ключ сокета IPv6, исключив затирание сетевых метрик.
6. **PipeClient (Устойчивость UI)**:
   - Добавить в `PipeClient.SendAsync` обработку `IOException` и `ObjectDisposedException` с принудительным вызовом события `Disconnected` для запуска авто-переподключения.

### Этап 2: Ликвидация фризов UI и оптимизация отрисовки 60 FPS (P1 — Высокий приоритет)
1. **AppIconService & FilePathToIconConverter**:
   - Перевести извлечение иконок на фоновый поток с кэшированием `BitmapSource.Freeze()`. В конвертере отдавать мгновенную заглушку.
2. **MainViewModel & Фильтрация**:
   - Заменить пересоздание `ObservableCollection` на дифференциальную in-place синхронизацию элементов списка.
3. **MainWindow.xaml (Виртуализация и скролл)**:
   - Включить `VirtualizingPanel.ScrollUnit="Pixel"` и кэш предзагрузки страниц во всех списках.
   - Заменить `ItemsControl` в черном списке на виртуализированный `ListBox`.
4. **TaskManagerViewModel & WindowHelper**:
   - Перенести `EnumWindows` на фоновый поток и заменить `GetWindowTextLength` на `SendMessageTimeout` с защитой от зависших окон Windows.
5. **MainWindow Lifecycle & Засыпание в трее**:
   - Добавить остановку `_pollingTimer` и `_themePollTimer` при сворачивании в системный трей.
   - Очищать и диспозить дизайнерские экземпляры UserControl при инициализации.

### Этап 3: Оптимизация сетевого мониторинга и памяти (P1/P2 — Средний приоритет)
1. **NetworkConnectionAlertService**:
   - Кэшировать список путей брандмауэра `rulePaths` на 60 секунд, сняв 5–15% нагрузки с CPU.
2. **ProcessNetworkTracker**:
   - Внедрить таймер очистки устаревших PID из `_procNameCache` с интервалом в 1 минуту.
3. **SystemScannerService & WMI**:
   - Обернуть все `ManagementObjectCollection` и задачи планировщика в `using` для освобождения COM RCW дескрипторов.
   - Пробросить `CancellationToken` во все методы сканирования системы.
4. **WindowsFirewallService**:
   - Расширить фильтрацию HRESULT кодов в `IsFirewallDown` (добавить коды деинсталлированной службы, таймаута и отключенного RPC).
5. **AuthenticodeVerifier**:
   - Заменить полный сброс кэша при 2000 записях на скользящее вытеснение 20% устаревших записей (защита от Cache Stampede).

### Этап 4: Полировка вспомогательных модулей (P2/P3 — Плановый рефакторинг)
1. **JsonAuditReportExporter**:
   - Сделать `JsonSerializerOptions` статическим синглтоном.
2. **ToolboxView**:
   - Объединить расчет контрольных сумм SHA256, SHA1 и MD5 в один проход одного потока `FileStream`.
3. **PipeServer IPC**:
   - Задать буфер канала в 64 КБ для уменьшения переключений контекста ядра ОС.
4. **SlidingWindowTracker & ActivityGate**:
   - Добавить периодический TTL-свип старых IP и подключить `ActivityGate` к фоновым циклам службы.

