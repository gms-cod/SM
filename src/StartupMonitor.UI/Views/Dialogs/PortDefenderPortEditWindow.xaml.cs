using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StartupMonitor.UI.Models;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Диалоговое окно добавления и редактирования защищаемого порта в Port Defender.
/// Предоставляет валидацию диапазона портов (1..65535), быстрый выбор популярных шаблонов
/// и интерактивный предпросмотр уровня риска и назначения порта.
/// </summary>
public partial class PortDefenderPortEditWindow
{
    private readonly int? _originalPort;
    private readonly HashSet<int> _existingPorts;

    /// <summary>
    /// Результирующий валидный номер порта после подтверждения пользователем.
    /// </summary>
    public int ResultPort { get; private set; }

    /// <summary>
    /// Инициализирует окно добавления или редактирования защищаемого порта.
    /// </summary>
    /// <param name="currentPort">Текущий редактируемый порт (null при создании нового).</param>
    /// <param name="existingPorts">Коллекция существующих портов для предотвращения дубликатов.</param>
    public PortDefenderPortEditWindow(int? currentPort = null, IEnumerable<int>? existingPorts = null)
    {
        InitializeComponent();

        _originalPort = currentPort;
        _existingPorts = existingPorts != null ? new HashSet<int>(existingPorts) : new HashSet<int>();

        // Если редактируем — исключаем сам исходный порт из проверки на дубликат
        if (_originalPort.HasValue)
        {
            _existingPorts.Remove(_originalPort.Value);
        }

        PopulatePresets();
        SetupMode(currentPort);
    }

    /// <summary>
    /// Настраивает заголовок и начальные значения формы в зависимости от режима.
    /// </summary>
    /// <param name="port">Номер порта для редактирования либо null.</param>
    private void SetupMode(int? port)
    {
        bool isEdit = port.HasValue;
        var title = isEdit ? "Редактирование защищаемого порта" : "Добавление защищаемого порта";
        Title = title;
        DialogTitleBar.Title = title;
        TxtHeaderTitle.Text = title;

        if (isEdit)
        {
            TxtPort.Text = port!.Value.ToString();
            UpdatePreview(port.Value);
            BtnSave.Content = "Применить";
        }
        else
        {
            TxtPort.Text = string.Empty;
            UpdatePreview(3389); // Дефолтный предпросмотр популярного RDP
            BtnSave.Content = "Добавить";
        }
    }

    /// <summary>
    /// Заполняет выпадающий список популярных сетевых служб и портов.
    /// </summary>
    private void PopulatePresets()
    {
        CmbPresets.Items.Clear();
        CmbPresets.Items.Add(new ComboBoxItem { Content = "— Выберите шаблон из каталога —", Tag = 0 });

        foreach (var (port, title) in PortDefenderPortHelper.GetPresets())
        {
            CmbPresets.Items.Add(new ComboBoxItem { Content = title, Tag = port });
        }

        CmbPresets.SelectedIndex = 0;
    }

    /// <summary>
    /// Обрабатывает выбор предустановленного шаблона из выпадающего списка.
    /// </summary>
    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CmbPresets.SelectedItem is ComboBoxItem { Tag: int port } && port > 0)
        {
            TxtPort.Text = port.ToString();
            TxtPort.CaretIndex = TxtPort.Text.Length;
            UpdatePreview(port);
            HideError();
        }
    }

    /// <summary>
    /// Обрабатывает изменение текста в поле ввода порта и обновляет превью.
    /// </summary>
    private void OnPortTextChanged(object sender, TextChangedEventArgs e)
    {
        HideError();

        if (int.TryParse(TxtPort.Text.Trim(), out var port) && port is >= 1 and <= 65535)
        {
            UpdatePreview(port);
        }
        else
        {
            TxtPreviewService.Text = "Служба: —";
            TxtPreviewDesc.Text = "Введите номер порта от 1 до 65535";
            TxtPreviewPortBadge.Text = "#?";
            TxtPreviewRisk.Text = "Неизвестно";
            BorderRiskBadge.Background = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        }
    }

    /// <summary>
    /// Обновляет блок предварительного просмотра информации о порте.
    /// </summary>
    /// <param name="port">Номер порта.</param>
    private void UpdatePreview(int port)
    {
        var (name, desc, risk) = PortDefenderPortHelper.GetPortInfo(port);

        TxtPreviewService.Text = $"Служба: {name}";
        TxtPreviewDesc.Text = desc;
        TxtPreviewPortBadge.Text = $"#{port}";
        TxtPreviewRisk.Text = PortDefenderPortHelper.GetRiskBadgeText(risk);

        try
        {
            var hex = PortDefenderPortHelper.GetRiskBadgeColor(risk);
            var color = (Color)ColorConverter.ConvertFromString(hex);
            BorderRiskBadge.Background = new SolidColorBrush(color);

            var textHex = PortDefenderPortHelper.GetRiskTextColor(risk);
            var textColor = (Color)ColorConverter.ConvertFromString(textHex);
            TxtPreviewRisk.Foreground = new SolidColorBrush(textColor);
        }
        catch
        {
            BorderRiskBadge.Background = new SolidColorBrush(Color.FromRgb(0, 137, 123));
            TxtPreviewRisk.Foreground = Brushes.White;
        }
    }

    /// <summary>
    /// Проверяет корректность введенного порта и сохраняет результат.
    /// </summary>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var text = (TxtPort.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowError("Укажите номер порта для защиты.");
            TxtPort.Focus();
            return;
        }

        if (!int.TryParse(text, out var port))
        {
            ShowError("Номер порта должен быть целым числом.");
            TxtPort.Focus();
            return;
        }

        if (port is < 1 or > 65535)
        {
            ShowError("Номер порта должен находиться в диапазоне от 1 до 65535.");
            TxtPort.Focus();
            return;
        }

        if (_existingPorts.Contains(port))
        {
            ShowError($"Порт {port} уже присутствует в списке отслеживаемых Port Defender.");
            TxtPort.Focus();
            return;
        }

        ResultPort = port;
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Отображает сообщение об ошибке валидации.
    /// </summary>
    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Скрывает сообщение об ошибке.
    /// </summary>
    private void HideError()
    {
        TxtError.Text = string.Empty;
        TxtError.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Закрывает диалог без сохранения изменений.
    /// </summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
