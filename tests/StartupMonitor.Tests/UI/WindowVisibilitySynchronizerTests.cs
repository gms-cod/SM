using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.UI;

/// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
[Collection("WpfNetworkRendering")]
public sealed class WindowVisibilitySynchronizerTests
{
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    public WindowVisibilitySynchronizerTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void NativeRestore_AfterFluentMinimizeToTray_RestoresAutomationContent(WindowState restoredState)
    {
        Wpf.Ui.Controls.FluentWindow? window = null;
        var handle = IntPtr.Zero;
        var restoreCount = 0;
        try
        {
            _host.Run(() =>
            {
                var text = new TextBlock { Text = "Restored Fluent content", FontSize = 24 };
                AutomationProperties.SetAutomationId(text, "RestoredFluentText");
                var created = new Wpf.Ui.Controls.FluentWindow
                {
                    Content = text, Width = 600, Height = 400, Left = 50, Top = 50,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
                    ExtendsContentIntoTitleBar = true, ShowInTaskbar = true,
                    WindowState = restoredState
                };
                window = created;
                var lastNonMinimizedState = restoredState;
                DispatcherOperation? pendingHide = null;
                var closed = false;
                created.Closed += (_, _) =>
                {
                    closed = true;
                    pendingHide?.Abort();
                    pendingHide = null;
                };
                created.StateChanged += (_, _) =>
                {
                    if (created.WindowState == WindowState.Minimized)
                    {
                        pendingHide ??= created.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            try
                            {
                                if (!closed && created.WindowState == WindowState.Minimized && created.IsVisible)
                                    created.Hide();
                            }
                            finally { pendingHide = null; }
                        }));
                    }
                    else
                    {
                        pendingHide?.Abort();
                        pendingHide = null;
                        lastNonMinimizedState = created.WindowState;
                    }
                };
                WindowVisibilitySynchronizer.Attach(created, () =>
                {
                    restoreCount++;
                    created.WindowState = lastNonMinimizedState;
                    created.Show();
                    created.Activate();
                    created.Focus();
                });
                created.Show();
                PumpDispatcher();
                handle = new WindowInteropHelper(created).Handle;
            });
            AssertAutomationContent(handle);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                // Verifies component behavior and assertions.
                ShowWindow(handle, 6);
                _host.Run(() =>
                {
                    PumpDispatcher();
                    Assert.NotNull(window);
                    Assert.False(window.IsVisible);
                });
                ShowWindow(handle, 9);
                _host.Run(() =>
                {
                    PumpDispatcher();
                    Assert.NotNull(window);
                    Assert.True(window.IsVisible);
                    Assert.Equal(restoredState, window.WindowState);
                    Assert.Equal(cycle + 1, restoreCount);
                    Assert.True(new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                        .IntersectsWith(SystemParameters.WorkArea), "Восстановленное окно находится за пределами рабочей области.");
                });
                AssertAutomationContent(handle);
            }
            _host.Run(() =>
            {
                Assert.NotNull(window);
                // Verifies component behavior and assertions.
                window.WindowState = WindowState.Minimized;
                Assert.True(window.IsVisible);
                window.WindowState = restoredState;
                window.Show();
                PumpDispatcher();
                Assert.True(window.IsVisible);
                Assert.Equal(restoredState, window.WindowState);
            });
            AssertAutomationContent(handle);
        }
        finally { _host.Run(() => window?.Close()); }
    }

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    private static void AssertAutomationContent(IntPtr handle)
    {
        var root = AutomationElement.FromHandle(handle);
        var text = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "RestoredFluentText"));
        Assert.NotNull(text);
        Assert.False(text.Current.IsOffscreen);
        Assert.Equal("Restored Fluent content", text.Current.Name);
    }

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    [Theory]
    [InlineData(false, WindowState.Normal)]
    [InlineData(true, WindowState.Normal)]
    [InlineData(false, WindowState.Maximized)]
    [InlineData(true, WindowState.Maximized)]
    public void NativeRestore_AfterWpfHide_RestoresVisibleRenderedContent(bool attachAfterShow, WindowState restoredState)
        => _host.Run(() =>
        {
            var text = new TextBlock
            {
                Text = "Visible WPF content",
                FontSize = 24,
                Foreground = Brushes.Black,
                Background = Brushes.White
            };
            // Verifies component behavior and assertions.
            var window = new Window
            {
                Style = new Style(typeof(Window)), Content = text, Width = 320, Height = 140,
                ShowInTaskbar = false, WindowState = restoredState
            };
            var restoreCount = 0;
            try
            {
                if (attachAfterShow) window.Show();
                WindowVisibilitySynchronizer.Attach(window, () =>
                {
                    restoreCount++;
                    window.Show();
                    window.WindowState = restoredState;
                });
                if (!attachAfterShow) window.Show();
                PumpDispatcher();
                Assert.Equal(0, restoreCount);

                var handle = new WindowInteropHelper(window).Handle;
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    window.Hide();
                    Assert.False(window.IsVisible);
                    ShowWindow(handle, 9);
                    Assert.True(IsWindowVisible(handle));
                    PumpDispatcher();
                    Assert.True(window.IsVisible);
                    Assert.True(text.IsVisible);
                    Assert.Equal(restoredState, window.WindowState);
                    Assert.Equal(cycle + 1, restoreCount);
                    AssertRenderedText(text);
                }

                // Verifies component behavior and assertions.
                window.Hide();
                window.Show();
                window.Show();
                PumpDispatcher();
                Assert.Equal(3, restoreCount);
                AssertRenderedText(text);
            }
            finally { window.Close(); }
        });

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    [Fact]
    public void ClosingWindow_CancelsPendingNativeRestore()
        => _host.Run(() =>
        {
            // Verifies component behavior and assertions.
            var window = new Window { Style = new Style(typeof(Window)), Width = 200, Height = 100, ShowInTaskbar = false };
            var restoreCount = 0;
            WindowVisibilitySynchronizer.Attach(window, () => restoreCount++);
            try
            {
                window.Show();
                PumpDispatcher();
                var handle = new WindowInteropHelper(window).Handle;
                window.Hide();
                ShowWindow(handle, 9);
                window.Close();
                PumpDispatcher();
                Assert.Equal(0, restoreCount);
            }
            finally { if (window.IsVisible) window.Close(); }
        });

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    [Fact]
    public void Attach_RejectsNullArguments()
        => _host.Run(() =>
        {
            // Verifies component behavior and assertions.
            Assert.Throws<ArgumentNullException>(() => WindowVisibilitySynchronizer.Attach(null!, () => { }));
            var window = new Window();
            try { Assert.Throws<ArgumentNullException>(() => WindowVisibilitySynchronizer.Attach(window, null!)); }
            finally { window.Close(); }
        });

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    private static void AssertRenderedText(TextBlock text)
    {
        text.UpdateLayout();
        Assert.True(text.ActualWidth > 0 && text.ActualHeight > 0);
        var width = (int)Math.Ceiling(text.ActualWidth);
        var height = (int)Math.Ceiling(text.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(text);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var darkPixels = 0;
        for (var index = 0; index < pixels.Length; index += 4)
            if (pixels[index + 3] > 200 && pixels[index] < 100 && pixels[index + 1] < 100 && pixels[index + 2] < 100)
                darkPixels++;
        Assert.True(darkPixels > 100, $"В WPF-рисунке отсутствуют ожидаемые буквы: тёмных пикселей {darkPixels}.");
    }

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);

    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowVisibilitySynchronizerTests.cs.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
}
