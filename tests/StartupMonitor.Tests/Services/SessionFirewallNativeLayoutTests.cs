using System.Reflection;
using System.Runtime.InteropServices;
using StartupMonitor.Service.Services.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет ABI управляемых WFP-структур против размеров и смещений Windows SDK x64 без нативных вызовов.</summary>
public sealed class SessionFirewallNativeLayoutTests
{
    /// <summary>Находит приватную структуру WFP без создания адаптера или открытия engine.</summary>
    /// <param name="name">Имя управляемого представления структуры.</param>
    /// <returns>Тип структуры из производственной сборки.</returns>
    private static Type NativeType(string name)
    {
        var policy = typeof(NetworkConnectionAlertService).Assembly.GetType("StartupMonitor.Service.Services.Firewall.SessionFirewallPolicy")
            ?? throw new TypeLoadException("Не найден адаптер временных правил.");
        return policy.GetNestedType(name, BindingFlags.NonPublic)
            ?? throw new TypeLoadException($"Не найдена структура {name}.");
    }

    /// <summary>Проверяет размеры структур, независимо подтверждённые C++ static_assert по Windows SDK x64.</summary>
    /// <param name="name">Имя управляемой структуры.</param>
    /// <param name="expected">Размер структуры в байтах.</param>
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

    /// <summary>Проверяет выравнивание action/context union и хвоста FWPM_FILTER0, от которых зависит корректность применения фильтра.</summary>
    /// <param name="field">Имя поля в управляемом FWPM_FILTER0.</param>
    /// <param name="expected">Смещение в байтах.</param>
    [Theory]
    [InlineData("Action", 128)]
    [InlineData("ContextLow", 152)]
    [InlineData("Reserved", 168)]
    [InlineData("Id", 176)]
    [InlineData("EffectiveWeight", 184)]
    public void NativeFilter_FieldOffsetsMatchWindowsSdkX64(string field, int expected)
        => Assert.Equal(expected, Marshal.OffsetOf(NativeType("NativeFilter"), field).ToInt32());
}
