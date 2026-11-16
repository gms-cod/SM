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

/// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
[CollectionDefinition("WpfNetworkRendering", DisableParallelization = true)]
public sealed class WpfNetworkRenderingCollection : ICollectionFixture<NetworkRenderingTestHost>;

/// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
[Collection("WpfNetworkRendering")]
public sealed partial class NetworkRenderingRegressionTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    public NetworkRenderingRegressionTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    private static void Render(NetworkTimelineChartControl chart)
    {
        var method = typeof(NetworkTimelineChartControl).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Production renderer was not found.");
        var visual = new DrawingVisual();
        using var context = visual.RenderOpen();
        method.Invoke(chart, new object[] { context });
    }
}

/// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
public sealed class NetworkRenderingTestHost : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    public void Run(Action action)
        => _dispatcher.InvokeAsync(action).Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    public WeakReference CreateDisposedViewModelReference()
        => _dispatcher.Invoke(CreateDisposedViewModel);

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    public void Dispose()
    {
        if (!_dispatcher.HasShutdownStarted)
            _dispatcher.InvokeAsync(() => Application.Current?.Shutdown()).Task
                .WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        Assert.True(_thread.Join(TimeSpan.FromSeconds(15)), "Тестовый STA-поток не завершился.");
    }

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
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
        // Verifies component behavior and assertions.
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

    /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
    private sealed class IsolatedTestApplication : Application
    {
        /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
        /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
        protected override void OnStartup(StartupEventArgs e) => ShutdownMode = ShutdownMode.OnExplicitShutdown;

        /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
        /// <summary>Verifies test scenario for NetworkRenderingRegressionTests.cs.</summary>
        protected override void OnExit(ExitEventArgs e) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
