using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.UI.Views;

/// <summary>
/// Центр сетевой безопасности: статус брандмауэра Windows, режимы фильтрации,
/// таблица правил, сводка Port Defender и журнал активных банов.
/// Zero-Crash: все обращения к службе обёрнуты в try-catch, ошибки уходят в статусную строку.
/// </summary>
public partial class NetworkSecurityHubView : UserControl
{
    private readonly PipeClient _pipeClient;
    private readonly DispatcherTimer _banTimer;
    private readonly DispatcherTimer _bansRefreshTimer;
    private readonly DispatcherTimer _liveConnTimer;
    private readonly DispatcherTimer _alertPollerTimer;
    private bool _alertsEnabled;
    private bool _suppressAlertsToggle;
    private bool _alertWindowOpen;
    private bool _isPollingAlerts;

    private List<FirewallRuleRow> _allRules = new();
    private List<BanRow> _banRows = new();
    private PortDefenderConfigDto? _defenderConfig;
    private FirewallFilteringMode _currentMode = FirewallFilteringMode.Normal;

    private string _searchText = string.Empty;
    private RuleFilter _activeFilter = RuleFilter.All;
    private bool _isLoading;
    private bool _suppressModeEvents;

    private List<LiveConnectionRow> _allConnections = new();
    private string _connSearchText = string.Empty;
    private ConnFilter _activeConnFilter = ConnFilter.All;
    private bool _isLoadingConn;
    private HashSet<string> _blockedIps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Фильтр таблицы правил брандмауэра.</summary>
    private enum RuleFilter
    {
        All,
        Inbound,
        Outbound,
        Blocking,
        Managed
    }

    /// <summary>Фильтр таблицы активных соединений (вкладка Live).</summary>
    private enum ConnFilter
    {
        All,
        Wan,
        Lan,
        Established,
        Listening
    }

    /// <summary>
    /// Строка таблицы правил: обёртка над <see cref="FirewallRuleDto"/>
    /// (<see cref="FirewallRuleDto"/> sealed, поэтому композиция вместо наследования),
    /// добавляет готовые к отображению свойства (без конвертеров в XAML).
    /// </summary>
    public sealed class FirewallRuleRow
    {
        public FirewallRuleRow(FirewallRuleDto dto) => Dto = dto ?? new FirewallRuleDto();

        /// <summary>Исходный DTO (мутируется только через свойства обёртки).</summary>
        public FirewallRuleDto Dto { get; }

        public string Name { get => Dto.Name; set => Dto.Name = value; }
        public string Description { get => Dto.Description; set => Dto.Description = value; }
        public FirewallDirection Direction { get => Dto.Direction; set => Dto.Direction = value; }
        public FirewallAction Action { get => Dto.Action; set => Dto.Action = value; }
        public FirewallProtocol Protocol { get => Dto.Protocol; set => Dto.Protocol = value; }
        public string LocalPorts { get => Dto.LocalPorts; set => Dto.LocalPorts = value; }
        public string? RemotePorts { get => Dto.RemotePorts; set => Dto.RemotePorts = value; }
        public string RemoteAddresses { get => Dto.RemoteAddresses; set => Dto.RemoteAddresses = value; }
        public string? ApplicationPath { get => Dto.ApplicationPath; set => Dto.ApplicationPath = value; }
        public string? ServiceName { get => Dto.ServiceName; set => Dto.ServiceName = value; }
        public bool Enabled { get => Dto.Enabled; set => Dto.Enabled = value; }
        public FirewallProfile Profiles { get => Dto.Profiles; set => Dto.Profiles = value; }
        public string? Grouping { get => Dto.Grouping; set => Dto.Grouping = value; }
        public bool IsManaged { get => Dto.IsManaged; set => Dto.IsManaged = value; }

        /// <summary>Создаёт независимую копию DTO для передачи в редактор правил.</summary>
        public FirewallRuleDto ToDtoCopy() => new()
        {
            Name = Dto.Name,
            Description = Dto.Description,
            Direction = Dto.Direction,
            Action = Dto.Action,
            Protocol = Dto.Protocol,
            LocalPorts = Dto.LocalPorts,
            RemotePorts = Dto.RemotePorts,
            RemoteAddresses = Dto.RemoteAddresses,
            ApplicationPath = Dto.ApplicationPath,
            ServiceName = Dto.ServiceName,
            Enabled = Dto.Enabled,
            Profiles = Dto.Profiles,
            Grouping = Dto.Grouping,
            IsManaged = Dto.IsManaged
        };

        /// <summary>Короткое имя программы (файл без пути) или прочерк.</summary>
        public string ShortProgram
        {
            get
            {
                var path = ApplicationPath;
                if (string.IsNullOrWhiteSpace(path))
                    return "—";
                try
                {
                    return Path.GetFileName(path.Trim()) is { Length: > 0 } file ? file : path;
                }
                catch
                {
                    return path;
                }
            }
        }

        /// <summary>Протокол для отображения.</summary>
        public string ProtocolDisplay => Protocol switch
        {
            FirewallProtocol.Tcp => "TCP",
            FirewallProtocol.Udp => "UDP",
            _ => "Любой"
        };

        /// <summary>Профили для отображения на русском.</summary>
        public string ProfilesDisplay
        {
            get
            {
                if (Profiles == FirewallProfile.All)
                    return "Все";
                var parts = new List<string>(3);
                if (Profiles.HasFlag(FirewallProfile.Domain))
                    parts.Add("Доменный");
                if (Profiles.HasFlag(FirewallProfile.Private))
                    parts.Add("Частный");
                if (Profiles.HasFlag(FirewallProfile.Public))
                    parts.Add("Публичный");
                return parts.Count > 0 ? string.Join(", ", parts) : Profiles.ToString();
            }
        }
    }

    /// <summary>
    /// Строка таблицы банов: обёртка над <see cref="ActiveBanDto"/>
    /// с живым обратным отсчётом до снятия бана.
    /// </summary>
    public sealed class BanRow : INotifyPropertyChanged
    {
        private string _remainingText = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public BanRow(ActiveBanDto dto) => Dto = dto;

        public ActiveBanDto Dto { get; }

        public string Ip => Dto.Ip;
        public int TargetPort => Dto.TargetPort;
        public string TargetProcess => string.IsNullOrWhiteSpace(Dto.TargetProcess) ? "—" : Dto.TargetProcess;
        public int AttackRate => Dto.AttackRate;
        public string Reason => string.IsNullOrWhiteSpace(Dto.Reason) ? "—" : Dto.Reason;
        public int RepeatCount => Dto.RepeatCount;

        public string RemainingText
        {
            get => _remainingText;
            private set
            {
                if (_remainingText != value)
                {
                    _remainingText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
                }
            }
        }

        /// <summary>Пересчитывает текст обратного отсчёта от текущего времени UTC.</summary>
        public void RefreshCountdown()
        {
            try
            {
                if (Dto.ExpiresAtUtc == null)
                {
                    RemainingText = "Навсегда";
                    return;
                }

                var remaining = Dto.ExpiresAtUtc.Value - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    RemainingText = "Истёк";
                else if (remaining.TotalHours >= 1)
                    RemainingText = $"{(int)remaining.TotalHours} ч {remaining.Minutes:00} мин";
                else if (remaining.TotalMinutes >= 1)
                    RemainingText = $"{(int)remaining.TotalMinutes} мин {remaining.Seconds:00} с";
                else
                    RemainingText = $"{remaining.Seconds} с";
            }
            catch
            {
                RemainingText = "—";
            }
        }
    }

    /// <summary>Конструктор для XAML-дизайнера (создаёт неподключённый клиент).</summary>
    public NetworkSecurityHubView() : this(new PipeClient())
    {
    }

    /// <summary>Основной конструктор: принимает клиент именованного канала службы.</summary>
    /// <param name="pipeClient">Клиент связи со службой StartupMonitor.Service.</param>
    public NetworkSecurityHubView(PipeClient pipeClient)
    {
        InitializeComponent();
        _pipeClient = pipeClient ?? new PipeClient();

        _banTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _banTimer.Tick += (_, _) => RefreshBanCountdowns();

        _bansRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _bansRefreshTimer.Tick += async (_, _) => await LoadBansAsync(silent: true);

        _liveConnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _liveConnTimer.Tick += async (_, _) => await LoadLiveConnectionsAsync();

        _alertPollerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _alertPollerTimer.Tick += async (_, _) => await PollNetworkAlertsAsync();

        Loaded += async (_, _) =>
        {
            await RefreshAllAsync();
            // Live-соединения: первичная загрузка + автообновление по чекбоксу.
            try
            {
                if (ChkAutoRefreshConn.IsChecked == true && !_liveConnTimer.IsEnabled)
                    _liveConnTimer.Start();
            }
            catch { }
            try
            {
                if (!_alertPollerTimer.IsEnabled)
                    _alertPollerTimer.Start();
            }
            catch { }
        };
        Unloaded += (_, _) => Deactivate();
    }

    /// <summary>Финализатор: страховка от зомби-таймеров при потере Unloaded.</summary>
    ~NetworkSecurityHubView()
    {
        try { _banTimer?.Stop(); } catch { }
        try { _bansRefreshTimer?.Stop(); } catch { }
        try { _liveConnTimer?.Stop(); } catch { }
        try { _alertPollerTimer?.Stop(); } catch { }
    }

    /// <summary>
    /// Останавливает все фоновые таймеры хаба (зомби-таймеры).
    /// Вызывается при уходе со страницы, сворачивании в трей и закрытии окна.
    /// Потокобезопасен: при вызове с фонового потока маршалится на UI-диспетчер.
    /// </summary>
    public void Deactivate()
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(Deactivate)); } catch { }
                return;
            }
        }
        catch { }

        try { _banTimer?.Stop(); } catch { }
        try { _bansRefreshTimer?.Stop(); } catch { }
        try { _liveConnTimer?.Stop(); } catch { }
        try { _alertPollerTimer?.Stop(); } catch { }
    }

    /// <summary>
    /// Возобновляет фоновые таймеры хаба при входе на страницу / разворачивании окна
    /// и запускает немедленное фоновое обновление.
    /// </summary>
    public void Activate()
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke(new Action(Activate)); } catch { }
                return;
            }
        }
        catch { }

        try
        {
            if (_banTimer != null && !_banTimer.IsEnabled)
                _banTimer.Start();
            if (_bansRefreshTimer != null && !_bansRefreshTimer.IsEnabled)
                _bansRefreshTimer.Start();
            try
            {
                if (_liveConnTimer != null && !_liveConnTimer.IsEnabled
                    && ChkAutoRefreshConn != null && ChkAutoRefreshConn.IsChecked == true)
                    _liveConnTimer.Start();
            }
            catch { }
            try
            {
                if (_alertPollerTimer != null && !_alertPollerTimer.IsEnabled && _alertsEnabled)
                    _alertPollerTimer.Start();
            }
            catch { }
        }
        catch { }

        // Немедленное фоновое обновление (fire-and-forget, Zero-Crash внутри RefreshAllAsync).
        try { _ = RefreshAllAsync(); } catch { }
    }

    // ===== Загрузка =====

    /// <summary>Полное обновление всех секций хаба.</summary>
    public async Task RefreshAllAsync()
    {
        if (_isLoading)
            return;
        _isLoading = true;
        try
        {
            SetStatus("Загрузка данных сетевой безопасности…");
            await LoadFirewallStatusAsync();
            await LoadFilteringModeAsync();
            await LoadAlertsEnabledStateAsync();
            await LoadRulesAsync();
            await LoadDefenderAsync();
            await LoadBansAsync(silent: true);
            await LoadLiveConnectionsAsync();
            SetStatus($"Обновлено в {DateTime.Now:HH:mm:ss} • Правил: {_allRules.Count} • Банов: {_banRows.Count} • Соединений: {_allConnections.Count}");
            if (!_banTimer.IsEnabled)
                _banTimer.Start();
            if (!_bansRefreshTimer.IsEnabled)
                _bansRefreshTimer.Start();
            if (!_alertPollerTimer.IsEnabled)
                _alertPollerTimer.Start();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка загрузки: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Загружает статус брандмауэра и обновляет бейджи профилей.</summary>
    private async Task LoadFirewallStatusAsync()
    {
        try
        {
            var status = await _pipeClient.GetFirewallStatusAsync();
            if (status == null)
            {
                TxtFirewallState.Text = "Брандмауэр: нет данных";
                TxtFirewallDetails.Text = "Служба недоступна — проверьте подключение.";
                SetProfileBadge(TxtProfileDomain, ProfileDomainBadge, "Доменный", null);
                SetProfileBadge(TxtProfilePrivate, ProfilePrivateBadge, "Частный", null);
                SetProfileBadge(TxtProfilePublic, ProfilePublicBadge, "Публичный", null);
                return;
            }

            TxtFirewallState.Text = status.Available
                ? (status.Enabled ? "Брандмауэр: включён" : "Брандмауэр: выключен")
                : "Брандмауэр: недоступен";
            TxtFirewallDetails.Text =
                $"Управляемых правил: {status.ManagedRulesCount} • Заблокированных IP: {status.BlockedIpsCount}";
            SetProfileBadge(TxtProfileDomain, ProfileDomainBadge, "Доменный", status.DomainEnabled);
            SetProfileBadge(TxtProfilePrivate, ProfilePrivateBadge, "Частный", status.PrivateEnabled);
            SetProfileBadge(TxtProfilePublic, ProfilePublicBadge, "Публичный", status.PublicEnabled);
        }
        catch (Exception ex)
        {
            TxtFirewallState.Text = "Брандмауэр: ошибка";
            TxtFirewallDetails.Text = ex.Message;
            SetStatus($"Не удалось получить статус брандмауэра: {ex.Message}");
        }
    }

    /// <summary>Обновляет текст и подсветку одного бейджа профиля.</summary>
    private static void SetProfileBadge(
        System.Windows.Controls.TextBlock label,
        System.Windows.Controls.Border badge,
        string title,
        bool? enabled)
    {
        try
        {
            label.Text = enabled == null ? $"{title}: —" : $"{title}: {(enabled.Value ? "вкл" : "выкл")}";
            badge.Background = enabled == true
                ? new SolidColorBrush(Color.FromArgb(0x20, 0x10, 0x7C, 0x41))
                : new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80));
        }
        catch
        {
            label.Text = $"{title}: —";
        }
    }

    /// <summary>Загружает текущий режим фильтрации и выставляет радиокнопки.</summary>
    private async Task LoadFilteringModeAsync()
    {
        try
        {
            _currentMode = await _pipeClient.GetFirewallFilteringModeAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось получить режим фильтрации: {ex.Message}");
            return;
        }

        _suppressModeEvents = true;
        try
        {
            RbModeNormal.IsChecked = _currentMode == FirewallFilteringMode.Normal;
            RbModeWhitelist.IsChecked = _currentMode == FirewallFilteringMode.WhitelistOutbound;
            RbModeKillswitch.IsChecked = _currentMode == FirewallFilteringMode.KillswitchBlockAll;
        }
        finally
        {
            _suppressModeEvents = false;
        }
    }

    /// <summary>Загружает состояние тумблера интерактивных уведомлений и синхронизирует чекбокс.</summary>
    private async Task LoadAlertsEnabledStateAsync()
    {
        try
        {
            _alertsEnabled = await _pipeClient.GetNetworkAlertsEnabledAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось получить состояние интерактивных уведомлений: {ex.Message}");
            return;
        }

        _suppressAlertsToggle = true;
        try
        {
            ChkInteractiveAlerts.IsChecked = _alertsEnabled;
            try
            {
                var silentPid0 = await _pipeClient.GetFirewallSilentPid0Async();
                ChkSilentPid0.IsChecked = silentPid0;
            }
            catch { }
        }
        catch { }
        finally
        {
            _suppressAlertsToggle = false;
        }
    }

    /// <summary>Обработчик тумблера интерактивных уведомлений: отправляет состояние службе.</summary>
    private async void OnSilentPid0Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        var silent = ChkSilentPid0.IsChecked == true;
        try
        {
            var ok = await _pipeClient.SetFirewallSilentPid0Async(silent);
            SetStatus(silent
                ? "Тихий режим PID=0 включен (алерты ядра подавлены)."
                : "Тихий режим PID=0 выключен.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка переключения тихого режима PID=0: {ex.Message}");
        }
    }

    private async void OnInteractiveAlertsChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressAlertsToggle || _isLoading)
            return;

        var enabled = ChkInteractiveAlerts.IsChecked == true;
        try
        {
            SetStatus(enabled ? "Включение интерактивных уведомлений…" : "Выключение интерактивных уведомлений…");
            var ok = await _pipeClient.SetNetworkAlertsEnabledAsync(enabled);
            if (ok)
            {
                _alertsEnabled = enabled;
                SetStatus(enabled
                    ? "Интерактивные уведомления включены."
                    : "Интерактивные уведомления выключены.");
            }
            else
            {
                // Откатываем чекбокс к фактическому состоянию.
                _suppressAlertsToggle = true;
                try { ChkInteractiveAlerts.IsChecked = _alertsEnabled; } catch { }
                finally { _suppressAlertsToggle = false; }
                SetStatus("Служба не подтвердила изменение интерактивных уведомлений.");
            }
        }
        catch (Exception ex)
        {
            _suppressAlertsToggle = true;
            try { ChkInteractiveAlerts.IsChecked = _alertsEnabled; } catch { }
            finally { _suppressAlertsToggle = false; }
            SetStatus($"Ошибка переключения интерактивных уведомлений: {ex.Message}");
        }
    }

    /// <summary>
    /// Фоновый опрос ожидающих сетевых алертов (тик _alertPollerTimer, 2.5 сек).
    /// Если уведомления включены и есть алерты, а окно ещё не открыто — открывает тост.
    /// Zero-Crash: любые ошибки подавляются, повторный вход запрещён флагом.
    /// </summary>
    private async Task PollNetworkAlertsAsync()
    {
        if (_isPollingAlerts)
            return;
        _isPollingAlerts = true;
        try
        {
            if (!_alertsEnabled || _alertWindowOpen)
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

            if (!ShouldShowAlertWindow(_alertsEnabled, alerts, _alertWindowOpen))
                return;

            ShowNetworkAlertWindow(alerts![0]);
        }
        catch { }
        finally
        {
            _isPollingAlerts = false;
        }
    }

    /// <summary>
    /// Чистая решающая функция тика поллера (покрыта UI-тестами без WPF-диспетчера):
    /// окно показывается только если уведомления включены, есть алерты и окно ещё не открыто.
    /// </summary>
    public static bool ShouldShowAlertWindow(
        bool alertsEnabled,
        IReadOnlyList<NetworkConnectionAlertDto>? alerts,
        bool windowAlreadyOpen)
    {
        if (!alertsEnabled)
            return false;
        if (windowAlreadyOpen)
            return false;
        if (alerts == null || alerts.Count == 0)
            return false;
        return true;
    }

    /// <summary>
    /// Формирует <see cref="ResolveNetworkAlertRequest"/> из DTO алерта и решения
    /// (единая точка маппинга для окна-тоста и UI-тестов).
    /// </summary>
    public static ResolveNetworkAlertRequest BuildResolveRequest(
        NetworkConnectionAlertDto alert,
        NetworkAlertResolution resolution)
    {
        if (alert == null)
            throw new ArgumentNullException(nameof(alert));

        return new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId ?? string.Empty,
            ProcessPath = alert.ProcessPath ?? string.Empty,
            ProcessName = alert.ProcessName ?? string.Empty,
            RemoteAddress = alert.RemoteAddress ?? string.Empty,
            Resolution = resolution
        };
    }

    /// <summary>
    /// Открывает <c>NetworkConnectionAlertWindow</c> для первого ожидающего алерта.
    /// Окно само позиционируется в правом нижнем углу; флаг _alertWindowOpen
    /// блокирует дубли до закрытия окна.
    /// </summary>
    private void ShowNetworkAlertWindow(NetworkConnectionAlertDto alert)
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<NetworkConnectionAlertDto>(ShowNetworkAlertWindow), alert);
                return;
            }

            if (_alertWindowOpen || alert == null)
                return;

            _alertWindowOpen = true;
            var window = new NetworkConnectionAlertWindow(alert, _pipeClient);
            window.Closed += (_, _) => { _alertWindowOpen = false; };
            window.Show();
        }
        catch
        {
            _alertWindowOpen = false;
        }
    }

    /// <summary>Загружает правила через GetAllFirewallRulesAsync и применяет фильтр.</summary>
    private async Task LoadRulesAsync()
    {
        try
        {
            var rules = await _pipeClient.GetAllFirewallRulesAsync();
            if (rules != null)
            {
                _allRules = rules.Select(r => new FirewallRuleRow(r)).OrderBy(r => r.Name).ToList();
                ApplyRuleFilter();
            }
            else
            {
                SetStatus("Не удалось получить правила брандмауэра (служба занята или недоступна).");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось загрузить правила брандмауэра: {ex.Message}");
        }
    }

    /// <summary>Загружает конфигурацию Port Defender и обновляет сводку.</summary>
    private async Task LoadDefenderAsync()
    {
        try
        {
            _defenderConfig = await _pipeClient.GetPortDefenderConfigAsync();
            if (_defenderConfig == null)
            {
                TxtDefenderState.Text = "Port Defender: нет данных";
                TxtDefenderDetails.Text = "Служба недоступна.";
                TxtDefenderPorts.Text = "—";
                return;
            }

            var ports = (_defenderConfig.ProtectedPorts ?? new List<int>())
                .Distinct().OrderBy(p => p).ToList();
            TxtDefenderState.Text = _defenderConfig.Enabled
                ? "Port Defender: защита активна"
                : "Port Defender: защита выключена";
            TxtDefenderDetails.Text =
                $"Порог: {_defenderConfig.MaxAttempts} попыток / {_defenderConfig.WindowSeconds} с • " +
                $"Бан: {FormatBanDuration(_defenderConfig.BanDurationMinutes)} • " +
                $"Доверять локальным сетям: {(_defenderConfig.TrustPrivateNetworks ? "да" : "нет")}";
            TxtDefenderPorts.Text = ports.Count > 0 ? string.Join(", ", ports) : "Нет защищаемых портов";
        }
        catch (Exception ex)
        {
            TxtDefenderState.Text = "Port Defender: ошибка";
            TxtDefenderDetails.Text = ex.Message;
            TxtDefenderPorts.Text = "—";
            SetStatus($"Не удалось загрузить конфигурацию Port Defender: {ex.Message}");
        }
    }

    private static string FormatBanDuration(int minutes) =>
        minutes <= 0 ? "навсегда" : minutes >= 1440 ? $"{minutes / 1440} дн." : $"{minutes} мин.";

    /// <summary>Загружает активные баны через GetActiveBansAsync.</summary>
    private async Task LoadBansAsync(bool silent)
    {
        try
        {
            if (!silent)
                SetStatus("Загрузка активных банов…");
            var bans = await _pipeClient.GetActiveBansAsync() ?? new List<ActiveBanDto>();
            _banRows = bans
                .OrderByDescending(b => b.BannedAtUtc)
                .Select(b =>
                {
                    var row = new BanRow(b);
                    row.RefreshCountdown();
                    return row;
                })
                .ToList();
            GridBans.ItemsSource = _banRows;
            TxtBansCount.Text = $"Активных банов: {_banRows.Count}";
            PanelBansEmpty.Visibility = _banRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!silent)
                SetStatus($"Загружено банов: {_banRows.Count}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось загрузить активные баны: {ex.Message}");
        }
    }

    /// <summary>Обновляет обратный отсчёт банов каждую секунду (без обращений к службе).</summary>
    private void RefreshBanCountdowns()
    {
        try
        {
            foreach (var row in _banRows)
                row.RefreshCountdown();
        }
        catch
        {
            // Zero-Crash: тик таймера никогда не роняет UI.
        }
    }

    // ===== Фильтрация правил =====

    /// <summary>Применяет поисковую строку и выбранный фильтр к таблице правил.</summary>
    private void ApplyRuleFilter()
    {
        try
        {
            var query = (_searchText ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = _allRules.Where(r =>
            {
                switch (_activeFilter)
                {
                    case RuleFilter.Inbound:
                        if (r.Direction != FirewallDirection.Inbound && r.Direction != FirewallDirection.Both)
                            return false;
                        break;
                    case RuleFilter.Outbound:
                        if (r.Direction != FirewallDirection.Outbound && r.Direction != FirewallDirection.Both)
                            return false;
                        break;
                    case RuleFilter.Blocking:
                        if (r.Action != FirewallAction.Block)
                            return false;
                        break;
                    case RuleFilter.Managed:
                        if (!r.IsManaged)
                            return false;
                        break;
                }

                if (string.IsNullOrEmpty(query))
                    return true;

                return (r.Name ?? string.Empty).ToLowerInvariant().Contains(query)
                    || (r.LocalPorts ?? string.Empty).ToLowerInvariant().Contains(query)
                    || (r.RemotePorts ?? string.Empty).ToLowerInvariant().Contains(query)
                    || (r.RemoteAddresses ?? string.Empty).ToLowerInvariant().Contains(query)
                    || (r.ApplicationPath ?? string.Empty).ToLowerInvariant().Contains(query)
                    || (r.Description ?? string.Empty).ToLowerInvariant().Contains(query);
            }).ToList();

            GridRules.ItemsSource = filtered;
            PanelRulesEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка фильтрации правил: {ex.Message}");
        }
    }

    private void OnRuleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            _searchText = TxtRuleSearch.Text ?? string.Empty;
            ApplyRuleFilter();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка поиска: {ex.Message}");
        }
    }

    private void OnRuleFilterChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ReferenceEquals(sender, FilterInbound))
                _activeFilter = RuleFilter.Inbound;
            else if (ReferenceEquals(sender, FilterOutbound))
                _activeFilter = RuleFilter.Outbound;
            else if (ReferenceEquals(sender, FilterBlocking))
                _activeFilter = RuleFilter.Blocking;
            else if (ReferenceEquals(sender, FilterManaged))
                _activeFilter = RuleFilter.Managed;
            else
                _activeFilter = RuleFilter.All;

            if (GridRules != null)
                ApplyRuleFilter();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка переключения фильтра: {ex.Message}");
        }
    }

    // ===== Режимы фильтрации / Killswitch =====

    private async void OnFilteringModeChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressModeEvents || _isLoading)
            return;

        FirewallFilteringMode target = FirewallFilteringMode.Normal;
        try
        {
            if (ReferenceEquals(sender, RbModeWhitelist))
                target = FirewallFilteringMode.WhitelistOutbound;
            else if (ReferenceEquals(sender, RbModeKillswitch))
                target = FirewallFilteringMode.KillswitchBlockAll;

            if (target == FirewallFilteringMode.KillswitchBlockAll)
            {
                var confirm = MessageBox.Show(Window.GetWindow(this),
                    LocalizationService.Instance.NetKillswitchConfirmMsg,
                    LocalizationService.Instance.NetKillswitchConfirmTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes)
                {
                    await LoadFilteringModeAsync();
                    return;
                }
            }

            SetStatus("Переключение режима фильтрации…");
            var result = await _pipeClient.SetFirewallFilteringModeAsync(target);
            if (result != null && result.Success)
            {
                _currentMode = target;
                SetStatus(result.Message);
                await LoadFirewallStatusAsync();
            }
            else
            {
                SetStatus(result?.Message ?? "Не удалось переключить режим фильтрации.");
                await LoadFilteringModeAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка переключения режима фильтрации: {ex.Message}");
            try { await LoadFilteringModeAsync(); } catch { }
        }
    }

    // ===== Действия с правилами =====

    /// <summary>Включение/выключение правила из чекбокса таблицы.</summary>
    private async void OnRuleToggleChecked(object sender, RoutedEventArgs e)
    {
        CheckBox? checkBox = null;
        FirewallRuleRow? row = null;
        try
        {
            checkBox = sender as CheckBox;
            row = checkBox?.DataContext as FirewallRuleRow;
            if (row == null)
                return;

            bool enable = checkBox?.IsChecked == true;
            SetStatus($"Переключение правила «{row.Name}»…");
            var result = await _pipeClient.ToggleFirewallRuleAsync(row.Name, row.Direction, enable);
            if (result != null && result.Success)
            {
                row.Enabled = enable;
                SetStatus(result.Message);
            }
            else
            {
                // Откатываем чекбокс к фактическому состоянию.
                checkBox!.IsChecked = row.Enabled;
                SetStatus(result?.Message ?? $"Не удалось переключить правило «{row.Name}».");
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (checkBox != null && row != null)
                    checkBox.IsChecked = row.Enabled;
            }
            catch { }
            SetStatus($"Ошибка переключения правила: {ex.Message}");
        }
    }

    private async void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var row = GridRules.SelectedItem as FirewallRuleRow;
            if (row == null)
            {
                SetStatus("Выберите правило для удаления.");
                return;
            }

            var confirm = MessageBox.Show(Window.GetWindow(this),
                LocalizationService.Instance.GetNetDeleteRuleConfirm(row.Name, row.Direction),
                LocalizationService.Instance.NetDeleteRuleConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            SetStatus($"Удаление правила «{row.Name}»…");
            var result = await _pipeClient.DeleteFirewallRuleAsync(row.Name, row.Direction);
            if (result != null && result.Success)
            {
                SetStatus(result.Message);
                await LoadRulesAsync();
            }
            else
            {
                SetStatus(result?.Message ?? $"Не удалось удалить правило «{row.Name}».");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка удаления правила: {ex.Message}");
        }
    }

    private void OnCreateRuleClick(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenRuleEditor(null);
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть редактор правил: {ex.Message}");
        }
    }

    private void OnEditRuleClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var row = GridRules.SelectedItem as FirewallRuleRow;
            if (row == null)
            {
                SetStatus("Выберите правило для редактирования.");
                return;
            }
            OpenRuleEditor(row);
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть редактор правил: {ex.Message}");
        }
    }

    private async void OnRuleGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var row = GridRules.SelectedItem as FirewallRuleRow;
            if (row != null)
                OpenRuleEditor(row);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть редактор правил: {ex.Message}");
        }
    }

    /// <summary>
    /// Открывает FirewallRuleEditWindow для создания (null) или редактирования правила.
    /// Окно создаётся соседним UI-воркером: поиск типа через рефлексию,
    /// чтобы хаб компилировался и работал независимо от готовности диалога.
    /// </summary>
    private async void OpenRuleEditor(FirewallRuleRow? existing)
    {
        try
        {
            var editorType = FindType("StartupMonitor.UI.Views.Dialogs.FirewallRuleEditWindow");
            if (editorType == null)
            {
                SetStatus("Окно редактирования правил (FirewallRuleEditWindow) пока недоступно.");
                return;
            }

            var owner = Window.GetWindow(this);
            object? dialog = TryCreateEditor(editorType, existing);
            if (dialog is not Window window)
            {
                SetStatus("Не удалось создать окно редактирования правил (несовместимый конструктор).");
                return;
            }

            if (owner != null && !ReferenceEquals(owner, window))
                window.Owner = owner;
            var accepted = window.ShowDialog() == true;
            if (accepted)
                await LoadRulesAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть редактор правил: {ex.Message}");
        }
    }

    /// <summary>Ищет тип сначала в текущей сборке, затем во всех загруженных.</summary>
    private static Type? FindType(string fullName)
    {
        try
        {
            var type = Type.GetType(fullName + ", StartupMonitor.UI");
            if (type != null)
                return type;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetType(fullName);
                    if (type != null)
                        return type;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Создаёт диалог редактора, перебирая известные сигнатуры конструкторов:
    /// (PipeClient, FirewallRuleDto), (PipeClient), ().
    /// </summary>
    private object? TryCreateEditor(Type editorType, FirewallRuleRow? existing)
    {
        // Копия DTO, чтобы неотправленные правки не трогали таблицу.
        FirewallRuleDto? dtoCopy = existing?.ToDtoCopy();

        var attempts = new Func<object?>[]
        {
            () => Activator.CreateInstance(editorType, _pipeClient, dtoCopy),
            () => Activator.CreateInstance(editorType, _pipeClient),
            () => Activator.CreateInstance(editorType)
        };

        foreach (var attempt in attempts)
        {
            try
            {
                var instance = attempt();
                if (instance != null)
                    return instance;
            }
            catch (MissingMethodException) { }
            catch (TargetInvocationException ex)
            {
                SetStatus($"Ошибка создания редактора правил: {ex.InnerException?.Message ?? ex.Message}");
                return null;
            }
            catch { }
        }
        return null;
    }

    private async void OnRefreshRulesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Обновление правил…");
            await LoadRulesAsync();
            SetStatus($"Загружено правил: {_allRules.Count}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка обновления правил: {ex.Message}");
        }
    }

    /// <summary>
    /// Анти-телеметрия в 1 клик: создаёт исходящее блокирующее правило
    /// для известных телеметрических диапазонов Microsoft.
    /// </summary>
    private async void OnAntiTelemetryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = MessageBox.Show(Window.GetWindow(this),
                "Создать исходящее блокирующее правило «StartupMonitor Anti-Telemetry» " +
                "для известных телеметрических диапазонов Microsoft?",
                "Анти-телеметрия (1 клик)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            SetStatus("Применение анти-телеметрических правил…");
            var rule = new FirewallRuleDto
            {
                Name = "StartupMonitor Anti-Telemetry (1-клик)",
                Description = "Блокировка исходящей телеметрии Microsoft. Создано Центром сетевой безопасности.",
                Direction = FirewallDirection.Outbound,
                Action = FirewallAction.Block,
                Protocol = FirewallProtocol.Any,
                LocalPorts = string.Empty,
                RemoteAddresses = "134.170.0.0/16, 157.56.0.0/14, 207.46.0.0/16, 65.52.0.0/14",
                Enabled = true,
                Profiles = FirewallProfile.All,
                Grouping = "StartupMonitor",
                IsManaged = true
            };

            var result = await _pipeClient.CreateOrUpdateFirewallRuleAsync(rule);
            if (result != null && result.Success)
            {
                SetStatus(result.Message);
                await LoadRulesAsync();
            }
            else
            {
                SetStatus(result?.Message ?? "Не удалось создать анти-телеметрические правила.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка анти-телеметрии: {ex.Message}");
        }
    }

    private async void OnRefreshStatusClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка обновления: {ex.Message}");
        }
    }

    // ===== Port Defender =====

    private void OnConfigureDefenderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var owner = Window.GetWindow(this);
            var dialog = new PortDefenderConfigWindow(_pipeClient);
            if (owner != null)
                dialog.Owner = owner;
            if (dialog.ShowDialog() == true)
            {
                SetStatus("Конфигурация Port Defender обновлена.");
                _ = LoadDefenderAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть настройку Port Defender: {ex.Message}");
        }
    }

    private async void OnRefreshDefenderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadDefenderAsync();
            SetStatus("Сводка Port Defender обновлена.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка обновления Port Defender: {ex.Message}");
        }
    }

    // ===== Баны =====

    private async void OnRefreshBansClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadBansAsync(silent: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка обновления банов: {ex.Message}");
        }
    }

    private async void OnUnblockIpClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var row = GridBans.SelectedItem as BanRow;
            if (row == null)
            {
                SetStatus("Выберите IP для разблокировки.");
                return;
            }

            SetStatus($"Разблокировка {row.Ip}…");
            var result = await _pipeClient.UnblockIpAsync(row.Ip, FirewallDirection.Both);
            if (result != null && result.Success)
            {
                SetStatus(result.Message);
                await LoadBansAsync(silent: true);
            }
            else
            {
                SetStatus(result?.Message ?? $"Не удалось разблокировать {row.Ip}.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка разблокировки IP: {ex.Message}");
        }
    }

    private async void OnClearBansClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_banRows.Count == 0)
            {
                SetStatus("Активных банов нет — сбрасывать нечего.");
                return;
            }

            var confirm = MessageBox.Show(Window.GetWindow(this),
                LocalizationService.Instance.GetNetClearBansConfirm(_banRows.Count),
                LocalizationService.Instance.NetClearBansConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            SetStatus("Сброс всех банов…");
            var ok = await _pipeClient.ClearAllBansAsync();
            SetStatus(ok ? "Все баны сняты." : "Служба не подтвердила сброс банов.");
            await LoadBansAsync(silent: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка сброса банов: {ex.Message}");
        }
    }

    // ===== Активные соединения (Live) =====

    /// <summary>
    /// Загружает живые соединения через GetProcessNetworkConnectionsAsync(-1),
    /// обогащает строки GeoIP (внутри LiveConnectionRow) и проставляет
    /// бейдж брандмауэра по списку заблокированных IP (баны + кэш правил).
    /// Zero-Crash: любые ошибки уходят в статусную строку.
    /// </summary>
    private async Task LoadLiveConnectionsAsync()
    {
        if (_isLoadingConn)
            return;
        _isLoadingConn = true;
        try
        {
            var sockets = await _pipeClient.GetProcessNetworkConnectionsAsync(-1)
                ?? new List<ProcessNetworkSocket>();

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var bans = await _pipeClient.GetActiveBansAsync() ?? new List<ActiveBanDto>();
                foreach (var ban in bans)
                {
                    var ip = (ban.Ip ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(ip))
                        blocked.Add(ip);
                }
            }
            catch
            {
                // Zero-Crash: баны недоступны — сверяемся только с кэшем правил.
            }

            // Локальный кэш правил: все RemoteAddresses блокирующих правил.
            try
            {
                foreach (var rule in _allRules)
                {
                    if (rule.Action != FirewallAction.Block)
                        continue;
                    var raw = rule.RemoteAddresses ?? string.Empty;
                    foreach (var token in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var addr = token.Trim();
                        if (string.IsNullOrWhiteSpace(addr)
                            || addr == "*"
                            || addr.Equals("Any", StringComparison.OrdinalIgnoreCase))
                            continue;
                        blocked.Add(addr);
                    }
                }
            }
            catch { }
            _blockedIps = blocked;

            var rows = new List<LiveConnectionRow>(sockets.Count);
            foreach (var socket in sockets)
            {
                if (socket == null)
                    continue;
                try
                {
                    var remote = (socket.RemoteAddress ?? string.Empty).Trim();
                    var isBlocked = !string.IsNullOrWhiteSpace(remote)
                        && (blocked.Contains(remote) || IsIpCoveredByBlockRules(remote));
                    // GeoIP-обогащение выполняется внутри конструктора
                    // LiveConnectionRow через GeoIpLookupService.Resolve(...).
                    rows.Add(new LiveConnectionRow(
                        socket,
                        isBlocked ? "🚫 Заблокирован" : "🟢 Разрешено"));
                }
                catch
                {
                    // Zero-Crash: битый сокет пропускаем.
                }
            }

            _allConnections = rows
                .OrderBy(r => r.ProcessName)
                .ThenBy(r => r.RemoteAddress)
                .ToList();
            ApplyConnFilter();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось загрузить соединения: {ex.Message}");
        }
        finally
        {
            _isLoadingConn = false;
        }
    }

    /// <summary>
    /// Проверяет, покрыт ли IP блокирующим правилом из локального кэша
    /// (точное вхождение или подсеть/токен содержит IP).
    /// </summary>
    private bool IsIpCoveredByBlockRules(string ip)
    {
        try
        {
            foreach (var rule in _allRules)
            {
                if (rule.Action != FirewallAction.Block)
                    continue;
                var raw = rule.RemoteAddresses ?? string.Empty;
                if (raw.IndexOf(ip, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Применяет поиск и фильтр WAN/LAN/ESTABLISHED/LISTEN к таблице соединений.</summary>
    private void ApplyConnFilter()
    {
        try
        {
            var query = (_connSearchText ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = _allConnections.Where(r => MatchesConnFilter(r, query)).ToList();

            GridLiveConnections.ItemsSource = filtered;
            PanelConnEmpty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var wan = _allConnections.Count(c => !c.IsPrivate);
            var procs = _allConnections.Select(c => c.ProcessId).Distinct().Count();
            TxtConnectionsSummary.Text =
                $"Соединений: {_allConnections.Count} • WAN: {wan} • Процессов: {procs}";
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка фильтрации соединений: {ex.Message}");
        }
    }

    /// <summary>
    /// Предикат фильтрации соединений: тип сети/состояния + поиск по
    /// ProcessName, ProcessId, RemoteAddress, LocalEndPoint, RemoteEndPoint.
    /// </summary>
    private bool MatchesConnFilter(LiveConnectionRow row, string queryLower)
    {
        switch (_activeConnFilter)
        {
            case ConnFilter.Wan:
                if (row.IsPrivate)
                    return false;
                break;
            case ConnFilter.Lan:
                if (!row.IsPrivate)
                    return false;
                break;
            case ConnFilter.Established:
                if (!string.Equals((row.State ?? string.Empty).Trim(), "ESTABLISHED", StringComparison.OrdinalIgnoreCase))
                    return false;
                break;
            case ConnFilter.Listening:
                var state = (row.State ?? string.Empty).Trim().ToUpperInvariant();
                if (state != "LISTENING" && state != "LISTEN" && state != "BOUND")
                    return false;
                break;
        }

        if (string.IsNullOrEmpty(queryLower))
            return true;

        return (row.ProcessName ?? string.Empty).ToLowerInvariant().Contains(queryLower)
            || row.ProcessId.ToString().Contains(queryLower, StringComparison.OrdinalIgnoreCase)
            || (row.RemoteAddress ?? string.Empty).ToLowerInvariant().Contains(queryLower)
            || (row.LocalEndPoint ?? string.Empty).ToLowerInvariant().Contains(queryLower)
            || (row.RemoteEndPoint ?? string.Empty).ToLowerInvariant().Contains(queryLower);
    }

    private void OnConnectionsSearchChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            _connSearchText = TxtSearchConnections.Text ?? string.Empty;
            if (GridLiveConnections != null)
                ApplyConnFilter();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка поиска соединений: {ex.Message}");
        }
    }

    private void OnConnectionsFilterChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ReferenceEquals(sender, RbConnWan))
                _activeConnFilter = ConnFilter.Wan;
            else if (ReferenceEquals(sender, RbConnLan))
                _activeConnFilter = ConnFilter.Lan;
            else if (ReferenceEquals(sender, RbConnEstablished))
                _activeConnFilter = ConnFilter.Established;
            else if (ReferenceEquals(sender, RbConnListening))
                _activeConnFilter = ConnFilter.Listening;
            else
                _activeConnFilter = ConnFilter.All;

            if (GridLiveConnections != null)
                ApplyConnFilter();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка переключения фильтра соединений: {ex.Message}");
        }
    }

    private async void OnRefreshConnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Обновление соединений…");
            await LoadLiveConnectionsAsync();
            SetStatus($"Загружено соединений: {_allConnections.Count}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка обновления соединений: {ex.Message}");
        }
    }

    private void OnAutoRefreshConnChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_liveConnTimer == null || ChkAutoRefreshConn == null)
                return;
            if (ChkAutoRefreshConn.IsChecked == true)
                _liveConnTimer.Start();
            else
                _liveConnTimer.Stop();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка автообновления соединений: {ex.Message}");
        }
    }

    /// <summary>Возвращает соединение, выбранное в таблице Live.</summary>
    private LiveConnectionRow? GetSelectedConnection() =>
        GridLiveConnections?.SelectedItem as LiveConnectionRow;

    /// <summary>Проверяет, что у строки есть реальный удалённый IP для действий.</summary>
    private static bool HasUsableRemoteIp(LiveConnectionRow row) =>
        !string.IsNullOrWhiteSpace(row.RemoteAddress)
        && row.RemoteAddress != "*"
        && row.RemoteAddress != "0.0.0.0"
        && row.RemoteAddress != "::";

    private async void OnCtxBlockRemoteIpClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = GetSelectedConnection();
            if (selected == null)
            {
                SetStatus("Выберите соединение для блокировки IP.");
                return;
            }
            if (!HasUsableRemoteIp(selected) || !selected.CanBlockIp)
            {
                SetStatus("У выбранного соединения нет публичного удалённого IP (LAN/слушающий сокет).");
                return;
            }

            var ip = selected.RemoteAddress.Trim();
            if (!FirewallGuard.IsBlockableAddress(ip, out var guardError))
            {
                SetStatus($"Блокировка запрещена: {guardError}");
                return;
            }

            SetStatus($"Блокировка {ip}…");
            var rule = new FirewallRuleDto
            {
                Name = $"StartupMonitor_Block_{ip}",
                Description = $"Ручная блокировка IP {ip} ({selected.ProcessName}, PID {selected.ProcessId}). Создано из вкладки «Активные соединения».",
                Direction = FirewallDirection.Both,
                Action = FirewallAction.Block,
                Protocol = FirewallProtocol.Any,
                LocalPorts = string.Empty,
                RemoteAddresses = ip,
                Enabled = true,
                Profiles = FirewallProfile.All,
                Grouping = "StartupMonitor",
                IsManaged = true
            };

            var result = await _pipeClient.CreateOrUpdateFirewallRuleAsync(rule);
            if (result != null && result.Success)
            {
                SetStatus(result.Message);
                await LoadRulesAsync();
                await LoadLiveConnectionsAsync();
            }
            else
            {
                SetStatus(result?.Message ?? $"Не удалось заблокировать {ip}.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка блокировки IP: {ex.Message}");
        }
    }

    private async void OnCtxCreateRuleForProcessClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = GetSelectedConnection();
            if (selected == null)
            {
                SetStatus("Выберите соединение для создания правила.");
                return;
            }

            var proto = (selected.Protocol ?? string.Empty).IndexOf("UDP", StringComparison.OrdinalIgnoreCase) >= 0
                ? FirewallProtocol.Udp
                : FirewallProtocol.Tcp;

            var dto = new FirewallRuleDto
            {
                Name = $"StartupMonitor_{selected.ProcessName}_{selected.RemoteAddress}",
                Description = $"Правило для {selected.ProcessName} (PID {selected.ProcessId}), удалённый адрес {selected.RemoteEndPoint}. Создано из вкладки «Активные соединения».",
                Direction = FirewallDirection.Outbound,
                Action = FirewallAction.Block,
                Protocol = proto,
                LocalPorts = string.Empty,
                RemoteAddresses = selected.CanBlockIp ? selected.RemoteAddress : string.Empty,
                ApplicationPath = null,
                Enabled = true,
                Profiles = FirewallProfile.All,
                Grouping = "StartupMonitor",
                IsManaged = true
            };

            var owner = Window.GetWindow(this);
            var dialog = new FirewallRuleEditWindow(_pipeClient, dto);
            if (owner != null)
                dialog.Owner = owner;
            if (dialog.ShowDialog() == true)
                await LoadRulesAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть редактор правил: {ex.Message}");
        }
    }

    private void OnCtxCopyRemoteIpClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = GetSelectedConnection();
            if (selected == null || string.IsNullOrWhiteSpace(selected.RemoteAddress))
            {
                SetStatus("Выберите соединение для копирования IP.");
                return;
            }
            Clipboard.SetText(selected.RemoteAddress);
            SetStatus($"IP {selected.RemoteAddress} скопирован в буфер обмена.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка копирования IP: {ex.Message}");
        }
    }

    private void OnCtxCopyEndpointClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = GetSelectedConnection();
            if (selected == null || string.IsNullOrWhiteSpace(selected.RemoteEndPoint))
            {
                SetStatus("Выберите соединение для копирования адреса.");
                return;
            }
            Clipboard.SetText(selected.RemoteEndPoint);
            SetStatus($"Адрес {selected.RemoteEndPoint} скопирован в буфер обмена.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка копирования адреса: {ex.Message}");
        }
    }

    private void OnCtxWhoisClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = GetSelectedConnection();
            if (selected == null || !HasUsableRemoteIp(selected))
            {
                SetStatus("Выберите соединение с удалённым IP для Whois.");
                return;
            }
            var url = $"https://ipinfo.io/{selected.RemoteAddress.Trim()}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть Whois: {ex.Message}");
        }
    }

    private void OnCtxVirusTotalClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = GetSelectedConnection();
            if (selected == null || !HasUsableRemoteIp(selected))
            {
                SetStatus("Выберите соединение с удалённым IP для VirusTotal.");
                return;
            }
            var url = $"https://www.virustotal.com/gui/ip-address/{selected.RemoteAddress.Trim()}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть VirusTotal: {ex.Message}");
        }
    }

    private async void OnCtxKillProcessClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = GetSelectedConnection();
            if (selected == null)
            {
                SetStatus("Выберите соединение для завершения процесса.");
                return;
            }

            var confirm = MessageBox.Show(Window.GetWindow(this),
                LocalizationService.Instance.GetNetKillProcessConfirm(selected.ProcessName, selected.ProcessId),
                LocalizationService.Instance.NetKillProcessConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            SetStatus($"Завершение процесса {selected.ProcessName} (PID {selected.ProcessId})…");
            var result = await _pipeClient.KillProcessAsync(selected.ProcessId, selected.ProcessName);
            if (result?.Success == true)
            {
                SetStatus(string.IsNullOrWhiteSpace(result.Message)
                    ? $"Процесс {selected.ProcessName} (PID {selected.ProcessId}) завершён."
                    : result.Message);
                await LoadLiveConnectionsAsync();
            }
            else
            {
                SetStatus(result?.Message ?? $"Не удалось завершить процесс {selected.ProcessId}.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка завершения процесса: {ex.Message}");
        }
    }

    // ===== Экспорт / импорт бэкапов и пресеты безопасности =====

    /// <summary>Псевдоним мгновенного обновления таблицы правил (требование Worker 3).</summary>
    public Task LoadAllRulesAsync() => LoadRulesAsync();

    /// <summary>Имя файла бэкапа по умолчанию: FirewallRules_Backup_yyyyMMdd.json.</summary>
    public static string BuildDefaultBackupFileName(DateTime now) =>
        $"FirewallRules_Backup_{now:yyyyMMdd}.json";

    /// <summary>Сериализует бэкап в JSON с отступами (формат файла экспорта).</summary>
    public static string SerializeBackupToJson(FirewallBackupDto backup)
    {
        if (backup == null)
            throw new ArgumentNullException(nameof(backup));
        return JsonSerializer.Serialize(backup, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        });
    }

    /// <summary>Десериализует бэкап из JSON файла импорта.</summary>
    public static FirewallBackupDto? DeserializeBackupFromJson(string json) =>
        JsonSerializer.Deserialize<FirewallBackupDto>(json ?? throw new ArgumentNullException(nameof(json)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    /// <summary>Строит Pipe-запрос импорта (единая точка для UI и тестов).</summary>
    public static ImportFirewallRulesRequest BuildImportRequest(FirewallBackupDto backup, bool overwrite) =>
        new(backup ?? throw new ArgumentNullException(nameof(backup)), overwrite);

    /// <summary>Строит Pipe-запрос применения пресета (единая точка для UI и тестов).</summary>
    public static ApplySecurityPresetRequest BuildPresetRequest(SecurityPresetType presetType) =>
        new(presetType);

    /// <summary>Человекочитаемое имя пресета безопасности.</summary>
    public static string GetSecurityPresetDisplayName(SecurityPresetType presetType) => presetType switch
    {
        SecurityPresetType.MaxPrivacy => "Максимальная приватность",
        SecurityPresetType.Gaming => "Игровой режим",
        SecurityPresetType.PublicWifi => "Публичный Wi-Fi",
        SecurityPresetType.ResetToDefaults => "Сброс к стандартным параметрам",
        _ => presetType.ToString()
    };

    /// <summary>
    /// Экспорт правил: диалог сохранения → ExportFirewallRulesAsync → JSON с отступами → файл.
    /// </summary>
    private async void OnExportRulesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = BuildDefaultBackupFileName(DateTime.Now),
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            SetStatus("Экспорт правил брандмауэра…");
            var backup = await _pipeClient.ExportFirewallRulesAsync();
            if (backup == null)
            {
                SetStatus("Не удалось экспортировать правила: служба недоступна.");
                return;
            }

            var json = SerializeBackupToJson(backup);
            await File.WriteAllTextAsync(dialog.FileName, json);
            var count = backup.Rules?.Count ?? 0;
            SetStatus(LocalizationService.Instance.GetNetStatusExported(count, dialog.FileName));
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка экспорта правил: {ex.Message}");
        }
    }

    /// <summary>
    /// Импорт правил: диалог открытия → чтение → десериализация → подтверждение →
    /// ImportFirewallRulesAsync(overwrite: true) → мгновенное обновление таблицы.
    /// </summary>
    private async void OnImportRulesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            string json;
            try
            {
                json = await File.ReadAllTextAsync(dialog.FileName);
            }
            catch (Exception ex)
            {
                SetStatus($"Не удалось прочитать файл: {ex.Message}");
                return;
            }

            FirewallBackupDto? backup;
            try
            {
                backup = DeserializeBackupFromJson(json);
            }
            catch (Exception ex)
            {
                SetStatus($"Некорректный файл бэкапа: {ex.Message}");
                return;
            }

            if (backup?.Rules == null || backup.Rules.Count == 0)
            {
                SetStatus("В файле нет правил для импорта.");
                return;
            }

            var confirm = MessageBox.Show(Window.GetWindow(this),
                LocalizationService.Instance.GetNetImportConfirm(backup.Rules.Count),
                LocalizationService.Instance.NetImportConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            SetStatus("Импорт правил брандмауэра…");
            var (imported, skipped) = await _pipeClient.ImportFirewallRulesAsync(backup, overwrite: true);
            await LoadAllRulesAsync();
            SetStatus(LocalizationService.Instance.GetNetImportCompleted(imported, skipped));
            MessageBox.Show(Window.GetWindow(this),
                LocalizationService.Instance.GetNetImportCompleted(imported, skipped),
                LocalizationService.Instance.NetImportCompletedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка импорта правил: {ex.Message}");
        }
    }

    /// <summary>Общий исполнитель пресетов: вызов службы → обновление таблицы → уведомление.</summary>
    private async Task ApplyPresetAsync(SecurityPresetType presetType)
    {
        try
        {
            var displayName = GetSecurityPresetDisplayName(presetType);
            SetStatus($"Применение пресета «{displayName}»…");
            var applied = await _pipeClient.ApplySecurityPresetAsync(presetType);
            await LoadAllRulesAsync();
            SetStatus(LocalizationService.Instance.GetNetStatusPresetApplied(displayName, applied));
            MessageBox.Show(Window.GetWindow(this),
                LocalizationService.Instance.GetNetStatusPresetApplied(displayName, applied),
                LocalizationService.Instance.NetPresetAppliedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка применения пресета: {ex.Message}");
        }
    }

    private async void OnApplyPresetMaxPrivacyClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(SecurityPresetType.MaxPrivacy);

    private async void OnApplyPresetGamingClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(SecurityPresetType.Gaming);

    private async void OnApplyPresetPublicWifiClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(SecurityPresetType.PublicWifi);

    private async void OnApplyPresetResetClick(object sender, RoutedEventArgs e) =>
        await ApplyPresetAsync(SecurityPresetType.ResetToDefaults);

    // ===== Статусная строка =====

    /// <summary>Показывает сообщение в статусной строке (Zero-Crash: никогда не бросает).</summary>
    private void SetStatus(string message)
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(SetStatus), message);
                return;
            }
            TxtStatus.Text = message ?? string.Empty;
        }
        catch { }
    }

    /// <summary>Динамически выделяет активную радиокнопку фильтра правил брандмауэра.</summary>
    private void UpdateRuleFilterVisuals()
    {
        try
        {
            if (FilterAll == null) return;
            FilterAll.FontWeight = FilterAll.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            FilterInbound.FontWeight = FilterInbound.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            FilterOutbound.FontWeight = FilterOutbound.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            FilterBlocking.FontWeight = FilterBlocking.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            FilterManaged.FontWeight = FilterManaged.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        }
        catch { }
    }

    /// <summary>Динамически выделяет активную радиокнопку фильтра активных соединений.</summary>
    private void UpdateConnectionsFilterVisuals()
    {
        try
        {
            if (RbConnAll == null) return;
            RbConnAll.FontWeight = RbConnAll.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            RbConnWan.FontWeight = RbConnWan.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            RbConnLan.FontWeight = RbConnLan.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            RbConnEstablished.FontWeight = RbConnEstablished.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            RbConnListening.FontWeight = RbConnListening.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        }
        catch { }
    }
}