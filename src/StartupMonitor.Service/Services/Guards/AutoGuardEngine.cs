using System;
using System.IO;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services.Guards;

/// <summary>
/// Тип сработавшей политики защиты Auto-Guards.
/// </summary>
public enum AutoGuardPolicyType
{
    None = 0,
    TempLocation = 1,
    UnsignedBinary = 2,
    ScriptAutostart = 3,
    DangerousCli = 4,
    NewService = 5
}

/// <summary>
/// Результат оценки записи автозапуска автоматическими политиками защиты.
/// </summary>
public record PolicyMatchResult(
    AutoGuardPolicyType Policy,
    string PolicyKey,
    string RuleTitle,
    string Description
);

/// <summary>
/// Движок автоматических политик защиты автозапуска (Auto-Guards).
/// Реализует эвристическую и превентивную оценку новых записей автозапуска.
/// </summary>
public static class AutoGuardEngine
{
    /// <summary>
    /// Оценивает запись автозапуска на соответствие активным политикам защиты.
    /// Возвращает PolicyMatchResult при первом совпадении с активной политикой, либо null.
    /// </summary>
    public static PolicyMatchResult? Evaluate(StartupEntry entry, AppSettings settings)
    {
        using var _prof = ExecutionProfiler.Measure("Service.AutoGuard.Evaluate");
        if (entry == null || settings == null) return null;

        // Безусловный вайтлистинг системных компонентов и доверенных бинарников
        if (IsWhitelisted(entry)) return null;

        // 1. Политика: Опасные параметры командной строки (Base64, IEX downloaders, маскировка)
        if (settings.GuardBlockDangerousCli)
        {
            var threat = entry.ThreatInfo ?? CliHeuristicAnalyzer.Analyze(entry.CommandLine, entry.FilePath);
            if (threat != null && threat.HasThreat && threat.ThreatLevel == CliThreatLevel.High)
            {
                return new PolicyMatchResult(
                    AutoGuardPolicyType.DangerousCli,
                    "dangerous_cli",
                    "Опасная командная строка (CLI Threat)",
                    threat.Description ?? "Обнаружен скрытый скрипт, сетевой загрузчик или опасные параметры командной строки"
                );
            }
        }

        // 2. Политика: Запуск из каталогов %TEMP%, %WINDIR%\Temp, Users\Public, PerfLogs
        if (settings.GuardBlockTempPath)
        {
            if (IsTempOrPublicLocation(entry.FilePath) || IsTempOrPublicLocation(entry.CommandLine))
            {
                return new PolicyMatchResult(
                    AutoGuardPolicyType.TempLocation,
                    "temp_path",
                    "Автозапуск из каталогов Temp / Public",
                    $"Исполняемый файл расположен в ненадежной временной директории: {entry.FilePath ?? entry.CommandLine}"
                );
            }
        }

        // 3. Политика: Скрипты и интерпретаторы (.bat, .vbs, .js, .ps1, mshta, wscript, cscript)
        if (settings.GuardBlockScripts)
        {
            if (IsScriptAutostart(entry))
            {
                return new PolicyMatchResult(
                    AutoGuardPolicyType.ScriptAutostart,
                    "script",
                    "Скрипт в автозагрузке",
                    $"Обнаружен запуск командного сценария или безфайлового скрипта: {entry.CommandLine ?? entry.FilePath}"
                );
            }
        }

        // 4. Политика: Бинарники без подлинной цифровой подписи Authenticode
        if (settings.GuardBlockUnsigned)
        {
            if (IsUnsignedOrUntrusted(entry))
            {
                return new PolicyMatchResult(
                    AutoGuardPolicyType.UnsignedBinary,
                    "unsigned",
                    "Отсутствует цифровая подпись",
                    $"Исполняемый файл не имеет доверенной цифровой подписи Authenticode (статус: {entry.SignatureStatus})"
                );
            }
        }

        // 5. Политика: Регистрация новых несистемных служб Windows
        if (settings.GuardBlockNewServices)
        {
            if (entry.Location is EntryLocation.Service or EntryLocation.Driver)
            {
                return new PolicyMatchResult(
                    AutoGuardPolicyType.NewService,
                    "new_service",
                    "Регистрация службы Windows",
                    $"Попытка регистрации сторонней службы или драйвера: {entry.Name}"
                );
            }
        }

        return null;
    }

    /// <summary>
    /// Проверяет, является ли запись безусловно доверенной (системной или собственной).
    /// </summary>
    public static bool IsWhitelisted(StartupEntry entry)
    {
        if (entry == null) return true;

        // 1. Собственные компоненты StartupMonitor
        if (EntryOperations.IsSelfEntry(entry)) return true;

        // 2. Системные защищенные компоненты ядра Windows
        if (SystemProtectionHelper.IsProtectedSystemEntry(entry)) return true;

        // 3. Системные службы и драйверы
        if (entry.Location is EntryLocation.Service or EntryLocation.Driver)
        {
            var svcName = entry.LocationDetail ?? entry.Name ?? "";
            if (SystemProtectionHelper.IsCoreWindowsService(svcName)) return true;
        }

        // 4. Подписанные системные компоненты Microsoft в Windows/System32
        if (entry.SignatureStatus == DigitalSignatureStatus.Valid)
        {
            var signer = entry.SignerName ?? "";
            if (signer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                var path = entry.FilePath ?? "";
                if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Проверяет, указывает ли путь на временный или общедоступный каталог.
    /// </summary>
    public static bool IsTempOrPublicLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var lower = path.ToLowerInvariant();

        // Проверяем явные маркеры временных путей
        if (lower.Contains(@"\appdata\local\temp") ||
            lower.Contains(@"\windows\temp") ||
            lower.Contains(@":\users\public") ||
            lower.Contains(@":\perflogs"))
        {
            return true;
        }

        try
        {
            var tempDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            if (!string.IsNullOrEmpty(tempDir) && lower.Contains(tempDir))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Проверяет, представляет ли запись запуск скрипта или интерпретатора.
    /// </summary>
    public static bool IsScriptAutostart(StartupEntry entry)
    {
        if (entry == null) return false;

        // WMI подписка на ActiveScript (VBScript/JScript)
        if (entry.Location == EntryLocation.WmiEventSubscription)
        {
            if (entry.RawData.TryGetValue("ConsumerType", out var cType) &&
                string.Equals(cType, "ActiveScriptEventConsumer", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Проверяем расширение целевого файла
        var path = entry.FilePath ?? "";
        if (!string.IsNullOrWhiteSpace(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".bat" or ".cmd" or ".vbs" or ".vbe" or ".js" or ".jse" or ".wsf" or ".wsh" or ".hta" or ".ps1")
            {
                return true;
            }
        }

        // Проверяем командную строку на вызовы интерпретаторов скриптов
        var cmd = (entry.CommandLine ?? "").ToLowerInvariant();
        if (cmd.Contains("wscript.exe") ||
            cmd.Contains("cscript.exe") ||
            cmd.Contains("mshta.exe") ||
            cmd.Contains(".bat") ||
            cmd.Contains(".cmd") ||
            cmd.Contains(".vbs") ||
            cmd.Contains(".js") ||
            cmd.Contains(".hta") ||
            (cmd.Contains("powershell") && (cmd.Contains("-file") || cmd.Contains(".ps1"))))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Проверяет, является ли бинарный файл автозапуска неподписанным или с недействительной подписью.
    /// </summary>
    public static bool IsUnsignedOrUntrusted(StartupEntry entry)
    {
        if (entry == null) return false;

        // Применяется только к исполняемым файлам
        var path = entry.FilePath;
        if (string.IsNullOrWhiteSpace(path)) return false;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".exe" or ".dll" or ".sys" or ".scr" or ".cpl" or ".ocx"))
        {
            return false;
        }

        // Валидная подпись пропускается
        if (entry.SignatureStatus == DigitalSignatureStatus.Valid)
        {
            return false;
        }

        // Запись ещё не прошла верификацию WinVerifyTrust (Unknown) — не блокируем легитимный файл до окончания проверки
        if (entry.SignatureStatus == DigitalSignatureStatus.Unknown)
        {
            return false;
        }

        // Если файл существует и имеет статус отличный от Valid (NotSigned, InvalidSignature, UntrustedRoot, Expired, Revoked)
        return true;
    }
}
