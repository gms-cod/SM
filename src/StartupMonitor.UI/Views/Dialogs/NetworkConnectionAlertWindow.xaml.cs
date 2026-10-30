using System;
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
/// </summary>
public partial class NetworkConnectionAlertWindow : Window
{
    private readonly NetworkConnectionAlertDto _alert;
    private readonly PipeClient _pipeClient;
    private readonly DispatcherTimer _countdownTimer;
    private readonly int _totalSeconds;
    private int _remainingSeconds;
    private bool _isResolved;
    private bool _isPausedByHover;

    /// <summary>Решение, выбранное пользователем (или применённое по таймауту).</summary>
    public NetworkAlertResolution? Resolution { get; private set; }

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

        // Интеллектуальная пауза при наведении курсора мыши
        MouseEnter += OnWindowMouseEnter;
        MouseLeave += OnWindowMouseLeave;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        FillAlertFields();
        PositionBottomRight();
        FadeIn();

        UpdateCountdownText();
        _countdownTimer.Start();
        _ = LoadAiVerdictAsync();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        try { _countdownTimer.Stop(); } catch { }
        _countdownTimer.Tick -= OnCountdownTick;
    }

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isResolved) return;
        _isPausedByHover = true;
        _countdownTimer.Stop();
        try
        {
            TxtCountdown.Text = "Обратный отсчёт приостановлен (курсор мыши)";
        }
        catch { }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isResolved) return;
        _isPausedByHover = false;
        UpdateCountdownText();
        _countdownTimer.Start();
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    /// <summary>Заполняет поля карточки данными алерта и результатом GeoIP-резолва.</summary>
    private void FillAlertFields()
    {
        try
        {
            TxtProcessName.Text = string.IsNullOrWhiteSpace(_alert.ProcessName)
                ? "Неизвестный процесс"
                : _alert.ProcessName.Trim();
            TxtProcessPid.Text = $"PID: {_alert.ProcessId}";
            TxtProcessPath.Text = string.IsNullOrWhiteSpace(_alert.ProcessPath)
                ? "Путь неизвестен"
                : _alert.ProcessPath;
            TxtProcessPath.ToolTip = _alert.ProcessPath;

            TxtRemoteEndpoint.Text = _alert.RemotePort > 0
                ? $"{_alert.RemoteAddress}:{_alert.RemotePort}"
                : _alert.RemoteAddress;
            TxtRemoteEndpoint.ToolTip = TxtRemoteEndpoint.Text;

            TxtProtocol.Text = string.IsNullOrWhiteSpace(_alert.Protocol)
                ? "TCP"
                : _alert.Protocol.Trim().ToUpperInvariant();

            var (countryName, countryFlag, _) = GeoIpLookupService.Resolve(_alert.RemoteAddress);
            TxtCountryFlag.Text = string.IsNullOrEmpty(countryFlag) ? "🌐" : countryFlag;
            TxtCountryName.Text = string.IsNullOrEmpty(countryName) ? "Интернет" : countryName;

            UpdateCountdownText();
        }
        catch
        {
            // Zero-Crash: окно обязано показаться даже при битых данных алерта.
        }
    }

    /// <summary>Позиционирует окно в правом нижнем углу рабочей области (над панелью задач).</summary>
    private void PositionBottomRight()
    {
        try
        {
            UpdateLayout();
            var workArea = SystemParameters.WorkArea;
            const double margin = 10.0;

            var width = double.IsNaN(Width) || Width <= 0 ? ActualWidth : Width;
            if (width <= 0) width = 420;
            var height = ActualHeight > 0 ? ActualHeight : 460;

            Left = Math.Max(workArea.Left, workArea.Right - width - margin);
            Top = Math.Max(workArea.Top, workArea.Bottom - height - margin);
        }
        catch
        {
            // Игнорируем ошибки позиционирования
        }
    }

    private void FadeIn()
    {
        try
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
            BeginAnimation(OpacityProperty, anim);
        }
        catch
        {
            Opacity = 1;
        }
    }

    private void UpdateCountdownText()
    {
        try
        {
            if (!_isPausedByHover)
            {
                TxtCountdown.Text = $"Автоматическое решение через {_remainingSeconds} сек...";
            }
            CountdownBar.Value = Math.Max(0, _remainingSeconds);
        }
        catch { }
    }

    private async void OnCountdownTick(object? sender, EventArgs e)
    {
        if (_isPausedByHover) return;

        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            // По умолчанию по таймауту — безопасная сессионная блокировка
            await ResolveAndCloseAsync(NetworkAlertResolution.BlockSession).ConfigureAwait(true);
            return;
        }

        UpdateCountdownText();
    }

    private async void OnWhitelistClick(object sender, RoutedEventArgs e) =>
        await ResolveAndCloseAsync(NetworkAlertResolution.WhitelistProgram).ConfigureAwait(true);

    private async void OnAllowSessionClick(object sender, RoutedEventArgs e) =>
        await ResolveAndCloseAsync(NetworkAlertResolution.AllowSession).ConfigureAwait(true);

    private async void OnBlockSessionClick(object sender, RoutedEventArgs e) =>
        await ResolveAndCloseAsync(NetworkAlertResolution.BlockSession).ConfigureAwait(true);

    private async void OnBlockAlwaysClick(object sender, RoutedEventArgs e) =>
        await ResolveAndCloseAsync(NetworkAlertResolution.BlacklistProgram).ConfigureAwait(true);

    private async void OnAllowPortClick(object sender, RoutedEventArgs e) =>
        await ResolveAndCloseAsync(NetworkAlertResolution.AllowPort).ConfigureAwait(true);

    private async void OnCloseClick(object sender, RoutedEventArgs e) =>
        await ResolveAndCloseAsync(NetworkAlertResolution.BlockSession).ConfigureAwait(true);

    /// <summary>
    /// Отправляет решение службе через <see cref="PipeClient.ResolveNetworkAlertAsync"/>
    /// и закрывает окно с плавной анимацией затухания.
    /// </summary>
    private async Task ResolveAndCloseAsync(NetworkAlertResolution resolution)
    {
        if (_isResolved)
            return;
        _isResolved = true;
        Resolution = resolution;

        try { _countdownTimer.Stop(); } catch { }
        SetButtonsEnabled(false);

        try
        {
            var request = new ResolveNetworkAlertRequest
            {
                AlertId = _alert.AlertId,
                ProcessPath = _alert.ProcessPath ?? string.Empty,
                ProcessName = _alert.ProcessName ?? string.Empty,
                RemoteAddress = _alert.RemoteAddress ?? string.Empty,
                RemotePort = _alert.RemotePort,
                Resolution = resolution
            };

            await _pipeClient.ResolveNetworkAlertAsync(request).ConfigureAwait(true);
        }
        catch
        {
            // Не блокируем закрытие окна при временной недоступности службы
        }

        FadeOutAndClose();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        try
        {
            BtnWhitelist.IsEnabled = enabled;
            BtnAllowSession.IsEnabled = enabled;
            BtnBlockSession.IsEnabled = enabled;
            BtnBlockAlways.IsEnabled = enabled;
            BtnAllowPort.IsEnabled = enabled;
            BtnClose.IsEnabled = enabled;
        }
        catch { }
    }

    /// <summary>Асинхронно загружает или отображает вердикт ИИ о назначении и рисках блокировки процесса.</summary>
    private async Task LoadAiVerdictAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_alert.AiPurpose))
            {
                DisplayAiVerdict(_alert.AiPurpose, _alert.AiBlockingRisk, _alert.AiRecommendation, _alert.AiRecommendedAction);
                return;
            }

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(25));
            var result = await _pipeClient.EvaluateNetworkAlertAsync(
                _alert.AlertId,
                _alert.ProcessName,
                _alert.ProcessPath,
                _alert.RemoteAddress,
                _alert.RemotePort,
                cts.Token).ConfigureAwait(true);

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
        catch
        {
            DisplayAiFallback();
        }
    }

    private void DisplayAiVerdict(string? purpose, string? risk, string? rec, string? action)
    {
        try
        {
            AiLoadingPanel.Visibility = Visibility.Collapsed;
            AiContentPanel.Visibility = Visibility.Visible;

            TxtAiPurpose.Text = !string.IsNullOrWhiteSpace(purpose) ? purpose : "Назначение не определено.";
            TxtAiBlockingRisk.Text = !string.IsNullOrWhiteSpace(risk) ? risk : "Особых рисков для критических служб не выявлено.";
            TxtAiRecommendation.Text = !string.IsNullOrWhiteSpace(rec) ? rec : "Решение на усмотрение пользователя.";

            if (!string.IsNullOrWhiteSpace(action))
            {
                var actionText = action switch
                {
                    "AllowAlways" => "Рекомендация: Разрешить всегда",
                    "AllowSession" => "Рекомендация: Разрешить на сессию",
                    "BlockAlways" => "Рекомендация: Заблокировать всегда",
                    "BlockSession" => "Рекомендация: Заблокировать на сессию",
                    _ => $"Рекомендация: {action}"
                };
                TxtAiRecommendedAction.Text = actionText;
            }
            else
            {
                TxtAiRecommendedAction.Text = string.Empty;
            }
        }
        catch { }
    }

    private void DisplayAiFallback()
    {
        try
        {
            AiLoadingPanel.Visibility = Visibility.Collapsed;
            AiContentPanel.Visibility = Visibility.Visible;
            TxtAiPurpose.Text = "Анализ нейросетью недоступен (проверьте настройки AI в параметрах).";
            TxtAiBlockingRisk.Text = "Стандартное сетевое подключение приложения.";
            TxtAiRecommendation.Text = "Применяйте стандартные правила брандмауэра.";
            TxtAiRecommendedAction.Text = string.Empty;
        }
        catch { }
    }

    /// <summary>Плавное затухание перед закрытием (200 мс).</summary>
    private void FadeOutAndClose()
    {
        try
        {
            var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
            anim.Completed += (_, _) =>
            {
                try { Close(); } catch { }
            };
            BeginAnimation(OpacityProperty, anim);
        }
        catch
        {
            try { Close(); } catch { }
        }
    }
}