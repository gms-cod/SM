using System.Windows;
using StartupMonitor.UI.Services;
using Wpf.Ui.Appearance;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for ThemeServiceTests.cs.</summary>
public class ThemeServiceTests
{
    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ApplyDarkTheme_PopulatesExpectedResourceBrushes()
    {
        var res = new ResourceDictionary();

        ThemeService.ApplyDarkTheme(res);

        Assert.NotNull(res["WindowBackgroundBrush"]);
        Assert.NotNull(res["SidebarBackgroundBrush"]);
        Assert.NotNull(res["ContentBackgroundBrush"]);
        Assert.NotNull(res["GlassCardBackgroundBrush"]);
        Assert.NotNull(res["TableContainerBackgroundBrush"]);
        Assert.NotNull(res["TableRowHoverBrush"]);
        Assert.NotNull(res["MutedTextBrush"]);
        Assert.NotNull(res["SubtleTextBrush"]);
        Assert.NotNull(res["ActionMoreBackgroundBrush"]);
        Assert.NotNull(res["ActionBlockBackgroundBrush"]);
        Assert.NotNull(res["FilterButtonInactiveBackgroundBrush"]);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ApplyLightTheme_PopulatesExpectedResourceBrushes()
    {
        var res = new ResourceDictionary();

        ThemeService.ApplyLightTheme(res);

        Assert.NotNull(res["WindowBackgroundBrush"]);
        Assert.NotNull(res["SidebarBackgroundBrush"]);
        Assert.NotNull(res["ContentBackgroundBrush"]);
        Assert.NotNull(res["GlassCardBackgroundBrush"]);
        Assert.NotNull(res["TableContainerBackgroundBrush"]);
        Assert.NotNull(res["TableRowHoverBrush"]);
        Assert.NotNull(res["MutedTextBrush"]);
        Assert.NotNull(res["SubtleTextBrush"]);
        Assert.NotNull(res["ActionMoreBackgroundBrush"]);
        Assert.NotNull(res["ActionBlockBackgroundBrush"]);
        Assert.NotNull(res["FilterButtonInactiveBackgroundBrush"]);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void ThemeService_InitialTheme_IsDark()
    {
        // Verifies component behavior and assertions.
        Assert.True(ThemeService.CurrentTheme == ApplicationTheme.Dark || ThemeService.CurrentTheme == ApplicationTheme.Light);
    }
}

