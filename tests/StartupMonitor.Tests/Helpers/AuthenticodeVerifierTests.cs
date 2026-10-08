using System;
using System.IO;
using System.Text.Json;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Helpers;

public class AuthenticodeVerifierTests
{
    [Fact]
    public void VerifyFile_NullOrEmpty_ReturnsUnknown()
    {
        var res1 = AuthenticodeVerifier.VerifyFile(null);
        Assert.Equal(DigitalSignatureStatus.Unknown, res1.Status);
        Assert.False(res1.IsSigned);

        var res2 = AuthenticodeVerifier.VerifyFile("   ");
        Assert.Equal(DigitalSignatureStatus.Unknown, res2.Status);
        Assert.False(res2.IsSigned);
    }

    [Fact]
    public void VerifyFile_NonExistentFile_ReturnsUnknown()
    {
        var res = AuthenticodeVerifier.VerifyFile(@"C:\NonExistentFolder\NoSuchApp_" + Guid.NewGuid().ToString("N") + ".exe");
        Assert.Equal(DigitalSignatureStatus.Unknown, res.Status);
        Assert.False(res.IsSigned);
        Assert.Equal("Файл не найден", res.ErrorDetail);
    }

    [Fact]
    public void VerifyFile_UnsignedFile_ReturnsNotSigned()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"unsigned_test_{Guid.NewGuid():N}.exe");
        try
        {
            // Создаем фиктивный исполняемый файл без подписи
            File.WriteAllBytes(tempFile, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });

            var res = AuthenticodeVerifier.VerifyFile(tempFile);
            Assert.True(res.Status == DigitalSignatureStatus.NotSigned, $"Actual status: {res.Status}, error: {res.ErrorDetail}");
            Assert.False(res.IsSigned);
            Assert.Null(res.SignerName);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyFile_SignedWindowsBinary_ReturnsValidAndMicrosoftSigner()
    {
        string signedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!File.Exists(signedPath))
        {
            signedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        }

        if (File.Exists(signedPath))
        {
            var res = AuthenticodeVerifier.VerifyFile(signedPath);
            Assert.True(res.Status == DigitalSignatureStatus.Valid, $"Actual status: {res.Status}, error: {res.ErrorDetail}, signer: {res.SignerName}");
            Assert.True(res.IsSigned);
            Assert.NotNull(res.SignerName);
            Assert.Contains("Microsoft", res.SignerName, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void VerifyFile_UsesCacheForSameFile()
    {
        string signedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!File.Exists(signedPath))
        {
            signedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        }

        if (File.Exists(signedPath))
        {
            var res1 = AuthenticodeVerifier.VerifyFile(signedPath);
            var res2 = AuthenticodeVerifier.VerifyFile(signedPath);
            Assert.Same(res1, res2);
        }
    }

    [Fact]
    public void StartupEntry_SignatureProperties_SerializeCorrectly()
    {
        var entry = new StartupEntry
        {
            Name = "TestSignedApp",
            IsSigned = true,
            SignatureStatus = DigitalSignatureStatus.Valid,
            SignerName = "Microsoft Corporation",
            SignatureIssuer = "Microsoft Root Certificate Authority"
        };

        string json = JsonSerializer.Serialize(entry);
        var deserialized = JsonSerializer.Deserialize<StartupEntry>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsSigned);
        Assert.Equal(DigitalSignatureStatus.Valid, deserialized.SignatureStatus);
        Assert.Equal("Microsoft Corporation", deserialized.SignerName);
        Assert.Equal("Microsoft Root Certificate Authority", deserialized.SignatureIssuer);
    }

    [Fact]
    public void ProcessInfo_SignatureProperties_SerializeCorrectly()
    {
        var proc = new ProcessInfo
        {
            ProcessName = "test.exe",
            IsSigned = true,
            SignatureStatus = DigitalSignatureStatus.Valid,
            SignerName = "Google LLC",
            SignatureIssuer = "Google Trust Services"
        };

        string json = JsonSerializer.Serialize(proc);
        var deserialized = JsonSerializer.Deserialize<ProcessInfo>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsSigned);
        Assert.Equal(DigitalSignatureStatus.Valid, deserialized.SignatureStatus);
        Assert.Equal("Google LLC", deserialized.SignerName);
        Assert.Equal("Google Trust Services", deserialized.SignatureIssuer);
    }
}
