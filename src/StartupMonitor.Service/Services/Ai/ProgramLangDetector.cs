using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace StartupMonitor.Service.Services.Ai;

public static class ProgramLangDetector
{
    [DllImport("version.dll", CharSet = CharSet.Unicode)]
    private static extern int GetFileVersionInfoSizeW(string filename, out int handle);

    [DllImport("version.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetFileVersionInfoW(string filename, int handle, int size, byte[] data);

    [DllImport("version.dll", CharSet = CharSet.Unicode)]
    private static extern bool VerQueryValueW(byte[] block, string subblock, out IntPtr buffer, out int len);

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
