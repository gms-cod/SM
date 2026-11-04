using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using StartupMonitor.Service.Services.ProcessInspector;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class ProcessInspectorTests
{
    private readonly ProcessInspectorService _inspector = new();

    [Fact]
    public void InspectProcess_CurrentProcess_ReturnsModules()
    {
        int currentPid = Process.GetCurrentProcess().Id;
        var response = _inspector.InspectProcess(currentPid, includeHandles: false);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(currentPid, response.ProcessId);
        Assert.NotEmpty(response.Modules);

        // В любом .NET процессе загружены ntdll.dll или kernel32.dll
        var hasSystemModule = response.Modules.Any(m =>
            m.ModuleName.Equals("ntdll.dll", StringComparison.OrdinalIgnoreCase) ||
            m.ModuleName.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase) ||
            m.ModuleName.Contains("coreclr", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasSystemModule, "Должен присутствовать системный модуль (ntdll/kernel32/coreclr)");
    }

    [Fact]
    public void InspectProcess_CurrentProcess_AuthenticodeSignaturesChecked()
    {
        int currentPid = Process.GetCurrentProcess().Id;
        var response = _inspector.InspectProcess(currentPid, includeHandles: false);

        Assert.NotNull(response);
        var ntdll = response.Modules.FirstOrDefault(m => m.ModuleName.Equals("ntdll.dll", StringComparison.OrdinalIgnoreCase));
        if (ntdll != null && File.Exists(ntdll.FilePath))
        {
            Assert.True(ntdll.IsSigned);
            Assert.Equal(DigitalSignatureStatus.Valid, ntdll.SignatureStatus);
            Assert.True(ntdll.IsSystem);
            Assert.False(ntdll.IsTempPath);
        }
    }

    [Fact]
    public void InspectProcess_IncludeHandles_False_ReturnsNoHandles()
    {
        int currentPid = Process.GetCurrentProcess().Id;
        var response = _inspector.InspectProcess(currentPid, includeHandles: false);

        Assert.NotNull(response);
        Assert.Empty(response.Handles);
    }

    [Fact]
    public void InspectProcess_InvalidPid_HandlesGracefully()
    {
        var response = _inspector.InspectProcess(99999999, includeHandles: true);

        Assert.NotNull(response);
        Assert.Empty(response.Modules);
        Assert.Empty(response.Handles);
    }

    [Fact]
    public void ProcessModuleInfo_DisplaySize_FormatsCorrectly()
    {
        var m1 = new ProcessModuleInfo { ModuleSize = 500 };
        Assert.Equal("500 B", m1.DisplaySize);

        var m2 = new ProcessModuleInfo { ModuleSize = 2048 };
        Assert.Equal("2.0 KB", m2.DisplaySize);

        var m3 = new ProcessModuleInfo { ModuleSize = 10 * 1024 * 1024 };
        Assert.Equal("10.00 MB", m3.DisplaySize);

        var m4 = new ProcessModuleInfo { ModuleSize = 0 };
        Assert.Equal("—", m4.DisplaySize);
    }

    [Fact]
    public void ProcessHandleInfo_AccessString_FormatsCorrectly()
    {
        var h = new ProcessHandleInfo { GrantedAccess = 0x001F0003 };
        Assert.Equal("0x001F0003", h.AccessString);
    }
}
