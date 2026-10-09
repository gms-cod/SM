using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services.VirusScan;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты для сервиса проверки репутации Kaspersky OpenTIP.
/// </summary>
public class KasperskyScanServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonDataStore _store;

    public KasperskyScanServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"sm-kaspersky-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _store = new JsonDataStore(Path.Combine(_testDir, "startup-entries.json"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { }
    }

    [Fact]
    public void ParseKasperskyResponse_CleanFile_ParsesCorrectly()
    {
        var json = @"{
            ""Zone"": ""Green"",
            ""FileGeneralInfo"": {
                ""FileStatus"": ""Clean"",
                ""Sha256"": ""308CAEEE4C0706A8AB67C5E0AA0AC21926239711C5017561BE97E0C9DFBB01F3"",
                ""Signer"": ""Microsoft Corporation"",
                ""HitsCount"": 10000
            }
        }";

        var result = KasperskyScanService.ParseKasperskyResponse(json);

        Assert.True(result.IsFound);
        Assert.Equal("Green", result.Zone);
        Assert.Equal("Clean", result.FileStatus);
        Assert.Equal("Microsoft Corporation", result.Signer);
        Assert.Equal(10000, result.HitsCount);
        Assert.Empty(result.DetectionName);
    }

    [Fact]
    public void ParseKasperskyResponse_MalwareFile_ExtractsDetections()
    {
        var json = @"{
            ""Zone"": ""Red"",
            ""FileGeneralInfo"": {
                ""FileStatus"": ""Malware"",
                ""HitsCount"": 250
            },
            ""DetectionsInfo"": [
                {
                    ""Zone"": ""Red"",
                    ""DetectionName"": ""Trojan-Ransom.Win32.Wanna.m"",
                    ""DescriptionUrl"": ""https://threats.kaspersky.com/en/threat/Trojan-Ransom.Win32.Wanna""
                },
                {
                    ""Zone"": ""Yellow"",
                    ""DetectionName"": ""not-a-virus:AdWare.Win32.Test""
                }
            ]
        }";

        var result = KasperskyScanService.ParseKasperskyResponse(json);

        Assert.True(result.IsFound);
        Assert.Equal("Red", result.Zone);
        Assert.Equal("Malware", result.FileStatus);
        Assert.Equal("Trojan-Ransom.Win32.Wanna.m", result.DetectionName);
        Assert.Equal(250, result.HitsCount);
        Assert.Equal("https://threats.kaspersky.com/en/threat/Trojan-Ransom.Win32.Wanna", result.DescriptionUrl);
    }

    [Fact]
    public void ParseKasperskyResponse_InvalidJson_ReturnsErrorResult()
    {
        var json = "{ invalid json content ";

        var result = KasperskyScanService.ParseKasperskyResponse(json);

        Assert.False(result.IsFound);
        Assert.Equal("Grey", result.Zone);
        Assert.Equal("ParseError", result.FileStatus);
    }

    [Fact]
    public async Task ScanEntryAsync_OwnService_ReturnsGreenTrustedWithoutNetwork()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("Network must not be called for own service"));
        using var client = new HttpClient(handler);
        var service = new KasperskyScanService(_store, NullLogger<KasperskyScanService>.Instance, client);

        var entry = new StartupEntry
        {
            Name = "StartupMonitor",
            FilePath = "C:\\Program Files\\StartupMonitor\\StartupMonitor.Service.exe",
            SHA256 = "dummyhash"
        };

        var scanned = await service.ScanEntryAsync(entry);

        Assert.True(scanned);
        Assert.Equal("Green", entry.KasperskyZone);
        Assert.Equal("Trusted", entry.KasperskyFileStatus);
    }

    [Fact]
    public async Task ScanEntryAsync_CleanResponse_UpdatesEntry()
    {
        var cleanJson = @"{
            ""Zone"": ""Green"",
            ""FileGeneralInfo"": {
                ""FileStatus"": ""Clean"",
                ""Signer"": ""Google LLC"",
                ""HitsCount"": 50000
            }
        }";

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("x-api-key", req.Headers.Select(h => h.Key));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cleanJson)
            };
        });

        using var client = new HttpClient(handler);
        var service = new KasperskyScanService(_store, NullLogger<KasperskyScanService>.Instance, client);

        var entry = new StartupEntry
        {
            Name = "Chrome",
            FilePath = "C:\\Program Files\\Google\\Chrome\\chrome.exe",
            SHA256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        };

        var scanned = await service.ScanEntryAsync(entry);

        Assert.True(scanned);
        Assert.Equal("Green", entry.KasperskyZone);
        Assert.Equal("Clean", entry.KasperskyFileStatus);
        Assert.Empty(entry.KasperskyDetection);
    }

    [Fact]
    public async Task ScanEntryAsync_NotFoundResponse_SetsGreyNotFound()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var service = new KasperskyScanService(_store, NullLogger<KasperskyScanService>.Instance, client);

        var entry = new StartupEntry
        {
            Name = "CustomTool",
            FilePath = "C:\\Tools\\custom.exe",
            SHA256 = "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff"
        };

        var scanned = await service.ScanEntryAsync(entry);

        Assert.True(scanned);
        Assert.Equal("Grey", entry.KasperskyZone);
        Assert.Equal("NotFound", entry.KasperskyFileStatus);
        Assert.Empty(entry.KasperskyDetection);
    }

    [Fact]
    public async Task TestConnectionAsync_ValidKey_ReturnsSuccess()
    {
        var cleanJson = @"{
            ""Zone"": ""Green"",
            ""FileGeneralInfo"": { ""FileStatus"": ""Clean"" }
        }";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(cleanJson)
        });

        using var client = new HttpClient(handler);
        var service = new KasperskyScanService(_store, NullLogger<KasperskyScanService>.Instance, client);

        var result = await service.TestConnectionAsync("test-key-12345");

        Assert.True(result.Success);
        Assert.Equal("Green", result.Zone);
        Assert.Equal("Clean", result.Status);
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidKey_ReturnsAuthError()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = new HttpClient(handler);
        var service = new KasperskyScanService(_store, NullLogger<KasperskyScanService>.Instance, client);

        var result = await service.TestConnectionAsync("bad-key");

        Assert.False(result.Success);
        Assert.Contains("403", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_EmptyKey_ReturnsImmediateFailure()
    {
        var handler = new MockHttpMessageHandler(_ => throw new Exception("Must not be called"));
        using var client = new HttpClient(handler);
        var service = new KasperskyScanService(_store, NullLogger<KasperskyScanService>.Instance, client);

        var result = await service.TestConnectionAsync("");

        Assert.False(result.Success);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
