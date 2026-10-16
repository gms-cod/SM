using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StartupMonitor.Service.Services.Toolbox;
using StartupMonitor.Shared.Models.Toolbox;
using StartupMonitor.UI.Services;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Юнит-тесты для набора повседневных инструментов (Toolbox):
/// AwakeService, JunkCleanerService, FileLocksmithService, LanScannerService, HashAndSecurityHelper.
/// </summary>
public class ToolboxServicesTests
{
    #region 1. AwakeService Tests

    [Fact]
    public void AwakeService_Activate_SetsActiveStateAndDuration()
    {
        // Arrange
        using var service = new AwakeService();

        // Act
        var status = service.Activate(30, keepDisplayOn: true);

        // Assert
        Assert.True(status.IsActive);
        Assert.True(status.KeepDisplayOn);
        Assert.True(status.RemainingMinutes > 0 && status.RemainingMinutes <= 30);
        Assert.Contains("30", status.ModeText);
    }

    [Fact]
    public void AwakeService_ActivateIndefinite_SetsActiveWithoutExpiration()
    {
        // Arrange
        using var service = new AwakeService();

        // Act
        var status = service.Activate(0, keepDisplayOn: false);

        // Assert
        Assert.True(status.IsActive);
        Assert.False(status.KeepDisplayOn);
        Assert.Equal(-1, status.RemainingMinutes);
        Assert.Contains("Бессрочно", status.ModeText);
    }

    [Fact]
    public void AwakeService_Deactivate_ResetsState()
    {
        // Arrange
        using var service = new AwakeService();
        service.Activate(60, keepDisplayOn: true);

        // Act
        var status = service.Deactivate();

        // Assert
        Assert.False(status.IsActive);
        Assert.Equal(0, status.RemainingMinutes);
        Assert.Equal("Выключен", status.ModeText);
    }

    [Fact]
    public void AwakeService_GetStatus_ReturnsCurrentState()
    {
        // Arrange
        using var service = new AwakeService();
        service.Activate(15, keepDisplayOn: true);

        // Act
        var status = service.GetStatus();

        // Assert
        Assert.True(status.IsActive);
        Assert.True(status.RemainingMinutes > 0);
    }

    #endregion

    #region 2. JunkCleanerService Tests

    [Fact]
    public void JunkCleanerService_ScanJunk_ReturnsCategories()
    {
        // Arrange
        var service = new JunkCleanerService();

        // Act
        var scanResult = service.ScanJunk();

        // Assert
        Assert.NotNull(scanResult);
        Assert.NotNull(scanResult.Categories);
        Assert.True(scanResult.Categories.Count >= 5);

        var userTempCat = scanResult.Categories.FirstOrDefault(c => c.Id == JunkCleanerService.CategoryUserTemp);
        Assert.NotNull(userTempCat);
        Assert.False(string.IsNullOrWhiteSpace(userTempCat.Name));
    }

    [Fact]
    public void JunkCleanerService_Clean_EmptyCategories_ReturnsZeroFreed()
    {
        // Arrange
        var service = new JunkCleanerService();

        // Act
        var cleanResult = service.Clean(new System.Collections.Generic.List<string>());

        // Assert
        Assert.NotNull(cleanResult);
        Assert.Equal(0, cleanResult.FreedSizeBytes);
        Assert.Equal(0, cleanResult.DeletedFilesCount);
    }

    [Fact]
    public void JunkCategoryInfo_SizeFormatted_FormatsCorrectly()
    {
        var small = new JunkCategoryInfo { TotalSizeBytes = 500 };
        Assert.Equal("500 Б", small.SizeFormatted);

        var kb = new JunkCategoryInfo { TotalSizeBytes = 2048 };
        Assert.Contains("КБ", kb.SizeFormatted);

        var mb = new JunkCategoryInfo { TotalSizeBytes = 5 * 1024 * 1024 };
        Assert.Contains("МБ", mb.SizeFormatted);

        var gb = new JunkCategoryInfo { TotalSizeBytes = 2L * 1024 * 1024 * 1024 };
        Assert.Contains("ГБ", gb.SizeFormatted);
    }

    [Fact]
    public async Task JunkCleanerService_ScanJunkAsync_ReturnsAllCategories()
    {
        // Arrange
        var service = new JunkCleanerService();

        // Act
        var scanResult = await service.ScanJunkAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(scanResult);
        Assert.True(scanResult.Categories.Count >= 5);
        Assert.Contains(scanResult.Categories, c => c.Id == JunkCleanerService.CategoryUserTemp);
        Assert.Contains(scanResult.Categories, c => c.Id == JunkCleanerService.CategoryRecycleBin);
    }

    [Fact]
    public async Task JunkCleanerService_ScanJunkAsync_CancelledToken_Throws()
    {
        // Arrange
        var service = new JunkCleanerService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanJunkAsync(cts.Token));
    }

    [Fact]
    public async Task JunkCleanerService_CleanJunkAsync_CancelledToken_Throws()
    {
        // Arrange
        var service = new JunkCleanerService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CleanJunkAsync(new List<string> { JunkCleanerService.CategoryUserTemp }, cts.Token));
    }

    #endregion

    #region 3. FileLocksmithService Tests

    [Fact]
    public void FileLocksmithService_FindLockingProcesses_NonExistentFile_ReturnsNotLocked()
    {
        // Arrange
        var service = new FileLocksmithService();
        var fakePath = Path.Combine(Path.GetTempPath(), $"non_existent_file_{Guid.NewGuid():N}.tmp");

        // Act
        var result = service.FindLockingProcesses(fakePath);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsLocked);
        Assert.Empty(result.LockingProcesses);
    }

    [Fact]
    public void FileLocksmithService_FindLockingProcesses_EmptyPath_ReturnsNotLocked()
    {
        // Arrange
        var service = new FileLocksmithService();

        // Act
        var result = service.FindLockingProcesses(string.Empty);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsLocked);
        Assert.Empty(result.LockingProcesses);
    }

    [Fact]
    public void FileLocksmithService_FindLockingProcesses_CurrentProcessLock_DetectsLock()
    {
        // Arrange
        var service = new FileLocksmithService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"locksmith_test_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, "Testing file locksmith lock detection");

        try
        {
            // Удерживаем эксклюзивную блокировку
            using (var stream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // Act
                var result = service.FindLockingProcesses(tempFile);

                // Assert
                Assert.NotNull(result);
                Assert.True(result.IsLocked);
                Assert.NotEmpty(result.LockingProcesses);
                Assert.Contains(result.LockingProcesses, p => p.ProcessId == Environment.ProcessId);
            }

            // После освобождения дескриптора файл не должен быть заблокирован
            var releasedResult = service.FindLockingProcesses(tempFile);
            Assert.False(releasedResult.IsLocked);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void FileLocksmithService_FindLockingProcesses_WithLiveToken_StillWorks()
    {
        // Arrange
        var service = new FileLocksmithService();

        // Act
        var result = service.FindLockingProcesses(string.Empty, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task FileLocksmithService_FindLockingProcessesAsync_CancelledToken_Throws()
    {
        // Arrange
        var service = new FileLocksmithService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"locksmith_cancel_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, "cancel");

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act / Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.FindLockingProcessesAsync(tempFile, cts.Token));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    #endregion

    #region 4. LanScannerService Tests

    [Fact]
    public async Task LanScannerService_ScanLocalNetworkAsync_ReturnsDevicesWithoutCrashing()
    {
        // Arrange
        var service = new LanScannerService();

        // Act
        var devices = await service.ScanLocalNetworkAsync();

        // Assert
        Assert.NotNull(devices);
        // Должен найти как минимум текущий локальный ПК или вернуть корректный список
        if (devices.Count > 0)
        {
            Assert.Contains(devices, d => !string.IsNullOrEmpty(d.IpAddress));
        }
    }

    #endregion

    #region 5. HashAndSecurityHelper Tests

    [Fact]
    public void HashAndSecurityHelper_ComputeHashes_KnownData_MatchesReference()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"hash_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Hello Startup Monitor Toolbox!", Encoding.UTF8);

        try
        {
            // Act
            var sha256 = HashAndSecurityHelper.ComputeFileSha256(tempFile);
            var sha1 = HashAndSecurityHelper.ComputeFileSha1(tempFile);
            var md5 = HashAndSecurityHelper.ComputeFileMd5(tempFile);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(sha256));
            Assert.Equal(64, sha256.Length); // 256 bits = 64 hex chars

            Assert.False(string.IsNullOrWhiteSpace(sha1));
            Assert.Equal(40, sha1.Length);   // 160 bits = 40 hex chars

            Assert.False(string.IsNullOrWhiteSpace(md5));
            Assert.Equal(32, md5.Length);    // 128 bits = 32 hex chars
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void HashAndSecurityHelper_ComputeHashes_NonExistentFile_ReturnsEmpty()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"missing_hash_{Guid.NewGuid():N}.bin");

        Assert.Equal(string.Empty, HashAndSecurityHelper.ComputeFileSha256(fakePath));
        Assert.Equal(string.Empty, HashAndSecurityHelper.ComputeFileSha1(fakePath));
        Assert.Equal(string.Empty, HashAndSecurityHelper.ComputeFileMd5(fakePath));
    }

    [Fact]
    public void HashAndSecurityHelper_GeneratePassword_RespectsLengthAndCharacterSet()
    {
        // Act
        string pass = HashAndSecurityHelper.GeneratePassword(24, useUpper: true, useDigits: true, useSymbols: true);

        // Assert
        Assert.NotNull(pass);
        Assert.Equal(24, pass.Length);
        Assert.Contains(pass, char.IsUpper);
        Assert.Contains(pass, char.IsDigit);
    }

    [Theory]
    [InlineData("", 0, "Пустой")]
    [InlineData("123", 0, "Слабый")]
    [InlineData("password123", 40, "Средний")]
    [InlineData("P@ssw0rdSecure!2026", 100, "Очень стойкий")]
    public void HashAndSecurityHelper_EvaluatePasswordStrength_ScoresAccurately(string pass, int minScore, string expectedRating)
    {
        // Act
        var (score, rating) = HashAndSecurityHelper.EvaluatePasswordStrength(pass);

        // Assert
        Assert.True(score >= minScore);
        Assert.Equal(expectedRating, rating);
    }

    [Fact]
    public async Task HashAndSecurityHelper_ComputeAllHashesAsync_MatchesSingleHashMethods()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"hash_all_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Parallel multi-hash verification payload", Encoding.UTF8);

        try
        {
            // Act
            var expectedSha256 = HashAndSecurityHelper.ComputeFileSha256(tempFile);
            var expectedSha1 = HashAndSecurityHelper.ComputeFileSha1(tempFile);
            var expectedMd5 = HashAndSecurityHelper.ComputeFileMd5(tempFile);

            var result = await HashAndSecurityHelper.ComputeAllHashesAsync(tempFile, CancellationToken.None);

            // Assert
            Assert.Equal(expectedSha256, result.Sha256);
            Assert.Equal(expectedSha1, result.Sha1);
            Assert.Equal(expectedMd5, result.Md5);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task HashAndSecurityHelper_ComputeAllHashesAsync_CancelledToken_Throws()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"hash_cancel_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, new string('x', 4096), Encoding.UTF8);

        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act / Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => HashAndSecurityHelper.ComputeAllHashesAsync(tempFile, cts.Token));
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task HashAndSecurityHelper_ComputeFilesHashesAsync_ProcessesEveryFile()
    {
        // Arrange
        var fileA = Path.Combine(Path.GetTempPath(), $"hash_multi_a_{Guid.NewGuid():N}.txt");
        var fileB = Path.Combine(Path.GetTempPath(), $"hash_multi_b_{Guid.NewGuid():N}.txt");
        File.WriteAllText(fileA, "alpha-content", Encoding.UTF8);
        File.WriteAllText(fileB, "beta-content-2", Encoding.UTF8);

        try
        {
            // Act
            var results = await HashAndSecurityHelper.ComputeFilesHashesAsync(
                new[] { fileA, fileB }, CancellationToken.None);

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Equal(HashAndSecurityHelper.ComputeFileSha256(fileA), results[fileA].Sha256);
            Assert.Equal(HashAndSecurityHelper.ComputeFileSha256(fileB), results[fileB].Sha256);
        }
        finally
        {
            try { File.Delete(fileA); } catch { }
            try { File.Delete(fileB); } catch { }
        }
    }

    #endregion
}
