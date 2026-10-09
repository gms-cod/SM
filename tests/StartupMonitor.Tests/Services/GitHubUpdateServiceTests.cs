using System.Text.Json;
using StartupMonitor.Service.Services.Update;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

public class GitHubUpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V2.0.0", "2.0.0")]
    [InlineData("1.0.5", "1.0.5")]
    [InlineData("v1.3.0-beta.1", "1.3.0")]
    [InlineData("v1.4.0-rc2", "1.4.0")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void CleanVersionTag_NormalizesProperly(string? input, string expected)
    {
        var result = GitHubUpdateService.CleanVersionTag(input ?? "");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseSemVer_ParsesValidVersionStrings()
    {
        var v1 = GitHubUpdateService.ParseSemVer("1.2.3");
        Assert.Equal(new Version(1, 2, 3), v1);

        var v2 = GitHubUpdateService.ParseSemVer("2.1");
        Assert.Equal(new Version(2, 1, 0), v2);

        var v3 = GitHubUpdateService.ParseSemVer("3");
        Assert.Equal(new Version(3, 0, 0), v3);

        var vInvalid = GitHubUpdateService.ParseSemVer("non-numeric");
        Assert.Equal(new Version(0, 0, 0), vInvalid);
    }

    [Fact]
    public void SemVerComparison_CorrectlyDetectsNewerVersion()
    {
        var current = GitHubUpdateService.ParseSemVer("1.2.2");

        var older = GitHubUpdateService.ParseSemVer("1.2.1");
        var same = GitHubUpdateService.ParseSemVer("1.2.2");
        var newerMinor = GitHubUpdateService.ParseSemVer("1.2.3");
        var newerMajor = GitHubUpdateService.ParseSemVer("2.0.0");

        Assert.False(older > current);
        Assert.False(same > current);
        Assert.True(newerMinor > current);
        Assert.True(newerMajor > current);
    }

    [Fact]
    public void FindInstallerAsset_PicksStartupMonitorSetupExe()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "source-code.zip",
                    "browser_download_url": "https://github.com/.../source.zip",
                    "size": 1024
                },
                {
                    "name": "checksums.txt",
                    "browser_download_url": "https://github.com/.../checksums.txt",
                    "size": 256
                },
                {
                    "name": "StartupMonitorSetup-1.2.3.exe",
                    "browser_download_url": "https://github.com/.../StartupMonitorSetup-1.2.3.exe",
                    "size": 15728640
                }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var (url, name, size) = GitHubUpdateService.FindInstallerAsset(doc.RootElement);

        Assert.Equal("StartupMonitorSetup-1.2.3.exe", name);
        Assert.Equal("https://github.com/.../StartupMonitorSetup-1.2.3.exe", url);
        Assert.Equal(15728640, size);
    }

    [Fact]
    public void ExtractSha256Checksum_FindsChecksumInBody()
    {
        var body1 = "Release notes for v1.2.3\n\nSHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\nEnjoy!";
        using var doc1 = JsonDocument.Parse("{}");
        var hash1 = GitHubUpdateService.ExtractSha256Checksum(body1, doc1.RootElement);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash1);

        var body2 = "Checksum: sha-256 = AABBCCDD98FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        var hash2 = GitHubUpdateService.ExtractSha256Checksum(body2, doc1.RootElement);
        Assert.Equal("aabbccdd98fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash2);

        var bodyEmpty = "No checksums here.";
        var hashNone = GitHubUpdateService.ExtractSha256Checksum(bodyEmpty, doc1.RootElement);
        Assert.Null(hashNone);
    }

    [Fact]
    public void UpdateInfo_SerializationRoundTrip_MaintainsIntegrity()
    {
        var original = new UpdateInfo
        {
            Version = "1.3.0",
            Title = "Startup Monitor 1.3.0 Release",
            Changelog = "- Added auto-updates\n- Performance enhancements",
            ReleaseDate = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc),
            DownloadUrl = "https://github.com/gms-cod/SM/releases/download/v1.3.0/Setup.exe",
            FileName = "StartupMonitorSetup-1.3.0.exe",
            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            SizeBytes = 12345678,
            IsUpdateAvailable = true,
            CurrentVersion = "1.2.2"
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<UpdateInfo>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.Changelog, restored.Changelog);
        Assert.Equal(original.ReleaseDate, restored.ReleaseDate);
        Assert.Equal(original.DownloadUrl, restored.DownloadUrl);
        Assert.Equal(original.FileName, restored.FileName);
        Assert.Equal(original.Sha256, restored.Sha256);
        Assert.Equal(original.SizeBytes, restored.SizeBytes);
        Assert.Equal(original.IsUpdateAvailable, restored.IsUpdateAvailable);
        Assert.Equal(original.CurrentVersion, restored.CurrentVersion);
    }

    [Fact]
    public void UpdateSettingsPayload_RecordsCorrectValues()
    {
        var payload = new UpdateSettingsPayload(true, false, "1.2.2");

        Assert.True(payload.AutoCheckUpdates);
        Assert.False(payload.AutoDownloadUpdates);
        Assert.Equal("1.2.2", payload.SkippedUpdateVersion);

        var json = JsonSerializer.Serialize(payload);
        var restored = JsonSerializer.Deserialize<UpdateSettingsPayload>(json);

        Assert.NotNull(restored);
        Assert.Equal(payload.AutoCheckUpdates, restored.AutoCheckUpdates);
        Assert.Equal(payload.AutoDownloadUpdates, restored.AutoDownloadUpdates);
        Assert.Equal(payload.SkippedUpdateVersion, restored.SkippedUpdateVersion);
    }

    [Fact]
    public void UpdateProgressInfo_DefaultValuesAreConsistent()
    {
        var progress = new UpdateProgressInfo();
        Assert.Equal("Idle", progress.Status);
        Assert.Equal(0, progress.Percent);
        Assert.Equal(0, progress.BytesDownloaded);
        Assert.Equal(0, progress.TotalBytes);
        Assert.Null(progress.DownloadedFilePath);
        Assert.Null(progress.ErrorMessage);
    }
}

