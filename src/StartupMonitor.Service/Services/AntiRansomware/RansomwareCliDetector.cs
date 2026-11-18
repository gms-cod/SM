using StartupMonitor.Shared.Models.Ransomware;

namespace StartupMonitor.Service.Services.AntiRansomware;

/// <summary>
/// Результат статического анализа командной строки процесса на наличие паттернов вымогателей.
/// </summary>
/// <param name="IsThreat">Признак обнаружения деструктивной команды.</param>
/// <param name="MatchedPattern">Обнаруженный фрагмент или сигнатура команды.</param>
/// <param name="ThreatDescription">Подробное описание вредоносного действия на русском языке.</param>
/// <param name="TriggerType">Тип триггера защиты.</param>
public record RansomwareCliVerdict(
    bool IsThreat,
    string MatchedPattern,
    string ThreatDescription,
    RansomwareTriggerType TriggerType = RansomwareTriggerType.VssTamper);

/// <summary>
/// Статический анализатор командной строки процессов ядра для выявления попыток нейтрализации резервных копий и VSS.
/// Выявляет команды: vssadmin delete shadows, wmic shadowcopy delete, wbadmin delete catalog, bcdedit recoveryenabled no и др.
/// </summary>
public static class RansomwareCliDetector
{
    private record CliSignature(
        string Pattern,
        string Description,
        RansomwareTriggerType Trigger = RansomwareTriggerType.VssTamper,
        string[]? ProcessFilter = null);

    private static readonly CliSignature[] s_signatures =
    [
        new("delete shadowstorage", "Удаление хранилища теневых копий тома (vssadmin delete shadowstorage)."),
        new("shadowstorage delete", "Удаление хранилища теневых копий тома (vssadmin shadowstorage delete)."),
        new("delete shadows", "Удаление теневых копий томов через vssadmin/wmic (VSS Wipeout)."),
        new("shadowcopy delete", "Уничтожение теневых снимков дисков через WMI (wmic shadowcopy delete)."),
        new("wbadmin delete catalog", "Уничтожение каталога резервных копий Windows Backup (wbadmin delete catalog)."),
        new("wbadmin delete systemstatebackup", "Удаление резервных копий состояния системы (wbadmin delete systemstatebackup)."),
        new("recoveryenabled no", "Отключение автоматического восстановления системы (bcdedit recoveryenabled no)."),
        new("ignoreallfailures", "Игнорирование сбоев загрузки для подавления окна восстановления (bcdedit ignoreallfailures)."),
        new("win32_shadowcopy", "Попытка манипуляции теневыми копиями через PowerShell/WMI (Win32_ShadowCopy).", RansomwareTriggerType.VssTamper, ["powershell.exe", "pwsh.exe", "wmic.exe"]),
        new("resize-partition", "Попытка сжатия или порчи системного раздела через PowerShell.", RansomwareTriggerType.VssTamper, ["powershell.exe", "pwsh.exe"]),
        new("wevtutil cl security", "Очистка журнала событий безопасности Security для сокрытия следов шифрования.", RansomwareTriggerType.VssTamper),
        new("wevtutil cl system", "Очистка системного журнала событий System для сокрытия следов атаки.", RansomwareTriggerType.VssTamper)
    ];

    /// <summary>
    /// Проверяет командную строку на наличие сигнатур деструктивных действий программ-вымогателей.
    /// </summary>
    /// <param name="commandLine">Командная строка процесса или аргументы запуска.</param>
    /// <param name="matchedPattern">Обнаруженная сигнатура или пустая строка.</param>
    /// <param name="threatDescription">Описание угрозы или пустая строка.</param>
    /// <returns>True, если обнаружена атака на теневые копии или средства восстановления.</returns>
    public static bool IsRansomwareTamperCommand(
        string? commandLine,
        out string matchedPattern,
        out string threatDescription)
    {
        var verdict = Evaluate(null, commandLine);
        matchedPattern = verdict.MatchedPattern;
        threatDescription = verdict.ThreatDescription;
        return verdict.IsThreat;
    }

    /// <summary>
    /// Выполняет детальную оценку процесса и его командной строки на вредоносную активность против системы восстановления.
    /// </summary>
    /// <param name="processName">Имя исполняемого файла (например, vssadmin.exe, powershell.exe, cmd.exe).</param>
    /// <param name="commandLine">Полная командная строка процесса.</param>
    /// <returns>Структурированный вердикт анализатора.</returns>
    public static RansomwareCliVerdict Evaluate(string? processName, string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new RansomwareCliVerdict(false, string.Empty, string.Empty);
        }

        string cmd = commandLine.Trim();
        string normalizedCmd = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) >= 0
            ? cmd.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            : cmd;
        string pName = processName?.Trim().ToLowerInvariant() ?? string.Empty;

        for (int i = 0; i < s_signatures.Length; i++)
        {
            var sig = s_signatures[i];

            // Если для сигнатуры задан фильтр процессов, проверяем имя процесса
            if (sig.ProcessFilter != null && sig.ProcessFilter.Length > 0 && !string.IsNullOrEmpty(pName))
            {
                bool matchesFilter = false;
                for (int f = 0; f < sig.ProcessFilter.Length; f++)
                {
                    if (pName.Equals(sig.ProcessFilter[f], StringComparison.OrdinalIgnoreCase))
                    {
                        matchesFilter = true;
                        break;
                    }
                }

                if (!matchesFilter)
                {
                    continue;
                }
            }

            // Быстрый безаллокационный поиск подстроки без учёта регистра
            if (cmd.IndexOf(sig.Pattern, StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedCmd.IndexOf(sig.Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new RansomwareCliVerdict(true, sig.Pattern, sig.Description, sig.Trigger);
            }
        }

        return new RansomwareCliVerdict(false, string.Empty, string.Empty);
    }
}
