using System.Diagnostics;
using System.Text.Json;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты серверной защиты от выполнения деструктивных команд над повторно
/// использованным идентификатором процесса (PID Reuse Guard, Раздел 13.1 ETW_IMPLEMENTATION_PLAN.md).
/// </summary>
public class ProcessInstanceCommandGuardTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Проверяет, что при передаче точного (или совпадающего с погрешностью до 20 мс) времени создания
    /// текущего процесса верификация возвращает true и не заполняет failureReason.
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

        // Проверка удобной перегрузки с возвратом кортежа
        var (checkValid, checkReason) = EntryOperations.CheckProcessIdentity(pid, creationTimeUtc);
        Assert.True(checkValid);
        Assert.Null(checkReason);

        // Проверка булевой перегрузки
        Assert.True(EntryOperations.VerifyProcessIdentity(pid, creationTimeUtc));
    }

    /// <summary>
    /// Проверяет, что при несовпадении времени создания (чужой timestamp, например смещение на 1 час)
    /// верификация возвращает false с диагностической причиной ProcessInstanceChanged.
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
    /// Проверяет обратную совместимость: если ExpectedCreationFileTimeUtc равен null,
    /// верификация безусловно возвращает true (поддержка старых клиентов).
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
    /// Проверяет, что для заведомо несуществующего идентификатора процесса (PID) верификация
    /// возвращает false с причиной ProcessInstanceChanged.
    /// </summary>
    [Fact]
    public void VerifyProcessIdentity_NonExistentPid_ReturnsFalseAndReason()
    {
        // Выбираем заведомо свободный PID
        int nonExistentPid = 999_999;
        long dummyTimestamp = DateTime.UtcNow.ToFileTimeUtc();

        bool isValid = EntryOperations.VerifyProcessIdentity(nonExistentPid, dummyTimestamp, out string? failureReason);

        Assert.False(isValid);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, failureReason);

        // Также отрицательный PID
        bool isValidNegative = EntryOperations.VerifyProcessIdentity(-1, dummyTimestamp, out string? negativeReason);
        Assert.False(isValidNegative);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, negativeReason);
    }

    /// <summary>
    /// Проверяет квантование времени: разница до 20 мс укладывается в допустимую погрешность Win32 FILETIME,
    /// а разница свыше 20 мс (например 50 мс) приводит к отклонению команды.
    /// </summary>
    [Fact]
    public void VerifyProcessIdentity_ToleranceBoundary_CheckedCorrectly()
    {
        using var currentProc = Process.GetCurrentProcess();
        int pid = currentProc.Id;
        long actualCreationTimeUtc = currentProc.StartTime.ToFileTimeUtc();

        // 10 мс погрешности (100 000 тактов FILETIME) — должно быть разрешено
        long timeWithinTolerance = actualCreationTimeUtc + (10L * 10_000L);
        Assert.True(EntryOperations.VerifyProcessIdentity(pid, timeWithinTolerance));

        // 50 мс погрешности (500 000 тактов FILETIME) — должно быть отклонено
        long timeOutsideTolerance = actualCreationTimeUtc + (50L * 10_000L);
        bool isValidOutside = EntryOperations.VerifyProcessIdentity(pid, timeOutsideTolerance, out string? reasonOutside);
        Assert.False(isValidOutside);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, reasonOutside);
    }

    /// <summary>
    /// Проверяет корректность сериализации и десериализации полезных нагрузок команд
    /// KillProcessPayload, SuspendProcessPayload, ResumeProcessPayload, BlockProcessPayload
    /// с сохранением опционального поля ExpectedCreationFileTimeUtc.
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

        // 5. ProcessActionResultPayload с причиной ProcessInstanceChanged
        var resultPayload = new ProcessActionResultPayload(false, EntryOperations.ProcessInstanceChangedReason, 1234);
        var resultJson = JsonSerializer.Serialize(resultPayload, JsonOptions);
        var resultDeserialized = JsonSerializer.Deserialize<ProcessActionResultPayload>(resultJson, JsonOptions);
        Assert.NotNull(resultDeserialized);
        Assert.False(resultDeserialized.Success);
        Assert.Equal(EntryOperations.ProcessInstanceChangedReason, resultDeserialized.Message);
        Assert.Equal(1234, resultDeserialized.ProcessId);
    }
}
