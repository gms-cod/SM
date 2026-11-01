using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Controls;
using StartupMonitor.UI.Services;
using StartupMonitor.UI.ViewModels;
using StartupMonitor.UI.Views;
using StartupMonitor.UI.Views.Dialogs;

namespace StartupMonitor.Tests.UI;

/// <summary>Изолирует глобальное WPF-приложение от остальных тестовых коллекций.</summary>
[CollectionDefinition("WpfNetworkRendering", DisableParallelization = true)]
public sealed class WpfNetworkRenderingCollection;

/// <summary>Проверяет производственную разметку и renderer вместо копирования формул в тестах.</summary>
[Collection("WpfNetworkRendering")]
public sealed partial class NetworkRenderingRegressionTests : IClassFixture<NetworkRenderingTestHost>
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Принимает общий STA-диспетчер для визуальных проверок.</summary>
    /// <param name="host">Изолированное WPF-окружение без запуска приложения и службы.</param>
    public NetworkRenderingRegressionTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Проверяет границы настоящих кнопок при длинном AI-тексте и компактной рабочей области.</summary>
    /// <param name="width">Ширина рабочей области в DIP.</param>
    /// <param name="height">Высота рабочей области в DIP.</param>
    [Theory]
    [InlineData(1920, 1040)]
    [InlineData(1366, 728)]
    [InlineData(1092.8, 582.4)]
    [InlineData(910.667, 485.333)]
    [InlineData(683, 364)]
    [InlineData(546.4, 291.2)]
    public void AlertLayout_AllActionButtonsRemainInsideWindow(double width, double height)
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var window = new NetworkConnectionAlertWindow(new NetworkConnectionAlertDto(), client);
            try
            {
                ((ScrollViewer)window.FindName("AiScrollViewer")).Visibility = Visibility.Visible;
                ((FrameworkElement)window.FindName("AiLoadingPanel")).Visibility = Visibility.Collapsed;
                ((TextBlock)window.FindName("TxtAiPurpose")).Text = new string('X', 4000);
                window.ApplyAdaptiveGeometry(new Rect(0, 0, width, height));
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(window.Width, window.MaxHeight));
                root.Arrange(new Rect(0, 0, window.Width, window.MaxHeight));
                root.UpdateLayout();
                foreach (var name in new[] { "BtnWhitelist", "BtnBlockAlways", "BtnAllowSession", "BtnBlockSession", "BtnAllowPort" })
                {
                    var button = (FrameworkElement)window.FindName(name);
                    var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                    Assert.True(bounds.Top >= -0.1 && bounds.Bottom <= window.MaxHeight + 0.1,
                        $"{name}: {bounds}; height={window.MaxHeight}");
                    Assert.True(bounds.Left >= -0.1 && bounds.Right <= window.Width + 0.1,
                        $"{name}: {bounds}; width={window.Width}");
                }
            }
            finally { window.Close(); }
        });

    /// <summary>Проверяет реальное ограничение размеров вместо дублирования алгоритма ширины.</summary>
    [Fact]
    public void AlertGeometry_TinyWorkAreaDoesNotExpandToDesignMinimum()
        => _host.Run(() =>
        {
            using var client = new PipeClient();
            var window = new NetworkConnectionAlertWindow(new NetworkConnectionAlertDto(), client);
            try
            {
                window.ApplyAdaptiveGeometry(new Rect(0, 0, 300, 200));
                Assert.True(window.Width <= 280);
                Assert.True(window.MaxHeight <= 180);
            }
            finally { window.Close(); }
        });

    /// <summary>Выполняет настоящий renderer с пропусками, нулями и нечисловыми данными на минимальной высоте.</summary>
    [Fact]
    public void ChartRender_InvalidSamplesAndSmallHoveredChartDoNotThrow()
        => _host.Run(() =>
        {
            var chart = new NetworkTimelineChartControl();
            chart.SetDataPoints(new[]
            {
                new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, 0, 0, 0, 0),
                new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, double.NaN, 100, 0, 0),
                new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, 100, double.PositiveInfinity, 1, 0),
                new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, 400, 200, 0, 0)
            });
            chart.Measure(new Size(500, 30));
            chart.Arrange(new Rect(0, 0, 500, 30));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(NetworkTimelineChartControl).GetField("_hoveredPoint", flags)?.SetValue(chart,
                new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, 400, 200, 0, 0));
            typeof(NetworkTimelineChartControl).GetField("_cursorPosition", flags)?.SetValue(chart, new Point(250, 10));
            Render(chart);
        });

    /// <summary>Повторная отрисовка без новых данных использует тот же замороженный рисунок.</summary>
    [Fact]
    public void ChartRender_ReusesGeometryUntilDataOrSizeChanges()
        => _host.Run(() =>
        {
            var chart = new NetworkTimelineChartControl();
            chart.SetDataPoints(new[]
            {
                new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, 10, 20, 0, 0),
                new NetworkTimelineChartControl.ChartDataPoint(DateTime.UtcNow, 40, 50, 0, 0)
            });
            chart.Measure(new Size(500, 80));
            chart.Arrange(new Rect(0, 0, 500, 80));
            Render(chart);
            var field = typeof(NetworkTimelineChartControl).GetField("_cachedDrawing", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException("Renderer drawing cache was not found.");
            var first = Assert.IsType<DrawingGroup>(field.GetValue(chart));
            Render(chart);
            Assert.Same(first, field.GetValue(chart));
            Assert.True(first.IsFrozen);
            chart.Arrange(new Rect(0, 0, 600, 80));
            Render(chart);
            Assert.NotSame(first, field.GetValue(chart));
        });

    /// <summary>Отсутствующий бакет сохраняется пропуском, события безопасности не превращаются в байты.</summary>
    [Fact]
    public void TimelineMapping_PreservesRealRatesAndMissingMeasurement()
    {
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var telemetry = new[]
        {
            new NetworkTrafficHistoryPoint(from.AddSeconds(15), 123, 456, true),
            new NetworkTrafficHistoryPoint(from.AddSeconds(45), 0, 0, false)
        };
        var history = new[] { new NetworkConnectionHistoryItem { Timestamp = from.AddSeconds(40), Verdict = NetworkConnectionVerdict.Blocked } };
        var points = NetworkSecurityHubView.BuildTimelinePoints(telemetry, history, from, from.AddMinutes(1));
        Assert.Equal(123, points[0].InboundBps);
        Assert.Equal(456, points[0].OutboundBps);
        Assert.True(points[0].HasMeasurement);
        Assert.False(points[1].HasMeasurement);
        Assert.Equal(0, points[1].InboundBps);
        Assert.Equal(1, points[1].BlockedCount);
    }

    /// <summary>Статический сервис иконок не удерживает освобождённую модель представления.</summary>
    [Fact]
    public void DisposedViewModel_IsCollectibleAfterStaticEventUnsubscription()
    {
        var reference = _host.CreateDisposedViewModelReference();
        for (var attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(reference.IsAlive);
    }

    /// <summary>Вызывает производственный OnRender с настоящим DrawingContext.</summary>
    /// <param name="chart">Контрол с уже выполненной компоновкой.</param>
    private static void Render(NetworkTimelineChartControl chart)
    {
        var method = typeof(NetworkTimelineChartControl).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Production renderer was not found.");
        var visual = new DrawingVisual();
        using var context = visual.RenderOpen();
        method.Invoke(chart, new object[] { context });
    }
}

/// <summary>Содержит единственный тестовый STA-поток и ресурсы WPF без вызова OnStartup.</summary>
public sealed class NetworkRenderingTestHost : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    /// <summary>Инициализирует ресурсы приложения и запускает ограниченно ожидаемый диспетчер.</summary>
    public NetworkRenderingTestHost()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                var app = new IsolatedTestApplication();
                app.Resources = LoadApplicationResources();
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "StartupMonitor-Wpf-Regression" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }

    /// <summary>Выполняет проверку на STA с ограничением времени.</summary>
    /// <param name="action">Действие над WPF-объектами.</param>
    public void Run(Action action)
        => _dispatcher.InvokeAsync(action).Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();

    /// <summary>Создаёт и освобождает модель в отдельном невстраиваемом вызове для проверки GC.</summary>
    /// <returns>Слабая ссылка без сохранения модели в closure теста.</returns>
    public WeakReference CreateDisposedViewModelReference()
        => _dispatcher.Invoke(CreateDisposedViewModel);

    /// <summary>Исключает удержание модели JIT-локальными переменными вызывающего теста.</summary>
    /// <returns>Слабая ссылка на освобождённую модель.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposedViewModel()
    {
        var config = Path.Combine(Path.GetTempPath(), "StartupMonitorVmGc_" + Guid.NewGuid().ToString("N") + ".json");
        var previousConfig = MainViewModel.CustomConfigPath;
        try
        {
            MainViewModel.CustomConfigPath = config;
            var model = new MainViewModel(new PipeClient());
            model.Dispose();
            return new WeakReference(model);
        }
        finally
        {
            MainViewModel.CustomConfigPath = previousConfig;
            if (File.Exists(config)) File.Delete(config);
        }
    }

    /// <summary>Отменяет тестовый диспетчер без вызова производственных startup/exit и управления службой.</summary>
    public void Dispose()
    {
        if (!_dispatcher.HasShutdownStarted)
            _dispatcher.InvokeAsync(() => Application.Current?.Shutdown()).Task
                .WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        Assert.True(_thread.Join(TimeSpan.FromSeconds(15)), "Тестовый STA-поток не завершился.");
    }

    /// <summary>Загружает ресурсный раздел настоящего App.xaml без создания производственного класса App.</summary>
    /// <returns>Ресурсы тем, контролов, локализации и конвертеров приложения.</returns>
    private static ResourceDictionary LoadApplicationResources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StartupMonitor.sln")))
            directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Каталог ресурсов приложения не найден.");
        var document = XDocument.Load(Path.Combine(directory.FullName, "src", "StartupMonitor.UI", "App.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = document.Descendants(presentation + "ResourceDictionary").First();
        var root = document.Root ?? throw new InvalidDataException("App.xaml не содержит корневого элемента.");
        foreach (var attribute in root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            dictionary.SetAttributeValue(attribute.Name, attribute.Value);
        // В loose XAML нет контекста компилятора UI: сборки для CLR-пространств задаются явно.
        dictionary.SetAttributeValue(XNamespace.Xmlns + "ui", "clr-namespace:Wpf.Ui.Markup;assembly=Wpf.Ui");
        dictionary.SetAttributeValue(XNamespace.Xmlns + "converters", "clr-namespace:StartupMonitor.UI.Converters;assembly=StartupMonitor.UI");
        dictionary.SetAttributeValue(XNamespace.Xmlns + "services", "clr-namespace:StartupMonitor.UI.Services;assembly=StartupMonitor.UI");
        var namespaces = new Dictionary<string, string>
        {
            ["http://schemas.lepo.co/wpfui/2022/xaml"] = "clr-namespace:Wpf.Ui.Markup;assembly=Wpf.Ui",
            ["clr-namespace:StartupMonitor.UI.Converters"] = "clr-namespace:StartupMonitor.UI.Converters;assembly=StartupMonitor.UI",
            ["clr-namespace:StartupMonitor.UI.Services"] = "clr-namespace:StartupMonitor.UI.Services;assembly=StartupMonitor.UI"
        };
        foreach (var element in dictionary.DescendantsAndSelf())
        {
            if (namespaces.TryGetValue(element.Name.NamespaceName, out var resolved))
                element.Name = XName.Get(element.Name.LocalName, resolved);
            foreach (var attribute in element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                if (namespaces.TryGetValue(attribute.Value, out var declared)) attribute.Value = declared;
        }
        var context = new ParserContext { BaseUri = new Uri("pack://application:,,,/StartupMonitor.UI;component/") };
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString(), context);
    }

    /// <summary>Тестовый Application не наследует производственные обработчики запуска и завершения.</summary>
    private sealed class IsolatedTestApplication : Application
    {
        /// <summary>Нейтрализует отложенный startup, который WPF ставит в Dispatcher уже в конструкторе Application.</summary>
        /// <param name="e">Параметры запуска тестового приложения.</param>
        protected override void OnStartup(StartupEventArgs e) => ShutdownMode = ShutdownMode.OnExplicitShutdown;

        /// <summary>Завершает только тестовый STA-поток; производственный App.OnExit не вызывается.</summary>
        /// <param name="e">Параметры завершения тестового приложения.</param>
        protected override void OnExit(ExitEventArgs e) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
