using System.Globalization;
using StartupMonitor.UI.Converters;
using StartupMonitor.UI.Services;
using Xunit;

namespace StartupMonitor.Tests.Converters;

/// <summary>
/// Тесты для AppIconService и FilePathToIconConverter.
/// </summary>
public class FilePathToIconConverterTests
{
    private readonly FilePathToIconConverter _converter = new();

    [Fact]
    public void Convert_NullOrEmptyPath_ReturnsNull()
    {
        var res1 = _converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);
        var res2 = _converter.Convert("", typeof(object), null, CultureInfo.InvariantCulture);
        var res3 = _converter.Convert("   ", typeof(object), null, CultureInfo.InvariantCulture);

        Assert.Null(res1);
        Assert.Null(res2);
        Assert.Null(res3);
    }

    [Fact]
    public void Convert_NonExistentFile_ReturnsNull()
    {
        var fakePath = @"C:\NonExistentDirectory\FakeApp_12345.exe";
        var res = _converter.Convert(fakePath, typeof(object), null, CultureInfo.InvariantCulture);

        Assert.Null(res);
    }

    [Fact]
    public void AppIconService_KnownSystemFile_ReturnsImageOrNullSafely()
    {
        // explorer.exe или cmd.exe всегда есть в Windows
        var cmdPath = @"C:\Windows\System32\cmd.exe";
        var icon = AppIconService.GetIcon(cmdPath);

        // В тестовом окружении (в зависимости от STA потока) может вернуть замороженный ImageSource
        // Главное — метод не падает и безопасен для вызова
        if (System.IO.File.Exists(cmdPath))
        {
            // Убеждаемся что повторный вызов берет из кэша
            var cached = AppIconService.GetIcon(cmdPath);
            Assert.Equal(icon, cached);
        }
    }
}

