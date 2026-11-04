using System;
using System.IO;
using StartupMonitor.UI.ViewModels;
using Xunit;

namespace StartupMonitor.Tests.ViewModels;

[Collection("MainViewModelConfig")]
public class WindowStateSettingsTests : IDisposable
{
    private readonly string _tempConfigFile;

    public WindowStateSettingsTests()
    {
        _tempConfigFile = Path.Combine(Path.GetTempPath(), $"test-vm-windowstate-{Guid.NewGuid():N}.json");
        MainViewModel.CustomConfigPath = _tempConfigFile;
    }

    public void Dispose()
    {
        MainViewModel.CustomConfigPath = null;
        try
        {
            if (File.Exists(_tempConfigFile))
                File.Delete(_tempConfigFile);
        }
        catch { }
    }

    [Fact]
    public void LoadSetting_DefaultValues_WhenFileDoesNotExist()
    {
        var width = MainViewModel.LoadSetting("windowWidth", 1200.0);
        var height = MainViewModel.LoadSetting("windowHeight", 800.0);
        var state = MainViewModel.LoadSetting("windowState", "Normal");
        var tab = MainViewModel.LoadSetting("lastActiveTab", 0);

        Assert.Equal(1200.0, width);
        Assert.Equal(800.0, height);
        Assert.Equal("Normal", state);
        Assert.Equal(0, tab);
    }

    [Fact]
    public void SaveAndLoad_DoubleWindowDimensions_PersistCorrectly()
    {
        MainViewModel.SaveSetting("windowWidth", 1440.5);
        MainViewModel.SaveSetting("windowHeight", 900.25);
        MainViewModel.SaveSetting("windowLeft", 100.0);
        MainViewModel.SaveSetting("windowTop", 50.0);

        var width = MainViewModel.LoadSetting("windowWidth", 1200.0);
        var height = MainViewModel.LoadSetting("windowHeight", 800.0);
        var left = MainViewModel.LoadSetting("windowLeft", double.NaN);
        var top = MainViewModel.LoadSetting("windowTop", double.NaN);

        Assert.Equal(1440.5, width);
        Assert.Equal(900.25, height);
        Assert.Equal(100.0, left);
        Assert.Equal(50.0, top);
    }

    [Fact]
    public void SaveAndLoad_WindowState_PersistsMaximizedAndNormal()
    {
        MainViewModel.SaveSetting("windowState", "Maximized");
        var state = MainViewModel.LoadSetting("windowState", "Normal");
        Assert.Equal("Maximized", state);

        MainViewModel.SaveSetting("windowState", "Normal");
        state = MainViewModel.LoadSetting("windowState", "Normal");
        Assert.Equal("Normal", state);
    }

    [Fact]
    public void SaveAndLoad_LastActiveTab_PersistsCorrectIndex()
    {
        MainViewModel.SaveSetting("lastActiveTab", 6); // Диспетчер задач
        var tab = MainViewModel.LoadSetting("lastActiveTab", 0);
        Assert.Equal(6, tab);

        MainViewModel.SaveSetting("lastActiveTab", 7); // Время загрузки
        tab = MainViewModel.LoadSetting("lastActiveTab", 0);
        Assert.Equal(7, tab);
    }
}

