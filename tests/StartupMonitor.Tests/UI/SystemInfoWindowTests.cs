using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.Tests.UI;

/// <summary>
/// Проверяет создание, парсинг XAML и визуализацию диалогового окна системных сведений SystemInfoWindow.
/// </summary>
[Collection("WpfNetworkRendering")]
public sealed class SystemInfoWindowTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Инициализирует тестовый класс с общим хостом отрисовки WPF.</summary>
    /// <param name="host">Хост для запуска окон в STA-потоке.</param>
    public SystemInfoWindowTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>
    /// Проверяет, что окно сведений о компьютере корректно загружается, парсит XAML без исключений привязки
    /// и отображает все расширенные аппаратные характеристики (модель, плата, BIOS, папка Windows).
    /// </summary>
    [Fact]
    public void SystemInfoWindow_InitializesAndRenders_WithoutBindingExceptions() => _host.Run(() =>
    {
        var info = new SystemComputerInfo
        {
            MachineName = "DESKTOP-TEST",
            UserName = "TestAdmin",
            OsName = "Windows 11 Pro",
            OsDisplayVersion = "24H2",
            OsBuild = "26100.2033",
            Architecture = "x64",
            CpuName = "AMD Ryzen 9 7950X 16-Core Processor",
            LogicalCores = 32,
            RamTotalGb = 64.0,
            RamAvailableGb = 42.5,
            RamUsedGb = 21.5,
            RamUsedPercent = 34,
            SystemUptime = TimeSpan.FromHours(50),
            ComputerModel = "ASUS ROG Crosshair X670E",
            Motherboard = "ASUSTeK COMPUTER INC. ROG CROSSHAIR X670E HERO",
            BiosVersion = "2401",
            BiosReleaseDate = "09/18/2026",
            WindowsDirectory = @"C:\Windows",
            Disks = new List<SystemDiskInfo>
            {
                new() { Name = "C:", Label = "System", Format = "NTFS", TotalGb = 1000, FreeGb = 600, UsedGb = 400, UsedPercent = 40 },
                new() { Name = "D:", Label = "Data", Format = "NTFS", TotalGb = 2000, FreeGb = 1200, UsedGb = 800, UsedPercent = 40 }
            }
        };

        var window = new SystemInfoWindow(info);
        Assert.NotNull(window);
        Assert.Equal(info, window.Info);

        // Проверяем отображение окна и жизненный цикл в visual tree
        window.Show();
        Assert.True(window.IsVisible);

        // Прокачиваем очередь Dispatcher, чтобы все операции отрисовки (включая TitleBar) завершились до закрытия
        PumpDispatcher();

        // Проверяем, что в дереве присутствуют TextBlock с расширенной информацией
        var textBlocks = FindVisualChildren<TextBlock>(window).ToList();
        Assert.NotEmpty(textBlocks);

        // Проверяем наличие ключевых строк модели и BIOS в отрендеренных блоках
        var allTexts = string.Join(" ", textBlocks.Select(t => t.Text));
        Assert.Contains("DESKTOP-TEST", allTexts);
        Assert.Contains("AMD Ryzen 9 7950X", allTexts);
        Assert.Contains("ASUS ROG Crosshair X670E", allTexts);
        Assert.Contains("ASUSTeK COMPUTER INC.", allTexts);
        Assert.Contains("2401", allTexts);
        Assert.Contains("09/18/2026", allTexts);
        Assert.Contains(@"C:\Windows", allTexts);

        window.Close();
        PumpDispatcher();
    });

    /// <summary>
    /// Проверяет, что конструктор по умолчанию без явных параметров автоматически собирает сведения и не падает.
    /// </summary>
    [Fact]
    public void SystemInfoWindow_DefaultConstructor_CollectsAndInitializesSuccessfully() => _host.Run(() =>
    {
        var window = new SystemInfoWindow(null);
        Assert.NotNull(window);
        Assert.NotNull(window.Info);
        Assert.False(string.IsNullOrWhiteSpace(window.Info.MachineName));
        Assert.False(string.IsNullOrWhiteSpace(window.Info.OsName));
        Assert.True(window.Info.LogicalCores > 0);
        Assert.True(window.Info.RamTotalGb > 0);
    });

    /// <summary>
    /// Проверяет, что структурированный текстовый отчёт ToReportString включает все расширенные поля.
    /// </summary>
    [Fact]
    public void SystemComputerInfo_ToReportString_IncludesAllExpandedHardwareFields()
    {
        var info = new SystemComputerInfo
        {
            MachineName = "DESKTOP-PC",
            UserName = "User",
            OsName = "Windows 11",
            OsDisplayVersion = "24H2",
            OsBuild = "26100",
            Architecture = "x64",
            CpuName = "Intel Core i9-14900K",
            LogicalCores = 32,
            RamTotalGb = 64.0,
            RamAvailableGb = 32.0,
            RamUsedGb = 32.0,
            RamUsedPercent = 50,
            SystemUptime = TimeSpan.FromHours(5),
            ComputerModel = "Custom Gaming PC",
            Motherboard = "MSI MAG Z790",
            BiosVersion = "A.20",
            BiosReleaseDate = "05/10/2026",
            WindowsDirectory = @"C:\Windows"
        };

        var report = info.ToReportString();
        Assert.Contains("Custom Gaming PC", report);
        Assert.Contains("MSI MAG Z790", report);
        Assert.Contains("A.20", report);
        Assert.Contains("05/10/2026", report);
        Assert.Contains(@"C:\Windows", report);
    }

    /// <summary>
    /// Прокачивает очередь Dispatcher до состояния ApplicationIdle.
    /// </summary>
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Рекурсивно находит всех визуальных потомков заданного типа.
    /// </summary>
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t)
            {
                yield return t;
            }
            foreach (var childOfChild in FindVisualChildren<T>(child))
            {
                yield return childOfChild;
            }
        }
    }
}
