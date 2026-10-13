using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Окно детального инспектирования активных сетевых соединений (сокетов) процесса.
/// </summary>
public partial class ProcessConnectionsWindow
{
    private readonly ProcessInfo _targetProcess;
    private readonly PipeClient _pipeClient;
    private readonly DispatcherTimer _pollTimer;
    private List<ProcessNetworkSocket> _allSockets = new();
    private bool _isLoading;

    private List<ActiveBanDto> _activeBans = new();
    private HashSet<string> _bannedIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionBlockedIps = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<int> _protectedPorts = new();
    private PortDefenderConfigDto? _defenderConfig;

    /// <summary>
    /// Строка таблицы соединений, обогащённая бейджем брандмауэра.
    /// Наследует все свойства <see cref="ProcessNetworkSocket"/>, поэтому существующие
    /// привязки DataGrid продолжают работать без изменений.
    /// </summary>
    public sealed class ConnectionRow : ProcessNetworkSocket
    {
        /// <summary>Текстовый бейдж статуса брандмауэра для колонки «Брандмауэр».</summary>
        public string FirewallBadge { get; set; } = "🟢 Разрешено";

        /// <summary>ConnectionRow: Выполняет операцию ConnectionRow.</summary>
        public ConnectionRow() { }

        /// <summary>ConnectionRow: Выполняет операцию ConnectionRow.</summary>
        /// <param name="socket">Входные данные: значение параметра socket (ProcessNetworkSocket).</param>
        /// <param name="badge">Входные данные: значение параметра badge (string).</param>
        public ConnectionRow(ProcessNetworkSocket socket, string badge)
        {
            ProcessId = socket.ProcessId;
            ProcessName = socket.ProcessName;
            Protocol = socket.Protocol;
            LocalAddress = socket.LocalAddress;
            LocalPort = socket.LocalPort;
            RemoteAddress = socket.RemoteAddress;
            RemotePort = socket.RemotePort;
            State = socket.State;
            IsRemotePrivate = socket.IsRemotePrivate;
            FirewallBadge = badge;
        }
    }

    public string WindowTitle => $"{_targetProcess.ProcessName} (PID: {_targetProcess.Pid}) — Сетевые соединения";

    /// <summary>ProcessConnectionsWindow: Выполняет операцию ProcessConnectionsWindow.</summary>
    /// <param name="targetProcess">Входные данные: значение параметра targetProcess (ProcessInfo).</param>
    /// <param name="pipeClient">Входные данные: значение параметра pipeClient (PipeClient).</param>
    public ProcessConnectionsWindow(ProcessInfo targetProcess, PipeClient pipeClient)
    {
        InitializeComponent();
        _targetProcess = targetProcess;
        _pipeClient = pipeClient;

        TxtProcessName.Text = _targetProcess.ProcessName;
        TxtProcessPid.Text = $"PID: {_targetProcess.Pid}";
        TxtExecutablePath.Text = !string.IsNullOrEmpty(_targetProcess.FilePath) ? _targetProcess.FilePath : "(путь неизвестен)";

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _pollTimer.Tick += async (_, _) => await LoadConnectionsAsync();

        Loaded += async (_, _) =>
        {
            await LoadConnectionsAsync();
            if (ChkAutoRefresh.IsChecked == true)
            {
                _pollTimer.Start();
            }
        };

        Closed += (_, _) => _pollTimer.Stop();
    }

    /// <summary>LoadConnectionsAsync: Загружает данные ConnectionsAsync.</summary>
    private async Task LoadConnectionsAsync()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Sockets.LoadConnections");
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            TxtStatus.Text = "Обновление сокетов...";
            var sockets = await _pipeClient.GetProcessNetworkConnectionsAsync(_targetProcess.Pid);
            _allSockets = sockets ?? new List<ProcessNetworkSocket>();

            TxtTotalCount.Text = _allSockets.Count.ToString();
            TxtEstablishedCount.Text = _allSockets.Count(s => s.State == "ESTABLISHED").ToString();
            TxtListeningCount.Text = _allSockets.Count(s => s.State == "LISTENING").ToString();

            await RefreshDefenderAsync();
            ApplyFilter();
            TxtStatus.Text = $"Обновлено в {DateTime.Now:HH:mm:ss} • Соединений: {_allSockets.Count}";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка обновления: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>RefreshDefenderAsync: обновляет Shield Card и кэш банов/защищённых портов.</summary>
    private async Task RefreshDefenderAsync()
    {
        try
        {
            var configTask = _pipeClient.GetPortDefenderConfigAsync();
            var bansTask = _pipeClient.GetActiveBansAsync();
            await Task.WhenAll(configTask, bansTask);

            _defenderConfig = configTask.Result;
            _activeBans = bansTask.Result ?? new List<ActiveBanDto>();
            _bannedIps = new HashSet<string>(
                _activeBans.Select(b => b.Ip).Where(ip => !string.IsNullOrWhiteSpace(ip)),
                StringComparer.OrdinalIgnoreCase);

            var ports = new HashSet<int>();
            if (_defenderConfig?.ProtectedPorts != null)
            {
                foreach (var p in _defenderConfig.ProtectedPorts)
                {
                    if (p >= 1 && p <= 65535) ports.Add(p);
                }
            }
            if (_defenderConfig?.Rules != null)
            {
                foreach (var rule in _defenderConfig.Rules)
                {
                    if (rule is { IsEnabled: true, LocalPort: >= 1 and <= 65535 })
                        ports.Add(rule.LocalPort);
                }
            }
            _protectedPorts = ports;

            var enabled = _defenderConfig?.Enabled ?? false;
            TxtDefenderStatus.Text = enabled
                ? "Port Defender: Защита портов активна"
                : "Port Defender: Защита портов выключена";
            TxtDefenderDetails.Text = $"Активных банов: {_activeBans.Count} • Защищённых портов: {_protectedPorts.Count}";
        }
        catch
        {
            TxtDefenderStatus.Text = "Port Defender: статус недоступен";
            TxtDefenderDetails.Text = "Служба не ответила на запрос статуса защиты";
        }
    }

    /// <summary>ComputeFirewallBadge: вычисляет бейдж колонки «Брандмауэр» для сокета.</summary>
    /// <param name="socket">Входные данные: значение параметра socket (ProcessNetworkSocket).</param>
    private string ComputeFirewallBadge(ProcessNetworkSocket socket)
    {
        var remote = socket.RemoteAddress?.Trim() ?? string.Empty;
        var hasRemote = !string.IsNullOrEmpty(remote) && remote != "*" && remote != "0.0.0.0" && remote != "::";

        if (hasRemote && _bannedIps.Contains(remote))
            return "🔴 В бане";
        if (hasRemote && _sessionBlockedIps.Contains(remote))
            return "⛔ Заблокирован";
        if (_protectedPorts.Contains(socket.LocalPort))
            return "🛡️ Защищён Defender";
        return "🟢 Разрешено";
    }

    /// <summary>ApplyFilter: Выполняет операцию ApplyFilter.</summary>
    private void ApplyFilter()
    {
        var filter = TxtSearch.Text.Trim();
        IEnumerable<ProcessNetworkSocket> items = _allSockets;

        if (!string.IsNullOrEmpty(filter))
        {
            items = items.Where(s =>
                s.LocalAddress.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.RemoteAddress.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.LocalPort.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.RemotePort.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.State.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.Protocol.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var list = items.Select(s => new ConnectionRow(s, ComputeFirewallBadge(s))).ToList<object>();
        GridConnections.ItemsSource = list;

        PanelEmpty.Visibility = (_allSockets.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>OnSearchTextChanged: Обрабатывает событие OnSearchTextChanged.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (TextChangedEventArgs).</param>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    /// <summary>OnRefreshClick: Обрабатывает событие OnRefreshClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await LoadConnectionsAsync();
    }

    /// <summary>OnAutoRefreshChanged: Обрабатывает событие OnAutoRefreshChanged.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnAutoRefreshChanged(object sender, RoutedEventArgs e)
    {
        if (_pollTimer == null) return;
        if (ChkAutoRefresh.IsChecked == true)
        {
            _pollTimer.Start();
        }
        else
        {
            _pollTimer.Stop();
        }
    }

    /// <summary>OnCopyRemoteIpClick: Обрабатывает событие OnCopyRemoteIpClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCopyRemoteIpClick(object sender, RoutedEventArgs e)
    {
        if (GridConnections.SelectedItem is ProcessNetworkSocket socket && !string.IsNullOrEmpty(socket.RemoteAddress) && socket.RemoteAddress != "*")
        {
            Clipboard.SetText(socket.RemoteAddress);
            TxtStatus.Text = $"IP-адрес {socket.RemoteAddress} скопирован в буфер обмена";
        }
    }

    /// <summary>OnCopyRemoteEndpointClick: Обрабатывает событие OnCopyRemoteEndpointClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCopyRemoteEndpointClick(object sender, RoutedEventArgs e)
    {
        if (GridConnections.SelectedItem is ProcessNetworkSocket socket)
        {
            Clipboard.SetText(socket.RemoteEndPoint);
            TxtStatus.Text = $"Адрес {socket.RemoteEndPoint} скопирован в буфер обмена";
        }
    }

    /// <summary>OnOpenWhoisClick: Обрабатывает событие OnOpenWhoisClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnOpenWhoisClick(object sender, RoutedEventArgs e)
    {
        if (GridConnections.SelectedItem is ProcessNetworkSocket socket && !string.IsNullOrEmpty(socket.RemoteAddress) && socket.RemoteAddress != "*")
        {
            var url = $"https://ipinfo.io/{socket.RemoteAddress}";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>OnOpenVirusTotalClick: Обрабатывает событие OnOpenVirusTotalClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnOpenVirusTotalClick(object sender, RoutedEventArgs e)
    {
        if (GridConnections.SelectedItem is ProcessNetworkSocket socket && !string.IsNullOrEmpty(socket.RemoteAddress) && socket.RemoteAddress != "*")
        {
            var url = $"https://www.virustotal.com/gui/ip-address/{socket.RemoteAddress}";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>GetSelectedSocket: возвращает сокет, выбранный в таблице.</summary>
    private ProcessNetworkSocket? GetSelectedSocket() =>
        GridConnections.SelectedItem as ProcessNetworkSocket;

    /// <summary>HasUsableRemoteIp: проверяет, что у сокета есть реальный удалённый IP.</summary>
    /// <param name="socket">Входные данные: значение параметра socket (ProcessNetworkSocket).</param>
    private static bool HasUsableRemoteIp(ProcessNetworkSocket socket) =>
        !string.IsNullOrWhiteSpace(socket.RemoteAddress)
        && socket.RemoteAddress != "*"
        && socket.RemoteAddress != "0.0.0.0"
        && socket.RemoteAddress != "::";

    /// <summary>OnConfigureDefenderClick: открывает окно настройки Port Defender.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnConfigureDefenderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new PortDefenderConfigWindow(_pipeClient) { Owner = this };
            dlg.ShowDialog();
            await RefreshDefenderAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Не удалось открыть настройки Defender: {ex.Message}";
        }
    }

    /// <summary>OnClearBansClick: сбрасывает все активные баны через PipeClient.ClearAllBansAsync.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnClearBansClick(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtStatus.Text = "Сброс активных банов...";
            var ok = await _pipeClient.ClearAllBansAsync();
            if (ok)
            {
                _sessionBlockedIps.Clear();
                await RefreshDefenderAsync();
                ApplyFilter();
                TxtStatus.Text = "Все активные баны сброшены";
            }
            else
            {
                TxtStatus.Text = "Служба не подтвердила сброс банов";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка сброса банов: {ex.Message}";
        }
    }

    /// <summary>OnBlockIp15mClick: блокирует удалённый IP на 15 минут.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnBlockIp15mClick(object sender, RoutedEventArgs e) =>
        await BlockSelectedRemoteIpAsync(15);

    /// <summary>OnBlockIp1hClick: блокирует удалённый IP на 1 час.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnBlockIp1hClick(object sender, RoutedEventArgs e) =>
        await BlockSelectedRemoteIpAsync(60);

    /// <summary>OnBlockIp24hClick: блокирует удалённый IP на 24 часа.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnBlockIp24hClick(object sender, RoutedEventArgs e) =>
        await BlockSelectedRemoteIpAsync(1440);

    /// <summary>OnBlockIpForeverClick: блокирует удалённый IP навсегда.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnBlockIpForeverClick(object sender, RoutedEventArgs e) =>
        await BlockSelectedRemoteIpAsync(null);

    /// <summary>BlockSelectedRemoteIpAsync: блокирует IP выбранного соединения через PipeClient.BlockIpAsync.</summary>
    /// <param name="banDurationMinutes">Длительность бана в минутах (null = навсегда).</param>
    private async Task BlockSelectedRemoteIpAsync(int? banDurationMinutes)
    {
        var socket = GetSelectedSocket();
        if (socket == null)
        {
            TxtStatus.Text = "Выберите соединение в таблице";
            return;
        }
        if (!HasUsableRemoteIp(socket))
        {
            TxtStatus.Text = "У выбранного соединения нет удалённого IP (слушающий сокет)";
            return;
        }

        var ip = socket.RemoteAddress.Trim();
        if (!FirewallGuard.IsBlockableAddress(ip, out var guardError))
        {
            TxtStatus.Text = $"Блокировка запрещена: {guardError}";
            return;
        }

        try
        {
            TxtStatus.Text = $"Блокировка {ip}...";
            var result = await _pipeClient.BlockIpAsync(new BlockIpRequest
            {
                Ip = ip,
                Direction = FirewallDirection.Both,
                Aggregated = true,
                Reason = $"Ручная блокировка из окна соединений: {_targetProcess.ProcessName} (PID {_targetProcess.Pid})",
                BanDurationMinutes = banDurationMinutes
            });

            if (result?.Success == true)
            {
                _sessionBlockedIps.Add(ip);
                await RefreshDefenderAsync();
                ApplyFilter();
                var dur = banDurationMinutes.HasValue ? $" на {banDurationMinutes} мин" : " навсегда";
                TxtStatus.Text = $"IP {ip} заблокирован{dur}: {result.Message}";
            }
            else
            {
                TxtStatus.Text = $"Не удалось заблокировать {ip}: {result?.Message ?? "нет ответа службы"}";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка блокировки {ip}: {ex.Message}";
        }
    }

    /// <summary>OnUnblockIpClick: разблокирует IP через PipeClient.UnblockIpAsync.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnUnblockIpClick(object sender, RoutedEventArgs e)
    {
        var socket = GetSelectedSocket();
        if (socket == null)
        {
            TxtStatus.Text = "Выберите соединение в таблице";
            return;
        }
        if (!HasUsableRemoteIp(socket))
        {
            TxtStatus.Text = "У выбранного соединения нет удалённого IP (слушающий сокет)";
            return;
        }

        var ip = socket.RemoteAddress.Trim();
        try
        {
            TxtStatus.Text = $"Разблокировка {ip}...";
            var result = await _pipeClient.UnblockIpAsync(ip, FirewallDirection.Both);
            if (result?.Success == true)
            {
                _sessionBlockedIps.Remove(ip);
                await RefreshDefenderAsync();
                ApplyFilter();
                TxtStatus.Text = $"IP {ip} разблокирован: {result.Message}";
            }
            else
            {
                TxtStatus.Text = $"Не удалось разблокировать {ip}: {result?.Message ?? "нет ответа службы"}";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка разблокировки {ip}: {ex.Message}";
        }
    }

    /// <summary>OnBlockPortClick: блокирует локальный порт через PipeClient.BlockPortAsync.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnBlockPortClick(object sender, RoutedEventArgs e)
    {
        var socket = GetSelectedSocket();
        if (socket == null)
        {
            TxtStatus.Text = "Выберите соединение в таблице";
            return;
        }
        if (!FirewallGuard.IsValidPort(socket.LocalPort))
        {
            TxtStatus.Text = $"Некорректный локальный порт: {socket.LocalPort}";
            return;
        }

        var protocol = socket.Protocol.Contains("UDP", StringComparison.OrdinalIgnoreCase)
            ? FirewallProtocol.Udp
            : FirewallProtocol.Tcp;

        try
        {
            TxtStatus.Text = $"Блокировка порта {socket.LocalPort}...";
            var result = await _pipeClient.BlockPortAsync(new BlockPortRequest
            {
                Port = socket.LocalPort,
                Protocol = protocol,
                Direction = FirewallDirection.Inbound,
                ApplicationPath = string.IsNullOrWhiteSpace(_targetProcess.FilePath) ? null : _targetProcess.FilePath,
                Reason = $"Ручная блокировка порта из окна соединений: {_targetProcess.ProcessName} (PID {_targetProcess.Pid})"
            });

            if (result?.Success == true)
            {
                await RefreshDefenderAsync();
                ApplyFilter();
                TxtStatus.Text = $"Порт {socket.LocalPort} заблокирован: {result.Message}";
            }
            else
            {
                TxtStatus.Text = $"Не удалось заблокировать порт {socket.LocalPort}: {result?.Message ?? "нет ответа службы"}";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка блокировки порта {socket.LocalPort}: {ex.Message}";
        }
    }

    /// <summary>OnConfigureDefenderForPortClick: открывает настройки Defender для локального порта строки.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnConfigureDefenderForPortClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var socket = GetSelectedSocket();
            var dlg = new PortDefenderConfigWindow(_pipeClient, socket?.LocalPort) { Owner = this };
            dlg.ShowDialog();
            await RefreshDefenderAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Не удалось открыть настройки Defender: {ex.Message}";
        }
    }

    /// <summary>OnCloseClick: Обрабатывает событие OnCloseClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

