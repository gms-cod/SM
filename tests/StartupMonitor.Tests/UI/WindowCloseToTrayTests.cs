using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.UI;

/// <summary>Проверяет настоящий FluentWindow: штатное закрытие скрывает окно, сохраняя его содержимое и исходное состояние.</summary>
[Collection("WpfNetworkRendering")]
public sealed class WindowCloseToTrayTests
{
    private readonly NetworkRenderingTestHost _host;

    /// <summary>Использует общий STA-host без production startup, IPC и пользовательских настроек.</summary>
    /// <param name="host">Диспетчер настоящих окон тестовой коллекции.</param>
    public WindowCloseToTrayTests(NetworkRenderingTestHost host) => _host = host;

    /// <summary>Повторные нажатия штатного Close не уничтожают HWND; восстановление сохраняет Normal/Maximized и WPF-дерево.</summary>
    /// <param name="state">Оконное состояние до закрытия крестиком.</param>
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
            // Владелец может явно прекратить поведение; настоящее закрытие после этого сохраняется.
            detach();
            window.Close();
            Assert.True(closed);
        }
        finally { detach(); if (!closed) window.Close(); }
    });

    /// <summary>API отклоняет отсутствующее окно, не создавая повреждённый оконный жизненный цикл.</summary>
    [Fact]
    public void Attach_NullWindow_Throws() => _host.Run(() =>
    {
        // Намеренный null! проверяет валидацию границы; production всегда передаёт созданное окно.
        Assert.Throws<ArgumentNullException>(() => WindowCloseToTrayBehavior.Attach(null!));
    });

    /// <summary>Обрабатывает нативную команду закрытия и последующий WPF-layout до проверки видимости.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
