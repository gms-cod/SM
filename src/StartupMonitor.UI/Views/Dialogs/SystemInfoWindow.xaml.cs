using System.Text;
using System.Windows;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Services;
using Wpf.Ui.Controls;

namespace StartupMonitor.UI.Views.Dialogs;

/// <summary>
/// Логика взаимодействия для диалогового окна SystemInfoWindow.
/// </summary>
public partial class SystemInfoWindow : FluentWindow
{
    public static readonly DependencyProperty InfoProperty =
        DependencyProperty.Register(nameof(Info), typeof(SystemComputerInfo), typeof(SystemInfoWindow),
            new PropertyMetadata(null));

    public SystemComputerInfo Info
    {
        get => (SystemComputerInfo)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    public SystemInfoWindow(SystemComputerInfo? initialInfo = null)
    {
        Info = initialInfo ?? SystemComputerInfo.Collect();
        InitializeComponent();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        Info = SystemComputerInfo.Collect();
        TxtStatus.Text = $"{LocalizationService.Instance.SysInfoTitle} обновлена ({DateTime.Now:HH:mm:ss})";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Info.ToReportString());
            TxtStatus.Text = LocalizationService.Instance.SysInfoCopiedToast;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка копирования: {ex.Message}";
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

