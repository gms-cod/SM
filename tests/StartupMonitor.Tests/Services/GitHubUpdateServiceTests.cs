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
        var (url, name, size, _) = GitHubUpdateService.FindInstallerAsset(doc.RootElement);

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
    [Fact]
    public void UpdateAvailable_WhenServiceIsCurrent_ButClientIsOlder_IsAvailableForClient()
    {
        var remote = GitHubUpdateService.ParseSemVer("1.2.4");
        var serviceVer = GitHubUpdateService.ParseSemVer("1.2.4");
        var clientVer = GitHubUpdateService.ParseSemVer("1.2.3");

        var serviceAvailable = remote > serviceVer;
        var clientAvailable = remote > clientVer;
        var effectivelyAvailable = serviceAvailable || clientAvailable;

        Assert.False(serviceAvailable);
        Assert.True(clientAvailable);
        Assert.True(effectivelyAvailable);
    }

    [Fact]
    public void UpdateInfo_DtoSerialization_SupportsClientAndServiceVersion()
    {
        var info = new UpdateInfo
        {
            Version = "1.2.4",
            CurrentVersion = "1.2.4",
            ServiceVersion = "1.2.4",
            ClientVersion = "1.2.3",
            IsUpdateAvailable = true,
            IsUpdateAvailableForClient = true,
            Title = "Startup Monitor v1.2.4"
        };

        var json = JsonSerializer.Serialize(info);
        var restored = JsonSerializer.Deserialize<UpdateInfo>(json);

        Assert.NotNull(restored);
        Assert.Equal("1.2.4", restored.ServiceVersion);
        Assert.Equal("1.2.3", restored.ClientVersion);
        Assert.True(restored.IsUpdateAvailable);
        Assert.True(restored.IsUpdateAvailableForClient);
    }

    [Fact]
    public void CheckForUpdatesRequest_SerializationRoundTrip()
    {
        var req = new CheckForUpdatesRequest("1.2.3");
        var json = JsonSerializer.Serialize(req);
        var restored = JsonSerializer.Deserialize<CheckForUpdatesRequest>(json);

        Assert.NotNull(restored);
        Assert.Equal("1.2.3", restored.ClientVersion);
    }

    [Fact]
    public void CheckForUpdatesRequest_WithBuildDate_MaintainsDateIntegrity()
    {
        var buildDate = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        var req = new CheckForUpdatesRequest("1.4.0", buildDate);

        var json = JsonSerializer.Serialize(req);
        var restored = JsonSerializer.Deserialize<CheckForUpdatesRequest>(json);

        Assert.NotNull(restored);
        Assert.Equal("1.4.0", restored.ClientVersion);
        Assert.Equal(buildDate, restored.ClientBuildDateUtc);
    }

    [Fact]
    public void FindInstallerAsset_ExtractsAssetTimestamps()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "StartupMonitorSetup-1.4.0.exe",
                    "browser_download_url": "https://github.com/gms-cod/SM/releases/download/v1.4.0/StartupMonitorSetup-1.4.0.exe",
                    "size": 50000000,
                    "created_at": "2026-10-14T10:00:00Z",
                    "updated_at": "2026-10-14T12:30:00Z"
                }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var (url, name, size, updatedAt) = GitHubUpdateService.FindInstallerAsset(doc.RootElement);

        Assert.Equal("StartupMonitorSetup-1.4.0.exe", name);
        Assert.Equal(50000000, size);
        Assert.NotNull(updatedAt);
        Assert.Equal(new DateTime(2026, 10, 14, 12, 30, 0, DateTimeKind.Utc), updatedAt.Value);
    }

    [Fact]
    public void IsUpdateCandidate_WhenVersionIsSame_ReturnsFalseEvenIfRemoteInstallerDateIsNewer()
    {
        var remoteVer = new Version(1, 4, 0);
        var localVer = new Version(1, 4, 0);
        var localBuildDate = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
        var remoteInstallerDate = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        var candidate = GitHubUpdateService.IsUpdateCandidate(
            remoteVer, localVer, remoteInstallerDate, localBuildDate, out var isNewerByDate);

        Assert.False(candidate);
        Assert.False(isNewerByDate);
    }

    [Fact]
    public void IsUpdateCandidate_WhenVersionIsSame_AndRemoteInstallerIsOlder_ReturnsFalse()
    {
        var remoteVer = new Version(1, 4, 0);
        var localVer = new Version(1, 4, 0);
        var localBuildDate = new DateTime(2026, 10, 14, 14, 0, 0, DateTimeKind.Utc);
        var remoteInstallerDate = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        var candidate = GitHubUpdateService.IsUpdateCandidate(
            remoteVer, localVer, remoteInstallerDate, localBuildDate, out var isNewerByDate);

        Assert.False(candidate);
        Assert.False(isNewerByDate);
    }

    [Fact]
    public void IsUpdateCandidate_WhenVersionIsSame_AndDifferenceIsUnderThreshold_ReturnsFalse()
    {
        var remoteVer = new Version(1, 4, 0);
        var localVer = new Version(1, 4, 0);
        var localBuildDate = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var remoteInstallerDate = new DateTime(2026, 10, 14, 12, 1, 0, DateTimeKind.Utc); // всего 1 минута

        var candidate = GitHubUpdateService.IsUpdateCandidate(
            remoteVer, localVer, remoteInstallerDate, localBuildDate, out var isNewerByDate);

        Assert.False(candidate);
        Assert.False(isNewerByDate);
    }

    [Fact]
    public void IsUpdateCandidate_WhenRemoteVersionIsStrictlyNewer_ReturnsTrueWithoutDateFlag()
    {
        var remoteVer = new Version(1, 4, 1);
        var localVer = new Version(1, 4, 0);
        var localBuildDate = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var remoteInstallerDate = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);

        var candidate = GitHubUpdateService.IsUpdateCandidate(
            remoteVer, localVer, remoteInstallerDate, localBuildDate, out var isNewerByDate);

        Assert.True(candidate);
        Assert.False(isNewerByDate);
    }

    [Fact]
    public void UpdateInfo_Serialization_PreservesInstallerDateAndNewerByDate()
    {
        var info = new UpdateInfo
        {
            Version = "1.4.0",
            CurrentVersion = "1.4.0",
            InstallerDate = new DateTime(2026, 10, 14, 12, 30, 0, DateTimeKind.Utc),
            InstalledBuildDate = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc),
            IsNewerByDate = true,
            IsUpdateAvailable = true
        };

        var json = JsonSerializer.Serialize(info);
        var restored = JsonSerializer.Deserialize<UpdateInfo>(json);

        Assert.NotNull(restored);
        Assert.Equal(info.InstallerDate, restored.InstallerDate);
        Assert.Equal(info.InstalledBuildDate, restored.InstalledBuildDate);
        Assert.True(restored.IsNewerByDate);
        Assert.True(restored.IsUpdateAvailable);
    }
}
