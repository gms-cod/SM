using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using StartupMonitor.Shared.Models.Toolbox;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Views;

/// <summary>
/// Представление набора повседневных инструментов (Toolbox):
/// File Locksmith, Keep-Awake, Junk Cleaner, LAN Scanner, Hash &amp; Password Security.
/// </summary>
public partial class ToolboxView : UserControl
{
    private readonly PipeClient _pipeClient;
    private readonly ObservableCollection<FileLockProcessInfo> _lockingProcesses = new();
    private readonly ObservableCollection<JunkCategoryInfo> _junkCategories = new();
    private readonly ObservableCollection<LanDeviceDto> _lanDevices = new();

    private CancellationTokenSource? _lanScanCts;
    private CancellationTokenSource? _junkCts;

    /// <summary>Дизайнерский конструктор.</summary>
    public ToolboxView() : this(new PipeClient()) { }

    /// <summary>Основной конструктор с внедрением общего подключенного PipeClient.</summary>
    public ToolboxView(PipeClient pipeClient)
    {
        InitializeComponent();
        _pipeClient = pipeClient ?? throw new ArgumentNullException(nameof(pipeClient));

        DgLockingProcesses.ItemsSource = _lockingProcesses;
        DgJunkCategories.ItemsSource = _junkCategories;
        DgLanDevices.ItemsSource = _lanDevices;

        Loaded += async (_, _) => await ActivateAsync();
        Unloaded += (_, _) => Deactivate();
    }

    /// <summary>Активация при переходе на вкладку: загрузка текущего статуса Awake.</summary>
    public async Task ActivateAsync()
    {
        await RefreshAwakeStatusAsync();
    }

    /// <summary>Деактивация при уходе с вкладки: отмена длительных операций.</summary>
    public void Deactivate()
    {
        _lanScanCts?.Cancel();
        _junkCts?.Cancel();
    }

    #region 1. File Locksmith

    private void OnBrowseLocksmithClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите файл для поиска блокировок",
            Filter = "Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
        {
            TxtLocksmithPath.Text = dlg.FileName;
            _ = FindLocksAsync(dlg.FileName);
        }
    }

    private async void OnFindLocksClick(object sender, RoutedEventArgs e)
    {
        var path = TxtLocksmithPath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show("Укажите путь к файлу или папке.", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await FindLocksAsync(path);
    }

    private async Task FindLocksAsync(string path)
    {
        BtnFindLocks.IsEnabled = false;
        TxtLocksmithDetails.Text = "Поиск блокирующих процессов...";
        _lockingProcesses.Clear();

        try
        {
            var result = await _pipeClient.FindFileLockingProcessesAsync(path);
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                TxtLocksmithDetails.Text = $"Ошибка: {result.ErrorMessage}";
                SetLockBadge("Ошибка", false);
                return;
            }

            if (result.LockingProcesses.Count == 0)
            {
                TxtLocksmithDetails.Text = "Файл/папка свободна. Блокирующие процессы не обнаружены.";
                SetLockBadge("Свободен", true);
            }
            else
            {
                foreach (var p in result.LockingProcesses)
                {
                    _lockingProcesses.Add(p);
                }
                TxtLocksmithDetails.Text = $"Обнаружено процессов: {result.LockingProcesses.Count}";
                SetLockBadge($"Заблокирован ({result.LockingProcesses.Count})", false);
            }
        }
        catch (Exception ex)
        {
            TxtLocksmithDetails.Text = $"Ошибка: {ex.Message}";
            SetLockBadge("Сбой", false);
        }
        finally
        {
            BtnFindLocks.IsEnabled = true;
        }
    }

    private void SetLockBadge(string text, bool isFree)
    {
        TxtLockStatusBadge.Text = text;
        BadgeLockStatus.Background = isFree
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) // Emerald
            : new SolidColorBrush(Color.FromRgb(239, 68, 68));  // Red
        TxtLockStatusBadge.Foreground = Brushes.White;
    }

    private void OnKillLockingProcessClick(object sender, RoutedEventArgs e)
    {
        if (DgLockingProcesses.SelectedItem is not FileLockProcessInfo proc)
        {
            MessageBox.Show("Выберите процесс для завершения.", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Завершить процесс {proc.ProcessName} (PID {proc.ProcessId})?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var p = Process.GetProcessById(proc.ProcessId);
            p.Kill();
            _lockingProcesses.Remove(proc);
            TxtLocksmithDetails.Text = $"Процесс {proc.ProcessName} успешно завершен.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось завершить процесс: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnKillAllLocksClick(object sender, RoutedEventArgs e)
    {
        if (_lockingProcesses.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Завершить ВСЕ ({_lockingProcesses.Count}) блокирующие процессы?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        int killed = 0;
        foreach (var proc in _lockingProcesses.ToList())
        {
            try
            {
                using var p = Process.GetProcessById(proc.ProcessId);
                p.Kill();
                _lockingProcesses.Remove(proc);
                killed++;
            }
            catch { }
        }
        TxtLocksmithDetails.Text = $"Завершено процессов: {killed}.";
    }

    #endregion

    #region 2. Keep-Awake

    private async Task RefreshAwakeStatusAsync()
    {
        try
        {
            var status = await _pipeClient.GetAwakeStateAsync();
            UpdateAwakeUi(status);
        }
        catch { }
    }

    private void UpdateAwakeUi(AwakeStatusDto status)
    {
        if (status.IsActive)
        {
            TxtAwakeBadge.Text = status.RemainingMinutes > 0
                ? $"Активен (осталось {status.RemainingMinutes} мин)"
                : "Активен (бессрочно)";
            BadgeAwakeState.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtAwakeBadge.Foreground = Brushes.White;
            IconAwake.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber Sun
            TxtAwakeDescription.Text = status.KeepDisplayOn
                ? "Спящий режим системы и выключение экрана заблокированы."
                : "Спящий режим системы заблокирован (экран может выключаться).";
        }
        else
        {
            TxtAwakeBadge.Text = "Отключен";
            BadgeAwakeState.Background = (Brush)FindResource("CardBackgroundFillColorSecondaryBrush");
            TxtAwakeBadge.Foreground = (Brush)FindResource("TextFillColorSecondaryBrush");
            IconAwake.Foreground = (Brush)FindResource("TextFillColorSecondaryBrush");
            TxtAwakeDescription.Text = "ПК переходит в спящий режим по стандартному плану электропитания Windows.";
        }
    }

    private async void OnAwakeIndefiniteClick(object sender, RoutedEventArgs e) =>
        await SetAwakeModeAsync(true, 0);

    private async void OnAwake30MinClick(object sender, RoutedEventArgs e) =>
        await SetAwakeModeAsync(true, 30);

    private async void OnAwake1HourClick(object sender, RoutedEventArgs e) =>
        await SetAwakeModeAsync(true, 60);

    private async void OnAwake2HoursClick(object sender, RoutedEventArgs e) =>
        await SetAwakeModeAsync(true, 120);

    private async void OnAwakeDeactivateClick(object sender, RoutedEventArgs e) =>
        await SetAwakeModeAsync(false, 0);

    private async Task SetAwakeModeAsync(bool enable, int minutes)
    {
        bool keepDisplay = ChkKeepDisplayOn.IsChecked ?? true;
        var status = await _pipeClient.SetAwakeStateAsync(enable, minutes, keepDisplay);
        UpdateAwakeUi(status);
    }

    #endregion

    #region 3. Junk Cleaner

    private async void OnScanJunkClick(object sender, RoutedEventArgs e)
    {
        BtnScanJunk.IsEnabled = false;
        BtnCleanJunk.IsEnabled = false;
        TxtJunkScanSummary.Text = "Сканирование дискового мусора...";
        _junkCategories.Clear();

        try
        {
            _junkCts?.Cancel();
            _junkCts = new CancellationTokenSource();
            var result = await _pipeClient.ScanJunkFilesAsync(_junkCts.Token);

            foreach (var cat in result.Categories)
            {
                _junkCategories.Add(cat);
            }

            TxtJunkScanSummary.Text = $"Найдено дискового мусора: {FormatBytes(result.TotalSizeBytes)} ({result.TotalFilesCount:N0} файлов).";
            TxtCleanerResultStatus.Text = "Сканирование завершено. Выберите категории для очистки.";
        }
        catch (Exception ex)
        {
            TxtJunkScanSummary.Text = $"Ошибка сканирования: {ex.Message}";
        }
        finally
        {
            BtnScanJunk.IsEnabled = true;
            BtnCleanJunk.IsEnabled = true;
        }
    }

    private async void OnCleanJunkClick(object sender, RoutedEventArgs e)
    {
        var selected = _junkCategories.Where(c => c.IsSelected).Select(c => c.Id).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Выберите хотя бы одну категорию для очистки.", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            "Выполнить очистку выбранных временных файлов?",
            "Очистка диска",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        BtnCleanJunk.IsEnabled = false;
        TxtCleanerResultStatus.Text = "Выполняется безопасная очистка...";

        try
        {
            _junkCts?.Cancel();
            _junkCts = new CancellationTokenSource();
            var result = await _pipeClient.CleanJunkFilesAsync(selected, _junkCts.Token);

            TxtCleanerResultStatus.Text = $"Очистка завершена! Освобождено: {FormatBytes(result.FreedSizeBytes)}, удалено файлов: {result.DeletedFilesCount:N0} (пропущено заблокированных: {result.ErrorsCount}).";
            // Автоматически обновляем сканирование
            OnScanJunkClick(sender, e);
        }
        catch (Exception ex)
        {
            TxtCleanerResultStatus.Text = $"Ошибка при очистке: {ex.Message}";
        }
        finally
        {
            BtnCleanJunk.IsEnabled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{(double)bytes / (1024 * 1024 * 1024):F2} ГБ";
        if (bytes >= 1024 * 1024)
            return $"{(double)bytes / (1024 * 1024):F1} МБ";
        if (bytes >= 1024)
            return $"{(double)bytes / 1024:F0} КБ";
        return $"{bytes} Б";
    }

    #endregion

    #region 4. LAN Scanner

    private async void OnScanLanClick(object sender, RoutedEventArgs e)
    {
        BtnScanLan.IsEnabled = false;
        TxtLanScannerStatus.Text = "Выполняется параллельное сканирование локальной сети (ARP/Ping)...";
        _lanDevices.Clear();

        try
        {
            _lanScanCts?.Cancel();
            _lanScanCts = new CancellationTokenSource();
            var devices = await _pipeClient.ScanLanDevicesAsync(_lanScanCts.Token);

            foreach (var d in devices)
            {
                _lanDevices.Add(d);
            }

            TxtLanDevicesCount.Text = $"Устройств обнаружено: {devices.Count}";
            TxtLanScannerStatus.Text = "Сканирование сети успешно завершено.";
        }
        catch (Exception ex)
        {
            TxtLanScannerStatus.Text = $"Ошибка сканирования сети: {ex.Message}";
        }
        finally
        {
            BtnScanLan.IsEnabled = true;
        }
    }

    #endregion

    #region 5. Hash & Password Security

    private async void OnBrowseHashFileClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите файл для расчета хешей",
            Filter = "Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
        {
            var path = dlg.FileName;
            TxtHashFilePath.Text = path;
            await CalculateHashesAsync(path);
        }
    }

    private async Task CalculateHashesAsync(string path)
    {
        TxtHashSha256.Text = "Вычисление...";
        TxtHashSha1.Text = "Вычисление...";
        TxtHashMd5.Text = "Вычисление...";

        try
        {
            var sha256 = await Task.Run(() => HashAndSecurityHelper.ComputeFileSha256(path));
            var sha1 = await Task.Run(() => HashAndSecurityHelper.ComputeFileSha1(path));
            var md5 = await Task.Run(() => HashAndSecurityHelper.ComputeFileMd5(path));

            TxtHashSha256.Text = sha256;
            TxtHashSha1.Text = sha1;
            TxtHashMd5.Text = md5;

            ValidateHashMatch();
        }
        catch (Exception ex)
        {
            TxtHashSha256.Text = $"Ошибка: {ex.Message}";
        }
    }

    private void OnCopySha256Click(object sender, RoutedEventArgs e) =>
        SafeSetClipboard(TxtHashSha256.Text);

    private void OnCopySha1Click(object sender, RoutedEventArgs e) =>
        SafeSetClipboard(TxtHashSha1.Text);

    private void OnCopyMd5Click(object sender, RoutedEventArgs e) =>
        SafeSetClipboard(TxtHashMd5.Text);

    private void OnHashCompareChanged(object sender, TextChangedEventArgs e) =>
        ValidateHashMatch();

    private void ValidateHashMatch()
    {
        var compare = TxtHashCompare.Text?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(compare))
        {
            BadgeHashMatch.Visibility = Visibility.Collapsed;
            return;
        }

        bool match = compare == TxtHashSha256.Text?.Trim().ToLowerInvariant()
                  || compare == TxtHashSha1.Text?.Trim().ToLowerInvariant()
                  || compare == TxtHashMd5.Text?.Trim().ToLowerInvariant();

        BadgeHashMatch.Visibility = Visibility.Visible;
        if (match)
        {
            BadgeHashMatch.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtHashMatchStatus.Text = "✓ Хеш полностью совпадает!";
            TxtHashMatchStatus.Foreground = Brushes.White;
        }
        else
        {
            BadgeHashMatch.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtHashMatchStatus.Text = "✗ Хеш НЕ совпадает";
            TxtHashMatchStatus.Foreground = Brushes.White;
        }
    }

    private void OnPasswordLengthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtPasswordLengthVal != null)
        {
            TxtPasswordLengthVal.Text = $"{(int)SliderPasswordLength.Value} символов";
        }
    }

    private void OnGeneratePasswordClick(object sender, RoutedEventArgs e)
    {
        int length = (int)SliderPasswordLength.Value;
        bool useUpper = ChkUpper.IsChecked ?? true;
        bool useDigits = ChkDigits.IsChecked ?? true;
        bool useSymbols = ChkSymbols.IsChecked ?? true;

        string pass = HashAndSecurityHelper.GeneratePassword(length, useUpper, useDigits, useSymbols);
        TxtGeneratedPassword.Text = pass;

        var (score, text) = HashAndSecurityHelper.EvaluatePasswordStrength(pass);
        TxtPasswordStrength.Text = $"Стойкость: {text} ({score} / 100)";
        BadgePasswordStrength.Background = score >= 70
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
            : new SolidColorBrush(Color.FromRgb(245, 158, 11));
        TxtPasswordStrength.Foreground = Brushes.White;
    }

    private void OnCopyPasswordClick(object sender, RoutedEventArgs e) =>
        SafeSetClipboard(TxtGeneratedPassword.Text);

    private static void SafeSetClipboard(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch { }
        }
    }

    #endregion
}
