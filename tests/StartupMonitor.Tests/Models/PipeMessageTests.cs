using System.Text.Json;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>Verifies test scenario for PipeMessageTests.cs.</summary>
public class PipeMessageTests
{
    /// <summary>
    /// <summary>Verifies test scenario for PipeMessageTests.cs.</summary>
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
    /// <summary>Verifies test scenario for PipeMessageTests.cs.</summary>
    /// </summary>
    [Fact]
    public void MessageTypes_AllUnique()
    {
        // Verifies component behavior and assertions.
        var values = typeof(PipeMessageTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.NotEmpty(values);
    }

    /// <summary>
    /// <summary>Verifies test scenario for PipeMessageTests.cs.</summary>
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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
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
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void StrictModePayload_JsonRoundTrip()
    {
        var payload = new StrictModePayload(true);
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<StrictModePayload>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Enabled);
    }
}
