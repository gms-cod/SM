using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Потокобезопасный сервис извлечения и кэширования иконок приложений (.exe, .lnk, .cpl и др.).
/// </summary>
public static class AppIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    /// <summary>SHGetFileInfo: Выполняет операцию SHGetFileInfo.</summary>
    /// <param name="pszPath">Входные данные: значение параметра pszPath (string).</param>
    /// <param name="dwFileAttributes">Входные данные: значение параметра dwFileAttributes (uint).</param>
    /// <param name="psfi">Входные данные: значение параметра psfi (ref SHFILEINFO).</param>
    /// <param name="cbFileInfo">Входные данные: значение параметра cbFileInfo (uint).</param>
    /// <param name="uFlags">Входные данные: значение параметра uFlags (uint).</param>
    /// <returns>Выходные данные: результирующее значение типа extern IntPtr.</returns>
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    /// <summary>DestroyIcon: Выполняет операцию DestroyIcon.</summary>
    /// <param name="hIcon">Входные данные: значение параметра hIcon (IntPtr).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Возвращает иконку приложения по пути к файлу с кэшированием.
    /// </summary>
    public static ImageSource? GetIcon(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var cleanPath = CleanPath(rawPath);
        if (string.IsNullOrWhiteSpace(cleanPath))
            return null;

        return Cache.GetOrAdd(cleanPath, ExtractIconCore);
    }

    /// <summary>ExtractIconCore: Выполняет операцию ExtractIconCore.</summary>
    /// <param name="path">Входные данные: значение параметра path (string).</param>
    /// <returns>Выходные данные: результирующее значение типа ImageSource?.</returns>
    private static ImageSource? ExtractIconCore(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var shinfo = new SHFILEINFO();
            var flags = SHGFI_ICON | SHGFI_SMALLICON;
            var res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

            if (res == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>CleanPath: Выполняет операцию CleanPath.</summary>
    /// <param name="input">Входные данные: значение параметра input (string).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    private static string CleanPath(string input)
    {
        var trimmed = input.Trim().Trim('"', '\'');
        try
        {
            trimmed = Environment.ExpandEnvironmentVariables(trimmed);
        }
        catch { }

        // Если в строке есть аргументы после пробела и файла с пробелами нет
        if (!File.Exists(trimmed) && trimmed.Contains(' '))
        {
            var firstSpace = trimmed.IndexOf(' ');
            var sub = trimmed[..firstSpace];
            if (File.Exists(sub))
                return sub;
        }

        return trimmed;
    }
}

