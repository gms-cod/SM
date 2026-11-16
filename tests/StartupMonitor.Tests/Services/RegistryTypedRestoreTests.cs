using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using StartupMonitor.Service.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for RegistryTypedRestoreTests.cs.</summary>
public sealed class RegistryTypedRestoreTests
{
    /// <summary>Verifies test scenario for RegistryTypedRestoreTests.cs.</summary>
    /// <summary>Verifies test scenario for RegistryTypedRestoreTests.cs.</summary>
    [Theory]
    [InlineData(RegistryValueKind.ExpandString)]
    [InlineData(RegistryValueKind.MultiString)]
    [InlineData(RegistryValueKind.Binary)]
    [InlineData(RegistryValueKind.DWord)]
    [InlineData(RegistryValueKind.QWord)]
    public void RestoreRegistry_PreservesOriginalType(RegistryValueKind kind)
    {
        var relative = @"Software\StartupMonitor.TypedRestoreTests\" + Guid.NewGuid().ToString("N");
        object expected = kind switch
        {
            RegistryValueKind.ExpandString => @"%TEMP%\Antigravity\app.exe",
            RegistryValueKind.MultiString => new[] { "first", "second" },
            RegistryValueKind.Binary => new byte[] { 0, 1, 127, 255 },
            RegistryValueKind.DWord => 123456,
            RegistryValueKind.QWord => long.MaxValue - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(relative);
            key.SetValue("Antigravity", expected, kind);
            var capture = typeof(EntryOperations).GetMethod("CaptureRegistryValue", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Не найден типизированный encoder.");
            var payload = Assert.IsType<EntryOperations.RegistryPayload>(capture.Invoke(null,
                new object[] { key, @"HKEY_CURRENT_USER\" + relative, "Antigravity" }));
            key.DeleteValue("Antigravity");
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

            var result = EntryOperations.RestoreRegistry(json);

            Assert.True(result.success, result.error);
            Assert.Equal(kind, key.GetValueKind("Antigravity"));
            var actual = key.GetValue("Antigravity", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (expected is byte[] bytes) Assert.Equal(bytes, Assert.IsType<byte[]>(actual));
            else if (expected is string[] strings) Assert.Equal(strings, Assert.IsType<string[]>(actual));
            else Assert.Equal(expected, actual);
        }
        finally
        {
            // Verifies component behavior and assertions.
            Registry.CurrentUser.DeleteSubKeyTree(relative, throwOnMissingSubKey: false);
        }
    }
}
