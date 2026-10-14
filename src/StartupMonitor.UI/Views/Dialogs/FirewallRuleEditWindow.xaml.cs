using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Модальное окно создания и редактирования правила брандмауэра Windows.
/// </summary>
public partial class FirewallRuleEditWindow
{
    private readonly PipeClient _pipeClient;
    private readonly FirewallRuleDto? _existingRule;

    /// <summary>
    /// Инициализирует окно. Если передано existingRule — поля заполняются из него,
    /// иначе задаются безопасные значения по умолчанию.
    /// </summary>
    /// <param name="pipeClient">Клиент именованного канала для связи со службой.</param>
    /// <param name="existingRule">Существующее правило для редактирования (null = создание).</param>
    public FirewallRuleEditWindow(PipeClient pipeClient, FirewallRuleDto? existingRule = null)
    {
        InitializeComponent();
        _pipeClient = pipeClient;
        _existingRule = existingRule;

        if (_existingRule != null)
            FillFromRule(_existingRule);
        else
            SetSafeDefaults();

        UpdateTitle(_existingRule != null);
    }

    /// <summary>Обновляет заголовок окна и шапки в зависимости от режима.</summary>
    /// <param name="isEdit">True — редактирование, false — создание.</param>
    private void UpdateTitle(bool isEdit)
    {
        var title = isEdit ? "Редактирование правила" : "Создание правила брандмауэра";
        Title = title;
        TitleBar.Title = title;
        TxtHeaderTitle.Text = title;
    }

    /// <summary>Задаёт безопасные значения по умолчанию для нового правила.</summary>
    private void SetSafeDefaults()
    {
        TxtName.Text = string.Empty;
        TxtDescription.Text = string.Empty;
        RadioBlock.IsChecked = true;
        RadioAllow.IsChecked = false;
        RadioInbound.IsChecked = true;
        RadioOutbound.IsChecked = false;
        SelectProtocol(FirewallProtocol.Any);
        TxtLocalPorts.Text = string.Empty;
        TxtRemotePorts.Text = string.Empty;
        TxtRemoteAddresses.Text = string.Empty;
        TxtApplicationPath.Text = string.Empty;
        TxtServiceName.Text = string.Empty;
        ChkDomain.IsChecked = true;
        ChkPrivate.IsChecked = true;
        ChkPublic.IsChecked = true;
        ChkEnabled.IsChecked = true;
        TxtStatus.Text = string.Empty;
    }

    /// <summary>Заполняет поля формы из существующего правила.</summary>
    /// <param name="rule">DTO правила брандмауэра.</param>
    private void FillFromRule(FirewallRuleDto rule)
    {
        TxtName.Text = rule.Name;
        TxtName.IsEnabled = false; // Имя — ключ правила, не переименовываем при редактировании.
        TxtDescription.Text = rule.Description;
        RadioAllow.IsChecked = rule.Action == FirewallAction.Allow;
        RadioBlock.IsChecked = rule.Action != FirewallAction.Allow;
        RadioInbound.IsChecked = rule.Direction != FirewallDirection.Outbound;
        RadioOutbound.IsChecked = rule.Direction == FirewallDirection.Outbound;
        SelectProtocol(rule.Protocol);
        TxtLocalPorts.Text = rule.LocalPorts == "*" ? string.Empty : rule.LocalPorts;
        TxtRemotePorts.Text = rule.RemotePorts == "*" ? string.Empty : (rule.RemotePorts ?? string.Empty);
        TxtRemoteAddresses.Text = rule.RemoteAddresses == "*" ? string.Empty : rule.RemoteAddresses;
        TxtApplicationPath.Text = rule.ApplicationPath ?? string.Empty;
        TxtServiceName.Text = rule.ServiceName ?? string.Empty;
        ChkDomain.IsChecked = rule.Profiles.HasFlag(FirewallProfile.Domain);
        ChkPrivate.IsChecked = rule.Profiles.HasFlag(FirewallProfile.Private);
        ChkPublic.IsChecked = rule.Profiles.HasFlag(FirewallProfile.Public);
        ChkEnabled.IsChecked = rule.Enabled;
        TxtStatus.Text = string.Empty;
    }

    /// <summary>Выбирает элемент протокола в ComboBox по значению.</summary>
    /// <param name="protocol">Значение протокола.</param>
    private void SelectProtocol(FirewallProtocol protocol)
    {
        var code = (int)protocol;
        foreach (var item in CmbProtocol.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == code.ToString())
            {
                CmbProtocol.SelectedItem = item;
                return;
            }
        }
        CmbProtocol.SelectedIndex = 0;
    }

    /// <summary>Возвращает выбранный протокол из ComboBox.</summary>
    private FirewallProtocol GetSelectedProtocol()
    {
        if (CmbProtocol.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Tag?.ToString(), out var code))
            return (FirewallProtocol)code;
        return FirewallProtocol.Any;
    }

    /// <summary>Собирает DTO правила из полей формы.</summary>
    private FirewallRuleDto BuildRule()
    {
        var profiles = FirewallProfile.All;
        // Если хотя бы один флажок снят — собираем маску вручную; все сняты = All (как в COM).
        var mask = (ChkDomain.IsChecked == true ? FirewallProfile.Domain : 0)
                 | (ChkPrivate.IsChecked == true ? FirewallProfile.Private : 0)
                 | (ChkPublic.IsChecked == true ? FirewallProfile.Public : 0);
        if (mask != 0)
            profiles = mask;

        var localPorts = TxtLocalPorts.Text.Trim();
        var remotePorts = TxtRemotePorts.Text.Trim();
        var remoteAddresses = TxtRemoteAddresses.Text.Trim();

        return new FirewallRuleDto
        {
            Name = TxtName.Text.Trim(),
            Description = TxtDescription.Text.Trim(),
            Direction = RadioOutbound.IsChecked == true ? FirewallDirection.Outbound : FirewallDirection.Inbound,
            Action = RadioAllow.IsChecked == true ? FirewallAction.Allow : FirewallAction.Block,
            Protocol = GetSelectedProtocol(),
            LocalPorts = string.IsNullOrEmpty(localPorts) ? "*" : localPorts,
            RemotePorts = string.IsNullOrEmpty(remotePorts) ? "*" : remotePorts,
            RemoteAddresses = string.IsNullOrEmpty(remoteAddresses) ? "*" : remoteAddresses,
            ApplicationPath = string.IsNullOrWhiteSpace(TxtApplicationPath.Text) ? null : TxtApplicationPath.Text.Trim(),
            ServiceName = string.IsNullOrWhiteSpace(TxtServiceName.Text) ? null : TxtServiceName.Text.Trim(),
            Enabled = ChkEnabled.IsChecked == true,
            Profiles = profiles,
        };
    }

    /// <summary>Валидирует форму перед сохранением. Возвращает текст ошибки или null.</summary>
    /// <param name="rule">Собранное DTO правила.</param>
    private string? Validate(FirewallRuleDto rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
            return "Укажите имя правила.";
        return null;
    }

    /// <summary>
    /// Проверяет, что порты заданы только для TCP/UDP.
    /// Возвращает true, если можно продолжать сохранение (порты очищены с согласия пользователя).
    /// </summary>
    /// <param name="rule">Собранное DTO правила.</param>
    private bool EnsurePortsMatchProtocol(FirewallRuleDto rule)
    {
        if (rule.Protocol is FirewallProtocol.Tcp or FirewallProtocol.Udp)
            return true;

        var hasPorts = (rule.LocalPorts != "*" && !string.IsNullOrWhiteSpace(rule.LocalPorts))
                    || (rule.RemotePorts != "*" && !string.IsNullOrWhiteSpace(rule.RemotePorts));
        if (!hasPorts)
            return true;

        var confirm = MessageBox.Show(this,
            "Порты применимы только к протоколам TCP и UDP. Очистить поля портов и продолжить?",
            "Предупреждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
            return false;

        rule.LocalPorts = "*";
        rule.RemotePorts = "*";
        TxtLocalPorts.Text = string.Empty;
        TxtRemotePorts.Text = string.Empty;
        return true;
    }

    /// <summary>
    /// Проверяет удалённые адреса через FirewallGuard при создании блокирующего правила.
    /// Возвращает текст ошибки или null.
    /// </summary>
    /// <param name="rule">Собранное DTO правила.</param>
    private static string? ValidateBlockableAddresses(FirewallRuleDto rule)
    {
        if (rule.Action != FirewallAction.Block)
            return null;
        if (string.IsNullOrWhiteSpace(rule.RemoteAddresses) || rule.RemoteAddresses.Trim() == "*")
            return null;

        var parts = rule.RemoteAddresses.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var entry = part.Trim();
            if (string.IsNullOrEmpty(entry) || entry == "*")
                continue;
            if (!FirewallGuard.IsBlockableAddress(entry, out var error))
                return error;
        }
        return null;
    }

    /// <summary>Обрабатывает нажатие кнопки «Обзор...» — выбор .exe через OpenFileDialog.</summary>
    private void OnBrowseAppClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Title = "Выбор программы для правила брандмауэра"
        };
        if (dlg.ShowDialog(this) == true)
            TxtApplicationPath.Text = dlg.FileName;
    }

    /// <summary>Сохраняет правило через PipeClient с валидацией.</summary>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var rule = BuildRule();

            var error = Validate(rule);
            if (error != null)
            {
                TxtStatus.Text = error;
                MessageBox.Show(this, error, "Проверка правила", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!EnsurePortsMatchProtocol(rule))
                return;

            var addrError = ValidateBlockableAddresses(rule);
            if (addrError != null)
            {
                TxtStatus.Text = addrError;
                MessageBox.Show(this, addrError, "Защита от самоблокировки", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            TxtStatus.Text = "Сохранение...";
            var result = await _pipeClient.CreateOrUpdateFirewallRuleAsync(rule);
            if (result != null && result.Success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                TxtStatus.Text = result?.Message ?? "Служба не подтвердила сохранение правила.";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка сохранения: {ex.Message}";
        }
    }

    /// <summary>Закрывает окно без сохранения.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
