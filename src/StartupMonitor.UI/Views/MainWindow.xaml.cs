using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StartupMonitor.Shared;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;

namespace StartupMonitor.UI.Views;

/// <summary>Главное окно приложения Startup Monitor.</summary>
public partial class MainWindow
{
    /// <summary>Клиент для связи со службой через named pipe.</summary>
    private readonly PipeClient _pipeClient;
    /// <summary>Основная модель представления.</summary>
    private readonly MainViewModel _viewModel;
    /// <summary>Флаг первого отображения окна.</summary>
    private bool _hasShown;
    /// <summary>Флаг завершения последовательности запуска (StartupSequence).</summary>
    private bool _isStartupComplete;
    /// <summary>Последнее неминимизированное состояние окна (Normal или Maximized).</summary>
    private WindowState _lastNonMinimizedState = WindowState.Normal;
    /// <summary>Индекс выбранной вкладки навигации.</summary>
    private int _selectedNavIndex;
    /// <summary>Флаг замены дизайнерского экземпляра хаба сетевой безопасности на подключённый к <see cref="_pipeClient"/>.</summary>
    private bool _firewallHubInitialized;

    /// <summary>Время последнего клика мышью для определения двойного клика.</summary>
    private DateTime _lastClickTime;
    /// <summary>Id записи последнего клика.</summary>
    private string? _lastClickEntryId;

    /// <summary>Инициализирует главное окно, настраивает Model-View-ViewModel и систему уведомлений.</summary>
    public MainWindow()
    {
        InitializeComponent();

        _pipeClient = new PipeClient();
        _viewModel = new MainViewModel(_pipeClient);
        _viewModel.SystemDangerConfirmationRequested = (entry, actionName) =>
        {
            var title = LocalizationService.Instance.SystemDangerTitle;
            var message = string.Format(
                System.Globalization.CultureInfo.CurrentUICulture,
                LocalizationService.Instance.SystemDangerMessage,
                entry.Name ?? entry.FilePath);

            var result = MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            return result == MessageBoxResult.Yes;
        };
        DataContext = _viewModel;

        // 1. Загрузка сохранённых размеров, положения и состояния окна
        var savedWidth = MainViewModel.LoadSetting("windowWidth", 1200.0);
        var savedHeight = MainViewModel.LoadSetting("windowHeight", 800.0);
        var savedLeft = MainViewModel.LoadSetting("windowLeft", double.NaN);
        var savedTop = MainViewModel.LoadSetting("windowTop", double.NaN);
        var savedState = MainViewModel.LoadSetting("windowState", "Normal");

        if (!double.IsNaN(savedLeft) && !double.IsNaN(savedTop))
        {
            var targetRect = new Rect(savedLeft, savedTop, savedWidth, savedHeight);
            if (IsWindowPlacementVisible(targetRect))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = savedLeft;
                Top = savedTop;
                Width = Math.Max(savedWidth, MinWidth);
                Height = Math.Max(savedHeight, MinHeight);
            }
            else
            {
                Width = Math.Max(savedWidth, MinWidth);
                Height = Math.Max(savedHeight, MinHeight);
            }
        }
        else
        {
            Width = Math.Max(savedWidth, MinWidth);
            Height = Math.Max(savedHeight, MinHeight);
        }

        if (savedState == "Maximized")
        {
            _lastNonMinimizedState = WindowState.Maximized;
            if (!App.IsMinimized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        else
        {
            _lastNonMinimizedState = WindowState.Normal;
        }

        Loaded += async (_, _) =>
        {
            _hasShown = true;
            var savedTab = MainViewModel.LoadSetting("lastActiveTab", 0);
            if (savedTab < 0 || savedTab > 8) savedTab = 0;
            App.Log($"MainWindow: Loaded, savedTab={savedTab}, WindowState={WindowState}, IsMinimized={App.IsMinimized}");
            NavigateToPage(savedTab);
            await StartupSequence();

            _isStartupComplete = true;

            if (savedTab != 0)
            {
                // Для вкладок, требующих соединения со службой, обновляем данные после инициализации pipe
                if (savedTab == 3) _ = _viewModel.LoadRemovedEntriesAsync();
                else if (savedTab == 4) _ = _viewModel.LoadHistoryAsync();
                else if (savedTab == 5) { _ = _viewModel.LoadLogsAsync(); _viewModel.StartLogWatch(); }
                else if (savedTab == 6 && WindowState != WindowState.Minimized)
                {
                    App.Log("MainWindow: Activating TaskManager (savedTab=6)");
                    _ = _viewModel.TaskManager.ActivateAsync();
                }
                else if (savedTab == 7) _ = _viewModel.LoadBootTimeAsync();
                else if (savedTab == 8) _ = RefreshFirewallHubAsync();
            }

            if (App.IsMinimized)
            {
                Hide();
            }
        };
        Closing += (_, e) =>
        {
            App.Log($"MainWindow: Closing event (Cancel={e.Cancel})");
            try
            {
                var rect = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;

                if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                {
                    MainViewModel.SaveSetting("windowLeft", rect.Left);
                    MainViewModel.SaveSetting("windowTop", rect.Top);
                    MainViewModel.SaveSetting("windowWidth", rect.Width);
                    MainViewModel.SaveSetting("windowHeight", rect.Height);
                }

                MainViewModel.SaveSetting("windowState", _lastNonMinimizedState == WindowState.Maximized ? "Maximized" : "Normal");
            }
            catch (Exception ex)
            {
                App.Log($"MainWindow: Error saving window state on Closing: {ex.Message}");
            }
        };
        Closed += (_, _) =>
        {
            App.Log("MainWindow: Closed event");
            try { FirewallHubView?.Deactivate(); } catch { }
            _viewModel.Dispose();
            _pipeClient.Dispose();
        };
        _viewModel.NotificationRequested += (title, text, entryId) =>
            ((App)Application.Current).ShowTrayBalloon(title, text, entryId, TrayNotificationAggregator.Classify(title));
        _viewModel.TaskManager.LocateStartupEntryRequested += FocusEntry;
        _viewModel.TaskManager.StatusMessageRequested += (title, text) =>
            ((App)Application.Current).ShowTrayBalloon(title, text, null, TrayNotificationKind.ProcessStatus);
        _viewModel.OpenSnapshotsRequested += () =>
        {
            try
            {
                App.Log("MainWindow: Opening SnapshotsWindow");
                var win = new Dialogs.SnapshotsWindow(_pipeClient, _viewModel) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Log($"MainWindow: Error opening SnapshotsWindow: {ex}");
                MessageBox.Show($"Не удалось открыть окно снимков: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        _viewModel.OpenSystemInfoRequested += () =>
        {
            try
            {
                App.Log("MainWindow: Opening SystemInfoWindow");
                var win = new Dialogs.SystemInfoWindow(_viewModel.ComputerInfo) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Log($"MainWindow: Error opening SystemInfoWindow: {ex}");
                MessageBox.Show($"Не удалось открыть окно информации о компьютере: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                // Глубокий покой UI в трее (п. 4.1): замораживаем ВСЕ фоновые таймеры и метрики
                _viewModel.SuspendAllBackgroundActivity();
                try { FirewallHubView?.Deactivate(); } catch { }
            }
            else
            {
                if (WindowState != WindowState.Minimized)
                    _viewModel.ResumeAllBackgroundActivity();
                if (_selectedNavIndex == 6 && _isStartupComplete && WindowState != WindowState.Minimized)
                    _ = _viewModel.TaskManager.ActivateAsync();
                if (_selectedNavIndex == 8 && _isStartupComplete && WindowState != WindowState.Minimized)
                {
                    try { FirewallHubView?.Activate(); } catch { }
                }
            }
        };

        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                // Окно свернуто/ушло в трей — полная заморозка фоновых таймеров (0% CPU)
                _viewModel.SuspendAllBackgroundActivity();
                try { FirewallHubView?.Deactivate(); } catch { }
                if (_hasShown && _viewModel.MinimizeToTray)
                    Hide();
            }
            else
            {
                _lastNonMinimizedState = WindowState;
                if (IsVisible)
                    _viewModel.ResumeAllBackgroundActivity();
                if (_selectedNavIndex == 6 && _isStartupComplete && IsVisible)
                    _ = _viewModel.TaskManager.ActivateAsync();
                if (_selectedNavIndex == 8 && _isStartupComplete && IsVisible)
                {
                    try { FirewallHubView?.Activate(); } catch { }
                }
            }
        };

        EntryList.AddHandler(
            System.Windows.UIElement.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(OnEntryPreviewMouseDown),
            true);
    }

    /// <summary>Обрабатывает предварительный клик левой кнопкой мыши для детекции двойного клика по записи.</summary>
    private void OnEntryPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastClickTime).TotalMilliseconds;
        _lastClickTime = now;

        var entry = (e.OriginalSource as FrameworkElement)?.DataContext as StartupEntry
                    ?? EntryList.SelectedItem as StartupEntry;
        var isManualDouble = entry != null && entry.Id == _lastClickEntryId
                             && elapsed > 0 && elapsed < 500;
        _lastClickEntryId = entry?.Id;

        if (e.ClickCount != 2 && !isManualDouble) return;
        if (entry == null) return;

        var capturedEntry = entry;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            /// <summary>Action: Выполняет операцию Action.</summary>
            /// <param name="(">Входные данные: значение параметра ( ().</param>
            new Action(() =>
            {
                var detailWindow = new Dialogs.EntryDetailWindow(capturedEntry, _viewModel)
                {
                    Owner = this
                };
                detailWindow.ShowDialog();
            }));

        e.Handled = true;
    }

    /// <summary>Обрабатывает клик по кнопке «Подробнее» (More) в строке списка.</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StartupEntry entry)
        {
            var detailWindow = new Dialogs.EntryDetailWindow(entry, _viewModel)
            {
                Owner = this
            };
            detailWindow.ShowDialog();
        }
    }

    /// <summary>Обрабатывает переключение тумблера включения/отключения автозагрузки в строке списка.</summary>
    private async void OnToggleEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.ToggleSwitch sw) return;
        var id = sw.Tag as string ?? (sw.DataContext as StartupEntry)?.Id;
        if (string.IsNullOrEmpty(id) || _viewModel == null) return;

        // Откатываем локальный флип до фактического подтверждения через ViewModel и Pipe
        sw.GetBindingExpression(Wpf.Ui.Controls.ToggleSwitch.IsCheckedProperty)?.UpdateTarget();
        sw.IsEnabled = false;
        try
        {
            await _viewModel.ToggleEntryCommand.ExecuteAsync(id);
        }
        finally
        {
            sw.IsEnabled = true;
            sw.GetBindingExpression(Wpf.Ui.Controls.ToggleSwitch.IsCheckedProperty)?.UpdateTarget();
        }
    }

    /// <summary>Последовательность запуска: установка службы, подключение, загрузка данных.</summary>
    private async Task StartupSequence()
    {
        var loc = Services.LocalizationService.Instance;
        OverlayErrorPanel.Visibility = Visibility.Collapsed;
        StartOverlay.Visibility = Visibility.Visible;

        void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                App.Log($"StartupSequence: {message}");
            });
        }

        try
        {
            App.Log("StartupSequence: начало");
            UpdateStatus(loc.StatusCheckingService);

            // 1. Проверка, настройка автозапуска и запуск службы
            await ServiceManager.EnsureServiceConfiguredAndRunningAsync(msg => UpdateStatus(msg), timeoutMs: 45000);

            // 2. Подключение к Named Pipe
            using var loader = new PipeClient();
            const int maxRetries = 10;
            for (var retry = 1; retry <= maxRetries; retry++)
            {
                UpdateStatus(loc.GetStatusConnectingAttempt(retry, maxRetries));
                await loader.ConnectAsync();
                if (loader.IsConnected) break;
                await Task.Delay(1500);
            }

            if (!loader.IsConnected)
            {
                throw new InvalidOperationException(loc.StatusConnectionFailed);
            }

            // 3. Сканирование и получение начальных данных
            UpdateStatus(loc.StatusLoadingEntries);
            using var scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(75));
            var entriesTask = loader.GetAllEntriesAsync();
            var blacklistTask = loader.GetBlacklistAsync();

            if (await Task.WhenAny(entriesTask, Task.Delay(-1, scanCts.Token)) != entriesTask)
            {
                throw new TimeoutException(loc.StatusScanTimeout);
            }

            var entries = await entriesTask;
            var blacklist = await blacklistTask;
            App.Log($"StartupSequence: загружено {entries.Count} записей, {blacklist.Count} правил");

            _viewModel.SetInitialData(entries, blacklist);
            UpdateStatus(loc.GetStatusLoaded(entries.Count));
            await Task.Delay(400);

            // 4. Завершение инициализации — скрываем оверлей
            StartOverlay.Visibility = Visibility.Collapsed;

            // 5. Запуск фоновой подписки на события и автообновления
            App.Log("StartupSequence: запускаю ConnectCommand...");
            await _viewModel.ConnectCommand.ExecuteAsync(null);

            App.Log("StartupSequence: запускаю LoadDataCommand...");
            await _viewModel.LoadDataCommand.ExecuteAsync(null);
            App.Log("StartupSequence: полностью завершено успешно");
        }
        catch (OperationCanceledException)
        {
            ShowStartupError(loc.ServiceUacCanceled);
        }
        catch (TimeoutException ex)
        {
            ShowStartupError(loc.StatusServiceTimeout + "\n" + ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            ShowStartupError(loc.GetServiceNotFound(ex.Message));
        }
        catch (Exception ex)
        {
            ShowStartupError(loc.GetStatusError(ex.Message));
        }
    }

    /// <summary>Отображает ошибку на оверлее с кнопками повтора и выхода.</summary>
    private void ShowStartupError(string errorMessage)
    {
        App.Log($"StartupSequence ОШИБКА: {errorMessage}");
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = errorMessage;
            OverlayErrorPanel.Visibility = Visibility.Visible;
        });
    }

    /// <summary>Повторяет запуск и подключение к службе по нажатию кнопки.</summary>
    private async void OnOverlayRetryClick(object sender, RoutedEventArgs e)
    {
        await StartupSequence();
    }

    /// <summary>Завершает приложение по нажатию кнопки выхода на оверлее.</summary>
    private void OnOverlayExitClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    /// <summary>Формирует контекстное меню для выбранной записи в списке.</summary>
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var entry = (e.OriginalSource as FrameworkElement)?.DataContext as StartupEntry;
        if (entry == null) return;
        EntryList.SelectedItem = entry;

        var loc = Services.LocalizationService.Instance;
        var cm = new ContextMenu();

        if (entry.Status == EntryStatus.Blocked)
        {
            var unblock = new MenuItem
            {
                Header = loc.ToggleUnblock,
                Command = _viewModel.UnblockEntryByIdCommand,
                CommandParameter = entry.Id
            };
            cm.Items.Add(unblock);
        }
        else if (entry.Status == EntryStatus.Delayed)
        {
            var cancelDelay = new MenuItem
            {
                Header = loc.CtxCancelDelay,
                Command = _viewModel.CancelDelayedEntryCommand,
                CommandParameter = entry.Id
            };
            cm.Items.Add(cancelDelay);

            var launchNow = new MenuItem
            {
                Header = loc.CtxLaunchNow,
                Command = _viewModel.LaunchDelayedEntryCommand,
                CommandParameter = entry.Id
            };
            cm.Items.Add(launchNow);

            var changeDelayMenu = new MenuItem
            {
                Header = $"{loc.CtxChangeDelay} ({entry.DelaySeconds} {loc.SecondsShort})"
            };
            foreach (var sec in new[] { 15, 30, 45, 60, 90, 120 })
            {
                var sub = new MenuItem
                {
                    Header = $"{sec} {loc.SecondsShort}",
                    Command = _viewModel.SetDelayedEntryCommand,
                    CommandParameter = $"{entry.Id}|{sec}",
                    IsChecked = entry.DelaySeconds == sec
                };
                changeDelayMenu.Items.Add(sub);
            }
            cm.Items.Add(changeDelayMenu);
        }
        else
        {
            var toggle = new MenuItem
            {
                Header = entry.Status == EntryStatus.Enabled ? loc.CtxToggleOff : loc.CtxToggleOn,
                Command = _viewModel.ToggleEntryCommand,
                CommandParameter = entry.Id
            };
            cm.Items.Add(toggle);

            var delayMenu = new MenuItem
            {
                Header = loc.CtxDelayedStartup
            };
            foreach (var sec in new[] { 15, 30, 45, 60, 90, 120 })
            {
                var sub = new MenuItem
                {
                    Header = $"{sec} {loc.SecondsShort}",
                    Command = _viewModel.SetDelayedEntryCommand,
                    CommandParameter = $"{entry.Id}|{sec}"
                };
                delayMenu.Items.Add(sub);
            }
            cm.Items.Add(delayMenu);
        }

        cm.Items.Add(new Separator());

        var openLoc = new MenuItem
        {
            Header = loc.CtxOpenFileLocation,
            Command = _viewModel.OpenFileLocationCommand,
            CommandParameter = entry.Id
        };
        cm.Items.Add(openLoc);

        var copyPath = new MenuItem
        {
            Header = loc.CtxCopyPath,
            Command = _viewModel.CopyFilePathCommand,
            CommandParameter = entry.Id
        };
        cm.Items.Add(copyPath);

        var scanKaspersky = new MenuItem
        {
            Header = loc.CtxScanKaspersky,
            Command = _viewModel.ScanKasperskyEntryCommand,
            CommandParameter = entry
        };
        cm.Items.Add(scanKaspersky);

        cm.Items.Add(new Separator());

        if (entry.Status != EntryStatus.Blocked)
        {
            var block = new MenuItem
            {
                Header = loc.CtxBlock,
                Command = _viewModel.BlockEntryByIdCommand,
                CommandParameter = entry.Id
            };
            cm.Items.Add(block);
        }

        cm.Items.Add(new Separator());

        var removeFromSystem = new MenuItem
        {
            Header = loc.CtxRemoveFromSystem,
            Tag = entry.Id
        };
        removeFromSystem.Click += async (s, e) =>
        {
            var id = (string)((MenuItem)s!).Tag;
            if (_viewModel.AutoBlockOnRemove)
            {
                await _viewModel.RemoveFromSystemCommand.ExecuteAsync(id);
            }
            else
            {
                var msg = loc.RemoveConfirmMessage.Replace("\\n", "\n").Replace("{0}", entry.Name);
                var result = MessageBox.Show(msg, loc.RemoveConfirmTitle,
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _viewModel.AutoBlockOnRemove = true;
                    await _viewModel.RemoveFromSystemCommand.ExecuteAsync(id);
                    _viewModel.AutoBlockOnRemove = false;
                }
                else if (result == MessageBoxResult.No)
                {
                    await _viewModel.RemoveFromSystemCommand.ExecuteAsync(id);
                }
            }
        };
        cm.Items.Add(removeFromSystem);

        EntryList.ContextMenu = cm;
    }

    /// <summary>Фокусирует определённую запись в списке по её идентификатору.</summary>
    public void FocusEntry(string entryId)
    {
        NavigateToPage(0);
        _viewModel.FilterAllCommand.Execute(null);
        var entry = _viewModel.FilteredEntries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) return;
        EntryList.SelectedItem = entry;
        EntryList.ScrollIntoView(entry);
        EntryList.Focus();
    }

    /// <summary>Обрабатывает клик по элементу навигации для переключения страниц.</summary>
    private void OnNavClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag && int.TryParse(tag, out var idx))
            NavigateToPage(idx);
    }

    /// <summary>Обрабатывает клик по кнопке выхода из приложения.</summary>
    private void OnExitClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Application.Current.Shutdown();
    }

    /// <summary>Переключает видимость страниц и обновляет состояние выбранного пункта навигации.</summary>
    private void NavigateToPage(int idx)
    {
        var previousNavIndex = _selectedNavIndex;
        _selectedNavIndex = idx;
        MainViewModel.SaveSetting("lastActiveTab", idx);

        OverviewPage.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        BlacklistPage.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        RemovedPage.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = idx == 5 ? Visibility.Visible : Visibility.Collapsed;
        TaskManagerPage.Visibility = idx == 6 ? Visibility.Visible : Visibility.Collapsed;
        BootTimePage.Visibility = idx == 7 ? Visibility.Visible : Visibility.Collapsed;
        FirewallPage.Visibility = idx == 8 ? Visibility.Visible : Visibility.Collapsed;

        var items = new[] { NavItem0, NavItem1, NavItem2, NavItem3, NavItem4, NavItem5, NavItem6, NavItem7, NavItem8 };
        var bars = new[] { NavBar0, NavBar1, NavBar2, NavBar3, NavBar4, NavBar5, NavBar6, NavBar7, NavBar8 };
        Brush selected;
        try { selected = (Brush)FindResource("ControlFillColorSecondaryBrush"); }
        catch { selected = Brushes.Transparent; }
        var transparent = Brushes.Transparent;
        for (int i = 0; i < items.Length; i++)
        {
            items[i].Background = i == idx ? selected : transparent;
            bars[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!_isStartupComplete)
        {
            // До завершения StartupSequence не выполняем сетевые запросы и фоновые активации
            return;
        }

        // Сетевой экран (8): останавливаем зомби-таймеры при уходе, возобновляем при входе.
        if (previousNavIndex == 8 && idx != 8)
        {
            try { FirewallHubView?.Deactivate(); } catch { }
        }

        // Набор инструментов (9): останавливаем длительные операции при уходе

        if (idx == 6)
            _ = _viewModel.TaskManager.ActivateAsync();
        else
            _ = _viewModel.TaskManager.DeactivateAsync();

        if (idx == 5)
        {
            _ = _viewModel.LoadLogsAsync();
            _viewModel.StartLogWatch();
        }
        else
        {
            _viewModel.StopLogWatch();
        }

        if (idx == 2)
            _viewModel.RefreshLocalizedOptions();
        else if (idx == 3)
            _ = _viewModel.LoadRemovedEntriesAsync();
        else if (idx == 4)
            _ = _viewModel.LoadHistoryAsync();
        else if (idx == 7)
            _ = _viewModel.LoadBootTimeAsync();
        else if (idx == 8)
            _ = RefreshFirewallHubAsync();
    }



    /// <summary>Гарантирует, что хаб сетевой безопасности использует общий подключённый <see cref="_pipeClient"/>.</summary>
    /// <remarks>XAML создаёт <see cref="NetworkSecurityHubView"/> через конструктор без параметров
    /// (с собственным неподключённым клиентом, чьи запросы молча возвращают null).
    /// При первом переходе на страницу 8 экземпляр заменяется на подключённый к общему каналу службы.</remarks>
    private void EnsureFirewallHub()
    {
        if (_firewallHubInitialized) return;
        _firewallHubInitialized = true;
        try
        {
            FirewallPage.Children.Clear();
            FirewallHubView = new NetworkSecurityHubView(_pipeClient);
            FirewallPage.Children.Add(FirewallHubView);
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow: Error initializing FirewallHub: {ex.Message}");
        }
    }

    /// <summary>Инициализирует хаб сетевой безопасности общим клиентом и обновляет его данные.</summary>
    private async Task RefreshFirewallHubAsync()
    {
        try
        {
            EnsureFirewallHub();
            if (FirewallHubView != null)
                FirewallHubView.Activate();
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow: Error refreshing FirewallHub: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    /// <summary>Восстанавливает главное окно из системного трея в его исходное состояние (Normal или Maximized).</summary>
    public void RestoreFromTray()
    {
        Show();
        WindowState = _lastNonMinimizedState;
        _viewModel.ResumeAllBackgroundActivity();
        Activate();
        Focus();
        if (_selectedNavIndex == 6 && _isStartupComplete)
        {
            _ = _viewModel.TaskManager.ActivateAsync();
        }
        if (_selectedNavIndex == 8 && _isStartupComplete)
        {
            try { FirewallHubView?.Activate(); } catch { }
        }
    }

    /// <summary>Проверяет, попадает ли прямоугольник окна хотя бы частично на видимую область подключенных мониторов.</summary>
    private static bool IsWindowPlacementVisible(Rect bounds)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var intersection = Rect.Intersect(virtualScreen, bounds);
        return !intersection.IsEmpty && intersection.Width >= 100 && intersection.Height >= 50;
    }
}
