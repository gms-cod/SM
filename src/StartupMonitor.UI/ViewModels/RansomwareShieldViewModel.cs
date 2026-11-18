using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Ransomware;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.ViewModels;

/// <summary>
/// Отображаемый элемент инцидента атаки программы-вымогателя в таблице журнала.
/// </summary>
public sealed class RansomwareIncidentItem
{
    public string Id { get; set; } = string.Empty;
    public string TimestampDisplay { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string TriggerReasonDisplay { get; set; } = string.Empty;
    public string ActionTakenDisplay { get; set; } = string.Empty;
    public string AffectedFilesDisplay { get; set; } = string.Empty;
}

/// <summary>
/// Модель представления вкладки «Защита от шифровальщиков» (Anti-Ransomware Shield).
/// Предоставляет управление опциями защиты, отображение 4 стеклянных карточек метрик
/// и журнал перехваченных инцидентов.
/// </summary>
public partial class RansomwareShieldViewModel : ObservableObject
{
    private readonly PipeClient _pipeClient;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _protectVssShadowCopies = true;

    [ObservableProperty]
    private bool _enableCanaryTraps = true;

    [ObservableProperty]
    private bool _autoSuspendProcess = true;

    [ObservableProperty]
    private bool _isolateNetworkOnAttack = true;

    [ObservableProperty]
    private bool _autoRollbackAffectedFiles = true;

    [ObservableProperty]
    private bool _whitelistTrustedSigners = true;

    [ObservableProperty]
    private int _activeTrapsCount = 6;

    [ObservableProperty]
    private int _blockedAttacksCount = 0;

    [ObservableProperty]
    private int _suspendedProcessesCount = 0;

    [ObservableProperty]
    private int _restoredFilesCount = 0;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string _statusBadgeText = string.Empty;

    [ObservableProperty]
    private bool _hasIncidents;

    public ObservableCollection<RansomwareIncidentItem> Incidents { get; } = [];

    public RansomwareShieldViewModel(PipeClient pipeClient)
    {
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));
        UpdateStatusBadge();
    }

    /// <summary>
    /// Загружает текущую конфигурацию и статистику защиты от службы.
    /// </summary>
    [RelayCommand]
    public async Task RefreshStatsAsync()
    {
        try
        {
            UpdateStatusBadge();

            // Если список инцидентов пуст, проверяем наличие
            HasIncidents = Incidents.Count > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RansomwareShieldViewModel] Ошибка обновления статистики: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Сохраняет измененные настройки анти-рансомваре защиты.
    /// </summary>
    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
        {
            var config = new RansomwareShieldConfigDto
            {
                Enabled = IsEnabled,
                ProtectVssShadowCopies = ProtectVssShadowCopies,
                EnableCanaryTraps = EnableCanaryTraps,
                AutoSuspendProcess = AutoSuspendProcess,
                IsolateNetworkOnAttack = IsolateNetworkOnAttack,
                AutoRollbackAffectedFiles = AutoRollbackAffectedFiles,
                WhitelistTrustedSigners = WhitelistTrustedSigners
            };

            IsActive = IsEnabled;
            UpdateStatusBadge();

            // Отправляем через PipeClient если подключен
            var message = new PipeMessage
            {
                Type = PipeMessageTypes.SetRansomwareShieldConfig,
                Payload = System.Text.Json.JsonSerializer.Serialize(config)
            };

            await _pipeClient.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RansomwareShieldViewModel] Ошибка сохранения настроек: {ex.Message}");
        }
    }

    /// <summary>
    /// Создает системную теневую копию тома VSS через утилиту wmic/vssadmin.
    /// </summary>
    [RelayCommand]
    public async Task CreateVssSnapshotAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"(Get-WmiObject -List Win32_ShadowCopy).Create('C:\\', 'ClientAccessible')\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
            });

            await RefreshStatsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RansomwareShieldViewModel] Ошибка создания снимка VSS: {ex.Message}");
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        IsActive = value;
        UpdateStatusBadge();
    }

    private void UpdateStatusBadge()
    {
        StatusBadgeText = IsEnabled
            ? _loc.RansomwareShieldActive
            : _loc.RansomwareShieldDisabled;
    }
}
