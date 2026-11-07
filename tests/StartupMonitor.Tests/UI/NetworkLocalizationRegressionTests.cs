using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Controls;
using StartupMonitor.UI.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.Views;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет смену языка в настоящих открытых контролах без подключения к рабочей службе.</summary>
public sealed partial class NetworkRenderingRegressionTests
{
    /// <summary>Перевод истории не меняет смысл выбранного вердикта и протокола.</summary>
    [Fact]
    public void HistoryLanguageSwitch_UpdatesLabelsAndPreservesFilters()
        => _host.Run(() =>
        {
            var loc = LocalizationService.Instance;
            var previousCulture = CultureInfo.CurrentUICulture.Name;
            using var client = new PipeClient();
            var view = new NetworkSecurityHubView(client);
            try
            {
                var history = new List<NetworkConnectionHistoryItem>
                {
                    new() { ProcessName = "allowed.exe", Protocol = "TCP", Verdict = NetworkConnectionVerdict.Allowed },
                    new() { ProcessName = "blocked.exe", Protocol = "TCP", Verdict = NetworkConnectionVerdict.Blocked },
                    new() { ProcessName = "blocked-udp.exe", Protocol = "UDP", Verdict = NetworkConnectionVerdict.BlockSession }
                };
                var field = typeof(NetworkSecurityHubView).GetField("_hubHistoryAll", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException("История страницы не найдена.");
                field.SetValue(view, history);
                ((ComboBox)view.FindName("CmbHubHistVerdict")).SelectedIndex = 3;
                ((ComboBox)view.FindName("CmbHubHistProtocol")).SelectedIndex = 2;
                foreach (var culture in new[] { "ru", "en", "ru" })
                {
                    loc.ChangeLanguage(culture);
                    Layout(view, 1000, 760);
                    var table = (DataGrid)view.FindName("GridHubHistory");
                    Assert.Equal("blocked-udp.exe", Assert.IsType<NetworkConnectionHistoryItem>(Assert.Single(table.Items.Cast<object>())).ProcessName);
                    Assert.Equal(culture == "en" ? "Time" : "Время", table.Columns[0].Header);
                    Assert.Equal(culture == "en" ? "Refresh" : "Обновить", ((ContentControl)view.FindName("BtnRefreshHubHist")).Content);
                    Assert.Equal(culture == "en" ? "Add port" : "Добавить порт", ((ContentControl)view.FindName("BtnAddDefenderPort")).Content);
                    Assert.Equal(culture == "en" ? "Port Defender: no data" : "Port Defender: нет данных", ((TextBlock)view.FindName("TxtDefenderState")).Text);
                    Assert.Equal(loc.Format("NetHistoryCountFormat", 1, 3), ((TextBlock)view.FindName("TxtHubHistSummary")).Text);
                }
            }
            finally { view.Deactivate(); loc.ChangeLanguage(previousCulture); }
        });

    /// <summary>Открытый редактор переводит заголовок, описание и ошибку, сохраняя введённое значение.</summary>
    [Fact]
    public void DefenderDialogs_LanguageSwitchPreservesInputAndValidation()
        => _host.Run(() =>
        {
            var loc = LocalizationService.Instance;
            var previousCulture = CultureInfo.CurrentUICulture.Name;
            using var client = new PipeClient();
            var editor = new PortDefenderPortEditWindow(existingPorts: new[] { 3389 });
            var config = new PortDefenderConfigWindow(client);
            var row = new PortDefenderPortItem(3389);
            try
            {
                var input = (TextBox)editor.FindName("TxtPort");
                input.Text = "3389";
                ((Button)editor.FindName("BtnSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var culture in new[] { "ru", "en", "ru" })
                {
                    loc.ChangeLanguage(culture);
                    Layout((FrameworkElement)editor.Content, 480, 460);
                    Layout((FrameworkElement)config.Content, 620, 720);
                    Assert.Equal("3389", input.Text);
                    Assert.Equal(culture == "en" ? "Add protected port" : "Добавление защищаемого порта", editor.Title);
                    Assert.Equal(loc.Format("DefenderDuplicatePortFormat", 3389), ((TextBlock)editor.FindName("TxtError")).Text);
                    Assert.Equal(culture == "en" ? "Critical" : "Критический", row.RiskBadge);
                    Assert.Equal(culture == "en" ? "Microsoft Windows Remote Desktop" : "Удалённый рабочий стол Microsoft Windows", row.Description);
                    Assert.Equal(culture == "en" ? "Add port" : "Добавить порт", ((ContentControl)config.FindName("BtnAddPort")).Content);
                    Assert.Equal(culture == "en" ? "Risk level" : "Уровень риска", ((DataGrid)config.FindName("GridProtectedPorts")).Columns[2].Header);
                }
            }
            finally { editor.Close(); config.Close(); loc.ChangeLanguage(previousCulture); }
        });

    /// <summary>Подсказка графика меняет язык без очередного сетевого снимка и движения мыши.</summary>
    [Fact]
    public void ChartLanguageSwitch_InvalidatesHoveredTooltipImmediately()
        => _host.Run(() =>
        {
            var loc = LocalizationService.Instance;
            var previousCulture = CultureInfo.CurrentUICulture.Name;
            var chart = new NetworkTimelineChartControl();
            var point = new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, 100, 200, 0, 0);
            chart.SetDataPoints(new[] { point, point });
            chart.Measure(new Size(700, 80));
            chart.Arrange(new Rect(0, 0, 700, 80));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(NetworkTimelineChartControl).GetField("_hoveredPoint", flags)?.SetValue(chart, point);
            typeof(NetworkTimelineChartControl).GetField("_cursorPosition", flags)?.SetValue(chart, new Point(20, 20));
            var tooltip = typeof(NetworkTimelineChartControl).GetField("_cachedTooltip", flags)
                ?? throw new MissingFieldException("Кэш подсказки не найден.");
            try
            {
                foreach (var culture in new[] { "ru", "en", "ru" })
                {
                    loc.ChangeLanguage(culture);
                    Assert.Null(tooltip.GetValue(chart));
                    Render(chart);
                    Assert.Contains(culture == "en" ? "B/s" : "Б/с", Assert.IsType<FormattedText>(tooltip.GetValue(chart)).Text);
                }
            }
            finally { loc.ChangeLanguage(previousCulture); }
        });

    /// <summary>Диалог удаляет правила исключённых портов и допускает явно пустой список, сохраняя параметры соседнего правила.</summary>
    [Fact]
    public void DefenderConfigDialog_ReconcilesRulesAndAllowsRemovingLastPort()
    {
        var retained = new PortDefenderRuleConfig { LocalPort = 9090, IsEnabled = false, ThresholdCount = 27 };
        var config = new PortDefenderConfigDto
        {
            ProtectedPorts = [8080, 9090],
            Rules = [new() { LocalPort = 8080, ProcessPath = @"C:\app.exe" }, retained]
        };
        PortDefenderConfigWindow.SynchronizeProtectedPorts(config, new[] { 9090 });
        Assert.Same(retained, Assert.Single(config.Rules));
        Assert.False(retained.IsEnabled);
        Assert.Equal(27, retained.ThresholdCount);
        PortDefenderConfigWindow.SynchronizeProtectedPorts(config, Array.Empty<int>());
        Assert.Empty(config.ProtectedPorts);
        Assert.Empty(config.Rules);
    }
}
