using System.Diagnostics;
using System.Text.Json;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
/// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
public class ProcessInstanceCommandGuardTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyProcessIdentity_CurrentProcessMatchingTime_ReturnsTrue()
    {
        using var currentProc = Process.GetCurrentProcess();
        int pid = currentProc.Id;
        long creationTimeUtc = currentProc.StartTime.ToFileTimeUtc();

        bool isValid = EntryOperations.VerifyProcessIdentity(pid, creationTimeUtc, out string? failureReason);

        Assert.True(isValid);
        Assert.Null(failureReason);

        // Verifies component behavior and assertions.
        var (checkValid, checkReason) = EntryOperations.CheckProcessIdentity(pid, creationTimeUtc);
        Assert.True(checkValid);
        Assert.Null(checkReason);

        // Verifies component behavior and assertions.
        Assert.True(EntryOperations.VerifyProcessIdentity(pid, creationTimeUtc));
    }

    /// <summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyProcessIdentity_MismatchedTimestamp_ReturnsFalseAndReason()
    {
        using var currentProc = Process.GetCurrentProcess();
        int pid = currentProc.Id;
        long wrongCreationTime = currentProc.StartTime.AddHours(-1).ToFileTimeUtc();

        bool isValid = EntryOperations.VerifyProcessIdentity(pid, wrongCreationTime, out string? failureReason);

        Assert.False(isValid);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, failureReason);

        var (checkValid, checkReason) = EntryOperations.CheckProcessIdentity(pid, wrongCreationTime);
        Assert.False(checkValid);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, checkReason);
    }

    /// <summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyProcessIdentity_NullExpectedTime_ReturnsTrue()
    {
        using var currentProc = Process.GetCurrentProcess();
        int pid = currentProc.Id;

        bool isValid = EntryOperations.VerifyProcessIdentity(pid, null, out string? failureReason);

        Assert.True(isValid);
        Assert.Null(failureReason);

        var (checkValid, checkReason) = EntryOperations.CheckProcessIdentity(pid, null);
        Assert.True(checkValid);
        Assert.Null(checkReason);
    }

    /// <summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyProcessIdentity_NonExistentPid_ReturnsFalseAndReason()
    {
        // Verifies component behavior and assertions.
        int nonExistentPid = 999_999;
        long dummyTimestamp = DateTime.UtcNow.ToFileTimeUtc();

        bool isValid = EntryOperations.VerifyProcessIdentity(nonExistentPid, dummyTimestamp, out string? failureReason);

        Assert.False(isValid);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, failureReason);

        // Verifies component behavior and assertions.
        bool isValidNegative = EntryOperations.VerifyProcessIdentity(-1, dummyTimestamp, out string? negativeReason);
        Assert.False(isValidNegative);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, negativeReason);
    }

    /// <summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// </summary>
    [Fact]
    public void VerifyProcessIdentity_AnyCreationTimeDifference_RejectsCommand()
    {
        using var currentProc = Process.GetCurrentProcess();
        int pid = currentProc.Id;
        long actualCreationTimeUtc = currentProc.StartTime.ToFileTimeUtc();

        foreach (long delta in new[] { -1L, 1L, -100_000L, 100_000L })
        {
            bool accepted = EntryOperations.VerifyProcessIdentity(pid, actualCreationTimeUtc + delta, out string? reason);
            Assert.False(accepted);
            Assert.Equal(EntryOperations.ProcessInstanceChangedReason, reason);
        }
    }

    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    [Fact]
    public void VerifyProcessHandleIdentity_UsesRetainedHandleAndExactCreationTime()
    {
        using var current = Process.GetCurrentProcess();
        long creation = current.StartTime.ToFileTimeUtc();

        Assert.True(EntryOperations.VerifyProcessHandleIdentity(current.Handle, creation, out var exactReason));
        Assert.Null(exactReason);
        Assert.False(EntryOperations.VerifyProcessHandleIdentity(current.Handle, creation + 1, out var changedReason));
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, changedReason);
        Assert.True(EntryOperations.VerifyProcessIdentity(current, creation, out var retainedReason));
        Assert.Null(retainedReason);
    }

    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    [Fact]
    public void VerifyProcessHandleIdentity_InvalidHandle_RejectsInsteadOfUsingPidLookup()
    {
        Assert.False(EntryOperations.VerifyProcessHandleIdentity(IntPtr.Zero, DateTime.UtcNow.ToFileTimeUtc(), out var reason));
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, reason);
    }

    /// <summary>
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// KillProcessPayload, SuspendProcessPayload, ResumeProcessPayload, BlockProcessPayload
    /// <summary>Verifies test scenario for ProcessInstanceCommandGuardTests.cs.</summary>
    /// </summary>
    [Fact]
    public void ProcessCommandPayloads_PreserveExpectedCreationFileTimeUtc_InJson()
    {
        long sampleFileTime = 133_500_000_000_000_000L;

        // 1. KillProcessPayload
        var killPayload = new KillProcessPayload(1234, "testApp.exe", sampleFileTime);
        var killJson = JsonSerializer.Serialize(killPayload, JsonOptions);
        var killDeserialized = JsonSerializer.Deserialize<KillProcessPayload>(killJson, JsonOptions);
        Assert.NotNull(killDeserialized);
        Assert.Equal(1234, killDeserialized.ProcessId);
        Assert.Equal("testApp.exe", killDeserialized.ProcessName);
        Assert.Equal(sampleFileTime, killDeserialized.ExpectedCreationFileTimeUtc);

        // 2. SuspendProcessPayload
        var suspendPayload = new SuspendProcessPayload(5678, "suspendApp.exe", sampleFileTime);
        var suspendJson = JsonSerializer.Serialize(suspendPayload, JsonOptions);
        var suspendDeserialized = JsonSerializer.Deserialize<SuspendProcessPayload>(suspendJson, JsonOptions);
        Assert.NotNull(suspendDeserialized);
        Assert.Equal(5678, suspendDeserialized.ProcessId);
        Assert.Equal(sampleFileTime, suspendDeserialized.ExpectedCreationFileTimeUtc);

        // 3. ResumeProcessPayload
        var resumePayload = new ResumeProcessPayload(9012, "resumeApp.exe", sampleFileTime);
        var resumeJson = JsonSerializer.Serialize(resumePayload, JsonOptions);
        var resumeDeserialized = JsonSerializer.Deserialize<ResumeProcessPayload>(resumeJson, JsonOptions);
        Assert.NotNull(resumeDeserialized);
        Assert.Equal(9012, resumeDeserialized.ProcessId);
        Assert.Equal(sampleFileTime, resumeDeserialized.ExpectedCreationFileTimeUtc);

        // 4. BlockProcessPayload
        var blockPayload = new BlockProcessPayload(3456, @"C:\tools\app.exe", "app.exe", sampleFileTime);
        var blockJson = JsonSerializer.Serialize(blockPayload, JsonOptions);
        var blockDeserialized = JsonSerializer.Deserialize<BlockProcessPayload>(blockJson, JsonOptions);
        Assert.NotNull(blockDeserialized);
        Assert.Equal(3456, blockDeserialized.ProcessId);
        Assert.Equal(@"C:\tools\app.exe", blockDeserialized.FilePath);
        Assert.Equal(sampleFileTime, blockDeserialized.ExpectedCreationFileTimeUtc);

        // Verifies component behavior and assertions.
        var resultPayload = new ProcessActionResultPayload(false, EntryOperations.ProcessInstanceChangedReason, 1234);
        var resultJson = JsonSerializer.Serialize(resultPayload, JsonOptions);
        var resultDeserialized = JsonSerializer.Deserialize<ProcessActionResultPayload>(resultJson, JsonOptions);
        Assert.NotNull(resultDeserialized);
        Assert.False(resultDeserialized.Success);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, resultDeserialized.Message);
        Assert.Equal(1234, resultDeserialized.ProcessId);
    }
}
