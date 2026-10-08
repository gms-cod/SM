using System;
using System.IO;
using System.Text.Json;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Wmi;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class WmiSubscriptionTests
{
    [Fact]
    public void WmiSubscriptionPayload_SerializationRoundtrip()
    {
        var payload = new WmiSubscriptionPayload
        {
            ConsumerType = "CommandLineEventConsumer",
            ConsumerName = "TestConsumer",
            CommandLineTemplate = "cmd.exe /c calc.exe",
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            WorkingDirectory = @"C:\Windows\System32",
            FilterName = "TestFilter",
            FilterQuery = "SELECT * FROM __InstanceCreationEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process'",
            FilterQueryLanguage = "WQL",
            FilterNamespace = @"root\cimv2"
        };

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<WmiSubscriptionPayload>(json);

        Assert.NotNull(restored);
        Assert.Equal("CommandLineEventConsumer", restored.ConsumerType);
        Assert.Equal("TestConsumer", restored.ConsumerName);
        Assert.Equal("cmd.exe /c calc.exe", restored.CommandLineTemplate);
        Assert.Equal(@"C:\Windows\System32\cmd.exe", restored.ExecutablePath);
        Assert.Equal("TestFilter", restored.FilterName);
        Assert.Equal(@"root\cimv2", restored.FilterNamespace);
    }

    [Fact]
    public void WmiDisablePayload_SerializationRoundtrip()
    {
        var disablePayload = new WmiDisablePayload(
            FilterPath: "__EventFilter.Name=\"EvilFilter\"",
            ConsumerPath: "CommandLineEventConsumer.Name=\"Evil\""
        );

        var json = JsonSerializer.Serialize(disablePayload);
        var restored = JsonSerializer.Deserialize<WmiDisablePayload>(json);

        Assert.NotNull(restored);
        Assert.Equal("__EventFilter.Name=\"EvilFilter\"", restored.FilterPath);
        Assert.Equal("CommandLineEventConsumer.Name=\"Evil\"", restored.ConsumerPath);
    }

    [Fact]
    public void SystemProtection_DetectsCoreWmiSubscriptions()
    {
        Assert.True(SystemProtectionHelper.IsProtectedWmiSubscription("SCM Event Log Consumer"));
        Assert.True(SystemProtectionHelper.IsProtectedWmiSubscription("SCM Event Log Filter"));
        Assert.True(SystemProtectionHelper.IsProtectedWmiSubscription("BTreeNodeFilter"));
        Assert.True(SystemProtectionHelper.IsProtectedWmiSubscription("TSLogonFilter"));
        Assert.True(SystemProtectionHelper.IsProtectedWmiSubscription("KernCapFilter"));

        Assert.False(SystemProtectionHelper.IsProtectedWmiSubscription("MyCustomMalwareConsumer"));
        Assert.False(SystemProtectionHelper.IsProtectedWmiSubscription("UpdaterWatcher"));
    }

    [Fact]
    public void SystemProtection_IsProtectedSystemEntry_WorksForWmi()
    {
        var entry = new StartupEntry
        {
            Name = "SCM Event Log Consumer",
            Location = EntryLocation.WmiEventSubscription,
            LocationDetail = "WMI EventSubscription: SCM Event Log Consumer"
        };

        Assert.True(SystemProtectionHelper.IsProtectedSystemEntry(entry));

        var userEntry = new StartupEntry
        {
            Name = "AdwareScriptConsumer",
            Location = EntryLocation.WmiEventSubscription,
            LocationDetail = "WMI EventSubscription: AdwareScriptConsumer"
        };

        Assert.False(SystemProtectionHelper.IsProtectedSystemEntry(userEntry));
    }

    [Fact]
    public void RemoveFromSystem_RejectsProtectedWmiEntry()
    {
        var entry = new StartupEntry
        {
            Name = "SCM Event Log Consumer",
            Location = EntryLocation.WmiEventSubscription,
            LocationDetail = "WMI EventSubscription: SCM Event Log Consumer"
        };

        var (success, error, payload) = EntryOperations.RemoveFromSystem(entry);

        Assert.False(success);
        Assert.Contains("защищённых системных компонентов", error);
        Assert.Null(payload);
    }

    [Fact]
    public void RestoreSubscription_WithNullOrInvalidPayload_ReturnsError()
    {
        var (s1, err1) = WmiSubscriptionHelper.RestoreSubscription(null);
        Assert.False(s1);
        Assert.NotNull(err1);

        var (s2, err2) = WmiSubscriptionHelper.RestoreSubscription("not a valid json");
        Assert.False(s2);
        Assert.NotNull(err2);
    }

    [Fact]
    public void ScanSubscriptions_SafeExecutionOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Сканирование WMI не должно бросать исключений
        var subs = WmiSubscriptionHelper.ScanSubscriptions();
        Assert.NotNull(subs);
        // На обычной Windows может быть 0 или несколько встроенных подписок (например SCM Event Log)
        foreach (var sub in subs)
        {
            Assert.Equal(EntryLocation.WmiEventSubscription, sub.Location);
            Assert.False(string.IsNullOrWhiteSpace(sub.Name));
            Assert.False(string.IsNullOrWhiteSpace(sub.Id));
        }
    }
}
