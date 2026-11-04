using System.IO.Pipes;
using System.Text.Json;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для PipeMessage, BlacklistRule и типов сообщений протокола обмена между UI и службой.
/// </summary>
public class PipeServerTests
{
    /// <summary>Настройки JSON-сериализации: регистронезависимые имена свойств.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Проверяет полный цикл сериализации/десериализации PipeMessage.
    /// </summary>
    [Fact]
    public void PipeMessage_SerializeDeserialize_RoundTrip()
    {
        var msg = new PipeMessage
        {
            Type = PipeMessageTypes.GetAllEntries,
            Payload = "test-payload",
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(msg, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(msg.Type, deserialized.Type);
        Assert.Equal(msg.Payload, deserialized.Payload);
    }

    /// <summary>
    /// Проверяет, что десериализация JSON с lowercase-ключами работает корректно.
    /// </summary>
    [Fact]
    public void PipeMessage_CaseInsensitive_Deserialization()
    {
        var json = """{"type":"GetAllEntries","payload":"test","timestamp":"2026-01-01T00:00:00Z"}""";
        var msg = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);

        Assert.NotNull(msg);
        Assert.Equal("GetAllEntries", msg.Type);
        Assert.Equal("test", msg.Payload);
    }

    /// <summary>
    /// Проверяет сериализацию списка StartupEntry как ответа на GetAllEntries.
    /// </summary>
    [Fact]
    public void GetAllEntriesResponse_Serializes_StartupEntryList()
    {
        var entries = new List<StartupEntry>
        {
            new() { Id = "1", Name = "TestApp", FilePath = "C:\\test.exe" },
            new() { Id = "2", Name = "TestApp2", FilePath = "D:\\test2.exe" }
        };

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<List<StartupEntry>>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Count);
        Assert.Equal("TestApp", deserialized[0].Name);
    }

    /// <summary>
    /// Проверяет полный цикл сериализации/десериализации BlacklistRule.
    /// </summary>
    [Fact]
    public void BlacklistRule_SerializeDeserialize_RoundTrip()
    {
        var rule = new BlacklistRule
        {
            Id = "rule1",
            Value = "C:\\bad\\*.exe",
            MatchType = BlacklistMatchType.PathWildcard,
            Action = BlacklistAction.NotifyAndRemove
        };

        var json = JsonSerializer.Serialize(rule, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BlacklistRule>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(rule.Value, deserialized.Value);
        Assert.Equal(BlacklistMatchType.PathWildcard, deserialized.MatchType);
        Assert.Equal(BlacklistAction.NotifyAndRemove, deserialized.Action);
    }

    /// <summary>
    /// Проверяет, что все константы типов сообщений PipeMessageTypes непустые.
    /// </summary>
    [Fact]
    public void PipeMessageTypes_AllDefined_AreNotEmpty()
    {
        Assert.NotEmpty(PipeMessageTypes.GetAllEntries);
        Assert.NotEmpty(PipeMessageTypes.GetAllEntriesResponse);
        Assert.NotEmpty(PipeMessageTypes.DisableEntry);
        Assert.NotEmpty(PipeMessageTypes.EnableEntry);
        Assert.NotEmpty(PipeMessageTypes.AddToBlacklist);
        Assert.NotEmpty(PipeMessageTypes.RemoveFromBlacklist);
        Assert.NotEmpty(PipeMessageTypes.GetBlacklist);
        Assert.NotEmpty(PipeMessageTypes.GetBlacklistResponse);
        Assert.NotEmpty(PipeMessageTypes.EntryAdded);
        Assert.NotEmpty(PipeMessageTypes.EntryModified);
        Assert.NotEmpty(PipeMessageTypes.EntryDeleted);
        Assert.NotEmpty(PipeMessageTypes.BlacklistMatch);
        Assert.NotEmpty(PipeMessageTypes.Error);
        Assert.NotEmpty(PipeMessageTypes.Success);
    }
}
