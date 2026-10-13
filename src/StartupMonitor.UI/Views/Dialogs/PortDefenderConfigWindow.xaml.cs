using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Окно настройки службы защиты портов Port Defender.
/// </summary>
public partial class PortDefenderConfigWindow
{
    private readonly PipeClient _pipeClient;
    private readonly int? _initialPort;
    private PortDefenderConfigDto? _config;
    private bool _isLoading;

    /// <summary>PortDefenderConfigWindow: Выполняет операцию PortDefenderConfigWindow.</summary>
    /// <param name="pipeClient">Входные данные: значение параметра pipeClient (PipeClient).</param>
    /// <param name="initialPort">Входные данные: значение параметра initialPort (int?).</param>
    public PortDefenderConfigWindow(PipeClient pipeClient, int? initialPort = null)
    {
        InitializeComponent();
        _pipeClient = pipeClient;
        _initialPort = initialPort;

        Loaded += async (_, _) => await LoadConfigAsync();
    }

    /// <summary>LoadConfigAsync: загружает текущую конфигурацию Port Defender из службы.</summary>
    private async Task LoadConfigAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            TxtStatus.Text = "Загрузка конфигурации...";
            _config = await _pipeClient.GetPortDefenderConfigAsync() ?? new PortDefenderConfigDto();

            // Если окно открыто для конкретного порта — предзаполнить его в списке защищаемых.
            if (_initialPort is >= 1 and <= 65535
                && _config.ProtectedPorts.All(p => p != _initialPort.Value))
            {
                TxtProtectedPorts.Text = string.Join(", ",
                    _config.ProtectedPorts.Append(_initialPort.Value).Distinct().OrderBy(p => p));
            }
            else
            {
                TxtProtectedPorts.Text = string.Join(", ", _config.ProtectedPorts.Distinct().OrderBy(p => p));
            }

            ChkEnabled.IsChecked = _config.Enabled;
            TxtMaxAttempts.Text = _config.MaxAttempts.ToString();
            TxtWindowSeconds.Text = _config.WindowSeconds.ToString();
            TxtBanDuration.Text = _config.BanDurationMinutes.ToString();
            ChkAutoEscalate.IsChecked = _config.AutoEscalate;
            ChkTrustPrivate.IsChecked = _config.TrustPrivateNetworks;
            TxtWhitelist.Text = string.Join(", ", _config.WhitelistIps ?? new List<string>());

            TxtStatus.Text = _initialPort.HasValue
                ? $"Порт {_initialPort.Value} предзаполнен в списке защищаемых."
                : "Конфигурация загружена.";
        }
        catch (Exception ex)
        {
            _config ??= new PortDefenderConfigDto();
            TxtStatus.Text = $"Не удалось загрузить конфигурацию: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>ParsePorts: разбирает список портов через запятую/пробел/точку с запятой.</summary>
    /// <param name="text">Входные данные: значение параметра text (string).</param>
    private static List<int> ParsePorts(string text)
    {
        var result = new List<int>();
        var parts = (text ?? string.Empty).Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out var port) && FirewallGuard.IsValidPort(port) && !result.Contains(port))
                result.Add(port);
        }
        return result;
    }

    /// <summary>ParseWhitelist: разбирает белый список IP через запятую или перенос строки.</summary>
    /// <param name="text">Входные данные: значение параметра text (string).</param>
    private static List<string> ParseWhitelist(string text)
    {
        var result = new List<string>();
        var parts = (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var ip = part.Trim();
            if (!string.IsNullOrEmpty(ip) && !result.Contains(ip, StringComparer.OrdinalIgnoreCase))
                result.Add(ip);
        }
        return result;
    }

    /// <summary>ParsePositiveInt: разбирает положительное целое число с дефолтом.</summary>
    /// <param name="text">Входные данные: значение параметра text (string).</param>
    /// <param name="fallback">Входные данные: значение параметра fallback (int).</param>
    private static int ParsePositiveInt(string text, int fallback) =>
        int.TryParse((text ?? string.Empty).Trim(), out var value) && value >= 0 ? value : fallback;

    /// <summary>OnSaveClick: сохраняет конфигурацию через PipeClient.SetPortDefenderConfigAsync.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtStatus.Text = "Сохранение...";
            var config = _config ?? new PortDefenderConfigDto();

            config.Enabled = ChkEnabled.IsChecked == true;
            config.ProtectedPorts = ParsePorts(TxtProtectedPorts.Text);
            if (config.ProtectedPorts.Count == 0 && _initialPort is >= 1 and <= 65535)
                config.ProtectedPorts.Add(_initialPort.Value);
            config.MaxAttempts = ParsePositiveInt(TxtMaxAttempts.Text, 10);
            if (config.MaxAttempts == 0) config.MaxAttempts = 10;
            config.WindowSeconds = ParsePositiveInt(TxtWindowSeconds.Text, 60);
            if (config.WindowSeconds == 0) config.WindowSeconds = 60;
            config.BanDurationMinutes = ParsePositiveInt(TxtBanDuration.Text, 15);
            config.AutoEscalate = ChkAutoEscalate.IsChecked == true;
            config.TrustPrivateNetworks = ChkTrustPrivate.IsChecked == true;
            config.WhitelistIps = ParseWhitelist(TxtWhitelist.Text);

            var ok = await _pipeClient.SetPortDefenderConfigAsync(config);
            if (ok)
            {
                TxtStatus.Text = "Конфигурация сохранена.";
                DialogResult = true;
                Close();
            }
            else
            {
                TxtStatus.Text = "Служба не подтвердила сохранение конфигурации.";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка сохранения: {ex.Message}";
        }
    }

    /// <summary>OnCancelClick: закрывает окно без сохранения.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
