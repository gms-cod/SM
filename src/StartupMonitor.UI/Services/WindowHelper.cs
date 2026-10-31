using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Помощник для быстрого определения PID процессов, имеющих видимые окна на рабочем столе пользователя.
/// Для UI-потока предусмотрен потокобезопасный кэш с фоновым обновлением (без блокировки Dispatcher).
/// </summary>
public static class WindowHelper
{
    /// <summary>EnumWindowsProc: Выполняет операцию EnumWindowsProc.</summary>
    /// <param name="hWnd">Входные данные: значение параметра hWnd (IntPtr).</param>
    /// <param name="lParam">Входные данные: значение параметра lParam (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа delegate bool.</returns>
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    /// <summary>EnumWindows: Выполняет операцию EnumWindows.</summary>
    /// <param name="lpEnumFunc">Входные данные: значение параметра lpEnumFunc (EnumWindowsProc).</param>
    /// <param name="lParam">Входные данные: значение параметра lParam (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    /// <summary>GetWindowThreadProcessId: Возвращает WindowThreadProcessId.</summary>
    /// <param name="hWnd">Входные данные: значение параметра hWnd (IntPtr).</param>
    /// <param name="lpdwProcessId">Входные данные: значение параметра lpdwProcessId (out uint).</param>
    /// <returns>Выходные данные: результирующее значение типа extern uint.</returns>
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>IsWindowVisible: Проверяет состояние IsWindowVisible.</summary>
    /// <param name="hWnd">Входные данные: значение параметра hWnd (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    /// <summary>GetWindowTextLength: Возвращает WindowTextLength.</summary>
    /// <param name="hWnd">Входные данные: значение параметра hWnd (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern int.</returns>
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    /// <summary>GetShellWindow: Возвращает ShellWindow.</summary>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    /// <summary>GetWindowLong: Возвращает WindowLong.</summary>
    /// <param name="hWnd">Входные данные: значение параметра hWnd (IntPtr).</param>
    /// <param name="nIndex">Входные данные: значение параметра nIndex (int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern int.</returns>
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>
    /// Возвращает набор идентификаторов процессов (PID), имеющих видимые окна с заголовком на рабочем столе.
    /// Выполняется практически мгновенно (&lt; 1 мс).
    /// </summary>
    public static HashSet<int> GetProcessIdsWithWindows()
    {
        var pids = new HashSet<int>();
        var shellWindow = GetShellWindow();

        try
        {
            EnumWindows((hWnd, _) =>
            {
                if (hWnd == IntPtr.Zero || hWnd == shellWindow) return true;
                if (!IsWindowVisible(hWnd)) return true;

                // Игнорируем плавающие tool-окна (тултипы, хинты)
                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                if (GetWindowTextLength(hWnd) > 0)
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid > 0)
                    {
                        pids.Add((int)pid);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Защита от непредвиденных сбоев P/Invoke
        }

        return pids;
    }

    // ─────────────────────────── Фоновый кэш (UI Dispatcher Offload) ───────────────────────────

    /// <summary>Потокобезопасный кэш PID процессов с видимыми окнами (обновляется в фоне).</summary>
    private static HashSet<int> _cachedPids = [];

    /// <summary>Момент времени (UTC), до которого кэш считается свежим.</summary>
    private static DateTime _cacheExpiryUtc = DateTime.MinValue;

    /// <summary>Флаг «обновление кэша уже выполняется» (защита от параллельных EnumWindows).</summary>
    private static int _refreshInFlight;

    /// <summary>Время жизни кэша PID с видимыми окнами (EnumWindows не чаще 1 раза в 2 с).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Последний кэшированный набор PID процессов с видимыми окнами.
    /// Не выполняет EnumWindows — безопасно вызывать с UI-потока.
    /// </summary>
    public static HashSet<int> CachedProcessIdsWithWindows => _cachedPids;

    /// <summary>
    /// Обновляет кэш PID с видимыми окнами в фоновом потоке (EnumWindows вне UI-потока),
    /// устраняя задержки отрисовки кадров WPF. При истечении TTL запускается не более одного
    /// обновления; по завершении вызывается <paramref name="onRefreshed"/> (на фоновом потоке).
    /// </summary>
    /// <param name="onRefreshed">Необязательный колбэк, выполняемый после успешного обновления кэша.</param>
    public static void RefreshProcessIdsWithWindowsInBackground(Action? onRefreshed = null)
    {
        if (DateTime.UtcNow < _cacheExpiryUtc) return;
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0) return;

        _ = Task.Run(() =>
        {
            try
            {
                var fresh = GetProcessIdsWithWindows();
                _cachedPids = fresh;
                _cacheExpiryUtc = DateTime.UtcNow.Add(CacheTtl);
                onRefreshed?.Invoke();
            }
            catch
            {
                // Игнорируем сбои P/Invoke — оставляем прежний кэш
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInFlight, 0);
            }
        });
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor = new();
        public RECT rcWork = new();
        public int dwFlags = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFO lpmi);

    /// <summary>
    /// Возвращает рабочую область монитора (в аппаратно-независимых единицах WPF DIP с учётом Per-Monitor DPI),
    /// на котором находится окно, либо на котором находится курсор мыши / окно-владелец.
    /// Гарантирует корректное определение экранных границ в многомониторных конфигурациях.
    /// </summary>
    /// <param name="window">Экземпляр окна WPF.</param>
    /// <returns>Прямоугольник рабочей области в координатах DIP рабочего стола.</returns>
    public static Rect GetCurrentMonitorWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var helper = new WindowInteropHelper(window);
            var hWnd = helper.Handle;
            var hMonitor = IntPtr.Zero;

            if (hWnd != IntPtr.Zero)
            {
                hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            }

            if (hMonitor == IntPtr.Zero && window.Owner != null)
            {
                var ownerHwnd = new WindowInteropHelper(window.Owner).Handle;
                if (ownerHwnd != IntPtr.Zero)
                {
                    hMonitor = MonitorFromWindow(ownerHwnd, MONITOR_DEFAULTTONEAREST);
                }
            }

            if (hMonitor == IntPtr.Zero && GetCursorPos(out POINT pt))
            {
                hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            }

            if (hMonitor == IntPtr.Zero)
            {
                return SystemParameters.WorkArea;
            }

            var mi = new MONITORINFO();
            if (!GetMonitorInfo(hMonitor, mi))
            {
                return SystemParameters.WorkArea;
            }

            var dpi = VisualTreeHelper.GetDpi(window);
            var dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            var dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            var left = mi.rcWork.Left / dpiX;
            var top = mi.rcWork.Top / dpiY;
            var width = (mi.rcWork.Right - mi.rcWork.Left) / dpiX;
            var height = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiY;

            if (width <= 0 || height <= 0)
            {
                return SystemParameters.WorkArea;
            }

            return new Rect(left, top, width, height);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }
}

