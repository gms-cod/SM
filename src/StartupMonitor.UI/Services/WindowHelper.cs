using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Помощник для быстрого определения PID процессов, имеющих видимые окна на рабочем столе пользователя.
/// </summary>
public static class WindowHelper
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
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
}

