using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Models;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.Views;
using Wpf.Ui.Appearance;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверки новой компоновки используют общий изолированный STA-host и настоящее визуальное дерево.</summary>
public sealed partial class NetworkRenderingRegressionTests
{
    /// <summary>Полный выбранный вариант политики помещается в свою ячейку при разных ширинах страницы.</summary>
    /// <param name="width">Доступная ширина содержимого страницы.</param>
    /// <param name="state">Исходная подтверждённая политика.</param>
    [Theory]
    [InlineData(760, AccessControlState.Allowed)]
    [InlineData(760, AccessControlState.Prompt)]
    [InlineData(760, AccessControlState.Blocked)]
    [InlineData(1040, AccessControlState.Blocked)]
    public void AccessPolicyPicker_FitsCellAndContainsAllChoices(double width, AccessControlState state)
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var view = new NetworkSecurityHubView(client);
            var model = CreateApplication("Browser.exe", state);
            try
            {
                var table = (DataGrid)view.FindName("GridAccessMatrix");
                table.ItemsSource = new[] { model };
                Layout(view, width, 760);
                var picker = Descendants<ComboBox>(table).Single(control => control.Name == "AccessPolicyPicker");
                Assert.Equal(3, picker.Items.Count);
                Assert.Equal(state, picker.SelectedValue);
                Assert.Equal(state, model.State);
                Assert.InRange(picker.ActualWidth, 150, 180);
                var cell = Ancestor<DataGridCell>(picker);
                var bounds = picker.TransformToAncestor(cell).TransformBounds(new Rect(picker.RenderSize));
                Assert.True(bounds.Left >= -0.1 && bounds.Right <= cell.ActualWidth + 0.1);
                foreach (var option in picker.Items.Cast<ComboBoxItem>())
                {
                    Assert.False(string.IsNullOrWhiteSpace(option.Content?.ToString()));
                    var label = new TextBlock { Text = option.Content?.ToString(), FontSize = picker.FontSize };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.True(label.DesiredSize.Width + 30 <= picker.ActualWidth,
                        $"Полный вариант {option.Content} не помещается в списке шириной {picker.ActualWidth}.");
                }
            }
            finally { view.Deactivate(); }
        });

    /// <summary>Критические процессы сохраняют доступность Allow/Prompt, но вариант Block отключён.</summary>
    [Fact]
    public void CriticalApplication_DisablesOnlyBlockedChoice()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var view = new NetworkSecurityHubView(client);
            var model = CreateApplication("svchost.exe", AccessControlState.Allowed, ProcessProtectionLevel.Critical);
            try
            {
                var table = (DataGrid)view.FindName("GridAccessMatrix");
                table.ItemsSource = new[] { model };
                Layout(view, 850, 760);
                var picker = Descendants<ComboBox>(table).Single(control => control.Name == "AccessPolicyPicker");
                foreach (var option in picker.Items.Cast<ComboBoxItem>())
                    Assert.Equal((AccessControlState)option.Tag != AccessControlState.Blocked, option.IsEnabled);
            }
            finally { view.Deactivate(); }
        });

    /// <summary>Отказ от защищённой блокировки сохраняет Allowed и не вызывает транспорт.</summary>
    [Fact]
    public async Task CancelledProtectedBlock_KeepsConfirmedAllowedState()
    {
        var item = CreateApplication("explorer.exe", AccessControlState.Allowed, ProcessProtectionLevel.HighRisk);
        var sent = false;
        var result = await NetworkSecurityHubView.ApplyMatrixPolicyAsync(item, AccessControlState.Blocked,
            () => false, (_, _) => { sent = true; return Task.FromResult(FirewallOperationResult.Ok("saved")); });
        Assert.Null(result);
        Assert.False(sent);
        Assert.Equal(AccessControlState.Allowed, item.State);
    }

    /// <summary>Неуспешный ответ не меняет состояние; успешный обновляет его только после завершения IPC.</summary>
    [Fact]
    public async Task PolicyChange_WaitsForServiceAndCommitsOnlySuccess()
    {
        var item = CreateApplication("Browser.exe", AccessControlState.Allowed);
        var completion = new TaskCompletionSource<FirewallOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetAppAccessStateRequest? sent = null;
        var pending = NetworkSecurityHubView.ApplyMatrixPolicyAsync(item, AccessControlState.Prompt,
            () => true, (request, _) => { sent = request; return completion.Task; });
        Assert.Equal(AccessControlState.Allowed, item.State);
        completion.SetResult(FirewallOperationResult.Fail("denied"));
        Assert.False((await pending)?.Success);
        Assert.Equal(AccessControlState.Allowed, item.State);
        var success = await NetworkSecurityHubView.ApplyMatrixPolicyAsync(item, AccessControlState.Prompt,
            () => true, (_, _) => Task.FromResult(FirewallOperationResult.Ok("saved")));
        Assert.True(success?.Success);
        Assert.Equal(AccessControlState.Prompt, item.State);
        Assert.NotNull(sent);
        Assert.Equal(item.FullPath, sent.ApplicationPath);
        Assert.Equal(AccessRuleScope.Permanent, sent.Scope);
    }

    /// <summary>Меню сохраняет стабильные идентификаторы страниц при новом визуальном порядке.</summary>
    [Fact]
    public void SidebarNavigation_UsesTaskOrderWithoutChangingSavedPageIds()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "StartupMonitor.UI", "Views", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var navigation = document.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "SidebarNavigation");
        var ids = navigation.Elements().Where(element => element.Attribute("Tag") is not null)
            .Select(element => (string?)element.Attribute("Tag")).ToArray();
        Assert.Equal(new[] { "0", "6", "8", "7", "9", "4", "1", "3", "5" }, ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    /// <summary>Сохраняет изображения реальной страницы для визуальной проверки тёмной и светлой темы.</summary>
    [Fact]
    public void FirewallWorkspace_RendersBothThemesAndAllTabsWithoutExtraTimers()
        => _host.Run(() =>
        {
            var previousCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            try
            {
                foreach (var theme in new[] { ApplicationTheme.Dark, ApplicationTheme.Light })
                {
                    ThemeService.ApplyTheme(theme, force: true);
                    using var client = new PipeClient();
                    var view = new NetworkSecurityHubView(client);
                    try
                    {
                        var tabs = (TabControl)view.FindName("MainTabs");
                        Assert.Equal(1, tabs.SelectedIndex);
                        var table = (DataGrid)view.FindName("GridAccessMatrix");
                        table.ItemsSource = new[]
                        {
                            CreateApplication("Browser.exe", AccessControlState.Allowed),
                            CreateApplication("Updater.exe", AccessControlState.Prompt),
                            CreateApplication("RemoteTool.exe", AccessControlState.Blocked),
                            CreateApplication("svchost.exe", AccessControlState.Allowed, ProcessProtectionLevel.Critical)
                        };
                        Layout(view, 1000, 760);
                        SavePreview(view, "firewall-" + theme.ToString().ToLowerInvariant() + ".png");
                        var nav = (WrapPanel)view.FindName("HubNavigation");
                        foreach (FrameworkElement button in nav.Children)
                        {
                            var bounds = button.TransformToAncestor(nav).TransformBounds(new Rect(button.RenderSize));
                            Assert.True(bounds.Right <= nav.ActualWidth + 0.1);
                        }
                        foreach (var index in new[] { 0, 2, 3, 4, 5 })
                        {
                            tabs.SelectedIndex = index;
                            Layout(view, 760, 760);
                            Assert.True(tabs.ActualHeight > 200, "Заголовки и панели не должны поглощать рабочую область.");
                        }
                    }
                    finally { view.Deactivate(); }
                }
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousCulture;
                ThemeService.ApplyTheme(ApplicationTheme.Dark, force: true);
            }
        });

    /// <summary>Создаёт тестовую строку без запросов службы или изменения firewall.</summary>
    /// <param name="name">Имя тестового приложения.</param>
    /// <param name="state">Подтверждённая политика.</param>
    /// <param name="protection">Уровень защиты приложения.</param>
    /// <returns>Независимая модель строки.</returns>
    private static AppAccessMatrixItemViewModel CreateApplication(string name, AccessControlState state,
        ProcessProtectionLevel protection = ProcessProtectionLevel.None) => new(new AppAccessMatrixItemDto
        {
            ProcessName = name, FullPath = @"C:\Apps\" + name, State = state,
            ProtectionLevel = protection, Scope = AccessRuleScope.Permanent, ActiveSocketsCount = 3
        });

    /// <summary>Выполняет настоящую WPF-компоновку в заданной области.</summary>
    /// <param name="element">Корневой элемент страницы.</param>
    /// <param name="width">Ширина в DIP.</param>
    /// <param name="height">Высота в DIP.</param>
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        // DataGrid рассчитывает ширины звёздочных колонок отложенно с приоритетом Render.
        element.Dispatcher.Invoke(() => { element.UpdateLayout(); }, System.Windows.Threading.DispatcherPriority.Loaded);
        element.UpdateLayout();
    }

    /// <summary>Перечисляет реальные дочерние элементы визуального дерева.</summary>
    /// <typeparam name="T">Искомый тип.</typeparam>
    /// <param name="parent">Родительский визуальный элемент.</param>
    /// <returns>Элементы указанного типа.</returns>
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    /// <summary>Находит контейнер ячейки для проверки реальных границ списка политики.</summary>
    /// <typeparam name="T">Тип предка.</typeparam>
    /// <param name="child">Проверяемый элемент.</param>
    /// <returns>Найденный предок.</returns>
    private static T Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T match) return match;
        throw new InvalidOperationException("Контейнер ячейки не найден.");
    }

    /// <summary>Возвращает корень checkout независимо от каталога запуска testhost.</summary>
    /// <returns>Каталог с решением.</returns>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "StartupMonitor.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Корень StartupMonitor не найден.");
    }

    /// <summary>Сохраняет тестовый render, не показывая окно пользователю и не запуская production lifecycle.</summary>
    /// <param name="view">Уже скомпонованная страница.</param>
    /// <param name="filename">Имя PNG в изолированном каталоге результатов.</param>
    private static void SavePreview(FrameworkElement view, string filename)
    {
        var directory = Path.Combine(RepositoryRoot(), "TestResults", "FirewallRedesign");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, filename));
        encoder.Save(stream);
    }
}
