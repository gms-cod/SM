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

    public string WindowTitle => $"{_targetProcess.ProcessName} (PID: {_targetProcess.Pid}) — Сетевые соединения";

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

        var list = items.ToList();
        GridConnections.ItemsSource = list;

        PanelEmpty.Visibility = (_allSockets.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await LoadConnectionsAsync();
    }

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

    private void OnCopyRemoteIpClick(object sender, RoutedEventArgs e)
    {
        if (GridConnections.SelectedItem is ProcessNetworkSocket socket && !string.IsNullOrEmpty(socket.RemoteAddress) && socket.RemoteAddress != "*")
        {
            Clipboard.SetText(socket.RemoteAddress);
            TxtStatus.Text = $"IP-адрес {socket.RemoteAddress} скопирован в буфер обмена";
        }
    }

    private void OnCopyRemoteEndpointClick(object sender, RoutedEventArgs e)
    {
        if (GridConnections.SelectedItem is ProcessNetworkSocket socket)
        {
            Clipboard.SetText(socket.RemoteEndPoint);
            TxtStatus.Text = $"Адрес {socket.RemoteEndPoint} скопирован в буфер обмена";
        }
    }

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

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

