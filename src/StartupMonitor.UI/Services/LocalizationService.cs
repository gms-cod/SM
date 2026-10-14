using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace StartupMonitor.UI.Services;

/// <summary>Сервис локализации: предоставляет локализованные строки из .resx и оповещает UI об их изменении.</summary>
public class LocalizationService : INotifyPropertyChanged
{
    /// <summary>Событие изменения свойства — вызывается при смене языка для обновления всех привязок.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Менеджер ресурсов для чтения локализованных строк из сборки.</summary>
    private static readonly ResourceManager ResourceManager = new(
        "StartupMonitor.UI.Resources.Strings",
        typeof(LocalizationService).Assembly);

    /// <summary>Синглтон-экземпляр сервиса локализации.</summary>
    public static LocalizationService Instance { get; } = new();

    /// <summary>Индексатор для доступа к строке по ключу через Resources.Strings.</summary>
    public string this[string key] => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    // ──── Статические строки UI, привязанные к ресурсам ────

    /// <summary>Метка настройки автоблокировки.</summary>
    public string AutoBlockLabel => this["AutoBlockLabel"];
    /// <summary>Текст переключателя автоблокировки.</summary>
    public string AutoBlockToggle => this["AutoBlockToggle"];
    /// <summary>Метка настройки тихого режима.</summary>
    public string SilentModeLabel => this["SilentModeLabel"];
    /// <summary>Текст переключателя тихого режима.</summary>
    public string SilentModeToggle => this["SilentModeToggle"];
    /// <summary>Кнопка очистки устаревших записей.</summary>
    public string CleanStaleButton => this["CleanStaleButton"];
    /// <summary>Название пункта навигации «Обзор».</summary>
    public string NavOverview => this["NavOverview"];
    /// <summary>Название пункта навигации «Чёрный список».</summary>
    public string NavBlacklist => this["NavBlacklist"];
    /// <summary>Название пункта навигации «История».</summary>
    public string NavHistory => this["NavHistory"];
    /// <summary>Название пункта навигации «Логи».</summary>
    public string NavLogs => this["NavLogs"];
    /// <summary>Заголовок страницы истории.</summary>
    public string HistoryTitle => this["HistoryTitle"];
    /// <summary>Подзаголовок страницы истории.</summary>
    public string HistorySubtitle => this["HistorySubtitle"];
    /// <summary>Название пункта навигации «Удалённые».</summary>
    public string NavRemoved => this["NavRemoved"];
    /// <summary>Название пункта навигации «Настройки».</summary>
    public string NavSettings => this["NavSettings"];
    /// <summary>Название пункта навигации «Процессы» (Диспетчер задач).</summary>
    public string NavTaskManager => this["NavTaskManager"];
    /// <summary>Заголовок страницы Диспетчера задач.</summary>
    public string TaskManagerTitle => this["TaskManagerTitle"];
    /// <summary>Подзаголовок страницы Диспетчера задач.</summary>
    public string TaskManagerSubtitle => this["TaskManagerSubtitle"];
    /// <summary>Название пункта навигации «Выход».</summary>
    public string NavExit => this["NavExit"];
    /// <summary>Текст фильтра «Недавние».</summary>
    public string FilterRecent => this["FilterRecent"];
    /// <summary>Текст фильтра «Прочие».</summary>
    public string FilterOther => this["FilterOther"];
    /// <summary>Заголовок колонки «Тип».</summary>
    public string ColumnType => this["ColumnType"];
    /// <summary>Текст бейджа «Стабильно».</summary>
    public string BadgeStable => this["BadgeStable"];
    /// <summary>Текст статуса системы «Защищено».</summary>
    public string SystemHealthSecure => this["SystemHealthSecure"];
    /// <summary>Префикс статуса системы.</summary>
    public string SystemHealthPrefix => this["SystemHealthPrefix"];
    /// <summary>Заголовок таблицы «Программы в автозагрузке».</summary>
    public string TableHeaderStartupApps => this["TableHeaderStartupApps"];
    /// <summary>Заголовок колонки «Значок».</summary>
    public string ColumnIcon => this["ColumnIcon"];
    /// <summary>Заголовок колонки «Издатель».</summary>
    public string ColumnPublisher => this["ColumnPublisher"];
    /// <summary>Заголовок колонки «Влияние».</summary>
    public string ColumnBootImpact => this["ColumnBootImpact"];
    /// <summary>Заголовок колонки «Безопасность».</summary>
    public string ColumnSecurity => this["ColumnSecurity"];
    /// <summary>Заголовок колонки «Действия».</summary>
    public string ColumnActions => this["ColumnActions"];
    /// <summary>Кнопка «Подробнее».</summary>
    public string ActionMore => this["ActionMore"];
    /// <summary>Кнопка «Блок».</summary>
    public string ActionBlock => this["ActionBlock"];
    /// <summary>Текст статуса «Вкл».</summary>
    public string StatusOn => this["StatusOn"];
    /// <summary>Текст статуса «Выкл».</summary>
    public string StatusOff => this["StatusOff"];
    /// <summary>Метка настройки «Запускать вместе с Windows».</summary>
    public string StartWithWindowsLabel => this["StartWithWindowsLabel"];
    /// <summary>Текст переключателя «Запускать вместе с Windows».</summary>
    public string StartWithWindowsToggle => this["StartWithWindowsToggle"];
    /// <summary>Заголовок колонки CIRCL.</summary>
    public string ColumnCircl => this["ColumnCircl"];
    /// <summary>Текст статуса «Подключено».</summary>
    public string StatusConnected => this["StatusConnected"];
    /// <summary>Текст статуса «Отключено».</summary>
    public string StatusDisconnected => this["StatusDisconnected"];
    /// <summary>Заголовок страницы удалённых записей.</summary>
    public string RemovedTitle => this["RemovedTitle"];
    /// <summary>Подзаголовок страницы удалённых записей.</summary>
    public string RemovedSubtitle => this["RemovedSubtitle"];
    /// <summary>Текст «Нет удалённых записей».</summary>
    public string RemovedEmpty => this["RemovedEmpty"];
    /// <summary>Кнопка восстановления удалённой записи.</summary>
    public string RemovedRestoreButton => this["RemovedRestoreButton"];
    /// <summary>Подзаголовок главной страницы.</summary>
    public string Subtitle => this["Subtitle"];
    /// <summary>Заголовок страницы логов.</summary>
    public string LogsTitle => this["LogsTitle"];
    /// <summary>Подзаголовок страницы логов.</summary>
    public string LogsSubtitle => this["LogsSubtitle"];
    /// <summary>Плейсхолдер поиска по логам.</summary>
    public string LogSearchPlaceholder => this["LogSearchPlaceholder"];
    /// <summary>Надпись карточки «Всего».</summary>
    public string CardTotal => this["CardTotal"];
    /// <summary>Надпись карточки «Включено».</summary>
    public string CardEnabled => this["CardEnabled"];
    /// <summary>Надпись карточки «Заблокировано».</summary>
    public string CardBlocked => this["CardBlocked"];
    /// <summary>Заголовок секции записей.</summary>
    public string SectionTitle => this["SectionTitle"];
    /// <summary>Плейсхолдер поиска.</summary>
    public string SearchPlaceholder => this["SearchPlaceholder"];
    /// <summary>Тултип кнопки очистки строки поиска.</summary>
    public string ClearSearch => this["ClearSearch"];
    /// <summary>Текст фильтра «Все».</summary>
    public string FilterAll => this["FilterAll"];
    /// <summary>Текст фильтра «Реестр».</summary>
    public string FilterRegistry => this["FilterRegistry"];
    /// <summary>Текст фильтра «Папка».</summary>
    public string FilterFolder => this["FilterFolder"];
    /// <summary>Текст фильтра «Службы».</summary>
    public string FilterServices => this["FilterServices"];
    /// <summary>Текст фильтра «Планировщик».</summary>
    public string FilterTasks => this["FilterTasks"];
    /// <summary>Текст фильтра процессов «Приложения».</summary>
    public string FilterApps => this["FilterApps"];
    /// <summary>Текст фильтра процессов «Фоновые».</summary>
    public string FilterBackground => this["FilterBackground"];
    /// <summary>Текст фильтра процессов «Автозапуск».</summary>
    public string FilterAutostart => this["FilterAutostart"];

    /// <summary>Надпись карточки процессов «Процессы».</summary>
    public string CardProcesses => this["CardProcesses"];

    /// <summary>Заголовок колонки процесса «Имя процесса».</summary>
    public string ColProcessName => this["ColProcessName"];
    /// <summary>Заголовок колонки процесса «PID».</summary>
    public string ColPid => this["ColPid"];
    /// <summary>Заголовок колонки процесса «ЦП».</summary>
    public string ColCpu => this["ColCpu"];
    /// <summary>Заголовок колонки процесса «Память».</summary>
    public string ColRam => this["ColRam"];
    /// <summary>Заголовок колонки процесса «Диск».</summary>
    public string ColDisk => this["ColDisk"];
    /// <summary>Заголовок колонки процесса «Сеть».</summary>
    public string ColNetwork => this["ColNetwork"];
    /// <summary>Заголовок колонки процесса «Издатель».</summary>
    public string ColPublisher => this["ColPublisher"];
    /// <summary>Заголовок колонки процесса «Источник автозапуска» (всегда по-русски).</summary>
    public string ColStartupOrigin => this["ColStartupOrigin"];
    /// <summary>Заголовок колонки процесса «Действия».</summary>
    public string ColActions => this["ColActions"];

    /// <summary>Режим отображения «Список».</summary>
    public string ViewModeList => this["ViewModeList"];
    /// <summary>Режим отображения «Дерево».</summary>
    public string ViewModeTree => this["ViewModeTree"];
    /// <summary>Подсказка режима «Список».</summary>
    public string TooltipViewModeList => this["TooltipViewModeList"];
    /// <summary>Подсказка режима «Дерево».</summary>
    public string TooltipViewModeTree => this["TooltipViewModeTree"];

    /// <summary>Заголовок предупреждения об опасности для системы.</summary>
    public string SystemDangerTitle => this["SystemDangerTitle"];
    /// <summary>Сообщение предупреждения об опасности для системы.</summary>
    public string SystemDangerMessage => this["SystemDangerMessage"];
    /// <summary>Ошибка при попытке заблокировать системный процесс.</summary>
    public string ErrCannotBlockSystemProcess => this["ErrCannotBlockSystemProcess"];

    /// <summary>Действие над процессом «Снять задачу».</summary>
    public string ActionKillProcess => this["ActionKillProcess"];
    /// <summary>Действие над процессом «Приостановить процесс».</summary>
    public string ActionSuspendProcess => this["ActionSuspendProcess"];
    /// <summary>Действие над процессом «Возобновить процесс».</summary>
    public string ActionResumeProcess => this["ActionResumeProcess"];
    /// <summary>Действие над процессом «Заблокировать в ЧС».</summary>
    public string ActionBlockProcess => this["ActionBlockProcess"];
    /// <summary>Действие над процессом «Найти в автозапуске».</summary>
    public string ActionLocateStartup => this["ActionLocateStartup"];
    /// <summary>Действие над процессом «Открыть папку с файлом».</summary>
    public string ActionOpenFileLocation => this["ActionOpenFileLocation"];
    /// <summary>Бейдж «Приостановлен».</summary>
    public string BadgeSuspended => this["BadgeSuspended"];
    /// <summary>Текст подсказки кнопки обновления.</summary>
    public string RefreshTooltip => this["RefreshTooltip"];
    /// <summary>Заголовок колонки «Имя».</summary>
    public string ColumnName => this["ColumnName"];
    /// <summary>Заголовок колонки «Дата».</summary>
    public string ColumnDate => this["ColumnDate"];
    /// <summary>Заголовок колонки «Статус».</summary>
    public string ColumnStatus => this["ColumnStatus"];
    /// <summary>Заголовок колонки «Рейтинг».</summary>
    public string ColumnRating => this["ColumnRating"];
    /// <summary>Заголовок страницы чёрного списка.</summary>
    public string BlacklistTitle => this["BlacklistTitle"];
    /// <summary>Подзаголовок страницы чёрного списка.</summary>
    public string BlacklistSubtitle => this["BlacklistSubtitle"];
    /// <summary>Заголовок секции добавления правила.</summary>
    public string AddRuleTitle => this["AddRuleTitle"];
    /// <summary>Плейсхолдер поля ввода правила.</summary>
    public string RulePlaceholder => this["RulePlaceholder"];
    /// <summary>Кнопка добавления правила.</summary>
    public string AddRuleButton => this["AddRuleButton"];
    /// <summary>Текст подсказки удаления правила.</summary>
    public string DeleteRuleTooltip => this["DeleteRuleTooltip"];
    /// <summary>Заголовок страницы настроек.</summary>
    public string SettingsTitle => this["SettingsTitle"];
    /// <summary>Заголовок секции «Основные».</summary>
    public string GeneralSection => this["GeneralSection"];
    /// <summary>Метка выбора языка.</summary>
    public string LanguageLabel => this["LanguageLabel"];
    /// <summary>Метка выбора темы.</summary>
    public string ThemeLabel => this["ThemeLabel"];
    /// <summary>Метка настройки уведомлений.</summary>
    public string NotificationsLabel => this["NotificationsLabel"];
    /// <summary>Текст переключателя уведомлений.</summary>
    public string NotifyToggle => this["NotifyToggle"];
    /// <summary>Метка настройки трея.</summary>
    public string TrayLabel => this["TrayLabel"];
    /// <summary>Текст пункта трея «Показать/Скрыть».</summary>
    public string TrayShowHide => this["TrayShowHide"];
    /// <summary>Текст пункта трея «Выход».</summary>
    public string TrayExit => this["TrayExit"];
    /// <summary>Текст пункта трея «Режим защиты».</summary>
    public string TrayLockdownMode => this["TrayLockdownMode"];
    /// <summary>Текст переключателя сворачивания в трей.</summary>
    public string MinimizeToTrayToggle => this["MinimizeToTrayToggle"];
    /// <summary>Метка настройки строгого режима.</summary>
    public string StrictModeLabel => this["StrictModeLabel"];
    /// <summary>Текст переключателя строгого режима.</summary>
    public string StrictModeToggle => this["StrictModeToggle"];
    /// <summary>Описание строгого режима.</summary>
    public string StrictModeDescription => this["StrictModeDescription"];
    /// <summary>Метка троттлинга CPU.</summary>
    public string CpuThrottleLabel => this["CpuThrottleLabel"];
    /// <summary>Текст переключателя троттлинга CPU.</summary>
    public string CpuThrottleToggle => this["CpuThrottleToggle"];
    /// <summary>Метка порога троттлинга CPU.</summary>
    public string CpuThresholdLabel => this["CpuThresholdLabel"];
    /// <summary>Описание троттлинга CPU.</summary>
    public string CpuThrottleDescription => this["CpuThrottleDescription"];
    /// <summary>Формат отображения текущей загрузки CPU.</summary>
    public string CurrentCpuUsageFormat => this["CurrentCpuUsageFormat"];
    /// <summary>Текст статуса «Запуск».</summary>
    public string StatusStarting => this["StatusStarting"];
    /// <summary>Текст статуса «Запуск службы».</summary>
    public string StatusStartingService => this["StatusStartingService"];
    /// <summary>Текст статуса «Подключение».</summary>
    public string StatusConnecting => this["StatusConnecting"];
    /// <summary>Текст статуса «Ошибка подключения».</summary>
    public string StatusConnectionFailed => this["StatusConnectionFailed"];
    /// <summary>Текст статуса «Сканирование».</summary>
    public string StatusScanning => this["StatusScanning"];
    /// <summary>Текст статуса «Таймаут сканирования».</summary>
    public string StatusScanTimeout => this["StatusScanTimeout"];
    /// <summary>Текст статуса «Таймаут службы».</summary>
    public string StatusServiceTimeout => this["StatusServiceTimeout"];
    /// <summary>Текст «Служба не найдена».</summary>
    public string StatusServiceNotFound => this["StatusServiceNotFound"];
    /// <summary>Название русского языка в селекторе.</summary>
    public string LangRussian => this["LangRussian"];
    /// <summary>Название английского языка в селекторе.</summary>
    public string LangEnglish => this["LangEnglish"];
    /// <summary>Название тёмной темы.</summary>
    public string ThemeDark => this["ThemeDark"];
    /// <summary>Название светлой темы.</summary>
    public string ThemeLight => this["ThemeLight"];
    /// <summary>Название системной темы.</summary>
    public string ThemeSystem => this["ThemeSystem"];
    /// <summary>Название приложения.</summary>
    public string AppName => this["AppName"];
    /// <summary>Тип совпадения «Путь».</summary>
    public string MatchTypePath => this["MatchTypePath"];
    /// <summary>Тип совпадения «Путь с маской».</summary>
    public string MatchTypePathWildcard => this["MatchTypePathWildcard"];
    /// <summary>Тип совпадения «Издатель».</summary>
    public string MatchTypePublisher => this["MatchTypePublisher"];
    /// <summary>Тип совпадения «SHA256».</summary>
    public string MatchTypeSHA256 => this["MatchTypeSHA256"];
    /// <summary>Действие «Уведомить и удалить».</summary>
    public string ActionNotifyRemove => this["ActionNotifyRemove"];
    /// <summary>Действие «Только уведомить».</summary>
    public string ActionNotifyOnly => this["ActionNotifyOnly"];
    /// <summary>Текст контекстного меню «Включить».</summary>
    public string CtxToggleOn => this["CtxToggleOn"];
    /// <summary>Текст контекстного меню «Отключить».</summary>
    public string CtxToggleOff => this["CtxToggleOff"];
    /// <summary>Текст контекстного меню «Открыть расположение».</summary>
    public string CtxOpenFileLocation => this["CtxOpenFileLocation"];
    /// <summary>Текст контекстного меню «Копировать путь».</summary>
    public string CtxCopyPath => this["CtxCopyPath"];
    /// <summary>Текст контекстного меню «Заблокировать».</summary>
    public string CtxBlock => this["CtxBlock"];
    /// <summary>Текст контекстного меню «Удалить из системы».</summary>
    public string CtxRemoveFromSystem => this["CtxRemoveFromSystem"];
    /// <summary>Заголовок диалога подтверждения удаления.</summary>
    public string RemoveConfirmTitle => this["RemoveConfirmTitle"];
    /// <summary>Текст сообщения подтверждения удаления.</summary>
    public string RemoveConfirmMessage => this["RemoveConfirmMessage"];
    /// <summary>Текст кнопки «Удалить с блокировкой».</summary>
    public string RemoveWithBlock => this["RemoveWithBlock"];
    /// <summary>Текст кнопки «Удалить без блокировки».</summary>
    public string RemoveWithoutBlock => this["RemoveWithoutBlock"];
    /// <summary>Заголовок уведомления.</summary>
    public string NotifTitle => this["NotifTitle"];
    /// <summary>Локализованное название расположения «Реестр Run».</summary>
    public string LocRegistryRun => this["LocRegistryRun"];
    /// <summary>Локализованное название расположения «Реестр RunOnce».</summary>
    public string LocRegistryRunOnce => this["LocRegistryRunOnce"];
    /// <summary>Локализованное название расположения «Реестр RunServices».</summary>
    public string LocRegistryRunServices => this["LocRegistryRunServices"];
    /// <summary>Локализованное название расположения «Реестр Policies».</summary>
    public string LocRegistryPolicies => this["LocRegistryPolicies"];
    /// <summary>Локализованное название расположения «Папка автозапуска».</summary>
    public string LocStartupFolder => this["LocStartupFolder"];
    /// <summary>Локализованное название расположения «Служба».</summary>
    public string LocService => this["LocService"];
    /// <summary>Локализованное название расположения «Драйвер».</summary>
    public string LocDriver => this["LocDriver"];
    /// <summary>Локализованное название расположения «Планировщик задач».</summary>
    public string LocTaskScheduler => this["LocTaskScheduler"];
    /// <summary>Локализованное название расположения «Winlogon».</summary>
    public string LocWinlogon => this["LocWinlogon"];
    /// <summary>Локализованное название расположения «BootExecute».</summary>
    public string LocBootExecute => this["LocBootExecute"];
    /// <summary>Локализованное название расположения «AppInit».</summary>
    public string LocAppInit => this["LocAppInit"];
    /// <summary>Локализованное название расположения «Обработчик контекстного меню».</summary>
    public string LocContextMenuHandler => this["LocContextMenuHandler"];
    /// <summary>Локализованное название расположения «BHO».</summary>
    public string LocBHO => this["LocBHO"];
    /// <summary>Локализованное название расположения «IFEO».</summary>
    public string LocIFEO => this["LocIFEO"];
    /// <summary>Локализованное название расположения «GPO Startup».</summary>
    public string LocGpoStartup => this["LocGpoStartup"];
    /// <summary>Локализованное название расположения «GPO Logon».</summary>
    public string LocGpoLogon => this["LocGpoLogon"];
    /// <summary>Локализованное название расположения «WMI».</summary>
    public string LocWmi => this["LocWmi"];
    /// <summary>Локализованное название расположения «COM-объект».</summary>
    public string LocComObject => this["LocComObject"];
    /// <summary>Локализованное название расположения «Плагин браузера».</summary>
    public string LocBrowserPlugin => this["LocBrowserPlugin"];
    /// <summary>Локализованное название расположения «Сетевой провайдер».</summary>
    public string LocNetworkProvider => this["LocNetworkProvider"];
    /// <summary>Локализованное название расположения «Монитор печати».</summary>
    public string LocPrintMonitor => this["LocPrintMonitor"];
    /// <summary>Локализованное название расположения «LSA».</summary>
    public string LocLsa => this["LocLsa"];
    /// <summary>Локализованное название расположения «Протокол».</summary>
    public string LocProtocol => this["LocProtocol"];
    /// <summary>Локализованное название расположения «AppPaths».</summary>
    public string LocAppPaths => this["LocAppPaths"];
    /// <summary>Локализованное название расположения «Клавиатурный хук».</summary>
    public string LocKeyboardHook => this["LocKeyboardHook"];
    /// <summary>Локализованное название расположения «AppX StartupTask».</summary>
    public string LocAppxStartupTask => this["LocAppxStartupTask"];
    /// <summary>Локализованное название расположения «Active Setup».</summary>
    public string LocActiveSetup => this["LocActiveSetup"];
    /// <summary>Локализованное название расположения «Кодеки (Drivers32)».</summary>
    public string LocDrivers32 => this["LocDrivers32"];
    /// <summary>Локализованное название расположения «Shell-расширение».</summary>
    public string LocShellExtension => this["LocShellExtension"];
    /// <summary>Локализованное название метки «Расположение».</summary>
    public string LocLocationLabel => this["LocLocationLabel"];
    /// <summary>Локализованное название расположения «Неизвестно».</summary>
    public string LocUnknown => this["LocUnknown"];
    /// <summary>Краткое название статуса «Вкл».</summary>
    public string StatusEnabledShort => this["StatusEnabledShort"];
    /// <summary>Краткое название статуса «Выкл».</summary>
    public string StatusDisabledShort => this["StatusDisabledShort"];
    /// <summary>Краткое название статуса «Заблокировано».</summary>
    public string StatusBlockedShort => this["StatusBlockedShort"];
    /// <summary>Краткое название статуса «Отложен».</summary>
    public string StatusDelayedShort => this["StatusDelayedShort"];
    /// <summary>Пункт меню «Отложить запуск при входе».</summary>
    public string CtxDelayedStartup => this["CtxDelayedStartup"];
    /// <summary>Пункт меню «Отменить задержку (Включить сразу)».</summary>
    public string CtxCancelDelay => this["CtxCancelDelay"];
    /// <summary>Пункт меню «Запустить сейчас (проверка)».</summary>
    public string CtxLaunchNow => this["CtxLaunchNow"];
    /// <summary>Пункт меню «Изменить задержку».</summary>
    public string CtxChangeDelay => this["CtxChangeDelay"];
    /// <summary>Сокращение «с» (секунды).</summary>
    public string SecondsShort => this["SecondsShort"];
    /// <summary>Текст действия «Включить».</summary>
    public string ToggleEnable => this["ToggleEnable"];
    /// <summary>Текст действия «Отключить».</summary>
    public string ToggleDisable => this["ToggleDisable"];
    /// <summary>Текст действия «Разблокировать».</summary>
    public string ToggleUnblock => this["ToggleUnblock"];

    /// <summary>Заголовок секции AI.</summary>
    public string AiSection => this["AiSection"];

    /// <summary>Метка настройки AI.</summary>
    public string AiEnableLabel => this["AiEnableLabel"];
    /// <summary>Текст переключателя AI.</summary>
    public string AiEnableToggle => this["AiEnableToggle"];
    /// <summary>Описание AI.</summary>
    public string AiEnableDescription => this["AiEnableDescription"];
    /// <summary>Метка поля API-ключа AI.</summary>
    public string AiApiKeyLabel => this["AiApiKeyLabel"];
    /// <summary>Описание поля API-ключа AI.</summary>
    public string AiApiKeyDescription => this["AiApiKeyDescription"];
    /// <summary>Метка поля API URL.</summary>
    public string AiApiUrlLabel => this["AiApiUrlLabel"];
    /// <summary>Описание поля API URL.</summary>
    public string AiApiUrlDescription => this["AiApiUrlDescription"];
    /// <summary>Метка поля модели AI.</summary>
    public string AiModelLabel => this["AiModelLabel"];
    /// <summary>Описание поля модели AI.</summary>
    public string AiModelDescription => this["AiModelDescription"];
    /// <summary>Текст кнопки тестирования AI.</summary>
    public string AiTestConnectionButton => this["AiTestConnectionButton"];
    /// <summary>Текст статуса тестирования AI.</summary>
    public string AiTestingStatus => this["AiTestingStatus"];

    /// <summary>Метка настройки «Завершение процессов».</summary>
    public string KillOnDisableLabel => this["KillOnDisableLabel"];
    /// <summary>Текст переключателя «Убивать процесс при отключении».</summary>
    public string KillOnDisableToggle => this["KillOnDisableToggle"];
    /// <summary>Описание настройки «Завершение процессов».</summary>
    public string KillOnDisableDescription => this["KillOnDisableDescription"];

    /// <summary>Текст «Клиент подключён».</summary>
    public string PipeClientConnected => this["PipeClientConnected"];
    /// <summary>Текст «Клиент отключён».</summary>
    public string PipeClientDisconnected => this["PipeClientDisconnected"];
    /// <summary>Текст ошибки клиента.</summary>
    public string PipeClientError => this["PipeClientError"];
    /// <summary>Текст ошибки сервера.</summary>
    public string PipeServerError => this["PipeServerError"];
    /// <summary>Текст «Сервер запущен».</summary>
    public string PipeServerStarted => this["PipeServerStarted"];
    /// <summary>Текст «Сервер остановлен».</summary>
    public string PipeServerStopped => this["PipeServerStopped"];
    /// <summary>Текст этапа сканирования реестра.</summary>
    public string ScanRegistry => this["ScanRegistry"];
    /// <summary>Текст этапа сканирования служб.</summary>
    public string ScanServices => this["ScanServices"];
    /// <summary>Текст этапа сканирования папки автозапуска.</summary>
    public string ScanStartupFolder => this["ScanStartupFolder"];
    /// <summary>Текст этапа сканирования планировщика.</summary>
    public string ScanTaskScheduler => this["ScanTaskScheduler"];
    /// <summary>Текст таймаута сканирования.</summary>
    public string ScanTimeoutMessage => this["ScanTimeoutMessage"];
    /// <summary>Текст «Служба не найдена».</summary>
    public string ServiceNotFound => this["ServiceNotFound"];
    /// <summary>Текст «Служба не запущена».</summary>
    public string ServiceNotStarted => this["ServiceNotStarted"];
    /// <summary>Текст «Ошибка регистрации службы».</summary>
    public string ServiceRegistrationFailed => this["ServiceRegistrationFailed"];
    /// <summary>Текст «Ошибка sc create».</summary>
    public string ServiceScCreateFailed => this["ServiceScCreateFailed"];
    /// <summary>Текст «Ошибка запуска службы».</summary>
    public string ServiceStartFailed => this["ServiceStartFailed"];
    /// <summary>Текст аварийного завершения планировщика.</summary>
    public string TaskSchedulerCrash => this["TaskSchedulerCrash"];
    /// <summary>Текст ошибки планировщика.</summary>
    public string TaskSchedulerError => this["TaskSchedulerError"];
    /// <summary>Текст «Неизвестный тип сообщения».</summary>
    public string UnknownMessageType => this["UnknownMessageType"];

    /// <summary>Возвращает локализованную строку с количеством загруженных записей.</summary>
    public string GetStatusLoaded(int count) =>
        string.Format(CultureInfo.CurrentUICulture, this["StatusLoaded"], count);

    /// <summary>Возвращает локализованную строку ошибки с указанным сообщением.</summary>
    public string GetStatusError(string message) =>
        string.Format(CultureInfo.CurrentUICulture, this["StatusError"], message);

    /// <summary>Возвращает локализованную строку «Служба не найдена» с указанным путём.</summary>
    public string GetServiceNotFound(string path) =>
        string.Format(CultureInfo.CurrentUICulture, this["ServiceNotFound"], path);

    /// <summary>Возвращает локализованный текст уведомления с именем и расположением записи.</summary>
    public string GetNotifText(string name, string location) =>
        string.Format(CultureInfo.CurrentUICulture, this["NotifText"], name, location);

    /// <summary>Заголовок предупреждения об авто-блокировке записи из чёрного списка.</summary>
    public string BlacklistNotifTitle => this["BlacklistNotifTitle"];

    /// <summary>Текст предупреждения об авто-блокировке записи из чёрного списка.</summary>
    public string GetBlacklistNotifText(string name) =>
        string.Format(CultureInfo.CurrentUICulture, this["BlacklistNotifText"], name);

    /// <summary>Заголовок уведомления о выполненных действиях над записями.</summary>
    public string ActionNotifTitle => this["ActionNotifTitle"];

    /// <summary>Возвращает локализованный текст уведомления о действии по коду действия.</summary>
    public string GetActionNotifText(string action, string name) =>
        string.Format(CultureInfo.CurrentUICulture, this[action switch
        {
            "killed" => "ActionKilled",
            "disabled" => "ActionDisabled",
            "disabledKilled" => "ActionDisabledKilled",
            "disabledKillSkipped" => "ActionDisabledKillSkipped",
            "autoDisabled" => "ActionAutoDisabled",
            "autoDisabledKilled" => "ActionAutoDisabledKilled",
            "autoDisabledKillSkipped" => "ActionAutoDisabledKillSkipped",
            "removed" => "ActionRemoved",
            "restored" => "ActionRestored",
            "removedAgain" => "ActionRemovedAgain",
            "lockdownBlocked" => "ActionLockdownBlocked",
            _ when action.StartsWith("policyBlocked") => "ActionPolicyBlocked",
            _ => "ActionPerformed"
        }], name);

    /// <summary>Текст «Проверка состояния службы...».</summary>
    public string StatusCheckingService => this["StatusCheckingService"];
    /// <summary>Текст «Регистрация службы в автозапуске Windows...».</summary>
    public string StatusRegisteringService => this["StatusRegisteringService"];
    /// <summary>Текст «Настройка автоматического запуска службы...».</summary>
    public string StatusConfiguringAutoStart => this["StatusConfiguringAutoStart"];
    /// <summary>Текст «Обновление пути к службе в системе...».</summary>
    public string StatusUpdatingServicePath => this["StatusUpdatingServicePath"];
    /// <summary>Текст «Запуск фоновой службы StartupMonitor...».</summary>
    public string StatusStartingServiceProcess => this["StatusStartingServiceProcess"];
    /// <summary>Текст «Служба готова. Загрузка записей автозапуска...».</summary>
    public string StatusLoadingEntries => this["StatusLoadingEntries"];
    /// <summary>Текст кнопки «Повторить».</summary>
    public string BtnRetry => this["BtnRetry"];
    /// <summary>Текст кнопки «Выход».</summary>
    public string BtnExit => this["BtnExit"];
    /// <summary>Текст отмены UAC.</summary>
    public string ServiceUacCanceled => this["ServiceUacCanceled"];
    /// <summary>Текст кнопки «Экспорт отчета».</summary>
    public string BtnExportReport => this["BtnExportReport"];
    /// <summary>Тултип кнопки «Экспорт отчета».</summary>
    public string TooltipExportReport => this["TooltipExportReport"];
    /// <summary>Заголовок диалога экспорта отчета.</summary>
    public string ExportReportDialogTitle => this["ExportReportDialogTitle"];
    /// <summary>Фильтр диалога экспорта отчета.</summary>
    public string ExportReportFilter => this["ExportReportFilter"];
    /// <summary>Сообщение успешного экспорта отчета.</summary>
    public string ExportReportSuccess => this["ExportReportSuccess"];
    /// <summary>Сообщение ошибки экспорта отчета.</summary>
    public string ExportReportError => this["ExportReportError"];

    /// <summary>Заголовок секции удаленных уведомлений.</summary>
    public string RemoteNotificationsSection => this["RemoteNotificationsSection"];
    /// <summary>Описание секции удаленных уведомлений.</summary>
    public string RemoteNotificationsDescription => this["RemoteNotificationsDescription"];
    /// <summary>Заголовок секции Telegram.</summary>
    public string TelegramSection => this["TelegramSection"];
    /// <summary>Текст переключателя Telegram.</summary>
    public string TelegramEnableToggle => this["TelegramEnableToggle"];
    /// <summary>Метка токена Telegram бота.</summary>
    public string TelegramBotTokenLabel => this["TelegramBotTokenLabel"];
    /// <summary>Описание поля токена Telegram бота.</summary>
    public string TelegramBotTokenDescription => this["TelegramBotTokenDescription"];
    /// <summary>Метка Chat ID Telegram.</summary>
    public string TelegramChatIdLabel => this["TelegramChatIdLabel"];
    /// <summary>Описание поля Chat ID Telegram.</summary>
    public string TelegramChatIdDescription => this["TelegramChatIdDescription"];
    /// <summary>Кнопка тестирования Telegram.</summary>
    public string TestTelegramButton => this["TestTelegramButton"];
    /// <summary>Заголовок секции Discord.</summary>
    public string DiscordSection => this["DiscordSection"];
    /// <summary>Текст переключателя Discord.</summary>
    public string DiscordEnableToggle => this["DiscordEnableToggle"];
    /// <summary>Метка Webhook URL Discord.</summary>
    public string DiscordWebhookUrlLabel => this["DiscordWebhookUrlLabel"];
    /// <summary>Описание поля Webhook URL Discord.</summary>
    public string DiscordWebhookUrlDescription => this["DiscordWebhookUrlDescription"];
    /// <summary>Кнопка тестирования Discord.</summary>
    public string TestDiscordButton => this["TestDiscordButton"];
    /// <summary>Заголовок секции событий для уведомлений.</summary>
    public string RemoteEventsSection => this["RemoteEventsSection"];
    /// <summary>Текст переключателя новых записей.</summary>
    public string RemoteNotifyNewEntryToggle => this["RemoteNotifyNewEntryToggle"];
    /// <summary>Текст переключателя подозрительных команд.</summary>
    public string RemoteNotifyThreatToggle => this["RemoteNotifyThreatToggle"];
    /// <summary>Текст переключателя блокировок.</summary>
    public string RemoteNotifyBlockedToggle => this["RemoteNotifyBlockedToggle"];
    /// <summary>Статус выполнения проверки удалённого канала.</summary>
    public string TestingRemoteNotificationStatus => this["TestingRemoteNotificationStatus"];

    /// <summary>Заголовок окна снимков системы.</summary>
    public string SnapshotsTitle => this["SnapshotsTitle"];
    /// <summary>Подзаголовок окна снимков системы.</summary>
    public string SnapshotsSubtitle => this["SnapshotsSubtitle"];
    /// <summary>Кнопка снимков системы.</summary>
    public string SnapshotsBtn => this["SnapshotsBtn"];
    /// <summary>Кнопка создания снимка.</summary>
    public string CreateSnapshotBtn => this["CreateSnapshotBtn"];
    /// <summary>Метка названия снимка.</summary>
    public string SnapshotNamePrompt => this["SnapshotNamePrompt"];
    /// <summary>Метка описания снимка.</summary>
    public string SnapshotDescPrompt => this["SnapshotDescPrompt"];
    /// <summary>Успешное создание снимка.</summary>
    public string SnapshotCreatedSuccess => this["SnapshotCreatedSuccess"];
    /// <summary>Кнопка сравнения со снимком.</summary>
    public string CompareSnapshotBtn => this["CompareSnapshotBtn"];
    /// <summary>Кнопка удаления снимка.</summary>
    public string DeleteSnapshotBtn => this["DeleteSnapshotBtn"];
    /// <summary>Подтверждение удаления снимка.</summary>
    public string SnapshotDeleteConfirm => this["SnapshotDeleteConfirm"];
    /// <summary>Новые записи со снимка.</summary>
    public string SnapshotDiffAdded => this["SnapshotDiffAdded"];
    /// <summary>Удаленные записи со снимка.</summary>
    public string SnapshotDiffRemoved => this["SnapshotDiffRemoved"];
    /// <summary>Измененные записи со снимка.</summary>
    public string SnapshotDiffModified => this["SnapshotDiffModified"];
    /// <summary>Записи без изменений со снимка.</summary>
    public string SnapshotDiffUnchanged => this["SnapshotDiffUnchanged"];
    /// <summary>Кнопка применения фильтра снимка.</summary>
    public string SnapshotApplyFilter => this["SnapshotApplyFilter"];
    /// <summary>Кнопка сброса фильтра снимка.</summary>
    public string SnapshotResetFilter => this["SnapshotResetFilter"];
    /// <summary>Текст отсутствия снимков.</summary>
    public string SnapshotNoSnapshots => this["SnapshotNoSnapshots"];
    /// <summary>Бейдж новой записи со снимка.</summary>
    public string SnapshotBadgeNew => this["SnapshotBadgeNew"];
    /// <summary>Бейдж измененной записи со снимка.</summary>
    public string SnapshotBadgeModified => this["SnapshotBadgeModified"];

    // ──── Boot Time Tracer ────
    /// <summary>Название пункта навигации «Загрузка» (Boot Time).</summary>
    public string NavBootTime => this["NavBootTime"];
    /// <summary>Заголовок страницы анализатора времени загрузки.</summary>
    public string BootTimeTitle => this["BootTimeTitle"];
    /// <summary>Подзаголовок страницы анализатора времени загрузки.</summary>
    public string BootTimeSubtitle => this["BootTimeSubtitle"];
    /// <summary>Кнопка обновления данных загрузки.</summary>
    public string BootTimeRefresh => this["BootTimeRefresh"];
    /// <summary>Текст при отсутствии данных журнала диагностики.</summary>
    public string BootTimeNoData => this["BootTimeNoData"];
    /// <summary>Карточка «Общее время» загрузки.</summary>
    public string BootTimeOverall => this["BootTimeOverall"];
    /// <summary>Карточка «До рабочего стола».</summary>
    public string BootTimeToDesktop => this["BootTimeToDesktop"];
    /// <summary>Карточка «Фоновая фаза».</summary>
    public string BootTimePostBoot => this["BootTimePostBoot"];
    /// <summary>Карточка «Приложений в старте».</summary>
    public string BootTimeAppsCount => this["BootTimeAppsCount"];
    /// <summary>Заголовок таблицы виновников замедления.</summary>
    public string BootTimeOffendersTitle => this["BootTimeOffendersTitle"];
    /// <summary>Заголовок таблицы истории загрузок.</summary>
    public string BootTimeHistoryTitle => this["BootTimeHistoryTitle"];
    /// <summary>Колонка «Категория» таблицы виновников.</summary>
    public string BootTimeColCategory => this["BootTimeColCategory"];
    /// <summary>Колонка «Программа / Путь» таблицы виновников.</summary>
    public string BootTimeColName => this["BootTimeColName"];
    /// <summary>Колонка «Издатель» таблицы виновников.</summary>
    public string BootTimeColPublisher => this["BootTimeColPublisher"];
    /// <summary>Колонка «Время работы» таблицы виновников.</summary>
    public string BootTimeColTotalTime => this["BootTimeColTotalTime"];
    /// <summary>Колонка «Замедление» таблицы виновников.</summary>
    public string BootTimeColDelay => this["BootTimeColDelay"];
    /// <summary>Колонка «Дата / Время» истории загрузок.</summary>
    public string BootTimeHistColDate => this["BootTimeHistColDate"];
    /// <summary>Колонка «Общее время» истории загрузок.</summary>
    public string BootTimeHistColTotal => this["BootTimeHistColTotal"];
    /// <summary>Колонка «До рабочего стола» истории загрузок.</summary>
    public string BootTimeHistColDesktop => this["BootTimeHistColDesktop"];
    /// <summary>Колонка «Фоновая фаза» истории загрузок.</summary>
    public string BootTimeHistColPost => this["BootTimeHistColPost"];
    /// <summary>Колонка «Приложений» истории загрузок.</summary>
    public string BootTimeHistColApps => this["BootTimeHistColApps"];
    /// <summary>Колонка «Оценка» истории загрузок.</summary>
    public string BootTimeHistColRating => this["BootTimeHistColRating"];

    public string DetailMainInfo => this["DetailMainInfo"];
    public string DetailPath => this["DetailPath"];
    public string DetailCommand => this["DetailCommand"];
    public string DetailDetected => this["DetailDetected"];
    public string DetailDelay => this["DetailDelay"];
    public string DetailHash => this["DetailHash"];
    public string DetailRefreshAi => this["DetailRefreshAi"];
    public string DetailDelay30s => this["DetailDelay30s"];

    public string PublisherUnverified => this["PublisherUnverified"];
    public string ImpactLow => this["ImpactLow"];
    public string ImpactMedium => this["ImpactMedium"];
    public string ImpactHigh => this["ImpactHigh"];

    public string SnapSavedSnapshots => this["SnapSavedSnapshots"];
    public string SnapColName => this["SnapColName"];
    public string SnapColDate => this["SnapColDate"];
    public string SnapColEntries => this["SnapColEntries"];
    public string SnapColMachine => this["SnapColMachine"];
    public string SnapColDesc => this["SnapColDesc"];
    public string SnapBtnCompare => this["SnapBtnCompare"];
    public string SnapCompareTooltip => this["SnapCompareTooltip"];
    public string SnapDeleteTooltip => this["SnapDeleteTooltip"];
    public string SnapResultsHeader => this["SnapResultsHeader"];
    public string SnapColLocation => this["SnapColLocation"];
    public string SnapColCommandLine => this["SnapColCommandLine"];
    public string SnapColChangedParams => this["SnapColChangedParams"];
    public string SnapColCurrentState => this["SnapColCurrentState"];
    public string SnapColBaselineState => this["SnapColBaselineState"];
    public string SnapBtnClose => this["SnapBtnClose"];

    /// <summary>Формирует строку «Быстрее на X» при сравнении с предыдущей загрузкой.</summary>
    public string GetBootTimeFaster(string delta) =>
        string.Format(CultureInfo.CurrentUICulture, this["BootTimeFaster"], delta);

    /// <summary>Формирует строку «Медленнее на X» при сравнении с предыдущей загрузкой.</summary>
    public string GetBootTimeSlower(string delta) =>
        string.Format(CultureInfo.CurrentUICulture, this["BootTimeSlower"], delta);


    /// <summary>Формирует заголовок окна диффа со снимком.</summary>
    public string GetSnapshotDiffTitle(string snapshotName) =>
        string.Format(CultureInfo.CurrentUICulture, this["SnapshotDiffTitle"], snapshotName);

    /// <summary>Формирует строку активного сравнения со снимком.</summary>
    public string GetSnapshotActiveComparison(string name, int added, int modified) =>
        string.Format(CultureInfo.CurrentUICulture, this["SnapshotActiveComparison"], name, added, modified);

    /// <summary>Формирует строку ожидания готовности службы.</summary>
    public string GetStatusWaitingServiceReady(int seconds) =>
        string.Format(CultureInfo.CurrentUICulture, this["StatusWaitingServiceReady"], seconds);

    /// <summary>Формирует строку попытки подключения к службе.</summary>
    public string GetStatusConnectingAttempt(int attempt, int total) =>
        string.Format(CultureInfo.CurrentUICulture, this["StatusConnectingAttempt"], attempt, total);

    /// <summary>Пункт меню «Сетевые соединения...»</summary>
    public string ActionProcessConnections => this["ActionProcessConnections"];
    /// <summary>Заголовок «Сетевые соединения процесса»</summary>
    public string ProcessConnectionsTitle => this["ProcessConnectionsTitle"];

    /// <summary>Заголовок секции Authenticode в настройках.</summary>
    public string AuthenticodeSection => this["AuthenticodeSection"];
    /// <summary>Описание секции Authenticode.</summary>
    public string AuthenticodeDescription => this["AuthenticodeDescription"];
    /// <summary>Статус движка Authenticode.</summary>
    public string AuthenticodeEngineStatus => this["AuthenticodeEngineStatus"];
    /// <summary>Примечание о кэшировании Authenticode.</summary>
    public string AuthenticodeCacheNotice => this["AuthenticodeCacheNotice"];
    /// <summary>Заголовок легенды индикаторов Authenticode.</summary>
    public string AuthenticodeLegendTitle => this["AuthenticodeLegendTitle"];
    /// <summary>Легенда действительной подписи.</summary>
    public string AuthenticodeLegendValid => this["AuthenticodeLegendValid"];
    /// <summary>Легенда модифицированного файла.</summary>
    public string AuthenticodeLegendTampered => this["AuthenticodeLegendTampered"];
    /// <summary>Легенда истекшего/недоверенного сертификата.</summary>
    public string AuthenticodeLegendUntrusted => this["AuthenticodeLegendUntrusted"];
    /// <summary>Легенда файла без подписи.</summary>
    public string AuthenticodeLegendUnsigned => this["AuthenticodeLegendUnsigned"];
    /// <summary>Интеграция с ИИ.</summary>
    public string AuthenticodeAiIntegration => this["AuthenticodeAiIntegration"];

    /// <summary>Заголовок секции Kaspersky в настройках.</summary>
    public string KasperskySection => this["KasperskySection"];
    /// <summary>Описание секции Kaspersky.</summary>
    public string KasperskyDescription => this["KasperskyDescription"];
    /// <summary>Метка включения Kaspersky.</summary>
    public string KasperskyEnabledLabel => this["KasperskyEnabledLabel"];
    /// <summary>Тумблер включения Kaspersky.</summary>
    public string KasperskyEnabledToggle => this["KasperskyEnabledToggle"];
    /// <summary>Метка API-токена Kaspersky.</summary>
    public string KasperskyApiKeyLabel => this["KasperskyApiKeyLabel"];
    /// <summary>Описание API-токена Kaspersky.</summary>
    public string KasperskyApiKeyDescription => this["KasperskyApiKeyDescription"];
    /// <summary>Метка URL API Kaspersky.</summary>
    public string KasperskyApiUrlLabel => this["KasperskyApiUrlLabel"];
    /// <summary>Описание URL API Kaspersky.</summary>
    public string KasperskyApiUrlDescription => this["KasperskyApiUrlDescription"];
    /// <summary>Кнопка тестирования Kaspersky.</summary>
    public string KasperskyTestConnectionButton => this["KasperskyTestConnectionButton"];
    /// <summary>Статус тестирования Kaspersky.</summary>
    public string KasperskyTestingStatus => this["KasperskyTestingStatus"];
    /// <summary>Пункт меню проверки в Kaspersky.</summary>
    public string CtxScanKaspersky => this["CtxScanKaspersky"];
    /// <summary>Заголовок завершения сканирования в Kaspersky.</summary>
    public string KasperskyScanCompletedTitle => this["KasperskyScanCompletedTitle"];
    /// <summary>Сообщение завершения сканирования в Kaspersky.</summary>
    public string KasperskyScanCompletedMsg => this["KasperskyScanCompletedMsg"];
    /// <summary>Сообщение ошибки сканирования в Kaspersky.</summary>
    public string KasperskyScanFailedMsg => this["KasperskyScanFailedMsg"];
    /// <summary>Кнопка проверки в Kaspersky.</summary>
    public string BtnCheckKaspersky => this["BtnCheckKaspersky"];

    /// <summary>Секция автоматических политик защиты (Auto-Guards).</summary>
    public string AutoGuardsSection => this["AutoGuardsSection"];
    public string AutoGuardsDescription => this["AutoGuardsDescription"];
    public string AutoGuardsModeLabel => this["AutoGuardsModeLabel"];
    public string AutoGuardsModeAudit => this["AutoGuardsModeAudit"];
    public string AutoGuardsModeEnforce => this["AutoGuardsModeEnforce"];
    public string AutoGuardsRulesLabel => this["AutoGuardsRulesLabel"];
    public string GuardDangerousCliLabel => this["GuardDangerousCliLabel"];
    public string GuardDangerousCliToggle => this["GuardDangerousCliToggle"];
    public string GuardDangerousCliDescription => this["GuardDangerousCliDescription"];
    public string GuardTempPathLabel => this["GuardTempPathLabel"];
    public string GuardTempPathToggle => this["GuardTempPathToggle"];
    public string GuardTempPathDescription => this["GuardTempPathDescription"];
    public string GuardScriptsLabel => this["GuardScriptsLabel"];
    public string GuardScriptsToggle => this["GuardScriptsToggle"];
    public string GuardScriptsDescription => this["GuardScriptsDescription"];
    public string GuardUnsignedLabel => this["GuardUnsignedLabel"];
    public string GuardUnsignedToggle => this["GuardUnsignedToggle"];
    public string GuardUnsignedDescription => this["GuardUnsignedDescription"];
    public string GuardNewServicesLabel => this["GuardNewServicesLabel"];
    public string GuardNewServicesToggle => this["GuardNewServicesToggle"];
    public string GuardNewServicesDescription => this["GuardNewServicesDescription"];
    public string GuardSystemProtectedNotice => this["GuardSystemProtectedNotice"];
    public string ActionPolicyBlocked => this["ActionPolicyBlocked"];

    public string ProcessModulesTitle => this["ProcessModulesTitle"];
    public string PmTabModules => this["PmTabModules"];
    public string PmTabHandles => this["PmTabHandles"];
    public string PmTotalModules => this["PmTotalModules"];
    public string PmSignedModules => this["PmSignedModules"];
    public string PmUnsignedOrTemp => this["PmUnsignedOrTemp"];
    public string PmTotalHandles => this["PmTotalHandles"];
    public string PmSearchPlaceholder => this["PmSearchPlaceholder"];
    public string PmColModuleName => this["PmColModuleName"];
    public string PmColBaseAddress => this["PmColBaseAddress"];
    public string PmColSize => this["PmColSize"];
    public string PmColSignature => this["PmColSignature"];
    public string PmColPublisher => this["PmColPublisher"];
    public string PmColPath => this["PmColPath"];
    public string PmColDescription => this["PmColDescription"];
    public string PmColHandleType => this["PmColHandleType"];
    public string PmColHandleValue => this["PmColHandleValue"];
    public string PmColHandleName => this["PmColHandleName"];
    public string PmCtxOpenLocation => this["PmCtxOpenLocation"];
    public string PmCtxCopyPath => this["PmCtxCopyPath"];
    public string PmCtxCopyName => this["PmCtxCopyName"];
    public string PmCtxCheckKaspersky => this["PmCtxCheckKaspersky"];
    public string PmCtxCheckVirusTotal => this["PmCtxCheckVirusTotal"];
    public string PmEmptyModules => this["PmEmptyModules"];
    public string PmEmptyHandles => this["PmEmptyHandles"];
    public string ActionProcessModules => this["ActionProcessModules"];
    public string SysInfoBtn => this["SysInfoBtn"];
    public string SysInfoTooltip => this["SysInfoTooltip"];
    public string SysInfoTitle => this["SysInfoTitle"];
    public string SysInfoSubtitle => this["SysInfoSubtitle"];
    public string SysInfoCardHardware => this["SysInfoCardHardware"];
    public string SysInfoCardOs => this["SysInfoCardOs"];
    public string SysInfoCardDisks => this["SysInfoCardDisks"];
    public string SysInfoCpu => this["SysInfoCpu"];
    public string SysInfoCores => this["SysInfoCores"];
    public string SysInfoRam => this["SysInfoRam"];
    public string SysInfoOsName => this["SysInfoOsName"];
    public string SysInfoBuild => this["SysInfoBuild"];
    public string SysInfoMachine => this["SysInfoMachine"];
    public string SysInfoUser => this["SysInfoUser"];
    public string SysInfoUptime => this["SysInfoUptime"];
    public string SysInfoCopyBtn => this["SysInfoCopyBtn"];
    public string SysInfoCopiedToast => this["SysInfoCopiedToast"];
    public string SysInfoRefreshBtn => this["SysInfoRefreshBtn"];

    public string UpdateSectionTitle => this["UpdateSectionTitle"];
    public string UpdateSectionDescription => this["UpdateSectionDescription"];
    public string AutoCheckUpdatesLabel => this["AutoCheckUpdatesLabel"];
    public string AutoCheckUpdatesToggle => this["AutoCheckUpdatesToggle"];
    public string UpdateCheckActionLabel => this["UpdateCheckActionLabel"];
    public string CheckUpdatesButton => this["CheckUpdatesButton"];
    public string UpdateInfoLabel => this["UpdateInfoLabel"];
    public string CheckingForUpdatesText => this["CheckingForUpdatesText"];
    public string UpdateLatestVersionInstalledText => this["UpdateLatestVersionInstalledText"];
    public string UpdateCheckFailedText => this["UpdateCheckFailedText"];
    public string UpdateAvailableFormat => this["UpdateAvailableFormat"];
    public string UpdateDialogTitle => this["UpdateDialogTitle"];
    public string UpdateDialogSubtitle => this["UpdateDialogSubtitle"];
    public string UpdateNowButton => this["UpdateNowButton"];
    public string RemindLaterButton => this["RemindLaterButton"];
    public string SkipVersionButton => this["SkipVersionButton"];
    public string InstallAndRestartButton => this["InstallAndRestartButton"];
    public string DownloadProgressStatusFormat => this["DownloadProgressStatusFormat"];
    public string UpdateChangelogLabel => this["UpdateChangelogLabel"];

    // ──── Firewall / Port Defender ────
    /// <summary>Статус брандмауэра «Разрешено».</summary>
    public string FwStatusAllowed => this["FwStatusAllowed"];
    /// <summary>Статус брандмауэра «Заблокирован».</summary>
    public string FwStatusBlocked => this["FwStatusBlocked"];
    /// <summary>Статус «Заблокирован (входящий)».</summary>
    public string FwStatusBlockedInbound => this["FwStatusBlockedInbound"];
    /// <summary>Статус «Заблокирован (исходящий)».</summary>
    public string FwStatusBlockedOutbound => this["FwStatusBlockedOutbound"];
    /// <summary>Статус «Заблокирован (оба направления)».</summary>
    public string FwStatusBlockedBoth => this["FwStatusBlockedBoth"];
    /// <summary>Статус «В бане» (активный бан Port Defender).</summary>
    public string FwStatusBanned => this["FwStatusBanned"];
    /// <summary>Статус «Защищён Defender» (доверенный/частный адрес под защитой).</summary>
    public string FwStatusProtected => this["FwStatusProtected"];
    /// <summary>Пункт меню «Заблокировать IP».</summary>
    public string BlockIp => this["BlockIp"];
    /// <summary>Пункт меню «Разблокировать IP».</summary>
    public string UnblockIp => this["UnblockIp"];
    /// <summary>Длительность бана 15 минут.</summary>
    public string BanDuration15m => this["BanDuration15m"];
    /// <summary>Длительность бана 1 час.</summary>
    public string BanDuration1h => this["BanDuration1h"];
    /// <summary>Длительность бана 24 часа.</summary>
    public string BanDuration24h => this["BanDuration24h"];
    /// <summary>Перманентный бан.</summary>
    public string BanPermanent => this["BanPermanent"];
    /// <summary>Подсказка блокировки IP.</summary>
    public string BlockIpTooltip => this["BlockIpTooltip"];
    /// <summary>Подсказка разблокировки IP.</summary>
    public string UnblockIpTooltip => this["UnblockIpTooltip"];
    /// <summary>Подсказка выбора длительности бана.</summary>
    public string BanDurationTooltip => this["BanDurationTooltip"];
    /// <summary>Заголовок окна Port Defender.</summary>
    public string PortDefenderTitle => this["PortDefenderTitle"];
    /// <summary>Подзаголовок окна Port Defender.</summary>
    public string PortDefenderSubtitle => this["PortDefenderSubtitle"];
    /// <summary>Переключатель включения Port Defender.</summary>
    public string PortDefenderEnabledToggle => this["PortDefenderEnabledToggle"];
    /// <summary>Метка защищаемых портов.</summary>
    public string PortDefenderProtectedPortsLabel => this["PortDefenderProtectedPortsLabel"];
    /// <summary>Подсказка защищаемых портов.</summary>
    public string PortDefenderProtectedPortsTooltip => this["PortDefenderProtectedPortsTooltip"];
    /// <summary>Метка порога срабатывания.</summary>
    public string PortDefenderThresholdLabel => this["PortDefenderThresholdLabel"];
    /// <summary>Подсказка порога срабатывания.</summary>
    public string PortDefenderThresholdTooltip => this["PortDefenderThresholdTooltip"];
    /// <summary>Метка временного окна.</summary>
    public string PortDefenderWindowLabel => this["PortDefenderWindowLabel"];
    /// <summary>Подсказка временного окна.</summary>
    public string PortDefenderWindowTooltip => this["PortDefenderWindowTooltip"];
    /// <summary>Метка длительности бана.</summary>
    public string PortDefenderBanDurationLabel => this["PortDefenderBanDurationLabel"];
    /// <summary>Подсказка длительности бана.</summary>
    public string PortDefenderBanDurationTooltip => this["PortDefenderBanDurationTooltip"];
    /// <summary>Переключатель автоэскалации.</summary>
    public string PortDefenderAutoEscalateToggle => this["PortDefenderAutoEscalateToggle"];
    /// <summary>Переключатель доверия частным сетям.</summary>
    public string PortDefenderTrustPrivateToggle => this["PortDefenderTrustPrivateToggle"];
    /// <summary>Метка белого списка.</summary>
    public string PortDefenderWhitelistLabel => this["PortDefenderWhitelistLabel"];
    /// <summary>Подсказка белого списка.</summary>
    public string PortDefenderWhitelistTooltip => this["PortDefenderWhitelistTooltip"];
    /// <summary>Заголовок правил по портам.</summary>
    public string PortDefenderRulesTitle => this["PortDefenderRulesTitle"];
    /// <summary>Заголовок активных банов.</summary>
    public string PortDefenderActiveBansTitle => this["PortDefenderActiveBansTitle"];
    /// <summary>Кнопка «Разбанить все».</summary>
    public string PortDefenderUnbanAllButton => this["PortDefenderUnbanAllButton"];
    /// <summary>Кнопка «Сохранить».</summary>
    public string PortDefenderSaveButton => this["PortDefenderSaveButton"];
    /// <summary>Кнопка «Закрыть».</summary>
    public string PortDefenderCloseButton => this["PortDefenderCloseButton"];

    // ──── Центр сетевой безопасности (NetworkSecurityHubView / FirewallPage) ────
    /// <summary>Название пункта навигации «Сетевой экран».</summary>
    public string NavFirewall => this["NavFirewall"];
    /// <summary>Заголовок хаба сетевой безопасности.</summary>
    public string FirewallHubTitle => this["FirewallHubTitle"];
    /// <summary>Действие правила «Разрешить».</summary>
    public string RuleActionAllow => this["RuleActionAllow"];
    /// <summary>Действие правила «Блокировать».</summary>
    public string RuleActionBlock => this["RuleActionBlock"];
    /// <summary>Направление правила «Входящее».</summary>
    public string RuleDirIn => this["RuleDirIn"];
    /// <summary>Направление правила «Исходящее».</summary>
    public string RuleDirOut => this["RuleDirOut"];
    /// <summary>Режим фильтрации «Обычный».</summary>
    public string FilteringModeNormal => this["FilteringModeNormal"];
    /// <summary>Режим фильтрации «Белый список».</summary>
    public string FilteringModeWhitelist => this["FilteringModeWhitelist"];
    /// <summary>Режим фильтрации «Killswitch».</summary>
    public string FilteringModeKillswitch => this["FilteringModeKillswitch"];
    /// <summary>Кнопка «Создать правило».</summary>
    public string BtnCreateRule => this["BtnCreateRule"];
    /// <summary>Кнопка «Редактировать» правило.</summary>
    public string BtnEditRule => this["BtnEditRule"];
    /// <summary>Кнопка «Удалить» правило.</summary>
    public string BtnDeleteRule => this["BtnDeleteRule"];

    /// <summary>Формирует строку счётчика сокетов «Соединений: N (... )».</summary>
    public string GetFwSocketCount(int total, int established, int listening) =>
        string.Format(CultureInfo.CurrentUICulture, this["FwSocketCountFormat"], total, established, listening);

    /// <summary>Формирует строку временного бана «В бане до ...».</summary>
    public string GetPortDefenderBanStatus(string expiresText, int repeatCount) =>
        string.Format(CultureInfo.CurrentUICulture, this["PortDefenderBanStatusFormat"], expiresText, repeatCount);

    /// <summary>Формирует строку перманентного бана.</summary>
    public string GetPortDefenderBanPermanent(int repeatCount) =>
        string.Format(CultureInfo.CurrentUICulture, this["PortDefenderBanPermanentFormat"], repeatCount);

    /// <summary>Переключает язык интерфейса и оповещает подписчиков через INotifyPropertyChanged.</summary>
    /// <param name="cultureCode">Код культуры (например, "ru", "en").</param>
    public void ChangeLanguage(string cultureCode)
    {
        var culture = new CultureInfo(cultureCode);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Принудительно оповещает всех подписчиков об изменении всех строк (для обновления привязок).</summary>
    public void RaiseAllChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    }
}
