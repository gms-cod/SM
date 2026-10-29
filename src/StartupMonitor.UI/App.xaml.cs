using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using SharpVectors.Converters;
using StartupMonitor.Shared;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.UI.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StartupMonitor.UI;

/// <summary>Главный класс приложения WPF.</summary>
public partial class App : Application
{
    /// <summary>Флаг запуска со свернутым окном (--minimized).</summary>
    internal static bool IsMinimized { get; private set; }

    /// <summary>Иконка в системном трее.</summary>
    private TaskbarIcon? _trayIcon;
    /// <summary>Интеллектуальный агрегатор уведомлений трея.</summary>
    private TrayNotificationAggregator? _trayAggregator;
    /// <summary>Список Id записей, вошедших в последнее всплывающее уведомление.</summary>
    private List<string> _lastNotifiedEntryIds = new();
    /// <summary>Id последней записи, для которой показано уведомление.</summary>
    private string? _lastNotifiedEntryId;
    /// <summary>Путь к лог-файлу UI.</summary>
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "logs", "ui-debug.log");
    /// <summary>Объект для синхронизации записи в лог.</summary>
    private static readonly object _logLock = new();
    /// <summary>Mutex для обеспечения единственного экземпляра приложения.</summary>
    private static Mutex? _instanceMutex;

    /// <summary>Записывает сообщение в лог-файл с автоматической ротацией при превышении 1 МБ.</summary>
    internal static void Log(string msg)
    {
        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                    File.Move(LogPath, LogPath + ".1", overwrite: true);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {msg}\n");
            }
        }
        catch { }
    }

    /// <summary>Пункт меню трея для приостановки/возобновления мониторинга.</summary>
    private System.Windows.Controls.MenuItem? _pauseResumeMenuItem;

    /// <summary>Обрабатывает запуск приложения: проверяет единственный экземпляр, настраивает тему, создаёт иконку трея.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        IsMinimized = e.Args.Contains("--minimized");
        Log($"OnStartup: вход в OnStartup, IsMinimized={IsMinimized}, Args={string.Join(' ', e.Args)}");

        var mutexName = $"Global\\StartupMonitorUI_{Environment.UserName}";
        _instanceMutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                if (_instanceMutex.WaitOne(TimeSpan.Zero, true))
                {
                    createdNew = true;
                }
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
        }
        if (!createdNew)
        {
            Log("OnStartup: второй экземпляр, завершаюсь");
            ActivateOtherInstance();
            Current.Shutdown();
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"UNHANDLED EXCEPTION: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DISPATCHER EXCEPTION: {args.Exception}");
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"UNOBSERVED TASK EXCEPTION: {args.Exception}");
            args.SetObserved();
        };

        Resources["Loc"] = Services.LocalizationService.Instance;

        var savedLanguage = ViewModels.MainViewModel.LoadLanguageFromFile();
        Services.LocalizationService.Instance.ChangeLanguage(savedLanguage);

        base.OnStartup(e);

        var savedTheme = ViewModels.MainViewModel.LoadThemeFromFile();
        var theme = savedTheme switch
        {
            "light" => ApplicationTheme.Light,
            "system" => IsWindowsThemeLight() ? ApplicationTheme.Light : ApplicationTheme.Dark,
            _ => ApplicationTheme.Dark,
        };
        Services.ThemeService.ApplyTheme(theme, force: true);

        // Периодический сброс сводки производительности UI в ui-debug.log каждые 5 минут
        _ = ExecutionProfiler.StartAutoReportAsync(
            TimeSpan.FromMinutes(5),
            msg => Log($"PERF REPORT (UI):\n{msg}"));

        // Проверяем и синхронизируем автозапуск UI с настройками пользователя
        try
        {
            var startWithWindows = ViewModels.MainViewModel.LoadStartWithWindowsFromFile();
            Services.AutostartService.EnsureSingleAutostartEntry(startWithWindows);
        }
        catch (Exception ex)
        {
            Log($"OnStartup: ошибка проверки автозапуска: {ex.Message}");
        }

        Log("OnStartup: создаю главное окно");
        var mainWindow = new Views.MainWindow();
        MainWindow = mainWindow;

        // Всегда вызываем Show(), чтобы окно создало HWND, событие Loaded сработал и
        // StartupSequence инициализировал Pipe + глобальный опрос сетевых алертов.
        // При --minimized окно стартует свёрнутым, но жизненный цикл проходит полностью:
        // без Show() Loaded не возникает, алерты никогда не начинают опрашиваться.
        mainWindow.Show();
        if (App.IsMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
        }

        Log("OnStartup: создаю иконку трея");
        _trayIcon = new TaskbarIcon
        {
            IconSource = LoadSvgIcon(),
            ToolTipText = Services.LocalizationService.Instance.AppName,
            ContextMenu = CreateTrayMenu()
        };

        _trayAggregator = new TrayNotificationAggregator(
            Dispatcher,
            (title, text, ids) =>
            {
                _lastNotifiedEntryIds = ids.ToList();
                _lastNotifiedEntryId = _lastNotifiedEntryIds.LastOrDefault();
                _trayIcon?.ShowBalloonTip(title, text, BalloonIcon.Info);
                Log($"Toast: показан баллун трея '{title}' (записей: {ids.Count})");
            });

        _trayIcon.TrayLeftMouseUp += (_, _) => RestoreMainWindow();
        _trayIcon.TrayBalloonTipClicked += (_, _) =>
        {
            RestoreMainWindow();
            if (MainWindow is Views.MainWindow wnd)
            {
                if (_lastNotifiedEntryIds.Count == 1)
                {
                    wnd.FocusEntry(_lastNotifiedEntryIds[0]);
                }
                else if (!string.IsNullOrEmpty(_lastNotifiedEntryId))
                {
                    wnd.FocusEntry(_lastNotifiedEntryId);
                }
            }
        };

        Services.LocalizationService.Instance.PropertyChanged += (_, _) =>
            Dispatcher.Invoke(() =>
            {
                if (_trayIcon == null) return;
                _trayIcon.ContextMenu = CreateTrayMenu();
                _trayIcon.ToolTipText = Services.LocalizationService.Instance.AppName;
            });

        Log("OnStartup: готово");
    }

    [DllImport("user32.dll")]
    /// <summary>FindWindow: Выполняет поиск Window.</summary>
    /// <param name="lpClassName">Входные данные: значение параметра lpClassName (string?).</param>
    /// <param name="lpWindowName">Входные данные: значение параметра lpWindowName (string?).</param>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>SetForegroundWindow: Устанавливает ForegroundWindow.</summary>
    /// <param name="hWnd">Входные данные: значение параметра hWnd (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>ShowWindow: Выполняет операцию ShowWindow.</summary>
    /// <param name="hWnd">Входные данные: значение параметра hWnd (IntPtr).</param>
    /// <param name="nCmdShow">Входные данные: значение параметра nCmdShow (int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    /// <summary>Создаёт контекстное меню для иконки трея.</summary>
    private System.Windows.Controls.ContextMenu CreateTrayMenu()
    {
        var loc = Services.LocalizationService.Instance;
        var menu = new System.Windows.Controls.ContextMenu();

        var showHide = new System.Windows.Controls.MenuItem { Header = loc.TrayShowHide };
        showHide.Click += (_, _) => ToggleWindow();
        menu.Items.Add(showHide);

        _pauseResumeMenuItem = new System.Windows.Controls.MenuItem();

        void RefreshPauseHeader()
        {
            if (_pauseResumeMenuItem == null) return;
            if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                _pauseResumeMenuItem.Header = vm.IsMonitoringPaused
                    ? loc["TrayResumeMonitoring"]
                    : loc["TrayPauseMonitoring"];
            }
            else
            {
                _pauseResumeMenuItem.Header = loc["TrayPauseMonitoring"];
            }
        }

        RefreshPauseHeader();

        _pauseResumeMenuItem.Click += async (_, _) =>
        {
            if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                if (!_pauseResumeMenuItem.IsEnabled) return;
                _pauseResumeMenuItem.IsEnabled = false;
                try
                {
                    await vm.TogglePauseMonitoringAsync();
                }
                finally
                {
                    _pauseResumeMenuItem.IsEnabled = true;
                    RefreshPauseHeader();
                }
            }
        };
        menu.Items.Add(_pauseResumeMenuItem);

        var lockdownItem = new System.Windows.Controls.MenuItem
        {
            Header = loc.TrayLockdownMode,
            IsCheckable = true
        };
        menu.Opened += (_, _) =>
        {
            RefreshPauseHeader();
            if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                lockdownItem.IsChecked = vm.StrictMode;
            }
        };
        lockdownItem.Click += (_, _) =>
        {
            if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                vm.StrictMode = lockdownItem.IsChecked;
            }
        };
        menu.Items.Add(lockdownItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exit = new System.Windows.Controls.MenuItem { Header = loc.TrayExit };
        exit.Click += async (_, _) =>
        {
            // Мягкая остановка службы через pipe, затем гарантированная через SCM/kill.
            try
            {
                using var pipeClient = new PipeClient();
                await pipeClient.ConnectAsync();
                await pipeClient.RequestServiceShutdownAsync();
            }
            catch (Exception ex)
            {
                Log($"OnExit: мягкая остановка службы не удалась: {ex.Message}");
            }
            try { ServiceManager.StopService(); } catch (Exception ex) { Log($"OnExit: StopService не удался: {ex.Message}"); }
            Current.Shutdown();
        };
        menu.Items.Add(exit);

        return menu;
    }

    /// <summary>Обновляет состояние иконки трея (цветная или серая при паузе), тултип и пункт меню.</summary>
    public void UpdateTrayMonitoringState(bool isPaused)
    {
        if (_trayIcon == null) return;
        _trayIcon.IconSource = LoadSvgIcon(isPaused);
        var loc = Services.LocalizationService.Instance;
        _trayIcon.ToolTipText = isPaused
            ? $"{loc.AppName} ({loc["TrayMonitoringPausedStatus"]})"
            : loc.AppName;

        if (_pauseResumeMenuItem != null)
        {
            _pauseResumeMenuItem.Header = isPaused
                ? loc["TrayResumeMonitoring"]
                : loc["TrayPauseMonitoring"];
        }
    }

    /// <summary>Переключает видимость главного окна (показать/скрыть).</summary>
    private void ToggleWindow()
    {
        if (MainWindow == null) return;
        if (MainWindow.IsVisible && MainWindow.WindowState != WindowState.Minimized)
        {
            MainWindow.Hide();
        }
        else
        {
            RestoreMainWindow();
        }
    }

    /// <summary>Восстанавливает главное окно из трея (с сохранением состояния Maximized/Normal).</summary>
    private void RestoreMainWindow()
    {
        if (MainWindow == null) return;
        MainWindow.Show();
        if (MainWindow is Views.MainWindow mainWnd)
        {
            mainWnd.RestoreFromTray();
        }
        else
        {
            if (MainWindow.WindowState == WindowState.Minimized)
                MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    /// <summary>Активирует окно другого экземпляра приложения.</summary>
    private static void ActivateOtherInstance()
    {
        var hWnd = FindWindow(null, "Startup Monitor");
        if (hWnd != IntPtr.Zero)
        {
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }
    }

    /// <summary>Показывает всплывающее уведомление в трее через агрегатор с группировкой однородных событий.</summary>
    internal void ShowTrayBalloon(string title, string text, string? entryId = null, TrayNotificationKind kind = TrayNotificationKind.Auto)
    {
        if (_trayAggregator != null)
        {
            _trayAggregator.Enqueue(title, text, entryId, kind);
        }
        else
        {
            _lastNotifiedEntryId = entryId;
            _trayIcon?.ShowBalloonTip(title, text, BalloonIcon.Info);
            Log("Toast: показан баллун трея (прямой вызов)");
        }
    }

    /// <summary>Очищает ресурсы при завершении приложения.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit: приложение завершается, код {e.ApplicationExitCode}");
        try { ServiceManager.StopService(); } catch (Exception ex) { Log($"OnExit: защитный StopService не удался: {ex.Message}"); }
        _trayAggregator?.Dispose();
        _trayAggregator = null;
        _trayIcon?.Dispose();
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    /// <summary>Определяет, используется ли светлая тема в Windows.</summary>
    private static bool IsWindowsThemeLight() => IsSystemThemeLight();
    /// <summary>IsSystemThemeLight: Проверяет состояние IsSystemThemeLight.</summary>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool IsSystemThemeLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 1;
        }
        catch { }
        return false;
    }

    /// <summary>Загружает векторную иконку щита с галочкой (логотип программы) для системного трея.</summary>
    /// <param name="isPaused">Если true, формирует спокойную монохромную серую иконку.</param>
    private static ImageSource LoadSvgIcon(bool isPaused = false)
    {
        try
        {
            var geom = Geometry.Parse("M3 5.75A.75.75 0 0 1 3.75 5c2.663 0 5.258-.943 7.8-2.85a.75.75 0 0 1 .9 0C14.992 4.057 17.587 5 20.25 5a.75.75 0 0 1 .75.75V11q0 .507-.04.996a6.5 6.5 0 0 0-1.465-.684q.005-.155.005-.312V6.478c-2.577-.152-5.08-1.09-7.5-2.8c-2.42 1.71-4.923 2.648-7.5 2.8V11c0 4.149 2.332 7.221 7.125 9.285a6.5 6.5 0 0 0 1.005 1.52l-.355.143a.75.75 0 0 1-.55 0C5.958 19.676 3 16 3 11zM23 17.5a5.5 5.5 0 1 1-11 0a5.5 5.5 0 0 1 11 0m-2.146-2.354a.5.5 0 0 0-.708 0L16.5 18.793l-1.646-1.647a.5.5 0 0 0-.708.708l2 2a.5.5 0 0 0 .708 0l4-4a.5.5 0 0 0 0-.708");
            
            Brush brush;
            if (isPaused)
            {
                // Серая сланцевая иконка при паузе
                brush = new LinearGradientBrush(
                    Color.FromRgb(0x94, 0xA3, 0xB8), // #94A3B8
                    Color.FromRgb(0x47, 0x55, 0x69), // #475569
                    new Point(0, 0),
                    new Point(1, 1));
            }
            else
            {
                // Фирменный бирюзово-синий градиент
                brush = new LinearGradientBrush(
                    Color.FromRgb(0x00, 0xD2, 0xD3),
                    Color.FromRgb(0x00, 0x78, 0xD4),
                    new Point(0, 0),
                    new Point(1, 1));
            }

            var drawing = new GeometryDrawing(brush, null, geom);
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch
        {
            var svgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.svg");
            if (File.Exists(svgPath))
            {
                var ext = new SvgImageExtension { Source = svgPath };
                return (ImageSource)ext.ProvideValue(null!);
            }
            return null!;
        }
    }
}
