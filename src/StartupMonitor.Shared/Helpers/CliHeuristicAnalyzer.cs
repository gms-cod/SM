using System;
using System.Collections.Generic;
using System.IO;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Shared.Helpers;

/// <summary>
/// Статический сервис эвристического анализа командных строк и путей исполнения.
/// Выявляет техники скрытия, обхода безопасности, Base64-скрипты и сомнительные локации.
/// </summary>
public static class CliHeuristicAnalyzer
{
    /// <summary>
    /// Проводит полный эвристический анализ командной строки и пути файла.
    /// </summary>
    public static CliThreatInfo Analyze(string? commandLine, string? filePath = null)
    {
        var result = new CliThreatInfo();
        var indicators = new List<string>();

        string cmd = commandLine ?? string.Empty;
        string path = filePath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cmd) && string.IsNullOrWhiteSpace(path))
        {
            return result;
        }

        string fullText = string.IsNullOrEmpty(cmd) ? path : (string.IsNullOrEmpty(path) ? cmd : $"{path} {cmd}");

        // 1. Проверка закодированных Base64 команд PowerShell
        bool isBase64Encoded = CheckBase64Patterns(fullText, indicators);

        // 2. Проверка сетевых загрузчиков и инжекторов кода
        bool isDownloader = CheckDownloaderPatterns(fullText, indicators);

        // 3. Проверка скрытого исполнения окон (-w hidden, vbhide, mshta)
        bool isHiddenExecution = CheckHiddenExecutionPatterns(fullText, indicators);

        // 4. Проверка запуска из временных / подозрительных путей
        bool isSuspiciousLocation = CheckSuspiciousLocations(path, cmd, indicators);

        // 5. Проверка двойных маскировочных расширений (например, invoice.pdf.exe)
        bool isDoubleExtension = CheckDoubleExtension(path, cmd, indicators);

        // 6. Проверка обхода политик PowerShell ExecutionPolicy
        bool isBypassPolicy = CheckBypassPolicy(fullText, indicators);

        if (indicators.Count > 0)
        {
            result.HasThreat = true;
            result.Indicators = indicators;

            // Вычисляем итоговый уровень риска и формируем бейдж/описание
            if (isBase64Encoded || isDownloader || isDoubleExtension || (isHiddenExecution && isBypassPolicy))
            {
                result.ThreatLevel = CliThreatLevel.High;
                if (isBase64Encoded)
                {
                    result.BadgeText = "Base64 скрипт";
                    result.Description = "Обнаружена закодированная Base64-команда PowerShell, используемая для сокрытия полезной нагрузки.";
                }
                else if (isDownloader)
                {
                    result.BadgeText = "Сетевой загрузчик";
                    result.Description = "Командная строка содержит инструкции для скачивания и запуска кода из сети интернет (IEX/curl/bitsadmin).";
                }
                else if (isDoubleExtension)
                {
                    result.BadgeText = "Маскировка расширения";
                    result.Description = "Файл использует двойное расширение для маскировки исполняемого кода под документ или картинку.";
                }
                else
                {
                    result.BadgeText = "Скрытый обход политик";
                    result.Description = "Скрытый запуск скрипта в фоновом режиме с принудительным обходом политик выполнения Windows.";
                }
            }
            else if (isSuspiciousLocation || isHiddenExecution || isBypassPolicy)
            {
                result.ThreatLevel = CliThreatLevel.Medium;
                if (isSuspiciousLocation)
                {
                    result.BadgeText = "Запуск из Temp";
                    result.Description = "Программа запускается из временной папки или общедоступного каталога (Temp/Public/PerfLogs).";
                }
                else if (isHiddenExecution)
                {
                    result.BadgeText = "Скрытый запуск";
                    result.Description = "Процесс запускается со скрытием окна консоли (-WindowStyle Hidden / vbHide).";
                }
                else
                {
                    result.BadgeText = "Обход политик";
                    result.Description = "Используются параметры обхода стандартных политик безопасности PowerShell (-ExecutionPolicy Bypass).";
                }
            }
            else
            {
                result.ThreatLevel = CliThreatLevel.Low;
                result.BadgeText = "Аномалия CLI";
                result.Description = "Обнаружены нестандартные параметры командной строки.";
            }
        }

        return result;
    }

    /// <summary>CheckBase64Patterns: Выполняет операцию CheckBase64Patterns.</summary>
    /// <param name="text">Входные данные: значение параметра text (string).</param>
    /// <param name="indicators">Входные данные: значение параметра indicators (List<string>).</param>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool CheckBase64Patterns(string text, List<string> indicators)
    {
        bool found = false;
        string lower = text.ToLowerInvariant();

        if (lower.Contains("powershell") || lower.Contains("pwsh"))
        {
            if (lower.Contains(" -enc ") || lower.Contains(" /enc ") ||
                lower.Contains(" -encodedcommand ") || lower.Contains(" /encodedcommand ") ||
                lower.Contains(" -e ") || lower.Contains(" /e "))
            {
                indicators.Add("PowerShell: EncodedCommand/Base64 flag");
                found = true;
            }
        }

        if (lower.Contains("frombase64string") || lower.Contains("[convert]::frombase64"))
        {
            indicators.Add("Inline Base64 decoding");
            found = true;
        }

        return found;
    }

    /// <summary>CheckDownloaderPatterns: Выполняет операцию CheckDownloaderPatterns.</summary>
    /// <param name="text">Входные данные: значение параметра text (string).</param>
    /// <param name="indicators">Входные данные: значение параметра indicators (List<string>).</param>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool CheckDownloaderPatterns(string text, List<string> indicators)
    {
        bool found = false;
        string lower = text.ToLowerInvariant();

        if (lower.Contains("downloadstring") || lower.Contains("downloadfile") || lower.Contains("downloaddata"))
        {
            indicators.Add("WebClient/Net.HttpClient download invocation");
            found = true;
        }

        if (lower.Contains("invoke-expression") || lower.Contains("iex(") || lower.Contains("iex ") || lower.Contains("iex`"))
        {
            if (lower.Contains("http://") || lower.Contains("https://") || lower.Contains("download"))
            {
                indicators.Add("Invoke-Expression (IEX) with network download");
                found = true;
            }
        }

        if (lower.Contains("bitsadmin") && lower.Contains("/transfer"))
        {
            indicators.Add("bitsadmin /transfer file download");
            found = true;
        }

        if (lower.Contains("certutil") && (lower.Contains("-urlcache") || lower.Contains("/urlcache")))
        {
            indicators.Add("certutil living-off-the-land downloader");
            found = true;
        }

        return found;
    }

    /// <summary>CheckHiddenExecutionPatterns: Выполняет операцию CheckHiddenExecutionPatterns.</summary>
    /// <param name="text">Входные данные: значение параметра text (string).</param>
    /// <param name="indicators">Входные данные: значение параметра indicators (List<string>).</param>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool CheckHiddenExecutionPatterns(string text, List<string> indicators)
    {
        bool found = false;
        string lower = text.ToLowerInvariant();

        if (lower.Contains("-w hidden") || lower.Contains("-windowstyle hidden") ||
            lower.Contains("/windowstyle hidden") || lower.Contains("-window hidden"))
        {
            indicators.Add("Hidden window style execution (-w hidden)");
            found = true;
        }

        if (lower.Contains("vbhide"))
        {
            indicators.Add("VBScript/VBA vbHide execution");
            found = true;
        }

        if (lower.Contains("mshta") && (lower.Contains("vbscript:") || lower.Contains("javascript:")))
        {
            indicators.Add("mshta living-off-the-land script injection");
            found = true;
        }

        return found;
    }

    /// <summary>CheckSuspiciousLocations: Выполняет операцию CheckSuspiciousLocations.</summary>
    /// <param name="path">Входные данные: значение параметра path (string).</param>
    /// <param name="cmd">Входные данные: значение параметра cmd (string).</param>
    /// <param name="indicators">Входные данные: значение параметра indicators (List<string>).</param>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool CheckSuspiciousLocations(string path, string cmd, List<string> indicators)
    {
        bool found = false;
        string combined = $"{path} {cmd}".ToLowerInvariant();

        string[] suspiciousPaths = new[]
        {
            "\\appdata\\local\\temp",
            "\\windows\\temp",
            ":\\users\\public",
            ":\\perflogs"
        };

        foreach (var sp in suspiciousPaths)
        {
            if (combined.Contains(sp))
            {
                indicators.Add($"Execution from untrusted location: {sp}");
                found = true;
                break;
            }
        }

        return found;
    }

    /// <summary>CheckDoubleExtension: Выполняет операцию CheckDoubleExtension.</summary>
    /// <param name="path">Входные данные: значение параметра path (string).</param>
    /// <param name="cmd">Входные данные: значение параметра cmd (string).</param>
    /// <param name="indicators">Входные данные: значение параметра indicators (List<string>).</param>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool CheckDoubleExtension(string path, string cmd, List<string> indicators)
    {
        string target = !string.IsNullOrWhiteSpace(path) ? path : cmd;
        if (string.IsNullOrWhiteSpace(target)) return false;

        string clean = target.Trim('"', '\'', ' ');
        int spaceIdx = clean.IndexOf(' ');
        if (spaceIdx > 0 && !File.Exists(clean))
        {
            clean = clean.Substring(0, spaceIdx);
        }

        string fileName = Path.GetFileName(clean);
        if (string.IsNullOrEmpty(fileName)) return false;

        string[] dangerousExecs = new[] { ".exe", ".scr", ".vbs", ".cmd", ".bat", ".js", ".ps1" };
        string[] decoyExtensions = new[] { ".pdf.", ".doc.", ".docx.", ".xls.", ".xlsx.", ".jpg.", ".png.", ".txt.", ".zip." };

        string lowerName = fileName.ToLowerInvariant();
        foreach (var de in decoyExtensions)
        {
            if (lowerName.Contains(de))
            {
                foreach (var exe in dangerousExecs)
                {
                    if (lowerName.EndsWith(exe))
                    {
                        indicators.Add($"Double decoy extension: {fileName}");
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>CheckBypassPolicy: Выполняет операцию CheckBypassPolicy.</summary>
    /// <param name="text">Входные данные: значение параметра text (string).</param>
    /// <param name="indicators">Входные данные: значение параметра indicators (List<string>).</param>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool CheckBypassPolicy(string text, List<string> indicators)
    {
        bool found = false;
        string lower = text.ToLowerInvariant();

        if (lower.Contains("-executionpolicy bypass") || lower.Contains("-ep bypass") ||
            lower.Contains("/executionpolicy bypass") || lower.Contains("-ep unrestricted"))
        {
            indicators.Add("PowerShell ExecutionPolicy Bypass");
            found = true;
        }

        return found;
    }
}

