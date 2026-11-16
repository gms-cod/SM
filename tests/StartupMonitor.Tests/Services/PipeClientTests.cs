using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Provides operations for this member.
/// </summary>
public class PipeClientTests
{
    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Dispose_WithoutConnection_DoesNotThrow()
    {
        var client = new PipeClient();
        var exception = Record.Exception(() => client.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var client = new PipeClient();
        client.Dispose();
        var exception = Record.Exception(() => client.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    /// <summary>
    /// Provides operations for this member.
    /// </summary>
    public async Task RequestServiceShutdownAsync_WhenDisconnected_ReturnsFalse()
    {
        using var client = new PipeClient();
        var result = await client.RequestServiceShutdownAsync(100);
        Assert.False(result);
    }
}
