using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Models;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Окно настройки службы защиты портов Port Defender.
/// Предоставляет полноценное интерактивное управление списком отслеживаемых TCP-портов,
/// порогами срабатывания, длительностью блокировок и белыми списками.
/// </summary>
public partial class PortDefenderConfigWindow
{
    private readonly PipeClient _pipeClient;
    private readonly int? _initialPort;
    private readonly ObservableCollection<PortDefenderPortItem> _portsCollection = new();
    private PortDefenderConfigDto? _config;
    private bool _isLoading;

    /// <summary>
    /// Инициализирует окно настройки службы защиты портов.
    /// </summary>
    /// <param name="pipeClient">Клиент именованного канала для связи с системной службой.</param>
    /// <param name="initialPort">Опциональный целевой порт для предзаполнения в защищаемые.</param>
    public PortDefenderConfigWindow(PipeClient pipeClient, int? initialPort = null)
    {
        InitializeComponent();
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));
        _initialPort = initialPort;

        GridProtectedPorts.ItemsSource = _portsCollection;

        Loaded += async (_, _) => await LoadConfigAsync();
    }

    /// <summary>
    /// Загружает текущую конфигурацию Port Defender из фоновой службы.
    /// </summary>
    private async Task LoadConfigAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            TxtStatus.Text = "Загрузка конфигурации Port Defender...";
            _config = await _pipeClient.GetPortDefenderConfigAsync() ?? new PortDefenderConfigDto();

            // Формируем уникальный отсортированный список портов
            var ports = new HashSet<int>(_config.ProtectedPorts ?? new List<int>());

            // Если окно открыто для конкретного порта — предзаполнить его в списке
            if (_initialPort is >= 1 and <= 65535)
            {
                ports.Add(_initialPort.Value);
            }

            // Заполняем реактивную коллекцию элементов
            _portsCollection.Clear();
            foreach (var port in ports.OrderBy(p => p))
            {
                _portsCollection.Add(new PortDefenderPortItem(port));
            }
            UpdatePortsCountBadge();

            ChkEnabled.IsChecked = _config.Enabled;
            TxtMaxAttempts.Text = _config.MaxAttempts.ToString();
            TxtWindowSeconds.Text = _config.WindowSeconds.ToString();
            TxtBanDuration.Text = _config.BanDurationMinutes.ToString();
            ChkAutoEscalate.IsChecked = _config.AutoEscalate;
            ChkTrustPrivate.IsChecked = _config.TrustPrivateNetworks;
            TxtWhitelist.Text = string.Join(", ", _config.WhitelistIps ?? new List<string>());

            TxtStatus.Text = _initialPort.HasValue
                ? $"Порт {_initialPort.Value} успешно добавлен в список для настройки."
                : "Конфигурация успешно загружена.";
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

    /// <summary>
    /// Обновляет бейдж количества портов в интерфейсе.
    /// </summary>
    private void UpdatePortsCountBadge()
    {
        TxtPortsCountBadge.Text = _portsCollection.Count switch
        {
            0 => "0 портов",
            1 => "1 порт",
            >= 2 and <= 4 => $"{_portsCollection.Count} порта",
            _ => $"{_portsCollection.Count} портов"
        };
    }

    /// <summary>
    /// Обрабатывает нажатие кнопки добавления нового порта.
    /// </summary>
    private void OnAddPortClick(object sender, RoutedEventArgs e)
    {
        var existing = _portsCollection.Select(p => p.Port);
        var dialog = new PortDefenderPortEditWindow(null, existing) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            var newPort = dialog.ResultPort;
            var newItem = new PortDefenderPortItem(newPort);

            // Вставляем с сохранением сортировки по возрастанию номера порта
            int index = 0;
            while (index < _portsCollection.Count && _portsCollection[index].Port < newPort)
            {
                index++;
            }
            _portsCollection.Insert(index, newItem);
            GridProtectedPorts.SelectedItem = newItem;
            UpdatePortsCountBadge();
            TxtStatus.Text = $"Порт #{newPort} ({newItem.ServiceName}) добавлен в список отслеживания.";
        }
    }

    /// <summary>
    /// Обрабатывает нажатие кнопки редактирования выбранного порта из панели действий.
    /// </summary>
    private void OnEditPortClick(object sender, RoutedEventArgs e)
    {
        if (GridProtectedPorts.SelectedItem is PortDefenderPortItem selectedItem)
        {
            EditPortItem(selectedItem);
        }
        else
        {
            TxtStatus.Text = "Выберите порт в таблице для редактирования.";
        }
    }

    /// <summary>
    /// Обрабатывает клик по кнопке редактирования внутри строки DataGrid.
    /// </summary>
    private void OnRowEditPortClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PortDefenderPortItem item })
        {
            EditPortItem(item);
        }
    }

    /// <summary>
    /// Открывает диалог редактирования для конкретного элемента порта.
    /// </summary>
    private void EditPortItem(PortDefenderPortItem item)
    {
        var existing = _portsCollection.Select(p => p.Port);
        var dialog = new PortDefenderPortEditWindow(item.Port, existing) { Owner = this };

        if (dialog.ShowDialog() == true && dialog.ResultPort != item.Port)
        {
            int oldPort = item.Port;
            item.UpdatePort(dialog.ResultPort);

            // Пересортировываем коллекцию
            var sorted = _portsCollection.OrderBy(p => p.Port).ToList();
            _portsCollection.Clear();
            foreach (var p in sorted)
            {
                _portsCollection.Add(p);
            }
            GridProtectedPorts.SelectedItem = item;
            TxtStatus.Text = $"Порт #{oldPort} изменён на #{dialog.ResultPort} ({item.ServiceName}).";
        }
    }

    /// <summary>
    /// Обрабатывает нажатие кнопки удаления выбранного порта из панели действий.
    /// </summary>
    private void OnDeletePortClick(object sender, RoutedEventArgs e)
    {
        if (GridProtectedPorts.SelectedItem is PortDefenderPortItem selectedItem)
        {
            DeletePortItem(selectedItem);
        }
        else
        {
            TxtStatus.Text = "Выберите порт в таблице для удаления.";
        }
    }

    /// <summary>
    /// Обрабатывает клик по кнопке удаления внутри строки DataGrid.
    /// </summary>
    private void OnRowDeletePortClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PortDefenderPortItem item })
        {
            DeletePortItem(item);
        }
    }

    /// <summary>
    /// Удаляет порт из коллекции с подтверждением и обновлением счетчика.
    /// </summary>
    private void DeletePortItem(PortDefenderPortItem item)
    {
        int portNumber = item.Port;
        var serviceName = item.ServiceName;

        _portsCollection.Remove(item);
        UpdatePortsCountBadge();
        TxtStatus.Text = $"Порт #{portNumber} ({serviceName}) удалён из списка отслеживаемых.";
    }

    /// <summary>
    /// Двойной клик по строке таблицы открывает диалог редактирования.
    /// </summary>
    private void OnGridPortsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridProtectedPorts.SelectedItem is PortDefenderPortItem selectedItem)
        {
            EditPortItem(selectedItem);
        }
    }

    /// <summary>
    /// Обработка горячей клавиши Delete на строках таблицы.
    /// </summary>
    private void OnGridPortsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && GridProtectedPorts.SelectedItem is PortDefenderPortItem selectedItem)
        {
            DeletePortItem(selectedItem);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Разбирает список доверенных IP-адресов через запятую, пробел или новую строку.
    /// </summary>
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

    /// <summary>
    /// Разбирает целое число с безопасным значением по умолчанию.
    /// </summary>
    private static int ParsePositiveInt(string text, int fallback) =>
        int.TryParse((text ?? string.Empty).Trim(), out var value) && value >= 0 ? value : fallback;

    /// <summary>
    /// Сохраняет обновленную конфигурацию Port Defender через именованный канал в системную службу.
    /// </summary>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtStatus.Text = "Синхронизация конфигурации со службой...";
            var config = _config ?? new PortDefenderConfigDto();

            config.Enabled = ChkEnabled.IsChecked == true;
            config.ProtectedPorts = _portsCollection.Select(p => p.Port).Distinct().OrderBy(p => p).ToList();

            // Если список пуст и был начальный порт — гарантируем его сохранение
            if (config.ProtectedPorts.Count == 0 && _initialPort is >= 1 and <= 65535)
            {
                config.ProtectedPorts.Add(_initialPort.Value);
            }

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
                TxtStatus.Text = "Конфигурация Port Defender успешно синхронизирована.";
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
            TxtStatus.Text = $"Ошибка сохранения конфигурации: {ex.Message}";
        }
    }

    /// <summary>
    /// Закрывает окно без сохранения изменений.
    /// </summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
