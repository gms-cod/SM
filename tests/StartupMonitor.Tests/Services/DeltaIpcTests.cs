using System.Collections.Generic;
using System.Text.Json;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class DeltaIpcTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void EntriesRevisionRequest_Serialization_PreservesClientRevision()
    {
        var req = new EntriesRevisionRequest(42);
        var json = JsonSerializer.Serialize(req, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<EntriesRevisionRequest>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(42, deserialized.ClientRevision);
    }

    [Fact]
    public void EntriesDeltaResponse_WhenNotModified_SerializesCorrectly()
    {
        var resp = new EntriesDeltaResponse(42, isModified: false, null);
        var json = JsonSerializer.Serialize(resp, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<EntriesDeltaResponse>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(42, deserialized.NewRevision);
        Assert.False(deserialized.IsModified);
        Assert.Null(deserialized.Entries);
    }

    [Fact]
    public void EntriesDeltaResponse_WhenModified_ContainsEntries()
    {
        var entries = new List<StartupEntry>
        {
            new() { Id = "test-1", Name = "TestApp", FilePath = @"C:\Program Files\Test\app.exe" }
        };

        var resp = new EntriesDeltaResponse(43, isModified: true, entries);
        var json = JsonSerializer.Serialize(resp, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<EntriesDeltaResponse>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(43, deserialized.NewRevision);
        Assert.True(deserialized.IsModified);
        Assert.NotNull(deserialized.Entries);
        Assert.Single(deserialized.Entries);
        Assert.Equal("TestApp", deserialized.Entries[0].Name);
    }
}

