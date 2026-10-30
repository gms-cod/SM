using System;
using System.IO;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.UI.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Набор автоматических тестов для Единой матрицы контроля доступа (ACL),
/// классификатора системных процессов Windows и моделей представления.
/// </summary>
public sealed class AccessControlMatrixTests
{
    [Theory]
    [InlineData("System", ProcessProtectionLevel.Critical)]
    [InlineData("svchost.exe", ProcessProtectionLevel.Critical)]
    [InlineData("lsass.exe", ProcessProtectionLevel.Critical)]
    [InlineData("services.exe", ProcessProtectionLevel.Critical)]
    [InlineData("csrss.exe", ProcessProtectionLevel.Critical)]
    [InlineData("StartupMonitor.Service.exe", ProcessProtectionLevel.Critical)]
    public void ProcessProtectionHelper_IdentifiesCriticalCoreProcesses(string procName, ProcessProtectionLevel expected)
    {
        var actual = ProcessProtectionHelper.EvaluateProtectionLevel(procName, $@"C:\Windows\System32\{procName}");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("explorer.exe", ProcessProtectionLevel.HighRisk)]
    [InlineData("spoolsv.exe", ProcessProtectionLevel.HighRisk)]
    [InlineData("dwm.exe", ProcessProtectionLevel.HighRisk)]
    [InlineData("RuntimeBroker.exe", ProcessProtectionLevel.HighRisk)]
    public void ProcessProtectionHelper_IdentifiesProtectedSubsystems(string procName, ProcessProtectionLevel expected)
    {
        var actual = ProcessProtectionHelper.EvaluateProtectionLevel(procName, $@"C:\Windows\System32\{procName}");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("chrome.exe", ProcessProtectionLevel.None)]
    [InlineData("telegram.exe", ProcessProtectionLevel.None)]
    [InlineData("curl.exe", ProcessProtectionLevel.None)]
    [InlineData("unknown_miner.exe", ProcessProtectionLevel.None)]
    public void ProcessProtectionHelper_IdentifiesUserApps(string procName, ProcessProtectionLevel expected)
    {
        var actual = ProcessProtectionHelper.EvaluateProtectionLevel(procName, $@"C:\Users\User\AppData\Local\{procName}");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AppAccessMatrixItemViewModel_CriticalProcess_CannotBlock()
    {
        var dto = new AppAccessMatrixItemDto
        {
            ProcessName = "svchost.exe",
            FullPath = @"C:\Windows\System32\svchost.exe",
            ProtectionLevel = ProcessProtectionLevel.Critical,
            State = AccessControlState.Allowed
        };

        var vm = new AppAccessMatrixItemViewModel(dto);

        Assert.True(vm.IsCriticalSystem);
        Assert.False(vm.IsProtectedSubsystem);
        Assert.False(vm.CanBlock, "Критический системный компонент ядра Windows не должен позволять аппаратную блокировку.");
        Assert.True(vm.IsAllowedChecked);
        Assert.False(vm.IsBlockedChecked);
    }

    [Fact]
    public void AppAccessMatrixItemViewModel_UserProcess_CanBlock()
    {
        var dto = new AppAccessMatrixItemDto
        {
            ProcessName = "telegram.exe",
            FullPath = @"C:\Program Files\Telegram\Telegram.exe",
            ProtectionLevel = ProcessProtectionLevel.None,
            State = AccessControlState.Prompt
        };

        var vm = new AppAccessMatrixItemViewModel(dto);

        Assert.False(vm.IsCriticalSystem);
        Assert.False(vm.IsProtectedSubsystem);
        Assert.True(vm.CanBlock, "Пользовательские программы должны свободно переключаться в режим блокировки.");
        Assert.True(vm.IsPromptChecked);
        Assert.False(vm.IsAllowedChecked);
        Assert.False(vm.IsBlockedChecked);

        // Переключение состояния
        vm.IsBlockedChecked = true;
        Assert.Equal(AccessControlState.Blocked, vm.State);
        Assert.True(vm.IsBlockedChecked);
        Assert.False(vm.IsPromptChecked);
    }

    [Fact]
    public void AppAccessMatrixItemViewModel_ScopeAndBadgeFormatting_Validates()
    {
        var dto = new AppAccessMatrixItemDto
        {
            ProcessName = "test.exe",
            FullPath = @"C:\Test\test.exe",
            Scope = AccessRuleScope.UntilRestart,
            BlockSource = BlockSourceType.SessionBlock,
            State = AccessControlState.Blocked
        };

        var vm = new AppAccessMatrixItemViewModel(dto);

        Assert.Contains("До перезагрузки", vm.ScopeDisplay);
        Assert.Contains("Сессионный блок", vm.SourceBadgeText);
    }

    [Fact]
    public void SetAppAccessStateRequest_ConstructsWithDefaults()
    {
        var req = new SetAppAccessStateRequest(@"C:\app.exe", "app.exe", AccessControlState.Allowed);
        Assert.Equal(@"C:\app.exe", req.ApplicationPath);
        Assert.Equal("app.exe", req.ProcessName);
        Assert.Equal(AccessControlState.Allowed, req.TargetState);
        Assert.Equal(AccessRuleScope.Permanent, req.Scope);
    }
}

