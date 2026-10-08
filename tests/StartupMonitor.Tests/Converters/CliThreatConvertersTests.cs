using System.Globalization;
using System.Windows.Media;
using StartupMonitor.Shared.Models;
using StartupMonitor.UI.Converters;
using Xunit;

namespace StartupMonitor.Tests.Converters;

public class CliThreatConvertersTests
{
    [Fact]
    public void ThreatLevelToBackgroundConverter_ReturnsExpectedBrushes()
    {
        var converter = new ThreatLevelToBackgroundConverter();

        var highBrush = converter.Convert(CliThreatLevel.High, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        var medBrush = converter.Convert(CliThreatLevel.Medium, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        var lowBrush = converter.Convert(CliThreatLevel.Low, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        var noneBrush = converter.Convert(CliThreatLevel.None, typeof(Brush), null, CultureInfo.InvariantCulture) as Brush;

        Assert.NotNull(highBrush);
        Assert.Equal(0xEF, highBrush.Color.R);
        Assert.NotNull(medBrush);
        Assert.Equal(0xF5, medBrush.Color.R);
        Assert.NotNull(lowBrush);
        Assert.Equal(0x3B, lowBrush.Color.R);
        Assert.Equal(Brushes.Transparent, noneBrush);
    }

    [Fact]
    public void ThreatLevelToForegroundConverter_ReturnsExpectedBrushes()
    {
        var converter = new ThreatLevelToForegroundConverter();

        var highBrush = converter.Convert(CliThreatLevel.High, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        var medBrush = converter.Convert(CliThreatLevel.Medium, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        var lowBrush = converter.Convert(CliThreatLevel.Low, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        var noneBrush = converter.Convert(CliThreatLevel.None, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;

        Assert.NotNull(highBrush);
        Assert.Equal(0xF8, highBrush.Color.R);
        Assert.NotNull(medBrush);
        Assert.Equal(0xFB, medBrush.Color.R);
        Assert.NotNull(lowBrush);
        Assert.Equal(0x60, lowBrush.Color.R);
        Assert.NotNull(noneBrush);
        Assert.Equal(0x94, noneBrush.Color.R);
    }
}

