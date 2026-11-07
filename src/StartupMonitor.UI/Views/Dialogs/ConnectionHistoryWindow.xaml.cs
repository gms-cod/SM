using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Окно просмотра и фильтрации полной истории сетевых подключений программ.
/// Позволяет мгновенно создавать правила Windows Firewall, управлять белыми/чёрными списками
/// и анализировать активность процессов во внешней сети.
/// </summary>
public partial class ConnectionHistoryWindow
{
    private readonly PipeClient _pipeClient;
    private List<NetworkConnectionHistoryItem> _allItems = new();

    /// <summary>
    /// Инициализирует окно истории сетевых подключений.
    /// </summary>
    /// <param name="pipeClient">Клиент IPC для взаимодействия со службой.</param>
    public ConnectionHistoryWindow(PipeClient pipeClient)
    {
        InitializeComponent();
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));
        Loaded += async (_, _) => await LoadHistoryAsync();
    }

    /// <summary>
    /// Загружает историю сетевых подключений из службы.
    /// </summary>
    private async Task LoadHistoryAsync()
    {
        try
        {
            TxtSummary.Text = "Загрузка истории соединений из службы...";
            var items = await _pipeClient.GetConnectionHistoryAsync();
            _allItems = items ?? new List<NetworkConnectionHistoryItem>();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            TxtSummary.Text = $"Ошибка загрузки истории: {ex.Message}";
        }
    }

    /// <summary>
    /// Применяет выбранные фильтры поиска, вердикта, периода и протокола.
    /// </summary>
    private void ApplyFilter()
    {
        try
        {
            IEnumerable<NetworkConnectionHistoryItem> query = _allItems;

            // 1. Текстовый поиск
            var search = TxtSearch?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(item =>
                    (item.ProcessName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.ProcessPath?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (item.RemoteAddress?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    item.RemotePort.ToString().Contains(search) ||
                    item.ProcessId.ToString().Contains(search));
            }

            // 2. Фильтр по вердикту
            var selectedVerdict = (CmbVerdict?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedVerdict) && !selectedVerdict.StartsWith("Все"))
            {
                query = selectedVerdict switch
                {
                    "Ожидает алерта" => query.Where(i => i.Verdict == NetworkConnectionVerdict.PendingAlert),
                    "Разрешено" => query.Where(i => i.Verdict == NetworkConnectionVerdict.Allowed || i.Verdict == NetworkConnectionVerdict.AllowSession),
                    "Заблокировано" => query.Where(i => i.Verdict == NetworkConnectionVerdict.Blocked || i.Verdict == NetworkConnectionVerdict.BlockSession),
                    "Белый список" => query.Where(i => i.Verdict == NetworkConnectionVerdict.Whitelisted),
                    "Чёрный список" => query.Where(i => i.Verdict == NetworkConnectionVerdict.Blacklisted),
                    "Создано правило" => query.Where(i => i.Verdict == NetworkConnectionVerdict.RuleCreated),
                    _ => query
                };
            }

            // 3. Фильтр по периоду времени
            var selectedPeriod = (CmbPeriod?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedPeriod) && !selectedPeriod.StartsWith("Все"))
            {
                var now = DateTime.UtcNow;
                query = selectedPeriod switch
                {
                    "Последний час" => query.Where(i => (now - i.Timestamp).TotalHours <= 1),
                    "Сегодня" => query.Where(i => i.LocalTimestamp.Date == DateTime.Now.Date),
                    "Последние 24 ч" => query.Where(i => (now - i.Timestamp).TotalHours <= 24),
                    _ => query
                };
            }

            // 4. Фильтр по протоколу
            var selectedProtocol = (CmbProtocol?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedProtocol) && !selectedProtocol.StartsWith("Все"))
            {
                query = query.Where(i => string.Equals(i.Protocol, selectedProtocol, StringComparison.OrdinalIgnoreCase));
            }

            var filtered = query.ToList();
            GridHistory.ItemsSource = filtered;
            TxtSummary.Text = $"Отображено записей: {filtered.Count} из {_allItems.Count}";
            UpdateButtonsState();
        }
        catch { }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadHistoryAsync();

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Вы действительно хотите полностью очистить журнал истории сетевых подключений?",
            "Подтверждение очистки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var success = await _pipeClient.ClearConnectionHistoryAsync();
            if (success)
            {
                _allItems.Clear();
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка очистки истории: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtonsState();

    private void UpdateButtonsState()
    {
        var item = GridHistory.SelectedItem as NetworkConnectionHistoryItem;
        var hasSelection = item != null;
        var hasPath = hasSelection && !string.IsNullOrWhiteSpace(item?.ProcessPath);

        BtnAllowRule.IsEnabled = hasSelection;
        BtnBlockRule.IsEnabled = hasSelection;
        BtnWhitelist.IsEnabled = hasSelection;
        BtnBlacklist.IsEnabled = hasSelection;
        BtnOpenFile.IsEnabled = hasPath;
        BtnCopyIp.IsEnabled = hasSelection;
    }

    private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GridHistory.SelectedItem is NetworkConnectionHistoryItem)
        {
            OnOpenLocationClick(sender, e);
        }
    }

    private async void OnCreateAllowRuleClick(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not NetworkConnectionHistoryItem item) return;

        try
        {
            var rule = new FirewallRuleDto
            {
                Name = $"Allow: {item.ProcessName}",
                ApplicationName = item.ProcessPath,
                Direction = FirewallDirection.Outbound,
                Action = FirewallAction.Allow,
                Protocol = string.Equals(item.Protocol, "UDP", StringComparison.OrdinalIgnoreCase) ? FirewallProtocol.Udp : FirewallProtocol.Tcp,
                RemotePorts = item.RemotePort > 0 ? item.RemotePort.ToString() : "*",
                Enabled = true
            };

            var res = await _pipeClient.CreateOrUpdateFirewallRuleAsync(rule);
            if (res?.Success == true)
            {
                MessageBox.Show($"Создано разрешающее правило для программы {item.ProcessName}.", "Брандмауэр", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadHistoryAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка создания правила: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnCreateBlockRuleClick(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not NetworkConnectionHistoryItem item) return;

        try
        {
            var rule = new FirewallRuleDto
            {
                Name = $"Block: {item.ProcessName}",
                ApplicationName = item.ProcessPath,
                Direction = FirewallDirection.Outbound,
                Action = FirewallAction.Block,
                Protocol = string.Equals(item.Protocol, "UDP", StringComparison.OrdinalIgnoreCase) ? FirewallProtocol.Udp : FirewallProtocol.Tcp,
                RemotePorts = item.RemotePort > 0 ? item.RemotePort.ToString() : "*",
                Enabled = true
            };

            var res = await _pipeClient.CreateOrUpdateFirewallRuleAsync(rule);
            if (res?.Success == true)
            {
                MessageBox.Show($"Создано блокирующее правило для программы {item.ProcessName}.", "Брандмауэр", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadHistoryAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка создания правила: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnAddToWhitelistClick(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not NetworkConnectionHistoryItem item) return;
        var app = !string.IsNullOrWhiteSpace(item.ProcessPath) ? item.ProcessPath : item.ProcessName;

        try
        {
            var success = await _pipeClient.AddToFirewallWhitelistAsync(app);
            if (success)
            {
                MessageBox.Show($"Программа «{item.ProcessName}» добавлена в белый список сетевого экрана.", "Белый список", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadHistoryAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка добавления в белый список: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnAddToBlacklistClick(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not NetworkConnectionHistoryItem item) return;
        var app = !string.IsNullOrWhiteSpace(item.ProcessPath) ? item.ProcessPath : item.ProcessName;

        try
        {
            var success = await _pipeClient.AddToFirewallBlacklistAsync(app);
            if (success)
            {
                MessageBox.Show($"Программа «{item.ProcessName}» добавлена в чёрный список сетевого экрана.", "Чёрный список", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadHistoryAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка добавления в чёрный список: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenLocationClick(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not NetworkConnectionHistoryItem item || string.IsNullOrWhiteSpace(item.ProcessPath))
            return;

        try
        {
            if (File.Exists(item.ProcessPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.ProcessPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void OnCopyIpClick(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not NetworkConnectionHistoryItem item) return;
        try
        {
            Clipboard.SetText(item.RemoteAddress);
        }
        catch { }
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (GridHistory.SelectedItem is not NetworkConnectionHistoryItem item || string.IsNullOrWhiteSpace(item.ProcessPath))
            return;
        try
        {
            Clipboard.SetText(item.ProcessPath);
        }
        catch { }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
