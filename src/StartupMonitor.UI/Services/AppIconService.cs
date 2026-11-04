using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StartupMonitor.UI.Services;

/// <summary>
/// Потокобезопасный сервис извлечения и асинхронного кэширования иконок приложений (.exe, .lnk, .cpl и др.).
/// Полностью исключает синхронные дисковые операции File.Exists и вызовы Win32 SHGetFileInfo из UI-потока.
/// </summary>
public static class AppIconService
{
    /// <summary>
    /// Кэш готовых иконок, замороженных через Freeze() для безопасного использования в любом потоке.
    /// </summary>
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Очередь активных фоновых задач извлечения иконок для предотвращения повторных параллельных вызовов одного и того же файла.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> InFlightTasks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Статическая прозрачная placeholder-заглушка с Freeze(), возвращаемая UI-потоку до завершения фоновой загрузки.
    /// </summary>
    public static readonly BitmapSource Placeholder = CreatePlaceholderBitmap();

    /// <summary>
    /// Событие успешной асинхронной загрузки иконки для уведомления подписчиков (например, MainViewModel).
    /// </summary>
    public static event Action<string, ImageSource>? IconLoaded;

    /// <summary>
    /// Создает пустую прозрачную заглушку 16x16 с фиксацией Freeze().
    /// </summary>
    private static BitmapSource CreatePlaceholderBitmap()
    {
        var pixels = new byte[16 * 16 * 4];
        var bmp = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
        bmp.Freeze();
        return bmp;
    }

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
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Быстрая очистка пути без дисковых операций (без File.Exists), безопасная для вызова в UI-потоке.
    /// </summary>
    public static string FastCleanPath(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim().Trim('"', '\'');
        try
        {
            trimmed = Environment.ExpandEnvironmentVariables(trimmed);
        }
        catch { }

        // Если путь содержит кавычки в начале, например "C:\Program Files\app.exe" --arg
        if (input.StartsWith('"'))
        {
            var closingQuote = input.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                return input.Substring(1, closingQuote - 1).Trim();
            }
        }

        // Если в строке есть пробел и расширение .exe, отсекаем аргументы
        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0 && exeIndex + 4 < trimmed.Length && (trimmed[exeIndex + 4] == ' ' || trimmed[exeIndex + 4] == '"'))
        {
            return trimmed.Substring(0, exeIndex + 4);
        }

        return trimmed;
    }

    /// <summary>
    /// Возвращает иконку приложения из кэша. Если иконка еще не извлечена, мгновенно возвращает
    /// placeholder-заглушку и запускает асинхронную фоновую загрузку без блокировки потока Dispatcher.
    /// </summary>
    public static ImageSource GetIconOrPlaceholder(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return Placeholder;

        var cleanPath = FastCleanPath(rawPath);
        if (string.IsNullOrWhiteSpace(cleanPath))
            return Placeholder;

        // Если иконка уже есть в кэше — возвращаем немедленно
        if (Cache.TryGetValue(cleanPath, out var cached))
        {
            return cached ?? Placeholder;
        }

        // Запускаем фоновое извлечение на пуле потоков без блокировки UI
        _ = FetchIconAsync(cleanPath);

        // Возвращаем placeholder для мгновенной плавной отрисовки
        return Placeholder;
    }

    /// <summary>
    /// Для обратной совместимости: возвращает иконку из кэша или пытается извлечь её синхронно/фоново.
    /// Если файл явно не существует на диске — возвращает null.
    /// </summary>
    public static ImageSource? GetIcon(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var cleanPath = FastCleanPath(rawPath);
        if (string.IsNullOrWhiteSpace(cleanPath))
            return null;

        if (Cache.TryGetValue(cleanPath, out var cached))
            return cached;

        // Если файл заведомо не существует на диске, не возвращаем заглушку в прямом вызове GetIcon
        var resolved = ResolvePathOnDisk(cleanPath);
        if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
        {
            Cache[cleanPath] = null;
            return null;
        }

        return GetIconOrPlaceholder(rawPath);
    }

    /// <summary>
    /// Асинхронно извлекает иконку в фоновом потоке, помещает результат в кэш и уведомляет подписчиков.
    /// </summary>
    public static Task<ImageSource?> FetchIconAsync(string cleanPath)
    {
        return InFlightTasks.GetOrAdd(cleanPath, path => Task.Run(() =>
        {
            try
            {
                var icon = ExtractIconCore(path);
                Cache[path] = icon;
                if (icon != null)
                {
                    IconLoaded?.Invoke(path, icon);
                }
                return icon;
            }
            catch
            {
                Cache[path] = null;
                return null;
            }
            finally
            {
                InFlightTasks.TryRemove(path, out _);
            }
        }));
    }

    /// <summary>
    /// Предварительная параллельная предзагрузка иконок для коллекции путей в фоновом потоке.
    /// </summary>
    public static async Task PreloadIconsAsync(IEnumerable<string?> paths)
    {
        var cleanPaths = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(FastCleanPath)
            .Where(p => !string.IsNullOrWhiteSpace(p) && !Cache.ContainsKey(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleanPaths.Count == 0) return;

        await Task.Run(() =>
        {
            Parallel.ForEach(cleanPaths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, path =>
            {
                try
                {
                    var icon = ExtractIconCore(path);
                    Cache[path] = icon;
                    if (icon != null)
                    {
                        IconLoaded?.Invoke(path, icon);
                    }
                }
                catch
                {
                    Cache[path] = null;
                }
            });
        });
    }

    /// <summary>
    /// Внутреннее извлечение иконки (выполняется СТРОГО в фоновом потоке).
    /// </summary>
    private static ImageSource? ExtractIconCore(string path)
    {
        try
        {
            var resolvedPath = ResolvePathOnDisk(path);
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
                return null;

            var shinfo = new SHFILEINFO();
            var flags = SHGFI_ICON | SHGFI_SMALLICON;
            var res = SHGetFileInfo(resolvedPath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);

            if (res == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // Замораживаем объект, делая его потокобезопасным и иммутабельным для сверхбыстрого рендеринга WPF
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

    /// <summary>
    /// Проверяет и разрешает путь на диске с поддержкой параметров командной строки (только в фоне).
    /// </summary>
    private static string ResolvePathOnDisk(string path)
    {
        if (File.Exists(path))
            return path;

        // Если путь содержит аргументы после пробела
        if (path.Contains(' '))
        {
            var firstSpace = path.IndexOf(' ');
            var sub = path[..firstSpace];
            if (File.Exists(sub))
                return sub;
        }

        return path;
    }
}

