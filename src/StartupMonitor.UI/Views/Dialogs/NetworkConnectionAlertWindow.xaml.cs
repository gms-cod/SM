using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Всплывающее Toast/Flyout окно уведомления о новом сетевом подключении.
/// Не забирает фокус ввода (ShowActivated = false, Focusable = false).
/// Поддерживает 5 действий, 30-секундный таймер с интеллектуальной паузой при наведении курсора.
/// Окно использует нижнюю привязку (Bottom-Anchored): при росте высоты из-за ответа ИИ
/// верхняя граница смещается вверх, а кнопки действий остаются неподвижными над панелью задач.
/// </summary>
public partial class NetworkConnectionAlertWindow : Window
{
    /// <summary>Отступ от краёв рабочей области монитора (в DIP).</summary>
    private const double ScreenMargin = 10.0;

    /// <summary>Ширина окна по умолчанию для обычных высоких мониторов (в DIP).</summary>
    private const double DefaultWindowWidth = 490.0;

    /// <summary>Максимальная адаптивная ширина окна на компактных мониторах (в DIP).</summary>
    private const double AdaptiveMaxWindowWidth = 560.0;

    /// <summary>Минимально допустимая ширина окна на узких экранах (в DIP).</summary>
    private const double MinimumWindowWidth = 320.0;

    /// <summary>Минимальная высота окна (в DIP).</summary>
    private const double MinimumWindowHeight = 220.0;

    /// <summary>Резервная высота, если размеры ещё не рассчитаны layout-движком (в DIP).</summary>
    private const double FallbackWindowHeight = 440.0;

    /// <summary>Данные алерта, отображаемые в карточке.</summary>
    private readonly NetworkConnectionAlertDto _alert;

    /// <summary>Клиент IPC для отправки решения и запроса AI-оценки.</summary>
    private readonly PipeClient _pipeClient;

    /// <summary>Таймер обратного отсчёта с шагом в одну секунду.</summary>
    private readonly DispatcherTimer _countdownTimer;

    /// <summary>Источник отмены единственного AI-запроса окна.</summary>
    private readonly CancellationTokenSource _aiCts = new();

    /// <summary>Полная длительность обратного отсчёта в секундах.</summary>
    private readonly int _totalSeconds;

    /// <summary>Оставшееся время обратного отсчёта в секундах.</summary>
    private int _remainingSeconds;

    /// <summary>Флаг «решение уже принято» (0 — нет, 1 — да). Доступ через Interlocked.</summary>
    private int _isResolved;

    /// <summary>Флаг «окно закрыто» (0 — нет, 1 — да). Доступ через Interlocked/Volatile.</summary>
    private int _isClosed;

    /// <summary>Признак паузы таймера из-за наведения курсора (изменяется только в UI-потоке).</summary>
    private bool _isPausedByHover;

    /// <summary>Признак ручного перемещения окна пользователем (изменяется только в UI-потоке).</summary>
    private bool _isManuallyMoved;

    /// <summary>Высота окна на предыдущем шаге layout для нижней привязки.</summary>
    private double _lastLayoutHeight = double.NaN;

    /// <summary>Решение, выбранное пользователем (или применённое по таймауту).</summary>
    public NetworkAlertResolution? Resolution { get; private set; }

    /// <summary>
    /// Создаёт окно уведомления о новом сетевом подключении.
    /// </summary>
    /// <param name="alert">Данные алерта (не может быть null).</param>
    /// <param name="pipeClient">Клиент IPC (не может быть null).</param>
    /// <param name="countdownSeconds">Длительность обратного отсчёта; значения ≤ 0 заменяются на 30 секунд.</param>
    public NetworkConnectionAlertWindow(
        NetworkConnectionAlertDto alert,
        PipeClient pipeClient,
        int countdownSeconds = 30)
    {
        _alert = alert ?? throw new ArgumentNullException(nameof(alert));
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));
        _totalSeconds = countdownSeconds > 0 ? countdownSeconds : 30;
        _remainingSeconds = _totalSeconds;

        InitializeComponent();

        CountdownBar.Maximum = _totalSeconds;
        CountdownBar.Value = _totalSeconds;

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnCountdownTick;

        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
        MouseLeftButtonDown += OnDragMove;
        SizeChanged += OnWindowSizeChanged;

        // Интеллектуальная пауза при наведении курсора мыши
        MouseEnter += OnWindowMouseEnter;
        MouseLeave += OnWindowMouseLeave;

        // Стартовая позиция до первого показа: правый нижний угол основной рабочей области.
        // Это исключает кратковременный «прыжок» окна из координаты (0,0) до первого RepositionWindow.
        var fallbackArea = SystemParameters.WorkArea;
        var fallbackWidth = GetPreferredWidth(fallbackArea);
        Left = Math.Max(fallbackArea.Left + ScreenMargin, fallbackArea.Right - fallbackWidth - ScreenMargin);
        Top = Math.Max(fallbackArea.Top + ScreenMargin, fallbackArea.Bottom - FallbackWindowHeight - ScreenMargin);
    }

    /// <summary>
    /// Инициализирует данные карточки, позиционирует окно и запускает таймер и AI-анализ.
    /// </summary>
    /// <param name="sender">Источник события загрузки окна.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (Volatile.Read(ref _isClosed) != 0)
            return;

        FillAlertFields();
        RepositionWindow(animate: false);
        FadeIn();

        UpdateCountdownText();
        _countdownTimer.Start();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Передаём токен отмены: закрытие окна или клик по любой кнопке мгновенно
        // прекращает ожидание AI-ответа на отдельном pipe-соединении.
        _ = LoadAiVerdictAsync(_aiCts.Token);
    }

    /// <summary>
    /// Освобождает подписки и отменяет незавершённый AI-запрос при закрытии окна.
    /// </summary>
    /// <param name="sender">Источник события закрытия.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
            return;

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SizeChanged -= OnWindowSizeChanged;
        MouseEnter -= OnWindowMouseEnter;
        MouseLeave -= OnWindowMouseLeave;
        MouseLeftButtonDown -= OnDragMove;
        Loaded -= OnWindowLoaded;

        _countdownTimer.Stop();
        _countdownTimer.Tick -= OnCountdownTick;

        // Отмена немедленно разблокирует отдельный pipe-канал AI-оценки.
        CancelAiEvaluation();
    }

    /// <summary>
    /// Приостанавливает обратный отсчёт при наведении курсора на окно.
    /// </summary>
    /// <param name="sender">Источник события.</param>
    /// <param name="e">Аргументы события мыши.</param>
    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        if (Volatile.Read(ref _isResolved) != 0)
            return;

        _isPausedByHover = true;
        _countdownTimer.Stop();
        TxtCountdown.Text = LocalizationService.Instance.NetAlertCountdownPaused;
    }

    /// <summary>
    /// Возобновляет обратный отсчёт при покидании курсором окна.
    /// </summary>
    /// <param name="sender">Источник события.</param>
    /// <param name="e">Аргументы события мыши.</param>
    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (Volatile.Read(ref _isResolved) != 0)
            return;

        _isPausedByHover = false;
        UpdateCountdownText();
        _countdownTimer.Start();
    }

    /// <summary>
    /// Перемещает окно за заголовок и удерживает его в границах текущего монитора.
    /// </summary>
    /// <param name="sender">Источник события.</param>
    /// <param name="e">Аргументы события мыши.</param>
    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        try
        {
            _isManuallyMoved = true;
            DragMove();
            ClampInsideCurrentMonitor();
        }
        catch (InvalidOperationException ex)
        {
            // DragMove выбрасывает исключение, если кнопка мыши уже отпущена.
            App.Log($"NetworkConnectionAlertWindow.DragMove: {ex.Message}");
        }
    }

    /// <summary>
    /// Заполняет поля карточки данными алерта и результатом GeoIP-резолва.
    /// Все пользовательские подписи берутся из сервиса локализации.
    /// </summary>
    private void FillAlertFields()
    {
        try
        {
            var loc = LocalizationService.Instance;

            TxtProcessName.Text = string.IsNullOrWhiteSpace(_alert.ProcessName)
                ? loc.NetAlertUnknownProcess
                : _alert.ProcessName.Trim();
            TxtProcessPid.Text = string.Format(loc.NetAlertPidFormat, _alert.ProcessId);
            TxtProcessPath.Text = string.IsNullOrWhiteSpace(_alert.ProcessPath)
                ? loc.NetAlertUnknownPath
                : _alert.ProcessPath;
            TxtProcessPath.ToolTip = _alert.ProcessPath;

            TxtRemoteEndpoint.Text = _alert.RemotePort > 0
                ? $"{_alert.RemoteAddress}:{_alert.RemotePort}"
                : _alert.RemoteAddress;
            TxtRemoteEndpoint.ToolTip = TxtRemoteEndpoint.Text;

            TxtProtocol.Text = string.IsNullOrWhiteSpace(_alert.Protocol)
                ? loc.NetAlertProtocolDefault
                : _alert.Protocol.Trim().ToUpperInvariant();

            var (countryName, countryFlag, _) = GeoIpLookupService.Resolve(_alert.RemoteAddress);
            TxtCountryFlag.Text = string.IsNullOrEmpty(countryFlag) ? "🌐" : countryFlag;
            TxtCountryName.Text = string.IsNullOrEmpty(countryName) ? loc.NetAlertCountryInternet : countryName;

            UpdateCountdownText();
        }
        catch (Exception ex)
        {
            // Zero-Crash: окно обязано показаться даже при битых данных алерта.
            App.Log($"NetworkConnectionAlertWindow.FillAlertFields: {ex.Message}");
        }
    }

    /// <summary>
    /// Пересчитывает позицию окна при смене настроек отображения (разрешение, DPI, панель задач).
    /// </summary>
    /// <param name="sender">Источник системного события.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        try
        {
            // SystemEvents вызывает обработчик из собственного потока — маршалируем в UI-поток.
            Dispatcher.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _isClosed) == 0)
                    RepositionWindow(animate: false);
            });
        }
        catch (InvalidOperationException ex)
        {
            // Dispatcher уже остановлен (приложение завершается).
            App.Log($"NetworkConnectionAlertWindow.DisplaySettingsChanged: {ex.Message}");
        }
    }

    /// <summary>
    /// Реализует нижнюю привязку: при росте высоты (например, после ответа ИИ)
    /// верхняя граница сдвигается на ту же дельту, поэтому нижняя кромка с кнопками
    /// остаётся неподвижной, а окно растёт вверх.
    /// </summary>
    /// <param name="sender">Источник события изменения размера.</param>
    /// <param name="e">Аргументы события с новым и предыдущим размером.</param>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Volatile.Read(ref _isClosed) != 0)
            return;
        if (!e.HeightChanged && !e.WidthChanged)
            return;

        if (e.HeightChanged && !double.IsNaN(_lastLayoutHeight) && !double.IsNaN(Top))
        {
            var delta = e.NewSize.Height - _lastLayoutHeight;
            if (Math.Abs(delta) > 0.5)
                Top -= delta;
        }

        _lastLayoutHeight = e.NewSize.Height;
        RepositionWindow(animate: false);
    }

    /// <summary>
    /// Вычисляет суммарную высоту всех не-AI элементов окна:
    /// внешние отступы Window/Border, верхняя панель (заголовок, процесс, адрес),
    /// футер (таймер, 5 кнопок, прогресс-бар) и постоянные отступы карточки ИИ.
    /// </summary>
    /// <returns>Высота в DIP, необходимая для гарантированного отображения не-AI контента.</returns>
    private double CalculateNonAiHeight()
    {
        var currentWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : DefaultWindowWidth);

        UpperPanel.Measure(new Size(currentWidth, double.PositiveInfinity));
        FooterPanel.Measure(new Size(currentWidth, double.PositiveInfinity));

        var upperHeight = UpperPanel.DesiredSize.Height;
        var footerHeight = FooterPanel.DesiredSize.Height;

        if (upperHeight <= 0) upperHeight = 166.0;
        if (footerHeight <= 0) footerHeight = 142.0;

        const double windowChromeHeight = 52.0;
        const double aiCardChromeHeight = 32.0;
        const double safetyBuffer = 6.0;

        return upperHeight + footerHeight + windowChromeHeight + aiCardChromeHeight + safetyBuffer;
    }

    /// <summary>
    /// Динамически вычисляет и применяет MaxHeight для AiScrollViewer на основе
    /// доступной высоты экрана (workArea.Height - nonAiHeight), чтобы окно никогда
    /// не выходило за пределы монитора, а футер с кнопками оставался полностью видимым.
    /// </summary>
    /// <param name="workArea">Рабочая область текущего монитора в DIP.</param>
    private void UpdateAiScrollViewerMaxHeight(Rect workArea)
    {
        var maxAllowedWindowHeight = Math.Max(MinimumWindowHeight, workArea.Height - (ScreenMargin * 2));
        var nonAiHeight = CalculateNonAiHeight();
        var availableAiHeight = maxAllowedWindowHeight - nonAiHeight;
        var targetMaxHeight = Math.Max(80.0, availableAiHeight);

        AiScrollViewer.MaxHeight = targetMaxHeight;
    }

    /// <summary>
    /// Динамически подбирает ширину и максимальные размеры окна под рабочую область монитора.
    /// Ширина плавно уменьшается на компактных экранах и никогда не выходит за границы экрана.
    /// </summary>
    /// <param name="workArea">Рабочая область текущего монитора в DIP.</param>
    private void ApplyAdaptiveGeometry(Rect workArea)
    {
        var availableWidth = Math.Max(MinimumWindowWidth, workArea.Width - (ScreenMargin * 2));
        var targetWidth = GetPreferredWidth(workArea);
        var maxHeight = Math.Max(MinimumWindowHeight, workArea.Height - (ScreenMargin * 2));

        MinWidth = Math.Min(MinimumWindowWidth, availableWidth);
        MaxWidth = Math.Min(AdaptiveMaxWindowWidth, availableWidth);
        MinHeight = Math.Min(MinimumWindowHeight, maxHeight);
        MaxHeight = maxHeight;

        // Меняем ширину только при существенном расхождении — это предотвращает
        // бесконечный цикл SizeChanged -> RepositionWindow -> Width -> SizeChanged.
        if (double.IsNaN(Width) || Math.Abs(Width - targetWidth) > 1.0)
            Width = targetWidth;

        UpdateAiScrollViewerMaxHeight(workArea);
    }

    /// <summary>
    /// Вычисляет предпочтительную ширину окна для заданной рабочей области.
    /// На экранах с ограниченной высотой (<= 750 DIP) окно расширяется до 560 DIP,
    /// чтобы многострочный текст занимал меньше места по вертикали.
    /// На мониторах умеренной высоты (<= 850 DIP) используется 520 DIP,
    /// а на просторных мониторах сохраняется каноничная ширина 490 DIP.
    /// </summary>
    /// <param name="workArea">Рабочая область монитора в DIP.</param>
    /// <returns>Ширина окна в DIP.</returns>
    private static double GetPreferredWidth(Rect workArea)
    {
        var availableWidth = Math.Max(MinimumWindowWidth, workArea.Width - (ScreenMargin * 2));

        double targetWidth;
        if (workArea.Height <= 750.0)
        {
            targetWidth = AdaptiveMaxWindowWidth;
        }
        else if (workArea.Height <= 850.0)
        {
            targetWidth = 520.0;
        }
        else
        {
            targetWidth = DefaultWindowWidth;
        }

        return Math.Clamp(targetWidth, MinimumWindowWidth, Math.Min(AdaptiveMaxWindowWidth, availableWidth));
    }

    /// <summary>
    /// Выполняет динамическое позиционирование окна в правом нижнем углу рабочей области текущего монитора.
    /// Реализует привязку к нижнему краю экрана («Bottom-Anchored»): при росте высоты окна
    /// (например, при подгрузке вердикта ИИ) окно динамически поднимается вверх,
    /// гарантируя, что все кнопки действий всегда остаются строго на своих координатах над панелью задач.
    /// </summary>
    /// <param name="animate">Флаг плавного анимирования координаты Top.</param>
    private void RepositionWindow(bool animate = false)
    {
        try
        {
            if (Volatile.Read(ref _isClosed) != 0)
                return;

            var workArea = WindowHelper.GetCurrentMonitorWorkArea(this);
            if (workArea.Width <= 0 || workArea.Height <= 0)
                return;

            ApplyAdaptiveGeometry(workArea);

            var width = ActualWidth > 0 ? ActualWidth : GetPreferredWidth(workArea);
            var height = ActualHeight > 0
                ? ActualHeight
                : (DesiredSize.Height > 0 ? DesiredSize.Height : FallbackWindowHeight);

            // Гарантируем, что окно не выйдет за рабочую область даже на очень малых экранах.
            var maxHeight = Math.Max(MinimumWindowHeight, workArea.Height - (ScreenMargin * 2));
            height = Math.Min(height, maxHeight);

            var targetLeft = Math.Max(workArea.Left + ScreenMargin, workArea.Right - width - ScreenMargin);

            // Строгая нижняя привязка: нижний край окна всегда зафиксирован на (workArea.Bottom - ScreenMargin).
            // При росте height значение targetTop уменьшается на ту же дельту, окно растет вверх,
            // а экранные координаты кнопок футера остаются неизменными.
            var targetTop = workArea.Bottom - height - ScreenMargin;

            if (_isManuallyMoved)
            {
                ClampInsideCurrentMonitor();
                return;
            }

            Left = targetLeft;

            if (animate && !double.IsNaN(Top) && Math.Abs(Top - targetTop) > 1.0)
            {
                BeginAnimation(TopProperty, null);
                var anim = new DoubleAnimation
                {
                    From = Top,
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };
                anim.Completed += (_, _) =>
                {
                    if (Volatile.Read(ref _isClosed) == 0)
                        Top = targetTop;
                };
                BeginAnimation(TopProperty, anim);
            }
            else
            {
                BeginAnimation(TopProperty, null);
                Top = targetTop;
            }
        }
        catch (Exception ex)
        {
            // Zero-Crash: защита от непредвиденных исключений layout.
            App.Log($"NetworkConnectionAlertWindow.Reposition: {ex.Message}");
        }
    }

    /// <summary>
    /// Удерживает окно строго в пределах видимой рабочей области монитора при ручном перемещении.
    /// </summary>
    private void ClampInsideCurrentMonitor()
    {
        try
        {
            var workArea = WindowHelper.GetCurrentMonitorWorkArea(this);
            if (workArea.Width <= 0 || workArea.Height <= 0)
                return;

            var width = ActualWidth > 0 ? ActualWidth : GetPreferredWidth(workArea);
            var height = ActualHeight > 0 ? ActualHeight : FallbackWindowHeight;

            var minLeft = workArea.Left + ScreenMargin;
            var minTop = workArea.Top + ScreenMargin;
            var maxLeft = Math.Max(minLeft, workArea.Right - width - ScreenMargin);
            var maxTop = Math.Max(minTop, workArea.Bottom - height - ScreenMargin);

            if (double.IsNaN(Left) || double.IsNaN(Top))
            {
                Left = maxLeft;
                Top = maxTop;
                return;
            }

            Left = Math.Clamp(Left, minLeft, maxLeft);
            Top = Math.Clamp(Top, minTop, maxTop);
        }
        catch (Exception ex)
        {
            App.Log($"NetworkConnectionAlertWindow.ClampInsideCurrentMonitor: {ex.Message}");
        }
    }

    /// <summary>
    /// Плавно проявляет окно при появлении.
    /// </summary>
    private void FadeIn()
    {
        try
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
            BeginAnimation(OpacityProperty, anim);
        }
        catch (InvalidOperationException ex)
        {
            App.Log($"NetworkConnectionAlertWindow.FadeIn: {ex.Message}");
            Opacity = 1;
        }
    }

    /// <summary>
    /// Обновляет текст и прогресс-бар обратного отсчёта (только в UI-потоке).
    /// </summary>
    private void UpdateCountdownText()
    {
        if (_isPausedByHover)
            return;

        TxtCountdown.Text = string.Format(
            LocalizationService.Instance.NetAlertCountdownFormat,
            _remainingSeconds);
        CountdownBar.Value = Math.Max(0, _remainingSeconds);
    }

    /// <summary>
    /// Обрабатывает секундный тик обратного отсчёта. По истечении времени
    /// применяется безопасное решение — блокировка на текущую сессию.
    /// </summary>
    /// <param name="sender">Источник события таймера.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnCountdownTick(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _isResolved) != 0 || _isPausedByHover)
            return;

        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            ResolveAndClose(NetworkAlertResolution.BlockSession);
            return;
        }

        UpdateCountdownText();
    }

    /// <summary>Пользователь выбрал «Добавить программу в белый список».</summary>
    private void OnWhitelistClick(object sender, RoutedEventArgs e) =>
        ResolveAndClose(NetworkAlertResolution.WhitelistProgram);

    /// <summary>Пользователь выбрал «Разрешить на текущую сессию».</summary>
    private void OnAllowSessionClick(object sender, RoutedEventArgs e) =>
        ResolveAndClose(NetworkAlertResolution.AllowSession);

    /// <summary>Пользователь выбрал «Заблокировать на текущую сессию».</summary>
    private void OnBlockSessionClick(object sender, RoutedEventArgs e) =>
        ResolveAndClose(NetworkAlertResolution.BlockSession);

    /// <summary>Пользователь выбрал «Заблокировать всегда».</summary>
    private void OnBlockAlwaysClick(object sender, RoutedEventArgs e) =>
        ResolveAndClose(NetworkAlertResolution.BlacklistProgram);

    /// <summary>Пользователь выбрал «Разрешить удалённый порт».</summary>
    private void OnAllowPortClick(object sender, RoutedEventArgs e) =>
        ResolveAndClose(NetworkAlertResolution.AllowPort);

    /// <summary>Пользователь закрыл окно крестиком (трактуется как блокировка на сессию).</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        ResolveAndClose(NetworkAlertResolution.BlockSession);

    /// <summary>
    /// Атомарно фиксирует решение, немедленно отменяет AI-запрос и запускает закрытие окна.
    /// Отправка решения службе выполняется в фоне, поэтому UI никогда не ждёт IPC-ответ
    /// и не зависает даже при недоступной службе.
    /// </summary>
    /// <param name="resolution">Выбранное пользователем решение.</param>
    private void ResolveAndClose(NetworkAlertResolution resolution)
    {
        // Interlocked.Exchange гарантирует ровно одно срабатывание даже при одновременном
        // клике и тике таймера: проверка и установка флага выполняются атомарно,
        // что защищает от повторного входа через асинхронные пути и повторную отправку решения.
        if (Interlocked.Exchange(ref _isResolved, 1) != 0)
            return;

        Resolution = resolution;

        // Мгновенная отмена AI-запроса: пользовательское действие имеет приоритет.
        CancelAiEvaluation();
        _countdownTimer.Stop();
        SetButtonsEnabled(false);

        var request = new ResolveNetworkAlertRequest
        {
            AlertId = _alert.AlertId,
            ProcessPath = _alert.ProcessPath ?? string.Empty,
            ProcessName = _alert.ProcessName ?? string.Empty,
            RemoteAddress = _alert.RemoteAddress ?? string.Empty,
            RemotePort = _alert.RemotePort,
            Resolution = resolution
        };

        // Решение досылается асинхронно: окно исчезает сразу, а служба получает команду
        // по основному каналу, который больше не занят длительной AI-оценкой.
        _ = SendResolutionInBackgroundAsync(request);

        FadeOutAndClose();
    }

    /// <summary>
    /// Отправляет решение службе в фоне с ограниченным таймаутом.
    /// Исключения не пробрасываются в UI-поток и журналируются.
    /// </summary>
    /// <param name="request">Запрос резолва алерта.</param>
    private async Task SendResolutionInBackgroundAsync(ResolveNetworkAlertRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _pipeClient.ResolveNetworkAlertAsync(request, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Служба не ответила за 5 секунд — окно уже закрыто, повтор не требуется.
        }
        catch (Exception ex)
        {
            App.Log($"NetworkConnectionAlertWindow.Resolve: {ex.Message}");
        }
    }

    /// <summary>
    /// Безопасно отменяет незавершённый AI-запрос.
    /// </summary>
    private void CancelAiEvaluation()
    {
        try
        {
            _aiCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // AI-запрос уже завершился и освободил источник отмены.
        }
    }

    /// <summary>
    /// Отключает все кнопки действий после принятия решения.
    /// </summary>
    /// <param name="enabled">True — включить кнопки, false — заблокировать.</param>
    private void SetButtonsEnabled(bool enabled)
    {
        BtnWhitelist.IsEnabled = enabled;
        BtnAllowSession.IsEnabled = enabled;
        BtnBlockSession.IsEnabled = enabled;
        BtnBlockAlways.IsEnabled = enabled;
        BtnAllowPort.IsEnabled = enabled;
        BtnClose.IsEnabled = enabled;
    }

    /// <summary>
    /// Асинхронно загружает или отображает вердикт ИИ о назначении и рисках блокировки процесса.
    /// </summary>
    /// <param name="ct">Токен отмены, привязанный к жизненному циклу окна.</param>
    private async Task LoadAiVerdictAsync(CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_alert.AiPurpose))
            {
                DisplayAiVerdict(_alert.AiPurpose, _alert.AiBlockingRisk, _alert.AiRecommendation, _alert.AiRecommendedAction);
                return;
            }

            var result = await _pipeClient.EvaluateNetworkAlertAsync(
                _alert.AlertId,
                _alert.ProcessName,
                _alert.ProcessPath,
                _alert.RemoteAddress,
                _alert.RemotePort,
                ct).ConfigureAwait(true);

            // После await обязательно проверяем отмену/закрытие: продолжение может
            // выполниться уже после уничтожения визуального дерева окна.
            if (ct.IsCancellationRequested || Volatile.Read(ref _isClosed) != 0)
                return;

            if (result != null && !string.IsNullOrWhiteSpace(result.Purpose))
            {
                _alert.AiPurpose = result.Purpose;
                _alert.AiBlockingRisk = result.BlockingRisk;
                _alert.AiRecommendation = result.Recommendation;
                _alert.AiRecommendedAction = result.RecommendedAction;

                DisplayAiVerdict(result.Purpose, result.BlockingRisk, result.Recommendation, result.RecommendedAction);
            }
            else
            {
                DisplayAiFallback();
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемая отмена при закрытии окна или действии пользователя.
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && Volatile.Read(ref _isClosed) == 0)
            {
                App.Log($"NetworkConnectionAlertWindow.LoadAiVerdict: {ex.Message}");
                DisplayAiFallback();
            }
        }
        finally
        {
            // Детерминированное освобождение источника отмены после завершения запроса.
            try { _aiCts.Dispose(); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Отображает вердикт нейросети в карточке ИИ.
    /// </summary>
    /// <param name="purpose">Назначение программы.</param>
    /// <param name="risk">Риски блокировки.</param>
    /// <param name="rec">Рекомендация.</param>
    /// <param name="action">Код рекомендуемого действия.</param>
    private void DisplayAiVerdict(string? purpose, string? risk, string? rec, string? action)
    {
        if (Volatile.Read(ref _isClosed) != 0)
            return;

        var loc = LocalizationService.Instance;

        AiLoadingPanel.Visibility = Visibility.Collapsed;
        AiScrollViewer.Visibility = Visibility.Visible;
        AiContentPanel.Visibility = Visibility.Visible;

        TxtAiPurpose.Text = !string.IsNullOrWhiteSpace(purpose) ? purpose : loc.NetAlertAiPurposeFallback;
        TxtAiBlockingRisk.Text = !string.IsNullOrWhiteSpace(risk) ? risk : loc.NetAlertAiRiskFallback;
        TxtAiRecommendation.Text = !string.IsNullOrWhiteSpace(rec) ? rec : loc.NetAlertAiRecommendationFallback;

        TxtAiRecommendedAction.Text = action switch
        {
            "AllowAlways" => loc.NetAlertRecAllowAlways,
            "AllowSession" => loc.NetAlertRecAllowSession,
            "BlockAlways" => loc.NetAlertRecBlockAlways,
            "BlockSession" => loc.NetAlertRecBlockSession,
            _ when !string.IsNullOrWhiteSpace(action) => action!,
            _ => string.Empty
        };

        UpdateLayout();
        RepositionWindow(animate: false);
    }

    /// <summary>
    /// Отображает резервный текст, если AI-анализ недоступен.
    /// </summary>
    private void DisplayAiFallback()
    {
        if (Volatile.Read(ref _isClosed) != 0)
            return;

        var loc = LocalizationService.Instance;

        AiLoadingPanel.Visibility = Visibility.Collapsed;
        AiScrollViewer.Visibility = Visibility.Visible;
        AiContentPanel.Visibility = Visibility.Visible;
        TxtAiPurpose.Text = loc.NetAlertAiUnavailable;
        TxtAiBlockingRisk.Text = loc.NetAlertAiStandardRisk;
        TxtAiRecommendation.Text = loc.NetAlertAiStandardRecommendation;
        TxtAiRecommendedAction.Text = string.Empty;

        UpdateLayout();
        RepositionWindow(animate: false);
    }

    /// <summary>Плавное затухание перед закрытием (200 мс).</summary>
    private void FadeOutAndClose()
    {
        try
        {
            var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
            anim.Completed += (_, _) =>
            {
                try { Close(); }
                catch (InvalidOperationException ex)
                {
                    App.Log($"NetworkConnectionAlertWindow.Close: {ex.Message}");
                }
            };
            BeginAnimation(OpacityProperty, anim);
        }
        catch (InvalidOperationException ex)
        {
            App.Log($"NetworkConnectionAlertWindow.FadeOut: {ex.Message}");
            try { Close(); }
            catch (InvalidOperationException closeEx)
            {
                App.Log($"NetworkConnectionAlertWindow.CloseFallback: {closeEx.Message}");
            }
        }
    }
}
