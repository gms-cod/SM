using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Helpers;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services.Wmi;

/// <summary>
/// DTO для полного сохранения и восстановления подписки WMI (бэкап в RemovedEntry.RestorePayload).
/// </summary>
public class WmiSubscriptionPayload
{
    [JsonPropertyName("consumer_type")]
    public string ConsumerType { get; set; } = "CommandLineEventConsumer";

    [JsonPropertyName("consumer_name")]
    public string ConsumerName { get; set; } = "";

    [JsonPropertyName("command_line_template")]
    public string? CommandLineTemplate { get; set; }

    [JsonPropertyName("executable_path")]
    public string? ExecutablePath { get; set; }

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("scripting_engine")]
    public string? ScriptingEngine { get; set; }

    [JsonPropertyName("script_text")]
    public string? ScriptText { get; set; }

    [JsonPropertyName("script_file_name")]
    public string? ScriptFileName { get; set; }

    [JsonPropertyName("filter_name")]
    public string FilterName { get; set; } = "";

    [JsonPropertyName("filter_query")]
    public string FilterQuery { get; set; } = "";

    [JsonPropertyName("filter_query_language")]
    public string FilterQueryLanguage { get; set; } = "WQL";

    [JsonPropertyName("filter_namespace")]
    public string FilterNamespace { get; set; } = @"root\cimv2";

    [JsonPropertyName("binding_filter_path")]
    public string? BindingFilterPath { get; set; }

    [JsonPropertyName("binding_consumer_path")]
    public string? BindingConsumerPath { get; set; }
}

/// <summary>
/// DTO для отключения подписки WMI (сохранение пути привязки в StartupEntry.DisablePayload).
/// </summary>
public record WmiDisablePayload(string FilterPath, string ConsumerPath);

/// <summary>
/// Помощник для сканирования, мониторинга и операций над постоянными WMI-подписками на события (root\subscription).
/// Охватывает CommandLineEventConsumer, ActiveScriptEventConsumer, __FilterToConsumerBinding и __EventFilter.
/// </summary>
public static class WmiSubscriptionHelper
{
    private const string WmiNamespace = @"root\subscription";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <summary>
    /// Сканирует пространство имён root\subscription и возвращает список всех обнаруженных подписок на события.
    /// </summary>
    public static List<StartupEntry> ScanSubscriptions()
    {
        using var _prof = ExecutionProfiler.Measure("Service.Wmi.ScanSubscriptions");
        var results = new List<StartupEntry>();

        try
        {
            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();

            // 1. Считываем все фильтры событий (__EventFilter)
            var filters = new Dictionary<string, (string Query, string QueryLang, string EventNs, string RelPath)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var filterSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, Query, QueryLanguage, EventNamespace FROM __EventFilter"));
                foreach (ManagementObject obj in filterSearcher.Get())
                {
                    using (obj)
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var query = obj["Query"]?.ToString() ?? "";
                        var queryLang = obj["QueryLanguage"]?.ToString() ?? "WQL";
                        var eventNs = obj["EventNamespace"]?.ToString() ?? @"root\cimv2";
                        var relPath = obj.Path?.RelativePath ?? $"__EventFilter.Name=\"{name}\"";

                        filters[name] = (query, queryLang, eventNs, relPath);
                    }
                }
            }
            catch { }

            // 2. Считываем все привязки (__FilterToConsumerBinding)
            // Привязка связывает фильтр и консьюмер: Consumer = "CommandLineEventConsumer.Name=\"Foo\"", Filter = "__EventFilter.Name=\"Bar\""
            var bindings = new List<(string FilterPath, string ConsumerPath, string FilterName, string ConsumerName, string BindingPath)>();
            try
            {
                using var bindingSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Filter, Consumer FROM __FilterToConsumerBinding"));
                foreach (ManagementObject obj in bindingSearcher.Get())
                {
                    using (obj)
                    {
                        var filterRef = obj["Filter"]?.ToString() ?? "";
                        var consumerRef = obj["Consumer"]?.ToString() ?? "";
                        var bindingPath = obj.Path?.RelativePath ?? "";

                        var filterName = ExtractNameFromWmiRef(filterRef);
                        var consumerName = ExtractNameFromWmiRef(consumerRef);

                        bindings.Add((filterRef, consumerRef, filterName, consumerName, bindingPath));
                    }
                }
            }
            catch { }

            // 3. Сканируем CommandLineEventConsumer
            try
            {
                using var cmdSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, CommandLineTemplate, ExecutablePath, WorkingDirectory FROM CommandLineEventConsumer"));
                foreach (ManagementObject obj in cmdSearcher.Get())
                {
                    using (obj)
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var cmdTemplate = obj["CommandLineTemplate"]?.ToString() ?? "";
                        var exePath = obj["ExecutablePath"]?.ToString() ?? "";
                        var workDir = obj["WorkingDirectory"]?.ToString() ?? "";
                        var consumerRelPath = obj.Path?.RelativePath ?? $"CommandLineEventConsumer.Name=\"{name}\"";

                        if (string.IsNullOrWhiteSpace(cmdTemplate) && string.IsNullOrWhiteSpace(exePath))
                            continue;

                        // Ищем привязку к фильтру
                        var matchedBinding = bindings.Find(b =>
                            string.Equals(b.ConsumerName, name, StringComparison.OrdinalIgnoreCase) ||
                            b.ConsumerPath.Contains(name, StringComparison.OrdinalIgnoreCase));

                        string? filterName = matchedBinding != default ? matchedBinding.FilterName : null;
                        string? filterQuery = null;
                        string? filterNs = null;
                        string? filterRelPath = null;

                        if (!string.IsNullOrEmpty(filterName) && filters.TryGetValue(filterName, out var filterInfo))
                        {
                            filterQuery = filterInfo.Query;
                            filterNs = filterInfo.EventNs;
                            filterRelPath = filterInfo.RelPath;
                        }

                        var filePath = !string.IsNullOrWhiteSpace(exePath)
                            ? exePath
                            : WatcherHelper.ParsePath(cmdTemplate);

                        var cmdLine = string.IsNullOrWhiteSpace(cmdTemplate) ? exePath : cmdTemplate;

                        var entry = new StartupEntry
                        {
                            Name = name,
                            FilePath = filePath,
                            CommandLine = cmdLine,
                            Location = EntryLocation.WmiEventSubscription,
                            LocationDetail = $"root\\subscription\\CommandLineEventConsumer: {name}{(filterName != null ? $" (Filter: {filterName})" : " [Unbound]")}",
                            RawData = new Dictionary<string, string>
                            {
                                ["ConsumerType"] = "CommandLineEventConsumer",
                                ["ConsumerName"] = name,
                                ["ConsumerPath"] = consumerRelPath,
                                ["CommandLineTemplate"] = cmdTemplate,
                                ["ExecutablePath"] = exePath,
                                ["WorkingDirectory"] = workDir,
                                ["FilterName"] = filterName ?? "",
                                ["FilterPath"] = matchedBinding != default ? matchedBinding.FilterPath : (filterRelPath ?? ""),
                                ["FilterQuery"] = filterQuery ?? "",
                                ["FilterNamespace"] = filterNs ?? @"root\cimv2",
                                ["BindingPath"] = matchedBinding != default ? matchedBinding.BindingPath : "",
                                ["IsBound"] = (matchedBinding != default).ToString()
                            }
                        };

                        entry.Id = WatcherHelper.ComputeStableId(entry);
                        EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
                        results.Add(entry);
                    }
                }
            }
            catch { }

            // 4. Сканируем ActiveScriptEventConsumer (VBScript / JScript безфайловый запуск)
            try
            {
                using var scriptSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, ScriptingEngine, ScriptText, ScriptFileName FROM ActiveScriptEventConsumer"));
                foreach (ManagementObject obj in scriptSearcher.Get())
                {
                    using (obj)
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var engine = obj["ScriptingEngine"]?.ToString() ?? "VBScript";
                        var scriptText = obj["ScriptText"]?.ToString() ?? "";
                        var scriptFile = obj["ScriptFileName"]?.ToString() ?? "";
                        var consumerRelPath = obj.Path?.RelativePath ?? $"ActiveScriptEventConsumer.Name=\"{name}\"";

                        if (string.IsNullOrWhiteSpace(scriptText) && string.IsNullOrWhiteSpace(scriptFile))
                            continue;

                        var matchedBinding = bindings.Find(b =>
                            string.Equals(b.ConsumerName, name, StringComparison.OrdinalIgnoreCase) ||
                            b.ConsumerPath.Contains(name, StringComparison.OrdinalIgnoreCase));

                        string? filterName = matchedBinding != default ? matchedBinding.FilterName : null;
                        string? filterQuery = null;
                        string? filterNs = null;
                        string? filterRelPath = null;

                        if (!string.IsNullOrEmpty(filterName) && filters.TryGetValue(filterName, out var filterInfo))
                        {
                            filterQuery = filterInfo.Query;
                            filterNs = filterInfo.EventNs;
                            filterRelPath = filterInfo.RelPath;
                        }

                        var filePath = !string.IsNullOrWhiteSpace(scriptFile)
                            ? scriptFile
                            : Path.Combine(Environment.SystemDirectory, "wscript.exe");

                        var cmdLine = !string.IsNullOrWhiteSpace(scriptFile)
                            ? $"wscript.exe \"{scriptFile}\""
                            : $"{engine}: {(scriptText.Length > 80 ? scriptText[..80] + "..." : scriptText)}";

                        var entry = new StartupEntry
                        {
                            Name = name,
                            FilePath = filePath,
                            CommandLine = cmdLine,
                            Location = EntryLocation.WmiEventSubscription,
                            LocationDetail = $"root\\subscription\\ActiveScriptEventConsumer: {name}{(filterName != null ? $" (Filter: {filterName})" : " [Unbound]")}",
                            RawData = new Dictionary<string, string>
                            {
                                ["ConsumerType"] = "ActiveScriptEventConsumer",
                                ["ConsumerName"] = name,
                                ["ConsumerPath"] = consumerRelPath,
                                ["ScriptingEngine"] = engine,
                                ["ScriptText"] = scriptText,
                                ["ScriptFileName"] = scriptFile,
                                ["FilterName"] = filterName ?? "",
                                ["FilterPath"] = matchedBinding != default ? matchedBinding.FilterPath : (filterRelPath ?? ""),
                                ["FilterQuery"] = filterQuery ?? "",
                                ["FilterNamespace"] = filterNs ?? @"root\cimv2",
                                ["BindingPath"] = matchedBinding != default ? matchedBinding.BindingPath : "",
                                ["IsBound"] = (matchedBinding != default).ToString()
                            }
                        };

                        entry.Id = WatcherHelper.ComputeStableId(entry);
                        EntryOperations.ResolveAndEnrichEntry(entry, computeHash: false);
                        results.Add(entry);
                    }
                }
            }
            catch { }
        }
        catch { }

        return results;
    }

    /// <summary>
    /// Удаляет подписку WMI из системы (привязку, консьюмер и фильтр) с созданием полного снимка для восстановления.
    /// </summary>
    public static (bool success, string? error, string? payload) RemoveSubscription(StartupEntry entry)
    {
        try
        {
            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();

            var consumerName = entry.RawData.GetValueOrDefault("ConsumerName", entry.Name);
            var consumerType = entry.RawData.GetValueOrDefault("ConsumerType", "CommandLineEventConsumer");
            var filterName = entry.RawData.GetValueOrDefault("FilterName", "");

            var payload = new WmiSubscriptionPayload
            {
                ConsumerType = consumerType,
                ConsumerName = consumerName,
                CommandLineTemplate = entry.RawData.GetValueOrDefault("CommandLineTemplate"),
                ExecutablePath = entry.RawData.GetValueOrDefault("ExecutablePath"),
                WorkingDirectory = entry.RawData.GetValueOrDefault("WorkingDirectory"),
                ScriptingEngine = entry.RawData.GetValueOrDefault("ScriptingEngine"),
                ScriptText = entry.RawData.GetValueOrDefault("ScriptText"),
                ScriptFileName = entry.RawData.GetValueOrDefault("ScriptFileName"),
                FilterName = filterName,
                FilterQuery = entry.RawData.GetValueOrDefault("FilterQuery", ""),
                FilterNamespace = entry.RawData.GetValueOrDefault("FilterNamespace", @"root\cimv2"),
                BindingFilterPath = entry.RawData.GetValueOrDefault("FilterPath"),
                BindingConsumerPath = entry.RawData.GetValueOrDefault("ConsumerPath")
            };

            // 1. Находим и удаляем __FilterToConsumerBinding
            var bindingQuery = new ObjectQuery("SELECT * FROM __FilterToConsumerBinding");
            using (var bindingSearcher = new ManagementObjectSearcher(scope, bindingQuery))
            {
                foreach (ManagementObject binding in bindingSearcher.Get())
                {
                    using (binding)
                    {
                        var consumerRef = binding["Consumer"]?.ToString() ?? "";
                        if (consumerRef.Contains(consumerName, StringComparison.OrdinalIgnoreCase))
                        {
                            payload.BindingFilterPath ??= binding["Filter"]?.ToString();
                            payload.BindingConsumerPath ??= consumerRef;
                            binding.Delete();
                        }
                    }
                }
            }

            // 2. Удаляем консьюмер (CommandLineEventConsumer или ActiveScriptEventConsumer)
            var consumerQuery = new ObjectQuery($"SELECT * FROM {consumerType} WHERE Name = '{consumerName.Replace("'", "\\'")}'");
            using (var consumerSearcher = new ManagementObjectSearcher(scope, consumerQuery))
            {
                foreach (ManagementObject consumer in consumerSearcher.Get())
                {
                    using (consumer)
                    {
                        consumer.Delete();
                    }
                }
            }

            // 3. Проверяем, остались ли другие привязки у этого фильтра. Если нет — удаляем и сам фильтр
            if (!string.IsNullOrWhiteSpace(filterName))
            {
                bool otherBindingsExist = false;
                using (var checkSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM __FilterToConsumerBinding")))
                {
                    foreach (ManagementObject b in checkSearcher.Get())
                    {
                        using (b)
                        {
                            var f = b["Filter"]?.ToString() ?? "";
                            if (f.Contains(filterName, StringComparison.OrdinalIgnoreCase))
                            {
                                otherBindingsExist = true;
                                break;
                            }
                        }
                    }
                }

                if (!otherBindingsExist)
                {
                    var filterQuery = new ObjectQuery($"SELECT * FROM __EventFilter WHERE Name = '{filterName.Replace("'", "\\'")}'");
                    using var filterSearcher = new ManagementObjectSearcher(scope, filterQuery);
                    foreach (ManagementObject filter in filterSearcher.Get())
                    {
                        using (filter)
                        {
                            filter.Delete();
                        }
                    }
                }
            }

            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            return (true, null, payloadJson);
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось удалить WMI-подписку: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Восстанавливает ранее удалённую WMI-подписку из JSON-пейлоада.
    /// </summary>
    public static (bool success, string? error) RestoreSubscription(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return (false, "Пейлоад для восстановления WMI-подписки пуст");

        try
        {
            var payload = JsonSerializer.Deserialize<WmiSubscriptionPayload>(payloadJson, JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.ConsumerName))
                return (false, "Некорректный формат пейлоада WMI-подписки");

            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();

            // 1. Восстанавливаем / проверяем __EventFilter
            if (!string.IsNullOrWhiteSpace(payload.FilterName) && !string.IsNullOrWhiteSpace(payload.FilterQuery))
            {
                bool filterExists = false;
                using (var filterCheck = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT Name FROM __EventFilter WHERE Name = '{payload.FilterName.Replace("'", "\\'")}'")))
                {
                    if (filterCheck.Get().Count > 0) filterExists = true;
                }

                if (!filterExists)
                {
                    using var filterClass = new ManagementClass(scope, new ManagementPath("__EventFilter"), null);
                    using var filterObj = filterClass.CreateInstance();
                    filterObj["Name"] = payload.FilterName;
                    filterObj["Query"] = payload.FilterQuery;
                    filterObj["QueryLanguage"] = payload.FilterQueryLanguage ?? "WQL";
                    filterObj["EventNamespace"] = payload.FilterNamespace ?? @"root\cimv2";
                    filterObj.Put();
                }
            }

            // 2. Восстанавливаем консьюмер
            var consumerClassPath = payload.ConsumerType == "ActiveScriptEventConsumer"
                ? "ActiveScriptEventConsumer"
                : "CommandLineEventConsumer";

            bool consumerExists = false;
            using (var consumerCheck = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT Name FROM {consumerClassPath} WHERE Name = '{payload.ConsumerName.Replace("'", "\\'")}'")))
            {
                if (consumerCheck.Get().Count > 0) consumerExists = true;
            }

            if (!consumerExists)
            {
                using var consumerClass = new ManagementClass(scope, new ManagementPath(consumerClassPath), null);
                using var consumerObj = consumerClass.CreateInstance();
                consumerObj["Name"] = payload.ConsumerName;

                if (consumerClassPath == "CommandLineEventConsumer")
                {
                    if (!string.IsNullOrEmpty(payload.CommandLineTemplate)) consumerObj["CommandLineTemplate"] = payload.CommandLineTemplate;
                    if (!string.IsNullOrEmpty(payload.ExecutablePath)) consumerObj["ExecutablePath"] = payload.ExecutablePath;
                    if (!string.IsNullOrEmpty(payload.WorkingDirectory)) consumerObj["WorkingDirectory"] = payload.WorkingDirectory;
                }
                else
                {
                    if (!string.IsNullOrEmpty(payload.ScriptingEngine)) consumerObj["ScriptingEngine"] = payload.ScriptingEngine;
                    if (!string.IsNullOrEmpty(payload.ScriptText)) consumerObj["ScriptText"] = payload.ScriptText;
                    if (!string.IsNullOrEmpty(payload.ScriptFileName)) consumerObj["ScriptFileName"] = payload.ScriptFileName;
                }
                consumerObj.Put();
            }

            // 3. Восстанавливаем привязку __FilterToConsumerBinding
            var filterPath = payload.BindingFilterPath ?? $"__EventFilter.Name=\"{payload.FilterName}\"";
            var consumerPath = payload.BindingConsumerPath ?? $"{consumerClassPath}.Name=\"{payload.ConsumerName}\"";

            using var bindingClass = new ManagementClass(scope, new ManagementPath("__FilterToConsumerBinding"), null);
            using var bindingObj = bindingClass.CreateInstance();
            bindingObj["Filter"] = filterPath;
            bindingObj["Consumer"] = consumerPath;
            bindingObj.Put();

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Не удалось восстановить WMI-подписку: {ex.Message}");
        }
    }

    /// <summary>
    /// Отключает WMI-подписку (деактивирует __FilterToConsumerBinding без удаления кода/консьюмера).
    /// </summary>
    public static (bool success, string? error) DisableSubscription(StartupEntry entry)
    {
        try
        {
            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();

            var consumerName = entry.RawData.GetValueOrDefault("ConsumerName", entry.Name);
            string? foundFilterPath = null;
            string? foundConsumerPath = null;

            // Находим и удаляем привязку, сохраняя её пути в payload отключения
            var query = new ObjectQuery("SELECT * FROM __FilterToConsumerBinding");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject binding in searcher.Get())
            {
                using (binding)
                {
                    var consumerRef = binding["Consumer"]?.ToString() ?? "";
                    if (consumerRef.Contains(consumerName, StringComparison.OrdinalIgnoreCase))
                    {
                        foundFilterPath = binding["Filter"]?.ToString();
                        foundConsumerPath = consumerRef;
                        binding.Delete();
                        break;
                    }
                }
            }

            if (foundFilterPath != null && foundConsumerPath != null)
            {
                entry.DisablePayload = JsonSerializer.Serialize(new WmiDisablePayload(foundFilterPath, foundConsumerPath), JsonOptions);
                return (true, null);
            }

            // Если привязки уже не было, но консьюмер существует — формируем fallback-путь
            var filterName = entry.RawData.GetValueOrDefault("FilterName", "");
            var consumerType = entry.RawData.GetValueOrDefault("ConsumerType", "CommandLineEventConsumer");
            if (!string.IsNullOrEmpty(filterName))
            {
                entry.DisablePayload = JsonSerializer.Serialize(new WmiDisablePayload(
                    $"__EventFilter.Name=\"{filterName}\"",
                    $"{consumerType}.Name=\"{consumerName}\""), JsonOptions);
                return (true, null);
            }

            return (false, "Не удалось найти привязку WMI-подписки для отключения");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка отключения WMI-подписки: {ex.Message}");
        }
    }

    /// <summary>
    /// Включает ранее отключённую WMI-подписку (восстанавливает __FilterToConsumerBinding).
    /// </summary>
    public static (bool success, string? error) EnableSubscription(StartupEntry entry)
    {
        try
        {
            string? filterPath = null;
            string? consumerPath = null;

            if (!string.IsNullOrWhiteSpace(entry.DisablePayload))
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<WmiDisablePayload>(entry.DisablePayload, JsonOptions);
                    if (dto != null)
                    {
                        filterPath = dto.FilterPath;
                        consumerPath = dto.ConsumerPath;
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(filterPath) || string.IsNullOrWhiteSpace(consumerPath))
            {
                var filterName = entry.RawData.GetValueOrDefault("FilterName", "");
                var consumerName = entry.RawData.GetValueOrDefault("ConsumerName", entry.Name);
                var consumerType = entry.RawData.GetValueOrDefault("ConsumerType", "CommandLineEventConsumer");

                if (string.IsNullOrEmpty(filterName) || string.IsNullOrEmpty(consumerName))
                    return (false, "Недостаточно данных для восстановления привязки WMI");

                filterPath = $"__EventFilter.Name=\"{filterName}\"";
                consumerPath = $"{consumerType}.Name=\"{consumerName}\"";
            }

            var scope = new ManagementScope(WmiNamespace);
            scope.Connect();

            using var bindingClass = new ManagementClass(scope, new ManagementPath("__FilterToConsumerBinding"), null);
            using var bindingObj = bindingClass.CreateInstance();
            bindingObj["Filter"] = filterPath;
            bindingObj["Consumer"] = consumerPath;
            bindingObj.Put();

            entry.DisablePayload = string.Empty;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка включения WMI-подписки: {ex.Message}");
        }
    }

    /// <summary>
    /// Извлекает имя объекта из ссылки WMI (например `__EventFilter.Name="MyFilter"` -> `MyFilter`).
    /// </summary>
    private static string ExtractNameFromWmiRef(string wmiRef)
    {
        if (string.IsNullOrWhiteSpace(wmiRef)) return "";
        var quoteIdx = wmiRef.IndexOf('"');
        if (quoteIdx >= 0)
        {
            var endQuote = wmiRef.IndexOf('"', quoteIdx + 1);
            if (endQuote > quoteIdx)
                return wmiRef.Substring(quoteIdx + 1, endQuote - quoteIdx - 1);
        }

        var eqIdx = wmiRef.IndexOf('=');
        if (eqIdx >= 0)
            return wmiRef[(eqIdx + 1)..].Trim('"', '\'', ' ');

        return wmiRef;
    }
}
