using StartupMonitor.UI.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Тесты для LogReaderService: чтение логов службы и UI, фильтрация, поиск последнего файла, ограничение записей.
/// </summary>
public class LogReaderServiceTests : IDisposable
{
    /// <summary>Временная директория для файлов логов.</summary>
    private readonly string _tempDir;

    public LogReaderServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Проверяет парсинг лога службы в правильном формате: дата-уровень-сообщение.
    /// </summary>
    [Fact]
    public void ReadServiceLog_ParsesCorrectFormat()
    {
        var logFile = Path.Combine(_tempDir, "service-2025-01-15.log");
        Directory.CreateDirectory(_tempDir);
        var lines = new[]
        {
            "2025-01-15 10:30:00.123 +03:00 [INFO] Service started",
            "2025-01-15 10:30:01.456 +03:00 [WARN] Something suspicious",
            "non-standard line without regex match"
        };
        File.WriteAllLines(logFile, lines);

        var result = LogReaderService.ReadServiceLog(logFile);

        Assert.Equal(3, result.Count);
        Assert.Equal("SERVICE", result[0].Source);
        Assert.Equal("INFO", result[0].Level);
        Assert.Equal("Service started", result[0].Message);
        Assert.Equal(new DateTime(2025, 1, 15, 10, 30, 0, 123), result[0].Timestamp);
        Assert.Equal("WARN", result[1].Level);
        Assert.Equal("Something suspicious", result[1].Message);
        Assert.Equal(DateTime.Today, result[2].Timestamp);
        Assert.Equal("", result[2].Level);
    }

    /// <summary>
    /// Проверяет парсинг лога UI: время-идентификатор-сообщение.
    /// </summary>
    [Fact]
    public void ReadUiLog_ParsesCorrectFormat()
    {
        var logFile = Path.Combine(_tempDir, "ui-debug.log");
        Directory.CreateDirectory(_tempDir);
        var today = new DateTime(2025, 1, 15);
        var lines = new[]
        {
            "10:30:00.123 [1] Started UI",
            "10:30:01.456 [2] Processing entries",
            "raw line without timestamp"
        };
        File.WriteAllLines(logFile, lines);

        var result = LogReaderService.ReadUiLog(logFile, today);

        Assert.Equal(3, result.Count);
        Assert.Equal("UI", result[0].Source);
        Assert.Equal("Started UI", result[0].Message);
        Assert.Equal(today.Add(TimeSpan.Parse("10:30:00.123")), result[0].Timestamp);
        Assert.Equal("UI", result[1].Source);
        Assert.Equal("Processing entries", result[1].Message);
        Assert.Equal(today, result[2].Timestamp);
        Assert.Equal("raw line without timestamp", result[2].Message);
    }

    /// <summary>
    /// Проверяет, что ReadServiceLog возвращает пустой список при отсутствии файла.
    /// </summary>
    [Fact]
    public void ReadServiceLog_ReturnsEmpty_WhenFileNotFound()
    {
        var result = LogReaderService.ReadServiceLog("nonexistent.log");

        Assert.Empty(result);
    }

    /// <summary>
    /// Проверяет, что ReadUiLog возвращает пустой список при отсутствии файла.
    /// </summary>
    [Fact]
    public void ReadUiLog_ReturnsEmpty_WhenFileNotFound()
    {
        var result = LogReaderService.ReadUiLog("nonexistent.log", DateTime.Today);

        Assert.Empty(result);
    }

    /// <summary>
    /// Проверяет, что FindLatestServiceLog находит самый новый файл среди нескольких.
    /// </summary>
    [Fact]
    public void FindLatestServiceLog_ReturnsNewestFile()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "service-2025-01-10.log"), "");
        File.WriteAllText(Path.Combine(_tempDir, "service-2025-01-15.log"), "");
        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(_tempDir, "service-2025-01-12.log"), "");

        var logDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, "service-2025-01-10.log"), "");
        File.WriteAllText(Path.Combine(logDir, "service-2025-01-15.log"), "");
        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(logDir, "service-2025-01-12.log"), "");

        var result = LogReaderService.FindLatestServiceLog(_tempDir);

        Assert.NotNull(result);
        Assert.Contains("service-2025-01-15", result);
    }

    /// <summary>
    /// Проверяет, что FindLatestServiceLog возвращает null при отсутствии папки logs.
    /// </summary>
    [Fact]
    public void FindLatestServiceLog_ReturnsNull_WhenNoLogsDir()
    {
        var result = LogReaderService.FindLatestServiceLog(_tempDir);

        Assert.Null(result);
    }

    /// <summary>
    /// Проверяет, что FindLatestServiceLog возвращает null при отсутствии файлов в папке logs.
    /// </summary>
    [Fact]
    public void FindLatestServiceLog_ReturnsNull_WhenNoLogFiles()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "logs"));

        var result = LogReaderService.FindLatestServiceLog(_tempDir);

        Assert.Null(result);
    }

    /// <summary>
    /// Проверяет, что ReadAllLogs комбинирует записи из логов службы и UI и сортирует по времени (новые — первые).
    /// </summary>
    [Fact]
    public void ReadAllLogs_CombinesAndSortsEntries()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "logs"));
        var serviceLog = Path.Combine(_tempDir, "logs", "service-2025-01-15.log");
        File.WriteAllLines(serviceLog, new[]
        {
            "2025-01-15 10:30:00.000 +03:00 [INFO] Service entry"
        });
        var uiLog = Path.Combine(_tempDir, "logs", "ui-debug.log");
        File.WriteAllLines(uiLog, new[]
        {
            "10:30:01.000 [1] UI entry"
        });
        var today = new DateTime(2025, 1, 15);

        var result = LogReaderService.ReadAllLogs(_tempDir);

        // Свежие записи — первыми (UI 10:30:01 новее SERVICE 10:30:00)
        Assert.Equal(2, result.Count);
        Assert.Equal("UI", result[0].Source);
        Assert.Equal("SERVICE", result[1].Source);
    }

    /// <summary>
    /// Проверяет, что ReadAllLogs ограничивает количество возвращаемых записей (макс. 2000).
    /// </summary>
    [Fact]
    public void ReadAllLogs_LimitsToMaxEntries()
    {
        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);
        var serviceLog = Path.Combine(logsDir, "service-2025-01-15.log");
        var lines = Enumerable.Range(0, 2500).Select(i =>
            $"2025-01-15 10:00:{i / 1000:D2}.{i % 1000:D3} +03:00 [INFO] Line {i}").ToArray();
        File.WriteAllLines(serviceLog, lines);

        var result = LogReaderService.ReadAllLogs(_tempDir);

        // Максимум 2000 свежих, самая новая — первой
        Assert.True(result.Count <= 2000);
        Assert.Equal("Line 2499", result[0].Message);
    }

    /// <summary>
    /// Проверяет, что фильтрация по пустой строке возвращает все записи.
    /// </summary>
    [Fact]
    public void Filter_ReturnsAll_WhenSearchEmpty()
    {
        var entries = new List<LogEntry>
        {
            new() { FullLine = "abc", Message = "abc", Level = "INFO", Source = "SERVICE" },
            new() { FullLine = "def", Message = "def", Level = "WARN", Source = "UI" }
        };

        var result = LogReaderService.Filter(entries, "");

        Assert.Equal(2, result.Count);
    }

    /// <summary>
    /// Проверяет, что фильтрация возвращает только записи, содержащие искомый текст.
    /// </summary>
    [Fact]
    public void Filter_ReturnsMatching()
    {
        var entries = new List<LogEntry>
        {
            new() { FullLine = "abc xyz", Message = "abc xyz", Level = "INFO", Source = "SERVICE" },
            new() { FullLine = "def", Message = "def", Level = "WARN", Source = "UI" }
        };

        var result = LogReaderService.Filter(entries, "xyz");

        Assert.Single(result);
        Assert.Equal("abc xyz", result[0].Message);
    }

    /// <summary>
    /// Проверяет, что фильтрация регистронезависима.
    /// </summary>
    [Fact]
    public void Filter_IsCaseInsensitive()
    {
        var entries = new List<LogEntry>
        {
            new() { FullLine = "Hello World", Message = "Hello World", Level = "INFO", Source = "SERVICE" }
        };

        var result = LogReaderService.Filter(entries, "hello");

        Assert.Single(result);
    }

    /// <summary>
    /// Проверяет парсинг лога UI с датой yyyy-MM-dd HH:mm:ss.fff.
    /// </summary>
    [Fact]
    public void ReadUiLog_ParsesFullDateFormat()
    {
        var logFile = Path.Combine(_tempDir, "ui-debug.log");
        Directory.CreateDirectory(_tempDir);
        var lines = new[]
        {
            "2026-09-14 22:30:00.123 [1] UI initialized",
            "2026-09-14 22:30:05.456 [2] Scanning completed"
        };
        File.WriteAllLines(logFile, lines);

        var result = LogReaderService.ReadUiLog(logFile, DateTime.Today);

        Assert.Equal(2, result.Count);
        Assert.Equal("UI", result[0].Source);
        Assert.Equal("UI initialized", result[0].Message);
        Assert.Equal(new DateTime(2026, 9, 14, 22, 30, 0, 123), result[0].Timestamp);
        Assert.Equal("Scanning completed", result[1].Message);
        Assert.Equal(new DateTime(2026, 9, 14, 22, 30, 5, 456), result[1].Timestamp);
    }

    /// <summary>
    /// Проверяет, что FindLatestServiceLogs возвращает до maxFiles самых свежих файлов в порядке убывания.
    /// </summary>
    [Fact]
    public void FindLatestServiceLogs_ReturnsDescendingOrder()
    {
        var logDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, "service-20260912.log"), "");
        File.WriteAllText(Path.Combine(logDir, "service-20260914.log"), "");
        File.WriteAllText(Path.Combine(logDir, "service-20260913.log"), "");
        File.WriteAllText(Path.Combine(logDir, "service-20260910.log"), "");

        var result = LogReaderService.FindLatestServiceLogs(_tempDir, 3);

        Assert.Equal(3, result.Count);
        Assert.Contains("service-20260914.log", result[0]);
        Assert.Contains("service-20260913.log", result[1]);
        Assert.Contains("service-20260912.log", result[2]);
    }

    /// <summary>Удаляет временные файлы теста.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
