using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using StartupMonitor.Service.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет точное восстановление типов в собственном ключе HKCU вне автозапуска Windows.</summary>
public sealed class RegistryTypedRestoreTests
{
    /// <summary>Типизированный payload возвращает исходные байты, числа, строки и нераскрытые переменные.</summary>
    /// <param name="kind">Исходный тип значения Windows Registry.</param>
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
            // Только уникальный собственный ключ; пользовательские Run/RunOnce не затрагиваются.
            Registry.CurrentUser.DeleteSubKeyTree(relative, throwOnMissingSubKey: false);
        }
    }
}
