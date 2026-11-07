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

    /// <summary>Отправитель решения; внедрение позволяет проверить настоящий жизненный цикл окна без IPC.</summary>
    private readonly Func<ResolveNetworkAlertRequest, CancellationToken, Task<bool>> _resolutionSender;

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

    /// <summary>Подтверждение службы, разрешающее окончательное закрытие карточки.</summary>
    private bool _resolutionConfirmed;

    /// <summary>После ошибки автоматический повтор запрещён до нового явного выбора пользователя.</summary>
    private bool _resolutionFailed;
    /// <summary>Флаг принудительного закрытия карточки в обход ожидания подтверждения службы.</summary>
    private bool _forceClosing;

    /// <summary>Признак паузы таймера из-за наведения курсора (изменяется только в UI-потоке).</summary>
    /// <summary>Выполнено ли первоначальное размещение на мониторе владельца или курсора.</summary>
    private bool _initialPlacementComplete;

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
    /// <param name="resolutionSender">Необязательный адаптер подтверждения решения для изолированной проверки.</param>
    public NetworkConnectionAlertWindow(
        NetworkConnectionAlertDto alert,
        PipeClient pipeClient,
        int countdownSeconds = 30,
        Func<ResolveNetworkAlertRequest, CancellationToken, Task<bool>>? resolutionSender = null)
    {
        _alert = alert ?? throw new ArgumentNullException(nameof(alert));
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));
        _resolutionSender = resolutionSender ?? _pipeClient.ResolveNetworkAlertAsync;
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
        Closing += OnWindowClosing;
        MouseLeftButtonDown += OnDragMove;
        SizeChanged += OnWindowSizeChanged;

        // Интеллектуальная пауза при наведении курсора мыши
        MouseEnter += OnWindowMouseEnter;
        MouseLeave += OnWindowMouseLeave;

        // Стартовая позиция до первого показа: правый нижний угол рабочей области владельца или курсора.
        // Это исключает кратковременный «прыжок» окна из координаты (0,0) до первого RepositionWindow.
        var fallbackArea = WindowHelper.GetCurrentMonitorWorkArea(this, initialPlacement: true);
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
        SetButtonsEnabled(true);
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

    /// <summary>Преобразует системное закрытие в подтверждаемую блокировку на сеанс.</summary>
    /// <param name="sender">Закрываемое окно.</param>
    /// <param name="e">Аргументы, позволяющие дождаться ответа службы без блокировки диспетчера.</param>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_resolutionConfirmed || !IsLoaded || _forceClosing)
            return;

        if (_resolutionFailed)
        {
            _forceClosing = true;
            return;
        }

        e.Cancel = true;
        ResolveAndClose(NetworkAlertResolution.BlockSession);
    }

    /// <summary>
    /// Приостанавливает обратный отсчёт при наведении курсора на окно.
    /// </summary>
    /// <param name="sender">Источник события.</param>
    /// <param name="e">Аргументы события мыши.</param>
    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        if (Volatile.Read(ref _isResolved) != 0 || _resolutionFailed)
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
        if (Volatile.Read(ref _isResolved) != 0 || _resolutionFailed)
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

    /// <summary>Измеряет неподвижную панель действий с учётом реальной внутренней ширины и оформления окна.</summary>
    /// <returns>Высота панели действий и внешних отступов в DIP.</returns>
    private double CalculateFixedHeight()
    {
        var chromeWidth = RootBorder.Margin.Left + RootBorder.Margin.Right + RootBorder.Padding.Left
            + RootBorder.Padding.Right + RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right;
        var chromeHeight = RootBorder.Margin.Top + RootBorder.Margin.Bottom + RootBorder.Padding.Top
            + RootBorder.Padding.Bottom + RootBorder.BorderThickness.Top + RootBorder.BorderThickness.Bottom;
        FooterPanel.Measure(new Size(Math.Max(1.0, Width - chromeWidth), double.PositiveInfinity));
        return FooterPanel.DesiredSize.Height + chromeHeight;
    }



    /// <summary>Ограничивает общую прокрутку сведений, оставляя доступное место панели действий.</summary>
    /// <param name="workArea">Рабочая область выбранного монитора в DIP.</param>
    private void UpdateContentViewport(Rect workArea)
    {
        var availableHeight = Math.Max(1.0, workArea.Height - ScreenMargin * 2);
        var fixedHeight = CalculateFixedHeight();
        var chromeHeight = RootBorder.Margin.Top + RootBorder.Margin.Bottom + RootBorder.Padding.Top
            + RootBorder.Padding.Bottom + RootBorder.BorderThickness.Top + RootBorder.BorderThickness.Bottom;
        ContentScrollViewer.MaxHeight = Math.Max(0.0, availableHeight - fixedHeight);
        FooterScrollViewer.MaxHeight = Math.Max(1.0, availableHeight - chromeHeight);
        AiScrollViewer.MaxHeight = double.PositiveInfinity;
    }



    /// <summary>Подбирает размеры окна и области прокрутки в пределах фактической рабочей области.</summary>
    /// <param name="workArea">Рабочая область монитора в DIP с положительными конечными размерами.</param>
    internal void ApplyAdaptiveGeometry(Rect workArea)
    {
        if (workArea.IsEmpty || !double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height)
            || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea), "Рабочая область должна иметь конечные положительные размеры.");
        var availableWidth = Math.Max(1.0, workArea.Width - ScreenMargin * 2);
        var availableHeight = Math.Max(1.0, workArea.Height - ScreenMargin * 2);
        MinWidth = Math.Min(MinimumWindowWidth, availableWidth);
        MaxWidth = Math.Min(AdaptiveMaxWindowWidth, availableWidth);
        MinHeight = Math.Min(MinimumWindowHeight, availableHeight);
        MaxHeight = availableHeight;
        var targetWidth = GetPreferredWidth(workArea);
        if (double.IsNaN(Width) || Math.Abs(Width - targetWidth) > 1.0)
            Width = targetWidth;
        UpdateContentViewport(workArea);
    }



    /// <summary>Выбирает удобную ширину, не превышающую фактически доступное место.</summary>
    /// <param name="workArea">Рабочая область текущего монитора в DIP.</param>
    /// <returns>Предпочтительная ширина окна в DIP.</returns>
    internal static double GetPreferredWidth(Rect workArea)
    {
        var availableWidth = Math.Max(1.0, workArea.Width - ScreenMargin * 2);
        var targetWidth = workArea.Height <= 750.0 ? AdaptiveMaxWindowWidth
            : workArea.Height <= 850.0 ? 520.0 : DefaultWindowWidth;
        return Math.Min(targetWidth, availableWidth);
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



            var workArea = WindowHelper.GetCurrentMonitorWorkArea(this, initialPlacement: !_initialPlacementComplete);
            _initialPlacementComplete = true;
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
        if (Volatile.Read(ref _isResolved) != 0 || _isPausedByHover || _resolutionFailed)
            return;

        if (!_alert.CanBlockSession)
        {
            _countdownTimer.Stop();
            TxtCountdown.Text = LocalizationService.Instance["NetAlertPathRequired"];
            return;
        }

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
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_resolutionFailed)
        {
            _forceClosing = true;
            FadeOutAndClose();
            return;
        }

        ResolveAndClose(NetworkAlertResolution.BlockSession);
    }

    /// <summary>
    /// Атомарно начинает применение решения, отменяет AI-запрос и асинхронно ожидает
    /// подтверждение службы. Карточка удерживает глобальный запрет повторного окна до ACK.
    /// </summary>
    /// <param name="resolution">Выбранное пользователем решение.</param>
    private void ResolveAndClose(NetworkAlertResolution resolution)
    {
        // Interlocked.Exchange гарантирует ровно одно срабатывание даже при одновременном
        // клике и тике таймера: проверка и установка флага выполняются атомарно,
        // что защищает от повторного входа через асинхронные пути и повторную отправку решения.
        if (Interlocked.Exchange(ref _isResolved, 1) != 0)
            return;

        // Мгновенная отмена AI-запроса: пользовательское действие имеет приоритет.
        CancelAiEvaluation();
        _countdownTimer.Stop();
        SetButtonsEnabled(false);
        _resolutionFailed = false;
        TxtCountdown.Text = LocalizationService.Instance["NetAlertApplying"];

        var request = new ResolveNetworkAlertRequest
        {
            AlertId = _alert.AlertId,
            ProcessPath = _alert.ProcessPath ?? string.Empty,
            ProcessName = _alert.ProcessName ?? string.Empty,
            RemoteAddress = _alert.RemoteAddress ?? string.Empty,
            RemotePort = _alert.RemotePort,
            Resolution = resolution
        };

        _ = SendResolutionInBackgroundAsync(request);
    }

    /// <summary>
    /// Отправляет решение без блокировки UI и закрывает карточку только после успешного ACK.
    /// Ошибка сохраняет ту же карточку и позволяет явно повторить выбор.
    /// </summary>
    /// <param name="request">Запрос резолва алерта.</param>
    private async Task SendResolutionInBackgroundAsync(ResolveNetworkAlertRequest request)
    {
        var applied = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            applied = await _resolutionSender(request, cts.Token).WaitAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            App.Log($"NetworkConnectionAlertWindow.Resolve timeout: {ex.Message}");
        }
        catch (Exception ex)
        {
            App.Log($"NetworkConnectionAlertWindow.Resolve: {ex.Message}");
        }

        if (Volatile.Read(ref _isClosed) != 0)
            return;

        if (applied)
        {
            Resolution = request.Resolution;
            _resolutionConfirmed = true;
            FadeOutAndClose();
            return;
        }

        App.Log($"NetworkConnectionAlertWindow.Resolve rejected: alert={request.AlertId}, decision={request.Resolution}");
        _resolutionFailed = true;
        Interlocked.Exchange(ref _isResolved, 0);
        SetButtonsEnabled(true);
        TxtCountdown.Text = LocalizationService.Instance["NetAlertApplyFailed"];
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
        BtnWhitelist.IsEnabled = enabled && _alert.CanBlockSession;
        BtnAllowSession.IsEnabled = enabled && _alert.CanAllowSession;
        BtnBlockSession.IsEnabled = enabled && _alert.CanBlockSession;
        BtnBlockAlways.IsEnabled = enabled && _alert.CanBlockSession;
        BtnAllowPort.IsEnabled = enabled;
        BtnClose.IsEnabled = enabled && _alert.CanBlockSession;
        BtnAllowSession.ToolTip = _alert.CanAllowSession
            ? LocalizationService.Instance.NetAlertAllowSessionTip
            : LocalizationService.Instance["NetAlertSessionAllowUnavailable"];
        BtnBlockSession.ToolTip = _alert.CanBlockSession
            ? LocalizationService.Instance.NetAlertBlockSessionTip
            : LocalizationService.Instance["NetAlertPathRequired"];
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(BtnAllowSession, true);
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(BtnBlockSession, true);
        TxtSessionRestriction.Visibility = _alert.CanAllowSession && _alert.CanBlockSession ? Visibility.Collapsed : Visibility.Visible;
        TxtSessionRestriction.Text = LocalizationService.Instance[_alert.CanBlockSession ? "NetAlertSessionAllowUnavailable" : "NetAlertPathRequired"];
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
