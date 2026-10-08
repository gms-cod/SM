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
            /// <summary>PropertyMetadata: Выполняет операцию PropertyMetadata.</summary>
            /// <param name="null)">Входные данные: значение параметра null) ().</param>
            new PropertyMetadata(null));

    public SystemComputerInfo Info
    {
        get => (SystemComputerInfo)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    /// <summary>SystemInfoWindow: Выполняет операцию SystemInfoWindow.</summary>
    /// <param name="initialInfo">Входные данные: значение параметра initialInfo (SystemComputerInfo?).</param>
    public SystemInfoWindow(SystemComputerInfo? initialInfo = null)
    {
        Info = initialInfo ?? SystemComputerInfo.Collect();
        InitializeComponent();
    }

    /// <summary>OnRefreshClick: Обрабатывает событие OnRefreshClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        Info = SystemComputerInfo.Collect();
        TxtStatus.Text = $"{LocalizationService.Instance.SysInfoTitle} обновлена ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>OnCopyClick: Обрабатывает событие OnCopyClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
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

    /// <summary>OnCloseClick: Обрабатывает событие OnCloseClick.</summary>
    /// <param name="sender">Входные данные: значение параметра sender (object).</param>
    /// <param name="e">Входные данные: значение параметра e (RoutedEventArgs).</param>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

