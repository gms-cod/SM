using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StartupMonitor.Shared.Models.Firewall;
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

    private List<FirewallRuleRow> _allRules = new();
    private List<BanRow> _banRows = new();
    private PortDefenderConfigDto? _defenderConfig;
    private FirewallFilteringMode _currentMode = FirewallFilteringMode.Normal;

    private string _searchText = string.Empty;
    private RuleFilter _activeFilter = RuleFilter.All;
    private bool _isLoading;
    private bool _suppressModeEvents;

    /// <summary>Фильтр таблицы правил брандмауэра.</summary>
    private enum RuleFilter
    {
        All,
        Inbound,
        Outbound,
        Blocking,
        Managed
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

        Loaded += async (_, _) => await RefreshAllAsync();
        Unloaded += (_, _) =>
        {
            _banTimer.Stop();
            _bansRefreshTimer.Stop();
        };
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
            await LoadRulesAsync();
            await LoadDefenderAsync();
            await LoadBansAsync(silent: true);
            SetStatus($"Обновлено в {DateTime.Now:HH:mm:ss} • Правил: {_allRules.Count} • Банов: {_banRows.Count}");
            if (!_banTimer.IsEnabled)
                _banTimer.Start();
            if (!_bansRefreshTimer.IsEnabled)
                _bansRefreshTimer.Start();
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

    /// <summary>Загружает правила через GetAllFirewallRulesAsync и применяет фильтр.</summary>
    private async Task LoadRulesAsync()
    {
        try
        {
            var rules = await _pipeClient.GetAllFirewallRulesAsync() ?? new List<FirewallRuleDto>();
            _allRules = rules.Select(r => new FirewallRuleRow(r)).OrderBy(r => r.Name).ToList();
            ApplyRuleFilter();
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
                    "Экстренный Killswitch заблокирует ВЕСЬ сетевой трафик.\nПродолжить?",
                    "Подтверждение Killswitch",
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
                $"Удалить правило «{row.Name}» ({row.Direction})?",
                "Подтверждение удаления",
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
                $"Снять все активные баны ({_banRows.Count})?",
                "Сброс всех банов",
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
}
