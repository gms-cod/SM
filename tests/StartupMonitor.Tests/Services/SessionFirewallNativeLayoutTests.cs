using System.Reflection;
using System.Runtime.InteropServices;
using StartupMonitor.Service.Services.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
public sealed class SessionFirewallNativeLayoutTests
{
    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    private static Type NativeType(string name)
    {
        var policy = typeof(NetworkConnectionAlertService).Assembly.GetType("StartupMonitor.Service.Services.Firewall.SessionFirewallPolicy")
            ?? throw new TypeLoadException("Не найден адаптер временных правил.");
        return policy.GetNestedType(name, BindingFlags.NonPublic)
            ?? throw new TypeLoadException($"Не найдена структура {name}.");
    }

    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    [Theory]
    [InlineData("NativeSession", 72)]
    [InlineData("NativeSublayer", 72)]
    [InlineData("NativeValue", 16)]
    [InlineData("NativeCondition", 40)]
    [InlineData("NativeAction", 20)]
    [InlineData("NativeFilter", 200)]
    public void NativeStructures_SizeMatchesWindowsSdkX64(string name, int expected)
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(expected, Marshal.SizeOf(NativeType(name)));
    }

    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    /// <summary>Verifies test scenario for SessionFirewallNativeLayoutTests.cs.</summary>
    [Theory]
    [InlineData("Action", 128)]
    [InlineData("ContextLow", 152)]
    [InlineData("Reserved", 168)]
    [InlineData("Id", 176)]
    [InlineData("EffectiveWeight", 184)]
    public void NativeFilter_FieldOffsetsMatchWindowsSdkX64(string field, int expected)
        => Assert.Equal(expected, Marshal.OffsetOf(NativeType("NativeFilter"), field).ToInt32());
}
