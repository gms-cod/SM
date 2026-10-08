using System.Text.Json;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>
/// Тесты для PipeMessage: сериализация/десериализация, уникальность типов сообщений, JSON-цикл StartupEntry.
/// </summary>
public class PipeMessageTests
{
    /// <summary>
    /// Проверяет полный цикл сериализации/десериализации PipeMessage.
    /// </summary>
    [Fact]
    public void SerializeDeserialize_RoundTrip()
    {
        var original = new PipeMessage
        {
            Type = PipeMessageTypes.EntryAdded,
            Payload = "{\"name\":\"test\"}",
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PipeMessage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Payload, deserialized.Payload);
    }

    /// <summary>
    /// Проверяет, что все строковые константы PipeMessageTypes уникальны (нет дубликатов).
    /// </summary>
    [Fact]
    public void MessageTypes_AllUnique()
    {
        // Все строковые константы протокола уникальны (рефлексия — не надо править при добавлении типов)
        var values = typeof(PipeMessageTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.NotEmpty(values);
    }

    /// <summary>
    /// Проверяет JSON-сериализацию StartupEntry с различными полями.
    /// </summary>
    [Fact]
    public void StartupEntry_JsonRoundTrip()
    {
        var entry = new StartupEntry
        {
            Name = "TestApp",
            FilePath = @"C:\Program Files\Test\app.exe",
            Publisher = "Corp",
            Location = EntryLocation.Service,
            Status = EntryStatus.Blocked,
            FromBlacklist = true
        };

        var json = JsonSerializer.Serialize(entry);
        var deserialized = JsonSerializer.Deserialize<StartupEntry>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(entry.Name, deserialized.Name);
        Assert.Equal(entry.FilePath, deserialized.FilePath);
        Assert.Equal(entry.Location, deserialized.Location);
        Assert.Equal(entry.Status, deserialized.Status);
        Assert.True(deserialized.FromBlacklist);
    }

    [Fact]
    public void CpuThrottlePayload_JsonRoundTrip()
    {
        var payload = new CpuThrottlePayload(true, 75);
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<CpuThrottlePayload>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Enabled);
        Assert.Equal(75, deserialized.Threshold);
    }

    [Fact]
    public void StrictModePayload_JsonRoundTrip()
    {
        var payload = new StrictModePayload(true);
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<StrictModePayload>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Enabled);
    }
}
