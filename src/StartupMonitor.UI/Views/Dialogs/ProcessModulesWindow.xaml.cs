using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Окно детального инспектирования загруженных модулей (DLL) и открытых дескрипторов (Handles) процесса.
/// </summary>
public partial class ProcessModulesWindow
{
    private readonly ProcessInfo _targetProcess;
    private readonly PipeClient _pipeClient;
    private List<ProcessModuleInfo> _allModules = new();
    private List<ProcessHandleInfo> _allHandles = new();
    private bool _isLoading;

    public string WindowTitle => $"{_targetProcess.ProcessName} (PID: {_targetProcess.Pid}) — {LocalizationService.Instance.ProcessModulesTitle}";

    /// <summary>ProcessModulesWindow: Выполняет операцию ProcessModulesWindow.</summary>
    /// <param name="targetProcess">Входные данные: значение параметра targetProcess (ProcessInfo).</param>
    /// <param name="pipeClient">Входные данные: значение параметра pipeClient (PipeClient).</param>
    public ProcessModulesWindow(ProcessInfo targetProcess, PipeClient pipeClient)
    {
        InitializeComponent();
        _targetProcess = targetProcess;
        _pipeClient = pipeClient;

        TxtProcessName.Text = _targetProcess.ProcessName;
        TxtProcessPid.Text = $"PID: {_targetProcess.Pid}";
        TxtExecutablePath.Text = !string.IsNullOrEmpty(_targetProcess.FilePath) ? _targetProcess.FilePath : "(путь неизвестен)";

        Loaded += async (_, _) => await LoadDataAsync();
    }

    /// <summary>LoadDataAsync: Загружает данные DataAsync.</summary>
    private async Task LoadDataAsync()
    {
        using var _prof = ExecutionProfiler.Measure("UI.Modules.LoadData");
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            TxtStatus.Text = "Загрузка модулей и дескрипторов...";
            var response = await _pipeClient.GetProcessModulesAsync(_targetProcess.Pid, true);

            if (response != null && response.Success)
            {
                _allModules = response.Modules ?? new();
                _allHandles = response.Handles ?? new();

                TxtTotalModulesCount.Text = _allModules.Count.ToString();
                TxtSignedModulesCount.Text = _allModules.Count(m => m.IsSigned).ToString();
                TxtUnsignedModulesCount.Text = _allModules.Count(m => !m.IsSigned || m.IsTempPath).ToString();
                TxtHandlesCount.Text = _allHandles.Count.ToString();

                ApplyFilter();
                TxtStatus.Text = $"Обновлено в {DateTime.Now:HH:mm:ss} • Модулей: {_allModules.Count}, Дескрипторов: {_allHandles.Count}";
            }
            else
            {
                TxtStatus.Text = response?.ErrorMessage ?? "Не удалось получить список модулей.";
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>ApplyFilter: Выполняет операцию ApplyFilter.</summary>
    private void ApplyFilter()
    {
        var filter = TxtSearch.Text?.Trim() ?? string.Empty;

        // Фильтрация модулей
        IEnumerable<ProcessModuleInfo> modules = _allModules;
        if (!string.IsNullOrEmpty(filter))
        {
            modules = modules.Where(m =>
                m.ModuleName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                m.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(m.FileDescription) && m.FileDescription.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(m.SignerName) && m.SignerName.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(m.CompanyName) && m.CompanyName.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                m.BaseAddress.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var modList = modules.ToList();
        GridModules.ItemsSource = modList;
        PanelEmptyModules.Visibility = (modList.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        // Фильтрация дескрипторов
        IEnumerable<ProcessHandleInfo> handles = _allHandles;
        if (!string.IsNullOrEmpty(filter))
        {
            handles = handles.Where(h =>
                h.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                h.HandleType.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                h.HandleValue.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var handleList = handles.ToList();
        GridHandles.ItemsSource = handleList;
        PanelEmptyHandles.Visibility = (handleList.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
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
        await LoadDataAsync();
    }

    /// <summary>OnOpenLocationClick: Обрабатывает событие OnOpenLocationClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnOpenLocationClick(object sender, RoutedEventArgs e)
    {
        if (GridModules.SelectedItem is ProcessModuleInfo module && !string.IsNullOrEmpty(module.FilePath) && File.Exists(module.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{module.FilePath}\"");
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Ошибка открытия проводника: {ex.Message}";
            }
        }
    }

    /// <summary>OnCopyPathClick: Обрабатывает событие OnCopyPathClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (GridModules.SelectedItem is ProcessModuleInfo module && !string.IsNullOrEmpty(module.FilePath))
        {
            Clipboard.SetText(module.FilePath);
            TxtStatus.Text = $"Путь скопирован в буфер: {module.FilePath}";
        }
    }

    /// <summary>OnCopyNameClick: Обрабатывает событие OnCopyNameClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCopyNameClick(object sender, RoutedEventArgs e)
    {
        if (GridModules.SelectedItem is ProcessModuleInfo module && !string.IsNullOrEmpty(module.ModuleName))
        {
            Clipboard.SetText(module.ModuleName);
            TxtStatus.Text = $"Имя модуля скопировано в буфер: {module.ModuleName}";
        }
    }

    /// <summary>OnCheckKasperskyClick: Обрабатывает событие OnCheckKasperskyClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCheckKasperskyClick(object sender, RoutedEventArgs e)
    {
        if (GridModules.SelectedItem is ProcessModuleInfo module && !string.IsNullOrEmpty(module.FilePath))
        {
            var hash = GetFileSha256(module.FilePath);
            var url = !string.IsNullOrEmpty(hash)
                ? $"https://opentip.kaspersky.com/{hash}"
                : "https://opentip.kaspersky.com/";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>OnCheckVirusTotalClick: Обрабатывает событие OnCheckVirusTotalClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCheckVirusTotalClick(object sender, RoutedEventArgs e)
    {
        if (GridModules.SelectedItem is ProcessModuleInfo module && !string.IsNullOrEmpty(module.FilePath))
        {
            var hash = GetFileSha256(module.FilePath);
            var url = !string.IsNullOrEmpty(hash)
                ? $"https://www.virustotal.com/gui/file/{hash}"
                : "https://www.virustotal.com/";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>OnCopyHandleNameClick: Обрабатывает событие OnCopyHandleNameClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCopyHandleNameClick(object sender, RoutedEventArgs e)
    {
        if (GridHandles.SelectedItem is ProcessHandleInfo handle && !string.IsNullOrEmpty(handle.Name))
        {
            Clipboard.SetText(handle.Name);
            TxtStatus.Text = $"Имя дескриптора скопировано: {handle.Name}";
        }
    }

    /// <summary>OnCloseClick: Обрабатывает событие OnCloseClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>GetFileSha256: Возвращает FileSha256.</summary>
    /// <param name="path">Входные данные: значение параметра path (string).</param>
    /// <returns>Выходные данные: результирующее значение типа string?.</returns>
    private static string? GetFileSha256(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
