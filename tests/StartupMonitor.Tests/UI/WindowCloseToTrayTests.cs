using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.UI;

/// <summary>Verifies test scenario for WindowCloseToTrayTests.cs.</summary>
[Collection("WpfNetworkRendering")]
public sealed class WindowCloseToTrayTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Verifies test scenario for WindowCloseToTrayTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowCloseToTrayTests.cs.</summary>
    public WindowCloseToTrayTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Verifies test scenario for WindowCloseToTrayTests.cs.</summary>
    /// <summary>Verifies test scenario for WindowCloseToTrayTests.cs.</summary>
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void CaptionClose_HidesWithoutClosing_AndRestoresSameContent(WindowState state) => _host.Run(() =>
    {
        var text = new TextBlock { Text = "Tray lifecycle content" };
        var window = new Wpf.Ui.Controls.FluentWindow { Content = text, Width = 400, Height = 300, WindowState = state };
        var detach = WindowCloseToTrayBehavior.Attach(window);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show();
            Pump();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                SystemCommands.CloseWindow(window);
                Pump();
                Assert.False(window.IsVisible);
                Assert.False(closed);
                Assert.Equal(state, window.WindowState);

                window.Show();
                Pump();
                Assert.True(window.IsVisible);
                Assert.True(text.IsVisible);
                Assert.Same(text, window.Content);
                Assert.Equal(state, window.WindowState);
            }
            // Verifies component behavior and assertions.
            detach();
            window.Close();
            Assert.True(closed);
        }
        finally { detach(); if (!closed) window.Close(); }
    });

    /// <summary>Verifies test scenario for WindowCloseToTrayTests.cs.</summary>
    [Fact]
    public void Attach_NullWindow_Throws() => _host.Run(() =>
    {
        // Verifies component behavior and assertions.
        Assert.Throws<ArgumentNullException>(() => WindowCloseToTrayBehavior.Attach(null!));
    });

    /// <summary>Verifies test scenario for WindowCloseToTrayTests.cs.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
