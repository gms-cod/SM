using System.Windows;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using Wpf.Ui.Controls;

namespace StartupMonitor.UI.Views.Dialogs;

public partial class UpdateAvailableWindow : FluentWindow
{
    private readonly UpdateInfo _updateInfo;
    private readonly PipeClient _pipeClient;
    private readonly Action<string>? _onVersionSkipped;
    private bool _isReadyToInstall;

    public UpdateAvailableWindow(UpdateInfo updateInfo, PipeClient pipeClient, Action<string>? onVersionSkipped = null)
    {
        InitializeComponent();
        _updateInfo = updateInfo;
        _pipeClient = pipeClient;
        _onVersionSkipped = onVersionSkipped;

        PopulateData();

        _pipeClient.UpdateProgressReceived += OnUpdateProgressReceived;
        Closed += (_, _) => _pipeClient.UpdateProgressReceived -= OnUpdateProgressReceived;
    }

    private void PopulateData()
    {
        TxtNewVersion.Text = $"Startup Monitor v{_updateInfo.Version}";
        var dateStr = _updateInfo.ReleaseDate.ToString("dd.MM.yyyy");
        var currentVer = string.IsNullOrEmpty(_updateInfo.CurrentVersion) ? "1.3.1" : _updateInfo.CurrentVersion;
        TxtSubtitle.Text = $"Опубликовано: {dateStr} • Текущая версия: v{currentVer}";

        TxtChangelog.Text = string.IsNullOrWhiteSpace(_updateInfo.Changelog)
            ? "Информация об изменениях не предоставлена автором релиза."
            : _updateInfo.Changelog;
    }

    private void OnUpdateProgressReceived(object? sender, UpdateProgressInfo progress)
    {
        Dispatcher.Invoke(() =>
        {
            DownloadProgressPanel.Visibility = Visibility.Visible;
            ProgressBarDownload.Value = progress.Percent;
            TxtProgressPercent.Text = $"{progress.Percent}%";

            if (progress.Status == "Downloading")
            {
                var dlMb = (progress.BytesDownloaded / 1048576.0).ToString("0.0");
                var totMb = (progress.TotalBytes / 1048576.0).ToString("0.0");
                var speedMb = (progress.SpeedBytesPerSec / 1048576.0).ToString("0.0");
                TxtProgressStatus.Text = $"Загрузка: {dlMb} МБ из {totMb} МБ ({speedMb} МБ/с)...";
            }
            else if (progress.Status == "Verifying")
            {
                TxtProgressStatus.Text = "Проверка контрольной суммы SHA-256...";
            }
            else if (progress.Status == "Ready")
            {
                _isReadyToInstall = true;
                TxtProgressStatus.Text = "Обновление готово к установке!";
                BtnUpdateAction.IsEnabled = true;
                BtnUpdateAction.Content = LocalizationService.Instance.InstallAndRestartButton;
            }
            else if (progress.Status == "Failed")
            {
                TxtProgressStatus.Text = $"Ошибка: {progress.ErrorMessage ?? "Не удалось скачать обновление."}";
                BtnUpdateAction.IsEnabled = true;
                BtnUpdateAction.Content = LocalizationService.Instance.UpdateNowButton;
            }
        });
    }

    private async void OnUpdateActionClick(object sender, RoutedEventArgs e)
    {
        if (_isReadyToInstall)
        {
            BtnUpdateAction.IsEnabled = false;
            var (ok, msg) = await _pipeClient.InstallUpdateAsync();
            if (!ok)
            {
                System.Windows.MessageBox.Show(this, msg, "Startup Monitor", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                BtnUpdateAction.IsEnabled = true;
                return;
            }

            Close();
            return;
        }

        // Запуск скачивания
        BtnUpdateAction.IsEnabled = false;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        TxtProgressStatus.Text = "Подключение к серверу GitHub...";
        ProgressBarDownload.Value = 0;
        TxtProgressPercent.Text = "0%";

        var started = await _pipeClient.DownloadUpdateAsync();
        if (!started)
        {
            TxtProgressStatus.Text = "Не удалось запустить процесс загрузки.";
            BtnUpdateAction.IsEnabled = true;
        }
    }

    private void OnRemindLaterClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSkipVersionClick(object sender, RoutedEventArgs e)
    {
        _onVersionSkipped?.Invoke(_updateInfo.Version);
        Close();
    }
}
