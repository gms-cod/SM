using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using StartupMonitor.Service.Services.ProcessLifecycle;
using Xunit;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты для NativeProcessTraceSession и связанных структур ETW Win32.
/// Проверяет корректность P/Invoke структур, смещений полей, констант ядра,
/// сериализации lease-файла и граничных условий создания сессии.
/// </summary>
public sealed class NativeProcessTraceSessionTests
{
    #region Проверка P/Invoke структур и смещений Win32 (x64)

    /// <summary>
    /// Проверяет, что размер структуры WNODE_HEADER на платформе x64 равен ровно 48 байтам.
    /// </summary>
    [Fact]
    public void WNodeHeader_StructSize_MatchesWin32Sdk48Bytes()
    {
        var size = Marshal.SizeOf<NativeProcessTraceSession.WNODE_HEADER>();
        Assert.Equal(48, size);
    }

    /// <summary>
    /// Проверяет, что размер структуры EVENT_TRACE_PROPERTIES на платформе x64 равен ровно 120 байтам.
    /// </summary>
    [Fact]
    public void EventTraceProperties_StructSize_MatchesWin32Sdk120Bytes()
    {
        var size = Marshal.SizeOf<NativeProcessTraceSession.EVENT_TRACE_PROPERTIES>();
        Assert.Equal(120, size);
    }

    /// <summary>
    /// Проверяет точные смещения полей в EVENT_TRACE_PROPERTIES:
    /// LoggerThreadId (смещение 104), LogFileNameOffset (112), LoggerNameOffset (116).
    /// </summary>
    [Fact]
    public void EventTraceProperties_FieldOffsets_MatchWin32Sdk()
    {
        var offsetWnode = Marshal.OffsetOf<NativeProcessTraceSession.EVENT_TRACE_PROPERTIES>(nameof(NativeProcessTraceSession.EVENT_TRACE_PROPERTIES.Wnode));
        var offsetBufferSize = Marshal.OffsetOf<NativeProcessTraceSession.EVENT_TRACE_PROPERTIES>(nameof(NativeProcessTraceSession.EVENT_TRACE_PROPERTIES.BufferSize));
        var offsetLoggerThreadId = Marshal.OffsetOf<NativeProcessTraceSession.EVENT_TRACE_PROPERTIES>(nameof(NativeProcessTraceSession.EVENT_TRACE_PROPERTIES.LoggerThreadId));
        var offsetLogFileName = Marshal.OffsetOf<NativeProcessTraceSession.EVENT_TRACE_PROPERTIES>(nameof(NativeProcessTraceSession.EVENT_TRACE_PROPERTIES.LogFileNameOffset));
        var offsetLoggerName = Marshal.OffsetOf<NativeProcessTraceSession.EVENT_TRACE_PROPERTIES>(nameof(NativeProcessTraceSession.EVENT_TRACE_PROPERTIES.LoggerNameOffset));

        Assert.Equal(0, offsetWnode.ToInt32());
        Assert.Equal(48, offsetBufferSize.ToInt32());
        Assert.Equal(104, offsetLoggerThreadId.ToInt32());
        Assert.Equal(112, offsetLogFileName.ToInt32());
        Assert.Equal(116, offsetLoggerName.ToInt32());
    }

    #endregion

    #region Проверка констант ядра Win32 ETW

    /// <summary>
    /// Проверяет числовые значения ключевых констант ETW Windows SDK.
    /// </summary>
    [Fact]
    public void Win32Constants_MatchWindowsSdkValues()
    {
        Assert.Equal(0x00020000u, NativeProcessTraceSession.WNODE_FLAG_TRACED_GUID);
        Assert.Equal(0x02000000u, NativeProcessTraceSession.EVENT_TRACE_SYSTEM_LOGGER_MODE);
        Assert.Equal(0x00000100u, NativeProcessTraceSession.EVENT_TRACE_REAL_TIME_MODE);
        Assert.Equal(0x00000001u, NativeProcessTraceSession.EVENT_TRACE_FLAG_PROCESS);

        Assert.Equal(0u, NativeProcessTraceSession.EVENT_TRACE_CONTROL_QUERY);
        Assert.Equal(1u, NativeProcessTraceSession.EVENT_TRACE_CONTROL_STOP);
        Assert.Equal(2u, NativeProcessTraceSession.EVENT_TRACE_CONTROL_UPDATE);
        Assert.Equal(3u, NativeProcessTraceSession.EVENT_TRACE_CONTROL_FLUSH);

        Assert.Equal(0u, NativeProcessTraceSession.ERROR_SUCCESS);
        Assert.Equal(5u, NativeProcessTraceSession.ERROR_ACCESS_DENIED);
        Assert.Equal(183u, NativeProcessTraceSession.ERROR_ALREADY_EXISTS);
        Assert.Equal(4201u, NativeProcessTraceSession.ERROR_WMI_INSTANCE_NOT_FOUND);

        Assert.Equal("StartupMonitor.ProcessLifecycle", NativeProcessTraceSession.DefaultSessionName);
        Assert.Equal(@"Global\StartupMonitor.EtwSession.Mutex", NativeProcessTraceSession.MachineMutexName);
    }

    #endregion

    #region Проверка сериализации и инвариантов EtwSessionLease

    /// <summary>
    /// Проверяет корректность JSON-сериализации и десериализации данных подтверждения владения (lease).
    /// </summary>
    [Fact]
    public void EtwSessionLease_JsonRoundTrip_PreservesAllFields()
    {
        var sessionGuid = Guid.NewGuid();
        var creationTime = new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);
        var writtenAt = DateTime.UtcNow;

        var lease = new EtwSessionLease
        {
            Status = "Active",
            ProcessId = 12345,
            ProcessCreationTimeUtc = creationTime,
            SessionGuid = sessionGuid,
            SessionName = "StartupMonitor.ProcessLifecycle",
            ProductId = "StartupMonitor",
            MachineName = "TEST-PC",
            WrittenAtUtc = writtenAt
        };

        var json = JsonSerializer.Serialize(lease, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<EtwSessionLease>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Active", deserialized.Status);
        Assert.Equal(12345, deserialized.ProcessId);
        Assert.Equal(creationTime, deserialized.ProcessCreationTimeUtc);
        Assert.Equal(sessionGuid, deserialized.SessionGuid);
        Assert.Equal("StartupMonitor.ProcessLifecycle", deserialized.SessionName);
        Assert.Equal("StartupMonitor", deserialized.ProductId);
        Assert.Equal("TEST-PC", deserialized.MachineName);
    }

    #endregion

    #region Проверка NativeTraceStatistics и NativeTraceSessionException

    /// <summary>
    /// Проверяет инициализацию и свойства NativeTraceStatistics.
    /// </summary>
    [Fact]
    public void NativeTraceStatistics_PropertiesAndEquality()
    {
        var stats1 = new NativeTraceStatistics(16, 8, 0, 100, 0, 0);
        var stats2 = new NativeTraceStatistics(16, 8, 0, 100, 0, 0);
        var stats3 = new NativeTraceStatistics(16, 4, 2, 100, 0, 1);

        Assert.Equal(16u, stats1.NumberOfBuffers);
        Assert.Equal(8u, stats1.FreeBuffers);
        Assert.Equal(0u, stats1.EventsLost);
        Assert.Equal(100u, stats1.BuffersWritten);
        Assert.Equal(0u, stats1.LogBuffersLost);
        Assert.Equal(0u, stats1.RealTimeBuffersLost);

        Assert.Equal(stats1, stats2);
        Assert.NotEqual(stats1, stats3);
    }

    /// <summary>
    /// Проверяет, что NativeTraceSessionException сохраняет код ошибки Win32 и имя сессии.
    /// </summary>
    [Fact]
    public void NativeTraceSessionException_StoresErrorCodeAndSessionName()
    {
        var ex = new NativeTraceSessionException(183, "StartupMonitor.ProcessLifecycle", "Сессия уже существует");

        Assert.Equal(183u, ex.ErrorCode);
        Assert.Equal("StartupMonitor.ProcessLifecycle", ex.SessionName);
        Assert.Contains("183", ex.Message);
        Assert.Contains("StartupMonitor.ProcessLifecycle", ex.Message);
    }

    #endregion

    #region Проверка валидации входных аргументов

    /// <summary>
    /// Проверяет, что запуск сессии с пустым именем или пробелами вызывает ArgumentException.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NativeProcessTraceSession_Start_EmptyName_ThrowsArgumentException(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => NativeProcessTraceSession.Start(invalidName));
    }

    /// <summary>
    /// Проверяет, что TryStart с некорректным именем возвращает false и описание ошибки без падения.
    /// </summary>
    [Fact]
    public void NativeProcessTraceSession_TryStart_EmptyName_ReturnsFalseWithError()
    {
        var success = NativeProcessTraceSession.TryStart(out var session, out var errorMessage, "  ");

        Assert.False(success);
        Assert.Null(session);
        Assert.NotNull(errorMessage);
        Assert.Contains("ETW", errorMessage);
    }

    #endregion
}
