using System.IO;
using System.Text.RegularExpressions;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.UI.Services;

/// <summary>Сервис чтения логов службы и UI из файлов логов в папке logs/.</summary>
public static class LogReaderService
{
    /// <summary>Регулярное выражение для парсинга строки лога службы (дата со смещением, уровень, сообщение).</summary>
    private static readonly Regex ServiceLogRegex = new(
        @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w+)\] (.*)$",
        RegexOptions.Compiled);

    /// <summary>Регулярное выражение для парсинга строки лога UI с датой (yyyy-MM-dd HH:mm:ss.fff [thread] msg).</summary>
    private static readonly Regex UiLogWithDateRegex = new(
        @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[\d+\] (.*)$",
        RegexOptions.Compiled);

    /// <summary>Регулярное выражение для устаревшей строки лога UI без даты (HH:mm:ss.fff [thread] msg).</summary>
    private static readonly Regex UiLogRegex = new(
        @"^(\d{2}:\d{2}:\d{2}\.\d{3}) \[\d+\] (.*)$",
        RegexOptions.Compiled);

    /// <summary>Максимальное количество строк лога, загружаемых в память.</summary>
    private const int MaxLines = 2000;

    /// <summary>Читает все логи (службы + UI), сортирует по времени (свежие сверху) и обрезает до MaxLines.</summary>
    /// <param name="serviceDir">Корневая папка, в которой искать подпапку logs/.</param>
    /// <returns>Список записей лога, отсортированный от новых к старым.</returns>
    public static List<LogEntry> ReadAllLogs(string serviceDir)
    {
        var entries = new List<LogEntry>();
        var today = DateTime.Today;

        // Читаем до 3 последних файлов службы, чтобы при смене суток история не исчезала
        var serviceLogs = FindLatestServiceLogs(serviceDir, 3);
        foreach (var logFile in serviceLogs)
        {
            var serviceEntries = ReadServiceLog(logFile);
            entries.AddRange(serviceEntries);
            if (entries.Count >= MaxLines)
                break;
        }

        var uiLogPath = Path.Combine(serviceDir, "logs", "ui-debug.log");
        if (File.Exists(uiLogPath))
            entries.AddRange(ReadUiLog(uiLogPath, today));

        // Свежие записи — сверху
        entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

        if (entries.Count > MaxLines)
            entries = entries.Take(MaxLines).ToList();

        return entries;
    }

    /// <summary>Находит самый свежий файл лога службы по маске service-*.log.</summary>
    /// <param name="serviceDir">Корневая папка с подпапкой logs/.</param>
    /// <returns>Полный путь к файлу или null, если логов нет.</returns>
    public static string? FindLatestServiceLog(string serviceDir)
    {
        return FindLatestServiceLogs(serviceDir, 1).FirstOrDefault();
    }

    /// <summary>Находит последние файлы логов службы по маске service-*.log (от новых к старым).</summary>
    /// <param name="serviceDir">Корневая папка с подпапкой logs/.</param>
    /// <param name="maxFiles">Максимальное количество файлов для чтения.</param>
    /// <returns>Список путей к файлам логов.</returns>
    public static List<string> FindLatestServiceLogs(string serviceDir, int maxFiles = 3)
    {
        var logDir = Path.Combine(serviceDir, "logs");
        if (!Directory.Exists(logDir)) return [];

        var files = Directory.GetFiles(logDir, "service-*.log");
        if (files.Length == 0) return [];

        return files.OrderByDescending(f => f).Take(maxFiles).ToList();
    }

    /// <summary>Разбирает файл лога службы в коллекцию LogEntry с использованием ServiceLogRegex.</summary>
    /// <param name="filePath">Полный путь к файлу лога службы.</param>
    /// <returns>Список распарсенных записей лога.</returns>
    public static List<LogEntry> ReadServiceLog(string filePath)
    {
        var entries = new List<LogEntry>();
        if (!File.Exists(filePath)) return entries;

        var today = DateTime.Today;

        foreach (var line in ReadTail(filePath, MaxLines))
        {
            var match = ServiceLogRegex.Match(line);
            if (!match.Success)
            {
                entries.Add(new LogEntry
                {
                    Timestamp = today,
                    Source = "SERVICE",
                    Level = "",
                    Message = line,
                    FullLine = line
                });
                continue;
            }

            DateTime ts;
            if (DateTimeOffset.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dto))
            {
                ts = dto.DateTime;
            }
            else if (!DateTime.TryParse(match.Groups[1].Value, out ts))
            {
                ts = today;
            }

            entries.Add(new LogEntry
            {
                Timestamp = ts,
                Source = "SERVICE",
                Level = match.Groups[2].Value,
                Message = match.Groups[3].Value,
                FullLine = line
            });
        }

        return entries;
    }

    /// <summary>Разбирает файл лога UI в коллекцию LogEntry с использованием UiLogWithDateRegex или UiLogRegex.</summary>
    /// <param name="filePath">Полный путь к ui-debug.log.</param>
    /// <param name="today">Дата сегодня (для конструирования временной метки из устаревших строк без даты).</param>
    /// <returns>Список распарсенных записей лога.</returns>
    public static List<LogEntry> ReadUiLog(string filePath, DateTime today)
    {
        var entries = new List<LogEntry>();
        if (!File.Exists(filePath)) return entries;

        foreach (var line in ReadTail(filePath, MaxLines))
        {
            // Сначала проверяем новый формат с датой: yyyy-MM-dd HH:mm:ss.fff [thread] message
            var matchWithDate = UiLogWithDateRegex.Match(line);
            if (matchWithDate.Success)
            {
                if (DateTime.TryParse(matchWithDate.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fullTs))
                {
                    entries.Add(new LogEntry
                    {
                        Timestamp = fullTs,
                        Source = "UI",
                        Level = "",
                        Message = matchWithDate.Groups[2].Value,
                        FullLine = line
                    });
                    continue;
                }
            }

            // Устаревший формат без даты: HH:mm:ss.fff [thread] message
            var match = UiLogRegex.Match(line);
            if (!match.Success)
            {
                entries.Add(new LogEntry
                {
                    Timestamp = today,
                    Source = "UI",
                    Level = "",
                    Message = line,
                    FullLine = line
                });
                continue;
            }

            if (TimeSpan.TryParse(match.Groups[1].Value, out var ts))
            {
                // Для строк без даты: если время больше текущего времени суток и today == Сегодня,
                // значит запись была сделана вчера, а не в будущем сегодня
                var entryDate = today;
                if (today == DateTime.Today && ts > DateTime.Now.TimeOfDay)
                {
                    entryDate = today.AddDays(-1);
                }

                entries.Add(new LogEntry
                {
                    Timestamp = entryDate.Add(ts),
                    Source = "UI",
                    Level = "",
                    Message = match.Groups[2].Value,
                    FullLine = line
                });
            }
            else
            {
                entries.Add(new LogEntry
                {
                    Timestamp = today,
                    Source = "UI",
                    Level = "",
                    Message = line,
                    FullLine = line
                });
            }
        }

        return entries;
    }

    /// <summary>Фильтрует записи лога по текстовому запросу (поиск в FullLine, Message, Level, Source).</summary>
    /// <param name="entries">Исходный список записей.</param>
    /// <param name="searchText">Текст для поиска (регистронезависимый).</param>
    /// <returns>Отфильтрованный список.</returns>
    public static List<LogEntry> Filter(List<LogEntry> entries, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return entries;

        return entries.Where(e =>
            e.FullLine.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            e.Level.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            e.Source.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    /// <summary>Читает хвост файла (последние maxLines строк), используя общий доступ на чтение/запись.</summary>
    /// <param name="filePath">Полный путь к файлу.</param>
    /// <param name="maxLines">Максимальное количество возвращаемых строк.</param>
    /// <returns>Последние строки файла (с конца).</returns>
    private static IEnumerable<string> ReadTail(string filePath, int maxLines)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
            if (lines.Count > maxLines)
                lines.RemoveAt(0);
        }

        return lines;
    }
}
