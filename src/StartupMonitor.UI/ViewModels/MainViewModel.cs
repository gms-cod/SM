using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StartupMonitor.Shared.Audit;
using StartupMonitor.Shared.Audit.Models;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Snapshot;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.UI.Services;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Views.Dialogs;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StartupMonitor.UI.ViewModels;

/// <summary>Главная ViewModel приложения: управление списком записей, настройками, фильтрацией, чёрным списком и подключением к службе.</summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Клиент именованного канала для связи со службой.</summary>
    private readonly PipeClient _pipeClient;

    /// <summary>Публичный доступ к клиенту именованного канала.</summary>
    public PipeClient PipeClient => _pipeClient;

    /// <summary>Глобальный таймер опроса сетевых алертов брандмауэра (работает автономно).</summary>
    private System.Threading.Timer? _networkAlertPollerTimer;

    /// <summary>Флаг активности отображаемого окна сетевого алерта (предотвращает повторные окна). 1 = открыто.</summary>
    private int _isAlertWindowOpen;

    /// <summary>Флаг защиты от повторного входа в поллер сетевых алертов.</summary>
    private int _isPollingNetworkAlerts;

    /// <summary>Таймер периодического опроса изменений данных (polling).</summary>
    private System.Threading.Timer? _pollingTimer;

    /// <summary>Таймер опроса системной темы (для автоматического переключения).</summary>
    private System.Threading.Timer? _themePollTimer;

    /// <summary>Таймер опроса загрузки CPU для отображения в UI.</summary>
    private System.Threading.Timer? _cpuUsageTimer;

    /// <summary>PID текущего UI-процесса (кэшируется, чтобы не создавать Process-объект каждый тик).</summary>
    private readonly int _uiPid = Environment.ProcessId;

    /// <summary>Флаг глубокого покоя CPU-таймера (окно скрыто в трей). 1 = приостановлен.</summary>
    private volatile bool _cpuTimerSuspended;

    /// <summary>Флаг полной заморозки фоновых таймеров UI (окно скрыто в трей). 1 = приостановлено (Interlocked).</summary>
    private int _allBackgroundSuspended;

    /// <summary>Была ли активна подписка Диспетчера задач на момент заморозки (для симметричной разморозки).</summary>
    private bool _taskManagerWasActive;

    /// <summary>Флаг защиты от реэнтрантного входа в LoadDataIfChanged (Interlocked).</summary>
    private int _isLoading;

    /// <summary>Флаг защиты от реэнтрантного переключения паузы мониторинга (Interlocked).</summary>
    private int _pauseToggling;

    /// <summary>Токен отмены фоновых циклов (загрузка рейтингов/хешей).</summary>
    private CancellationTokenSource? _backgroundCts;

    /// <summary>Флаг: восстанавливаются ли настройки (подавляет триггеры OnXxxChanged).</summary>
    private bool _isRestoring;

    /// <summary>Полный неотфильтрованный список записей автозапуска.</summary>
    private List<StartupEntry> _allEntries = [];

    /// <summary>Блокировка доступа к <see cref="_allEntries"/> (фоновый polling vs UI-поток).</summary>
    private readonly object _entriesLock = new();

    /// <summary>
    /// Ревизия записей автозапуска, известная клиенту (синхронизирована с сервером через GetEntriesDelta).
    /// 0 = первая синхронизация (сервер вернёт полный список + актуальную ревизию).
    /// </summary>
    private long _entriesRevision;

    /// <summary>Хеш идентификаторов записей на момент последней загрузки (для детектирования новых).</summary>
    private HashSet<string> _previousEntryIds = [];

    /// <summary>true во время первого poll-цикла (подавляет уведомления о «новых» записях при старте).</summary>
    private bool _isFirstPoll = false;

    /// <summary>Имя колонки для сортировки ("name", "date", "status", "rating", "kaspersky").</summary>
    private string _sortColumn = "name";

    /// <summary>Направление сортировки: true = по возрастанию.</summary>
    private bool _sortAscending = true;

    /// <summary>Модель представления Диспетчера задач (процессы и системная производительность).</summary>
    public TaskManagerViewModel TaskManager { get; }

    /// <summary>Флаг подключения к службе.</summary>
    [ObservableProperty]
    private bool _isConnected;

    partial void OnIsConnectedChanged(bool value)
    {
        UpdateServiceStatusText();
    }

    /// <summary>Подробная строка статуса службы (для левого нижнего угла).</summary>
    [ObservableProperty]
    private string _serviceStatusDetails = "...";

    /// <summary>Идентификатор процесса службы (PID).</summary>
    [ObservableProperty]
    private int? _servicePid;

    /// <summary>Версия службы.</summary>
    [ObservableProperty]
    private string _serviceVersion = "1.0.25";

    /// <summary>Флаг паузы глобального мониторинга.</summary>
    [ObservableProperty]
    private bool _isMonitoringPaused;

    partial void OnIsMonitoringPausedChanged(bool value)
    {
        if (value)
        {
            PauseMonitoringButtonText = LocalizationService.Instance["BtnResumeMonitoring"];
            PauseMonitoringButtonTooltip = LocalizationService.Instance["TooltipResumeMonitoring"];
            PauseMonitoringButtonAppearance = ControlAppearance.Primary;
            PauseMonitoringButtonIcon = "Play24";
            _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else
        {
            PauseMonitoringButtonText = LocalizationService.Instance["BtnPauseMonitoring"];
            PauseMonitoringButtonTooltip = LocalizationService.Instance["TooltipPauseMonitoring"];
            PauseMonitoringButtonAppearance = ControlAppearance.Secondary;
            PauseMonitoringButtonIcon = "Pause24";
            _pollingTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        UpdateServiceStatusText();
        (System.Windows.Application.Current as App)?.UpdateTrayMonitoringState(value);
    }

    /// <summary>Текст кнопки приостановки/возобновления.</summary>
    [ObservableProperty]
    private string _pauseMonitoringButtonText = LocalizationService.Instance["BtnPauseMonitoring"];

    /// <summary>Подсказка кнопки приостановки/возобновления.</summary>
    [ObservableProperty]
    private string _pauseMonitoringButtonTooltip = LocalizationService.Instance["TooltipPauseMonitoring"];

    /// <summary>Внешний вид кнопки приостановки/возобновления.</summary>
    [ObservableProperty]
    private ControlAppearance _pauseMonitoringButtonAppearance = ControlAppearance.Secondary;

    /// <summary>Иконка кнопки приостановки/возобновления.</summary>
    [ObservableProperty]
    private string _pauseMonitoringButtonIcon = "Pause24";

    /// <summary>Общее количество записей автозапуска.</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>Количество включённых (Enabled) записей.</summary>
    [ObservableProperty]
    private int _enabledCount;

    /// <summary>Количество заблокированных (Blocked) записей.</summary>
    [ObservableProperty]
    private int _blockedCount;

    /// <summary>Текст бейджа карточки «Всего».</summary>
    public string TotalBadgeText => "+3";

    /// <summary>Текст бейджа карточки «Активные».</summary>
    public string EnabledBadgeText => LocalizationService.Instance.BadgeStable;

    /// <summary>Текст бейджа карточки «Заблокировано».</summary>
    public string BlockedBadgeText => "+1";

    /// <summary>Статус безопасности системы.</summary>
    public string SystemHealthStatus => LocalizationService.Instance.SystemHealthSecure;

    /// <summary>Запись для отображения выпадающего списка (Display/Code).</summary>
    public record SelectOption(string Display, string Code);

    /// <summary>Путь в реестре к настройкам темы Windows.</summary>
    private const string RegistryThemePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Список доступных языков интерфейса для выпадающего списка.</summary>
    public ObservableCollection<SelectOption> LanguageOptions { get; } = [];

    /// <summary>Список доступных тем оформления для выпадающего списка.</summary>
    public ObservableCollection<SelectOption> ThemeOptions { get; } = [];

    /// <summary>Коллекция всех записей автозапуска (привязана к DataGrid).</summary>
    [ObservableProperty]
    private ObservableCollection<StartupEntry> _entries = [];

    /// <summary>Отфильтрованная коллекция записей (для отображения).</summary>
    [ObservableProperty]
    private ObservableCollection<StartupEntry> _filteredEntries = [];

    /// <summary>Индикатор сортировки по имени (▲/▼).</summary>
    public string SortNameIndicator => _sortColumn == "name" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";

    /// <summary>Индикатор сортировки по дате.</summary>
    public string SortDateIndicator => _sortColumn == "date" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";

    /// <summary>Индикатор сортировки по издателю.</summary>
    public string SortPublisherIndicator => _sortColumn == "publisher" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";

    /// <summary>Индикатор сортировки по влиянию на запуск.</summary>
    public string SortImpactIndicator => _sortColumn == "impact" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";

    /// <summary>Индикатор сортировки по статусу.</summary>
    public string SortStatusIndicator => _sortColumn == "status" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";

    /// <summary>Индикатор сортировки по рейтингу.</summary>
    public string SortRatingIndicator => _sortColumn == "rating" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";
    public string SortKasperskyIndicator => _sortColumn == "kaspersky" ? (_sortAscending ? "\u25B2" : "\u25BC") : "";

    /// <summary>Текущий чёрный список (список правил).</summary>
    [ObservableProperty]
    private ObservableCollection<BlacklistRule> _blacklist = [];

    /// <summary>Таймер отложенной фильтрации поиска (debouncing 250 мс).</summary>
    private System.Threading.Timer? _searchDebounceTimer;

    /// <summary>Текст поискового запроса для фильтрации записей.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Вызывается при изменении текста поиска — применяет фильтр с задержкой 250 мс.</summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = new System.Threading.Timer(_ =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(ApplyFilter);
        }, null, 250, Timeout.Infinite);
    }

    /// <summary>Очищает строку поиска (кнопка-крестик в поле поиска).</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = null;
        SearchText = string.Empty;
        ApplyFilter();
    }

    /// <summary>Сортирует записи по указанной колонке; повторный вызов по той же колонке переключает направление.</summary>
    /// <param name="column">Имя колонки: "name", "date", "status", "rating", "kaspersky".</param>
    public void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
        OnPropertyChanged(nameof(SortNameIndicator));
        OnPropertyChanged(nameof(SortDateIndicator));
        OnPropertyChanged(nameof(SortPublisherIndicator));
        OnPropertyChanged(nameof(SortImpactIndicator));
        OnPropertyChanged(nameof(SortStatusIndicator));
        OnPropertyChanged(nameof(SortRatingIndicator));
        OnPropertyChanged(nameof(SortKasperskyIndicator));
        ApplySort();
        ApplyFilter();
        SaveSort();
    }

    /// <summary>Применяет текущий порядок сортировки к _allEntries (по колонке и направлению).</summary>
    private void ApplySort()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Main.ApplySort");
        StartupEntry[] snapshot;
        lock (_entriesLock)
        {
            snapshot = _allEntries.ToArray();
        }
        var sorted = _sortColumn switch
        {
            "name" => _sortAscending
                ? [.. snapshot.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]
                : [.. snapshot.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)],
            "date" => _sortAscending
                ? [.. snapshot.OrderBy(e => e.DetectedAt)]
                : [.. snapshot.OrderByDescending(e => e.DetectedAt)],
            "publisher" => _sortAscending
                ? [.. snapshot.OrderBy(e => string.IsNullOrWhiteSpace(e.Publisher)).ThenBy(e => e.Publisher, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]
                : [.. snapshot.OrderBy(e => string.IsNullOrWhiteSpace(e.Publisher)).ThenByDescending(e => e.Publisher, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
            "impact" => _sortAscending
                ? [.. snapshot.OrderBy(e => ImpactSeverity(e.Location)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]
                : [.. snapshot.OrderByDescending(e => ImpactSeverity(e.Location)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
            "status" => _sortAscending
                ? [.. snapshot.OrderBy(e => e.Status).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]
                : [.. snapshot.OrderByDescending(e => e.Status).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
            "rating" => _sortAscending
                ? [.. snapshot.OrderBy(e => e.AiRating).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]
                : [.. snapshot.OrderByDescending(e => e.AiRating).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
            "kaspersky" => _sortAscending
                ? [.. snapshot.OrderBy(e => CirclSeverity(e.KasperskyZone)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)]
                : [.. snapshot.OrderByDescending(e => CirclSeverity(e.KasperskyZone)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)],
            _ => snapshot.ToList(),
        };
        lock (_entriesLock)
        {
            _allEntries = sorted;
        }
    }

    /// <summary>Инициализирует выпадающие списки языка и темы переведёнными названиями.</summary>
    private void InitOptions()
    {
        LanguageOptions.Add(new(LocalizationService.Instance.LangRussian, "ru"));
        LanguageOptions.Add(new(LocalizationService.Instance.LangEnglish, "en"));
        ThemeOptions.Add(new(LocalizationService.Instance.ThemeDark, "dark"));
        ThemeOptions.Add(new(LocalizationService.Instance.ThemeLight, "light"));
        ThemeOptions.Add(new(LocalizationService.Instance.ThemeSystem, "system"));
    }

    /// <summary>Код выбранной темы (dark/light/system).</summary>
    [ObservableProperty]
    private string _selectedThemeCode = "dark";

    /// <summary>Применяет тему при её изменении пользователем; для system включает watcher системной темы.</summary>
    partial void OnSelectedThemeCodeChanged(string value)
    {
        if (_isRestoring) return;
        SaveSetting("theme", value);
        if (value == "system")
        {
            ApplySystemTheme();
            StartSystemThemeWatcher();
        }
        else
        {
            StopSystemThemeWatcher();
            Services.ThemeService.ApplyTheme(value == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark);
        }
    }

    /// <summary>Код выбранного языка (ru/en).</summary>
    [ObservableProperty]
    private string _selectedLanguageCode = "ru";

    /// <summary>Меняет язык интерфейса и обновляет локализованные тексты в выпадающих списках.</summary>
    partial void OnSelectedLanguageCodeChanged(string value)
    {
        SaveSetting("language", value);
        LocalizationService.Instance.ChangeLanguage(value);
        RefreshLocalizedOptions();
        _ = _pipeClient.SetLanguageAsync(value);
    }

    /// <summary>Перестраивает выпадающие списки с новыми локализованными названиями, сохраняя выбранные значения.</summary>
    public void RefreshLocalizedOptions()
    {
        _isRestoring = true;

        var savedLang = SelectedLanguageCode ?? "ru";
        var savedTheme = SelectedThemeCode ?? "dark";

        LanguageOptions.Clear();
        LanguageOptions.Add(new(LocalizationService.Instance.LangRussian, "ru"));
        LanguageOptions.Add(new(LocalizationService.Instance.LangEnglish, "en"));
        ThemeOptions.Clear();
        ThemeOptions.Add(new(LocalizationService.Instance.ThemeDark, "dark"));
        ThemeOptions.Add(new(LocalizationService.Instance.ThemeLight, "light"));
        ThemeOptions.Add(new(LocalizationService.Instance.ThemeSystem, "system"));

        SelectedLanguageCode = savedLang;
        SelectedThemeCode = savedTheme;
        OnPropertyChanged(nameof(MatchTypes));
        OnPropertyChanged(nameof(Actions));

        if (IsMonitoringPaused)
        {
            PauseMonitoringButtonText = LocalizationService.Instance["BtnResumeMonitoring"];
            PauseMonitoringButtonTooltip = LocalizationService.Instance["TooltipResumeMonitoring"];
        }
        else
        {
            PauseMonitoringButtonText = LocalizationService.Instance["BtnPauseMonitoring"];
            PauseMonitoringButtonTooltip = LocalizationService.Instance["TooltipPauseMonitoring"];
        }
        UpdateServiceStatusText();

        _isRestoring = false;
    }

    /// <summary>Определяет, включена ли светлая тема в Windows (по реестру).</summary>
    /// <returns>true, если Windows использует светлую тему.</returns>
    private static bool IsWindowsThemeLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryThemePath);
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 1;
        }
        catch { }
        return false;
    }

    /// <summary>Применяет тему Windows (светлую/тёмную) к приложению.</summary>
    private void ApplySystemTheme()
    {
        var isLight = IsWindowsThemeLight();
        Services.ThemeService.ApplyTheme(isLight ? ApplicationTheme.Light : ApplicationTheme.Dark);
    }

    /// <summary>Запускает таймер опроса системной темы (каждые 2 с) для автоматического переключения.</summary>
    private void StartSystemThemeWatcher()
    {
        StopSystemThemeWatcher();
        _themePollTimer = new System.Threading.Timer(_ =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(ApplySystemTheme);
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    /// <summary>Останавливает таймер опроса системной темы.</summary>
    private void StopSystemThemeWatcher()
    {
        _themePollTimer?.Dispose();
        _themePollTimer = null;
    }

    /// <summary>Освобождает ресурсы: таймеры, токены, pipe-клиент.</summary>
    public void Dispose()
    {
        _pipeClient.Connected -= OnPipeClientConnected;
        _pipeClient.EventsReceived -= OnPipeClientEventsReceived;
        _pipeClient.Disconnected -= OnPipeClientDisconnected;
        StopSystemThemeWatcher();
        _networkAlertPollerTimer?.Dispose();
        _networkAlertPollerTimer = null;
        _pollingTimer?.Dispose();
        _cpuUsageTimer?.Dispose();
        _logWatchTimer?.Dispose();
        _searchDebounceTimer?.Dispose();
        _backgroundCts?.Cancel();
        _backgroundCts?.Dispose();
        TaskManager?.Dispose();
        _pipeClient?.Dispose();
    }

    /// <summary>Приостанавливает замер CPU и фоновые таймеры (глубокий покой UI в трее): 0% CPU от таймеров.</summary>
    public void SuspendCpuTimer()
    {
        _cpuTimerSuspended = true;
        try { _cpuUsageTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        try { _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        try { _themePollTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
    }

    /// <summary>Возобновляет замер CPU и фоновые таймеры при восстановлении окна из трея.</summary>
    public void ResumeCpuTimer()
    {
        _cpuTimerSuspended = false;
        try { _cpuUsageTimer?.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)); } catch { }
        if (!IsMonitoringPaused)
            try { _pollingTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5)); } catch { }
        if (_themePollTimer != null)
            try { _themePollTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2)); } catch { }
    }

    /// <summary>Приостановлен ли CPU-таймер (окно скрыто в трей).</summary>
    public bool IsCpuTimerSuspended => _cpuTimerSuspended;

    /// <summary>Полностью ли приостановлена фоновая активность UI (окно скрыто в трей).</summary>
    public bool IsAllBackgroundSuspended => _allBackgroundSuspended == 1;

    /// <summary>
    /// Полная заморозка фоновой активности UI при уходе окна в трей (0% CPU от таймеров):
    /// останавливает polling, опрос системной темы, замер CPU и watch логов,
    /// отменяет фоновые циклы рейтингов/хешей и отписывается от потока метрик процессов службы.
    /// Идемпотентно (повторный вызов безвреден).
    /// </summary>
    public void SuspendAllBackgroundActivity()
    {
        if (Interlocked.Exchange(ref _allBackgroundSuspended, 1) == 1) return;

        try { _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        try { _themePollTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        try { _logWatchTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }

        // Глубокий покой CPU-таймера: флаг + остановка (двойная защита от срабатывания in-flight тика)
        _cpuTimerSuspended = true;
        try { _cpuUsageTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }

        // Останавливаем фоновые циклы рейтингов AI и дозагрузки хешей
        try { _backgroundCts?.Cancel(); } catch { }

        // Отписка от потока метрик процессов службы (UnsubscribeProcessMetricsAsync):
        // служба перестаёт собирать снапшоты и рассылать метрики (0% CPU).
        _taskManagerWasActive = TaskManager.IsActive;
        _ = TaskManager.DeactivateAsync();

        // ВАЖНО: _networkAlertPollerTimer НЕ останавливается здесь.
        // Сетевые алерты должны приходить даже когда UI свёрнут в трей — это функция безопасности.
        // Таймер привязан к состоянию Pipe-соединения (OnPipeClientConnected / OnPipeClientDisconnected),
        // а не к видимости окна. Проверка _allBackgroundSuspended внутри PollNetworkAlertsAsync тоже убрана.
    }

    /// <summary>
    /// Симметричная разморозка фоновой активности UI при восстановлении окна из трея:
    /// мгновенно перезапускает все таймеры, возобновляет фоновые циклы и обновляет данные.
    /// Идемпотентно (повторный вызов безвреден).
    /// </summary>
    public void ResumeAllBackgroundActivity()
    {
        if (Interlocked.Exchange(ref _allBackgroundSuspended, 0) == 0) return;

        // Polling: возобновляем только если мониторинг не на паузе
        if (!IsMonitoringPaused)
            try { _pollingTimer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5)); } catch { }

        // Опрос системной темы: только если watcher реально создан (тема = system)
        if (_themePollTimer != null)
            try { _themePollTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2)); } catch { }

        // Watch логов: только если страница «Логи» была активна
        if (_logWatchTimer != null)
            try { _logWatchTimer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)); } catch { }

        // Сетевые алерты: возобновляем поллер (пересоздаём, если был остановлен)
        try
        {
            if (_networkAlertPollerTimer != null)
                _networkAlertPollerTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            else
                StartNetworkAlertPoller();
        }
        catch { }

        _cpuTimerSuspended = false;
        try { _cpuUsageTimer?.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)); } catch { }

        // Возобновляем подписку Диспетчера задач, если она была активна до заморозки
        if (_taskManagerWasActive)
        {
            _taskManagerWasActive = false;
            _ = TaskManager.ActivateAsync();
        }

        // Перезапускаем фоновые циклы рейтингов/хешей новым токеном
        _backgroundCts?.Dispose();
        _backgroundCts = new CancellationTokenSource();
        List<StartupEntry> entries;
        lock (_entriesLock) { entries = _allEntries.ToList(); }
        if (entries.Count > 0)
        {
            _ = LoadRatingsAsync(entries, _backgroundCts.Token);
            _ = LoadHashDataAsync(entries, _backgroundCts.Token);
        }

        // Однократный немедленный запрос актуального состояния после разморозки
        _ = LoadDataIfChanged();
    }

    /// <summary>Включены ли уведомления о новых записях.</summary>
    [ObservableProperty]
    private bool _notifyOnNewEntry = true;

    /// <summary>Сохраняет настройку уведомлений при изменении.</summary>
    partial void OnNotifyOnNewEntryChanged(bool value)
    {
        SaveSetting("notifyOnNewEntry", value);
    }

    /// <summary>Сворачивать ли в трей при закрытии окна.</summary>
    [ObservableProperty]
    private bool _minimizeToTray = true;

    /// <summary>Сохраняет настройку сворачивания в трей.</summary>
    partial void OnMinimizeToTrayChanged(bool value)
    {
        SaveSetting("minimizeToTray", value);
    }

    /// <summary>Запускать ли UI вместе с Windows.</summary>
    [ObservableProperty]
    private bool _startWithWindows = true;

    /// <summary>Обновляет автозапуск UI в реестре и сохраняет настройку.</summary>
    partial void OnStartWithWindowsChanged(bool value)
    {
        SaveSetting("startWithWindows", value);
        Services.AutostartService.SetEnabled(value);
    }

    /// <summary>Строгий режим: блокировка любых новых записей автозапуска.</summary>
    [ObservableProperty]
    private bool _strictMode;

    /// <summary>Сохраняет настройку строгого режима и отправляет её службе.</summary>
    partial void OnStrictModeChanged(bool value)
    {
        SaveSetting("strictMode", value);
        _ = _pipeClient.SetStrictModeAsync(value);
    }

    /// <summary>Включен ли троттлинг сканирования реестра при высокой нагрузке CPU.</summary>
    [ObservableProperty]
    private bool _cpuThrottleEnabled;

    /// <summary>Сохраняет настройку троттлинга и отправляет службе.</summary>
    partial void OnCpuThrottleEnabledChanged(bool value)
    {
        SaveSetting("cpuThrottleEnabled", value);
        _ = _pipeClient.SetCpuThrottleAsync(value, CpuThrottleThreshold);
    }

    /// <summary>Порог загрузки CPU (%) для пропуска сканирования реестра.</summary>
    [ObservableProperty]
    private int _cpuThrottleThreshold = 70;

    /// <summary>Сохраняет порог загрузки CPU и отправляет службе.</summary>
    partial void OnCpuThrottleThresholdChanged(int value)
    {
        SaveSetting("cpuThrottleThreshold", value);
        _ = _pipeClient.SetCpuThrottleAsync(CpuThrottleEnabled, value);
    }

    /// <summary>Текст текущей загрузки процессора для отображения в настройках.</summary>
    [ObservableProperty]
    private string _currentCpuUsageText = "...";

    /// <summary>Процент общей загрузки процессора для карточки метрики.</summary>
    [ObservableProperty]
    private string _cpuUsagePercentText = "0%";

    /// <summary>Детализация загрузки процессора сторонними процессами.</summary>
    [ObservableProperty]
    private string _cpuUsageDetailText = "";

    /// <summary>Автоматически блокировать запись при удалении из системы.</summary>
    [ObservableProperty]
    private bool _autoBlockOnRemove;

    /// <summary>Сохраняет настройку автоблокировки при удалении.</summary>
    partial void OnAutoBlockOnRemoveChanged(bool value)
    {
        SaveSetting("autoBlockOnRemove", value);
    }

    /// <summary>Убивать процесс при отключении записи (настройка службы).</summary>
    [ObservableProperty]
    private bool _killOnDisable = false;

    /// <summary>Сохраняет настройку и отправляет её службе.</summary>
    partial void OnKillOnDisableChanged(bool value)
    {
        SaveSetting("killOnDisable", value);
        _ = _pipeClient.SetKillOnDisableAsync(value);
    }

    /// <summary>Тихий режим: подавление toast-уведомлений.</summary>
    [ObservableProperty]
    private bool _silentMode;

    /// <summary>Сохраняет настройку тихого режима.</summary>
    partial void OnSilentModeChanged(bool value)
    {
        SaveSetting("silentMode", value);
    }

    /// <summary>Включена ли AI-оценка записей через Groq API.</summary>
    [ObservableProperty]
    private bool _aiEnabled = true;

    /// <summary>Сохраняет настройку AI.</summary>
    partial void OnAiEnabledChanged(bool value)
    {
        SaveSetting("aiEnabled", value);
    }

    /// <summary>API-ключ для AI (LLM-оценка записей).</summary>
    [ObservableProperty]
    private string _aiApiKey = string.Empty;

    /// <summary>Сохраняет ключ и отправляет его службе через pipe.</summary>
    partial void OnAiApiKeyChanged(string value)
    {
        SaveSetting("aiApiKey", value);
        _ = _pipeClient.SetAiConfigAsync(value, AiApiUrl, AiModel);
        // После ввода ключа сразу запускаем опрос рейтингов (иначе ждём до следующего 5-с цикла)
        if (!string.IsNullOrWhiteSpace(value) && AiEnabled)
        {
            _backgroundCts?.Cancel();
            _backgroundCts = new CancellationTokenSource();
            _ = LoadRatingsAsync(_allEntries, _backgroundCts.Token);
        }
    }

    /// <summary>URL эндпоинта AI (OpenAI-совместимый).</summary>
    [ObservableProperty]
    private string _aiApiUrl = "https://api.groq.com/openai/v1/chat/completions";

    partial void OnAiApiUrlChanged(string value)
    {
        SaveSetting("aiApiUrl", value);
        _ = _pipeClient.SetAiConfigAsync(AiApiKey, value, AiModel);
    }

    /// <summary>Имя модели AI для оценки.</summary>
    [ObservableProperty]
    private string _aiModel = "openai/gpt-oss-120b";

    partial void OnAiModelChanged(string value)
    {
        SaveSetting("aiModel", value);
        _ = _pipeClient.SetAiConfigAsync(AiApiKey, AiApiUrl, value);
    }

    /// <summary>Сообщение статуса проверки подключения к AI.</summary>
    [ObservableProperty]
    private string _aiTestStatusMessage = string.Empty;

    /// <summary>Флаг успешности проверки AI (true = зеленый, false = красный).</summary>
    [ObservableProperty]
    private bool _aiTestSuccess;

    /// <summary>Выполняется ли тестирование подключения к AI.</summary>
    [ObservableProperty]
    private bool _isAiTesting;

    /// <summary>Тестирует соединение с настроенным AI эндпоинтом.</summary>
    [RelayCommand]
    private async Task TestAiConnectionAsync()
    {
        if (IsAiTesting) return;
        IsAiTesting = true;
        AiTestStatusMessage = LocalizationService.Instance.AiTestingStatus;

        try
        {
            var (success, message) = await _pipeClient.TestAiConnectionAsync(AiApiKey, AiApiUrl, AiModel);
            AiTestSuccess = success;
            AiTestStatusMessage = message;
        }
        catch (Exception ex)
        {
            AiTestSuccess = false;
            AiTestStatusMessage = ex.Message;
        }
        finally
        {
            IsAiTesting = false;
        }
    }

    /// <summary>Включена ли репутационная проверка через Kaspersky OpenTIP.</summary>
    [ObservableProperty]
    private bool _kasperskyEnabled = true;

    partial void OnKasperskyEnabledChanged(bool value)
    {
        SaveSetting("kasperskyEnabled", value);
        _ = _pipeClient.SetKasperskyConfigAsync(value, KasperskyApiKey, KasperskyApiUrl);
    }

    /// <summary>API-токен для Kaspersky OpenTIP.</summary>
    [ObservableProperty]
    private string _kasperskyApiKey = "Zem+jUJbQEiqHPpwsfjTyQ==";

    partial void OnKasperskyApiKeyChanged(string value)
    {
        SaveSetting("kasperskyApiKey", value);
        _ = _pipeClient.SetKasperskyConfigAsync(KasperskyEnabled, value, KasperskyApiUrl);
    }

    /// <summary>URL API Kaspersky OpenTIP.</summary>
    [ObservableProperty]
    private string _kasperskyApiUrl = "https://opentip.kaspersky.com/api/v1/search/hash";

    partial void OnKasperskyApiUrlChanged(string value)
    {
        SaveSetting("kasperskyApiUrl", value);
        _ = _pipeClient.SetKasperskyConfigAsync(KasperskyEnabled, KasperskyApiKey, value);
    }

    /// <summary>Сообщение статуса проверки подключения к Kaspersky OpenTIP.</summary>
    [ObservableProperty]
    private string _kasperskyTestStatusMessage = string.Empty;

    /// <summary>Флаг успешности проверки Kaspersky (true = зеленый, false = красный).</summary>
    [ObservableProperty]
    private bool _kasperskyTestSuccess;

    /// <summary>Выполняется ли тестирование подключения к Kaspersky OpenTIP.</summary>
    [ObservableProperty]
    private bool _isKasperskyTesting;

    /// <summary>Тестирует соединение с настроенным Kaspersky OpenTIP API.</summary>
    [RelayCommand]
    private async Task TestKasperskyConnectionAsync()
    {
        if (IsKasperskyTesting) return;
        IsKasperskyTesting = true;
        KasperskyTestStatusMessage = LocalizationService.Instance.KasperskyTestingStatus;

        try
        {
            var res = await _pipeClient.TestKasperskyConnectionAsync(KasperskyApiKey, KasperskyApiUrl);
            KasperskyTestSuccess = res.Success;
            KasperskyTestStatusMessage = res.Message;
        }
        catch (Exception ex)
        {
            KasperskyTestSuccess = false;
            KasperskyTestStatusMessage = ex.Message;
        }
        finally
        {
            IsKasperskyTesting = false;
        }
    }

    // ───────────────────────────── Auto-Guards ─────────────────────────────

    /// <summary>Режим работы автоматических политик защиты ("AuditOnly", "EnforceBlock").</summary>
    [ObservableProperty]
    private string _autoGuardMode = "AuditOnly";

    public bool IsAutoGuardEnforce
    {
        get => string.Equals(AutoGuardMode, "EnforceBlock", StringComparison.OrdinalIgnoreCase);
        set
        {
            AutoGuardMode = value ? "EnforceBlock" : "AuditOnly";
            OnPropertyChanged(nameof(IsAutoGuardEnforce));
            OnPropertyChanged(nameof(IsAutoGuardAudit));
            SaveSetting("autoGuardMode", AutoGuardMode);
            SyncAutoGuardConfig();
        }
    }

    public bool IsAutoGuardAudit
    {
        get => !IsAutoGuardEnforce;
        set => IsAutoGuardEnforce = !value;
    }

    /// <summary>Политика: блокировать опасные параметры CLI (Base64, IEX downloaders, маскировка).</summary>
    [ObservableProperty]
    private bool _guardBlockDangerousCli;

    partial void OnGuardBlockDangerousCliChanged(bool value)
    {
        SaveSetting("guardBlockDangerousCli", value);
        SyncAutoGuardConfig();
    }

    /// <summary>Политика: блокировать запуск из каталогов Temp и Public.</summary>
    [ObservableProperty]
    private bool _guardBlockTempPath;

    partial void OnGuardBlockTempPathChanged(bool value)
    {
        SaveSetting("guardBlockTempPath", value);
        SyncAutoGuardConfig();
    }

    /// <summary>Политика: блокировать автозапуск скриптов (.bat, .vbs, .js, .ps1, mshta, cscript, wscript).</summary>
    [ObservableProperty]
    private bool _guardBlockScripts;

    partial void OnGuardBlockScriptsChanged(bool value)
    {
        SaveSetting("guardBlockScripts", value);
        SyncAutoGuardConfig();
    }

    /// <summary>Политика: блокировать бинарники без подлинной цифровой подписи Authenticode.</summary>
    [ObservableProperty]
    private bool _guardBlockUnsigned;

    partial void OnGuardBlockUnsignedChanged(bool value)
    {
        SaveSetting("guardBlockUnsigned", value);
        SyncAutoGuardConfig();
    }

    /// <summary>Политика: блокировать регистрацию новых несистемных служб Windows.</summary>
    [ObservableProperty]
    private bool _guardBlockNewServices;

    partial void OnGuardBlockNewServicesChanged(bool value)
    {
        SaveSetting("guardBlockNewServices", value);
        SyncAutoGuardConfig();
    }

    /// <summary>SyncAutoGuardConfig: Выполняет операцию SyncAutoGuardConfig.</summary>
    private void SyncAutoGuardConfig()
    {
        _ = _pipeClient.SetAutoGuardConfigAsync(new AutoGuardConfigPayload(
            Mode: AutoGuardMode,
            BlockTempPath: GuardBlockTempPath,
            BlockUnsigned: GuardBlockUnsigned,
            BlockScripts: GuardBlockScripts,
            BlockDangerousCli: GuardBlockDangerousCli,
            BlockNewServices: GuardBlockNewServices
        ));
    }

    /// <summary>Принудительно сканирует выбранную запись автозапуска в Kaspersky OpenTIP.</summary>
    [RelayCommand]
    public async Task ScanKasperskyEntryAsync(object? param)
    {
        var target = (param as StartupEntry) ?? _allEntries.FirstOrDefault(e => e.Id == (param as string));
        if (target == null) return;

        try
        {
            var updated = await _pipeClient.ScanKasperskyEntryAsync(target.Id);
            if (updated != null)
            {
                target.KasperskyZone = updated.KasperskyZone;
                target.KasperskyDetection = updated.KasperskyDetection;
                target.KasperskyFileStatus = updated.KasperskyFileStatus;
                NotificationRequested?.Invoke(
                    LocalizationService.Instance.KasperskyScanCompletedTitle,
                    string.Format(LocalizationService.Instance.KasperskyScanCompletedMsg, target.Name, target.KasperskyZone, target.KasperskyFileStatus),
                    target.Id);
            }
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke(
                "Startup Monitor",
                string.Format(LocalizationService.Instance.KasperskyScanFailedMsg, ex.Message),
                target.Id);
        }
    }

    /// <summary>Включены ли уведомления в Telegram.</summary>
    [ObservableProperty]
    private bool _telegramEnabled;

    partial void OnTelegramEnabledChanged(bool value)
    {
        SaveSetting("telegramEnabled", value);
        SyncRemoteNotifications();
    }

    /// <summary>Токен Telegram бота.</summary>
    [ObservableProperty]
    private string _telegramBotToken = string.Empty;

    partial void OnTelegramBotTokenChanged(string value)
    {
        SaveSetting("telegramBotToken", value);
        SyncRemoteNotifications();
    }

    /// <summary>ID чата Telegram.</summary>
    [ObservableProperty]
    private string _telegramChatId = string.Empty;

    partial void OnTelegramChatIdChanged(string value)
    {
        SaveSetting("telegramChatId", value);
        SyncRemoteNotifications();
    }

    /// <summary>Включены ли уведомления через Discord Webhook.</summary>
    [ObservableProperty]
    private bool _discordWebhookEnabled;

    partial void OnDiscordWebhookEnabledChanged(bool value)
    {
        SaveSetting("discordWebhookEnabled", value);
        SyncRemoteNotifications();
    }

    /// <summary>URL вебхука Discord.</summary>
    [ObservableProperty]
    private string _discordWebhookUrl = string.Empty;

    partial void OnDiscordWebhookUrlChanged(string value)
    {
        SaveSetting("discordWebhookUrl", value);
        SyncRemoteNotifications();
    }

    /// <summary>Оповещать о новых записях автозапуска.</summary>
    [ObservableProperty]
    private bool _remoteNotifyOnNewEntry = true;

    partial void OnRemoteNotifyOnNewEntryChanged(bool value)
    {
        SaveSetting("remoteNotifyOnNewEntry", value);
        SyncRemoteNotifications();
    }

    /// <summary>Оповещать о подозрительных командах (CLI Threat).</summary>
    [ObservableProperty]
    private bool _remoteNotifyOnThreat = true;

    partial void OnRemoteNotifyOnThreatChanged(bool value)
    {
        SaveSetting("remoteNotifyOnThreat", value);
        SyncRemoteNotifications();
    }

    /// <summary>Оповещать о блокировках Чёрным списком и Lockdown.</summary>
    [ObservableProperty]
    private bool _remoteNotifyOnBlocked = true;

    partial void OnRemoteNotifyOnBlockedChanged(bool value)
    {
        SaveSetting("remoteNotifyOnBlocked", value);
        SyncRemoteNotifications();
    }

    /// <summary>Сообщение статуса тестирования Telegram.</summary>
    [ObservableProperty]
    private string _telegramTestStatusMessage = string.Empty;

    /// <summary>Флаг успешности проверки Telegram.</summary>
    [ObservableProperty]
    private bool _telegramTestSuccess;

    /// <summary>Выполняется ли проверка Telegram.</summary>
    [ObservableProperty]
    private bool _isTelegramTesting;

    /// <summary>Сообщение статуса тестирования Discord.</summary>
    [ObservableProperty]
    private string _discordTestStatusMessage = string.Empty;

    /// <summary>Флаг успешности проверки Discord.</summary>
    [ObservableProperty]
    private bool _discordTestSuccess;

    /// <summary>Выполняется ли проверка Discord.</summary>
    [ObservableProperty]
    private bool _isDiscordTesting;

    /// <summary>Синхронизирует параметры удалённых уведомлений со службой Windows.</summary>
    private void SyncRemoteNotifications()
    {
        _ = _pipeClient.SetRemoteNotificationConfigAsync(
            TelegramEnabled,
            TelegramBotToken,
            TelegramChatId,
            DiscordWebhookEnabled,
            DiscordWebhookUrl,
            RemoteNotifyOnNewEntry,
            RemoteNotifyOnThreat,
            RemoteNotifyOnBlocked);
    }

    /// <summary>Отправляет тестовое уведомление в Telegram.</summary>
    [RelayCommand]
    private async Task TestTelegramAsync()
    {
        if (IsTelegramTesting) return;
        IsTelegramTesting = true;
        TelegramTestStatusMessage = LocalizationService.Instance.TestingRemoteNotificationStatus;

        try
        {
            SyncRemoteNotifications();
            var (success, message) = await _pipeClient.TestRemoteNotificationAsync("telegram");
            TelegramTestSuccess = success;
            TelegramTestStatusMessage = message;
        }
        catch (Exception ex)
        {
            TelegramTestSuccess = false;
            TelegramTestStatusMessage = ex.Message;
        }
        finally
        {
            IsTelegramTesting = false;
        }
    }

    /// <summary>Отправляет тестовое уведомление в Discord Webhook.</summary>
    [RelayCommand]
    private async Task TestDiscordAsync()
    {
        if (IsDiscordTesting) return;
        IsDiscordTesting = true;
        DiscordTestStatusMessage = LocalizationService.Instance.TestingRemoteNotificationStatus;

        try
        {
            SyncRemoteNotifications();
            var (success, message) = await _pipeClient.TestRemoteNotificationAsync("discord");
            DiscordTestSuccess = success;
            DiscordTestStatusMessage = message;
        }
        catch (Exception ex)
        {
            DiscordTestSuccess = false;
            DiscordTestStatusMessage = ex.Message;
        }
        finally
        {
            IsDiscordTesting = false;
        }
    }

    /// <summary>Включена ли автоматическая проверка обновлений на GitHub.</summary>
    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        SaveSetting("autoCheckUpdates", value);
        _ = _pipeClient.SetUpdateSettingsAsync(new UpdateSettingsPayload(value, AutoDownloadUpdates, SkippedUpdateVersion));
    }

    /// <summary>Включено ли автоматическое скачивание обновлений в фоне.</summary>
    [ObservableProperty]
    private bool _autoDownloadUpdates;

    partial void OnAutoDownloadUpdatesChanged(bool value)
    {
        SaveSetting("autoDownloadUpdates", value);
        _ = _pipeClient.SetUpdateSettingsAsync(new UpdateSettingsPayload(AutoCheckUpdates, value, SkippedUpdateVersion));
    }

    /// <summary>Пропущенная пользователем версия обновления.</summary>
    [ObservableProperty]
    private string? _skippedUpdateVersion;

    /// <summary>Признак выполнения проверки обновлений.</summary>
    [ObservableProperty]
    private bool _isCheckingUpdates;

    /// <summary>Статусное сообщение о проверке/наличии обновления.</summary>
    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    /// <summary>Сведения о текущей версии и последней проверке.</summary>
    [ObservableProperty]
    private string _currentVersionAndLastCheckText = string.Empty;

    /// <summary>Диалоговое окно обновления (если уже открыто).</summary>
    private Views.Dialogs.UpdateAvailableWindow? _activeUpdateDialog;

    /// <summary>Обновляет отображение текущей версии и времени последней проверки.</summary>
    public void RefreshCurrentVersionAndLastCheck()
    {
        var curVer = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.2.2";
        var lastCheck = LoadSetting("lastUpdateCheckTime", string.Empty);
        CurrentVersionAndLastCheckText = string.IsNullOrWhiteSpace(lastCheck)
            ? $"v{curVer}"
            : $"v{curVer} • {lastCheck}";
    }

    /// <summary>Запуск ручной проверки наличия обновлений на GitHub.</summary>
    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        UpdateStatusMessage = LocalizationService.Instance.CheckingForUpdatesText;

        try
        {
            var res = await _pipeClient.CheckForUpdatesAsync();
            if (res == null || !res.Success)
            {
                UpdateStatusMessage = res?.ErrorMessage ?? LocalizationService.Instance.UpdateCheckFailedText;
                return;
            }

            var nowStr = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            SaveSetting("lastUpdateCheckTime", nowStr);
            RefreshCurrentVersionAndLastCheck();

            if (res.UpdateInfo != null && IsUpdateEffectivelyAvailable(res.UpdateInfo))
            {
                UpdateStatusMessage = string.Format(LocalizationService.Instance.UpdateAvailableFormat, res.UpdateInfo.Version);
                ShowUpdateAvailableDialog(res.UpdateInfo);
            }
            else
            {
                UpdateStatusMessage = LocalizationService.Instance.UpdateLatestVersionInstalledText;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = ex.Message;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    /// <summary>
    /// Локальная переоценка доступности обновления для UI-клиента.
    /// Возвращает true, если служба сообщила обновление для себя/клиента
    /// ЛИБО если версия UI-клиента старее удалённой (защита от рассинхрона версий службы и UI).
    /// </summary>
    private static bool IsUpdateEffectivelyAvailable(UpdateInfo? info)
    {
        if (info == null) return false;
        if (info.IsUpdateAvailable || info.IsUpdateAvailableForClient) return true;
        var uiVer = typeof(MainViewModel).Assembly.GetName().Version;
        Version? remoteVer = null;
        if (info.Version != null)
        {
            try { remoteVer = ParseRemoteSemVer(CleanRemoteVersionTag(info.Version)); } catch { }
        }
        bool uiIsStale = uiVer != null && remoteVer != null && remoteVer > new Version(uiVer.Major, uiVer.Minor, Math.Max(0, uiVer.Build));
        return uiIsStale;
    }

    /// <summary>Очищает тег версии (убирает префикс 'v' и суффиксы типа '-beta'). Локальная копия логики службы.</summary>
    private static string CleanRemoteVersionTag(string rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag)) return "0.0.0";
        var cleaned = rawTag.Trim().TrimStart('v', 'V');
        var dashIndex = cleaned.IndexOf('-');
        return dashIndex > 0 ? cleaned[..dashIndex] : cleaned;
    }

    /// <summary>Парсит SemVer-строку в <see cref="Version"/>. Локальная копия логики службы.</summary>
    private static Version ParseRemoteSemVer(string versionStr)
    {
        if (Version.TryParse(versionStr, out var v))
        {
            var build = v.Build < 0 ? 0 : v.Build;
            var rev = v.Revision < 0 ? -1 : v.Revision;
            return rev >= 0 ? new Version(v.Major, v.Minor, build, rev) : new Version(v.Major, v.Minor, build);
        }

        var parts = versionStr.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out var major))
            return new Version(major, 0, 0);

        return new Version(0, 0, 0);
    }

    /// <summary>Отображает модальное окно с информацией о новой версии.</summary>
    public void ShowUpdateAvailableDialog(UpdateInfo updateInfo)
    {
        if (System.Windows.Application.Current == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_activeUpdateDialog != null && _activeUpdateDialog.IsLoaded)
            {
                _activeUpdateDialog.Activate();
                return;
            }

            _activeUpdateDialog = new Views.Dialogs.UpdateAvailableWindow(updateInfo, _pipeClient, skippedVersion =>
            {
                SkippedUpdateVersion = skippedVersion;
                SaveSetting("skippedUpdateVersion", skippedVersion);
                _ = _pipeClient.SetUpdateSettingsAsync(new UpdateSettingsPayload(AutoCheckUpdates, AutoDownloadUpdates, skippedVersion));
            })
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            _activeUpdateDialog.Closed += (_, _) => _activeUpdateDialog = null;
            _activeUpdateDialog.Show();
        });
    }

    [ObservableProperty]
    private string _currentFilter = "all";

    /// <summary>При смене фильтра переприменяет фильтрацию списка.</summary>
    partial void OnCurrentFilterChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>Флаг: данные загружены впервые (используется для подавления лишних запросов).</summary>
    [ObservableProperty]
    private bool _isDataLoaded;

    /// <summary>Список удалённых из системы записей.</summary>
    [ObservableProperty]
    private ObservableCollection<RemovedEntry> _removedEntries = [];

    /// <summary>История изменений записей автозапуска.</summary>
    [ObservableProperty]
    private ObservableCollection<HistoryEntry> _historyEntries = [];

    /// <summary>Записи логов (для страницы «Логи»).</summary>
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = [];

    /// <summary>Текст поиска по логам.</summary>
    [ObservableProperty]
    private string _logSearchText = string.Empty;

    /// <summary>При изменении текста поиска по логам применяет фильтр.</summary>
    partial void OnLogSearchTextChanged(string value)
    {
        ApplyLogFilter();
    }

    /// <summary>Полный (неотфильтрованный) список записей логов.</summary>
    private List<LogEntry> _allLogEntries = [];

    /// <summary>Фильтрует _allLogEntries по _logSearchText и обновляет LogEntries.</summary>
    private void ApplyLogFilter()
    {
        if (string.IsNullOrWhiteSpace(LogSearchText))
        {
            LogEntries = new ObservableCollection<LogEntry>(_allLogEntries);
            return;
        }
        var filtered = _allLogEntries.Where(e =>
            e.FullLine.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase) ||
            e.Source.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase) ||
            e.Level.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        LogEntries = new ObservableCollection<LogEntry>(filtered);
    }

    [ObservableProperty]
    private string _newRuleValue = string.Empty;

    /// <summary>Доступные типы совпадений для правил чёрного списка.</summary>
    public List<string> MatchTypes =>
    [
        LocalizationService.Instance.MatchTypePath,
        LocalizationService.Instance.MatchTypePathWildcard,
        LocalizationService.Instance.MatchTypePublisher,
        LocalizationService.Instance.MatchTypeSHA256,
    ];

    /// <summary>Выбранный тип совпадения для нового правила.</summary>
    [ObservableProperty]
    private string _newRuleMatchType = "";

    /// <summary>Доступные действия для правил чёрного списка.</summary>
    public List<string> Actions =>
    [
        LocalizationService.Instance.ActionNotifyRemove,
        LocalizationService.Instance.ActionNotifyOnly,
    ];

    /// <summary>Выбранное действие для нового правила.</summary>
    [ObservableProperty]
    private string _newRuleAction = "";

    /// <summary>Событие запроса toast-уведомления (заголовок, текст, id записи).</summary>
    public event Action<string, string, string>? NotificationRequested;

    /// <summary>Запрос подтверждения опасных действий над системно значимыми компонентами Windows.</summary>
    public Func<StartupEntry, string, bool>? SystemDangerConfirmationRequested;

    /// <summary>Позволяет переопределить путь к ui-settings.json для изоляции тестов.</summary>
    internal static string? CustomConfigPath { get; set; }

    /// <summary>Путь к файлу конфигурации UI (ui-settings.json).</summary>
    internal static string FilterConfigPath => CustomConfigPath ?? System.IO.Path.Combine(
        AppContext.BaseDirectory,
        "ui-settings.json");

    /// <summary>Загружает тему из ui-settings.json. Возвращает "dark" по умолчанию.</summary>
    internal static string LoadThemeFromFile()
    {
        try
        {
            var path = FilterConfigPath;
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path).Trim();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("theme", out var v))
                    return v.GetString() ?? "dark";
            }
        }
        catch { }
        return "dark";
    }

    /// <summary>Загружает язык из ui-settings.json. Возвращает "ru" по умолчанию.</summary>
    internal static string LoadLanguageFromFile()
    {
        try
        {
            var path = FilterConfigPath;
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path).Trim();
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("language", out var v))
                    return v.GetString() ?? "ru";
            }
        }
        catch { }
        return "ru";
    }

    /// <summary>Загружает настройку автозапуска UI из файла конфигурации ui-settings.json (по умолчанию true).</summary>
    internal static bool LoadStartWithWindowsFromFile()
    {
        return LoadSetting("startWithWindows", true);
    }

    /// <summary>Создаёт MainViewModel, инициализирует опции и подписывается на события PipeClient.</summary>
    public MainViewModel(PipeClient pipeClient)
    {
        _pipeClient = pipeClient;
        InitOptions();
        _newRuleMatchType = LocalizationService.Instance.MatchTypePath;
        _newRuleAction = LocalizationService.Instance.ActionNotifyRemove;

        // Загружаем настройки
        _selectedThemeCode = LoadSetting("theme", "dark");
        _selectedLanguageCode = LoadSetting("language", "ru");
        _notifyOnNewEntry = LoadSetting("notifyOnNewEntry", true);
        _minimizeToTray = LoadSetting("minimizeToTray", true);
        _strictMode = LoadSetting("strictMode", false);
        _autoBlockOnRemove = LoadSetting("autoBlockOnRemove", false);
        _killOnDisable = LoadSetting("killOnDisable", false);
        _silentMode = LoadSetting("silentMode", false);
        _aiEnabled = LoadSetting("aiEnabled", true);
        _aiApiKey = LoadSetting("aiApiKey", string.Empty);
        _aiApiUrl = LoadSetting("aiApiUrl", "https://api.groq.com/openai/v1/chat/completions");
        _aiModel = LoadSetting("aiModel", "openai/gpt-oss-120b");
        _startWithWindows = LoadSetting("startWithWindows", true);
        _cpuThrottleEnabled = LoadSetting("cpuThrottleEnabled", false);
        _cpuThrottleThreshold = LoadSetting("cpuThrottleThreshold", 70);
        _telegramEnabled = LoadSetting("telegramEnabled", false);
        _telegramBotToken = LoadSetting("telegramBotToken", string.Empty);
        _telegramChatId = LoadSetting("telegramChatId", string.Empty);
        _discordWebhookEnabled = LoadSetting("discordWebhookEnabled", false);
        _discordWebhookUrl = LoadSetting("discordWebhookUrl", string.Empty);
        _remoteNotifyOnNewEntry = LoadSetting("remoteNotifyOnNewEntry", true);
        _remoteNotifyOnThreat = LoadSetting("remoteNotifyOnThreat", true);
        _remoteNotifyOnBlocked = LoadSetting("remoteNotifyOnBlocked", true);
        _kasperskyEnabled = LoadSetting("kasperskyEnabled", true);
        _kasperskyApiKey = LoadSetting("kasperskyApiKey", "Zem+jUJbQEiqHPpwsfjTyQ==");
        _kasperskyApiUrl = LoadSetting("kasperskyApiUrl", "https://opentip.kaspersky.com/api/v1/search/hash");
        _autoGuardMode = LoadSetting("autoGuardMode", "AuditOnly");
        _guardBlockDangerousCli = LoadSetting("guardBlockDangerousCli", false);
        _guardBlockTempPath = LoadSetting("guardBlockTempPath", false);
        _guardBlockScripts = LoadSetting("guardBlockScripts", false);
        _guardBlockUnsigned = LoadSetting("guardBlockUnsigned", false);
        _guardBlockNewServices = LoadSetting("guardBlockNewServices", false);
        _autoCheckUpdates = LoadSetting("autoCheckUpdates", true);
        _autoDownloadUpdates = LoadSetting("autoDownloadUpdates", false);
        _skippedUpdateVersion = LoadSetting("skippedUpdateVersion", (string?)null);
        RefreshCurrentVersionAndLastCheck();

        // Проверяем реестр на дубликаты и гарантируем единую каноническую точку автозапуска UI
        Services.AutostartService.EnsureSingleAutostartEntry(_startWithWindows);
        // Отправляем службе актуальные настройки (при первом подключении)
        _ = _pipeClient.SetKillOnDisableAsync(_killOnDisable);
        _ = _pipeClient.SetStrictModeAsync(_strictMode);
        _ = _pipeClient.SetCpuThrottleAsync(_cpuThrottleEnabled, _cpuThrottleThreshold);
        _ = _pipeClient.SetAiConfigAsync(_aiApiKey, _aiApiUrl, _aiModel);
        _ = _pipeClient.SetKasperskyConfigAsync(_kasperskyEnabled, _kasperskyApiKey, _kasperskyApiUrl);
        _ = _pipeClient.SetUpdateSettingsAsync(new UpdateSettingsPayload(_autoCheckUpdates, _autoDownloadUpdates, _skippedUpdateVersion));
        SyncRemoteNotifications();
        SyncAutoGuardConfig();

        // Таймер периодического обновления текущей загрузки CPU (каждую 1 секунду).
        // П. 2.1: замеряем только UI-процесс + сервис по PID, без тяжёлого Process.GetProcesses().
        _cpuUsageTimer = new System.Threading.Timer(_ =>
        {
            if (_cpuTimerSuspended) return;
            try
            {
                var svcPid = ServicePid;
                int[] pids = svcPid.HasValue && svcPid.Value > 0 && svcPid.Value != _uiPid
                    ? [_uiPid, svcPid.Value]
                    : [_uiPid];
                var (total, external) = CpuUsageHelper.GetCpuUsagePairForPids(pids);
                var format = LocalizationService.Instance.CurrentCpuUsageFormat;
                var text = string.Format(System.Globalization.CultureInfo.CurrentUICulture, format, total, external);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentCpuUsageText = text;
                    CpuUsagePercentText = $"{total:F1}%";
                    CpuUsageDetailText = string.Format(System.Globalization.CultureInfo.CurrentUICulture, LocalizationService.Instance["CpuExternalFormat"], external.ToString("F1"));
                });
            }
            catch { }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        CurrentFilter = LoadFilter();
        LoadSort();
        TaskManager = new TaskManagerViewModel(_pipeClient);
        _pipeClient.Connected += OnPipeClientConnected;
        _pipeClient.EventsReceived += OnPipeClientEventsReceived;
        _pipeClient.Disconnected += OnPipeClientDisconnected;
        AppIconService.IconLoaded += OnIconLoaded;
    }

    /// <summary>
    /// Обработчик события асинхронной загрузки иконки: уведомляет привязки UI для обновления строки без сброса скролла.
    /// </summary>
    private void OnIconLoaded(string path, System.Windows.Media.ImageSource icon)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.BeginInvoke(() =>
        {
            lock (_entriesLock)
            {
                foreach (var entry in _allEntries)
                {
                    if (string.Equals(entry.FilePath, path, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(AppIconService.FastCleanPath(entry.FilePath), path, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.RefreshIcon();
                    }
                }
            }
        });
    }

    /// <summary>OnPipeClientConnected: Обрабатывает событие OnPipeClientConnected.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object?).</param>
    /// <param name="args">Входные данные: значение параметра args (EventArgs).</param>
    private async void OnPipeClientConnected(object? sender, EventArgs args)
    {
        App.Log("MainViewModel: Connected event");
        IsConnected = true;
        _ = UpdateServiceInfoAsync();

        // Синхронизируем настройки со службой при каждом подключении
        _ = _pipeClient.SetKillOnDisableAsync(KillOnDisable);
        _ = _pipeClient.SetStrictModeAsync(StrictMode);
        _ = _pipeClient.SetCpuThrottleAsync(CpuThrottleEnabled, CpuThrottleThreshold);
        _ = _pipeClient.SetLanguageAsync(SelectedLanguageCode);
        SyncRemoteNotifications();
        SyncAutoGuardConfig();

        if (!IsDataLoaded)
        {
            App.Log("MainViewModel: Connected → LoadDataAsync (IsDataLoaded=false)");
            await LoadDataAsync();
        }
        else
        {
            App.Log("MainViewModel: Connected → IsDataLoaded=true, запускаю polling");
        }
        _networkAlertPollerTimer?.Dispose();
        _networkAlertPollerTimer = null;
        _pollingTimer?.Dispose();
        if (!IsMonitoringPaused)
        {
            _pollingTimer = new Timer(async _ => await LoadDataIfChanged(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
        StartNetworkAlertPoller();
        // Автообновление счётчика сохранённых снимков при подключении к службе
        _ = RefreshSavedSnapshotsCountAsync();
        App.Log("MainViewModel: Connected handler завершён");
    }

    /// <summary>OnPipeClientEventsReceived: Обрабатывает событие OnPipeClientEventsReceived.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object?).</param>
    /// <param name="batch">Входные данные: значение параметра batch (List<PipeMessage>).</param>
    private async void OnPipeClientEventsReceived(object? sender, List<PipeMessage> batch)
    {
        if (batch.Any(m => m.Type == PipeMessageTypes.EntryAdded
            || m.Type == PipeMessageTypes.EntryModified
            || m.Type == PipeMessageTypes.EntryDeleted
            || m.Type == PipeMessageTypes.BlacklistMatch))
        {
            await LoadDataIfChanged();
        }

        // Изменение глобального состояния мониторинга (Pause / Resume)
        foreach (var m in batch.Where(m => m.Type == PipeMessageTypes.MonitoringStateChanged))
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var p = System.Text.Json.JsonSerializer.Deserialize<MonitoringStatePayload>(m.Payload, options);
                if (p != null)
                {
                    App.Log($"[EventPipe] MonitoringStateChanged received: IsPaused={p.IsPaused}");
                    if (System.Windows.Application.Current?.Dispatcher != null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            IsMonitoringPaused = p.IsPaused;
                        });
                    }
                    else
                    {
                        IsMonitoringPaused = p.IsPaused;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"Ошибка обработки MonitoringStateChanged: {ex.Message}");
            }
        }

        // Предупреждение об авто-блокировке записи из чёрного списка (удалил процесс/запись)
        if (SilentMode) return;
        foreach (var m in batch.Where(m => m.Type == PipeMessageTypes.BlacklistMatch))
        {
            try
            {
                var e = System.Text.Json.JsonSerializer.Deserialize<StartupEntry>(m.Payload);
                if (e == null) continue;
                NotificationRequested?.Invoke(
                    LocalizationService.Instance.BlacklistNotifTitle,
                    LocalizationService.Instance.GetBlacklistNotifText(e.Name ?? ""),
                    e.Id);
            }
            catch { }
        }

        // Уведомление о выполненных действиях (отключено, процесс завершён, удалено, восстановлено, доступно обновление)
        foreach (var m in batch.Where(m => m.Type == PipeMessageTypes.ActionNotice))
        {
            try
            {
                var p = System.Text.Json.JsonSerializer.Deserialize<ActionNoticePayload>(m.Payload);
                if (p == null) continue;

                if (p.Action == "update_available")
                {
                    NotificationRequested?.Invoke(
                        LocalizationService.Instance.UpdateDialogTitle,
                        p.Name ?? "Доступна новая версия Startup Monitor",
                        string.Empty);

                    _ = System.Windows.Application.Current?.Dispatcher?.InvokeAsync(async () =>
                    {
                        var res = await _pipeClient.CheckForUpdatesAsync();
                        if (res?.UpdateInfo != null && IsUpdateEffectivelyAvailable(res.UpdateInfo))
                        {
                            ShowUpdateAvailableDialog(res.UpdateInfo);
                        }
                    });
                }
                else
                {
                    NotificationRequested?.Invoke(
                        LocalizationService.Instance.ActionNotifTitle,
                        LocalizationService.Instance.GetActionNotifText(p.Action, p.Name ?? ""),
                        string.Empty);
                }
            }
            catch { }
        }
    }

    /// <summary>OnPipeClientDisconnected: Обрабатывает событие OnPipeClientDisconnected.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object?).</param>
    /// <param name="args">Входные данные: значение параметра args (EventArgs).</param>
    private async void OnPipeClientDisconnected(object? sender, EventArgs args)
    {
        App.Log("MainViewModel: Disconnected event");
        IsConnected = false;
        _networkAlertPollerTimer?.Dispose();
        _networkAlertPollerTimer = null;
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        StopNetworkAlertPoller();
        UpdateServiceStatusText();
        await Task.Delay(3000);
        App.Log("MainViewModel: Disconnected → ConnectAsync");
        _ = ConnectAsync();
    }

    /// <summary>Обновляет отображаемую текстовую строку статуса службы.</summary>
    public void UpdateServiceStatusText()
    {
        var loc = LocalizationService.Instance;
        if (!IsConnected)
        {
            ServiceStatusDetails = loc["StatusServiceStopped"];
            return;
        }

        if (IsMonitoringPaused)
        {
            ServiceStatusDetails = string.Format(loc["StatusServicePaused"], ServiceVersion, ServicePid?.ToString() ?? "?");
        }
        else
        {
            ServiceStatusDetails = string.Format(loc["StatusServiceActive"], ServiceVersion, ServicePid?.ToString() ?? "?");
        }
    }

    /// <summary>Запрашивает актуальную информацию о процессе службы (PID, версия, статус).</summary>
    public async Task UpdateServiceInfoAsync()
    {
        try
        {
            var info = await _pipeClient.GetServiceInfoAsync();
            if (info != null)
            {
                ServicePid = info.Pid;
                ServiceVersion = info.Version;
                IsMonitoringPaused = info.IsPaused;
            }
        }
        catch (Exception ex)
        {
            App.Log($"UpdateServiceInfoAsync error: {ex.Message}");
        }
        finally
        {
            UpdateServiceStatusText();
        }
    }

    /// <summary>Переключает состояние глобальной паузы мониторинга автозапуска.</summary>
    [RelayCommand]
    public async Task TogglePauseMonitoringAsync()
    {
        if (Interlocked.Exchange(ref _pauseToggling, 1) == 1) return;
        try
        {
            var targetPaused = !IsMonitoringPaused;
            App.Log($"[TogglePauseMonitoringAsync] Запрос переключения: targetPaused={targetPaused} (текущий IsMonitoringPaused={IsMonitoringPaused})");

            bool ok = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                ok = targetPaused
                    ? await _pipeClient.PauseMonitoringAsync()
                    : await _pipeClient.ResumeMonitoringAsync();

                if (ok) break;

                App.Log($"[TogglePauseMonitoringAsync] Попытка {attempt} вернула false, повтор через 200мс...");
                await Task.Delay(200);
            }

            if (!ok)
            {
                App.Log($"[TogglePauseMonitoringAsync] Не удалось переключить мониторинг после 3 попыток. Запрос актуального состояния.");
                await UpdateServiceInfoAsync();
                return;
            }

            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsMonitoringPaused = targetPaused;
                });
            }
            else
            {
                IsMonitoringPaused = targetPaused;
            }
            App.Log($"[TogglePauseMonitoringAsync] Успешно переключено: IsMonitoringPaused={IsMonitoringPaused}");
        }
        catch (Exception ex)
        {
            App.Log($"TogglePauseMonitoringAsync error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _pauseToggling, 0);
        }
    }

    /// <summary>Устанавливает начальные данные (записи + чёрный список) при первой загрузке.</summary>
    public void SetInitialData(List<StartupEntry> entries, List<BlacklistRule> rules)
    {
        _entriesRevision = 0; // ревизия неизвестна — первый дельта-опрос вернёт полный список + ревизию
        lock (_entriesLock) { _allEntries = entries; }
        _previousEntryIds = entries.Select(e => e.Id).ToHashSet();
        _isFirstPoll = true;
        ApplyBlacklistToEntries(rules);
        ApplySort();
        RefreshStats();
        ApplyFilter();
        Blacklist = new ObservableCollection<BlacklistRule>(rules);
        IsDataLoaded = true;
    }

    /// <summary>Проверяет, изменились ли данные, и при необходимости обновляет список и уведомляет о новых записях.</summary>
    private async Task LoadDataIfChanged()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Main.LoadDataIfChanged");
        // Защита от реэнтрантности: тик таймера + событие event-pipe могут совпасть
        if (Interlocked.Exchange(ref _isLoading, 1) == 1) return;
        if (_pauseToggling == 1)
        {
            Interlocked.Exchange(ref _isLoading, 0);
            return;
        }
        try
        {
            // Дельта-опрос: при отсутствии изменений сервер отвечает NotModified —
            // мгновенный выход (0.5–1 мс вместо 123 мс, 0 аллокаций в LOH).
            // Ревизия покрывает записи + чёрный список, поэтому пропускаем оба запроса.
            var (isModified, newRevision, deltaEntries) = await _pipeClient.GetEntriesDeltaAsync(_entriesRevision);
            if (!isModified)
                return;

            _entriesRevision = newRevision;
            var entries = deltaEntries ?? await _pipeClient.GetAllEntriesAsync();
            var rules = await _pipeClient.GetBlacklistAsync();

            if (entries.Count == 0)
            {
                int currentCount;
                lock (_entriesLock) { currentCount = _allEntries.Count; }
                App.Log($"LoadDataIfChanged: сервис вернул 0 записей — пропускаю, чтобы не затереть {currentCount} записей");
                return;
            }

            if (HasChanges(entries, rules))
            {
                int oldCount;
                lock (_entriesLock) { oldCount = _allEntries.Count; }
                App.Log($"LoadDataIfChanged: изменения — было {oldCount}, стало {entries.Count} записей");
                var newIds = entries.Select(e => e.Id).ToHashSet();
                if (NotifyOnNewEntry && !_isFirstPoll && !SilentMode)
                {
                    foreach (var entry in entries)
                    {
                        if (!_previousEntryIds.Contains(entry.Id))
                        {
                            var locDisplay = new Converters.LocationToDisplayConverter().Convert(entry.Location, typeof(string), null, System.Globalization.CultureInfo.CurrentUICulture) as string ?? LocalizationService.Instance.LocUnknown;
                            NotificationRequested?.Invoke(
                                LocalizationService.Instance.NotifTitle,
                                $"{entry.Name} ({locDisplay})",
                                entry.Id);
                        }
                    }
                }
                _isFirstPoll = false;

                lock (_entriesLock) { _allEntries = entries; }
                _previousEntryIds = newIds;
                ApplyBlacklistToEntries(Blacklist.ToList());
                ApplySort();

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() =>
                    {
                        ApplyFilter();
                        Blacklist = new ObservableCollection<BlacklistRule>(rules);
                    });
                    _ = System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => RefreshStats());
                }
                else
                {
                    ApplyFilter();
                    Blacklist = new ObservableCollection<BlacklistRule>(rules);
                    if (dispatcher != null)
                        _ = dispatcher.InvokeAsync(() => RefreshStats());
                    else
                        RefreshStats();
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"LoadDataIfChanged: ошибка — {ex.Message}");
        }
        finally
        {
            _isLoading = 0;
        }
    }

    /// <summary>Проставляет статус Blocked записям, совпадающим с правилами чёрного списка по пути.</summary>
    private void ApplyBlacklistToEntries(List<BlacklistRule> rules)
    {
        var pathRules = rules.Where(r => r.MatchType == BlacklistMatchType.Path)
            .Select(r => r.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        StartupEntry[] snapshot;
        lock (_entriesLock) { snapshot = _allEntries.ToArray(); }
        foreach (var entry in snapshot)
        {
            if (!string.IsNullOrEmpty(entry.FilePath) && pathRules.Contains(entry.FilePath))
            {
                entry.Status = EntryStatus.Blocked;
                entry.FromBlacklist = true;
            }
        }
    }

    /// <summary>Сравнивает текущие данные с полученными — есть ли различия.</summary>
    /// <returns>true, если количество или содержимое записей/правил изменилось.</returns>
    private bool HasChanges(List<StartupEntry> entries, List<BlacklistRule> rules)
    {
        using var _prof = ExecutionProfiler.Measure("UI.Main.HasChanges");
        StartupEntry[] snapshot;
        lock (_entriesLock) { snapshot = _allEntries.ToArray(); }
        if (entries.Count != snapshot.Length || rules.Count != Blacklist.Count)
            return true;

        var oldById = snapshot.ToDictionary(e => e.Id);
        foreach (var e in entries)
        {
            if (!oldById.TryGetValue(e.Id, out var old))
                return true;
            if (old.FilePath != e.FilePath)
                return true;
            if (old.Status != e.Status)
                return true;
            if (old.AiRating != e.AiRating)
                return true;
            if (old.KasperskyZone != e.KasperskyZone)
                return true;
            if (old.ThreatInfo?.ThreatLevel != e.ThreatInfo?.ThreatLevel ||
                old.ThreatInfo?.HasThreat != e.ThreatInfo?.HasThreat ||
                old.ThreatInfo?.BadgeText != e.ThreatInfo?.BadgeText)
                return true;
        }

        return false;
    }

    /// <summary>Обновляет статистику (Total, Enabled, Blocked) на основе _allEntries.</summary>
    private void RefreshStats()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Main.RefreshStats");
        StartupEntry[] snapshot;
        lock (_entriesLock) { snapshot = _allEntries.ToArray(); }
        TotalCount = snapshot.Length;
        EnabledCount = snapshot.Count(e => e.Status == EntryStatus.Enabled);
        BlockedCount = snapshot.Count(e => e.Status == EntryStatus.Blocked);
    }

    /// <summary>Активен ли режим сравнения с контрольным снимком («Было — Стало»).</summary>
    [ObservableProperty]
    private bool _isSnapshotComparisonActive;

    /// <summary>Имя активного снимка для сравнения.</summary>
    [ObservableProperty]
    private string? _activeSnapshotName;

    /// <summary>Текст информации о результатах сравнения со снимком.</summary>
    [ObservableProperty]
    private string? _activeSnapshotComparisonText;

    /// <summary>Количество сохранённых контрольных снимков системы (для бейджа в навигации).</summary>
    [ObservableProperty]
    private int _savedSnapshotsCount;

    /// <summary>Список Id новых записей со снимка.</summary>
    public HashSet<string> SnapshotAddedEntryIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Словарь Id измененных записей и краткой сводки изменений со снимка.</summary>
    public Dictionary<string, string> SnapshotModifiedEntrySummaries { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Аппаратные и системные характеристики компьютера.</summary>
    [ObservableProperty]
    private SystemComputerInfo _computerInfo = SystemComputerInfo.Collect();

    /// <summary>Событие запроса открытия окна системной информации о компьютере.</summary>
    public event Action? OpenSystemInfoRequested;

    /// <summary>Открывает диалоговое окно полной системной информации о компьютере.</summary>
    [RelayCommand]
    public void OpenSystemInfo()
    {
        // При открытии моментально обновляем динамические параметры (RAM, аптайм, диски)
        ComputerInfo = SystemComputerInfo.Collect();
        OpenSystemInfoRequested?.Invoke();
    }

    /// <summary>Событие запроса открытия окна контрольных снимков системы.</summary>
    public event Action? OpenSnapshotsRequested;

    /// <summary>Открывает окно управления и сравнения контрольных снимков системы.</summary>
    [RelayCommand]
    public void OpenSnapshots()
    {
        OpenSnapshotsRequested?.Invoke();
    }

    /// <summary>Обновляет счётчик сохранённых снимков через службу (для бейджа в левой навигации).</summary>
    public async Task RefreshSavedSnapshotsCountAsync()
    {
        try
        {
            var snapshots = await _pipeClient.GetSnapshotsAsync();
            SavedSnapshotsCount = snapshots?.Count ?? 0;
        }
        catch
        {
            // Счётчик некритичен: при недоступности службы оставляем предыдущее значение.
        }
    }

    /// <summary>Сбрасывает режим сравнения со снимком, очищая бейджи и восстанавливая стандартную фильтрацию.</summary>
    [RelayCommand]
    public void ResetSnapshotComparison()
    {
        IsSnapshotComparisonActive = false;
        ActiveSnapshotName = null;
        ActiveSnapshotComparisonText = null;
        SnapshotAddedEntryIds.Clear();
        SnapshotModifiedEntrySummaries.Clear();

        foreach (var entry in _allEntries)
        {
            entry.SnapshotDiffBadge = null;
            entry.SnapshotDiffColor = null;
            entry.SnapshotDiffTooltip = null;
        }

        ApplyFilter();
    }

    /// <summary>Применяет дифференциальные результаты сравнения со снимком («Было — Стало»).</summary>
    public void ApplySnapshotComparison(SnapshotComparisonResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        IsSnapshotComparisonActive = true;
        ActiveSnapshotName = diff.SnapshotName;
        ActiveSnapshotComparisonText = LocalizationService.Instance.GetSnapshotActiveComparison(
            diff.SnapshotName, diff.AddedEntries.Count, diff.ModifiedEntries.Count);

        SnapshotAddedEntryIds.Clear();
        foreach (var a in diff.AddedEntries)
        {
            SnapshotAddedEntryIds.Add(a.Id);
        }

        SnapshotModifiedEntrySummaries.Clear();
        foreach (var m in diff.ModifiedEntries)
        {
            SnapshotModifiedEntrySummaries[m.CurrentEntry.Id] = m.Summary;
        }

        // Обновляем бейджи на элементах
        foreach (var entry in _allEntries)
        {
            if (SnapshotAddedEntryIds.Contains(entry.Id))
            {
                entry.SnapshotDiffBadge = LocalizationService.Instance.SnapshotBadgeNew;
                entry.SnapshotDiffColor = "#10B981";
                entry.SnapshotDiffTooltip = LocalizationService.Instance["SnapshotNewTooltip"];
            }
            else if (SnapshotModifiedEntrySummaries.TryGetValue(entry.Id, out var summary))
            {
                entry.SnapshotDiffBadge = LocalizationService.Instance.SnapshotBadgeModified;
                entry.SnapshotDiffColor = "#3B82F6";
                entry.SnapshotDiffTooltip = string.Format(LocalizationService.Instance["SnapshotModifiedTooltip"], summary);
            }
            else
            {
                entry.SnapshotDiffBadge = null;
                entry.SnapshotDiffColor = null;
                entry.SnapshotDiffTooltip = null;
            }
        }

        ApplyFilter();
    }

    /// <summary>Фильтрует Entries по текущему фильтру (CurrentFilter) и поисковому запросу (SearchText).</summary>
    private void ApplyFilter()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Main.ApplyFilter");
        StartupEntry[] snapshot;
        lock (_entriesLock) { snapshot = _allEntries.ToArray(); }
        var filtered = snapshot.AsEnumerable();

        if (IsSnapshotComparisonActive)
        {
            filtered = filtered.Where(e => SnapshotAddedEntryIds.Contains(e.Id) || SnapshotModifiedEntrySummaries.ContainsKey(e.Id));
        }
        else
        {
            if (CurrentFilter == "registry")
                filtered = filtered.Where(e => e.Location is
                    EntryLocation.RegistryRun or
                    EntryLocation.RegistryRunOnce or
                    EntryLocation.RegistryRunServices or
                    EntryLocation.RegistryPolicies);
            else if (CurrentFilter == "folder")
                filtered = filtered.Where(e => e.Location == EntryLocation.StartupFolder);
            else if (CurrentFilter == "services")
                filtered = filtered.Where(e => e.Location == EntryLocation.Service);
            else if (CurrentFilter == "tasks")
                filtered = filtered.Where(e => e.Location == EntryLocation.TaskScheduler);

            if (CurrentFilter == "other")
                filtered = filtered.Where(e => e.Location is not (
                    EntryLocation.RegistryRun or EntryLocation.RegistryRunOnce
                    or EntryLocation.RegistryRunServices or EntryLocation.RegistryPolicies
                    or EntryLocation.StartupFolder or EntryLocation.Service
                    or EntryLocation.TaskScheduler));

            if (CurrentFilter == "recent")
                filtered = filtered.Where(e => e.DetectedAt >= DateTime.UtcNow.AddHours(-24));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(e =>
                e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                FilteredEntries = new ObservableCollection<StartupEntry>(list);
            });
        }
        else
        {
            FilteredEntries = new ObservableCollection<StartupEntry>(list);
        }
    }

    /// <summary>Подключается к службе: запускает службу при необходимости и открывает pipe-соединение.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            await ServiceManager.EnsureServiceRunningAsync();
            await _pipeClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка подключения: {ex.Message}");
        }
    }

    /// <summary>Загружает данные (записи + чёрный список) и запускает фоновые циклы рейтингов и хешей.</summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Main.LoadDataAsync");
        try
        {
            // Первичная загрузка через дельту (ClientRevision=0): сервер вернёт полный список + ревизию
            var (isModified, newRevision, deltaEntries) = await _pipeClient.GetEntriesDeltaAsync(0);
            var entries = (isModified && deltaEntries != null)
                ? deltaEntries
                : await _pipeClient.GetAllEntriesAsync();
            if (isModified)
                _entriesRevision = newRevision;
            var rules = await _pipeClient.GetBlacklistAsync();

            if (entries.Count == 0)
            {
                App.Log("LoadDataAsync: сервис вернул 0 записей — оставляю текущие");
                if (Blacklist.Count == 0 && rules.Count > 0)
                    Blacklist = new ObservableCollection<BlacklistRule>(rules);
                return;
            }

            lock (_entriesLock) { _allEntries = entries; }
            _previousEntryIds = entries.Select(e => e.Id).ToHashSet();
            ApplySort();
            RefreshStats();
            ApplyFilter();
            Blacklist = new ObservableCollection<BlacklistRule>(rules);
            IsDataLoaded = true;

            // Отменяем предыдущие фоновые циклы — иначе они плодятся при переподключениях
            _backgroundCts?.Cancel();
            _backgroundCts = new CancellationTokenSource();
            var ct = _backgroundCts.Token;
            _ = LoadRatingsAsync(entries, ct);
            _ = LoadHashDataAsync(entries, ct);
        }
        catch { }
    }

    /// <summary>Фоновый опрос AI-рейтингов (каждые 5 с, макс 3 мин) до полной загрузки.</summary>
    private async Task LoadRatingsAsync(List<StartupEntry> entries, CancellationToken ct)
    {
        if (!AiEnabled) return;
        try
        {
            var totalEntries = entries.Count;
            var lastLoadedCount = -1;

            // Максимум ~3 минуты ожидания — без API-ключа рейтинги никогда не появятся
            for (var attempt = 0; attempt < 36 && !ct.IsCancellationRequested; attempt++)
            {
                var ratings = await _pipeClient.GetRatingsAsync();
                var loadedCount = 0;
                foreach (var entry in entries)
                {
                    if (ratings.TryGetValue(entry.Id, out var r))
                    {
                        entry.AiCategory = r.Category;
                        entry.AiRisk = r.Risk;
                        entry.AiRecommendation = r.Recommendation;
                        // Храним реальный score модели (1-10) — конвертер звёзд сам маппит
                        entry.AiRating = r.Score;
                        loadedCount++;
                    }
                }

                if (loadedCount != lastLoadedCount)
                {
                    App.Log($"LoadRatingsAsync: {loadedCount}/{totalEntries} рейтингов загружено");
                    lastLoadedCount = loadedCount;
                    ApplyFilter();
                }

                if (loadedCount >= totalEntries) break;
                await Task.Delay(5000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadRatingsAsync error: {ex.Message}");
        }
    }

    /// <summary>Фоновый опрос хеш-данных (CIRCL/Kaspersky) до полной загрузки (макс 60 с).</summary>
    private async Task LoadHashDataAsync(List<StartupEntry> entries, CancellationToken ct)
    {
        try
        {
            // Ждём пока фоновый HashScanLoop обработает хеши
            for (var i = 0; i < 12 && !ct.IsCancellationRequested; i++) // до 60 секунд
            {
                await Task.Delay(5000, ct);

                var updatedEntries = await _pipeClient.GetAllEntriesAsync();
                var hasChanges = false;
                foreach (var updated in updatedEntries)
                {
                    var existing = entries.Find(e => e.Id == updated.Id);
                    if (existing != null &&
                        existing.KasperskyZone != updated.KasperskyZone)
                    {
                        existing.KasperskyZone = updated.KasperskyZone;
                        existing.KasperskyDetection = updated.KasperskyDetection;
                        existing.KasperskyFileStatus = updated.KasperskyFileStatus;
                        hasChanges = true;
                    }
                }

                if (hasChanges)
                {
                    App.Log($"LoadHashData: обновлены хеш-данные для записей");
                    ApplyFilter();
                }

                // Проверяем, все ли записи обработаны
                if (entries.All(e => !string.IsNullOrEmpty(e.KasperskyZone)))
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadHashData error: {ex.Message}");
        }
    }

    /// <summary>Сортирует по имени (RelayCommand для UI).</summary>
    [RelayCommand]
    private void SortByName() => SortBy("name");

    /// <summary>Сортирует по дате обнаружения.</summary>
    [RelayCommand]
    private void SortByDate() => SortBy("date");

    /// <summary>Сортирует по издателю.</summary>
    [RelayCommand]
    private void SortByPublisher() => SortBy("publisher");

    /// <summary>Сортирует по влиянию на запуск.</summary>
    [RelayCommand]
    private void SortByImpact() => SortBy("impact");

    /// <summary>Сортирует по статусу.</summary>
    [RelayCommand]
    private void SortByStatus() => SortBy("status");

    /// <summary>Сортирует по AI-рейтингу.</summary>
    [RelayCommand]
    private void SortByRating() => SortBy("rating");

    /// <summary>Сортирует по зоне CIRCL.</summary>
    [RelayCommand]
    private void SortByKaspersky() => SortBy("kaspersky");

    /// <summary>Устанавливает фильтр и сохраняет его в настройки.</summary>
    private void SetFilter(string filter)
    {
        CurrentFilter = filter;
        SaveFilter(filter);
    }

    /// <summary>Возвращает числовой вес влияния на запуск для сортировки: Высокое (3) → Среднее (2) → Низкое (1).</summary>
    private static int ImpactSeverity(EntryLocation loc) => loc switch
    {
        EntryLocation.BootExecute or EntryLocation.AppInitDll or EntryLocation.Driver
            or EntryLocation.ImageFileExecutionOptions or EntryLocation.Drivers32 => 3,
        EntryLocation.Service or EntryLocation.Winlogon or EntryLocation.TaskScheduler
            or EntryLocation.WmiEventSubscription or EntryLocation.ActiveSetup => 2,
        _ => 1
    };

    /// <summary>Возвращает числовой вес зоны CIRCL для сортировки: Red (3) → Green (0).</summary>
    private static int CirclSeverity(string? zone) => zone switch
    {
        "Red" => 3,
        "Yellow" => 2,
        "Grey" => 1,
        "Green" => 0,
        _ => -1,
    };

    /// <summary>Фильтр: показать все записи.</summary>
    [RelayCommand]
    private void FilterAll() => SetFilter("all");

    /// <summary>Фильтр: только записи из реестра.</summary>
    [RelayCommand]
    private void FilterRegistry() => SetFilter("registry");

    /// <summary>Фильтр: только записи из папки автозапуска.</summary>
    [RelayCommand]
    private void FilterFolder() => SetFilter("folder");

    /// <summary>Фильтр: только службы.</summary>
    [RelayCommand]
    private void FilterServices() => SetFilter("services");

    /// <summary>Фильтр: только задачи планировщика.</summary>
    [RelayCommand]
    private void FilterTasks() => SetFilter("tasks");

    /// <summary>Фильтр: записи, добавленные за последние 24 часа.</summary>
    [RelayCommand]
    private void FilterRecent() => SetFilter("recent");

    /// <summary>Фильтр: прочие расположения (не реестр/папка/служба/планировщик).</summary>
    [RelayCommand]
    private void FilterOther() => SetFilter("other");

    /// <summary>Загружает сохранённый фильтр из настроек.</summary>
    private static string LoadFilter()
    {
        return LoadSetting("filter", "all");
    }

    /// <summary>Сохраняет выбранный фильтр в настройки.</summary>
    private static void SaveFilter(string filter)
    {
        SaveSetting("filter", filter);
    }

    /// <summary>Загружает настройки сортировки (колонка + направление) из файла.</summary>
    private void LoadSort()
    {
        _sortColumn = LoadSetting<string>("sortColumn") ?? "name";
        _sortAscending = LoadSetting("sortAscending", true);
    }

    /// <summary>Сохраняет текущие настройки сортировки в JSON-файл.</summary>
    private void SaveSort()
    {
        try
        {
            var path = FilterConfigPath;
            var json = System.IO.File.Exists(path)
                ? System.IO.File.ReadAllText(path)
                : "{}";
            var obj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? [];
            obj["sortColumn"] = _sortColumn;
            obj["sortAscending"] = _sortAscending;
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(obj));
        }
        catch { }
    }

    /// <summary>Загружает настройку из ui-settings.json с возвратом значения по умолчанию при отсутствии.</summary>
    internal static T LoadSetting<T>(string key, T defaultValue)
    {
        lock (_settingsLock)
        {
            try
            {
                var path = FilterConfigPath;
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path).Trim();
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty(key, out var v))
                    {
                        if (typeof(T) == typeof(bool) && v.ValueKind == System.Text.Json.JsonValueKind.True)
                            return (T)(object)true;
                        if (typeof(T) == typeof(bool) && v.ValueKind == System.Text.Json.JsonValueKind.False)
                            return (T)(object)false;
                        if (typeof(T) == typeof(int) && v.TryGetInt32(out var intVal))
                            return (T)(object)intVal;
                        if (typeof(T) == typeof(double) && v.TryGetDouble(out var dVal))
                            return (T)(object)dVal;
                        if (typeof(T) == typeof(string))
                            return (T)(object)(v.GetString() ?? (string)(object)defaultValue!);
                    }
                }
            }
            catch { }
            return defaultValue;
        }
    }

    /// <summary>Загружает настройку с nullable-возвратом (только для string).</summary>
    internal static T? LoadSetting<T>(string key) where T : class
    {
        lock (_settingsLock)
        {
            try
            {
                var path = FilterConfigPath;
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path).Trim();
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty(key, out var v))
                    {
                        if (typeof(T) == typeof(string))
                            return (T)(object)(v.GetString() ?? "");
                    }
                }
            }
            catch { }
            return null;
        }
    }

    /// <summary>Объект блокировки для потокобезопасного read-modify-write ui-settings.json.</summary>
    private static readonly object _settingsLock = new();

    /// <summary>Сохраняет настройку в ui-settings.json (read-modify-write под блокировкой, атомарная запись).</summary>
    internal static void SaveSetting(string key, object value)
    {
        // read-modify-write всего файла — сериализуем, иначе параллельные сохранения затирают друг друга
        lock (_settingsLock)
        {
            try
            {
                var path = FilterConfigPath;
                var json = System.IO.File.Exists(path)
                    ? System.IO.File.ReadAllText(path)
                    : "{}";
                var obj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? [];
                obj[key] = value;
                var tmp = path + ".tmp";
                System.IO.File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(obj));
                System.IO.File.Move(tmp, path, overwrite: true);
            }
            catch { }
        }
    }

    /// <summary>Отключает запись (EntryStatus.Disabled) через службу.</summary>
    [RelayCommand]
    private async Task DisableEntryAsync(string entryId)
    {
        await _pipeClient.DisableEntryAsync(entryId);
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null) entry.Status = EntryStatus.Disabled;
        RefreshStats();
        ApplyFilter();
    }

    /// <summary>Включает запись (EntryStatus.Enabled) через службу.</summary>
    [RelayCommand]
    private async Task EnableEntryAsync(string entryId)
    {
        await _pipeClient.EnableEntryAsync(entryId);
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null) entry.Status = EntryStatus.Enabled;
        RefreshStats();
        ApplyFilter();
    }

    /// <summary>Переключает состояние записи: Blocked → Enable, Delayed → Enable, Enabled → Disable, Disabled → Enable.</summary>
    [RelayCommand]
    private async Task ToggleEntryAsync(string entryId)
    {
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) return;
        if (entry.Status == EntryStatus.Blocked)
            await UnblockEntryAsync(entry);
        else if (entry.Status == EntryStatus.Delayed)
            await CancelDelayedEntryAsync(entryId);
        else if (entry.Status == EntryStatus.Enabled)
        {
            if (SystemProtectionHelper.IsProtectedSystemEntry(entry))
            {
                if (SystemDangerConfirmationRequested?.Invoke(entry, "Disable") == false)
                    return;
            }
            await DisableEntryAsync(entryId);
        }
        else
            await EnableEntryAsync(entryId);
    }

    /// <summary>Переводит запись в режим отложенного запуска с указанным интервалом задержки.</summary>
    [RelayCommand]
    public async Task SetDelayedEntryAsync(string entryIdWithDelay)
    {
        string entryId = entryIdWithDelay;
        int delay = 30;
        if (entryIdWithDelay.Contains('|'))
        {
            var parts = entryIdWithDelay.Split('|');
            entryId = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out var parsedDelay))
                delay = parsedDelay;
        }

        var (ok, err) = await _pipeClient.SetDelayedEntryAsync(entryId, delay);
        if (ok)
        {
            var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
            if (entry != null)
            {
                entry.Status = EntryStatus.Delayed;
                entry.DelaySeconds = delay;
            }
            RefreshStats();
            ApplyFilter();
        }
        else if (!string.IsNullOrEmpty(err))
        {
            NotificationRequested?.Invoke(LocalizationService.Instance["NotifDelayedStartup"], err, entryId);
        }
    }

    /// <summary>Отменяет отложенный запуск записи и возвращает обычный автозапуск.</summary>
    [RelayCommand]
    public async Task CancelDelayedEntryAsync(string entryId)
    {
        var (ok, err) = await _pipeClient.CancelDelayedEntryAsync(entryId);
        if (ok)
        {
            var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
            if (entry != null)
            {
                entry.Status = EntryStatus.Enabled;
            }
            RefreshStats();
            ApplyFilter();
        }
        else if (!string.IsNullOrEmpty(err))
        {
            NotificationRequested?.Invoke(LocalizationService.Instance["NotifDelayedStartup"], err, entryId);
        }
    }

    /// <summary>Выполняет ручной тестовый запуск отложенной программы.</summary>
    [RelayCommand]
    public async Task LaunchDelayedEntryAsync(string entryId)
    {
        var (ok, msg) = await _pipeClient.LaunchDelayedEntryAsync(entryId);
        NotificationRequested?.Invoke(LocalizationService.Instance["NotifTestLaunch"], msg, entryId);
    }

    /// <summary>Экспортирует полный отчет аудита безопасности в выбранном пользователем формате (HTML, CSV, JSON).</summary>
    [RelayCommand]
    private async Task ExportReportAsync()
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = LocalizationService.Instance.ExportReportDialogTitle,
                Filter = LocalizationService.Instance.ExportReportFilter,
                FilterIndex = 1,
                FileName = $"StartupMonitor_Audit_{DateTime.Now:yyyyMMdd_HHmmss}.html",
                DefaultExt = ".html"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            var snapshot = TaskManager.PerformanceSnapshot ?? await _pipeClient.GetProcessesAsync();
            var processes = snapshot?.Processes ?? [];

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.15";
            var report = AuditReport.Create(
                _allEntries,
                processes,
                snapshot,
                appVersion: version,
                isServiceConnected: IsConnected,
                strictMode: StrictMode,
                silentMode: SilentMode);

            var extension = System.IO.Path.GetExtension(saveDialog.FileName);
            var bytes = AuditReportGenerator.ExportBytes(report, extension);

            await System.IO.File.WriteAllBytesAsync(saveDialog.FileName, bytes);

            var msg = string.Format(LocalizationService.Instance.ExportReportSuccess, saveDialog.FileName);
            NotificationRequested?.Invoke("Startup Monitor", msg, "");

            // Для HTML открываем в браузере по умолчанию, для остальных файлов — папку с выделением
            if (string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                }
                catch { /* игнорируем ошибку запуска ассоциации */ }
            }
            else
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{saveDialog.FileName}\"");
                }
                catch { /* игнорируем */ }
            }
        }
        catch (Exception ex)
        {
            var errorMsg = string.Format(LocalizationService.Instance.ExportReportError, ex.Message);
            NotificationRequested?.Invoke("Startup Monitor", errorMsg, "");
        }
    }

    /// <summary>Открывает в проводнике папку с файлом записи и выделяет его.</summary>
    [RelayCommand]
    private void OpenFileLocation(string entryId)
    {
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null || string.IsNullOrEmpty(entry.FilePath)) return;
        var dir = System.IO.Path.GetDirectoryName(entry.FilePath);
        if (System.IO.Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{entry.FilePath}\"");
    }

    /// <summary>Копирует полный путь к файлу записи в буфер обмена.</summary>
    [RelayCommand]
    private void CopyFilePath(string entryId)
    {
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null || string.IsNullOrEmpty(entry.FilePath)) return;
        System.Windows.Clipboard.SetText(entry.FilePath);
    }

    /// <summary>Блокирует запись по её идентификатору.</summary>
    [RelayCommand]
    private async Task BlockEntryByIdAsync(string entryId)
    {
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
            await BlockEntryAsync(entry);
    }

    /// <summary>Добавляет запись в чёрный список (блокирует) и помечает как Blocked.</summary>
    [RelayCommand]
    private async Task BlockEntryAsync(StartupEntry entry)
    {
        if (SystemProtectionHelper.IsProtectedSystemEntry(entry))
        {
            if (SystemDangerConfirmationRequested?.Invoke(entry, "Block") == false)
                return;
        }

        // Для служб правило создается по имени службы, а не по бинарнику хоста svchost.exe!
        var ruleValue = (entry.Location is EntryLocation.Service or EntryLocation.Driver && !string.IsNullOrWhiteSpace(entry.LocationDetail))
            ? entry.LocationDetail
            : entry.FilePath;

        // Запрещаем создавать правила на чистый путь системных хост-процессов (svchost и др.)
        if (SystemProtectionHelper.IsProtectedProcessName(ruleValue))
        {
            NotificationRequested?.Invoke(
                LocalizationService.Instance.SystemDangerTitle,
                LocalizationService.Instance.ErrCannotBlockSystemProcess,
                entry.Id);
            return;
        }

        var rule = new BlacklistRule
        {
            Value = ruleValue,
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyAndRemove,
            Comment = $"Заблокировано: {entry.Name}"
        };
        await _pipeClient.AddToBlacklistAsync(rule);
        Blacklist.Add(rule);
        entry.Status = EntryStatus.Blocked;
        entry.FromBlacklist = true;
        RefreshStats();
        ApplyFilter();
    }

    /// <summary>Удаляет запись из системы (реестр/файл) через службу. При AutoBlockOnRemove предварительно блокирует.</summary>
    [RelayCommand]
    private async Task RemoveFromSystemAsync(string entryId)
    {
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) return;

        if (SystemProtectionHelper.IsProtectedSystemEntry(entry))
        {
            if (SystemDangerConfirmationRequested?.Invoke(entry, "Remove") == false)
                return;
        }

        if (AutoBlockOnRemove)
        {
            var ruleValue = (entry.Location is EntryLocation.Service or EntryLocation.Driver && !string.IsNullOrWhiteSpace(entry.LocationDetail))
                ? entry.LocationDetail
                : entry.FilePath;

            if (!SystemProtectionHelper.IsProtectedProcessName(ruleValue))
            {
                var rule = new BlacklistRule
                {
                    Value = ruleValue,
                    MatchType = BlacklistMatchType.Path,
                    Action = BlacklistAction.NotifyAndRemove,
                    Comment = $"auto-blocked on remove ({DateTime.Now:g})"
                };
                await _pipeClient.AddToBlacklistAsync(rule);
                if (!Blacklist.Any(r => r.Value == rule.Value))
                    Blacklist.Add(rule);
            }
        }

        var success = await _pipeClient.RemoveFromSystemAsync(entryId);
        if (success)
        {
            if (entry != null)
            {
                _allEntries.Remove(entry);
                RefreshStats();
                ApplyFilter();
            }
            await LoadRemovedEntriesAsync();
        }
    }

    /// <summary>Загружает список удалённых записей из службы.</summary>
    public async Task LoadRemovedEntriesAsync()
    {
        var list = await _pipeClient.GetRemovedEntriesAsync();
        RemovedEntries = new ObservableCollection<RemovedEntry>(list);
    }

    /// <summary>Загружает историю изменений из службы (с сортировкой от новых к старым).</summary>
    public async Task LoadHistoryAsync()
    {
        var list = await _pipeClient.GetHistoryAsync();
        var sorted = list.OrderByDescending(h => h.Timestamp).ToList();
        HistoryEntries = new ObservableCollection<HistoryEntry>(sorted);
    }

    /// <summary>Загружает логи (служба + UI) и отображает их на странице логов.</summary>
    public async Task LoadLogsAsync()
    {
        var baseDir = AppContext.BaseDirectory;
        _allLogEntries = await Task.Run(() => LogReaderService.ReadAllLogs(baseDir));
        LogSearchText = string.Empty;
        LogEntries = new ObservableCollection<LogEntry>(_allLogEntries);
    }

    /// <summary>Запрашивает немедленную переоценку AI-рейтинга для указанной записи и обновляет локальную модель.</summary>
    public async Task<StartupEntry?> ReRateEntryAsync(string entryId)
    {
        var updated = await _pipeClient.ReRateEntryAsync(entryId);
        if (updated != null)
        {
            var existing = Entries.FirstOrDefault(e => e.Id == entryId);
            if (existing != null)
            {
                existing.AiRating = updated.AiRating;
                existing.AiCategory = updated.AiCategory;
                existing.AiRisk = updated.AiRisk;
                existing.AiRecommendation = updated.AiRecommendation;
            }
        }
        return updated;
    }

    /// <summary>Устанавливает ручной рейтинг для записи (от 1 до 5 звёзд) или сбрасывает к автоматической AI-оценке.</summary>
    public async Task<StartupEntry?> SetUserRatingAsync(string entryId, int stars, bool resetToAi = false)
    {
        var updated = await _pipeClient.SetUserRatingAsync(entryId, stars, resetToAi);
        if (updated != null)
        {
            var existing = Entries.FirstOrDefault(e => e.Id == entryId);
            if (existing != null)
            {
                existing.IsUserRating = updated.IsUserRating;
                existing.AiRating = updated.AiRating;
                existing.AiCategory = updated.AiCategory;
                existing.AiRisk = updated.AiRisk;
                existing.AiRecommendation = updated.AiRecommendation;
            }
        }
        return updated;
    }

    /// <summary>Запрашивает полный журнал истории сетевых подключений у службы.</summary>
    public async Task<List<NetworkConnectionHistoryItem>> GetConnectionHistoryAsync(CancellationToken ct = default)
        => await _pipeClient.GetConnectionHistoryAsync(ct);

    /// <summary>Очищает журнал истории сетевых подключений у службы.</summary>
    public async Task<bool> ClearConnectionHistoryAsync(CancellationToken ct = default)
        => await _pipeClient.ClearConnectionHistoryAsync(ct);

    // ───────── Живое обновление логов ─────────

    /// <summary>Таймер автообновления логов (срабатывает каждые 3 с).</summary>
    private System.Threading.Timer? _logWatchTimer;

    /// <summary>Включает автообновление логов (страница «Логи» открыта).</summary>
    public void StartLogWatch()
    {
        _logWatchTimer?.Dispose();
        _logWatchTimer = new System.Threading.Timer(
            async _ => await RefreshLogsIfChanged(),
            null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    /// <summary>Выключает автообновление (ушли со страницы «Логи»).</summary>
    public void StopLogWatch()
    {
        _logWatchTimer?.Dispose();
        _logWatchTimer = null;
    }

    /// <summary>Проверяет, изменились ли файлы логов, и при необходимости обновляет коллекцию.</summary>
    private async Task RefreshLogsIfChanged()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var fresh = await Task.Run(() => LogReaderService.ReadAllLogs(baseDir));

            // Обновляем коллекцию только если действительно появились изменения
            if (_allLogEntries.Count > 0 && fresh.Count == _allLogEntries.Count)
            {
                if (string.Equals(fresh[0].FullLine, _allLogEntries[0].FullLine, StringComparison.Ordinal) &&
                    string.Equals(fresh[^1].FullLine, _allLogEntries[^1].FullLine, StringComparison.Ordinal))
                {
                    return;
                }
            }

            var filter = LogSearchText;
            _allLogEntries = fresh;
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                LogSearchText = filter;
                ApplyLogFilter();
            });
        }
        catch { }
    }

    /// <summary>Восстанавливает удалённую запись в системе.</summary>
    [RelayCommand]
    private async Task RestoreEntryAsync(string removedId)
    {
        var success = await _pipeClient.RestoreEntryAsync(removedId);
        if (success)
            await LoadRemovedEntriesAsync();
    }

    /// <summary>Запускает очистку устаревших записей на стороне службы.</summary>
    [RelayCommand]
    private async Task CleanStaleConfigAsync()
    {
        await _pipeClient.CleanStaleAsync();
        await LoadDataIfChanged();
    }

    /// <summary>Снимает блокировку с записи: удаляет из чёрного списка и включает обратно.</summary>
    [RelayCommand]
    private async Task UnblockEntryAsync(StartupEntry entry)
    {
        var matchingRule = Blacklist.FirstOrDefault(r =>
            r.Value.Equals(entry.FilePath, StringComparison.OrdinalIgnoreCase) &&
            r.MatchType == BlacklistMatchType.Path);
        if (matchingRule != null)
        {
            await _pipeClient.RemoveFromBlacklistAsync(matchingRule.Id);
            Blacklist.Remove(matchingRule);
        }
        // Сервер тоже должен снять Blocked — иначе следующий poll вернёт его версию записи
        await _pipeClient.EnableEntryAsync(entry.Id);
        entry.Status = EntryStatus.Enabled;
        entry.FromBlacklist = false;
        RefreshStats();
        ApplyFilter();
    }

    /// <summary>Снимает блокировку с записи по идентификатору.</summary>
    [RelayCommand]
    private async Task UnblockEntryByIdAsync(string entryId)
    {
        var entry = _allEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
            await UnblockEntryAsync(entry);
    }

    /// <summary>Добавляет правило в чёрный список через службу.</summary>
    [RelayCommand]
    private async Task AddToBlacklistAsync(BlacklistRule rule)
    {
        await _pipeClient.AddToBlacklistAsync(rule);
        Blacklist.Add(rule);
    }

    /// <summary>Удаляет правило из чёрного списка по ID.</summary>
    [RelayCommand]
    private async Task RemoveFromBlacklistAsync(string ruleId)
    {
        await _pipeClient.RemoveFromBlacklistAsync(ruleId);
        var ruleToRemove = Blacklist.FirstOrDefault(r => r.Id == ruleId);
        if (ruleToRemove != null) Blacklist.Remove(ruleToRemove);
    }

    /// <summary>Добавляет новое правило через UI-форму (с маппингом типов и действий).</summary>
    [RelayCommand]
    private async Task AddRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewRuleValue))
            return;

        var matchType = NewRuleMatchType switch
        {
            {} t when t == LocalizationService.Instance.MatchTypePathWildcard => BlacklistMatchType.PathWildcard,
            {} t when t == LocalizationService.Instance.MatchTypePublisher => BlacklistMatchType.Publisher,
            {} t when t == LocalizationService.Instance.MatchTypeSHA256 => BlacklistMatchType.SHA256,
            _ => BlacklistMatchType.Path,
        };

        var action = NewRuleAction == LocalizationService.Instance.ActionNotifyOnly
            ? BlacklistAction.NotifyOnly
            : BlacklistAction.NotifyAndRemove;

        var rule = new BlacklistRule
        {
            Value = NewRuleValue.Trim(),
            MatchType = matchType,
            Action = action,
        };

        if (matchType is BlacklistMatchType.Path or BlacklistMatchType.PathWildcard &&
            SystemProtectionHelper.IsProtectedProcessName(rule.Value))
        {
            NotificationRequested?.Invoke(
                LocalizationService.Instance.SystemDangerTitle,
                LocalizationService.Instance.ErrCannotBlockSystemProcess,
                "");
            return;
        }

        await _pipeClient.AddToBlacklistAsync(rule);
        Blacklist.Add(rule);
        NewRuleValue = string.Empty;
    }

    // ──────────────────────────────────────────────
    // Boot Time Tracer
    // ──────────────────────────────────────────────

    /// <summary>Отчёт анализатора времени загрузки Windows (Boot Time Tracer).</summary>
    [ObservableProperty]
    private StartupMonitor.Shared.Models.BootTime.BootTimeReport? _bootTimeReport;

    /// <summary>true — идёт загрузка данных Boot Time Tracer.</summary>
    [ObservableProperty]
    private bool _isBootTimeLoading;

    /// <summary>Строка дельты загрузки относительно предыдущей (⚡ быстрее / ⚠️ медленнее).</summary>
    [ObservableProperty]
    private string _bootTimeDeltaText = string.Empty;

    /// <summary>Цвет строки дельты (зелёный / красный / серый).</summary>
    [ObservableProperty]
    private string _bootTimeDeltaColor = "#888888";

    /// <summary>Загружает/обновляет отчёт Boot Time Tracer из службы.</summary>
    [RelayCommand]
    public async Task LoadBootTimeAsync()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Main.LoadBootTime");
        if (IsBootTimeLoading) return;
        IsBootTimeLoading = true;
        try
        {
            var report = await _pipeClient.GetBootTimeReportAsync();
            BootTimeReport = report;

            if (report?.DeltaPreviousBootMs != null)
            {
                var delta = report.DeltaPreviousBootMs.Value;
                var abs = Math.Abs(delta);
                var formatted = StartupMonitor.Shared.Models.BootTime.BootSessionInfo.FormatDuration(abs);
                if (delta < -1000)
                {
                    BootTimeDeltaText = LocalizationService.Instance.GetBootTimeFaster(formatted);
                    BootTimeDeltaColor = "#22AA44";
                }
                else if (delta > 1000)
                {
                    BootTimeDeltaText = LocalizationService.Instance.GetBootTimeSlower(formatted);
                    BootTimeDeltaColor = "#FF5555";
                }
                else
                {
                    BootTimeDeltaText = string.Empty;
                    BootTimeDeltaColor = "#888888";
                }
            }
            else
            {
                BootTimeDeltaText = string.Empty;
                BootTimeDeltaColor = "#888888";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BootTime] Ошибка загрузки отчёта: {ex.Message}");
        }
        finally
        {
            IsBootTimeLoading = false;
        }
    }

    /// <summary>Запускает глобальный опрос сетевых алертов брандмауэра (каждые 2 секунды).
    /// Работает независимо от видимости главного окна и флага <see cref="_allBackgroundSuspended"/>,
    /// потому что алерты брандмауэра — функция безопасности, которая обязана работать
    /// даже когда UI свёрнут в трей. <see cref="SuspendAllBackgroundActivity"/> явно не
    /// останавливает этот таймер, поэтому здесь нет проверки на приостановку.
    /// </summary>
    public void StartNetworkAlertPoller()
    {
        _networkAlertPollerTimer?.Dispose();
        _networkAlertPollerTimer = new Timer(_ => _ = SafePollNetworkAlertsAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private async Task SafePollNetworkAlertsAsync()
    {
        try
        {
            await PollNetworkAlertsAsync();
        }
        catch (Exception ex)
        {
            App.Log($"MainViewModel.SafePollNetworkAlerts: {ex.Message}");
        }
    }

    /// <summary>Останавливает глобальный опрос сетевых алертов брандмауэра.</summary>
    public void StopNetworkAlertPoller()
    {
        _networkAlertPollerTimer?.Dispose();
        _networkAlertPollerTimer = null;
        Interlocked.Exchange(ref _isPollingNetworkAlerts, 0);
        Interlocked.Exchange(ref _isAlertWindowOpen, 0);
    }

    /// <summary>
    /// Неблокирующий глобальный опрос ожидающих сетевых подключений.
    /// Показывает ненавязчивое окно без перехвата фокуса.
    /// </summary>
    private async Task PollNetworkAlertsAsync()
    {
        if (Interlocked.CompareExchange(ref _isPollingNetworkAlerts, 1, 0) != 0)
            return;

        var windowOpened = false;
        try
        {
            if (!IsConnected)
                return;
            if (Interlocked.CompareExchange(ref _isAlertWindowOpen, 0, 0) == 1)
                return;

            List<NetworkConnectionAlertDto>? alerts = null;
            try
            {
                alerts = await _pipeClient.GetPendingNetworkAlertsAsync();
            }
            catch
            {
                return;
            }

            if (alerts == null || alerts.Count == 0)
                return;
            if (Interlocked.CompareExchange(ref _isAlertWindowOpen, 0, 0) == 1)
                return;

            if (Interlocked.CompareExchange(ref _isAlertWindowOpen, 1, 0) != 0)
                return;
            windowOpened = true;

            var alert = alerts[0];
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var window = new NetworkConnectionAlertWindow(alert, _pipeClient, countdownSeconds: 30);
                    window.Closed += (_, _) => Interlocked.Exchange(ref _isAlertWindowOpen, 0);
                    // Owner устанавливаем только если главное окно видимо и не свёрнуто — иначе тост
                    // может оказаться «заперт» за скрытым окном при работе из трея.
                    var owner = System.Windows.Application.Current.MainWindow;
                    if (owner != null && owner.IsVisible && owner.WindowState != System.Windows.WindowState.Minimized)
                        window.Owner = owner;
                    window.Show();
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _isAlertWindowOpen, 0);
                    windowOpened = false;
                    App.Log($"MainViewModel.PollNetworkAlerts.Show: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            if (windowOpened)
                Interlocked.Exchange(ref _isAlertWindowOpen, 0);
            App.Log($"MainViewModel.PollNetworkAlerts: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isPollingNetworkAlerts, 0);
        }
    }
}