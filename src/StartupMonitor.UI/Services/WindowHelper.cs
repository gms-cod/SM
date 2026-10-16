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
}

