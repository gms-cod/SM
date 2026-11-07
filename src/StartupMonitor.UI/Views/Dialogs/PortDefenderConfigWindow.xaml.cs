using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// <summary>Ключ и параметры текущего статуса позволяют переводить уже открытое окно без повторного IPC.</summary>
    private string _statusKey = "NetStatusLoading";
    private object?[] _statusArguments = [];

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
        PropertyChangedEventManager.AddHandler(LocalizationService.Instance, OnLanguageChanged, string.Empty);
    }

    /// <summary>Сохраняет семантику статуса и отображает её на активном языке.</summary>
    /// <param name="key">Ресурс сообщения.</param>
    /// <param name="arguments">Значения заполнителей.</param>
    private void SetStatus(string key, params object?[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        TxtStatus.Text = LocalizationService.Instance.Format(key, arguments);
    }

    /// <summary>Обновляет счётчик и текущий статус без потери несохранённых настроек.</summary>
    /// <param name="sender">Сервис локализации.</param>
    /// <param name="e">Уведомление о смене языка.</param>
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdatePortsCountBadge();
        TxtStatus.Text = LocalizationService.Instance.Format(_statusKey, _statusArguments);
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
            SetStatus("DefenderConfigLoading");
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

            if (_initialPort.HasValue) SetStatus("DefenderInitialAddedFormat", _initialPort.Value);
            else SetStatus("DefenderConfigLoaded");
        }
        catch (Exception ex)
        {
            _config ??= new PortDefenderConfigDto();
            SetStatus("DefenderConfigLoadErrorFormat", ex.Message);
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
        TxtPortsCountBadge.Text = LocalizationService.Instance.Format("DefenderPortsCountFormat", _portsCollection.Count);
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
            SetStatus("DefenderListAddedFormat", newPort, newItem.ServiceName);
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
            SetStatus("DefenderSelectEdit");
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
            // Перенос сохраняет индивидуальные параметры и выключенное состояние всех правил исходного порта.
            if (_config?.Rules is not null)
                foreach (var rule in _config.Rules.Where(rule => rule is not null && rule.LocalPort == oldPort))
                    rule.LocalPort = dialog.ResultPort;

            // Пересортировываем коллекцию
            var sorted = _portsCollection.OrderBy(p => p.Port).ToList();
            _portsCollection.Clear();
            foreach (var p in sorted)
            {
                _portsCollection.Add(p);
            }
            GridProtectedPorts.SelectedItem = item;
            SetStatus("DefenderListMovedFormat", oldPort, dialog.ResultPort, item.ServiceName);
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
            SetStatus("DefenderSelectDelete");
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
        SetStatus("DefenderListRemovedFormat", portNumber, serviceName);
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

    /// <summary>Согласует оба представления портов после редактирования, сохраняя параметры оставшихся правил.</summary>
    /// <param name="config">Изменяемая конфигурация, полученная службой при открытии диалога.</param>
    /// <param name="ports">Полный список портов, подтверждённый пользователем; пустой список снимает всю защиту портов.</param>
    internal static void SynchronizeProtectedPorts(PortDefenderConfigDto config, IEnumerable<int> ports)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(ports);
        var protectedPorts = ports.ToHashSet();
        if (protectedPorts.Any(port => port is < 1 or > 65535)) throw new ArgumentOutOfRangeException(nameof(ports));
        config.ProtectedPorts = protectedPorts.OrderBy(port => port).ToList();
        config.Rules?.RemoveAll(rule => rule is null || !protectedPorts.Contains(rule.LocalPort));
    }

    /// <summary>
    /// Сохраняет обновленную конфигурацию Port Defender через именованный канал в системную службу.
    /// </summary>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("DefenderConfigSaving");
            var config = _config ?? new PortDefenderConfigDto();

            config.Enabled = ChkEnabled.IsChecked == true;
            // Пустой список — явный выбор пользователя; предзаполнение при открытии не отменяет последующее удаление.
            SynchronizeProtectedPorts(config, _portsCollection.Select(port => port.Port));

            config.MaxAttempts = ParsePositiveInt(TxtMaxAttempts.Text, 10);
            if (config.MaxAttempts == 0) config.MaxAttempts = 10;
            config.WindowSeconds = ParsePositiveInt(TxtWindowSeconds.Text, 60);
            if (config.WindowSeconds == 0) config.WindowSeconds = 60;
            config.BanDurationMinutes = ParsePositiveInt(TxtBanDuration.Text, 15);
            config.AutoEscalate = ChkAutoEscalate.IsChecked == true;
            config.TrustPrivateNetworks = ChkTrustPrivate.IsChecked == true;
            config.WhitelistIps = ParseWhitelist(TxtWhitelist.Text);

            var result = await _pipeClient.SetPortDefenderConfigAsync(config);
            if (result.Success)
            {
                SetStatus("DefenderConfigSaved");
                DialogResult = true;
                Close();
            }
            else
            {
                SetStatus("DefenderConfigSaveFailedFormat", result.Message);
            }
        }
        catch (Exception ex)
        {
            SetStatus("DefenderConfigSaveErrorFormat", ex.Message);
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
