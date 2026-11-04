using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace StartupMonitor.UI.Services;

/// <summary>Согласует нативное восстановление скрытого HWND с видимостью его WPF-содержимого.</summary>
internal static class WindowVisibilitySynchronizer
{
    /// <summary>
    /// Подключает обработку WM_SHOWWINDOW и WM_WINDOWPOSCHANGED: внешний ShowWindow восстанавливает только HWND,
    /// поэтому скрытое через Window.Hide содержимое повторно показывается штатным WPF-действием.
    /// Проверка откладывается до завершения сообщения, чтобы обычный Window.Show не вызывал рекурсию.
    /// </summary>
    /// <param name="window">Окно, принадлежащее текущему Dispatcher.</param>
    /// <param name="restore">Штатное действие восстановления окна, вызывающее Window.Show.</param>
    public static void Attach(Window window, Action restore)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(restore);
        window.VerifyAccess();

        HwndSource? source = null;
        DispatcherOperation? pendingRestore = null;
        bool closed = false;

        /// <summary>Подключает hook после создания HWND либо использует уже существующий источник.</summary>
        /// <param name="sender">Окно, создавшее нативный источник.</param>
        /// <param name="args">Аргументы события создания источника.</param>
        void AttachSource(object? sender, EventArgs args)
        {
            if (closed || source is not null) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            source = HwndSource.FromHwnd(handle)
                ?? throw new InvalidOperationException("Для HWND окна не найден источник WPF при подключении синхронизации видимости.");
            source.AddHook(OnWindowMessage);
        }

        /// <summary>После обработки нативного сообщения восстанавливает только ещё скрытое WPF-окно.</summary>
        void RestoreIfHidden()
        {
            try
            {
                if (!closed && !window.IsVisible && !window.Dispatcher.HasShutdownStarted
                    && IsWindowVisible(new WindowInteropHelper(window).Handle))
                    restore();
            }
            finally { pendingRestore = null; }
        }

        /// <summary>Отслеживает нативный показ, не перехватывая штатную обработку сообщения WPF.</summary>
        /// <param name="handle">Нативный дескриптор окна.</param>
        /// <param name="message">Код оконного сообщения.</param>
        /// <param name="wParam">Признак нативной видимости для WM_SHOWWINDOW.</param>
        /// <param name="lParam">Дополнительные параметры сообщения Windows.</param>
        /// <param name="handled">Признак перехвата; остаётся неизменным.</param>
        /// <returns>Нулевой результат, поскольку сообщение обрабатывается штатным оконным механизмом.</returns>
        IntPtr OnWindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // При восстановлении развёрнутого окна WM_SHOWWINDOW может отсутствовать;
            // WM_WINDOWPOSCHANGED проверяет реальную видимость HWND только при скрытом WPF-окне.
            bool nativeShow = (message == 0x0018 && wParam != IntPtr.Zero)
                || (message == 0x0047 && !window.IsVisible && IsWindowVisible(handle));
            if (nativeShow && !window.IsVisible && !closed && pendingRestore is null && !window.Dispatcher.HasShutdownStarted)
            {
                pendingRestore = window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(RestoreIfHidden));
            }
            return IntPtr.Zero;
        }

        /// <summary>Отменяет отложенное восстановление и освобождает hook при окончательном закрытии окна.</summary>
        /// <param name="sender">Закрытое окно.</param>
        /// <param name="args">Аргументы события закрытия.</param>
        void Detach(object? sender, EventArgs args)
        {
            closed = true;
            pendingRestore?.Abort();
            pendingRestore = null;
            if (source is not null && !source.IsDisposed)
                source.RemoveHook(OnWindowMessage);
            source = null;
            window.SourceInitialized -= AttachSource;
            window.Closed -= Detach;
        }

        window.SourceInitialized += AttachSource;
        window.Closed += Detach;
        AttachSource(window, EventArgs.Empty);
    }

    /// <summary>Проверяет нативную видимость перед восстановлением, чтобы поздний callback не отменял намеренный Hide.</summary>
    /// <param name="handle">Дескриптор проверяемого окна.</param>
    /// <returns>Признак видимости HWND, независимый от свойства WPF IsVisible.</returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
}
