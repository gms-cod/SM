using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace StartupMonitor.Service.Services.Ai;

public static class ProgramLangDetector
{
    [DllImport("version.dll", CharSet = CharSet.Unicode)]
    /// <summary>GetFileVersionInfoSizeW: Возвращает FileVersionInfoSizeW.</summary>
    /// <param name="filename">Входные данные: значение параметра filename (string).</param>
    /// <param name="handle">Входные данные: значение параметра handle (out int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern int.</returns>
    private static extern int GetFileVersionInfoSizeW(string filename, out int handle);

    [DllImport("version.dll", CharSet = CharSet.Unicode)]
    /// <summary>GetFileVersionInfoW: Возвращает FileVersionInfoW.</summary>
    /// <param name="filename">Входные данные: значение параметра filename (string).</param>
    /// <param name="handle">Входные данные: значение параметра handle (int).</param>
    /// <param name="size">Входные данные: значение параметра size (int).</param>
    /// <param name="data">Входные данные: значение параметра data (byte[]).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool GetFileVersionInfoW(string filename, int handle, int size, byte[] data);

    [DllImport("version.dll", CharSet = CharSet.Unicode)]
    /// <summary>VerQueryValueW: Выполняет операцию VerQueryValueW.</summary>
    /// <param name="block">Входные данные: значение параметра block (byte[]).</param>
    /// <param name="subblock">Входные данные: значение параметра subblock (string).</param>
    /// <param name="buffer">Входные данные: значение параметра buffer (out IntPtr).</param>
    /// <param name="len">Входные данные: значение параметра len (out int).</param>
    /// <returns>Выходные данные: результирующее значение типа extern bool.</returns>
    private static extern bool VerQueryValueW(byte[] block, string subblock, out IntPtr buffer, out int len);

    /// <summary>Detect: Выполняет операцию Detect.</summary>
    /// <param name="filePath">Входные данные: значение параметра filePath (string).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    public static string Detect(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return "en";

        try
        {
            var size = GetFileVersionInfoSizeW(filePath, out _);
            if (size <= 0) return "en";

            var data = new byte[size];
            if (!GetFileVersionInfoW(filePath, 0, size, data)) return "en";

            // Читаем таблицу трансляций (язык + кодировка)
            if (!VerQueryValueW(data, @"\VarFileInfo\Translation", out var buf, out var len))
                return "en";

            if (len < 4) return "en";
            Marshal.Copy(buf, data, 0, 4);
            var langId = (ushort)(data[0] | (data[1] << 8));

            var ci = CultureInfo.GetCultureInfo(langId);
            return ci.TwoLetterISOLanguageName;
        }
        catch
        {
            return "en";
        }
    }
}
