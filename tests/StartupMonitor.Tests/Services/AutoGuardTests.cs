using System;
using StartupMonitor.Service.Services.Guards;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class AutoGuardTests
{
    [Fact]
    public void Evaluate_WhenNoPoliciesActive_ReturnsNull()
    {
        var settings = new AppSettings
        {
            GuardBlockDangerousCli = false,
            GuardBlockTempPath = false,
            GuardBlockScripts = false,
            GuardBlockUnsigned = false,
            GuardBlockNewServices = false
        };

        var entry = new StartupEntry
        {
            Name = "UnsignedTemp",
            FilePath = @"C:\Users\John\AppData\Local\Temp\dropper.exe",
            CommandLine = @"C:\Users\John\AppData\Local\Temp\dropper.exe",
            Location = EntryLocation.RegistryRun,
            SignatureStatus = DigitalSignatureStatus.NotSigned
        };

        var result = AutoGuardEngine.Evaluate(entry, settings);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_TempPathPolicy_MatchesTempLocations()
    {
        var settings = new AppSettings { GuardBlockTempPath = true };

        var tempEntry = new StartupEntry
        {
            Name = "TempDropper",
            FilePath = @"C:\Users\Admin\AppData\Local\Temp\update.exe",
            Location = EntryLocation.RegistryRun
        };

        var result = AutoGuardEngine.Evaluate(tempEntry, settings);
        Assert.NotNull(result);
        Assert.Equal(AutoGuardPolicyType.TempLocation, result.Policy);
        Assert.Equal("temp_path", result.PolicyKey);

        var publicEntry = new StartupEntry
        {
            Name = "PublicDropper",
            FilePath = @"C:\Users\Public\payload.exe",
            Location = EntryLocation.RegistryRun
        };

        var resultPublic = AutoGuardEngine.Evaluate(publicEntry, settings);
        Assert.NotNull(resultPublic);
        Assert.Equal(AutoGuardPolicyType.TempLocation, resultPublic.Policy);

        // Легитимная программа в Program Files не блокируется
        var cleanEntry = new StartupEntry
        {
            Name = "CleanApp",
            FilePath = @"C:\Program Files\App\app.exe",
            Location = EntryLocation.RegistryRun
        };
        Assert.Null(AutoGuardEngine.Evaluate(cleanEntry, settings));
    }

    [Fact]
    public void Evaluate_ScriptsPolicy_MatchesScriptExtensionsAndInterpreters()
    {
        var settings = new AppSettings { GuardBlockScripts = true };

        var batEntry = new StartupEntry
        {
            Name = "BatchScript",
            FilePath = @"C:\Tools\run.bat",
            CommandLine = @"cmd.exe /c C:\Tools\run.bat",
            Location = EntryLocation.StartupFolder
        };
        var resBat = AutoGuardEngine.Evaluate(batEntry, settings);
        Assert.NotNull(resBat);
        Assert.Equal(AutoGuardPolicyType.ScriptAutostart, resBat.Policy);

        var vbsEntry = new StartupEntry
        {
            Name = "VbsScript",
            CommandLine = @"wscript.exe //B C:\Scripts\silent.vbs",
            Location = EntryLocation.RegistryRun
        };
        var resVbs = AutoGuardEngine.Evaluate(vbsEntry, settings);
        Assert.NotNull(resVbs);
        Assert.Equal(AutoGuardPolicyType.ScriptAutostart, resVbs.Policy);

        var wmiScriptEntry = new StartupEntry
        {
            Name = "WmiActiveScript",
            Location = EntryLocation.WmiEventSubscription,
            RawData = { ["ConsumerType"] = "ActiveScriptEventConsumer" }
        };
        var resWmi = AutoGuardEngine.Evaluate(wmiScriptEntry, settings);
        Assert.NotNull(resWmi);
        Assert.Equal(AutoGuardPolicyType.ScriptAutostart, resWmi.Policy);
    }

    [Fact]
    public void Evaluate_UnsignedPolicy_MatchesUnsignedExecutables()
    {
        var settings = new AppSettings { GuardBlockUnsigned = true };

        var unsignedEntry = new StartupEntry
        {
            Name = "UnsignedTool",
            FilePath = @"C:\MyTools\tool.exe",
            Location = EntryLocation.RegistryRun,
            SignatureStatus = DigitalSignatureStatus.NotSigned
        };
        var res = AutoGuardEngine.Evaluate(unsignedEntry, settings);
        Assert.NotNull(res);
        Assert.Equal(AutoGuardPolicyType.UnsignedBinary, res.Policy);
        Assert.Equal("unsigned", res.PolicyKey);

        var invalidEntry = new StartupEntry
        {
            Name = "TamperedTool",
            FilePath = @"C:\MyTools\hacked.exe",
            Location = EntryLocation.RegistryRun,
            SignatureStatus = DigitalSignatureStatus.InvalidSignature
        };
        var resInvalid = AutoGuardEngine.Evaluate(invalidEntry, settings);
        Assert.NotNull(resInvalid);
        Assert.Equal(AutoGuardPolicyType.UnsignedBinary, resInvalid.Policy);

        // Подписанный валидный файл не блокируется
        var signedEntry = new StartupEntry
        {
            Name = "SignedApp",
            FilePath = @"C:\Program Files\Vendor\app.exe",
            Location = EntryLocation.RegistryRun,
            SignatureStatus = DigitalSignatureStatus.Valid
        };
        Assert.Null(AutoGuardEngine.Evaluate(signedEntry, settings));
    }

    [Fact]
    public void Evaluate_DangerousCliPolicy_MatchesBase64AndDownloaders()
    {
        var settings = new AppSettings { GuardBlockDangerousCli = true };

        var cliEntry = new StartupEntry
        {
            Name = "PowershellEncoded",
            CommandLine = "powershell.exe -w hidden -enc JABzACAAPQAgAE4AZQB3AC0ATwBiAGoAZQBjAHQA",
            Location = EntryLocation.RegistryRun
        };

        var res = AutoGuardEngine.Evaluate(cliEntry, settings);
        Assert.NotNull(res);
        Assert.Equal(AutoGuardPolicyType.DangerousCli, res.Policy);
        Assert.Equal("dangerous_cli", res.PolicyKey);
    }

    [Fact]
    public void Evaluate_NewServicesPolicy_MatchesThirdPartyServices()
    {
        var settings = new AppSettings { GuardBlockNewServices = true };

        var thirdPartyService = new StartupEntry
        {
            Name = "CryptoMinerService",
            Location = EntryLocation.Service,
            LocationDetail = "CryptoMinerService",
            FilePath = @"C:\Tools\miner.exe"
        };

        var res = AutoGuardEngine.Evaluate(thirdPartyService, settings);
        Assert.NotNull(res);
        Assert.Equal(AutoGuardPolicyType.NewService, res.Policy);
        Assert.Equal("new_service", res.PolicyKey);
    }

    [Fact]
    public void Evaluate_SystemProtection_ExemptsCoreWindowsAndSelfComponents()
    {
        var allSettings = new AppSettings
        {
            GuardBlockDangerousCli = true,
            GuardBlockTempPath = true,
            GuardBlockScripts = true,
            GuardBlockUnsigned = true,
            GuardBlockNewServices = true
        };

        // 1. Системная служба Windows (rpcss)
        var rpcssEntry = new StartupEntry
        {
            Name = "rpcss",
            Location = EntryLocation.Service,
            LocationDetail = "rpcss",
            FilePath = @"C:\Windows\System32\svchost.exe"
        };
        Assert.Null(AutoGuardEngine.Evaluate(rpcssEntry, allSettings));

        // 2. Системный svchost.exe
        var svchostEntry = new StartupEntry
        {
            Name = "HostProcess",
            FilePath = @"C:\Windows\System32\svchost.exe",
            Location = EntryLocation.Service
        };
        Assert.Null(AutoGuardEngine.Evaluate(svchostEntry, allSettings));

        // 3. Собственные компоненты StartupMonitor
        var selfServiceEntry = new StartupEntry
        {
            Name = "StartupMonitor.Service",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.Service.exe",
            Location = EntryLocation.Service
        };
        Assert.Null(AutoGuardEngine.Evaluate(selfServiceEntry, allSettings));

        var selfUiEntry = new StartupEntry
        {
            Name = "StartupMonitor.UI",
            FilePath = @"C:\Program Files\StartupMonitor\StartupMonitor.UI.exe",
            Location = EntryLocation.RegistryRun
        };
        Assert.Null(AutoGuardEngine.Evaluate(selfUiEntry, allSettings));
    }
}
