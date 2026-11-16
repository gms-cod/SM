using System.Globalization;
using StartupMonitor.UI.Converters;
using StartupMonitor.UI.Services;
using Xunit;

namespace StartupMonitor.Tests.Converters;

/// <summary>Verifies test scenario for FilePathToIconConverterTests.cs.</summary>
public class FilePathToIconConverterTests
{
    private readonly FilePathToIconConverter _converter = new();

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Convert_NonExistentFile_ReturnsPlaceholder()
    {
        // Verifies component behavior and assertions.
        var fakePath = @"C:\NonExistentDirectory\FakeApp_12345.exe";
        var res = _converter.Convert(fakePath, typeof(object), null, CultureInfo.InvariantCulture);

        Assert.Equal(AppIconService.Placeholder, res);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void AppIconService_KnownSystemFile_ReturnsImageOrNullSafely()
    {
        // Verifies component behavior and assertions.
        var cmdPath = @"C:\Windows\System32\cmd.exe";
        var icon = AppIconService.GetIcon(cmdPath);

        // Verifies component behavior and assertions.
        // Verifies component behavior and assertions.
        if (System.IO.File.Exists(cmdPath))
        {
            // Verifies component behavior and assertions.
            var cached = AppIconService.GetIcon(cmdPath);
            Assert.Equal(icon, cached);
        }
    }
}

