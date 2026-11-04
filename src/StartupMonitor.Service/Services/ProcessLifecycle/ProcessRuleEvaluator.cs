// <copyright file="ProcessRuleEvaluator.cs" company="StartupMonitor">
// Copyright (c) StartupMonitor. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Результат сопоставления процесса с правилами чёрного списка.
/// </summary>
/// <param name="IsMatched">Признак совпадения с правилом, требующим принудительного завершения.</param>
/// <param name="MatchedRule">Первое совпавшее правило чёрного списка либо null.</param>
/// <param name="RejectionReason">Причина отклонения совпадения (например, "ProtectedProcess", "EmptyProcessName", "NoRuleMatched").</param>
public readonly record struct ProcessRuleMatchResult(
    bool IsMatched,
    BlacklistRule? MatchedRule,
    string? RejectionReason
);

/// <summary>
/// Иммутабельный снимок активных правил чёрного списка с отдельным номером поколения (Generation).
/// Позволяет атомарно сопоставлять правила из параллельных потоков и читателей ETW без гонок.
/// </summary>
public sealed class BlacklistRuleSnapshot
{
    /// <summary>
    /// Монотонный номер поколения снимка правил. Инкрементируется при любой мутации списка правил.
    /// </summary>
    public long Generation { get; }

    /// <summary>
    /// Неизменяемый список активных правил завершения (NotifyAndRemove + Path/PathWildcard).
    /// </summary>
    public IReadOnlyList<BlacklistRule> ActiveRules { get; }

    /// <summary>
    /// Инициализирует новый иммутабельный снимок правил.
    /// </summary>
    /// <param name="generation">Номер поколения правил.</param>
    /// <param name="rules">Полный перечень правил чёрного списка.</param>
    public BlacklistRuleSnapshot(long generation, IEnumerable<BlacklistRule>? rules)
    {
        Generation = generation;
        ActiveRules = rules?
            .Where(r => r.Action == BlacklistAction.NotifyAndRemove &&
                        r.MatchType is BlacklistMatchType.Path or BlacklistMatchType.PathWildcard)
            .ToList() ?? (IReadOnlyList<BlacklistRule>)Array.Empty<BlacklistRule>();
    }
}

/// <summary>
/// Выделенный детерминированный матчер правил чёрного списка для процессов.
/// Гарантирует абсолютную идентичность поведения (policy parity) между legacy polling-свипом и событиями ETW.
/// </summary>
public static class ProcessRuleEvaluator
{
    /// <summary>
    /// Проверяет, является ли процесс системным или защищённым от принудительного завершения.
    /// Сохраняет все существующие исключения для системных хостов и скриптовых интерпретаторов.
    /// </summary>
    /// <param name="processName">Имя процесса (с расширением или без).</param>
    /// <param name="processPath">Полный путь к исполняемому файлу процесса (если известен).</param>
    /// <returns>True, если процесс защищён и завершению не подлежит; иначе False.</returns>
    public static bool IsProtected(string processName, string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return true;
        }

        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

        if (EntryOperations.IsProtectedProcessName(nameWithExe) ||
            SystemProtectionHelper.IsHostProcess(nameWithExe) ||
            SystemProtectionHelper.IsHostProcess(processName))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(processPath))
        {
            if (EntryOperations.IsProtectedProcessName(processPath) ||
                SystemProtectionHelper.IsHostProcess(processPath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Оценивает процесс против набора активных правил чёрного списка.
    /// Принудительное завершение применяется ТОЛЬКО к правилам с действием NotifyAndRemove
    /// и типами сопоставления Path или PathWildcard.
    /// </summary>
    /// <param name="processName">Имя процесса.</param>
    /// <param name="processPath">Полный путь к образу процесса (может быть null при отсутствии доступа).</param>
    /// <param name="activeRules">Коллекция активных правил для сопоставления.</param>
    /// <returns>Результат оценки с указанием совпавшего правила или причины отказа.</returns>
    public static ProcessRuleMatchResult Evaluate(
        string processName,
        string? processPath,
        IEnumerable<BlacklistRule>? activeRules)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return new ProcessRuleMatchResult(false, null, "EmptyProcessName");
        }

        if (IsProtected(processName, processPath))
        {
            return new ProcessRuleMatchResult(false, null, "ProtectedProcess");
        }

        if (activeRules == null)
        {
            return new ProcessRuleMatchResult(false, null, "NoActiveRules");
        }

        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

        var testPath = processPath ?? nameWithExe;

        foreach (var rule in activeRules)
        {
            // Фильтрация неприменимых правил: только NotifyAndRemove и Path/PathWildcard
            if (rule.Action != BlacklistAction.NotifyAndRemove ||
                (rule.MatchType != BlacklistMatchType.Path && rule.MatchType != BlacklistMatchType.PathWildcard))
            {
                continue;
            }

            bool matched;
            if (!string.IsNullOrEmpty(processPath))
            {
                // При известном пути образа сопоставляем правило против реального пути и тестового пути
                matched = BlacklistManager.MatchesFilePath(rule, processPath)
                          || BlacklistManager.MatchesFilePath(rule, testPath);
            }
            else
            {
                // Если путь образа неизвестен (procPath == null), нельзя сопоставлять path-правила против testPath ("имя.exe"):
                // иначе маска вроде *chrome.exe убьёт легитимный Chrome в Program Files.
                // Без пути сопоставляем только правила уровня имени файла (без слэшей и двоеточий).
                matched = rule.Value != null
                          && !rule.Value.Contains('/') && !rule.Value.Contains('\\') && !rule.Value.Contains(':')
                          && BlacklistManager.MatchesFilePath(rule, testPath);
            }

            if (matched)
            {
                return new ProcessRuleMatchResult(true, rule, null);
            }
        }

        return new ProcessRuleMatchResult(false, null, "NoRuleMatched");
    }
}

