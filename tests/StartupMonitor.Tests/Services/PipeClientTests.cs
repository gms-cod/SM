using StartupMonitor.UI.Services;

namespace StartupMonitor.Tests.Services;

public class PipeClientTests
{
    [Fact]
    public void Dispose_WithoutConnection_DoesNotThrow()
    {
        var client = new PipeClient();
        var exception = Record.Exception(() => client.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var client = new PipeClient();
        client.Dispose();
        var exception = Record.Exception(() => client.Dispose());
        Assert.Null(exception);
    }
}
